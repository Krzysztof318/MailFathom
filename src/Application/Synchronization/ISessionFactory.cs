// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Synchronization;

/// <summary>Creates explicit persistence sessions for local write operations that span multiple stores.</summary>
public interface ISessionFactory
{
    /// <summary>Begins a short-lived provider-neutral session for one local synchronization write batch.</summary>
    Task<ISession> BeginSessionAsync(CancellationToken cancellationToken);
}
