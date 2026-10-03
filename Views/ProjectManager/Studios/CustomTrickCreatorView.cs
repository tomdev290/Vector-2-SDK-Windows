using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class CustomTrickCreatorView : UserControl
{
    private readonly string _root;
    private readonly Action<string> _status;
    private readonly TrickPreviewControl _preview = new() { ShowDebugEdges = false };
    private readonly Slider _frame = new() { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock _frameLabel = new();
    private readonly TextBlock _fileLabel = new() { Text = "No animation imported" };
    private readonly TextBlock _compatibility = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11,
        Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _previewPrompt = new() { Text = "Import an animation to preview it",
        Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBox _name = StudioUi.Field();
    private readonly TextBox _display = StudioUi.Field();
    private readonly TextBox _description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 66,
        Padding = new Thickness(8, 5, 8, 5) };
    private readonly TextBox _entry = StudioUi.Field("0");
    private readonly TextBox _safeStart = StudioUi.Field("0");
    private readonly TextBox _safeEnd = StudioUi.Field("0");
    private readonly TextBox _exitFrame = StudioUi.Field("1");
    private readonly TextBox _exitName = StudioUi.Field("RunForward");
    private readonly ComboBox _finish = new() { ItemsSource = new[] { "Regular", "Chain" }, SelectedIndex = 0 };
    private readonly StackPanel _chainOptions = new();
    private readonly TextBox _price = StudioUi.Field("1100");
    private readonly ComboBox _pivot = new() { ItemsSource = new[] { "DetectorH", "DetectorV", "COM" }, SelectedIndex = 0 };
    private readonly ComboBox _rarity = new() { ItemsSource = new[] { "Common", "Rare", "Epic" }, SelectedIndex = 0 };
    private readonly ComboBox _image = new();
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "New Animation", "Override Animation" }, SelectedIndex = 0 };
    private readonly ComboBox _target = new();
    private readonly StackPanel _newOptions = new();
    private readonly StackPanel _overrideOptions = new();
    private readonly Button _saveButton;
    private readonly Button _playButton;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private string? _animation;
    private int _frames;
    private bool _playing;
    private readonly TextBlock _message = new() { Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(16, 8, 16, 8) };

    public CustomTrickCreatorView(string root, Action<string> status, Action close)
    {
        _root = root;
        _status = status;
        var outer = new DockPanel { Background = StudioUi.Resource("ProjectManagerCardBrush") };
        outer.Resources.MergedDictionaries.Add(new ResourceDictionary {
            Source = new Uri("/Vector2LevelEditor;component/Views/ProjectManager/Studios/StudioTheme.xaml", UriKind.Relative) });
        var heading = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        var back = StudioUi.Button("Back to Tricks", (_, _) => close());
        DockPanel.SetDock(back, Dock.Right);
        heading.Children.Add(back);
        var import = StudioUi.Button("Import .bytes", (_, _) => Import(), true);
        DockPanel.SetDock(import, Dock.Right);
        heading.Children.Add(import);
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Custom Animations", FontSize = 20, FontWeight = FontWeights.SemiBold });
        title.Children.Add(_fileLabel);
        heading.Children.Add(title);
        DockPanel.SetDock(heading, Dock.Top);
        outer.Children.Add(heading);
        DockPanel.SetDock(_message, Dock.Bottom);
        outer.Children.Add(_message);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
        var stage = new DockPanel { Margin = new Thickness(16) };
        _preview.MinHeight = 340;
        var previewSurface = new Grid();
        previewSurface.Children.Add(_preview);
        previewSurface.Children.Add(_previewPrompt);
        stage.Children.Add(previewSurface);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(transport, Dock.Bottom);
        _playButton = StudioUi.Button("\uE768", (sender, _) => {
            _playing = !_playing;
            if (sender is Button button) button.Content = _playing ? "\uE769" : "\uE768";
        });
        _playButton.FontFamily = new FontFamily("Segoe MDL2 Assets"); _playButton.ToolTip = "Play or pause";
        transport.Children.Add(_playButton);
        var previous = StudioUi.Button("\uE892", (_, _) => _frame.Value = Math.Max(0, _frame.Value - 1));
        previous.FontFamily = new FontFamily("Segoe MDL2 Assets"); previous.ToolTip = "Previous frame";
        transport.Children.Add(previous);
        _frame.Width = 165;
        _frame.ValueChanged += (_, _) => { _preview.Frame = (int)_frame.Value; _frameLabel.Text = $"{(int)_frame.Value} / {Math.Max(0, _frames - 1)}"; };
        transport.Children.Add(_frame);
        var next = StudioUi.Button("\uE893", (_, _) => _frame.Value = Math.Min(_frame.Maximum, _frame.Value + 1));
        next.FontFamily = new FontFamily("Segoe MDL2 Assets"); next.ToolTip = "Next frame";
        transport.Children.Add(next);
        transport.Children.Add(_frameLabel);
        stage.Children.Insert(0, transport);
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        DockPanel.SetDock(zoomRow, Dock.Bottom);
        zoomRow.Children.Add(new TextBlock { Text = "Zoom", FontSize = 11, Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        var zoom = new Slider { Minimum = .4, Maximum = 10, Value = 3.5, Width = 170, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        zoom.ValueChanged += (_, _) => _preview.PreviewZoom = zoom.Value;
        zoomRow.Children.Add(zoom);
        zoomRow.Children.Add(StudioUi.Button("Reset view", (_, _) => { _preview.ResetView(); zoom.Value = 3.5; }));
        stage.Children.Insert(1, zoomRow);
        layout.Children.Add(stage);
        var setup = new StackPanel { Margin = new Thickness(12, 4, 16, 20) };
        setup.Children.Add(StudioUi.Heading("Animation Setup", "\uE768", Color.FromRgb(51, 127, 220)));
        setup.Children.Add(StudioUi.Label("Install as"));
        setup.Children.Add(StudioUi.Segments(_mode));
        _newOptions.Children.Add(StudioUi.Heading("Animation", "\uE8D4", Color.FromRgb(51, 127, 220)));
        Add(_newOptions, "Animation name", _name);
        Add(_newOptions, "Pivot", _pivot);
        _newOptions.Children.Add(StudioUi.Heading("Shop Card", "\uE7BF", Color.FromRgb(51, 166, 92)));
        Add(_newOptions, "Display name", _display);
        Add(_newOptions, "Description", _description);
        _image.Items.Add("Default trick image");
        _image.SelectedIndex = 0;
        var textureFolder = Path.Combine(root, "custom_textures");
        if (Directory.Exists(textureFolder))
            foreach (var file in Directory.EnumerateFiles(textureFolder).Where(file => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(Path.GetExtension(file).ToLowerInvariant())))
                _image.Items.Add(Path.GetFileName(file));
        Add(_newOptions, "Card image", _image);
        Add(_newOptions, "Rarity", _rarity);
        Add(_newOptions, "Shop price", _price);
        _price.IsEnabled = Vector2LevelEditor.Models.ProjectManager.ProjectSection.ShopEditingAvailable;
        _rarity.IsEnabled = Vector2LevelEditor.Models.ProjectManager.ProjectSection.ShopEditingAvailable;
        _price.ToolTip = _rarity.ToolTip = "Shop editing is unavailable in this release.";
        _newOptions.Children.Add(StudioUi.Heading("Timeline", "\uE787", Color.FromRgb(51, 127, 220)));
        Add(_newOptions, "Entry frame", _entry);
        Add(_newOptions, "Safe start", _safeStart);
        Add(_newOptions, "Safe end", _safeEnd);
        _newOptions.Children.Add(StudioUi.Label("Finish"));
        _newOptions.Children.Add(StudioUi.Segments(_finish));
        Add(_chainOptions, "End animation", _exitName);
        Add(_chainOptions, "Start next at frame", _exitFrame);
        _newOptions.Children.Add(_chainOptions);
        try
        {
            foreach (var move in new GameTrickPreviewService().LoadMoves()) _target.Items.Add(move);
        }
        catch (Exception ex) { _message.Text = $"Built-in animations unavailable: {ex.Message}"; }
        Add(_overrideOptions, "Override target", _target);
        _overrideOptions.Children.Add(_compatibility);
        _overrideOptions.Children.Add(StudioUi.Button("Restore Default", (_, _) => RestoreDefault()));
        setup.Children.Add(_newOptions);
        setup.Children.Add(_overrideOptions);
        _pivot.SelectionChanged += (_, _) => RefreshPreview();
        _target.SelectionChanged += (_, _) => { RefreshPreview(); UpdateMode(); };
        _finish.SelectionChanged += (_, _) => UpdateMode();
        _mode.SelectionChanged += (_, _) => { UpdateMode(); RefreshPreview(); };
        _saveButton = StudioUi.Button("Create Custom Animation", (_, _) => Save(), true);
        setup.Children.Add(_saveButton);
        var inspector = new ScrollViewer { Content = setup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(inspector, 1);
        layout.Children.Add(inspector);
        outer.Children.Add(layout);
        Content = outer;
        _timer.Tick += (_, _) => { if (_playing && _frames > 0) _frame.Value = _frame.Value >= _frame.Maximum ? 0 : _frame.Value + 1; };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        UpdateMode();
    }

    private static void Add(Panel panel, string label, Control control)
    {
        panel.Children.Add(StudioUi.Label(label));
        control.Margin = new Thickness(0, 4, 0, 12);
        panel.Children.Add(control);
    }

    private void UpdateMode()
    {
        var isOverride = _mode.SelectedIndex == 1;
        _newOptions.Visibility = isOverride ? Visibility.Collapsed : Visibility.Visible;
        _overrideOptions.Visibility = isOverride ? Visibility.Visible : Visibility.Collapsed;
        _chainOptions.Visibility = !isOverride && _finish.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _saveButton.Content = isOverride ? "Install Override" : "Create Custom Animation";
        _saveButton.IsEnabled = _animation is not null && (!isOverride || _target.SelectedItem is GameTrickMove move && _frames > Math.Max(move.FirstFrame, move.EndFrame));
        _compatibility.Text = _target.SelectedItem is GameTrickMove selected
            ? (_frames > Math.Max(selected.FirstFrame, selected.EndFrame)
                ? $"Compatible: {_frames} frames; {selected.Name} needs at least {Math.Max(selected.FirstFrame, selected.EndFrame) + 1}."
                : $"Needs at least {Math.Max(selected.FirstFrame, selected.EndFrame) + 1} frames; imported file has {_frames}.")
            : "Choose a move from the game's animation library.";
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "Import Vector animation", Filter = "Vector animation (*.bytes)|*.bytes" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _frames = GameTrickPreviewService.ValidateImportedFrames(dialog.FileName);
            _previewPrompt.Visibility = Visibility.Collapsed;
            _animation = dialog.FileName;
            _fileLabel.Text = $"{Path.GetFileName(_animation)}   {_frames} frames";
            _name.Text = Path.GetFileNameWithoutExtension(_animation).Replace(" ", "");
            _display.Text = Path.GetFileNameWithoutExtension(_animation);
            _description.Text = "Custom trick: " + _display.Text;
            _safeEnd.Text = Math.Max(0, _frames - 2).ToString();
            _frame.Maximum = _frames - 1;
            _frame.Value = 0;
            _frameLabel.Text = $"0 / {_frames - 1}";
            _playing = false;
            _playButton.Content = "\uE768";
            RefreshPreview();
            UpdateMode();
            _message.Text = $"Valid Vector animation: {_frames} frames and 46 nodes per frame.";
        }
        catch (Exception ex) { _animation = null; _frames = 0; _preview.Playback = null; _previewPrompt.Visibility = Visibility.Visible; _message.Text = $"Import failed: {ex.Message}"; UpdateMode(); }
    }

    private void RefreshPreview()
    {
        if (_animation is null) return;
        try
        {
            var pivot = _mode.SelectedIndex == 1 && _target.SelectedItem is GameTrickMove target
                ? target.PivotNode : _pivot.SelectedItem?.ToString() ?? "DetectorH";
            _preview.Playback = new GameTrickPreviewService().LoadImportedPlayback(_animation, _name.Text, pivot);
        }
        catch (Exception ex) { _preview.Playback = null; _message.Text = $"Preview unavailable: {ex.Message}"; }
    }

    private void Save()
    {
        if (_animation is null) { _message.Text = "Import a .bytes animation first."; return; }
        try
        {
            string path;
            if (_mode.SelectedIndex == 1)
            {
                if (_target.SelectedItem is not GameTrickMove move) throw new InvalidDataException("Choose an animation to override.");
                path = CustomTrickPackageService.SaveOverride(Path.Combine(_root, "custom_tricks"), move.Name, _animation, Math.Max(move.FirstFrame, move.EndFrame) + 1);
            }
            else
            {
                int Parse(TextBox field, string label) => int.TryParse(field.Text, out var value) ? value : throw new InvalidDataException($"{label} must be a whole number.");
                var draft = new CustomTrickDraft(_name.Text, _display.Text, _description.Text, _animation,
                    _pivot.SelectedItem?.ToString() ?? "DetectorH", Parse(_entry, "Entry"), Parse(_safeStart, "Safe start"),
                    Parse(_safeEnd, "Safe end"), _finish.SelectedIndex == 1 ? _exitName.Text : "RunForward",
                    _finish.SelectedIndex == 1 ? Parse(_exitFrame, "Exit frame") : 1,
                    _image.SelectedIndex <= 0 ? "" : _image.SelectedItem?.ToString() ?? "",
                    _rarity.SelectedIndex + 1, Parse(_price, "Price"));
                path = CustomTrickPackageService.SaveNew(Path.Combine(_root, "custom_tricks"), draft);
            }
            _message.Text = $"Saved {Path.GetFileName(Path.GetDirectoryName(path))}.";
            _status(_message.Text);
        }
        catch (Exception ex) { _message.Text = $"Save failed: {ex.Message}"; }
    }

    private void RestoreDefault()
    {
        if (_target.SelectedItem is not GameTrickMove move) { _message.Text = "Choose an animation target first."; return; }
        if (MessageBox.Show($"Restore the default {move.Name} animation?", "Restore Default",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var removed = CustomTrickPackageService.RestoreDefault(Path.Combine(_root, "custom_tricks"), move.Name);
            _message.Text = removed ? $"Restored {move.Name} to default." : $"{move.Name} has no installed override.";
            _status(_message.Text);
        }
        catch (Exception ex) { _message.Text = $"Restore failed: {ex.Message}"; }
    }
}
