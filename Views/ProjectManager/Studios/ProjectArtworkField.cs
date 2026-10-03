using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

internal sealed class ProjectArtworkField : Grid
{
    public ProjectArtworkField(string root, TextBox value)
    {
        Margin = new Thickness(0,5,0,15);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var files = new[] { "custom_textures", "custom_backgrounds" }
            .Select(folder => Path.Combine(root, folder)).Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(file).ToLowerInvariant())).ToList();
        var choices = new[] { "" }.Concat(files.Select(Path.GetFileName).OfType<string>()).Append(value.Text).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
        var picker = new ComboBox { ItemsSource = choices, SelectedItem = value.Text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(picker,1); Children.Add(picker);
        var import = StudioUi.Button("Import Image", (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var destination = Path.Combine(root, "custom_textures");
                Directory.CreateDirectory(destination);
                var candidate = Path.Combine(destination, Path.GetFileName(dialog.FileName));
                if (File.Exists(candidate) && !Path.GetFullPath(candidate).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase) &&
                    MessageBox.Show("Replace the existing image with this name?", "Import Image", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var path = Path.GetFullPath(candidate).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)
                    ? candidate : Services.CustomContentService.ImportTexture(dialog.FileName, destination);
                if (!files.Contains(path)) files.Add(path);
                var selected = Path.GetFileName(path);
                picker.ItemsSource = choices = choices.Append(selected).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
                picker.SelectedItem = selected;
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Image import failed"); }
        });
        Grid.SetColumn(import, 2); Children.Add(import);
        var preview = new Grid { Background = StudioUi.Resource("ProjectManagerArtworkBrush"), Height = 66, Width = 86, ClipToBounds = true };
        Children.Add(preview);
        void Refresh()
        {
            preview.Children.Clear();
            var path = files.FirstOrDefault(file => Path.GetFileName(file).Equals(value.Text,StringComparison.OrdinalIgnoreCase));
            if (path is not null)
            {
                try {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.DecodePixelWidth = 172; image.EndInit(); image.Freeze();
                    preview.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill }); return;
                } catch { }
            }
            preview.Children.Add(new TextBlock { Text = "\uEB9F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = StudioUi.Resource("ProjectManagerMutedBrush") });
        }
        picker.SelectionChanged += (_, _) => { value.Text = picker.SelectedItem as string ?? ""; Refresh(); };
        Refresh();
    }
}
