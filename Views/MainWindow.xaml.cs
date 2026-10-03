using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class MainWindow : Window
{
    private Point _hierarchyDragStart;
    private LevelNode? _hierarchyDragSource;
    private LevelNode? _hierarchySelectionAnchor;
    private SwarmDesignerWindow? _swarmDesigner;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        if (ProjectManagerWorkspace.DataContext is ViewModels.ProjectManager.ProjectManagerViewModel projectManager)
        {
            projectManager.GameDataPathProvider = () =>
            {
                var executable = viewModel.GameExecutablePath;
                if (string.IsNullOrWhiteSpace(executable) || !System.IO.File.Exists(executable)) return null;
                var data = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(executable)!, System.IO.Path.GetFileNameWithoutExtension(executable) + "_Data");
                return System.IO.Directory.Exists(data) ? data : null;
            };
            viewModel.RoomSaved += path =>
            {
                if (projectManager.Project is not { } project) return;
                var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(project.RootPath, "custom_rooms")) + System.IO.Path.DirectorySeparatorChar;
                if (System.IO.Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    projectManager.StudioChanged("Saved " + System.IO.Path.GetFileName(path) + ".");
            };
        }
        Loaded += (_, _) => Dispatcher.BeginInvoke(viewModel.RefreshAssets);
        KeyDown += OnEditorKeyDown;
    }

    private void CustomAnimationsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.OpenProjectManagerCommand.Execute(null);
        ProjectManagerWorkspace.OpenCustomAnimations();
    }

    private void SwarmDesignerMenu_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SelectDocumentCommand.Execute(vm.SelectedDocument);
        if (_swarmDesigner is not null)
        {
            _swarmDesigner.Activate();
            return;
        }
        _swarmDesigner = new SwarmDesignerWindow(vm.SelectedDocument, vm) { Owner = this };
        _swarmDesigner.Closed += (_, _) => _swarmDesigner = null;
        _swarmDesigner.Show();
    }

    private void ProjectStudioMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string section }) return;
        if (DataContext is MainWindowViewModel vm) vm.OpenProjectManagerCommand.Execute(null);
        ProjectManagerWorkspace.OpenStudioSection(section);
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e.Key == Key.Escape && !e.Handled && vm.RoomLayoutsVisibility == Visibility.Visible)
        {
            vm.CloseRoomLayoutsCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && !e.Handled && (vm.CenterTab != "Canvas" || vm.IsProjectManagerVisible || vm.IsAIStudioVisible || vm.IsPlayerStudioVisible))
        {
            switch (vm.CenterTab)
            {
                case "Dynamic Studio": vm.CloseDynamicStudioCommand.Execute(null); break;
                case "Background Designer": vm.CloseBackgroundDesignerCommand.Execute(null); break;
                case "Trick Preview": vm.CloseTrickPreviewCommand.Execute(null); break;
                default: vm.SelectCenterTabCommand.Execute("Canvas"); vm.SelectDocumentCommand.Execute(vm.SelectedDocument); break;
            }
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.ComboBox or PasswordBox) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.OemComma when Keyboard.Modifiers == ModifierKeys.None:
                if (LevelCanvas.IsVisible) e.Handled = LevelCanvas.CenterOnSelection();
                break;
            case Key.D1:
            case Key.NumPad1:
                vm.SelectedTool = EditorTool.Cursor;
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                vm.SelectedTool = EditorTool.Images;
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                vm.SelectedTool = EditorTool.Backgrounds;
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                vm.SelectedTool = EditorTool.Trapezoid;
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                vm.SelectedTool = EditorTool.Collision;
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                vm.SelectedTool = EditorTool.Trigger;
                e.Handled = true;
                break;
            case Key.D7:
            case Key.NumPad7:
                vm.SelectedTool = EditorTool.Area;
                e.Handled = true;
                break;
            case Key.D8:
            case Key.NumPad8:
                vm.SelectedTool = EditorTool.ObjectReference;
                e.Handled = true;
                break;
            case Key.D9:
            case Key.NumPad9:
                vm.SelectedTool = EditorTool.Object;
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                vm.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when ctrl:
                vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V when ctrl:
                vm.PasteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N when ctrl:
                vm.NewCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.O when ctrl:
                vm.OpenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S when ctrl:
                vm.ExportCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.B when ctrl:
                vm.BrowserCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F5:
                vm.RefreshAssetsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void DocumentTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not FrameworkElement { DataContext: LevelDocument document } || DataContext is not MainWindowViewModel vm) return;
        vm.CloseDocumentCommand.Execute(document);
        e.Handled = true;
    }

    private void StudioTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not FrameworkElement { Tag: string studio } || DataContext is not MainWindowViewModel vm) return;
        switch (studio)
        {
            case "Project Manager": vm.CloseProjectManagerCommand.Execute(null); break;
            case "AI": vm.CloseAIStudioCommand.Execute(null); break;
            case "Player": vm.ClosePlayerStudioCommand.Execute(null); break;
        }
        e.Handled = true;
    }

    private void HelpMenu_Click(object sender, RoutedEventArgs e)
        => new HelpWindow { Owner = this }.ShowDialog();

    private void AboutMenu_Click(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();

    private void HierarchyTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainWindowViewModel vm && e.NewValue is LevelNode node)
        {
            vm.SelectNode(node);
        }
    }

    private void HierarchyTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _hierarchyDragStart = e.GetPosition(null);
        _hierarchyDragSource = FindVisualParent<TreeViewItem>((DependencyObject)e.OriginalSource)?.DataContext as LevelNode;
        if (_hierarchyDragSource is null || DataContext is not MainWindowViewModel vm) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            vm.SelectHierarchyRange(_hierarchySelectionAnchor, _hierarchyDragSource);
            _hierarchySelectionAnchor = _hierarchyDragSource;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            vm.ToggleNodeSelection(_hierarchyDragSource);
            _hierarchySelectionAnchor = _hierarchyDragSource;
            e.Handled = true;
        }
        else
        {
            _hierarchySelectionAnchor = _hierarchyDragSource;
        }
    }

    private void HierarchyTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _hierarchyDragSource is null) return;
        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _hierarchyDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _hierarchyDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _hierarchyDragSource, DragDropEffects.Move);
        _hierarchyDragSource = null;
    }

    private void HierarchyTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LevelNode)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void HierarchyTree_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !e.Data.GetDataPresent(typeof(LevelNode))) return;
        var source = (LevelNode)e.Data.GetData(typeof(LevelNode))!;
        var target = FindVisualParent<TreeViewItem>((DependencyObject)e.OriginalSource)?.DataContext as LevelNode;
        vm.ReparentSelection(source, target);
        e.Handled = true;
    }

    private void HierarchyRoot_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LevelNode)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void HierarchyRoot_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !e.Data.GetDataPresent(typeof(LevelNode))) return;
        vm.ReparentSelection((LevelNode)e.Data.GetData(typeof(LevelNode))!, null);
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
