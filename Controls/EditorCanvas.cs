using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Controls;

/// <summary>
/// The Windows canvas: grid, drawing, hit testing, panning, zooming, resize
/// handles, rotation handles, and V-snap.
///
/// Think of this as the WPF twin of CanvasRenderer.swift. It should not know
/// Vector 2 XML trivia; it only turns mouse movement into editor-space numbers.
/// The ViewModel decides what those numbers do to the document.
/// </summary>
public sealed class EditorCanvas : FrameworkElement
{
    private const double ResizeHandleVisualSize = 12;
    private const double ResizeHandleHitSize = 24;
    private const double ResizeEdgeHitThickness = 9;
    private const double RotationHandleDistance = 36;
    private const double RotationHandleRadius = 7;
    private static readonly Dictionary<string, BitmapImage?> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    private Point? _lastDragScreen;
    private LevelNode? _dragNode;
    private Point _dragStartScreen;
    private Point _dragStartWorld;
    private Vector _snapOffsetWorld;
    private Vector _moveRemainder;
    private Vector _resizeRemainder;
    private Point _rotationCenterWorld;
    private bool _isPanning;
    private bool _isMarqueeSelecting;
    private bool _isDraggingTrickPreview;
    private Point _marqueeStartScreen;
    private Point _marqueeCurrentScreen;
    private Vector _pan = new(420, 220);
    private ResizeHandle _activeResizeHandle = ResizeHandle.None;
    private MainWindowViewModel? _subscribedViewModel;
    private LevelDocument? _subscribedDocument;
    private readonly HashSet<LevelNode> _subscribedNodes = [];

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MainWindowViewModel), typeof(EditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnViewModelChanged));

    public MainWindowViewModel? ViewModel
    {
        get => (MainWindowViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public bool CenterOnSelection()
    {
        var node = ViewModel?.SelectedDocument.SelectedNode;
        if (node is null || ActualWidth <= 0 || ActualHeight <= 0) return false;
        var bounds = node.VisualBounds;
        var zoom = ViewModel!.Zoom;
        _pan = new Vector(ActualWidth / 2 - (bounds.Left + bounds.Width / 2) * zoom,
            ActualHeight / 2 - (bounds.Top + bounds.Height / 2) * zoom);
        InvalidateVisual();
        return true;
    }

    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (EditorCanvas)dependencyObject;
        canvas.AttachViewModel(e.OldValue as MainWindowViewModel, e.NewValue as MainWindowViewModel);
    }

    private void AttachViewModel(MainWindowViewModel? oldViewModel, MainWindowViewModel? newViewModel)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribedViewModel = null;
        AttachDocument(null);

        if (newViewModel is null) return;
        _subscribedViewModel = newViewModel;
        newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        AttachDocument(newViewModel.SelectedDocument);
        InvalidateSoon();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null && e.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
        {
            AttachDocument(_subscribedViewModel.SelectedDocument);
        }

        InvalidateSoon();
    }

    private void AttachDocument(LevelDocument? document)
    {
        if (_subscribedDocument is not null)
        {
            _subscribedDocument.PropertyChanged -= OnDocumentPropertyChanged;
            _subscribedDocument.Nodes.CollectionChanged -= OnDocumentNodesChanged;
        }

        foreach (var node in _subscribedNodes.ToList())
        {
            UnsubscribeNode(node);
        }
        _subscribedNodes.Clear();
        _subscribedDocument = document;

        if (document is null) return;
        document.PropertyChanged += OnDocumentPropertyChanged;
        document.Nodes.CollectionChanged += OnDocumentNodesChanged;
        foreach (var node in document.Nodes)
        {
            SubscribeNodeTree(node);
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateSoon();

    private void OnDocumentNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscribedDocument is not null)
        {
            AttachDocument(_subscribedDocument);
        }
        InvalidateSoon();
    }

    private void SubscribeNodeTree(LevelNode node)
    {
        if (!_subscribedNodes.Add(node)) return;
        node.PropertyChanged += OnNodePropertyChanged;
        node.Children.CollectionChanged += OnNodeChildrenChanged;
        foreach (var child in node.Children)
        {
            SubscribeNodeTree(child);
        }
    }

    private void UnsubscribeNode(LevelNode node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        node.Children.CollectionChanged -= OnNodeChildrenChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateSoon();

    private void OnNodeChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscribedDocument is not null)
        {
            AttachDocument(_subscribedDocument);
        }
        InvalidateSoon();
    }

    private void InvalidateSoon()
    {
        if (Dispatcher.CheckAccess())
        {
            InvalidateVisual();
            return;
        }

        Dispatcher.BeginInvoke(new Action(InvalidateVisual), DispatcherPriority.Render);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(ThemeBrush("CanvasBrush", Brushes.White), null, new Rect(RenderSize));
        DrawGrid(dc);

        var vm = ViewModel;
        if (vm is null) return;

        foreach (var node in vm.SelectedDocument.SceneNodes.Where(n => !n.IsHidden).OrderBy(DrawOrder))
        {
            DrawNode(dc, vm, node);
        }

        if (_isMarqueeSelecting)
        {
            DrawMarquee(dc);
        }

        DrawGameTrickPreview(dc, vm);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        var vm = ViewModel;
        if (vm is null) return;

        var screen = e.GetPosition(this);
        var placeTrapezoidType2 = Keyboard.IsKeyDown(Key.Y);
        _lastDragScreen = screen;
        _dragStartScreen = screen;
        _dragStartWorld = ScreenToWorld(screen);
        _snapOffsetWorld = new Vector();
        _moveRemainder = new Vector();
        _resizeRemainder = new Vector();
        var world = ScreenToWorld(screen);
        _activeResizeHandle = ResizeHandle.None;
        _dragNode = null;

        if (e.ChangedButton == MouseButton.Left && IsTrickPreviewCloseHit(screen, vm))
        {
            vm.ClearGameTrickPreview();
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && IsInsideTrickPreview(screen, vm))
        {
            _isDraggingTrickPreview = true;
            _isPanning = false;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && vm.SelectedTool == EditorTool.Cursor && IsShiftDown())
        {
            _isMarqueeSelecting = true;
            _marqueeStartScreen = screen;
            _marqueeCurrentScreen = screen;
            _isPanning = false;
        }
        else if (e.ChangedButton == MouseButton.Middle || Keyboard.IsKeyDown(Key.Space))
        {
            _isPanning = true;
        }
        else if (vm.SelectedDocument.SelectedNode is { } selectedNode &&
                 (_activeResizeHandle = HitTestHandle(screen, selectedNode)) != ResizeHandle.None)
        {
            _dragNode = selectedNode;
            _isPanning = false;
        }
        else if (HitTestNode(world) is { } hitNode)
        {
            _dragNode = vm.ResolveCanvasDragTarget(hitNode);
            if (!vm.SelectedDocument.SelectedNodeIds.Contains(_dragNode.Id))
            {
                vm.SelectNode(_dragNode);
            }
            _activeResizeHandle = HitTestHandle(screen, _dragNode);
            _isPanning = false;
        }
        else if (vm.SelectedTool == EditorTool.Cursor)
        {
            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                vm.SelectNode(null);
            }
            _isPanning = false;
        }
        else
        {
            vm.PlaceAt(world, placeTrapezoidType2);
            _dragNode = vm.SelectedDocument.SelectedNode;
            _isPanning = false;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || _lastDragScreen is null) return;
        if (!_isPanning && e.LeftButton != MouseButtonState.Pressed) return;
        if (_isPanning && e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(this);
        var delta = current - _lastDragScreen.Value;
        _lastDragScreen = current;

        if (_isDraggingTrickPreview)
        {
            vm.MoveGameTrickPreview(new Vector(delta.X / vm.Zoom, delta.Y / vm.Zoom));
        }
        else if (_isPanning)
        {
            _pan += delta;
        }
        else if (_isMarqueeSelecting)
        {
            _marqueeCurrentScreen = current;
        }
        else if (_dragNode is not null && _activeResizeHandle != ResizeHandle.None)
        {
            if (_activeResizeHandle == ResizeHandle.Rotation)
            {
                var world = ScreenToWorld(current);
                var angle = Math.Atan2(world.Y - _rotationCenterWorld.Y, world.X - _rotationCenterWorld.X) * 180.0 / Math.PI + 90.0;
                vm.RotateSelectedTo(angle);
            }
            else
            {
                var worldDelta = new Vector(delta.X / vm.Zoom, delta.Y / vm.Zoom);
                var (left, top, right, bottom) = ResizeDelta(_activeResizeHandle, worldDelta);
                _resizeRemainder += new Vector(left + right, top + bottom);
                var wholeX = Math.Truncate(_resizeRemainder.X);
                var wholeY = Math.Truncate(_resizeRemainder.Y);
                if (wholeX != 0 || wholeY != 0)
                {
                    _resizeRemainder -= new Vector(wholeX, wholeY);
                    var (resizeLeft, resizeTop, resizeRight, resizeBottom) = ResizeDelta(_activeResizeHandle, new Vector(wholeX, wholeY));
                    vm.ResizeSelected(resizeLeft, resizeTop, resizeRight, resizeBottom);
                }
            }
        }
        else if (_dragNode is not null)
        {
            var worldDelta = new Vector(delta.X / vm.Zoom, delta.Y / vm.Zoom);
            if (Keyboard.IsKeyDown(Key.T))
            {
                worldDelta.Y = 0;
            }
            if (Keyboard.IsKeyDown(Key.G))
            {
                worldDelta.X = 0;
            }
            var proposedDelta = worldDelta - _snapOffsetWorld;
            if (Keyboard.IsKeyDown(Key.U))
            {
                worldDelta = SnapWorldCenter(_dragNode, proposedDelta, out _snapOffsetWorld);
            }
            else if (Keyboard.IsKeyDown(Key.V))
            {
                worldDelta = SnapWorldPosition(_dragNode, proposedDelta, out _snapOffsetWorld);
            }
            else
            {
                worldDelta = proposedDelta;
                _snapOffsetWorld = new Vector();
            }
            _moveRemainder += worldDelta;
            var moveX = Math.Truncate(_moveRemainder.X);
            var moveY = Math.Truncate(_moveRemainder.Y);
            if (moveX != 0 || moveY != 0)
            {
                _moveRemainder -= new Vector(moveX, moveY);
                vm.MoveSelected(moveX, moveY);
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isMarqueeSelecting && ViewModel is { } vm)
        {
            _marqueeCurrentScreen = e.GetPosition(this);
            if (IsTinyDrag(_marqueeStartScreen, _marqueeCurrentScreen))
            {
                if (HitTestNode(ScreenToWorld(_marqueeCurrentScreen)) is { } hitNode)
                {
                    vm.ToggleNodeSelection(hitNode);
                }
            }
            else
            {
                vm.SelectNodesInRect(ScreenRectToWorld(NormalizedRect(_marqueeStartScreen, _marqueeCurrentScreen)), additive: true);
            }
        }

        _lastDragScreen = null;
        _dragNode = null;
        _activeResizeHandle = ResizeHandle.None;
        _snapOffsetWorld = new Vector();
        _moveRemainder = new Vector();
        _resizeRemainder = new Vector();
        _isPanning = false;
        _isMarqueeSelecting = false;
        _isDraggingTrickPreview = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        if (Keyboard.IsKeyDown(Key.Q))
        {
            vm.SelectedTool = EditorToolInfo.Cycle(vm.SelectedTool, e.Delta > 0 ? -1 : 1);
            e.Handled = true;
            return;
        }

        var before = ScreenToWorld(e.GetPosition(this));
        vm.Zoom *= e.Delta > 0 ? 1.1 : 0.9;
        var afterScreen = WorldToScreen(before);
        _pan += e.GetPosition(this) - afterScreen;
        _lastDragScreen = e.GetPosition(this);
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawGrid(DrawingContext dc)
    {
        var zoom = ViewModel?.Zoom ?? 1;
        var step = Math.Max(8, 50 * zoom);
        var pen = new Pen(ThemeBrush("GridLineBrush", new SolidColorBrush(Color.FromRgb(217, 222, 229))), 1);

        for (var x = _pan.X % step; x < ActualWidth; x += step)
        {
            dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (var y = _pan.Y % step; y < ActualHeight; y += step)
        {
            dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawMarquee(DrawingContext dc)
    {
        var rect = NormalizedRect(_marqueeStartScreen, _marqueeCurrentScreen);
        var fill = new SolidColorBrush(Color.FromArgb(45, 0, 180, 255));
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 220)), 1);
        dc.DrawRectangle(fill, stroke, rect);
    }

    private void DrawGameTrickPreview(DrawingContext dc, MainWindowViewModel vm)
    {
        var playback = vm.GameTrickPlayback;
        if (playback is null || playback.Frames.Count == 0 || !TryGetTrickPreviewAnchorScreen(vm, out var anchor)) return;
        var sample = playback.Sample(vm.GameTrickTick);
        if (sample.Pose.Count == 0) return;
        var points = playback.Model.ResolvePoints(sample.Pose);
        var initialPoints = playback.Model.ResolvePoints(playback.Sample(0).Pose);
        var originPivot = initialPoints.GetValueOrDefault(playback.Move.PivotNode, initialPoints.Values.FirstOrDefault());
        // Vector 2 animation coordinates are already room/world units.
        const double unitsPerCanvasPoint = 1.0;
        Point CanvasPoint(Point point) => new(
            anchor.X + ((point.X - originPivot.X) / unitsPerCanvasPoint) * vm.Zoom,
            anchor.Y + ((point.Y - originPivot.Y) / unitsPerCanvasPoint) * vm.Zoom);

        foreach (var triangle in playback.Model.Triangles)
        {
            var trianglePoints = triangle.NodeNames.Where(points.ContainsKey).Select(name => CanvasPoint(points[name])).ToList();
            if (trianglePoints.Count != 3) continue;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(trianglePoints[0], true, true);
                context.PolyLineTo(trianglePoints.Skip(1).ToList(), true, false);
            }
            geometry.Freeze();
            var outline = vm.GameTrickShowModelSphereOutlines
                ? new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), Math.Max(.5, .8 * vm.Zoom))
                : null;
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(240, 0, 0, 0)), outline, geometry);
        }

        var edgeByName = playback.Model.Edges
            .GroupBy(edge => edge.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (playback.Model.Capsules.Count > 0)
        {
            foreach (var capsule in playback.Model.Capsules)
            {
                if (!edgeByName.TryGetValue(capsule.EdgeName, out var edge) ||
                    !points.TryGetValue(edge.StartName, out var start) || !points.TryGetValue(edge.EndName, out var end)) continue;
                var dx = start.X - end.X;
                var dy = start.Y - end.Y;
                var a = new Point(start.X - dx * capsule.Margin1, start.Y - dy * capsule.Margin1);
                var b = new Point(end.X + dx * capsule.Margin2, end.Y + dy * capsule.Margin2);
                var width = Math.Max(1.5, capsule.Radius * 2 / unitsPerCanvasPoint * vm.Zoom);
                var outline = new Pen(new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)), width + Math.Max(1, 2 * vm.Zoom)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var body = new Pen(new SolidColorBrush(Color.FromArgb(245, 0, 0, 0)), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (vm.GameTrickShowModelSphereOutlines) dc.DrawLine(outline, CanvasPoint(a), CanvasPoint(b));
                dc.DrawLine(body, CanvasPoint(a), CanvasPoint(b));
            }
        }
        else
        {
            var skeleton = new Pen(new SolidColorBrush(Color.FromArgb(235, 0, 220, 235)), Math.Max(1.2, 2 * vm.Zoom)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            foreach (var edge in playback.Model.Edges)
            {
                if (points.TryGetValue(edge.StartName, out var start) && points.TryGetValue(edge.EndName, out var end)) dc.DrawLine(skeleton, CanvasPoint(start), CanvasPoint(end));
            }
        }

        if (vm.GameTrickShowModelNodeSpheres)
        {
            foreach (var nodePoint in playback.Model.NodePoints)
            {
                if (!points.TryGetValue(nodePoint.NodeName, out var point)) continue;
                var radius = Math.Max(1.5, nodePoint.Radius / unitsPerCanvasPoint * vm.Zoom);
                var outline = vm.GameTrickShowModelSphereOutlines
                    ? new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), .7)
                    : null;
                dc.DrawEllipse(Brushes.Black, outline, CanvasPoint(point), radius, radius);
            }
        }

        for (var index = 0; index < Math.Min(46, sample.Pose.Count); index++)
        {
            var isPivot = playback.Model.Nodes.ElementAtOrDefault(index)?.Name.Equals(playback.Move.PivotNode, StringComparison.OrdinalIgnoreCase) == true;
            if ((isPivot && !vm.GameTrickShowDetectorDots) || (!isPivot && !vm.GameTrickShowSkeletonPoints)) continue;
            dc.DrawEllipse(isPivot ? Brushes.Orange : new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)), null, CanvasPoint(sample.Pose[index]), 2.4, 2.4);
        }

        var label = new FormattedText(
            $"{playback.Move.Name}  frame {sample.FrameIndex} +{sample.Subframe}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var labelRect = new Rect(anchor.X + 12, anchor.Y - 58, label.Width + 16, label.Height + 10);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null, labelRect, labelRect.Height / 2, labelRect.Height / 2);
        dc.DrawText(label, new Point(labelRect.X + 8, labelRect.Y + 5));

        var close = TrickPreviewClosePoint(anchor);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), new Pen(Brushes.White, 1), close, 11, 11);
        dc.DrawLine(new Pen(Brushes.White, 1.5), new Point(close.X - 4, close.Y - 4), new Point(close.X + 4, close.Y + 4));
        dc.DrawLine(new Pen(Brushes.White, 1.5), new Point(close.X + 4, close.Y - 4), new Point(close.X - 4, close.Y + 4));
    }

    private bool TryGetTrickPreviewAnchorScreen(MainWindowViewModel vm, out Point anchor)
    {
        anchor = default;
        var playback = vm.GameTrickPlayback;
        if (playback is null) return false;
        var node = playback.AnchorNodeId is Guid id
            ? vm.SelectedDocument.SceneNodes.FirstOrDefault(candidate => candidate.Id == id)
            : vm.SelectedDocument.SelectedNode;
        node ??= vm.SelectedDocument.SelectedNode;
        if (node is null) return false;
        var bounds = node.VisualBounds;
        var previewBaseYOffset = Math.Max(72, Math.Min(180, bounds.Height * .55 + 40));
        var worldAnchor = new Point(
            bounds.Left + bounds.Width / 2 + node.VisualOffsetX + playback.PlacementOffset.X,
            bounds.Top + bounds.Height / 2 + previewBaseYOffset + node.VisualOffsetY + playback.PlacementOffset.Y);
        anchor = WorldToScreen(worldAnchor);
        return true;
    }

    private bool IsInsideTrickPreview(Point screen, MainWindowViewModel vm)
        => TryGetTrickPreviewAnchorScreen(vm, out var anchor) && new Rect(anchor.X - 82, anchor.Y - 102, 164, 204).Contains(screen);

    private bool IsTrickPreviewCloseHit(Point screen, MainWindowViewModel vm)
        => TryGetTrickPreviewAnchorScreen(vm, out var anchor) && (screen - TrickPreviewClosePoint(anchor)).Length <= 16;

    private static Point TrickPreviewClosePoint(Point anchor) => new(anchor.X + 96, anchor.Y - 86);

    private void DrawNode(DrawingContext dc, MainWindowViewModel vm, LevelNode node, bool drawSelection = true)
    {
        var rect = WorldRectToScreen(node.Bounds);

        if (node.RendersAsRuntimeGraph)
        {
            var shapeWrapperWins = ShapeWrapperWins(node);
            if (!shapeWrapperWins && !IsPhantomOwner(node) && !string.IsNullOrWhiteSpace(node.ImagePath) && TryLoadImage(node.ImagePath, out var ownerPreview))
            {
                dc.DrawImage(ownerPreview, WorldRectToScreen(new Rect(node.X + node.VisualOffsetX, node.Y + node.VisualOffsetY, node.Width, node.Height)));
            }

            foreach (var child in RuntimeOverlayChildren(node).OrderBy(DrawOrder))
            {
                DrawNode(dc, vm, child);
            }

            if (drawSelection && vm.SelectedDocument.SelectedNodeIds.Contains(node.Id))
            {
                var visualRect = WorldRectToScreen(shapeWrapperWins ? RuntimeShapeBounds(node) : Vector2LibraryObjectBuilder.OwnerPreviewBounds(node));
                DrawSelection(dc, visualRect, node.Rotation);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.ImagePath) && node.HasAffineBasis && TryLoadImage(node.ImagePath, out var affineImage))
        {
            DrawAffineImage(dc, affineImage, node);
            if (!node.IsPreviewOnly)
            {
                var affineBounds = WorldRectToScreen(node.AffineBounds);
                dc.DrawRectangle(null, node.StrokePen, affineBounds);
                DrawNodeLabel(dc, node.Name, affineBounds.TopLeft);
            }
        }
        else
        {
        // WPF transforms are screen-space here, but the node data is editor
        // space. We rotate around the rectangle center for normal nodes; grouped
        // library previews get their selection/rotation center from VisualBounds.
        dc.PushTransform(new RotateTransform(node.Rotation, rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0));
        try
        {
            if (ShouldForceWallJumpRedBox(node))
            {
                DrawWallJumpRedBox(dc, rect);
            }
            else if (!string.IsNullOrWhiteSpace(node.ImagePath) && TryLoadImage(node.ImagePath, out var image))
            {
                var imageRect = node.Kind == LevelNodeKind.Image || node.IsPreviewOnly
                    ? rect
                    : ImageDrawRect(image, rect);
                if (IsStuntIconNode(node))
                {
                    dc.DrawRoundedRectangle(
                        new SolidColorBrush(Color.FromRgb(35, 45, 62)),
                        new Pen(new SolidColorBrush(Color.FromRgb(77, 94, 120)), 1),
                        imageRect,
                        5,
                        5);
                }
                dc.DrawImage(image, imageRect);
                if (!node.IsPreviewOnly)
                {
                    dc.DrawRectangle(null, node.StrokePen, rect);
                }
            }
            else if (node.Kind == LevelNodeKind.Trapezoid)
            {
                // Type 1 and Type 2 export the same BuildMap shape with a Type
                // flag. The game rebuilds the shortened side internally, so this
                // preview is just the editor-friendly visual, not extra XML data.
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    var type2 = node.Variant.Contains("type2", StringComparison.OrdinalIgnoreCase) ||
                                node.Name.Contains("type2", StringComparison.OrdinalIgnoreCase) ||
                                node.Variant == "2";
                    if (type2)
                    {
                        ctx.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                        ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                        ctx.LineTo(new Point(rect.Left, rect.Bottom), true, false);
                    }
                    else
                    {
                        ctx.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
                        ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                        ctx.LineTo(new Point(rect.Right, rect.Top), true, false);
                    }
                }
                dc.DrawGeometry(node.FillBrush, node.StrokePen, geometry);
            }
            else
            {
                dc.DrawRectangle(node.FillBrush, node.StrokePen, rect);
            }

            if (!string.IsNullOrWhiteSpace(node.Name) && ShouldShowNodeLabel(node))
            {
                DrawNodeLabel(dc, node.Name, rect.TopLeft);
            }
        }
        finally
        {
            dc.Pop();
        }
        }

        if (drawSelection && vm.SelectedDocument.SelectedNodeIds.Contains(node.Id))
        {
            // Selection uses VisualBounds so parented library objects get a box
            // around what you see, not around the tiny XML owner anchor.
            var visualRect = WorldRectToScreen(node.VisualBounds);
            DrawSelection(dc, visualRect, node.Rotation);
        }
    }

    private void DrawSelection(DrawingContext dc, Rect rect, double rotation)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        dc.PushTransform(new RotateTransform(rotation, center.X, center.Y));
        try
        {
            dc.DrawRectangle(null, new Pen(Brushes.Lime, 2), rect);
        }
        finally
        {
            dc.Pop();
        }

        DrawHandles(dc, rect, rotation);
    }

    private void DrawHandles(DrawingContext dc, Rect rect, double rotation)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        foreach (var p in new[]
        {
            rect.TopLeft, new Point(rect.Left + rect.Width / 2, rect.Top), rect.TopRight,
            new Point(rect.Left, rect.Top + rect.Height / 2), new Point(rect.Right, rect.Top + rect.Height / 2),
            rect.BottomLeft, new Point(rect.Left + rect.Width / 2, rect.Bottom), rect.BottomRight
        }.Select(p => RotatePoint(p, center, rotation)))
        {
            var half = ResizeHandleVisualSize / 2.0;
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), new Rect(p.X - half, p.Y - half, ResizeHandleVisualSize, ResizeHandleVisualSize));
        }
        var rotationHandle = RotatePoint(new Point(center.X, rect.Top - RotationHandleDistance), center, rotation);
        dc.DrawLine(new Pen(Brushes.LimeGreen, 1), RotatePoint(new Point(center.X, rect.Top), center, rotation), rotationHandle);
        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Gray, 1), rotationHandle, RotationHandleRadius, RotationHandleRadius);
    }

    private void DrawNodeLabel(DrawingContext dc, string textValue, Point topLeft)
    {
        if (string.IsNullOrWhiteSpace(textValue)) return;
        var text = new FormattedText(textValue, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var origin = new Point(topLeft.X + 4, topLeft.Y + 4);
        var labelRect = new Rect(origin.X - 2, origin.Y - 1, text.Width + 4, text.Height + 2);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), null, labelRect);
        dc.DrawText(text, origin);
    }

    private void DrawAffineImage(DrawingContext dc, BitmapSource image, LevelNode node)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0) return;
        var anchor = WorldToScreen(new Point(node.X, node.Y));
        var zoom = ViewModel?.Zoom ?? 1;
        var matrix = new Matrix(
            node.BasisXX * zoom / image.PixelWidth,
            node.BasisXY * zoom / image.PixelWidth,
            node.BasisYX * zoom / image.PixelHeight,
            node.BasisYY * zoom / image.PixelHeight,
            anchor.X,
            anchor.Y);
        dc.PushTransform(new MatrixTransform(matrix));
        try
        {
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }
        finally
        {
            dc.Pop();
        }
    }

    private static void DrawWallJumpRedBox(DrawingContext dc, Rect rect)
    {
        var fill = new SolidColorBrush(Color.FromArgb(95, 255, 90, 105));
        var stroke = new Pen(Brushes.IndianRed, 1);
        dc.DrawRectangle(fill, stroke, rect);
    }

    private static bool ShouldDrawRuntimeOverlay(LevelNode owner, LevelNode node)
        => !node.IsHidden && (node.IsRuntimeLogicalChild || (IsPhantomOwner(owner) && IsPhantomVisualizer(node)));

    private static bool ShouldShowNodeLabel(LevelNode node)
        => !node.IsPreviewOnly || node.IsRuntimeLogicalChild || IsPhantomVisualizer(node);

    private static IEnumerable<LevelNode> RuntimeOverlayChildren(LevelNode node)
        => node.Children
            .SelectMany(child => child.Flatten())
            .Where(child => ShouldDrawRuntimeOverlay(node, child));

    private static bool ShapeWrapperWins(LevelNode node)
        => IsWallJumpLike(node) && RuntimeShapeChildren(node).Any();

    private static IEnumerable<LevelNode> RuntimeShapeChildren(LevelNode node)
        => node.Children
            .SelectMany(child => child.Flatten())
            .Where(child => !child.IsHidden && child.Kind is LevelNodeKind.Trigger or LevelNodeKind.Area or LevelNodeKind.Platform or LevelNodeKind.Trapezoid);

    private static Rect RuntimeShapeBounds(LevelNode node)
    {
        var shapes = RuntimeShapeChildren(node).ToList();
        if (shapes.Count == 0) return node.VisualBounds;

        var bounds = shapes[0].VisualBounds;
        foreach (var shape in shapes.Skip(1))
        {
            bounds.Union(shape.VisualBounds);
        }
        return bounds;
    }

    private static bool IsWallJumpLike(LevelNode node)
    {
        var combined = string.Join(' ', node.Name, node.ClassName, node.Template, node.Choice, node.Filename, node.Variant,
            string.Join(' ', node.Children.SelectMany(child => child.Flatten()).Select(child => child.Name)));
        var normalized = new string(combined.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Contains("walljump", StringComparison.Ordinal) ||
               normalized.Contains("wallrun", StringComparison.Ordinal);
    }

    private static bool ShouldForceWallJumpRedBox(LevelNode node)
        => IsWallJumpLike(node) &&
           node.Kind is LevelNodeKind.ObjectReference or LevelNodeKind.Object or LevelNodeKind.Trigger or LevelNodeKind.Area &&
           !string.IsNullOrWhiteSpace(node.ImagePath);

    private static bool IsPhantomOwner(LevelNode node)
        => node.Filename.Equals("phantoms.xml", StringComparison.OrdinalIgnoreCase) ||
           node.Choice.Equals("phantoms.xml", StringComparison.OrdinalIgnoreCase) ||
           node.Template.Contains("Phantom", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhantomVisualizer(LevelNode node)
        => node.Template.Equals("PhantomStuntIcon", StringComparison.OrdinalIgnoreCase) ||
           node.Template.Equals("PhantomArea", StringComparison.OrdinalIgnoreCase);

    private static bool IsStuntIconNode(LevelNode node)
        => node.Template.Equals("PhantomStuntIcon", StringComparison.OrdinalIgnoreCase) ||
           node.ClassName.Contains("track_trick_", StringComparison.OrdinalIgnoreCase) ||
           node.ImagePath.Contains("track_trick_", StringComparison.OrdinalIgnoreCase);

    private static Brush ThemeBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private ResizeHandle HitTestHandle(Point screen, LevelNode node)
    {
        if (ViewModel?.SelectedDocument.SelectedNodeIds.Contains(node.Id) != true) return ResizeHandle.None;
        var rect = WorldRectToScreen(node.VisualBounds);
        var handles = HandleRects(rect, node.Rotation);
        foreach (var pair in handles)
        {
            if (pair.Value.Contains(screen))
            {
                if (pair.Key == ResizeHandle.Rotation)
                {
                    var visualBounds = node.VisualBounds;
                    _rotationCenterWorld = new Point(
                        visualBounds.Left + visualBounds.Width / 2.0,
                        visualBounds.Top + visualBounds.Height / 2.0);
                }
                return pair.Key;
            }
        }

        return HitTestResizeEdge(screen, rect, node.Rotation);
    }

    private static Dictionary<ResizeHandle, Rect> HandleRects(Rect rect, double rotation)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        return new()
        {
            [ResizeHandle.TopLeft] = HandleRect(RotatePoint(rect.TopLeft, center, rotation)),
            [ResizeHandle.Top] = HandleRect(RotatePoint(new Point(rect.Left + rect.Width / 2, rect.Top), center, rotation)),
            [ResizeHandle.TopRight] = HandleRect(RotatePoint(rect.TopRight, center, rotation)),
            [ResizeHandle.Left] = HandleRect(RotatePoint(new Point(rect.Left, rect.Top + rect.Height / 2), center, rotation)),
            [ResizeHandle.Right] = HandleRect(RotatePoint(new Point(rect.Right, rect.Top + rect.Height / 2), center, rotation)),
            [ResizeHandle.BottomLeft] = HandleRect(RotatePoint(rect.BottomLeft, center, rotation)),
            [ResizeHandle.Bottom] = HandleRect(RotatePoint(new Point(rect.Left + rect.Width / 2, rect.Bottom), center, rotation)),
            [ResizeHandle.BottomRight] = HandleRect(RotatePoint(rect.BottomRight, center, rotation)),
            [ResizeHandle.Rotation] = HandleRect(RotatePoint(new Point(center.X, rect.Top - RotationHandleDistance), center, rotation))
        };
    }

    private static Rect HandleRect(Point p)
    {
        var half = ResizeHandleHitSize / 2.0;
        return new(p.X - half, p.Y - half, ResizeHandleHitSize, ResizeHandleHitSize);
    }

    private static ResizeHandle HitTestResizeEdge(Point screen, Rect rect, double rotation)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        var point = RotatePoint(screen, center, -rotation);
        var inflated = rect;
        inflated.Inflate(ResizeEdgeHitThickness, ResizeEdgeHitThickness);
        if (!inflated.Contains(point)) return ResizeHandle.None;

        var nearLeft = Math.Abs(point.X - rect.Left) <= ResizeEdgeHitThickness;
        var nearRight = Math.Abs(point.X - rect.Right) <= ResizeEdgeHitThickness;
        var nearTop = Math.Abs(point.Y - rect.Top) <= ResizeEdgeHitThickness;
        var nearBottom = Math.Abs(point.Y - rect.Bottom) <= ResizeEdgeHitThickness;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, false, true, false) => ResizeHandle.TopLeft,
            (false, true, true, false) => ResizeHandle.TopRight,
            (true, false, false, true) => ResizeHandle.BottomLeft,
            (false, true, false, true) => ResizeHandle.BottomRight,
            (true, false, false, false) => ResizeHandle.Left,
            (false, true, false, false) => ResizeHandle.Right,
            (false, false, true, false) => ResizeHandle.Top,
            (false, false, false, true) => ResizeHandle.Bottom,
            _ => ResizeHandle.None
        };
    }

    private static (double left, double top, double right, double bottom) ResizeDelta(ResizeHandle handle, Vector delta) => handle switch
    {
        ResizeHandle.TopLeft => (delta.X, delta.Y, 0, 0),
        ResizeHandle.Top => (0, delta.Y, 0, 0),
        ResizeHandle.TopRight => (0, delta.Y, delta.X, 0),
        ResizeHandle.Left => (delta.X, 0, 0, 0),
        ResizeHandle.Right => (0, 0, delta.X, 0),
        ResizeHandle.BottomLeft => (delta.X, 0, 0, delta.Y),
        ResizeHandle.Bottom => (0, 0, 0, delta.Y),
        ResizeHandle.BottomRight => (0, 0, delta.X, delta.Y),
        _ => (0, 0, 0, 0)
    };

    private static Point RotatePoint(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Point(center.X + (dx * cos - dy * sin), center.Y + (dx * sin + dy * cos));
    }

    private Vector SnapWorldPosition(LevelNode moving, Vector delta, out Vector snapOffset)
    {
        return SnapWorldToNearbyGeometry(moving, delta, out snapOffset, includeCorners: true);
    }

    private Vector SnapWorldCenter(LevelNode moving, Vector delta, out Vector snapOffset)
    {
        return SnapWorldToNearbyGeometry(moving, delta, out snapOffset, includeCorners: false);
    }

    private Vector SnapWorldToNearbyGeometry(LevelNode moving, Vector delta, out Vector snapOffset, bool includeCorners)
    {
        // Snap to complete nearby points, or to an edge/center line only when
        // the objects overlap on the other axis. The overlap gate preserves
        // free movement in open space while allowing contact anywhere along a
        // long platform or the middle of an object's side.
        snapOffset = new Vector();
        var vm = ViewModel;
        if (vm is null) return delta;

        var moved = moving.VisualBounds;
        moved.Offset(delta);
        var threshold = SnapThresholdWorld(includeCorners ? 18 : 22);
        var ignoredIds = SnapIgnoredIds(vm, moving);
        var movingPoints = SnapPoints(moved, moving.Rotation, includeCorners, includeCenter: true);

        Vector? bestPointCorrection = null;
        var bestPointDistance = threshold;
        double? bestXCorrection = null;
        double? bestYCorrection = null;
        var bestXDistance = threshold;
        var bestYDistance = threshold;

        foreach (var other in SnapTargets(vm, ignoredIds))
        {
            var otherBounds = other.VisualBounds;
            var targetPoints = SnapPoints(otherBounds, other.Rotation, includeCorners, includeCenter: true);
            foreach (var movingPoint in movingPoints)
            foreach (var targetPoint in targetPoints)
            {
                var correction = targetPoint - movingPoint;
                var distance = correction.Length;
                if (distance <= bestPointDistance)
                {
                    bestPointDistance = distance;
                    bestPointCorrection = correction;
                }
            }

            if (SpansOverlap(moved.Top, moved.Bottom, otherBounds.Top, otherBounds.Bottom))
            {
                FindBestAxisCorrection(
                    new[] { moved.Left, moved.Left + moved.Width / 2.0, moved.Right },
                    new[] { otherBounds.Left, otherBounds.Left + otherBounds.Width / 2.0, otherBounds.Right },
                    threshold,
                    ref bestXCorrection,
                    ref bestXDistance);
            }

            if (SpansOverlap(moved.Left, moved.Right, otherBounds.Left, otherBounds.Right))
            {
                FindBestAxisCorrection(
                    new[] { moved.Top, moved.Top + moved.Height / 2.0, moved.Bottom },
                    new[] { otherBounds.Top, otherBounds.Top + otherBounds.Height / 2.0, otherBounds.Bottom },
                    threshold,
                    ref bestYCorrection,
                    ref bestYDistance);
            }
        }

        if (bestPointCorrection is { } pointCorrection)
        {
            snapOffset = pointCorrection;
        }
        else
        {
            snapOffset = new Vector(bestXCorrection ?? 0, bestYCorrection ?? 0);
        }

        return delta + snapOffset;
    }

    private static bool SpansOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
        => firstEnd >= secondStart && secondEnd >= firstStart;

    private static void FindBestAxisCorrection(
        IReadOnlyList<double> movingAnchors,
        IReadOnlyList<double> targetAnchors,
        double threshold,
        ref double? bestCorrection,
        ref double bestDistance)
    {
        foreach (var movingAnchor in movingAnchors)
        foreach (var targetAnchor in targetAnchors)
        {
            var correction = targetAnchor - movingAnchor;
            var distance = Math.Abs(correction);
            if (distance <= bestDistance && distance <= threshold)
            {
                bestDistance = distance;
                bestCorrection = correction;
            }
        }
    }

    private double SnapThresholdWorld(double screenPixels)
    {
        var zoom = Math.Max(0.01, ViewModel?.Zoom ?? 1);
        return screenPixels / zoom;
    }

    private static Point[] SnapPoints(Rect bounds, double rotation, bool includeCorners, bool includeCenter = false)
    {
        var center = new Point(bounds.Left + bounds.Width / 2.0, bounds.Top + bounds.Height / 2.0);
        var points = new List<Point>();
        if (includeCorners)
        {
            points.Add(bounds.TopLeft);
            points.Add(bounds.TopRight);
            points.Add(bounds.BottomRight);
            points.Add(bounds.BottomLeft);
        }

        points.Add(new Point(center.X, bounds.Top));
        points.Add(new Point(bounds.Right, center.Y));
        points.Add(new Point(center.X, bounds.Bottom));
        points.Add(new Point(bounds.Left, center.Y));
        if (includeCenter) points.Add(center);

        if (Math.Abs(rotation) < 0.001) return points.ToArray();
        return points.Select(point => RotatePoint(point, center, rotation)).ToArray();
    }

    private static HashSet<Guid> SnapIgnoredIds(MainWindowViewModel vm, LevelNode moving)
    {
        var selectedIds = vm.SelectedDocument.SelectedNodeIds.ToHashSet();
        selectedIds.Add(moving.Id);
        var ignoredIds = new HashSet<Guid>();
        foreach (var root in vm.SelectedDocument.Nodes)
        {
            CollectSnapIgnoredIds(root, selectedIds, selectedAncestor: false, ignoredIds);
        }
        return ignoredIds;
    }

    private static bool CollectSnapIgnoredIds(LevelNode node, HashSet<Guid> selectedIds, bool selectedAncestor, HashSet<Guid> ignoredIds)
    {
        var selfSelected = selectedIds.Contains(node.Id);
        var hasSelectedDescendant = false;
        foreach (var child in node.Children)
        {
            if (CollectSnapIgnoredIds(child, selectedIds, selectedAncestor || selfSelected, ignoredIds))
            {
                hasSelectedDescendant = true;
            }
        }

        if (selectedAncestor || selfSelected || hasSelectedDescendant)
        {
            ignoredIds.Add(node.Id);
        }
        return selfSelected || hasSelectedDescendant;
    }

    private static IEnumerable<LevelNode> SnapTargets(MainWindowViewModel vm, HashSet<Guid> ignoredIds)
        => vm.SelectedDocument.SceneNodes
            .Where(node => !node.IsHidden && !ignoredIds.Contains(node.Id) && node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor));

    private static Rect ImageDrawRect(BitmapSource image, Rect bounds)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return bounds;
        }

        var imageAspect = (double)image.PixelWidth / image.PixelHeight;
        var boundsAspect = bounds.Width / bounds.Height;
        if (Math.Abs(imageAspect - boundsAspect) < 0.01)
        {
            return bounds;
        }

        if (imageAspect > boundsAspect)
        {
            var height = bounds.Width / imageAspect;
            return new Rect(bounds.Left, bounds.Top + (bounds.Height - height) / 2.0, bounds.Width, height);
        }

        var width = bounds.Height * imageAspect;
        return new Rect(bounds.Left + (bounds.Width - width) / 2.0, bounds.Top, width, bounds.Height);
    }

    private static bool TryLoadImage(string path, out BitmapImage image)
    {
        image = new BitmapImage();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (ImageCache.TryGetValue(path, out var cached))
        {
            if (cached is null) return false;
            image = cached;
            return true;
        }

        try
        {
            var loaded = new BitmapImage();
            loaded.BeginInit();
            loaded.CacheOption = BitmapCacheOption.OnLoad;
            loaded.UriSource = new Uri(path);
            loaded.EndInit();
            loaded.Freeze();
            ImageCache[path] = loaded;
            image = loaded;
            return true;
        }
        catch
        {
            ImageCache[path] = null;
            DiagnosticsLog.Warn($"Failed to load texture preview: {path}");
            return false;
        }
    }

    private LevelNode? HitTestNode(Point world)
    {
        var vm = ViewModel;
        return vm?.SelectedDocument.SceneNodes
            .SelectMany(HitTestCandidates)
            .Where(candidate => candidate.Node.VisualBounds.Contains(world) || candidate.Node.Bounds.Contains(world))
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => DrawOrder(candidate.Node))
            .Select(candidate => candidate.Node)
            .LastOrDefault();
    }

    private static IEnumerable<(LevelNode Node, int Priority)> HitTestCandidates(LevelNode node)
    {
        yield return (node, DrawOrder(node));

        if (!node.RendersAsRuntimeGraph) yield break;

        foreach (var child in RuntimeOverlayChildren(node))
        {
            // A stunt icon is editor-only art for the owning phantom. Selecting
            // it directly moves the picture but leaves the exported phantom X/Y
            // behind, so clicks on that art must resolve to the owner instead.
            if (child.IsPreviewOnly && IsStuntIconNode(child)) continue;
            yield return (child, 100 + DrawOrder(child));
        }
    }

    private Rect WorldRectToScreen(Rect rect)
    {
        var zoom = ViewModel?.Zoom ?? 1;
        return new Rect(rect.X * zoom + _pan.X, rect.Y * zoom + _pan.Y, rect.Width * zoom, rect.Height * zoom);
    }

    private Point WorldToScreen(Point point)
    {
        var zoom = ViewModel?.Zoom ?? 1;
        return new Point(point.X * zoom + _pan.X, point.Y * zoom + _pan.Y);
    }

    private Point ScreenToWorld(Point point)
    {
        var zoom = ViewModel?.Zoom ?? 1;
        return new Point((point.X - _pan.X) / zoom, (point.Y - _pan.Y) / zoom);
    }

    private Rect ScreenRectToWorld(Rect rect)
    {
        var topLeft = ScreenToWorld(rect.TopLeft);
        var bottomRight = ScreenToWorld(rect.BottomRight);
        return NormalizedRect(topLeft, bottomRight);
    }

    private static Rect NormalizedRect(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static bool IsShiftDown()
        => Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

    private static bool IsTinyDrag(Point start, Point end)
        => Math.Abs(start.X - end.X) < 4 && Math.Abs(start.Y - end.Y) < 4;

    private static int DrawOrder(LevelNode node)
    {
        // Visual-only nudge to match the macOS editor: fans/ventilators and
        // wall-prop pieces preview near CAperture so they sit behind collision,
        // but not buried behind every image. Export keeps SortingLayer untouched.
        if (ShouldPreviewNearCAperture(node)) return 7;

        return node.Kind switch
        {
            LevelNodeKind.Image => 0,
            LevelNodeKind.Platform => 10,
            LevelNodeKind.Trapezoid => 11,
            LevelNodeKind.Trigger or LevelNodeKind.Area => 20,
            _ => 30
        };
    }

    private static bool ShouldPreviewNearCAperture(LevelNode node)
    {
        if (node.Kind != LevelNodeKind.Image && string.IsNullOrWhiteSpace(node.ImagePath)) return false;
        var combined = string.Join(' ', node.Name, node.ClassName, node.ImagePath, node.Filename, node.Choice, node.Variant)
            .Replace("__", ".", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal)
            .ToLowerInvariant();

        return combined.Contains("ventilator", StringComparison.Ordinal) ||
               combined.Contains(".v_fan", StringComparison.Ordinal) ||
               combined.Contains("v_fan", StringComparison.Ordinal) ||
               combined.Contains("_fan", StringComparison.Ordinal) ||
               combined.Contains(".fan", StringComparison.Ordinal) ||
               combined.Contains(" fan", StringComparison.Ordinal) ||
               combined.Contains("wall_props", StringComparison.Ordinal) ||
               combined.Contains("z2_wall_props", StringComparison.Ordinal) ||
               combined.Contains("walls.", StringComparison.Ordinal);
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Left,
        Right,
        BottomLeft,
        Bottom,
        BottomRight,
        Rotation
    }
}
