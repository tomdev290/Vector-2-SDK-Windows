using System.Windows.Controls;
using System.Windows.Media;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class StructuredStudioView : UserControl
{
    public StructuredStudioView(string root, string sectionName, Action<string> status)
    {
        var section = ProjectSection.All.Single(value => value.Name == sectionName);
        var project = new Vector2Project { RootPath = root };
        var details = sectionName switch
        {
            "Chapters" => ("Chapter Designer", "Organize progression floors, artwork, and chapter identity.", "\uE82D", Color.FromRgb(73, 119, 191)),
            "Zones" => ("Zone Designer", "Connect chapters to isolated room, trick, and artwork pools.", "\uE81E", Color.FromRgb(48, 151, 111)),
            "Quests" => ("Quest Designer", "Build quest information, rewards, counters, and start triggers.", "\uE8A4", Color.FromRgb(186, 92, 73)),
            _ => ("Localization", "Create stable phrase keys and translated player-facing text.", "\uE8C1", Color.FromRgb(135, 93, 183))
        };
        if (sectionName == "Localization")
        {
            Content = new NarrativeContentStudioView(root, sectionName, status);
        }
        else if (sectionName is "Chapters" or "Zones")
        {
            Content = new ChapterZoneStudioView(project, section, status);
        }
        else
        {
            var editor = new XmlDefinitionStudio(Path.Combine(root, section.Folder), $"No {sectionName.ToLowerInvariant()} yet.", name => new ProjectTemplateService().Create(project, section, name), status);
            Content = StudioUi.Shell(details.Item1, details.Item2, details.Item3, new SolidColorBrush(details.Item4), editor);
        }
    }
}
