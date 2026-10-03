using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed class ProjectValidationService
{
    private static readonly IReadOnlyDictionary<string, (string Root, string Item, string Id)> Rules =
        new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chapters"] = ("Chapters", "Chapter", "Id"), ["Zones"] = ("Zones", "Zone", "Id"),
            ["Quests"] = ("Quests", "Quest", "Name"), ["Localization"] = ("Localization", "Phrase", "Key"),
            ["Protocols"] = ("Protocols", "Protocol", "Id")
        };

    public IReadOnlyList<ProjectValidationIssue> Validate(Vector2Project project, IEnumerable<ProjectFileItem> files)
    {
        var issues = new List<ProjectValidationIssue>();
        if (string.IsNullOrWhiteSpace(project.Name)) issues.Add(new("Error", ProjectManifestService.ManifestName, "Project name is empty."));
        if (project.Version.Split('.').Any(part => !int.TryParse(part, out _))) issues.Add(new("Warning", ProjectManifestService.ManifestName, "Version is not a numeric dotted version."));
        foreach (var file in files.Where(file => file.Extension.Equals("XML", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var document = XDocument.Load(file.FullPath);
                if (document.Root is null) { issues.Add(new("Error", file.RelativePath, "XML has no root element.")); continue; }
                if (!Rules.TryGetValue(file.Section, out var rule)) continue;
                if (document.Root.Name.LocalName != rule.Root)
                {
                    issues.Add(new("Error", file.RelativePath, $"Expected <{rule.Root}> as the root element."));
                    continue;
                }
                var items = document.Root.Elements(rule.Item).ToList();
                if (items.Count == 0) issues.Add(new("Error", file.RelativePath, $"Contains no <{rule.Item}> entries."));
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var id = ((string?)item.Attribute(rule.Id))?.Trim() ?? "";
                    if (id.Length == 0) issues.Add(new("Error", file.RelativePath, $"<{rule.Item}> is missing {rule.Id}."));
                    else if (!seen.Add(id)) issues.Add(new("Error", file.RelativePath, $"Duplicate {rule.Id} '{id}'."));
                    if (file.Section == "Quests" && item.Element("Info") is null) issues.Add(new("Error", file.RelativePath, $"Quest '{id}' is missing Info."));
                    if (file.Section == "Quests" && item.Element("StartTrigger")?.Element("Content") is null) issues.Add(new("Error", file.RelativePath, $"Quest '{id}' is missing StartTrigger/Content."));
                    if (file.Section == "Quests" && !item.Elements("Trigger").Any(trigger => trigger.Element("Content") is not null))
                        issues.Add(new("Warning", file.RelativePath, $"Quest '{id}' is unfinished and will not be installed until it has an objective trigger."));
                }
            }
            catch (Exception ex) { issues.Add(new("Error", file.RelativePath, ex.Message)); }
        }
        ValidateReferences(project, files, issues);
        issues.AddRange(ProjectIntegrationAudit.Validate(project.RootPath));
        return issues.Distinct().OrderBy(issue => issue.Severity).ThenBy(issue => issue.Source).ToList();
    }

    private static void ValidateReferences(Vector2Project project, IEnumerable<ProjectFileItem> files, ICollection<ProjectValidationIssue> issues)
    {
        var chapters = Definitions(files, "Chapters", "Chapter", "Id");
        var zones = Definitions(files, "Zones", "Zone", "Id");
        var linkedChapters = files.Where(file => file.Section == "Zones" && file.Extension == "XML")
            .SelectMany(file => SafeElements(file.FullPath, "Zone"))
            .Select(zone => ((string?)zone.Attribute("Chapter"))?.Trim() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => file.Section == "Chapters" && file.Extension == "XML"))
        foreach (var chapter in SafeElements(file.FullPath, "Chapter"))
        {
            var id = ((string?)chapter.Attribute("Id"))?.Trim() ?? "";
            if (!linkedChapters.Contains(id))
                issues.Add(new("Warning", file.RelativePath, $"Chapter '{id}' has no linked zone. The game menu shows zones; create a zone with Chapter='{id}' to expose this chapter's floors."));
        }
        foreach (var file in files.Where(file => file.Section == "Chapters" && file.Extension == "XML"))
        foreach (var zone in SafeElements(file.FullPath, "Chapter").SelectMany(chapter => chapter.Elements("Zone")))
        {
            var id = ((string?)zone.Attribute("Id"))?.Trim() ?? "";
            if (id.Length > 0 && !zones.Contains(id)) issues.Add(new("Error", file.RelativePath, $"References missing zone '{id}'."));
        }
        foreach (var file in files.Where(file => file.Section == "Zones" && file.Extension == "XML"))
        foreach (var zone in SafeElements(file.FullPath, "Zone"))
        {
            var id = ((string?)zone.Attribute("Id")) ?? file.Name;
            var chapter = ((string?)zone.Attribute("Chapter"))?.Trim() ?? "";
            if (chapter.Length == 0 || !chapters.Contains(chapter)) issues.Add(new("Error", file.RelativePath, $"Zone '{id}' needs an existing chapter."));
            var rooms = ((string?)zone.Attribute("RoomsPath"))?.Trim() ?? "";
            if (rooms.Length == 0) issues.Add(new("Error", file.RelativePath, $"Zone '{id}' needs its own RoomsPath."));
            else
            {
                try
                {
                    var folder = ProjectManifestService.SafeCombine(project.RootPath, rooms);
                    if (!Directory.Exists(folder)) issues.Add(new("Error", file.RelativePath, $"Room folder '{rooms}' does not exist."));
                    else if (!Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).Any())
                        issues.Add(new("Warning", file.RelativePath, $"Zone '{id}' has no rooms in '{rooms}'; its menu can open but no custom level can be generated yet."));
                }
                catch { issues.Add(new("Error", file.RelativePath, $"Room folder '{rooms}' is unsafe.")); }
            }
        }
    }

    private static HashSet<string> Definitions(IEnumerable<ProjectFileItem> files, string section, string item, string id) =>
        files.Where(file => file.Section == section && file.Extension == "XML").SelectMany(file => SafeElements(file.FullPath, item))
            .Select(element => ((string?)element.Attribute(id))?.Trim()).Where(value => !string.IsNullOrEmpty(value)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<XElement> SafeElements(string path, string name)
    {
        try { return XDocument.Load(path).Descendants(name).ToList(); }
        catch { return []; }
    }
}
