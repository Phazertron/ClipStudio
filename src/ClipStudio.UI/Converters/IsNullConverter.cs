using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Value converter that returns <c>true</c> when a value is <c>null</c>, and <c>false</c>
/// when the value is non-null. Used to toggle placeholder visibility for optional images.
/// </summary>
public sealed class IsNullConverter : IValueConverter
{
    /// <summary>Gets the shared singleton instance of this converter.</summary>
    public static readonly IsNullConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IsNullConverter)} does not support back-conversion.");
}
