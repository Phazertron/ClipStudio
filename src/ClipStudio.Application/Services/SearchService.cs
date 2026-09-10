using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Answers one search term across everything the library holds.
/// </summary>
/// <remarks>
/// The filter panel answers "narrow the library to these clips". This answers a different question:
/// "where is the thing I am thinking of", which might be a clip, a highlight, a tag, a player or
/// something somebody said. Each kind is queried separately and capped, so one kind with hundreds
/// of matches cannot bury the others.
/// </remarks>
public sealed class SearchService : ISearchService
{
    /// <summary>The most results shown per group before the rest are counted instead.</summary>
    /// <remarks>
    /// Small on purpose. The flyout is for recognising something at a glance; anything longer is
    /// what the filter panel is for, and the group says how many more there are.
    /// </remarks>
    private const int PerGroupLimit = 5;

    /// <summary>The shortest term worth searching for.</summary>
    /// <remarks>
    /// A single character matches most of the library, which is slow to produce and useless to read.
    /// </remarks>
    public const int MinimumTermLength = 2;

    private readonly IClipService _clips;
    private readonly ITagService _tags;
    private readonly IPlayerService _players;
    private readonly IHighlightRepository _highlights;
    private readonly ITranscriptionRepository _transcriptions;
    private readonly ILogger<SearchService> _logger;

    /// <summary>Initialises a new <see cref="SearchService"/>.</summary>
    /// <param name="clips">The clip service.</param>
    /// <param name="tags">The tag service.</param>
    /// <param name="players">The player service.</param>
    /// <param name="highlights">The highlight repository.</param>
    /// <param name="transcriptions">The transcription repository.</param>
    /// <param name="logger">The logger.</param>
    public SearchService(
        IClipService clips,
        ITagService tags,
        IPlayerService players,
        IHighlightRepository highlights,
        ITranscriptionRepository transcriptions,
        ILogger<SearchService> logger)
    {
        _clips          = clips;
        _tags           = tags;
        _players        = players;
        _highlights     = highlights;
        _transcriptions = transcriptions;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResultGroup>> SearchAsync(
        string term, bool includeCaptions = false, CancellationToken cancellationToken = default)
    {
        term = term.Trim();
        if (term.Length < MinimumTermLength) return [];

        var groups = new List<SearchResultGroup>();

        // Each kind is asked for independently, and a failure in one is not allowed to empty the
        // others - a missing transcript table should not stop you finding a clip by name.
        await AddAsync(groups, async () => [await ClipsAsync(term, cancellationToken)], "clips");
        await AddAsync(groups, async () => [await HighlightsAsync(term, cancellationToken)], "highlights");
        await AddAsync(groups, () => TagsAndGamesAsync(term, cancellationToken), "tags");
        await AddAsync(groups, async () => [await PlayersAsync(term, cancellationToken)], "players");

        if (includeCaptions)
            await AddAsync(groups, async () => [await CaptionsAsync(term, cancellationToken)], "captions");

        return groups;
    }

    /// <summary>Runs one query, keeping a failure from emptying the rest.</summary>
    /// <param name="groups">The groups being built.</param>
    /// <param name="query">The query to run. May produce more than one group.</param>
    /// <param name="what">What is being searched, for the log.</param>
    /// <remarks>
    /// Empty groups are dropped rather than shown as a heading with nothing under it.
    /// </remarks>
    private async Task AddAsync(
        List<SearchResultGroup> groups,
        Func<Task<IReadOnlyList<SearchResultGroup>>> query,
        string what)
    {
        try
        {
            groups.AddRange((await query()).Where(g => g.Items.Count > 0));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Searching {What} failed.", what);
        }
    }

    private async Task<SearchResultGroup> ClipsAsync(string term, CancellationToken ct)
    {
        var matches = await _clips.SearchAsync(new ClipSearchQuery { SearchText = term }, ct);

        var items = matches
            .Take(PerGroupLimit)
            .Select(c => new SearchResultItem(
                SearchResultKind.Clip,
                c.FileName,
                c.CreatedAt.ToLocalTime().ToString("dd MMM yyyy"),
                ClipId: c.Id))
            .ToList();

        return new SearchResultGroup(SearchResultKind.Clip, "Clips", items, matches.Count);
    }

    private async Task<SearchResultGroup> HighlightsAsync(string term, CancellationToken ct)
    {
        // One over the limit, so the group can say there are more without a second count query.
        var matches = await _highlights.SearchByLabelAsync(term, PerGroupLimit + 1, ct);

        var items = matches
            .Take(PerGroupLimit)
            .Select(h => new SearchResultItem(
                SearchResultKind.Highlight,
                string.IsNullOrWhiteSpace(h.Label) ? "(unlabelled)" : h.Label,
                h.ClipFileName,
                ClipId: h.ClipId,
                SeekTo: h.StartTime))
            .ToList();

        return new SearchResultGroup(SearchResultKind.Highlight, "Highlights", items, matches.Count);
    }

    /// <summary>
    /// Finds matching tags, split into games and everything else.
    /// </summary>
    /// <param name="term">The search term.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tag group and, when there are any, the game group.</returns>
    /// <remarks>
    /// Both come from one read of the tag table. They are shown separately because filtering by a
    /// game is a different act from filtering by a general tag, and the two are easy to confuse
    /// under a shared heading.
    /// </remarks>
    private async Task<IReadOnlyList<SearchResultGroup>> TagsAndGamesAsync(string term, CancellationToken ct)
    {
        var all = await _tags.GetAllAsync(ct);

        var matched = all
            .Where(t => t.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var games = matched.Where(t => t.Type == TagType.Game).ToList();
        var plain = matched.Where(t => t.Type != TagType.Game).ToList();

        return
        [
            Build(SearchResultKind.Game, "Games", games),
            Build(SearchResultKind.Tag, "Tags", plain),
        ];

        static SearchResultGroup Build(SearchResultKind kind, string title, List<Core.Entities.Tag> tags)
            => new(
                kind,
                title,
                tags.Take(PerGroupLimit)
                    .Select(t => new SearchResultItem(kind, t.Name, EntityId: t.Id))
                    .ToList(),
                tags.Count);
    }

    private async Task<SearchResultGroup> PlayersAsync(string term, CancellationToken ct)
    {
        var all = await _players.GetAllAsync(ct);

        var matched = all
            .Where(p => p.DisplayName.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new SearchResultGroup(
            SearchResultKind.Player,
            "Players",
            matched.Take(PerGroupLimit)
                   .Select(p => new SearchResultItem(SearchResultKind.Player, p.DisplayName, EntityId: p.Id))
                   .ToList(),
            matched.Count);
    }

    private async Task<SearchResultGroup> CaptionsAsync(string term, CancellationToken ct)
    {
        var matches = await _transcriptions.SearchSegmentsAsync(term, PerGroupLimit + 1, ct);

        var items = matches
            .Take(PerGroupLimit)
            .Select(c => new SearchResultItem(
                SearchResultKind.Caption,
                c.Text.Trim(),
                $"{c.ClipFileName} at {Format(c.StartTime)}",
                ClipId: c.ClipId,
                SeekTo: c.StartTime))
            .ToList();

        return new SearchResultGroup(SearchResultKind.Caption, "Captions", items, matches.Count);
    }

    /// <summary>Formats a position the way the player readouts do.</summary>
    /// <param name="value">The position.</param>
    /// <returns>A <c>h:mm:ss</c> or <c>m:ss</c> string.</returns>
    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
