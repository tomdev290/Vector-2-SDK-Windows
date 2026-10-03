using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public static class ProjectIntegrationAudit
{
    public static IReadOnlyList<ProjectValidationIssue> Validate(string root)
    {
        var issues = new List<ProjectValidationIssue>();
        var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { "custom_chapters", "custom_zones", "custom_protocols", "custom_models", "custom_story",
            "custom_dialogue", "custom_characters", "custom_missions", "custom_rewards", "custom_tutorials", "custom_traps", "custom_quests" })
        {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).Where(file => !ProjectInstallerService.IsAuthoringArtifact(Path.GetRelativePath(directory, file))))
                try { documents[file] = XDocument.Load(file); }
                catch (Exception error) { Add(file, "Error", error.Message); }
        }
        void Add(string file, string severity, string message) => issues.Add(new(severity, Path.GetRelativePath(root, file), message));
        HashSet<string> IDs(string element, string attribute) => documents.Values.SelectMany(document => document.Descendants(element))
            .Select(node => Value(node, attribute)).Where(id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chapters = IDs("Chapter", "Id"); var zones = IDs("Zone", "Id");
        var models = IDs("CustomModel", "ID"); var dialogue = IDs("Dialogue", "Id"); var characters = IDs("Character", "Id");
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { "custom_textures", "custom_backgrounds" })
            if (Directory.Exists(Path.Combine(root, folder)))
                foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories))
                { images.Add(Path.GetFileName(file)); images.Add(Path.GetFileNameWithoutExtension(file)); }
        void ImageReference(string file, XElement node, string attribute)
        {
            var image = Value(node, attribute);
            if (image.Length > 0 && image != "box_unknown" && !images.Contains(image) && !images.Contains(Path.GetFileNameWithoutExtension(image)))
                Add(file, "Warning", $"{node.Name.LocalName} references missing {attribute} image '{image}'.");
        }
        foreach (var kind in new[] { ("Chapter", "Id"), ("Zone", "Id"), ("Protocol", "Id"), ("CustomModel", "ID"),
            ("StoryGraph", "Id"), ("Dialogue", "Id"), ("Character", "Id"), ("CustomMission", "Id"), ("CustomReward", "Id"), ("CustomTutorial", "Id") })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, document) in documents)
                foreach (var node in document.Descendants(kind.Item1).Where(node => kind.Item1 != "Zone" || node.Parent?.Name.LocalName == "Zones"))
                {
                    var id = Value(node, kind.Item2); if (id.Length == 0) continue;
                    if (!seen.Add(id)) Add(file, "Error", $"Duplicate {kind.Item1} ID '{id}' across project files.");
                }
        }
        foreach (var (file, document) in documents)
        {
            foreach (var chapter in document.Descendants("Chapter")) ImageReference(file, chapter, "Artwork");
            foreach (var zone in document.Descendants("Zone"))
                foreach (var key in new[] { "Artwork", "MainBackground", "LoaderBackground" }) ImageReference(file, zone, key);
            foreach (var protocol in document.Descendants("Protocol"))
            {
                var chapter = Value(protocol, "Chapter");
                if (chapter.Length == 0 || !chapters.Contains(chapter)) Add(file, "Warning", $"Protocol '{Value(protocol, "Id")}' references an absent custom chapter '{chapter}'. Check built-in chapter references in the game.");
                if (!int.TryParse(Value(protocol, "StartFloor"), out var floor) || floor < 1) Add(file, "Error", "Protocol start floor must be positive.");
                foreach (var model in new[] { Value(protocol, "PlayerModel") }.Concat(protocol.Elements("Armor").Select(node => Value(node, "Model"))))
                    if (model.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) && !models.Contains(model[7..]))
                        Add(file, "Error", $"Protocol references missing custom model '{model}'.");
                ImageReference(file, protocol, "Artwork");
            }
            foreach (var graph in document.Descendants("StoryGraph"))
            {
                var graphId = Value(graph, "Id");
                var nodes = graph.Elements("Node").ToList();
                var ids = nodes.Select(node => Value(node, "Id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!ids.Contains(Value(graph, "Start"))) Add(file, "Error", $"Story '{graphId}' has no valid starting block.");
                if (ids.Contains("") || ids.Count != nodes.Count) Add(file, "Error", $"Story '{graphId}' has empty or duplicate block IDs.");
                foreach (var node in nodes)
                {
                    if (Value(node, "Type") == "Dialogue" && !dialogue.Contains(Value(node, "Dialogue"))) Add(file, "Warning", $"Story '{graphId}' references missing dialogue '{Value(node, "Dialogue")}'.");
                    if (Value(node, "Type") == "Entrance" && !characters.Contains(Value(node, "Speaker"))) Add(file, "Warning", $"Story '{graphId}' references missing character '{Value(node, "Speaker")}'.");
                    foreach (var target in new[] { Value(node, "Next"), Value(node, "True"), Value(node, "False") }.Concat(node.Elements("Choice").Select(choice => Value(choice, "Next"))))
                        if (target.Length > 0 && !ids.Contains(target)) Add(file, "Error", $"Story '{graphId}' points to missing block '{target}'.");
                }
            }
            foreach (var mission in document.Descendants("CustomMission"))
            {
                if (!int.TryParse(Value(mission, "Difficulty", "1"), out var difficulty) || difficulty is < 1 or > 3) Add(file, "Error", "Mission difficulty must be 1, 2 or 3.");
                if (!int.TryParse(Value(mission, "Target"), out var target) || target < 1) Add(file, "Error", "Mission needs a positive objective target.");
                if (!int.TryParse(Value(mission, "Amount", "0"), out var amount) || amount < 0) Add(file, "Error", "Mission cannot pay negative credits.");
                if (!int.TryParse(Value(mission, "Weight", "100"), out var weight) || weight < 1) Add(file, "Error", "Mission needs a positive selection weight.");
                if (int.TryParse(Value(mission, "Order", "0"), out var minimum) && int.TryParse(Value(mission, "MaximumFloor", "0"), out var maximum) && maximum > 0 && maximum < minimum)
                    Add(file, "Error", "Mission ends before its first available floor.");
            }
            foreach (var quest in document.Descendants("Quest"))
            {
                var id = Value(quest, "Name");
                var steps = quest.Elements("Trigger").Where(node => Value(node, "EditorManaged") == "Sequence").ToList();
                var events = steps.Select(step => step.Descendants("OnCall").FirstOrDefault(node => Value(node, "Name") == "Trigger")).Select(node => node is null ? "" : Value(node, "Message")).ToList();
                if (events.Any(name => name.Length == 0) || events.Distinct(StringComparer.OrdinalIgnoreCase).Count() != events.Count)
                    Add(file, "Error", $"Quest '{id}' has missing or repeated sequence events.");
                for (var index = 0; index < steps.Count; index++)
                    if (!steps[index].Descendants("CounterRange").Any(node => Value(node, "Namespace") == id && Value(node, "Name") == "Step" && Value(node, "Equal") == index.ToString()))
                        Add(file, "Error", $"Quest '{id}' step {index + 1} has a broken order guard.");
                if (steps.Count > 0 && (steps.Sum(step => step.Descendants("QuestComplete").Count()) != 1 || !steps[^1].Descendants("QuestComplete").Any()))
                    Add(file, "Error", $"Quest '{id}' must complete exactly once on its final step.");
            }
            if (document.Root?.Name.LocalName == "CustomTrap")
            {
                var trap = document.Root; var id = Value(trap, "ID"); var library = Value(trap, "Library");
                if (!library.StartsWith("v2trap_", StringComparison.Ordinal) || !library.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(library) != library)
                    Add(file, "Error", $"Trap '{id}' has an invalid runtime library name.");
                else
                {
                    var compiled = Path.Combine(root, "custom_gamedata", "run_data", "libraries", library);
                    try
                    {
                        if (!XDocument.Load(compiled).Descendants("Object").Any(node => Value(node, "Name") == Value(trap, "Object")))
                            Add(file, "Error", $"Trap '{id}' is missing its compiled runtime object.");
                    }
                    catch { Add(file, "Error", $"Trap '{id}' needs to be saved and compiled again."); }
                }
                foreach (var key in new[] { "Artwork", "ChargeArtwork", "HitArtwork", "DisabledArtwork" }) ImageReference(file, trap, key);
            }
        }
        return issues.Distinct().ToArray();
    }
    private static string Value(XElement node, string name, string fallback = "") => ((string?)node.Attribute(name) ?? fallback).Trim();
}
