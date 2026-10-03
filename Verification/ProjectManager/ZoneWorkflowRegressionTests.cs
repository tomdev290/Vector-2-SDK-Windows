using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

static class ZoneWorkflowRegressionTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Vector2ZoneWorkflow", Guid.NewGuid().ToString("N"));
        try
        {
            var project = new ProjectManifestService().Create(Path.Combine(root, "project"));
            var templates = new ProjectTemplateService();
            var linkedChapterPath = templates.CreateChapterWithZone(project, "Linked Chapter");
            var linkedZonePath = Path.Combine(project.RootPath, "custom_zones", "Linked_Chapter.xml");
            Check(File.Exists(linkedZonePath) && XDocument.Load(linkedChapterPath).Descendants("Chapter").Single().Element("Zone") is not null,
                "Default New Chapter workflow created a chapter invisible in the game's zone menu");
            ProjectTemplateService.SyncChapterAppearance(project, "Linked_Chapter", "Linked Chapter", "", "Renamed Chapter", "cover.png");
            var linkedDefinition = XDocument.Load(linkedZonePath).Descendants("Zone").Single();
            Check((string?)linkedDefinition.Attribute("Name") == "Renamed Chapter" &&
                (string?)linkedDefinition.Attribute("MainBackground") == "cover.png" &&
                (string?)linkedDefinition.Attribute("LoaderBackground") == "cover.png",
                "Chapter name and artwork were not propagated to the runtime zone");
            linkedDefinition.SetAttributeValue("MainBackground", "custom-override.png");
            linkedDefinition.Document!.Save(linkedZonePath);
            ProjectTemplateService.SyncChapterAppearance(project, "Linked_Chapter", "Renamed Chapter", "cover.png", "Final Chapter", "new-cover.png");
            Check((string?)XDocument.Load(linkedZonePath).Descendants("Zone").Single().Attribute("MainBackground") == "custom-override.png",
                "Chapter appearance sync destroyed an explicit zone artwork override");
            var chapterPath = templates.Create(project, ProjectSection.All.Single(section => section.Name == "Chapters"), "Test Chapter");
            var chapterId = (string)XDocument.Load(chapterPath).Descendants("Chapter").Single().Attribute("Id")!;
            var zonePath = templates.CreateZone(project, "First Zone", chapterId);
            var definition = XDocument.Load(zonePath).Descendants("Zone").Single();
            var zoneId = (string)definition.Attribute("Id")!;
            var relative = (string)definition.Attribute("RoomsPath")!;
            Check((string?)definition.Attribute("Chapter") == chapterId &&
                XDocument.Load(chapterPath).Descendants("Chapter").Single().Elements("Zone")
                    .Any(reference => (string?)reference.Attribute("Id") == zoneId), "New zone was not linked in both directions");
            var folder = ProjectManifestService.SafeCombine(project.RootPath, relative);
            var createdRoom = ZoneRoomImportService.CreateRoom(project.RootPath, relative, "Created Room");
            Check(Path.GetDirectoryName(createdRoom) == folder && XDocument.Load(createdRoom).Root?.Element("Track") is not null,
                "New Room command created content outside its selected zone");
            var createdBytes = File.ReadAllBytes(createdRoom);
            try { ZoneRoomImportService.CreateRoom(project.RootPath, relative, "Created Room"); throw new Exception("New Room overwrote an existing room"); }
            catch (IOException) { }
            Check(File.ReadAllBytes(createdRoom).AsSpan().SequenceEqual(createdBytes), "Room collision changed original content");
            Check(Directory.Exists(Path.Combine(folder, "start_rooms")) && Directory.Exists(Path.Combine(folder, "finish_rooms")),
                "New zone did not create all room pools");
            ProjectInstallerService.ValidateProgression(project.RootPath);
            var source = Path.Combine(root, "normal.xml");
            var original = new Exporter().Export(LevelDocument.Empty());
            File.WriteAllText(source, original);
            File.WriteAllText(Path.ChangeExtension(source, ".meta.xml"), "<RoomMetadata Author='Test'/>");
            var imported = ZoneRoomImportService.Import(project.RootPath, relative, [source]);
            Check(imported.Count == 1 && File.ReadAllText(imported[0]) == original &&
                File.Exists(Path.ChangeExtension(imported[0], ".meta.xml")), "Room or metadata changed during import");
            try { ZoneRoomImportService.Import(project.RootPath, relative, [source]); throw new Exception("Room collision overwrote content"); }
            catch (IOException) { }
            var valid = Path.Combine(root, "second.xml"); File.WriteAllText(valid, original);
            var invalid = Path.Combine(root, "invalid.xml"); File.WriteAllText(invalid, "<Trigger/>");
            try { ZoneRoomImportService.Import(project.RootPath, relative, [valid, invalid]); throw new Exception("Non-room XML imported"); }
            catch (InvalidDataException) { }
            Check(!File.Exists(Path.Combine(folder, "second.xml")), "Failed import left a partial room batch");
            foreach (var kind in new[] { StructuralRoomKind.Entrance, StructuralRoomKind.Exit })
            {
                var path = Path.Combine(root, kind + ".xml");
                File.WriteAllText(path, new Exporter().Export(StructuralRoomService.Create(kind)));
                var target = ZoneRoomImportService.Import(project.RootPath, relative, [path]).Single();
                Check(Path.GetFileName(Path.GetDirectoryName(target)) == (kind == StructuralRoomKind.Entrance ? "start_rooms" : "finish_rooms"),
                    "Structural room was imported into the normal generator pool");
                var beforeReplacement = File.ReadAllText(target);
                ZoneRoomImportService.Import(project.RootPath, relative, [path]);
                Check(File.ReadAllText(target) == beforeReplacement && Directory.EnumerateFiles(Path.Combine(project.RootPath, ".backups", "room_import"), "*.xml", SearchOption.AllDirectories).Any(), "Entrance/exit replacement failed or lost its backup");
                var freshRoom = StructuralRoomService.Create(kind);
                freshRoom.Name = Path.GetFileName(target);
                Check(StructuralRoomService.SaveToZone(freshRoom, project.RootPath, new StructuralZone("Test", "Test", relative)) == target, "A new entrance/exit document could not replace the existing filename");
            }
            var secondZonePath = templates.CreateZone(project, "Second Zone", chapterId);
            var secondRelative = (string)XDocument.Load(secondZonePath).Descendants("Zone").Single().Attribute("RoomsPath")!;
            Check(ZoneRoomImportService.Import(project.RootPath, secondRelative, [source]).Single() != imported[0],
                "Two zone pools shared a room target");
            try { ZoneRoomImportService.EnsurePool(project.RootPath, "custom_rooms"); throw new Exception("Shared room root accepted as zone"); }
            catch (InvalidDataException) { }
            try { ZoneRoomImportService.EnsurePool(project.RootPath, "../outside"); throw new Exception("Zone escaped project"); }
            catch (IOException) { }
            var installedRoot = Path.Combine(root, "game-content");
            var emptyZonePath = templates.CreateZone(project, "Empty Zone", chapterId);
            var emptyRelative = (string)XDocument.Load(emptyZonePath).Descendants("Zone").Single().Attribute("RoomsPath")!;
            var installer = new ProjectInstallerService(installedRoot);
            installer.Install(project);
            Check(Directory.Exists(Path.Combine(installedRoot, emptyRelative, "start_rooms")) &&
                Directory.Exists(Path.Combine(installedRoot, emptyRelative, "finish_rooms")),
                "Installer discarded the folder structure for a new empty zone");
            Check(File.ReadAllText(Path.Combine(installedRoot, relative, "normal.xml")) == original,
                "Zone room did not install at the exact runtime-relative path");
            Check(installer.Install(project).Copied == 0, "Repeat zone install rewrote unchanged content");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!)
                Check(process.WaitForExit(15000) && process.ExitCode == 0, "Process exit fixture failed");
            Check(File.Exists(Path.Combine(installedRoot, Path.GetRelativePath(project.RootPath, chapterPath))) &&
                File.Exists(Path.Combine(installedRoot, Path.GetRelativePath(project.RootPath, zonePath))),
                "Process exit removed installed chapters or zones");
            Check(!Directory.EnumerateFiles(Path.Combine(installedRoot, ".vector2-editor-projects"), "*.session.json").Any(),
                "Installation armed an automatic uninstall session");
            Console.WriteLine("Zone workflow verification passed: linked creation, room pools, metadata, collision safety, batch preflight and installation.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
