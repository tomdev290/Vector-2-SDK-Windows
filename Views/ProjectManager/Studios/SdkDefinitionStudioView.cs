using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public enum SdkDefinitionKind { Mission, Reward, Tutorial }

public sealed class SdkDefinitionStudioView : UserControl
{
    private readonly string _folder;
    private readonly string _root;
    private readonly SdkDefinitionKind _kind;
    private readonly Action<string> _status;
    private readonly ListBox _library = new();
    private readonly StackPanel _details = new();
    private readonly TextBox _search = StudioUi.Field();
    private readonly Dictionary<string, TextBox> _fields = [];
    private XDocument? _document;
    private string? _path;

    public SdkDefinitionStudioView(string root, SdkDefinitionKind kind, Action<string> status)
    {
        _root = root; _kind = kind; _status = status;
        var section = ProjectSection.All.Single(value => value.Name == kind + "s");
        _folder = Path.Combine(root, section.Folder);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel();
        var search = StudioUi.Search(_search, "Find a " + kind.ToString().ToLowerInvariant());
        search.Margin = new Thickness(12); DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        _search.TextChanged += (_, _) => Reload(_path);
        _library.SelectionChanged += (_, _) => LoadSelected();
        left.Children.Add(_library);
        body.Children.Add(new Border { BorderBrush = StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness = new Thickness(0,0,1,0), Child = left });
        var scroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        Content = StudioUi.Shell(kind + "s", kind switch { SdkDefinitionKind.Mission => "Choose the goal, availability, and payout.", SdkDefinitionKind.Reward => "Build the reward a player receives.", _ => "Arrange the instructions shown to the player." },
            kind switch { SdkDefinitionKind.Mission => "\uE9D9", SdkDefinitionKind.Reward => "\uF133", _ => "\uE82F" },
            new SolidColorBrush(kind switch { SdkDefinitionKind.Mission => Color.FromRgb(225,82,89), SdkDefinitionKind.Reward => Color.FromRgb(234,155,50), _ => Color.FromRgb(114,98,218) }),
            body, StudioUi.Button("Reload", (_, _) => Reload(_path)), StudioUi.Button("New " + kind, (_, _) => Create(), true));
        Reload();
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var entries = new List<ListBoxItem>();
        foreach (var file in Directory.EnumerateFiles(_folder, "*.xml").OrderBy(Path.GetFileName))
        {
            try
            {
                var root = XDocument.Load(file).Root;
                if (root?.Name.LocalName != "Custom" + _kind) continue;
                var name = (string?)root.Attribute("Name") ?? Path.GetFileNameWithoutExtension(file);
                var stableId = (string?)root.Attribute("Id") ?? "";
                if (!name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) &&
                    !stableId.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                var labels = new StackPanel();
                labels.Children.Add(new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                labels.Children.Add(new TextBlock { Text = _kind == SdkDefinitionKind.Tutorial ? $"{root.Elements("Step").Count()} steps" : $"{(string?)root.Attribute("Type")}  {(string?)root.Attribute(_kind == SdkDefinitionKind.Mission ? "Target" : "Amount")}", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0,4,0,0) });
                entries.Add(new ListBoxItem { Tag = file, Content = labels });
            }
            catch (Exception ex) { _status($"Could not load {Path.GetFileName(file)}: {ex.Message}"); }
        }
        _library.ItemsSource = entries;
        _library.SelectedItem = entries.FirstOrDefault(item => (string)item.Tag == selected) ?? entries.FirstOrDefault();
        if (entries.Count == 0)
        {
            _details.Children.Clear();
            var empty = new StackPanel { Margin = new Thickness(32,100,32,0), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(new TextBlock { Text = "No " + _kind.ToString().ToLowerInvariant() + " selected", FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0,0,0,16) });
            empty.Children.Add(StudioUi.Button("New " + _kind, (_, _) => Create(), true));
            _details.Children.Add(empty);
        }
    }

    private void Create()
    {
        try
        {
            var name = "New" + _kind;
            for (var i = 2; File.Exists(Path.Combine(_folder, name + ".xml")); i++) name = "New" + _kind + i;
            var path = new ProjectTemplateService().Create(new Vector2Project { RootPath = _root }, ProjectSection.All.Single(section => section.Name == _kind + "s"), name);
            Reload(path); _fields.GetValueOrDefault("Name")?.Focus();
            _status("Created " + _kind.ToString().ToLowerInvariant() + " " + name + ".");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void LoadSelected()
    {
        _path = (_library.SelectedItem as ListBoxItem)?.Tag as string;
        _details.Children.Clear(); _fields.Clear();
        if (_path is null) return;
        try
        {
            _document = XDocument.Load(_path, LoadOptions.PreserveWhitespace);
            var root = _document.Root!;
            var panel = new StackPanel { MaxWidth = 930, Margin = new Thickness(30,24,30,30) };
            panel.Children.Add(new TextBlock { Text = _kind + " Details", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,18) });
            Field(panel, root, "Name", "Display name");
            Field(panel, root, "Id", _kind + " ID");
            var description = Field(panel, root, "Description", "Description"); description.Height = 55; description.AcceptsReturn = true; description.TextWrapping = TextWrapping.Wrap;
            if (_kind == SdkDefinitionKind.Mission) Mission(panel, root);
            if (_kind == SdkDefinitionKind.Reward) Reward(panel, root);
            if (_kind == SdkDefinitionKind.Tutorial) Tutorial(panel, root);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,24,0,0) };
            bar.Children.Add(StudioUi.Button("Delete", (_, _) => Delete()));
            bar.Children.Add(StudioUi.Button("Revert", (_, _) => LoadSelected()));
            bar.Children.Add(StudioUi.Button("Save " + _kind, (_, _) => Save(), true));
            panel.Children.Add(bar); _details.Children.Add(panel);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private TextBox Field(Panel panel, XElement root, string key, string label, string fallback = "")
    {
        panel.Children.Add(StudioUi.Label(label));
        var input = StudioUi.Field((string?)root.Attribute(key) ?? fallback);
        _fields[key] = input; panel.Children.Add(input); return input;
    }

    private void Number(Panel panel, XElement root, string key, string label, int minimum, int maximum, string fallback)
    {
        var input = Field(panel, root, key, label, fallback);
        panel.Children.Remove(input); panel.Children.Add(StudioUi.Number(input, minimum, maximum));
    }

    private ComboBox Choice(Panel panel, XElement root, string key, string label, string[] choices)
    {
        panel.Children.Add(StudioUi.Label(label));
        var value = (string?)root.Attribute(key) ?? choices[0];
        if (!choices.Contains(value)) choices = [..choices, value];
        var picker = new ComboBox { ItemsSource = choices, SelectedItem = value, Margin = new Thickness(0,5,0,15) };
        root.SetAttributeValue(key, value);
        picker.SelectionChanged += (_, _) => root.SetAttributeValue(key, picker.SelectedItem as string);
        panel.Children.Add(picker); return picker;
    }

    private void Mission(Panel panel, XElement root)
    {
        panel.Children.Add(StudioUi.Heading("Objective Board", "\uE9D9", Color.FromRgb(220,76,83)));
        Choice(panel, root, "Type", "Objective", ["Points", "Money", "StuntsCount", "ContexCombo", "MaxPoints", "RedCoinsCount"]);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var goal = new StackPanel { Margin = new Thickness(0,0,16,0) };
        Number(goal, root, "Target", "Target", 1, 100000, "100");
        Number(goal, root, "Difficulty", "Difficulty", 1, 3, "1");
        Number(goal, root, "Amount", "Credits payout", 0, 100000, "100");
        columns.Children.Add(goal);
        var availability = new StackPanel();
        Number(availability, root, "Order", "Available from floor", 0, 999, "1");
        Number(availability, root, "MaximumFloor", "Last floor (0 = unlimited)", 0, 999, "0");
        Number(availability, root, "Weight", "Selection weight", 1, 1000, "100");
        Grid.SetColumn(availability,1); columns.Children.Add(availability); panel.Children.Add(columns);
        Field(panel, root, "Protocol", "Protocol filter", "Any");
        Field(panel, root, "Reference", "Additional reward ID");
    }

    private void Reward(Panel panel, XElement root)
    {
        panel.Children.Add(StudioUi.Heading("Player Payout", "\uF133", Color.FromRgb(222,145,40)));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var preview = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        preview.Children.Add(new TextBlock { Text = "\uF133", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 45, Foreground = new SolidColorBrush(Color.FromRgb(228,154,49)), HorizontalAlignment = HorizontalAlignment.Center });
        var amount = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,18,0,5) }; preview.Children.Add(amount);
        var kind = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center }; preview.Children.Add(kind);
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(20,234,155,50)), CornerRadius = new CornerRadius(8), Margin = new Thickness(0,0,20,0), Padding = new Thickness(12), Child = preview });
        var controls = new StackPanel();
        var type = Choice(controls, root, "Type", "Reward type", ["Credits", "Premium Currency", "Card", "Energy", "Item"]);
        Number(controls, root, "Amount", "Amount", 1, 1000000, "100");
        Field(controls, root, "Reference", "Card or item ID");
        void Preview() { amount.Text = "x" + _fields["Amount"].Text; kind.Text = type.SelectedItem as string; }
        type.SelectionChanged += (_, _) => Preview(); _fields["Amount"].TextChanged += (_, _) => Preview(); Preview();
        Grid.SetColumn(controls,1); grid.Children.Add(controls); panel.Children.Add(grid);
    }

    private void Tutorial(Panel panel, XElement root)
    {
        panel.Children.Add(StudioUi.Heading("Tutorial Sequence", "\uE82F", Color.FromRgb(114,98,218)));
        Choice(panel, root, "Type", "Start when", ["Manual", "Menu Open", "Chapter Start", "Zone Start", "Floor Start", "Custom Event"]);
        Field(panel, root, "Reference", "Trigger reference");
        var once = new CheckBox { Content = "Show only once", IsChecked = (string?)root.Attribute("Once") != "0", Margin = new Thickness(0,0,0,18) };
        once.SetResourceReference(StyleProperty, "StudioSwitch");
        once.Checked += (_, _) => root.SetAttributeValue("Once", "1"); once.Unchecked += (_, _) => root.SetAttributeValue("Once", "0"); panel.Children.Add(once);
        var steps = new StackPanel(); panel.Children.Add(steps);
        void RenderSteps()
        {
            steps.Children.Clear();
            var elements = root.Elements("Step").ToList();
            for (var index = 0; index < elements.Count; index++)
            {
                var step = elements[index]; var number = index;
                var block = new StackPanel { Margin = new Thickness(0,0,0,12) };
                var top = new DockPanel { LastChildFill = false };
                top.Children.Add(new TextBlock { Text = "Step " + (index + 1), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var remove = StudioUi.Button("Remove", (_, _) => { step.Remove(); RenderSteps(); }); DockPanel.SetDock(remove,Dock.Right); top.Children.Add(remove);
                var up = StudioUi.Button("Move Up", (_, _) => { if (number == 0) return; step.Remove(); elements[number-1].AddBeforeSelf(step); RenderSteps(); }); DockPanel.SetDock(up,Dock.Right); top.Children.Add(up);
                block.Children.Add(top);
                var message = StudioUi.Field((string?)step.Attribute("Message") ?? ""); message.AcceptsReturn = true; message.Height = 66; message.TextWrapping = TextWrapping.Wrap;
                message.TextChanged += (_, _) => step.SetAttributeValue("Message", message.Text); block.Children.Add(message);
                block.Children.Add(StudioUi.Label("Portrait"));
                var portrait = StudioUi.Field((string?)step.Attribute("Portrait") ?? ""); portrait.TextChanged += (_, _) => step.SetAttributeValue("Portrait", portrait.Text); block.Children.Add(portrait);
                steps.Children.Add(block);
            }
        }
        panel.Children.Add(StudioUi.Button("+ Add Step", (_, _) => { root.Add(new XElement("Step", new XAttribute("Message",""), new XAttribute("Portrait",""))); RenderSteps(); }));
        RenderSteps();
    }

    private void Save()
    {
        if (_document?.Root is not { } root || _path is null) return;
        try
        {
            foreach (var key in new[] { "Id", "Name" }) if (string.IsNullOrWhiteSpace(_fields[key].Text)) throw new InvalidDataException(key + " is required.");
            foreach (var key in new[] { "Target", "Amount", "Order", "MaximumFloor", "Difficulty", "Weight" })
                if (_fields.TryGetValue(key, out var field) && (!int.TryParse(field.Text, out var value) || value < 0)) throw new InvalidDataException(key + " must be a non-negative whole number.");
            foreach (var pair in _fields) root.SetAttributeValue(pair.Key, pair.Value.Text.Trim());
            if (_kind == SdkDefinitionKind.Tutorial)
            {
                var steps = root.Elements("Step").ToList();
                if (steps.Count == 0 || steps.Any(step => string.IsNullOrWhiteSpace((string?)step.Attribute("Message")))) throw new InvalidDataException("Each tutorial needs at least one step, and every step needs an instruction.");
                for (var i=0;i<steps.Count;i++) steps[i].SetAttributeValue("Order",i+1);
            }
            var temporary = _path + ".tmp"; _document.Save(temporary); File.Move(temporary,_path,true);
            _status("Saved " + _kind.ToString().ToLowerInvariant() + "."); Reload(_path);
        }
        catch(Exception ex) { _status(ex.Message); }
    }

    private void Delete()
    {
        if (_path is null || MessageBox.Show("Delete this " + _kind.ToString().ToLowerInvariant() + "?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(_path); Reload(); _status("Deleted " + _kind.ToString().ToLowerInvariant() + "."); }
        catch(Exception ex) { _status(ex.Message); }
    }
}
