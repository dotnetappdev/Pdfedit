using System.Globalization;
using System.Windows.Data;

namespace PdfEdit.Converters;

/// <summary>Converts 0-based page index to 1-based display, and back.</summary>
[ValueConversion(typeof(int), typeof(double))]
public class ZeroToOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (double)(i + 1) : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? (int)d - 1 : 0;
}

/// <summary>Converts zoom factor (0.0–5.0) to percentage (0–500), and back.</summary>
[ValueConversion(typeof(double), typeof(double))]
public class ZoomPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? d * 100.0 : 100.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? d / 100.0 : 1.0;
}

/// <summary>
/// Converts an enum value to bool by comparing to the converter parameter string.
/// Used to two-way bind radio buttons in the ribbon.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}

/// <summary>Returns true when the value is NOT null.</summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
