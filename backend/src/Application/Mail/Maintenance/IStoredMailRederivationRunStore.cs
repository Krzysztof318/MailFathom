// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Keeps the one re-derivation run a scope may have, from the request that asked for it to the counts it ended with.</summary>
/// <remarks>
/// Separate from <see cref="IStoredMailRederivationStore" />, which is the walk's own cursor over stored mail. The two
/// answer different questions and have different lifetimes: the cursor exists only while a scope is part-walked and is
/// cleared when the walk reaches the end, while the run survives its own ending so that an operator asking afterwards
/// reads what it found rather than silence.
/// </remarks>
public interface IStoredMailRederivationRunStore
{
    /// <summary>Reads the run this scope last had, whether it is still outstanding or has ended.</summary>
    /// <param name="scope">The account, and the one folder of it, whose run is read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The run, or <see langword="null" /> when this scope has never been asked for one.</returns>
    /// <remarks>
    /// One method rather than one for the outstanding run and one for the last, because a scope keeps one row and the
    /// record says for itself whether it ended. A run that has finished is exactly as much of an answer as one still
    /// going, so reporting only the outstanding one would leave "it completed an hour ago" and "you never asked"
    /// looking identical from the outside.
    /// </remarks>
    Task<StoredMailRederivationRun?> FindAsync(StoredMailScope scope, CancellationToken cancellationToken);

    /// <summary>Stages a run — a request, a segment's progress, or its ending — in the session it commits through.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="run">The run as it stands.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the write is staged in the session.</returns>
    /// <remarks>
    /// One run per scope, so this replaces whatever the scope last had rather than appending. Committing the counts with
    /// the segment they account for is what keeps the record and the chain of jobs from disagreeing about how far the
    /// walk has come.
    /// </remarks>
    Task SaveAsync(IPersistenceSession session, StoredMailRederivationRun run, CancellationToken cancellationToken);
}
