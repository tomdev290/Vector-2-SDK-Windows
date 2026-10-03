using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class ObstacleLibraryStudioView : UserControl
{
    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly WrapPanel _packages = new();

    public ObstacleLibraryStudioView(string root, Action<string> status)
    {
        _root = root; _folder = Path.Combine(root, "custom_obstacles"); _status = status;
        _packages.Margin = new Thickness(20);
        Content = StudioUi.Shell("Obstacle Library", "Reusable obstacle packages, including XML, previews, and textures.",
            "\uE7B8", new SolidColorBrush(Color.FromRgb(211, 129, 55)),
            new ScrollViewer { Content = _packages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            StudioUi.Button("Open Folder", (_, _) => { Directory.CreateDirectory(_folder); Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true }); }),
            StudioUi.Button("Import Package", (_, _) => Import(), true));
        Reload();
    }

    private void Reload()
    {
        Directory.CreateDirectory(_folder);
        _packages.Children.Clear();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.v2obstacle").OrderBy(Path.GetFileName))
        {
            try
            {
                var info = ObstacleArchiveService.Read(path);
                var card = new StackPanel { Width = 235 };
                var preview = new Grid { Height = 145, Background = StudioUi.Resource("ProjectManagerArtworkBrush") };
                if (info.Preview is not null)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(info.Preview);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 470;
                        bitmap.EndInit(); bitmap.Freeze();
                        preview.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
                    }
                    catch { }
                }
                if (preview.Children.Count == 0)
                    preview.Children.Add(new TextBlock { Text = "\uE7B8", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 44, Foreground = new SolidColorBrush(Color.FromRgb(211, 129, 55)),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                card.Children.Add(preview);
                card.Children.Add(new TextBlock { Text = info.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
                card.Children.Add(new TextBlock { Text = info.Id + "  |  " + info.TextureCount + " textures",
                    FontSize = 11, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
                buttons.Children.Add(StudioUi.Button("Reveal", (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })));
                buttons.Children.Add(StudioUi.Button("Delete", (_, _) => Delete(path)));
                card.Children.Add(buttons);
                _packages.Children.Add(StudioUi.Card(card, new Thickness(0, 0, 12, 12)));
            }
            catch (Exception ex) { _status("Could not read " + Path.GetFileName(path) + ": " + ex.Message); }
        }
        if (_packages.Children.Count == 0)
            _packages.Children.Add(new TextBlock { Text = "No obstacle packages yet.", Foreground = StudioUi.Resource("ProjectManagerMutedBrush"),
                Margin = new Thickness(20, 80, 0, 0) });
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "Import Vector obstacle packages",
            Filter = "Vector obstacle package (*.v2obstacle)|*.v2obstacle", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            foreach (var source in dialog.FileNames) ObstacleArchiveService.Import(source, _root);
            Reload();
            _status("Imported " + dialog.FileNames.Length + " obstacle package(s) and indexed their textures.");
        }
        catch (Exception ex) { _status("Obstacle import failed: " + ex.Message); Reload(); }
    }

    private void Delete(string path)
    {
        if (MessageBox.Show("Delete " + Path.GetFileName(path) + "?", "Delete obstacle",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(path); Reload(); _status("Deleted " + Path.GetFileName(path) + "."); }
        catch (Exception ex) { _status(ex.Message); }
    }
}
