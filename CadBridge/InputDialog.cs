using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CadBridge
{
    // Tiny "type a name" window (WPF has no built-in InputBox). Returns null if cancelled.
    public static class InputDialog
    {
        public static string Show(Window owner, string title, string prompt, string defaultText)
        {
            var win = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };

            win.SetResourceReference(Window.BackgroundProperty, "BgBrush");
            win.SetResourceReference(Window.ForegroundProperty, "TextBrush");
            win.SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(win);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });

            var box = new TextBox
            {
                Text = defaultText ?? "",
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0)
            };
            panel.Children.Add(box);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "OK", Width = 90, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 30, IsCancel = true };
            try
            {
                ok.Style = (Style)Application.Current.Resources["PrimaryButton"];
                cancel.Style = (Style)Application.Current.Resources["SecondaryButton"];
            }
            catch { }

            string result = null;
            ok.Click += (s, e) => { result = box.Text; win.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            win.Content = panel;
            win.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            win.ShowDialog();
            return result;
        }
    }
}
