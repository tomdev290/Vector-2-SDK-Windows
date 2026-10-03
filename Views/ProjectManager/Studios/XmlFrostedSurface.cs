using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

internal static class XmlFrostedSurface
{
    public static Action Apply(Border surface, FrameworkElement anchor, double x, double y, Func<Point>? position = null)
    {
        if (surface.Child is not UIElement content || Window.GetWindow(anchor) is not { } window) return () => { };
        if (window.ActualWidth <= 0 || window.ActualHeight <= 0) return () => { };
        var snapshot = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        snapshot.Render(window);
        snapshot.Freeze();
        var layers = new Grid();
        var backdrop = new Border { IsHitTestVisible = false, Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance } };
        var wash = new Border { Opacity = .72, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, "ProjectManagerCardBrush");
        surface.Child = null;
        layers.Children.Add(backdrop); layers.Children.Add(wash); layers.Children.Add(content);
        surface.Background = Brushes.Transparent;
        surface.Child = layers;
        void Update()
        {
            if (layers.ActualWidth <= 0 || layers.ActualHeight <= 0) return;
            var offset = position?.Invoke() ?? new Point(x, y);
            var origin = anchor.TranslatePoint(new Point(offset.X + 12, offset.Y + 12), window);
            // Snapshot sampling avoids a WPF inheritance cycle between popup and owner.
            backdrop.Background = new ImageBrush(snapshot) { ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(origin, new Size(layers.ActualWidth, layers.ActualHeight)), Stretch = Stretch.Fill };
            var radius = Math.Max(0, surface.CornerRadius.TopLeft - surface.BorderThickness.Left);
            layers.Clip = new RectangleGeometry(new Rect(0, 0, layers.ActualWidth, layers.ActualHeight), radius, radius);
        }
        layers.Loaded += (_, _) => Update();
        layers.SizeChanged += (_, _) => Update();
        return Update;
    }
}
