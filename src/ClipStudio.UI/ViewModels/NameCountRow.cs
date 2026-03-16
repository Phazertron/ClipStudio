namespace ClipStudio.UI.ViewModels;

/// <summary>
/// A simple immutable row used in the statistics dashboard top-lists and rating distribution chart.
/// </summary>
public sealed class NameCountRow
{
    /// <summary>Gets the display name (tag name, game name, player name, or file name).</summary>
    public string Name { get; }

    /// <summary>Gets the occurrence count.</summary>
    public int Count { get; }

    /// <summary>
    /// Gets the bar width as a proportion of the maximum value in the set, in the range [0, 1].
    /// Used to drive a relative bar chart without requiring a converter.
    /// Zero when <paramref name="maxCount"/> is zero.
    /// </summary>
    public double BarFraction { get; }

    /// <summary>Initialises a new instance of <see cref="NameCountRow"/>.</summary>
    /// <param name="name">The display name.</param>
    /// <param name="count">The occurrence count.</param>
    /// <param name="maxCount">The maximum count in the set, used to compute <see cref="BarFraction"/>. Pass 0 to skip bar.</param>
    public NameCountRow(string name, int count, int maxCount)
    {
        Name        = name;
        Count       = count;
        BarFraction = maxCount > 0 ? (double)count / maxCount : 0;
    }
}
