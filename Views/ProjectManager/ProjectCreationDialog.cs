using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Vector2LevelEditor.Views.ProjectManager;

public sealed class ProjectCreationDialog : Window
{
    private readonly TextBox _name = new() { Text = "My Vector 2 Project", Margin = new Thickness(0, 5, 0, 14) };
    private readonly TextBox _location = new() { Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), IsReadOnly = true };

    public string ProjectName => _name.Text.Trim();
    public string ParentFolder => _location.Text.Trim();

    public ProjectCreationDialog()
    {
        Title = "New Vector 2 Project";
        Width = 560;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["PanelBrush"];
        Foreground = (Brush)Application.Current.Resources["WindowTextBrush"];

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Add(root, new TextBlock { Text = "Create a project workspace", FontSize = 22, FontWeight = FontWeights.Bold }, 0);
        Add(root, new TextBlock { Text = "The editor will create and organize the project folder for you.", Foreground = (Brush)Application.Current.Resources["MutedTextBrush"], Margin = new Thickness(0, 4, 0, 18) }, 1);
        var namePanel = new StackPanel();
        namePanel.Children.Add(new TextBlock { Text = "Project name" });
        namePanel.Children.Add(_name);
        Add(root, namePanel, 2);

        var locationPanel = new StackPanel();
        locationPanel.Children.Add(new TextBlock { Text = "Create inside" });
        var locationRow = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        locationRow.ColumnDefinitions.Add(new ColumnDefinition());
        locationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        locationRow.Children.Add(_location);
        var browse = new Button { Content = "Choose…", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        browse.Click += (_, _) => ChooseLocation();
        Grid.SetColumn(browse, 1);
        locationRow.Children.Add(browse);
        locationPanel.Children.Add(locationRow);
        Add(root, locationPanel, 3);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 82, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var create = new Button { Content = "Create Project", MinWidth = 116, IsDefault = true };
        create.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(create);
        Add(root, actions, 5);
        Content = root;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private void ChooseLocation()
    {
        var dialog = new OpenFolderDialog { Title = "Choose where the new project folder will be created", InitialDirectory = ParentFolder };
        if (dialog.ShowDialog(this) == true) _location.Text = dialog.FolderName;
    }

    private void Accept()
    {
        if (ProjectName.Length == 0) { MessageBox.Show(this, "Enter a project name.", Title); return; }
        if (ProjectName is "." or ".." || ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(this, "The project name contains characters Windows cannot use in a folder name.", Title); return; }
        if (!Directory.Exists(ParentFolder)) { MessageBox.Show(this, "Choose an existing parent location.", Title); return; }
        if (Directory.Exists(Path.Combine(ParentFolder, ProjectName))) { MessageBox.Show(this, "A folder with that project name already exists. Choose another name.", Title); return; }
        DialogResult = true;
    }

    private static void Add(Grid root, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        root.Children.Add(element);
    }
}
