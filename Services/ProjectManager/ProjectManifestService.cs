using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed class ProjectManifestService
{
    public const string ManifestName = "project.xml";

    public Vector2Project Create(string rootPath)
    {
        if (Directory.Exists(rootPath))
            throw new IOException("A folder with that project name already exists. Choose another name.");
        Directory.CreateDirectory(rootPath);
        EnsureFolders(rootPath);
        var project = new Vector2Project { RootPath = Path.GetFullPath(rootPath), Name = Path.GetFileName(rootPath) };
        Save(project);
        return project;
    }

    public Vector2Project Load(string rootPath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var manifestPath = Path.Combine(fullRoot, ManifestName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("The selected folder does not contain project.xml.", manifestPath);
        var root = XDocument.Load(manifestPath).Root;
        if (root?.Name.LocalName != "Vector2Project" || (string?)root.Attribute("SchemaVersion") != "1")
            throw new InvalidDataException("project.xml is not a supported Vector 2 project manifest.");
        var project = new Vector2Project
        {
            RootPath = fullRoot,
            ProjectId = (string?)root.Attribute("ProjectID") ?? Guid.NewGuid().ToString(),
            Name = (string?)root.Attribute("Name") ?? Path.GetFileName(fullRoot),
            Author = (string?)root.Attribute("Author") ?? "",
            Version = (string?)root.Attribute("Version") ?? "1.0.0",
            OutputFolder = (string?)root.Attribute("OutputFolder") ?? "",
            GenerateDebugData = string.Equals((string?)root.Attribute("GenerateDebugData"), "true", StringComparison.OrdinalIgnoreCase),
            GameSourcePath = (string?)root.Attribute("GameSourcePath") ?? "",
            GameDataPath = (string?)root.Attribute("GameDataPath") ?? "",
            Description = root.Element("Description")?.Value ?? "",
            CoverPath = SafeRelativePath(root.Element("Cover")?.Value ?? "")
        };
        foreach (var entry in root.Elements("ZoneArtwork"))
        {
            var folder = (string?)entry.Attribute("Folder") ?? "";
            var path = SafeRelativePath(entry.Value);
            if (folder.Length > 0 && path.Length > 0) project.ZoneArtwork.Add(new(folder, path));
        }
        EnsureFolders(fullRoot);
        return project;
    }

    public void Save(Vector2Project project)
    {
        if (string.IsNullOrWhiteSpace(project.Name)) throw new InvalidDataException("Project name cannot be empty.");
        Directory.CreateDirectory(project.RootPath);
        var path = Path.Combine(project.RootPath, ManifestName);
        XDocument document;
        try { document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Vector2Project")); }
        catch { document = new XDocument(new XElement("Vector2Project")); }
        var root = document.Root ?? new XElement("Vector2Project");
        if (document.Root is null) document.Add(root);
        root.Name = "Vector2Project";
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["SchemaVersion"] = "1", ["ProjectID"] = project.ProjectId, ["Name"] = project.Name.Trim(),
            ["Author"] = project.Author.Trim(), ["Version"] = project.Version.Trim(), ["OutputFolder"] = project.OutputFolder,
            ["GenerateDebugData"] = project.GenerateDebugData ? "true" : "false", ["GameSourcePath"] = project.GameSourcePath,
            ["GameDataPath"] = project.GameDataPath, ["LiveSync"] = "false"
        }) root.SetAttributeValue(key, value);
        root.Elements("Description").Remove();
        root.Elements("Cover").Remove();
        root.Elements("ZoneArtwork").Remove();
        root.Add(new XElement("Description", project.Description), new XElement("Cover", SafeRelativePath(project.CoverPath)));
        foreach (var item in project.ZoneArtwork.OrderBy(item => item.Folder, StringComparer.OrdinalIgnoreCase))
            root.Add(new XElement("ZoneArtwork", new XAttribute("Folder", item.Folder), SafeRelativePath(item.Path)));
        var temp = path + ".tmp";
        document.Save(temp);
        File.Move(temp, path, true);
    }

    public static void EnsureFolders(string rootPath)
    {
        var folders = ProjectSection.All.Where(section => section.Folder.Length > 0).Select(section => section.Folder)
            .Concat(["custom_dialogue", "custom_characters", "custom_gamedata\\settings", "custom_gamedata\\sounds", "custom_gamedata\\text\\localization", "custom_gamedata\\run_data\\libraries", "custom_gamedata\\run_data\\templates", "custom_tricks\\animation_overrides"])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders) Directory.CreateDirectory(SafeCombine(rootPath, folder));
    }

    public static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("The path escapes the project folder.");
        return combined;
    }

    public static string SafeRelativePath(string value)
    {
        var normalized = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Contains("..")) return "";
        return normalized;
    }
}
