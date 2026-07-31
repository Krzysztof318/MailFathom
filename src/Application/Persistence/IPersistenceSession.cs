// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Application.Persistence;

/// <summary>Represents a short-lived application-owned persistence session shared by repositories that participate in one local transaction.</summary>
/// <remarks>
/// The session is declared here rather than taken from EF Core's <c>IDbContextTransaction</c> because that type would
/// appear in the signature of every use case that writes, and because this contract carries behavior the provider's
/// does not: a commit reports an optimistic concurrency conflict as a result its caller loops on, which is the lower
/// of the two altitudes
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// signals a conflict at.
/// </remarks>
public interface IPersistenceSession : IAsyncDisposable
{
    /// <summary>Attempts to commit all repository writes joined to this session.</summary>
    /// <param name="cancellationToken">Cancels the commit or concurrency-conflict rollback.</param>
    /// <returns>The commit outcome. The session is invalid after any returned outcome.</returns>
    Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken);
}
