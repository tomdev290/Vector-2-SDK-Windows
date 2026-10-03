using System.Windows;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isAIStudioOpen;
    private bool _isAIStudioVisible;
    private LevelDocument? _aiStudioTarget;

    public RelayCommand OpenAIStudioCommand { get; private set; } = null!;
    public RelayCommand SelectAIStudioCommand { get; private set; } = null!;
    public RelayCommand CloseAIStudioCommand { get; private set; } = null!;

    public LevelDocument? AIStudioTarget
    {
        get => _aiStudioTarget;
        private set => Set(ref _aiStudioTarget, value);
    }
    public bool IsAIStudioVisible
    {
        get => _isAIStudioVisible;
        private set
        {
            if (!Set(ref _isAIStudioVisible, value)) return;
            OnPropertyChanged(nameof(AIStudioVisibility));
            OnPropertyChanged(nameof(EditorWorkspaceVisibility));
        }
    }
    public Visibility AIStudioVisibility => IsAIStudioVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AIStudioTabVisibility => _isAIStudioOpen ? Visibility.Visible : Visibility.Collapsed;

    private void InitializeAIStudioCommands()
    {
        OpenAIStudioCommand = new RelayCommand(() =>
        {
            AIStudioTarget = SelectedDocument;
            _isAIStudioOpen = true;
            OnPropertyChanged(nameof(AIStudioTabVisibility));
            IsProjectManagerVisible = false;
            IsPlayerStudioVisible = false;
            IsAIStudioVisible = true;
            StatusText = $"AI Designer opened for {AIStudioTarget.Name}.";
        });
        SelectAIStudioCommand = new RelayCommand(() =>
        {
            if (!_isAIStudioOpen) return;
            IsProjectManagerVisible = false;
            IsPlayerStudioVisible = false;
            IsAIStudioVisible = true;
        });
        CloseAIStudioCommand = new RelayCommand(() =>
        {
            IsAIStudioVisible = false;
            _isAIStudioOpen = false;
            AIStudioTarget = null;
            OnPropertyChanged(nameof(AIStudioTabVisibility));
        });
    }
}
