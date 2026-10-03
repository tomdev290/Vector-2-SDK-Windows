using System.Collections.ObjectModel;

namespace Vector2LevelEditor.Models;

/// <summary>
/// One open editor tab. The root keeps Vector 2's scene hierarchy, while
/// SelectedNodeIds lets the canvas and hierarchy share selection state.
/// </summary>
public sealed class LevelDocument : NotifyObject
{
    private string _name = "untitled_1.xml";
    private string? _sourcePath;
    private LevelNode? _selectedNode;
    private string _rawXml = "";
    private string _customBackgroundName = "";
    private bool _hasCustomBackgroundAssignment;
    private StructuralRoomKind? _structuralRoomKind;

    public Guid Id { get; } = Guid.NewGuid();
    public ObservableCollection<LevelNode> Nodes { get; } = [];
    public ObservableCollection<AICharacterDefinition> AICharacters { get; } = [];
    public ObservableCollection<AIGroupDefinition> AIGroups { get; } = [];
    public ObservableCollection<string> PlayerSkinFiles { get; } = [];
    public HashSet<Guid> SelectedNodeIds { get; } = [];

    public string Name { get => _name; set => Set(ref _name, value); }
    public string? SourcePath { get => _sourcePath; set => Set(ref _sourcePath, value); }
    public LevelNode? SelectedNode { get => _selectedNode; set => Set(ref _selectedNode, value); }
    public string RawXml { get => _rawXml; set => Set(ref _rawXml, value); }
    public string CustomBackgroundName { get => _customBackgroundName; set => Set(ref _customBackgroundName, value); }
    public bool HasCustomBackgroundAssignment { get => _hasCustomBackgroundAssignment; set => Set(ref _hasCustomBackgroundAssignment, value); }
    public StructuralRoomKind? StructuralRoomKind { get => _structuralRoomKind; set => Set(ref _structuralRoomKind, value); }

    public IEnumerable<LevelNode> SceneNodes => Nodes.SelectMany(n => n.FlattenEditableScene());

    public static LevelDocument Empty()
    {
        var document = new LevelDocument();
        var factor = new LevelNode { Name = "Object Factor = 1", Kind = LevelNodeKind.Factor, Factor = "1", Template = "Object" };
        factor.Children.Add(new LevelNode { Name = "In", Kind = LevelNodeKind.GateIn, Tag = "In", X = 0, Y = 0, Width = 72, Height = 72 });
        factor.Children.Add(new LevelNode { Name = "Out", Kind = LevelNodeKind.GateOut, Tag = "Out", X = 1200, Y = 0, Width = 72, Height = 72 });
        factor.Children.Add(new LevelNode { Name = "Platform", Kind = LevelNodeKind.Platform, Tag = "Platform", SortingLayer = "Collision", X = 0, Y = 120, Width = 1400, Height = 260 });
        var track = new LevelNode { Name = "Track", Kind = LevelNodeKind.Track, Factor = "1", Template = "Track" };
        track.Children.Add(factor);
        document.Nodes.Add(track);
        document.SelectedNode = document.SceneNodes.FirstOrDefault();
        return document;
    }
}
