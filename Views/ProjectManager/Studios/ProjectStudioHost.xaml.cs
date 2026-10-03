using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Vector2LevelEditor.ViewModels.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public partial class ProjectStudioHost : UserControl
{
    private ProjectManagerViewModel? _viewModel;
    private bool _rebuilding;
    private readonly Dictionary<string, UserControl> _studios = new(StringComparer.OrdinalIgnoreCase);
    private string? _projectRoot;
    private int _inventoryRevision = -1;

    public ProjectStudioHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) Rebuild(); };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel = e.NewValue as ProjectManagerViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnPropertyChanged;
        Rebuild();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectManagerViewModel.SelectedSection) or nameof(ProjectManagerViewModel.Project) or nameof(ProjectManagerViewModel.InventoryRevision))
            Rebuild();
    }

    private void Rebuild()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
        if (_viewModel?.Project is null)
        {
            StudioContent.Content = null;
            _studios.Clear();
            return;
        }
        if (!IsVisible) return;
        var root = _viewModel.Project.RootPath;
        if (_projectRoot != root || _inventoryRevision != _viewModel.InventoryRevision)
        {
            _studios.Clear();
            _projectRoot = root;
            _inventoryRevision = _viewModel.InventoryRevision;
        }
        var section = _viewModel.SelectedSection.Name;
        if (!ProjectStudioRouteFactory.SupportedSections.Contains(section))
        {
            StudioContent.Content = null;
            return;
        }
        if (!_studios.TryGetValue(section, out var studio))
        {
        studio = ProjectStudioRouteFactory.Create(
            section, root, _viewModel.Project.GameDataPath,
            path => _viewModel.OpenProjectRoomFromStudio(path), SetStatus,
            name => _viewModel.SelectedSection = Models.ProjectManager.ProjectSection.All.Single(section => section.Name == name));
        _studios[section] = studio;
        }
        StudioContent.Content = studio;
        }
        finally { _rebuilding = false; }
    }

    private void SetStatus(string value)
    {
        if (_viewModel is null) return;
        _viewModel.StudioChanged(value);
    }

    public void OpenCustomAnimations()
    {
        if (_viewModel?.Project is null) return;
        _viewModel.SelectedSection = Models.ProjectManager.ProjectSection.All.Single(section => section.Name == "Tricks");
        Rebuild();
        if (StudioContent.Content is TrickLibraryStudioView tricks) tricks.OpenCreator();
    }
}
