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
    /// Imports a file despite the duplicate, returning whether it succeeded.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>How many were imported and how many were skipped.</returns>
    public static async Task<DuplicateResolutionSummary> ResolveAsync(
        IReadOnlyList<ImportResult> results,
        Func<DuplicateClipPrompt, Task<DuplicateResolution>> ask,
        Func<string, Task<bool>> importAnyway,
        CancellationToken cancellationToken = default)
    {
        var duplicates = results
            .Where(r => r.IsDuplicate && !string.IsNullOrEmpty(r.DuplicateFilePath))
            .ToList();

        if (duplicates.Count == 0) return new DuplicateResolutionSummary(0, 0);

        var imported = 0;
        var skipped  = 0;

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
                    duplicates.Count - i - 1));

                decision = answer.Decision;

                if (answer.ApplyToRemaining)
                    standingDecision = answer.Decision;
            }

            if (decision == DuplicateClipDecision.ImportAnyway
                && await importAnyway(duplicate.DuplicateFilePath!))
            {
                imported++;
            }
            else
            {
                skipped++;
            }
        }

        return new DuplicateResolutionSummary(imported, skipped);
    }
}
