// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

/// <summary>Represents a short-lived application-owned persistence session shared by repositories that participate in one local transaction.</summary>
public interface IPersistenceSession : IAsyncDisposable
{
    /// <summary>Commits all repository writes joined to this session. The session is invalid for further use after commit.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
