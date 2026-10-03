namespace Vector2LevelEditor.Models;

public sealed record CustomModelItem(
    string Id, string Name, string Category, string Author, string ModelPath, string PackagePath)
{
    public string Reference => "custom:" + Id;
}
