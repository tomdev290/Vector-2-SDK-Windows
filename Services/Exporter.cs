using System.IO;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

/// <summary>
/// Writes Vector 2 XML from the Windows document model.
///
/// This file is where "looks good in the editor" becomes "the game can actually
/// load it." Keep it boring. If Windows and macOS ever disagree, macOS/Unity
/// BuildMap wins and this file should be patched to match.
/// </summary>
public sealed class Exporter
{
    public string Export(LevelDocument document)
    {
        var aiIssues = AIValidationService.Validate(document);
        if (aiIssues.Count > 0) throw new InvalidDataException(string.Join(" ", aiIssues));
        var generatedTriggers = new List<(LevelNode Node, string Content)>();
        foreach (var trigger in document.SceneNodes.Where(node => node.Kind == LevelNodeKind.Trigger &&
            !string.IsNullOrWhiteSpace(node.TriggerTarget)))
        {
            if (string.IsNullOrWhiteSpace(trigger.TriggerAction))
                throw new InvalidDataException($"Trigger '{trigger.Name}' needs a complete Target, Template, and Value before export.");
            generatedTriggers.Add((trigger, TriggerActionService.BuildContent(document, trigger).ToString(SaveOptions.DisableFormatting)));
        }
        // Generated templates follow the current actor names and spawn references, not an old inspector cache.
        foreach (var (trigger, xml) in generatedTriggers) trigger.TriggerContentXml = xml;
        DiagnosticsLog.Info($"Exporting {document.Name}: {document.Nodes.Count} root nodes, {document.SceneNodes.Count()} visible nodes.");

        // The macOS editor exports Root -> Track -> Content -> room nodes.
        // Older test snippets sometimes used Track directly, but the real editor
        // path uses this wrapper, so Windows follows it too.
        var content = new XElement("Content",
            RootSceneNodesForExport(document)
                .Where(node => ShouldExportRoomNode(document, node))
                .Select(node => ExportNode(node, null)).Where(e => e is not null)!);
        var layoutSelection = RoomLayoutService.ExportCatalogue(document);
        var originalProperties = document.Nodes.FirstOrDefault(node => node.Kind == LevelNodeKind.Track)?.SourcePropertiesXml;
        var trackProperties = string.IsNullOrWhiteSpace(originalProperties) ? null : XElement.Parse(originalProperties);
        if (layoutSelection is not null)
        {
            trackProperties ??= new XElement("Properties");
            var selection = trackProperties.Element("Selection");
            if (selection is null) trackProperties.Add(layoutSelection);
            else
            {
                foreach (var choice in layoutSelection.Elements("Choice"))
                {
                    var name = (string?)choice.Attribute("Name");
                    selection.Elements("Choice").Where(item => (string?)item.Attribute("Name") == name).Remove();
                    selection.Add(new XElement(choice));
                }
            }
        }
        var track = new XElement("Track",
            trackProperties,
            content);
        var root = new XElement("Root", track);
        if (layoutSelection is not null) root.SetAttributeValue("EditorRoomLayouts", "1");
        if (document.StructuralRoomKind is { } structuralKind)
            root.SetAttributeValue("EditorStructuralRoom", structuralKind);
        if (document.AICharacters.Count > 0 || document.AIGroups.Count > 0)
        {
            var ai = new XElement("AICharacters");
            foreach (var character in document.AICharacters)
            {
                ai.Add(new XElement("AICharacter",
                    new XAttribute("EditorID", character.Id.ToString().ToUpperInvariant()),
                    new XAttribute("Name", character.Name),
                    new XAttribute("Kind", character.Kind),
                    new XAttribute("AI", character.AIChannel),
                    new XAttribute("BirthSpawn", character.BirthSpawn),
                    new XAttribute("Time", character.StartDelay.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)),
                    new XAttribute("Skins", string.Join("|", character.SkinFiles)),
                    new XAttribute("EditorBodySkin", character.BodySkin),
                    new XAttribute("EditorChestSkin", character.ChestSkin),
                    new XAttribute("EditorHelmetSkin", character.HelmetSkin),
                    new XAttribute("EditorHairSkin", character.HairSkin),
                    new XAttribute("EditorCustomLayers", string.Join("|", character.CustomLayers)),
                    new XAttribute("SpawnOnStart", AIExportPolicy.SpawnOnStart(document, character) ? "1" : "0")));
            }
            if (document.AIGroups.Count > 0)
            {
                ai.Add(new XElement("Groups", document.AIGroups.Select(group =>
                    new XElement("Group",
                        new XAttribute("EditorID", group.Id.ToString().ToUpperInvariant()),
                        new XAttribute("Name", group.Name),
                        new XAttribute("Members", string.Join("|", group.CharacterIds
                            .OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant())))))));
            }
            root.Add(ai);
        }
        if (document.PlayerSkinFiles.Count > 0)
            root.Add(new XElement("PlayerAppearance",
                new XAttribute("Skins", string.Join("|", document.PlayerSkinFiles))));
        if (document.HasCustomBackgroundAssignment && !string.IsNullOrWhiteSpace(document.CustomBackgroundName))
        {
            root.SetAttributeValue("CustomBackground", document.CustomBackgroundName.Trim());
        }

        var xdoc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return xdoc.ToString();
    }

    public string ExportSelection(LevelNode? node)
    {
        if (node is null) return "<!-- Nothing selected -->";
        return ExportNode(node, null)?.ToString() ?? "<!-- Editor-only node -->";
    }

    private static XElement? ExportNode(LevelNode node, LevelNode? parent)
    {
        if (node.IsEditorOnly || node.IsPreviewOnly || node.Kind is LevelNodeKind.Comment or LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor)
        {
            return null;
        }

        if (ShouldCenterDynamicRotation(node))
        {
            var centered = ExportCenteredDynamicNode(node, parent);
            AddRoomLayoutSelections(centered, node);
            return centered;
        }

        if (ShouldWrapAttachedChildren(node))
        {
            var wrapper = ExportObjectWrapper(node, parent);
            AddDynamicProperties(wrapper, node);
            AddDynamicTriggerLoop(wrapper, node);
            AddRoomLayoutSelections(wrapper, node);
            return wrapper;
        }

        if (TryExportSimpleRuntimeAreaReference(node, parent, out var runtimeArea))
        {
            AddDynamicProperties(runtimeArea!, node);
            AddRoomLayoutSelections(runtimeArea!, node);
            return runtimeArea;
        }

        var element = node.Kind switch
        {
            LevelNodeKind.Image => ExportImage(node, parent),
            LevelNodeKind.Platform => ExportPlatform(node, parent),
            LevelNodeKind.Trapezoid => ExportTrapezoid(node, parent),
            LevelNodeKind.Trigger => ExportTrigger(node, parent),
            LevelNodeKind.Area => ExportArea(node, parent),
            LevelNodeKind.GateIn => Gate("In", node, parent),
            LevelNodeKind.GateOut => Gate("Out", node, parent),
            LevelNodeKind.ObjectReference or LevelNodeKind.Coin or LevelNodeKind.Camera => ExportObjectReference(node, parent),
            LevelNodeKind.Waypoint => ExportWaypoint(node, parent),
            LevelNodeKind.Object or LevelNodeKind.Dynamic => ShouldExportAsObjectReference(node) ? ExportObjectReference(node, parent) : ExportObject(node, parent),
            _ => null
        };
        if (element is not null)
        {
            // Keep extension attributes that the typed editor does not own.
            foreach (var (name, value) in node.SourceAttributes)
            {
                if (name is "Name" or "X" or "Y" or "Width" or "Height" or "Rotation" or "Factor" or
                    "ClassName" or "Filename" or "Layer" or "Tag" or "Type" or "Variant" or "Blend" or
                    "EditorElement" or "EditorAITarget" or "EditorAIAction" or "EditorAIValue") continue;
                if (element.Attribute(name) is null) element.SetAttributeValue(name, value);
            }
            AddDynamicProperties(element, node);
            AddDynamicTriggerLoop(element, node);
            AddRoomLayoutSelections(element, node);
        }
        return element;
    }

    private static void AddRoomLayoutSelections(XElement element, LevelNode node)
    {
        if (node.RoomLayoutRules.Count == 0) return;
        var properties = element.Element("Properties") ?? new XElement("Properties");
        if (properties.Parent is null) element.Add(properties);
        var statics = properties.Element("Static") ?? new XElement("Static");
        if (statics.Parent is null) properties.AddFirst(statics);
        statics.Elements("Selection")
            .Where(selection => RoomLayoutService.Match((string?)selection.Attribute("Choice")) is not null)
            .Remove();
        foreach (var rule in node.RoomLayoutRules.Distinct())
        {
            var selection = new XElement("Selection",
                new XAttribute("Choice", rule.Choice),
                new XAttribute("Variant", rule.Variant));
            if (rule.Parent.Length > 0) selection.SetAttributeValue("Parent", rule.Parent);
            statics.Add(selection);
        }
    }

    private static bool TryExportSimpleRuntimeAreaReference(LevelNode node, LevelNode? parent, out XElement? element)
    {
        element = null;
        if (node.Kind is not (LevelNodeKind.ObjectReference or LevelNodeKind.Object or LevelNodeKind.Coin) ||
            !IsTriggerLibraryReference(node) ||
            !ShouldExportAsObjectReference(node))
        {
            return false;
        }

        var runtimeShapes = node.Children
            .SelectMany(child => child.Flatten())
            .Where(child => !child.IsHidden && child.IsPreviewOnly &&
                            child.Kind is LevelNodeKind.Area or LevelNodeKind.Trigger or LevelNodeKind.Platform or LevelNodeKind.Trapezoid)
            .ToList();
        if (runtimeShapes.Count != 1 || runtimeShapes[0].Kind != LevelNodeKind.Area)
        {
            return false;
        }

        element = ExportArea(runtimeShapes[0], parent);
        return true;
    }

    private static bool IsTriggerLibraryReference(LevelNode node)
        => node.Filename.Equals("triggers.xml", StringComparison.OrdinalIgnoreCase) ||
           node.Choice.Equals("triggers.xml", StringComparison.OrdinalIgnoreCase);

    private static XElement ExportObjectWrapper(LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var wrapper = new XElement("Object",
            new XAttribute("Name", string.IsNullOrWhiteSpace(node.Name) ? node.Kind.ToString() : node.Name),
            new XAttribute("X", x),
            new XAttribute("Y", y));

        var self = ExportNodeWithoutChildrenAtOrigin(node);
        var children = AttachedExportChildren(node).Select(child => ExportNode(child, node)).Where(e => e is not null).ToArray();
        wrapper.Add(new XElement("Content", new[] { self }.Concat(children!)));
        return wrapper;
    }

    private static IEnumerable<LevelNode> AttachedExportChildren(LevelNode node)
        => node.Children.Where(child =>
            !child.IsHidden &&
            (child.IsHierarchyAttachment || (!child.IsPreviewOnly && !child.IsEditorOnly)));

    private static bool ShouldWrapAttachedChildren(LevelNode node)
    {
        if (!AttachedExportChildren(node).Any()) return false;
        if (node.Kind is LevelNodeKind.Object or LevelNodeKind.Dynamic)
        {
            return ShouldExportAsObjectReference(node);
        }
        return node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor or LevelNodeKind.Comment);
    }

    private static bool ShouldCenterDynamicRotation(LevelNode node)
        => node.Width > 0 && node.Height > 0 &&
           node.DynamicXml.Contains("<RotationInterval", StringComparison.OrdinalIgnoreCase) &&
           !node.Children.Any(child => !child.IsPreviewOnly && !child.IsEditorOnly);

    private static XElement ExportCenteredDynamicNode(LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var wrapper = new XElement("Object",
            new XAttribute("Name", string.IsNullOrWhiteSpace(node.Name) ? "DynamicPivot" : $"{node.Name}_DynamicPivot"),
            new XAttribute("EditorDynamicPivot", "1"),
            new XAttribute("EditorOriginalName", node.Name),
            new XAttribute("X", x + node.Width / 2),
            new XAttribute("Y", y + node.Height / 2));

        var local = CopyNode(node);
        local.X = -node.Width / 2;
        local.Y = -node.Height / 2;
        local.DynamicXml = "";
        local.DynamicTimelineJson = "";
        local.DynamicTriggerXml = "";
        var content = ExportNode(local, null);
        if (content is not null) wrapper.Add(new XElement("Content", content));
        AddDynamicProperties(wrapper, node);
        return wrapper;
    }

    private static XElement ExportNodeWithoutChildrenAtOrigin(LevelNode node)
    {
        var copy = CopyNode(node);
        copy.X = 0;
        copy.Y = 0;
        copy.DynamicXml = "";
        copy.DynamicTriggerXml = "";
        return ExportNode(copy, null)!;
    }

    private static LevelNode CopyNode(LevelNode node)
    {
        var copy = new LevelNode
        {
            Name = node.Name,
            Kind = node.Kind,
            X = 0,
            Y = 0,
            Width = node.Width,
            Height = node.Height,
            Rotation = node.Rotation,
            Factor = node.Factor,
            Tag = node.Tag,
            SortingLayer = node.SortingLayer,
            Template = node.Template,
            Choice = node.Choice,
            Variant = node.Variant,
            Blend = node.Blend,
            ClassName = node.ClassName,
            Filename = node.Filename,
            ImagePath = node.ImagePath,
            NativeX = node.NativeX,
            NativeY = node.NativeY,
            VisualType = node.VisualType,
            VisualDepth = node.VisualDepth,
            OverrideStuntName = node.OverrideStuntName,
            DynamicXml = node.DynamicXml,
            DynamicTimelineJson = node.DynamicTimelineJson,
            DynamicTriggerXml = node.DynamicTriggerXml
            ,TriggerTarget = node.TriggerTarget,
            TriggerAction = node.TriggerAction,
            TriggerValue = node.TriggerValue,
            TriggerContentXml = node.TriggerContentXml
        };
        return copy;
    }

    private static IEnumerable<LevelNode> RootSceneNodesForExport(LevelDocument document)
    {
        var tracks = document.Nodes.Where(n => n.Kind == LevelNodeKind.Track).ToList();
        if (tracks.Count == 0)
        {
            return document.Nodes;
        }

        return tracks.SelectMany(track => track.Children.SelectMany(child =>
            child.Kind == LevelNodeKind.Factor ? child.Children : Enumerable.Repeat(child, 1)));
    }

    private static bool ShouldExportRoomNode(LevelDocument document, LevelNode node)
    {
        if (!document.HasCustomBackgroundAssignment ||
            string.IsNullOrWhiteSpace(document.CustomBackgroundName) ||
            document.CustomBackgroundName.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return !BackgroundDesignerService.IsBackground(node);
    }

    private static XElement ExportImage(LevelNode node, LevelNode? parent)
    {
        var element = Rect("Image", node, parent);
        AddIf(element, "ClassName", ExportClassName(node.ClassName));
        AddIf(element, "Layer", node.SortingLayer);
        AddIf(element, "Factor", node.Factor);
        AddIf(element, "Tag", node.Tag);
        element.SetAttributeValue("Type", node.VisualType);
        element.SetAttributeValue("Depth", node.VisualDepth);
        element.SetAttributeValue("NativeX", NativeImageWidth(node));
        element.SetAttributeValue("NativeY", NativeImageHeight(node));

        var staticNode = new XElement("Static", Matrix(node));
        if (ShouldExportImageSelection(node))
        {
            staticNode.Add(new XElement("Selection",
                string.IsNullOrWhiteSpace(node.Choice) ? null : new XAttribute("Choice", node.Choice),
                string.IsNullOrWhiteSpace(node.Variant) ? null : new XAttribute("Variant", node.Variant)));
        }
        if (!node.Blend.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            staticNode.Add(new XElement("BlendMode", new XAttribute("Mode", node.Blend)));
        }
        element.Add(new XElement("Properties",
            staticNode));
        return element;
    }

    private static XElement ExportPlatform(LevelNode node, LevelNode? parent)
    {
        if (Math.Abs(node.Rotation) > 0.001)
        {
            return ExportRotatedPlatform(node, parent);
        }

        var element = Rect("Platform", node, parent);
        if (node.Variant.Contains("sticky", StringComparison.OrdinalIgnoreCase))
        {
            element.SetAttributeValue("Sticky", "1");
        }
        return element;
    }

    private static XElement ExportRotatedPlatform(LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var element = new XElement("Object",
            new XAttribute("Name", string.IsNullOrWhiteSpace(node.Name) ? "Platform" : node.Name),
            new XAttribute("X", x),
            new XAttribute("Y", y));

        var platform = new XElement("Platform",
            new XAttribute("X", 0),
            new XAttribute("Y", 0),
            new XAttribute("Width", node.Width),
            new XAttribute("Height", node.Height));
        if (node.Variant.Contains("sticky", StringComparison.OrdinalIgnoreCase))
        {
            platform.SetAttributeValue("Sticky", "1");
        }

        element.Add(new XElement("Content", platform));
        element.Add(new XElement("Properties",
            new XElement("Static",
                RectRotationMatrix(node.Width, node.Height, node.Rotation, scaled: false))));
        return element;
    }

    private static XElement ExportTrapezoid(LevelNode node, LevelNode? parent)
    {
        var type = TrapezoidType(node);
        // Do not add Height1 here. Vector 2's Element.CreateTrapezoid ignores it
        // and rebuilds the short side from Height/Width/Type. Adding "helpful"
        // extra trapezoid data is exactly how slopes became kaiju-sized before.
        var element = Rect("Trapezoid", node, parent);
        AddIf(element, "Name", node.Name);
        element.SetAttributeValue("Type", type);
        return element;
    }

    private static string TrapezoidType(LevelNode node)
    {
        var combined = string.Join(' ', node.Variant, node.Name, node.ImagePath)
            .Trim()
            .ToLowerInvariant();
        return combined.Contains("type2", StringComparison.Ordinal) ||
               combined.Contains("right", StringComparison.Ordinal) ||
               combined == "2"
            ? "2"
            : "1";
    }

    private static XElement ExportObjectReference(LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var stuntName = !string.IsNullOrWhiteSpace(node.OverrideStuntName) ? node.OverrideStuntName : node.Name;
        var exportsAsStunt = node.Filename == "phantoms.xml" ||
                             (!string.IsNullOrWhiteSpace(node.OverrideStuntName) &&
                              node.Filename.Equals("triggers.xml", StringComparison.OrdinalIgnoreCase) &&
                              node.Name.Equals("Stunt", StringComparison.OrdinalIgnoreCase));
        var liftPartName = LiftPartName(node);
        var element = new XElement("ObjectReference",
            new XAttribute("Name", !string.IsNullOrWhiteSpace(liftPartName) ? "Lift" : exportsAsStunt ? "Stunt" : string.IsNullOrWhiteSpace(node.Name) ? "Object" : node.Name),
            new XAttribute("X", x),
            new XAttribute("Y", y));
        AddIf(element, "Filename", exportsAsStunt ? "triggers.xml" : node.Filename);
        foreach (var (name, value) in node.SourceAttributes)
        {
            if (name is "Name" or "X" or "Y" or "Filename" or "EditorElement") continue;
            element.SetAttributeValue(name, value);
        }
        var staticChildren = new List<object>();
        if (Math.Abs(node.Rotation) > 0.001)
        {
            staticChildren.Add(ObjectReferenceMatrix(node));
        }
        if (exportsAsStunt)
        {
            staticChildren.Add(new XElement("OverrideVariable",
                new XElement("Variable",
                    new XAttribute("Name", "StuntName"),
                    new XAttribute("Type", "E_AreaTrick"),
                    new XAttribute("Value", stuntName))));
        }
        if (!string.IsNullOrWhiteSpace(liftPartName))
        {
            staticChildren.Add(new XElement("OverrideVariable",
                new XElement("Variable",
                    new XAttribute("Name", liftPartName),
                    new XAttribute("Type", "E_Bool"),
                    new XAttribute("Value", "1"))));
        }
        if (!exportsAsStunt && !string.IsNullOrWhiteSpace(node.SourcePropertiesXml))
        {
            var properties = XElement.Parse(node.SourcePropertiesXml);
            var statics = properties.Element("Static") ?? new XElement("Static");
            if (statics.Parent is null) properties.AddFirst(statics);
            statics.Elements("OverrideVariable").Remove();
            if (node.LibraryOverrides.Count > 0)
                statics.Add(new XElement("OverrideVariable", node.LibraryOverrides.OrderBy(pair => pair.Key)
                    .Select(pair => new XElement("Variable",
                        new XAttribute("Name", pair.Key), new XAttribute("Value", pair.Value)))));
            if (Math.Abs(node.Rotation) > .001)
            {
                statics.Elements("Matrix").Remove();
                statics.Add(ObjectReferenceMatrix(node));
            }
            if (!string.IsNullOrWhiteSpace(node.DynamicXml)) properties.Elements("Dynamic").Remove();
            element.Add(properties);
        }
        else
        {
            if (!exportsAsStunt && node.LibraryOverrides.Count > 0)
                staticChildren.Add(new XElement("OverrideVariable", node.LibraryOverrides.OrderBy(pair => pair.Key)
                    .Select(pair => new XElement("Variable",
                        new XAttribute("Name", pair.Key), new XAttribute("Value", pair.Value)))));
            if (staticChildren.Count > 0)
                element.Add(new XElement("Properties", new XElement("Static", staticChildren)));
        }
        return element;
    }

    private static string LiftPartName(LevelNode node)
    {
        if (!node.Filename.Equals("obstacles_moving.xml", StringComparison.OrdinalIgnoreCase)) return "";
        return node.Name switch
        {
            "Left_Lift" => "Left_Lift",
            "Right_Lift" => "Right_Lift",
            _ => ""
        };
    }

    private static XElement ObjectReferenceMatrix(LevelNode node)
    {
        var bounds = node.VisualBounds;
        var width = Math.Max(1, (int)Math.Round(bounds.Width));
        var height = Math.Max(1, (int)Math.Round(bounds.Height));
        return RectRotationMatrix(width, height, node.Rotation, scaled: false);
    }

    private static XElement ExportObject(LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var element = new XElement("Object",
            new XAttribute("Name", string.IsNullOrWhiteSpace(node.Name) ? "Object" : node.Name),
            new XAttribute("X", x),
            new XAttribute("Y", y));
        var children = node.Children.Select(child => ExportNode(child, node)).Where(e => e is not null).ToArray();
        if (children.Length > 0) element.Add(new XElement("Content", children!));
        return element;
    }

    private static XElement NamedRect(string name, LevelNode node, LevelNode? parent)
    {
        var element = Rect(name, node, parent);
        AddIf(element, "Name", node.Name);
        return element;
    }

    private static XElement Rect(string name, LevelNode node, LevelNode? parent, int? heightOverride = null)
    {
        var (x, y) = LocalPosition(node, parent);
        return new XElement(name,
            new XAttribute("X", x),
            new XAttribute("Y", y),
            new XAttribute("Width", node.Width),
            new XAttribute("Height", heightOverride ?? node.Height));
    }

    private static XElement Rect(string name, LevelNode node, int? heightOverride = null)
        => new(name,
            new XAttribute("X", node.X),
            new XAttribute("Y", node.Y),
            new XAttribute("Width", node.Width),
            new XAttribute("Height", heightOverride ?? node.Height));

    private static XElement Matrix(LevelNode node)
    {
        // Same math as Swift's image matrix export. The editor is Y-down, Unity
        // matrix data has its own rotation expectations, so the negative angle
        // here is intentional. If image rotations look mirrored, check this first.
        var radians = -node.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var width = (double)node.Width;
        var height = (double)node.Height;
        var a = width * cos;
        var b = -width * sin;
        var c = height * sin;
        var d = node.Height * cos;
        var tx = width / 2.0 - ((a * 0.5) + (c * 0.5));
        var ty = height / 2.0 - ((b * 0.5) + (d * 0.5));

        return new XElement("Matrix",
            new XAttribute("A", Format(a)),
            new XAttribute("B", Format(b)),
            new XAttribute("C", Format(c)),
            new XAttribute("D", Format(d)),
            new XAttribute("Tx", Format(tx)),
            new XAttribute("Ty", Format(ty)));
    }

    private static XElement RectRotationMatrix(int width, int height, double rotation, bool scaled)
    {
        var radians = -rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var widthValue = (double)width;
        var heightValue = (double)height;
        var scaleWidth = scaled ? widthValue : 1.0;
        var scaleHeight = scaled ? heightValue : 1.0;
        var a = scaleWidth * cos;
        var b = -scaleWidth * sin;
        var c = scaleHeight * sin;
        var d = scaleHeight * cos;
        var tx = scaled
            ? widthValue / 2.0 - ((a * 0.5) + (c * 0.5))
            : widthValue / 2.0 - ((a * widthValue / 2.0) + (c * heightValue / 2.0));
        var ty = scaled
            ? heightValue / 2.0 - ((b * 0.5) + (d * 0.5))
            : heightValue / 2.0 - ((b * widthValue / 2.0) + (d * heightValue / 2.0));

        return new XElement("Matrix",
            new XAttribute("A", Format(a)),
            new XAttribute("B", Format(b)),
            new XAttribute("C", Format(c)),
            new XAttribute("D", Format(d)),
            new XAttribute("Tx", Format(tx)),
            new XAttribute("Ty", Format(ty)));
    }

    private static XElement Gate(string name, LevelNode node, LevelNode? parent)
    {
        var (x, y) = LocalPosition(node, parent);
        var element = new XElement(name,
            new XAttribute("X", x),
            new XAttribute("Y", y));
        AddIf(element, "Name", node.Name);
        return element;
    }

    private static XElement ExportWaypoint(LevelNode node, LevelNode? parent)
    {
        var spawn = node.SourceAttributes.GetValueOrDefault("EditorElement") == "Spawn";
        var element = NamedRect(spawn ? "Spawn" : "Waypoint", node, parent);
        if (spawn)
        {
            if (node.SourceAttributes.TryGetValue("Animation", out var animation))
                element.SetAttributeValue("Animation", animation);
            return element;
        }
        foreach (var (name, value) in node.SourceAttributes)
        {
            if (name is "Name" or "X" or "Y" or "Width" or "Height" or "EditorElement") continue;
            element.SetAttributeValue(name, value);
        }
        element.SetAttributeValue("SpawnX", node.SourceAttributes.GetValueOrDefault("SpawnX", "0"));
        element.SetAttributeValue("SpawnY", node.SourceAttributes.GetValueOrDefault("SpawnY", "0"));
        element.SetAttributeValue("SpawnDelay", node.SourceAttributes.GetValueOrDefault("SpawnDelay", "0"));
        if (element.Attribute("Type") is null && node.Name.Equals("Start", StringComparison.OrdinalIgnoreCase))
            element.SetAttributeValue("Type", "Start");
        var properties = string.IsNullOrWhiteSpace(node.SourcePropertiesXml)
            ? new XElement("Properties", new XElement("Static", new XElement("Next")))
            : XElement.Parse(node.SourcePropertiesXml);
        if (!string.IsNullOrWhiteSpace(node.DynamicXml)) properties.Elements("Dynamic").Remove();
        element.Add(properties);
        return element;
    }

    private static XElement ExportArea(LevelNode node, LevelNode? parent)
    {
        var element = NamedRect("Area", node, parent);
        var areaName = node.Name.Trim();
        element.SetAttributeValue("Name", areaName.Length == 0 || areaName.Equals("Area", StringComparison.OrdinalIgnoreCase) ? "RunInhibition" : areaName);
        element.SetAttributeValue("Type", "Animation");
        return element;
    }

    private static (int x, int y) LocalPosition(LevelNode node, LevelNode? parent)
        => parent is null ? (node.X, node.Y) : (node.X - parent.X, node.Y - parent.Y);

    private static XElement ExportTrigger(LevelNode node, LevelNode? parent)
    {
        var element = NamedRect("Trigger", node, parent);
        if (!string.IsNullOrWhiteSpace(node.TriggerTarget))
        {
            element.SetAttributeValue("EditorAITarget", node.TriggerTarget);
            element.SetAttributeValue("EditorAIAction", node.TriggerAction);
            element.SetAttributeValue("EditorAIValue", node.TriggerValue);
        }
        if (!string.IsNullOrWhiteSpace(node.TriggerContentXml))
        {
            var content = XElement.Parse(node.TriggerContentXml);
            if (content.Elements("Trigger").Any())
            {
                var (x, y) = LocalPosition(node, parent);
                return new XElement("Object", new XAttribute("Name", node.Name),
                    new XAttribute("EditorAITarget", node.TriggerTarget),
                    new XAttribute("EditorAIAction", node.TriggerAction),
                    new XAttribute("EditorAIValue", node.TriggerValue),
                    new XAttribute("X", x), new XAttribute("Y", y), content);
            }
            element.Add(content);
            return element;
        }
        element.Add(new XElement("Content",
            new XElement("Init",
                new XElement("SetVariable", new XAttribute("Name", "$AI"), new XAttribute("Type", "AI"), new XAttribute("Value", "0")),
                new XElement("SetVariable", new XAttribute("Name", "$Active"), new XAttribute("Type", "Bool"), new XAttribute("Value", "1")),
                new XElement("SetVariable", new XAttribute("Name", "$Node"), new XAttribute("Type", "Node"), new XAttribute("Value", "COM")))));
        return element;
    }

    private static void AddDynamicProperties(XElement element, LevelNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.DynamicTimelineJson))
            element.SetAttributeValue("EditorDynamicTimeline", node.DynamicTimelineJson);
        if (string.IsNullOrWhiteSpace(node.DynamicXml)) return;
        if (!string.IsNullOrWhiteSpace(node.Name) && element.Attribute("Name") is null)
            element.SetAttributeValue("Name", node.Name);
        try
        {
            var parsed = XElement.Parse(node.DynamicXml.Trim());
            var dynamic = parsed.Name.LocalName == "Dynamic" ? parsed : parsed.Element("Dynamic");
            if (dynamic is null) return;
            var properties = element.Element("Properties");
            if (properties is null)
            {
                properties = new XElement("Properties");
                element.Add(properties);
            }
            properties.Elements("Dynamic").Remove();
            properties.Add(new XElement(dynamic));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Dynamic XML for '{node.Name}' was not exported: {ex.Message}");
        }
    }

    private static void AddDynamicTriggerLoop(XElement element, LevelNode node)
    {
        if (string.IsNullOrWhiteSpace(node.DynamicTriggerXml)) return;
        try
        {
            var loop = XElement.Parse(node.DynamicTriggerXml.Trim());
            var content = element.Element("Content");
            if (content is null)
            {
                content = new XElement("Content");
                element.AddFirst(content);
            }
            content.Elements("Loop")
                .Where(existing => string.Equals((string?)existing.Attribute("Name"), (string?)loop.Attribute("Name"), StringComparison.OrdinalIgnoreCase))
                .Remove();
            content.Add(new XElement(loop));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Dynamic trigger XML for '{node.Name}' was not exported: {ex.Message}");
        }
    }

    private static bool ShouldExportAsObjectReference(LevelNode node)
        => !string.IsNullOrWhiteSpace(node.Filename) &&
           (node.Template is "LibraryObject" or "EditorPrefab" or "EditorPrefabPhantom" ||
            node.Variant is "Resolved" or "Prefab");

    private static void AddIf(XElement element, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) element.SetAttributeValue(name, value);
    }

    private static bool ShouldExportImageSelection(LevelNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Choice)) return false;
        return !(node.Template == "Image" && node.Variant.Equals("Default", StringComparison.OrdinalIgnoreCase));
    }

    private static int NativeImageWidth(LevelNode node)
    {
        if (ExportClassName(node.ClassName).Equals("black.v_black", StringComparison.OrdinalIgnoreCase)) return 50;
        var bitmapSize = BitmapSize(node.ImagePath);
        if (bitmapSize.Width > 0) return bitmapSize.Width;
        return node.NativeX > 0 ? node.NativeX : node.Width;
    }

    private static int NativeImageHeight(LevelNode node)
    {
        if (ExportClassName(node.ClassName).Equals("black.v_black", StringComparison.OrdinalIgnoreCase)) return 50;
        var bitmapSize = BitmapSize(node.ImagePath);
        if (bitmapSize.Height > 0) return bitmapSize.Height;
        return node.NativeY > 0 ? node.NativeY : node.Height;
    }

    private static string ExportClassName(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return "";
        var normalized = className.Trim().Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0) normalized = normalized[(slash + 1)..];

        var extension = Path.GetExtension(normalized);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^extension.Length];
        }

        var separator = normalized.LastIndexOf("__", StringComparison.Ordinal);
        if (separator >= 0 && separator + 2 < normalized.Length)
        {
            normalized = normalized[(separator + 2)..];
        }

        return normalized.Replace("__", ".").Replace("-", "_").Trim();
    }

    private static (int Width, int Height) BitmapSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (0, 0);
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault();
            return frame is null ? (0, 0) : (Math.Max(1, frame.PixelWidth), Math.Max(1, frame.PixelHeight));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static string Format(double value)
        => Math.Abs(value % 1) < 0.001 ? ((int)Math.Round(value)).ToString() : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
