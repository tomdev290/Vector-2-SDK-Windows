using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Vector2LevelEditor.Controls;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;

namespace Vector2LevelEditor.Views;

public sealed class AnimationSelectionWindow : Window
{
    private sealed record Entry(string Name, GameTrickMove? Move, string? AnimationPath, string Pivot = "DetectorH")
    { public override string ToString() => Name; }
    private readonly List<Entry> _entries = [];
    private readonly ListBox _list = new();
    private readonly TrickPreviewControl _preview = new() { CenterOnVisibleModel = true, ClipToBounds = true,
        ShowModelNodePoints = false, ShowModelOutlines = false, ShowDebugEdges = false, PreviewZoom = 4 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    public string SelectedAnimation { get; private set; } = "";

    public AnimationSelectionWindow(bool tricksOnly, string current, string? roomPath)
    {
        Title = tricksOnly ? "Choose a Trick" : "Choose an Animation";
        Width = 760; Height = 540; MinWidth = 580; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Vector2LevelEditor;component/Views/ProjectManager/Studios/StudioTheme.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "ProjectManagerCardBrush");
        var service = new GameTrickPreviewService();
        try { _entries.AddRange(service.LoadMoves().Where(move => !tricksOnly || move.IsTrick).Select(move => new Entry(move.Name, move, null))); }
        catch (Exception error) { _error.Text = error.Message; }
        var directory = string.IsNullOrWhiteSpace(roomPath) ? null : new DirectoryInfo(Path.GetDirectoryName(roomPath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "project.xml"))) directory = directory.Parent;
        var custom = directory is null ? null : Path.Combine(directory.FullName, "custom_tricks");
        if (custom is not null && Directory.Exists(custom))
        foreach (var path in Directory.EnumerateFiles(custom, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var definition = XDocument.Load(path).Root;
                if (definition?.Name.LocalName != "CustomTrick") continue;
                var name = (string?)definition.Attribute("Name") ?? "";
                var fileName = (string?)definition.Attribute("FileName") ?? "";
                if (name.Length > 0 && fileName.Length > 0 && !_entries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    _entries.Insert(0, new Entry(name, null, Services.ProjectManager.ProjectManifestService.SafeCombine(Path.GetDirectoryName(path)!, fileName), (string?)definition.Attribute("PivotNode") ?? "DetectorH"));
            }
            catch (Exception error) { Diagnostics.DiagnosticsLog.Warn("Trick picker skipped " + path + ": " + error.Message); }
        }
        var shell = new Grid { Margin = new Thickness(18) };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 12), ToolTip = "Search animations" }; shell.Children.Add(search);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(_list); Grid.SetColumn(_preview, 1); body.Children.Add(_preview); Grid.SetRow(body, 1); shell.Children.Add(body);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var choose = new Button { Content = "Use " + (tricksOnly ? "Trick" : "Animation"), Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        DockPanel.SetDock(choose, Dock.Right); footer.Children.Add(choose); footer.Children.Add(_error); Grid.SetRow(footer, 2); shell.Children.Add(footer);
        Content = shell;
        search.TextChanged += (_, _) => _list.ItemsSource = _entries.Where(entry => entry.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        _list.SelectionChanged += (_, _) =>
        {
            choose.IsEnabled = _list.SelectedItem is Entry;
            if (_list.SelectedItem is not Entry selected) { _preview.Playback = null; return; }
            try
            {
                _preview.Playback = selected.Move is not null ? service.LoadPlayback(selected.Move)
                    : service.LoadImportedPlayback(selected.AnimationPath!, selected.Name, selected.Pivot);
                _preview.Frame = _preview.Playback.StartFrame; _error.Text = "";
            }
            catch (Exception error) { _preview.Playback = null; _error.Text = error.Message; }
        };
        void Accept() { if (_list.SelectedItem is Entry selected) { SelectedAnimation = selected.Name; DialogResult = true; } }
        choose.Click += (_, _) => Accept(); _list.MouseDoubleClick += (_, _) => Accept();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
        _timer.Tick += (_, _) => { if (_preview.Playback is { Frames.Count: > 0 } playback) _preview.Frame = (_preview.Frame + 1) % playback.Frames.Count; };
        Loaded += (_, _) => { _timer.Start(); search.Focus(); }; Closed += (_, _) => _timer.Stop();
        _list.ItemsSource = _entries; _list.SelectedItem = _entries.FirstOrDefault(entry => entry.Name == current) ?? _entries.FirstOrDefault();
    }
}
