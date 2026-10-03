using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record TriggerTemplateOption(string Title, string Reference, string Kind);

public static class TriggerTemplateService
{
    public static IReadOnlyList<TriggerTemplateOption> Options { get; } = [
        new("Camera zoom", "CameraZoom", "Whole"), new("Camera smoothness", "CameraSmoothness", "Whole"),
        new("Camera follow", "CameraFollow", "Whole"), new("Forced animation", "ForcedAnimation", "Whole"),
        new("Death area", "Death", "Whole"), new("Control toggle", "Control", "Whole"),
        new("Model animation", "ModelAnimation", "Whole"), new("Standard door opener", "standard_door_opener", "Whole"),
        new("No-type collision", "NoneType", "Whole"), new("Camera zoom loop", "CameraZoom.Only", "Loop"),
        new("Enter", "FreqUsed.Enter", "Events"), new("Exit", "FreqUsed.Exit", "Events"),
        new("Enter or exit", "FreqUsed.EnterOrExit", "Events"), new("Activated", "FreqUsed.Activate", "Events"),
        new("Collision", "FreqUsed.OnCollision", "Events"),
        new("Player only", "FreqUsed.TriggeredByPlayer", "Conditions"),
        new("Any bot", "FreqUsed.TriggeredByAnyBot", "Conditions"),
        new("Player alive", "FreqUsed.CheckIfAlive", "Conditions"),
        new("Required animation", "CommonLib.RequiredAnimation", "Conditions"),
        new("Play configured sound", "CommonLib.Sound", "Actions"),
        new("Wait configured delay", "CommonLib.Delay", "Actions"),
        new("Switch trigger on", "FreqUsed.SwitchOn", "Actions"),
        new("Switch trigger off", "FreqUsed.SwitchOff", "Actions"),
        new("Dispatch event", "FreqUsed.DispatchEvent", "Actions")
    ];

    public static void Apply(XElement trigger, TriggerTemplateOption option, int loopIndex)
    {
        var content = trigger.Element("Content") ?? new XElement("Content");
        if (content.Parent is null) trigger.Add(content);
        if (option.Kind == "Whole")
        {
            content.Elements("Template").Remove();
            content.Elements("Loop").Remove();
            content.Add(new XElement("Template", new XAttribute("Name", option.Reference)));
            return;
        }
        if (option.Kind == "Loop")
        {
            content.Add(new XElement("Loop", new XAttribute("Template", option.Reference)));
            return;
        }
        var loop = content.Elements("Loop").ElementAtOrDefault(loopIndex)
            ?? throw new InvalidOperationException("Add a loop before inserting a template block.");
        var group = loop.Element(option.Kind) ?? new XElement(option.Kind);
        if (group.Parent is null) loop.Add(group);
        var nodeName = option.Kind switch { "Events" => "EventBlock", "Conditions" => "ConditionBlock", _ => "ActionBlock" };
        group.Add(new XElement(nodeName, new XAttribute("Template", option.Reference)));
    }

    public static void ApplyOutcome(XElement trigger, string outcome, string reference)
    {
        var content = trigger.Element("Content") ?? new XElement("Content");
        if (content.Parent is null) trigger.Add(content);
        content.Elements("Template").Remove();
        content.Elements("Loop").Remove();
        var eventName = outcome == "Music" ? "OnStartGame" : "Enter";
        var actions = new XElement("Actions");
        switch (outcome)
        {
            case "Dialogue": actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "Content.Dialogue:" + reference))); break;
            case "Zone door":
                actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "Content.Zone:" + reference)));
                actions.Add(new XElement("EndGame", new XAttribute("Result", "Win"), new XAttribute("Model", "Player"), new XAttribute("Frames", "30")));
                break;
            case "Quest": actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "QuestEventName"))); break;
            case "Tutorial": actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "Content.Tutorial:" + reference))); break;
            case "Story": actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "Content.Story:" + reference))); break;
            case "Project event": actions.Add(new XElement("ExecuteCall", new XAttribute("Message", "Content.CustomEvent:event_id"))); break;
            case "Sound": actions.Add(new XElement("Sound", new XAttribute("Name", reference), new XAttribute("Action", "Play"), new XAttribute("Channel", "Sound"), new XAttribute("Volume", "1"))); break;
            case "Music": actions.Add(new XElement("Music", new XAttribute("Track", reference), new XAttribute("Action", "Play"))); break;
            case "Kill player": actions.Add(new XElement("Kill", new XAttribute("Model", "Player"))); break;
            default: throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        content.Add(new XElement("Loop", new XElement("Events", new XElement(eventName)), new XElement("Conditions"), actions));
    }

    public static void EnsureRuntimeInit(XElement trigger)
    {
        var content = trigger.Element("Content") ?? new XElement("Content");
        if (content.Parent is null) trigger.Add(content);
        var init = content.Element("Init") ?? new XElement("Init");
        if (init.Parent is null) content.AddFirst(init);
        foreach (var (name, type, value) in new[] { ("$AI", "AI", "0"), ("$Active", "Bool", "1"), ("$Node", "Node", "COM") })
            if (!init.Elements("SetVariable").Any(node => (string?)node.Attribute("Name") == name))
                init.Add(new XElement("SetVariable", new XAttribute("Name", name), new XAttribute("Type", type), new XAttribute("Value", value)));
    }
}
