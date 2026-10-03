using System.IO.Compression;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record ObstacleArchiveInfo(string Id, string Name, int TextureCount, byte[]? Preview);

public static class ObstacleArchiveService
{
    public static ObstacleArchiveInfo Read(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return Inspect(archive).Info;
    }

    public static string Import(string archivePath, string projectRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var (info, packageRoot, manifest) = Inspect(archive);
        var obstacleFolder = Path.Combine(projectRoot, "custom_obstacles");
        var textureFolder = Path.Combine(projectRoot, "custom_textures");
        var target = Path.Combine(obstacleFolder, info.Id + ".v2obstacle");
        if (File.Exists(target)) throw new IOException("An obstacle package with this ID already exists.");
        var textures = new List<(string Name, byte[] Data)>();
        foreach (var texture in manifest.Element("Textures")?.Elements("Texture") ?? [])
        {
            var name = (string?)texture.Attribute("File") ?? "";
            if (!SafeName(name)) throw new InvalidDataException("The obstacle package has an unsafe texture path.");
            var entry = FindEntry(archive, packageRoot + "textures/" + name)
                ?? throw new InvalidDataException("The obstacle package is missing texture " + name + ".");
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var data = buffer.ToArray();
            var existing = Path.Combine(textureFolder, name);
            if (File.Exists(existing) && !File.ReadAllBytes(existing).SequenceEqual(data))
                throw new IOException("A different custom texture named " + name + " is already installed.");
            textures.Add((name, data));
        }
        Directory.CreateDirectory(obstacleFolder);
        Directory.CreateDirectory(textureFolder);
        foreach (var texture in textures)
        {
            var path = Path.Combine(textureFolder, texture.Name);
            if (!File.Exists(path)) File.WriteAllBytes(path, texture.Data);
        }
        var temporary = target + ".tmp";
        File.Copy(archivePath, temporary, true);
        File.Move(temporary, target, true);
        return target;
    }

    private static (ObstacleArchiveInfo Info, string PackageRoot, XElement Manifest) Inspect(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Split('/').Any(segment => segment is ".." or ".") ||
                path.Contains(':') || path.Contains("//"))
                throw new InvalidDataException("The obstacle package contains an unsafe path.");
        }
        var manifests = archive.Entries.Where(entry =>
            entry.FullName.Replace('\\', '/').EndsWith("/manifest.xml", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Equals("manifest.xml", StringComparison.OrdinalIgnoreCase)).ToList();
        if (manifests.Count != 1) throw new InvalidDataException("The obstacle package needs one manifest.xml.");
        var manifestEntry = manifests[0];
        var root = manifestEntry.FullName.Replace('\\', '/');
        root = root[..^"manifest.xml".Length];
        using var manifestStream = manifestEntry.Open();
        var manifest = XDocument.Load(manifestStream).Root;
        if (manifest?.Name.LocalName != "Vector2Obstacle") throw new InvalidDataException("The obstacle manifest is not Vector2Obstacle.");
        var id = ProjectTemplateService.SafeId((string?)manifest.Attribute("Id") ?? "");
        if (id.Length == 0) throw new InvalidDataException("The obstacle manifest needs an ID.");
        var xmlName = (string?)manifest.Attribute("XML") ?? "obstacle.xml";
        if (!SafeName(xmlName)) throw new InvalidDataException("The obstacle XML path is unsafe.");
        var modelEntry = FindEntry(archive, root + xmlName)
            ?? throw new InvalidDataException("The obstacle package is missing obstacle.xml.");
        using (var modelStream = modelEntry.Open()) XDocument.Load(modelStream);
        byte[]? preview = null;
        var previewName = (string?)manifest.Attribute("Preview") ?? "";
        if (previewName.Length > 0)
        {
            if (!SafeName(previewName)) throw new InvalidDataException("The obstacle preview path is unsafe.");
            var previewEntry = FindEntry(archive, root + previewName);
            if (previewEntry is not null)
            {
                using var stream = previewEntry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                preview = buffer.ToArray();
            }
        }
        return (new ObstacleArchiveInfo(id, (string?)manifest.Attribute("Name") ?? id,
            manifest.Element("Textures")?.Elements("Texture").Count() ?? 0, preview), root, manifest);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path) =>
        archive.Entries.FirstOrDefault(entry => entry.FullName.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase));

    private static bool SafeName(string name) => name.Length > 0 && Path.GetFileName(name) == name &&
        name is not "." and not ".." && !name.Contains(':') && !name.Contains('\\') && !name.Contains('/');
}
