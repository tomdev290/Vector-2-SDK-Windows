namespace Vector2LevelEditor.Models;

/// <summary>
/// The left rail, in the same order as the macOS editor.
/// </summary>
public enum EditorTool
{
    Cursor,
    Images,
    Backgrounds,
    Trapezoid,
    Collision,
    Trigger,
    Area,
    Coin,
    Camera,
    Comment,
    ObjectReference,
    Object,
    Dynamic,
    PlayerIn,
    PlayerOut,
    RunFast,
    Swarm,
    Waypoint
}

public static class EditorToolInfo
{
    public static readonly EditorTool[] Ordered =
    [
        EditorTool.Cursor,
        EditorTool.Images,
        EditorTool.Backgrounds,
        EditorTool.Trapezoid,
        EditorTool.Collision,
        EditorTool.Trigger,
        EditorTool.Area,
        EditorTool.Coin,
        EditorTool.Camera,
        EditorTool.Comment,
        EditorTool.ObjectReference,
        EditorTool.Object,
        EditorTool.Dynamic,
        EditorTool.PlayerIn,
        EditorTool.PlayerOut,
        EditorTool.RunFast,
        EditorTool.Swarm,
        EditorTool.Waypoint
    ];

    public static string Label(this EditorTool tool) => tool switch
    {
        EditorTool.Cursor => "Select",
        EditorTool.Images => "Images",
        EditorTool.Backgrounds => "BG",
        EditorTool.Trapezoid => "Trapezoid",
        EditorTool.Collision => "Collision",
        EditorTool.Trigger => "Trigger",
        EditorTool.Area => "Area",
        EditorTool.Coin => "Coins",
        EditorTool.Camera => "Camera",
        EditorTool.Comment => "Comment",
        EditorTool.ObjectReference => "Obj Ref",
        EditorTool.Object => "Object",
        EditorTool.Dynamic => "Dynamic",
        EditorTool.PlayerIn => "IN",
        EditorTool.PlayerOut => "OUT",
        EditorTool.RunFast => "RF",
        EditorTool.Swarm => "S",
        EditorTool.Waypoint => "W",
        _ => tool.ToString()
    };

    public static string Glyph(this EditorTool tool) => tool switch
    {
        EditorTool.Cursor => "",
        EditorTool.Images => "",
        EditorTool.Backgrounds => "",
        EditorTool.Trapezoid => "",
        EditorTool.Collision => "",
        EditorTool.Trigger => "T",
        EditorTool.Area => "A",
        EditorTool.Coin => "",
        EditorTool.Camera => "",
        EditorTool.Comment => "",
        EditorTool.ObjectReference => "",
        EditorTool.Object => "",
        EditorTool.Dynamic => "",
        EditorTool.PlayerIn => "IN",
        EditorTool.PlayerOut => "OUT",
        EditorTool.RunFast => "RF",
        EditorTool.Swarm => "S",
        EditorTool.Waypoint => "W",
        _ => "?"
    };

    public static string IconData(this EditorTool tool) => tool switch
    {
        EditorTool.Cursor => "M 5 3 L 5 21 L 10 16 L 13 23 L 16 22 L 13 15 L 20 15 Z",
        EditorTool.Images => "M 4 5 H 22 V 21 H 4 Z M 7 18 L 12 12 L 16 16 L 18 13 L 21 18 M 16 8 A 2 2 0 1 0 16 12 A 2 2 0 1 0 16 8",
        EditorTool.Backgrounds => "M 5 7 H 21 M 5 12 H 21 M 5 17 H 21 M 5 22 H 21",
        EditorTool.Trapezoid => "M 13 5 L 22 21 H 4 Z",
        EditorTool.Collision => "M 6 6 H 20 V 20 H 6 Z",
        EditorTool.Coin => "M 13 5 L 21 13 L 13 21 L 5 13 Z",
        EditorTool.Camera => "M 5 10 H 17 L 21 7 V 21 L 17 18 H 5 Z",
        EditorTool.Comment => "M 13 5 A 8 8 0 1 0 13 21 A 8 8 0 1 0 13 5",
        EditorTool.ObjectReference => "M 9 7 C 6 7 4 9 4 12 C 4 15 6 17 9 17 M 17 7 C 20 7 22 9 22 12 C 22 15 20 17 17 17 M 8 12 H 18",
        EditorTool.Object => "M 13 4 L 21 8 V 18 L 13 22 L 5 18 V 8 Z",
        EditorTool.Dynamic => "M 5 6 H 20 V 21 H 5 Z M 9 10 H 16 V 17 H 9 Z",
        _ => ""
    };

    public static bool CanPlaceDirectly(this EditorTool tool) => tool is not
        (EditorTool.Cursor or EditorTool.Images or EditorTool.Backgrounds or EditorTool.ObjectReference or EditorTool.Object);

    public static EditorTool Cycle(EditorTool current, int direction)
    {
        var index = Array.IndexOf(Ordered, current);
        if (index < 0) return current;
        var next = (index + direction + Ordered.Length) % Ordered.Length;
        return Ordered[next];
    }
}
