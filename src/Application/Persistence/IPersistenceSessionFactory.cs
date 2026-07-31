// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Application.Persistence;

/// <summary>Creates explicit persistence sessions for local write operations that span multiple stores.</summary>
/// <remarks>
/// EF Core opens a transaction only through the <c>Database</c> facade of a concrete <c>DbContext</c>, which
/// <c>Application</c> must not see, so the seam has no published interface to take.
/// <see href="../../../docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// chose an explicit session over an ambient one, and this contract is what keeps the transaction boundary visible in
/// the signature of the write that spans it rather than hidden in asynchronous flow state.
/// </remarks>
public interface IPersistenceSessionFactory
{
    /// <summary>Begins a short-lived provider-neutral session for one local synchronization write batch.</summary>
    Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken);
}
