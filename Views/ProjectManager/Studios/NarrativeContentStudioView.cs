using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class NarrativeContentStudioView : UserControl
{
    private readonly string _root;
    private readonly string _mode;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _items = new();
    private readonly StackPanel _inspector = new();
    private readonly TextBox _search = StudioUi.Field();
    private readonly TextBox _newName = StudioUi.Field();
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly Dictionary<string, ComboBox> _choices = new();
    private XDocument? _document;
    private XElement? _item;
    private string? _path;

    public NarrativeContentStudioView(string root, string mode, Action<string> status, bool embedded = false)
    {
        _root = root; _mode = mode; _status = status;
        _folder = Path.Combine(root, mode switch
        {
            "Dialogue" => "custom_dialogue",
            "Cast" => "custom_characters",
            _ => "custom_localization"
        });
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(12) };
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        tools.Children.Add(StudioUi.Search(_search, "Search " + mode.ToLowerInvariant()));
        tools.Children.Add(_newName);
        tools.Children.Add(StudioUi.Button("New " + (mode == "Cast" ? "Character" : mode == "Localization" ? "Phrase" : mode), (_, _) => Create(), true));
        DockPanel.SetDock(tools, Dock.Top); left.Children.Add(tools);
        _search.TextChanged += (_, _) => Reload(_path);
        _items.SelectionChanged += (_, _) => Load();
        left.Children.Add(_items);
        workspace.Children.Add(left);
        var edge = new Border { Background = StudioUi.Resource("ProjectManagerEdgeBrush") };
        Grid.SetColumn(edge, 1); workspace.Children.Add(edge);
        var scroll = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 18, 24, 30) };
        Grid.SetColumn(scroll, 2); workspace.Children.Add(scroll);
        if (embedded) Content = workspace;
        else Content = StudioUi.Shell(mode == "Localization" ? "Localization" : mode,
            mode == "Localization" ? "Create phrase keys and translated player-facing text." : "Edit story content and presentation.",
            mode == "Localization" ? "\uE8C1" : "\uE8F2", new SolidColorBrush(Color.FromRgb(107, 92, 190)), workspace);
        Reload();
    }

    private string ElementName => _mode switch { "Cast" => "Character", "Localization" => "Phrase", _ => "Dialogue" };
    private string IdName => _mode == "Localization" ? "Key" : "Id";

    private void Reload(string? select = null)
    {
        Directory.CreateDirectory(_folder);
        var query = _search.Text.Trim();
        var paths = Directory.EnumerateFiles(_folder, "*.xml").Where(path =>
            query.Length == 0 || Path.GetFileNameWithoutExtension(path).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName).ToList();
        _items.ItemsSource = paths;
        _items.ItemTemplate = ItemTemplate();
        _items.SelectedItem = select is not null && paths.Contains(select) ? select : paths.FirstOrDefault();
        if (paths.Count == 0) { _inspector.Children.Clear(); _path = null; }
    }

    private static DataTemplate ItemTemplate()
    {
        var template = new DataTemplate();
        var block = new FrameworkElementFactory(typeof(TextBlock));
        block.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new FileNameConverter() });
        block.SetValue(TextBlock.PaddingProperty, new Thickness(8));
        template.VisualTree = block;
        return template;
    }

    private void Create()
    {
        try
        {
            var name = _newName.Text.Trim();
            var id = ProjectTemplateService.SafeId(name);
            if (id.Length == 0)
            {
                id = ElementName;
                for (var i = 2; File.Exists(Path.Combine(_folder, id + ".xml")); i++) id = ElementName + i;
                name = id;
            }
            var path = Path.Combine(_folder, id + ".xml");
            if (File.Exists(path)) throw new IOException(id + ".xml already exists.");
            var element = _mode switch
            {
                "Dialogue" => new XElement("Dialogue", new XAttribute("Id", id), new XAttribute("Speaker", ""),
                    new XAttribute("Title", name), new XAttribute("Text", ""), new XAttribute("Button", ""),
                    new XAttribute("Image", ""), new XAttribute("Trigger", "Manual"), new XAttribute("Reference", ""), new XAttribute("Once", "1")),
                "Cast" => new XElement("Character", new XAttribute("Id", id), new XAttribute("Name", name),
                    new XAttribute("Portrait", ""), new XAttribute("Color", "")),
                _ => new XElement("Phrase", new XAttribute("Key", id), new XAttribute("Language", "Default"),
                    new XAttribute("Value", ""))
            };
            new XDocument(new XElement(_mode switch { "Cast" => "Characters", "Localization" => "Localization", _ => "Dialogue" }, element)).Save(path);
            _newName.Clear();
            Reload(path);
            _status("Created " + Path.GetFileName(path) + ".");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Load()
    {
        _path = _items.SelectedItem as string;
        _document = null; _item = null; _fields.Clear(); _choices.Clear(); _inspector.Children.Clear();
        if (_path is null) return;
        try
        {
            _document = XDocument.Load(_path, LoadOptions.PreserveWhitespace);
            _item = _document.Descendants(ElementName).FirstOrDefault(element => element.Attribute(IdName) is not null);
            if (_item is null) throw new InvalidDataException("No " + ElementName + " element in file.");
            var title = (string?)_item.Attribute(IdName) ?? Path.GetFileNameWithoutExtension(_path);
            _inspector.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
            AddField(IdName, "Stable " + (IdName == "Key" ? "phrase key" : "ID"));
            if (_mode == "Localization")
            {
                AddField("Language", "Language");
                AddField("Value", "Translation", true);
            }
            else if (_mode == "Cast")
            {
                AddField("Name", "Display name");
                AddArtwork("Portrait", "Portrait");
                AddField("Color", "Accent color");
            }
            else
            {
                AddField("Title", "Display name");
                AddChoice("Speaker", "Speaker", LoadIds("custom_characters", "Character"));
                AddField("Text", "Dialogue text", true);
                AddField("Button", "Continue button");
                AddArtwork("Image", "Override portrait");
                AddChoice("Trigger", "Trigger", ["Manual", "Chapter Start", "Zone Enter", "Room Enter", "Quest Event"]);
                AddField("Reference", "Trigger reference");
                AddChoice("Once", "Trigger frequency", ["1", "0"], ["Once", "Every time"]);
            }
            var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            commands.Children.Add(StudioUi.Button("Delete", (_, _) => Delete()));
            commands.Children.Add(StudioUi.Button("Revert", (_, _) => Load()));
            commands.Children.Add(StudioUi.Button("Save Changes", (_, _) => Save(), true));
            _inspector.Children.Add(commands);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private string[] LoadIds(string folder, string element)
    {
        var path = Path.Combine(_root, folder);
        if (!Directory.Exists(path)) return [];
        return Directory.EnumerateFiles(path, "*.xml").SelectMany(file =>
        {
            try { return XDocument.Load(file).Descendants(element).Select(item => (string?)item.Attribute("Id")).OfType<string>().ToArray(); }
            catch { return []; }
        }).Distinct().OrderBy(id => id).ToArray();
    }

    private void AddField(string attribute, string label, bool multiline = false)
    {
        _inspector.Children.Add(StudioUi.Label(label));
        var field = StudioUi.Field((string?)_item?.Attribute(attribute) ?? "");
        if (multiline) { field.Height = 100; field.AcceptsReturn = true; field.TextWrapping = TextWrapping.Wrap; }
        _fields[attribute] = field; _inspector.Children.Add(field);
    }

    private void AddArtwork(string attribute, string label)
    {
        _inspector.Children.Add(StudioUi.Label(label));
        var field = StudioUi.Field((string?)_item?.Attribute(attribute) ?? "");
        _fields[attribute] = field;
        _inspector.Children.Add(new ProjectArtworkField(_root, field));
    }

    private void AddChoice(string attribute, string label, string[] choices, string[]? labels = null)
    {
        _inspector.Children.Add(StudioUi.Label(label));
        var current = (string?)_item?.Attribute(attribute) ?? "";
        var picker = new ComboBox { Margin = new Thickness(0, 4, 0, 12) };
        if (labels is null)
        {
            picker.ItemsSource = new[] { "" }.Concat(choices).Append(current).Distinct().ToArray();
            picker.SelectedItem = current;
        }
        else
        {
            for (var index = 0; index < choices.Length; index++)
                picker.Items.Add(new ComboBoxItem { Content = labels[index], Tag = choices[index] });
            picker.SelectedItem = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == current);
        }
        _choices[attribute] = picker; _inspector.Children.Add(picker);
    }

    private void Save()
    {
        if (_document is null || _item is null || _path is null) return;
        try
        {
            var id = ProjectTemplateService.SafeId(_fields[IdName].Text);
            if (id.Length == 0) throw new InvalidDataException("Enter a valid " + IdName + ".");
            foreach (var field in _fields) _item.SetAttributeValue(field.Key, field.Key == IdName ? id : field.Value.Text);
            foreach (var choice in _choices)
                _item.SetAttributeValue(choice.Key, choice.Value.SelectedItem is ComboBoxItem item
                    ? item.Tag as string ?? "" : choice.Value.SelectedItem as string ?? "");
            var temporary = _path + ".tmp";
            _document.Save(temporary); File.Move(temporary, _path, true);
            _status("Saved " + Path.GetFileName(_path) + ".");
            Reload(_path);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Delete()
    {
        if (_path is null) return;
        if (MessageBox.Show("Delete " + Path.GetFileName(_path) + "?", "Delete " + ElementName,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(_path); _status("Deleted " + Path.GetFileName(_path) + "."); Reload(); }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class FileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Path.GetFileNameWithoutExtension(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
