using System.Windows;
using System.Windows.Threading;
using LiteSSH.Core;
using LiteSSH.Miuix;
using LiteSSH.Views;

namespace LiteSSH;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "LiteSSH.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("LiteSSH is already running.", "LiteSSH", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Theme.Apply(AppState.Current.Settings.Theme, AppState.Current.Settings.Accent);

        DispatcherUnhandledException += OnUnhandledException;

        Ssh.HostKeyVerifier = request =>
        {
            var dispatcher = Current?.Dispatcher;
            if (dispatcher == null)
            {
                return false;
            }

            return dispatcher.Invoke(() =>
            {
                var dialog = new HostKeyDialog(request);
                return dialog.ShowDialog() == true;
            });
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (!Store.IsDataFolderWritable())
        {
            window.Dispatcher.BeginInvoke(() => window.ShowStorageWarning());
        }

        var connectTarget = ParseConnectArgument(e.Args);
        if (connectTarget != null)
        {
            window.Dispatcher.BeginInvoke(() => window.ConnectByName(connectTarget));
        }
    }

    private static string? ParseConnectArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--connect" or "-c" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            if (args[i].StartsWith("--connect=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--connect=".Length..];
            }
        }
        return null;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            MessageBox.Show(e.Exception.Message, "LiteSSH", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // nothing to do
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppState.Current.Save();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
