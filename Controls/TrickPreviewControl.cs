using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Controls;

public sealed class TrickPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty PlaybackProperty = DependencyProperty.Register(nameof(Playback), typeof(GameTrickPlayback), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(nameof(Frame), typeof(int), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowDebugEdgesProperty = DependencyProperty.Register(nameof(ShowDebugEdges), typeof(bool), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowModelOutlinesProperty = DependencyProperty.Register(nameof(ShowModelOutlines), typeof(bool), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowModelNodePointsProperty = DependencyProperty.Register(nameof(ShowModelNodePoints), typeof(bool), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PreviewBackgroundProperty = DependencyProperty.Register(nameof(PreviewBackground), typeof(Brush), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(22, 27, 34)), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PreviewZoomProperty = DependencyProperty.Register(nameof(PreviewZoom), typeof(double), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(3.5, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CenterOnVisibleModelProperty = DependencyProperty.Register(nameof(CenterOnVisibleModel), typeof(bool), typeof(TrickPreviewControl), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private Vector _pan;
    private Point? _dragOrigin;
    public GameTrickPlayback? Playback { get => (GameTrickPlayback?)GetValue(PlaybackProperty); set => SetValue(PlaybackProperty, value); }
    public int Frame { get => (int)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public bool ShowDebugEdges { get => (bool)GetValue(ShowDebugEdgesProperty); set => SetValue(ShowDebugEdgesProperty, value); }
    public bool ShowModelOutlines { get => (bool)GetValue(ShowModelOutlinesProperty); set => SetValue(ShowModelOutlinesProperty, value); }
    public bool ShowModelNodePoints { get => (bool)GetValue(ShowModelNodePointsProperty); set => SetValue(ShowModelNodePointsProperty, value); }
    public Brush PreviewBackground { get => (Brush)GetValue(PreviewBackgroundProperty); set => SetValue(PreviewBackgroundProperty, value); }
    public double PreviewZoom { get => (double)GetValue(PreviewZoomProperty); set => SetValue(PreviewZoomProperty, value); }
    public bool CenterOnVisibleModel { get => (bool)GetValue(CenterOnVisibleModelProperty); set => SetValue(CenterOnVisibleModelProperty, value); }

    public TrickPreviewControl() => ClipToBounds = true;

    public void ResetView() { _pan = default; PreviewZoom = 3.5; InvalidateVisual(); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        PreviewZoom = Math.Clamp(PreviewZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), .4, 10);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        _dragOrigin = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragOrigin is not Point previous) return;
        var current = e.GetPosition(this);
        _pan += current - previous;
        _dragOrigin = current;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _dragOrigin = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(PreviewBackground, null, new Rect(RenderSize), 5, 5);
        if (Playback is null || Playback.Frames.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var model = Playback.Model;
        var current = model.ResolvePoints(Playback.Frames[Math.Clamp(Frame, 0, Playback.Frames.Count - 1)]);
        var initial = model.ResolvePoints(Playback.Frames[Math.Clamp(Playback.StartFrame, 0, Playback.Frames.Count - 1)]);
        if (current.Count == 0 || initial.Count == 0) return;
        var pivot = Pivot(initial, Playback.Move.PivotNode);
        var visibleNames = model.Edges.SelectMany(edge => new[] { edge.StartName, edge.EndName })
            .Concat(model.Triangles.SelectMany(triangle => triangle.NodeNames))
            .Concat(model.NodePoints.Select(sphere => sphere.NodeName))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var fitPose = CenterOnVisibleModel ? current : initial;
        var visiblePoints = visibleNames.Where(name => IsBodyPoint(name) && fitPose.ContainsKey(name)).Select(name => fitPose[name]).ToArray();
        if (visiblePoints.Length == 0)
            visiblePoints = model.Nodes.Take(41).Where(node => fitPose.ContainsKey(node.Name)).Select(node => fitPose[node.Name]).ToArray();
        if (visiblePoints.Length == 0) visiblePoints = fitPose.Values.ToArray();
        var width = visiblePoints.Max(point => point.X) - visiblePoints.Min(point => point.X);
        var height = visiblePoints.Max(point => point.Y) - visiblePoints.Min(point => point.Y);
        var fitFraction = CenterOnVisibleModel ? .74 : .8;
        var scale = Math.Min(PreviewZoom / 3.0, Math.Min(ActualWidth * fitFraction / Math.Max(width, 1), ActualHeight * fitFraction / Math.Max(height, 1)));
        var originX = CenterOnVisibleModel ? (visiblePoints.Min(point => point.X) + visiblePoints.Max(point => point.X)) / 2 : pivot.X;
        var originY = CenterOnVisibleModel ? (visiblePoints.Min(point => point.Y) + visiblePoints.Max(point => point.Y)) / 2 : pivot.Y;
        var anchorY = ActualHeight * (CenterOnVisibleModel ? .5 : .7);
        Point Screen(Point point) => new(ActualWidth / 2 + _pan.X + (point.X - originX) * scale, anchorY + _pan.Y + (point.Y - originY) * scale);
        var modelBrush = Brushes.Black;

        foreach (var triangle in model.Triangles)
        {
            var vertices = triangle.NodeNames.Where(name => IsBodyPoint(name) && current.ContainsKey(name)).Select(name => Screen(current[name])).ToArray();
            if (vertices.Length != 3) continue;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open()) { path.BeginFigure(vertices[0], true, true); path.PolyLineTo(vertices.Skip(1).ToArray(), true, false); }
            geometry.Freeze();
            dc.DrawGeometry(modelBrush,
                ShowModelOutlines ? new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), Math.Max(.5, .8 * scale)) : null, geometry);
        }

        var edges = model.Edges.GroupBy(edge => edge.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (model.Capsules.Count > 0)
        {
            foreach (var capsule in model.Capsules)
            {
                if (!edges.TryGetValue(capsule.EdgeName, out var edge) || !IsBodyPoint(edge.StartName) || !IsBodyPoint(edge.EndName) ||
                    !current.TryGetValue(edge.StartName, out var start) || !current.TryGetValue(edge.EndName, out var end)) continue;
                var difference = start - end;
                var a = start - difference * capsule.Margin1;
                var b = end + difference * capsule.Margin2;
                var thickness = Math.Max(1.5, capsule.Radius * 2 * scale);
                var pen = new Pen(modelBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (ShowModelOutlines)
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), thickness + Math.Max(1, 2 * scale))
                        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Screen(a), Screen(b));
                dc.DrawLine(pen, Screen(a), Screen(b));
            }
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(78, 214, 231)), Math.Max(1.2, scale * 2));
            foreach (var edge in model.Edges)
                if (IsBodyPoint(edge.StartName) && IsBodyPoint(edge.EndName) && current.TryGetValue(edge.StartName, out var start) && current.TryGetValue(edge.EndName, out var end))
                    dc.DrawLine(pen, Screen(start), Screen(end));
        }
        if (ShowModelNodePoints)
            foreach (var sphere in model.NodePoints)
                if (IsBodyPoint(sphere.NodeName) && current.TryGetValue(sphere.NodeName, out var point))
                    dc.DrawEllipse(Brushes.Black, null,
                        Screen(point), Math.Max(1.5, sphere.Radius * scale), Math.Max(1.5, sphere.Radius * scale));
        if (ShowDebugEdges)
            foreach (var node in model.Nodes.Take(46))
                if (current.TryGetValue(node.Name, out var point))
                    dc.DrawEllipse(node.Name.Equals(Playback.Move.PivotNode, StringComparison.OrdinalIgnoreCase) ? Brushes.Orange : Brushes.White, null, Screen(point), 2.4, 2.4);
    }

    private static Point Pivot(IReadOnlyDictionary<string, Point> points, string name)
    {
        foreach (var candidate in new[] { name, "DetectorH", "NPivot", "COM" })
            if (points.TryGetValue(candidate, out var point)) return point;
        return points.Values.First();
    }

    private static bool IsBodyPoint(string name) =>
        !name.Equals("DetectorV", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("Camera", StringComparison.OrdinalIgnoreCase);
}
