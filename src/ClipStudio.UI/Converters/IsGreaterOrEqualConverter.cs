using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Converts an <see cref="int"/> value to a <see cref="bool"/> indicating whether the value is
/// greater than or equal to the integer supplied in <c>ConverterParameter</c>.
/// Used to render each star button in the star-rating editor.
/// </summary>
public sealed class IsGreaterOrEqualConverter : IValueConverter
{
    /// <summary>Gets the singleton instance of <see cref="IsGreaterOrEqualConverter"/>.</summary>
    public static readonly IsGreaterOrEqualConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int n && parameter is string s && int.TryParse(s, out var threshold))
            return n >= threshold;

        return false;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IsGreaterOrEqualConverter)} does not support ConvertBack.");
}
