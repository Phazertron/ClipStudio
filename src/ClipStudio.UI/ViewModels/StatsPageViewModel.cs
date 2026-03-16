using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Statistics dashboard page.
/// Loads aggregated library statistics via <see cref="IStatsService"/> and exposes
/// display-ready properties for AXAML bindings.
/// </summary>
public sealed partial class StatsPageViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Gets or sets a value indicating whether data is currently being loaded.</summary>
    [ObservableProperty]
    private bool _isLoading;

    // ---- Counts ----

    /// <summary>Gets or sets the total number of active clips.</summary>
    [ObservableProperty] private int _totalClips;

    /// <summary>Gets or sets the number of unreviewed clips.</summary>
    [ObservableProperty] private int _unreviewedClips;

    /// <summary>Gets or sets the number of reviewed clips.</summary>
    [ObservableProperty] private int _reviewedClips;

    /// <summary>Gets or sets the number of archived clips.</summary>
    [ObservableProperty] private int _archivedClips;

    /// <summary>Gets or sets the total number of highlights.</summary>
    [ObservableProperty] private int _totalHighlights;

    /// <summary>Gets or sets the number of favourite clips.</summary>
    [ObservableProperty] private int _favouriteClips;

    /// <summary>Gets or sets the number of rated clips.</summary>
    [ObservableProperty] private int _ratedClips;

    /// <summary>Gets or sets the total number of times clips have been played.</summary>
    [ObservableProperty] private int _totalPlayCount;

    // ---- Duration ----

    /// <summary>Gets or sets the total library duration as a formatted string.</summary>
    [ObservableProperty] private string _totalDurationDisplay = "0h 0m";

    // ---- Rating distribution ----

    /// <summary>Gets or sets the number of clips rated 1 star.</summary>
    [ObservableProperty] private int _rated1;

    /// <summary>Gets or sets the number of clips rated 2 stars.</summary>
    [ObservableProperty] private int _rated2;

    /// <summary>Gets or sets the number of clips rated 3 stars.</summary>
    [ObservableProperty] private int _rated3;

    /// <summary>Gets or sets the number of clips rated 4 stars.</summary>
    [ObservableProperty] private int _rated4;

    /// <summary>Gets or sets the number of clips rated 5 stars.</summary>
    [ObservableProperty] private int _rated5;

    /// <summary>Gets or sets the maximum rating count, used to scale bar chart widths.</summary>
    [ObservableProperty] private int _maxRatingCount;

    // ---- Top lists ----

    /// <summary>Gets the top game tags by clip count.</summary>
    public ObservableCollection<NameCountRow> TopGames { get; } = new();

    /// <summary>Gets the top general tags by clip count.</summary>
    public ObservableCollection<NameCountRow> TopTags { get; } = new();

    /// <summary>Gets the top players by clip count.</summary>
    public ObservableCollection<NameCountRow> TopPlayers { get; } = new();

    /// <summary>Gets the most-played clips.</summary>
    public ObservableCollection<NameCountRow> MostPlayed { get; } = new();

    /// <summary>Gets the command that loads statistics from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Initialises a new instance of <see cref="StatsPageViewModel"/>.</summary>
    /// <param name="scopeFactory">
    /// Factory used to create an isolated DI scope for each database operation,
    /// preventing concurrent root-scope DbContext access.
    /// </param>
    public StatsPageViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        LoadCommand   = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
    }

    private async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        LoadCommand.NotifyCanExecuteChanged();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var statsService = scope.ServiceProvider.GetRequiredService<IStatsService>();
            var stats = await statsService.GetStatisticsAsync();
            Apply(stats);
        }
        catch
        {
            // Non-fatal: stats will be empty on error.
        }
        finally
        {
            IsLoading = false;
            LoadCommand.NotifyCanExecuteChanged();
        }
    }

    private void Apply(LibraryStatistics stats)
    {
        TotalClips      = stats.TotalClips;
        UnreviewedClips = stats.UnreviewedClips;
        ReviewedClips   = stats.ReviewedClips;
        ArchivedClips   = stats.ArchivedClips;
        TotalHighlights = stats.TotalHighlights;
        FavouriteClips  = stats.FavouriteClips;
        RatedClips      = stats.RatedClips;
        TotalPlayCount  = stats.TotalPlayCount;

        var hours   = (int)stats.TotalDuration.TotalHours;
        var minutes = stats.TotalDuration.Minutes;
        TotalDurationDisplay = $"{hours}h {minutes}m";

        Rated1 = stats.RatingDistribution[1];
        Rated2 = stats.RatingDistribution[2];
        Rated3 = stats.RatingDistribution[3];
        Rated4 = stats.RatingDistribution[4];
        Rated5 = stats.RatingDistribution[5];
        MaxRatingCount = Math.Max(1, new[] { Rated1, Rated2, Rated3, Rated4, Rated5 }.Max());

        TopGames.Clear();
        foreach (var (name, count) in stats.TopGames)
            TopGames.Add(new NameCountRow(name, count, MaxRatingCount));

        TopTags.Clear();
        foreach (var (name, count) in stats.TopTags)
            TopTags.Add(new NameCountRow(name, count, 0));

        TopPlayers.Clear();
        foreach (var (name, count) in stats.TopPlayers)
            TopPlayers.Add(new NameCountRow(name, count, 0));

        MostPlayed.Clear();
        foreach (var (fileName, playCount) in stats.MostPlayed)
            MostPlayed.Add(new NameCountRow(fileName, playCount, 0));
    }
}
