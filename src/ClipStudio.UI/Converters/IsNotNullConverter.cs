using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Value converter that returns <c>true</c> when a value is non-<c>null</c>, and <c>false</c>
/// when the value is <c>null</c>. Used to toggle image visibility for optional bitmaps.
/// </summary>
public sealed class IsNotNullConverter : IValueConverter
{
    /// <summary>Gets the shared singleton instance of this converter.</summary>
    public static readonly IsNotNullConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IsNotNullConverter)} does not support back-conversion.");
}
