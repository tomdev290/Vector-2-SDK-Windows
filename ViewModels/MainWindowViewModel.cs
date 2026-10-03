using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Views;

namespace Vector2LevelEditor.ViewModels;

/// <summary>
/// The Windows "glue brain" for the editor.
///
/// macOS gets to keep a lot of state directly in SwiftUI. WPF is happier when
/// the window binds to properties and commands, so this class translates button
/// clicks, text fields, hierarchy selection, and canvas gestures into the same
/// model/service calls the Swift editor uses. Keep gameplay/XML rules out of
/// here when possible; parser/exporter/catalog services are the source of truth.
/// </summary>
public sealed partial class MainWindowViewModel : NotifyObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vector2LevelEditor",
        "settings.json");

    private readonly XmlSceneParser _parser = new();
    private readonly GameRoomWeaverService _roomWeaver = new();
    private readonly Exporter _exporter = new();
    private readonly Vector2PlayBridge _playBridge;
    private readonly Vector2Catalog _catalog = new();
    private EditorTool _selectedTool = EditorTool.Cursor;
    private LevelDocument _selectedDocument;
    private TextureAsset? _selectedAsset;
    private string _statusText = "Vector 2 editor ready";
    private double _zoom = 0.55;
    private string _hierarchySearch = "";
    private string _assetSearch = "";
    private string _assetCategory = "All";
    private string _centerTab = "Canvas";
    private string _importPipeline = "Game Room Weaver";
    private string _projectAssetsRoot = "";
    private string _customRoomsDir = "";
    private string _gameExecutablePath = "";
    private bool _isDarkMode;
    private LevelNode? _selectedHierarchyNode;
    private CancellationTokenSource? _assetRefreshCts;
    private int _assetRefreshGeneration;

    public ObservableCollection<LevelDocument> Documents { get; } = [];
    public ObservableCollection<DiagnosticEntry> Diagnostics => DiagnosticsLog.Entries;
    private bool _showConsole = true;
    public bool ShowConsole
    {
        get => _showConsole;
        set { if (Set(ref _showConsole, value)) { OnPropertyChanged(nameof(ConsoleVisibility)); SaveSettings(); } }
    }
    public Visibility ConsoleVisibility => ShowConsole ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<ToolButtonViewModel> Tools { get; } = new(EditorToolInfo.Ordered.Select(t => new ToolButtonViewModel(t)));
    public ObservableCollection<TextureAsset> Assets => _catalog.Assets;
    public IReadOnlyList<string> SortingLayers => Vector2Catalog.SortingLayers;
    public IReadOnlyList<string> Tags => Vector2Catalog.Tags;
    public IReadOnlyList<string> ImportPipelineOptions { get; } = ["Game Room Weaver", "Runtime Graph", "ConvertXmlObject2"];

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand BrowserCommand { get; }
    public RelayCommand RefreshShopOffersCommand { get; }
    public RelayCommand ApplyRawXmlCommand { get; }
    public RelayCommand PromoteSelectionCommand { get; }
    public RelayCommand ParentSelectionToPreviousCommand { get; }
    public RelayCommand BrowseProjectAssetsCommand { get; }
    public RelayCommand BrowseCustomRoomsCommand { get; }
    public RelayCommand BrowseGameExecutableCommand { get; }
    public RelayCommand RefreshAssetsCommand { get; }
    public RelayCommand ClearDiagnosticsCommand { get; }
    public RelayCommand SaveDiagnosticsCommand { get; }
    public RelayCommand HelpCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand<EditorTool> SelectToolCommand { get; }
    public RelayCommand<string> SelectCenterTabCommand { get; }
    public RelayCommand<LevelDocument> SelectDocumentCommand { get; }
    public RelayCommand<LevelDocument> CloseDocumentCommand { get; }
    public RelayCommand<TextureAsset> SelectAssetCommand { get; }
    public RelayCommand<LevelNode> SelectNodeCommand { get; }

    public MainWindowViewModel()
    {
        _playBridge = new Vector2PlayBridge(_exporter);
        _selectedDocument = LevelDocument.Empty();
        Documents.Add(_selectedDocument);
        SyncSelectedDocumentState(_selectedDocument);
        foreach (var tool in Tools)
        {
            tool.IsSelected = tool.Tool == _selectedTool;
        }

        NewCommand = new RelayCommand(NewDocument);
        OpenCommand = new RelayCommand(OpenDocument);
        ImportCommand = new RelayCommand(OpenDocument);
        ExportCommand = new RelayCommand(ExportDocument);
        CopyCommand = new RelayCommand(CopySelection);
        PasteCommand = new RelayCommand(PasteSelection);
        DeleteCommand = new RelayCommand(DeleteSelection);
        BrowserCommand = new RelayCommand(() => CenterTab = CenterTab == "Asset Browser" ? "Canvas" : "Asset Browser");
        RefreshShopOffersCommand = new RelayCommand(() => { }, () => false);
        ApplyRawXmlCommand = new RelayCommand(ApplyRawXml, CanApplyRawXml);
        _selectedDocument.PropertyChanged += RawXmlCommandStateChanged;
        PromoteSelectionCommand = new RelayCommand(PromoteSelectionToSceneRoot);
        ParentSelectionToPreviousCommand = new RelayCommand(ParentSelectionToPreviousNode);
        BrowseProjectAssetsCommand = new RelayCommand(BrowseProjectAssetsRoot);
        BrowseCustomRoomsCommand = new RelayCommand(BrowseCustomRoomsDir);
        BrowseGameExecutableCommand = new RelayCommand(BrowseGameExecutable);
        RefreshAssetsCommand = new RelayCommand(RefreshAssets);
        ClearDiagnosticsCommand = new RelayCommand(DiagnosticsLog.Clear);
        SaveDiagnosticsCommand = new RelayCommand(SaveDiagnostics);
        HelpCommand = new RelayCommand(ShowHelp);
        AboutCommand = new RelayCommand(ShowAbout);
        PlayCommand = new RelayCommand(async () => await PreviewInVector2Async());
        InitializeMacFeatureCommands();
        InitializeProjectManagerCommands();
        InitializeAIStudioCommands();
        InitializePlayerStudioCommands();
        InitializeStructuralStudioCommands();
        InitializeRoomLayoutCommands();
        SelectToolCommand = new RelayCommand<EditorTool>(tool => SelectedTool = tool);
        SelectCenterTabCommand = new RelayCommand<string>(tab => CenterTab = string.IsNullOrWhiteSpace(tab) ? "Canvas" : tab!);
        SelectDocumentCommand = new RelayCommand<LevelDocument>(SelectDocument);
        CloseDocumentCommand = new RelayCommand<LevelDocument>(CloseDocument);
        SelectAssetCommand = new RelayCommand<TextureAsset>(asset =>
        {
            SelectedAsset = asset;
            if (asset is not null)
            {
                SelectedTool = asset.Kind == TextureAssetKind.Texture ? EditorTool.Images : EditorTool.ObjectReference;
                CenterTab = "Canvas";
                StatusText = $"Selected {asset.Name}. Click canvas to place.";
            }
        });
        SelectNodeCommand = new RelayCommand<LevelNode>(SelectNode);
        LoadSettings();
        // Project Manager is a workspace the user opens explicitly. A restored
        // project must never replace the level canvas during application startup.
        IsProjectManagerVisible = false;
        IsProjectManagerOpen = false;
    }

    public async void RefreshAssets()
    {
        _assetRefreshCts?.Cancel();
        _assetRefreshCts?.Dispose();
        var generation = ++_assetRefreshGeneration;
        var cts = new CancellationTokenSource();
        _assetRefreshCts = cts;

        Vector2Catalog.ProjectAssetsOverride = ProjectAssetsRoot;
        StatusText = "Refreshing assets...";

        try
        {
            await Task.Delay(120, cts.Token);
            var snapshot = await Task.Run(_catalog.RefreshSnapshot, cts.Token);
            if (cts.IsCancellationRequested || generation != _assetRefreshGeneration) return;

            _catalog.ApplySnapshot(snapshot);
            OnPropertyChanged(nameof(FilteredAssets));
            OnPropertyChanged(nameof(AssetCategories));
            StatusText = $"Assets refreshed: {Assets.Count} cards";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Asset refresh failed: {ex.Message}");
            StatusText = "Asset refresh failed. Check Console tab.";
        }
    }

    public EditorTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (!Set(ref _selectedTool, value)) return;
            if (value == EditorTool.Cursor || value.CanPlaceDirectly())
            {
                SelectedAsset = null;
            }
            if (value is EditorTool.Images or EditorTool.Backgrounds or EditorTool.ObjectReference or EditorTool.Object)
            {
                CenterTab = "Asset Browser";
                StatusText = "Pick an asset, then click the canvas to place it.";
            }
            foreach (var tool in Tools)
            {
                tool.IsSelected = tool.Tool == value;
            }
        }
    }
    public LevelDocument SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (ReferenceEquals(_selectedDocument, value)) return;
            _selectedDocument.PropertyChanged -= RawXmlCommandStateChanged;
            if (!Set(ref _selectedDocument, value)) return;
            _selectedDocument.PropertyChanged += RawXmlCommandStateChanged;
            ApplyRawXmlCommand.RaiseCanExecuteChanged();
            IsAIStudioVisible = false;
            IsPlayerStudioVisible = false;
            _isRoomLayoutsOpen = false;
            SyncSelectedDocumentState(value);
            OnPropertyChanged(nameof(VisibleHierarchyNodes));
            OnPropertyChanged(nameof(HierarchyRootNodes));
            OnPropertyChanged(nameof(StructuralRoomBarVisibility));
            OnPropertyChanged(nameof(RoomLayoutsVisibility));
        }
    }
    public TextureAsset? SelectedAsset { get => _selectedAsset; set => Set(ref _selectedAsset, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public double Zoom { get => _zoom; set => Set(ref _zoom, Math.Clamp(value, 0.15, 3.0)); }
    public bool XmlPredictionEnabled
    {
        get => XmlAuthoringPreferences.PredictionEnabled;
        set { XmlAuthoringPreferences.PredictionEnabled = value; OnPropertyChanged(); SaveSettings(); }
    }
    public string HierarchySearch
    {
        get => _hierarchySearch;
        set
        {
            if (Set(ref _hierarchySearch, value))
            {
                OnPropertyChanged(nameof(VisibleHierarchyNodes));
                OnPropertyChanged(nameof(HierarchyRootNodes));
                AutoSelectFirstHierarchyMatch();
            }
        }
    }
    public string AssetSearch
    {
        get => _assetSearch;
        set
        {
            if (Set(ref _assetSearch, value))
            {
                OnPropertyChanged(nameof(FilteredAssets));
            }
        }
    }
    public string AssetCategory
    {
        get => _assetCategory;
        set
        {
            if (Set(ref _assetCategory, string.IsNullOrWhiteSpace(value) ? "All" : value))
            {
                OnPropertyChanged(nameof(FilteredAssets));
            }
        }
    }
    public string CenterTab
    {
        get => _centerTab;
        set
        {
            if (!Set(ref _centerTab, value)) return;
            OnPropertyChanged(nameof(CanvasVisibility));
            OnPropertyChanged(nameof(AssetBrowserVisibility));
            OnPropertyChanged(nameof(SettingsVisibility));
            OnPropertyChanged(nameof(DynamicStudioVisibility));
            OnPropertyChanged(nameof(BackgroundDesignerVisibility));
            OnPropertyChanged(nameof(TrickPreviewVisibility));
            OnPropertyChanged(nameof(StructuralRoomBarVisibility));
            OnPropertyChanged(nameof(RoomLayoutsVisibility));
        }
    }
    public string ImportPipeline
    {
        get => _importPipeline;
        set { if (Set(ref _importPipeline, value)) SaveSettings(); }
    }
    public string ProjectAssetsRoot
    {
        get => _projectAssetsRoot;
        set
        {
            if (!Set(ref _projectAssetsRoot, value)) return;
            Vector2Catalog.ProjectAssetsOverride = value;
            SaveSettings();
        }
    }
    public string CustomRoomsDir
    {
        get => _customRoomsDir;
        set
        {
            if (!Set(ref _customRoomsDir, value)) return;
            _playBridge.CustomRoomsOverride = value;
            var content = CustomContentService.EnsureFolders(value);
            BackgroundsDirectory = content.Backgrounds;
            CustomTexturesDirectory = content.Textures;
            SaveSettings();
        }
    }
    public string GameExecutablePath
    {
        get => _gameExecutablePath;
        set
        {
            if (!Set(ref _gameExecutablePath, value)) return;
            _playBridge.GameExecutableOverride = value;
            SaveSettings();
        }
    }
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (!Set(ref _isDarkMode, value)) return;
            EditorTheme.Apply(value);
            SaveSettings();
        }
    }
    public LevelNode? SelectedHierarchyNode
    {
        get => _selectedHierarchyNode;
        set
        {
            if (!Set(ref _selectedHierarchyNode, value)) return;
            if (value is not null && SelectedDocument.SelectedNode?.Id != value.Id)
            {
                SelectNode(value);
            }
        }
    }
    public Visibility CanvasVisibility => CenterTab is "Canvas" or "Dynamic Studio" or "Background Designer" or "Trick Preview" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AssetBrowserVisibility => CenterTab == "Asset Browser" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => CenterTab == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DynamicStudioVisibility => CenterTab == "Dynamic Studio" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BackgroundDesignerVisibility => CenterTab == "Background Designer" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TrickPreviewVisibility => CenterTab == "Trick Preview" ? Visibility.Visible : Visibility.Collapsed;

    public IEnumerable<TextureAsset> FilteredAssets =>
        Assets.Where(a => (AssetCategory == "All" || a.Group == AssetCategory || a.Category == AssetCategory) &&
                          (string.IsNullOrWhiteSpace(AssetSearch) ||
                           a.Name.Contains(AssetSearch, StringComparison.OrdinalIgnoreCase) ||
                           a.Group.Contains(AssetSearch, StringComparison.OrdinalIgnoreCase) ||
                           a.Category.Contains(AssetSearch, StringComparison.OrdinalIgnoreCase) ||
                           a.ClassName.Contains(AssetSearch, StringComparison.OrdinalIgnoreCase)));

    public IEnumerable<string> AssetCategories =>
        new[] { "All" }
            .Concat(Assets.Select(a => a.Group).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Order())
            .Concat(Assets.Select(a => a.Category).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Order());

    public IEnumerable<LevelNode> VisibleHierarchyNodes =>
        string.IsNullOrWhiteSpace(HierarchySearch)
            ? SelectedDocument.SceneNodes
            : SelectedDocument.SceneNodes.Where(n => n.Name.Contains(HierarchySearch, StringComparison.OrdinalIgnoreCase) || n.Kind.ToString().Contains(HierarchySearch, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<LevelNode> HierarchyRootNodes =>
        string.IsNullOrWhiteSpace(HierarchySearch)
            ? SelectedDocument.Nodes
            : VisibleHierarchyNodes;

    private void AutoSelectFirstHierarchyMatch()
    {
        if (string.IsNullOrWhiteSpace(HierarchySearch)) return;
        var match = VisibleHierarchyNodes.FirstOrDefault();
        if (match is not null && SelectedDocument.SelectedNode?.Id != match.Id)
        {
            SelectNode(match);
        }
    }

    private void SelectDocument(LevelDocument? document)
    {
        if (document is null) return;
        IsProjectManagerVisible = false;
        IsAIStudioVisible = false;
        IsPlayerStudioVisible = false;
        if (document.Id == SelectedDocument.Id) return;
        SelectedDocument = document;
        CenterTab = "Canvas";
        StatusText = $"Switched to {document.Name}";
    }

    private void CloseDocument(LevelDocument? document)
    {
        if (document is null || Documents.Count <= 1) return;
        if (AIStudioTarget?.Id == document.Id) CloseAIStudioCommand.Execute(null);
        if (PlayerStudioTarget?.Id == document.Id) ClosePlayerStudioCommand.Execute(null);
        var index = Documents.IndexOf(document);
        Documents.Remove(document);
        if (SelectedDocument.Id == document.Id)
        {
            SelectedDocument = Documents[Math.Clamp(index, 0, Documents.Count - 1)];
        }
        StatusText = $"Closed {document.Name}";
    }

    private void SyncSelectedDocumentState(LevelDocument document)
    {
        var node = document.SelectedNode ?? document.SceneNodes.FirstOrDefault();
        document.SelectedNode = node;
        document.SelectedNodeIds.Clear();
        if (node is not null) document.SelectedNodeIds.Add(node.Id);
        SyncHierarchySelectionFlags(document);
        document.RawXml = _exporter.ExportSelection(node);
        _selectedHierarchyNode = node;
        OnPropertyChanged(nameof(SelectedHierarchyNode));
    }

    public void PlaceAt(Point world, bool trapezoidType2 = false)
    {
        if (!SelectedTool.CanPlaceDirectly() && SelectedAsset is null) return;

        // Library/object tools are not real shapes by themselves. On macOS they
        // only become meaningful after the asset browser has picked a prefab or
        // library XML entry. Without this guard Windows would drop fake black-box
        // placeholders, which looks like an editor bug and exports junk XML.
        var node = SelectedAsset is not null && SelectedTool is EditorTool.Images or EditorTool.Backgrounds or EditorTool.ObjectReference or EditorTool.Object
            ? NodeFromAsset(SelectedAsset, world)
            : NodeFromTool(SelectedTool, world, trapezoidType2);

        if (node is null) return;
        AppendSceneNode(SelectedDocument, node);
        SelectNode(node);
        StatusText = $"Placed {node.Name}";
        DiagnosticsLog.Info($"Placed {node.Kind} '{node.Name}' at {node.X},{node.Y}.");
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
    }

    public void SelectNode(LevelNode? node)
    {
        SelectedDocument.SelectedNode = node;
        _selectedHierarchyNode = node;
        OnPropertyChanged(nameof(SelectedHierarchyNode));
        SelectedDocument.SelectedNodeIds.Clear();
        if (node is not null) SelectedDocument.SelectedNodeIds.Add(node.Id);
        SyncHierarchySelectionFlags(SelectedDocument);
        SelectedDocument.RawXml = _exporter.ExportSelection(node);
        OnPropertyChanged(nameof(SelectedDocument));
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
        NotifyBackgroundSelectionChanged();
        if (CenterTab == "Dynamic Studio" && node is not null &&
            node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor or LevelNodeKind.Trigger) &&
            DynamicTarget?.Id != node.Id)
        {
            StopDynamicPreview(true);
            DynamicTarget = node;
            LoadDynamicTimeline(node);
            BeginDynamicPreviewSession();
        }
    }

    public void ToggleNodeSelection(LevelNode node)
    {
        if (SelectedDocument.SelectedNodeIds.Contains(node.Id))
        {
            SelectedDocument.SelectedNodeIds.Remove(node.Id);
            if (SelectedDocument.SelectedNode?.Id == node.Id)
            {
                SelectedDocument.SelectedNode = SelectedSceneSelection().LastOrDefault();
            }
        }
        else
        {
            SelectedDocument.SelectedNodeIds.Add(node.Id);
            SelectedDocument.SelectedNode = node;
        }

        SyncHierarchySelectionFlags(SelectedDocument);

        _selectedHierarchyNode = SelectedDocument.SelectedNode;
        SelectedDocument.RawXml = _exporter.ExportSelection(SelectedDocument.SelectedNode);
        OnPropertyChanged(nameof(SelectedHierarchyNode));
        OnPropertyChanged(nameof(SelectedDocument));
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
    }

    public void SelectNodesInRect(Rect worldRect, bool additive)
    {
        var matches = SelectedDocument.SceneNodes
            .Where(node => !node.IsHidden &&
                           node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor) &&
                           worldRect.IntersectsWith(node.VisualBounds))
            .ToList();

        if (!additive)
        {
            SelectedDocument.SelectedNodeIds.Clear();
        }

        foreach (var node in matches)
        {
            SelectedDocument.SelectedNodeIds.Add(node.Id);
        }

        SyncHierarchySelectionFlags(SelectedDocument);

        var selected = matches.LastOrDefault() ?? SelectedDocument.SelectedNode;
        if (selected is not null && SelectedDocument.SelectedNodeIds.Contains(selected.Id))
        {
            SelectedDocument.SelectedNode = selected;
        }
        else
        {
            SelectedDocument.SelectedNode = SelectedSceneSelection().LastOrDefault();
        }

        _selectedHierarchyNode = SelectedDocument.SelectedNode;
        SelectedDocument.RawXml = _exporter.ExportSelection(SelectedDocument.SelectedNode);
        OnPropertyChanged(nameof(SelectedHierarchyNode));
        OnPropertyChanged(nameof(SelectedDocument));
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));

        StatusText = SelectedDocument.SelectedNodeIds.Count switch
        {
            0 => "No selection",
            1 => $"Selected {SelectedDocument.SelectedNode?.Name}",
            _ => $"Selected {SelectedDocument.SelectedNodeIds.Count} nodes"
        };
    }

    public void MoveSelected(double deltaX, double deltaY)
    {
        var selected = SelectedSceneSelection().Where(node => !HasSelectedAncestor(node)).ToList();
        if (selected.Count == 0 && SelectedDocument.SelectedNode is { } fallback)
        {
            selected.Add(fallback);
        }
        if (selected.Count == 0) return;
        var dx = (int)Math.Round(deltaX);
        var dy = (int)Math.Round(deltaY);
        foreach (var node in selected)
        {
            MoveTree(node, dx, dy);
        }
        SelectedDocument.RawXml = _exporter.ExportSelection(SelectedDocument.SelectedNode);
    }

    public void ResizeSelected(double left, double top, double right, double bottom)
    {
        var node = SelectedDocument.SelectedNode;
        if (node is null || node.Children.Count > 0) return;

        // No group resizing. We already learned that lesson the hard way 💀.
        // Moving groups is safe, rotating groups is manageable, but resizing a
        // parent that owns resolved prefab pieces mangles the Vector 2 contract.
        var oldRight = node.X + node.Width;
        var oldBottom = node.Y + node.Height;
        var newLeft = node.X + (int)Math.Round(left);
        var newTop = node.Y + (int)Math.Round(top);
        var newRight = oldRight + (int)Math.Round(right);
        var newBottom = oldBottom + (int)Math.Round(bottom);

        const int minSize = 8;
        if (newRight - newLeft < minSize)
        {
            if (Math.Abs(left) > 0.001 && Math.Abs(right) < 0.001)
            {
                newLeft = newRight - minSize;
            }
            else
            {
                newRight = newLeft + minSize;
            }
        }
        if (newBottom - newTop < minSize)
        {
            if (Math.Abs(top) > 0.001 && Math.Abs(bottom) < 0.001)
            {
                newTop = newBottom - minSize;
            }
            else
            {
                newBottom = newTop + minSize;
            }
        }

        node.X = newLeft;
        node.Y = newTop;
        node.Width = newRight - newLeft;
        node.Height = newBottom - newTop;
        SelectedDocument.RawXml = _exporter.ExportSelection(node);
    }

    public void RotateSelectedTo(double angle)
    {
        var node = SelectedDocument.SelectedNode;
        if (node is null) return;

        // Rotate around the visible union, not the XML owner's tiny anchor rect.
        // Library objects are often "parent XML node + child preview sprites";
        // using raw X/Y here is why old builds spun black silhouettes from a
        // corner instead of from the object center.
        var delta = angle - node.Rotation;
        var visualBounds = node.VisualBounds;
        var center = new Point(visualBounds.Left + visualBounds.Width / 2.0, visualBounds.Top + visualBounds.Height / 2.0);
        node.Rotation = angle;
        RotateDescendants(node, center, delta);
        SelectedDocument.RawXml = _exporter.ExportSelection(node);
    }

    private LevelNode? NodeFromAsset(TextureAsset asset, Point world)
    {
        if (asset.Kind == TextureAssetKind.Texture)
        {
            var size = TexturePlacementSize(asset.FilePath);
            return new LevelNode
            {
                Name = asset.Name,
                Kind = LevelNodeKind.Image,
                X = (int)world.X,
                Y = (int)world.Y,
                Width = size.Width,
                Height = size.Height,
                ClassName = asset.ClassName,
                ImagePath = asset.FilePath,
                SortingLayer = asset.DefaultLayer,
                Factor = "0",
                Tag = "Image",
                Template = "Image",
                Choice = asset.Category,
                Variant = "Default"
            };
        }

        var node = new LevelNode
        {
            Name = asset.Name,
            Kind = LevelNodeKind.ObjectReference,
            X = (int)world.X,
            Y = (int)world.Y,
            Width = 180,
            Height = 120,
            Filename = Path.GetFileName(string.IsNullOrWhiteSpace(asset.SourcePath) ? asset.FilePath : asset.SourcePath),
            ClassName = asset.ClassName,
            ImagePath = asset.FilePath,
            Template = "LibraryObject",
            Choice = Path.GetFileName(string.IsNullOrWhiteSpace(asset.SourcePath) ? asset.FilePath : asset.SourcePath),
            Variant = "Resolved",
            Tag = "ObjectReference"
        };
        if (asset.Kind == TextureAssetKind.Prefab)
        {
            UnityPrefabObjectBuilder.TryReconstructPrefab(node, asset.SourcePath);
            AlignVisibleBoundsToClick(node, world);
        }
        else
        {
            Vector2LibraryObjectBuilder.TryReconstructAsset(node, asset);
            AlignVisibleBoundsToClick(node, world);
        }
        return node;
    }

    private static LevelNode? NodeFromTool(EditorTool tool, Point world, bool trapezoidType2 = false) => tool switch
    {
        EditorTool.Collision => RectNode("Platform", LevelNodeKind.Platform, "Platform", world, 300, 120, className: "Platform", choice: "Collision"),
        EditorTool.Trapezoid => TrapezoidPlacementNode(world, trapezoidType2),
        EditorTool.Trigger => RectNode("Trigger", LevelNodeKind.Trigger, "Trigger", world, 180, 100, className: "Trigger"),
        EditorTool.Area => RectNode("Area", LevelNodeKind.Area, "Area", world, 240, 120, className: "Area"),
        EditorTool.Comment => RectNode("Comment", LevelNodeKind.Comment, "Comment", world, 180, 90, editorOnly: true, className: "Comment"),
        EditorTool.PlayerIn => RectNode("In", LevelNodeKind.GateIn, "In", world, 72, 72, className: "In", template: "SpawnIn", choice: "Player", variant: "Default"),
        EditorTool.PlayerOut => RectNode("Out", LevelNodeKind.GateOut, "Out", world, 72, 72, className: "Out", template: "SpawnOut", choice: "Player", variant: "Default"),
        EditorTool.RunFast => RectNode("RunFast", LevelNodeKind.Area, "Area", world, 700, 90, className: "RunFast", template: "RunFast", choice: "Areas", variant: "Animation"),
        EditorTool.Swarm => RectNode("Swarm", LevelNodeKind.Waypoint, "Waypoint", world, 83, 141, className: "Swarm", template: "Waypoint", choice: "Swarm", variant: "Default"),
        EditorTool.Waypoint => RectNode("Waypoint", LevelNodeKind.Waypoint, "Waypoint", world, 83, 141, className: "Waypoint", template: "Waypoint", choice: "Default", variant: "Default"),
        EditorTool.Coin => RectNode("Bonus", LevelNodeKind.Coin, "Bonus", world, 72, 72, className: "Bonus", template: "Bonus", choice: "Bonus", variant: "Default"),
        EditorTool.Camera => RectNode("Camera", LevelNodeKind.Camera, "Camera", world, 200, 200, className: "Camera"),
        EditorTool.ObjectReference => null,
        EditorTool.Object => null,
        EditorTool.Dynamic => RectNode("Dynamic", LevelNodeKind.Dynamic, "Dynamic", world, 160, 120, className: "Dynamic"),
        _ => null
    };

    private static LevelNode RectNode(
        string name,
        LevelNodeKind kind,
        string tag,
        Point world,
        int width,
        int height,
        bool editorOnly = false,
        string variant = "",
        string className = "",
        string template = "",
        string choice = "")
        => new()
        {
            Name = name,
            Kind = kind,
            Tag = tag,
            X = (int)world.X,
            Y = (int)world.Y,
            Width = width,
            Height = height,
            SortingLayer = kind == LevelNodeKind.Platform || kind == LevelNodeKind.Trapezoid ? "Collision" : "Default",
            Template = string.IsNullOrWhiteSpace(template) ? kind.ToString() : template,
            Choice = choice,
            Variant = variant,
            ClassName = className,
            IsEditorOnly = editorOnly
        };

    private static LevelNode TrapezoidPlacementNode(Point world, bool type2)
    {
        return new LevelNode
        {
            Name = type2 ? "TrapezoidType2" : "Trapezoid",
            Kind = LevelNodeKind.Trapezoid,
            Tag = "Trapezoid",
            X = (int)world.X,
            Y = (int)world.Y,
            Width = 180,
            Height = 120,
            Factor = "1",
            ClassName = "Trapezoid",
            SortingLayer = "Collision",
            Template = "Trapezoid",
            Choice = "Collision",
            Variant = type2 ? "SlopeType2" : "Slope"
        };
    }

    private void NewDocument()
    {
        IsProjectManagerVisible = false;
        var document = LevelDocument.Empty();
        document.Name = $"untitled_{Documents.Count + 1}.xml";
        Documents.Add(document);
        SelectedDocument = document;
        StatusText = $"Created {document.Name}";
    }

    private void OpenDocument()
    {
        var dialog = new OpenFileDialog { Filter = "Vector 2 XML (*.xml)|*.xml|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IReadOnlyDictionary<string, string>? variants = null;
            if (ImportPipeline == "Game Room Weaver")
            {
                variants = Vector2LevelEditor.Views.RoomWeaverVariantDialog.Choose(_roomWeaver.ChoiceOptions(dialog.FileName));
                if (variants is null) { StatusText = "Room import cancelled."; return; }
            }
            var document = ImportPipeline == "Game Room Weaver"
                ? _roomWeaver.Load(dialog.FileName, variants)
                : _parser.Load(dialog.FileName);
            Documents.Add(document);
            SelectedDocument = document;
            IsProjectManagerVisible = false;
            StatusText = $"Imported {document.Name}";
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Import failed: {ex.Message}");
            StatusText = "Import failed. Check Console tab.";
        }
    }

    private void ExportDocument()
    {
        var dialog = new SaveFileDialog { Filter = "Vector 2 XML (*.xml)|*.xml", FileName = SelectedDocument.Name,
            InitialDirectory = SelectedDocument.SourcePath is { Length: > 0 } source ? Path.GetDirectoryName(source) : null };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _exporter.Export(SelectedDocument));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Export failed: {ex.Message}");
            StatusText = "Export failed. Check Console tab.";
            return;
        }
        SelectedDocument.SourcePath = dialog.FileName;
        SelectedDocument.Name = Path.GetFileName(dialog.FileName);
        StatusText = $"Exported {SelectedDocument.Name}";
        RoomSaved?.Invoke(dialog.FileName);
    }

    private void SaveDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text log (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"vector2-editor-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllLines(dialog.FileName, Diagnostics.Select(entry => entry.Display));
            StatusText = $"Saved console log to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Saving console log failed: {ex.Message}");
            StatusText = "Saving console log failed. Check Console tab.";
        }
    }

    private void CopySelection()
    {
        if (SelectedDocument.SelectedNode is null) return;
        Clipboard.SetText(_exporter.ExportSelection(SelectedDocument.SelectedNode));
        StatusText = "Copied selected XML";
    }

    private void PasteSelection()
    {
        if (!Clipboard.ContainsText()) return;
        try
        {
            var node = _parser.ParseFragment(Clipboard.GetText());
            node.X += 24;
            node.Y += 24;
            AppendSceneNode(SelectedDocument, node);
            SelectNode(node);
            StatusText = $"Pasted {node.Name}";
            OnPropertyChanged(nameof(VisibleHierarchyNodes));
            OnPropertyChanged(nameof(HierarchyRootNodes));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Paste failed: {ex.Message}");
            StatusText = "Paste failed. Check Console tab.";
        }
    }

    private bool CanApplyRawXml()
    {
        if (SelectedDocument.SelectedNode is null) return false;
        try { return System.Xml.Linq.XDocument.Parse(SelectedDocument.RawXml).Root is not null; }
        catch (System.Xml.XmlException) { return false; }
    }

    private void RawXmlCommandStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(LevelDocument.RawXml) or nameof(LevelDocument.SelectedNode))
            ApplyRawXmlCommand.RaiseCanExecuteChanged();
    }

    private void ApplyRawXml()
    {
        try
        {
            var selected = SelectedDocument.SelectedNode
                ?? throw new InvalidOperationException("Select an object before applying XML.");
            var xml = System.Xml.Linq.XDocument.Parse(SelectedDocument.RawXml);
            var element = xml.Root
                ?? throw new InvalidOperationException("The draft contains no XML element.");
            var original = System.Xml.Linq.XElement.Parse(_exporter.ExportSelection(selected));
            if (element.Name != original.Name)
                throw new InvalidOperationException($"Keep the selected object's {original.Name} root; import rooms through File instead.");
            foreach (var attribute in element.DescendantsAndSelf().SelectMany(node => node.Attributes())
                .Where(attribute => attribute.Name.LocalName is "X" or "Y" or "Width" or "Height" or "Rotation"))
            {
                if (!double.TryParse(attribute.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                    throw new InvalidOperationException($"{attribute.Name} must contain a finite number, not '{attribute.Value}'.");
            }
            if (element.Name.LocalName == "Trigger") Services.ProjectManager.TriggerRuntimeSchema.Validate(element);
            // Parse and validate before touching the live tree. This pane edits one object, not the room.
            var replacement = _parser.ParseFragment(element.ToString());
            replacement.Id = selected.Id;
            replacement.IsHidden = selected.IsHidden;
            var siblings = FindParent(SelectedDocument.Nodes, selected.Id)?.Children ?? SelectedDocument.Nodes;
            var index = siblings.IndexOf(selected);
            if (index < 0) throw new InvalidOperationException("The selected object is no longer in this room.");
            siblings[index] = replacement;
            SelectNode(replacement);
            StatusText = "Applied XML to selected object";
            OnPropertyChanged(nameof(VisibleHierarchyNodes));
            OnPropertyChanged(nameof(HierarchyRootNodes));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Raw XML apply failed: {ex.Message}");
            StatusText = "Raw XML apply failed. Check Console tab.";
        }
    }

    private void DeleteSelection()
    {
        var selected = SelectedSceneSelection().Where(node => !HasSelectedAncestor(node)).ToList();
        if (selected.Count == 0 && SelectedDocument.SelectedNode is { } fallback)
        {
            selected.Add(fallback);
        }
        if (selected.Count == 0) return;

        var removed = 0;
        foreach (var node in selected)
        {
            if (RemoveNode(SelectedDocument.Nodes, node.Id)) removed++;
        }

        SelectNode(null);
        StatusText = removed == 1 ? "Deleted selected node" : $"Deleted {removed} selected nodes";
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
    }

    private List<LevelNode> SelectedSceneSelection()
    {
        var ids = SelectedDocument.SelectedNodeIds;
        return SelectedDocument.SceneNodes.Where(node => ids.Contains(node.Id)).ToList();
    }

    private bool HasSelectedAncestor(LevelNode node)
        => SelectedDocument.SceneNodes.Any(candidate =>
            candidate.Id != node.Id &&
            SelectedDocument.SelectedNodeIds.Contains(candidate.Id) &&
            candidate.Flatten().Any(descendant => descendant.Id == node.Id));

    private static bool RemoveNode(IList<LevelNode> nodes, Guid id)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Id == id)
            {
                nodes.RemoveAt(i);
                return true;
            }
            if (RemoveNode(nodes[i].Children, id)) return true;
        }
        return false;
    }

    private static LevelNode? FindParent(IEnumerable<LevelNode> nodes, Guid childId)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Any(child => child.Id == childId)) return node;
            var nested = FindParent(node.Children, childId);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void AppendSceneNode(LevelDocument document, LevelNode node)
    {
        var factor = document.Nodes
            .Where(n => n.Kind == LevelNodeKind.Track)
            .SelectMany(n => n.Children)
            .FirstOrDefault(n => n.Kind == LevelNodeKind.Factor);
        if (factor is not null)
        {
            factor.Children.Add(node);
            return;
        }

        document.Nodes.Add(node);
    }

    public void SelectHierarchyRange(LevelNode? anchor, LevelNode target)
    {
        var subtree = target.HierarchyChildren.SelectMany(node => node.Flatten()).Prepend(target).ToList();
        if (subtree.Count > 1)
        {
            SetHierarchySelection(subtree, target);
            return;
        }

        var visible = HierarchyRootNodes.SelectMany(FlattenHierarchy).ToList();
        var anchorIndex = anchor is null ? -1 : visible.FindIndex(node => node.Id == anchor.Id);
        var targetIndex = visible.FindIndex(node => node.Id == target.Id);
        if (anchorIndex < 0 || targetIndex < 0)
        {
            SelectNode(target);
            return;
        }

        var start = Math.Min(anchorIndex, targetIndex);
        var count = Math.Abs(anchorIndex - targetIndex) + 1;
        SetHierarchySelection(visible.GetRange(start, count), target);
    }

    private static IEnumerable<LevelNode> FlattenHierarchy(LevelNode node)
    {
        yield return node;
        foreach (var child in node.HierarchyChildren)
        {
            foreach (var descendant in FlattenHierarchy(child)) yield return descendant;
        }
    }

    private void SetHierarchySelection(IEnumerable<LevelNode> nodes, LevelNode primary)
    {
        SelectedDocument.SelectedNodeIds.Clear();
        foreach (var node in nodes) SelectedDocument.SelectedNodeIds.Add(node.Id);
        SelectedDocument.SelectedNode = primary;
        _selectedHierarchyNode = primary;
        SyncHierarchySelectionFlags(SelectedDocument);
        SelectedDocument.RawXml = _exporter.ExportSelection(primary);
        OnPropertyChanged(nameof(SelectedHierarchyNode));
        OnPropertyChanged(nameof(SelectedDocument));
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
    }

    public void SelectNodes(IEnumerable<LevelNode> nodes)
    {
        var selection = nodes.ToList();
        if (selection.Count > 0) SetHierarchySelection(selection, selection[0]);
    }

    private static void SyncHierarchySelectionFlags(LevelDocument document)
    {
        foreach (var node in document.Nodes.SelectMany(node => node.Flatten()))
        {
            node.IsHierarchySelected = document.SelectedNodeIds.Contains(node.Id);
        }
    }

    public void ReparentNode(LevelNode source, LevelNode? target) => ReparentSelection(source, target);

    /// <summary>
    /// Canvas clicks on a grouped child operate on its immediate group owner,
    /// matching the macOS editor. The hierarchy can still select the child
    /// directly for individual property edits.
    /// </summary>
    public LevelNode ResolveCanvasDragTarget(LevelNode node)
    {
        var parent = FindParent(SelectedDocument.Nodes, node.Id);
        if (parent is null || parent.Kind is LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor)
        {
            return node;
        }

        var isGroupedChild = node.IsHierarchyAttachment || (!node.IsPreviewOnly && !node.IsEditorOnly);
        return isGroupedChild ? parent : node;
    }

    public void ReparentSelection(LevelNode source, LevelNode? target)
    {
        var allNodes = SelectedDocument.Nodes.SelectMany(node => node.Flatten()).ToList();
        var selectedIds = SelectedDocument.SelectedNodeIds.Contains(source.Id)
            ? SelectedDocument.SelectedNodeIds.ToHashSet()
            : new HashSet<Guid> { source.Id };
        var moving = allNodes
            .Where(node => selectedIds.Contains(node.Id))
            .Where(node => node.Kind is not (LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor))
            .Where(node => !allNodes.Any(parent => parent.Id != node.Id && selectedIds.Contains(parent.Id) && parent.Flatten().Any(child => child.Id == node.Id)))
            .ToList();
        if (moving.Count == 0) return;
        if (target is not null && moving.Any(node => node.Id == target.Id || node.Flatten().Any(child => child.Id == target.Id))) return;

        foreach (var node in moving)
        {
            if (!RemoveNode(SelectedDocument.Nodes, node.Id)) return;
        }

        foreach (var node in moving)
        {
            if (target is null)
            {
                node.MarkHierarchyAttachment(false);
                AppendSceneNode(SelectedDocument, node);
            }
            else if (target.Kind == LevelNodeKind.Track)
            {
                node.MarkHierarchyAttachment(false);
                var factor = target.Children.FirstOrDefault(n => n.Kind == LevelNodeKind.Factor);
                if (factor is null)
                {
                    factor = new LevelNode { Name = "Object Factor = 1", Kind = LevelNodeKind.Factor, Factor = "1", Template = "Object" };
                    target.Children.Add(factor);
                }
                factor.Children.Add(node);
            }
            else
            {
                // A runtime-graph helper can be selected from the hierarchy.
                // Once the user explicitly parents it, it is real room content,
                // not disposable preview artwork.
                node.IsPreviewOnly = false;
                node.MarkHierarchyAttachment(true);
                target.Children.Add(node);
            }
        }

        SetHierarchySelection(moving, moving[0]);
        var subject = moving.Count == 1 ? moving[0].Name : $"{moving.Count} objects";
        StatusText = target is null ? $"Moved {subject} to scene root" : $"Parented {subject} under {target.Name}";
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
    }

    private void PromoteSelectionToSceneRoot()
    {
        var selected = SelectedDocument.SelectedNode;
        if (selected is null) return;
        ReparentSelection(selected, null);
    }

    private void ParentSelectionToPreviousNode()
    {
        var selected = SelectedDocument.SelectedNode;
        if (selected is null) return;
        var flat = SelectedDocument.SceneNodes.ToList();
        var allNodes = SelectedDocument.Nodes.SelectMany(node => node.Flatten()).ToList();
        var selectedIndex = flat.FindIndex(n => n.Id == selected.Id);
        if (selectedIndex <= 0) return;
        var selectedIds = SelectedDocument.SelectedNodeIds.Contains(selected.Id)
            ? SelectedDocument.SelectedNodeIds
            : new HashSet<Guid> { selected.Id };
        var parent = flat.Take(selectedIndex).LastOrDefault(n =>
            !selectedIds.Contains(n.Id) &&
            !selectedIds.Any(id => allDescendants(id).Contains(n.Id)));
        if (parent is null) return;
        ReparentSelection(selected, parent);

        HashSet<Guid> allDescendants(Guid id)
        {
            var node = allNodes.FirstOrDefault(candidate => candidate.Id == id);
            return node?.Flatten().Select(descendant => descendant.Id).ToHashSet() ?? [];
        }
    }

    private void BrowseProjectAssetsRoot()
    {
        var dialog = new OpenFolderDialog { Title = "Choose ProjectAssets/Vector2 folder" };
        if (dialog.ShowDialog() != true) return;
        ProjectAssetsRoot = dialog.FolderName;
        RefreshAssets();
    }

    private void BrowseCustomRoomsDir()
    {
        var dialog = new OpenFolderDialog { Title = "Choose Vector 2 custom_rooms folder" };
        if (dialog.ShowDialog() != true) return;
        CustomRoomsDir = dialog.FolderName;
        StatusText = $"Using custom_rooms at {CustomRoomsDir}";
    }

    private void BrowseGameExecutable()
    {
        var dialog = new OpenFileDialog { Title = "Choose Vector 2 executable", Filter = "Windows executable (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        GameExecutablePath = dialog.FileName;
        StatusText = $"Using Vector 2 executable at {GameExecutablePath}";
    }

    private async Task PreviewInVector2Async()
    {
        try
        {
            var exported = await _playBridge.PreviewAsync(SelectedDocument);
            StatusText = $"Exported {exported} and sent editorplay to Vector 2";
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Play failed: {ex.Message}");
            StatusText = "Play failed. Check Console tab.";
        }
    }

    private static void MoveTree(LevelNode node, int deltaX, int deltaY)
    {
        node.X += deltaX;
        node.Y += deltaY;
        foreach (var child in node.Children)
        {
            MoveTree(child, deltaX, deltaY);
        }
    }

    private static void AlignVisibleBoundsToClick(LevelNode node, Point world)
    {
        var bounds = Vector2LibraryObjectBuilder.OwnerPreviewBounds(node);
        var deltaX = (int)Math.Round(world.X - bounds.Left);
        var deltaY = (int)Math.Round(world.Y - bounds.Top);
        if (deltaX == 0 && deltaY == 0) return;
        MoveTree(node, deltaX, deltaY);
    }

    private static void RotateDescendants(LevelNode node, Point center, double degrees)
    {
        if (Math.Abs(degrees) < 0.001) return;
        foreach (var child in node.Children)
        {
            var childCenter = new Point(child.X + child.Width / 2.0, child.Y + child.Height / 2.0);
            var rotated = RotatePoint(childCenter, center, degrees);
            child.X = (int)Math.Round(rotated.X - child.Width / 2.0);
            child.Y = (int)Math.Round(rotated.Y - child.Height / 2.0);
            child.Rotation += degrees;
            RotateDescendants(child, center, degrees);
        }
    }

    private static (int Width, int Height) TexturePlacementSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (220, 120);
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault();
            if (frame is null || frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return (220, 120);
            const double maxSide = 520;
            var scale = Math.Min(1.0, maxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
            return (Math.Max(24, (int)Math.Round(frame.PixelWidth * scale)), Math.Max(24, (int)Math.Round(frame.PixelHeight * scale)));
        }
        catch
        {
            return (220, 120);
        }
    }

    private static Point RotatePoint(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Point(center.X + (dx * cos - dy * sin), center.Y + (dx * sin + dy * cos));
    }

    private void LoadSettings()
    {
        try
        {
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(SettingsPath))
                : null;

            _gameExecutablePath = Vector2PlayBridge.ResolveGameExecutable(settings?.GameExecutablePath);
            var integration = Vector2GameIntegrationLocator.Detect();
            var dataFolder = string.IsNullOrWhiteSpace(_gameExecutablePath) ? "" : Path.Combine(
                Path.GetDirectoryName(_gameExecutablePath)!, Path.GetFileNameWithoutExtension(_gameExecutablePath) + "_Data");
            var requestedRooms = Path.Combine(Vector2GameIntegrationLocator.ModDataRootFor(dataFolder), "custom_rooms");
            var content = CustomContentService.EnsureFolders(requestedRooms);
            _customRoomsDir = content.Rooms;
            _backgroundsDirectory = integration is not null || string.IsNullOrWhiteSpace(settings?.BackgroundsDirectory)
                ? content.Backgrounds : settings.BackgroundsDirectory;
            _customTexturesDirectory = integration is not null || string.IsNullOrWhiteSpace(settings?.CustomTexturesDirectory)
                ? content.Textures : settings.CustomTexturesDirectory;
            Directory.CreateDirectory(_backgroundsDirectory);
            Directory.CreateDirectory(_customTexturesDirectory);
            _projectAssetsRoot = settings?.ProjectAssetsRoot ?? "";
            _isDarkMode = settings?.IsDarkMode ?? false;
            _showConsole = settings?.ShowConsole ?? true;
            _importPipeline = string.IsNullOrWhiteSpace(settings?.ImportPipeline) ? "Game Room Weaver" : settings.ImportPipeline;
            XmlAuthoringPreferences.PredictionEnabled = settings?.XmlPredictionEnabled ?? true;
            _gameTrickShowSkeletonPoints = settings?.ShowSkeletonPoints ?? false;
            _gameTrickShowDetectorDots = settings?.ShowDetectorDots ?? false;
            _gameTrickShowModelNodeSpheres = settings?.ShowModelNodeSpheres ?? false;
            _gameTrickShowModelSphereOutlines = settings?.ShowModelOutlines ?? false;
            EditorTheme.Apply(_isDarkMode);
            Vector2Catalog.ProjectAssetsOverride = _projectAssetsRoot;
            Vector2Catalog.CustomTexturesOverride = _customTexturesDirectory;
            _playBridge.CustomRoomsOverride = _customRoomsDir;
            _playBridge.GameExecutableOverride = _gameExecutablePath;
            OnPropertyChanged(nameof(ProjectAssetsRoot));
            OnPropertyChanged(nameof(CustomRoomsDir));
            OnPropertyChanged(nameof(GameExecutablePath));
            OnPropertyChanged(nameof(BackgroundsDirectory));
            OnPropertyChanged(nameof(CustomTexturesDirectory));
            OnPropertyChanged(nameof(IsDarkMode));
            OnPropertyChanged(nameof(ImportPipeline));
            SaveSettings();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not load editor settings: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var settings = new EditorSettings(ProjectAssetsRoot, CustomRoomsDir, GameExecutablePath, IsDarkMode,
                BackgroundsDirectory, CustomTexturesDirectory, ImportPipeline, XmlPredictionEnabled,
                GameTrickShowSkeletonPoints, GameTrickShowDetectorDots, GameTrickShowModelNodeSpheres, GameTrickShowModelSphereOutlines, ShowConsole);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Could not save editor settings: {ex.Message}");
        }
    }

    private sealed record EditorSettings(
        string ProjectAssetsRoot,
        string CustomRoomsDir,
        string GameExecutablePath,
        bool IsDarkMode = false,
        string BackgroundsDirectory = "",
        string CustomTexturesDirectory = "",
        string ImportPipeline = "Game Room Weaver",
        bool XmlPredictionEnabled = true, bool ShowSkeletonPoints = false, bool ShowDetectorDots = false,
        bool ShowModelNodeSpheres = false, bool ShowModelOutlines = false, bool ShowConsole = true);

    private static void ShowAbout()
    {
        new AboutWindow { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    private static void ShowHelp()
    {
        new HelpWindow { Owner = Application.Current.MainWindow }.ShowDialog();
    }

}
