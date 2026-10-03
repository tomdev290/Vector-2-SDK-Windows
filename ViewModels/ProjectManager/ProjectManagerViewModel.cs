using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Views.ProjectManager;
using System.Xml.Linq;

namespace Vector2LevelEditor.ViewModels.ProjectManager;

public sealed class ProjectManagerViewModel : NotifyObject
{
    private static readonly HashSet<string> StudioSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content", "Chapters", "Zones", "Story", "Quests", "Localization",
        "Obstacles", "Tricks", "Assets", "Backgrounds",
        "Trigger Designer", "Protocols", "Generator", "Traps", "Upgrades", "Shop",
        "Missions", "Rewards", "Tutorials", "Audio", "Save Data", "Models"
    };
    public event Action<string>? RoomOpenRequested;
    public Func<string?>? GameDataPathProvider { get; set; }
    private static readonly string LastProjectFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vector2LevelEditor", "last-project.txt");
    private readonly ProjectManifestService _manifests = new();
    private readonly ProjectInventoryService _inventory = new();
    private readonly ProjectTemplateService _templates = new();
    private readonly ProjectValidationService _validation = new();
    private readonly ProjectInstallerService _installer;
    private Vector2Project? _project;
    private ProjectSection _selectedSection = ProjectSection.Overview;
    private ProjectFileItem? _selectedFile;
    private string _searchText = "";
    private bool _searchFocused;
    private string _status = "Create or open a project to begin.";
    private string _validationSummary = "Not checked";
    private string _newItemName = "";
    private string _editorText = "";
    private string _editorTitle = "";
    private bool _isEditing;
    private int _inventoryRevision;
    private string _studioDescription = "";
    private string _studioType = "";
    private string _studioTarget = "100";
    private string _studioAmount = "100";

    public ObservableCollection<ProjectSection> Sections { get; } = new(ProjectSection.All);
    public ObservableCollection<ProjectFileItem> Files { get; } = [];
    public ObservableCollection<ProjectValidationIssue> ValidationIssues { get; } = [];
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand CreateItemCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ConnectGameCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand SaveEditorCommand { get; }
    public RelayCommand CloseEditorCommand { get; }
    public RelayCommand<ProjectSection> SelectSectionCommand { get; }
    public RelayCommand ChooseCoverCommand { get; }
    public RelayCommand<ProjectFileItem> OpenRecentRoomCommand { get; }
    public RelayCommand ShowRoomsCommand { get; }
    public RelayCommand ShowZonesCommand { get; }
    public RelayCommand ShowAssetsCommand { get; }
    public RelayCommand<ProjectOverviewZone> OpenZoneFolderCommand { get; }
    public RelayCommand<ProjectFileItem> OpenSearchResultCommand { get; }
    public RelayCommand<ProjectSection> OpenSearchSectionCommand { get; }
    public RelayCommand<ProjectSearchHit> OpenSearchHitCommand { get; }

    public ProjectManagerViewModel(string? installationRoot = null)
    {
        _installer = new ProjectInstallerService(installationRoot);
        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        SaveProjectCommand = new RelayCommand(SaveProject, HasProject);
        RefreshCommand = new RelayCommand(Refresh, HasProject);
        ImportCommand = new RelayCommand(Import, CanWorkInSection);
        CreateItemCommand = new RelayCommand(CreateItem, CanCreateItem);
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedFile is not null);
        OpenFileCommand = new RelayCommand(OpenSelected, () => SelectedFile is not null);
        RevealCommand = new RelayCommand(RevealSelected, () => SelectedFile is not null);
        OpenFolderCommand = new RelayCommand(OpenSectionFolder, HasProject);
        ConnectGameCommand = new RelayCommand(ConnectGame, HasProject);
        InstallCommand = new RelayCommand(Install, HasProject);
        ValidateCommand = new RelayCommand(Validate, HasProject);
        ExportCommand = new RelayCommand(Export, HasProject);
        SaveEditorCommand = new RelayCommand(SaveEditor, () => IsEditing && SelectedFile is not null);
        CloseEditorCommand = new RelayCommand(CloseEditor);
        SelectSectionCommand = new RelayCommand<ProjectSection>(section => { if (section is not null) SelectedSection = section; });
        ChooseCoverCommand = new RelayCommand(ChooseCover, HasProject);
        OpenRecentRoomCommand = new RelayCommand<ProjectFileItem>(item => { if (item is not null) RoomOpenRequested?.Invoke(item.FullPath); });
        ShowRoomsCommand = new RelayCommand(() => SelectedSection = ProjectSection.All.Single(section => section.Name == "Content"));
        ShowZonesCommand = new RelayCommand(() => SelectedSection = ProjectSection.All.Single(section => section.Name == "Zones"));
        ShowAssetsCommand = new RelayCommand(() => SelectedSection = ProjectSection.All.Single(section => section.Name == "Assets"));
        OpenZoneFolderCommand = new RelayCommand<ProjectOverviewZone>(zone => { if (zone is not null) Shell(zone.Folder); });
        OpenSearchResultCommand = new RelayCommand<ProjectFileItem>(OpenSearchResult);
        OpenSearchSectionCommand = new RelayCommand<ProjectSection>(section => { if (section is null) return; SearchText = ""; SelectedSection = section; });
        OpenSearchHitCommand = new RelayCommand<ProjectSearchHit>(hit =>
        {
            if (hit is null) return;
            if (hit.File is not null) OpenSearchResult(hit.File);
            else if (hit.TargetSection is not null) SelectedSection = hit.TargetSection;
            SearchText = "";
            SearchFocused = false;
        });
        RestoreLastProject();
    }

    public Vector2Project? Project
    {
        get => _project;
        private set
        {
            if (ReferenceEquals(_project, value)) return;
            if (_project is not null) _project.PropertyChanged -= OnProjectPropertyChanged;
            Set(ref _project, value);
            if (_project is not null) _project.PropertyChanged += OnProjectPropertyChanged;
            NotifyProjectChanged();
        }
    }
    private void OnProjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Vector2Project.Description)) OnPropertyChanged(nameof(ProjectDescriptionText));
    }
    public bool HasOpenProject => Project is not null;
    public int InventoryRevision { get => _inventoryRevision; private set => Set(ref _inventoryRevision, value); }
    public Visibility WelcomeVisibility => Project is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProjectVisibility => Project is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility OverviewVisibility => Project is not null && SelectedSection.Name == "Overview" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BrowserVisibility => Project is not null && SelectedSection.Name != "Overview" && !IsStudioSection && !IsEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StudioVisibility => Project is not null && IsStudioSection && !IsEditing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EditorVisibility => Project is not null && IsEditing ? Visibility.Visible : Visibility.Collapsed;
    public ProjectSection SelectedSection { get => _selectedSection; set { if (Set(ref _selectedSection, value)) { IsEditing = false; ResetStudioDefaults(); OnPropertyChanged(nameof(SectionTitle)); OnPropertyChanged(nameof(SectionDescription)); OnPropertyChanged(nameof(OverviewVisibility)); OnPropertyChanged(nameof(BrowserVisibility)); OnPropertyChanged(nameof(StudioVisibility)); OnPropertyChanged(nameof(IsStudioSection)); OnPropertyChanged(nameof(IsCommerceStudio)); OnPropertyChanged(nameof(IsSdkStudio)); OnPropertyChanged(nameof(CreationVisibility)); OnPropertyChanged(nameof(FilteredFiles)); RaiseCommands(); } } }
    public ProjectFileItem? SelectedFile { get => _selectedFile; set { if (Set(ref _selectedFile, value)) RaiseCommands(); } }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) { OnPropertyChanged(nameof(FilteredFiles)); OnPropertyChanged(nameof(SearchResults)); OnPropertyChanged(nameof(MatchingSections)); OnPropertyChanged(nameof(SearchHits)); OnPropertyChanged(nameof(NoSearchResultsVisibility)); OnPropertyChanged(nameof(SearchResultsVisibility)); OnPropertyChanged(nameof(SearchPlaceholderVisibility)); } } }
    public bool SearchFocused { get => _searchFocused; set { if (Set(ref _searchFocused, value)) { OnPropertyChanged(nameof(SearchResultsVisibility)); OnPropertyChanged(nameof(SearchFocusVisibility)); } } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string ValidationSummary { get => _validationSummary; set => Set(ref _validationSummary, value); }
    public string NewItemName { get => _newItemName; set { if (Set(ref _newItemName, value)) RaiseCommands(); } }
    public string EditorText { get => _editorText; set => Set(ref _editorText, value); }
    public string EditorTitle { get => _editorTitle; private set => Set(ref _editorTitle, value); }
    public bool IsEditing { get => _isEditing; private set { if (!Set(ref _isEditing, value)) return; OnPropertyChanged(nameof(BrowserVisibility)); OnPropertyChanged(nameof(StudioVisibility)); OnPropertyChanged(nameof(EditorVisibility)); RaiseCommands(); } }
    public bool IsStudioSection => StudioSections.Contains(SelectedSection.Name);
    public bool IsCommerceStudio => SelectedSection.Name is "Shop" or "Upgrades";
    public bool IsSdkStudio => SelectedSection.Name is "Missions" or "Rewards" or "Tutorials";
    public string StudioDescription { get => _studioDescription; set => Set(ref _studioDescription, value); }
    public string StudioType { get => _studioType; set => Set(ref _studioType, value); }
    public string StudioTarget { get => _studioTarget; set => Set(ref _studioTarget, value); }
    public string StudioAmount { get => _studioAmount; set => Set(ref _studioAmount, value); }
    public string SectionTitle => SelectedSection.Name;
    public string SectionDescription => SelectedSection.Description;
    public Visibility CreationVisibility => CanCreateInSection() ? Visibility.Visible : Visibility.Collapsed;
    public string ProjectPath => Project?.RootPath ?? "No project";
    public string GameStatus => Project is null || string.IsNullOrWhiteSpace(Project.GameDataPath) ? "Game not connected" : "Vector 2 connected";
    public int TotalFiles => Files.Select(file => file.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int RoomCount => Files.Count(file => file.Section == "Content" && file.Extension == "XML");
    public int ZoneCount => Files.Count(file => file.Section == "Zones" && file.Extension == "XML");
    public int ContentTypeCount => Files.Select(file => file.Section).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int AssetCount => Files.Count(file => file.Section == "Assets");
    public int ModelCount => Files.Count(file => file.Section == "Models" && file.Name.Equals("model.xml", StringComparison.OrdinalIgnoreCase));
    public int TrickCount => Files.Count(file => file.Section == "Tricks" && file.Name.Equals("trick.xml", StringComparison.OrdinalIgnoreCase));
    public int AudioCount => Files.Count(file => file.Section == "Audio" && file.Extension is not "XML");
    public string ProjectDescriptionText => string.IsNullOrWhiteSpace(Project?.Description)
        ? "Add a description in Project Settings."
        : Project.Description;
    public IEnumerable<ProjectFileItem> RecentFiles => Files.OrderByDescending(file => file.Modified).Take(8);
    public IEnumerable<ProjectFileItem> RecentRooms => Files.Where(file => file.Section == "Content" && file.Extension == "XML").OrderByDescending(file => file.Modified).Take(5);
    public IEnumerable<ProjectFileItem> SearchResults => string.IsNullOrWhiteSpace(SearchText)
        ? []
        : Files.Where(file => SearchMatches(file.Name, SearchText) ||
                              SearchMatches(file.RelativePath, SearchText))
            .DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(15).ToList();
    public IEnumerable<ProjectSection> MatchingSections => string.IsNullOrWhiteSpace(SearchText)
        ? []
        : Sections.Where(section => SearchMatches(section.DisplayName, SearchText)).Take(5).ToList();
    public IReadOnlyList<ProjectSearchHit> SearchHits => ProjectSearchService.Search(SearchText, Sections, Files);
    public Visibility NoSearchResultsVisibility => SearchHits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    private static bool SearchMatches(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        value.Replace('_', ' ').Contains(query, StringComparison.OrdinalIgnoreCase);
    public Visibility SearchResultsVisibility => !SearchFocused || string.IsNullOrWhiteSpace(SearchText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SearchFocusVisibility => SearchFocused ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SearchPlaceholderVisibility => string.IsNullOrEmpty(SearchText) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoRoomsVisibility => RecentRooms.Any() ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NoZonesVisibility => OverviewZones.Any() ? Visibility.Collapsed : Visibility.Visible;
    public IEnumerable<ProjectOverviewZone> OverviewZones
    {
        get
        {
            if (Project is null) return [];
            return ProjectOverviewService.LoadZones(Project.RootPath);
        }
    }
    public string? OverviewCoverPath => Project is null || string.IsNullOrWhiteSpace(Project.CoverPath) ? null : Path.Combine(Project.RootPath, Project.CoverPath);
    public IEnumerable<ProjectFileItem> FilteredFiles => Files.Where(file =>
        (SelectedSection.Name == file.Section) &&
        (string.IsNullOrWhiteSpace(SearchText) || file.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || file.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
    public int FilteredFileCount => FilteredFiles.Count();

    private bool HasProject() => Project is not null;
    private bool CanWorkInSection() => Project is not null && SelectedSection.Folder.Length > 0;
    private bool CanCreateItem() => CanCreateInSection() && !string.IsNullOrWhiteSpace(NewItemName);
    private bool CanCreateInSection() => CanWorkInSection() && SelectedSection.Name is not ("Content" or "Models" or "Audio" or "Assets" or "Backgrounds" or "Tricks" or "Obstacles" or "Save Data");

    private void NewProject()
    {
        var dialog = new ProjectCreationDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        var projectFolder = Path.Combine(dialog.ParentFolder, dialog.ProjectName);
        Run(() =>
        {
            var project = _manifests.Create(projectFolder);
            project.Name = dialog.ProjectName;
            _manifests.Save(project);
            LoadProject(project);
        }, "Project created. Connect Vector 2 Data only when you are ready to install.");
    }

    private void OpenProject()
    {
        var dialog = new OpenFolderDialog { Title = "Open the folder containing project.xml", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        Run(() => LoadProject(_manifests.Load(dialog.FolderName)), "Project ready.");
    }

    private void LoadProject(Vector2Project project)
    {
        var bundledGame = Vector2GameIntegrationLocator.Detect();
        if (bundledGame is not null &&
            (string.IsNullOrWhiteSpace(project.GameDataPath) || !Directory.Exists(project.GameDataPath)))
            project.GameDataPath = bundledGame.DataFolder;
        Project = project;
        SelectedSection = ProjectSection.Overview;
        Refresh();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastProjectFile)!);
            File.WriteAllText(LastProjectFile, project.RootPath);
        }
        catch (IOException ex) { global::Vector2LevelEditor.Diagnostics.DiagnosticsLog.Error($"Recent project preference could not be saved: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { global::Vector2LevelEditor.Diagnostics.DiagnosticsLog.Error($"Recent project preference could not be saved: {ex.Message}"); }
    }

    private void RestoreLastProject()
    {
        var path = File.Exists(LastProjectFile) ? File.ReadAllText(LastProjectFile).Trim() : "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(Path.Combine(path, ProjectManifestService.ManifestName))) return;
        try { LoadProject(_manifests.Load(path)); Status = "Project ready."; }
        catch (Exception ex) { Status = $"Could not refresh the last project: {ex.Message}"; }
    }

    private void SaveProject() => Run(() => _manifests.Save(Project!), "Project saved.");

    private void OpenSearchResult(ProjectFileItem? item)
    {
        if (item is null) return;
        SearchText = "";
        var section = ProjectSection.All.FirstOrDefault(value => value.Name == item.Section);
        if (section is not null) SelectedSection = section;
        SelectedFile = item;
        OpenSelected();
    }

    private void ChooseCover()
    {
        if (Project is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog() != true) return;
        Run(() =>
        {
            var artwork = Path.Combine(Project.RootPath, "project_artwork");
            Directory.CreateDirectory(artwork);
            var target = Path.Combine(artwork, "cover" + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            File.Copy(dialog.FileName, target, true);
            Project.CoverPath = Path.GetRelativePath(Project.RootPath, target).Replace('\\', '/');
            _manifests.Save(Project);
            OnPropertyChanged(nameof(OverviewCoverPath));
        }, "Project artwork updated.");
    }

    private void Refresh()
    {
        RefreshInventory();
        InventoryRevision++;
    }

    public void RefreshInventory()
    {
        if (Project is null) return;
        var selectedPath = SelectedFile?.FullPath;
        var scan = _inventory.Scan(Project);
        Files.Clear();
        foreach (var item in scan) Files.Add(item);
        SelectedFile = selectedPath is null ? null : Files.FirstOrDefault(file => file.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        NotifyInventoryChanged();
    }

    private void Import()
    {
        if (Project is null) return;
        var dialog = new OpenFileDialog { Title = $"Import files into {SelectedSection.Name}", Multiselect = true, Filter = "Supported content|*.xml;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wav;*.ogg;*.mp3;*.json;*.bytes|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        Run(() => { var count = _inventory.Import(Project, SelectedSection, dialog.FileNames).Count; Refresh(); Status = $"Imported {count} files into {SelectedSection.Name}."; });
    }

    private void CreateItem()
    {
        if (Project is null) return;
        Run(() =>
        {
            var path = _templates.Create(Project, SelectedSection, NewItemName);
            ApplyStudioFields(path);
            NewItemName = "";
            Refresh();
            SelectedFile = Files.FirstOrDefault(file => file.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            Status = $"Created {Path.GetFileName(path)} in {SelectedSection.Name}.";
        });
    }

    private void ApplyStudioFields(string path)
    {
        if (!IsStudioSection || !File.Exists(path)) return;
        var document = XDocument.Load(path);
        var target = document.Root?.Name.LocalName is "Protocols" ? document.Root.Elements().FirstOrDefault() : document.Root;
        if (target is null) return;
        if (!string.IsNullOrWhiteSpace(StudioDescription)) target.SetAttributeValue("Description", StudioDescription.Trim());
        if (!string.IsNullOrWhiteSpace(StudioType) && IsSdkStudio) target.SetAttributeValue("Type", StudioType.Trim());
        if (IsSdkStudio)
        {
            target.SetAttributeValue("Target", StudioTarget.Trim());
            target.SetAttributeValue("Amount", StudioAmount.Trim());
        }
        if (IsCommerceStudio && int.TryParse(StudioAmount, out var price)) target.SetAttributeValue("Price", Math.Max(0, price));
        document.Save(path);
    }

    private void ResetStudioDefaults()
    {
        StudioDescription = "";
        StudioType = SelectedSection.Name switch { "Missions" => "Points", "Rewards" => "Credits", "Tutorials" => "Menu Open", _ => "" };
        StudioTarget = "100";
        StudioAmount = IsCommerceStudio ? "1000" : "100";
    }

    private void DeleteSelected()
    {
        if (SelectedFile is null) return;
        if (MessageBox.Show($"Delete {SelectedFile.Name} from this project?", "Delete project item", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Run(() => { File.Delete(SelectedFile.FullPath); Refresh(); Status = "Project item deleted. Install Changes will remove its previous game copy."; });
    }

    private void OpenSelected()
    {
        if (SelectedFile is null) return;
        if (SelectedFile.Section == "Content" && SelectedFile.Extension.Equals("XML", StringComparison.OrdinalIgnoreCase))
        {
            RoomOpenRequested?.Invoke(SelectedFile.FullPath);
            return;
        }
        if (SelectedFile.Extension.Equals("XML", StringComparison.OrdinalIgnoreCase))
        {
            Run(() =>
            {
                EditorText = File.ReadAllText(SelectedFile.FullPath);
                EditorTitle = $"Edit {SelectedFile.Name}";
                IsEditing = true;
                Status = $"Editing {SelectedFile.RelativePath}.";
            });
            return;
        }
        Shell(SelectedFile.FullPath);
    }

    public void OpenProjectRoomFromStudio(string path) => RoomOpenRequested?.Invoke(path);

    private void SaveEditor()
    {
        if (SelectedFile is null) return;
        Run(() =>
        {
            var parsed = XDocument.Parse(EditorText, LoadOptions.PreserveWhitespace);
            var temporary = SelectedFile.FullPath + ".tmp";
            File.WriteAllText(temporary, parsed.Declaration is null ? EditorText : parsed.ToString(SaveOptions.DisableFormatting));
            File.Move(temporary, SelectedFile.FullPath, true);
            Refresh();
            Status = $"Saved and validated {SelectedFile.Name}.";
        });
    }

    private void CloseEditor()
    {
        IsEditing = false;
        EditorText = "";
        EditorTitle = "";
    }
    private void RevealSelected() { if (SelectedFile is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedFile.FullPath}\"") { UseShellExecute = true }); }
    private void OpenSectionFolder() { if (Project is not null) Shell(SelectedSection.Folder.Length == 0 ? Project.RootPath : ProjectManifestService.SafeCombine(Project.RootPath, SelectedSection.Folder)); }

    private void ConnectGame()
    {
        if (Project is null) return;
        var dialog = new OpenFolderDialog { Title = "Choose the Vector 2_Data folder", Multiselect = false, InitialDirectory = Project.GameDataPath };
        if (dialog.ShowDialog() != true) return;
        Run(() => { Project.GameDataPath = ProjectInstallerService.ValidateGameDataPath(dialog.FolderName); _manifests.Save(Project); NotifyProjectChanged(); Status = "Vector 2 connected. Nothing is copied until Install Changes."; });
    }

    private void Install()
    {
        if (Project is null) return;
        Run(() => {
            _manifests.Save(Project);
            var data = GameDataPathProvider?.Invoke() ?? Project.GameDataPath;
            var result = _installer.Install(Project, data);
            Status = InstallSummary(result);
        });
    }

    public void StudioChanged(string message)
    {
        Status = message;
        RefreshInventory();
        if (Project is null || !new[] { "Saved ", "Created ", "Imported ", "Deleted ", "Added ", "Assigned ", "Unassigned ", "Applied " }
            .Any(prefix => message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var data = GameDataPathProvider?.Invoke() ?? Project.GameDataPath;
            var result = _installer.Install(Project, data);
            Status = message + " " + InstallSummary(result);
        }
        catch (Exception error)
        {
            Status = message + " Project change saved, but game content installation failed: " + error.Message;
        }
    }

    private static string InstallSummary(ProjectInstallResult result)
    {
        var destination = Path.GetDirectoryName(Path.GetDirectoryName(result.ReceiptPath))!;
        var summary = result.Copied == 0 && result.Removed == 0
            ? $"Installed content is already up to date: {result.Verified} files verified in {destination}."
            : $"Installed content verified: {result.Verified} files in {destination}; updated {result.Copied}, removed {result.Removed}.";
        return summary + (result.UnfinishedQuests == 0 ? "" :
            $" {result.UnfinishedQuests} unfinished quest file(s) kept in the project, not installed; add objectives and save to enable them.");
    }

    private void Validate()
    {
        if (Project is null) return;
        Refresh();
        ValidationIssues.Clear();
        foreach (var issue in _validation.Validate(Project, Files)) ValidationIssues.Add(issue);
        ValidationSummary = ValidationIssues.Count == 0 ? "Project structure passed" : $"{ValidationIssues.Count} issues";
        Status = ValidationIssues.Count == 0 ? "XML and project structure checks passed. Final behavior still needs a playtest." : "Validation found issues. Review the list on Overview.";
    }

    private void Export()
    {
        if (Project is null) return;
        var dialog = new OpenFolderDialog { Title = "Choose a parent folder for the project export", Multiselect = false, InitialDirectory = Project.OutputFolder };
        if (dialog.ShowDialog() != true) return;
        Run(() =>
        {
            _manifests.Save(Project);
            var target = Path.Combine(dialog.FolderName, Path.GetFileName(Project.RootPath) + "-export");
            if (Directory.Exists(target)) throw new IOException("The export folder already exists; exports are never overwritten.");
            CopyDirectory(Project.RootPath, target);
            Project.OutputFolder = dialog.FolderName;
            _manifests.Save(Project);
            Status = $"Exported project to {target}.";
        });
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), false);
    }

    private static void Shell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void Run(Action action, string? success = null) { try { action(); if (success is not null) Status = success; } catch (Exception ex) { Status = ex.Message; } }
    private void NotifyProjectChanged() { OnPropertyChanged(nameof(HasOpenProject)); OnPropertyChanged(nameof(WelcomeVisibility)); OnPropertyChanged(nameof(ProjectVisibility)); OnPropertyChanged(nameof(OverviewVisibility)); OnPropertyChanged(nameof(BrowserVisibility)); OnPropertyChanged(nameof(StudioVisibility)); OnPropertyChanged(nameof(EditorVisibility)); OnPropertyChanged(nameof(ProjectPath)); OnPropertyChanged(nameof(GameStatus)); OnPropertyChanged(nameof(OverviewCoverPath)); OnPropertyChanged(nameof(OverviewZones)); OnPropertyChanged(nameof(ProjectDescriptionText)); RaiseCommands(); }
    private void NotifyInventoryChanged() { OnPropertyChanged(nameof(FilteredFiles)); OnPropertyChanged(nameof(FilteredFileCount)); OnPropertyChanged(nameof(RecentFiles)); OnPropertyChanged(nameof(RecentRooms)); OnPropertyChanged(nameof(OverviewZones)); OnPropertyChanged(nameof(NoRoomsVisibility)); OnPropertyChanged(nameof(NoZonesVisibility)); OnPropertyChanged(nameof(SearchResults)); OnPropertyChanged(nameof(SearchHits)); OnPropertyChanged(nameof(NoSearchResultsVisibility)); OnPropertyChanged(nameof(TotalFiles)); OnPropertyChanged(nameof(RoomCount)); OnPropertyChanged(nameof(ZoneCount)); OnPropertyChanged(nameof(ContentTypeCount)); OnPropertyChanged(nameof(AssetCount)); OnPropertyChanged(nameof(ModelCount)); OnPropertyChanged(nameof(TrickCount)); OnPropertyChanged(nameof(AudioCount)); }
    private void RaiseCommands() { foreach (var command in new[] { SaveProjectCommand, RefreshCommand, ImportCommand, CreateItemCommand, DeleteCommand, OpenFileCommand, RevealCommand, OpenFolderCommand, ConnectGameCommand, InstallCommand, ValidateCommand, ExportCommand, SaveEditorCommand, CloseEditorCommand }) command.RaiseCanExecuteChanged(); }
}
