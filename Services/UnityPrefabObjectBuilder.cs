using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Rebuilds Unity editor prefabs into WPF preview nodes.
///
/// The macOS editor reads these prefab YAML files so asset-browser objects look
/// like the real Vector 2 chunks instead of mystery boxes. This Windows version
/// follows the same idea: parse GameObjects, Transforms, SpriteRenderers, nested
/// ObjectReferences, and the sprite GUID map, then keep the compact prefab owner
/// node for export while drawing the reconstructed children for humans.
/// </summary>
public static partial class UnityPrefabObjectBuilder
{
    private const int MaxPieces = 260;

    private sealed record GameObjectData(string Id, string Name, bool IsActive);
    private sealed record TransformData(string Id, string GameObjectId, string ParentId, Point Position, Vector Scale, double Rotation);
    private sealed record RendererData(string GameObjectId, string ClassName, string SortingLayer, int SortingOrder, bool IsEnabled);

    private readonly struct WorldTransform(Point position, Vector scale, double rotation)
    {
        public Point Position { get; } = position;
        public Vector Scale { get; } = scale;
        public double Rotation { get; } = rotation;
    }

    public static string FirstVisualClass(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath) || !File.Exists(prefabPath)) return "";
        try
        {
            foreach (var line in File.ReadLines(prefabPath))
            {
                var match = VisualNameRegex().Match(line);
                if (match.Success) return match.Groups["class"].Value.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            DiagnosticsLog.Warn($"Could not inspect prefab preview '{prefabPath}': {ex.Message}");
        }
        return "";
    }

    public static bool TryGetRunnerInfo(string prefabPath, out string runnerName, out bool isArea)
    {
        runnerName = "";
        isArea = false;
        if (string.IsNullOrWhiteSpace(prefabPath) || !File.Exists(prefabPath)) return false;
        try
        {
            foreach (var line in File.ReadLines(prefabPath))
            {
                var match = RunnerNameRegex().Match(line);
                if (!match.Success) continue;
                runnerName = match.Groups["name"].Value.Trim();
                isArea = match.Groups["type"].Value.Equals("Area", StringComparison.OrdinalIgnoreCase) ||
                         IsWallRunner(runnerName);
                return !string.IsNullOrWhiteSpace(runnerName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            DiagnosticsLog.Warn($"Could not inspect prefab runner '{prefabPath}': {ex.Message}");
        }
        return false;
    }

    public static bool TryReconstructPrefab(LevelNode reference, string? importPath = null)
    {
        var filename = string.IsNullOrWhiteSpace(reference.Filename) ? reference.Choice : reference.Filename;
        var prefabPath = Vector2Catalog.ResolvePrefabPath(filename, importPath);
        if (string.IsNullOrWhiteSpace(prefabPath)) return false;

        (Dictionary<string, GameObjectData> GameObjects, Dictionary<string, TransformData> Transforms, Dictionary<string, RendererData> Renderers) parsed;
        try
        {
            parsed = ParsePrefab(File.ReadAllText(prefabPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or ArgumentException)
        {
            DiagnosticsLog.Warn($"Could not reconstruct prefab '{prefabPath}': {ex.Message}");
            return false;
        }
        var transformByGameObject = parsed.Transforms.Values
            .GroupBy(t => t.GameObjectId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var pieces = new List<LevelNode>();
        foreach (var transform in parsed.Transforms.Values)
        {
            if (pieces.Count >= MaxPieces) break;
            if (!parsed.GameObjects.TryGetValue(transform.GameObjectId, out var gameObject) || !gameObject.IsActive) continue;

            var world = ResolveWorldTransform(transform.Id, parsed.Transforms);
            if (TryBuildRunnerShapePiece(reference, gameObject, world, pieces)) continue;
            if (TryBuildSpritePiece(reference, gameObject, transform, world, parsed.Renderers, pieces)) continue;
            TryBuildReferencePiece(reference, gameObject, transform, world, prefabPath, transformByGameObject, pieces);
        }

        // Wall-run/jump prefabs contain a VisualRunner editor card as well as
        // the real AreaRunner gameplay volume. The card is only a Unity authoring
        // aid; drawing it beside the area produced the large "W" phantom in WPF.
        // Keep the prefab owner for export, but let its resolved area be the
        // complete canvas visual, matching the macOS shape-wrapper behavior.
        if (pieces.Any(piece =>
                piece.Template.Equals("EditorPrefabRunner", StringComparison.OrdinalIgnoreCase) &&
                piece.Kind == LevelNodeKind.Area &&
                IsWallRunner(piece.Name)))
        {
            pieces.RemoveAll(piece => piece.Kind == LevelNodeKind.Image);
        }

        var stuntIcons = pieces
            .Where(piece => piece.Template.Equals("EditorPrefabRunner", StringComparison.OrdinalIgnoreCase) &&
                            !IsWallRunner(piece.Name))
            .Select(piece => CreateRunnerStuntIcon(piece, reference.Factor))
            .Where(piece => piece is not null)
            .Cast<LevelNode>()
            .ToList();
        pieces.AddRange(stuntIcons);

        if (pieces.Count == 0) return false;

        reference.Template = "EditorPrefab";
        reference.Filename = Path.GetFileName(prefabPath);
        reference.Choice = reference.Filename;
        reference.Variant = "Prefab";
        reference.ImagePath = "";
        reference.VisualOffsetX = 0;
        reference.VisualOffsetY = 0;
        reference.Children.Clear();
        foreach (var piece in pieces.OrderBy(p => p.VisualDepth).Take(MaxPieces))
        {
            piece.MarkPreviewOnly(true);
            reference.Children.Add(piece);
        }
        ApplyBounds(reference);
        DiagnosticsLog.Info($"Reconstructed prefab '{reference.Name}' with {reference.Children.Count} preview pieces.");
        return true;
    }

    private static LevelNode? CreateRunnerStuntIcon(LevelNode runner, string factor)
    {
        if (!Vector2LibraryObjectBuilder.TryResolveStuntIcon(runner.Name, out var className, out var imagePath)) return null;
        var size = Math.Max(24, Math.Min(96, Math.Min(runner.Width, runner.Height) - 8));
        return new LevelNode
        {
            Name = runner.Name,
            Kind = LevelNodeKind.Image,
            X = runner.X + (runner.Width - size) / 2,
            Y = runner.Y + (runner.Height - size) / 2,
            Width = size,
            Height = size,
            Factor = factor,
            ClassName = className,
            ImagePath = imagePath,
            SortingLayer = "Debug",
            Tag = "Image",
            Template = "PhantomStuntIcon",
            Choice = "EditorPrefab",
            Variant = "InGameStuntIcon",
            VisualDepth = 101
        };
    }

    private static bool TryBuildRunnerShapePiece(
        LevelNode reference,
        GameObjectData gameObject,
        WorldTransform world,
        List<LevelNode> pieces)
    {
        var runnerMatch = RunnerNameRegex().Match($"m_Name: '{gameObject.Name}'");
        if (!runnerMatch.Success) return false;

        var runnerType = runnerMatch.Groups["type"].Value;
        var runnerName = runnerMatch.Groups["name"].Value.Trim();
        var kind = runnerType.Equals("Area", StringComparison.OrdinalIgnoreCase)
            ? LevelNodeKind.Area
            : LevelNodeKind.Trigger;
        if (IsWallRunner(runnerName))
        {
            kind = LevelNodeKind.Area;
        }
        var width = Math.Max(16, (int)Math.Round(Math.Abs(world.Scale.X * 50)));
        var height = Math.Max(16, (int)Math.Round(Math.Abs(world.Scale.Y * 50)));
        var centerX = reference.X + (int)Math.Round(world.Position.X);
        var centerY = reference.Y - (int)Math.Round(world.Position.Y);

        pieces.Add(new LevelNode
        {
            Name = string.IsNullOrWhiteSpace(runnerName) ? runnerType : runnerName,
            Kind = kind,
            X = centerX - width / 2,
            Y = centerY - height / 2,
            Width = width,
            Height = height,
            Rotation = -world.Rotation,
            Factor = reference.Factor,
            SortingLayer = "Debug",
            Tag = kind == LevelNodeKind.Area ? "Area" : "Trigger",
            Template = "EditorPrefabRunner",
            Choice = "EditorPrefab",
            Variant = runnerType,
            VisualDepth = 100
        });
        return true;
    }

    private static bool IsWallRunner(string name)
    {
        var normalized = new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Contains("walljump", StringComparison.Ordinal) ||
               normalized.Contains("wallrun", StringComparison.Ordinal);
    }

    private static bool TryBuildSpritePiece(
        LevelNode reference,
        GameObjectData gameObject,
        TransformData transform,
        WorldTransform world,
        IReadOnlyDictionary<string, RendererData> renderers,
        List<LevelNode> pieces)
    {
        var visualMatch = VisualNameRegex().Match($"m_Name: '{gameObject.Name}'");
        var className = visualMatch.Success ? visualMatch.Groups["class"].Value.Trim() : "";
        var sortingLayer = "Default";
        var sortingOrder = 0;

        if (renderers.TryGetValue(gameObject.Id, out var renderer))
        {
            if (!renderer.IsEnabled) return false;
            if (string.IsNullOrWhiteSpace(className)) className = renderer.ClassName;
            sortingLayer = renderer.SortingLayer;
            sortingOrder = renderer.SortingOrder;
        }

        if (string.IsNullOrWhiteSpace(className)) return false;

        var width = Math.Max(8, (int)Math.Round(Math.Abs(world.Scale.X * 50)));
        var height = Math.Max(8, (int)Math.Round(Math.Abs(world.Scale.Y * 50)));
        var centerX = reference.X + (int)Math.Round(world.Position.X);
        var centerY = reference.Y - (int)Math.Round(world.Position.Y);

        pieces.Add(new LevelNode
        {
            Name = className,
            Kind = LevelNodeKind.Image,
            X = centerX - width / 2,
            Y = centerY - height / 2,
            Width = width,
            Height = height,
            Rotation = -world.Rotation,
            ClassName = className,
            ImagePath = Vector2Catalog.ResolveImagePath(className),
            SortingLayer = sortingLayer,
            Tag = "Image",
            Template = "Image",
            Choice = "EditorPrefab",
            Variant = "PrefabPiece",
            VisualDepth = sortingOrder
        });
        return true;
    }

    private static bool TryBuildReferencePiece(
        LevelNode reference,
        GameObjectData gameObject,
        TransformData transform,
        WorldTransform world,
        string prefabPath,
        IReadOnlyDictionary<string, TransformData> transformByGameObject,
        List<LevelNode> pieces)
    {
        var refMatch = ReferenceNameRegex().Match($"m_Name: '{gameObject.Name}'");
        var objectMatch = ObjectNameRegex().Match($"m_Name: '{gameObject.Name}'");
        var objectName = refMatch.Success
            ? refMatch.Groups["name"].Value.Trim()
            : objectMatch.Success ? objectMatch.Groups["name"].Value.Trim() : "";
        if (string.IsNullOrWhiteSpace(objectName)) return false;

        var library = Vector2Catalog.FindLibraryContainingObject(objectName, prefabPath);
        var child = new LevelNode
        {
            Name = objectName,
            Kind = LevelNodeKind.ObjectReference,
            X = reference.X + (int)Math.Round(world.Position.X),
            Y = reference.Y - (int)Math.Round(world.Position.Y),
            Width = 120,
            Height = 100,
            Rotation = -world.Rotation,
            Factor = reference.Factor,
            Filename = string.IsNullOrWhiteSpace(library) ? "" : Path.GetFileName(library),
            Choice = string.IsNullOrWhiteSpace(library) ? "" : Path.GetFileName(library),
            Template = "LibraryObject",
            Variant = "PrefabReference",
            Tag = "ObjectReference",
            ClassName = objectName
        };

        if (!string.IsNullOrWhiteSpace(library))
        {
            Vector2LibraryObjectBuilder.TryReconstructReference(child, library);
        }

        if (child.Children.Count == 0 && transformByGameObject.TryGetValue(gameObject.Id, out var ownerTransform))
        {
            child.Width = Math.Max(16, (int)Math.Round(Math.Abs(ownerTransform.Scale.X * 80)));
            child.Height = Math.Max(16, (int)Math.Round(Math.Abs(ownerTransform.Scale.Y * 80)));
        }

        pieces.Add(child);
        return true;
    }

    private static (Dictionary<string, GameObjectData> GameObjects, Dictionary<string, TransformData> Transforms, Dictionary<string, RendererData> Renderers) ParsePrefab(string text)
    {
        var gameObjects = new Dictionary<string, GameObjectData>(StringComparer.OrdinalIgnoreCase);
        var transforms = new Dictionary<string, TransformData>(StringComparer.OrdinalIgnoreCase);
        var renderers = new Dictionary<string, RendererData>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in text.Split("--- !u!", StringSplitOptions.RemoveEmptyEntries))
        {
            var id = HeaderIdRegex().Match(block).Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (block.Contains("GameObject:", StringComparison.Ordinal))
            {
                var name = NameRegex().Match(block).Groups["name"].Value.Trim();
                var active = !IsActiveRegex().Match(block).Success || IsActiveRegex().Match(block).Groups["active"].Value != "0";
                gameObjects[id] = new GameObjectData(id, name, active);
                continue;
            }

            if (block.Contains("Transform:", StringComparison.Ordinal))
            {
                transforms[id] = new TransformData(
                    id,
                    GameObjectRefRegex().Match(block).Groups["id"].Value,
                    FatherRegex().Match(block).Groups["id"].Value,
                    ParsePoint(PositionRegex().Match(block)),
                    ParseScale(ScaleRegex().Match(block)),
                    ParseRotation(RotationRegex().Match(block)));
                continue;
            }

            if (block.Contains("SpriteRenderer:", StringComparison.Ordinal))
            {
                var gameObjectId = GameObjectRefRegex().Match(block).Groups["id"].Value;
                var sprite = SpriteRegex().Match(block);
                var className = sprite.Success
                    ? Vector2Catalog.ResolveSpriteClassName(sprite.Groups["guid"].Value, sprite.Groups["file"].Value)
                    : "";
                renderers[gameObjectId] = new RendererData(
                    gameObjectId,
                    className,
                    SortingLayerName(SortingLayerRegex().Match(block).Groups["layer"].Value),
                    Int(SortingOrderRegex().Match(block).Groups["order"].Value),
                    !EnabledRegex().Match(block).Success || EnabledRegex().Match(block).Groups["enabled"].Value != "0");
            }
        }

        return (gameObjects, transforms, renderers);
    }

    private static WorldTransform ResolveWorldTransform(string transformId, IReadOnlyDictionary<string, TransformData> transforms)
    {
        if (!transforms.TryGetValue(transformId, out var current))
        {
            return new WorldTransform(new Point(), new Vector(1, 1), 0);
        }

        if (string.IsNullOrWhiteSpace(current.ParentId) || current.ParentId == "0" || !transforms.ContainsKey(current.ParentId))
        {
            return new WorldTransform(current.Position, current.Scale, current.Rotation);
        }

        var parent = ResolveWorldTransform(current.ParentId, transforms);
        var scaled = new Vector(current.Position.X * parent.Scale.X, current.Position.Y * parent.Scale.Y);
        var rotated = Rotate(scaled, parent.Rotation);
        return new WorldTransform(
            new Point(parent.Position.X + rotated.X, parent.Position.Y + rotated.Y),
            new Vector(parent.Scale.X * current.Scale.X, parent.Scale.Y * current.Scale.Y),
            parent.Rotation + current.Rotation);
    }

    private static void ApplyBounds(LevelNode reference)
    {
        var left = reference.Children.Min(c => c.VisualBounds.Left);
        var top = reference.Children.Min(c => c.VisualBounds.Top);
        var right = reference.Children.Max(c => c.VisualBounds.Right);
        var bottom = reference.Children.Max(c => c.VisualBounds.Bottom);
        reference.Width = Math.Max(reference.Width, (int)Math.Round(right - left));
        reference.Height = Math.Max(reference.Height, (int)Math.Round(bottom - top));
    }

    private static Vector Rotate(Vector value, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Vector(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
    }

    private static Point ParsePoint(Match match)
        => match.Success
            ? new Point(Parse(match.Groups["x"].Value), Parse(match.Groups["y"].Value))
            : new Point();

    private static Vector ParseScale(Match match)
        => match.Success
            ? new Vector(Parse(match.Groups["x"].Value), Parse(match.Groups["y"].Value))
            : new Vector(1, 1);

    private static double ParseRotation(Match match)
    {
        if (!match.Success) return 0;
        var z = Parse(match.Groups["z"].Value);
        var w = Parse(match.Groups["w"].Value);
        return Math.Atan2(2 * w * z, 1 - 2 * z * z) * 180.0 / Math.PI;
    }

    private static double Parse(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static int Int(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static string SortingLayerName(string layer)
        => layer switch
        {
            "-26" => "Wall",
            "-20" => "Black",
            "-8" => "Lights",
            "0" => "Default",
            _ => "Default"
        };

    [GeneratedRegex(@"&(?<id>-?\d+)")]
    private static partial Regex HeaderIdRegex();

    [GeneratedRegex(@"m_GameObject:\s*\{fileID:\s*(?<id>-?\d+)\}")]
    private static partial Regex GameObjectRefRegex();

    [GeneratedRegex(@"m_Father:\s*\{fileID:\s*(?<id>-?\d+)\}")]
    private static partial Regex FatherRegex();

    [GeneratedRegex(@"m_Name:\s*(?:'(?<name>[^']*)'|(?<name>[^\r\n]+))")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"m_Name:\s*'VisualRunner:\s*(?<class>[^']+)'")]
    private static partial Regex VisualNameRegex();

    [GeneratedRegex(@"m_Name:\s*'(?<type>Area|Trigger)Runner:\s*(?<name>[^']+)'")]
    private static partial Regex RunnerNameRegex();

    [GeneratedRegex(@"m_Name:\s*'ObjectReference:\s*(?<name>[^']+)'")]
    private static partial Regex ReferenceNameRegex();

    [GeneratedRegex(@"m_Name:\s*'Object:\s*(?<name>[^']+)'")]
    private static partial Regex ObjectNameRegex();

    [GeneratedRegex(@"m_LocalPosition:\s*\{x:\s*(?<x>-?[\d.]+),\s*y:\s*(?<y>-?[\d.]+),\s*z:\s*(?<z>-?[\d.]+)\}")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"m_LocalScale:\s*\{x:\s*(?<x>-?[\d.]+),\s*y:\s*(?<y>-?[\d.]+),\s*z:\s*(?<z>-?[\d.]+)\}")]
    private static partial Regex ScaleRegex();

    [GeneratedRegex(@"m_LocalRotation:\s*\{x:\s*(?<x>-?[\d.]+),\s*y:\s*(?<y>-?[\d.]+),\s*z:\s*(?<z>-?[\d.]+),\s*w:\s*(?<w>-?[\d.]+)\}")]
    private static partial Regex RotationRegex();

    [GeneratedRegex(@"m_Sprite:\s*\{fileID:\s*(?<file>-?\d+),\s*guid:\s*(?<guid>[0-9a-fA-F]+),")]
    private static partial Regex SpriteRegex();

    [GeneratedRegex(@"m_SortingLayer:\s*(?<layer>-?\d+)")]
    private static partial Regex SortingLayerRegex();

    [GeneratedRegex(@"m_SortingOrder:\s*(?<order>-?\d+)")]
    private static partial Regex SortingOrderRegex();

    [GeneratedRegex(@"m_IsActive:\s*(?<active>[01])")]
    private static partial Regex IsActiveRegex();

    [GeneratedRegex(@"m_Enabled:\s*(?<enabled>[01])")]
    private static partial Regex EnabledRegex();
}
