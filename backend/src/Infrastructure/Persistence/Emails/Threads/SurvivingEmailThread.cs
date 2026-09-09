// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Follows a conversation identifier to the conversation that survived whatever merges have happened since.</summary>
/// <remarks>
/// <para>
/// What makes an identifier a tool or a screen published before a merge keep working. Every message of a merged thread
/// is repointed at the survivor in the same transaction as the merge, so this is only ever needed for the identifier
/// itself rather than for finding the membership — which is why every read that already has messages in hand joins on
/// their own column instead.
/// </para>
/// <para>
/// One walk rather than one per reader. Two surfaces resolve a conversation this way — the thread a screen is drawn
/// from and the state derived beside it — and a second copy of the walk would be a second place for the ceiling and the
/// cycle guard to drift.
/// </para>
/// </remarks>
internal static class SurvivingEmailThread
{
    /// <summary>How many merges one identifier is followed through before the chain is treated as unusable.</summary>
    /// <remarks>
    /// A merge points straight at the survivor, so a chain forms only when a survivor is itself merged into a thread
    /// older still — which needs the older thread to have been unreachable until a third message named both. That is
    /// rare and shallow. The ceiling is against a chain that reached the database some other way, where following it
    /// forever would hang a call rather than answer it.
    /// </remarks>
    internal const int MaximumMergeChainWalk = 64;

    /// <summary>Follows a merged conversation to the one it was folded into, or reports that nothing holds it.</summary>
    /// <param name="dbContext">The scoped context the read runs on.</param>
    /// <param name="threadId">The conversation as the caller named it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The surviving conversation, or <see langword="null" /> where no row holds the identifier or the chain is unusable.</returns>
    internal static async Task<Guid?> ResolveAsync(
        MailFathomDbContext dbContext,
        Guid threadId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var candidate = (Guid?)threadId;

        for (var step = 0; step < MaximumMergeChainWalk && candidate is { } current && visited.Add(current); step++)
        {
            var merged = await dbContext.EmailThreads
                .AsNoTracking()
                .Where(thread => thread.Id == current)
                .Select(thread => new { thread.MergedIntoEmailThreadId })
                .SingleOrDefaultAsync(cancellationToken);

            if (merged is null)
            {
                return null;
            }

            if (merged.MergedIntoEmailThreadId is not { } survivor)
            {
                return current;
            }

            candidate = survivor;
        }

        return null;
    }
}
