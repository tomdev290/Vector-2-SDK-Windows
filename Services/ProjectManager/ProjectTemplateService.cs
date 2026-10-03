using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;

namespace Vector2LevelEditor.Services.ProjectManager;

public sealed class ProjectTemplateService
{
    public string CreateChapterWithZone(Vector2Project project, string name)
    {
        var chapter = Create(project, ProjectSection.All.Single(section => section.Name == "Chapters"), name);
        try
        {
            var id = (string)XDocument.Load(chapter).Descendants("Chapter").Single().Attribute("Id")!;
            CreateZone(project, name, id);
            return chapter;
        }
        catch { File.Delete(chapter); throw; }
    }

    public static void SyncChapterAppearance(Vector2Project project, string chapterId, string oldName, string oldArtwork, string newName, string newArtwork)
    {
        var folder = Path.Combine(project.RootPath, "custom_zones");
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            var changed = false;
            foreach (var zone in document.Descendants("Zone").Where(zone =>
                (string?)zone.Attribute("Chapter") == chapterId && (string?)zone.Attribute("Id") == chapterId))
            {
                if (((string?)zone.Attribute("Name") ?? "") == oldName) { zone.SetAttributeValue("Name", newName); changed = true; }
                foreach (var field in new[] { "Artwork", "MainBackground", "LoaderBackground" })
                {
                    var current = (string?)zone.Attribute(field) ?? "";
                    if (current.Length == 0 || current == oldArtwork) { zone.SetAttributeValue(field, newArtwork); changed = true; }
                }
            }
            if (!changed) continue;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { document.Save(temporary); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public string CreateZone(Vector2Project project, string name, string chapterId)
    {
        var chapters = Path.Combine(project.RootPath, "custom_chapters");
        var match = Directory.Exists(chapters) ? Directory.EnumerateFiles(chapters, "*.xml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Document: XDocument.Load(path)))
            .FirstOrDefault(item => item.Document.Descendants("Chapter").Any(chapter =>
                string.Equals((string?)chapter.Attribute("Id"), chapterId, StringComparison.OrdinalIgnoreCase))) : default;
        if (match.Document is null) throw new InvalidDataException("Choose an existing chapter before creating a zone.");
        var path = Create(project, ProjectSection.All.Single(section => section.Name == "Zones"), name);
        try
        {
            var definition = XDocument.Load(path);
            var zone = definition.Descendants("Zone").Single();
            var id = (string)zone.Attribute("Id")!;
            zone.SetAttributeValue("Chapter", chapterId);
            var chapter = match.Document.Descendants("Chapter").First(chapter =>
                string.Equals((string?)chapter.Attribute("Id"), chapterId, StringComparison.OrdinalIgnoreCase));
            var artwork = (string?)chapter.Attribute("Artwork") ?? "";
            zone.SetAttributeValue("Artwork", artwork);
            zone.SetAttributeValue("MainBackground", artwork);
            zone.SetAttributeValue("LoaderBackground", artwork);
            definition.Save(path);
            if (!chapter.Elements("Zone").Any(reference => string.Equals((string?)reference.Attribute("Id"), id, StringComparison.OrdinalIgnoreCase)))
                chapter.Add(new XElement("Zone", new XAttribute("Id", id)));
            var temporary = match.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { match.Document.Save(temporary); File.Move(temporary, match.Path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
        catch { File.Delete(path); throw; }
    }

    private static readonly IReadOnlyDictionary<string, (string Root, string Item, string Id)> Schemas =
        new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chapters"] = ("Chapters", "Chapter", "Id"), ["Zones"] = ("Zones", "Zone", "Id"),
            ["Quests"] = ("Quests", "Quest", "Name"), ["Localization"] = ("Localization", "Phrase", "Key"),
            ["Protocols"] = ("Protocols", "Protocol", "Id"), ["Missions"] = ("CustomMission", "", "Id"),
            ["Rewards"] = ("CustomReward", "", "Id"), ["Tutorials"] = ("CustomTutorial", "", "Id"),
            ["Upgrades"] = ("CustomUpgrade", "", "Id"), ["Save Data"] = ("SaveProfile", "", "Id"),
            ["Trigger Designer"] = ("Triggers", "Trigger", "Name"), ["Story"] = ("StoryGraphs", "StoryGraph", "Id")
        };

    public string Create(Vector2Project project, ProjectSection section, string requestedName)
    {
        if (section.Folder.Length == 0) throw new InvalidOperationException("Choose a content section first.");
        var id = SafeId(requestedName);
        if (id.Length == 0) throw new InvalidDataException("Enter a name containing at least one letter or number.");
        var folder = ProjectManifestService.SafeCombine(project.RootPath, section.Folder);
        Directory.CreateDirectory(folder);
        if (section.Name is "Shop" or "Upgrades")
            return CreateCommerceCard(folder, id, requestedName.Trim());
        var path = Path.Combine(folder, id + ".xml");
        if (File.Exists(path)) throw new IOException($"{id}.xml already exists.");
        (string Root, string Item, string Id) schema = Schemas.TryGetValue(section.Name, out var found)
            ? found
            : ("Vector2Content", "Item", "Id");
        var root = CreateRuntimeTemplate(section.Name, schema, id, requestedName.Trim());
        if (section.Name == "Zones")
        {
            ZoneRoomImportService.EnsurePool(project.RootPath, $"custom_rooms/{id}");
            Directory.CreateDirectory(ProjectManifestService.SafeCombine(project.RootPath, $"custom_tricks/{id}"));
        }
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
        return path;
    }

    private static XElement CreateRuntimeTemplate(string section, (string Root, string Item, string Id) schema, string id, string displayName)
    {
        if (section == "Chapters")
            return new XElement("Chapters", new XElement("Chapter",
                new XAttribute("Id", id), new XAttribute("Name", displayName),
                new XAttribute("Description", ""), new XAttribute("Artwork", ""),
                new XElement("Floor", new XAttribute("Number", "1"))));
        if (section == "Zones")
            return new XElement("Zones", new XElement("Zone",
                new XAttribute("Id", id), new XAttribute("Name", displayName),
                new XAttribute("Chapter", ""), new XAttribute("Description", ""),
                new XAttribute("RoomsPath", $"custom_rooms/{id}"), new XAttribute("Artwork", ""),
                new XAttribute("MainBackground", ""), new XAttribute("LoaderBackground", ""),
                new XAttribute("TricksPath", $"custom_tricks/{id}"), new XAttribute("Order", "1")));
        if (section == "Localization")
            return new XElement("Localization", new XElement("Phrase",
                new XAttribute("Key", id), new XAttribute("Language", "Default"), new XAttribute("Value", displayName)));
        if (section == "Quests")
        {
            var info = new XElement("Info",
                new XElement("VisualName", new XAttribute("Value", displayName)),
                new XElement("Description", new XAttribute("Value", ""), new XAttribute("Progress", "")),
                new XElement("Reward", new XAttribute("VisualName", ""), new XAttribute("ImageName", ""), new XAttribute("Name", ""), new XAttribute("Type", "None")));
            var start = new XElement("StartTrigger", new XAttribute("Name", "StartQuest"), new XAttribute("EditorManaged", "1"),
                new XElement("Content", new XElement("Loop",
                    new XElement("Events", new XElement("OnScreen", new XAttribute("Name", "Start"))),
                    new XElement("Conditions", new XElement("CounterRange", new XAttribute("Name", id), new XAttribute("Namespace", "ST_Quests"), new XAttribute("Equal", "0"))),
                    new XElement("Actions", new XElement("QuestStart"), new XElement("SetCounter", new XAttribute("Name", "Step"), new XAttribute("Namespace", id), new XAttribute("Value", "0"))))));
            return new XElement("Quests", new XElement("Quest", new XAttribute("Name", id), info, start));
        }
        if (section == "Trigger Designer")
            return new XElement("Trigger",
                new XAttribute("Name", id), new XAttribute("X", "0"), new XAttribute("Y", "0"),
                new XAttribute("Width", "320"), new XAttribute("Height", "180"),
                new XElement("Content", new XElement("Loop",
                    new XElement("Events", new XElement("Enter")),
                    new XElement("Conditions"),
                    new XElement("Actions"))));
        if (section == "Protocols")
            return new XElement("Protocols", new XElement("Protocol",
                new XAttribute("Id", id), new XAttribute("Name", displayName), new XAttribute("Description", ""),
                new XAttribute("Artwork", "box_unknown"), new XAttribute("Chapter", ""), new XAttribute("BaseProtocol", "BasicProtocol"),
                new XAttribute("PlayerModel", ""), new XAttribute("StartFloor", "1"), new XAttribute("Order", "0"),
                new XElement("Gameplay", new XAttribute("EffectID", "None"), new XAttribute("Interval", "5.00"), new XAttribute("Amount", "1"), new XAttribute("Maximum", "12")),
                new XElement("ArmourDefense", new XAttribute("Helmet", "100"), new XAttribute("Torso", "100"), new XAttribute("Hands", "100"), new XAttribute("Legs", "100"), new XAttribute("Belt", "100"))));
        if (section is "Missions" or "Rewards" or "Tutorials")
        {
            var kind = section[..^1];
            var element = new XElement("Custom" + kind,
                new XAttribute("Id", id), new XAttribute("Name", displayName), new XAttribute("Description", ""),
                new XAttribute("Type", section == "Missions" ? "Points" : section == "Rewards" ? "Credits" : "Menu Open"),
                new XAttribute("Target", "100"), new XAttribute("Amount", "100"), new XAttribute("Order", "1"),
                new XAttribute("Reference", ""), new XAttribute("Message", ""), new XAttribute("Once", "1"),
                new XAttribute("Difficulty", "1"), new XAttribute("MaximumFloor", "0"), new XAttribute("Weight", "100"), new XAttribute("Protocol", "Any"));
            if (section == "Tutorials") element.Add(new XElement("Step", new XAttribute("Order", "1"), new XAttribute("Message", "New tutorial step")));
            return element;
        }
        if (schema.Item.Length == 0)
            return new XElement(schema.Root, new XAttribute(schema.Id, id), new XAttribute("Name", displayName));
        var item = new XElement(schema.Item, new XAttribute(schema.Id, id), new XAttribute("Name", displayName));
        if (section == "Story") item.SetAttributeValue("Start", "");
        return new XElement(schema.Root, item);
    }

    private static string CreateCommerceCard(string folder, string id, string displayName)
    {
        var package = Path.Combine(folder, id);
        if (Directory.Exists(package)) throw new IOException($"A card package named {id} already exists.");
        Directory.CreateDirectory(package);
        var path = Path.Combine(package, "card.xml");
        var root = new XElement("CustomCard",
            new XAttribute("Name", id), new XAttribute("CardName", id), new XAttribute("VisualName", displayName),
            new XAttribute("Description", ""), new XAttribute("Image", ""), new XAttribute("Rarity", "1"),
            new XAttribute("Price", "1000"), new XAttribute("ShopEnabled", "1"), new XAttribute("Group", "CustomCards"),
            new XAttribute("SetupMin", "0"), new XAttribute("SetupMax", "99"), new XAttribute("Weight", "1000"),
            new XAttribute("WeightForUse", "1"), new XAttribute("CardType", "Passive"), new XAttribute("Category", "Custom"),
            new XAttribute("EffectID", "None"), new XAttribute("Slot", "Torso"), new XAttribute("MaxLevel", "5"));
        foreach (var level in new[] { (2, 100), (3, 120), (5, 145), (7, 175), (9, 210) }.Select((value, index) => new XElement("Level", new XAttribute("Number", index + 1), new XAttribute("Cards", value.Item1), new XAttribute("Points", value.Item2))))
            root.Add(level);
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
        return path;
    }

    public static string SafeId(string value) => new string(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray()).Trim('_');
}
