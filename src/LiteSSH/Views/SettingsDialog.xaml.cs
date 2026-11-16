using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LiteSSH.Core;
using LiteSSH.Miuix;
using LiteSSH.Terminal;

namespace LiteSSH.Views;

public partial class SettingsDialog : MiuixWindow
{
    private static readonly string[] Accents =
    {
        "#3482FF", "#4F7CFF", "#8A5CF6", "#12A594", "#12945F", "#E0A100", "#E0613B", "#E04A4F",
    };

    private static readonly int[] ScrollbackOptions = { 500, 1000, 2000, 5000, 10000 };

    private bool _loading = true;

    public SettingsDialog()
    {
        InitializeComponent();
        LoadCurrent();
        _loading = false;
    }

    private void LoadCurrent()
    {
        var s = AppState.Current.Settings;

        ThemeBox.SelectedIndex = (int)s.Theme;

        BuildAccents(s.Accent);

        LanguageBox.SelectedIndex = L10n.IsEnglish ? 1 : 0;

        TermThemeBox.Items.Clear();
        TermThemeBox.Items.Add(new ComboBoxItem { Content = L10n.IsEnglish ? "Miuix (auto)" : "Miuix 跟随主题", Tag = "miuix-auto" });
        foreach (var palette in TerminalPalette.All)
        {
            TermThemeBox.Items.Add(new ComboBoxItem { Content = palette.Display, Tag = palette.Id });
        }
        TermThemeBox.SelectedIndex = Math.Max(0, TermThemeBox.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == s.TerminalTheme));

        FontBox.Items.Clear();
        foreach (var family in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            FontBox.Items.Add(new ComboBoxItem { Content = family, Tag = family });
        }
        var fontIndex = FontBox.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => string.Equals((string?)i.Tag, s.FontFamily, StringComparison.OrdinalIgnoreCase));
        if (fontIndex < 0)
        {
            FontBox.Items.Insert(0, new ComboBoxItem { Content = s.FontFamily, Tag = s.FontFamily });
            fontIndex = 0;
        }
        FontBox.SelectedIndex = fontIndex;

        FontSizeSlider.Value = s.FontSize;
        FontSizeLabel.Text = s.FontSize.ToString("0");

        ScrollbackBox.Items.Clear();
        foreach (var option in ScrollbackOptions)
        {
            ScrollbackBox.Items.Add(new ComboBoxItem { Content = option.ToString(), Tag = option });
        }
        var scrollbackIndex = ScrollbackBox.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (int?)i.Tag == s.Scrollback);
        ScrollbackBox.SelectedIndex = scrollbackIndex >= 0 ? scrollbackIndex : 2;

        CopyOnSelectSwitch.IsChecked = s.CopyOnSelect;
        ConfirmCloseSwitch.IsChecked = s.ConfirmOnClose;
        SftpDefaultSwitch.IsChecked = s.ShowSftpByDefault;

        VersionLabel.Text = "1.0.0";
        StorageLabel.Text = AppState.DataPath;
        HintLabel.Text = File.Exists(AppState.DataPath)
            ? $"{new FileInfo(AppState.DataPath).Length / 1024.0:0.#} KB"
            : "";
    }

    private void BuildAccents(string current)
    {
        AccentPanel.Children.Clear();
        foreach (var hex in Accents)
        {
            var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 0),
                Background = Theme.Brush(hex),
                BorderThickness = new Thickness(selected ? 2.5 : 0),
                BorderBrush = Brushes.White,
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                AppState.Current.Settings.Accent = hex;
                Theme.Apply(AppState.Current.Settings.Theme, hex);
                BuildAccents(hex);
                ApplyToPanes();
                AppState.Current.Save();
            };
            AccentPanel.Children.Add(swatch);
        }
    }

    private void ApplyToPanes()
    {
        if (Owner is MainWindow main)
        {
            main.ApplySettings();
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var s = AppState.Current.Settings;
        s.Theme = (ThemeMode)Math.Max(0, ThemeBox.SelectedIndex);
        Theme.Apply(s.Theme, s.Accent);
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        L10n.SetLanguage(LanguageBox.SelectedIndex == 1 ? "en" : "zh");
        ApplyToPanes();
    }

    private void TermThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TermThemeBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        AppState.Current.Settings.TerminalTheme = (string?)item.Tag ?? "miuix-auto";
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        AppState.Current.Settings.FontFamily = (string?)item.Tag ?? "Consolas";
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }
        var size = Math.Round(FontSizeSlider.Value);
        AppState.Current.Settings.FontSize = size;
        if (FontSizeLabel != null)
        {
            FontSizeLabel.Text = size.ToString("0");
        }
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void ScrollbackBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ScrollbackBox.SelectedItem is not ComboBoxItem item || item.Tag is not int value)
        {
            return;
        }
        AppState.Current.Settings.Scrollback = value;
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var s = AppState.Current.Settings;
        s.CopyOnSelect = CopyOnSelectSwitch.IsChecked == true;
        s.ConfirmOnClose = ConfirmCloseSwitch.IsChecked == true;
        s.ShowSftpByDefault = SftpDefaultSwitch.IsChecked == true;
        ApplyToPanes();
        AppState.Current.Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.Save();
        DialogResult = true;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => TitleBarMouseDown(this.Content as FrameworkElement ?? this, e);
}
