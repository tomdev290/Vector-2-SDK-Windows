using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services.ProjectManager;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;
using Vector2LevelEditor.ViewModels.ProjectManager;
using Vector2LevelEditor.Views.ProjectManager.Studios;
using Vector2LevelEditor.Views.ProjectManager;
using Vector2LevelEditor.Views;
using Vector2LevelEditor.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Reflection;
using System.IO.Compression;
using System.Xml.Linq;

if (args.Length == 2 && args[0] == "--refresh-shop")
{
    var count = ProjectShopRefresh.RefreshInstalledOffers(args[1]);
    Console.WriteLine($"Shop hot reload requested by timestamp-only refresh: {count} enabled manifest(s).");
    return;
}

if (args.Length == 3 && args[0] == "--prepare-chapter")
{
    var project = new ProjectManifestService().Load(args[1]);
    var id = args[2];
    var chapterPath = Directory.EnumerateFiles(Path.Combine(project.RootPath, "custom_chapters"), "*.xml", SearchOption.AllDirectories)
        .First(path => XDocument.Load(path).Descendants("Chapter").Any(chapter => (string?)chapter.Attribute("Id") == id));
    var chapter = XDocument.Load(chapterPath).Descendants("Chapter").First(chapter => (string?)chapter.Attribute("Id") == id);
    var backup = Path.Combine(project.RootPath, ".backups", "chapter-link", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    Directory.CreateDirectory(backup);
    File.Copy(chapterPath, Path.Combine(backup, Path.GetFileName(chapterPath)));
    var existing = StructuralRoomService.LoadZones(project.RootPath).FirstOrDefault(zone => zone.Id == id);
    if (existing is null)
    {
        var zonePath = new ProjectTemplateService().CreateZone(project, (string?)chapter.Attribute("Name") ?? id, id);
        existing = StructuralRoomService.LoadZones(project.RootPath).First(zone => zone.Id == id);
        Console.WriteLine("Created linked zone: " + zonePath);
    }
    var pool = ZoneRoomImportService.EnsurePool(project.RootPath, existing.RoomsPath);
    if (!Directory.EnumerateFiles(pool, "*.xml").Any())
        Console.WriteLine("Created editable starter room: " + ZoneRoomImportService.CreateRoom(project.RootPath, existing.RoomsPath, id + "_room"));
    foreach (var kind in new[] { StructuralRoomKind.Entrance, StructuralRoomKind.Exit })
    {
        var subfolder = kind == StructuralRoomKind.Entrance ? "start_rooms" : "finish_rooms";
        if (!Directory.EnumerateFiles(Path.Combine(pool, subfolder), "*.xml").Any())
            Console.WriteLine("Created structural starter room: " + StructuralRoomService.SaveToZone(StructuralRoomService.Create(kind), project.RootPath, existing));
    }
    ProjectInstallerService.ValidateProgression(project.RootPath);
    Console.WriteLine("Chapter linkage and room pools prepared. Backup: " + backup);
    return;
}

if (args.Length is 3 or 4 && args[0] == "--install-project")
{
    var project = new ProjectManifestService().Load(args[1]);
    var dataPath = args.Length == 4 ? args[3] : project.GameDataPath;
    var destination = Directory.Exists(dataPath) ? Vector2GameIntegrationLocator.ModDataRootFor(dataPath) : CustomContentService.DefaultStorageRoot;
    var backup = Path.GetFullPath(args[2]);
    if (Directory.Exists(backup)) throw new IOException("Use a new backup directory.");
    Directory.CreateDirectory(backup);
    if (Directory.Exists(destination))
        foreach (var folder in Directory.EnumerateDirectories(destination).Where(folder =>
            Path.GetFileName(folder).StartsWith("custom_", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(folder).StartsWith(".vector2-", StringComparison.OrdinalIgnoreCase)))
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(backup, Path.GetRelativePath(destination, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
    var installer = new ProjectInstallerService();
    var commandModel = new ProjectManagerViewModel();
    commandModel.GameDataPathProvider = () => dataPath;
    typeof(ProjectManagerViewModel).GetProperty("Project")!.SetValue(commandModel, project);
    commandModel.InstallCommand.Execute(null);
    Require(commandModel.Status.StartsWith("Installed "), "Actual UI Install command failed: " + commandModel.Status);
    Console.WriteLine("UI Install command: " + commandModel.Status);
    var result = installer.Install(project, dataPath);
    var paths = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(result.ReceiptPath))!;
    foreach (var relative in paths)
    {
        var source = File.ReadAllBytes(ProjectManifestService.SafeCombine(project.RootPath, relative));
        var installed = File.ReadAllBytes(ProjectManifestService.SafeCombine(destination, relative));
        Require(source.AsSpan().SequenceEqual(installed), "Installed content mismatch: " + relative);
    }
    var repeat = installer.Install(project, dataPath);
    Require(repeat.Copied == 0 && repeat.Removed == 0, "Repeat install changed identical files.");
    Console.WriteLine($"Real game content install verified: {paths.Length} files; copied {result.Copied}, removed {result.Removed}. Destination: {destination}. Backup: {backup}");
    return;
}

var preferences = new[] { "last-project.txt", "settings.json" }.Select(name => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vector2LevelEditor", name))
    .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
var root = Path.Combine(Path.GetTempPath(), "Vector2EditorProjectManagerVerification", Guid.NewGuid().ToString("N"));
try
{
    var shopFixture = Path.Combine(root, "shop-touch"); Directory.CreateDirectory(Path.Combine(shopFixture, "custom_tricks", "enabled"));
    Directory.CreateDirectory(Path.Combine(shopFixture, "custom_tricks", "disabled"));
    var enabledManifest = Path.Combine(shopFixture, "custom_tricks", "enabled", "trick.xml");
    var disabledManifest = Path.Combine(shopFixture, "custom_tricks", "disabled", "trick.xml");
    File.WriteAllText(enabledManifest, "<CustomTrick Name='test' ShopEnabled='1'/>");
    File.WriteAllText(disabledManifest, "<CustomTrick Name='hidden' ShopEnabled='0'/>");
    var payload = File.ReadAllBytes(enabledManifest); var oldTime = DateTime.UtcNow.AddHours(-1);
    File.SetLastWriteTimeUtc(enabledManifest, oldTime); File.SetLastWriteTimeUtc(disabledManifest, oldTime);
    Require(ProjectShopRefresh.TouchManifests(shopFixture, new[] { "custom_tricks/enabled/trick.xml", "custom_tricks/disabled/trick.xml" }) == 1 &&
        payload.SequenceEqual(File.ReadAllBytes(enabledManifest)) && File.GetLastWriteTimeUtc(enabledManifest) > oldTime && File.GetLastWriteTimeUtc(disabledManifest) == oldTime,
        "Shop workaround modified manifest content or touched a disabled offer");
    HandoffRegressionTests.Run();
    ZoneWorkflowRegressionTests.Run();
    Require(!new MainWindowViewModel().IsProjectManagerVisible, "Project Manager replaced the level editor during startup");
    var aiXml = "<Root><Track><Content/></Track><AICharacters><AICharacter EditorID=\"11111111-1111-1111-1111-111111111111\" Name=\"Hunter\" Kind=\"Enemy\" AI=\"3\" BirthSpawn=\"GateIn\" Time=\"0.250\" Skins=\"1.xml|hair.xml\" SpawnOnStart=\"0\"/><Groups><Group EditorID=\"22222222-2222-2222-2222-222222222222\" Name=\"Hunters\" Members=\"11111111-1111-1111-1111-111111111111\"/></Groups></AICharacters></Root>";
    var aiDocument = new XmlSceneParser().LoadFromString(aiXml, "ai_room.xml");
    Require(aiDocument.AICharacters.Count == 1 && aiDocument.AIGroups.Count == 1 &&
        aiDocument.AICharacters[0].AIChannel == 3 && !aiDocument.AICharacters[0].SpawnOnStart &&
        aiDocument.AIGroups[0].CharacterIds.Contains(aiDocument.AICharacters[0].Id),
        "Mac AI characters or groups did not import into the room document");
    var aiExport = new Exporter().Export(aiDocument);
    aiDocument.AICharacters[0].ChestSkin = "";
    aiDocument.AICharacters[0].HelmetSkin = "black_helmet.xml";
    aiDocument.AICharacters[0].HairSkin = "hair.xml";
    aiDocument.AICharacters[0].CustomLayers.Add("custom:coat");
    var skinReload = new XmlSceneParser().LoadFromString(new Exporter().Export(aiDocument)).AICharacters.Single();
    Require(skinReload.ChestSkin == "" && skinReload.HelmetSkin == "black_helmet.xml" && skinReload.HairSkin == "hair.xml" && skinReload.CustomLayers.Single() == "custom:coat", "AI empty armour slots shifted during import");
    var dynamicRoom = new XmlSceneParser().LoadFromString("<Root><Track><Content><Platform Name='Mover' X='10' Y='20' Width='100' Height='50'><Properties><Dynamic><Transformation Name='Travel'><MoveInterval Frames='30' Type='Bezier'><Point X='0' Y='0'/><Point X='60' Y='-10'/></MoveInterval><RotationInterval Frames='15' Type='EaseOut' Angle='90'/></Transformation></Dynamic></Properties></Platform></Content></Track></Root>");
    var mover = dynamicRoom.SceneNodes.Single(node => node.Name == "Mover");
    var importedTimeline = DynamicStudioService.ImportTimeline(mover)!;
    Require(importedTimeline.Keyframes.Count == 3 && importedTimeline.TotalFrames == 45 && importedTimeline.Keyframes.Last().X == 70 && importedTimeline.Keyframes.Last().Y == 10 && importedTimeline.RotationMode == DynamicRotationMode.CubicEaseOut, "Native dynamic intervals did not reimport in runtime sequence");
    mover.DynamicTimelineJson = DynamicStudioService.EncodeTimeline(importedTimeline);
    var dynamicReload = new XmlSceneParser().LoadFromString(new Exporter().Export(dynamicRoom)).SceneNodes.Single(node => node.Name == "Mover");
    Require(dynamicReload.DynamicTimelineJson == mover.DynamicTimelineJson && XElement.DeepEquals(XElement.Parse(dynamicReload.DynamicXml), XElement.Parse(mover.DynamicXml)), "Dynamic timeline or runtime XML changed during export/import");
    Exception? cameraFailure = null;
    var cameraThread = new Thread(() =>
    {
    try {
    var cameraVm = new MainWindowViewModel();
    cameraVm.SelectedDocument.SelectedNode = new LevelNode { Name = "CameraTarget", X = 5000, Y = -2000, Width = 100, Height = 80 };
    var camera = new Vector2LevelEditor.Controls.EditorCanvas { ViewModel = cameraVm, Width = 800, Height = 600 };
    camera.Measure(new System.Windows.Size(800, 600)); camera.Arrange(new System.Windows.Rect(0, 0, 800, 600));
    cameraVm.Zoom = 2;
    Require(camera.CenterOnSelection(), "Camera did not center a selected object");
    var cameraPan = (System.Windows.Vector)typeof(Vector2LevelEditor.Controls.EditorCanvas).GetField("_pan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(camera)!;
    Require(cameraPan.X == 400 - 5050 * 2 && cameraPan.Y == 300 - -1960 * 2, "Camera recenter ignored zoom or object bounds");
    cameraVm.SelectedDocument.SelectedNode = null;
    Require(!camera.CenterOnSelection(), "Camera recenter accepted an empty selection");
    cameraVm.ShowConsole = false;
    Require(cameraVm.ConsoleVisibility == System.Windows.Visibility.Collapsed, "Console setting did not hide the console");
    cameraVm.ShowConsole = true;
    Require(cameraVm.ConsoleVisibility == System.Windows.Visibility.Visible, "Console setting did not restore the console");
    } catch (Exception error) { cameraFailure = error; }
    });
    cameraThread.SetApartmentState(ApartmentState.STA); cameraThread.Start(); cameraThread.Join();
    if (cameraFailure is not null) throw cameraFailure;
    var aiReload = new XmlSceneParser().LoadFromString(aiExport);
    Require(aiReload.AICharacters.Count == 1 && aiReload.AICharacters[0].Kind == "Enemy" &&
        aiReload.AICharacters[0].StartDelay == .25 && !aiReload.AICharacters[0].SpawnOnStart &&
        aiReload.AIGroups[0].CharacterIds.Contains(aiReload.AICharacters[0].Id),
        "AI characters or groups changed during room XML round trip");
    aiDocument.PlayerSkinFiles.Add("1.xml");
    aiDocument.PlayerSkinFiles.Add("custom:runner");
    var appearanceReload = new XmlSceneParser().LoadFromString(new Exporter().Export(aiDocument));
    Require(appearanceReload.PlayerSkinFiles.SequenceEqual(["1.xml", "custom:runner"]),
        "player appearance layers changed during room XML round trip");
    var actionRoom = LevelDocument.Empty();
    var triggerNode = new LevelNode { Name = "StartEvent", Kind = LevelNodeKind.Trigger, X = 40, Y = 50,
        Width = 100, Height = 200, TriggerTarget = "project", TriggerAction = "Send Event", TriggerValue = "Content.Zone:Z3" };
    triggerNode.TriggerContentXml = TriggerActionService.BuildContent(actionRoom, triggerNode).ToString(SaveOptions.DisableFormatting);
    actionRoom.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor).Children.Add(triggerNode);
    var actionExport = new Exporter().Export(actionRoom);
    Require(actionExport.Contains("ExecuteCall") && actionExport.Contains("Content.Zone:Z3"),
        "project event trigger did not export a runtime action");
    var actionReload = new XmlSceneParser().LoadFromString(actionExport);
    var reloadedTrigger = actionReload.SceneNodes.Single(node => node.Kind == LevelNodeKind.Trigger);
    Require(reloadedTrigger.TriggerTarget == "project" && reloadedTrigger.TriggerAction == "Send Event" &&
        reloadedTrigger.TriggerValue == "Content.Zone:Z3" && reloadedTrigger.TriggerContentXml.Contains("ExecuteCall"),
        "trigger target or runtime content was lost when reopening a room");
    reloadedTrigger.TriggerTarget = "player"; reloadedTrigger.TriggerAction = "Trick"; reloadedTrigger.TriggerValue = "MonkeyVault";
    reloadedTrigger.TriggerContentXml = TriggerActionService.BuildContent(actionReload, reloadedTrigger).ToString(SaveOptions.DisableFormatting);
    Require(new Exporter().Export(actionReload).Contains("ForceAnimation Name=\"MonkeyVault\""),
        "player trick trigger was not exported as a game action");
    var runner = new AICharacterDefinition { Name = "Runner", AIChannel = 1 };
    var hunter = new AICharacterDefinition { Name = "Hunter", AIChannel = 2 };
    actionRoom.AICharacters.Add(runner); actionRoom.AICharacters.Add(hunter);
    var group = new AIGroupDefinition { Name = "Team" };
    group.CharacterIds.Add(runner.Id); group.CharacterIds.Add(hunter.Id); actionRoom.AIGroups.Add(group);
    var groupTrigger = new LevelNode { Name = "GroupRun", Kind = LevelNodeKind.Trigger, Width = 130, Height = 240,
        TriggerTarget = "group:" + group.Id, TriggerAction = "RunForward" };
    groupTrigger.TriggerContentXml = TriggerActionService.BuildContent(actionRoom, groupTrigger).ToString(SaveOptions.DisableFormatting);
    actionRoom.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor).Children.Add(groupTrigger);
    var groupXml = XDocument.Parse(new Exporter().Export(actionRoom));
    Require(groupXml.Descendants("Object").Any(item => (string?)item.Attribute("EditorAIAction") == "RunForward" &&
        item.Element("Content")?.Elements("Trigger").Count() == 2),
        "AI group trigger did not export separate runtime triggers per character");
    var groupReload = new XmlSceneParser().LoadFromString(groupXml.ToString());
    Require(groupReload.SceneNodes.Count(node => node.Name == "GroupRun") == 1 &&
        groupReload.SceneNodes.Single(node => node.Name == "GroupRun").Width == 130,
        "group trigger wrapper was not preserved when reopening a room");
    groupTrigger.TriggerTarget = "character:" + runner.Id; groupTrigger.TriggerAction = "Spawn";
    groupTrigger.TriggerContentXml = TriggerActionService.BuildContent(actionRoom, groupTrigger).ToString(SaveOptions.DisableFormatting);
    var spawnXml = XDocument.Parse(new Exporter().Export(actionRoom));
    Require(spawnXml.Descendants("Object").Any(item => (string?)item.Attribute("EditorAIAction") == "Spawn" &&
        item.Element("Content")?.Element("Spawn") is not null),
        "AI spawn trigger did not create the in-game spawn marker wrapper");
    groupTrigger.TriggerContentXml = "";
    Require(new Exporter().Export(actionRoom).Contains("OnlyIfDisabledAI"),
        "valid Spawn metadata did not regenerate an outdated inspector cache");
    groupTrigger.TriggerAction = "";
    try { new Exporter().Export(actionRoom); throw new Exception("incomplete AI action exported as an empty trigger"); }
    catch (InvalidDataException) { }
    groupTrigger.TriggerAction = "Spawn";
    Require(GameplayEffectCatalog.CardEffects.Select(effect => effect.Id).SequenceEqual(
        ["None", "RestoreChargesOnFloorStart", "FillChargesOnFloorStart"]),
        "Shop offers an effect the Mac runtime does not support");
    var effectCard = new XElement("CustomCard", new XElement("Level", new XAttribute("Amount", 99), new XAttribute("Points", 100)));
    GameplayEffectCatalog.ConfigureLevels(effectCard, "RestoreChargesOnFloorStart");
    Require((string?)effectCard.Element("Level")?.Attribute("Amount") == "2" &&
        (string?)effectCard.Element("Level")?.Attribute("Points") == "100",
        "changing a Shop effect did not preserve level power and install supported parameters");
    aiReload.AICharacters.Add(new AICharacterDefinition { Name = "Hunter", AIChannel = 3 });
    try { new Exporter().Export(aiReload); throw new Exception("Duplicate AI settings were exported"); }
    catch (InvalidDataException) { }
    var playerModelSource = Path.Combine(root, "runner_model.xml");
    Directory.CreateDirectory(root);
    File.WriteAllText(playerModelSource, "<Scene><Nodes><Hip Type=\"Node\"/></Nodes></Scene>");
    var modelFolder = Path.Combine(root, "custom_models");
    var importedModel = CustomModelCatalogService.Import(modelFolder, playerModelSource, "Runner Gear", "Body", "Tester");
    Require(importedModel.Reference == "custom:runner_gear" &&
        CustomModelCatalogService.Load(modelFolder).Single().Name == "Runner Gear",
        "Player Designer model package did not install a readable manifest");
    var swarmRoom = LevelDocument.Empty();
    var swarmStart = SwarmDesignerService.CreateBasic(swarmRoom, "Test");
    Require(SwarmDesignerService.Validate(swarmRoom).Count == 0 &&
        SwarmDesignerService.Connections(swarmStart).Single().Name == "End" &&
        SwarmDesignerService.References(swarmRoom, "SwarmActivator").Single()
            .LibraryOverrides.GetValueOrDefault("SpawnPoint") == "Start",
        "basic Swarm setup is missing runtime pieces or path links");
    SwarmDesignerService.SetMotion(swarmStart, "Speed", "5");
    SwarmDesignerService.RenameWaypoint(swarmRoom,
        SwarmDesignerService.Waypoints(swarmRoom).Single(node => node.Name == "End"), "Finish");
    var swarmExport = new Exporter().Export(swarmRoom);
    var swarmReload = new XmlSceneParser().LoadFromString(swarmExport);
    Require(SwarmDesignerService.Connections(SwarmDesignerService.Waypoints(swarmReload).Single(node => node.Name == "Start"))
            .Single().Name == "Finish" &&
        SwarmDesignerService.Motion(SwarmDesignerService.Waypoints(swarmReload).Single(node => node.Name == "Start"), "Speed") == "5" &&
        SwarmDesignerService.References(swarmReload, "SwarmActivator").Single()
            .LibraryOverrides.GetValueOrDefault("SpawnPoint") == "Start" &&
        SwarmDesignerService.Validate(swarmReload).Count == 0,
        "Swarm waypoint paths, motion or activator overrides changed during room XML round trip");
    var entrance = StructuralRoomService.Create(StructuralRoomKind.Entrance);
    var exitRoom = StructuralRoomService.Create(StructuralRoomKind.Exit);
    Require(StructuralRoomService.Validate(entrance).Count == 0 &&
        StructuralRoomService.Validate(exitRoom).Count == 0,
        "Entrance or Exit Designer template is missing required room pieces");
    var entranceReload = new XmlSceneParser().LoadFromString(new Exporter().Export(entrance), entrance.Name);
    Require(entranceReload.StructuralRoomKind == StructuralRoomKind.Entrance &&
        entranceReload.SceneNodes.Any(node => node.Kind == LevelNodeKind.Waypoint && node.Name == "DefaultSpawn") &&
        StructuralRoomService.Validate(entranceReload).Count == 0,
        "Entrance room markers or structural metadata did not round trip");
    var layoutRoom = LevelDocument.Empty();
    var layoutNodes = layoutRoom.SceneNodes.ToList();
    var layoutIn = layoutNodes.Single(node => node.Kind == LevelNodeKind.GateIn);
    var layoutOut = layoutNodes.Single(node => node.Kind == LevelNodeKind.GateOut);
    var layoutFloor = layoutNodes.Single(node => node.Kind == LevelNodeKind.Platform);
    layoutRoom.SelectedNodeIds.Add(layoutIn.Id);
    var startLayout = RoomLayoutService.Create(layoutRoom, RoomLayoutSection.Start, "Opening");
    layoutRoom.SelectedNodeIds.Clear();
    layoutRoom.SelectedNodeIds.Add(layoutFloor.Id);
    var middleLayout = RoomLayoutService.Create(layoutRoom, RoomLayoutSection.Middle, "Route A");
    var secondMiddle = new LevelNode { Name = "Alternative Route", Kind = LevelNodeKind.Platform, X = 300, Y = 80 };
    layoutRoom.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor)
        .Children.Add(secondMiddle);
    layoutRoom.SelectedNodeIds.Clear();
    layoutRoom.SelectedNodeIds.Add(secondMiddle.Id);
    RoomLayoutService.Create(layoutRoom, RoomLayoutSection.Middle, "Route B");
    layoutRoom.SelectedNodeIds.Clear();
    layoutRoom.SelectedNodeIds.Add(layoutOut.Id);
    RoomLayoutService.Create(layoutRoom, RoomLayoutSection.Finish, "Ending");
    RoomLayoutService.SetParent(layoutRoom, middleLayout, startLayout);
    Require(RoomLayoutService.Options(layoutRoom).Where(option => option.Section == RoomLayoutSection.Middle)
        .All(option => option.Parent.EndsWith("Start_Zone.Opening", StringComparison.OrdinalIgnoreCase)),
        "linking one Middle layout left a sibling at the root as a second game choice");
    var layoutExport = new Exporter().Export(layoutRoom);
    var layoutXml = System.Xml.Linq.XDocument.Parse(layoutExport);
    Require((string?)layoutXml.Root?.Attribute("EditorRoomLayouts") == "1" &&
        layoutXml.Descendants("Track").Elements("Properties").Descendants("Choice")
            .Any(element => (string?)element.Attribute("Name") == "Start_Zone") &&
        layoutXml.Descendants("Platform").Descendants("Selection")
            .Any(element => (string?)element.Attribute("Choice") == "Middle_Zone" &&
                ((string?)element.Attribute("Parent"))?.Contains("Start_Zone.Opening") == true),
        "Room Layouts did not export Track choices and object guards");
    var layoutReload = new XmlSceneParser().LoadFromString(layoutExport);
    Require(RoomLayoutService.Options(layoutReload).Count == 4 &&
        RoomLayoutService.Validate(layoutReload).Count == 0,
        "Room Layouts choices changed during room XML round trip");
    var navigation = new MainWindowViewModel();
    navigation.OpenProjectManagerCommand.Execute(null);
    Require(navigation.IsProjectManagerOpen && navigation.IsProjectManagerVisible, "Tools did not open and select the Project Manager tab");
    navigation.SelectDocumentCommand.Execute(navigation.SelectedDocument);
    Require(navigation.IsProjectManagerOpen && !navigation.IsProjectManagerVisible, "selecting a room removed the open Project Manager tab");
    var expectedStudios = ProjectSection.All.Where(section => section.Name != "Overview").Select(section => section.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Require(expectedStudios.SetEquals(ProjectStudioRouteFactory.SupportedSections), "one or more Project Manager sections fall back to a generic browser");
    var projectRoot = Path.Combine(root, "Project");
    var manifest = new ProjectManifestService();
    var project = manifest.Create(projectRoot);
    project.Name = "Verification Project";
    manifest.Save(project);
    var saveRoot = Path.Combine(root, "GameData", "userdata");
    Directory.CreateDirectory(saveRoot);
    var savePath = Path.Combine(saveRoot, "user.xml");
    File.WriteAllText(savePath, "<User Name=\"Tester\"><Items><Stash><Item Name=\"Money\"><Group Name=\"Countable\" Quantity=\"10\"/></Item><Item Name=\"MoneyPremium\"><Group Name=\"Countable\" Quantity=\"20\"/></Item><Item Name=\"Points\"><Group Name=\"Countable\" Quantity=\"30\"/></Item></Stash><Equipped><Item Name=\"Starter\" ItemType=\"StarterPack\"/><Item Name=\"Helmet\"><Group Name=\"ST_MyGadgets\" ST_Slot=\"Head\"/></Item></Equipped></Items><UserProperties><Energy Level=\"40\"/><Zones Current=\"old\" CurrentCustom=\"z1\"/></UserProperties><UserCounters><Namespace Name=\"ST_Statistics\"><Counter Name=\"CardsCount\" Value=\"50\"/><Counter Name=\"ST_run_counter\" Value=\"60\"/></Namespace></UserCounters></User>");
    var profile = PlayerSaveService.Load(savePath, Path.GetDirectoryName(saveRoot));
    Require(profile.Credits == "10" && profile.Zone == "z1" && profile.Equipment.Single().Slot == "Head",
        "player save profile does not match the Swift player overview");
    var edits = new Dictionary<string, string> {
        ["Credits"] = "11", ["Premium"] = "21", ["Points"] = "31", ["Energy"] = "41",
        ["Cards"] = "51", ["Runs"] = "61", ["Current custom zone ID"] = "z2"
    };
    var beforeInvalid = File.ReadAllText(savePath);
    edits["Credits"] = "-1";
    try { PlayerSaveService.Save(savePath, edits); throw new Exception("Invalid player save value was accepted"); }
    catch (InvalidDataException) { }
    Require(File.ReadAllText(savePath) == beforeInvalid, "invalid player save changed the file");
    edits["Credits"] = "11";
    var saveBackup = PlayerSaveService.Save(savePath, edits);
    Require(File.ReadAllText(saveBackup) == beforeInvalid &&
        PlayerSaveService.Load(savePath, Path.GetDirectoryName(saveRoot)).Credits == "11",
        "player save did not preserve a backup or apply edits");
    var incompleteSave = Path.Combine(saveRoot, "incomplete.xml");
    File.WriteAllText(incompleteSave, "<User><Items/></User>");
    try { PlayerSaveService.Save(incompleteSave, edits); throw new Exception("Incomplete player save was accepted"); }
    catch (InvalidDataException) { }
    Require(File.ReadAllText(incompleteSave) == "<User><Items/></User>", "missing player save nodes caused a partial write");
    var animation = Path.Combine(root, "sample.bytes");
    using (var binary = new BinaryWriter(File.Create(animation)))
    {
        binary.Write(2);
        for (var frame = 0; frame < 2; frame++)
        {
            binary.Write((byte)0);
            binary.Write(46);
            for (var point = 0; point < 46; point++) { binary.Write((float)point); binary.Write((float)frame); binary.Write(0f); }
        }
    }
    Require(GameTrickPreviewService.ValidateImportedFrames(animation) == 2, "valid Vector animation was rejected");
    var trickFolder = Path.Combine(root, "TrickPackageVerification");
    var trickPath = CustomTrickPackageService.SaveNew(trickFolder,
        new CustomTrickDraft("TestTrick", "Test Trick", "A test", animation, "DetectorH", 0, 0, 1, "RunForward", 1, "", 1, 1100));
    var trick = System.Xml.Linq.XDocument.Load(trickPath);
    var catalogProject = Path.Combine(root, "persistent-catalog");
    var persistentPackage = Path.Combine(catalogProject, "custom_tricks", "TestTrick");
    Directory.CreateDirectory(persistentPackage);
    foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(trickPath)!)) File.Copy(file, Path.Combine(persistentPackage, Path.GetFileName(file)));
    File.Delete(animation);
    Require(GameTrickPreviewService.LoadCustomMoves(catalogProject).Single().Name == "TestTrick" &&
        GameTrickPreviewService.LoadCustomMoves(catalogProject).Single().FileName == Path.Combine(persistentPackage, "TestTrick.bytes"), "Saved custom animation depends on the original import file or is missing from the persistent catalog");
    File.Copy(Path.Combine(persistentPackage, "TestTrick.bytes"), animation);
    Require((string?)trick.Root?.Attribute("EndFrame") == "1" && trick.Root?.Elements("Level").Count() == 5 &&
        File.Exists(Path.Combine(Path.GetDirectoryName(trickPath)!, "TestTrick.bytes")),
        "custom animation package has the wrong timeline or missing files");
    var overridePath = CustomTrickPackageService.SaveOverride(trickFolder, "RunForward", animation, 2);
    Require((string?)System.Xml.Linq.XDocument.Load(overridePath).Root?.Attribute("Target") == "RunForward" &&
        File.Exists(Path.Combine(Path.GetDirectoryName(overridePath)!, "replacement.bytes")),
        "animation override package is incomplete");
    Require(CustomTrickPackageService.RestoreDefault(trickFolder, "RunForward") &&
        !Directory.Exists(Path.GetDirectoryName(overridePath)),
        "restoring a built-in animation left an override installed");
    var invalidAnimation = Path.Combine(root, "invalid.bytes");
    File.WriteAllBytes(invalidAnimation, [1, 0, 0, 0, 0, 45, 0, 0, 0]);
    try { GameTrickPreviewService.ValidateImportedFrames(invalidAnimation); throw new Exception("Invalid Vector animation was accepted"); }
    catch (InvalidDataException) { }
    var previewTemplates = new ProjectTemplateService();
    foreach (var section in new[] { "Content", "Chapters", "Zones", "Story", "Trigger Designer", "Protocols", "Shop" })
        previewTemplates.Create(project, ProjectSection.All.Single(item => item.Name == section), "preview_" + section.Replace(' ', '_'));
    RunSta(() =>
    {
        var app = new Vector2LevelEditor.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StudioParityRegressionTests.Run(root);
        var inspectorRoom = LevelDocument.Empty();
        var inspectorActor = new AICharacterDefinition { Name = "Inspector Guard", AIChannel = 1 };
        inspectorRoom.AICharacters.Add(inspectorActor);
        var inspectorFactor = inspectorRoom.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor);
        var inspectorSpawn = new LevelNode { Kind = LevelNodeKind.Trigger, Name = "Guard entrance",
            TriggerTarget = "character:" + inspectorActor.Id, TriggerAction = "Spawn" };
        var inspectorActivate = new LevelNode { Kind = LevelNodeKind.Trigger, Name = "Activate guard",
            TriggerTarget = "character:" + inspectorActor.Id, TriggerAction = "Activate Spawn" };
        inspectorFactor.Children.Add(inspectorSpawn); inspectorFactor.Children.Add(inspectorActivate);
        var actionInspector = new TriggerActionInspector { Document = inspectorRoom, SelectedNode = inspectorActivate };
        var inspectorHost = new Window { Content = actionInspector, Width = 330, Height = 380 };
        inspectorHost.Show(); inspectorHost.UpdateLayout();
        var inspectorChoices = FindVisuals<ComboBox>(actionInspector).ToArray();
        Require(inspectorChoices.Length == 3 && inspectorChoices[2].Visibility == Visibility.Visible,
            "Activate Spawn still requires a manually entered trigger ID");
        inspectorChoices[2].SelectedValue = inspectorSpawn.Id.ToString("D");
        Require(inspectorActivate.TriggerValue == inspectorSpawn.Id.ToString("D") &&
            inspectorActivate.TriggerContentXml.Contains("EditorAISpawnTrigger_" + inspectorSpawn.Id.ToString("D")),
            "Spawn picker did not update runtime activation content");
        inspectorChoices[0].SelectedValue = "project";
        Require(inspectorActivate.TriggerAction == "Send Event" && inspectorActivate.TriggerValue == "",
            "Changing targets retained an incompatible AI action");
        inspectorChoices[0].SelectedValue = "player";
        inspectorChoices[1].SelectedItem = "StopMoveRun";
        Require(inspectorActivate.TriggerContentXml.Contains("Control"), "Player StopMoveRun picker did not generate runtime control");
        inspectorHost.Close();
        var main = new MainWindow();
        var mainModel = (MainWindowViewModel)main.DataContext;
        var editorWorkspace = (FrameworkElement)main.FindName("EditorWorkspace")!;
        var projectWorkspace = (FrameworkElement)main.FindName("ProjectManagerWorkspace")!;
        main.Show();
        main.UpdateLayout();
        Console.WriteLine($"Initial workspace: editor={editorWorkspace.Visibility}, project={projectWorkspace.Visibility}, model={mainModel.IsProjectManagerVisible}");
        Require(editorWorkspace.Visibility == Visibility.Visible &&
            projectWorkspace.Visibility == Visibility.Collapsed,
            "the level editor is not the initial workspace");
        var firstRoom = mainModel.SelectedDocument;
        var backKey = typeof(MainWindow).GetMethod("OnEditorKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var screen in new[] { "Settings", "Asset Browser" })
        {
            mainModel.CenterTab = screen;
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(main)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
            backKey.Invoke(main, [main, escape]);
            Require(escape.Handled && mainModel.CenterTab == "Canvas", "Escape failed to return from " + screen);
        }
        mainModel.OpenProjectManagerCommand.Execute(null);
        var projectEscape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(main)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
        backKey.Invoke(main, [main, projectEscape]);
        Require(!mainModel.IsProjectManagerVisible && mainModel.IsProjectManagerOpen, "Escape closed the Project Manager tab instead of navigating back");
        var extraTab = LevelDocument.Empty(); mainModel.Documents.Add(extraTab);
        var middle = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Middle) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent };
        typeof(MainWindow).GetMethod("DocumentTab_PreviewMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [new Border { DataContext = extraTab }, middle]);
        Require(middle.Handled && !mainModel.Documents.Contains(extraTab) && mainModel.Documents.Contains(firstRoom), "Middle click did not close only the clicked document tab");
        mainModel.OpenRoomLayoutsCommand.Execute(null);
        main.UpdateLayout();
        var roomLayoutPanel = (RoomLayoutPanel)main.FindName("RoomLayoutWorkspace")!;
        Require(roomLayoutPanel.Visibility == Visibility.Visible &&
            editorWorkspace.Visibility == Visibility.Visible,
            "Tools Room Layouts did not open under the active room canvas");
        var panelIn = firstRoom.SceneNodes.Single(node => node.Kind == LevelNodeKind.GateIn);
        mainModel.SelectNodeCommand.Execute(panelIn);
        ((TextBox)roomLayoutPanel.FindName("StartName")!).Text = "Opening";
        typeof(RoomLayoutPanel).GetMethod("CreateStart_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(roomLayoutPanel, [null, new RoutedEventArgs()]);
        Require(RoomLayoutService.Options(firstRoom).Any(option => option.Section == RoomLayoutSection.Start &&
            option.Variant == "Opening"), "Room Layouts create control did not assign the selected object");
        main.UpdateLayout();
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } layoutScreenshotDirectory)
        {
            Directory.CreateDirectory(layoutScreenshotDirectory);
            var layoutBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            layoutBitmap.Render(main);
            var layoutEncoder = new PngBitmapEncoder();
            layoutEncoder.Frames.Add(BitmapFrame.Create(layoutBitmap));
            using var layoutOutput = File.Create(Path.Combine(layoutScreenshotDirectory, "Room-Layouts.png"));
            layoutEncoder.Save(layoutOutput);
        }
        mainModel.CloseRoomLayoutsCommand.Execute(null);
        Require(roomLayoutPanel.Visibility == Visibility.Collapsed, "Room Layouts did not close");
        RoomLayoutService.MakeShared([panelIn]);
        firstRoom.SelectedNodeIds.Clear();
        mainModel.OpenEntranceStudioCommand.Execute(null);
        main.UpdateLayout();
        Require(mainModel.SelectedDocument.StructuralRoomKind == StructuralRoomKind.Entrance &&
            mainModel.StructuralRoomBarVisibility == Visibility.Visible &&
            FindVisual<StructuralRoomStudioBar>(editorWorkspace) is not null,
            "Tools Entrance Designer did not open an editable structural room");
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } structuralScreenshotDirectory)
        {
            Directory.CreateDirectory(structuralScreenshotDirectory);
            var structuralBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            structuralBitmap.Render(main);
            var structuralEncoder = new PngBitmapEncoder();
            structuralEncoder.Frames.Add(BitmapFrame.Create(structuralBitmap));
            using var structuralOutput = File.Create(Path.Combine(structuralScreenshotDirectory, "Entrance-Designer.png"));
            structuralEncoder.Save(structuralOutput);
        }
        mainModel.OpenExitStudioCommand.Execute(null);
        Require(mainModel.SelectedDocument.StructuralRoomKind == StructuralRoomKind.Exit &&
            StructuralRoomService.Validate(mainModel.SelectedDocument).Count == 0,
            "Tools Exit Designer did not open a valid exit room");
        mainModel.SelectDocumentCommand.Execute(firstRoom);
        mainModel.OpenAIStudioCommand.Execute(null);
        var aiWorkspace = (AIStudioView)main.FindName("AIStudioWorkspace")!;
        main.UpdateLayout();
        Require(aiWorkspace.Visibility == Visibility.Visible && editorWorkspace.Visibility == Visibility.Collapsed &&
            ReferenceEquals(aiWorkspace.Document, mainModel.SelectedDocument),
            "Tools AI Designer did not open a separate editor tab for the room");
        mainModel.SelectedDocument.AICharacters.Add(new AICharacterDefinition { Name = "Friendly1", AIChannel = 1 });
        main.UpdateLayout();
        var aiPreview = (TrickPreviewControl)aiWorkspace.FindName("Preview")!;
        Require(aiPreview.CenterOnVisibleModel && !aiPreview.ShowDebugEdges && !aiPreview.ShowModelOutlines && !aiPreview.ShowModelNodePoints,
            "AI Designer preview still exposes model joints or uses the in-room anchor");
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } aiScreenshotDirectory)
        {
            Directory.CreateDirectory(aiScreenshotDirectory);
            var aiBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            aiBitmap.Render(main);
            var aiEncoder = new PngBitmapEncoder();
            aiEncoder.Frames.Add(BitmapFrame.Create(aiBitmap));
            using var aiOutput = File.Create(Path.Combine(aiScreenshotDirectory, "AI-Designer.png"));
            aiEncoder.Save(aiOutput);
        }
        mainModel.SelectDocumentCommand.Execute(mainModel.SelectedDocument);
        Require(aiWorkspace.Visibility == Visibility.Collapsed && editorWorkspace.Visibility == Visibility.Visible &&
            mainModel.AIStudioTabVisibility == Visibility.Visible,
            "AI Designer replaced the level editor or lost its tab");
        mainModel.SelectAIStudioCommand.Execute(null);
        Require(aiWorkspace.Visibility == Visibility.Visible, "AI Designer tab did not reopen");
        mainModel.CloseAIStudioCommand.Execute(null);
        Require(editorWorkspace.Visibility == Visibility.Visible && mainModel.AIStudioTabVisibility == Visibility.Collapsed,
            "AI Designer tab did not close back to the level editor");
        mainModel.OpenPlayerStudioCommand.Execute(null);
        var playerWorkspace = (PlayerStudioView)main.FindName("PlayerStudioWorkspace")!;
        main.UpdateLayout();
        Require(playerWorkspace.Visibility == Visibility.Visible && editorWorkspace.Visibility == Visibility.Collapsed &&
            ReferenceEquals(playerWorkspace.Document, mainModel.SelectedDocument),
            "Tools Player Designer did not open for the active room");
        var playerPreview = (TrickPreviewControl)playerWorkspace.FindName("Preview")!;
        Require(playerPreview.CenterOnVisibleModel && playerPreview.ClipToBounds && !playerPreview.ShowDebugEdges &&
            !playerPreview.ShowModelOutlines && !playerPreview.ShowModelNodePoints && playerPreview.Playback is { Frames.Count: > 0 },
            "Player Designer preview is not bounded and fitted to the visible model");
        var animationPicker = (ComboBox)playerWorkspace.FindName("PreviewAnimation")!;
        for (var animationIndex = 0; animationIndex < animationPicker.Items.Count; animationIndex++)
        {
            animationPicker.SelectedIndex = animationIndex;
            main.UpdateLayout();
            Require(playerPreview.Playback is { Frames.Count: > 0 },
                $"Player Designer animation {animationIndex} did not load a model preview");
            if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } previewScreenshotDirectory)
            {
                Directory.CreateDirectory(previewScreenshotDirectory);
                var previewBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                previewBitmap.Render(main);
                var previewEncoder = new PngBitmapEncoder();
                previewEncoder.Frames.Add(BitmapFrame.Create(previewBitmap));
                using var previewOutput = File.Create(Path.Combine(previewScreenshotDirectory, $"Player-Designer-Animation-{animationIndex}.png"));
                previewEncoder.Save(previewOutput);
            }
        }
        animationPicker.SelectedIndex = 0;
        main.UpdateLayout();
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } playerScreenshotDirectory)
        {
            Directory.CreateDirectory(playerScreenshotDirectory);
            var playerBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            playerBitmap.Render(main);
            var playerEncoder = new PngBitmapEncoder();
            playerEncoder.Frames.Add(BitmapFrame.Create(playerBitmap));
            using var playerOutput = File.Create(Path.Combine(playerScreenshotDirectory, "Player-Designer.png"));
            playerEncoder.Save(playerOutput);
        }
        mainModel.SelectDocumentCommand.Execute(mainModel.SelectedDocument);
        Require(playerWorkspace.Visibility == Visibility.Collapsed && mainModel.PlayerStudioTabVisibility == Visibility.Visible,
            "Player Designer replaced the level editor or lost its tab");
        mainModel.SelectPlayerStudioCommand.Execute(null);
        Require(playerWorkspace.Visibility == Visibility.Visible, "Player Designer tab did not reopen");
        mainModel.ClosePlayerStudioCommand.Execute(null);
        Require(editorWorkspace.Visibility == Visibility.Visible && mainModel.PlayerStudioTabVisibility == Visibility.Collapsed,
            "Player Designer did not close back to the level editor");
        typeof(MainWindow).GetMethod("SwarmDesignerMenu_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(main, [null, new RoutedEventArgs()]);
        var swarmWindow = (SwarmDesignerWindow?)typeof(MainWindow)
            .GetField("_swarmDesigner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
        Require(swarmWindow is { IsVisible: true } && editorWorkspace.Visibility == Visibility.Visible,
            "Tools Swarm Designer did not open over the active room");
        typeof(SwarmDesignerWindow).GetMethod("CreateBasic_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(swarmWindow, [null, new RoutedEventArgs()]);
        Require(SwarmDesignerService.Validate(mainModel.SelectedDocument).Count == 0,
            "Swarm Designer setup button did not create a valid room path");
        swarmWindow!.UpdateLayout();
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } swarmScreenshotDirectory)
        {
            Directory.CreateDirectory(swarmScreenshotDirectory);
            var swarmBitmap = new RenderTargetBitmap((int)swarmWindow.ActualWidth, (int)swarmWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            swarmBitmap.Render(swarmWindow);
            var swarmEncoder = new PngBitmapEncoder();
            swarmEncoder.Frames.Add(BitmapFrame.Create(swarmBitmap));
            using var swarmOutput = File.Create(Path.Combine(swarmScreenshotDirectory, "Swarm-Designer.png"));
            swarmEncoder.Save(swarmOutput);
        }
        swarmWindow.Close();
        mainModel.OpenProjectManagerCommand.Execute(null);
        Require(editorWorkspace.Visibility == Visibility.Collapsed &&
            projectWorkspace.Visibility == Visibility.Visible,
            "opening Project Manager did not switch to its own workspace");
        var activeProject=(ProjectManagerViewModel)projectWorkspace.DataContext;
        typeof(ProjectManagerViewModel).GetMethod("LoadProject",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(activeProject,[project]);
        activeProject.SearchText = "rooms";
        activeProject.SearchFocused = true;
        Require(activeProject.SearchHits.Any(hit => hit.TargetSection?.Name == "Content"),
            "global project search did not find rooms by alias");
        main.UpdateLayout();
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } searchScreenshotDirectory)
        {
            var waitFrame = new System.Windows.Threading.DispatcherFrame();
            var waitTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            waitTimer.Tick += (_, _) => { waitTimer.Stop(); waitFrame.Continue = false; }; waitTimer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(waitFrame);
            var searchPanel = (Border)((ProjectManagerWindow)projectWorkspace).FindName("SearchResultsPanel")!;
            Require(searchPanel.Opacity > .99, "Search results remained dim after their entrance animation");
            Require(FindVisuals<Border>(searchPanel).Any(border => border.Background is ImageBrush && border.Effect is System.Windows.Media.Effects.BlurEffect), "Search panel lacks its frosted backdrop");
            Directory.CreateDirectory(searchScreenshotDirectory);
            var searchBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            searchBitmap.Render(main);
            var searchEncoder = new PngBitmapEncoder();
            searchEncoder.Frames.Add(BitmapFrame.Create(searchBitmap));
            using var searchOutput = File.Create(Path.Combine(searchScreenshotDirectory, "Project-Search.png"));
            searchEncoder.Save(searchOutput);
        }
        activeProject.SearchText = "";
        activeProject.SearchFocused = false;
        activeProject.SelectedSection=ProjectSection.All.Single(section=>section.Name=="Shop");
        main.UpdateLayout();
        var host=FindVisual<ProjectStudioHost>(projectWorkspace) ?? throw new Exception("Project Studio host was not created.");
        var studioContent=(ContentControl)host.FindName("StudioContent")!;
        var originalStudio=studioContent.Content as CommerceStudioView ?? throw new Exception("Shop did not open a dedicated studio.");
        Require(!((Grid)typeof(CommerceStudioView).GetField("_shell",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(originalStudio)!).IsEnabled && !mainModel.RefreshShopOffersCommand.CanExecute(null),
            "Release shop controls or the refresh workaround remain enabled");
        Require(FindVisuals<TextBlock>(originalStudio).Any(text => text.Text.StartsWith("Shop disabled for this release")),
            "Disabled shop lacks a visible release notice");
        var shopCatalogue = (ListBox)typeof(CommerceStudioView).GetField("_catalogue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(originalStudio)!;
        Require(shopCatalogue.Background is SolidColorBrush catalogueBrush && catalogueBrush.Color == ((SolidColorBrush)app.Resources["ProjectManagerCardBrush"]).Color,
            "Shop catalogue does not use the current theme background");
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR") is { Length: > 0 } shopScreenshotDirectory)
        {
            EditorTheme.Apply(true); main.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(main); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(shopScreenshotDirectory, "Shop-Disabled-Dark.png")); encoder.Save(output);
            EditorTheme.Apply(mainModel.IsDarkMode);
        }
        var draftInputs=(Dictionary<string,TextBox>)typeof(CommerceStudioView).GetField("_inputs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(originalStudio)!;
        draftInputs["VisualName"].Text="Unsaved Draft";
        typeof(ProjectStudioHost).GetMethod("SetStatus",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,["Validation message"]);
        Require(ReferenceEquals(studioContent.Content,originalStudio) && draftInputs["VisualName"].Text=="Unsaved Draft",
            "a Project Manager status message discarded unsaved edits");
        mainModel.SelectDocumentCommand.Execute(mainModel.SelectedDocument);
        mainModel.OpenProjectManagerCommand.Execute(null);
        main.UpdateLayout();
        Require(ReferenceEquals(studioContent.Content,originalStudio) && draftInputs["VisualName"].Text=="Unsaved Draft",
            "switching between the level editor and Project Manager discarded unsaved edits");
        ((ProjectManagerWindow)projectWorkspace).OpenCustomAnimations();
        main.UpdateLayout();
        Require(activeProject.SelectedSection.Name == "Tricks" &&
            studioContent.Content is TrickLibraryStudioView trickLibrary &&
            trickLibrary.Content is CustomTrickCreatorView,
            "Tools Custom Animations did not open the creator inside the Project Manager tab");
        foreach (var (menuSection, studioType) in new (string, Type)[] {
            ("Trigger Designer", typeof(TriggerStudioView)),
            ("Traps", typeof(TrapStudioView)),
            ("Obstacles", typeof(ObstacleLibraryStudioView)) })
        {
            ((ProjectManagerWindow)projectWorkspace).OpenStudioSection(menuSection);
            main.UpdateLayout();
            Require(activeProject.SelectedSection.Name == menuSection && studioContent.Content?.GetType() == studioType,
                $"Tools {menuSection} did not open its working studio");
        }
        activeProject.SelectedSection = ProjectSection.All.Single(section => section.Name == "Shop");
        main.UpdateLayout();
        var studioSurface=(Grid)originalStudio.Content;
        Vector2LevelEditor.Services.EditorTheme.Apply(true);
        Require(((SolidColorBrush)studioSurface.Background).Color==Color.FromRgb(37,42,49),"Project Manager studio stayed light in dark mode");
        var darkScreenshot = Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_DARK_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(darkScreenshot))
        {
            main.UpdateLayout();
            var darkBitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            darkBitmap.Render(main);
            var darkEncoder = new PngBitmapEncoder();
            darkEncoder.Frames.Add(BitmapFrame.Create(darkBitmap));
            using var darkOutput = File.Create(darkScreenshot);
            darkEncoder.Save(darkOutput);
        }
        Vector2LevelEditor.Services.EditorTheme.Apply(false);
        Require(((SolidColorBrush)studioSurface.Background).Color==Colors.White,"Project Manager studio stayed dark in light mode");
        activeProject.SelectedSection=ProjectSection.Overview;
        main.UpdateLayout();
        var screenshot = Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(screenshot))
        {
            main.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(main);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(screenshot);
            encoder.Save(output);
        }
        var screenshotDirectory = Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory) && projectWorkspace.DataContext is ProjectManagerViewModel projectModel)
        {
            Console.WriteLine($"Restored project status: {projectModel.Status}");
            Directory.CreateDirectory(screenshotDirectory);
            foreach (var section in ProjectSection.All.Select(item => item.Name))
            {
                projectModel.SelectedSection = ProjectSection.All.Single(item => item.Name == section);
                main.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(main);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(screenshotDirectory, section.Replace(' ', '-') + ".png"));
                encoder.Save(output);
            }
        }
        mainModel.SelectDocumentCommand.Execute(mainModel.SelectedDocument);
        Require(editorWorkspace.Visibility == Visibility.Visible &&
            projectWorkspace.Visibility == Visibility.Collapsed &&
            mainModel.IsProjectManagerOpen,
            "returning to the document did not keep Project Manager as a separate open tab");
        var inspectorTrigger = new LevelNode { Name = "PreviewTrigger", Kind = LevelNodeKind.Trigger, TriggerTarget = "player",
            TriggerAction = "Animation", TriggerValue = "RunForward" };
        mainModel.SelectedDocument.Nodes.SelectMany(node => node.Flatten()).First(node => node.Kind == LevelNodeKind.Factor)
            .Children.Add(inspectorTrigger);
        mainModel.SelectNode(inspectorTrigger);
        ((TabControl)main.FindName("RightDockTabs")!).SelectedIndex = 1;
        main.UpdateLayout();
        Require(FindVisual<TriggerActionInspector>(main) is { Visibility: Visibility.Visible },
            "selecting a trigger did not reveal the Mac-style Trigger Action inspector");
        mainModel.CloseProjectManagerCommand.Execute(null);
        Require(!mainModel.IsProjectManagerOpen && editorWorkspace.Visibility == Visibility.Visible,
            "closing the Project Manager tab removed the editor workspace");
        if (Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_SURFACES_DIR") is { Length: > 0 } surfacesDirectory)
        {
            Directory.CreateDirectory(surfacesDirectory);
            var previewAsset = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "ProjectAssets"), "*.png", SearchOption.AllDirectories).First();
            for (var index = 0; index < 8; index++) mainModel.Assets.Add(new TextureAsset("Test", TextureAssetKind.Texture, "All", $"Asset {index + 1}", "", previewAsset, "Default"));
            mainModel.AssetSearch = "Asset";
            foreach (var surface in new[] { "Asset Browser", "Settings" })
            {
                mainModel.SelectCenterTabCommand.Execute(surface);
                main.UpdateLayout();
                Thread.Sleep(250);
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                main.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(main);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(surfacesDirectory, surface.Replace(' ', '-') + ".png"));
                encoder.Save(output);
            }
            mainModel.SelectCenterTabCommand.Execute("Canvas");
        }
        main.Close();

        var projectManager = new ProjectManagerWindow();
        Require(projectManager.DataContext is ProjectManagerViewModel, "Project Manager failed during initial Overview layout");
        foreach (var section in ProjectStudioRouteFactory.SupportedSections)
        {
            var studio = ProjectStudioRouteFactory.Create(section, projectRoot, null, _ => { }, _ => { });
            if (studio is null) throw new Exception($"{section} did not construct a dedicated studio");
            studio.Measure(new Size(1200, 700));
            studio.Arrange(new Rect(0, 0, 1200, 700));
            studio.UpdateLayout();
            var populatedDirectory = Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_POPULATED_DIR");
            if (!string.IsNullOrWhiteSpace(populatedDirectory))
            {
                Directory.CreateDirectory(populatedDirectory!);
                studio.Measure(new Size(1200, 700));
                studio.Arrange(new Rect(0, 0, 1200, 700));
                studio.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(studio);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(populatedDirectory!, section.Replace(' ', '-') + ".png"));
                encoder.Save(output);
            }
        }
        var protocolPreviewStudio = new ProtocolStudioView(projectRoot, _ => { });
        protocolPreviewStudio.Measure(new Size(1200, 700));
        protocolPreviewStudio.Arrange(new Rect(0, 0, 1200, 700));
        protocolPreviewStudio.UpdateLayout();
        Require(FindVisual<TrickPreviewControl>(protocolPreviewStudio) is { CenterOnVisibleModel: true, ShowModelNodePoints: false },
            "Protocols is missing the fitted player appearance preview from the Mac studio");
        var chapterStudio = new ChapterZoneStudioView(project, ProjectSection.All.Single(section => section.Name == "Chapters"), _ => { });
        var chapterPath = Path.Combine(projectRoot, "custom_chapters", "preview_Chapters.xml");
        typeof(ChapterZoneStudioView).GetMethod("Edit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chapterStudio, [chapterPath]);
        chapterStudio.Measure(new Size(1000, 700)); chapterStudio.Arrange(new Rect(0, 0, 1000, 700)); chapterStudio.UpdateLayout();
        var addFloorButton = FindVisuals<Button>(chapterStudio).First(button => button.Content as string == "+ Add Start Button");
        for (var index = 1; index < 12; index++) addFloorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!addFloorButton.IsEnabled && FindVisuals<TextBlock>(chapterStudio).Any(block => block.Text == "12 of 12 menu start buttons"),
            "chapter editor allowed more than 12 game menu start buttons");
        var zoneStatus = "";
        var zoneStudio = new ChapterZoneStudioView(project, ProjectSection.All.Single(section => section.Name == "Zones"), value => zoneStatus = value);
        var zonePath = Directory.EnumerateFiles(Path.Combine(projectRoot, "custom_zones"), "*.xml").First();
        typeof(ChapterZoneStudioView).GetMethod("Edit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(zoneStudio, [zonePath]);
        zoneStudio.Measure(new Size(1000, 700)); zoneStudio.Arrange(new Rect(0, 0, 1000, 700)); zoneStudio.UpdateLayout();
        FindVisuals<Button>(zoneStudio).Single(button => button.Content as string == "Save Changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(zoneStatus.StartsWith("Saved "), "zone save was incorrectly rejected by chapter-only floor validation: " + zoneStatus);
        var trickStudio = new TrickLibraryStudioView(projectRoot, _ => { });
        typeof(TrickLibraryStudioView).GetMethod("ShowCreate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(trickStudio, null);
        Require(trickStudio.Content is CustomTrickCreatorView, "New Trick did not open the custom animation creator");
        var creator = (CustomTrickCreatorView)trickStudio.Content;
        var mode = (ComboBox)typeof(CustomTrickCreatorView).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!;
        mode.SelectedIndex = 1;
        var newOptions = (StackPanel)typeof(CustomTrickCreatorView).GetField("_newOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!;
        var overrideOptions = (StackPanel)typeof(CustomTrickCreatorView).GetField("_overrideOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!;
        Require(newOptions.Visibility == Visibility.Collapsed && overrideOptions.Visibility == Visibility.Visible,
            "override mode still shows the new-animation controls");
        mode.SelectedIndex = 0;
        var finish = (ComboBox)typeof(CustomTrickCreatorView).GetField("_finish", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!;
        var chainOptions = (StackPanel)typeof(CustomTrickCreatorView).GetField("_chainOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!;
        Require(chainOptions.Visibility == Visibility.Collapsed, "regular tricks show unused chain controls");
        finish.SelectedIndex = 1;
        Require(chainOptions.Visibility == Visibility.Visible, "chain tricks hide their exit controls");
        trickStudio.Measure(new Size(1200, 700));
        trickStudio.Arrange(new Rect(0, 0, 1200, 700));
        trickStudio.UpdateLayout();
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(trickStudio);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(screenshotDirectory, "Custom-Trick-Creator.png"));
            encoder.Save(output);
        }
        var importedAnimation = Path.Combine(AppContext.BaseDirectory, "GameData", "animations", "cs_swarm_idle.bytes");
        typeof(CustomTrickCreatorView).GetField("_animation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(creator, importedAnimation);
        typeof(CustomTrickCreatorView).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(creator,
            GameTrickPreviewService.ValidateImportedFrames(importedAnimation));
        ((TextBlock)typeof(CustomTrickCreatorView).GetField("_previewPrompt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(creator)!).Visibility = Visibility.Collapsed;
        typeof(CustomTrickCreatorView).GetMethod("RefreshPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(creator, null);
        Require(FindVisual<TrickPreviewControl>(creator)?.Playback is { Frames.Count: > 0 },
            "imported trick does not load the real game model into the preview");
        creator.UpdateLayout();
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(trickStudio);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(screenshotDirectory, "Imported-Trick-Preview.png"));
            encoder.Save(output);
        }
        var playerSaveStudio = new SaveProfileStudioView(projectRoot, Path.GetDirectoryName(saveRoot), _ => { });
        playerSaveStudio.Measure(new Size(1200, 700));
        playerSaveStudio.Arrange(new Rect(0, 0, 1200, 700));
        playerSaveStudio.UpdateLayout();
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            var bitmap = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(playerSaveStudio);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(screenshotDirectory, "Player-Save-Populated.png"));
            encoder.Save(output);
        }
        var mediaTestRoot = Path.Combine(root, "MediaVerification");
        var backgroundStudio = new ProjectMediaStudioView(mediaTestRoot, "Backgrounds", "custom_backgrounds", null, _ => { });
        typeof(ProjectMediaStudioView).GetMethod("WriteTag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(backgroundStudio, ["scene.png", "z3", "Loading"]);
        var tagsPath = Path.Combine(mediaTestRoot, "custom_backgrounds", "zone_backgrounds.xml");
        var tag = System.Xml.Linq.XDocument.Load(tagsPath).Root?.Element("Background");
        Require((string?)tag?.Attribute("File") == "scene.png" &&
            (string?)tag.Attribute("Zone") == "z3" && (string?)tag.Attribute("Role") == "Loading",
            "background zone/role tag does not match the Swift manifest");
        typeof(ProjectMediaStudioView).GetMethod("WriteTag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(backgroundStudio, ["scene.png", "", "Menu"]);
        Require(!System.Xml.Linq.XDocument.Load(tagsPath).Descendants("Background").Any(),
            "unassigning a background left a stale game tag");
        var audioRoot = Path.Combine(root, "AudioVerification");
        Directory.CreateDirectory(Path.Combine(audioRoot, "custom_audio"));
        File.WriteAllBytes(Path.Combine(audioRoot, "custom_audio", "run.wav"), [1, 2, 3]);
        var audioStatus = "";
        var audioStudio = new AudioStudioView(audioRoot, message => audioStatus = message);
        typeof(AudioStudioView).GetMethod("AddPool", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(audioStudio, ["Zone"]);
        var reference = (TextBox)typeof(AudioStudioView).GetField("_reference", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(audioStudio)!;
        reference.Text = "z3";
        var rows = (System.Collections.ObjectModel.ObservableCollection<AudioStudioView.AudioTrackRow>)typeof(AudioStudioView)
            .GetField("_trackRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audioStudio)!;
        rows.Add(new AudioStudioView.AudioTrackRow { Id = "run", Weight = 3, MinFloor = 1, MaxFloor = 5 });
        typeof(AudioStudioView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(audioStudio, [audioStudio, new RoutedEventArgs()]);
        var pool = System.Xml.Linq.XDocument.Load(Path.Combine(audioRoot, "custom_audio", "audio_manifest.xml"))
            .Descendants("Pool").Single();
        Require((string?)pool.Attribute("Scope") == "Zone" && (string?)pool.Attribute("Reference") == "z3" &&
            (string?)pool.Element("Track")?.Attribute("Id") == "run" && audioStatus.StartsWith("Saved"),
            "audio pool save did not produce the Swift schema");
        var previewCard = Path.Combine(projectRoot, "custom_upgrades", "preview_Shop", "card.xml");
        var previewXml = System.Xml.Linq.XDocument.Load(previewCard);
        previewXml.Root!.SetAttributeValue("FutureGameField", "preserve");
        previewXml.Save(previewCard);
        var cardStatus = "";
        var cardStudio = new CommerceStudioView(projectRoot, true, message => cardStatus = message);
        var inputs = (System.Collections.IDictionary)typeof(CommerceStudioView)
            .GetField("_inputs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cardStudio)!;
        var priceInput = (TextBox)inputs["Price"]!;
        var saveCard = typeof(CommerceStudioView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!;
        priceInput.Text = "invalid";
        saveCard.Invoke(cardStudio, null);
        Require(cardStatus.Contains("Price") && (string?)System.Xml.Linq.XDocument.Load(previewCard).Root?.Attribute("Price") == "1000",
            "invalid shop price changed the package");
        priceInput.Text = "1400";
        saveCard.Invoke(cardStudio, null);
        var savedCard = System.Xml.Linq.XDocument.Load(previewCard).Root;
        Require((string?)savedCard?.Attribute("Price") == "1400" &&
            (string?)savedCard.Attribute("FutureGameField") == "preserve",
            "shop save lost the new price or an unknown game field");
        var previewTrigger = Path.Combine(projectRoot, "custom_triggers", "preview_Trigger_Designer.xml");
        var triggerDocument = System.Xml.Linq.XDocument.Load(previewTrigger);
        triggerDocument.Root!.SetAttributeValue("FutureTriggerField", "preserve");
        triggerDocument.Save(previewTrigger);
        var triggerStudio = new TriggerStudioView(projectRoot, _ => { });
        var placementRoom = mainModel.SelectedDocument;
        var applyTarget = placementRoom.SceneNodes.First(node => node.Kind == LevelNodeKind.Platform);
        mainModel.SelectNodeCommand.Execute(applyTarget);
        var rootsBeforeApply = placementRoom.Nodes.ToArray();
        var siblingsBeforeApply = placementRoom.SceneNodes.Where(node => node.Id != applyTarget.Id).ToArray();
        var applyDraft = XElement.Parse(new Exporter().ExportSelection(applyTarget));
        applyDraft.SetAttributeValue("Name", "XML regression platform");
        applyDraft.SetAttributeValue("FutureRuntimeField", "preserve");
        placementRoom.RawXml = applyDraft.ToString();
        mainModel.ApplyRawXmlCommand.Execute(null);
        Require(placementRoom.SelectedNode?.Name == "XML regression platform" && placementRoom.SelectedNode.Id == applyTarget.Id &&
            placementRoom.Nodes.SequenceEqual(rootsBeforeApply) &&
            siblingsBeforeApply.All(node => placementRoom.SceneNodes.Contains(node)),
            "Apply XML replaced the room or removed unselected siblings");
        Require(XElement.Parse(new Exporter().ExportSelection(placementRoom.SelectedNode)).Attribute("FutureRuntimeField")?.Value == "preserve",
            "Apply XML discarded an unknown runtime attribute");
        var validSelection = placementRoom.SelectedNode;
        placementRoom.RawXml = "<Platform Name=\"broken\">";
        mainModel.ApplyRawXmlCommand.Execute(null);
        Require(ReferenceEquals(placementRoom.SelectedNode, validSelection) &&
            siblingsBeforeApply.All(node => placementRoom.SceneNodes.Contains(node)) &&
            placementRoom.RawXml == "<Platform Name=\"broken\">",
            "Invalid XML modified the room or discarded the draft");
        var existingTriggerCount = placementRoom.SceneNodes.Count(node => node.Kind == LevelNodeKind.Trigger);
        mainModel.PlaceAuthoredTrigger(triggerDocument.ToString());
        Require(ReferenceEquals(mainModel.SelectedDocument, placementRoom) &&
            placementRoom.SceneNodes.Count(node => node.Kind == LevelNodeKind.Trigger) == existingTriggerCount + 1,
            "Trigger placement replaced the open room instead of inserting a copy");
        triggerStudio.Measure(new Size(1400, 900));
        triggerStudio.Arrange(new Rect(0, 0, 1400, 900));
        triggerStudio.UpdateLayout();
        Require(FindVisuals<Button>(triggerStudio).Any(button => button.Content as string == "Game Templates") &&
            FindVisuals<Button>(triggerStudio).Any(button => button.Content as string == "Choose an outcome"),
            "trigger templates and outcome presets are not exposed in the Windows studio");
        var guideToggle = FindVisuals<Button>(triggerStudio).Single(button => button.Content as string == "Guide");
        guideToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var triggerIdentity = (StackPanel)typeof(TriggerStudioView).GetField("_identity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(triggerStudio)!;
        Require(triggerIdentity.Visibility == Visibility.Visible && ((StackPanel)triggerIdentity.Parent).Visibility == Visibility.Visible,
            "Guide hides the trigger area or starting-variable inspector");
        guideToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var draftBox = new TextBox();
        var themeBeforeAssistant = mainModel.IsDarkMode;
        mainModel.IsDarkMode = true;
        EditorTheme.Apply(true);
        var draftEditor = new TriggerXmlEditor(draftBox);
        var draftHost = new Window { Content = draftEditor, Width = 650, Height = 500, ShowInTaskbar = false };
        draftHost.Show(); draftHost.UpdateLayout();
        Require(draftBox.Foreground is SolidColorBrush nativeText && nativeText.Color.A > 0,
            "XML editor relies on transparent native text while typing");
        draftBox.Text = "<Trigger Name='test' Width='320' Height='180'>\n<Content><Loop><Events><Enter/></Events><Actions/></Loop>\n</Trigger>";
        typeof(TriggerXmlEditor).GetMethod("Review", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draftEditor, null);
        var issue = (TextBlock)typeof(TriggerXmlEditor).GetField("_issue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(draftEditor)!;
        Require(issue.Text.Contains("Line 3") && draftBox.Text.Contains("<Content>"), "XML assistant did not report malformed draft without modifying it");
        var repairReview = XmlDraftReviewer.Review(draftBox.Text);
        typeof(TriggerXmlEditor).GetMethod("RenderSolutions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draftEditor, [repairReview]);
        Require(FindVisuals<Button>(draftEditor).Any(button => button.Content?.ToString()?.StartsWith("View suggested fixes") == true),
            "Repair choices have no visible entry point");
        var showSolution = typeof(TriggerXmlEditor).GetMethod("ShowSolution", BindingFlags.Instance | BindingFlags.NonPublic)!;
        showSolution.Invoke(draftEditor, [repairReview, repairReview.Solutions[0]]);
        var fixPopup = (System.Windows.Controls.Primitives.Popup)typeof(TriggerXmlEditor).GetField("_fixPopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(draftEditor)!;
        var fixSurface = (FrameworkElement)fixPopup.Child;
        fixSurface.Measure(new Size(600, 420)); fixSurface.Arrange(new Rect(fixSurface.DesiredSize)); fixSurface.UpdateLayout();
        Require(FindVisuals<TextBlock>(fixSurface).Any(block => block.Text.Contains("Recommended")), "Supported repair has no recommendation label");
        Require(FindVisuals<Border>(fixSurface).Any(border => border.Background is ImageBrush && border.Effect is System.Windows.Media.Effects.BlurEffect), "Repair popup uses transparency without a blurred backdrop");
        var repairScreenshot = Environment.GetEnvironmentVariable("VECTOR2_XML_REPAIR_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(repairScreenshot))
        {
            var bitmap = new RenderTargetBitmap((int)fixSurface.ActualWidth, (int)fixSurface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(fixSurface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(repairScreenshot); encoder.Save(output);
        }
        draftBox.Text += "<!-- newer -->";
        var changedDraft = draftBox.Text;
        FindVisuals<Button>(fixSurface).Single(button => button.Content as string == "Apply Fix").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(draftBox.Text == changedDraft && issue.Text.Contains("stale fix"), "Stale solution overwrote a newer draft");
        draftBox.Text = "<Wait>";
        draftBox.CaretIndex = 5;
        var completions = (ListBox)typeof(TriggerXmlEditor).GetField("_choices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(draftEditor)!;
        completions.ItemsSource = new[] { "Wait" }; completions.SelectedIndex = 0;
        typeof(TriggerXmlEditor).GetField("_prefixStart", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(draftEditor, 0);
        typeof(TriggerXmlEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draftEditor, null);
        Require(draftBox.Text == "<Wait>", "XML completion changed markup beyond the name token");
        draftBox.Text = "<WaitSuffix Frames='30'/>"; draftBox.CaretIndex = 3;
        completions.ItemsSource = new[] { "Wait" }; completions.SelectedIndex = 0;
        typeof(TriggerXmlEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draftEditor, null);
        Require(XElement.Parse(draftBox.Text).Name == "Wait" && XElement.Parse(draftBox.Text).Attribute("Frames")?.Value == "30", "Mid-word completion left the token suffix behind or changed attributes");
        draftBox.Text = "<ContSuffix><Init/></Content>"; draftBox.CaretIndex = 4;
        completions.ItemsSource = new[] { "Content" }; completions.SelectedIndex = 0;
        typeof(TriggerXmlEditor).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(draftEditor, null);
        Require(draftBox.Text == "<Content><Init/></Content>", "Name completion duplicated the existing closing tag or deleted children");
        var completionSurface = (FrameworkElement)typeof(TriggerXmlEditor).GetField("_completionSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(draftEditor)!;
        completions.ItemsSource = new[] { "Content" }; completions.SelectedIndex = 0;
        completionSurface.GetType().GetMethod("Update")!.Invoke(completionSurface, new object?[] { 1, "Content" });
        completionSurface.Measure(new Size(304, 500));
        completionSurface.Arrange(new Rect(new Point(), completionSurface.DesiredSize));
        completionSurface.UpdateLayout();
        Require(FindVisuals<TextBlock>(completionSurface).Any(block => block.Text == "Content" && block.Foreground is SolidColorBrush ink && ink.Color.R == 19 && ink.Color.G == 42 && ink.Color.B == 73), "Dark assistant selected text is not readable against its pale selection");
        Require(FindVisuals<TextBlock>(completionSurface).Any(block => block.Text == "XML Assistant") &&
            FindVisuals<TextBlock>(completionSurface).Any(block => block.Text == "Recommended" && block.Visibility == Visibility.Visible),
            "Completion popup is missing the Mac assistant header or supported recommendation badge");
        completionSurface.GetType().GetMethod("Open")!.Invoke(completionSurface, null);
        var glowPhase = (RotateTransform)completionSurface.GetType().GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(completionSurface)!;
        Require(glowPhase.HasAnimatedProperties == SystemParameters.ClientAreaAnimation, "Completion glow ignored reduced-motion settings");
        completionSurface.GetType().GetMethod("Close")!.Invoke(completionSurface, null);
        Require(!glowPhase.HasAnimatedProperties, "Completion glow continued after closing");
        draftBox.Text = "<Trigger Name='typing'><Content/></Trigger>";
        draftBox.CaretIndex = 20; draftBox.SelectedText = "x";
        draftEditor.Measure(new Size(600, 350)); draftEditor.Arrange(new Rect(0, 0, 600, 350)); draftEditor.UpdateLayout();
        var nativeBitmap = new RenderTargetBitmap((int)draftBox.ActualWidth, (int)draftBox.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        nativeBitmap.Render(draftBox);
        var nativePixels = new byte[nativeBitmap.PixelWidth * nativeBitmap.PixelHeight * 4];
        nativeBitmap.CopyPixels(nativePixels, nativeBitmap.PixelWidth * 4, 0);
        var nativeInk = 0;
        for (var y = 8; y < Math.Min(nativeBitmap.PixelHeight - 8, 65); y++)
            for (var x = 8; x < nativeBitmap.PixelWidth - 8; x++)
            {
                var pixel = (y * nativeBitmap.PixelWidth + x) * 4;
                if (nativePixels[pixel + 3] > 0 && nativePixels[pixel] < 190 && nativePixels[pixel + 1] < 190 && nativePixels[pixel + 2] < 190) nativeInk++;
            }
        Require(nativeInk > 80, "Native XML text disappeared after inserting a character");
        var completionScreenshot = Environment.GetEnvironmentVariable("VECTOR2_XML_COMPLETION_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(completionScreenshot))
        {
            var bitmap = new RenderTargetBitmap(304, Math.Max(1, (int)Math.Ceiling(completionSurface.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(completionSurface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(completionScreenshot); encoder.Save(output);
        }
        mainModel.IsDarkMode = themeBeforeAssistant;
        EditorTheme.Apply(themeBeforeAssistant);
        Require(XmlAssistantLexing.NameAtCaret("<Object Name='a > < b'>", 20) is null,
            "Quoted markup was treated as a completion tag");
        Require(XmlAssistantLexing.IsEmptyDraft("<!-- valid -->\n<!-- also valid -->"), "Comment-only draft was marked malformed");
        draftHost.Close();
        var stress = "<Trigger><Content><Loop><Events><Enter/></Events><Actions>" + string.Concat(Enumerable.Repeat("<ForceAnimation/>", 1500)) + "</Actions></Loop></Content></Trigger>";
        Require(!XmlDraftReviewer.Review("<Trigger Name='Audio' Width='320' Height='180'><Content><Loop><Events><Enter/></Events><Actions><Sound Action='Stop'/><Music Action='Pause'/></Actions></Loop></Content></Trigger>").Intents.Any(), "Assistant demanded an audio name for stop/pause commands");
        var modelReview = XmlDraftReviewer.Review("<CustomModel><!-- keep --><Extension setting='custom'/></CustomModel>");
        Require(modelReview.Intents.Single().MissingFields.SequenceEqual(new[] { "ID", "FileName" }), "Custom model manifest fields were not offered");
        var modelRepair = XmlDraftReviewer.CompleteIntent(modelReview.Source, modelReview.Intents.Single(), new Dictionary<string,string> { ["ID"] = "MyModel", ["FileName"] = "model.xml" });
        Require(modelRepair.Code.Contains("<!-- keep -->") && modelRepair.Code.Contains("setting=\"custom\""), "Manifest repair lost extension data or comments");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stressReview = XmlDraftReviewer.Review(stress); watch.Stop();
        Require(stressReview.Solutions.Count <= 32 && stressReview.Intents.Count == 1500 && watch.Elapsed < TimeSpan.FromSeconds(5), "Repair budget dropped later editable fields or exceeded the stress time bound");
        var finalIntent = stressReview.Intents.Last();
        var stressFix = XmlDraftReviewer.CompleteIntent(stress, finalIntent, finalIntent.MissingFields.ToDictionary(key => key, key => key == "Model" ? "Player" : "RunForward"));
        Require(XDocument.Parse(stressFix.Code).Descendants("ForceAnimation").Last().Attribute("Name")?.Value == "RunForward", "Budget fallback could not produce code for a late issue");
        var ampSource = "<Object Name='A & B'><!-- & untouched --><![CDATA[ & untouched ]]></Object>";
        var ampReview = XmlDraftReviewer.Review(ampSource);
        Require(ampReview.Solutions.Count == 1 && ampReview.Solutions[0].Code.Contains("A &amp; B") &&
            ampReview.Solutions[0].Code.Contains("<!-- & untouched -->") && ampReview.Solutions[0].Code.Contains("<![CDATA[ & untouched ]]>"),
            "Ampersand repair changed comments or CDATA");
        var duplicates = XmlDraftReviewer.Review("<Object Name='first' Name=\"second\"/>");
        Require(duplicates.Solutions.Count == 2 && duplicates.Solutions.All(solution => !solution.Recommended) &&
            duplicates.Solutions.Select(solution => XElement.Parse(solution.Code).Attribute("Name")!.Value).Order().SequenceEqual(new[] { "first", "second" }),
            "Conflicting duplicate values were ranked or lost");
        var aiHints = XmlDraftReviewer.Review("<Trigger><Content><Init><SetVariable Name='$Hunter' Type='AI' Value='2'/></Init><Loop><Actions><Kill/></Actions></Loop></Content></Trigger>");
        Require(aiHints.Solutions.Any(solution => solution.Title == "Set Model to $Hunter") &&
            !aiHints.Solutions.Any(solution => solution.Title == "Set Model to $AI"), "Assistant invented an undeclared AI variable");
        var diff = XmlSolutionDiff.Lines("a\nb\nc", "a\nd\nc");
        Require(string.Join("\n", diff.Where(line => line.Kind != XmlDiffKind.Added).Select(line => line.Text)) == "a\nb\nc" &&
            string.Join("\n", diff.Where(line => line.Kind != XmlDiffKind.Removed).Select(line => line.Text)) == "a\nd\nc",
            "Solution diff could not reconstruct both drafts");
        var variantFixture = Path.Combine(root, "variant-import.xml");
        File.WriteAllText(variantFixture, "<Root><Track><Properties><Selection><Choice Name='Test'><Variant Name='A'/><Variant Name='B'/></Choice></Selection></Properties><Content><Platform Name='A' Width='10' Height='10'><Properties><Static><Selection Choice='Test' Variant='A'/></Static></Properties></Platform><Platform Name='B' Width='10' Height='10'><Properties><Static><Selection Choice='Test' Variant='B'/></Static></Properties></Platform></Content></Track></Root>");
        var variantImporter = new GameRoomWeaverService();
        var branchB = variantImporter.Load(variantFixture, new Dictionary<string, string> { ["Test"] = "B" });
        Require(branchB.SceneNodes.Any(node => node.Name == "B") && !branchB.SceneNodes.Any(node => node.Name == "A"), "Room Weaver ignored the selected variant");
        var variantExport = System.Xml.Linq.XDocument.Parse(new Exporter().Export(branchB));
        Require(variantExport.Descendants("Choice").Single(element => (string?)element.Attribute("Name") == "Test").Elements("Variant").Single().Attribute("Name")?.Value == "B",
            "Export discarded the selected imported Track variant");
        Require(variantImporter.Load(variantFixture, new Dictionary<string, string> { ["Test"] = "A" }).SceneNodes.Any(node => node.Name == "A"), "Import mutated the cached source document");
        var nestedFixture = Path.Combine(root, "nested-variants.xml");
        File.WriteAllText(nestedFixture, "<Root><Track><Properties><Selection><Choice Name='Path'><Variant Name='A'><Choice Name='Detail'><Variant Name='One'/><Variant Name='Two'/></Choice></Variant><Variant Name='B'><Choice Name='Detail'><Variant Name='One'/><Variant Name='Three'/></Choice></Variant></Choice></Selection></Properties><Content><Platform Name='chosen' Width='10' Height='10'><Properties><Static><Selection Choice='Detail' Parent='Path.B' Variant='Three'/><Selection Choice='Detail' Parent='Path.A' Variant='Two'/></Static></Properties></Platform><Platform Name='inactive' Width='10' Height='10'><Properties><Static><Selection Choice='Detail' Parent='Path.A' Variant='One'/></Static></Properties></Platform></Content></Track></Root>");
        Require(variantImporter.ChoiceOptions(nestedFixture).ContainsKey("Path.B/Detail"), "Nested choices lost their parent scope");
        var nestedRoom = variantImporter.Load(nestedFixture, new Dictionary<string, string> { ["Path"] = "B", ["Path.B/Detail"] = "Three" });
        Require(nestedRoom.SceneNodes.Any(node => node.Name == "chosen") && !nestedRoom.SceneNodes.Any(node => node.Name == "inactive"),
            "Nested variant selection differs from the Mac any-matching-rule behavior");
        typeof(TriggerStudioView).GetMethod("AddLoop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(triggerStudio, null);
        typeof(TriggerStudioView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(triggerStudio, null);
        var savedTrigger = System.Xml.Linq.XDocument.Load(previewTrigger);
        Require(savedTrigger.Descendants("Loop").Count() == 2 &&
            (string?)savedTrigger.Root?.Attribute("FutureTriggerField") == "preserve",
            "trigger visual edit did not save its loop or preserve unknown XML");
        var triggerDraft = (TextBox)typeof(TriggerStudioView).GetField("_raw", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(triggerStudio)!;
        var rawEdited = XDocument.Parse(triggerDraft.Text); rawEdited.Root!.SetAttributeValue("Name", "saved_raw_draft");
        triggerDraft.Text = rawEdited.ToString();
        typeof(TriggerStudioView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(triggerStudio, null);
        Require(XDocument.Load(previewTrigger).Root?.Attribute("Name")?.Value == "saved_raw_draft", "Trigger Save ignored the current raw XML draft");
        var savedBeforeInvalid = File.ReadAllText(previewTrigger);
        triggerDraft.Text = "<Trigger>";
        typeof(TriggerStudioView).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(triggerStudio, null);
        Require(File.ReadAllText(previewTrigger) == savedBeforeInvalid && triggerDraft.Text == "<Trigger>", "Invalid trigger Save overwrote the file or draft");
        var questPath = new ProjectTemplateService().Create(project, ProjectSection.All.Single(section => section.Name == "Quests"), "screen_quest");
        var questXml = System.Xml.Linq.XDocument.Load(questPath);
        questXml.Descendants("Quest").First().SetAttributeValue("FutureQuestField","preserve");
        questXml.Save(questPath);
        var questStudio = new QuestStudioView(projectRoot, _ => { });
        var questFields = (Dictionary<string,TextBox>)typeof(QuestStudioView).GetField("_fields",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(questStudio)!;
        questFields["Name"].Text = "A real quest";
        questFields["Reference"].Text = "previous_quest";
        questFields["RewardId"].Text = "custom_reward";
        typeof(QuestStudioView).GetField("_start",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(questStudio,"After another quest");
        typeof(QuestStudioView).GetField("_reward",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(questStudio,"CustomReward");
        var questSteps=(System.Collections.IList)typeof(QuestStudioView).GetField("_steps",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(questStudio)!;
        var step=questSteps[0]!;
        step.GetType().GetField("Event")!.SetValue(step,"DoorOpened");
        typeof(QuestStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(questStudio,null);
        var savedQuest=System.Xml.Linq.XDocument.Load(questPath);
        Require((string?)savedQuest.Descendants("Quest").First().Attribute("FutureQuestField")=="preserve" &&
            (string?)savedQuest.Descendants("Trigger").First().Descendants("OnCall").First().Attribute("Message")=="DoorOpened" &&
            savedQuest.Descendants("AddItem").Any(node=>(string?)node.Attribute("Preset")=="custom_reward") &&
            savedQuest.Descendants("CounterRange").Any(node=>(string?)node.Attribute("Name")=="previous_quest"),
            "quest visual editor did not compile the ordered objective, prerequisite and reward");
        var storyStudio = new StoryStudioView(projectRoot, _ => { });
        var switchMode=typeof(StoryStudioView).GetMethod("SwitchMode",BindingFlags.Instance|BindingFlags.NonPublic)!;
        switchMode.Invoke(storyStudio,["Dialogue"]);
        switchMode.Invoke(storyStudio,["Cast"]);
        switchMode.Invoke(storyStudio,["Story Flow"]);
        switchMode.Invoke(storyStudio,["Dialogue"]);
        switchMode.Invoke(storyStudio,["Story Flow"]);
        var phraseStudio = new NarrativeContentStudioView(projectRoot,"Localization",_ => { });
        typeof(NarrativeContentStudioView).GetMethod("Create",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(phraseStudio,null);
        var phraseFields=(Dictionary<string,TextBox>)typeof(NarrativeContentStudioView).GetField("_fields",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(phraseStudio)!;
        phraseFields["Value"].Text="Open the door";
        typeof(NarrativeContentStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(phraseStudio,null);
        Require((string?)System.Xml.Linq.XDocument.Load(Path.Combine(projectRoot,"custom_localization","Phrase.xml")).Descendants("Phrase").First().Attribute("Value")=="Open the door",
            "localization form did not save phrase text");
        var dialogueStudio = new NarrativeContentStudioView(projectRoot,"Dialogue",_ => { });
        typeof(NarrativeContentStudioView).GetMethod("Create",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialogueStudio,null);
        var dialogueFields=(Dictionary<string,TextBox>)typeof(NarrativeContentStudioView).GetField("_fields",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialogueStudio)!;
        dialogueFields["Text"].Text="Keep moving.";
        typeof(NarrativeContentStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialogueStudio,null);
        Require((string?)System.Xml.Linq.XDocument.Load(Path.Combine(projectRoot,"custom_dialogue","Dialogue.xml")).Descendants("Dialogue").First(element=>element.Attribute("Id") is not null).Attribute("Text")=="Keep moving.",
            "dialogue form did not save dialogue text");
        var modelFolder=Path.Combine(projectRoot,"custom_models","screenrunner");
        Directory.CreateDirectory(modelFolder);
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Scene",new System.Xml.Linq.XElement("Nodes",new System.Xml.Linq.XElement("Node",new System.Xml.Linq.XAttribute("Name","Root"))),new System.Xml.Linq.XElement("Figures",new System.Xml.Linq.XElement("Figure")))).Save(Path.Combine(modelFolder,"model.xml"));
        var modelManifest=Path.Combine(modelFolder,"manifest.xml");
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("CustomModel",new System.Xml.Linq.XAttribute("ID","screenrunner"),new System.Xml.Linq.XAttribute("Name","Screen Runner"),new System.Xml.Linq.XAttribute("FileName","model.xml"),new System.Xml.Linq.XAttribute("FutureModelField","preserve"))).Save(modelManifest);
        var modelStudio=new ModelStudioView(projectRoot,_ => { });
        var modelPreview=(TrickPreviewControl?)typeof(ModelStudioView).GetField("_preview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(modelStudio);
        Require(modelPreview is { CenterOnVisibleModel: true, ShowModelNodePoints: false, ShowModelOutlines: false, ClipToBounds: true },
            "Models studio must expose a contained preview without joint markers");
        var modelFields=(Dictionary<string,TextBox>)typeof(ModelStudioView).GetField("_fields",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(modelStudio)!;
        modelFields["Author"].Text="Artist";
        typeof(ModelStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(modelStudio,null);
        var savedModel=System.Xml.Linq.XDocument.Load(modelManifest).Root!;
        Require((string?)savedModel.Attribute("Author")=="Artist" && (string?)savedModel.Attribute("FutureModelField")=="preserve",
            "model designer did not save metadata while preserving unknown fields");
        var obstacleArchive=Path.Combine(root,"example.v2obstacle");
        using (var zip=ZipFile.Open(obstacleArchive,ZipArchiveMode.Create))
        {
            void Add(string name,string value)
            {
                using var writer=new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(value);
            }
            Add("staging/manifest.xml","<Vector2Obstacle Id=\"example\" Name=\"Example Obstacle\" XML=\"obstacle.xml\"><Textures><Texture File=\"surface.png\"/></Textures></Vector2Obstacle>");
            Add("staging/obstacle.xml","<Root><Objects/></Root>");
            Add("staging/textures/surface.png","texture bytes");
        }
        var installedObstacle=ObstacleArchiveService.Import(obstacleArchive,projectRoot);
        Require(File.Exists(installedObstacle) && File.Exists(Path.Combine(projectRoot,"custom_textures","surface.png")) &&
            ObstacleArchiveService.Read(installedObstacle).TextureCount==1,
            "obstacle archive import did not install the package and texture");
        var unsafeArchive=Path.Combine(root,"unsafe.v2obstacle");
        using (var zip=ZipFile.Open(unsafeArchive,ZipArchiveMode.Create))
        {
            using var writer=new StreamWriter(zip.CreateEntry("../escape.xml").Open());
            writer.Write("<Root/>");
        }
        try { ObstacleArchiveService.Read(unsafeArchive); throw new Exception("unsafe obstacle archive was accepted"); }
        catch (InvalidDataException) { }
        var collisionRoot=Path.Combine(root,"Collision Project");
        Directory.CreateDirectory(Path.Combine(collisionRoot,"custom_textures"));
        File.WriteAllText(Path.Combine(collisionRoot,"custom_textures","surface.png"),"a different texture");
        try { ObstacleArchiveService.Import(obstacleArchive,collisionRoot); throw new Exception("conflicting obstacle texture was overwritten"); }
        catch (IOException) { }
        Require(!File.Exists(Path.Combine(collisionRoot,"custom_obstacles","example.v2obstacle")),
            "obstacle archive was copied despite a texture conflict");
        var trapManifest=CustomTrapCompiler.DefaultManifest("screen_trap","Screen Trap");
        trapManifest.SetAttributeValue("Impact","Damage armour");
        trapManifest.SetAttributeValue("DamageAmount","5");
        trapManifest.SetAttributeValue("HitSound","impact.wav");
        var trapPath=CustomTrapCompiler.Save(projectRoot,trapManifest);
        var trapLibrary=System.Xml.Linq.XDocument.Load(Path.Combine(projectRoot,"custom_gamedata","run_data","libraries","v2trap_screen_trap.xml"));
        Require(trapLibrary.Descendants("ArmorDamage").Any(node=>(string?)node.Attribute("Amount")=="5") &&
            trapLibrary.Descendants("Trigger").Count()==2 && trapLibrary.Descendants("Sound").Any(node=>(string?)node.Attribute("Name")=="impact.wav"),
            "trap compiler did not create activation, damage, and sound behavior");
        var trapStudio=new TrapStudioView(projectRoot,_ => { });
        var trapScreenshot=Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_TRAP_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(trapScreenshot))
        {
            trapStudio.Measure(new Size(1200,700));
            trapStudio.Arrange(new Rect(0,0,1200,700));
            trapStudio.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1200,700,96,96,PixelFormats.Pbgra32);
            bitmap.Render(trapStudio);
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(trapScreenshot);
            encoder.Save(output);
        }
        var trapFields=(Dictionary<string,TextBox>)typeof(TrapStudioView).GetField("_fields",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(trapStudio)!;
        var originalTrap = File.ReadAllText(trapPath);
        trapFields["SoundVolume"].Text = "NaN";
        typeof(TrapStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(trapStudio,null);
        Require(File.ReadAllText(trapPath) == originalTrap, "Invalid trap volume altered the saved manifest");
        trapFields["SoundVolume"].Text = "0.8";
        trapFields["DamageAmount"].Text="7";
        typeof(TrapStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(trapStudio,null);
        Require((string?)System.Xml.Linq.XDocument.Load(trapPath).Root?.Attribute("DamageAmount")=="7" &&
            System.Xml.Linq.XDocument.Load(Path.Combine(projectRoot,"custom_gamedata","run_data","libraries","v2trap_screen_trap.xml"))
                .Descendants("ArmorDamage").Any(node=>(string?)node.Attribute("Amount")=="7"),
            "trap visual editor did not recompile the changed damage");
        var trapBeforeBadArtwork = File.ReadAllText(trapPath);
        var badArtwork = XElement.Load(trapPath);
        badArtwork.SetAttributeValue("Artwork", "missing.gif");
        try { CustomTrapCompiler.Save(projectRoot, badArtwork); throw new Exception("Missing artwork was accepted"); }
        catch (InvalidDataException) { }
        Require(File.ReadAllText(trapPath) == trapBeforeBadArtwork, "Failed artwork validation replaced the saved trap");
        var gifPath=Path.Combine(projectRoot,"custom_textures","trap.gif");
        var gif=new GifBitmapEncoder();
        gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,
            Enumerable.Repeat((byte)255,16).ToArray(),8)));
        using (var output=File.Create(gifPath)) gif.Save(output);
        var animatedTrap=CustomTrapCompiler.DefaultManifest("animated_trap","Animated Trap");
        animatedTrap.SetAttributeValue("Artwork","trap.gif");
        CustomTrapCompiler.Save(projectRoot,animatedTrap);
        Require(File.Exists(Path.Combine(projectRoot,"custom_textures","v2trap_animated_trap","idle_0000.png")) &&
            File.Exists(Path.Combine(projectRoot,"custom_traps","animated_trap","animations","v2trap_animated_trap_idle.xml")) &&
            System.Xml.Linq.XDocument.Load(Path.Combine(projectRoot,"custom_gamedata","run_data","libraries","v2trap_animated_trap.xml"))
                .Descendants("CustomAnimation").Any(),
            "trap GIF was not compiled into runtime frames and animation XML");
        var tutorialStudio = new SdkDefinitionStudioView(projectRoot, SdkDefinitionKind.Tutorial, _ => { });
        typeof(SdkDefinitionStudioView).GetMethod("Create",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(tutorialStudio,null);
        var tutorialDocument=(System.Xml.Linq.XDocument)typeof(SdkDefinitionStudioView).GetField("_document",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tutorialStudio)!;
        tutorialDocument.Root!.Add(new System.Xml.Linq.XElement("Step",new System.Xml.Linq.XAttribute("Message","Jump over the laser")));
        typeof(SdkDefinitionStudioView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(tutorialStudio,null);
        var tutorialPath=Path.Combine(projectRoot,"custom_tutorials","NewTutorial.xml");
        Require(System.Xml.Linq.XDocument.Load(tutorialPath).Root!.Elements("Step").Any(node=>(string?)node.Attribute("Message")=="Jump over the laser"),
            "tutorial step editor did not save a playable step");
    });
    try
    {
        manifest.Create(projectRoot);
        throw new Exception("new-project creation reused an existing folder");
    }
    catch (IOException) { }

    Require(File.Exists(Path.Combine(projectRoot, "project.xml")), "project.xml was not created");
    Require(ProjectSection.All.Where(section => section.Folder.Length > 0)
        .All(section => Directory.Exists(Path.Combine(projectRoot, section.Folder))), "a project section folder is missing");
    Require(!Directory.Exists(Path.Combine(projectRoot, "custom_backgrounds_pool")), "excluded background pool folder was created");

    var rooms = ProjectSection.All.Single(section => section.Name == "Content");
    var sample = new ProjectTemplateService().Create(project, rooms, "Room One");
    Require(File.Exists(sample), "content template was not created");
    Require(Path.GetFullPath(sample).StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase), "template escaped project root");
    var searchModel = new ProjectManagerViewModel();
    searchModel.Files.Clear();
    searchModel.Files.Add(new ProjectFileItem
    {
        FullPath = sample,
        RelativePath = Path.GetRelativePath(projectRoot, sample),
        Name = Path.GetFileName(sample),
        Extension = "XML",
        Section = "Content",
        Size = new FileInfo(sample).Length,
        Modified = File.GetLastWriteTime(sample)
    });
    searchModel.SearchText = "Room One";
    var match = searchModel.SearchResults.SingleOrDefault();
    Require(match?.FullPath == sample, "global search did not find the room");
    string? openedRoom = null;
    searchModel.RoomOpenRequested += path => openedRoom = path;
    searchModel.OpenSearchResultCommand.Execute(match);
    Require(openedRoom == sample && searchModel.SearchText.Length == 0, "global search result did not open the room");
    searchModel.SearchText = "protocol";
    Require(searchModel.SearchHits.FirstOrDefault()?.TargetSection?.Name == "Protocols",
        "ranked search did not prioritize the Protocols tool");
    searchModel.SearchText = "credits";
    Require(searchModel.SearchHits.FirstOrDefault()?.TargetSection?.Name == "Save Data",
        "search alias did not find the player profile");
    searchModel.SearchText = "protocol";
    var protocolSection = searchModel.MatchingSections.SingleOrDefault();
    Require(protocolSection?.Name == "Protocols", "global search did not find the Protocols tab");
    searchModel.OpenSearchSectionCommand.Execute(protocolSection);
    Require(searchModel.SelectedSection.Name == "Protocols" && searchModel.SearchText.Length == 0,
        "searching for a section did not navigate to it");

    var templates = new ProjectTemplateService();
    var zone = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Zones"), "maintenance");
    var zoneXml = System.Xml.Linq.XDocument.Load(zone);
    var zoneElement = zoneXml.Root?.Element("Zone");
    Require((string?)zoneElement?.Attribute("RoomsPath") == "custom_rooms/maintenance", "zone room isolation does not match the Swift schema");
    Require((string?)zoneElement?.Attribute("TricksPath") == "custom_tricks/maintenance", "zone trick isolation does not match the Swift schema");
    var structuralZone = StructuralRoomService.LoadZones(projectRoot).Single(item => item.Id == "maintenance");
    var savedEntrance = StructuralRoomService.SaveToZone(entrance, projectRoot, structuralZone);
    Require(File.Exists(savedEntrance) && savedEntrance.Contains("start_rooms") &&
        new XmlSceneParser().Load(savedEntrance).StructuralRoomKind == StructuralRoomKind.Entrance,
        "Entrance Designer did not save into the selected zone's start-room pool");
    StructuralRoomService.SaveToZone(entrance, projectRoot, structuralZone);
    Require(Directory.EnumerateFiles(Path.Combine(projectRoot, ".backups", "structural_rooms",
        structuralZone.Id, "start_rooms"), "*.backup_*").Any(),
        "saving a structural room again did not preserve a backup");
    Require(Directory.Exists(Path.Combine(projectRoot, "custom_rooms", "maintenance")), "zone room folder was not created");
    Require(Directory.Exists(Path.Combine(projectRoot, "custom_tricks", "maintenance")), "zone trick folder was not created");

    var quest = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Quests"), "first_quest");
    var questXml = System.Xml.Linq.XDocument.Load(quest);
    Require(questXml.Root?.Element("Quest")?.Element("Info")?.Element("Reward") is not null, "quest info/reward schema is missing");
    Require(questXml.Descendants("CounterRange").Any(node => (string?)node.Attribute("Namespace") == "ST_Quests"), "quest start counter contract is missing");

    var trigger = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Trigger Designer"), "door_trigger");
    var triggerXml = System.Xml.Linq.XDocument.Load(trigger);
    Require(triggerXml.Root?.Name.LocalName == "Trigger" && triggerXml.Descendants("Events").Any(), "trigger designer runtime graph is missing");

    var protocol = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Protocols"), "runner_protocol");
    var protocolXml = System.Xml.Linq.XDocument.Load(protocol);
    Require(protocolXml.Descendants("Gameplay").Any() && protocolXml.Descendants("ArmourDefense").Any(), "protocol gameplay/armor schema is missing");

    var reward = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Rewards"), "daily_reward");
    Require(System.Xml.Linq.XDocument.Load(reward).Root?.Name.LocalName == "CustomReward", "reward SDK root is incorrect");

    var mission = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Missions"), "first_mission");
    Require(System.Xml.Linq.XDocument.Load(mission).Root?.Name.LocalName == "CustomMission", "mission SDK root is incorrect");

    var tutorial = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Tutorials"), "movement_tutorial");
    Require(System.Xml.Linq.XDocument.Load(tutorial).Root?.Element("Step") is not null, "tutorial steps are missing");

    var shop = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Shop"), "starter_card");
    var shopXml = System.Xml.Linq.XDocument.Load(shop);
    Require(Path.GetFileName(shop) == "card.xml" && shopXml.Root?.Name.LocalName == "CustomCard", "shop card package is incorrect");
    Require(shopXml.Root?.Elements("Level").Count() == 5, "shop card levels are incomplete");

    var modelSource = Path.Combine(root, "runner.xml");
    File.WriteAllText(modelSource, "<Scene><Node Name=\"Runner\" /></Scene>");
    var modelSection = ProjectSection.All.Single(section => section.Name == "Models");
    new ProjectInventoryService().Import(project, modelSection, [modelSource]);
    var modelPackage = Path.Combine(projectRoot, "custom_models", "runner");
    Require(File.Exists(Path.Combine(modelPackage, "model.xml")), "model XML was not packaged");
    var manifestXml = System.Xml.Linq.XDocument.Load(Path.Combine(modelPackage, "manifest.xml"));
    Require(manifestXml.Root?.Name.LocalName == "CustomModel" && (string?)manifestXml.Root.Attribute("ID") == "runner", "model manifest does not match the Swift package contract");
    var scratchLibrary = Path.Combine(root, "scratch-packages");
    var scratchPackage = Path.Combine(scratchLibrary, "example");
    Directory.CreateDirectory(scratchPackage);
    var scratchManifest = Path.Combine(scratchPackage, "manifest.xml");
    File.WriteAllText(scratchManifest, "<Package/>");
    ProjectPackageService.DeletePackage(scratchLibrary, scratchManifest);
    Require(!Directory.Exists(scratchPackage), "package deletion left files behind");
    var looseManifest = Path.Combine(scratchLibrary, "manifest.xml");
    File.WriteAllText(looseManifest, "<Package/>");
    try { ProjectPackageService.DeletePackage(scratchLibrary, looseManifest); throw new Exception("loose manifest deleted its entire library"); }
    catch (InvalidOperationException) { }
    Require(File.Exists(looseManifest), "loose manifest was removed");

    var gameData = Path.Combine(root, "Vector2_Data");
    Directory.CreateDirectory(Path.Combine(gameData, "Managed"));
    var fakeGame = Path.Combine(root, "Bundle");
    var fakeData = Path.Combine(fakeGame, "Vector 2_Data");
    var fakeMetadata = Path.Combine(fakeData, "il2cpp_data", "Metadata");
    Directory.CreateDirectory(fakeMetadata);
    File.WriteAllText(Path.Combine(fakeMetadata, "global-metadata.dat"), "Vector2ModData");
    File.WriteAllText(Path.Combine(fakeGame, "Vector 2.exe"), "");
    var detectedGame = Vector2GameIntegrationLocator.Detect(fakeGame);
    Require(detectedGame?.DataFolder == fakeData &&
        detectedGame.CustomRooms == Path.Combine(fakeData, "StreamingAssets", "Vector2ModData", "custom_rooms"),
        "the supplied Vector 2 player could not be located without a preexisting StreamingAssets folder");
    File.WriteAllText(Path.Combine(fakeMetadata, "global-metadata.dat"), "CustomProjectHotReload");
    Require(Vector2GameIntegrationLocator.ModDataRootFor(fakeData) == CustomContentService.DefaultStorageRoot,
        "an older player build was incorrectly assumed to support bundled mod data");
    var modData = Path.Combine(gameData, "StreamingAssets", "Vector2ModData");
    var gameMetadata = Path.Combine(gameData, "il2cpp_data", "Metadata");
    Directory.CreateDirectory(gameMetadata);
    File.WriteAllText(Path.Combine(gameMetadata, "global-metadata.dat"), "Vector2ModData");
    var installer = new ProjectInstallerService(modData);
    project.GameDataPath = gameData;
    ProjectInstallerService.ValidateProgression(projectRoot);
    var chapterFile = Path.Combine(projectRoot, "custom_chapters", "preview_Chapters.xml");
    var chapterDefinition = XDocument.Load(chapterFile);
    var chapterNode = chapterDefinition.Root!.Element("Chapter")!;
    chapterNode.SetAttributeValue("Id", "verification_chapter");
    foreach (var reference in chapterNode.Elements("Zone").ToList()) reference.Remove();
    foreach (var zoneFile in Directory.EnumerateFiles(Path.Combine(projectRoot, "custom_zones"), "*.xml"))
    {
        var zoneDefinition = XDocument.Load(zoneFile);
        var zoneNode = zoneDefinition.Root!.Element("Zone")!;
        var zoneId = (string)zoneNode.Attribute("Id")!;
        zoneNode.SetAttributeValue("Chapter", "verification_chapter");
        var zoneRoomFolder = ProjectManifestService.SafeCombine(projectRoot, (string)zoneNode.Attribute("RoomsPath")!);
        Directory.CreateDirectory(zoneRoomFolder);
        File.Copy(sample, Path.Combine(zoneRoomFolder, "room.xml"), true);
        chapterNode.Add(new XElement("Zone", new XAttribute("Id", zoneId)));
        zoneDefinition.Save(zoneFile);
    }
    chapterDefinition.Save(chapterFile);
    var originalChapter = File.ReadAllBytes(chapterFile);
    chapterNode.Add(new XElement(chapterNode.Element("Floor")!));
    chapterDefinition.Save(chapterFile);
    try { installer.Install(project); throw new Exception("duplicate starting floors were silently installed"); }
    catch (InvalidDataException error) { Require(error.Message.Contains("duplicate starting floors"), "unexpected duplicate-floor failure"); }
    File.WriteAllBytes(chapterFile, originalChapter);
    var orphanChapterPath = new ProjectTemplateService().Create(project,
        ProjectSection.All.Single(section => section.Name == "Chapters"), "unlinked_chapter");
    var progressionWarnings = new ProjectValidationService().Validate(project, new ProjectInventoryService().Scan(project));
    Require(progressionWarnings.Any(issue => issue.Message.Contains("unlinked_chapter") && issue.Message.Contains("no linked zone")),
        "an invisible unlinked chapter was reported as ready for gameplay");
    File.Delete(orphanChapterPath);
    var first = installer.Install(project);
    var draftQuestPath = new ProjectTemplateService().Create(project,
        ProjectSection.All.Single(section => section.Name == "Quests"), "unsafe_empty_quest");
    var draftInstalled = Path.Combine(modData, "custom_quests", Path.GetFileName(draftQuestPath));
    var receiptEntries = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(first.ReceiptPath))!;
    receiptEntries.Add(Path.GetRelativePath(projectRoot, draftQuestPath));
    Directory.CreateDirectory(Path.GetDirectoryName(draftInstalled)!);
    File.Copy(draftQuestPath, draftInstalled);
    File.WriteAllText(first.ReceiptPath, System.Text.Json.JsonSerializer.Serialize(receiptEntries));
    var safeInstall = installer.Install(project);
    Require(safeInstall.UnfinishedQuests > 0 && File.Exists(draftQuestPath) && !File.Exists(draftInstalled),
        "unfinished quest was deleted from the project or left installed to crash the menu");
    var completedQuest = XDocument.Load(draftQuestPath);
    completedQuest.Descendants("Quest").First().Add(QuestSequenceCompiler.ProgressTriggers("unsafe_empty_quest",
        new[] { new QuestStep("Open a door", "DoorOpened") }, ""));
    completedQuest.Save(draftQuestPath);
    installer.Install(project);
    Require(File.Exists(draftInstalled), "finished quest was still excluded from installation");
    var selectedGameModel = new ProjectManagerViewModel();
    typeof(ProjectManagerViewModel).GetProperty("Project")!.SetValue(selectedGameModel, project);
    selectedGameModel.GameDataPathProvider = () => fakeData;
    // Active game selection must override a project's stale game path.
    File.WriteAllText(Path.Combine(fakeMetadata, "global-metadata.dat"), "Vector2ModData");
    selectedGameModel.InstallCommand.Execute(null);
    var selectedRoot = Path.Combine(fakeData, "StreamingAssets", "Vector2ModData");
    var selectedInstalled = Path.Combine(selectedRoot, "custom_rooms", Path.GetFileName(sample));
    Require(File.Exists(selectedInstalled) && File.ReadAllBytes(selectedInstalled).AsSpan().SequenceEqual(File.ReadAllBytes(sample)),
        "UI Install did not write identical room content to the active player's loader directory: " + selectedGameModel.Status);
    selectedGameModel.InstallCommand.Execute(null);
    Require(selectedGameModel.Status.Contains("already up to date"), "repeat UI install did not verify unchanged files");
    selectedGameModel.StudioChanged("Saved test studio.");
    Require(selectedGameModel.Status.Contains(selectedRoot), "studio auto-install used a different game destination");
    var installed = Path.Combine(modData, "custom_rooms", Path.GetFileName(sample));
    Require(first.Copied > 0 && File.Exists(installed), "project content was not installed");
    var installedShop = Path.Combine(modData, "custom_upgrades", "preview_Shop", "card.xml");
    Require((string?)System.Xml.Linq.XDocument.Load(installedShop).Root?.Attribute("Price") == "1400",
        "saved shop card was not installed into the game");

    var unchanged = installer.Install(project);
    Require(unchanged.Copied == 0 && unchanged.Removed == 0,
        "install recopied unchanged content and triggered unnecessary game reloads");
    File.Delete(sample);
    var second = installer.Install(project);
    Require(second.Removed > 0 && !File.Exists(installed), "stale installed content was not removed");
    Require(!Directory.Exists(Path.Combine(modData, "custom_backgrounds_pool")), "excluded background pool data was installed");

    var invalidGame = Path.Combine(root, "NotVector2_Data");
    Directory.CreateDirectory(invalidGame);
    project.GameDataPath = invalidGame;
    installer.Install(project);
    project.GameDataPath = "";
    Require(installer.Install(project).Copied == 0,
        "persistent-data installation incorrectly requires a game executable or data folder");

    var syncRoot = Path.Combine(root, "StudioSyncDestination");
    var syncModel = new ProjectManagerViewModel(syncRoot);
    typeof(ProjectManagerViewModel).GetProperty("Project")!.SetValue(syncModel, project);
    foreach (var section in ProjectSection.All.Where(section => section.Folder.Length > 0))
    {
        var relative = Path.Combine(section.Folder, "studio-sync-verification.dat");
        var sourceFile = ProjectManifestService.SafeCombine(projectRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, section.Name);
        syncModel.StudioChanged("Saved " + section.Name + ".");
        var installedFile = ProjectManifestService.SafeCombine(syncRoot, relative);
        Require(File.Exists(installedFile) && File.ReadAllText(installedFile) == section.Name,
            section.Name + " save callback did not install its own content folder: " + syncModel.Status);
        File.Delete(sourceFile);
        syncModel.StudioChanged("Deleted " + section.Name + ".");
        Require(!File.Exists(installedFile), section.Name + " delete callback left stale installed content");
    }
    Console.WriteLine("Project Manager verification passed.");
}
finally
{
    foreach (var (path, bytes) in preferences)
        if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
        else File.WriteAllBytes(path, bytes);
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void RunSta(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new Exception("Project studio UI smoke test failed.", failure);
}

static T? FindVisual<T>(DependencyObject node) where T : DependencyObject
{
    if(node is T match) return match;
    for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
    {
        var found=FindVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(node,i));
        if(found is not null) return found;
    }
    return null;
}

static IEnumerable<T> FindVisuals<T>(DependencyObject node) where T : DependencyObject
{
    if (node is T match) yield return match;
    for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); index++)
        foreach (var child in FindVisuals<T>(System.Windows.Media.VisualTreeHelper.GetChild(node, index)))
            yield return child;
}
