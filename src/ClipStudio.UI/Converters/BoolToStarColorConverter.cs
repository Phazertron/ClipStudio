using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to a star-rating foreground brush: gold for <c>true</c> (rated)
/// and dimmed grey for <c>false</c> (empty). Intended for use with the five-star rating controls.
/// </summary>
public sealed class BoolToStarColorConverter : IValueConverter
{
    /// <summary>Gets the singleton instance of <see cref="BoolToStarColorConverter"/>.</summary>
    public static readonly BoolToStarColorConverter Instance = new();

    private static readonly IBrush GoldBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
    private static readonly IBrush GreyBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80));

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? GoldBrush : GreyBrush;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BoolToStarColorConverter)} does not support ConvertBack.");
}
