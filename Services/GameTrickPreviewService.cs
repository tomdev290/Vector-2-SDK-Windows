using System.IO;
using System.Xml.Linq;
using System.Windows;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Reads the same moves, model skins, and binary frames as Vector 2's
/// AnimationLoader/AnimationBinaryParser. Unity rendering is intentionally replaced
/// with a native WPF preview, but the source data and 46-node frame contract are exact.
/// </summary>
public sealed class GameTrickPreviewService
{
    public IReadOnlyList<string> LoadAnimationAreaNames()
    {
        var path = Path.Combine(ResolveGameDataRoot(), "run_data", "libraries", "moves_new.xml");
        return ParseAnimationAreaNames(XDocument.Load(path));
    }

    public static IReadOnlyList<string> ParseAnimationAreaNames(XDocument document) => document.Descendants()
        .Select(element => ((string?)element.Attribute("AreaName"))?.Trim())
        .OfType<string>().Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<GameTrickMove> LoadMoves()
    {
        var gamedata = ResolveGameDataRoot();
        var path = Path.Combine(gamedata, "run_data", "libraries", "moves_new.xml");
        if (!File.Exists(path)) throw new FileNotFoundException("moves_new.xml was not found under the selected game Assets folder.", path);
        var document = XDocument.Load(path);
        var moves = document.Descendants("Moves").Elements()
            .Where(node => node.Attribute("FileName") is not null)
            .Select(node => new GameTrickMove(
                node.Name.LocalName,
                (string?)node.Attribute("FileName") ?? "",
                Int(node, "FirstFrame"),
                Int(node, "EndFrame"),
                Math.Max(1, Int(node, "MidFrames", 2)),
                Bool(node, "Loop"),
                (string?)node.Attribute("PivotNode") ?? "NPivot",
                Bool(node, "Trick"),
                (string?)node.Attribute("Parts") ?? ""))
            .Where(move => !string.IsNullOrWhiteSpace(move.FileName))
            .OrderByDescending(move => move.IsTrick)
            .ThenBy(move => move.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var projectRoot = StructuralRoomService.CurrentProjectRoot();
        var customRoot = projectRoot.Length > 0 ? projectRoot : Vector2GameIntegrationLocator.Detect()?.ModDataRoot;
        if (!string.IsNullOrWhiteSpace(customRoot))
            foreach (var move in LoadCustomMoves(customRoot))
                if (!moves.Any(existing => existing.Name.Equals(move.Name, StringComparison.OrdinalIgnoreCase))) moves.Insert(0, move);
        DiagnosticsLog.Info($"Game trick catalog loaded {moves.Count} stock and custom moves.");
        return moves;
    }

    public static IReadOnlyList<GameTrickMove> LoadCustomMoves(string root)
    {
        var folder = Path.Combine(root, "custom_tricks");
        var result = new List<GameTrickMove>();
        if (!Directory.Exists(folder)) return result;
        foreach (var path in Directory.EnumerateFiles(folder, "trick.xml", SearchOption.AllDirectories))
        {
            try
            {
                var node = XDocument.Load(path).Root;
                if (node?.Name.LocalName != "CustomTrick") continue;
                var name = (string?)node.Attribute("Name") ?? "";
                var file = Services.ProjectManager.ProjectManifestService.SafeCombine(Path.GetDirectoryName(path)!, (string?)node.Attribute("FileName") ?? "");
                if (name.Length == 0 || !File.Exists(file)) continue;
                var frames = ValidateImportedFrames(file);
                result.Add(new(name, file, Int(node, "FirstFrame"), Int(node, "EndFrame", frames - 1),
                    Math.Max(1, Int(node, "MidFrames", 2)), Bool(node, "Loop"), (string?)node.Attribute("PivotNode") ?? "DetectorH", true, "Custom"));
            }
            catch (Exception error) when (error is IOException or System.Xml.XmlException or InvalidDataException or ArgumentException)
            { DiagnosticsLog.Warn("Custom animation skipped " + path + ": " + error.Message); }
        }
        return result.DistinctBy(move => move.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<GameTrickModelSkin> LoadModelSkins()
    {
        var modelsRoot = Path.Combine(ResolveGameDataRoot(), "run_data", "models");
        return Directory.EnumerateFiles(modelsRoot, "*.xml", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(filename => !string.IsNullOrWhiteSpace(filename))
            .Select(filename => new GameTrickModelSkin(filename!, DisplaySkinName(filename!)))
            .OrderBy(skin => skin.Filename == "0.xml" ? 0 : skin.Filename == "1.xml" ? 1 : 2)
            .ThenBy(skin => skin.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public GameTrickPlayback LoadPlayback(GameTrickMove move, IReadOnlyList<string>? skins = null, int startEarlierFrames = 5, Guid? anchorNodeId = null, string? customModelsDirectory = null)
    {
        var gamedata = ResolveGameDataRoot();
        var selectedSkins = (skins is { Count: > 0 } ? skins : ["0.xml", "1.xml"])
            .Where(skin => !string.IsNullOrWhiteSpace(skin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!selectedSkins.Contains("0.xml", StringComparer.OrdinalIgnoreCase)) selectedSkins.Insert(0, "0.xml");
        return new GameTrickPlayback
        {
            Move = move,
            Model = LoadModel(Path.Combine(gamedata, "run_data", "models"), selectedSkins, customModelsDirectory),
            Frames = LoadFrames(Path.Combine(gamedata, "animations", move.FileName)),
            StartFrame = Math.Max(0, move.FirstFrame - Math.Clamp(startEarlierFrames, 0, 30)),
            SelectedSkins = selectedSkins,
            AnchorNodeId = anchorNodeId
        };
    }

    public GameTrickPlayback LoadImportedPlayback(string path, string name, string pivot)
    {
        var frames = LoadFrames(path);
        if (frames.Count == 0) throw new InvalidDataException("The animation contains no frames.");
        var gamedata = ResolveGameDataRoot();
        return new GameTrickPlayback
        {
            Move = new GameTrickMove(name, Path.GetFileName(path), 0, frames.Count - 1, 1, false, pivot, true, ""),
            Model = LoadModel(Path.Combine(gamedata, "run_data", "models"), ["0.xml", "1.xml"]),
            Frames = frames,
            StartFrame = 0,
            SelectedSkins = ["0.xml", "1.xml"]
        };
    }

    public static int ValidateImportedFrames(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Animation file not found.", path);
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.BaseStream.Length < 4) throw new InvalidDataException("The animation is empty.");
        var count = reader.ReadInt32();
        if (count is < 1 or > 100000) throw new InvalidDataException("The animation frame count is invalid.");
        for (var frame = 0; frame < count; frame++)
        {
            if (reader.BaseStream.Length - reader.BaseStream.Position < 5) throw new InvalidDataException("The animation is truncated.");
            _ = reader.ReadByte();
            var points = reader.ReadInt32();
            if (points != 46) throw new InvalidDataException($"Frame {frame} has {points} nodes; Vector 2 requires 46.");
            if (reader.BaseStream.Length - reader.BaseStream.Position < 46 * 12) throw new InvalidDataException("The animation is truncated.");
            reader.BaseStream.Seek(46 * 12, SeekOrigin.Current);
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("The animation contains trailing data.");
        return count;
    }

    private static GameTrickModel LoadModel(string modelsRoot, IReadOnlyList<string> skins, string? customModelsDirectory = null)
    {
        var nodes = new List<GameTrickNode>();
        var edges = new List<GameTrickEdge>();
        var capsules = new List<GameTrickCapsule>();
        var triangles = new List<GameTrickTriangle>();
        var nodePoints = new List<GameTrickNodePoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customModels = CustomModelCatalogService.Load(customModelsDirectory ?? CustomModelCatalogService.DefaultDirectory())
            .ToDictionary(item => item.Reference, item => item.ModelPath, StringComparer.OrdinalIgnoreCase);
        foreach (var file in skins)
        {
            var path = file.StartsWith("custom:", StringComparison.OrdinalIgnoreCase)
                ? customModels.GetValueOrDefault(file, "")
                : Path.Combine(modelsRoot, file);
            if (!File.Exists(path)) continue;
            var document = XDocument.Load(path);
            foreach (var element in document.Root?.Element("Nodes")?.Elements() ?? [])
            {
                var type = (string?)element.Attribute("Type") ?? "";
                var name = element.Name.LocalName;
                if (type is not ("Node" or "CenterOfMass" or "MacroNode") || !seen.Add(name)) continue;
                var count = Int(element, "NodesCount");
                var childNames = Enumerable.Range(1, count).Select(i => (string?)element.Attribute($"ChildNode{i}") ?? "").Where(v => v.Length > 0).ToList();
                var weights = Enumerable.Range(1, count).Select(i => Double(element, $"LCC{i}", 1)).ToList();
                nodes.Add(new GameTrickNode(name, new Point(Double(element, "X"), -Double(element, "Y")), nodes.Count < 46 ? nodes.Count : null, childNames, weights));
            }
            foreach (var element in document.Root?.Element("Edges")?.Elements() ?? [])
            {
                var type = (string?)element.Attribute("Type") ?? "";
                if (type is not ("Edge" or "Muscle")) continue;
                var start = (string?)element.Attribute("End1") ?? "";
                var end = (string?)element.Attribute("End2") ?? "";
                if (start.Length > 0 && end.Length > 0) edges.Add(new GameTrickEdge(element.Name.LocalName, start, end));
            }
            foreach (var element in document.Root?.Element("Figures")?.Elements() ?? [])
            {
                switch ((string?)element.Attribute("Type"))
                {
                    case "Capsule":
                        var edge = (string?)element.Attribute("Edge") ?? "";
                        if (edge.Length > 0) capsules.Add(new GameTrickCapsule(edge, Math.Max(.5, Double(element, "Radius1", 3)), Double(element, "Margin1"), Double(element, "Margin2")));
                        break;
                    case "Triangle":
                        var triangleNodes = new[] { "Node1", "Node2", "Node3" }
                            .Select(name => (string?)element.Attribute(name) ?? "").Where(name => name.Length > 0).ToList();
                        if (triangleNodes.Count == 3) triangles.Add(new GameTrickTriangle(triangleNodes));
                        break;
                    case "NodePoint":
                        var nodeName = (string?)element.Attribute("Node") ?? "";
                        if (nodeName.Length > 0) nodePoints.Add(new GameTrickNodePoint(nodeName, Math.Max(1, Double(element, "Radius", 3))));
                        break;
                }
            }
        }
        if (nodes.Count == 0) throw new InvalidDataException("Vector 2 model skins 0.xml/1.xml could not be loaded.");
        return new GameTrickModel { Nodes = nodes, Edges = edges, Capsules = capsules, Triangles = triangles, NodePoints = nodePoints };
    }

    private static IReadOnlyList<IReadOnlyList<Point>> LoadFrames(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Animation byte stream was not found.", path);
        using var reader = new BinaryReader(File.OpenRead(path));
        var frameCount = reader.ReadInt32();
        if (frameCount < 0 || frameCount > 100000) throw new InvalidDataException("Animation frame count is invalid.");
        var frames = new List<IReadOnlyList<Point>>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            _ = reader.ReadByte();
            var vectorCount = reader.ReadInt32();
            var points = Enumerable.Repeat(new Point(), 46).ToArray();
            for (var index = 0; index < vectorCount; index++)
            {
                var x = reader.ReadSingle();
                var y = -reader.ReadSingle();
                _ = reader.ReadSingle();
                if (index < 46) points[index] = new Point(x, y);
            }
            frames.Add(points);
        }
        return frames;
    }

    private string ResolveGameDataRoot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GameData");
        return Directory.Exists(Path.Combine(path, "run_data")) && Directory.Exists(Path.Combine(path, "animations"))
            ? path
            : throw new DirectoryNotFoundException("The bundled Vector 2 Trick Preview data is missing. Reinstall the complete editor package.");
    }

    private static int Int(XElement node, string name, int fallback = 0) => int.TryParse((string?)node.Attribute(name), out var value) ? value : fallback;
    private static double Double(XElement node, string name, double fallback = 0) => double.TryParse((string?)node.Attribute(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static bool Bool(XElement node, string name) => ((string?)node.Attribute(name)) is string raw && (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static string DisplaySkinName(string filename) => filename switch
    {
        "0.xml" => "Skeleton",
        "1.xml" => "Default body",
        "helper.xml" => "Helper body",
        _ => Path.GetFileNameWithoutExtension(filename).Replace('_', ' ')
    };
}
