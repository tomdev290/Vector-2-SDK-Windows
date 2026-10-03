using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class ProjectMediaStudioView : UserControl
{
    private readonly string _root;
    private readonly string _folder;
    private readonly string _section;
    private readonly Action<string>? _open;
    private readonly Action<string> _status;
    private readonly StackPanel _items = new();
    private readonly List<(string Id, string Name)> _zones = [];
    private readonly ComboBox _roomZone = new() { MinWidth = 180, MaxWidth = 320, DisplayMemberPath = nameof(StructuralZone.Name) };
    private bool _refreshingZones;

    public ProjectMediaStudioView(string root, string section, string relative, Action<string>? open, Action<string> status, Action<string>? navigate = null)
    {
        _root = root;
        _folder = Path.Combine(root, relative);
        _section = section;
        _open = open;
        _status = status;
        var meta = section switch
        {
            "Content" => ("Room Library", "Open or import the rooms used by this project.", "\uE8B7", Color.FromRgb(48, 130, 228)),
            "Assets" => ("Texture Library", "Artwork and textures shipped with this project.", "\uEB9F", Color.FromRgb(72, 146, 166)),
            _ => ("Background Library", "Background artwork available to rooms and zones.", "\uE91B", Color.FromRgb(82, 119, 178))
        };
        var scroll = new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(20, 0, 20, 16)
        };
        UIElement body = scroll;
        if (section == "Content")
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            var toolbar = new WrapPanel { Margin = new Thickness(24, 12, 24, 10) };
            toolbar.Children.Add(new TextBlock { Text = "Zone", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var zones = StructuralRoomService.LoadZones(root);
            _roomZone.ItemsSource = zones;
            _roomZone.SelectedItem = zones.FirstOrDefault();
            toolbar.Children.Add(_roomZone);
            var newRoom = StudioUi.Button("New Room", (_, _) => CreateRoom(), true);
            newRoom.Margin = new Thickness(8, 0, 0, 0); toolbar.Children.Add(newRoom);
            var all = StudioUi.Button("All Rooms", (_, _) => { _roomZone.SelectedItem = null; Reload(); });
            all.Margin = new Thickness(8, 0, 0, 0); toolbar.Children.Add(all);
            if (navigate is not null)
            {
                var create = StudioUi.Button("New Zone", (_, _) => navigate("Zones"));
                create.Margin = new Thickness(8, 0, 0, 0); toolbar.Children.Add(create);
            }
            _roomZone.SelectionChanged += (_, _) => { if (!_refreshingZones) Reload(); };
            grid.Children.Add(toolbar); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); body = grid;
        }
        Content = StudioUi.Shell(meta.Item1, meta.Item2, meta.Item3, new SolidColorBrush(meta.Item4),
            body,
            StudioUi.Button("Open Folder", (_, _) => OpenFolder()),
            StudioUi.Button(section == "Content" ? "Import Content" : $"Import {section}", Import, true));
        Reload();
        Loaded += (_, _) => { RefreshRoomZones(); Reload(); };
    }

    private void RefreshRoomZones()
    {
        if (_section != "Content") return;
        var selected = (_roomZone.SelectedItem as StructuralZone)?.Id;
        _refreshingZones = true;
        try
        {
            var zones = StructuralRoomService.LoadZones(_root);
            _roomZone.ItemsSource = zones;
            _roomZone.SelectedItem = zones.FirstOrDefault(zone => zone.Id == selected);
        }
        finally { _refreshingZones = false; }
    }

    private void CreateRoom()
    {
        if (_roomZone.SelectedItem is not StructuralZone zone) { _status("Select a zone before creating a room."); return; }
        var window = new Window { Title = "New Room", Width = 370, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Vector2LevelEditor;component/Views/ProjectManager/Studios/StudioTheme.xaml", UriKind.Relative) });
        window.SetResourceReference(BackgroundProperty, "ProjectManagerCardBrush");
        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(StudioUi.Label("Room name"));
        var name = StudioUi.Field("New Room"); content.Children.Add(name);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 8) };
        content.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(StudioUi.Button("Cancel", (_, _) => window.Close()));
        buttons.Children.Add(StudioUi.Button("Create", (_, _) =>
        {
            try
            {
                var path = ZoneRoomImportService.CreateRoom(_root, zone.RoomsPath, name.Text);
                _status($"Created {Path.GetFileName(path)} in {zone.Name}.");
                window.Close(); Reload(); _open?.Invoke(path);
            }
            catch (Exception failure) { error.Text = failure.Message; }
        }, true));
        content.Children.Add(buttons); window.Content = content;
        window.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        window.ShowDialog();
    }

    private void Reload()
    {
        Directory.CreateDirectory(_folder);
        _items.Children.Clear();
        if (_section == "Backgrounds")
        {
            LoadZones();
            ShowSceneBackgrounds();
        }
        if (_section == "Content")
            _items.Children.Add(new TextBlock { Text = "ROOMS", FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(32, 8, 0, 8) });

        var files = Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories)
            .Where(path => _section != "Content" || _roomZone.SelectedItem is not StructuralZone zone ||
                string.Equals(Path.GetDirectoryName(path), _folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(ProjectManifestService.SafeCombine(_root, zone.RoomsPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".meta.xml", StringComparison.OrdinalIgnoreCase))
            .Where(path => _section == "Content" ? path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) :
                new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var artwork = new WrapPanel { Margin = new Thickness(8, 0, 0, 0) };

        foreach (var file in files)
        {
            if (_section != "Content")
            {
                artwork.Children.Add(ArtworkItem(file));
                continue;
            }
            var row = new Grid { Height = 52, Margin = new Thickness(30, 0, 30, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 27, Height = 27, CornerRadius = new CornerRadius(6),
                Background = StudioUi.Resource("ProjectManagerHoverBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = _section == "Content" ? "\uE8A5" : "\uEB9F",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Foreground = new SolidColorBrush(Color.FromRgb(44, 127, 229)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(icon);

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(file), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = Path.GetRelativePath(_folder, Path.GetDirectoryName(file)!), FontSize = 10, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);

            var modified = new TextBlock
            {
                Text = File.GetLastWriteTime(file).ToString("MMM d, yyyy  h:mm tt"),
                FontSize = 10,
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(modified, 2);
            row.Children.Add(modified);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (_roomZone.SelectedItem is StructuralZone selectedZone &&
                string.Equals(Path.GetDirectoryName(file), _folder, StringComparison.OrdinalIgnoreCase))
            {
                var assign = StudioUi.Button("Add to Zone", (_, _) =>
                {
                    try
                    {
                        ZoneRoomImportService.Import(_root, selectedZone.RoomsPath, [file]);
                        _status($"Added {Path.GetFileName(file)} to {selectedZone.Name}; the original room was preserved.");
                        Reload();
                    }
                    catch (Exception error) { _status("Could not add room to zone: " + error.Message); }
                });
                assign.ToolTip = $"Copy this unassigned room into {selectedZone.RoomsPath}";
                assign.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(assign);
            }
            actions.Children.Add(StudioUi.Button("Open", (_, _) => OpenFile(file)));
            var delete = StudioUi.Button("\uE74D", (_, _) => Delete(file));
            delete.FontFamily = new FontFamily("Segoe MDL2 Assets");
            delete.ToolTip = "Delete";
            delete.MinWidth = 28;
            delete.Padding = new Thickness(5);
            delete.Margin = new Thickness(5, 0, 0, 0);
            actions.Children.Add(delete);
            Grid.SetColumn(actions, 3);
            row.Children.Add(actions);
            _items.Children.Add(row);
        }
        if (_section != "Content" && artwork.Children.Count > 0) _items.Children.Add(artwork);

        if (files.Count == 0)
            _items.Children.Add(new TextBlock
            {
                Text = _section == "Content" ? "No rooms yet." : "No files yet.",
                FontSize = 13,
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"),
                Margin = new Thickness(32, 28, 0, 0)
            });
    }

    private void LoadZones()
    {
        _zones.Clear();
        var folder = Path.Combine(_root, "custom_zones");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var zone in XDocument.Load(file).Descendants("Zone"))
                {
                    var id = (string?)zone.Attribute("Id");
                    if (!string.IsNullOrWhiteSpace(id) && !_zones.Any(item => item.Id == id))
                        _zones.Add((id, (string?)zone.Attribute("Name") ?? id));
                }
            }
            catch (System.Xml.XmlException) { }
        }
    }

    private void ShowSceneBackgrounds()
    {
        var path = Path.Combine(_folder, "custom_backgrounds.xml");
        if (!File.Exists(path)) return;
        try
        {
            var scenes = XDocument.Load(path).Root?.Elements("Background").ToList() ?? [];
            if (scenes.Count == 0) return;
            _items.Children.Add(new TextBlock { Text = "ROOM BACKGROUND SETS", FontWeight = FontWeights.SemiBold,
                Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), Margin = new Thickness(10, 10, 0, 8) });
            foreach (var scene in scenes)
            {
                var name = (string?)scene.Attribute("Name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var row = new Grid { Margin = new Thickness(8, 0, 8, 6), MinHeight = 42 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var place = StudioUi.Button("Place in Room", (_, _) =>
                {
                    try
                    {
                        if (Application.Current?.MainWindow?.DataContext is not Vector2LevelEditor.ViewModels.MainWindowViewModel editor)
                            throw new InvalidDataException("No level editor is open.");
                        var definition = BackgroundDesignerService.Load(_folder).FirstOrDefault(item => item.Name == name)
                            ?? throw new InvalidDataException("The background set no longer exists.");
                        editor.PlaceSceneBackground(definition);
                        _status($"Placed {name} in the open room.");
                    }
                    catch (Exception error) { _status($"Could not place background: {error.Message}"); }
                });
                place.Margin = new Thickness(8, 0, 12, 0); Grid.SetColumn(place, 1); row.Children.Add(place);
                var zone = ZonePicker((string?)scene.Attribute("Zone") ?? "");
                zone.SelectionChanged += (_, _) =>
                {
                    if (zone.SelectedItem is ZoneChoice selected)
                    {
                        try
                        {
                            var document = XDocument.Load(path);
                            var item = document.Root?.Elements("Background").FirstOrDefault(node => (string?)node.Attribute("Name") == name);
                            if (item is null) throw new InvalidDataException("The background set no longer exists.");
                            item.SetAttributeValue("Zone", selected.Id.Length == 0 ? null : selected.Id);
                            document.Save(path);
                            _status($"Assigned {name} to {(selected.Id.Length == 0 ? "no zone" : selected.Name)}.");
                        }
                        catch (Exception ex) { _status($"Could not assign background: {ex.Message}"); }
                    }
                };
                Grid.SetColumn(zone, 2);
                row.Children.Add(zone);
                _items.Children.Add(row);
            }
        }
        catch (Exception ex) { _status($"Could not read room backgrounds: {ex.Message}"); }
    }

    private Border ArtworkItem(string file)
    {
        var card = new StackPanel { Width = 275 };
        var imageArea = new Border { Height = _section == "Backgrounds" ? 155 : 185,
            Background = StudioUi.Resource("ProjectManagerArtworkBrush"), CornerRadius = new CornerRadius(5) };
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(file);
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.DecodePixelWidth = 520;
            source.EndInit();
            source.Freeze();
            imageArea.Child = new Image { Source = source, Stretch = Stretch.Uniform };
        }
        catch
        {
            imageArea.Child = new TextBlock { Text = "\uEB9F", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        card.Children.Add(imageArea);
        card.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(file), FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 9, 0, 7) });
        if (_section == "Backgrounds")
        {
            var tags = ReadTags();
            var fileName = Path.GetFileName(file);
            tags.TryGetValue(fileName, out var tag);
            var settings = new StackPanel { Orientation = Orientation.Horizontal };
            var zone = ZonePicker(tag.Zone ?? "");
            zone.Width = 165;
            var role = new ComboBox { ItemsSource = new[] { "Menu", "Loading" }, Width = 88,
                Margin = new Thickness(8, 0, 0, 0), SelectedItem = tag.Role ?? "Menu", IsEnabled = !string.IsNullOrWhiteSpace(tag.Zone) };
            void SaveTag()
            {
                if (zone.SelectedItem is not ZoneChoice selected) return;
                try { WriteTag(fileName, selected.Id, role.SelectedItem?.ToString() ?? "Menu"); role.IsEnabled = selected.Id.Length > 0; }
                catch (Exception ex) { _status($"Could not tag background: {ex.Message}"); }
            }
            zone.SelectionChanged += (_, _) => SaveTag();
            role.SelectionChanged += (_, _) => SaveTag();
            settings.Children.Add(zone); settings.Children.Add(role);
            card.Children.Add(settings);
        }
        var footer = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
        footer.Children.Add(new TextBlock { Text = Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),
            Foreground = StudioUi.Resource("ProjectManagerMutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(StudioUi.Button("Open", (_, _) => OpenFile(file)));
        var delete = StudioUi.Button("\uE74D", (_, _) => Delete(file));
        delete.FontFamily = new FontFamily("Segoe MDL2 Assets"); delete.ToolTip = "Delete from project";
        actions.Children.Add(delete); footer.Children.Add(actions);
        card.Children.Add(footer);
        return StudioUi.Card(card, new Thickness(0, 0, 12, 12));
    }

    private sealed record ZoneChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
    private ComboBox ZonePicker(string id)
    {
        var choices = new[] { new ZoneChoice("", "Unassigned") }.Concat(_zones.Select(zone => new ZoneChoice(zone.Id, zone.Name))).ToList();
        return new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(choice => choice.Id == id) ?? choices[0] };
    }
    private string TagsPath => Path.Combine(_folder, "zone_backgrounds.xml");
    private Dictionary<string, (string? Zone, string? Role)> ReadTags()
    {
        if (!File.Exists(TagsPath)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return XDocument.Load(TagsPath).Descendants("Background")
                .Where(node => !string.IsNullOrWhiteSpace((string?)node.Attribute("File")))
                .GroupBy(node => (string)node.Attribute("File")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => ((string?)group.Last().Attribute("Zone"), (string?)group.Last().Attribute("Role")),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException) { return new(StringComparer.OrdinalIgnoreCase); }
    }
    private void WriteTag(string file, string zone, string role)
    {
        var tags = ReadTags();
        if (zone.Length == 0) tags.Remove(file); else tags[file] = (zone, role);
        new XDocument(new XElement("ZoneBackgrounds", tags.OrderBy(pair => pair.Key).Select(pair =>
            new XElement("Background", new XAttribute("File", pair.Key), new XAttribute("Zone", pair.Value.Zone!),
                new XAttribute("Role", pair.Value.Role ?? "Menu"))))).Save(TagsPath);
        _status(zone.Length == 0 ? $"Unassigned {file}." : $"Assigned {file} to {zone} ({role}).");
    }

    private void OpenFile(string file)
    {
        try
        {
            if (_open is not null) _open(file);
            else Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Delete(string file)
    {
        if (MessageBox.Show($"Delete {Path.GetFileName(file)} from this project?", "Delete file",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(file);
            if (_section == "Backgrounds" && ReadTags().ContainsKey(Path.GetFileName(file)))
                WriteTag(Path.GetFileName(file), "", "Menu");
            _status($"Deleted {Path.GetFileName(file)}.");
            Reload();
        }
        catch (Exception ex) { _status(ex.Message); }
    }

    private void Import(object sender, RoutedEventArgs e)
    {
        var filter = _section == "Content" ? "Vector room XML (*.xml)|*.xml" : "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (_section == "Content")
            {
                if (_roomZone.SelectedItem is not StructuralZone zone)
                    throw new InvalidOperationException("Select a zone before importing rooms. Create a zone first if none exists.");
                var imported = ZoneRoomImportService.Import(_root, zone.RoomsPath, dialog.FileNames);
                _status($"Imported {imported.Count} room(s) into {zone.Name} ({zone.RoomsPath}).");
                Reload(); return;
            }
            foreach (var source in dialog.FileNames)
            {
                var target = Path.Combine(_folder, Path.GetFileName(source));
                if (File.Exists(target)) throw new IOException($"{Path.GetFileName(source)} already exists.");
                File.Copy(source, target);
            }
            _status($"Imported {dialog.FileNames.Length} file(s) into {_section}.");
            Reload();
        }
        catch (Exception ex) { _status(ex.Message); }
    }
}
