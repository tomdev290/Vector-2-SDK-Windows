namespace Vector2LevelEditor.Models;

public enum RoomLayoutSection { Start, Middle, Finish, Dynamic }

public sealed record RoomLayoutRule(RoomLayoutSection Section, string Variant, string Parent = "")
{
    public string Choice => Section == RoomLayoutSection.Dynamic ? "Dynamic" : Section + "_Zone";
}

public sealed record RoomLayoutOption(RoomLayoutSection Section, string Variant, string Parent, IReadOnlyList<LevelNode> Nodes)
{
    public string DisplayName => Variant.Replace('_', ' ');
}
