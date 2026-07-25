// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

/// <summary>Represents a short-lived application-owned persistence session shared by repositories that participate in one local transaction.</summary>
public interface IPersistenceSession : IAsyncDisposable
{
    /// <summary>Attempts to commit all repository writes joined to this session.</summary>
    /// <param name="cancellationToken">Cancels the commit or concurrency-conflict rollback.</param>
    /// <returns>The commit outcome. The session is invalid after any returned outcome.</returns>
    Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken);
}
