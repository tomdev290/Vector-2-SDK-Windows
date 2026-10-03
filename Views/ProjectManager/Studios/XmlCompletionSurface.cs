using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

internal sealed class XmlCompletionSurface : Border
{
    private readonly RotateTransform _phase = new(0, .5, .5);
    private readonly TextBlock _count = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly LinearGradientBrush _edge = new();
    private readonly ListBox _choices;
    private string? _recommended;
    public Brush GlowBrush => _edge;

    public XmlCompletionSurface(ListBox choices)
    {
        _choices = choices;
        Padding = new Thickness(10); Width = 304; CornerRadius = new CornerRadius(18);
        var fill = StudioUi.Resource("ProjectManagerCardBrush") is SolidColorBrush solid ? solid.Color : Colors.White;
        fill.A = 210; Background = new SolidColorBrush(fill);
        BorderBrush = StudioUi.Resource("ProjectManagerEdgeBrush"); BorderThickness = new Thickness(1);
        Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = .2 };
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(8, 3, 8, 6) };
        DockPanel.SetDock(_count, Dock.Right); header.Children.Add(_count);
        header.Children.Add(new TextBlock { Text = "{ }", Foreground = Brushes.MediumPurple,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) });
        header.Children.Add(new TextBlock { Text = "XML Assistant", FontWeight = FontWeights.SemiBold, FontSize = 11 });
        panel.Children.Add(header);
        panel.Children.Add(new Border { Height = 1, Background = StudioUi.Resource("ProjectManagerEdgeBrush"), Margin = new Thickness(0, 0, 0, 4) });
        foreach (var (color, offset) in new[] { (Colors.Cyan, 0d), (Colors.RoyalBlue, .25),
            (Colors.MediumPurple, .5), (Colors.HotPink, .75), (Colors.Orange, 1d) })
            _edge.GradientStops.Add(new GradientStop(color, offset));
        _edge.RelativeTransform = _phase;
        choices.MinWidth = 0; choices.MaxHeight = 360; choices.BorderThickness = new Thickness(0);
        choices.Background = Brushes.Transparent;
        choices.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("WindowTextBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1.8)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 3)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        frame.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame }));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(225, 236, 255))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(19, 42, 73))));
        style.Triggers.Add(selected);
        var recommended = new Trigger { Property = FrameworkElement.TagProperty, Value = true };
        recommended.Setters.Add(new Setter(Control.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(GlowBrush)) { Source = this }));
        recommended.Setters.Add(new Setter(UIElement.EffectProperty, new DropShadowEffect { Color = Colors.HotPink,
            BlurRadius = 9, ShadowDepth = 0, Opacity = .45 }));
        style.Triggers.Add(recommended); choices.ItemContainerStyle = style;
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var badge = new FrameworkElementFactory(typeof(TextBlock));
        badge.SetValue(DockPanel.DockProperty, Dock.Right);
        badge.SetValue(TextBlock.TextProperty, "Recommended"); badge.SetValue(TextBlock.FontSizeProperty, 9d);
        badge.SetValue(TextBlock.ForegroundProperty, Brushes.RoyalBlue);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        badge.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("Tag") {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
            Converter = new BooleanToVisibilityConverter() });
        row.AppendChild(badge);
        var icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetValue(TextBlock.TextProperty, "</>");
        icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0)); row.AppendChild(icon);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        name.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
        name.SetValue(TextBlock.FontSizeProperty, 13d); name.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        row.AppendChild(name);
        choices.ItemTemplate = new DataTemplate { VisualTree = row };
        panel.Children.Add(choices);
        choices.ItemContainerGenerator.StatusChanged += (_, _) => RefreshRecommendation();
        Child = panel;
    }

    public void Update(int count, string? recommended)
    {
        _count.Text = count.ToString(); _recommended = recommended; RefreshRecommendation();
    }
    private void RefreshRecommendation()
    {
        foreach (var item in _choices.Items)
            if (_choices.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                container.Tag = item is string name && name == _recommended;
    }
    public void Open()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        _phase.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.5))
            { RepeatBehavior = RepeatBehavior.Forever });
        BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(140)));
    }
    public void Close()
    {
        _phase.BeginAnimation(RotateTransform.AngleProperty, null);
        BeginAnimation(OpacityProperty, null);
    }
}
