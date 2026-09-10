using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Models;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Walks the duplicates a scan turned up, asks what to do with each, and imports the ones the user
/// wants kept.
/// </summary>
/// <remarks>
/// Import refuses a duplicate rather than deciding for the user, which leaves the decision to be
/// made somewhere. Doing it here, after the scan, rather than inside the import loop means a long
/// scan is never blocked waiting on a dialog.
/// </remarks>
public static class DuplicateImportResolver
{
    /// <summary>
    /// Resolves every duplicate in a set of import results.
    /// </summary>
    /// <param name="results">The results of a scan.</param>
    /// <param name="ask">
    /// Asks the user about one duplicate. Answering with
    /// <see cref="DuplicateResolution.ApplyToRemaining"/> stops any further asking and applies the
    /// same decision to the rest.
    /// </param>
    /// <param name="importAnyway">
    /// Imports a file despite the duplicate, returning the new clip's identifier, or null when the
    /// import failed.
    /// </param>
    /// <param name="link">
    /// Links the newly imported clip to the one it duplicates. Null when linking is unavailable,
    /// in which case "import and link" degrades to a plain import rather than failing.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>How many were imported and how many were skipped.</returns>
    public static async Task<DuplicateResolutionSummary> ResolveAsync(
        IReadOnlyList<ImportResult> results,
        Func<DuplicateClipPrompt, Task<DuplicateResolution>> ask,
        Func<string, Task<int?>> importAnyway,
        Func<int, int, Task>? link = null,
        CancellationToken cancellationToken = default)
    {
        var duplicates = results
            .Where(r => r.IsDuplicate && !string.IsNullOrEmpty(r.DuplicateFilePath))
            .ToList();

        if (duplicates.Count == 0) return new DuplicateResolutionSummary(0, 0);

        var imported = 0;
        var skipped  = 0;
        var linked   = 0;

        // Set once the user answers "do this for the rest", after which nothing more is asked.
        DuplicateClipDecision? standingDecision = null;

        for (var i = 0; i < duplicates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var duplicate = duplicates[i];
            var decision  = standingDecision;

            if (decision is null)
            {
                var answer = await ask(new DuplicateClipPrompt(
                    duplicate.DuplicateFilePath!,
                    duplicate.DuplicateOf!,
                    duplicates.Count - i - 1,
                    duplicate.DuplicateMatch));

                decision = answer.Decision;

                if (answer.ApplyToRemaining)
                    standingDecision = answer.Decision;
            }

            if (decision is not (DuplicateClipDecision.ImportAnyway or DuplicateClipDecision.ImportAndLink))
            {
                skipped++;
                continue;
            }

            var newClipId = await importAnyway(duplicate.DuplicateFilePath!);
            if (newClipId is null)
            {
                skipped++;
                continue;
            }

            imported++;

            // The link is a second, separate write. A failure to draw it must not undo an import
            // the user asked for and already got, so it is reported by omission rather than by
            // turning the whole resolution into a skip.
            if (decision == DuplicateClipDecision.ImportAndLink
                && link is not null
                && duplicate.DuplicateOf is not null)
            {
                await link(newClipId.Value, duplicate.DuplicateOf.Id);
                linked++;
            }
        }

        return new DuplicateResolutionSummary(imported, skipped, linked);
    }
}
