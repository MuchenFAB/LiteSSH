using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace LiteSSH.Core;

public sealed class HostKeyRequest
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Fingerprint { get; init; } = "";
    public string Expected { get; init; } = "";
    public bool Known => !string.IsNullOrEmpty(Expected);
    public bool Changed => Known && !string.Equals(Expected, Fingerprint, StringComparison.Ordinal);
}

public static class Ssh
{
    /// <summary>Invoked on a background thread when an unknown or changed host key is seen.</summary>
    public static Func<HostKeyRequest, bool>? HostKeyVerifier { get; set; }

    public static string FingerprintOf(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    public static ConnectionInfo Build(SessionProfile p)
    {
        var methods = new List<AuthenticationMethod>();
        var password = p.Password ?? "";

        if (p.Auth == AuthKind.PrivateKey && !string.IsNullOrWhiteSpace(p.KeyPath))
        {
            PrivateKeyFile key = string.IsNullOrEmpty(p.KeyPassphrase)
                ? new PrivateKeyFile(ExpandPath(p.KeyPath))
                : new PrivateKeyFile(ExpandPath(p.KeyPath), p.KeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(p.User, key));
            if (!string.IsNullOrEmpty(password))
            {
                methods.Add(BuildPassword(p.User, password));
            }
        }
        else
        {
            methods.Add(BuildPassword(p.User, password));
        }

        return new ConnectionInfo(p.Host, p.Port, p.User, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(20),
            Encoding = Encoding.UTF8,
        };
    }

    private static AuthenticationMethod BuildPassword(string user, string password)
    {
        var ki = new KeyboardInteractiveAuthenticationMethod(user);
        ki.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };
        return new PasswordAuthenticationMethod(user, password);
    }

    private static AuthenticationMethod BuildKeyboard(string user, string password)
    {
        var ki = new KeyboardInteractiveAuthenticationMethod(user);
        ki.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };
        return ki;
    }

    public static ConnectionInfo BuildWithPasswordOnly(SessionProfile p)
    {
        var methods = new List<AuthenticationMethod> { BuildKeyboard(p.User, p.Password ?? "") };
        return new ConnectionInfo(p.Host, p.Port, p.User, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(20),
            Encoding = Encoding.UTF8,
        };
    }

    public static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }
        return Environment.ExpandEnvironmentVariables(path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }

    public static void AttachHostKeyCheck(BaseClient client, SessionProfile profile, Action<string>? onTrusted = null)
    {
        client.HostKeyReceived += (_, e) =>
        {
            var fp = string.IsNullOrEmpty(e.FingerPrintSHA256) ? FingerprintOf(e.HostKey) : e.FingerPrintSHA256;
            var known = AppState.Current.FindKnownHost(profile.Host, profile.Port);
            var expected = known?.Fingerprint ?? "";
            if (known != null && string.Equals(expected, fp, StringComparison.Ordinal))
            {
                onTrusted?.Invoke(fp);
                return;
            }

            var verifier = HostKeyVerifier;
            var trust = verifier != null && verifier(new HostKeyRequest
            {
                Host = profile.Host,
                Port = profile.Port,
                Fingerprint = fp,
                Expected = expected,
            });

            e.CanTrust = trust;
            if (trust)
            {
                AppState.Current.TrustHost(profile.Host, profile.Port, fp);
                onTrusted?.Invoke(fp);
            }
        };
    }
}

public sealed class ShellSession : IDisposable
{
    private static readonly System.Reflection.FieldInfo? ShellChannelField =
        typeof(ShellStream).GetField("_channel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static readonly System.Reflection.MethodInfo? WindowChangeMethod =
        ShellChannelField?.FieldType.GetMethod(
            "SendWindowChangeRequest",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    private readonly object _sync = new();
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _cts;

    public ShellSession(SessionProfile profile) => Profile = profile;

    public SessionProfile Profile { get; }
    public string Fingerprint { get; private set; } = "";

    public event Action<byte[]>? Output;
    public event Action<string>? Closed;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _client?.IsConnected == true && _shell != null;
            }
        }
    }

    public void Connect(int cols, int rows)
    {
        var info = Ssh.Build(Profile);
        var client = new SshClient(info)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20),
        };
        Ssh.AttachHostKeyCheck(client, Profile, fp => Fingerprint = fp);

        try
        {
            client.Connect();
        }
        catch (SshAuthenticationException)
        {
            client.Dispose();
            var retryInfo = Ssh.BuildWithPasswordOnly(Profile);
            client = new SshClient(retryInfo) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
            Ssh.AttachHostKeyCheck(client, Profile, fp => Fingerprint = fp);
            client.Connect();
        }

        var shell = client.CreateShellStream("xterm-256color", (uint)Math.Max(2, cols), (uint)Math.Max(2, rows), 0, 0, 1 << 16);
        lock (_sync)
        {
            _client = client;
            _shell = shell;
            _cts = new CancellationTokenSource();
        }

        var ct = _cts!.Token;
        Task.Run(() => ReadLoop(shell, ct));
    }

    private void ReadLoop(ShellStream shell, CancellationToken ct)
    {
        var buffer = new byte[32768];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = shell.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                Output?.Invoke(chunk);
            }
        }
        catch (Exception)
        {
            // stream torn down
        }

        if (!ct.IsCancellationRequested)
        {
            Closed?.Invoke(L10n.T("Term.Disconnected"));
        }
    }

    public void Send(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            if (_shell == null)
            {
                return;
            }

            try
            {
                _shell.Write(data, 0, data.Length);
                _shell.Flush();
            }
            catch
            {
                // ignore, closed handler reports it
            }
        }
    }

    public void Send(string text) => Send(Encoding.UTF8.GetBytes(text));

    public void Resize(int cols, int rows)
    {
        lock (_sync)
        {
            try
            {
                if (_shell == null)
                {
                    return;
                }

                var channel = ShellChannelField?.GetValue(_shell);
                if (channel != null)
                {
                    WindowChangeMethod?.Invoke(channel, new object[] { (uint)Math.Max(2, cols), (uint)Math.Max(2, rows), 0u, 0u });
                }
            }
            catch
            {
                // resize is best-effort
            }
        }
    }

    public void Dispose()
    {
        ShellStream? shell;
        SshClient? client;
        lock (_sync)
        {
            shell = _shell;
            client = _client;
            _shell = null;
            _client = null;
            _cts?.Cancel();
        }

        try { shell?.Dispose(); } catch { }
        try { client?.Disconnect(); } catch { }
        try { client?.Dispose(); } catch { }
    }
}

public sealed class RemoteEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Mode { get; init; } = "";

    public bool IsParent => Name == "..";

    public string SizeText => IsDirectory ? "<DIR>" : FormatSize(Size);

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}

public sealed class SftpService : IDisposable
{
    private readonly SessionProfile _profile;
    private readonly object _sync = new();
    private SftpClient? _sftp;

    public SftpService(SessionProfile profile) => _profile = profile;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _sftp?.IsConnected == true;
            }
        }
    }

    private SftpClient EnsureConnected()
    {
        lock (_sync)
        {
            if (_sftp is { IsConnected: true })
            {
                return _sftp;
            }

            _sftp?.Dispose();

            var sftp = new SftpClient(Ssh.Build(_profile));
            Ssh.AttachHostKeyCheck(sftp, _profile);
            sftp.Connect();
            _sftp = sftp;
            return sftp;
        }
    }

    public string Home()
    {
        var sftp = EnsureConnected();
        return string.IsNullOrEmpty(sftp.WorkingDirectory) ? "/" : sftp.WorkingDirectory;
    }

    public IReadOnlyList<RemoteEntry> List(string path)
    {
        var sftp = EnsureConnected();
        var list = new List<RemoteEntry>();
        foreach (var item in sftp.ListDirectory(path))
        {
            if (item.Name is "." or "..")
            {
                continue;
            }
            list.Add(new RemoteEntry
            {
                Name = item.Name,
                FullPath = Combine(path, item.Name),
                IsDirectory = item.IsDirectory,
                Size = item.Length,
                Modified = item.LastWriteTime,
                Mode = item.IsDirectory ? "d" : "-",
            });
        }

        return list.OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string Combine(string dir, string name)
        => dir.TrimEnd('/') + "/" + name;

    public static string Parent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        if (index <= 0)
        {
            return "/";
        }
        return trimmed[..index];
    }

    public void Download(string remoteFile, string localFile, Action<long, long>? progress, CancellationToken ct)
    {
        var sftp = EnsureConnected();
        using var fs = File.Create(localFile);
        sftp.DownloadFile(remoteFile, fs, (ulong done) => progress?.Invoke((long)done, 0));
        ct.ThrowIfCancellationRequested();
    }

    public void Upload(string localFile, string remoteFile, Action<long, long>? progress, CancellationToken ct)
    {
        var sftp = EnsureConnected();
        using var fs = File.OpenRead(localFile);
        sftp.UploadFile(fs, remoteFile, true, (ulong done) => progress?.Invoke((long)done, fs.Length));
        ct.ThrowIfCancellationRequested();
    }

    public void Delete(RemoteEntry entry)
    {
        var sftp = EnsureConnected();
        if (entry.IsDirectory)
        {
            sftp.DeleteDirectory(entry.FullPath);
        }
        else
        {
            sftp.DeleteFile(entry.FullPath);
        }
    }

    public void CreateDirectory(string path) => EnsureConnected().CreateDirectory(path);

    public void Rename(string from, string to) => EnsureConnected().RenameFile(from, to);

    public void Dispose()
    {
        lock (_sync)
        {
            try { _sftp?.Dispose(); } catch { }
            _sftp = null;
        }
    }
}

public sealed class PortForwarder : IDisposable
{
    private readonly SessionProfile _profile;
    private readonly Dictionary<string, ForwardedPort> _ports = new();
    private readonly ConcurrentDictionary<string, string> _errors = new();
    private SshClient? _client;
    private readonly object _sync = new();

    public PortForwarder(SessionProfile profile) => _profile = profile;

    public event Action? Changed;

    public string? GetError(string ruleId) => _errors.TryGetValue(ruleId, out var e) ? e : null;

    public bool IsRunning(string ruleId)
    {
        lock (_sync)
        {
            return _ports.TryGetValue(ruleId, out var port) && port.IsStarted;
        }
    }

    private SshClient EnsureClient()
    {
        lock (_sync)
        {
            if (_client is { IsConnected: true })
            {
                return _client;
            }

            _client?.Dispose();
            var client = new SshClient(Ssh.Build(_profile)) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
            Ssh.AttachHostKeyCheck(client, _profile);
            client.Connect();
            _client = client;
            return client;
        }
    }

    public void Start(ForwardRule rule)
    {
        lock (_sync)
        {
            if (_ports.TryGetValue(rule.Id, out var existing) && existing.IsStarted)
            {
                return;
            }

            var client = EnsureClient();
            ForwardedPort port = rule.Type == ForwardType.Local
                ? new ForwardedPortLocal(rule.LocalAddress, (uint)rule.LocalPort, rule.RemoteHost, (uint)rule.RemotePort)
                : new ForwardedPortRemote(rule.LocalAddress, (uint)rule.LocalPort, rule.RemoteHost, (uint)rule.RemotePort);

            try
            {
                client.AddForwardedPort(port);
                port.Start();
                _ports[rule.Id] = port;
                _errors.TryRemove(rule.Id, out _);
            }
            catch (Exception ex)
            {
                _errors[rule.Id] = ex.Message;
                try { port.Dispose(); } catch { }
                Changed?.Invoke();
                throw;
            }
        }
        Changed?.Invoke();
    }

    public void Stop(string ruleId)
    {
        lock (_sync)
        {
            if (_ports.Remove(ruleId, out var port))
            {
                try { port.Stop(); } catch { }
                try { port.Dispose(); } catch { }
            }
        }
        Changed?.Invoke();
    }

    public void StopAll()
    {
        lock (_sync)
        {
            foreach (var port in _ports.Values)
            {
                try { port.Stop(); } catch { }
                try { port.Dispose(); } catch { }
            }
            _ports.Clear();
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        StopAll();
        lock (_sync)
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
    }
}
