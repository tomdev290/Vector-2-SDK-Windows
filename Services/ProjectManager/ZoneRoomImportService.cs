using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class ZoneRoomImportService
{
    public static string CreateRoom(string projectRoot, string relative, string name)
    {
        var id = ProjectTemplateService.SafeId(name);
        if (id.Length == 0) throw new InvalidDataException("Enter a room name.");
        var folder = EnsurePool(projectRoot, relative);
        var path = Path.Combine(folder, id + ".xml");
        var xml = new Exporter().Export(LevelDocument.Empty());
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(xml);
        return path;
    }

    public static string EnsurePool(string projectRoot, string relative)
    {
        var folder = ProjectManifestService.SafeCombine(projectRoot, relative);
        var roomsRoot = Path.GetFullPath(Path.Combine(projectRoot, "custom_rooms")) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(roomsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a zone folder inside custom_rooms, not the shared root.");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "start_rooms"));
        Directory.CreateDirectory(Path.Combine(folder, "finish_rooms"));
        return folder;
    }

    public static IReadOnlyList<string> Import(string projectRoot, string relative, IEnumerable<string> sources)
    {
        var folder = EnsurePool(projectRoot, relative);
        var plan = new List<(string Source, string Target)>();
        var replaceable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Path.GetExtension(source).Equals(".xml", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Select room XML, not a room metadata sidecar.");
            var parsed = XmlDraftParsing.Parse(File.ReadAllText(source));
            if (parsed.Root?.Name.LocalName != "Root" || parsed.Root.Element("Track") is null)
                throw new InvalidDataException($"{Path.GetFileName(source)} is not a Vector room (expected Root/Track).");
            var room = new XmlSceneParser().Load(source);
            var destination = room.StructuralRoomKind switch
            {
                StructuralRoomKind.Entrance => Path.Combine(folder, "start_rooms"),
                StructuralRoomKind.Exit => Path.Combine(folder, "finish_rooms"),
                _ => folder
            };
            var target = Path.Combine(destination, Path.GetFileName(source));
            if (Path.GetFullPath(source).Equals(target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This room is already in the selected zone.");
            var structural = room.StructuralRoomKind is StructuralRoomKind.Entrance or StructuralRoomKind.Exit;
            if ((!structural && File.Exists(target)) || !targets.Add(target))
                throw new IOException($"{Path.GetFileName(source)} already exists in this zone; existing rooms were not overwritten.");
            plan.Add((source, target));
            if (structural) replaceable.Add(target);
            var sidecar = Path.ChangeExtension(source, ".meta.xml");
            if (File.Exists(sidecar))
            {
                XmlDraftParsing.Parse(File.ReadAllText(sidecar));
                var sidecarTarget = Path.ChangeExtension(target, ".meta.xml");
                if ((!structural && File.Exists(sidecarTarget)) || !targets.Add(sidecarTarget)) throw new IOException("Room metadata already exists in this zone.");
                plan.Add((sidecar, sidecarTarget));
                if (structural) replaceable.Add(sidecarTarget);
            }
        }
        var written = new List<string>();
        var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (_, target) in plan.Where(item => replaceable.Contains(item.Target) && File.Exists(item.Target)))
            {
                var backup = ProjectManifestService.SafeCombine(projectRoot, ".backups/room_import/" + Guid.NewGuid().ToString("N") + "/" + Path.GetRelativePath(projectRoot, target));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup); backups[target] = backup;
            }
            foreach (var (source, target) in plan)
            {
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(source, temporary); File.Move(temporary, target, replaceable.Contains(target)); written.Add(target); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        catch
        {
            foreach (var target in written)
                if (backups.TryGetValue(target, out var backup)) File.Copy(backup, target, true);
                else File.Delete(target);
            throw;
        }
        return written.Where(path => !path.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
