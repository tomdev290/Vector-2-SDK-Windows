using System.ComponentModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class AIStudioView : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(LevelDocument), typeof(AIStudioView),
        new PropertyMetadata(null, (owner, _) => ((AIStudioView)owner).RefreshDocument()));

    private readonly GameTrickPreviewService _previewService = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IReadOnlyList<GameTrickModelSkin> _skins = [];
    private AICharacterDefinition? _selectedCharacter;
    private string _catalogError = "";
    private LevelDocument? _boundDocument;

    public LevelDocument? Document
    {
        get => (LevelDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public AIStudioView()
    {
        InitializeComponent();
        RootLayout.DataContext = this;
        KindPicker.ItemsSource = new[] { "Friendly", "Enemy" };
        _previewTimer.Tick += (_, _) =>
        {
            if (Preview.Playback is { Frames.Count: > 0 } playback)
                Preview.Frame = (Preview.Frame + 1) % playback.Frames.Count;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                LoadCatalog();
                RefreshPreview();
                _previewTimer.Start();
            }
            else _previewTimer.Stop();
        };
        Unloaded += (_, _) => _previewTimer.Stop();
    }

    private void RefreshDocument()
    {
        if (Characters is null) return;
        if (_boundDocument is not null) _boundDocument.AICharacters.CollectionChanged -= OnCharactersChanged;
        _boundDocument = Document;
        if (_boundDocument is not null) _boundDocument.AICharacters.CollectionChanged += OnCharactersChanged;
        if (_selectedCharacter is not null) _selectedCharacter.PropertyChanged -= OnCharacterChanged;
        _selectedCharacter = null;
        Characters.ItemsSource = Document?.AICharacters;
        Groups.ItemsSource = Document?.AIGroups;
        Characters.SelectedItem = Document?.AICharacters.FirstOrDefault();
        Groups.SelectedItem = Document?.AIGroups.FirstOrDefault();
        RefreshPreview();
    }

    private void OnCharactersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Characters.SelectedItem is null)
            Characters.SelectedItem = Document?.AICharacters.FirstOrDefault();
        RefreshGroupMembers();
    }

    private void LoadCatalog()
    {
        try
        {
            _skins = _previewService.LoadModelSkins();
            _catalogError = "";
        }
        catch (Exception ex)
        {
            _skins = [];
            _catalogError = ex.Message;
        }
        var selectable = _skins.Where(skin => skin.Filename != "0.xml").ToList();
        var none = new GameTrickModelSkin("", "None");
        BodyPicker.ItemsSource = selectable.Where(skin => !IsAccessory(skin.Filename)).ToList();
        ChestPicker.ItemsSource = new[] { none }.Concat(selectable.Where(skin =>
            ContainsAny(skin.Filename, "armor", "shirt", "jacket", "shorts", "scarf"))).ToList();
        HelmetPicker.ItemsSource = new[] { none }.Concat(selectable.Where(skin =>
            ContainsAny(skin.Filename, "helmet", "cap"))).ToList();
        HairPicker.ItemsSource = new[] { none }.Concat(selectable.Where(skin =>
            ContainsAny(skin.Filename, "hair"))).ToList();
        RefreshCustomLayerChoices();
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsAccessory(string name) =>
        ContainsAny(name, "hair", "helmet", "cap", "armor", "gear", "shirt", "jacket", "shorts", "scarf");

    private void Characters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedCharacter is not null) _selectedCharacter.PropertyChanged -= OnCharacterChanged;
        _selectedCharacter = Characters.SelectedItem as AICharacterDefinition;
        if (_selectedCharacter is not null) _selectedCharacter.PropertyChanged += OnCharacterChanged;
        Configuration.DataContext = _selectedCharacter;
        ConfigurationScroll.Visibility = _selectedCharacter is null ? Visibility.Collapsed : Visibility.Visible;
        NoCharacterMessage.Visibility = _selectedCharacter is null ? Visibility.Visible : Visibility.Collapsed;
        CustomLayers.ItemsSource = _selectedCharacter?.CustomLayers;
        RefreshCustomLayerChoices();
        RefreshGroupMembers();
        RefreshPreview();
    }

    private void OnCharacterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AICharacterDefinition.Name) or nameof(AICharacterDefinition.BodySkin)
            or nameof(AICharacterDefinition.ChestSkin) or nameof(AICharacterDefinition.HelmetSkin)
            or nameof(AICharacterDefinition.HairSkin) or nameof(AICharacterDefinition.Kind))
            RefreshPreview();
    }

    private void RefreshCustomLayerChoices()
    {
        AvailableCustomLayers.Children.Clear();
        if (_selectedCharacter is null) return;
        foreach (var model in CustomModelCatalogService.Load(CustomModelCatalogService.DefaultDirectory()))
        {
            var reference = "custom:" + model.Id;
            var toggle = new CheckBox
            {
                Content = model.Name + " · " + model.Category,
                IsChecked = _selectedCharacter.CustomLayers.Contains(reference),
                Margin = new Thickness(0, 0, 0, 7), ToolTip = reference
            };
            var character = _selectedCharacter;
            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true)
                {
                    if (!character.CustomLayers.Contains(reference)) character.CustomLayers.Add(reference);
                }
                else character.CustomLayers.Remove(reference);
                RefreshPreview();
            };
            AvailableCustomLayers.Children.Add(toggle);
        }
    }

    private void RefreshPreview()
    {
        PreviewTitle.Text = _selectedCharacter?.Name ?? "Create an AI";
        if (_selectedCharacter is null)
        {
            Preview.Playback = null;
            PreviewMessage.Text = "Add an AI to preview the model";
            return;
        }
        try
        {
            var moves = _previewService.LoadMoves();
            var move = moves.FirstOrDefault(item => item.FileName.Contains("cs_swarm_idle", StringComparison.OrdinalIgnoreCase))
                ?? moves.FirstOrDefault(item => item.Name.Contains("stand", StringComparison.OrdinalIgnoreCase))
                ?? moves.FirstOrDefault()
                ?? throw new InvalidDataException("No preview animation found.");
            Preview.Playback = _previewService.LoadPlayback(move,
                new[] { "0.xml" }.Concat(_selectedCharacter.SkinFiles).ToList());
            Preview.Frame = Preview.Playback.StartFrame;
            PreviewMessage.Text = "";
        }
        catch (Exception ex)
        {
            Preview.Playback = null;
            PreviewMessage.Text = _catalogError.Length > 0 ? _catalogError : ex.Message;
        }
    }

    private void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var channel = Document.AICharacters.Count == 0 ? 1 : Document.AICharacters.Max(character => character.AIChannel) + 1;
        var number = Document.AICharacters.Count + 1;
        var character = new AICharacterDefinition
        {
            Name = $"Friendly{number}", AIChannel = channel,
            BodySkin = _skins.Any(skin => skin.Filename == "helper.xml") ? "helper.xml" : "1.xml",
            HairSkin = _skins.Any(skin => skin.Filename == "hair.xml") ? "hair.xml" : ""
        };
        Document.AICharacters.Add(character);
        Characters.SelectedItem = character;
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || _selectedCharacter is null) return;
        var id = _selectedCharacter.Id;
        Document.AICharacters.Remove(_selectedCharacter);
        foreach (var group in Document.AIGroups) group.CharacterIds.Remove(id);
        Characters.SelectedItem = Document.AICharacters.FirstOrDefault();
        RefreshGroupMembers();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var group = new AIGroupDefinition { Name = $"Group {Document.AIGroups.Count + 1}" };
        Document.AIGroups.Add(group);
        Groups.SelectedItem = group;
    }

    private void Groups_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshGroupMembers();

    private void RefreshGroupMembers()
    {
        if (GroupMembers is null) return;
        GroupMembers.Children.Clear();
        if (Groups.SelectedItem is not AIGroupDefinition group || Document is null) return;
        GroupMembers.Children.Add(new TextBox
        {
            Text = group.Name, Margin = new Thickness(0, 5, 0, 5),
            ToolTip = "Group name"
        });
        if (GroupMembers.Children[0] is TextBox name)
            name.TextChanged += (_, _) => group.Name = name.Text;
        foreach (var character in Document.AICharacters)
        {
            var member = new CheckBox
            {
                Content = character.Name, Tag = character, IsChecked = group.CharacterIds.Contains(character.Id),
                Margin = new Thickness(0, 2, 0, 2)
            };
            member.Click += (_, _) =>
            {
                if (member.IsChecked == true)
                {
                    if (!group.CharacterIds.Contains(character.Id)) group.CharacterIds.Add(character.Id);
                }
                else group.CharacterIds.Remove(character.Id);
            };
            GroupMembers.Children.Add(member);
        }
        var remove = new Button { Content = "Delete group", Margin = new Thickness(0, 6, 0, 0) };
        remove.Click += (_, _) =>
        {
            Document.AIGroups.Remove(group);
            Groups.SelectedItem = Document.AIGroups.FirstOrDefault();
        };
        GroupMembers.Children.Add(remove);
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var layer = NewLayerName.Text.Trim();
        if (_selectedCharacter is null || layer.Length == 0 || _selectedCharacter.CustomLayers.Contains(layer)) return;
        _selectedCharacter.CustomLayers.Add(layer);
        NewLayerName.Clear();
        RefreshCustomLayerChoices();
        RefreshPreview();
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null || CustomLayers.SelectedItem is not string layer) return;
        _selectedCharacter.CustomLayers.Remove(layer);
        RefreshCustomLayerChoices();
        RefreshPreview();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Preview is not null) Preview.PreviewZoom = e.NewValue;
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = 3.5;

    private void SaveRoom_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var issues = AIValidationService.Validate(Document);
        if (issues.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, issues), "AI Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "Vector 2 XML (*.xml)|*.xml",
            FileName = Document.Name,
            InitialDirectory = Document.SourcePath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, new Exporter().Export(Document));
            Document.SourcePath = dialog.FileName;
            Document.Name = Path.GetFileName(dialog.FileName);
            if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
                editor.StatusText = $"Exported {Document.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save room", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
