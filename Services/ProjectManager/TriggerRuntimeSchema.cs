using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record TriggerRuntimeItem(string Name, string[] Required, IReadOnlyDictionary<string, string> Defaults);
public sealed record TriggerDiagnostic(bool IsError, string Message, int Line = 0);

public static class TriggerRuntimeSchema
{
    private static TriggerRuntimeItem Item(string name, string required = "", params string[] defaults) =>
        new(name, required.Split(',', StringSplitOptions.RemoveEmptyEntries), defaults.Select(value => value.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]));

    public static IReadOnlyList<TriggerRuntimeItem> Events { get; } = [
        Item("EventBlock", "Template"), Item("Enter"), Item("Exit"), Item("Timeout"), Item("KeyPressed"), Item("Activate"),
        Item("Line", "Position,Type"), Item("Collision"), Item("OnShow"), Item("OnHide"), Item("OnShowWidescreen"),
        Item("OnHideWidescreen"), Item("ValueChange", "Value"), Item("OnStartGame"), Item("OnGlobalTimer"),
        Item("SwarmArrival"), Item("SwarmDeparture"), Item("SwarmDec"), Item("EndGame"), Item("ActivateNearPlayer"), Item("OnDeath")
    ];
    public static IReadOnlyList<TriggerRuntimeItem> Conditions { get; } = [
        Item("ConditionBlock", "Template"), Item("Equal", "Value1,Value2"), Item("Greater", "Value1,Value2"),
        Item("Less", "Value1,Value2"), Item("GreaterEqual", "Value1,Value2"), Item("LessEqual", "Value1,Value2"),
        Item("Operator", "Type"), Item("Select", "Object,From,Section")
    ];
    public static IReadOnlyList<TriggerRuntimeItem> Actions { get; } = [
        Item("ActionBlock", "Template"), Item("SoundSource", "Name", "Switch=On", "VolumeFactor=1"),
        Item("Camera"), Item("Wait", "Frames", "Frames=30"), Item("SetVariable", "Name,Value"), Item("AppendValue", "Name,Value"),
        Item("Press", "Key,Model"), Item("ForceAnimation", "Name,Model", "Frame=-1", "Reversed=0"),
        Item("Control", "Model,Switch"), Item("EndGame", "Result,Model", "Result=Win", "Model=Player", "Frames=30"),
        Item("SetTimer", "Frames", "Frames=30"), Item("Spawn", "Model"), Item("ExitAI", "Model"), Item("Transform", "Name"),
        Item("Choose", "Set,Order"), Item("Activate", "ActionID"), Item("ModelExecute", "AnimName,AnimFrame"),
        Item("Kill", "Model"), Item("ArmorDamage"), Item("AddItem"), Item("Sound", "Name", "Action=Play", "Channel=Sound", "Volume=1"),
        Item("Music", "Track", "Action=Play", "Time=0"), Item("MakeRoom"), Item("ChangeExit", "Name"),
        Item("Impulse", "Model,Impulse,R", "Absorption=0.8"), Item("RunModelEffect", "Name,Model"), Item("Tutorial"),
        Item("SetModelParameter", "ModelName,ParamName,Type,Value"), Item("FloatingText"), Item("TutorialSequence", "Name"),
        Item("GlobalTimer", "Action", "Frames=600"), Item("ExecuteCall", "Message"), Item("Statistics", "SignalMessage"),
        Item("GUI", "Action"), Item("Swarm", "Type,SwarmName"), Item("Chapter"), Item("ActivatePassiveEffect", "ActionID"),
        Item("ActivateNearPlayer", "ActionID"), Item("BlockAnimationKey", "Key,Value")
    ];

    public static XElement Create(string name, string group = "Actions")
    {
        var catalogue = group == "Events" ? Events : group == "Conditions" ? Conditions : Actions;
        var item = catalogue.FirstOrDefault(item => item.Name == name);
        var node = new XElement(name);
        if (item is null) return node;
        foreach (var key in item.Required) node.SetAttributeValue(key, item.Defaults.GetValueOrDefault(key, ""));
        foreach (var pair in item.Defaults) node.SetAttributeValue(pair.Key, pair.Value);
        return node;
    }

    public static void Validate(XElement trigger)
    {
        var error = Diagnose(trigger).FirstOrDefault(item => item.IsError);
        if (error is not null) throw new InvalidDataException(error.Message);
    }

    public static IReadOnlyList<TriggerDiagnostic> Diagnose(XElement trigger)
    {
        var result = new List<TriggerDiagnostic>();
        static int Line(XElement node) => node is System.Xml.IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
        if (string.IsNullOrWhiteSpace((string?)trigger.Attribute("Name"))) result.Add(new(true, "Trigger Name is required.", Line(trigger)));
        foreach (var name in new[] { "Width", "Height" })
            if (!double.TryParse((string?)trigger.Attribute(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size <= 0)
                result.Add(new(true, $"Trigger {name} must be greater than zero.", Line(trigger)));
        var content = trigger.Element("Content");
        if (content?.Elements("Loop").Any() != true && content?.Elements("Template").Any(item => !string.IsNullOrWhiteSpace((string?)item.Attribute("Name"))) != true)
            result.Add(new(true, "A trigger needs a loop or a named template.", Line(content ?? trigger)));
        var index = 0;
        foreach (var loop in content?.Elements("Loop") ?? [])
        {
            index++;
            if (string.IsNullOrWhiteSpace((string?)loop.Attribute("Template")))
            {
                if (loop.Element("Events")?.Elements().Any() != true) result.Add(new(true, $"Loop {index} needs an event.", Line(loop)));
                if (loop.Element("Actions")?.Elements().Any() != true) result.Add(new(false, $"Loop {index} has no actions.", Line(loop)));
            }
            foreach (var (group, catalogue) in new[] { ("Events", Events), ("Conditions", Conditions), ("Actions", Actions) })
            foreach (var node in loop.Element(group)?.Elements() ?? [])
            {
                var item = catalogue.FirstOrDefault(item => item.Name == node.Name.LocalName);
                if (item is null) { result.Add(new(false, $"Unknown {group.ToLowerInvariant()} node <{node.Name}> is preserved as raw XML.", Line(node))); continue; }
                foreach (var required in XmlAssistantContext.RequiredFields(node, item))
                    if (string.IsNullOrWhiteSpace((string?)node.Attribute(required))) result.Add(new(true, $"{node.Name} requires {required}.", Line(node)));
            }
        }
        return result;
    }
}
