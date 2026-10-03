using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

internal static class StudioUi
{
    public static Grid Shell(string title, string subtitle, string glyph, Brush accent, UIElement body, params Button[] actions)
    {
        var root = new Grid { Background = Resource("ProjectManagerCardBrush") };
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Vector2LevelEditor;component/Views/ProjectManager/Studios/StudioTheme.xaml", UriKind.Relative) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(24, 18, 24, 18) };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(8), Background = accent, Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        header.Children.Add(icon);
        var labels = new StackPanel { Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        labels.Children.Add(new TextBlock { Text = subtitle, Foreground = Resource("ProjectManagerMutedBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(labels, 1); header.Children.Add(labels);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in actions) { button.Margin = new Thickness(6, 0, 0, 0); buttons.Children.Add(button); }
        Grid.SetColumn(buttons, 2); header.Children.Add(buttons);
        header.SizeChanged += (_, _) =>
        {
            var compact = actions.Length > 0 && header.ActualWidth < buttons.DesiredSize.Width + 380;
            Grid.SetRow(buttons, compact ? 1 : 0);
            Grid.SetColumn(buttons, compact ? 0 : 2);
            Grid.SetColumnSpan(buttons, compact ? 3 : 1);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        };
        root.Children.Add(new Border { BorderBrush = Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = header });
        Grid.SetRow(body, 1); root.Children.Add(body);
        return root;
    }

    public static Border Card(UIElement child, Thickness? margin = null) => new()
    {
        Background = Resource("ProjectManagerCardBrush"), BorderBrush = Resource("ProjectManagerEdgeBrush"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12),
        Margin = margin ?? new Thickness(0), Child = child
    };

    public static TextBox Field(string value = "")
    {
        var field = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(8, 5, 8, 5) };
        field.SetResourceReference(Control.BackgroundProperty, "ProjectManagerFieldBrush");
        return field;
    }
    public static TextBlock Label(string value) => new() { Text = value, Foreground = Resource("ProjectManagerMutedBrush"), FontSize = 11, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var iconOnly = text.Length == 1;
        var button = new Button
        {
            Content = text,
            Padding = iconOnly ? new Thickness(0) : new Thickness(11, 6, 11, 6),
            MinWidth = iconOnly ? 27 : 0,
            MinHeight = iconOnly ? 27 : 26,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "StudioButton");
        if (primary) button.Background = new SolidColorBrush(Color.FromRgb(0, 122, 255));
        if (primary) button.Foreground = Brushes.White;
        button.Click += click;
        return button;
    }

    public static Grid Search(TextBox input, string placeholder)
    {
        var grid = new Grid();
        input.Margin = new Thickness(0);
        input.Padding = new Thickness(27, 5, 8, 5);
        grid.Children.Add(input);
        grid.Children.Add(new TextBlock { Text = "\uE721", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Foreground = Resource("ProjectManagerMutedBrush") });
        var hint = new TextBlock { Text = placeholder, FontSize = 11, Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Foreground = Resource("ProjectManagerMutedBrush") };
        grid.Children.Add(hint);
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return grid;
    }

    public static UIElement Number(TextBox input, int minimum, int maximum, int step = 1)
    {
        var grid = new Grid { Margin = input.Margin };
        input.Margin = new Thickness(0);
        input.Padding = new Thickness(8, 5, 25, 5);
        grid.Children.Add(input);
        var arrows = new StackPanel { Width = 21, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 2, 2) };
        foreach (var direction in new[] { 1, -1 })
        {
            var button = Button(direction > 0 ? "\uE70E" : "\uE70D", (_, _) => {
                if (int.TryParse(input.Text, out var current)) input.Text = Math.Clamp(current + direction * step, minimum, maximum).ToString();
            });
            button.FontFamily = new FontFamily("Segoe MDL2 Assets"); button.FontSize = 7;
            button.MinWidth = 0; button.MinHeight = 0; button.Height = 12; button.Padding = new Thickness(0);
            button.Background = Brushes.Transparent; button.ToolTip = direction > 0 ? "Increase" : "Decrease";
            arrows.Children.Add(button);
        }
        grid.Children.Add(arrows);
        return grid;
    }

    public static UIElement Segments(ComboBox selection)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var buttons = new List<Button>();
        void Update()
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].Background = i == selection.SelectedIndex ? new SolidColorBrush(Color.FromRgb(0, 122, 255)) : Brushes.Transparent;
                buttons[i].Foreground = i == selection.SelectedIndex ? Brushes.White : Resource("WindowTextBrush");
            }
        }
        for (var i = 0; i < selection.Items.Count; i++)
        {
            var index = i;
            var button = Button(selection.Items[i]?.ToString() ?? "", (_, _) => selection.SelectedIndex = index);
            button.Padding = new Thickness(9, 4, 9, 4); button.MinHeight = 25;
            grid.Children.Add(button); buttons.Add(button);
        }
        selection.SelectionChanged += (_, _) => Update();
        Update();
        return new Border { Background = Resource("ProjectManagerHoverBrush"), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = grid, Margin = new Thickness(0, 4, 0, 12) };
    }

    public static TextBlock Heading(string text, string glyph, Color color)
    {
        var block = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color), Margin = new Thickness(0, 0, 0, 12) };
        block.Inlines.Add(new System.Windows.Documents.Run(glyph) { FontFamily = new FontFamily("Segoe MDL2 Assets") });
        block.Inlines.Add("  " + text);
        return block;
    }
    public static DynamicResourceExtension Dynamic(string key) => new(key);
    public static Brush Resource(string key) => Services.EditorTheme.SharedBrush(key);
}
