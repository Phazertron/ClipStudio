using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Value converter that returns <c>true</c> when an integer value is greater than zero.
/// Used to control the visibility of badge overlays on navigation items.
/// </summary>
public sealed class IsPositiveConverter : IValueConverter
{
    /// <summary>Gets the shared singleton instance of this converter.</summary>
    public static readonly IsPositiveConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IsPositiveConverter)} does not support back-conversion.");
}
