using System.IO;
using System.Windows;
using System.Windows.Controls;
using Vector2LevelEditor.Models;
using Vector2LevelEditor.Services;
using Vector2LevelEditor.ViewModels;

namespace Vector2LevelEditor.Views;

public partial class RoomLayoutPanel : UserControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(LevelDocument), typeof(RoomLayoutPanel),
        new PropertyMetadata(null, (owner, _) => ((RoomLayoutPanel)owner).Refresh()));

    private RoomLayoutOption? _selected;
    private bool _refreshing;
    private sealed record ParentChoice(string DisplayName, RoomLayoutOption? Option);

    public LevelDocument? Document
    {
        get => (LevelDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public RoomLayoutPanel() => InitializeComponent();

    private void Refresh(RoomLayoutSection? section = null, string? variant = null)
    {
        if (StartOptions is null) return;
        _refreshing = true;
        var options = Document is null ? [] : RoomLayoutService.Options(Document);
        StartOptions.ItemsSource = options.Where(option => option.Section == RoomLayoutSection.Start).ToList();
        MiddleOptions.ItemsSource = options.Where(option => option.Section == RoomLayoutSection.Middle).ToList();
        FinishOptions.ItemsSource = options.Where(option => option.Section == RoomLayoutSection.Finish).ToList();
        var wanted = section is null ? _selected : options.FirstOrDefault(option =>
            option.Section == section && option.Variant == variant);
        _selected = wanted is null ? null : options.FirstOrDefault(option =>
            option.Section == wanted.Section && option.Variant == wanted.Variant);
        if (_selected is not null)
            ListFor(_selected.Section).SelectedItem = _selected;
        _refreshing = false;
        RefreshDetails();
    }

    private ListBox ListFor(RoomLayoutSection section) => section switch
    {
        RoomLayoutSection.Start => StartOptions,
        RoomLayoutSection.Middle => MiddleOptions,
        _ => FinishOptions
    };

    private void Options_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || sender is not ListBox { SelectedItem: RoomLayoutOption option }) return;
        _refreshing = true;
        foreach (var list in new[] { StartOptions, MiddleOptions, FinishOptions })
            if (!ReferenceEquals(list, sender)) list.SelectedItem = null;
        _refreshing = false;
        _selected = option;
        RefreshDetails();
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.SelectNodes(option.Nodes);
    }

    private void RefreshDetails()
    {
        SelectionTitle.Text = _selected is null ? "Select a layout" :
            $"{_selected.DisplayName} · {_selected.Section} · {_selected.Nodes.Count} objects";
        RenameName.Text = _selected?.Variant ?? "";
        var options = Document is null ? [] : RoomLayoutService.Options(Document);
        var parents = new List<ParentChoice> { new("Any", null) };
        parents.AddRange(options.Where(option => _selected is null ||
            option.Section != _selected.Section || option.Variant != _selected.Variant)
            .Select(option => new ParentChoice($"{option.Section}: {option.DisplayName}", option)));
        ParentPicker.ItemsSource = parents;
        ParentPicker.SelectedItem = parents.FirstOrDefault(choice =>
            choice.Option is not null && _selected?.Parent.EndsWith(
                new RoomLayoutRule(choice.Option.Section, choice.Option.Variant).Choice +
                "." + choice.Option.Variant, StringComparison.OrdinalIgnoreCase) == true)
            ?? parents[0];
    }

    private void CreateStart_Click(object sender, RoutedEventArgs e) =>
        Create(RoomLayoutSection.Start, StartName);
    private void CreateMiddle_Click(object sender, RoutedEventArgs e) =>
        Create(RoomLayoutSection.Middle, MiddleName);
    private void CreateFinish_Click(object sender, RoutedEventArgs e) =>
        Create(RoomLayoutSection.Finish, FinishName);

    private void Create(RoomLayoutSection section, TextBox input)
    {
        if (Document is null) return;
        try
        {
            var option = RoomLayoutService.Create(Document, section, input.Text);
            input.Clear();
            Refresh(section, option.Variant);
            SetStatus($"Created {option.DisplayName}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || _selected is null) return;
        var nodes = RoomLayoutService.SelectedNodes(Document);
        if (nodes.Count == 0) { SetStatus("Select objects on the canvas first."); return; }
        RoomLayoutService.Assign(nodes, _selected.Section, _selected.Variant, _selected.Parent);
        Refresh(_selected.Section, _selected.Variant);
        SetStatus($"Added {nodes.Count} objects to {_selected.DisplayName}.");
    }

    private void MakeShared_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var nodes = RoomLayoutService.SelectedNodes(Document);
        if (nodes.Count == 0) { SetStatus("Select objects on the canvas first."); return; }
        RoomLayoutService.MakeShared(nodes);
        Refresh();
        SetStatus($"Made {nodes.Count} objects shared.");
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || _selected is null) return;
        try
        {
            var section = _selected.Section;
            RoomLayoutService.Rename(Document, _selected, RenameName.Text);
            Refresh(section, RenameName.Text.Trim().Replace(' ', '_'));
            SetStatus("Layout renamed.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || _selected is null || ParentPicker.SelectedItem is not ParentChoice parent) return;
        try
        {
            RoomLayoutService.SetParent(Document, _selected, parent.Option);
            Refresh(_selected.Section, _selected.Variant);
            SetStatus(parent.Option is null ? "Layout can appear in any route." :
                $"Linked to {parent.Option.DisplayName}.");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null || _selected is null) return;
        if (MessageBox.Show($"Delete {_selected.DisplayName} layout? The room objects stay in place.",
            "Delete layout", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RoomLayoutService.Delete(Document, _selected);
        _selected = null;
        Refresh();
        SetStatus("Layout deleted; its objects are now shared.");
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
        var issues = RoomLayoutService.Validate(Document);
        if (issues.Count == 0) SetStatus("All layouts are ready.");
        else
        {
            SetStatus($"{issues.Count} layout issues.");
            MessageBox.Show(string.Join(Environment.NewLine, issues), "Room Layouts",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.CloseRoomLayoutsCommand.Execute(null);
    }

    private void SetStatus(string text)
    {
        Status.Text = text;
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel editor)
            editor.StatusText = text;
    }
}
