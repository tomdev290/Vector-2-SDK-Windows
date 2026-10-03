using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class ChapterZoneStudioView : UserControl
{
    private readonly Vector2Project _project;
    private readonly ProjectSection _section;
    private readonly Action<string> _status;
    private readonly StackPanel _content = new();
    private readonly string _folder;

    public ChapterZoneStudioView(Vector2Project project, ProjectSection section, Action<string> status)
    {
        _project = project;
        _section = section;
        _status = status;
        _folder = Path.Combine(project.RootPath, section.Folder);
        var isChapter = section.Name == "Chapters";
        var shell = StudioUi.Shell(section.Name,
            isChapter ? "Set up the floors and zones shown in the chapter menu." : "Choose a zone's rooms, menu position, and artwork.",
            isChapter ? "\uE82D" : "\uE81E",
            new SolidColorBrush(isChapter ? Color.FromRgb(69, 126, 218) : Color.FromRgb(51, 156, 116)),
            new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            StudioUi.Button("Import Existing", Import),
            StudioUi.Button("Open Folder", (_, _) => OpenFolder()),
            StudioUi.Button(isChapter ? "New Chapter" : "New Zone", (_, _) => ShowCreate(), true));
        Content = shell;
        Reload();
    }

    private void Reload()
    {
        Directory.CreateDirectory(_folder);
        _content.Children.Clear();
        var cards = new WrapPanel { Margin = new Thickness(24, 12, 24, 24) };
        foreach (var file in Directory.EnumerateFiles(_folder, "*.xml", SearchOption.AllDirectories).OrderBy(Path.GetFileName))
        {
            try
            {
                foreach (var item in XDocument.Load(file).Descendants(_section.Name == "Chapters" ? "Chapter" : "Zone"))
                {
                var name = (string?)item.Attribute("Name") ?? Path.GetFileNameWithoutExtension(file);
                var id = (string?)item.Attribute("Id") ?? "";
                var description = (string?)item.Attribute("Description") ?? "";
                var card = new StackPanel { Width = 180 };
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition());
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(new TextBlock
                {
                    Text = _section.Name == "Chapters" ? "\uE82D" : "\uE81E",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Foreground = new SolidColorBrush(Color.FromRgb(50, 127, 220)),
                    FontSize = 14
                });
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(StudioUi.Button("Edit", (_, _) => EditItem(file, id), true));
                var delete = StudioUi.Button("\uE74D", (_, _) => Delete(file, id));
                delete.FontFamily = new FontFamily("Segoe MDL2 Assets");
                delete.ToolTip = "Delete";
                delete.MinWidth = 28;
                delete.Padding = new Thickness(5);
                actions.Children.Add(delete);
                Grid.SetColumn(actions, 1);
                top.Children.Add(actions);
                card.Children.Add(top);
                card.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 9, 0, 3), TextTrimming = TextTrimming.CharacterEllipsis });
                card.Children.Add(new TextBlock { Text = id, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), FontSize = 10 });
                card.Children.Add(new TextBlock { Text = description, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), FontSize = 10, Margin = new Thickness(0, 6, 0, 12), Height = 28, TextWrapping = TextWrapping.Wrap });
                var count = _section.Name == "Chapters" ? item.Elements("Zone").Count() : CountRooms(item);
                card.Children.Add(new TextBlock { Text = _section.Name == "Chapters" ? $"{count} zones" : $"{count} rooms", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
                cards.Children.Add(StudioUi.Card(card, new Thickness(0, 0, 10, 10)));
                }
            }
            catch (Exception ex) { _status($"Could not read {Path.GetFileName(file)}: {ex.Message}"); }
        }
        if (cards.Children.Count == 0)
            cards.Children.Add(new TextBlock { Text = _section.Name == "Chapters" ? "No chapters yet." : "No zones yet.", Margin = new Thickness(12), Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        _content.Children.Add(cards);
    }

    private int CountRooms(XElement zone)
    {
        var relative = (string?)zone.Attribute("RoomsPath");
        if (string.IsNullOrWhiteSpace(relative)) return 0;
        var folder = Path.GetFullPath(Path.Combine(_project.RootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!folder.StartsWith(Path.GetFullPath(_project.RootPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 0;
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories)
            .Count(path => !path.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase)) : 0;
    }

    private void ShowCreate()
    {
        _content.Children.Clear();
        var panel = new StackPanel { Width = 350, Margin = new Thickness(30, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = _section.Name == "Chapters" ? "New Chapter" : "New Zone", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(StudioUi.Label("Name"));
        var name = StudioUi.Field();
        panel.Children.Add(name);
        ComboBox? chapterPicker = null;
        CheckBox? linkedZone = null;
        if (_section.Name == "Chapters")
        {
            linkedZone = new CheckBox { Content = "Create a linked zone", IsChecked = true, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(linkedZone);
        }
        if (_section.Name == "Zones")
        {
            panel.Children.Add(StudioUi.Label("Chapter"));
            var chapters = ChapterChoices();
            chapterPicker = new ComboBox { ItemsSource = chapters, DisplayMemberPath = nameof(ChapterChoice.Name),
                SelectedItem = chapters.FirstOrDefault(), Margin = new Thickness(0, 4, 0, 12) };
            panel.Children.Add(chapterPicker);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(StudioUi.Button("Cancel", (_, _) => Reload()));
        var create = StudioUi.Button("Create", (_, _) =>
        {
            try
            {
                var templates = new ProjectTemplateService();
                var path = _section.Name == "Zones" ? templates.CreateZone(_project, name.Text,
                    (chapterPicker?.SelectedItem as ChapterChoice)?.Id ?? "") :
                    linkedZone?.IsChecked == true ? templates.CreateChapterWithZone(_project, name.Text) : templates.Create(_project, _section, name.Text);
                _status($"Created {Path.GetFileName(path)}.");
                Edit(path);
            }
            catch (Exception ex) { _status(ex.Message); }
        }, true);
        create.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(create);
        panel.Children.Add(buttons);
        _content.Children.Add(panel);
        name.Focus();
    }

    private void Edit(string file) => EditItem(file, null);

    private void EditItem(string file, string? id)
    {
        try
        {
            var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            var element = document.Descendants(_section.Name == "Chapters" ? "Chapter" : "Zone")
                .First(item => id is null || (string?)item.Attribute("Id") == id);
            var originalName = (string?)element.Attribute("Name") ?? "";
            var originalArtwork = (string?)element.Attribute("Artwork") ?? "";
            var originalId = (string?)element.Attribute("Id") ?? "";
            _content.Children.Clear();
            var panel = new StackPanel { MaxWidth = 850, Margin = new Thickness(30, 24, 30, 30) };
            panel.Children.Add(new TextBlock { Text = _section.Name == "Chapters" ? "Edit Chapter" : "Edit Zone", FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) });
            var fields = new List<(string Name, TextBox Editor)>();
            TextBox Field(string name, string label, bool artwork = false)
            {
                panel.Children.Add(StudioUi.Label(label));
                var input = StudioUi.Field((string?)element.Attribute(name) ?? "");
                panel.Children.Add(artwork ? new ProjectArtworkField(_project.RootPath,input) : input);
                fields.Add((name,input)); return input;
            }
            Field("Id", _section.Name == "Chapters" ? "Chapter ID" : "Zone ID");
            Field("Name", "Display name");
            var description = Field("Description", "Description"); description.Height = 60; description.AcceptsReturn = true; description.TextWrapping = TextWrapping.Wrap;
            var floors = new List<TextBox>();
            TextBox? zoneIds = null;
            if (_section.Name == "Chapters")
            {
                var linked = StructuralRoomService.LoadZones(_project.RootPath).Any(zone => zone.Id == originalId);
                if (!linked)
                    panel.Children.Add(StudioUi.Button("Create Linked Zone", (_, _) =>
                    {
                        try { new ProjectTemplateService().CreateZone(_project, originalName, originalId);
                            _status("Created linked zone for " + originalName + "."); EditItem(file, originalId); }
                        catch (Exception error) { _status(error.Message); }
                    }, true));
                panel.Children.Add(StudioUi.Heading("Chapter Content", "\uE81E", Color.FromRgb(39,129,227)));
                panel.Children.Add(StudioUi.Label("Zone IDs"));
                zoneIds = StudioUi.Field(string.Join(", ",element.Elements("Zone").Select(zone => (string?)zone.Attribute("Id")).Where(id => !string.IsNullOrEmpty(id))));
                panel.Children.Add(zoneIds);
                Field("Artwork","Chapter artwork",true);
                panel.Children.Add(StudioUi.Heading("Starting Floors", "\uE768", Color.FromRgb(39,129,227)));
                var floorCount = new TextBlock { Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 10), FontSize = 11 };
                panel.Children.Add(floorCount);
                var floorPanel = new WrapPanel(); panel.Children.Add(floorPanel);
                Button? addFloor = null;
                void UpdateFloorCount()
                {
                    floorCount.Text = $"{floors.Count} of 12 menu start buttons";
                    if (addFloor is not null) addFloor.IsEnabled = floors.Count < 12;
                    var index = 0;
                    foreach (var block in floorPanel.Children.OfType<StackPanel>())
                        if (block.Children.OfType<DockPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                            label.Text = $"Start option {++index}";
                }
                void AddFloor(int value)
                {
                    var block = new StackPanel { Width = 165, Margin = new Thickness(0,0,14,12) };
                    var top = new DockPanel { LastChildFill = false };
                    top.Children.Add(new TextBlock { Text = $"Start option {floors.Count + 1}", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                    var input = StudioUi.Field(value.ToString());
                    var remove = StudioUi.Button("\uE74D", (_, _) => { if (floors.Count <= 1) return; floors.Remove(input); floorPanel.Children.Remove(block); UpdateFloorCount(); });
                    remove.FontFamily = new FontFamily("Segoe MDL2 Assets"); remove.ToolTip = "Remove starting floor";
                    DockPanel.SetDock(remove,Dock.Right); top.Children.Add(remove); block.Children.Add(top);
                    block.Children.Add(StudioUi.Number(input,1,999)); floors.Add(input); floorPanel.Children.Add(block); UpdateFloorCount();
                }
                foreach (var floor in element.Elements("Floor")) AddFloor(int.TryParse((string?)floor.Attribute("Number"),out var number) ? number : 1);
                if (floors.Count == 0) AddFloor(1);
                addFloor = StudioUi.Button("+ Add Start Button", (_, _) => { if(floors.Count < 12) AddFloor(int.TryParse(floors.Last().Text,out var last) ? Math.Min(999,last+1) : 1); });
                panel.Children.Add(addFloor);
                UpdateFloorCount();
            }
            else
            {
                panel.Children.Add(StudioUi.Heading("Menu & Story", "\uE80F", Color.FromRgb(39,129,227)));
                var chapterId = Field("Chapter","Chapter");
                var chapterPicker = new ComboBox { ItemsSource = ChapterChoices(), DisplayMemberPath = nameof(ChapterChoice.Name),
                    SelectedValuePath = nameof(ChapterChoice.Id), SelectedValue = chapterId.Text, Margin = chapterId.Margin,
                    IsEditable = true, Text = chapterId.Text };
                chapterPicker.SelectionChanged += (_, _) => { if (chapterPicker.SelectedValue is string chosen) chapterId.Text = chosen; };
                chapterPicker.LostKeyboardFocus += (_, _) => { if (chapterPicker.SelectedItem is not ChapterChoice) chapterId.Text = chapterPicker.Text; };
                panel.Children.Remove(chapterId); panel.Children.Add(chapterPicker);
                var order = Field("Order","Menu position"); if(string.IsNullOrEmpty(order.Text)) order.Text="1";
                panel.Children.Remove(order); panel.Children.Add(StudioUi.Number(order,1,999));
                Field("Artwork","Menu artwork",true); Field("MainBackground","Main menu background",true); Field("LoaderBackground","Loading background",true);
                panel.Children.Add(StudioUi.Heading("Gameplay Content", "\uE7FC", Color.FromRgb(41,174,94)));
                Field("RoomsPath","Room pool folder"); Field("TricksPath","Tricks folder");
                var roomActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                roomActions.Children.Add(StudioUi.Button("Open Room Folder", (_, _) =>
                {
                    try { var folder = ZoneRoomImportService.EnsurePool(_project.RootPath, fields.Single(field => field.Name == "RoomsPath").Editor.Text.Trim());
                        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
                    catch (Exception error) { _status(error.Message); }
                }));
                var addRooms = StudioUi.Button("Add Rooms", (_, _) =>
                {
                    var dialog = new OpenFileDialog { Filter = "Vector room XML (*.xml)|*.xml", Multiselect = true };
                    if (dialog.ShowDialog() != true) return;
                    try {
                        var roomPath = fields.Single(field => field.Name == "RoomsPath").Editor.Text.Trim();
                        if (roomPath != ((string?)element.Attribute("RoomsPath") ?? ""))
                            throw new InvalidDataException("Save the changed room pool folder before adding rooms.");
                        var imported = ZoneRoomImportService.Import(_project.RootPath, roomPath, dialog.FileNames);
                        _status($"Added {imported.Count} room(s) to {(string?)element.Attribute("Name")}."); }
                    catch (Exception error) { _status(error.Message); }
                }, true);
                addRooms.Margin = new Thickness(8, 0, 0, 0); roomActions.Children.Add(addRooms); panel.Children.Add(roomActions);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(StudioUi.Button("Back", (_, _) => Reload()));
            var save = StudioUi.Button("Save Changes", (_, _) =>
            {
                try
                {
                    if (fields.Any(field => field.Name is "Id" or "Name" && string.IsNullOrWhiteSpace(field.Editor.Text))) throw new InvalidDataException("ID and display name are required.");
                    if (_section.Name == "Chapters" && (floors.Count is < 1 or > 12)) throw new InvalidDataException("A chapter needs 1 to 12 start buttons.");
                    if (_section.Name == "Chapters" && floors.Any(input => !int.TryParse(input.Text,out var number) || number < 1 || number > 999)) throw new InvalidDataException("Starting floors must be between 1 and 999.");
                    if (_section.Name == "Chapters" && floors.Select(input => int.Parse(input.Text)).Distinct().Count() != floors.Count)
                        throw new InvalidDataException("Starting floors must be unique; the game merges duplicate floor buttons.");
                    if (_section.Name == "Zones" && fields.FirstOrDefault(field => field.Name == "Order") is var orderField &&
                        (!int.TryParse(orderField.Editor?.Text, out var orderValue) || orderValue < 1 || orderValue > 999))
                        throw new InvalidDataException("Menu position must be between 1 and 999.");
                    foreach(var field in fields.Where(field => field.Name is "RoomsPath" or "TricksPath"))
                        if(!string.IsNullOrWhiteSpace(field.Editor.Text)) ProjectManifestService.SafeCombine(_project.RootPath,field.Editor.Text.Trim());
                    if (_section.Name == "Zones")
                    {
                        var roomPath = fields.Single(field => field.Name == "RoomsPath").Editor;
                        if (string.IsNullOrWhiteSpace(roomPath.Text)) roomPath.Text = "custom_rooms/" + ProjectTemplateService.SafeId(fields.Single(field => field.Name == "Id").Editor.Text);
                        ZoneRoomImportService.EnsurePool(_project.RootPath, roomPath.Text.Trim());
                    }
                    foreach (var field in fields) element.SetAttributeValue(field.Name, field.Editor.Text.Trim());
                    if (zoneIds is not null)
                    {
                        element.Elements("Zone").Remove();
                        foreach(var id in zoneIds.Text.Split(',',StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase)) element.Add(new XElement("Zone",new XAttribute("Id",id)));
                        element.Elements("Floor").Remove();
                        foreach(var input in floors) element.Add(new XElement("Floor",new XAttribute("Number",input.Text.Trim())));
                    }
                    var temporary = file + ".tmp"; document.Save(temporary); File.Move(temporary,file,true);
                    if (_section.Name == "Chapters")
                        ProjectTemplateService.SyncChapterAppearance(_project, originalId, originalName, originalArtwork,
                            (string?)element.Attribute("Name") ?? "", (string?)element.Attribute("Artwork") ?? "");
                    _status($"Saved {Path.GetFileName(file)}.");
                    Reload();
                }
                catch (Exception ex) { _status(ex.Message); }
            }, true);
            save.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);
            _content.Children.Add(panel);
        }
        catch (Exception ex) { _status(ex.Message); Reload(); }
    }

    private void Import(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "XML files (*.xml)|*.xml", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imports = new List<(string Target, XDocument Document)>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in dialog.FileNames)
            {
                var target = Path.Combine(_folder, Path.GetFileName(source));
                if (File.Exists(target) || !targets.Add(target)) throw new IOException($"{Path.GetFileName(source)} already exists or occurs twice in this import.");
                var document = XmlDraftParsing.Parse(File.ReadAllText(source));
                if (document.Root?.Name.LocalName != _section.Name) throw new InvalidDataException($"Expected {_section.Name} XML, not {document.Root?.Name.LocalName}.");
                imports.Add((target, document));
                if (_section.Name == "Zones")
                    foreach (var zone in document.Descendants("Zone"))
                    {
                        var id = ProjectTemplateService.SafeId((string?)zone.Attribute("Id") ?? "");
                        if (id.Length == 0) throw new InvalidDataException("Imported zone needs an ID.");
                        var rooms = (string?)zone.Attribute("RoomsPath");
                        if (string.IsNullOrWhiteSpace(rooms)) { rooms = "custom_rooms/" + id; zone.SetAttributeValue("RoomsPath", rooms); }
                        ZoneRoomImportService.EnsurePool(_project.RootPath, rooms);
                    }
            }
            var written = new List<string>();
            try
            {
                foreach (var (target, document) in imports)
                {
                    using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                    written.Add(target);
                    document.Save(stream);
                }
            }
            catch { foreach (var target in written) File.Delete(target); throw; }
            Reload();
            _status($"Imported {imports.Count} {_section.Name.ToLowerInvariant()} file(s).");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_folder);
        Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
    }

    private void Delete(string file, string id)
    {
        if (MessageBox.Show($"Delete {Path.GetFileName(file)}?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var document = XDocument.Load(file);
            var items = document.Descendants(_section.Name == "Chapters" ? "Chapter" : "Zone").ToList();
            if (items.Count <= 1) File.Delete(file);
            else
            {
                items.First(item => (string?)item.Attribute("Id") == id).Remove();
                var temporary = file + ".tmp";
                document.Save(temporary); File.Move(temporary, file, true);
            }
            Reload();
            _status("Deleted " + id + ".");
        }
        catch (Exception error) { _status("Could not delete definition: " + error.Message); }
    }
    private ChapterChoice[] ChapterChoices()
    {
        var result = new List<ChapterChoice>();
        var folder = Path.Combine(_project.RootPath, "custom_chapters");
        if (!Directory.Exists(folder)) return [];
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
            try { result.AddRange(XDocument.Load(file).Descendants("Chapter")
                .Select(chapter => new ChapterChoice((string?)chapter.Attribute("Id") ?? "", (string?)chapter.Attribute("Name") ?? ""))); }
            catch (Exception error) { _status("Could not read chapter: " + error.Message); }
        return result.Where(chapter => chapter.Id.Length > 0).DistinctBy(chapter => chapter.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private sealed record ChapterChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
