using System.Globalization;
using System.Windows.Data;

namespace SchoolManager.App;

/// <summary>
/// Dreht einen Wahrheitswert um - etwa damit ein Feld gesperrt ist, solange
/// „Ganztägig“ gesetzt ist.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
