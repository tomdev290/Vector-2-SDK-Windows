using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Controls;

public sealed class DynamicTimelineControl : FrameworkElement
{
    public static readonly DependencyProperty TotalFramesProperty = DependencyProperty.Register(
        nameof(TotalFrames), typeof(int), typeof(DynamicTimelineControl),
        new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentFrameProperty = DependencyProperty.Register(
        nameof(CurrentFrame), typeof(int), typeof(DynamicTimelineControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty KeyframesProperty = DependencyProperty.Register(
        nameof(Keyframes), typeof(IEnumerable<DynamicStudioKeyframe>), typeof(DynamicTimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnKeyframesChanged));

    public static readonly DependencyProperty SelectedKeyframeProperty = DependencyProperty.Register(
        nameof(SelectedKeyframe), typeof(DynamicStudioKeyframe), typeof(DynamicTimelineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private INotifyCollectionChanged? _observedCollection;
    private bool _draggingPlayhead;

    public int TotalFrames { get => (int)GetValue(TotalFramesProperty); set => SetValue(TotalFramesProperty, value); }
    public int CurrentFrame { get => (int)GetValue(CurrentFrameProperty); set => SetValue(CurrentFrameProperty, value); }
    public IEnumerable<DynamicStudioKeyframe>? Keyframes { get => (IEnumerable<DynamicStudioKeyframe>?)GetValue(KeyframesProperty); set => SetValue(KeyframesProperty, value); }
    public DynamicStudioKeyframe? SelectedKeyframe { get => (DynamicStudioKeyframe?)GetValue(SelectedKeyframeProperty); set => SetValue(SelectedKeyframeProperty, value); }

    public DynamicTimelineControl()
    {
        MinHeight = 88;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var background = ResourceBrush("ControlBrush", Color.FromRgb(245, 246, 248));
        var border = ResourceBrush("ControlBorderBrush", Color.FromRgb(199, 203, 209));
        var text = ResourceBrush("WindowTextBrush", Color.FromRgb(32, 33, 36));
        var muted = ResourceBrush("MutedTextBrush", Color.FromRgb(107, 114, 128));
        var blue = new SolidColorBrush(Color.FromRgb(35, 122, 232));
        dc.DrawRoundedRectangle(background, new Pen(border, 1), bounds, 8, 8);

        var timelineWidth = Math.Max(1, bounds.Width - 20);
        var left = 10.0;
        for (var tick = 0; tick <= 10; tick++)
        {
            var x = left + (tick / 10.0 * timelineWidth);
            dc.DrawLine(new Pen(border, 1), new Point(x, 0), new Point(x, bounds.Height));
            var label = ((int)Math.Round(Math.Max(1, TotalFrames) * tick / 10.0)).ToString();
            var formatted = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                muted,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(formatted, new Point(Math.Clamp(x - formatted.Width / 2, 3, bounds.Width - formatted.Width - 3), 5));
        }

        var middle = bounds.Height * 0.58;
        foreach (var keyframe in Keyframes ?? [])
        {
            var x = FrameX(keyframe.Frame, left, timelineWidth);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(x, middle - 7), true, true);
                context.LineTo(new Point(x + 7, middle), true, false);
                context.LineTo(new Point(x, middle + 7), true, false);
                context.LineTo(new Point(x - 7, middle), true, false);
            }
            geometry.Freeze();
            var fill = SelectedKeyframe?.Id == keyframe.Id ? blue : background;
            dc.DrawGeometry(fill, new Pen(blue, 2), geometry);
        }

        var playheadX = FrameX(CurrentFrame, left, timelineWidth);
        dc.DrawLine(new Pen(blue, 2), new Point(playheadX, 0), new Point(playheadX, bounds.Height));
        dc.DrawGeometry(blue, null, PlayheadHead(playheadX));

        var frameText = new FormattedText($"F{CurrentFrame}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10,
            text,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(frameText, new Point(Math.Clamp(playheadX + 5, 3, bounds.Width - frameText.Width - 3), bounds.Height - 18));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _draggingPlayhead = true;
        UpdateFromPointer(e.GetPosition(this), chooseKeyframe: true);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingPlayhead && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateFromPointer(e.GetPosition(this), chooseKeyframe: false);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_draggingPlayhead) return;
        UpdateFromPointer(e.GetPosition(this), chooseKeyframe: false);
        _draggingPlayhead = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void UpdateFromPointer(Point point, bool chooseKeyframe)
    {
        var width = Math.Max(1, ActualWidth - 20);
        var ratio = Math.Clamp((point.X - 10) / width, 0, 1);
        CurrentFrame = (int)Math.Round(ratio * Math.Max(1, TotalFrames));
        if (!chooseKeyframe) return;

        var nearest = (Keyframes ?? [])
            .Select(keyframe => (keyframe, distance: Math.Abs(FrameX(keyframe.Frame, 10, width) - point.X)))
            .Where(candidate => candidate.distance <= 11)
            .OrderBy(candidate => candidate.distance)
            .FirstOrDefault();
        if (nearest.keyframe is not null)
        {
            SelectedKeyframe = nearest.keyframe;
            CurrentFrame = nearest.keyframe.Frame;
        }
    }

    private double FrameX(int frame, double left, double width)
        => left + Math.Clamp(frame / (double)Math.Max(1, TotalFrames), 0, 1) * width;

    private static StreamGeometry PlayheadHead(double x)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(x - 5, 0), true, true);
        context.LineTo(new Point(x + 5, 0), true, false);
        context.LineTo(new Point(x, 7), true, false);
        geometry.Freeze();
        return geometry;
    }

    private Brush ResourceBrush(string key, Color fallback)
        => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static void OnKeyframesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (DynamicTimelineControl)dependencyObject;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged -= control.OnCollectionChanged;
        }
        control._observedCollection = args.NewValue as INotifyCollectionChanged;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged += control.OnCollectionChanged;
        }
        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
