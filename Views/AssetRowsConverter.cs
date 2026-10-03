using System.Globalization;
using System.Windows.Data;
using Vector2LevelEditor.Models;

namespace Vector2LevelEditor.Views;

public sealed class AssetRowsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable<TextureAsset> assets ? assets.Chunk(4).ToArray() : Array.Empty<TextureAsset[]>();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
