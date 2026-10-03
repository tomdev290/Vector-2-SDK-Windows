using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Vector2LevelEditor.Views.ProjectManager.Studios;

namespace Vector2LevelEditor.Views;

public sealed class XmlAuthoringView : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(XmlAuthoringView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public XmlAuthoringView()
    {
        var editor = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 11, AcceptsReturn = true,
            AcceptsTab = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        editor.SetBinding(TextBox.TextProperty, new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Content = new TriggerXmlEditor(editor, false);
    }
}
