using System.Windows;
using System.Windows.Input;
using LiteSSH.Core;
using LiteSSH.Miuix;

namespace LiteSSH.Views;

public partial class HostKeyDialog : MiuixWindow
{
    public HostKeyDialog(HostKeyRequest request)
    {
        InitializeComponent();
        HostLabel.Text = $"{request.Host}:{request.Port}";
        FingerprintLabel.Text = request.Fingerprint;
        Title = L10n.T("HostKey.Title");

        if (request.Changed)
        {
            WarningPanel.Visibility = Visibility.Visible;
            DescriptionLabel.Visibility = Visibility.Collapsed;
        }

        if (request.Known)
        {
            ExpectedPanel.Visibility = Visibility.Visible;
            ExpectedLabel.Text = request.Expected;
        }
    }

    private void Trust_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => TitleBarMouseDown(this.Content as FrameworkElement ?? this, e);
}
