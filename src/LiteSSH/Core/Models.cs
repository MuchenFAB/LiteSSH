using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSSH.Core;

public enum AuthKind
{
    Password,
    PrivateKey,
}

public enum ForwardType
{
    Local,
    Remote,
}

public sealed class ForwardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ForwardType Type { get; set; } = ForwardType.Local;
    public string LocalAddress { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; } = 8080;
    public string RemoteHost { get; set; } = "127.0.0.1";
    public int RemotePort { get; set; } = 80;
    public bool AutoStart { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Name)
        ? $"{Type} {LocalAddress}:{LocalPort} → {RemoteHost}:{RemotePort}"
        : Name;
}

public sealed class SessionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public AuthKind Auth { get; set; } = AuthKind.Password;
    public string Password { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string KeyPassphrase { get; set; } = "";
    public string Color { get; set; } = "#3482FF";
    public bool SavePassword { get; set; } = true;
    public List<ForwardRule> Forwards { get; set; } = new();

    [JsonIgnore]
    public string Endpoint => $"{User}@{Host}:{Port}";

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Group) ? Endpoint : $"{Group} · {Endpoint}";

    public SessionProfile Clone()
    {
        var copy = (SessionProfile)MemberwiseClone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Forwards = Forwards.Select(f => new ForwardRule
        {
            Name = f.Name,
            Type = f.Type,
            LocalAddress = f.LocalAddress,
            LocalPort = f.LocalPort,
            RemoteHost = f.RemoteHost,
            RemotePort = f.RemotePort,
            AutoStart = f.AutoStart,
        }).ToList();
        return copy;
    }
}

public sealed class KnownHost
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Fingerprint { get; set; } = "";
    public DateTime FirstSeen { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string Key => $"{Host}:{Port}";
}

public enum ThemeMode
{
    Auto,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    public string Accent { get; set; } = "#3482FF";
    public string FontFamily { get; set; } = "Cascadia Mono, Consolas, Microsoft YaHei UI";
    public double FontSize { get; set; } = 13;
    public int Scrollback { get; set; } = 2000;
    public string Language { get; set; } = "zh";
    public string TerminalTheme { get; set; } = "miuix-auto";
    public bool CopyOnSelect { get; set; }
    public bool ConfirmOnClose { get; set; } = true;
    public bool SidebarCollapsed { get; set; }
    public bool ShowSftpByDefault { get; set; }
}

public sealed class AppData
{
    public List<SessionProfile> Sessions { get; set; } = new();
    public List<KnownHost> KnownHosts { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

/// <summary>Portable state container: everything lives in one DPAPI-protected file next to the exe.</summary>
public sealed class AppState : INotifyPropertyChanged
{
    private static AppState? _current;
    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    public static AppState Current => _current ??= Load();

    public AppData Data { get; private set; } = new();
    public ObservableCollection<SessionProfile> Sessions { get; } = new();
    public AppSettings Settings => Data.Settings;
    public List<KnownHost> KnownHosts => Data.KnownHosts;

    public event PropertyChangedEventHandler? PropertyChanged;

    private AppState() { }

    public static string DataPath => Path.Combine(AppContext.BaseDirectory, Store.FileName);

    private static AppState Load()
    {
        var state = new AppState();
        state.Data = Store.Load();
        foreach (var s in state.Data.Sessions
                     .OrderBy(s => s.Group, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            state.Sessions.Add(s);
        }
        return state;
    }

    public void Save() => ScheduleSave(0);

    public void ScheduleSave(int delayMs = 400)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.CheckAccess())
        {
            Store.Save(Snapshot());
            return;
        }

        _saveTimer ??= new System.Windows.Threading.DispatcherTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTick;
        _saveTimer.Tick += OnSaveTick;
        _saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        Store.Save(Snapshot());
    }

    public AppData Snapshot() => new()
    {
        Sessions = Sessions.ToList(),
        KnownHosts = Data.KnownHosts,
        Settings = Data.Settings,
    };

    public void AddSession(SessionProfile profile)
    {
        Sessions.Add(profile);
        Save();
    }

    public void RemoveSession(SessionProfile profile)
    {
        Sessions.Remove(profile);
        Save();
    }

    public KnownHost? FindKnownHost(string host, int port)
        => KnownHosts.FirstOrDefault(k => string.Equals(k.Host, host, StringComparison.OrdinalIgnoreCase) && k.Port == port);

    public void TrustHost(string host, int port, string fingerprint)
    {
        var existing = FindKnownHost(host, port);
        if (existing == null)
        {
            KnownHosts.Add(new KnownHost { Host = host, Port = port, Fingerprint = fingerprint });
        }
        else
        {
            existing.Fingerprint = fingerprint;
        }
        Save();
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Store
{
    public const string FileName = "LiteSSH.dat";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppData Load()
    {
        try
        {
            if (!File.Exists(AppState.DataPath))
            {
                return new AppData();
            }

            var raw = File.ReadAllBytes(AppState.DataPath);
            var json = System.Security.Cryptography.ProtectedData.Unprotect(raw, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AppData>(json, Options) ?? new AppData();
        }
        catch
        {
            return new AppData();
        }
    }

    public static bool Save(AppData data)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(data, Options);
            var encrypted = System.Security.Cryptography.ProtectedData.Protect(json, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var tmp = AppState.DataPath + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, AppState.DataPath, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when the app folder can hold the portable data file.</summary>
    public static bool IsDataFolderWritable()
    {
        try
        {
            if (File.Exists(AppState.DataPath))
            {
                using var handle = new FileStream(AppState.DataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                return handle.CanWrite;
            }

            var probe = AppState.DataPath + ".probe";
            using var probeHandle = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return probeHandle.CanWrite;
        }
        catch
        {
            return false;
        }
    }
}
