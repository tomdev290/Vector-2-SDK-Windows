using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Views;

public sealed class TriggerActionInspector : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(LevelDocument), typeof(TriggerActionInspector), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(nameof(SelectedNode), typeof(LevelNode), typeof(TriggerActionInspector), new PropertyMetadata(null, Changed));
    private readonly StackPanel _panel = new() { Margin = new Thickness(0, 0, 0, 14) };
    private bool _updating;

    public LevelDocument? Document { get => (LevelDocument?)GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public LevelNode? SelectedNode { get => (LevelNode?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }

    public TriggerActionInspector() { Content = _panel; Render(); }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var inspector = (TriggerActionInspector)sender;
        if (e.Property == DocumentProperty)
        {
            if (e.OldValue is LevelDocument previous)
            {
                previous.AICharacters.CollectionChanged -= inspector.OnTargetsChanged;
                previous.AIGroups.CollectionChanged -= inspector.OnTargetsChanged;
            }
            if (e.NewValue is LevelDocument current)
            {
                current.AICharacters.CollectionChanged += inspector.OnTargetsChanged;
                current.AIGroups.CollectionChanged += inspector.OnTargetsChanged;
            }
        }
        inspector.Render();
    }

    private void OnTargetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Render();

    private void Render()
    {
        _panel.Children.Clear();
        if (SelectedNode?.Kind != LevelNodeKind.Trigger || Document is null) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        _updating = true;
        _panel.Children.Add(new TextBlock { Text = "TRIGGER ACTION", FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(0, 0, 0, 9) });
        var targets = new List<Choice> { new("No action", ""), new("Player", "player"), new("Project event", "project"), new("All AI", "all") };
        targets.AddRange(Document.AICharacters.Select(character => new Choice(character.Name, "character:" + character.Id)));
        targets.AddRange(Document.AIGroups.Select(group => new Choice("Group: " + group.Name, "group:" + group.Id)));
        AddLabel("Target");
        var target = new ComboBox { ItemsSource = targets, DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Value),
            SelectedValue = SelectedNode.TriggerTarget, Margin = new Thickness(0, 0, 0, 10) };
        _panel.Children.Add(target);
        AddLabel("Template");
        var action = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        _panel.Children.Add(action);
        AddLabel("Value");
        var value = new TextBox { Text = SelectedNode.TriggerValue, Margin = new Thickness(0, 0, 0, 5) };
        _panel.Children.Add(value);
        var browse = new Button { Content = "Browse...", HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        _panel.Children.Add(browse);
        browse.Click += (_, _) =>
        {
            var picker = new AnimationSelectionWindow(SelectedNode.TriggerAction == "Trick", value.Text, Document.SourcePath) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true) value.Text = picker.SelectedAnimation;
        };
        var valueChoice = new ComboBox { DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Value),
            Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
        _panel.Children.Add(valueChoice);
        var hint = new TextBlock { FontSize = 10, Foreground = (Brush)FindResource("MutedTextBrush"), TextWrapping = TextWrapping.Wrap };
        _panel.Children.Add(hint);
        void RefreshActions()
        {
            var wasUpdating = _updating;
            _updating = true;
            var selected = action.SelectedItem as string ?? SelectedNode.TriggerAction;
            action.ItemsSource = TriggerActionService.ActionsFor(SelectedNode.TriggerTarget);
            action.SelectedItem = TriggerActionService.ActionsFor(SelectedNode.TriggerTarget).Contains(selected) ? selected : null;
            SelectedNode.TriggerAction = action.SelectedItem as string ?? "";
            _updating = wasUpdating;
            RefreshValueState();
        }
        void RefreshValueState()
        {
            var previousUpdating = _updating;
            _updating = true;
            var needsValue = SelectedNode.TriggerAction is "Trick" or "Animation" or "Spawn" or "Respawn" or "Activate Spawn" or "Send Event" or "WallJump";
            value.IsEnabled = needsValue;
            var usesPicker = SelectedNode.TriggerAction is "Activate Spawn" or "WallJump";
            value.Visibility = usesPicker ? Visibility.Collapsed : Visibility.Visible;
            valueChoice.Visibility = usesPicker ? Visibility.Visible : Visibility.Collapsed;
            browse.Visibility = SelectedNode.TriggerAction is "Trick" or "Animation" ? Visibility.Visible : Visibility.Collapsed;
            if (usesPicker)
            {
                valueChoice.ItemsSource = SelectedNode.TriggerAction == "WallJump"
                    ? new[] { new Choice("Left", "Left"), new Choice("Right", "Right") }
                    : Document.SceneNodes.Where(node => node.Kind == LevelNodeKind.Trigger && node.TriggerAction is "Spawn" or "Respawn")
                        .Select(node => new Choice(string.IsNullOrWhiteSpace(node.Name) ? node.TriggerAction : node.Name, node.Id.ToString("D"))).ToArray();
                valueChoice.SelectedValue = Guid.TryParse(SelectedNode.TriggerValue, out var spawnId)
                    ? spawnId.ToString("D") : SelectedNode.TriggerValue;
            }
            hint.Text = SelectedNode.TriggerAction switch
            {
                "Trick" or "Animation" => "Use the animation name from the game or your trick library.",
                "Send Event" => "Event name used by project content, dialogue, or tutorials.",
                "Activate Spawn" => "Choose a Spawn or Respawn trigger in this room.",
                "WallJump" => "",
                "Spawn" or "Respawn" => "Optional spawn name; leave blank for an automatic name.",
                _ => ""
            };
            _updating = previousUpdating;
        }
        RefreshActions();
        _updating = false;
        void UpdateContent()
        {
            if (_updating) return;
            try { SelectedNode.TriggerContentXml = SelectedNode.TriggerTarget.Length > 0 && SelectedNode.TriggerAction.Length > 0
                ? TriggerActionService.BuildContent(Document, SelectedNode).ToString(System.Xml.Linq.SaveOptions.DisableFormatting) : ""; }
            catch (InvalidDataException) { SelectedNode.TriggerContentXml = ""; }
        }
        target.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            SelectedNode.TriggerTarget = target.SelectedValue as string ?? "";
            SelectedNode.TriggerAction = SelectedNode.TriggerTarget == "project" ? "Send Event" : "";
            action.SelectedItem = null;
            SelectedNode.TriggerValue = "";
            value.Text = "";
            _updating = false;
            RefreshActions(); UpdateContent();
        };
        action.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            SelectedNode.TriggerAction = action.SelectedItem as string ?? "";
            RefreshValueState(); UpdateContent();
        };
        value.TextChanged += (_, _) => { if (_updating) return; SelectedNode.TriggerValue = value.Text; UpdateContent(); };
        valueChoice.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            SelectedNode.TriggerValue = valueChoice.SelectedValue as string ?? "";
            value.Text = SelectedNode.TriggerValue;
            UpdateContent();
        };
    }

    private void AddLabel(string label) => _panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 3) });
    private sealed record Choice(string Label, string Value)
    {
        public override string ToString() => Label;
    }
}
