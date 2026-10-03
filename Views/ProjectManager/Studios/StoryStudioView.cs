using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class StoryStudioView : UserControl
{
    private static readonly string[] NodeTypes = ["Dialogue", "Entrance", "Choice", "QuestSignal", "SetFlag", "Branch", "Cutscene", "SceneEvent", "End"];
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly Grid _body = new();
    private readonly ListBox _stories = new();
    private readonly StackPanel _graph = new();
    private readonly TextBox _newName = StudioUi.Field();
    private XDocument? _document;
    private string? _selectedPath;
    private string _mode = "Story Flow";
    private Grid? _flowWorkspace;
    private string? _selectedNodeId;

    public StoryStudioView(string root, Action<string> status)
    {
        _root = root;
        _folder = Path.Combine(root, "custom_story");
        _status = status;
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition());
        var modes = new ComboBox { ItemsSource = new[] { "Story Flow", "Dialogue", "Cast" }, SelectedIndex = 0 };
        var tabs = new Border { Width = 290, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 5), Child = StudioUi.Segments(modes) };
        modes.SelectionChanged += (_, _) => SwitchMode(modes.SelectedItem as string ?? "Story Flow");
        _body.Children.Add(tabs);
        Content = StudioUi.Shell("Story Graph", "Choose what happens next after each line, choice, or quest event.",
            "\uE8F2", new SolidColorBrush(Color.FromRgb(118, 91, 212)), _body,
            StudioUi.Button("New Story", (_, _) => CreateStory(), true));
        SwitchMode("Story Flow");
    }

    private void SwitchMode(string mode)
    {
        _mode = mode;
        foreach (UIElement child in _body.Children.Cast<UIElement>().Where(child => Grid.GetRow(child) == 1).ToList())
            _body.Children.Remove(child);
        if (mode != "Story Flow")
        {
            var editor = new NarrativeContentStudioView(_root, mode, _status, embedded: true);
            Grid.SetRow(editor, 1);
            _body.Children.Add(editor);
            return;
        }
        if (_flowWorkspace is not null)
        {
            _body.Children.Add(_flowWorkspace);
            return;
        }
        var workspace = new Grid();
        _flowWorkspace = workspace;
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(10, 5, 10, 10) };
        var create = new StackPanel();
        create.Children.Add(StudioUi.Label("STORIES"));
        create.Children.Add(_newName);
        create.Children.Add(StudioUi.Button("Create story", (_, _) => CreateStory(), true));
        DockPanel.SetDock(create, Dock.Top);
        left.Children.Add(create);
        _stories.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_stories);
        workspace.Children.Add(left);
        var scroll = new ScrollViewer { Content = _graph, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        workspace.Children.Add(scroll);
        Grid.SetRow(workspace, 1);
        _body.Children.Add(workspace);
        Reload();
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var files = Directory.EnumerateFiles(_folder, "*.xml").OrderBy(Path.GetFileName).ToList();
        _stories.ItemsSource = files;
        _stories.ItemTemplate = FileTemplate();
        _stories.SelectedItem = selected is not null && files.Contains(selected) ? selected : files.FirstOrDefault();
        if (files.Count == 0)
        {
            _graph.Children.Clear();
            _graph.Children.Add(new TextBlock { Text = "Create a story", HorizontalAlignment = HorizontalAlignment.Center, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 200, 0, 0) });
        }
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new StoryNameConverter() });
        factory.SetValue(TextBlock.PaddingProperty, new Thickness(8));
        template.VisualTree = factory;
        return template;
    }

    private void CreateStory()
    {
        if (_mode != "Story Flow") { SwitchMode("Story Flow"); return; }
        try
        {
            var name = _newName.Text.Trim();
            var id = ProjectTemplateService.SafeId(name);
            if (id.Length == 0)
            {
                name = "New Story"; id = "NewStory";
                for (var i=2;File.Exists(Path.Combine(_folder,id + ".xml"));i++) { name = "New Story " + i; id = "NewStory" + i; }
            }
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, id + ".xml");
            if (File.Exists(path)) throw new IOException($"{id}.xml already exists.");
            new XDocument(new XElement("StoryGraphs", new XElement("StoryGraph",
                new XAttribute("Id", id), new XAttribute("Name", name), new XAttribute("Trigger", "Chapter Start"),
                new XAttribute("Reference", ""), new XAttribute("Once", "1"), new XAttribute("Start", "1"),
                new XElement("Node", new XAttribute("Id", "1"), new XAttribute("Type", "Dialogue"), new XAttribute("Dialogue", "")))))
                .Save(path);
            _newName.Clear();
            Reload(path);
            _status("Created story " + name + ".");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void LoadSelected()
    {
        _selectedPath = _stories.SelectedItem as string;
        _graph.Children.Clear();
        if (_selectedPath is null) return;
        try
        {
            _document = XDocument.Load(_selectedPath, LoadOptions.PreserveWhitespace);
            var story = _document.Descendants("StoryGraph").FirstOrDefault() ?? throw new InvalidDataException("Missing StoryGraph.");
            var panel = new StackPanel { Margin = new Thickness(18, 10, 18, 24) };
            panel.Children.Add(new TextBlock { Text = (string?)story.Attribute("Name") ?? "Story", FontSize = 17, FontWeight = FontWeights.Bold });
            var basics = new Grid();
            for(var i=0;i<3;i++) basics.ColumnDefinitions.Add(new ColumnDefinition());
            var basicIndex=0;
            foreach (var key in new[] { "Name", "Trigger", "Reference" })
            {
                var group = new StackPanel { Margin = new Thickness(0,12,12,10) };
                group.Children.Add(StudioUi.Label(key == "Trigger" ? "Starts when" : key == "Reference" ? "Matching chapter, zone, floor or event" : "Story name"));
                if (key == "Trigger")
                {
                    var triggerPicker = new ComboBox { ItemsSource = new[] { "Menu Open", "Chapter Start", "Zone Selected", "Floor Start", "Custom Event" }, SelectedItem = (string?)story.Attribute(key) ?? "Chapter Start", Margin = new Thickness(0,4,0,12) };
                    triggerPicker.SelectionChanged += (_, _) => story.SetAttributeValue(key,triggerPicker.SelectedItem as string);
                    group.Children.Add(triggerPicker);
                }
                else
                {
                    var field = StudioUi.Field((string?)story.Attribute(key) ?? "");
                    field.TextChanged += (_, _) => story.SetAttributeValue(key, field.Text);
                    group.Children.Add(field);
                }
                Grid.SetColumn(group,basicIndex++); basics.Children.Add(group);
            }
            panel.Children.Add(basics);
            var once = new CheckBox { Content = "Only once", IsChecked = (string?)story.Attribute("Once") != "0", Margin = new Thickness(0,0,0,16) };
            once.SetResourceReference(StyleProperty,"StudioSwitch");
            once.Checked += (_,_) => story.SetAttributeValue("Once","1"); once.Unchecked += (_,_) => story.SetAttributeValue("Once","0"); panel.Children.Add(once);
            var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 14) };
            var picker = new ComboBox { ItemsSource = NodeTypes, SelectedIndex = 0, Width = 150 };
            add.Children.Add(picker);
            add.Children.Add(StudioUi.Button("+ Add Block", (_, _) =>
            {
                var next = story.Elements("Node").Select(node => int.TryParse((string?)node.Attribute("Id"), out var value) ? value : 0).DefaultIfEmpty().Max() + 1;
                var node = new XElement("Node", new XAttribute("Id", next), new XAttribute("Type", picker.SelectedItem as string ?? "Dialogue"));
                story.Add(node); _selectedNodeId = next.ToString();
                RenderNodes(panel, story);
            }, true));
            panel.Children.Add(add);
            _graph.Children.Add(panel);
            RenderNodes(panel, story);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void RenderNodes(StackPanel panel, XElement story)
    {
        while (panel.Children.Count > 0 && panel.Children[^1] is FrameworkElement last && last.Tag is "StoryNodes" or "StoryActions")
            panel.Children.RemoveAt(panel.Children.Count - 1);
        var section = new StackPanel { Tag = "StoryNodes" };
        var flow = new StackPanel { Orientation = Orientation.Horizontal };
        var allNodes = story.Elements("Node").ToList();
        var selected = allNodes.FirstOrDefault(node => (string?)node.Attribute("Id") == _selectedNodeId) ?? allNodes.FirstOrDefault();
        foreach (var node in allNodes)
        {
            var card = new StackPanel { Width = 140, Height = 90 };
            card.Children.Add(new TextBlock { Text = (string?)node.Attribute("Id"), Foreground = new SolidColorBrush(Color.FromRgb(109,89,210)), FontSize = 11, FontWeight = FontWeights.Bold });
            card.Children.Add(new TextBlock { Text = (string?)node.Attribute("Type"), FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0,12,0,6) });
            card.Children.Add(new TextBlock { Text = (string?)node.Attribute("Dialogue") ?? (string?)node.Attribute("Text") ?? (string?)node.Attribute("Signal") ?? (string?)node.Attribute("Event") ?? "", Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxHeight = 28 });
            var select = StudioUi.Button("", (_,_) => { _selectedNodeId = (string?)node.Attribute("Id"); RenderNodes(panel,story); });
            select.Content = card; select.Padding = new Thickness(12); select.Margin = new Thickness(0,0,9,0);
            select.Background = StudioUi.Resource("ProjectManagerCardBrush"); select.BorderThickness = new Thickness(node == selected ? 2 : 1);
            select.BorderBrush = node == selected ? new SolidColorBrush(Color.FromRgb(109,89,210)) : StudioUi.Resource("ProjectManagerEdgeBrush");
            flow.Children.Add(select);
            if (node != allNodes.Last()) flow.Children.Add(new TextBlock { Text = "\uE72A", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,9,0) });
        }
        section.Children.Add(new Border { Padding = new Thickness(14), Background = new SolidColorBrush(Color.FromArgb(14,109,89,210)), CornerRadius = new CornerRadius(8), Child = new ScrollViewer { Content = flow, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        if (selected is not null)
        {
            var node = selected;
            var inspector = new StackPanel { Margin = new Thickness(0,24,0,10) };
            var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0,0,0,14) };
            header.Children.Add(StudioUi.Heading("Edit " + (string?)node.Attribute("Type"), "\uE70F", Color.FromRgb(109,89,210)));
            var remove = StudioUi.Button("Remove", (_,_) => { node.Remove(); RenderNodes(panel,story); });
            DockPanel.SetDock(remove,Dock.Right); header.Children.Add(remove);
            foreach(var direction in new[] { -1,1 })
            {
                var move = StudioUi.Button(direction < 0 ? "\uE72B" : "\uE72A",(_,_) => {
                    var index = allNodes.IndexOf(node); var target=index+direction;
                    if(target<0 || target>=allNodes.Count) return;
                    node.Remove(); if(direction<0) allNodes[target].AddBeforeSelf(node); else allNodes[target].AddAfterSelf(node); RenderNodes(panel,story);
                });
                move.FontFamily = new FontFamily("Segoe MDL2 Assets"); move.ToolTip = direction<0 ? "Move earlier" : "Move later";
                DockPanel.SetDock(move,Dock.Right); header.Children.Add(move);
            }
            inspector.Children.Add(header);
            void Field(XElement target,string key,string label,bool multiline=false,string[]? choices=null)
            {
                inspector.Children.Add(StudioUi.Label(label));
                if (choices is not null)
                {
                    var current=(string?)target.Attribute(key) ?? "";
                    var picker = new ComboBox { ItemsSource = new[] { "" }.Concat(choices).Append(current).Distinct().ToArray(), SelectedItem=current, Margin=new Thickness(0,4,0,12) };
                    picker.SelectionChanged += (_,_) => target.SetAttributeValue(key,picker.SelectedItem as string); inspector.Children.Add(picker);
                }
                else
                {
                    var input=StudioUi.Field((string?)target.Attribute(key) ?? "");
                    if(multiline) { input.Height=65; input.AcceptsReturn=true; input.TextWrapping=TextWrapping.Wrap; }
                    input.TextChanged += (_,_) => target.SetAttributeValue(key,input.Text); inspector.Children.Add(input);
                }
            }
            string[] Ids(string folder,string element) => Directory.Exists(Path.Combine(_root,folder)) ? Directory.EnumerateFiles(Path.Combine(_root,folder),"*.xml").SelectMany(file => {
                try { return XDocument.Load(file).Descendants(element).Select(item => (string?)item.Attribute("Id")).OfType<string>().ToArray(); } catch { return []; }
            }).ToArray() : [];
            var targets=allNodes.Where(item=>item!=node).Select(item=>(string?)item.Attribute("Id") ?? "").ToArray();
            switch((string?)node.Attribute("Type"))
            {
                case "Dialogue": Field(node,"Dialogue","Dialogue",choices:Ids("custom_dialogue","Dialogue")); break;
                case "Entrance": Field(node,"Speaker","Character",choices:Ids("custom_characters","Character")); Field(node,"Text","What they say",true); break;
                case "Choice":
                    Field(node,"Text","Question",true);
                    while(node.Elements("Choice").Count()<2) node.Add(new XElement("Choice",new XAttribute("Text",""),new XAttribute("Next","")));
                    var index=0;
                    foreach(var choice in node.Elements("Choice")) { Field(choice,"Text",++index == 1 ? "First answer" : "Second answer"); Field(choice,"Next","Goes to",choices:targets); }
                    break;
                case "QuestSignal": Field(node,"Signal","Quest event name"); break;
                case "SetFlag": Field(node,"Flag","Story flag"); Field(node,"Value","Value"); break;
                case "Branch": Field(node,"Flag","Story flag"); Field(node,"Equals","Equals"); Field(node,"True","If true",choices:targets); Field(node,"False","If false",choices:targets); break;
                case "Cutscene": case "SceneEvent": Field(node,"Event","Event name"); break;
                default: inspector.Children.Add(StudioUi.Label("The story stops here.")); break;
            }
            section.Children.Add(inspector);
        }
        panel.Children.Add(section);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Tag = "StoryActions" };
        actions.Children.Add(StudioUi.Button("Delete Story", (_, _) => Delete()));
        actions.Children.Add(StudioUi.Button("Revert", (_, _) => LoadSelected()));
        actions.Children.Add(StudioUi.Button("Save Story", (_, _) => Save(), true));
        panel.Children.Add(actions);
    }

    private void Save()
    {
        if (_selectedPath is null || _document is null) return;
        try
        {
            var story=_document.Descendants("StoryGraph").First();
            var nodes=story.Elements("Node").ToList();
            story.SetAttributeValue("Start",(string?)nodes.FirstOrDefault()?.Attribute("Id") ?? "");
            for(var i=0;i<nodes.Count;i++) nodes[i].SetAttributeValue("Next",i+1<nodes.Count ? (string?)nodes[i+1].Attribute("Id") : "");
            var temporary=_selectedPath + ".tmp"; _document.Save(temporary); File.Move(temporary,_selectedPath,true);
            _status($"Saved {Path.GetFileName(_selectedPath)}.");
            Reload(_selectedPath);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Delete()
    {
        if (_selectedPath is null) return;
        if (MessageBox.Show($"Delete {Path.GetFileName(_selectedPath)}?", "Delete story", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(_selectedPath); Reload(); _status("Deleted story."); }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class StoryNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try { return (string?)XDocument.Load((string)value).Descendants("StoryGraph").FirstOrDefault()?.Attribute("Name") ?? Path.GetFileNameWithoutExtension((string)value); }
            catch { return Path.GetFileNameWithoutExtension((string)value); }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
