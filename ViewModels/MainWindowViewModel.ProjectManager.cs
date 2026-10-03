namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    public event Action<string>? RoomSaved;
    public RelayCommand OpenProjectManagerCommand { get; private set; } = null!;
    public RelayCommand CloseProjectManagerCommand { get; private set; } = null!;
    private bool _isProjectManagerVisible;
    private bool _isProjectManagerOpen;
    public bool IsProjectManagerVisible
    {
        get => _isProjectManagerVisible;
        private set
        {
            if (!Set(ref _isProjectManagerVisible, value)) return;
            OnPropertyChanged(nameof(ProjectManagerVisibility));
            OnPropertyChanged(nameof(ProjectManagerTabVisibility));
            OnPropertyChanged(nameof(EditorWorkspaceVisibility));
        }
    }
    public System.Windows.Visibility ProjectManagerVisibility => IsProjectManagerVisible
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility EditorWorkspaceVisibility => IsProjectManagerVisible || IsAIStudioVisible || IsPlayerStudioVisible
        ? System.Windows.Visibility.Collapsed
        : System.Windows.Visibility.Visible;
    public System.Windows.Visibility ProjectManagerTabVisibility => IsProjectManagerOpen
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    private void InitializeProjectManagerCommands()
    {
        OpenProjectManagerCommand = new RelayCommand(() =>
        {
            IsAIStudioVisible = false;
            IsPlayerStudioVisible = false;
            IsProjectManagerOpen = true;
            IsProjectManagerVisible = true;
        });
        CloseProjectManagerCommand = new RelayCommand(() =>
        {
            IsProjectManagerVisible = false;
            IsProjectManagerOpen = false;
        });
    }

    public bool IsProjectManagerOpen
    {
        get => _isProjectManagerOpen;
        private set
        {
            if (!Set(ref _isProjectManagerOpen, value)) return;
            OnPropertyChanged(nameof(ProjectManagerTabVisibility));
        }
    }

    public void OpenProjectRoom(string path)
    {
        try
        {
            var document = _parser.Load(path);
            Documents.Add(document);
            SelectedDocument = document;
            IsProjectManagerVisible = false;
            StatusText = $"Opened {document.Name} from the project.";
        }
        catch (Exception ex)
        {
            global::Vector2LevelEditor.Diagnostics.DiagnosticsLog.Error($"Project room import failed: {ex.Message}");
            StatusText = "Could not open the project room. Check Console.";
        }
    }
}
