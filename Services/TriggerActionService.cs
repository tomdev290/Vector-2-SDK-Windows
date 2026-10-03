using System.Xml.Linq;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Services;

public static class TriggerActionService
{
    public static readonly string[] AiActions = ["RunForward", "RunFast", "StopMoveRun", "StartMoveRun", "Jump", "WallJump", "Slide", "DivingKong", "DivingKongFly", "SpeedVault", "MonkeyVault", "DashVault", "PopVaultStart", "HurdleJump", "Trick", "Animation", "Spawn", "Activate Spawn", "Respawn", "Ragdoll", "Exit"];

    public static string[] ActionsFor(string target) => target switch
    {
        "player" => ["StopMoveRun", "StartMoveRun", "Trick", "Animation"],
        "project" => ["Send Event"],
        "" => [],
        _ => AiActions
    };

    public static XElement BuildContent(LevelDocument document, LevelNode node) => BuildContent(document, node, true, node.Id);

    private static XElement BuildContent(LevelDocument document, LevelNode node, bool allowWrapper, Guid sourceId)
    {
        var target = node.TriggerTarget;
        var action = node.TriggerAction;
        var value = node.TriggerValue.Trim();
        if (target.Length == 0 || action.Length == 0) throw new InvalidDataException("Choose a trigger target and action.");
        if (!ActionsFor(target).Contains(action)) throw new InvalidDataException($"{action} is not valid for {target}.");
        if (action is "Trick" or "Animation" or "Send Event" or "Activate Spawn" && value.Length == 0)
            throw new InvalidDataException($"{action} needs a value.");
        if (action == "Activate Spawn")
        {
            if (!Guid.TryParse(value, out var spawnId) || !document.SceneNodes.Any(candidate =>
                candidate.Id == spawnId && candidate.Kind == LevelNodeKind.Trigger && candidate.TriggerAction is "Spawn" or "Respawn"))
                throw new InvalidDataException("Choose an existing Spawn or Respawn trigger in this room.");
            value = spawnId.ToString("D");
        }
        if (action == "WallJump" && value is not ("Left" or "Right"))
            throw new InvalidDataException("Choose Left or Right for WallJump.");

        var characters = ResolveTargets(document, target);
        if (target is not ("player" or "project") && characters.Count == 0)
            throw new InvalidDataException("The selected AI or group no longer exists.");
        if (allowWrapper && target is not ("player" or "project") && (characters.Count > 1 || action is "Spawn" or "Respawn"))
        {
            var wrapper = new XElement("Content");
            foreach (var character in characters)
            {
                if (action is "Spawn" or "Respawn" && value.Length == 0)
                    wrapper.Add(new XElement("Spawn", new XAttribute("Name", SpawnName(sourceId, character.Id)),
                        new XAttribute("X", Math.Max(0, node.Width / 2)), new XAttribute("Y", Math.Max(0, node.Height / 2)),
                        new XAttribute("Animation", "RunForward|0")));
                var single = new LevelNode { TriggerTarget = "character:" + character.Id,
                    TriggerAction = action, TriggerValue = value };
                wrapper.Add(new XElement("Trigger", new XAttribute("Name", node.Name),
                    new XAttribute("EditorAITarget", target), new XAttribute("EditorAIAction", action), new XAttribute("EditorAIValue", value),
                    new XAttribute("X", 0), new XAttribute("Y", 0), new XAttribute("Width", node.Width), new XAttribute("Height", node.Height),
                    BuildContent(document, single, false, sourceId)));
            }
            return wrapper;
        }
        var content = new XElement("Content",
            new XElement("Init",
                Variable("$AI", target is "player" or "project" || action is "Spawn" or "Respawn" or "Activate Spawn" ? 0 : characters[0].AIChannel,
                    target is "player" or "project" ? "AI" : null),
                Variable("$Active", 1, target is "player" or "project" ? "Bool" : null),
                Variable("$Node", "COM", target is "player" or "project" ? "Node" : null)));
        if (target is "player" or "project")
        {
            IEnumerable<XElement> playerActions = action switch
            {
                "StopMoveRun" => [Control("Player", "Off"), Press("Left", "Player")],
                "StartMoveRun" => [Control("Player", "On"), Press("Right", "Player")],
                _ => [Force(value, "Player", action == "Trick" ? -1 : 0)]
            };
            content.Add(Loop("Enter", target == "project"
                ? [new XElement("ExecuteCall", new XAttribute("Message", value))] : playerActions));
            if (action == "StopMoveRun") content.Add(Loop("Exit", [Control("Player", "On")]));
            return content;
        }
        foreach (var character in characters)
        {
            var model = character.Name;
            var actions = new List<XElement>();
            switch (action)
            {
                case "WallJump":
                    actions.Add(Press("Up", model));
                    actions.Add(new XElement("Wait", new XAttribute("Frames", 3)));
                    actions.Add(Press(value == "Right" ? "Right" : "Left", model));
                    break;
                case "RunForward": case "Jump": case "Slide":
                    actions.Add(Press(action == "RunForward" ? "Right" : action == "Jump" ? "Up" : "Down", model));
                    break;
                case "StopMoveRun":
                    actions.Add(Control(model, "Off")); actions.Add(Press("Left", model));
                    break;
                case "StartMoveRun":
                    actions.Add(Control(model, "On")); actions.Add(Press("Right", model));
                    break;
                case "Trick": case "Animation":
                    if (action == "Trick") { actions.Add(Press("Up", model)); actions.Add(new XElement("Wait", new XAttribute("Frames", 3))); }
                    actions.Add(Force(value, model, -1));
                    break;
                case "Activate Spawn":
                    actions.Add(new XElement("Activate", new XAttribute("ActionID", "EditorAISpawnTrigger_" + value)));
                    break;
                case "Spawn": case "Respawn":
                    var spawnName = value.Length > 0 ? value : SpawnName(sourceId, character.Id);
                    actions.Add(new XElement("Spawn", new XAttribute("Spawn", spawnName), new XAttribute("Model", model),
                        new XAttribute(action == "Respawn" ? "AllowDeadAI" : "OnlyIfDisabledAI", 1)));
                    if (action == "Spawn") actions.Add(Press("Right", model));
                    break;
                case "Ragdoll": actions.Add(new XElement("Kill", new XAttribute("Model", model))); break;
                case "Exit": actions.Add(new XElement("ExitAI", new XAttribute("Model", model))); break;
                default:
                    var firstFrame = action switch { "DivingKong" or "DivingKongFly" => 16, "SpeedVault" => 11,
                        "MonkeyVault" => 13, "DashVault" => 9, "PopVaultStart" => 7, "HurdleJump" => 5, _ => 0 };
                    actions.Add(Force(action, model, firstFrame));
                    break;
            }
            content.Add(Loop("Enter", actions));
            if (action is "Spawn" or "Respawn")
                content.Add(new XElement("Loop", new XElement("Events", new XElement("Activate")),
                    new XElement("Conditions", new XElement("Equal", new XAttribute("Value1", "_$ActionID"),
                        new XAttribute("Value2", "EditorAISpawnTrigger_" + sourceId.ToString("D")))),
                    new XElement("Actions", actions.Select(item => new XElement(item)))));
            if (action == "StopMoveRun") content.Add(Loop("Exit", [Control(model, "On")]));
        }
        return content;
    }

    private static List<AICharacterDefinition> ResolveTargets(LevelDocument document, string target)
    {
        if (target == "all") return document.AICharacters.ToList();
        var parts = target.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[1], out var id)) return [];
        if (parts[0] == "character") return document.AICharacters.Where(character => character.Id == id).ToList();
        if (parts[0] == "group")
        {
            var group = document.AIGroups.FirstOrDefault(item => item.Id == id);
            return group is null ? [] : document.AICharacters.Where(character => group.CharacterIds.Contains(character.Id)).ToList();
        }
        return [];
    }

    private static XElement Variable(string name, object value, string? type = null) =>
        new("SetVariable", new XAttribute("Name", name), type is null ? null : new XAttribute("Type", type), new XAttribute("Value", value));
    private static string SpawnName(Guid triggerId, Guid characterId) => $"EditorAISpawn_{triggerId:D}_{characterId:D}";
    private static XElement Press(string key, string model) => new("Press", new XAttribute("Key", key), new XAttribute("Model", model));
    private static XElement Control(string model, string state) => new("Control", new XAttribute("Model", model), new XAttribute("Switch", state));
    private static XElement Force(string name, string model, int frame) => new("ForceAnimation", new XAttribute("Name", name), new XAttribute("Model", model), new XAttribute("Frame", frame), new XAttribute("Reversed", 0));
    private static XElement Loop(string eventName, IEnumerable<XElement> actions) => new("Loop", new XElement("Events", new XElement(eventName)), new XElement("Actions", actions));
}
