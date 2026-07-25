// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Persistence;

/// <summary>Creates explicit persistence sessions for local write operations that span multiple stores.</summary>
public interface IPersistenceSessionFactory
{
    /// <summary>Begins a short-lived provider-neutral session for one local synchronization write batch.</summary>
    Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken);
}
