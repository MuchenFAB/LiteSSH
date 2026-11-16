using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using LiteSSH.Core;
using LiteSSH.Miuix;

namespace LiteSSH.Views;

public partial class SftpPanel : UserControl, IDisposable
{
    private readonly SessionProfile _profile;
    private SftpService? _service;
    private string _path = "";
    private bool _loaded;
    private bool _busy;

    public SftpPanel(SessionProfile profile)
    {
        InitializeComponent();
        _profile = profile;
        L10n.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_path))
        {
            PathBox.Text = _path;
        }
    }

    public void Reload()
    {
        if (_busy)
        {
            return;
        }
        _ = LoadAsync(_loaded ? _path : "");
    }

    private async Task LoadAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetStatus(L10n.T("Sftp.Loading"), null);
        try
        {
            _service ??= new SftpService(_profile);
            var service = _service;
            var (resolved, entries) = await Task.Run(() =>
            {
                var target = string.IsNullOrEmpty(path) ? service!.Home() : path;
                return (target, service!.List(target));
            });

            _path = resolved;
            _loaded = true;
            PathBox.Text = resolved;
            FileList.ItemsSource = entries;
            SetStatus(entries.Count == 0 ? L10n.T("Sftp.Empty") : $"{entries.Count}", null);
        }
        catch (Exception ex)
        {
            SetStatus($"{L10n.T("Sftp.Failed")}: {ex.Message}", null);
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetStatus(string text, double? progress)
    {
        StatusLabel.Text = text;
        if (progress == null)
        {
            Progress.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
        }
        else
        {
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = progress < 0;
            Progress.Value = progress >= 0 ? progress.Value : 0;
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_path))
        {
            Reload();
            return;
        }
        _ = LoadAsync(SftpService.Parent(_path));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        var target = PathBox.Text.Trim();
        if (target.Length > 0)
        {
            _ = LoadAsync(target);
        }
        e.Handled = true;
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not RemoteEntry entry)
        {
            return;
        }
        if (entry.IsDirectory)
        {
            _ = LoadAsync(entry.FullPath);
        }
    }

    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FileList.SelectedItem is RemoteEntry entry && entry.IsDirectory)
        {
            _ = LoadAsync(entry.FullPath);
            e.Handled = true;
        }
    }

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrEmpty(_path))
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = L10n.T("Sftp.UploadPrompt") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        _ = TransferAsync(async () =>
        {
            _service ??= new SftpService(_profile);
            var service = _service;
            foreach (var file in dialog.FileNames)
            {
                var remote = SftpService.Combine(_path, System.IO.Path.GetFileName(file));
                var length = new System.IO.FileInfo(file).Length;
                SetStatus($"{L10n.T("Sftp.Transferring")} {System.IO.Path.GetFileName(file)}", 0);
                await Task.Run(() => service.Upload(file, remote, (done, _) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        Progress.IsIndeterminate = false;
                        Progress.Value = length > 0 ? Math.Min(100, done * 100.0 / length) : 0;
                        SetStatus($"{L10n.T("Sftp.Transferring")} {System.IO.Path.GetFileName(file)}  {RemoteEntry.FormatSize(done)}", null);
                        Progress.Visibility = Visibility.Visible;
                        Progress.Value = length > 0 ? Math.Min(100, done * 100.0 / length) : 0;
                    });
                }, CancellationToken.None));
            }
        });
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || FileList.SelectedItem is not RemoteEntry entry || entry.IsDirectory)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = entry.Name,
            Title = L10n.T("Sftp.DownloadPrompt"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        _ = TransferAsync(async () =>
        {
            _service ??= new SftpService(_profile);
            var service = _service;
            SetStatus($"{L10n.T("Sftp.Transferring")} {entry.Name}", 0);
            await Task.Run(() => service.Download(entry.FullPath, dialog.FileName, (done, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Progress.Visibility = Visibility.Visible;
                    Progress.IsIndeterminate = false;
                    Progress.Value = entry.Size > 0 ? Math.Min(100, done * 100.0 / entry.Size) : 0;
                    SetStatus($"{L10n.T("Sftp.Transferring")} {entry.Name}  {RemoteEntry.FormatSize(done)}", null);
                });
            }, CancellationToken.None));
        });
    }

    private async Task TransferAsync(Func<Task> action)
    {
        _busy = true;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        try
        {
            await action();
            SetStatus(L10n.T("Sftp.Done"), null);
        }
        catch (Exception ex)
        {
            SetStatus($"{L10n.T("Sftp.Failed")}: {ex.Message}", null);
        }
        finally
        {
            _busy = false;
            Progress.Visibility = Visibility.Collapsed;
            _ = LoadAsync(_path);
        }
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Prompt(Window.GetWindow(this), L10n.T("Sftp.NewFolderTitle"), L10n.T("Sftp.Name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _ = TransferAsync(() => Task.Run(() =>
        {
            _service ??= new SftpService(_profile);
            _service.CreateDirectory(SftpService.Combine(_path, name.Trim()));
        }));
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not RemoteEntry entry)
        {
            return;
        }

        var name = Dialogs.Prompt(Window.GetWindow(this), L10n.T("Sftp.RenameTitle"), L10n.T("Sftp.Name"), entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name)
        {
            return;
        }

        _ = TransferAsync(() => Task.Run(() =>
        {
            _service ??= new SftpService(_profile);
            _service.Rename(entry.FullPath, SftpService.Combine(_path, name.Trim()));
        }));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var entries = FileList.SelectedItems.OfType<RemoteEntry>().ToList();
        if (entries.Count == 0)
        {
            return;
        }

        if (!Dialogs.Confirm(Window.GetWindow(this), L10n.T("Sftp.DeleteConfirm"), L10n.T("Action.Delete"), danger: true))
        {
            return;
        }

        _ = TransferAsync(() => Task.Run(() =>
        {
            _service ??= new SftpService(_profile);
            foreach (var entry in entries)
            {
                _service.Delete(entry);
            }
        }));
    }

    public void Dispose()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        try { _service?.Dispose(); } catch { }
        _service = null;
    }
}

