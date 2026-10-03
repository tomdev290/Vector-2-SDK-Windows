using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class TriggerStudioView : UserControl
{
    private static readonly string[] Events = TriggerRuntimeSchema.Events.Select(item => item.Name).ToArray();
    private static readonly string[] Conditions = TriggerRuntimeSchema.Conditions.Select(item => item.Name).ToArray();
    private static readonly string[] Actions = TriggerRuntimeSchema.Actions.Select(item => item.Name).ToArray();
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _library = new();
    private readonly StackPanel _behavior = new();
    private readonly StackPanel _identity = new();
    private readonly StackPanel _checks = new();
    private readonly TextBox _raw = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _newName = StudioUi.Field();
    private readonly TextBox _completionFilter = StudioUi.Field();
    private XDocument? _document;
    private string? _selectedPath;
    private int _loopIndex;

    public TriggerStudioView(string root, Action<string> status)
    {
        _root = root;
        _folder = Path.Combine(root, "custom_triggers");
        _status = status;
        var shell = new Grid { Background = StudioUi.Resource("ProjectManagerCardBrush") };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 270 });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });

        var library = new DockPanel { Margin = new Thickness(10) };
        var libraryHeader = new StackPanel();
        libraryHeader.Children.Add(new TextBlock { Text = "TRIGGER LIBRARY", FontSize = 11, FontWeight = FontWeights.Bold });
        libraryHeader.Children.Add(new TextBlock { Text = "Reusable in every room.", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 8) };
        var open = StudioUi.Button("\uE8B5", (_, _) => OpenXml());
        open.FontFamily = new FontFamily("Segoe MDL2 Assets");
        open.ToolTip = "Open a trigger or room XML";
        controls.Children.Add(open);
        var create = StudioUi.Button("+", (_, _) => Create(), true);
        create.ToolTip = "Create trigger";
        controls.Children.Add(create);
        libraryHeader.Children.Add(controls);
        libraryHeader.Children.Add(_newName);
        _newName.ToolTip = "Trigger name (optional)";
        DockPanel.SetDock(libraryHeader, Dock.Top);
        library.Children.Add(libraryHeader);
        _library.SelectionChanged += (_, _) => LoadSelected();
        library.Children.Add(_library);
        shell.Children.Add(library);

        var guide = new StackPanel { Margin = new Thickness(12, 14, 12, 0) };
        guide.Children.Add(new TextBlock { Text = "Trigger guide", FontWeight = FontWeights.Bold, FontSize = 13 });
        guide.Children.Add(StudioUi.Label("Start with an event, add optional conditions, then actions."));
        foreach (var item in new[] { ("1  WHEN", "Starts the loop when an event occurs."), ("2  IF", "Conditions must pass before actions run."), ("3  DO", "Actions run in order from top to bottom.") })
        {
            guide.Children.Add(new TextBlock { Text = item.Item1, FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 14, 0, 3) });
            guide.Children.Add(new TextBlock { Text = item.Item2, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap });
        }
        guide.Children.Add(new TextBlock { Text = "Messages", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,24,0,8) });
        foreach (var example in new[] { "Content.Zone:zone_id", "Content.Dialogue:dialogue_id", "Content.Tutorial:tutorial_id", "Content.Story:story_id", "Content.CustomEvent:event_name" })
            guide.Children.Add(new TextBlock { Text = example, FontFamily = new FontFamily("Consolas"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,3), Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        var inspector = new StackPanel();
        inspector.Children.Add(guide);
        inspector.Children.Add(_identity);
        inspector.Children.Add(_checks);
        _identity.Margin = new Thickness(12, 0, 12, 0);
        var guideScroll = new ScrollViewer { Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(guideScroll, 1);
        shell.Children.Add(guideScroll);

        var center = new DockPanel { Margin = new Thickness(12, 12, 12, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = "Behavior", FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var addLoop = StudioUi.Button("+ Loop", (_, _) => AddLoop());
        addLoop.Margin = new Thickness(14, 0, 0, 0);
        title.Children.Add(addLoop);
        var removeLoop = StudioUi.Button("\uE74D", (_, _) => {
            var loops = _document?.Root?.Element("Content")?.Elements("Loop").ToList();
            if (loops is null || loops.Count <= 1) { _status("Keep at least one loop."); return; }
            loops[Math.Clamp(_loopIndex, 0, loops.Count - 1)].Remove(); Render();
        });
        removeLoop.FontFamily = new FontFamily("Segoe MDL2 Assets"); removeLoop.ToolTip = "Delete loop";
        title.Children.Add(removeLoop);
        DockPanel.SetDock(title, Dock.Top);
        center.Children.Add(title);
        center.Children.Add(new ScrollViewer { Content = _behavior, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(center, 2);
        shell.Children.Add(center);

        var xml = new DockPanel { Margin = new Thickness(8, 12, 12, 0) };
        var xmlHeader = new StackPanel();
        xmlHeader.Children.Add(new TextBlock { Text = "Raw Vector XML", FontSize = 13, FontWeight = FontWeights.Bold });
        xmlHeader.Children.Add(new TextBlock { Text = "Apply XML to rebuild the visual editor.", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 4, 0, 9) });
        DockPanel.SetDock(xmlHeader, Dock.Top);
        xml.Children.Add(xmlHeader);
        var xmlActions = new WrapPanel { Orientation = Orientation.Horizontal };
        xmlActions.Children.Add(StudioUi.Button("Apply XML", (_, _) => ApplyRawXml()));
        xmlActions.Children.Add(StudioUi.Button("Format", (_, _) => FormatRawXml()));
        xmlActions.Children.Add(StudioUi.Button("Revert", (_, _) => Render()));
        xmlActions.Children.Add(StudioUi.Button("Copy", (_, _) => CopyRawXml()));
        xmlActions.Children.Add(StudioUi.Button("Save Changes", (_, _) => Save(), true));
        xmlActions.Children.Add(StudioUi.Button("Place in Open Room", (_, _) =>
        {
            try
            {
                if (_document?.Root is null) throw new InvalidDataException("Select or create a trigger first.");
                if (Application.Current?.MainWindow?.DataContext is not Vector2LevelEditor.ViewModels.MainWindowViewModel editor)
                    throw new InvalidDataException("No level editor is open.");
                editor.PlaceAuthoredTrigger(_document.ToString());
                _status("Placed a copy in the open room.");
            }
            catch (Exception ex) { _status(ex.Message); }
        }, true));
        DockPanel.SetDock(xmlActions, Dock.Bottom);
        xml.Children.Add(xmlActions);
        var rawWorkspace = new Grid();
        rawWorkspace.RowDefinitions.Add(new RowDefinition());
        rawWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rawWorkspace.Children.Add(new TriggerXmlEditor(_raw));
        var assistant = new DockPanel { Margin = new Thickness(0, 7, 0, 7) };
        _completionFilter.ToolTip = "Find an event, condition or action";
        var insert = StudioUi.Button("Insert", (_, _) => OpenCompletionMenu());
        DockPanel.SetDock(insert, Dock.Right);
        assistant.Children.Add(insert);
        assistant.Children.Add(_completionFilter);
        Grid.SetRow(assistant, 1);
        rawWorkspace.Children.Add(assistant);
        xml.Children.Add(rawWorkspace);
        Grid.SetColumn(xml, 3);
        shell.Children.Add(xml);
        foreach (var column in new[] { 1, 2 })
        {
            var divider = new GridSplitter
            {
                Width = 4, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                Background = StudioUi.Resource("ProjectManagerEdgeBrush"),
                ToolTip = "Resize workspace columns"
            };
            Grid.SetColumn(divider, column);
            shell.Children.Add(divider);
        }
        var workspace = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 10, 12, 0) };
        var panels = StudioUi.Button("Panels", (_, _) => { });
        var panelMenu = new ContextMenu();
        foreach (var entry in new[] { (Name: "Setup", View: (FrameworkElement)guideScroll, Column: 1),
            (Name: "Behavior", View: (FrameworkElement)center, Column: 2), (Name: "Raw XML", View: (FrameworkElement)xml, Column: 3) })
        {
            var item = new MenuItem { Header = entry.Name, IsCheckable = true, IsChecked = true };
            var definition = shell.ColumnDefinitions[entry.Column];
            var savedWidth = definition.Width;
            var minimum = definition.MinWidth;
            item.Click += (_, _) =>
            {
                if (!item.IsChecked) savedWidth = definition.Width;
                entry.View.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                definition.MinWidth = item.IsChecked ? minimum : 0;
                definition.Width = item.IsChecked ? savedWidth : new GridLength(0);
            };
            panelMenu.Items.Add(item);
        }
        panels.Click += (_, _) => OpenMenu(panels, panelMenu);
        toolbar.Children.Add(panels);
        var guideButton = StudioUi.Button("Guide", (_, _) =>
        {
            guide.Visibility = guide.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        });
        guideButton.ToolTip = "Show or hide the trigger guide";
        toolbar.Children.Add(guideButton);
        toolbar.Children.Add(TemplateButton());
        toolbar.Children.Add(OutcomeButton());
        DockPanel.SetDock(toolbar, Dock.Top);
        workspace.Children.Add(toolbar);
        workspace.Children.Add(shell);
        Content = StudioUi.Shell("Trigger Designer", "Choose what starts the trigger and what happens next.",
            "\uE7C8", new SolidColorBrush(Color.FromRgb(91, 84, 220)), workspace);
        Reload();
    }

    private void Reload(string? selected = null)
    {
        Directory.CreateDirectory(_folder);
        var files = Directory.EnumerateFiles(_folder, "*.xml").OrderBy(Path.GetFileName).ToList();
        _library.ItemsSource = files;
        _library.ItemTemplate = FileTemplate();
        _library.SelectedItem = selected is not null && files.Contains(selected) ? selected : files.FirstOrDefault();
        if (files.Count == 0)
        {
            _behavior.Children.Clear();
            _behavior.Children.Add(new TextBlock { Text = "Create or open a trigger.", Margin = new Thickness(0, 28, 0, 0), Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
            _raw.Clear();
        }
    }

    private static DataTemplate FileTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new FileNameConverter() });
        factory.SetValue(TextBlock.PaddingProperty, new Thickness(8, 9, 8, 9));
        template.VisualTree = factory;
        return template;
    }

    private void LoadSelected()
    {
        _selectedPath = _library.SelectedItem as string;
        if (_selectedPath is null) return;
        try
        {
            _document = XDocument.Load(_selectedPath, LoadOptions.PreserveWhitespace);
            if (_document.Root?.Name.LocalName != "Trigger") throw new InvalidDataException("This file does not contain a standalone trigger.");
            _loopIndex = 0;
            Render();
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Render()
    {
        _behavior.Children.Clear();
        if (_document?.Root is not { } trigger) return;
        RenderIdentity(trigger);
        var loops = trigger.Element("Content")?.Elements("Loop").ToList() ?? [];
        _raw.Text = _document.ToString();
        _behavior.Children.Add(StudioUi.Label($"TRIGGER: {(string?)trigger.Attribute("Name") ?? ""}"));
        if (loops.Count == 0)
        {
            _behavior.Children.Add(new TextBlock { Text = "A game template controls this trigger. Add a loop to extend it.", Margin = new Thickness(0, 24, 0, 0), Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), TextWrapping = TextWrapping.Wrap });
            RenderChecks(trigger);
            return;
        }
        _loopIndex = Math.Clamp(_loopIndex, 0, loops.Count - 1);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 12) };
        tabs.Children.Add(new TextBlock { Text = "Loop", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var loopPicker = new ComboBox { Width = 100, ItemsSource = Enumerable.Range(1, loops.Count).Select(index => $"Loop {index}").ToArray(),
            SelectedIndex = _loopIndex, ToolTip = "Active behavior loop" };
        loopPicker.SelectionChanged += (_, _) =>
        {
            if (loopPicker.SelectedIndex < 0 || loopPicker.SelectedIndex == _loopIndex) return;
            _loopIndex = loopPicker.SelectedIndex; Render();
        };
        tabs.Children.Add(loopPicker);
        _behavior.Children.Add(tabs);
        var loop = loops[_loopIndex];
        AddColumn(loop, "WHEN", "Events", Events);
        AddColumn(loop, "IF", "Conditions", Conditions);
        AddColumn(loop, "DO", "Actions", Actions);
        RenderChecks(trigger);
    }

    private void RenderIdentity(XElement trigger)
    {
        _identity.Children.Clear();
        _identity.Margin = new Thickness(0, 24, 0, 0);
        _identity.Children.Add(new TextBlock { Text = "Trigger area", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
        _identity.Children.Add(new TextBlock { Text = "The part of the room that detects the player.", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _identity.Children.Add(TriggerAreaPreview(trigger));
        _identity.Children.Add(AttributeField(trigger, "Name", "Trigger name"));
        var bounds = new UniformGrid { Columns = 2, Margin = new Thickness(0, 5, 0, 0) };
        foreach (var name in new[] { "X", "Y", "Width", "Height" }) bounds.Children.Add(AttributeField(trigger, name, name));
        _identity.Children.Add(bounds);

        _identity.Children.Add(new TextBlock { Text = "Starting variables", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 18, 0, 3) });
        _identity.Children.Add(new TextBlock { Text = "Values available when this trigger starts.", FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var init = trigger.Element("Content")?.Element("Init");
        foreach (var variable in init?.Elements("SetVariable").ToList() ?? [])
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = StudioUi.Field((string?)variable.Attribute("Name") ?? "");
            name.ToolTip = "Variable name";
            name.TextChanged += (_, _) => UpdateAttribute(variable, "Name", name.Text);
            row.Children.Add(name);
            var type = new ComboBox { ItemsSource = new[] { "Bool", "Int", "Float", "String", "Node", "AI" }, SelectedValue = (string?)variable.Attribute("Type") ?? "Bool", Margin = new Thickness(5, 0, 5, 0) };
            type.SelectionChanged += (_, _) => UpdateAttribute(variable, "Type", type.SelectedValue as string ?? "Bool");
            Grid.SetColumn(type, 1); row.Children.Add(type);
            var value = StudioUi.Field((string?)variable.Attribute("Value") ?? "");
            value.ToolTip = "Initial value";
            value.TextChanged += (_, _) => UpdateAttribute(variable, "Value", value.Text);
            Grid.SetColumn(value, 2); row.Children.Add(value);
            var remove = StudioUi.Button("\uE74D", (_, _) => { variable.Remove(); Render(); });
            remove.FontFamily = new FontFamily("Segoe MDL2 Assets"); remove.ToolTip = "Remove variable";
            Grid.SetColumn(remove, 3); row.Children.Add(remove);
            _identity.Children.Add(row);
        }
        var addVariable = StudioUi.Button("+ Add variable", (_, _) =>
        {
            var content = trigger.Element("Content") ?? new XElement("Content");
            if (content.Parent is null) trigger.Add(content);
            var target = content.Element("Init") ?? new XElement("Init");
            if (target.Parent is null) content.AddFirst(target);
            target.Add(new XElement("SetVariable", new XAttribute("Name", "$Variable"), new XAttribute("Type", "Bool"), new XAttribute("Value", "0")));
            Render();
        });
        _identity.Children.Add(addVariable);
    }

    private static FrameworkElement TriggerAreaPreview(XElement trigger)
    {
        return new RuntimeTriggerPreview(trigger);
    }

    private sealed class RuntimeTriggerPreview : FrameworkElement
    {
        private readonly XElement _trigger;
        public RuntimeTriggerPreview(XElement trigger) { _trigger = trigger; Height = 240; ClipToBounds = true; Margin = new Thickness(0, 0, 0, 8); }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(StudioUi.Resource("ProjectManagerHoverBrush"), null, new Rect(RenderSize));
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(18, 120, 120, 120)), 1);
            for (var x = 0d; x < ActualWidth; x += 32) dc.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
            for (var y = 0d; y < ActualHeight; y += 32) dc.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
            var trigger = _trigger;
            static double Size(XElement element, string attribute) =>
                double.TryParse((string?)element.Attribute(attribute), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? Math.Max(1, value) : 1;
            var width = Size(trigger, "Width");
            var height = Size(trigger, "Height");
            var scale = Math.Max(0, Math.Min(1, Math.Min((ActualWidth - 48) / width, (ActualHeight - 48) / height)));
            var rect = new Rect((ActualWidth - width * scale) / 2, (ActualHeight - height * scale) / 2, width * scale, height * scale);
            var node = new Vector2LevelEditor.Models.LevelNode { Kind = Vector2LevelEditor.Models.LevelNodeKind.Trigger };
            dc.DrawRectangle(node.FillBrush, node.StrokePen, rect);
            var title = new FormattedText((string?)trigger.Attribute("Name") ?? "Trigger", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, node.StrokePen.Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            title.MaxTextWidth = Math.Max(1, rect.Width - 6);
            dc.DrawText(title, new Point(rect.X + 3, rect.Y + 3));
        }
    }

    private FrameworkElement AttributeField(XElement element, string attribute, string label)
    {
        var panel = new StackPanel { Margin = new Thickness(2) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        var input = StudioUi.Field((string?)element.Attribute(attribute) ?? "");
        input.TextChanged += (_, _) => UpdateAttribute(element, attribute, input.Text);
        panel.Children.Add(input);
        return panel;
    }

    private void UpdateAttribute(XElement element, string name, string value)
    {
        element.SetAttributeValue(name, value);
        foreach (var preview in _identity.Children.OfType<RuntimeTriggerPreview>()) preview.InvalidateVisual();
        if (_document is not null) _raw.Text = _document.ToString();
        if (_document?.Root is { } trigger) RenderChecks(trigger);
    }

    private void RenderChecks(XElement trigger)
    {
        _checks.Children.Clear();
        _checks.Margin = new Thickness(0, 18, 0, 0);
        _checks.Children.Add(new TextBlock { Text = "Checks", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
        var diagnostics = TriggerRuntimeSchema.Diagnose(trigger);
        if (diagnostics.Count == 0)
        {
            _checks.Children.Add(new TextBlock { Text = "Ready for Vector 2", Foreground = new SolidColorBrush(Color.FromRgb(35, 160, 92)), FontSize = 10 });
        }
        foreach (var diagnostic in diagnostics)
        {
            _checks.Children.Add(new TextBlock { Text = diagnostic.Message,
                Foreground = new SolidColorBrush(diagnostic.IsError ? Color.FromRgb(188, 58, 58) : Color.FromRgb(166, 112, 25)),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
        }
    }

    private Button TemplateButton()
    {
        var button = StudioUi.Button("Game Templates", (_, _) => { });
        var menu = new ContextMenu();
        foreach (var group in TriggerTemplateService.Options.GroupBy(option => option.Kind == "Whole" ? "Complete game triggers" : option.Kind == "Loop" ? "Add complete loop" : $"Add to current loop: {option.Kind}"))
        {
            var parent = new MenuItem { Header = group.Key };
            foreach (var option in group)
            {
                var item = new MenuItem { Header = $"{option.Title}  -  {option.Reference}" };
                item.Click += (_, _) => ApplyTemplate(option);
                parent.Items.Add(item);
            }
            menu.Items.Add(parent);
        }
        button.Click += (_, _) => OpenMenu(button, menu);
        button.ToolTip = "Exact reusable blocks from Vector 2 trigger templates";
        return button;
    }

    private Button OutcomeButton()
    {
        var button = StudioUi.Button("Choose an outcome", (_, _) => { });
        var menu = new ContextMenu();
        foreach (var item in new[]
        {
            ("Show dialogue when entered", "Dialogue", "custom_dialogue", "dialogue_id"),
            ("Door: finish run and open a custom zone", "Zone door", "custom_zones", "zone_id"),
            ("Complete a quest objective", "Quest", "custom_quests", "QuestEventName"),
            ("Start a tutorial", "Tutorial", "custom_tutorials", "tutorial_id"),
            ("Start a story", "Story", "custom_story", "story_id"),
            ("Send a reusable project event", "Project event", "", "event_id"),
            ("Play a sound", "Sound", "custom_audio", "sound_id"),
            ("Play music when the room starts", "Music", "custom_audio", "music_id"),
            ("Kill the player", "Kill player", "", "Player")
        })
        {
            var option = new MenuItem { Header = item.Item1 };
            option.Click += (_, _) => ApplyOutcome(item.Item2, FirstProjectId(item.Item3, item.Item4));
            menu.Items.Add(option);
        }
        button.Click += (_, _) => OpenMenu(button, menu);
        return button;
    }

    private static void OpenMenu(Button button, ContextMenu menu)
    {
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void ApplyTemplate(TriggerTemplateOption option)
    {
        if (_document?.Root is not { } trigger) return;
        try
        {
            TriggerTemplateService.Apply(trigger, option, _loopIndex);
            var loopCount = trigger.Element("Content")?.Elements("Loop").Count() ?? 0;
            _loopIndex = Math.Clamp(option.Kind == "Loop" ? loopCount - 1 : _loopIndex, 0, Math.Max(0, loopCount - 1));
            Render();
            _status($"Applied game template {option.Reference}.");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void ApplyOutcome(string outcome, string reference)
    {
        if (_document?.Root is not { } trigger) return;
        try
        {
            TriggerTemplateService.ApplyOutcome(trigger, outcome, reference);
            _loopIndex = 0;
            Render();
            _status($"Applied outcome: {outcome}.");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private string FirstProjectId(string folder, string fallback)
    {
        if (folder.Length == 0) return fallback;
        var path = Path.Combine(_root, folder);
        if (!Directory.Exists(path)) return fallback;
        return Directory.EnumerateFiles(path, "*.xml").Select(Path.GetFileNameWithoutExtension).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? fallback;
    }

    private void AddColumn(XElement loop, string title, string elementName, string[] options)
    {
        var group = loop.Element(elementName);
        if (group is null) { group = new XElement(elementName); loop.Add(group); }
        var section = new StackPanel();
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(81, 57, 168)), VerticalAlignment = VerticalAlignment.Center });
        var target = group;
        var add = StudioUi.Button("+ Add", (_, _) => { });
        var menu = new ContextMenu();
        foreach (var option in options) {
            var item = new MenuItem { Header = option };
            item.Click += (_, _) => { target.Add(TriggerRuntimeSchema.Create(option, elementName)); Render(); };
            menu.Items.Add(item);
        }
        add.Click += (_, _) => { menu.PlacementTarget = add; menu.IsOpen = true; };
        DockPanel.SetDock(add, Dock.Right);
        add.ToolTip = $"Add {title.ToLowerInvariant()} node";
        header.Children.Add(add);
        section.Children.Add(header);
        foreach (var node in group.Elements().ToList())
        {
            var card = new StackPanel();
            var heading = new DockPanel { LastChildFill = false };
            heading.Children.Add(new TextBlock { Text = node.Name.LocalName, FontWeight = FontWeights.SemiBold, FontSize = 11 });
            var remove = StudioUi.Button("\uE74D", (_, _) => { node.Remove(); Render(); });
            remove.FontFamily = new FontFamily("Segoe MDL2 Assets");
            remove.ToolTip = "Remove";
            DockPanel.SetDock(remove, Dock.Right);
            heading.Children.Add(remove);
            foreach (var direction in new[] { -1, 1 }) {
                var move = StudioUi.Button(direction < 0 ? "\uE70E" : "\uE70D", (_, _) => {
                    var siblings = group.Elements().ToList(); var index = siblings.IndexOf(node); var next = index + direction;
                    if (next < 0 || next >= siblings.Count) return;
                    node.Remove(); if (direction < 0) siblings[next].AddBeforeSelf(node); else siblings[next].AddAfterSelf(node); Render();
                });
                move.FontFamily = new FontFamily("Segoe MDL2 Assets"); move.FontSize = 9; move.ToolTip = direction < 0 ? "Move earlier" : "Move later";
                DockPanel.SetDock(move, Dock.Right); heading.Children.Add(move);
            }
            card.Children.Add(heading);
            var catalogue = elementName == "Events" ? TriggerRuntimeSchema.Events
                : elementName == "Conditions" ? TriggerRuntimeSchema.Conditions : TriggerRuntimeSchema.Actions;
            var schema = catalogue.FirstOrDefault(item => item.Name == node.Name.LocalName);
            var keys = node.Attributes().Select(attribute => attribute.Name.LocalName)
                .Concat(schema?.Required ?? [])
                .Concat(schema?.Defaults.Keys ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(key => key).ToList();
            foreach (var key in keys)
            {
                card.Children.Add(StudioUi.Label(key));
                var input = StudioUi.Field((string?)node.Attribute(key) ?? "");
                input.TextChanged += (_, _) =>
                {
                    node.SetAttributeValue(key, input.Text);
                    if (_document is not null) _raw.Text = _document.ToString();
                    if (_document?.Root is { } trigger) RenderChecks(trigger);
                };
                var row = new DockPanel();
                var choices = ProjectChoices(node, key);
                if (choices.Count > 0)
                {
                    var pick = StudioUi.Button("\uE71D", (_, _) => { });
                    pick.FontFamily = new FontFamily("Segoe MDL2 Assets");
                    pick.ToolTip = "Pick from this project";
                    var pickMenu = new ContextMenu();
                    foreach (var choice in choices)
                    {
                        var value = choice;
                        var item = new MenuItem { Header = choice };
                        item.Click += (_, _) => input.Text = value;
                        pickMenu.Items.Add(item);
                    }
                    pick.Click += (_, _) => OpenMenu(pick, pickMenu);
                    DockPanel.SetDock(pick, Dock.Right);
                    row.Children.Add(pick);
                }
                row.Children.Add(input);
                card.Children.Add(row);
            }
            section.Children.Add(new Border { Background = StudioUi.Resource("ProjectManagerCardBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Margin = new Thickness(0,0,0,6), Child = card });
        }
        _behavior.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(45, 110, 85, 220)), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 110, 85, 220)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), Child = section });
    }

    private IReadOnlyList<string> ProjectChoices(XElement node, string attribute)
    {
        if ((node.Name.LocalName == "SetVariable" && attribute == "Name") || attribute == "Value")
        {
            var variables = _document?.Descendants("SetVariable")
                .Select(item => (string?)item.Attribute("Name"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList() ?? [];
            if (variables.Count > 0) return variables;
        }
        string[] folders = (node.Name.LocalName, attribute) switch
        {
            ("Sound", "Name") or ("SoundSource", "Name") or ("Music", "Track") => ["custom_audio"],
            ("TutorialSequence", "Name") => ["custom_tutorials"],
            ("ExecuteCall", "Message") => ["custom_dialogue", "custom_story", "custom_quests", "custom_zones"],
            _ => []
        };
        var result = new List<string>();
        foreach (var folder in folders)
        {
            var path = Path.Combine(_root, folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.xml"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                result.Add(node.Name.LocalName == "ExecuteCall" ? folder switch
                {
                    "custom_dialogue" => "Content.Dialogue:" + id,
                    "custom_story" => "Content.Story:" + id,
                    "custom_zones" => "Content.Zone:" + id,
                    _ => id
                } : id);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
    }

    private static XElement NewNode(string name)
    {
        var node = new XElement(name);
        var attributes = name switch
        {
            "Wait" or "SetTimer" => new[] { ("Frames", "30") },
            "ExecuteCall" => new[] { ("Message", "") },
            "EndGame" => new[] { ("Result", "Win"), ("Model", "Player") },
            "Sound" => new[] { ("Name", ""), ("Action", "Play"), ("Channel", "Sound"), ("Volume", "1") },
            "SetVariable" or "AppendValue" => new[] { ("Name", ""), ("Value", "0") },
            "Equal" or "Greater" or "Less" => new[] { ("Value1", ""), ("Value2", "") },
            "Line" => new[] { ("Position", "0"), ("Type", "0") },
            _ => Array.Empty<(string, string)>()
        };
        foreach (var attribute in attributes) node.SetAttributeValue(attribute.Item1, attribute.Item2);
        return node;
    }

    private void AddLoop()
    {
        if (_document?.Root is not { } trigger) return;
        var content = trigger.Element("Content");
        if (content is null) { content = new XElement("Content"); trigger.Add(content); }
        content.Add(new XElement("Loop", new XElement("Events", new XElement("Enter")), new XElement("Conditions"), new XElement("Actions")));
        _loopIndex = content.Elements("Loop").Count() - 1;
        Render();
    }

    private void ApplyRawXml()
    {
        try
        {
            var parsed = XDocument.Parse(_raw.Text, LoadOptions.PreserveWhitespace);
            if (parsed.Root?.Name.LocalName != "Trigger") throw new InvalidDataException("XML root must be Trigger.");
            TriggerRuntimeSchema.Validate(parsed.Root);
            _document = parsed;
            _loopIndex = 0;
            Render();
            _status("XML applied to the visual trigger.");
        }
        catch (Exception ex) { _status($"XML was not applied: {ex.Message}"); }
    }

    private void OpenCompletionMenu()
    {
        var filter = _completionFilter.Text.Trim();
        var menu = new ContextMenu();
        foreach (var group in new[]
        {
            (Title: "WHEN", Name: "Events", Items: TriggerRuntimeSchema.Events),
            (Title: "IF", Name: "Conditions", Items: TriggerRuntimeSchema.Conditions),
            (Title: "DO", Name: "Actions", Items: TriggerRuntimeSchema.Actions)
        })
        {
            var parent = new MenuItem { Header = group.Title };
            foreach (var schema in group.Items.Where(item => filter.Length == 0 || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(12))
            {
                var item = new MenuItem { Header = schema.Name };
                item.Click += (_, _) => InsertCompletion(schema, group.Name);
                parent.Items.Add(item);
            }
            if (parent.Items.Count > 0) menu.Items.Add(parent);
        }
        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No matching game nodes", IsEnabled = false });
        }
        menu.PlacementTarget = _completionFilter;
        menu.IsOpen = true;
    }

    private void InsertCompletion(TriggerRuntimeItem schema, string groupName)
    {
        try
        {
            var document = XDocument.Parse(_raw.Text, LoadOptions.PreserveWhitespace);
            var loop = document.Descendants("Loop").ElementAtOrDefault(_loopIndex)
                ?? throw new InvalidDataException("Add a Loop before inserting a game node.");
            var group = loop.Element(groupName);
            if (group is null)
            {
                group = new XElement(groupName);
                var actions = loop.Element("Actions");
                if (groupName == "Conditions" && actions is not null) actions.AddBeforeSelf(group);
                else loop.Add(group);
            }
            group.Add(TriggerRuntimeSchema.Create(schema.Name, groupName));
            _raw.Text = document.ToString();
            _status($"Inserted {schema.Name}. Apply XML to rebuild the visual trigger.");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void FormatRawXml()
    {
        try
        {
            _raw.Text = XDocument.Parse(_raw.Text, LoadOptions.PreserveWhitespace).ToString();
            _status("Formatted trigger XML.");
        }
        catch (Exception ex) { _status($"XML could not be formatted: {ex.Message}"); }
    }

    private void CopyRawXml()
    {
        if (_raw.Text.Length == 0) return;
        Clipboard.SetText(_raw.Text);
        _status("Copied trigger XML.");
    }

    private void Save()
    {
        if (_selectedPath is null || _document?.Root is null) return;
        try
        {
            var draft = XDocument.Parse(_raw.Text, LoadOptions.PreserveWhitespace);
            var root = draft.Root;
            if (root?.Name.LocalName != "Trigger") throw new InvalidDataException("XML root must be Trigger.");
            TriggerTemplateService.EnsureRuntimeInit(root);
            TriggerRuntimeSchema.Validate(root);
            draft.Save(_selectedPath);
            _document = draft;
            _status($"Saved {Path.GetFileName(_selectedPath)}.");
            Reload(_selectedPath);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Create()
    {
        try
        {
            var name = _newName.Text.Trim();
            if (name.Length == 0) {
                name = "NewTrigger";
                for (var i = 2; File.Exists(Path.Combine(_folder, name + ".xml")); i++) name = "NewTrigger" + i;
            }
            var path = new ProjectTemplateService().Create(new Vector2Project { RootPath = _root },
                ProjectSection.All.Single(section => section.Name == "Trigger Designer"), name);
            _newName.Clear();
            Reload(path);
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void OpenXml()
    {
        var dialog = new OpenFileDialog { Filter = "Vector XML (*.xml)|*.xml" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var source = XDocument.Load(dialog.FileName);
            var triggers = source.Root?.Name.LocalName == "Trigger" ? [source.Root] : source.Descendants("Trigger").ToList();
            if (triggers.Count == 0) throw new InvalidDataException("No Trigger element was found.");
            Directory.CreateDirectory(_folder);
            string? firstTarget = null;
            foreach (var trigger in triggers)
            {
                var baseId = ProjectTemplateService.SafeId((string?)trigger.Attribute("Name") ?? "ImportedTrigger");
                var id = baseId;
                for (var index = 2; File.Exists(Path.Combine(_folder, id + ".xml")); index++) id = baseId + index;
                var target = Path.Combine(_folder, id + ".xml");
                new XDocument(new XElement(trigger)).Save(target);
                firstTarget ??= target;
            }
            Reload(firstTarget);
            _status(triggers.Count == 1 ? "Imported 1 trigger." : $"Imported all {triggers.Count} triggers from {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private sealed class FileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Path.GetFileNameWithoutExtension(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
