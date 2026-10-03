using System.IO;
using System.Globalization;
using System.Xml.Linq;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Runtime-graph-friendly XML importer.
/// This is intentionally tolerant: Vector 2 rooms can arrive wrapped as
/// Root/Track/Content/Object, or as a direct Track/Object tree.
/// </summary>
public sealed class XmlSceneParser
{
    private static readonly HashSet<string> SceneTags =
    [
        "Track", "Objects", "Object", "ObjectReference", "Image", "Platform", "Trapezoid", "Trigger", "Area",
        "In", "Out", "Camera", "Dynamic", "DynamicTrigger", "UnityModel", "Waypoint", "Spawn"
    ];

    public LevelDocument Load(string path)
    {
        DiagnosticsLog.Info($"Importing XML: {path}");
        var xdoc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        return LoadDocument(xdoc, Path.GetFileName(path), path);
    }

    public LevelDocument LoadFromString(string xml, string name = "raw.xml", string? sourcePath = null)
    {
        DiagnosticsLog.Info($"Applying Raw XML for {name}.");
        var xdoc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return LoadDocument(xdoc, name, sourcePath);
    }

    private static LevelDocument LoadDocument(XDocument xdoc, string name, string? sourcePath)
    {
        var document = new LevelDocument
        {
            Name = name,
            SourcePath = sourcePath ?? "",
            RawXml = xdoc.ToString(),
            CustomBackgroundName = (string?)xdoc.Root?.Attribute("CustomBackground") ?? ""
        };
        document.StructuralRoomKind = Enum.TryParse<StructuralRoomKind>(
            (string?)xdoc.Root?.Attribute("EditorStructuralRoom"), true, out var structuralKind)
            ? structuralKind
            : name.StartsWith("custom_start_room", StringComparison.OrdinalIgnoreCase) ? StructuralRoomKind.Entrance
            : name.StartsWith("custom_finish_room", StringComparison.OrdinalIgnoreCase) ? StructuralRoomKind.Exit
            : null;
        document.HasCustomBackgroundAssignment = !string.IsNullOrWhiteSpace(document.CustomBackgroundName);
        ParseAICharacters(xdoc.Root!, document);
        foreach (var skin in ((string?)xdoc.Root!.Element("PlayerAppearance")?.Attribute("Skins") ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries))
            document.PlayerSkinFiles.Add(skin);

        foreach (var node in CanonicalDocumentChildren(CollectSceneNodes(xdoc.Root!, "1", sourcePath).ToList()))
        {
            document.Nodes.Add(node);
        }

        document.SelectedNode = document.SceneNodes.FirstOrDefault();
        DiagnosticsLog.Info($"Imported {document.SceneNodes.Count()} scene nodes from {document.Name}.");
        return document;
    }

    private static void ParseAICharacters(XElement root, LevelDocument document)
    {
        var container = root.Element("AICharacters");
        if (container is null) return;
        foreach (var element in container.Elements("AICharacter"))
        {
            var skins = ((string?)element.Attribute("Skins") ?? "1.xml")
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            var character = new AICharacterDefinition
            {
                Id = Guid.TryParse((string?)element.Attribute("EditorID"), out var id) ? id : Guid.NewGuid(),
                Name = (string?)element.Attribute("Name") ?? "AI",
                Kind = (string?)element.Attribute("Kind") == "Enemy" ? "Enemy" : "Friendly",
                AIChannel = Math.Max(1, IntAttr(element, "AI", 1)),
                BodySkin = (string?)element.Attribute("EditorBodySkin") ?? skins.ElementAtOrDefault(0) ?? "1.xml",
                ChestSkin = (string?)element.Attribute("EditorChestSkin") ?? skins.ElementAtOrDefault(1) ?? "",
                HelmetSkin = (string?)element.Attribute("EditorHelmetSkin") ?? skins.ElementAtOrDefault(2) ?? "",
                HairSkin = (string?)element.Attribute("EditorHairSkin") ?? skins.ElementAtOrDefault(3) ?? "",
                BirthSpawn = (string?)element.Attribute("BirthSpawn") ?? "DefaultSpawn",
                StartDelay = DoubleAttr(element, "Time"),
                SpawnOnStart = (string?)element.Attribute("SpawnOnStart") != "0"
            };
            var layers = element.Attribute("EditorCustomLayers") is { } customLayers
                ? customLayers.Value.Split('|', StringSplitOptions.RemoveEmptyEntries) : skins.Skip(4);
            foreach (var skin in layers) character.CustomLayers.Add(skin);
            document.AICharacters.Add(character);
        }
        foreach (var element in container.Element("Groups")?.Elements("Group") ?? [])
        {
            var group = new AIGroupDefinition
            {
                Id = Guid.TryParse((string?)element.Attribute("EditorID"), out var id) ? id : Guid.NewGuid(),
                Name = (string?)element.Attribute("Name") ?? "AI Group"
            };
            foreach (var member in ((string?)element.Attribute("Members") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(member, out var memberId)) group.CharacterIds.Add(memberId);
            document.AIGroups.Add(group);
        }
    }

    public LevelNode ParseFragment(string xml)
    {
        var wrapped = $"<Root>{xml}</Root>";
        var root = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace).Root!;
        var first = root.Elements().FirstOrDefault(e => SceneTags.Contains(e.Name.LocalName))
            ?? throw new InvalidOperationException("No Vector 2 scene XML element was found.");
        return ParseNode(first, "1", null);
    }

    private static IEnumerable<LevelNode> CollectSceneNodes(XElement element, string inheritedFactor, string? sourcePath)
    {
        foreach (var child in element.Elements())
        {
            var factor = (string?)child.Attribute("Factor") ?? inheritedFactor;
            if (SceneTags.Contains(child.Name.LocalName))
            {
                yield return ParseNode(child, factor, sourcePath);
            }
            else
            {
                foreach (var nested in CollectSceneNodes(child, factor, sourcePath))
                {
                    yield return nested;
                }
            }
        }
    }

    private static LevelNode ParseNode(XElement element, string inheritedFactor, string? sourcePath)
    {
        var tag = element.Name.LocalName;
        var factor = (string?)element.Attribute("Factor") ?? inheritedFactor;

        if (tag == "Object" && (string?)element.Attribute("EditorDynamicPivot") == "1" &&
            element.Element("Content")?.Elements().Where(child => SceneTags.Contains(child.Name.LocalName)).ToList() is { Count: 1 } pivotChildren)
        {
            var original = ParseNode(pivotChildren[0], factor, sourcePath);
            original.X += IntAttr(element, "X");
            original.Y += IntAttr(element, "Y");
            original.Name = (string?)element.Attribute("EditorOriginalName") ?? original.Name;
            ApplyDynamicMetadata(element, original);
            return original;
        }

        if (tag == "Track")
        {
            var track = WithChildren(new LevelNode
            {
                Kind = LevelNodeKind.Track,
                Name = "Track",
                Factor = factor,
                Template = "Track",
                SourcePropertiesXml = element.Element("Properties")?.ToString(SaveOptions.DisableFormatting) ?? ""
            }, CollectSceneNodes(element, factor, sourcePath));
            ApplyDynamicMetadata(element, track);
            return track;
        }

        if (tag == "Objects")
        {
            return WithChildren(new LevelNode
            {
                Kind = LevelNodeKind.Track,
                Name = "Objects",
                Factor = factor,
                Template = "Objects"
            }, CollectSceneNodes(element, factor, sourcePath));
        }

        if (tag == "Object" && element.Attribute("Factor") is not null && element.Attribute("X") is null)
        {
            var factorNode = WithChildren(new LevelNode
            {
                Kind = LevelNodeKind.Factor,
                Name = $"Object Factor = {factor}",
                Factor = factor,
                Template = "Object",
                SourcePropertiesXml = element.Element("Properties")?.ToString(SaveOptions.DisableFormatting) ?? ""
            }, CollectSceneNodes(element, factor, sourcePath));
            ApplyDynamicMetadata(element, factorNode);
            return factorNode;
        }

        var wrapperTrigger = tag == "Object" && element.Attribute("EditorAITarget") is not null
            ? element.Element("Content")?.Element("Trigger") : null;
        var kind = wrapperTrigger is not null
            ? LevelNodeKind.Trigger : KindFor(tag);
        var node = new LevelNode
        {
            Kind = kind,
            Name = (string?)element.Attribute("Name") ?? (string?)element.Attribute("ClassName") ?? tag,
            Factor = factor,
            X = IntAttr(element, "X"),
            Y = IntAttr(element, "Y"),
            Width = Math.Max(1, IntAttr(element, "Width", wrapperTrigger is null ? DefaultWidth(tag) : IntAttr(wrapperTrigger, "Width", DefaultWidth("Trigger")))),
            Height = Math.Max(1, IntAttr(element, "Height", wrapperTrigger is null ? DefaultHeight(tag) : IntAttr(wrapperTrigger, "Height", DefaultHeight("Trigger")))),
            Rotation = DoubleAttr(element, "Rotation"),
            ClassName = (string?)element.Attribute("ClassName") ?? "",
            Filename = (string?)element.Attribute("Filename") ?? "",
            SortingLayer = (string?)element.Attribute("Layer") ?? "Default",
            Tag = (string?)element.Attribute("Tag") ?? DefaultTag(kind),
            Template = tag,
            Choice = (string?)element.Attribute("Filename") ?? "",
            Variant = kind == LevelNodeKind.Image ? "" : ((string?)element.Attribute("Type") ?? (string?)element.Attribute("Variant") ?? ""),
            Blend = (string?)element.Attribute("Blend") ?? "Normal"
        };

        if (kind == LevelNodeKind.Trigger)
        {
            node.TriggerTarget = (string?)element.Attribute("EditorAITarget") ?? "";
            node.TriggerAction = (string?)element.Attribute("EditorAIAction") ?? "";
            node.TriggerValue = (string?)element.Attribute("EditorAIValue") ?? "";
            node.TriggerContentXml = element.Element("Content")?.ToString(SaveOptions.DisableFormatting) ?? "";
        }

        ApplyStaticMetadata(element, node);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.NamespaceName.Length == 0)
                node.SourceAttributes[attribute.Name.LocalName] = attribute.Value;
        }
        foreach (var selection in element.Element("Properties")?.Element("Static")?.Elements("Selection") ?? [])
        {
            if (RoomLayoutService.Match((string?)selection.Attribute("Choice")) is not { } section ||
                (string?)selection.Attribute("Variant") is not { Length: > 0 } variant) continue;
            node.RoomLayoutRules.Add(new RoomLayoutRule(section, variant,
                (string?)selection.Attribute("Parent") ?? ""));
        }
        ApplyOverrideVariables(element, node);
        ApplyDynamicMetadata(element, node);
        if (kind is LevelNodeKind.Waypoint or LevelNodeKind.ObjectReference)
        {
            foreach (var attribute in element.Attributes())
                node.SourceAttributes[attribute.Name.LocalName] = attribute.Value;
            node.SourcePropertiesXml = element.Element("Properties")?.ToString(SaveOptions.DisableFormatting) ?? "";
            if (tag == "Spawn") node.SourceAttributes["EditorElement"] = "Spawn";
        }

        if (node.Kind == LevelNodeKind.Image)
        {
            node.ImagePath = Vector2Catalog.ResolveImagePath(node.ClassName);
            if (string.IsNullOrWhiteSpace(node.ImagePath))
            {
                DiagnosticsLog.Warn($"Image '{node.Name}' imported without a texture preview. ClassName='{node.ClassName}'.");
            }
        }

        if (node.Kind == LevelNodeKind.Trapezoid)
        {
            node.Variant = ((string?)element.Attribute("Type") == "2") ? "Type2" : node.Variant;
        }

        foreach (var child in element.Elements("Content").Elements().Where(e =>
            SceneTags.Contains(e.Name.LocalName) && !(kind == LevelNodeKind.Trigger && tag == "Object")))
        {
            node.Children.Add(ParseNode(child, node.Factor, sourcePath));
        }

        if (node.Kind == LevelNodeKind.ObjectReference)
        {
            if (node.Filename.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                UnityPrefabObjectBuilder.TryReconstructPrefab(node, sourcePath);
            }
            else
            {
                Vector2LibraryObjectBuilder.TryReconstructReference(node, sourcePath);
            }
        }
        else if (node.Kind == LevelNodeKind.Object && node.Template == "LibraryObject")
        {
            Vector2LibraryObjectBuilder.TryReconstructReference(node, sourcePath);
        }

        return node;
    }

    private static IEnumerable<LevelNode> CanonicalDocumentChildren(IReadOnlyList<LevelNode> parsedChildren)
    {
        var tracks = parsedChildren.Where(n => n.Kind == LevelNodeKind.Track).ToList();
        var looseSceneNodes = parsedChildren.Where(n => n.Kind != LevelNodeKind.Track).ToList();

        if (tracks.Count == 0)
        {
            var factor = WithChildren(new LevelNode
            {
                Name = "Object Factor = 1",
                Kind = LevelNodeKind.Factor,
                Factor = "1",
                Template = "Object"
            }, looseSceneNodes);
            yield return WithChildren(new LevelNode
            {
                Name = "Track",
                Kind = LevelNodeKind.Track,
                Factor = "1",
                Template = "Track"
            }, new[] { factor });
            yield break;
        }

        foreach (var track in tracks)
        {
            var existingFactors = track.Children.Where(n => n.Kind == LevelNodeKind.Factor).ToList();
            var directSceneNodes = track.Children.Where(n => n.Kind != LevelNodeKind.Factor).ToList();
            if (existingFactors.Count == 0)
            {
                track.Children.Clear();
                track.Children.Add(WithChildren(new LevelNode
                {
                    Name = "Object Factor = 1",
                    Kind = LevelNodeKind.Factor,
                    Factor = "1",
                    Template = "Object"
                }, directSceneNodes));
            }
            else if (directSceneNodes.Count > 0)
            {
                foreach (var direct in directSceneNodes)
                {
                    track.Children.Remove(direct);
                }
                track.Children.Add(WithChildren(new LevelNode
                {
                    Name = "Object Factor = 1",
                    Kind = LevelNodeKind.Factor,
                    Factor = "1",
                    Template = "Object"
                }, directSceneNodes));
            }
        }

        if (looseSceneNodes.Count > 0)
        {
            tracks[0].Children.Add(WithChildren(new LevelNode
            {
                Name = "Object Factor = 1",
                Kind = LevelNodeKind.Factor,
                Factor = "1",
                Template = "Object"
            }, looseSceneNodes));
        }

        foreach (var track in tracks)
        {
            yield return track;
        }
    }

    private static LevelNode WithChildren(LevelNode node, IEnumerable<LevelNode> children)
    {
        foreach (var child in children)
        {
            node.Children.Add(child);
        }
        return node;
    }

    /// <summary>
    /// Mirrors the important parts of XMLSceneParser.swift:
    /// - Template/Selection/BlendMode become inspector metadata.
    /// - Image Static.Matrix becomes canvas size + rotation instead of a plain
    ///   default 72x72 box. This is why imported game rooms stop collapsing into
    ///   blurry/incorrect rectangles.
    /// </summary>
    private static void ApplyStaticMetadata(XElement element, LevelNode node)
    {
        var properties = element.Element("Properties");
        var statics = properties?.Element("Static");
        if (properties is null) return;

        node.Template = (string?)statics?.Element("Template")?.Attribute("Name") ?? (string?)properties.Element("Template")?.Attribute("Name") ?? node.Template;
        node.Choice = (string?)statics?.Element("Selection")?.Attribute("Choice") ?? (string?)properties.Element("Selection")?.Attribute("Choice") ?? node.Choice;
        node.Variant = (string?)statics?.Element("Selection")?.Attribute("Variant") ?? (string?)properties.Element("Selection")?.Attribute("Variant") ?? node.Variant;
        node.Blend = (string?)statics?.Element("BlendMode")?.Attribute("Mode") ?? (string?)properties.Element("BlendMode")?.Attribute("Mode") ?? node.Blend;

        if (node.Kind != LevelNodeKind.Image || statics is null) return;

        node.VisualType = IntAttr(element, "Type", node.VisualType);
        node.VisualDepth = IntAttr(element, "Depth", node.VisualDepth);
        node.NativeX = IntAttr(element, "NativeX", 0);
        node.NativeY = IntAttr(element, "NativeY", 0);

        var matrix = statics.Element("Matrix");
        if (matrix is null) return;

        var a = DoubleAttr(matrix, "A", 1);
        var b = DoubleAttr(matrix, "B", 0);
        var c = DoubleAttr(matrix, "C", 0);
        var d = DoubleAttr(matrix, "D", 1);
        var tx = DoubleAttr(matrix, "Tx", 0);
        var ty = DoubleAttr(matrix, "Ty", 0);

        node.X += (int)Math.Round(tx);
        node.Y += (int)Math.Round(ty);

        var width = Math.Sqrt((a * a) + (b * b));
        var height = Math.Sqrt((c * c) + (d * d));
        if (width > 0.01) node.Width = Math.Max(1, (int)Math.Round(width));
        if (height > 0.01) node.Height = Math.Max(1, (int)Math.Round(height));
        node.Rotation = Math.Round(Math.Atan2(-b, a) * 180.0 / Math.PI, 3);
    }

    private static void ApplyOverrideVariables(XElement element, LevelNode node)
    {
        foreach (var variable in element
            .Descendants("OverrideVariable")
            .Descendants("Variable"))
        {
            var name = (string?)variable.Attribute("Name") ?? "";
            var value = (string?)variable.Attribute("Value") ?? "";
            if (name.Length > 0) node.LibraryOverrides[name] = value;
            if (name.Equals("StuntName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                node.OverrideStuntName = value;
                if (node.Name.Equals("Stunt", StringComparison.OrdinalIgnoreCase))
                {
                    node.ClassName = value;
                    node.Variant = string.IsNullOrWhiteSpace(node.Variant) ? "Stunt" : node.Variant;
                }
            }
        }
    }

    private static void ApplyDynamicMetadata(XElement element, LevelNode node)
    {
        node.DynamicTimelineJson = (string?)element.Attribute("EditorDynamicTimeline") ?? "";
        var dynamics = element.Element("Properties")?.Elements("Dynamic").ToList() ?? [];
        if (dynamics.Count > 0)
        {
            node.DynamicXml = string.Join(Environment.NewLine, dynamics.Select(dynamic => dynamic.ToString()));
        }

        var dynamicLoop = element.Element("Content")?.Elements("Loop")
            .FirstOrDefault(loop => loop.Descendants("Transform").Any());
        if (dynamicLoop is not null)
        {
            node.DynamicTriggerXml = dynamicLoop.ToString();
        }
    }

    private static LevelNodeKind KindFor(string tag) => tag switch
    {
        "Image" => LevelNodeKind.Image,
        "Platform" => LevelNodeKind.Platform,
        "Trapezoid" => LevelNodeKind.Trapezoid,
        "Trigger" => LevelNodeKind.Trigger,
        "Area" => LevelNodeKind.Area,
        "In" => LevelNodeKind.GateIn,
        "Out" => LevelNodeKind.GateOut,
        "ObjectReference" => LevelNodeKind.ObjectReference,
        "Camera" => LevelNodeKind.Camera,
        "Waypoint" or "Spawn" => LevelNodeKind.Waypoint,
        "Dynamic" or "DynamicTrigger" => LevelNodeKind.Dynamic,
        _ => LevelNodeKind.Object
    };

    private static string DefaultTag(LevelNodeKind kind) => kind switch
    {
        LevelNodeKind.Platform => "Platform",
        LevelNodeKind.Trapezoid => "Trapezoid",
        LevelNodeKind.Trigger => "Trigger",
        LevelNodeKind.Area => "Area",
        LevelNodeKind.GateIn => "In",
        LevelNodeKind.GateOut => "Out",
        LevelNodeKind.Image => "Image",
        LevelNodeKind.ObjectReference => "ObjectReference",
        LevelNodeKind.Waypoint => "Waypoint",
        _ => "Object"
    };

    private static int DefaultWidth(string tag) => tag switch
    {
        "In" or "Out" => 72,
        "ObjectReference" or "Object" => 100,
        "Image" or "UnityModel" => 120,
        _ => 100
    };

    private static int DefaultHeight(string tag) => tag switch
    {
        "In" or "Out" => 72,
        "ObjectReference" or "Object" => 100,
        "Image" or "UnityModel" => 120,
        _ => 100
    };

    private static int IntAttr(XElement element, string name, int fallback = 0)
        => int.TryParse((string?)element.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double DoubleAttr(XElement element, string name, double fallback = 0)
    {
        var raw = ((string?)element.Attribute(name))?.Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
