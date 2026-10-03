using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void PlaceAuthoredTrigger(string xml)
    {
        var trigger = XElement.Parse(xml);
        if (trigger.Name.LocalName != "Trigger") throw new InvalidDataException("Only a Trigger can be placed here.");
        TriggerTemplateService.EnsureRuntimeInit(trigger);
        TriggerRuntimeSchema.Validate(trigger);
        var scene = new XDocument(new XElement("Root", new XElement("Track", new XElement("Content", trigger))));
        var imported = _parser.LoadFromString(scene.ToString(), "trigger.xml", "");
        var node = imported.SceneNodes.FirstOrDefault(item => item.Kind == LevelNodeKind.Trigger)
            ?? throw new InvalidDataException("The trigger could not be converted into an editable canvas region.");
        AppendSceneNode(SelectedDocument, node);
        IsProjectManagerVisible = false;
        SelectNode(node);
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
        StatusText = $"Placed {node.Name} in {SelectedDocument.Name}";
    }
}
