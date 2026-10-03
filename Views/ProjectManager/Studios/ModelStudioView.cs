using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Threading;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class ModelStudioView : UserControl
{
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _models = new();
    private readonly StackPanel _details = new();
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly Dictionary<string, ComboBox> _choices = new();
    private string? _selectedManifest;
    private string? _selectedModel;
    private readonly GameTrickPreviewService _previewService = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private TrickPreviewControl? _preview;

    public ModelStudioView(string root, Action<string> status)
    {
        _folder = Path.Combine(root, "custom_models");
        _status = status;
        _previewTimer.Tick += (_, _) =>
        {
            if (_preview?.Playback is { Frames.Count: > 0 } playback)
                _preview.Frame = (_preview.Frame + 1) % playback.Frames.Count;
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) _previewTimer.Start(); else _previewTimer.Stop(); };
        Unloaded += (_, _) => _previewTimer.Stop();
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        _models.Margin = new Thickness(12);
        _models.SelectionChanged += (_, _) => LoadSelected();
        workspace.Children.Add(_models);
        var divider = new Border { Background = StudioUi.Resource("ProjectManagerEdgeBrush") };
        Grid.SetColumn(divider, 1); workspace.Children.Add(divider);
        var scroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 20, 24, 30) };
        Grid.SetColumn(scroll, 2); workspace.Children.Add(scroll);
        Content = StudioUi.Shell("Models", "Package character models and inspect their runtime structure.",
            "\uE809", new SolidColorBrush(Color.FromRgb(123, 89, 180)), workspace,
            StudioUi.Button("Open Folder", (_, _) => Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true })),
            StudioUi.Button("Import Model", (_, _) => Import(), true));
        Reload();
    }

    private void Reload(string? select = null)
    {
        Directory.CreateDirectory(_folder);
        var manifests = Directory.EnumerateFiles(_folder, "manifest.xml", SearchOption.AllDirectories).ToList();
        var claimed = manifests.Select(path => ReadModelPath(path)).Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loose = Directory.EnumerateFiles(_folder, "*.xml", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals("manifest.xml", StringComparison.OrdinalIgnoreCase) && !claimed.Contains(path));
        var entries = manifests.Concat(loose).OrderBy(Path.GetFileName).ToList();
        _models.ItemsSource = entries;
        _models.ItemTemplate = EntryTemplate();
        _models.SelectedItem = select is not null && entries.Contains(select) ? select : entries.FirstOrDefault();
        if (entries.Count == 0)
        {
            _details.Children.Clear();
            _details.Children.Add(new TextBlock { Text = "Import a Vector model XML.", Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(30, 120, 0, 0) });
        }
    }

    private static string? ReadModelPath(string manifest)
    {
        try
        {
            var name = (string?)XDocument.Load(manifest).Root?.Attribute("FileName") ?? "model.xml";
            return ProjectManifestService.SafeCombine(Path.GetDirectoryName(manifest)!, name);
        }
        catch { return null; }
    }

    private static DataTemplate EntryTemplate()
    {
        var template = new DataTemplate();
        var block = new FrameworkElementFactory(typeof(TextBlock));
        block.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new ModelNameConverter() });
        block.SetValue(TextBlock.PaddingProperty, new Thickness(9));
        template.VisualTree = block;
        return template;
    }

    private void LoadSelected()
    {
        _details.Children.Clear(); _fields.Clear(); _choices.Clear();
        _preview = null;
        var selected = _models.SelectedItem as string;
        _selectedManifest = selected is not null && Path.GetFileName(selected).Equals("manifest.xml", StringComparison.OrdinalIgnoreCase) ? selected : null;
        _selectedModel = _selectedManifest is not null ? ReadModelPath(_selectedManifest) : selected;
        if (_selectedModel is null) return;
        try
        {
            var manifest = _selectedManifest is null ? null : XDocument.Load(_selectedManifest).Root;
            var document = XDocument.Load(_selectedModel);
            var nodeCount = document.Root?.Element("Nodes")?.Elements().Count() ?? 0;
            var shapeCount = document.Root?.Element("Figures")?.Elements().Count() ?? 0;
            var valid = document.Root?.Name.LocalName == "Scene" && nodeCount > 0;
            var id = (string?)manifest?.Attribute("ID") ?? Path.GetFileName(Path.GetDirectoryName(_selectedModel)) ?? "";
            if (id.Equals("custom_models", StringComparison.OrdinalIgnoreCase)) id = Path.GetFileNameWithoutExtension(_selectedModel);
            var name = (string?)manifest?.Attribute("Name") ?? id.Replace('_', ' ');
            AddPreview(id, valid);
            _details.Children.Add(new TextBlock { Text = name, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            _details.Children.Add(new TextBlock { Text = Path.GetFileName(_selectedModel) + "  |  " + nodeCount + " nodes  |  " + shapeCount + " shapes",
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 16) });
            _details.Children.Add(new TextBlock { Text = valid ? "Ready for Vector 2" : "Model must contain a Scene root and at least one node.",
                Foreground = new SolidColorBrush(valid ? Color.FromRgb(30, 156, 83) : Color.FromRgb(198, 120, 34)), Margin = new Thickness(0, 0, 0, 18) });
            AddField("Name", "Display name", name);
            AddField("ID", "Stable ID", id);
            AddChoice("Category", "Category", (string?)manifest?.Attribute("Category") ?? "Player", ["Player", "Armor", "Character", "Object"]);
            AddField("Author", "Author", (string?)manifest?.Attribute("Author") ?? "Unknown");
            AddChoice("Skeleton", "Skeleton", (string?)manifest?.Attribute("Skeleton") ?? "VectorHuman46", ["VectorHuman46", "ModelDefined"]);
            _details.Children.Add(StudioUi.Label("Runtime reference"));
            var reference = new DockPanel { Margin = new Thickness(0, 5, 0, 15) };
            var copy = StudioUi.Button("Copy Reference", (_, _) => Clipboard.SetText("custom:" + ProjectTemplateService.SafeId(_fields["ID"].Text)));
            DockPanel.SetDock(copy, Dock.Right); reference.Children.Add(copy);
            reference.Children.Add(new TextBlock { Text = "custom:" + id, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center });
            _details.Children.Add(reference);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(StudioUi.Button("Show XML", (_, _) => Process.Start(new ProcessStartInfo(_selectedModel) { UseShellExecute = true })));
            actions.Children.Add(StudioUi.Button("Reload", (_, _) => Reload(selected)));
            var save = StudioUi.Button("Save Package", (_, _) => Save(), true);
            save.IsEnabled = valid; actions.Children.Add(save);
            _details.Children.Add(actions);
        }
        catch (Exception ex) { _status("Could not read model: " + ex.Message); }
    }

    private void AddField(string key, string label, string value)
    {
        _details.Children.Add(StudioUi.Label(label));
        var field = StudioUi.Field(value); _fields[key] = field; _details.Children.Add(field);
    }

    private void AddPreview(string id, bool valid)
    {
        if (!valid) return;
        _preview = new TrickPreviewControl
        {
            Height = 330, PreviewZoom = 5, CenterOnVisibleModel = true,
            ShowDebugEdges = false, ShowModelOutlines = false, ShowModelNodePoints = false,
            ClipToBounds = true, PreviewBackground = StudioUi.Resource("ProjectManagerHoverBrush")
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var animations = new ComboBox { Width = 190, DisplayMemberPath = "Name" };
        DockPanel.SetDock(animations, Dock.Right);
        header.Children.Add(animations);
        header.Children.Add(new TextBlock { Text = "Animated preview", FontSize = 16, FontWeight = FontWeights.SemiBold });
        _details.Children.Add(header);
        _details.Children.Add(_preview);
        var preview = _preview;
        var zoom = new Slider { Minimum = .75, Maximum = 8, Value = 5, Width = 180,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 18), ToolTip = "Preview zoom" };
        zoom.ValueChanged += (_, args) => preview.PreviewZoom = args.NewValue;
        _details.Children.Add(zoom);
        try
        {
            var moves = _previewService.LoadMoves();
            var move = moves.FirstOrDefault(item => item.FileName.Contains("cs_swarm_idle", StringComparison.OrdinalIgnoreCase))
                ?? moves.FirstOrDefault() ?? throw new InvalidDataException("Game animation assets are unavailable.");
            animations.ItemsSource = moves;
            animations.SelectionChanged += (_, _) =>
            {
                if (animations.SelectedItem is not Vector2LevelEditor.Models.GameTrickMove selected) return;
                try
                {
                    preview.Playback = _previewService.LoadPlayback(selected, ["0.xml", "custom:" + id], customModelsDirectory: _folder);
                    preview.Frame = preview.Playback.StartFrame;
                }
                catch (Exception ex) { preview.Playback = null; _status("Model preview failed: " + ex.Message); }
            };
            animations.SelectedItem = move;
        }
        catch (Exception ex)
        {
            _details.Children.Add(new TextBlock { Text = "Preview unavailable: " + ex.Message,
                TextWrapping = TextWrapping.Wrap, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        }
    }

    private void AddChoice(string key, string label, string value, string[] options)
    {
        _details.Children.Add(StudioUi.Label(label));
        var picker = new ComboBox { ItemsSource = options.Append(value).Distinct().ToArray(), SelectedItem = value, Margin = new Thickness(0, 4, 0, 12) };
        _choices[key] = picker; _details.Children.Add(picker);
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "Import a Vector model XML", Filter = "Vector model XML (*.xml)|*.xml" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var scene = XDocument.Load(dialog.FileName);
            if (scene.Root?.Name.LocalName != "Scene" || scene.Root.Element("Nodes")?.Elements().Any() != true)
                throw new InvalidDataException("A Vector model needs a Scene root and at least one node.");
            var id = ProjectTemplateService.SafeId(Path.GetFileNameWithoutExtension(dialog.FileName));
            if (id.Length == 0) throw new InvalidDataException("Model filename needs a stable ID.");
            var package = Path.Combine(_folder, id);
            if (Directory.Exists(package)) throw new IOException("A model package named " + id + " already exists.");
            Directory.CreateDirectory(package);
            var target = Path.Combine(package, "model.xml");
            File.Copy(dialog.FileName, target);
            var manifest = Path.Combine(package, "manifest.xml");
            new XDocument(new XElement("CustomModel", new XAttribute("ID", id), new XAttribute("Name", Path.GetFileNameWithoutExtension(dialog.FileName)),
                new XAttribute("Category", "Player"), new XAttribute("Author", "Unknown"), new XAttribute("FileName", "model.xml"),
                new XAttribute("Skeleton", "VectorHuman46"))).Save(manifest);
            Reload(manifest);
            _status("Imported model as custom:" + id + ".");
        }
        catch (Exception ex) { _status("Model import failed: " + ex.Message); }
    }

    private void Save()
    {
        if (_selectedModel is null) return;
        try
        {
            var id = ProjectTemplateService.SafeId(_fields["ID"].Text);
            if (id.Length == 0) throw new InvalidDataException("Enter a stable ID.");
            var scene = XDocument.Load(_selectedModel);
            if (scene.Root?.Name.LocalName != "Scene" || scene.Root.Element("Nodes")?.Elements().Any() != true)
                throw new InvalidDataException("The model needs at least one node.");
            var manifest = _selectedManifest ?? Path.Combine(Path.GetDirectoryName(_selectedModel)!, "manifest.xml");
            var document = File.Exists(manifest) ? XDocument.Load(manifest) : new XDocument(new XElement("CustomModel"));
            var root = document.Root!;
            foreach (var entry in _fields) root.SetAttributeValue(entry.Key, entry.Key == "ID" ? id : entry.Value.Text.Trim());
            foreach (var entry in _choices) root.SetAttributeValue(entry.Key, entry.Value.SelectedItem as string ?? "");
            root.SetAttributeValue("FileName", Path.GetFileName(_selectedModel));
            var temporary = manifest + ".tmp"; document.Save(temporary); File.Move(temporary, manifest, true);
            _status("Saved model package custom:" + id + ".");
            Reload(manifest);
        }
        catch (Exception ex) { _status("Could not save model: " + ex.Message); }
    }

    private sealed class ModelNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var path = value as string ?? "";
            if (!Path.GetFileName(path).Equals("manifest.xml", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path);
            try { return (string?)XDocument.Load(path).Root?.Attribute("Name") ?? Path.GetFileName(Path.GetDirectoryName(path)) ?? ""; }
            catch { return Path.GetFileName(Path.GetDirectoryName(path)) ?? ""; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
