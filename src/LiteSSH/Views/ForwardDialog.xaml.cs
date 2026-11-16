using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiteSSH.Core;
using LiteSSH.Miuix;

namespace LiteSSH.Views;

public sealed class ForwardRow : INotifyPropertyChanged
{
    public ForwardRule Rule { get; init; } = new();
    private string _status = "";

    public string Display => Rule.Display;

    public string StatusText
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ForwardDialog : MiuixWindow
{
    private readonly SessionProfile _profile;
    private readonly PortForwarder _forwarder;
    private readonly ObservableCollection<ForwardRow> _rows = new();

    public ForwardDialog(SessionProfile profile, PortForwarder forwarder)
    {
        InitializeComponent();
        _profile = profile;
        _forwarder = forwarder;

        foreach (var rule in profile.Forwards)
        {
            _rows.Add(new ForwardRow { Rule = rule, StatusText = L10n.T("Forward.Stopped") });
        }

        RuleList.ItemsSource = _rows;
        RefreshStatuses();
        if (_rows.Count > 0)
        {
            RuleList.SelectedIndex = 0;
        }
        else
        {
            LoadRule(new ForwardRule());
        }
    }

    private ForwardRow? SelectedRow => RuleList.SelectedItem as ForwardRow;

    private void RefreshStatuses()
    {
        foreach (var row in _rows)
        {
            var running = _forwarder.IsRunning(row.Rule.Id);
            var error = _forwarder.GetError(row.Rule.Id);
            row.StatusText = running
                ? $"{L10n.T("Forward.Running")} · {row.Rule.LocalAddress}:{row.Rule.LocalPort}"
                : error != null ? $"{L10n.T("Sftp.Failed")}: {error}" : L10n.T("Forward.Stopped");
        }
    }

    private void LoadRule(ForwardRule rule)
    {
        NameBox.Text = rule.Name;
        TypeBox.SelectedIndex = rule.Type == ForwardType.Remote ? 1 : 0;
        BindBox.Text = rule.LocalAddress;
        BindPortBox.Text = rule.LocalPort.ToString();
        TargetBox.Text = rule.RemoteHost;
        TargetPortBox.Text = rule.RemotePort.ToString();
        AutoStartSwitch.IsChecked = rule.AutoStart;
    }

    private ForwardRule ReadEditor(ForwardRule target)
    {
        target.Name = NameBox.Text.Trim();
        target.Type = TypeBox.SelectedIndex == 1 ? ForwardType.Remote : ForwardType.Local;
        target.LocalAddress = BindBox.Text.Trim();
        int.TryParse(BindPortBox.Text.Trim(), out var bindPort);
        target.LocalPort = bindPort;
        target.RemoteHost = TargetBox.Text.Trim();
        int.TryParse(TargetPortBox.Text.Trim(), out var targetPort);
        target.RemotePort = targetPort;
        target.AutoStart = AutoStartSwitch.IsChecked == true;
        return target;
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedRow is { } row)
        {
            LoadRule(row.Rule);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var rule = new ForwardRule { LocalPort = 8080, RemotePort = 80 };
        _rows.Add(new ForwardRow { Rule = rule, StatusText = L10n.T("Forward.Stopped") });
        RuleList.SelectedItem = _rows[^1];
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }
        _forwarder.Stop(row.Rule.Id);
        _rows.Remove(row);
        if (_rows.Count > 0)
        {
            RuleList.SelectedIndex = 0;
        }
    }

    private void SaveRule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var rule = ReadEditor(row.Rule);
        if (string.IsNullOrWhiteSpace(rule.LocalAddress) || rule.LocalPort <= 0 || rule.RemotePort <= 0 || string.IsNullOrWhiteSpace(rule.RemoteHost))
        {
            StatusLabel.Text = L10n.T("Forward.Required");
            return;
        }

        rule.Name = string.IsNullOrWhiteSpace(rule.Name)
            ? $"{rule.LocalPort} → {rule.RemoteHost}:{rule.RemotePort}"
            : rule.Name;
        Persist();
        RuleList.Items.Refresh();
        StatusLabel.Text = L10n.T("Action.Save");
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }
        SaveRule_Click(sender, e);
        try
        {
            _forwarder.Start(row.Rule);
            StatusLabel.Text = $"{L10n.T("Forward.Running")} {row.Rule.LocalAddress}:{row.Rule.LocalPort}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"{L10n.T("Sftp.Failed")}: {ex.Message}";
        }
        RefreshStatuses();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }
        _forwarder.Stop(row.Rule.Id);
        StatusLabel.Text = L10n.T("Forward.Stopped");
        RefreshStatuses();
    }

    private void Persist()
    {
        _profile.Forwards = _rows.Select(r => r.Rule).ToList();
        AppState.Current.Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Persist();
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => TitleBarMouseDown(this.Content as FrameworkElement ?? this, e);
}
