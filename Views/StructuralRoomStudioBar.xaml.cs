using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class StructuralRoomStudioBar : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(LevelDocument), typeof(StructuralRoomStudioBar),
        new PropertyMetadata(null, (owner, _) => ((StructuralRoomStudioBar)owner).Refresh()));

    public LevelDocument? Document
    {
        get => (LevelDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public StructuralRoomStudioBar() => InitializeComponent();

    private void Refresh()
    {
        if (RoomName is null || Document is null) return;
        RoomName.DataContext = Document;
        TitleText.Text = Document.StructuralRoomKind + " Designer";
        var projectRoot = StructuralRoomService.CurrentProjectRoot();
        var zones = StructuralRoomService.LoadZones(projectRoot);
        ZonePicker.ItemsSource = zones;
        ZonePicker.SelectedItem = zones.FirstOrDefault(zone =>
            Document.SourcePath?.Contains(zone.RoomsPath, StringComparison.OrdinalIgnoreCase) == true)
            ?? zones.FirstOrDefault();
        SaveButton.IsEnabled = zones.Count > 0;
        var issues = StructuralRoomService.Validate(Document);
        StatusText.Text = projectRoot.Length == 0 ? "Open a project in Project Manager to save this room to a zone." :
            issues.Count > 0 ? issues[0] : "Room ready to save in the selected zone.";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.StructuralRoomKind is not { } kind) return;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.NewStructuralRoom(kind);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.StructuralRoomKind is not { } kind) return;
        var root = StructuralRoomService.CurrentProjectRoot();
        var dialog = new OpenFileDialog { Filter = "Vector 2 XML (*.xml)|*.xml",
            InitialDirectory = root.Length > 0 ? Path.Combine(root, "custom_rooms") : null };
        if (dialog.ShowDialog() != true) return;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.OpenStructuralRoom(dialog.FileName, kind);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || ZonePicker.SelectedItem is not StructuralZone zone) return;
        try
        {
            var root = StructuralRoomService.CurrentProjectRoot();
            if (root.Length == 0) throw new DirectoryNotFoundException("Open a project in Project Manager first.");
            var path = StructuralRoomService.SaveToZone(Document, root, zone);
            SetStatus($"Saved {Path.GetFileName(path)} to project. Install Changes in Project Manager when the game is ready.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.StatusText = message;
    }
}
