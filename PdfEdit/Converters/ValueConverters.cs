using System.Globalization;
using System.Windows;
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

/// <summary>Maps bool to Visibility: true → Visible, false → Collapsed.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Returns !value for bool inputs.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>Maps bool to Visibility: false → Visible, true → Collapsed (inverse of BoolToVisibilityConverter).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}

/// <summary>Maps bool to FontWeight: true → Bold, false → Normal.</summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.FontWeight fw && fw == System.Windows.FontWeights.Bold;
}

/// <summary>Maps bool to FontStyle: true → Italic, false → Normal.</summary>
public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.FontStyle fs && fs == System.Windows.FontStyles.Italic;
}

/// <summary>Maps bool to TextDecorationCollection: true → Underline, false → null.</summary>
public class BoolToTextDecorationConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.TextDecorations.Underline : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null;
}

/// <summary>Converts opacity 0.0–1.0 to percent 0–100 for spinners.</summary>
public class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Round(d * 100) : 100.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Clamp(d / 100.0, 0.0, 1.0) : 1.0;
}

/// <summary>Converts float opacity 0.0f–1.0f to percent 0–100 for spinners (and back to float).</summary>
public class OpacityPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is float f ? Math.Round(f * 100.0) : (value is double d ? Math.Round(d * 100.0) : 100.0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double dv ? (float)Math.Clamp(dv / 100.0, 0.1, 1.0) : 0.4f;
}

/// <summary>Converts an enum value to Visibility by comparing its ToString() to the converter parameter.</summary>
public class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null && parameter != null && value.ToString() == parameter.ToString()
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Converts a pixel gap (double) to a left-side Thickness margin.</summary>
public class DoubleToLeftMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(value is double d ? d : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Thickness t ? t.Left : 0.0;
}

/// <summary>Converts a pixel gap (double) to a right-side Thickness margin.</summary>
public class DoubleToRightMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(0, 0, value is double d ? d : 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Thickness t ? t.Right : 0.0;
}

/// <summary>Converts bool to TextWrapping (true = Wrap, false = NoWrap).</summary>
public class BoolToTextWrapConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.TextWrapping.Wrap : System.Windows.TextWrapping.NoWrap;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.TextWrapping w && w == System.Windows.TextWrapping.Wrap;
}

/// <summary>Converts a WPF Color to a SolidColorBrush for binding to Background/Foreground.</summary>
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.Media.Color c ? new System.Windows.Media.SolidColorBrush(c) : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.Media.SolidColorBrush b ? b.Color : System.Windows.Media.Colors.Transparent;
}
