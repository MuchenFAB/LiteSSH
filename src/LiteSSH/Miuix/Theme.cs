using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using LiteSSH.Core;

namespace LiteSSH.Miuix;

public static class Theme
{
    public static event EventHandler? Changed;

    public static bool IsDark { get; private set; }

    public static void Apply(ThemeMode mode, string accent)
    {
        var dark = mode == ThemeMode.Dark || (mode == ThemeMode.Auto && SystemIsDark());
        IsDark = dark;

        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };

        var merged = app.Resources.MergedDictionaries;
        var replaced = false;
        for (var i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.Contains("/Themes/Light.xaml") || src.Contains("/Themes/Dark.xaml"))
            {
                merged[i] = dict;
                replaced = true;
                break;
            }
        }
        if (!replaced)
        {
            merged.Add(dict);
        }

        ApplyAccent(accent, dark);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyAccent(string hex, bool dark)
    {
        if (!TryParse(hex, out var baseColor))
        {
            baseColor = Color.FromRgb(0x34, 0x82, 0xFF);
        }

        var accent = baseColor;
        var hover = Shift(baseColor, dark ? 0.12 : -0.1);
        var pressed = Shift(baseColor, dark ? -0.08 : -0.2);
        var soft = Blend(dark ? Color.FromRgb(0x19, 0x1A, 0x1F) : Colors.White, baseColor, dark ? 0.22 : 0.14);

        var res = Application.Current.Resources;
        res["Accent"] = Freeze(new SolidColorBrush(accent));
        res["AccentHover"] = Freeze(new SolidColorBrush(hover));
        res["AccentPressed"] = Freeze(new SolidColorBrush(pressed));
        res["AccentSoft"] = Freeze(new SolidColorBrush(soft));
    }

    public static Color AccentColor()
        => Application.Current?.Resources["Accent"] is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x34, 0x82, 0xFF);

    public static Color ColorOf(string hex, string fallback = "#3482FF")
        => TryParse(hex, out var c) ? c : TryParse(fallback, out var f) ? f : Colors.DodgerBlue;

    public static bool TryParse(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static SolidColorBrush Brush(string hex)
        => Freeze(new SolidColorBrush(ColorOf(hex)));

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Color Shift(Color color, double amount)
    {
        byte Adjust(byte v) => (byte)Math.Clamp(amount >= 0 ? v + (255 - v) * amount : v + v * amount, 0, 255);
        return Color.FromRgb(Adjust(color.R), Adjust(color.G), Adjust(color.B));
    }

    private static Color Blend(Color background, Color foreground, double amount)
        => Color.FromRgb(
            (byte)Math.Clamp(background.R + (foreground.R - background.R) * amount, 0, 255),
            (byte)Math.Clamp(background.G + (foreground.G - background.G) * amount, 0, 255),
            (byte)Math.Clamp(background.B + (foreground.B - background.B) * amount, 0, 255));

    private static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
            {
                return i == 0;
            }
        }
        catch
        {
            // fall through
        }
        return true;
    }

    public static void ApplyWindowChrome(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            var round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch
        {
            // older Windows builds
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void TrackSystemTheme(Window window)
    {
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (AppState.Current.Settings.Theme == ThemeMode.Auto)
            {
                window.Dispatcher.Invoke(() => Apply(ThemeMode.Auto, AppState.Current.Settings.Accent));
            }
        };
    }
}
