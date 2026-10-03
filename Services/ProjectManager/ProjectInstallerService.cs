using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed class ProjectInstallerService
{
    private const string ReceiptFolder = ".vector2-editor-projects";
    private readonly string? _storageRoot;

    public ProjectInstallerService(string? storageRoot = null) => _storageRoot = storageRoot;

    public ProjectInstallResult Install(Vector2Project project, string? gameDataPath = null)
    {
        if (!Directory.Exists(project.RootPath)) throw new DirectoryNotFoundException("The project folder is missing; installed content was not changed.");
        ValidateProgression(project.RootPath);
        var dataPath = gameDataPath ?? project.GameDataPath;
        var destination = _storageRoot ?? (Directory.Exists(dataPath)
            ? Vector2GameIntegrationLocator.ModDataRootFor(dataPath) : CustomContentService.DefaultStorageRoot);
        var current = EnumerateInstallable(project.RootPath)
            .DistinctBy(item => item.Relative, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Relative, item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var unfinishedQuests = new List<string>();
        foreach (var source in current.Values.Where(file => Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var parsed = XmlDraftParsing.Parse(File.ReadAllText(source));
                // Quest.CheckEvent requires both the start trigger and a non-null objective list.
                if (parsed.Descendants("Quest").Any(quest => quest.Element("StartTrigger")?.Element("Content") is null ||
                    !quest.Elements("Trigger").Any(trigger => trigger.Element("Content") is not null)))
                    unfinishedQuests.Add(Path.GetRelativePath(project.RootPath, source));
            }
            catch (System.Xml.XmlException error) { throw new InvalidDataException($"{Path.GetRelativePath(project.RootPath, source)} has invalid XML at line {error.LineNumber}: {error.Message}. Installed content was not changed.", error); }
        }
        foreach (var unfinished in unfinishedQuests) current.Remove(unfinished);
        var receiptDirectory = Path.Combine(destination, ReceiptFolder);
        var receiptPath = Path.Combine(receiptDirectory, SafeReceiptName(project.ProjectId) + ".json");
        var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(receiptDirectory))
            foreach (var other in Directory.EnumerateFiles(receiptDirectory, "*.json"))
                if (!other.Equals(receiptPath, StringComparison.OrdinalIgnoreCase) && !other.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                    foreach (var relative in LoadReceipt(other).Where(IsSafeInstallRelative)) shared.Add(relative);
        foreach (var (relative, source) in current)
        {
            var target = ProjectManifestService.SafeCombine(destination, relative);
            if (shared.Contains(relative) && File.Exists(target) && !SameContent(source, target))
                throw new IOException($"Another installed project owns '{relative}' with different content. Choose a unique ID or filename; installed content was not changed.");
        }
        Directory.CreateDirectory(destination);
        foreach (var zone in LoadDefinitions(Path.Combine(project.RootPath, "custom_zones"), "Zones", "Zone").Values)
        {
            var relative = ((string?)zone.Attribute("RoomsPath"))?.Trim() ?? "";
            if (relative.Length == 0) relative = "custom_rooms/" + ProjectTemplateService.SafeId((string?)zone.Attribute("Id") ?? "");
            var pool = ProjectManifestService.SafeCombine(project.RootPath, relative);
            var roomsRoot = Path.GetFullPath(Path.Combine(project.RootPath, "custom_rooms")) + Path.DirectorySeparatorChar;
            if (pool.StartsWith(roomsRoot, StringComparison.OrdinalIgnoreCase))
                ZoneRoomImportService.EnsurePool(destination, relative);
        }
        Directory.CreateDirectory(receiptDirectory);
        var previous = LoadReceipt(receiptPath);
        var removed = 0;
        foreach (var stale in previous.Except(current.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsSafeInstallRelative(stale) || shared.Contains(stale)) continue;
            var target = ProjectManifestService.SafeCombine(destination, stale);
            if (File.Exists(target)) { File.Delete(target); removed++; }
        }
        var copied = 0;
        foreach (var (relative, source) in current)
        {
            var target = ProjectManifestService.SafeCombine(destination, relative);
            if (File.Exists(target) && SameContent(source, target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary);
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            copied++;
        }
        var temp = receiptPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(current.Keys.Order(StringComparer.OrdinalIgnoreCase), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, receiptPath, true);
        foreach (var (relative, source) in current)
            if (!SameContent(source, ProjectManifestService.SafeCombine(destination, relative)))
                throw new IOException("Installed file verification failed: " + relative);
        return new(copied, removed, receiptPath, current.Count, unfinishedQuests.Count);
    }

    private static bool SameContent(string source, string target)
    {
        if (new FileInfo(source).Length != new FileInfo(target).Length) return false;
        using var sourceStream = File.OpenRead(source);
        using var targetStream = File.OpenRead(target);
        return SHA256.HashData(sourceStream).AsSpan().SequenceEqual(SHA256.HashData(targetStream));
    }

    public static void ValidateProgression(string root)
    {
        var chapters = LoadDefinitions(Path.Combine(root, "custom_chapters"), "Chapters", "Chapter");
        var zones = LoadDefinitions(Path.Combine(root, "custom_zones"), "Zones", "Zone");
        foreach (var (id, chapter) in chapters)
        {
            var floors = chapter.Elements("Floor").ToList();
            if (floors.Count is < 1 or > 12 || floors.Any(floor => !int.TryParse((string?)floor.Attribute("Number"), out var value) || value is < 1 or > 999))
                throw new InvalidDataException($"Chapter '{id}' needs 1 to 12 valid starting floors (1-999). Install cancelled; game files were not changed.");
            if (floors.Select(floor => int.Parse((string)floor.Attribute("Number")!)).Distinct().Count() != floors.Count)
                throw new InvalidDataException($"Chapter '{id}' has duplicate starting floors; the game merges duplicates. Install cancelled; installed content was not changed.");
            foreach (var reference in chapter.Elements("Zone"))
            {
                var zoneId = ((string?)reference.Attribute("Id"))?.Trim() ?? "";
                if (zoneId.Length == 0)
                    throw new InvalidDataException($"Chapter '{id}' contains an empty zone reference.");
            }
        }
        foreach (var (id, zone) in zones)
        {
            var chapterId = ((string?)zone.Attribute("Chapter"))?.Trim() ?? "";
            // References can point to built-in runtime chapters; an editor project is not a closed index.
            var rooms = ((string?)zone.Attribute("RoomsPath"))?.Trim() ?? "";
            var roomFolder = rooms.Length == 0 ? "" : ProjectManifestService.SafeCombine(root, rooms);
            // Empty pools are authoring warnings, not failures to copy a project.
        }
    }

    private static Dictionary<string, XElement> LoadDefinitions(string folder, string rootName, string itemName)
    {
        var result = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).Where(file => !IsAuthoringArtifact(Path.GetRelativePath(folder, file))))
        {
            var document = XDocument.Load(file);
            if (document.Root?.Name.LocalName != rootName)
                throw new InvalidDataException($"{Path.GetFileName(file)} needs a <{rootName}> root. Install cancelled; game files were not changed.");
            foreach (var item in document.Root.Elements(itemName))
            {
                var id = ((string?)item.Attribute("Id"))?.Trim() ?? "";
                if (id.Length == 0 || !result.TryAdd(id, item))
                    throw new InvalidDataException($"{Path.GetFileName(file)} has an empty or duplicate {itemName} ID '{id}'. Install cancelled; game files were not changed.");
            }
        }
        return result;
    }

    public static string ValidateGameDataPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Connect Vector 2 Data before installing this project.");
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        if (!Directory.Exists(full) ||
            !Directory.Exists(Path.Combine(full, "Managed")) &&
            !Directory.Exists(Path.Combine(full, "il2cpp_data")))
            throw new InvalidDataException("Choose the Vector 2_Data folder, not the game executable or project folder.");
        return full;
    }

    private static IEnumerable<(string Relative, string FullPath)> EnumerateInstallable(string root)
    {
        var installRoots = ProjectSection.All.Select(section => section.Folder).Where(folder => folder.Length > 0)
            .Concat(["custom_dialogue", "custom_characters", "custom_gamedata", "custom_economy"]).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in installRoots)
        {
            var path = ProjectManifestService.SafeCombine(root, folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (IsSafeInstallRelative(relative) && !IsAuthoringArtifact(relative)) yield return (relative, file);
            }
        }
    }

    internal static bool IsAuthoringArtifact(string relative)
    {
        var segments = relative.Replace('\\', '/').Split('/');
        var filename = segments[^1];
        return segments.Any(segment => segment.Equals(".backups", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) || segment.Equals(".svn", StringComparison.OrdinalIgnoreCase)) ||
            filename.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) || filename.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
            filename.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeInstallRelative(string relative)
    {
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(relative) || segments.Contains("..")) return false;
        var first = segments.FirstOrDefault() ?? "";
        return first.StartsWith("custom_", StringComparison.OrdinalIgnoreCase) && !first.Equals("custom_backgrounds_pool", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> LoadReceipt(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [] : []; }
        catch { return []; }
    }

    private static string SafeReceiptName(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } clean ? clean : "project";
}

public sealed record ProjectInstallResult(int Copied, int Removed, string ReceiptPath, int Verified = 0, int UnfinishedQuests = 0);
