using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Vector2LevelEditor.Diagnostics;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly DispatcherTimer _dynamicPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _trickPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 120.0) };
    private readonly Stopwatch _trickPreviewClock = new();
    private readonly GameTrickPreviewService _gameTrickPreview = new();
    private LevelNode? _dynamicTarget;
    private DynamicStudioKeyframe? _selectedDynamicKeyframe;
    private LevelNode? _selectedDynamicTrigger;
    private string _dynamicTransformName = "NewTransform";
    private int _dynamicTotalFrames = 300;
    private int _dynamicCurrentFrame;
    private bool _dynamicScrubPreview = true;
    private bool _dynamicIsPreviewing;
    private DynamicPathMode _dynamicMovementMode = DynamicPathMode.Bezier;
    private DynamicSinQuarter _dynamicMovementQuarter = DynamicSinQuarter.EaseInOut;
    private DynamicPathMode _dynamicSizeMode = DynamicPathMode.Bezier;
    private DynamicSinQuarter _dynamicSizeQuarter = DynamicSinQuarter.EaseInOut;
    private DynamicRotationMode _dynamicRotationMode = DynamicRotationMode.Linear;
    private Dictionary<Guid, HierarchyTransformService.NodeTransform>? _dynamicOriginalHierarchy;
    private string _backgroundsDirectory = "";
    private string _customTexturesDirectory = "";
    private string _backgroundName = "";
    private GameTrickMove? _selectedGameTrickMove;
    private GameTrickPlayback? _gameTrickPlayback;
    private int _gameTrickTick;
    private int _gameTrickTickBase;
    private bool _gameTrickPlaying;
    private bool _gameTrickShowSkeletonPoints = true;
    private bool _gameTrickShowDetectorDots = true;
    private bool _gameTrickShowModelNodeSpheres = true;
    private bool _gameTrickShowModelSphereOutlines = true;
    private string _gameTrickSearch = "";
    private string _selectedTrickBody = "1.xml";
    private string _selectedTrickChest = "black_armor.xml";
    private string _selectedTrickHelmet = "black_helmet.xml";
    private string _selectedTrickHair = "hair.xml";
    private int _gameTrickStartEarlierFrames = 5;

    public ObservableCollection<DynamicStudioKeyframe> DynamicKeyframes { get; } = [];
    public ObservableCollection<GameTrickMove> GameTrickMoves { get; } = [];
    public ObservableCollection<GameTrickModelSkin> GameTrickSkins { get; } = [];
    public IReadOnlyList<DynamicPathMode> DynamicPathModes { get; } = Enum.GetValues<DynamicPathMode>();
    public IReadOnlyList<DynamicSinQuarter> DynamicSinQuarters { get; } = Enum.GetValues<DynamicSinQuarter>();
    public IReadOnlyList<DynamicRotationMode> DynamicRotationModes { get; } = Enum.GetValues<DynamicRotationMode>();
    public IReadOnlyList<string> BackgroundLayers => BackgroundDesignerService.Layers;

    public RelayCommand OpenDynamicStudioCommand { get; private set; } = null!;
    public RelayCommand CloseDynamicStudioCommand { get; private set; } = null!;
    public RelayCommand AddDynamicKeyframeCommand { get; private set; } = null!;
    public RelayCommand DeleteDynamicKeyframeCommand { get; private set; } = null!;
    public RelayCommand SaveDynamicCommand { get; private set; } = null!;
    public RelayCommand ToggleDynamicPreviewCommand { get; private set; } = null!;
    public RelayCommand CreateDynamicTriggerCommand { get; private set; } = null!;
    public RelayCommand OpenBackgroundDesignerCommand { get; private set; } = null!;
    public RelayCommand CloseBackgroundDesignerCommand { get; private set; } = null!;
    public RelayCommand BrowseBackgroundsDirectoryCommand { get; private set; } = null!;
    public RelayCommand BrowseCustomTexturesDirectoryCommand { get; private set; } = null!;
    public RelayCommand ImportBackgroundTexturesCommand { get; private set; } = null!;
    public RelayCommand SaveBackgroundCommand { get; private set; } = null!;
    public RelayCommand AssignBackgroundCommand { get; private set; } = null!;
    public RelayCommand ClearBackgroundAssignmentCommand { get; private set; } = null!;
    public RelayCommand ResetBackgroundImageCommand { get; private set; } = null!;
    public RelayCommand OpenTrickPreviewCommand { get; private set; } = null!;
    public RelayCommand CloseTrickPreviewCommand { get; private set; } = null!;
    public RelayCommand RefreshTrickPreviewCommand { get; private set; } = null!;
    public RelayCommand ToggleTrickPreviewCommand { get; private set; } = null!;
    public RelayCommand PreviewSelectedGameTrickCommand { get; private set; } = null!;
    public RelayCommand DismissGameTrickOverlayCommand { get; private set; } = null!;

    public LevelNode? DynamicTarget { get => _dynamicTarget; private set { if (Set(ref _dynamicTarget, value)) OnPropertyChanged(nameof(DynamicTargetLabel)); } }
    public string DynamicTargetLabel => DynamicTarget is null ? "Select an object on the canvas" : $"Editing {DynamicTarget.Name}";
    public string DynamicTransformName { get => _dynamicTransformName; set { if (Set(ref _dynamicTransformName, value)) RefreshDynamicXml(); } }
    public int DynamicTotalFrames { get => _dynamicTotalFrames; set => Set(ref _dynamicTotalFrames, Math.Max(1, value)); }
    public int DynamicCurrentFrame
    {
        get => _dynamicCurrentFrame;
        set
        {
            if (!Set(ref _dynamicCurrentFrame, Math.Clamp(value, 0, DynamicTotalFrames))) return;
            if (DynamicScrubPreview && !_dynamicIsPreviewing) ApplyDynamicPose(value);
        }
    }
    public bool DynamicScrubPreview
    {
        get => _dynamicScrubPreview;
        set
        {
            if (!Set(ref _dynamicScrubPreview, value)) return;
            if (value) BeginDynamicPreviewSession(); else RestoreDynamicPose();
        }
    }
    public bool DynamicIsPreviewing { get => _dynamicIsPreviewing; private set { if (Set(ref _dynamicIsPreviewing, value)) OnPropertyChanged(nameof(DynamicPreviewButtonText)); } }
    public string DynamicPreviewButtonText => DynamicIsPreviewing ? "Stop" : "Preview";
    public DynamicPathMode DynamicMovementMode { get => _dynamicMovementMode; set { if (Set(ref _dynamicMovementMode, value)) RefreshDynamicXml(); } }
    public DynamicSinQuarter DynamicMovementQuarter { get => _dynamicMovementQuarter; set { if (Set(ref _dynamicMovementQuarter, value)) RefreshDynamicXml(); } }
    public DynamicPathMode DynamicSizeMode { get => _dynamicSizeMode; set { if (Set(ref _dynamicSizeMode, value)) RefreshDynamicXml(); } }
    public DynamicSinQuarter DynamicSizeQuarter { get => _dynamicSizeQuarter; set { if (Set(ref _dynamicSizeQuarter, value)) RefreshDynamicXml(); } }
    public DynamicRotationMode DynamicRotationMode { get => _dynamicRotationMode; set { if (Set(ref _dynamicRotationMode, value)) RefreshDynamicXml(); } }
    public DynamicStudioKeyframe? SelectedDynamicKeyframe { get => _selectedDynamicKeyframe; set => Set(ref _selectedDynamicKeyframe, value); }
    public LevelNode? SelectedDynamicTrigger { get => _selectedDynamicTrigger; set => Set(ref _selectedDynamicTrigger, value); }
    public IEnumerable<LevelNode> DynamicTriggerNodes => SelectedDocument.SceneNodes.Where(node => node.Kind == LevelNodeKind.Trigger);
    public string DynamicGeneratedXml => DynamicStudioService.GenerateDynamicXml(DynamicTransformName, DynamicKeyframes,
        DynamicMovementMode, DynamicMovementQuarter, DynamicSizeMode, DynamicSizeQuarter, DynamicRotationMode);

    public string BackgroundsDirectory
    {
        get => _backgroundsDirectory;
        set
        {
            if (!Set(ref _backgroundsDirectory, value)) return;
            if (!string.IsNullOrWhiteSpace(value)) Directory.CreateDirectory(value);
            ReloadBackgroundCatalog(value);
            SaveSettings();
        }
    }
    public string CustomTexturesDirectory
    {
        get => _customTexturesDirectory;
        set
        {
            if (!Set(ref _customTexturesDirectory, value)) return;
            if (!string.IsNullOrWhiteSpace(value)) Directory.CreateDirectory(value);
            Vector2Catalog.CustomTexturesOverride = value;
            SaveSettings();
        }
    }
    public string BackgroundName { get => _backgroundName; set => Set(ref _backgroundName, value); }
    public LevelNode? SelectedBackgroundImage => SelectedDocument.SelectedNode?.Kind == LevelNodeKind.Image ? SelectedDocument.SelectedNode : null;
    public string SelectedBackgroundImageLabel => SelectedBackgroundImage is null ? "Select an Image node on the canvas" : $"Editing {SelectedBackgroundImage.Name}";
    public bool BackgroundIncluded
    {
        get => SelectedBackgroundImage is { } node && BackgroundDesignerService.IsBackground(node);
        set
        {
            if (SelectedBackgroundImage is not { } node) return;
            node.Tag = value ? "Background" : "Image";
            if (value && !node.SortingLayer.StartsWith("Bg", StringComparison.OrdinalIgnoreCase)) node.SortingLayer = "BgMiddle";
            if (!value && node.SortingLayer.StartsWith("Bg", StringComparison.OrdinalIgnoreCase)) node.SortingLayer = "Wall";
            NotifyBackgroundSelectionChanged();
        }
    }
    public bool BackgroundParallax
    {
        get => SelectedBackgroundImage is { } node && ParseFactor(node.Factor) > 0;
        set
        {
            if (SelectedBackgroundImage is not { } node) return;
            node.Factor = value ? BackgroundDesignerService.DefaultFactor(node.SortingLayer) : "0";
            NotifyBackgroundSelectionChanged();
        }
    }
    public string BackgroundLayer
    {
        get => SelectedBackgroundImage?.SortingLayer ?? "BgMiddle";
        set
        {
            if (SelectedBackgroundImage is not { } node) return;
            node.SortingLayer = value;
            node.Tag = "Background";
            if (ParseFactor(node.Factor) <= 0) node.Factor = BackgroundDesignerService.DefaultFactor(value);
            NotifyBackgroundSelectionChanged();
        }
    }
    public double BackgroundFactor
    {
        get => SelectedBackgroundImage is { } node ? ParseFactor(node.Factor) : 0.75;
        set
        {
            if (SelectedBackgroundImage is not { } node) return;
            node.Factor = Math.Clamp(value, 0.05, 0.95).ToString("0.##", CultureInfo.InvariantCulture);
            OnPropertyChanged();
        }
    }

    public GameTrickMove? SelectedGameTrickMove
    {
        get => _selectedGameTrickMove;
        set
        {
            if (!Set(ref _selectedGameTrickMove, value)) return;
            OnPropertyChanged(nameof(SelectedGameTrickDetails));
        }
    }
    public GameTrickPlayback? GameTrickPlayback
    {
        get => _gameTrickPlayback;
        private set
        {
            if (!Set(ref _gameTrickPlayback, value)) return;
            OnPropertyChanged(nameof(HasGameTrickPreview));
            OnPropertyChanged(nameof(GameTrickFrame));
            OnPropertyChanged(nameof(GameTrickSubframe));
        }
    }
    public bool HasGameTrickPreview => GameTrickPlayback is not null;
    public int GameTrickTick { get => _gameTrickTick; private set => Set(ref _gameTrickTick, Math.Max(0, value)); }
    public int GameTrickFrame => GameTrickPlayback?.Sample(GameTrickTick).FrameIndex ?? 0;
    public int GameTrickSubframe => GameTrickPlayback?.Sample(GameTrickTick).Subframe ?? 0;
    public bool GameTrickPlaying
    {
        get => _gameTrickPlaying;
        private set { if (Set(ref _gameTrickPlaying, value)) OnPropertyChanged(nameof(GameTrickPlayButtonText)); }
    }
    public string GameTrickPlayButtonText => GameTrickPlaying ? "Pause" : "Play";
    public bool GameTrickShowSkeletonPoints { get => _gameTrickShowSkeletonPoints; set { if (Set(ref _gameTrickShowSkeletonPoints, value)) SaveSettings(); } }
    public bool GameTrickShowDetectorDots { get => _gameTrickShowDetectorDots; set { if (Set(ref _gameTrickShowDetectorDots, value)) SaveSettings(); } }
    public bool GameTrickShowModelNodeSpheres { get => _gameTrickShowModelNodeSpheres; set { if (Set(ref _gameTrickShowModelNodeSpheres, value)) SaveSettings(); } }
    public bool GameTrickShowModelSphereOutlines { get => _gameTrickShowModelSphereOutlines; set { if (Set(ref _gameTrickShowModelSphereOutlines, value)) SaveSettings(); } }
    public string GameTrickSearch
    {
        get => _gameTrickSearch;
        set { if (Set(ref _gameTrickSearch, value)) OnPropertyChanged(nameof(FilteredGameTrickMoves)); }
    }
    public IEnumerable<GameTrickMove> FilteredGameTrickMoves => GameTrickMoves.Where(move =>
        string.IsNullOrWhiteSpace(GameTrickSearch) ||
        $"{move.Name} {move.FileName} {move.PivotNode}".Contains(GameTrickSearch.Trim(), StringComparison.OrdinalIgnoreCase));
    public string SelectedGameTrickDetails => SelectedGameTrickMove is null
        ? "Select a move"
        : $"{SelectedGameTrickMove.FileName}  |  first {SelectedGameTrickMove.FirstFrame}  |  end {(SelectedGameTrickMove.EndFrame > 0 ? SelectedGameTrickMove.EndFrame : "file")}  |  pivot {SelectedGameTrickMove.PivotNode}"
          + (string.IsNullOrWhiteSpace(SelectedGameTrickMove.Parts) ? "" : $"  |  parts {SelectedGameTrickMove.Parts}");
    public IEnumerable<GameTrickModelSkin> GameTrickBodySkins => GameTrickSkins.Where(skin =>
        skin.Filename != "0.xml" && !SkinContains(skin, "hair", "helmet", "cap", "armor", "gear", "shirt", "jacket", "shorts", "scarf"));
    public IEnumerable<GameTrickModelSkin> GameTrickChestSkins => OptionalSkins(GameTrickSkins.Where(skin => SkinContains(skin, "armor", "shirt", "jacket", "shorts", "scarf")));
    public IEnumerable<GameTrickModelSkin> GameTrickHelmetSkins => OptionalSkins(GameTrickSkins.Where(skin => SkinContains(skin, "helmet", "cap")));
    public IEnumerable<GameTrickModelSkin> GameTrickHairSkins => OptionalSkins(GameTrickSkins.Where(skin => SkinContains(skin, "hair")));
    public string SelectedTrickBody { get => _selectedTrickBody; set => Set(ref _selectedTrickBody, value); }
    public string SelectedTrickChest { get => _selectedTrickChest; set => Set(ref _selectedTrickChest, value); }
    public string SelectedTrickHelmet { get => _selectedTrickHelmet; set => Set(ref _selectedTrickHelmet, value); }
    public string SelectedTrickHair { get => _selectedTrickHair; set => Set(ref _selectedTrickHair, value); }
    public int GameTrickStartEarlierFrames { get => _gameTrickStartEarlierFrames; set => Set(ref _gameTrickStartEarlierFrames, Math.Clamp(value, 0, 30)); }

    private void InitializeMacFeatureCommands()
    {
        _dynamicPreviewTimer.Tick += (_, _) => AdvanceDynamicPreview();
        _trickPreviewTimer.Tick += (_, _) => AdvanceGameTrickPreview();
        OpenDynamicStudioCommand = new RelayCommand(OpenDynamicStudio);
        CloseDynamicStudioCommand = new RelayCommand(CloseDynamicStudio);
        AddDynamicKeyframeCommand = new RelayCommand(AddDynamicKeyframe);
        DeleteDynamicKeyframeCommand = new RelayCommand(DeleteDynamicKeyframe);
        SaveDynamicCommand = new RelayCommand(SaveDynamic);
        ToggleDynamicPreviewCommand = new RelayCommand(ToggleDynamicPreview);
        CreateDynamicTriggerCommand = new RelayCommand(CreateDynamicTrigger);
        OpenBackgroundDesignerCommand = new RelayCommand(() => { StopDynamicPreview(true); CenterTab = "Background Designer"; NotifyBackgroundSelectionChanged(); });
        CloseBackgroundDesignerCommand = new RelayCommand(() => CenterTab = "Canvas");
        BrowseBackgroundsDirectoryCommand = new RelayCommand(BrowseBackgroundsDirectory);
        BrowseCustomTexturesDirectoryCommand = new RelayCommand(BrowseCustomTexturesDirectory);
        ImportBackgroundTexturesCommand = new RelayCommand(ImportBackgroundTextures);
        SaveBackgroundCommand = new RelayCommand(SaveBackground);
        AssignBackgroundCommand = new RelayCommand(() => AssignBackground(BackgroundName));
        ClearBackgroundAssignmentCommand = new RelayCommand(() => AssignBackground("none"));
        ResetBackgroundImageCommand = new RelayCommand(ResetBackgroundImage);
        OpenTrickPreviewCommand = new RelayCommand(OpenGameTrickPreview);
        CloseTrickPreviewCommand = new RelayCommand(CloseGameTrickPreview);
        RefreshTrickPreviewCommand = new RelayCommand(RefreshGameTrickCatalog);
        ToggleTrickPreviewCommand = new RelayCommand(ToggleGameTrickPreview);
        PreviewSelectedGameTrickCommand = new RelayCommand(PreviewSelectedGameTrick);
        DismissGameTrickOverlayCommand = new RelayCommand(ClearGameTrickPreview);
    }

    private void OpenDynamicStudio()
    {
        var target = SelectedDocument.SelectedNode;
        if (target is null || target.Kind is LevelNodeKind.Document or LevelNodeKind.Track or LevelNodeKind.Factor or LevelNodeKind.Trigger)
        {
            MessageBox.Show("Select an object, image, platform, area, or library object first.", "Dynamic Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DynamicTarget = target;
        LoadDynamicTimeline(target);
        CenterTab = "Dynamic Studio";
        BeginDynamicPreviewSession();
        StatusText = $"Dynamic Studio: {target.Name}";
    }

    private void CloseDynamicStudio()
    {
        StopDynamicPreview(true);
        CenterTab = "Canvas";
    }

    private void LoadDynamicTimeline(LevelNode target)
    {
        DynamicKeyframes.Clear();
        var package = DynamicStudioService.DecodeTimeline(target.DynamicTimelineJson) ?? DynamicStudioService.ImportTimeline(target);
        if (package is not null && package.Keyframes.Count > 0)
        {
            _dynamicTransformName = package.Name;
            _dynamicTotalFrames = Math.Max(1, package.TotalFrames);
            _dynamicMovementMode = package.MovementMode;
            _dynamicMovementQuarter = package.MovementQuarter;
            _dynamicSizeMode = package.SizeMode;
            _dynamicSizeQuarter = package.SizeQuarter;
            _dynamicRotationMode = package.RotationMode;
            foreach (var frame in package.Keyframes.OrderBy(frame => frame.Frame)) DynamicKeyframes.Add(frame);
        }
        else
        {
            _dynamicTransformName = DynamicStudioService.TransformNameFromXml(target.DynamicXml, $"{target.Name}Transform");
            DynamicKeyframes.Add(Snapshot(target, 0));
        }
        _dynamicCurrentFrame = 0;
        SelectedDynamicKeyframe = DynamicKeyframes.FirstOrDefault();
        SelectedDynamicTrigger = DynamicTriggerNodes.FirstOrDefault();
        OnPropertyChanged(nameof(DynamicTransformName));
        OnPropertyChanged(nameof(DynamicTotalFrames));
        OnPropertyChanged(nameof(DynamicCurrentFrame));
        OnPropertyChanged(nameof(DynamicMovementMode));
        OnPropertyChanged(nameof(DynamicMovementQuarter));
        OnPropertyChanged(nameof(DynamicSizeMode));
        OnPropertyChanged(nameof(DynamicSizeQuarter));
        OnPropertyChanged(nameof(DynamicRotationMode));
        OnPropertyChanged(nameof(DynamicTriggerNodes));
        RefreshDynamicXml();
    }

    private void AddDynamicKeyframe()
    {
        if (DynamicTarget is null) return;
        var frame = Snapshot(DynamicTarget, DynamicCurrentFrame);
        var old = DynamicKeyframes.FirstOrDefault(item => item.Frame == frame.Frame);
        if (old is not null) DynamicKeyframes.Remove(old);
        DynamicKeyframes.Add(frame);
        SelectedDynamicKeyframe = frame;
        PersistDynamicTimeline();
        RefreshDynamicXml();
    }

    private void DeleteDynamicKeyframe()
    {
        if (SelectedDynamicKeyframe is null) return;
        DynamicKeyframes.Remove(SelectedDynamicKeyframe);
        SelectedDynamicKeyframe = DynamicKeyframes.OrderBy(frame => frame.Frame).FirstOrDefault();
        PersistDynamicTimeline();
        RefreshDynamicXml();
    }

    private void SaveDynamic()
    {
        if (DynamicTarget is null || DynamicKeyframes.Count < 2)
        {
            MessageBox.Show("Add at least two keyframes before saving.", "Dynamic Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        StopDynamicPreview(true);
        var first = DynamicKeyframes.OrderBy(frame => frame.Frame).First();
        var baseline = HierarchyTransformService.Capture(DynamicTarget);
        HierarchyTransformService.ApplyFromSnapshot(DynamicTarget, baseline, TransformOf(first));
        DynamicTarget.DynamicXml = DynamicGeneratedXml;
        PersistDynamicTimeline();
        if (SelectedDynamicTrigger is not null)
        {
            SelectedDynamicTrigger.DynamicTriggerXml = DynamicStudioService.GenerateTriggerLoop(DynamicTransformName);
        }
        SelectedDocument.RawXml = _exporter.ExportSelection(DynamicTarget);
        _dynamicOriginalHierarchy = HierarchyTransformService.Capture(DynamicTarget);
        StatusText = $"Saved {DynamicTransformName} with {DynamicKeyframes.Count} keyframes";
        DiagnosticsLog.Info($"Dynamic Studio saved '{DynamicTransformName}' on {DynamicTarget.Name}.");
    }

    private void CreateDynamicTrigger()
    {
        if (DynamicTarget is null) return;
        var trigger = new LevelNode
        {
            Name = $"Trigger_{DynamicStudioService.SafeName(DynamicTransformName)}",
            Kind = LevelNodeKind.Trigger,
            Factor = DynamicTarget.Factor,
            X = DynamicTarget.X,
            Y = DynamicTarget.Y,
            Width = Math.Max(180, DynamicTarget.Width),
            Height = Math.Max(100, DynamicTarget.Height),
            Choice = "Dynamic",
            Variant = "Default",
            Tag = "Trigger",
            DynamicTriggerXml = DynamicStudioService.GenerateTriggerLoop(DynamicTransformName)
        };
        AppendSceneNode(SelectedDocument, trigger);
        SelectedDynamicTrigger = trigger;
        OnPropertyChanged(nameof(DynamicTriggerNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
        StatusText = $"Created trigger for {DynamicTransformName}";
    }

    private void ToggleDynamicPreview()
    {
        if (DynamicIsPreviewing) StopDynamicPreview(true);
        else
        {
            if (DynamicKeyframes.Count < 2 || DynamicTarget is null) return;
            BeginDynamicPreviewSession();
            _dynamicCurrentFrame = DynamicKeyframes.Min(frame => frame.Frame);
            OnPropertyChanged(nameof(DynamicCurrentFrame));
            DynamicIsPreviewing = true;
            _dynamicPreviewTimer.Start();
            ApplyDynamicPose(DynamicCurrentFrame);
        }
    }

    private void AdvanceDynamicPreview()
    {
        if (!DynamicIsPreviewing) return;
        var last = DynamicKeyframes.Max(frame => frame.Frame);
        if (DynamicCurrentFrame >= last)
        {
            StopDynamicPreview(true);
            return;
        }
        _dynamicCurrentFrame = Math.Min(last, DynamicCurrentFrame + 3);
        OnPropertyChanged(nameof(DynamicCurrentFrame));
        ApplyDynamicPose(DynamicCurrentFrame);
    }

    private void BeginDynamicPreviewSession()
    {
        if (DynamicTarget is not null && _dynamicOriginalHierarchy is null)
        {
            _dynamicOriginalHierarchy = HierarchyTransformService.Capture(DynamicTarget);
        }
    }

    private void StopDynamicPreview(bool restore)
    {
        _dynamicPreviewTimer.Stop();
        DynamicIsPreviewing = false;
        if (restore) RestoreDynamicPose();
    }

    private void RestoreDynamicPose()
    {
        if (DynamicTarget is not null && _dynamicOriginalHierarchy is { } original)
        {
            HierarchyTransformService.Restore(DynamicTarget, original);
        }
        _dynamicOriginalHierarchy = null;
    }

    private void ApplyDynamicPose(int frame)
    {
        if (DynamicTarget is null || DynamicKeyframes.Count < 2) return;
        BeginDynamicPreviewSession();
        var pose = DynamicStudioService.Interpolate(DynamicKeyframes, frame, DynamicMovementMode, DynamicMovementQuarter,
            DynamicSizeMode, DynamicSizeQuarter, DynamicRotationMode);
        if (pose is not null && _dynamicOriginalHierarchy is { } original)
        {
            HierarchyTransformService.ApplyFromSnapshot(DynamicTarget, original, TransformOf(pose));
        }
    }

    private void PersistDynamicTimeline()
    {
        if (DynamicTarget is null) return;
        DynamicTarget.DynamicTimelineJson = DynamicStudioService.EncodeTimeline(new DynamicStudioTimelinePackage
        {
            Name = DynamicStudioService.SafeName(DynamicTransformName),
            TotalFrames = DynamicTotalFrames,
            MovementMode = DynamicMovementMode,
            MovementQuarter = DynamicMovementQuarter,
            SizeMode = DynamicSizeMode,
            SizeQuarter = DynamicSizeQuarter,
            RotationMode = DynamicRotationMode,
            Keyframes = DynamicKeyframes.OrderBy(frame => frame.Frame).ToList()
        });
    }

    private void RefreshDynamicXml()
    {
        OnPropertyChanged(nameof(DynamicGeneratedXml));
    }

    private static DynamicStudioKeyframe Snapshot(LevelNode node, int frame) => new()
    {
        Frame = frame, X = node.X, Y = node.Y, Width = node.Width, Height = node.Height, Rotation = node.Rotation
    };

    private static HierarchyTransformService.NodeTransform TransformOf(DynamicStudioKeyframe pose)
        => new(pose.X, pose.Y, pose.Width, pose.Height, pose.Rotation);

    private void NotifyBackgroundSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBackgroundImage));
        OnPropertyChanged(nameof(SelectedBackgroundImageLabel));
        OnPropertyChanged(nameof(BackgroundIncluded));
        OnPropertyChanged(nameof(BackgroundParallax));
        OnPropertyChanged(nameof(BackgroundLayer));
        OnPropertyChanged(nameof(BackgroundFactor));
    }

    private void BrowseBackgroundsDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Choose shared custom_backgrounds folder" };
        if (dialog.ShowDialog() == true) BackgroundsDirectory = dialog.FolderName;
    }

    private void ReloadBackgroundCatalog(string folder)
    {
        var catalog = BackgroundDesignerService.LoadCatalog(folder);
        var currentName = _backgroundName.Trim();
        var matchingDefinition = catalog.Definitions.FirstOrDefault(definition =>
            definition.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));

        if (matchingDefinition is null)
        {
            if (!string.IsNullOrEmpty(_backgroundName))
            {
                _backgroundName = "";
                OnPropertyChanged(nameof(BackgroundName));
            }
        }
        else
        {
            if (_backgroundName != matchingDefinition.Name)
            {
                _backgroundName = matchingDefinition.Name;
                OnPropertyChanged(nameof(BackgroundName));
            }
        }
    }

    private void BrowseCustomTexturesDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Choose custom_textures folder" };
        if (dialog.ShowDialog() == true) CustomTexturesDirectory = dialog.FolderName;
    }

    private void ImportBackgroundTextures()
    {
        if (string.IsNullOrWhiteSpace(CustomTexturesDirectory))
        {
            MessageBox.Show("Choose the custom_textures folder first.", "Background Designer");
            return;
        }
        var dialog = new OpenFileDialog { Filter = "Vector 2 textures|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        Directory.CreateDirectory(CustomTexturesDirectory);
        var imported = 0;
        foreach (var source in dialog.FileNames)
        {
            try
            {
                CustomContentService.ImportTexture(source, CustomTexturesDirectory);
                imported++;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Warn($"Custom texture import skipped '{Path.GetFileName(source)}': {ex.Message}");
            }
        }
        Vector2Catalog.CustomTexturesOverride = CustomTexturesDirectory;
        RefreshAssets();
        StatusText = $"Imported {imported} custom texture(s)";
    }

    private void SaveBackground()
    {
        var cleanName = BackgroundName.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(BackgroundsDirectory))
        {
            MessageBox.Show("Enter a background name and choose the backgrounds folder.", "Background Designer");
            return;
        }
        var pieces = SelectedDocument.SceneNodes.Where(BackgroundDesignerService.IsBackground)
            .Select(node => _exporter.ExportSelection(node)).Where(xml => xml.Contains("<Image", StringComparison.Ordinal)).ToList();
        if (pieces.Count == 0)
        {
            MessageBox.Show("No Image nodes tagged Background or placed on a Bg* layer were found.", "Background Designer");
            return;
        }
        BackgroundDesignerService.SaveCurrent(
            BackgroundsDirectory,
            new CustomBackgroundDefinition
            {
                Name = cleanName,
                Pieces = pieces
            });
        StatusText = $"Saved background {cleanName}: {pieces.Count} pieces";
        DiagnosticsLog.Info($"Background Designer saved '{cleanName}' with {pieces.Count} image pieces.");
    }

    public void PlaceSceneBackground(CustomBackgroundDefinition definition)
    {
        if (definition.Pieces.Count == 0) throw new InvalidDataException("This background set has no pieces.");
        var nodes = definition.Pieces.Select(piece => _parser.ParseFragment(piece)).ToList();
        if (nodes.Any(node => node.Kind != LevelNodeKind.Image))
            throw new InvalidDataException("A scene background must contain image pieces.");
        foreach (var node in nodes)
        {
            node.Tag = "Background";
            AppendSceneNode(SelectedDocument, node);
        }
        IsProjectManagerVisible = false;
        CenterTab = "Canvas";
        SelectNode(nodes[0]);
        OnPropertyChanged(nameof(VisibleHierarchyNodes));
        OnPropertyChanged(nameof(HierarchyRootNodes));
        StatusText = $"Placed {definition.Name} in {SelectedDocument.Name}";
    }

    private void AssignBackground(string name)
    {
        var clean = name.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;
        SelectedDocument.CustomBackgroundName = clean;
        SelectedDocument.HasCustomBackgroundAssignment = true;
        BackgroundName = clean.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : clean;
        StatusText = clean.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "Stock backgrounds disabled for this room"
            : $"Assigned background {clean}";
    }

    private void ResetBackgroundImage()
    {
        if (SelectedBackgroundImage is not { } node) return;
        node.Tag = "Background";
        node.SortingLayer = "BgMiddle";
        node.Factor = BackgroundDesignerService.DefaultFactor("BgMiddle");
        NotifyBackgroundSelectionChanged();
    }

    private static double ParseFactor(string value)
        => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;


    private void OpenGameTrickPreview()
    {
        CenterTab = "Trick Preview";
        if (GameTrickMoves.Count == 0) RefreshGameTrickCatalog();
    }

    private void CloseGameTrickPreview()
    {
        CenterTab = "Canvas";
    }

    private void RefreshGameTrickCatalog()
    {
        try
        {
            var moves = _gameTrickPreview.LoadMoves();
            var skins = _gameTrickPreview.LoadModelSkins();
            GameTrickMoves.Clear();
            foreach (var move in moves) GameTrickMoves.Add(move);
            GameTrickSkins.Clear();
            foreach (var skin in skins) GameTrickSkins.Add(skin);
            EnsureSelectedSkin(ref _selectedTrickBody, "1.xml", GameTrickBodySkins);
            EnsureSelectedSkin(ref _selectedTrickChest, "black_armor.xml", GameTrickChestSkins);
            EnsureSelectedSkin(ref _selectedTrickHelmet, "black_helmet.xml", GameTrickHelmetSkins);
            EnsureSelectedSkin(ref _selectedTrickHair, "hair.xml", GameTrickHairSkins);
            OnPropertyChanged(nameof(GameTrickBodySkins));
            OnPropertyChanged(nameof(GameTrickChestSkins));
            OnPropertyChanged(nameof(GameTrickHelmetSkins));
            OnPropertyChanged(nameof(GameTrickHairSkins));
            OnPropertyChanged(nameof(SelectedTrickBody));
            OnPropertyChanged(nameof(SelectedTrickChest));
            OnPropertyChanged(nameof(SelectedTrickHelmet));
            OnPropertyChanged(nameof(SelectedTrickHair));
            OnPropertyChanged(nameof(FilteredGameTrickMoves));
            SelectedGameTrickMove = BestInitialGameTrickMove()
                                    ?? GameTrickMoves.FirstOrDefault(move => move.IsTrick)
                                    ?? GameTrickMoves.FirstOrDefault();
            StatusText = $"Trick Preview loaded {GameTrickMoves.Count} moves and {GameTrickSkins.Count} model skins";
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Trick Preview could not load game data: {ex.Message}");
            StatusText = "Trick Preview needs the game Assets folder in Settings";
        }
    }

    private GameTrickMove? BestInitialGameTrickMove()
    {
        var node = SelectedDocument.SelectedNode;
        if (node is null) return null;
        var hints = new[] { node.Name, node.ClassName, node.Template, node.Variant, node.Choice }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());
        foreach (var hint in hints)
        {
            var exact = GameTrickMoves.FirstOrDefault(move => move.Name.Equals(hint, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var partial = GameTrickMoves.FirstOrDefault(move =>
                hint.Contains(move.Name, StringComparison.OrdinalIgnoreCase) ||
                move.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (partial is not null) return partial;
        }
        return null;
    }

    private void PreviewSelectedGameTrick()
    {
        var move = SelectedGameTrickMove;
        if (move is null) return;
        try
        {
            var skins = new[] { "0.xml", SelectedTrickBody, SelectedTrickChest, SelectedTrickHelmet, SelectedTrickHair }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            GameTrickPlayback = _gameTrickPreview.LoadPlayback(
                move, skins, GameTrickStartEarlierFrames, SelectedDocument.SelectedNode?.Id);
            GameTrickTick = 0;
            _gameTrickTickBase = 0;
            GameTrickPlaying = true;
            _trickPreviewClock.Restart();
            _trickPreviewTimer.Start();
            CenterTab = "Canvas";
            StatusText = $"Previewing trick {move.Name}";
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error($"Could not preview trick '{move.Name}': {ex.Message}");
            GameTrickPlayback = null;
        }
    }

    private void ToggleGameTrickPreview()
    {
        if (GameTrickPlayback is null) return;
        if (GameTrickPlaying)
        {
            AdvanceGameTrickPreview();
            _gameTrickTickBase = GameTrickTick;
            _trickPreviewClock.Reset();
            GameTrickPlaying = false;
            _trickPreviewTimer.Stop();
        }
        else
        {
            _gameTrickTickBase = GameTrickTick;
            _trickPreviewClock.Restart();
            GameTrickPlaying = true;
            _trickPreviewTimer.Start();
        }
    }

    private void AdvanceGameTrickPreview()
    {
        if (GameTrickPlayback is null || !GameTrickPlaying) return;
        var realTimeTick = _gameTrickTickBase + (int)Math.Floor(_trickPreviewClock.Elapsed.TotalSeconds * 60.0);
        if (realTimeTick == GameTrickTick) return;
        GameTrickTick = realTimeTick;
        OnPropertyChanged(nameof(GameTrickFrame));
        OnPropertyChanged(nameof(GameTrickSubframe));
    }

    public void MoveGameTrickPreview(Vector worldDelta)
    {
        if (GameTrickPlayback is null) return;
        GameTrickPlayback.PlacementOffset += worldDelta;
        OnPropertyChanged(nameof(GameTrickPlayback));
    }

    public void ClearGameTrickPreview()
    {
        _trickPreviewTimer.Stop();
        _trickPreviewClock.Reset();
        GameTrickPlaying = false;
        GameTrickPlayback = null;
        GameTrickTick = 0;
        _gameTrickTickBase = 0;
        StatusText = "Trick preview closed";
    }

    private static bool SkinContains(GameTrickModelSkin skin, params string[] needles)
        => needles.Any(needle => skin.Filename.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<GameTrickModelSkin> OptionalSkins(IEnumerable<GameTrickModelSkin> skins)
        => new[] { new GameTrickModelSkin("", "None") }.Concat(skins);

    private static void EnsureSelectedSkin(ref string selected, string preferred, IEnumerable<GameTrickModelSkin> options)
    {
        var list = options.ToList();
        var current = selected;
        if (list.Any(skin => skin.Filename.Equals(current, StringComparison.OrdinalIgnoreCase))) return;
        selected = list.FirstOrDefault(skin => skin.Filename.Equals(preferred, StringComparison.OrdinalIgnoreCase))?.Filename
                   ?? list.FirstOrDefault()?.Filename ?? "";
    }
}
