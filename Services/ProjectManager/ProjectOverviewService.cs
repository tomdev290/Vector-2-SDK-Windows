using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class ProjectOverviewService
{
    public static IReadOnlyList<ProjectOverviewZone> LoadZones(string root)
    {
        var folder = Path.Combine(root, "custom_zones");
        if (!Directory.Exists(folder)) return [];
        var result = new List<ProjectOverviewZone>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var zone in XDocument.Load(file).Descendants("Zone").Where(node => node.Parent?.Name.LocalName == "Zones"))
                {
                    var id = (string?)zone.Attribute("Id") ?? "";
                    if (id.Length == 0) continue;
                    var rooms = ProjectManifestService.SafeCombine(root, (string?)zone.Attribute("RoomsPath") ?? "custom_rooms/" + id);
                    var artwork = (string?)zone.Attribute("Artwork") ?? "";
                    var art = new[] { "custom_textures", "custom_backgrounds" }.Select(name => Path.Combine(root, name)).Where(Directory.Exists)
                        .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        .FirstOrDefault(path => Path.GetFileName(path).Equals(artwork, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(path).Equals(artwork, StringComparison.OrdinalIgnoreCase));
                    result.Add(new((string?)zone.Attribute("Name") ?? id,
                        Directory.Exists(rooms) ? Directory.EnumerateFiles(rooms, "*.xml", SearchOption.AllDirectories).Count(path => !path.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase)) : 0, rooms, art));
                }
            }
            catch (Exception error) when (error is System.Xml.XmlException or IOException or InvalidDataException) { Diagnostics.DiagnosticsLog.Warn("Overview skipped " + file + ": " + error.Message); }
        }
        return result.OrderBy(zone => zone.Name).Take(6).ToArray();
    }
}
