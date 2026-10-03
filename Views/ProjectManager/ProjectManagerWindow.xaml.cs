using Vector2LevelEditor.ViewModels.ProjectManager;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Vector2LevelEditor.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vector2LevelEditor.Views.ProjectManager.Studios;

namespace Vector2LevelEditor.Views.ProjectManager;

public partial class ProjectManagerWindow : UserControl
{
    private bool _searchGlassApplied;
    public ProjectManagerWindow()
    {
        InitializeComponent();
        var viewModel = new ProjectManagerViewModel();
        viewModel.RoomOpenRequested += OpenRoomInEditor;
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ProjectManagerViewModel.SearchFocused)) AnimateSearchField(); };
        SizeChanged += (_, _) => AnimateSearchField(false);
        SearchResultsPanel.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (!_searchGlassApplied && Window.GetWindow(this) is { ActualWidth: > 0 })
                {
                    var opacity = SearchResultsPanel.Opacity;
                    SearchResultsPanel.Opacity = 0;
                    try { XmlFrostedSurface.Apply(SearchResultsPanel, SearchResultsPanel, -12, -12); _searchGlassApplied = true; }
                    finally { SearchResultsPanel.Opacity = opacity; }
                }
                if (!SystemParameters.ClientAreaAnimation) return;
                var transform = (TransformGroup)SearchResultsPanel.RenderTransform;
                var duration = TimeSpan.FromMilliseconds(240);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                SearchResultsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
                ((TranslateTransform)transform.Children[1]).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, duration) { EasingFunction = ease });
                var scale = (ScaleTransform)transform.Children[0];
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, 1, duration) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, 1, duration) { EasingFunction = ease });
            }));
        };
        PreviewKeyDown += OnSearchKeyDown;
        PreviewMouseDown += OnSearchMouseDown;
    }

    private void AnimateSearchField(bool animate = true)
    {
        var available = Math.Max(180, ActualWidth - 174 - 240);
        var focused = DataContext is ProjectManagerViewModel { SearchFocused: true };
        var width = Math.Min(focused ? 610 : 520, available);
        SearchResultsPanel.Width = Math.Min(610, available);
        var from = SearchFieldBorder.ActualWidth > 0 ? SearchFieldBorder.ActualWidth : width;
        SearchFieldBorder.BeginAnimation(WidthProperty, null);
        SearchFieldBorder.Width = width;
        if (animate && SystemParameters.ClientAreaAnimation)
            SearchFieldBorder.BeginAnimation(WidthProperty, new DoubleAnimation(from, width, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SearchField_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ProjectManagerViewModel model) model.SearchFocused = true;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectManagerViewModel model) model.SearchText = "";
        SearchField.Focus();
    }

    private void DoneSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectManagerViewModel model) model.SearchFocused = false;
        Keyboard.ClearFocus();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProjectManagerViewModel model) return;
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchField.Focus();
            SearchField.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && model.SearchFocused)
        {
            model.SearchFocused = false;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && model.SearchFocused && SearchField.IsKeyboardFocused)
        {
            if (model.SearchHits.FirstOrDefault() is { } hit)
                model.OpenSearchHitCommand.Execute(hit);
            e.Handled = true;
        }
    }

    private void OnSearchMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProjectManagerViewModel model || e.OriginalSource is not DependencyObject source) return;
        if (!SearchFieldBorder.IsAncestorOf(source) && !SearchResultsPanel.IsAncestorOf(source))
            model.SearchFocused = false;
    }

    private void OpenRoomInEditor(string path)
    {
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.OpenProjectRoom(path);
    }

    public void OpenCustomAnimations()
    {
        if (DataContext is ProjectManagerViewModel { Project: null } model)
        {
            model.Status = "Open or create a project before creating a custom animation.";
            return;
        }
        StudioHost.OpenCustomAnimations();
    }

    public void OpenStudioSection(string section)
    {
        if (DataContext is not ProjectManagerViewModel model) return;
        if (model.Project is null)
        {
            model.Status = "Open or create a project first.";
            return;
        }
        var target = Models.ProjectManager.ProjectSection.All.FirstOrDefault(item => item.Name == section);
        if (target is not null) model.SelectedSection = target;
    }
}
