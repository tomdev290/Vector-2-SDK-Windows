namespace Vector2LevelEditor.Models;

public enum StructuralRoomKind { Entrance, Exit }

public sealed record StructuralZone(string Id, string Name, string RoomsPath)
{
    public override string ToString() => Name;
}
