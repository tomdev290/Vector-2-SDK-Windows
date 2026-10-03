using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class PlayerStudioView : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(LevelDocument), typeof(PlayerStudioView),
        new PropertyMetadata(null, (owner, _) => ((PlayerStudioView)owner).Refresh()));

    private readonly GameTrickPreviewService _previewService = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IReadOnlyList<GameTrickModelSkin> _skins = [];
    private IReadOnlyList<CustomModelItem> _models = [];
    private string _modelDirectory = "";
    private string _pendingModelPath = "";

    public LevelDocument? Document
    {
        get => (LevelDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public PlayerStudioView()
    {
        InitializeComponent();
        PreviewAnimation.SelectedIndex = 0;
        ModelCategory.SelectedIndex = 6;
        _timer.Tick += (_, _) =>
        {
            if (Preview.Playback is { Frames.Count: > 0 } playback)
                Preview.Frame = (Preview.Frame + 1) % playback.Frames.Count;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else _timer.Stop();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        if (BuiltInLayers is null) return;
        _modelDirectory = CustomModelCatalogService.DefaultDirectory();
        ModelDirectoryLabel.Text = _modelDirectory.Length == 0
            ? "Open a project or choose its custom_models folder."
            : _modelDirectory;
        ImportModelButton.IsEnabled = _modelDirectory.Length > 0;
        try { _skins = _previewService.LoadModelSkins(); }
        catch { _skins = []; }
        _models = CustomModelCatalogService.Load(_modelDirectory);
        RefreshLayers();
        RebuildPreview();
    }

    private void RefreshLayers()
    {
        BuiltInLayers.Children.Clear();
        ImportedLayers.Children.Clear();
        ClearModelsButton.Visibility = _models.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Document is null) return;
        if (_skins.Count == 0)
            BuiltInLayers.Children.Add(new TextBlock { Text = "Game model data is unavailable.", Foreground = Brushes.Gray });
        foreach (var skin in _skins.Where(skin => skin.Filename != "0.xml"))
        {
            var checkbox = new CheckBox
            {
                Content = skin.Filename == "1.xml" ? "Default Body" : skin.DisplayName,
                IsChecked = Document.PlayerSkinFiles.Contains(skin.Filename),
                Margin = new Thickness(0, 3, 0, 3)
            };
            checkbox.Click += (_, _) => ToggleSkin(skin.Filename, checkbox.IsChecked == true);
            BuiltInLayers.Children.Add(checkbox);
        }
        if (_models.Count == 0)
            ImportedLayers.Children.Add(new TextBlock { Text = "No imported models installed.", Foreground = Brushes.Gray });
        foreach (var model in _models)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 5) };
            var remove = new Button { Content = "&#xE74D;", FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 28,
                ToolTip = $"Remove {model.Name}" };
            remove.Click += (_, _) => RemoveModel(model);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var checkbox = new CheckBox
            {
                Content = $"{model.Name}\n{model.Category} · {model.Author}",
                IsChecked = Document.PlayerSkinFiles.Contains(model.Reference),
                VerticalAlignment = VerticalAlignment.Center
            };
            checkbox.Click += (_, _) => ToggleSkin(model.Reference, checkbox.IsChecked == true);
            row.Children.Add(checkbox);
            ImportedLayers.Children.Add(row);
        }
    }

    private void ToggleSkin(string reference, bool enabled)
    {
        if (Document is null) return;
        if (enabled && !Document.PlayerSkinFiles.Contains(reference))
            Document.PlayerSkinFiles.Add(reference);
        else if (!enabled)
            Document.PlayerSkinFiles.Remove(reference);
        SetStatus($"Updated player model for {Document.Name}.");
        RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (Document is null)
        {
            Preview.Playback = null;
            PreviewMessage.Text = "";
            return;
        }
        try
        {
            var moves = _previewService.LoadMoves();
            var selected = PreviewAnimation.SelectedIndex;
            var move = selected switch
            {
                1 => moves.FirstOrDefault(item => item.Name.Equals("RunForward", StringComparison.OrdinalIgnoreCase)),
                2 => moves.FirstOrDefault(item => item.Name.Equals("MonkeyVault", StringComparison.OrdinalIgnoreCase)),
                _ => moves.FirstOrDefault(item => item.FileName.Contains("cs_swarm_idle", StringComparison.OrdinalIgnoreCase))
            };
            move ??= moves.FirstOrDefault() ?? throw new InvalidDataException("No preview animation found.");
            var skins = Document.PlayerSkinFiles.Count == 0
                ? new[] { "0.xml", "1.xml" }
                : new[] { "0.xml" }.Concat(Document.PlayerSkinFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Preview.Playback = _previewService.LoadPlayback(move, skins, customModelsDirectory: _modelDirectory);
            Preview.Frame = Preview.Playback.StartFrame;
            PreviewMessage.Text = "";
        }
        catch (Exception ex)
        {
            Preview.Playback = null;
            PreviewMessage.Text = ex.Message;
        }
    }

    private void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        if (_modelDirectory.Length == 0)
        {
            SetStatus("Open a project or choose a custom_models folder first.");
            return;
        }
        var dialog = new OpenFileDialog { Filter = "Vector model XML (*.xml)|*.xml" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var xml = XDocument.Load(dialog.FileName);
            if (xml.Root?.Name != "Scene" || xml.Root.Element("Nodes")?.Elements().Any() != true)
                throw new InvalidDataException("Model XML must contain a Scene with nodes.");
            _pendingModelPath = dialog.FileName;
            ModelName.Text = Path.GetFileNameWithoutExtension(dialog.FileName).Replace('_', ' ');
            PendingImport.Visibility = Visibility.Visible;
            SetStatus($"Ready to install {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void InstallModel_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingModelPath.Length == 0 || _modelDirectory.Length == 0) return;
        try
        {
            var model = CustomModelCatalogService.Import(_modelDirectory, _pendingModelPath,
                ModelName.Text, ((ComboBoxItem)ModelCategory.SelectedItem).Content.ToString() ?? "Accessory", ModelAuthor.Text);
            if (Document is not null && !Document.PlayerSkinFiles.Contains(model.Reference))
                Document.PlayerSkinFiles.Add(model.Reference);
            PendingImport.Visibility = Visibility.Collapsed;
            _pendingModelPath = "";
            _models = CustomModelCatalogService.Load(_modelDirectory);
            RefreshLayers();
            RebuildPreview();
            SetStatus($"Installed {model.Name}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the project's custom_models folder",
            InitialDirectory = _modelDirectory.Length == 0 ? null : _modelDirectory
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            CustomModelCatalogService.RememberDirectory(dialog.FolderName);
            Refresh();
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void RemoveModel(CustomModelItem model)
    {
        if (MessageBox.Show($"Remove {model.Name} from custom_models?", "Remove model",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var root = Path.GetFullPath(_modelDirectory).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var package = Path.GetFullPath(model.PackagePath);
            if (!package.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Model package is outside custom_models.");
            Directory.Delete(package, true);
            Document?.PlayerSkinFiles.Remove(model.Reference);
            _models = CustomModelCatalogService.Load(_modelDirectory);
            RefreshLayers();
            RebuildPreview();
            SetStatus($"Removed {model.Name}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void ClearModels_Click(object sender, RoutedEventArgs e)
    {
        if (_models.Count == 0 || MessageBox.Show("Remove every imported model package?",
                "Clear imported models", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var model in _models.ToList())
        {
            try
            {
                var root = Path.GetFullPath(_modelDirectory).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var package = Path.GetFullPath(model.PackagePath);
                if (!package.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model package is outside custom_models.");
                Directory.Delete(package, true);
                Document?.PlayerSkinFiles.Remove(model.Reference);
            }
            catch (Exception ex) { SetStatus($"Could not remove {model.Name}: {ex.Message}"); }
        }
        _models = CustomModelCatalogService.Load(_modelDirectory);
        RefreshLayers();
        RebuildPreview();
    }

    private void RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        Document?.PlayerSkinFiles.Clear();
        RefreshLayers();
        RebuildPreview();
        SetStatus("Restored the default equipped appearance.");
    }

    private void PreviewAnimation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Preview is not null) RebuildPreview();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Preview is not null) Preview.PreviewZoom = e.NewValue;
    }

    private void ResetPreview_Click(object sender, RoutedEventArgs e)
    {
        Preview.ResetView();
        ZoomSlider.Value = Preview.PreviewZoom;
    }

    private void SaveRoom_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var dialog = new SaveFileDialog { Filter = "Vector 2 XML (*.xml)|*.xml", FileName = Document.Name,
            InitialDirectory = Document.SourcePath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, new Exporter().Export(Document));
            Document.SourcePath = dialog.FileName;
            Document.Name = Path.GetFileName(dialog.FileName);
            SetStatus($"Exported {Document.Name}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void SetStatus(string message)
    {
        Status.Text = message;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.StatusText = message;
    }
}
