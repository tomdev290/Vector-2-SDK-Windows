using System.Text.Json.Serialization;

namespace Vector2LevelEditor.Models;

public enum DynamicPathMode
{
    Bezier,
    Sin
}

public enum DynamicSinQuarter
{
    EaseIn,
    EaseOut,
    EaseInOut,
    FastSlowFast,
    BacktrackThenFinish
}

public enum DynamicRotationMode
{
    Linear,
    CubicEaseIn,
    CubicEaseOut,
    SmoothEaseIn,
    SmoothEaseOut,
    SmoothEaseInOut,
    FastSlowFast,
    BacktrackThenFinish
}

public sealed class DynamicStudioKeyframe : NotifyObject
{
    private int _frame;
    private int _x;
    private int _y;
    private int _width = 72;
    private int _height = 72;
    private double _rotation;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Frame { get => _frame; set => Set(ref _frame, Math.Max(0, value)); }
    public int X { get => _x; set => Set(ref _x, value); }
    public int Y { get => _y; set => Set(ref _y, value); }
    public int Width { get => _width; set => Set(ref _width, Math.Max(1, value)); }
    public int Height { get => _height; set => Set(ref _height, Math.Max(1, value)); }
    public double Rotation { get => _rotation; set => Set(ref _rotation, value); }

    [JsonIgnore]
    public string Summary => $"Frame {Frame}: ({X}, {Y}) {Width}x{Height} r={Rotation:0.##}";
}

public sealed class DynamicStudioTimelinePackage
{
    public string Name { get; set; } = "NewTransform";
    public int TotalFrames { get; set; } = 300;
    public bool CustomEase { get; set; }
    public DynamicPathMode MovementMode { get; set; } = DynamicPathMode.Bezier;
    public DynamicSinQuarter MovementQuarter { get; set; } = DynamicSinQuarter.EaseInOut;
    public DynamicPathMode SizeMode { get; set; } = DynamicPathMode.Bezier;
    public DynamicSinQuarter SizeQuarter { get; set; } = DynamicSinQuarter.EaseInOut;
    public DynamicRotationMode RotationMode { get; set; } = DynamicRotationMode.Linear;
    public List<DynamicStudioKeyframe> Keyframes { get; set; } = [];
}

public sealed class CustomBackgroundDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<string> Pieces { get; set; } = [];
}
