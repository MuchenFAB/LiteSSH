using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiteSSH.Core;
using LiteSSH.Miuix;
using LiteSSH.Terminal;

namespace LiteSSH.Views;

public partial class TerminalPane : UserControl, INotifyPropertyChanged
{
    private readonly TerminalView _view = new();
    private readonly TerminalEmulator _emulator;
    private readonly ConcurrentQueue<byte[]> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private ShellSession? _session;
    private PortForwarder? _forwarder;
    private SftpPanel? _sftp;
    private bool _connecting;
    private bool _sftpVisible;
    private Brush _statusBrush = Brushes.Gray;

    public TerminalPane(SessionProfile profile, bool openSftp)
    {
        InitializeComponent();
        Profile = profile;
        _emulator = new TerminalEmulator(100, 30, AppState.Current.Settings.Scrollback);

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        _flushTimer.Tick += (_, _) => FlushOutput();
        _flushTimer.Start();

        _view.Attach(_emulator);
        _view.Input += OnInput;
        _view.GridResized += OnGridResized;
        _view.ReconnectRequested += () => Connect();
        _emulator.TitleChanged += OnRemoteTitle;
        TerminalHost.Child = _view;

        ApplySettings();
        UpdateStatus(L10n.T("Term.Ready"), Theme.ColorOf(profile.Color));
        EndpointText.Text = profile.Endpoint;
        SftpButton.ToolTip = L10n.T("Action.OpenSftp");
        Loaded += (_, _) =>
        {
            _view.Focus();
            UpdateGridText(_emulator.Screen.Cols, _emulator.Screen.Rows);
        };
        L10n.LanguageChanged += OnLanguageChanged;

        if (openSftp)
        {
            ShowSftp(true);
        }
    }

    public SessionProfile Profile { get; }

    public string Title => string.IsNullOrWhiteSpace(Profile.Name) ? Profile.Host : Profile.Name;

    public bool IsConnected => _session?.IsConnected == true;

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set
        {
            _statusBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        }
    }

    public event Action<TerminalPane>? CloseRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void FocusTerminal() => _view.Focus();

    public void ApplySettings()
    {
        var s = AppState.Current.Settings;
        _view.FontFamilyName = s.FontFamily;
        _view.FontSize = s.FontSize;
        _view.CopyOnSelect = s.CopyOnSelect;
        _emulator.Screen.SetScrollbackLimit(s.Scrollback);
        _view.Palette = TerminalPalette.Resolve(s.TerminalTheme, Theme.IsDark);
        _view.InvalidateVisual();
    }

    public void ShowSftp(bool show)
    {
        _sftpVisible = show;
        if (show)
        {
            _sftp ??= new SftpPanel(Profile);
            SftpHost.Child = _sftp;
            SftpColumn.Width = new GridLength(340);
            SplitterColumn.Width = new GridLength(6);
            Splitter.Width = 6;
        }
        else
        {
            SftpColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            Splitter.Width = 0;
        }
    }

    public void Connect()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        _emulator.Reset();
        UpdateStatus(L10n.T("Term.Connecting"), Theme.ColorOf(Profile.Color));
        _view.Focus();

        var cols = Math.Max(2, _emulator.Screen.Cols);
        var rows = Math.Max(2, _emulator.Screen.Rows);
        var session = new ShellSession(Profile);
        session.Output += OnOutput;
        session.Closed += OnClosed;
        _session = session;

        Task.Run(() =>
        {
            try
            {
                session.Connect(cols, rows);
                Dispatcher.Invoke(() =>
                {
                    _connecting = false;
                    UpdateStatus(L10n.T("Term.Connected"), Theme.ColorOf(Profile.Color, "#12945F"));
                    StartAutoForwards();
                    RefreshSftp();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    _connecting = false;
                    UpdateStatus(L10n.T("Term.Failed"), Colors.OrangeRed);
                    AppendLocalLine($"\r\n\u001b[31m{L10n.T("Conn.Error")}{ex.Message}\u001b[0m\r\n");
                    AppendLocalLine($"\u001b[90m{L10n.T("Term.PressR")}\u001b[0m\r\n");
                });
            }
        });
    }

    private void RefreshSftp()
    {
        if (_sftpVisible)
        {
            _sftp?.Reload();
        }
    }

    private void StartAutoForwards()
    {
        var rules = Profile.Forwards.Where(f => f.AutoStart).ToList();
        if (rules.Count == 0)
        {
            return;
        }

        _forwarder ??= new PortForwarder(Profile);
        foreach (var rule in rules)
        {
            try
            {
                _forwarder.Start(rule);
            }
            catch
            {
                // surfaced in the forwarding dialog
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SftpButton.ToolTip = L10n.T("Action.OpenSftp");
        ForwardButton.ToolTip = L10n.T("Action.Forward");
        ReconnectButton.ToolTip = L10n.T("Action.Reconnect");
        DisconnectButton.ToolTip = L10n.T("Action.Disconnect");
        UpdateStatus(IsConnected ? L10n.T("Term.Connected") : L10n.T("Term.Ready"),
            IsConnected ? Theme.ColorOf(Profile.Color, "#12945F") : Colors.Gray);
    }

    private void OnRemoteTitle(string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            ToolTip = title;
        }
    }

    private void OnOutput(byte[] data) => _pending.Enqueue(data);

    private void FlushOutput()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var chunks = new List<byte[]>();
        while (_pending.TryDequeue(out var chunk))
        {
            chunks.Add(chunk);
        }

        var total = chunks.Sum(c => c.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
            offset += chunk.Length;
        }

        _view.Feed(buffer);
    }

    private void AppendLocalLine(string text)
    {
        _view.Feed(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private void OnClosed(string reason)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateStatus(L10n.T("Term.Disconnected"), Colors.OrangeRed);
            AppendLocalLine($"\r\n\u001b[90m— {reason} —\u001b[0m\r\n");
            _forwarder?.StopAll();
        });
    }

    private void OnGridResized(int cols, int rows)
    {
        _session?.Resize(cols, rows);
        UpdateGridText(cols, rows);
    }

    private void UpdateGridText(int cols, int rows) => GridText.Text = $"{cols} × {rows}";

    private void OnInput(byte[] data) => _session?.Send(data);

    private void UpdateStatus(string text, Color color)
    {
        StatusText.Text = text;
        StatusBrush = new SolidColorBrush(color);
        StatusDot.Fill = StatusBrush;
    }

    private void Sftp_Click(object sender, RoutedEventArgs e) => ShowSftp(!_sftpVisible);

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (!IsConnected)
        {
            Dialogs.Info(Window.GetWindow(this), L10n.T("Forward.NeedConnection"));
            return;
        }

        _forwarder ??= new PortForwarder(Profile);
        var dialog = new ForwardDialog(Profile, _forwarder) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        AppState.Current.Save();
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e) => Connect();

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _session?.Dispose();
        _forwarder?.StopAll();
        UpdateStatus(L10n.T("Term.Disconnected"), Colors.Gray);
    }

    public void Dispose()
    {
        _flushTimer.Stop();
        L10n.LanguageChanged -= OnLanguageChanged;
        try { _session?.Dispose(); } catch { }
        try { _forwarder?.Dispose(); } catch { }
        try { _sftp?.Dispose(); } catch { }
    }

    public void Close() => CloseRequested?.Invoke(this);
}
