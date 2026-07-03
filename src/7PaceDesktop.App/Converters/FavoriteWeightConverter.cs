using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PaceDesktop.App.Converters;

public sealed class FavoriteWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? FontWeights.Bold : FontWeights.Normal;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
