using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public sealed record SwarmConnection(string Name, string Delay, string SpeedDelta)
{
    public string Display => Name +
        (Delay.Length > 0 ? $"  delay {Delay}" : "") +
        (SpeedDelta.Length > 0 ? $"  speed {SpeedDelta}" : "");
}

public static class SwarmDesignerService
{
    private static readonly string[] PieceNames = ["SwarmActivator", "SwarmHole", "SwarmDeactivator"];

    public static IReadOnlyList<LevelNode> Waypoints(LevelDocument document) =>
        document.Nodes.SelectMany(node => node.Flatten())
            .Where(node => node.Kind == LevelNodeKind.Waypoint &&
                node.SourceAttributes.GetValueOrDefault("EditorElement") != "Spawn")
            .ToList();

    public static IReadOnlyList<LevelNode> References(LevelDocument document, string name) =>
        document.Nodes.SelectMany(node => node.Flatten())
            .Where(node => node.Kind == LevelNodeKind.ObjectReference &&
                node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

    public static LevelNode AddReference(LevelDocument document, string name, int x = 300, int y = -500)
    {
        if (!PieceNames.Contains(name)) throw new ArgumentOutOfRangeException(nameof(name));
        var file = name == "SwarmHole" ? "traps.xml" : "triggers.xml";
        var node = new LevelNode
        {
            Kind = LevelNodeKind.ObjectReference, Name = name, ClassName = name,
            Filename = file, Choice = file, Template = "ObjectReference", Tag = "ObjectReference",
            Factor = "1", X = x, Y = y, Width = 100, Height = 100
        };
        Vector2LibraryObjectBuilder.TryReconstructReference(node, document.SourcePath);
        Append(document, node);
        return node;
    }

    public static LevelNode AddWaypoint(LevelDocument document, string baseName = "Waypoint", int x = 500, int y = -300)
    {
        var name = UniqueName(baseName, Waypoints(document).Select(node => node.Name));
        var node = new LevelNode
        {
            Kind = LevelNodeKind.Waypoint, Name = name, Tag = "Waypoint",
            Template = "Waypoint", Choice = "Swarm", Variant = "Default",
            Factor = "1", X = x, Y = y, Width = 80, Height = 80
        };
        node.SourceAttributes["SpawnX"] = "0";
        node.SourceAttributes["SpawnY"] = "0";
        node.SourceAttributes["SpawnDelay"] = "0";
        if (baseName == "Start") node.SourceAttributes["Type"] = "Start";
        node.SourcePropertiesXml = new XElement("Properties", new XElement("Static", new XElement("Next")))
            .ToString(SaveOptions.DisableFormatting);
        Append(document, node);
        return node;
    }

    public static LevelNode CreateBasic(LevelDocument document, string swarmName)
    {
        var start = AddWaypoint(document, "Start", 300, -300);
        var end = AddWaypoint(document, "End", 1000, -300);
        SetConnections(start, [new SwarmConnection(end.Name, "", "")]);
        SetConnections(end, [new SwarmConnection("next_room", "", "")]);
        var activator = AddReference(document, "SwarmActivator", 180, -520);
        activator.LibraryOverrides["SpawnPoint"] = start.Name;
        activator.SourceAttributes["SwarmName"] = swarmName;
        AddReference(document, "SwarmHole", 300, 0);
        document.SelectedNode = start;
        return start;
    }

    public static IReadOnlyList<SwarmConnection> Connections(LevelNode waypoint)
    {
        var next = Properties(waypoint).Element("Static")?.Element("Next");
        return next?.Elements("Waypoint").Select(element =>
            new SwarmConnection((string?)element.Attribute("Name") ?? "",
                (string?)element.Attribute("Delay") ?? "",
                (string?)element.Attribute("SpeedDelta") ?? ""))
            .Where(connection => connection.Name.Length > 0).ToList() ?? [];
    }

    public static void SetConnections(LevelNode waypoint, IEnumerable<SwarmConnection> connections)
    {
        var properties = Properties(waypoint);
        var statics = Static(properties);
        var next = statics.Element("Next") ?? new XElement("Next");
        if (next.Parent is null) statics.AddFirst(next);
        next.RemoveNodes();
        foreach (var connection in connections)
        {
            var element = new XElement("Waypoint", new XAttribute("Name", connection.Name));
            if (connection.Delay.Length > 0) element.SetAttributeValue("Delay", connection.Delay);
            if (connection.SpeedDelta.Length > 0) element.SetAttributeValue("SpeedDelta", connection.SpeedDelta);
            next.Add(element);
        }
        waypoint.SourcePropertiesXml = properties.ToString(SaveOptions.DisableFormatting);
    }

    public static string Motion(LevelNode waypoint, string key) =>
        (string?)Properties(waypoint).Element("Static")?.Element("Motion")?.Attribute(key) ?? "";

    public static void SetMotion(LevelNode waypoint, string key, string value)
    {
        if (key is not ("Speed" or "StartAccFrames" or "StopAccFrames"))
            throw new ArgumentOutOfRangeException(nameof(key));
        var properties = Properties(waypoint);
        var statics = Static(properties);
        var motion = statics.Element("Motion") ?? new XElement("Motion");
        if (motion.Parent is null) statics.Add(motion);
        motion.SetAttributeValue(key, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        waypoint.SourcePropertiesXml = properties.ToString(SaveOptions.DisableFormatting);
    }

    public static void RenameWaypoint(LevelDocument document, LevelNode waypoint, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidDataException("Give this waypoint a name.");
        if (Waypoints(document).Any(node => node.Id != waypoint.Id && node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"A waypoint named {name} already exists.");
        var old = waypoint.Name;
        waypoint.Name = name;
        foreach (var node in Waypoints(document))
        {
            var connections = Connections(node);
            if (!connections.Any(connection => connection.Name == old)) continue;
            SetConnections(node, connections.Select(connection =>
                connection.Name == old ? connection with { Name = name } : connection));
        }
        foreach (var activator in References(document, "SwarmActivator"))
            if (activator.LibraryOverrides.GetValueOrDefault("SpawnPoint") == old)
                activator.LibraryOverrides["SpawnPoint"] = name;
    }

    public static IReadOnlyList<string> Validate(LevelDocument document)
    {
        var issues = new List<string>();
        if (References(document, "SwarmActivator").Count == 0) issues.Add("Missing SwarmActivator");
        if (References(document, "SwarmHole").Count == 0) issues.Add("Missing SwarmHole");
        var waypoints = Waypoints(document);
        var starts = waypoints.Where(node => node.Name == "Start" ||
            node.SourceAttributes.GetValueOrDefault("Type") == "Start").ToList();
        if (starts.Count == 0) issues.Add("Missing Start waypoint");
        if (starts.Count > 1) issues.Add("More than one Start waypoint");
        var duplicates = waypoints.GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length > 0 && group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0) issues.Add("Duplicate waypoint names: " + string.Join(", ", duplicates));
        var names = waypoints.Select(node => node.Name).Append("next_room").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var waypoint in waypoints)
            foreach (var connection in Connections(waypoint))
                if (!names.Contains(connection.Name))
                    issues.Add($"{waypoint.Name} links to missing {connection.Name}");
        if (starts.FirstOrDefault() is { } start && Connections(start).Count == 0)
            issues.Add("Start has no outgoing path");
        return issues;
    }

    private static string UniqueName(string basis, IEnumerable<string> names)
    {
        var used = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(basis)) return basis;
        var number = 2;
        while (used.Contains(basis + number)) number++;
        return basis + number;
    }

    private static void Append(LevelDocument document, LevelNode node)
    {
        var factor = document.Nodes.SelectMany(root => root.Flatten())
            .FirstOrDefault(candidate => candidate.Kind == LevelNodeKind.Factor);
        if (factor is null)
        {
            var track = new LevelNode { Name = "Track", Kind = LevelNodeKind.Track, Factor = "1" };
            factor = new LevelNode { Name = "Object Factor = 1", Kind = LevelNodeKind.Factor, Factor = "1" };
            track.Children.Add(factor);
            document.Nodes.Add(track);
        }
        factor.Children.Add(node);
    }

    private static XElement Properties(LevelNode waypoint) =>
        string.IsNullOrWhiteSpace(waypoint.SourcePropertiesXml)
            ? new XElement("Properties", new XElement("Static", new XElement("Next")))
            : XElement.Parse(waypoint.SourcePropertiesXml);

    private static XElement Static(XElement properties)
    {
        var statics = properties.Element("Static") ?? new XElement("Static");
        if (statics.Parent is null) properties.Add(statics);
        return statics;
    }
}
