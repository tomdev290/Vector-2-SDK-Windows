using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class ProtocolStudioView : UserControl
{
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _library = new();
    private readonly StackPanel _details = new();
    private readonly TextBox _newName = StudioUi.Field();
    private readonly List<(XElement Element, string Name, TextBox Input)> _inputs = [];
    private readonly List<(string Slot, ComboBox Picker)> _armorModels = [];
    private readonly GameTrickPreviewService _previewService = new();
    private TrickPreviewControl _preview = new() { Height = 245, ShowDebugEdges = false, ShowModelNodePoints = false, CenterOnVisibleModel = true,
        PreviewZoom = 3.5, PreviewBackground = new SolidColorBrush(Color.FromRgb(235, 241, 247)) };
    private TextBlock _previewMessage = new() { TextAlignment = TextAlignment.Center,
        Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 10) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private XDocument? _document;
    private string? _selectedPath;

    public ProtocolStudioView(string root, Action<string> status)
    {
        _root = root;
        _folder = Path.Combine(root, "custom_protocols");
        _status = status;
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(10, 12, 10, 10) };
        var create = new StackPanel();
        create.Children.Add(StudioUi.Label("New protocol"));
        create.Children.Add(_newName);
        create.Children.Add(StudioUi.Button("Create", (_, _) => Create(), true));
        DockPanel.SetDock(create, Dock.Top);
        left.Children.Add(create);
        _library.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_library);
        body.Children.Add(left);
        var scroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        Content = StudioUi.Shell("Protocols", "Set the player's starting floor, armor, and character model.",
            "\uEA18", new SolidColorBrush(Color.FromRgb(28, 168, 177)), body,
            StudioUi.Button("Reload", (_, _) => Reload(_selectedPath)),
            StudioUi.Button("New Protocol", (_, _) => { _newName.Focus(); }, true));
        _previewTimer.Tick += (_, _) =>
        {
            if (_preview.Playback is { Frames.Count: > 0 } playback)
                _preview.Frame = (_preview.Frame + 1) % playback.Frames.Count;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _previewTimer.Start();
            else _previewTimer.Stop();
        };
        Unloaded += (_, _) => _previewTimer.Stop();
        Reload();
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var paths = Directory.EnumerateFiles(_folder, "*.xml").OrderBy(Path.GetFileName).ToList();
        _library.ItemsSource = paths;
        _library.ItemTemplate = FileTemplate();
        _library.SelectedItem = selected is not null && paths.Contains(selected) ? selected : paths.FirstOrDefault();
        if (paths.Count == 0)
        {
            _details.Children.Clear();
            _details.Children.Add(new TextBlock
            {
                Text = "No protocol selected\nCreate a protocol to define a floor button, armor base and character appearance.",
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"),
                FontSize = 14, Width = 400, Margin = new Thickness(0, 150, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new ProtocolNameConverter() });
        factory.SetValue(TextBlock.PaddingProperty, new Thickness(8, 10, 8, 10));
        template.VisualTree = factory;
        return template;
    }

    private void LoadSelected()
    {
        _selectedPath = _library.SelectedItem as string;
        _details.Children.Clear();
        _inputs.Clear(); _armorModels.Clear();
        if (_selectedPath is null) return;
        try
        {
            _document = XDocument.Load(_selectedPath, LoadOptions.PreserveWhitespace);
            var item = _document.Root?.Element("Protocol");
            if (item is null) throw new InvalidDataException("Expected a Protocols/Protocol document.");
            _preview = new TrickPreviewControl { Height = 245, ShowDebugEdges = false, ShowModelNodePoints = false, CenterOnVisibleModel = true,
                PreviewZoom = 3.5, PreviewBackground = new SolidColorBrush(Color.FromRgb(235, 241, 247)) };
            _previewMessage = new TextBlock { TextAlignment = TextAlignment.Center,
                Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 10) };
            var panel = new StackPanel { Margin = new Thickness(26, 20, 26, 28), MaxWidth = 950 };
            panel.Children.Add(new TextBlock { Text = (string?)item.Attribute("Name") ?? "Protocol", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
            var previewPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            previewPanel.Children.Add(_preview);
            previewPanel.Children.Add(_previewMessage);
            panel.Children.Add(previewPanel);
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition());
            var identity = AddGroup(top, 0, "Identity", item, ["Id", "Name", "Description", "Chapter", "Artwork", "StartFloor", "Order"]);
            var appearance = AddGroup(top, 1, "Player appearance", item, ["BaseProtocol", "PlayerModel"]);
            var modelOptions = ModelChoices();
            foreach (var slot in new[] { "Head", "Torso", "Hands", "Legs" })
            {
                appearance.Children.Add(StudioUi.Label(slot + " layer"));
                var current = (string?)item.Elements("Armor").FirstOrDefault(node => (string?)node.Attribute("Slot") == slot)?.Attribute("Model") ?? "";
                if (current.StartsWith("custom:",StringComparison.OrdinalIgnoreCase)) current=current[7..];
                var options = new[] { "" }.Concat(modelOptions).Append(current).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var picker = new ComboBox { ItemsSource = options, SelectedItem = current, Margin = new Thickness(0,4,0,12) };
                appearance.Children.Add(picker); _armorModels.Add((slot,picker));
                picker.SelectionChanged += (_, _) => RebuildPreview();
            }
            panel.Children.Add(top);
            var gameplay = item.Element("Gameplay") ?? new XElement("Gameplay");
            if (gameplay.Parent is null) item.Add(gameplay);
            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition());
            AddGroup(bottom, 0, "Armour gameplay", gameplay, ["EffectID", "Interval", "Amount", "Maximum"]);
            var defense = item.Element("ArmourDefense") ?? new XElement("ArmourDefense");
            if (defense.Parent is null) item.Add(defense);
            AddGroup(bottom, 1, "Armour defense", defense, ["Helmet", "Torso", "Hands", "Legs", "Belt"]);
            panel.Children.Add(bottom);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            actions.Children.Add(StudioUi.Button("Delete", (_, _) => Delete()));
            actions.Children.Add(StudioUi.Button("Revert", (_, _) => LoadSelected()));
            actions.Children.Add(StudioUi.Button("Save Protocol", (_, _) => Save(), true));
            panel.Children.Add(actions);
            _details.Children.Add(panel);
            _inputs.First(field => field.Name == "PlayerModel").Input.TextChanged += (_, _) => RebuildPreview();
            RebuildPreview();
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void RebuildPreview()
    {
        if (_document?.Root?.Element("Protocol") is null) return;
        try
        {
            var move = _previewService.LoadMoves()
                .FirstOrDefault(item => item.FileName.Contains("cs_swarm_idle", StringComparison.OrdinalIgnoreCase))
                ?? _previewService.LoadMoves().FirstOrDefault()
                ?? throw new InvalidDataException("Game animation assets are unavailable.");
            var layers = new List<string> { "0.xml" };
            var player = _inputs.FirstOrDefault(field => field.Name == "PlayerModel").Input?.Text.Trim() ?? "";
            if (player.Length > 0) layers.Add(player.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? player : "custom:" + player);
            foreach (var (_, picker) in _armorModels)
            {
                var model = picker.SelectedItem as string ?? "";
                if (model.Length > 0) layers.Add(model.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ? model : "custom:" + model);
            }
            if (layers.Count == 1) layers.Add("1.xml");
            _preview.Playback = _previewService.LoadPlayback(move,
                layers.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                customModelsDirectory: Path.Combine(_root, "custom_models"));
            _preview.Frame = _preview.Playback.StartFrame;
            _previewMessage.Text = "";
        }
        catch (Exception ex)
        {
            _preview.Playback = null;
            _previewMessage.Text = ex.Message;
        }
    }

    private StackPanel AddGroup(Grid panel, int column, string title, XElement element, string[] attributes)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 20, 22) };
        group.Children.Add(StudioUi.Heading(title, column == 0 ? "\uEA18" : "\uE809", column == 0 ? Color.FromRgb(22,169,181) : Color.FromRgb(41,133,219)));
        foreach (var name in attributes)
        {
            group.Children.Add(StudioUi.Label(name switch { "Id" => "Stable ID", "StartFloor" => "Starting floor", "Order" => "Menu order", "BaseProtocol" => "Base armor and stats", "EffectID" => "Gameplay effect", "PlayerModel" => "Player model", _ => name }));
            var current = (string?)element.Attribute(name) ?? "";
            if(name == "PlayerModel" && current.StartsWith("custom:",StringComparison.OrdinalIgnoreCase)) current=current[7..];
            var input = StudioUi.Field(current);
            if(name == "Artwork") group.Children.Add(new ProjectArtworkField(_root,input));
            else if(name == "Chapter")
            {
                var options = new[] { "" }.Concat(ChapterChoices()).Append(current).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var picker = new ComboBox { ItemsSource = options, SelectedItem = current, Margin = new Thickness(0,4,0,12) };
                picker.SelectionChanged += (_,_) => input.Text = picker.SelectedItem as string ?? ""; group.Children.Add(picker);
            }
            else if(name is "BaseProtocol" or "PlayerModel" or "EffectID")
            {
                var options = name switch {
                    "BaseProtocol" => new[] { "BasicProtocol","StandardProtocol","AdvancedProtocol","ExperimentalProtocol" },
                    "PlayerModel" => new[] { "" }.Concat(ModelChoices()).ToArray(),
                    _ => new[] { "None","RegenerateCharges","RestoreChargesOnFloorStart","FillChargesOnFloorStart" }
                };
                var picker = new ComboBox { ItemsSource = options.Append(current).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), SelectedItem = current, Margin = new Thickness(0,4,0,12) };
                picker.SelectionChanged += (_,_) => input.Text = picker.SelectedItem as string ?? ""; group.Children.Add(picker);
            }
            else if(name is "StartFloor" or "Order" or "Amount" or "Maximum" or "Helmet" or "Torso" or "Hands" or "Legs" or "Belt")
                group.Children.Add(StudioUi.Number(input,name=="StartFloor" ? 1 : 0, name=="Order" ? 99 : 999));
            else
            {
                if(name=="Description") { input.AcceptsReturn=true; input.TextWrapping=TextWrapping.Wrap; input.Height=58; }
                group.Children.Add(input);
            }
            _inputs.Add((element, name, input));
        }
        Grid.SetColumn(group,column); panel.Children.Add(group);
        return group;
    }

    private string[] ChapterChoices()
    {
        var folder = Path.Combine(_root,"custom_chapters");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder,"*.xml").SelectMany(path => {
            try { return XDocument.Load(path).Descendants("Chapter").Select(node => (string?)node.Attribute("Id")).OfType<string>().ToArray(); }
            catch { return []; }
        }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id).ToArray();
    }
    private string[] ModelChoices()
    {
        var folder = Path.Combine(_root,"custom_models");
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder,"manifest.xml",SearchOption.AllDirectories).Select(path => {
            try { return (string?)XDocument.Load(path).Root?.Attribute("ID") ?? ""; }
            catch { return ""; }
        }).Where(id=>id.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id).ToArray();
    }

    private void Create()
    {
        try
        {
            var name=_newName.Text.Trim();
            if(name.Length==0) { name="NewProtocol"; for(var i=2;File.Exists(Path.Combine(_folder,name+".xml"));i++) name="NewProtocol"+i; }
            var path = new ProjectTemplateService().Create(new Vector2Project { RootPath = _root },
                ProjectSection.All.Single(section => section.Name == "Protocols"), name);
            _newName.Clear();
            Reload(path);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Save()
    {
        if (_selectedPath is null || _document?.Root?.Element("Protocol") is not { } item) return;
        try
        {
            var proposed = _inputs.ToDictionary(field => (field.Element, field.Name), field => field.Input.Text.Trim());
            if (string.IsNullOrWhiteSpace(proposed[(item, "Id")]) || string.IsNullOrWhiteSpace(proposed[(item, "Name")]))
                throw new InvalidDataException("Protocol ID and name are required.");
            if (string.IsNullOrWhiteSpace(proposed[(item, "Chapter")]))
                throw new InvalidDataException("Choose a chapter for this protocol.");
            if (!int.TryParse(proposed[(item, "StartFloor")], out var floor) || floor < 1)
                throw new InvalidDataException("Starting floor must be at least 1.");
            foreach (var field in _inputs)
                field.Element.SetAttributeValue(field.Name, field.Name == "PlayerModel" && !string.IsNullOrWhiteSpace(field.Input.Text) ? "custom:" + field.Input.Text.Trim() : field.Input.Text.Trim());
            foreach(var entry in _armorModels) {
                var selected=entry.Picker.SelectedItem as string ?? "";
                var armor=item.Elements("Armor").FirstOrDefault(node=>(string?)node.Attribute("Slot")==entry.Slot);
                if(selected.Length==0) armor?.Remove();
                else {
                    armor ??= new XElement("Armor",new XAttribute("Slot",entry.Slot));
                    if(armor.Parent is null) item.Add(armor);
                    armor.SetAttributeValue("Model","custom:"+selected);
                }
            }
            var temporary=_selectedPath+".tmp"; _document.Save(temporary); File.Move(temporary,_selectedPath,true);
            _status($"Saved {Path.GetFileName(_selectedPath)}.");
            Reload(_selectedPath);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Delete()
    {
        if (_selectedPath is null) return;
        if (MessageBox.Show($"Delete {Path.GetFileName(_selectedPath)}?", "Delete protocol", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(_selectedPath); Reload(); _status("Deleted protocol."); }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class ProtocolNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try { return (string?)XDocument.Load((string)value).Root?.Element("Protocol")?.Attribute("Name") ?? Path.GetFileNameWithoutExtension((string)value); }
            catch { return Path.GetFileNameWithoutExtension((string)value); }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
