using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class CustomModelCatalogService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vector2LevelEditor");
    private static readonly string SelectedFolderFile = Path.Combine(SettingsFolder, "custom-models-folder.txt");
    private static readonly string LastProjectFile = Path.Combine(SettingsFolder, "last-project.txt");

    public static string DefaultDirectory()
    {
        if (File.Exists(LastProjectFile))
        {
            var project = File.ReadAllText(LastProjectFile).Trim();
            var projectModels = Path.Combine(project, "custom_models");
            if (Directory.Exists(projectModels)) return projectModels;
        }
        if (File.Exists(SelectedFolderFile))
        {
            var selected = File.ReadAllText(SelectedFolderFile).Trim();
            if (Directory.Exists(selected)) return selected;
        }
        return "";
    }

    public static void RememberDirectory(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(SelectedFolderFile, Path.GetFullPath(directory));
    }
    public static IReadOnlyList<CustomModelItem> Load(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var models = new List<CustomModelItem>();
        foreach (var manifestPath in Directory.EnumerateFiles(directory, "manifest.xml", SearchOption.AllDirectories))
        {
            try
            {
                var root = XDocument.Load(manifestPath).Root;
                if (root?.Name != "CustomModel") continue;
                var id = (string?)root.Attribute("ID") ?? "";
                var filename = (string?)root.Attribute("FileName") ?? "";
                if (id.Length == 0 || filename.Length == 0) continue;
                var package = Path.GetDirectoryName(manifestPath)!;
                var model = Path.GetFullPath(Path.Combine(package, filename));
                if (!model.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(model)) continue;
                models.Add(new CustomModelItem(id, (string?)root.Attribute("Name") ?? id,
                    (string?)root.Attribute("Category") ?? "Accessory",
                    (string?)root.Attribute("Author") ?? "Unknown", model, package));
            }
            catch (Exception) { }
        }
        return models.DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static CustomModelItem Import(string directory, string source, string name, string category, string author)
    {
        var xml = XDocument.Load(source);
        if (xml.Root?.Name != "Scene" || xml.Root.Element("Nodes")?.Elements().Any() != true)
            throw new InvalidDataException("Model XML must contain a Scene with nodes.");
        var id = new string(name.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (id.Length == 0) throw new InvalidDataException("Give this model a name.");
        var package = Path.GetFullPath(Path.Combine(directory, id));
        if (!package.StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model package must stay inside custom_models.");
        if (Directory.Exists(package)) throw new IOException($"A model named {name} is already installed.");
        Directory.CreateDirectory(package);
        try
        {
            var modelPath = Path.Combine(package, "model.xml");
            xml.Save(modelPath);
            new XDocument(new XElement("CustomModel",
                new XAttribute("ID", id), new XAttribute("Name", name.Trim()),
                new XAttribute("Category", category), new XAttribute("Author", string.IsNullOrWhiteSpace(author) ? "Unknown" : author.Trim()),
                new XAttribute("FileName", "model.xml"), new XAttribute("Skeleton", "VectorHuman46")))
                .Save(Path.Combine(package, "manifest.xml"));
            return new CustomModelItem(id, name.Trim(), category,
                string.IsNullOrWhiteSpace(author) ? "Unknown" : author.Trim(), modelPath, package);
        }
        catch
        {
            Directory.Delete(package, true);
            throw;
        }
    }
}
