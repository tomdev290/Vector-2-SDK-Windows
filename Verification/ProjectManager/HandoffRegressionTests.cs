using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

static class HandoffRegressionTests
{
    public static void Run()
    {
        var selectedGame = Path.Combine(Path.GetTempPath(), "SelectedGame", "Vector 2.exe");
        Check(Vector2PlayBridge.MatchRunningGame(selectedGame, []) is null, "No running game incorrectly matched");
        Check(Vector2PlayBridge.MatchRunningGame(selectedGame, [selectedGame]) == selectedGame, "Selected running game was rejected");
        try { Vector2PlayBridge.MatchRunningGame(selectedGame, [Path.Combine(Path.GetTempPath(), "OldGame", "Vector 2.exe")]); throw new Exception("Different running game was silently selected"); }
        catch (IOException) { }
        string[] damaged = [
            "<Trigger><Content><Loop><Events><Enter /></Events><Conditions/><Actions/></Loop><Content></Trigger>",
            "<Trigger Width=320 Height=180><Content><Loop><Events><Enter/></Events></Loop></Content></Trigger>",
            "<Trigger><Content><Loop><Events><Enter/></Events><Actions>text & more</Actions></Content></Trigger>",
            "<Trigger><Content><Loop><Events><Enter/></Events></Loop></Content>",
            "<Trigger><Content><Loop><Events><Enter/></Events></Loop></Contnet></Trigger>",
            "<Trigger><Content><!-- > < untouched --><![CDATA[<x & y>]]><Loop></Content></Trigger>"
        ];
        foreach (var draft in damaged)
        {
            var review = XmlDraftReviewer.Review(draft);
            Check(review.Source == draft && review.Solutions.Count > 0, "No repair for " + draft);
            foreach (var solution in review.Solutions)
                Check(XDocument.Parse(solution.Code).Root?.Name == "Trigger", "Repair changed the root");
            Check(review.Solutions.Count(solution => solution.Recommended) <= 1,
                "Competing structural repairs were both recommended");
        }
        Check(XmlDraftReviewer.Review(new string('x', 200_001)).Solutions.Count == 0, "Draft bound missing");
        var strayLine = "<Trigger>\n  <Content><Loop /></Content>\n  <Content>\n</Trigger>";
        Check(XmlStructuralRepairs.Proposals(strayLine).Any(solution =>
            solution.Code == "<Trigger>\n  <Content><Loop /></Content>\n</Trigger>"),
            "Deleting a standalone stray tag left whitespace or reformatted unrelated lines");
        Check(XmlDraftReviewer.Review("<Trigger><Content></Trigger>").Solutions.All(solution =>
            XDocument.Parse(solution.Code).Root?.Element("Content") is not null), "Closing repair deleted the original Content section");
        var headerDraft = "<?xml version='1.0' encoding='utf-8'?><Trigger Name='test' Width='320' Height='180'><Content><Loop><Events><Enter/></Events><Actions><Wiat Frames='30'/></Actions></Loop></Content></Trigger>";
        var spellingReview = XmlDraftReviewer.Review(headerDraft);
        Check(spellingReview.Solutions.Any(solution => XDocument.Parse(solution.Code).Descendants("Wait").Any() && solution.Code.StartsWith("<?xml")),
            "Supported spelling repair removed the XML declaration");
        var scopeDraft = "<Root><Trigger><Content><Init><SetVariable Name='$Active' Type='Bool' Value='1'/><SetVariable Name='Count' Value='3'/></Init><Loop><Conditions><Equal Value1='_$Active' Value2=''/></Conditions></Loop></Content></Trigger><Trigger><Content><Init><SetVariable Name='Other' Value='2'/></Init></Content></Trigger></Root>";
        var scopeReview = XmlDraftReviewer.Review(scopeDraft);
        Check(scopeReview.Solutions.Any(solution => solution.Code.Contains("Value2=\"_Count\"")) &&
            !scopeReview.Solutions.Any(solution => solution.Code.Contains("Value2=\"_$Active\"") || solution.Code.Contains("Value2=\"_Other\"")) &&
            scopeReview.Solutions.All(solution => !solution.Recommended), "Comparison choices leaked scope, self-compared, or implied intent");
        var intent = scopeReview.Intents.Single();
        var explicitChoice = XmlDraftReviewer.CompleteIntent(scopeDraft, intent, new Dictionary<string, string> { ["Value2"] = "9 & 10" });
        Check(XDocument.Parse(explicitChoice.Code).Descendants("Equal").Single().Attribute("Value2")?.Value == "9 & 10",
            "Explicit intent values were not XML escaped");
        try { XmlDraftReviewer.CompleteIntent(scopeDraft, intent, new Dictionary<string, string>()); throw new Exception("Incomplete intent accepted"); }
        catch (InvalidDataException) { }

        var document = LevelDocument.Empty();
        var actor = new AICharacterDefinition { Name = "Guard" };
        document.AICharacters.Add(actor);
        Check(AIExportPolicy.SpawnOnStart(document, actor), "Untargeted actor did not start");
        var factor = document.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor);
        var spawn = new LevelNode { Kind = LevelNodeKind.Trigger, TriggerAction = "Spawn",
            TriggerTarget = "character:" + actor.Id };
        factor.Children.Add(spawn);
        spawn.TriggerContentXml = TriggerActionService.BuildContent(document, spawn).ToString();
        var spawnContent = XElement.Parse(spawn.TriggerContentXml);
        var activation = spawnContent.Descendants("Loop").Single(loop => loop.Element("Events")?.Element("Activate") is not null);
        Check((string?)activation.Element("Conditions")?.Element("Equal")?.Attribute("Value2") == "EditorAISpawnTrigger_" + spawn.Id.ToString("D"),
            "Activate Spawn cannot address the authored trigger");
        Check(!AIExportPolicy.SpawnOnStart(document, actor), "Actor started before its authored Spawn");
        var activateSpawn = new LevelNode { Kind = LevelNodeKind.Trigger, TriggerTarget = "character:" + actor.Id,
            TriggerAction = "Activate Spawn", TriggerValue = spawn.Id.ToString("D").ToUpperInvariant() };
        factor.Children.Add(activateSpawn);
        Check((string?)TriggerActionService.BuildContent(document, activateSpawn).Descendants("Activate").Single().Attribute("ActionID") ==
            "EditorAISpawnTrigger_" + spawn.Id.ToString("D"), "Imported uppercase spawn IDs did not match the generated action");
        activateSpawn.TriggerValue = Guid.NewGuid().ToString("D");
        try { TriggerActionService.BuildContent(document, activateSpawn); throw new Exception("Missing spawn was accepted"); }
        catch (InvalidDataException) { }
        activateSpawn.TriggerValue = spawn.Id.ToString("D");
        actor.Name = "RenamedGuard";
        var exported = XDocument.Parse(new Exporter().Export(document));
        Check(exported.Descendants("Spawn").Any(item => (string?)item.Attribute("Model") == "RenamedGuard"),
            "Renaming an actor left generated spawn actions pointing to its old name");
        var playerStop = new LevelNode { Kind = LevelNodeKind.Trigger, TriggerTarget = "player", TriggerAction = "StopMoveRun" };
        Check(TriggerActionService.BuildContent(document, playerStop).Descendants("Control").Count() == 2,
            "Player movement control did not restore control when leaving the trigger");
        Check((string?)TriggerActionService.BuildContent(document, playerStop).Descendants("Press").Single().Attribute("Key") == "Left",
            "StopMoveRun bypassed the game's RunInhibition reaction instead of matching the Mac exporter");
        Check((string?)exported.Descendants("AICharacter").Single().Attribute("SpawnOnStart") == "0",
            "Spawn inference did not reach exported XML");
        var group = new AIGroupDefinition { Name = "Guards" };
        group.CharacterIds.Add(actor.Id);
        document.AIGroups.Add(group);
        spawn.TriggerTarget = "group:" + group.Id;
        Check(!AIExportPolicy.SpawnOnStart(document, actor), "Group Spawn started actor early");
        spawn.TriggerTarget = "all";
        Check(!AIExportPolicy.SpawnOnStart(document, actor), "All-AI Spawn started actor early");
        spawn.TriggerTarget = "group:invalid";
        Check(AIExportPolicy.SpawnOnStart(document, actor), "Invalid group changed actor policy");
        actor.SpawnOnStart = false;
        Check(!AIExportPolicy.SpawnOnStart(document, actor), "Explicit disabled start was overwritten");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
