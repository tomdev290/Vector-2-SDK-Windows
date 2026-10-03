using System.Windows;
using System.Windows.Controls;

namespace Vector2LevelEditor.Views;

public static class RoomWeaverVariantDialog
{
    public static IReadOnlyDictionary<string, string>? Choose(IReadOnlyDictionary<string, string[]> options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.Count == 0) return result;
        var window = new Window { Title = "Room Weaver Variants", Width = 500, Height = 420,
            MinWidth = 380, MinHeight = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow };
        window.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        window.SetResourceReference(Control.ForegroundProperty, "WindowTextBrush");
        var shell = new DockPanel { Margin = new Thickness(18) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var accept = new Button { Content = "Import", IsDefault = true, MinWidth = 80 };
        accept.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Bottom); shell.Children.Add(actions);
        var fields = new StackPanel();
        foreach (var pair in options.Where(pair => pair.Value.Length > 0))
        {
            fields.Children.Add(new TextBlock { Text = pair.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 5) });
            var picker = new ComboBox { ItemsSource = pair.Value, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
            result[pair.Key] = pair.Value[0];
            var key = pair.Key;
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string value) result[key] = value; };
            fields.Children.Add(picker);
        }
        shell.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = shell;
        return window.ShowDialog() == true ? result : null;
    }
}
