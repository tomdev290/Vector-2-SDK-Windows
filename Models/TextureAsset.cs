namespace Vector2LevelEditor.Models;

/// <summary>
/// Asset browser card. Textures become Image nodes; library/prefab entries become
/// ObjectReference/Object nodes that preserve Filename/ClassName for export.
/// </summary>
public sealed record TextureAsset(
    string Group,
    TextureAssetKind Kind,
    string Category,
    string Name,
    string ClassName,
    string FilePath,
    string DefaultLayer,
    string SourcePath = "",
    string InstanceKey = "")
{
    public bool IsStuntIcon =>
        Name.Contains("track_trick_", StringComparison.OrdinalIgnoreCase) ||
        ClassName.Contains("track_trick_", StringComparison.OrdinalIgnoreCase) ||
        System.IO.Path.GetFileNameWithoutExtension(FilePath).Contains("track_trick_", StringComparison.OrdinalIgnoreCase);
}

public enum TextureAssetKind
{
    Texture,
    LibraryObject,
    Prefab
}
