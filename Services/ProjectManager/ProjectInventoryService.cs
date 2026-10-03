using Vector2LevelEditor.Models.ProjectManager;
using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed class ProjectInventoryService
{
    public IReadOnlyList<ProjectFileItem> Scan(Vector2Project project)
    {
        var items = new List<ProjectFileItem>();
        foreach (var section in ProjectSection.All.Where(section => section.Folder.Length > 0))
        {
            var folder = ProjectManifestService.SafeCombine(project.RootPath, section.Folder);
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var file = new FileInfo(path);
                if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                items.Add(new ProjectFileItem
                {
                    FullPath = file.FullName,
                    RelativePath = Path.GetRelativePath(project.RootPath, file.FullName),
                    Name = file.Name,
                    Extension = file.Extension.TrimStart('.').ToUpperInvariant(),
                    Section = section.Name,
                    Size = file.Length,
                    Modified = file.LastWriteTime
                });
            }
        }
        return items.OrderByDescending(item => item.Modified).ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> Import(Vector2Project project, ProjectSection section, IEnumerable<string> sources)
    {
        if (section.Folder.Length == 0) return [];
        var destination = ProjectManifestService.SafeCombine(project.RootPath, section.Folder);
        Directory.CreateDirectory(destination);
        var imported = new List<string>();
        foreach (var source in sources.Where(File.Exists))
        {
            if (section.Name == "Models")
            {
                imported.Add(ImportModelPackage(destination, source));
                continue;
            }
            var target = Path.Combine(destination, Path.GetFileName(source));
            if (File.Exists(target)) throw new IOException($"{Path.GetFileName(source)} already exists; existing project files are never overwritten.");
            File.Copy(source, target, false);
            imported.Add(target);
        }
        return imported;
    }

    private static string ImportModelPackage(string destination, string source)
    {
        var document = XDocument.Load(source);
        if (document.Root?.Name.LocalName != "Scene")
            throw new InvalidDataException($"{Path.GetFileName(source)} is not a Vector model. Its XML root must be <Scene>.");
        var id = ProjectTemplateService.SafeId(Path.GetFileNameWithoutExtension(source));
        if (id.Length == 0) throw new InvalidDataException("The model filename cannot be converted into a stable ID.");
        var package = Path.Combine(destination, id);
        if (Directory.Exists(package)) throw new IOException($"A model package named {id} already exists.");
        Directory.CreateDirectory(package);
        try
        {
            var model = Path.Combine(package, "model.xml");
            File.Copy(source, model, false);
            new XDocument(new XDeclaration("1.0", "utf-8", null),
                new XElement("CustomModel",
                    new XAttribute("ID", id), new XAttribute("Name", Path.GetFileNameWithoutExtension(source)),
                    new XAttribute("Category", "Player"), new XAttribute("Author", "Unknown"),
                    new XAttribute("FileName", "model.xml"), new XAttribute("Skeleton", "VectorHuman46")))
                .Save(Path.Combine(package, "manifest.xml"));
            return model;
        }
        catch
        {
            Directory.Delete(package, true);
            throw;
        }
    }
}
