using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DebtManager.Desktop.Converters;

/// <summary>
/// Converts boolean to Visibility.
/// Supports ConverterParameter="Inverse" to invert the logic.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        if (value is bool b)
        {
            if (invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        if (value is Visibility v)
        {
            var result = v == Visibility.Visible;
            return invert ? !result : result;
        }
        return false;
    }
}

/// <summary>
/// Converts null/empty to Collapsed, non-null to Visible.
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is string s && string.IsNullOrEmpty(s))
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts boolean value.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}

/// <summary>
/// Converts DateOnly to DateTime for DatePicker binding.
/// </summary>
public sealed class DateOnlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateOnly dateOnly)
            return dateOnly.ToDateTime(TimeOnly.MinValue);
        return DateTime.Today;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
            return DateOnly.FromDateTime(dateTime);
        return DateOnly.FromDateTime(DateTime.Today);
    }
}

/// <summary>
/// Converts string to Visibility based on equality with parameter.
/// </summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var valueStr = value?.ToString() ?? "";
        var paramStr = parameter?.ToString() ?? "";
        return string.Equals(valueStr, paramStr, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Formats money value with currency.
/// </summary>
public sealed class MoneyFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            var currency = parameter?.ToString() ?? "EGP";
            return $"{d:N2} {currency}";
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts health status to color.
/// </summary>
public sealed class HealthStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.ToLowerInvariant() ?? "";
        return status switch
        {
            "healthy" or "active" or "paid" => "#10B981", // Green
            "atrisk" or "at risk" => "#F59E0B", // Yellow
            "overdue" or "delinquent" or "critical" => "#EF4444", // Red
            "closed" => "#6B7280", // Gray
            _ => "#A0A0A5" // Default gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Shows Visible when collection is empty, Collapsed otherwise.
/// </summary>
public sealed class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ICollection collection)
            return collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        
        if (value is int count)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Shows Visible when collection is NOT empty, Collapsed otherwise.
/// </summary>
public sealed class CollectionNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ICollection collection)
            return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Shows Visible when count > 0, Collapsed otherwise.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is ICollection collection)
            return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Shows Visible when decimal > 0, Collapsed otherwise.
/// </summary>
public sealed class DecimalToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is double dbl)
            return dbl > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is int i)
            return i > 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Shows Visible when string is not null or empty, Collapsed otherwise.
/// </summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrEmpty(s))
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a progress ratio (0.0–1.0) to a pixel width.
/// ConverterParameter specifies the max width (e.g. "200").
/// </summary>
public sealed class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var ratio = 0.0;
        if (value is double d) ratio = d;
        else if (value is decimal dec) ratio = (double)dec;

        var maxWidth = 200.0;
        if (parameter is string ps && double.TryParse(ps, NumberStyles.Any, CultureInfo.InvariantCulture, out var mw))
            maxWidth = mw;

        return Math.Max(0, Math.Min(maxWidth, ratio * maxWidth));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
