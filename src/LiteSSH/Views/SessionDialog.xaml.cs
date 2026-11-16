using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteSSH.Core;
using LiteSSH.Miuix;

namespace LiteSSH.Views;

public partial class SessionDialog : MiuixWindow
{
    private static readonly string[] PresetColors =
    {
        "#3482FF", "#8A5CF6", "#12A594", "#12945F", "#E0A100", "#E0613B", "#E04A4F", "#7A8494",
    };

    private readonly SessionProfile? _original;
    private string _color = "#3482FF";

    public SessionDialog(SessionProfile? original, SessionProfile? model)
    {
        InitializeComponent();
        _original = original;
        Result = model ?? new SessionProfile();

        TitleLabel.Text = original == null ? L10n.T("Session.New") : L10n.T("Session.Edit");
        Title = TitleLabel.Text;

        NameBox.Text = Result.Name;
        GroupBox.Text = Result.Group;
        HostBox.Text = Result.Host;
        PortBox.Text = Result.Port.ToString();
        UserBox.Text = Result.User;
        AuthBox.SelectedIndex = Result.Auth == AuthKind.PrivateKey ? 1 : 0;
        PasswordInput.Password = Result.Password;
        SavePasswordSwitch.IsChecked = Result.SavePassword;
        KeyPathBox.Text = Result.KeyPath;
        KeyPassBox.Password = Result.KeyPassphrase;
        _color = string.IsNullOrWhiteSpace(Result.Color) ? "#3482FF" : Result.Color;

        BuildColorSwatches();
        UpdateAuthPanels();
        Loaded += (_, _) => NameBox.Focus();
    }

    public SessionProfile? Result { get; private set; }

    public bool ConnectNow { get; private set; }

    private void BuildColorSwatches()
    {
        ColorsPanel.Children.Clear();
        foreach (var hex in PresetColors)
        {
            var selected = string.Equals(hex, _color, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 8, 8),
                Background = Theme.Brush(hex),
                BorderThickness = new Thickness(selected ? 2.5 : 0),
                BorderBrush = new SolidColorBrush(Theme.ColorOf("#FFFFFF")),
                Cursor = Cursors.Hand,
                Tag = hex,
            };

            if (selected)
            {
                swatch.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Theme.ColorOf(hex),
                    BlurRadius = 6,
                    ShadowDepth = 0,
                    Opacity = 0.9,
                };
            }

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _color = hex;
                BuildColorSwatches();
            };
            ColorsPanel.Children.Add(swatch);
        }
    }

    private void AuthBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    private void UpdateAuthPanels()
    {
        if (PasswordPanel == null || KeyPanel == null)
        {
            return;
        }
        var useKey = AuthBox.SelectedIndex == 1;
        PasswordPanel.Visibility = useKey ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = useKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L10n.T("Session.KeyPath"),
            Filter = "All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            KeyPathBox.Text = dialog.FileName;
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            ErrorLabel.Text = L10n.T("Session.HostRequired");
            return false;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            ErrorLabel.Text = L10n.T("Session.ValidPort");
            return false;
        }

        if (AuthBox.SelectedIndex == 1 && string.IsNullOrWhiteSpace(KeyPathBox.Text))
        {
            ErrorLabel.Text = L10n.T("Session.KeyRequired");
            return false;
        }

        ErrorLabel.Text = "";
        return true;
    }

    private void Collect()
    {
        if (Result == null)
        {
            return;
        }

        Result.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? HostBox.Text.Trim() : NameBox.Text.Trim();
        Result.Group = GroupBox.Text.Trim();
        Result.Host = HostBox.Text.Trim();
        int.TryParse(PortBox.Text.Trim(), out var port);
        Result.Port = port <= 0 ? 22 : port;
        Result.User = string.IsNullOrWhiteSpace(UserBox.Text) ? "root" : UserBox.Text.Trim();
        Result.Auth = AuthBox.SelectedIndex == 1 ? AuthKind.PrivateKey : AuthKind.Password;
        Result.Password = SavePasswordSwitch.IsChecked == true ? PasswordInput.Password : "";
        Result.SavePassword = SavePasswordSwitch.IsChecked == true;
        Result.KeyPath = KeyPathBox.Text.Trim();
        Result.KeyPassphrase = KeyPassBox.Password;
        Result.Color = _color;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }
        Collect();
        ConnectNow = false;
        DialogResult = true;
    }

    private void SaveConnect_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }
        Collect();
        ConnectNow = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => TitleBarMouseDown(this.Content as FrameworkElement ?? this, e);
}
