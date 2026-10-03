using System.Windows;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isPlayerStudioOpen;
    private bool _isPlayerStudioVisible;
    private LevelDocument? _playerStudioTarget;

    public RelayCommand OpenPlayerStudioCommand { get; private set; } = null!;
    public RelayCommand SelectPlayerStudioCommand { get; private set; } = null!;
    public RelayCommand ClosePlayerStudioCommand { get; private set; } = null!;
    public LevelDocument? PlayerStudioTarget
    {
        get => _playerStudioTarget;
        private set => Set(ref _playerStudioTarget, value);
    }
    public bool IsPlayerStudioVisible
    {
        get => _isPlayerStudioVisible;
        private set
        {
            if (!Set(ref _isPlayerStudioVisible, value)) return;
            OnPropertyChanged(nameof(PlayerStudioVisibility));
            OnPropertyChanged(nameof(EditorWorkspaceVisibility));
        }
    }
    public Visibility PlayerStudioVisibility => IsPlayerStudioVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlayerStudioTabVisibility => _isPlayerStudioOpen ? Visibility.Visible : Visibility.Collapsed;

    private void InitializePlayerStudioCommands()
    {
        OpenPlayerStudioCommand = new RelayCommand(() =>
        {
            PlayerStudioTarget = SelectedDocument;
            _isPlayerStudioOpen = true;
            OnPropertyChanged(nameof(PlayerStudioTabVisibility));
            IsProjectManagerVisible = false;
            IsAIStudioVisible = false;
            IsPlayerStudioVisible = true;
            StatusText = $"Player Designer opened for {PlayerStudioTarget.Name}.";
        });
        SelectPlayerStudioCommand = new RelayCommand(() =>
        {
            if (!_isPlayerStudioOpen) return;
            IsProjectManagerVisible = false;
            IsAIStudioVisible = false;
            IsPlayerStudioVisible = true;
        });
        ClosePlayerStudioCommand = new RelayCommand(() =>
        {
            IsPlayerStudioVisible = false;
            _isPlayerStudioOpen = false;
            PlayerStudioTarget = null;
            OnPropertyChanged(nameof(PlayerStudioTabVisibility));
        });
    }
}
