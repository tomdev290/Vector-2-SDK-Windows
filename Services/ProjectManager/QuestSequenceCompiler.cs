using System.Xml.Linq;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed record QuestStep(string Title, string Event, string VisualGroup = "", string VisualGif = "");

public static class QuestSequenceCompiler
{
    public static IReadOnlyList<XElement> ProgressTriggers(string questId, IReadOnlyList<QuestStep> steps, string rewardPreset)
    {
        var triggers = new List<XElement>();
        for (var index=0;index<steps.Count;index++)
        {
            var step=steps[index];
            var actions=new XElement("Actions");
            if (!string.IsNullOrWhiteSpace(step.VisualGroup) && !string.IsNullOrWhiteSpace(step.VisualGif))
                actions.Add(new XElement("ExecuteCall",new XAttribute("Message",$"Content.Visual:{step.VisualGroup.Trim()}:gif_{Path.GetFileNameWithoutExtension(step.VisualGif.Trim())}.xml")));
            if(index+1<steps.Count)
                actions.Add(new XElement("SetCounter",new XAttribute("Name","Step"),new XAttribute("Namespace",questId),new XAttribute("Value",index+1)));
            else
            {
                if(!string.IsNullOrWhiteSpace(rewardPreset)) actions.Add(new XElement("AddItem",new XAttribute("Preset",rewardPreset.Trim())));
                actions.Add(new XElement("QuestComplete"));
            }
            var loop=new XElement("Loop",new XAttribute("Name",$"Step_{index+1}"),
                new XElement("Events",new XElement("OnCall",new XAttribute("Name","Trigger"),new XAttribute("Message",step.Event.Trim()))),
                new XElement("Conditions",
                    new XElement("CounterRange",new XAttribute("Name",questId),new XAttribute("Namespace","ST_Quests"),new XAttribute("Equal","1")),
                    new XElement("CounterRange",new XAttribute("Name","Step"),new XAttribute("Namespace",questId),new XAttribute("Equal",index))),
                actions);
            triggers.Add(new XElement("Trigger",new XAttribute("Name",$"Step_{index+1}"),new XAttribute("EditorTitle",step.Title),new XAttribute("EditorManaged","Sequence"),new XElement("Content",loop)));
        }
        return triggers;
    }

    public static XElement StartTrigger(string questId, string start, string reference)
    {
        var eventNode=start switch {
            "After another quest" => new XElement("OnCall",new XAttribute("Name","QuestComplete")),
            "When the menu opens" => new XElement("OnScreen",new XAttribute("Name","Start")),
            _ => new XElement("OnCall",new XAttribute("Name","Trigger"),new XAttribute("Message",StartMessage(start,reference)))
        };
        var conditions=new XElement("Conditions",new XElement("CounterRange",new XAttribute("Name",questId),new XAttribute("Namespace","ST_Quests"),new XAttribute("Equal","0")));
        if(start=="After another quest" && !string.IsNullOrWhiteSpace(reference))
            conditions.Add(new XElement("CounterRange",new XAttribute("Name",reference.Trim()),new XAttribute("Namespace","ST_Quests"),new XAttribute("Equal","-1")));
        return new XElement("StartTrigger",new XAttribute("Name","StartQuest"),new XAttribute("EditorManaged","1"),
            new XElement("Content",new XElement("Loop",new XElement("Events",eventNode),conditions,
                new XElement("Actions",new XElement("QuestStart"),new XElement("SetCounter",new XAttribute("Name","Step"),new XAttribute("Namespace",questId),new XAttribute("Value","0"))))));
    }

    public static string StartMessage(string start,string reference)
    {
        var prefix=start switch {
            "When a zone is selected" => "Content.ZoneSelected",
            "When a chapter starts" => "Content.ChapterStart",
            "When a floor starts" => "Content.FloorStart",
            "From a custom event" => "Content.CustomEvent",
            _ => "Content.MenuOpen"
        };
        return string.IsNullOrWhiteSpace(reference) ? prefix : prefix+":"+reference.Trim();
    }
}
