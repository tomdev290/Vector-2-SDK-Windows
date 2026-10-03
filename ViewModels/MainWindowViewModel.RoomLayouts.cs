using System.Windows;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isRoomLayoutsOpen;
    public RelayCommand OpenRoomLayoutsCommand { get; private set; } = null!;
    public RelayCommand CloseRoomLayoutsCommand { get; private set; } = null!;
    public Visibility RoomLayoutsVisibility =>
        _isRoomLayoutsOpen && SelectedDocument.StructuralRoomKind is null && CenterTab == "Canvas"
            ? Visibility.Visible : Visibility.Collapsed;

    private void InitializeRoomLayoutCommands()
    {
        OpenRoomLayoutsCommand = new RelayCommand(() =>
        {
            IsProjectManagerVisible = false;
            IsAIStudioVisible = false;
            IsPlayerStudioVisible = false;
            CenterTab = "Canvas";
            _isRoomLayoutsOpen = true;
            OnPropertyChanged(nameof(RoomLayoutsVisibility));
            StatusText = "Room Layouts opened for " + SelectedDocument.Name + ".";
        });
        CloseRoomLayoutsCommand = new RelayCommand(() =>
        {
            _isRoomLayoutsOpen = false;
            OnPropertyChanged(nameof(RoomLayoutsVisibility));
        });
    }
}
