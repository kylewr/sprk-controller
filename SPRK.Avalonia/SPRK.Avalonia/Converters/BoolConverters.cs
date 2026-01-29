using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SPRK.Avalonia.Converters;

public static class BoolConverters
{
    public static readonly IValueConverter ToBoxBrushes =
        new FuncValueConverter<bool, IBrush>(b => b ? Brushes.LightGreen : Brushes.LightGray);
}