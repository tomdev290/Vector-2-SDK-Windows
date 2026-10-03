using System.Windows;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.ViewModels;

public sealed partial class MainWindowViewModel
{
    public RelayCommand OpenEntranceStudioCommand { get; private set; } = null!;
    public RelayCommand OpenExitStudioCommand { get; private set; } = null!;
    public Visibility StructuralRoomBarVisibility =>
        SelectedDocument.StructuralRoomKind is not null && CenterTab == "Canvas"
            ? Visibility.Visible : Visibility.Collapsed;

    private void InitializeStructuralStudioCommands()
    {
        OpenEntranceStudioCommand = new RelayCommand(() => OpenStructuralStudio(StructuralRoomKind.Entrance));
        OpenExitStudioCommand = new RelayCommand(() => OpenStructuralStudio(StructuralRoomKind.Exit));
    }

    public void OpenStructuralStudio(StructuralRoomKind kind)
    {
        var existing = Documents.FirstOrDefault(document =>
            document.StructuralRoomKind == kind && string.IsNullOrWhiteSpace(document.SourcePath));
        if (existing is null)
        {
            existing = StructuralRoomService.Create(kind);
            Documents.Add(existing);
        }
        SelectedDocument = existing;
        IsProjectManagerVisible = false;
        CenterTab = "Canvas";
        OnPropertyChanged(nameof(StructuralRoomBarVisibility));
        StatusText = $"{kind} Designer opened.";
    }

    public void NewStructuralRoom(StructuralRoomKind kind)
    {
        var document = StructuralRoomService.Create(kind);
        Documents.Add(document);
        SelectedDocument = document;
        CenterTab = "Canvas";
        OnPropertyChanged(nameof(StructuralRoomBarVisibility));
        StatusText = $"New {kind.ToString().ToLowerInvariant()} room.";
    }

    public void OpenStructuralRoom(string path, StructuralRoomKind kind)
    {
        try
        {
            var document = _parser.Load(path);
            document.StructuralRoomKind ??= kind;
            Documents.Add(document);
            SelectedDocument = document;
            CenterTab = "Canvas";
            OnPropertyChanged(nameof(StructuralRoomBarVisibility));
            StatusText = $"Opened {document.Name}.";
        }
        catch (Exception ex)
        {
            global::Vector2LevelEditor.Diagnostics.DiagnosticsLog.Error($"Structural room import failed: {ex.Message}");
            StatusText = "Could not open the room. Check Console.";
        }
    }
}
