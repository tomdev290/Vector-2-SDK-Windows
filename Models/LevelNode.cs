using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace Vector2LevelEditor.Models;

/// <summary>
/// One editable thing in a Vector 2 room.
///
/// Everything talks through this model: importer, exporter, hierarchy,
/// inspector, canvas, and asset browser. Some nodes are literal XML rectangles
/// like Platform. Others are "owner" nodes with child preview pieces underneath
/// them, which is how library objects and prefab reconstructions stay editable
/// without lying about the XML we export.
/// </summary>
public sealed class LevelNode : NotifyObject
{
    private string _name = "Node";
    private LevelNodeKind _kind;
    private int _x;
    private int _y;
    private int _width = 72;
    private int _height = 72;
    private double _rotation;
    private string _factor = "1";
    private string _tag = "";
    private string _sortingLayer = "Default";
    private string _template = "";
    private string _choice = "";
    private string _variant = "";
    private string _blend = "Normal";
    private string _className = "";
    private string _filename = "";
    private string _imagePath = "";
    private string _overrideStuntName = "";
    private int _nativeX;
    private int _nativeY;
    private int _visualType = 3;
    private int _visualDepth;
    private int _visualOffsetX;
    private int _visualOffsetY;
    private bool _hasAffineBasis;
    private double _basisXX;
    private double _basisXY;
    private double _basisYX;
    private double _basisYY;
    private bool _isHidden;
    private bool _isEditorOnly;
    private bool _isPreviewOnly;
    private bool _isHierarchyAttachment;
    private bool _isHierarchySelected;
    private string _dynamicXml = "";
    private string _dynamicTimelineJson = "";
    private string _dynamicTriggerXml = "";
    private string _triggerTarget = "";
    private string _triggerAction = "";
    private string _triggerValue = "";
    private string _triggerContentXml = "";
    private string _sourcePropertiesXml = "";

    public LevelNode()
    {
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HierarchyChildren));
            OnPropertyChanged(nameof(VisualBounds));
            OnPropertyChanged(nameof(RendersAsRuntimeGraph));
        };
    }

    public Guid Id { get; internal set; } = Guid.NewGuid();
    public ObservableCollection<LevelNode> Children { get; } = [];
    public ObservableCollection<RoomLayoutRule> RoomLayoutRules { get; } = [];
    public Dictionary<string, string> SourceAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> LibraryOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string SourcePropertiesXml { get => _sourcePropertiesXml; set => Set(ref _sourcePropertiesXml, value); }
    public IEnumerable<LevelNode> HierarchyChildren => VisibleHierarchyChildren();
    public bool IsHierarchyVisible => IsHierarchyAttachment || !IsPreviewOnly || IsRuntimeLogicalChild;
    public bool IsRuntimeLogicalChild => Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor or LevelNodeKind.Image);

    public string Name { get => _name; set => Set(ref _name, value); }
    public LevelNodeKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(IsHierarchyVisible));
            OnPropertyChanged(nameof(IsRuntimeLogicalChild));
        }
    }
    public int X { get => _x; set => Set(ref _x, value); }
    public int Y { get => _y; set => Set(ref _y, value); }
    public int Width { get => _width; set => Set(ref _width, Math.Max(1, value)); }
    public int Height { get => _height; set => Set(ref _height, Math.Max(1, value)); }
    public double Rotation { get => _rotation; set => Set(ref _rotation, value); }
    public string Factor { get => _factor; set => Set(ref _factor, value); }
    public string Tag { get => _tag; set => Set(ref _tag, value); }
    public string SortingLayer { get => _sortingLayer; set => Set(ref _sortingLayer, value); }
    public string Template { get => _template; set => Set(ref _template, value); }
    public string Choice { get => _choice; set => Set(ref _choice, value); }
    public string Variant { get => _variant; set => Set(ref _variant, value); }
    public string Blend { get => _blend; set => Set(ref _blend, value); }
    public string ClassName { get => _className; set => Set(ref _className, value); }
    public string Filename { get => _filename; set => Set(ref _filename, value); }
    public string ImagePath { get => _imagePath; set => Set(ref _imagePath, value); }
    public string OverrideStuntName { get => _overrideStuntName; set => Set(ref _overrideStuntName, value); }

    /// <summary>
    /// Image XML has extra native-size/matrix info that plain rectangles do not.
    /// We keep those values here so imported Vector 2 rooms can round-trip
    /// through Windows without quietly flattening image data.
    /// </summary>
    public int NativeX { get => _nativeX; set => Set(ref _nativeX, value); }
    public int NativeY { get => _nativeY; set => Set(ref _nativeY, value); }
    public int VisualType { get => _visualType; set => Set(ref _visualType, value); }
    public int VisualDepth { get => _visualDepth; set => Set(ref _visualDepth, value); }
    public int VisualOffsetX { get => _visualOffsetX; set => Set(ref _visualOffsetX, value); }
    public int VisualOffsetY { get => _visualOffsetY; set => Set(ref _visualOffsetY, value); }
    public bool HasAffineBasis { get => _hasAffineBasis; set => Set(ref _hasAffineBasis, value); }
    public double BasisXX { get => _basisXX; set => Set(ref _basisXX, value); }
    public double BasisXY { get => _basisXY; set => Set(ref _basisXY, value); }
    public double BasisYX { get => _basisYX; set => Set(ref _basisYX, value); }
    public double BasisYY { get => _basisYY; set => Set(ref _basisYY, value); }

    public bool IsHidden { get => _isHidden; set => Set(ref _isHidden, value); }
    public bool IsEditorOnly { get => _isEditorOnly; set => Set(ref _isEditorOnly, value); }
    public bool IsHierarchyAttachment
    {
        get => _isHierarchyAttachment;
        set
        {
            if (!Set(ref _isHierarchyAttachment, value)) return;
            OnPropertyChanged(nameof(IsHierarchyVisible));
        }
    }
    public bool IsHierarchySelected { get => _isHierarchySelected; set => Set(ref _isHierarchySelected, value); }
    public bool IsPreviewOnly
    {
        get => _isPreviewOnly;
        set
        {
            if (!Set(ref _isPreviewOnly, value)) return;
            OnPropertyChanged(nameof(IsHierarchyVisible));
        }
    }

    public string DynamicXml { get => _dynamicXml; set => Set(ref _dynamicXml, value); }
    public string DynamicTimelineJson { get => _dynamicTimelineJson; set => Set(ref _dynamicTimelineJson, value); }
    public string DynamicTriggerXml { get => _dynamicTriggerXml; set => Set(ref _dynamicTriggerXml, value); }
    public string TriggerTarget { get => _triggerTarget; set => Set(ref _triggerTarget, value); }
    public string TriggerAction { get => _triggerAction; set => Set(ref _triggerAction, value); }
    public string TriggerValue { get => _triggerValue; set => Set(ref _triggerValue, value); }
    public string TriggerContentXml { get => _triggerContentXml; set => Set(ref _triggerContentXml, value); }

    public Rect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// Bounds of the actual visible art.
    ///
    /// Library/ObjectReference nodes can have tiny owner rectangles plus child
    /// sprite pieces. Selection and group rotation need this union or the box
    /// appears miles off and rotation happens from the wrong corner.
    /// </summary>
    public Rect VisualBounds
    {
        get
        {
            var visibleChildren = Children.Where(child => !child.IsHidden).ToList();
            if (visibleChildren.Count == 0)
            {
                return HasAffineBasis ? AffineBounds : Bounds;
            }

            var bounds = visibleChildren[0].VisualBounds;
            foreach (var child in visibleChildren.Skip(1))
            {
                bounds.Union(child.VisualBounds);
            }
            return bounds;
        }
    }

    public Rect AffineBounds
    {
        get
        {
            if (!HasAffineBasis) return Bounds;
            var points = new[]
            {
                new Point(X, Y),
                new Point(X + BasisXX, Y + BasisXY),
                new Point(X + BasisXX + BasisYX, Y + BasisXY + BasisYY),
                new Point(X + BasisYX, Y + BasisYY)
            };
            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }
    }

    public Brush FillBrush => Kind switch
    {
        LevelNodeKind.Platform => new SolidColorBrush(Color.FromArgb(190, 0, 0, 120)),
        LevelNodeKind.Trapezoid => new SolidColorBrush(Color.FromArgb(170, 120, 115, 210)),
        LevelNodeKind.Trigger => new SolidColorBrush(Color.FromArgb(90, 255, 190, 0)),
        LevelNodeKind.Area => new SolidColorBrush(Color.FromArgb(95, 255, 90, 105)),
        LevelNodeKind.Comment => new SolidColorBrush(Color.FromArgb(95, 255, 40, 40)),
        LevelNodeKind.GateIn or LevelNodeKind.GateOut => new SolidColorBrush(Color.FromArgb(160, 70, 190, 100)),
        LevelNodeKind.Image => Brushes.Transparent,
        _ => new SolidColorBrush(Color.FromArgb(125, 70, 210, 120))
    };

    public Pen StrokePen => Kind switch
    {
        LevelNodeKind.Platform => new Pen(Brushes.Blue, 1.5),
        LevelNodeKind.Trapezoid => new Pen(Brushes.Blue, 1.25),
        LevelNodeKind.Trigger => new Pen(Brushes.Orange, 1),
        LevelNodeKind.Area => new Pen(Brushes.IndianRed, 1),
        LevelNodeKind.Comment => new Pen(Brushes.Red, 1),
        _ => new Pen(Brushes.SeaGreen, 1)
    };

    public IEnumerable<LevelNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var item in child.Flatten())
            {
                yield return item;
            }
        }
    }

    public IEnumerable<LevelNode> FlattenEditableScene()
    {
        if (IsHidden) yield break;

        if (Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor) && !IsPreviewOnly)
        {
            yield return this;

            if (RendersAsRuntimeGraph)
            {
                yield break;
            }
        }

        foreach (var child in Children)
        {
            foreach (var item in child.FlattenEditableScene())
            {
                yield return item;
            }
        }
    }

    public bool RendersAsRuntimeGraph => Children.Count > 0 && Kind is
        LevelNodeKind.Object or
        LevelNodeKind.ObjectReference or
        LevelNodeKind.Dynamic or
        LevelNodeKind.Camera or
        LevelNodeKind.Coin;

    public void MarkPreviewOnly(bool previewOnly)
    {
        IsPreviewOnly = previewOnly;
        foreach (var child in Children)
        {
            child.MarkPreviewOnly(previewOnly);
        }
    }

    public void MarkHierarchyAttachment(bool attached)
    {
        IsHierarchyAttachment = attached;
        foreach (var child in Children)
        {
            child.MarkHierarchyAttachment(attached);
        }
    }

    private IEnumerable<LevelNode> VisibleHierarchyChildren()
    {
        foreach (var child in Children)
        {
            if (child.IsHierarchyVisible)
            {
                yield return child;
                continue;
            }

            foreach (var descendant in child.HierarchyChildren)
            {
                yield return descendant;
            }
        }
    }
}

public enum LevelNodeKind
{
    Document,
    Track,
    Factor,
    Image,
    Object,
    Platform,
    Trapezoid,
    Trigger,
    Area,
    Comment,
    Coin,
    Camera,
    ObjectReference,
    Dynamic,
    Waypoint,
    GateIn,
    GateOut
}
