using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class StructuralRoomService
{
    private static readonly string LastProjectFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vector2LevelEditor", "last-project.txt");

    public static LevelDocument Create(StructuralRoomKind kind)
    {
        var document = new LevelDocument
        {
            Name = kind == StructuralRoomKind.Entrance ? "custom_start_room.xml" : "custom_finish_room.xml",
            StructuralRoomKind = kind
        };
        var track = new LevelNode { Name = "Track", Kind = LevelNodeKind.Track, Factor = "1", Template = "Track" };
        var factor = new LevelNode { Name = "Object Factor = 1", Kind = LevelNodeKind.Factor, Factor = "1", Template = "Object" };
        track.Children.Add(factor);
        document.Nodes.Add(track);
        factor.Children.Add(new LevelNode { Name = "In", Kind = LevelNodeKind.GateIn, Tag = "In", Factor = "1",
            X = 0, Y = 0, Width = 72, Height = 72 });
        factor.Children.Add(new LevelNode { Name = "Out", Kind = LevelNodeKind.GateOut, Tag = "Out", Factor = "1",
            X = 2000, Y = 0, Width = 72, Height = 72 });
        factor.Children.Add(new LevelNode { Name = "Platform", Kind = LevelNodeKind.Platform, Tag = "Platform",
            Factor = "1", X = 0, Y = 36, Width = 2050, Height = 860 });
        if (kind == StructuralRoomKind.Entrance)
        {
            var spawn = new LevelNode { Name = "DefaultSpawn", Kind = LevelNodeKind.Waypoint, Tag = "Waypoint",
                Factor = "1", X = 120, Y = -124, Width = 72, Height = 72 };
            spawn.SourceAttributes["EditorElement"] = "Spawn";
            spawn.SourceAttributes["Animation"] = "JumpOff|18";
            factor.Children.Add(spawn);
            factor.Children.Add(new LevelNode { Name = "", Kind = LevelNodeKind.Camera, Factor = "1",
                X = 920, Y = -44, Width = 72, Height = 72 });
            factor.Children.Add(Reference("CameraStart", 680, -354));
        }
        else
        {
            factor.Children.Add(Reference("PauseTimer", 1450, -120));
            factor.Children.Add(Reference("CtrlOut", 1580, -120));
            factor.Children.Add(Reference("Victory", 1720, -120));
        }
        document.SelectedNode = factor.Children.First();
        return document;
    }

    public static IReadOnlyList<string> Validate(LevelDocument document)
    {
        if (document.StructuralRoomKind is null) return [];
        var issues = new List<string>();
        var factor = document.Nodes.SelectMany(node => node.Flatten())
            .FirstOrDefault(node => node.Kind == LevelNodeKind.Factor);
        var top = factor?.Children.ToList() ?? [];
        if (top.Count(node => node.Kind == LevelNodeKind.GateIn) != 1 ||
            top.Count(node => node.Kind == LevelNodeKind.GateOut) != 1)
            issues.Add("Keep exactly one In and one Out at the top level.");
        if (document.Nodes.SelectMany(node => node.Flatten())
            .Count(node => node.Kind is LevelNodeKind.GateIn or LevelNodeKind.GateOut) != 2)
            issues.Add("An In or Out is nested inside another object.");
        if (document.StructuralRoomKind == StructuralRoomKind.Entrance &&
            document.Nodes.SelectMany(node => node.Flatten()).Count(node =>
                node.Kind == LevelNodeKind.Waypoint && node.Name == "DefaultSpawn" &&
                node.SourceAttributes.GetValueOrDefault("EditorElement") == "Spawn") != 1)
            issues.Add("The entrance needs exactly one DefaultSpawn.");
        if (!document.Nodes.SelectMany(node => node.Flatten())
            .Any(node => node.Kind is LevelNodeKind.Platform or LevelNodeKind.Trapezoid))
            issues.Add("Add a floor platform before saving.");
        return issues;
    }

    public static string CurrentProjectRoot()
    {
        var path = File.Exists(LastProjectFile) ? File.ReadAllText(LastProjectFile).Trim() : "";
        return Directory.Exists(path) && File.Exists(Path.Combine(path, "project.xml")) ? path : "";
    }

    public static IReadOnlyList<StructuralZone> LoadZones(string projectRoot)
    {
        if (projectRoot.Length == 0) return [];
        var folder = Path.Combine(projectRoot, "custom_zones");
        if (!Directory.Exists(folder)) return [];
        var zones = new List<StructuralZone>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var element in XDocument.Load(file).Descendants("Zone"))
                {
                    var id = (string?)element.Attribute("Id") ?? "";
                    if (id.Length == 0) continue;
                    var rawPath = (string?)element.Attribute("RoomsPath") ?? "";
                    var relative = rawPath.Length == 0 ? $"custom_rooms/{id}" : rawPath.Replace('\\', '/');
                    var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !parts[0].Equals("custom_rooms", StringComparison.OrdinalIgnoreCase) ||
                        parts.Any(part => part is "." or "..")) continue;
                    zones.Add(new StructuralZone(id, (string?)element.Attribute("Name") ?? id,
                        Path.Combine(parts)));
                }
            }
            catch (Exception) { }
        }
        return zones.DistinctBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(zone => zone.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string SaveToZone(LevelDocument document, string projectRoot, StructuralZone zone)
    {
        if (document.StructuralRoomKind is not { } kind)
            throw new InvalidOperationException("Open Entrance or Exit Designer first.");
        var issues = Validate(document);
        if (issues.Count > 0) throw new InvalidDataException(string.Join(" ", issues));
        var folderName = kind == StructuralRoomKind.Entrance ? "start_rooms" : "finish_rooms";
        var folder = Path.GetFullPath(Path.Combine(projectRoot, zone.RoomsPath, folderName));
        var roomsRoot = Path.GetFullPath(Path.Combine(projectRoot, "custom_rooms"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(roomsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected zone has an invalid room path.");
        var filename = Path.GetFileName(document.Name);
        if (!filename.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) filename += ".xml";
        if (filename is "" or ".xml") throw new InvalidDataException("Give this room a filename.");
        var target = Path.GetFullPath(Path.Combine(folder, filename));
        if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The room filename must stay inside the zone.");
        Directory.CreateDirectory(folder);
        var xml = new Exporter().Export(document);
        if (File.Exists(target))
        {
            var backupFolder = Path.Combine(projectRoot, ".backups", "structural_rooms", zone.Id, folderName);
            Directory.CreateDirectory(backupFolder);
            var backup = Path.Combine(backupFolder, filename + ".backup_" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff") + "_" + Guid.NewGuid().ToString("N"));
            File.Copy(target, backup, false);
        }
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, xml);
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        document.Name = filename;
        document.SourcePath = target;
        return target;
    }

    private static LevelNode Reference(string name, int x, int y)
    {
        var node = new LevelNode { Name = name, Kind = LevelNodeKind.ObjectReference,
            ClassName = name, Filename = "triggers.xml", Choice = "triggers.xml",
            Template = "LibraryObject", Variant = "Resolved", Tag = "ObjectReference",
            Factor = "1", X = x, Y = y, Width = 100, Height = 100 };
        Vector2LibraryObjectBuilder.TryReconstructReference(node);
        return node;
    }
}
