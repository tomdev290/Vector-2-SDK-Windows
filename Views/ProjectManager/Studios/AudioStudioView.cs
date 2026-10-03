using System.Xml.Linq;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class AudioStudioView : UserControl
{
    private readonly string _root; private readonly Action<string> _status;
    private readonly ListBox _pools = new(); private readonly ComboBox _scope = new() { ItemsSource = new[] { "Chapter", "Zone" }, SelectedIndex = 0 };
    private readonly TextBox _reference = StudioUi.Field(); private readonly ComboBox _ambient = new();
    private readonly TextBox _volume = StudioUi.Field("0.5");
    private readonly List<string> _audioIds = [];
    private readonly DataGridComboBoxColumn _trackChoice = new() { Header = "Track", Width = new DataGridLength(2, DataGridLengthUnitType.Star), MinWidth = 120, SelectedItemBinding = new System.Windows.Data.Binding(nameof(AudioTrackRow.Id)) };
    private readonly ObservableCollection<AudioTrackRow> _trackRows = [];
    private readonly DataGrid _trackGrid = new() { AutoGenerateColumns = false, CanUserAddRows = false, Height = 240, Margin = new Thickness(0, 5, 0, 10) };
    private readonly StackPanel _editor = new();
    private readonly TextBlock _empty = new() { Text = "No music list selected. Add a chapter or zone pool to choose the tracks it can play.",
        TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(20),
        Foreground = StudioUi.Resource("ProjectManagerMutedBrush") };

    public AudioStudioView(string root, Action<string> status)
    {
        _root = root; _status = status;
        var body = new Grid { Margin = new Thickness(24, 0, 24, 24) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel();
        var poolActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var addChapter = StudioUi.Button("+ Chapter", (_, _) => AddPool("Chapter"));
        var addZone = StudioUi.Button("+ Zone", (_, _) => AddPool("Zone"));
        var deletePool = StudioUi.Button("Delete Pool", (_, _) => DeletePool());
        poolActions.Children.Add(addChapter); poolActions.Children.Add(addZone); poolActions.Children.Add(deletePool);
        DockPanel.SetDock(poolActions, Dock.Top); left.Children.Add(poolActions);
        _pools.SelectionChanged += (_, _) => LoadPool(); left.Children.Add(_pools); body.Children.Add(left);
        _trackGrid.Columns.Add(_trackChoice);
        _trackGrid.Columns.Add(new DataGridTextColumn { Header = "Weight", Binding = new System.Windows.Data.Binding(nameof(AudioTrackRow.Weight)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 70 });
        _trackGrid.Columns.Add(new DataGridTextColumn { Header = "From floor", Binding = new System.Windows.Data.Binding(nameof(AudioTrackRow.MinFloor)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 80 });
        _trackGrid.Columns.Add(new DataGridTextColumn { Header = "To floor", Binding = new System.Windows.Data.Binding(nameof(AudioTrackRow.MaxFloor)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 80 });
        _trackGrid.ItemsSource = _trackRows;
        var fields = new StackPanel(); fields.Children.Add(Heading("Where it plays")); Add(fields, "Scope", StudioUi.Segments(_scope)); Add(fields, "Exact custom chapter or zone ID", _reference); fields.Children.Add(Heading("Ambient")); Add(fields, "Ambient loop", _ambient);
        var volumeRow = new Grid(); volumeRow.ColumnDefinitions.Add(new ColumnDefinition()); volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, SmallChange = 0.01, LargeChange = 0.1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 10) };
        var updatingVolume = false;
        slider.ValueChanged += (_, _) => { if (updatingVolume) return; updatingVolume = true; _volume.Text = slider.Value.ToString("0.00", CultureInfo.InvariantCulture); updatingVolume = false; };
        _volume.TextChanged += (_, _) => { if (updatingVolume || !double.TryParse(_volume.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value is < 0 or > 1) return; updatingVolume = true; slider.Value = value; updatingVolume = false; };
        volumeRow.Children.Add(slider); Grid.SetColumn(_volume, 1); volumeRow.Children.Add(_volume); Add(fields, "Volume", volumeRow); fields.Children.Add(Heading("Tracks"));
        fields.Children.Add(new TextBlock { Text = "Vector chooses randomly by weight. Floor 0 means no limit.", Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(0, 0, 0, 6) });
        fields.Children.Add(_trackGrid);
        var trackActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        trackActions.Children.Add(StudioUi.Button("Add Track", (_, _) => _trackRows.Add(new AudioTrackRow { Id = _audioIds.FirstOrDefault() ?? "" })));
        trackActions.Children.Add(StudioUi.Button("Remove Track", (_, _) => { if (_trackGrid.SelectedItem is AudioTrackRow row) _trackRows.Remove(row); }));
        fields.Children.Add(trackActions);
        var save = StudioUi.Button("Save Music Pools", Save, true); save.HorizontalAlignment = HorizontalAlignment.Right; fields.Children.Add(save);
        _editor.Children.Add(fields);
        _editor.Children.Add(_empty);
        var editorScroll = new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) }; Grid.SetColumn(editorScroll, 2); body.Children.Add(editorScroll);
        Content = StudioUi.Shell("Audio", "Choose which music and sounds are used by each chapter or zone.", "\uE8D6", new SolidColorBrush(Color.FromRgb(224, 138, 48)), body, StudioUi.Button("Import Audio", Import));
        Reload();
    }
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 12) };
    private static void Add(Panel panel, string label, UIElement field) { panel.Children.Add(StudioUi.Label(label)); panel.Children.Add(field); }
    private string Manifest => Path.Combine(_root, "custom_audio", "audio_manifest.xml");
    private void Reload()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Manifest)!);
        if (!File.Exists(Manifest)) new XDocument(new XElement("CustomAudio", new XAttribute("SchemaVersion", "1"), new XElement("MusicPools"))).Save(Manifest);
        _audioIds.Clear();
        _audioIds.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(Manifest)!, "*", SearchOption.AllDirectories).Where(path =>
            new[] { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".m4a" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)!);
        _trackChoice.ItemsSource = _audioIds;
        _ambient.ItemsSource = new[] { "" }.Concat(_audioIds).ToList();
        var selected = _pools.SelectedIndex;
        var doc = XDocument.Load(Manifest);
        _pools.ItemsSource = doc.Descendants("Pool").Select((x, i) =>
            new PoolRow(i, $"{(string?)x.Attribute("Scope") ?? "Chapter"}: {(string?)x.Attribute("Reference") ?? "New Pool"}  ({x.Elements("Track").Count()} tracks)")).ToList();
        _pools.DisplayMemberPath = nameof(PoolRow.Label);
        if (_pools.Items.Count > 0) _pools.SelectedIndex = Math.Clamp(selected, 0, _pools.Items.Count - 1);
        UpdateVisibility();
    }
    private void UpdateVisibility()
    {
        var selected = _pools.SelectedItem is PoolRow;
        if (_editor.Children.Count > 0) _editor.Children[0].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
    }
    private void LoadPool()
    {
        UpdateVisibility();
        if (_pools.SelectedItem is not PoolRow row) return;
        var pool = XDocument.Load(Manifest).Descendants("Pool").ElementAtOrDefault(row.Index);
        if (pool is null) return;
        _scope.SelectedItem = (string?)pool.Attribute("Scope") == "Zone" ? "Zone" : "Chapter";
        _reference.Text = (string?)pool.Attribute("Reference") ?? "";
        _ambient.SelectedItem = (string?)pool.Attribute("Ambient") ?? "";
        _volume.Text = (string?)pool.Attribute("AmbientVolume") ?? "0.5";
        _trackRows.Clear();
        foreach (var track in pool.Elements("Track"))
            _trackRows.Add(new AudioTrackRow { Id = (string?)track.Attribute("Id") ?? "", Weight = (int?)track.Attribute("Weight") ?? 1,
                MinFloor = (int?)track.Attribute("MinFloor") ?? 0, MaxFloor = (int?)track.Attribute("MaxFloor") ?? 0 });
    }
    private void AddPool(string scope)
    {
        try
        {
            var doc = XDocument.Load(Manifest);
            doc.Root!.Element("MusicPools")!.Add(new XElement("Pool", new XAttribute("Scope", scope), new XAttribute("Reference", ""),
                new XAttribute("Ambient", ""), new XAttribute("AmbientVolume", "0.5")));
            WriteManifest(doc); Reload(); _pools.SelectedIndex = _pools.Items.Count - 1;
            _status("Created " + scope.ToLowerInvariant() + " music pool.");
        }
        catch (Exception ex) { _status($"Could not add music pool: {ex.Message}"); }
    }
    private void DeletePool()
    {
        if (_pools.SelectedItem is not PoolRow row) return;
        if (MessageBox.Show("Delete this music pool?", "Delete Pool", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RemovePool();
    }
    private void RemovePool()
    {
        if (_pools.SelectedItem is not PoolRow row) return;
        try { var doc = XDocument.Load(Manifest); doc.Descendants("Pool").ElementAt(row.Index).Remove(); WriteManifest(doc); Reload(); _status("Deleted music pool."); }
        catch (Exception ex) { _status($"Could not delete music pool: {ex.Message}"); }
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        if (_pools.SelectedItem is not PoolRow row) return;
        try
        {
            _trackGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _trackGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var reference = _reference.Text.Trim();
            if (reference.Length == 0) throw new InvalidDataException("Enter the exact chapter or zone ID.");
            if (!double.TryParse(_volume.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume) || !double.IsFinite(volume) || volume is < 0 or > 1)
                throw new InvalidDataException("Volume must be between 0 and 1.");
            foreach (var track in _trackRows)
                if (!_audioIds.Contains(track.Id, StringComparer.OrdinalIgnoreCase) || track.Weight is < 1 or > 100 ||
                    track.MinFloor is < 0 or > 999 || track.MaxFloor is < 0 or > 999 ||
                    (track.MaxFloor != 0 && track.MaxFloor < track.MinFloor))
                    throw new InvalidDataException("Choose an imported track and valid weight/floor values.");
            var ambient = _ambient.SelectedItem?.ToString() ?? "";
            if (ambient.Length > 0 && !_audioIds.Contains(ambient, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose an imported ambient loop.");
            var doc = XDocument.Load(Manifest);
            var pool = doc.Descendants("Pool").ElementAt(row.Index);
            pool.SetAttributeValue("Scope", _scope.SelectedItem?.ToString() ?? "Chapter");
            pool.SetAttributeValue("Reference", reference);
            pool.SetAttributeValue("Ambient", ambient);
            pool.SetAttributeValue("AmbientVolume", volume.ToString(CultureInfo.InvariantCulture));
            pool.Elements("Track").Remove();
            foreach (var track in _trackRows) pool.Add(new XElement("Track", new XAttribute("Id", track.Id),
                new XAttribute("Weight", track.Weight), new XAttribute("MinFloor", track.MinFloor), new XAttribute("MaxFloor", track.MaxFloor)));
            WriteManifest(doc);
            _status("Saved chapter and zone music pools."); Reload();
        }
        catch (Exception ex) { _status($"Could not save audio settings: {ex.Message}"); }
    }
    private void WriteManifest(XDocument document)
    {
        var temporary = Manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { document.Save(temporary); File.Move(temporary, Manifest, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void Import(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.ogg;*.aif;*.aiff;*.m4a", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var folder = Path.GetDirectoryName(Manifest)!;
            foreach (var file in dialog.FileNames) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
            Reload(); _status($"Imported {dialog.FileNames.Length} audio file(s).");
        }
        catch (Exception ex) { _status($"Audio import failed: {ex.Message}"); }
    }
    private sealed record PoolRow(int Index, string Label);
    public sealed class AudioTrackRow { public string Id { get; set; } = ""; public int Weight { get; set; } = 1; public int MinFloor { get; set; } public int MaxFloor { get; set; } }
}
