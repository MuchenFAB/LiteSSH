using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiteSSH.Core;

namespace LiteSSH.Miuix;

public static class Dialogs
{
    public static void Info(Window? owner, string message, string? title = null)
        => Build(owner, title ?? L10n.T("Common.Tip"), message, oneButton: true);

    public static bool Confirm(Window? owner, string message, string? title = null, bool danger = false)
        => Build(owner, title ?? L10n.T("Common.Confirm"), message, oneButton: false, danger: danger);

    public static string? Prompt(Window? owner, string title, string label, string initial = "", bool password = false)
    {
        var window = CreateWindow(owner, title, 420, out var content);
        var stack = new StackPanel { Margin = new Thickness(24, 6, 24, 24) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.FindResource("CaptionText"),
            Margin = new Thickness(0, 0, 0, 6),
        });

        Control input;
        if (password)
        {
            input = new PasswordBox { Style = (Style)Application.Current.FindResource("MiuixPasswordBox"), Password = initial };
        }
        else
        {
            input = new TextBox { Style = (Style)Application.Current.FindResource("MiuixTextBox"), Text = initial };
        }
        stack.Children.Add(input);

        string? result = null;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = MakeButton(L10n.T("Action.Cancel"), "SecondaryButton");
        cancel.Click += (_, _) => window.DialogResult = false;
        var ok = MakeButton(L10n.T("Action.Ok"), "PrimaryButton");
        ok.Click += (_, _) =>
        {
            result = input is PasswordBox pb ? pb.Password : ((TextBox)input).Text;
            window.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        content.Children.Add(stack);

        window.Loaded += (_, _) =>
        {
            input.Focus();
            if (input is TextBox tb)
            {
                tb.SelectAll();
            }
        };

        return window.ShowDialog() == true ? result : null;
    }

    private static bool Build(Window? owner, string title, string message, bool oneButton, bool danger = false)
    {
        var window = CreateWindow(owner, title, 430, out var content);
        var stack = new StackPanel { Margin = new Thickness(24, 6, 24, 24) };
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Style = (Style)Application.Current.FindResource("BodyText"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
        };
        if (!oneButton)
        {
            var cancel = MakeButton(L10n.T("Action.Cancel"), "SecondaryButton");
            cancel.Click += (_, _) => window.DialogResult = false;
            buttons.Children.Add(cancel);
        }
        var ok = MakeButton(L10n.T("Action.Ok"), danger ? "DangerButton" : "PrimaryButton");
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        content.Children.Add(stack);

        return window.ShowDialog() == true;
    }

    private static Button MakeButton(string text, string styleKey)
        => new()
        {
            Content = text,
            Style = (Style)Application.Current.FindResource(styleKey),
            MinWidth = 88,
            Margin = new Thickness(8, 0, 0, 0),
        };

    private static MiuixWindow CreateWindow(Window? owner, string title, double width, out Grid content)
    {
        var window = new MiuixWindow
        {
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new Grid { Background = Brushes.Transparent };
        bar.MouseLeftButtonDown += (_, e) => window.TitleBarMouseDown(bar, e);
        bar.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.FindResource("H2Text"),
            Margin = new Thickness(22, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var close = new Button
        {
            Content = "\uE711",
            Style = (Style)Application.Current.FindResource("ChromeCloseButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        close.Click += (_, _) => window.Close();
        bar.Children.Add(close);
        root.Children.Add(bar);

        content = new Grid();
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        window.Content = root;
        return window;
    }
}
