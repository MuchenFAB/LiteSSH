using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace LiteSSH.Miuix;

/// <summary>Borderless Miuix window shell: rounded DWM frame plus custom caption handled by content.</summary>
public class MiuixWindow : Window
{
    public MiuixWindow()
    {
        WindowStyle = WindowStyle.None;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        FontFamily = (FontFamily)TryFindResource("UiFont") ?? new FontFamily("Segoe UI");
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "TextPrimary");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var chrome = new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = ResizeMode == ResizeMode.CanResize ? new Thickness(6) : new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        };
        WindowChrome.SetWindowChrome(this, chrome);
        Theme.ApplyWindowChrome(this);
    }

    public void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2 && ResizeMode == ResizeMode.CanResize)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // dragging can throw when the mouse was released early
        }
    }

    public void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
