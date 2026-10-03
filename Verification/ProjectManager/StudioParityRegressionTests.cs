using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;
using Vector2LevelEditor.Views.ProjectManager.Studios;

static class StudioParityRegressionTests
{
    public static void Run(string parent)
    {
        var project = new ProjectManifestService().Create(Path.Combine(parent, "parity-regression"));
        var template = new ProjectTemplateService();
        var chapter = template.CreateChapterWithZone(project, "Alpha");
        Directory.CreateDirectory(Path.Combine(project.RootPath, "custom_rooms", "DeletedZone"));
        Check(ProjectOverviewService.LoadZones(project.RootPath).Select(zone => zone.Name).SequenceEqual(new[] { "Alpha" }), "Overview showed an orphan room folder as an installed zone");
        Check(!ProjectIntegrationAudit.Validate(project.RootPath).Any(issue => issue.Message.Contains("Duplicate Zone")), "Chapter references were mistaken for duplicate zone definitions");
        var mission = Path.Combine(project.RootPath, "custom_missions", "invalid.xml");
        File.WriteAllText(mission, "<CustomMission Id='bad' Difficulty='4' Target='0' Amount='-1' Weight='0' Order='5' MaximumFloor='2'/>");
        Check(ProjectIntegrationAudit.Validate(project.RootPath).Count(issue => issue.Source.EndsWith("invalid.xml")) == 5, "Mission audit missed invalid runtime values");
        File.Delete(mission);
        var story = Path.Combine(project.RootPath, "custom_story", "broken.xml");
        File.WriteAllText(story, "<StoryGraphs><StoryGraph Id='broken' Start='1'><Node Id='1' Type='End' Next='missing'/></StoryGraph></StoryGraphs>");
        Check(ProjectIntegrationAudit.Validate(project.RootPath).Any(issue => issue.Message.Contains("missing block")), "Broken story connection was not diagnosed");
        File.Delete(story);
        var areaNames = GameTrickPreviewService.ParseAnimationAreaNames(XDocument.Parse("<Root><Reaction AreaName=' NewArea '/><Other AreaName='newarea'/><Move AreaName='WallJump'/><Empty AreaName=' '/></Root>"));
        Check(areaNames.SequenceEqual(new[] { "NewArea", "WallJump" }), "Animation area catalogue did not trim/deduplicate complete runtime names");
        Check(PlayerSaveService.NormalizeSkin("white_armor") == "white_armor.xml" && PlayerSaveService.NormalizeSkin("custom:coat") == "custom:coat" && PlayerSaveService.NormalizeSkin("hair.xml") == "hair.xml", "Equipped model filenames were not normalized safely");
        var attributeDraft = "<EndGame Model='Player' FraXXX='30'/>";
        var attribute = XmlAttributeCompletion.AtCaret(attributeDraft, attributeDraft.IndexOf("FraXXX", StringComparison.Ordinal) + 3);
        Check(attribute?.Length == 6 && attribute.Choices.Contains("Frames") && !attribute.Choices.Contains("Model"), "Mid-word attribute completion lost its replacement range or repeated an existing attribute");
        var modelsDraft = "<Trigger><Content><Init><SetVariable Name='$Friend' Type='AI' Value='1'/></Init><Loop><Actions><Spawn Model='$Fxxx'/></Actions></Loop></Content></Trigger>";
        var modelToken = XmlAttributeCompletion.AtCaret(modelsDraft, modelsDraft.IndexOf("$Fxxx", StringComparison.Ordinal) + 2);
        Check(modelToken?.Length == 5 && modelToken.Choices.Contains("$Friend") && !modelToken.Choices.Contains("$AI"), "Model predictions did not use this trigger's declared AI variables");
        var quotedDraft = "<Sound Name='a > b' Volum='1'/>";
        Check(XmlAttributeCompletion.AtCaret("<!-- <Spawn Model='P'/> -->", 18) is null && XmlAttributeCompletion.AtCaret(quotedDraft, quotedDraft.IndexOf("Volum", StringComparison.Ordinal) + 3)?.Choices.Contains("Volume") == true, "Attribute predictions did not respect comments or quoted markup");
        var zoom = new Vector2LevelEditor.Models.LevelNode { Name = "ZoomMin", Kind = Vector2LevelEditor.Models.LevelNodeKind.ObjectReference, Filename = "triggers.xml", ImagePath = "browser-thumbnail.png", Width = 180, Height = 120 };
        Check(Vector2LibraryObjectBuilder.TryReconstructReference(zoom) && zoom.ImagePath.Length == 0 && zoom.Children.Any(node => node.Name == "TriggerZoomMin" && node.Height == 3500), "Zoom trigger stretched its browser thumbnail or changed runtime height");
        var editor = new Vector2LevelEditor.ViewModels.MainWindowViewModel();
        var initialNodes = editor.SelectedDocument.SceneNodes.Count();
        editor.PlaceSceneBackground(new Vector2LevelEditor.Models.CustomBackgroundDefinition { Name = "test", Pieces = ["<Image Name='sky' X='3' Y='7' Width='100' Height='80' SortingLayer='BgFar' Factor='0.8' ClassName='sky_texture'/>"] });
        Check(editor.SelectedDocument.SceneNodes.Count() == initialNodes + 1 && editor.SelectedDocument.SceneNodes.Last().Tag == "Background", "Scene background placement did not use editable background nodes");
        var afterPlacement = editor.SelectedDocument.SceneNodes.Count();
        try { editor.PlaceSceneBackground(new Vector2LevelEditor.Models.CustomBackgroundDefinition { Name = "invalid", Pieces = ["<Image Name='valid'/>", "<broken"] }); throw new Exception("Invalid background was accepted"); } catch (System.Xml.XmlException) { }
        Check(editor.SelectedDocument.SceneNodes.Count() == afterPlacement, "Invalid background inserted a partial scene");

        var audioStatus = "";
        var audio = new AudioStudioView(project.RootPath, status => audioStatus = status);
        Invoke(audio, "AddPool", "Chapter");
        Check(audioStatus.StartsWith("Created "), "Audio pool creation did not notify installation");
        var reference = Field<TextBox>(audio, "_reference"); reference.Text = "Alpha";
        var volume = Field<TextBox>(audio, "_volume"); volume.Text = "NaN";
        var audioManifest = Path.Combine(project.RootPath, "custom_audio", "audio_manifest.xml");
        var before = File.ReadAllText(audioManifest);
        Invoke(audio, "Save", null!, new RoutedEventArgs());
        Check(File.ReadAllText(audioManifest) == before, "Invalid audio volume altered the saved manifest");
        volume.Text = "0.75"; Invoke(audio, "Save", null!, new RoutedEventArgs());
        Check(audioStatus.StartsWith("Saved ") && (string?)XDocument.Load(audioManifest).Descendants("Pool").Single().Attribute("Reference") == "Alpha", "Audio pool did not save its exact reference");
        Invoke(audio, "RemovePool");
        Check(audioStatus.StartsWith("Deleted ") && !XDocument.Load(audioManifest).Descendants("Pool").Any(), "Audio deletion did not update installation or persisted content");

        var sound = Path.Combine(project.RootPath, "custom_audio", "alarm.wav"); File.WriteAllBytes(sound, [0]);
        var image = Path.Combine(project.RootPath, "custom_textures", "wide.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16 * 8 * 4).ToArray(), 16 * 4)));
        using (var output = File.Create(image)) encoder.Save(output);
        var trapDefinition = CustomTrapCompiler.DefaultManifest("alpha_trap", "Alpha Trap"); trapDefinition.SetAttributeValue("IdleSound", "alarm.wav");
        var trapManifest = CustomTrapCompiler.Save(project.RootPath, trapDefinition);
        var trap = new TrapStudioView(project.RootPath, _ => { });
        var fields = Field<Dictionary<string, TextBox>>(trap, "_fields");
        fields["Artwork"].Text = "wide.png";
        Check(fields["Width"].Text == "360" && fields["Height"].Text == "180", "Trap artwork change did not fit its aspect ratio like Swift");
        Invoke(trap, "Save");
        var compiled = XDocument.Load(Path.Combine(project.RootPath, "custom_gamedata", "run_data", "libraries", "v2trap_alpha_trap.xml"));
        Check(compiled.Descendants("Sound").Any(node => (string?)node.Attribute("Name") == "alarm") &&
            !compiled.Descendants("Sound").Any(node => ((string?)node.Attribute("Name"))?.EndsWith(".wav") == true), "Trap sound choice exported a filename instead of its runtime ID");
        var malformedModel = Path.Combine(project.RootPath, "custom_models", "manifest.xml");
        File.WriteAllText(malformedModel, "<CustomModel ID='unsafe' FileName='../../outside.xml'/>");
        var modelPath = typeof(ModelStudioView).GetMethod("ReadModelPath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [malformedModel]);
        Check(modelPath is null, "A model manifest escaped its package directory"); File.Delete(malformedModel);

        var installed = Path.Combine(parent, "shared-install"); var installer = new ProjectInstallerService(installed);
        File.WriteAllText(Path.Combine(project.RootPath, "custom_chapters", "incomplete.xml.tmp"), "<broken");
        Directory.CreateDirectory(Path.Combine(project.RootPath, "custom_chapters", ".backups"));
        File.WriteAllText(Path.Combine(project.RootPath, "custom_chapters", ".backups", "old.xml"), "<broken");
        installer.Install(project);
        Check(!File.Exists(Path.Combine(installed, "custom_chapters", "incomplete.xml.tmp")) &&
            !Directory.Exists(Path.Combine(installed, "custom_chapters", ".backups")), "Authoring artifacts were installed into the game catalog");
        var other = new ProjectManifestService().Create(Path.Combine(parent, "other-project"));
        var otherChapter = template.CreateChapterWithZone(other, "Alpha");
        File.WriteAllText(otherChapter, "<Chapters><Chapter Id='Alpha' Name='Conflict'><Floor Number='1'/><Zone Id='Alpha'/></Chapter></Chapters>");
        var target = Path.Combine(installed, "custom_chapters", "Alpha.xml"); var installedBefore = File.ReadAllText(target);
        try { installer.Install(other); throw new Exception("A different project overwrote an owned chapter"); } catch (IOException) { }
        Check(File.ReadAllText(target) == installedBefore, "Conflict preflight changed installed content");
        File.Copy(chapter, otherChapter, true); installer.Install(other);
        File.Delete(chapter); installer.Install(project);
        Check(File.Exists(target), "Deleting a project file removed content still owned by another installed project");
        var renderRoot = Environment.GetEnvironmentVariable("VECTOR2_VERIFICATION_POPULATED_DIR");
        if (!string.IsNullOrWhiteSpace(renderRoot))
        {
            Directory.CreateDirectory(renderRoot);
            trap.Measure(new Size(1000, 700)); trap.Arrange(new Rect(0, 0, 1000, 700)); trap.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1000, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(trap);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(renderRoot, "Trap-Parity.png")); png.Save(output);
        }
        Console.WriteLine("Studio parity regressions passed: audio lifecycle, trap visuals/sound IDs, model path safety, runtime area names, integration checks and shared-install safety.");
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
