// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Persistence;

/// <summary>Represents a short-lived application-owned persistence session shared by repositories that participate in one local transaction.</summary>
/// <remarks>
/// The session is declared here rather than taken from EF Core's <c>IDbContextTransaction</c> because that type would
/// appear in the signature of every use case that writes, and because this contract carries behavior the provider's
/// does not: a commit reports an optimistic concurrency conflict as a result its caller loops on, which is the lower
/// of the two altitudes
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// signals a conflict at.
/// </remarks>
public interface IPersistenceSession : IAsyncDisposable
{
    /// <summary>Attempts to commit all repository writes joined to this session.</summary>
    /// <param name="cancellationToken">Cancels the commit or concurrency-conflict rollback.</param>
    /// <returns>The commit outcome. The session is invalid after any returned outcome.</returns>
    /// <exception cref="PersistenceTransientFailureException">
    /// Thrown when the commit met a failure the database may not produce again. It is raised rather than reported as
    /// an outcome because nothing this session could repeat would help: the transaction the statements belonged to is
    /// already gone, so the whole unit of work is what a caller repeats. The session is invalid afterwards.
    /// </exception>
    Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken);

    /// <summary>Ends this session on a failure a write raised before the commit, where the database may not raise it again.</summary>
    /// <param name="failure">The failure a write joined to this session raised.</param>
    /// <returns><see langword="true" /> when the failure can clear on its own, in which case the session has ended on it.</returns>
    /// <remarks>
    /// <para>
    /// A write joined to a session does not only stage changes: it issues statements of its own, and a set-based
    /// update or a row lock has already reached the server long before anything is committed. The failure of one of
    /// those never passes through the commit, so this is where it is classified instead — by the session, which is the
    /// only side of this contract that knows what the provider raised, and for the caller above it, which is the only
    /// side that holds the body a replay would run again. The session is invalid afterwards either way.
    /// </para>
    /// <para>
    /// The default answers <see langword="false" />, because a session with no database beneath it has no provider
    /// failure to recognize and nothing to end: what such a session raised is the caller's to report rather than a
    /// race with a connection to replay.
    /// </para>
    /// </remarks>
    bool TryEndOnTransientFailure(Exception failure) => false;
}
