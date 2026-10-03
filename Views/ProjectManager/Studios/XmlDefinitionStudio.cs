using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

internal sealed class XmlDefinitionStudio : Grid
{
    private readonly string _folder;
    private readonly Func<string, string> _create;
    private readonly Action<string> _status;
    private readonly Func<string, bool> _include;
    private readonly Action<string> _delete;
    private readonly ListBox _files = new();
    private readonly StackPanel _fields = new();
    private readonly TextBox _newName = StudioUi.Field();
    private readonly List<(XElement Element, string Attribute, TextBox Editor)> _attributeEditors = [];
    private readonly List<(XElement Element, TextBox Editor)> _textEditors = [];
    private string? _selectedPath;
    private XDocument? _document;
    private XElement? _target;

    public XmlDefinitionStudio(string folder, string emptyText, Func<string, string> create, Action<string> status,
        Func<string, bool>? include = null, Action<string>? delete = null)
    {
        _folder = folder; _create = create; _status = status; _include = include ?? (_ => true); _delete = delete ?? File.Delete;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        ColumnDefinitions.Add(new ColumnDefinition());
        Margin = new Thickness(0);

        var left = new DockPanel { Margin = new Thickness(10, 8, 10, 10) };
        var createPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        createPanel.Children.Add(StudioUi.Label("New item"));
        createPanel.Children.Add(_newName);
        createPanel.Children.Add(StudioUi.Button("Create", Create, true));
        DockPanel.SetDock(createPanel, Dock.Top); left.Children.Add(createPanel);
        _files.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_files);
        Children.Add(left);
        var divider = new Border { Background = StudioUi.Resource("ProjectManagerEdgeBrush") };
        Grid.SetColumn(divider, 1);
        Children.Add(divider);

        var editor = new DockPanel { Margin = new Thickness(18, 10, 18, 12) };
        var saveBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 12) };
        saveBar.Children.Add(StudioUi.Button("Delete", Delete));
        saveBar.Children.Add(StudioUi.Button("Revert", (_, _) => LoadSelected()));
        saveBar.Children.Add(StudioUi.Button("Save Changes", Save, true));
        DockPanel.SetDock(saveBar, Dock.Top); editor.Children.Add(saveBar);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _fields };
        editor.Children.Add(scroll);
        Grid.SetColumn(editor, 2); Children.Add(editor);
        Reload();
        if (_files.Items.Count == 0) _fields.Children.Add(new TextBlock { Text = emptyText, FontSize = 13, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(20, 60, 0, 0) });
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var paths = Directory.EnumerateFiles(_folder, "*.xml", SearchOption.AllDirectories).Where(_include).OrderBy(Path.GetFileName).ToList();
        _files.ItemsSource = paths;
        _files.DisplayMemberPath = "";
        _files.ItemTemplate = FileTemplate();
        _files.SelectedItem = selected ?? paths.FirstOrDefault();
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new FileNameConverter() });
        factory.SetValue(TextBlock.PaddingProperty, new Thickness(8));
        template.VisualTree = factory;
        return template;
    }

    private void LoadSelected()
    {
        _selectedPath = _files.SelectedItem as string;
        _fields.Children.Clear(); _attributeEditors.Clear(); _textEditors.Clear(); _document = null; _target = null;
        if (_selectedPath is null) return;
        try
        {
            _document = XDocument.Load(_selectedPath, LoadOptions.PreserveWhitespace);
            _target = _document.Root?.HasAttributes == true ? _document.Root : _document.Root?.Elements().FirstOrDefault();
            if (_target is null) throw new InvalidDataException("XML has no editable root.");
            _fields.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(_selectedPath), FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
            RenderElement(_target, _target.Name.LocalName);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Create(object sender, RoutedEventArgs e)
    {
        try { var path = _create(_newName.Text); _newName.Clear(); Reload(path); _status($"Created {Path.GetFileName(path)}."); }
        catch (Exception ex) { _status(ex.Message); }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (_document is null || _target is null || _selectedPath is null) return;
        try
        {
            foreach (var entry in _attributeEditors) entry.Element.SetAttributeValue(entry.Attribute, entry.Editor.Text.Trim());
            foreach (var entry in _textEditors) entry.Element.Value = entry.Editor.Text;
            var temporary = _selectedPath + ".tmp";
            _document.Save(temporary);
            File.Move(temporary, _selectedPath, true);
            _status($"Saved {Path.GetFileName(_selectedPath)}."); Reload(_selectedPath);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void RenderElement(XElement element, string path)
    {
        if (element != _target)
        {
            _fields.Children.Add(new TextBlock { Text = path, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6), Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        }
        foreach (var attribute in element.Attributes())
        {
            _fields.Children.Add(StudioUi.Label(attribute.Name.LocalName));
            var field = StudioUi.Field(attribute.Value);
            _attributeEditors.Add((element, attribute.Name.LocalName, field));
            _fields.Children.Add(field);
        }
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            _fields.Children.Add(StudioUi.Label("Value"));
            var field = StudioUi.Field(element.Value);
            _textEditors.Add((element, field));
            _fields.Children.Add(field);
        }
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in element.Elements())
        {
            names[child.Name.LocalName] = names.GetValueOrDefault(child.Name.LocalName) + 1;
            var suffix = element.Elements(child.Name).Skip(1).Any() ? $" {names[child.Name.LocalName]}" : "";
            RenderElement(child, $"{path} / {child.Name.LocalName}{suffix}");
        }
    }
    private void Delete(object sender, RoutedEventArgs e)
    {
        if (_selectedPath is null) return;
        if (MessageBox.Show($"Delete {Path.GetFileName(_selectedPath)}?", "Delete project item", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _delete(_selectedPath); _status($"Deleted {Path.GetFileName(_selectedPath)}."); Reload(); }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class FileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Path.GetFileNameWithoutExtension(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
