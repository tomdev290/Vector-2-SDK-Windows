using System.Windows;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.ViewModels;

public sealed class ToolButtonViewModel : NotifyObject
{
    private bool _isSelected;

    public ToolButtonViewModel(EditorTool tool)
    {
        Tool = tool;
        Label = tool.Label();
        Glyph = tool.Glyph();
        IconData = tool.IconData();
        IconVisibility = string.IsNullOrWhiteSpace(IconData) ? Visibility.Collapsed : Visibility.Visible;
        TextVisibility = string.IsNullOrWhiteSpace(IconData) ? Visibility.Visible : Visibility.Collapsed;
    }

    public EditorTool Tool { get; }
    public string Label { get; }
    public string Glyph { get; }
    public string IconData { get; }
    public Visibility IconVisibility { get; }
    public Visibility TextVisibility { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
