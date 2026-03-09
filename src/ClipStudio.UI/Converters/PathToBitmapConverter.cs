using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClipStudio.UI.Converters;

/// <summary>
/// Value converter that turns an absolute file-system path (string) into an Avalonia
/// <see cref="Bitmap"/> suitable for use as an image source.
/// Returns <c>null</c> when the path is null, empty, or the file does not exist.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    /// <summary>Gets a shared singleton instance of the converter.</summary>
    public static readonly PathToBitmapConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(PathToBitmapConverter)} does not support back-conversion.");
}
