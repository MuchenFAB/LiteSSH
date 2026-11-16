using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LiteSSH.Core;
using LiteSSH.Miuix;

namespace LiteSSH.Views;

public partial class MainWindow : MiuixWindow
{
    private readonly ICollectionView _sessionView;

    public MainWindow()
    {
        InitializeComponent();

        Tabs = new ObservableCollection<TerminalPane>();
        TabStrip.ItemsSource = Tabs;
        _sessionView = CollectionViewSource.GetDefaultView(AppState.Current.Sessions);
        _sessionView.Filter = FilterSession;
        SessionList.ItemsSource = _sessionView;

        AppState.Current.Sessions.CollectionChanged += (_, _) => UpdateEmptyHint();
        L10n.LanguageChanged += (_, _) => UpdateEmptyHint();
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Loaded += (_, _) =>
        {
            UpdateEmptyHint();
            SearchBox.Focus();
        };
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public ObservableCollection<TerminalPane> Tabs { get; }

    private SessionProfile? SelectedProfile => SessionList.SelectedItem as SessionProfile;

    private void UpdateEmptyHint()
    {
        SessionEmptyHint.Visibility = AppState.Current.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (AppState.Current.Sessions.Count == 0)
        {
            SessionEmptyHint.Text = L10n.T("Nav.Empty");
        }
    }

    private bool FilterSession(object item)
    {
        if (item is not SessionProfile profile)
        {
            return false;
        }
        var query = SearchBox?.Text?.Trim() ?? "";
        if (query.Length == 0)
        {
            return true;
        }
        return profile.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || profile.Host.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || profile.User.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || profile.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _sessionView.Refresh();

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeButton != null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => TitleBarMouseDown(TitleBar, e);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SessionDialog(null, null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
        {
            return;
        }

        var profile = dialog.Result;
        AppState.Current.AddSession(profile);
        _sessionView.Refresh();
        SessionList.SelectedItem = profile;
        UpdateEmptyHint();
        if (dialog.ConnectNow)
        {
            OpenSession(profile, autoConnect: true, openSftp: false);
        }
    }

    private void SessionConnect_Click(object sender, RoutedEventArgs e) => ConnectSelected();

    private void SessionSftp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is { } profile)
        {
            OpenSession(profile, autoConnect: true, openSftp: true);
        }
    }

    private void SessionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelected();

    private void SessionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConnectSelected();
            e.Handled = true;
        }
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // selection only feeds the context menu actions
    }

    private void ConnectSelected()
    {
        if (SelectedProfile is { } profile)
        {
            OpenSession(profile, autoConnect: true, openSftp: false);
        }
    }

    private void SessionEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var dialog = new SessionDialog(profile, profile.Clone()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
        {
            return;
        }

        var edited = dialog.Result;
        profile.Name = edited.Name;
        profile.Group = edited.Group;
        profile.Host = edited.Host;
        profile.Port = edited.Port;
        profile.User = edited.User;
        profile.Auth = edited.Auth;
        profile.Password = edited.Password;
        profile.KeyPath = edited.KeyPath;
        profile.KeyPassphrase = edited.KeyPassphrase;
        profile.Color = edited.Color;
        profile.SavePassword = edited.SavePassword;
        profile.Forwards = edited.Forwards;
        AppState.Current.Save();
        _sessionView.Refresh();
    }

    private void SessionDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }
        var copy = profile.Clone();
        copy.Name = profile.Name + " (2)";
        AppState.Current.AddSession(copy);
        _sessionView.Refresh();
        SessionList.SelectedItem = copy;
        UpdateEmptyHint();
    }

    private void SessionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }
        if (!Dialogs.Confirm(this, string.Format(L10n.IsEnglish ? "Delete session \"{0}\"?" : "删除会话「{0}」？", profile.Name), L10n.T("Action.Delete"), danger: true))
        {
            return;
        }
        AppState.Current.RemoveSession(profile);
        _sessionView.Refresh();
        UpdateEmptyHint();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ApplySettings();
        }
    }

    public void ApplySettings()
    {
        foreach (var tab in Tabs)
        {
            tab.ApplySettings();
        }
    }

    /// <summary>Opens a saved session by name or id (used by --connect and shortcuts).</summary>
    public void ConnectByName(string nameOrId)
    {
        var profile = AppState.Current.Sessions.FirstOrDefault(s =>
                string.Equals(s.Id, nameOrId, StringComparison.OrdinalIgnoreCase))
            ?? AppState.Current.Sessions.FirstOrDefault(s =>
                string.Equals(s.Name, nameOrId, StringComparison.CurrentCultureIgnoreCase));

        if (profile == null)
        {
            return;
        }

        SessionList.SelectedItem = profile;
        OpenSession(profile, autoConnect: true, openSftp: false);
    }

    public void ShowStorageWarning()
    {
        var message = L10n.IsEnglish
            ? $"LiteSSH cannot write to its own folder:\n{AppState.DataPath}\n\nSessions and settings will not be saved. Move LiteSSH.exe to a writable folder."
            : $"LiteSSH 无法写入自身所在目录：\n{AppState.DataPath}\n\n会话与设置将无法保存，请把 LiteSSH.exe 移到可写入的目录。";
        Dialogs.Info(this, message, L10n.T("Common.Warning"));
    }

    private void OpenSession(SessionProfile profile, bool autoConnect, bool openSftp)
    {
        var existing = Tabs.FirstOrDefault(t => t.Profile.Id == profile.Id);
        if (existing != null)
        {
            TabStrip.SelectedItem = existing;
            existing.FocusTerminal();
            if (openSftp)
            {
                existing.ShowSftp(true);
            }
            if (autoConnect && !existing.IsConnected)
            {
                existing.Connect();
            }
            return;
        }

        var pane = new TerminalPane(profile, openSftp || AppState.Current.Settings.ShowSftpByDefault);
        pane.CloseRequested += CloseTab;
        Tabs.Add(pane);
        TabStrip.SelectedItem = pane;
        ContentHost.Content = pane;
        WelcomePanel.Visibility = Visibility.Collapsed;
        if (autoConnect)
        {
            pane.Connect();
        }
        pane.FocusTerminal();
    }

    private void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabStrip.SelectedItem is TerminalPane pane)
        {
            ContentHost.Content = pane;
            WelcomePanel.Visibility = Visibility.Collapsed;
            pane.FocusTerminal();
        }
        else
        {
            ContentHost.Content = null;
            WelcomePanel.Visibility = Visibility.Visible;
        }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TerminalPane pane })
        {
            CloseTab(pane);
            e.Handled = true;
        }
    }

    private void CloseTab(TerminalPane pane)
    {
        if (AppState.Current.Settings.ConfirmOnClose && pane.IsConnected)
        {
            if (!Dialogs.Confirm(this, string.Format(L10n.IsEnglish ? "Close \"{0}\" and disconnect?" : "关闭「{0}」并断开连接？", pane.Title)))
            {
                return;
            }
        }

        pane.Dispose();
        Tabs.Remove(pane);
        if (Tabs.Count == 0)
        {
            ContentHost.Content = null;
            WelcomePanel.Visibility = Visibility.Visible;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!ctrl)
        {
            return;
        }

        if (e.Key == Key.N)
        {
            NewSession_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.W && TabStrip.SelectedItem is TerminalPane pane)
        {
            CloseTab(pane);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            if (Tabs.Count > 1)
            {
                var index = TabStrip.SelectedIndex;
                TabStrip.SelectedIndex = (index + 1) % Tabs.Count;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.OemComma)
        {
            Settings_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var connected = Tabs.Count(t => t.IsConnected);
        if (connected > 0)
        {
            var message = L10n.IsEnglish
                ? $"There are {connected} active session(s). Quit LiteSSH?"
                : $"当前有 {connected} 个活动会话，确定退出 LiteSSH？";
            if (!Dialogs.Confirm(this, message, L10n.T("Common.Confirm")))
            {
                e.Cancel = true;
                return;
            }
        }

        foreach (var tab in Tabs.ToList())
        {
            tab.Dispose();
        }
        AppState.Current.Save();
    }
}
