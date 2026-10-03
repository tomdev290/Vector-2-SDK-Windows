using System.Globalization;
using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Services;

public static class XmlAssistantContext
{
    public static string[] RequiredFields(XElement element, TriggerRuntimeItem item)
    {
        var fields = item.Required.ToList();
        if (element.Name.LocalName is "Sound" or "Music" &&
            ((string?)element.Attribute("Action") ?? "Play") != "Play")
            fields.Remove(element.Name.LocalName == "Sound" ? "Name" : "Track");
        if (element.Name.LocalName == "Swarm")
        {
            if ((string?)element.Attribute("Type") == "Spawn") fields.Add("Waypoint");
            if ((string?)element.Attribute("Type") == "Activate") fields.Add("ActionID");
        }
        return fields.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string[] ProjectFieldsNeedingInput(XElement element)
    {
        var root = element.AncestorsAndSelf().Last();
        if (root.Name.NamespaceName.Length != 0 || element.Name.NamespaceName.Length != 0) return [];
        var required = new List<string>();
        var integers = new List<string>();
        var numbers = new List<string>();
        if (ReferenceEquals(root, element))
        {
            switch (root.Name.LocalName)
            {
                case "CustomModel": required.AddRange(["ID", "FileName"]); break;
                case "CustomTrick": required.AddRange(["Name", "FileName"]); integers.AddRange(["FirstFrame", "EndFrame", "MidFrames"]); break;
                case "AnimationOverride": required.AddRange(["Target", "FileName"]); break;
                case "CustomMission": case "CustomReward": case "CustomTutorial":
                    integers.AddRange(["Target", "Amount", "Order", "Difficulty", "MaximumFloor", "Weight"]); break;
                case "CustomTrap":
                    integers.AddRange(["Width", "Height", "CycleFrames", "Reach", "DamageAmount", "ChargeFrames", "HitFrames", "DangerX", "DangerY", "DangerWidth", "DangerHeight", "ActivationX", "ActivationY", "ActivationWidth", "ActivationHeight"]);
                    numbers.Add("SoundVolume"); break;
            }
        }
        else if (root.Name == "Protocols" && element.Parent == root && element.Name == "Protocol")
        {
            required.Add("Id"); integers.AddRange(["StartFloor", "Order"]);
        }
        else if (root.Name == "Protocols" && element.Parent?.Name == "Protocol" && element.Parent.Parent == root)
        {
            if (element.Name == "Gameplay") { numbers.Add("Interval"); integers.AddRange(["Amount", "Maximum"]); }
            if (element.Name == "ArmourDefense") integers.AddRange(["Capacity", "Impact", "Heat", "Electric", "Swarm", "Helmet", "Torso", "Hands", "Legs", "Belt"]);
        }
        return required.Where(key => string.IsNullOrWhiteSpace((string?)element.Attribute(key)))
            .Concat(integers.Where(key => element.Attribute(key) is { } value && !int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            .Concat(numbers.Where(key => element.Attribute(key) is { } value && (!double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))))
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
