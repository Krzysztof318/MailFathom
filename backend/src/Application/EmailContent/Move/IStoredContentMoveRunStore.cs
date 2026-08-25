// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Keeps the one move a deployment may have, from the request that asked for it to the counts it ended with.</summary>
/// <remarks>
/// Separate from <see cref="IStoredContentMoveStore" />, which is the walk over the content itself. The two answer
/// different questions: this is what an operator started, paused, and comes back to read, and it survives its own ending
/// so that a finished move and a move nobody ever asked for are not the same silence.
/// </remarks>
public interface IStoredContentMoveRunStore
{
    /// <summary>Reads the move this deployment last had, whether it is running, paused, or finished.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The move, or <see langword="null" /> when none has ever been asked for.</returns>
    Task<StoredContentMoveRun?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Stages the move — the request that started it, a pass's progress, or an operator's decision — in the session it commits through.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="run">The move as it stands.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes once the write is staged in the session.</returns>
    /// <remarks>
    /// One move per deployment, so this replaces whatever the deployment last had rather than appending. Committing a
    /// pass's counts with the position they were reached at is what keeps the record and the walk from disagreeing about
    /// how far the move has come.
    /// </remarks>
    Task SaveAsync(IPersistenceSession session, StoredContentMoveRun run, CancellationToken cancellationToken);
}
