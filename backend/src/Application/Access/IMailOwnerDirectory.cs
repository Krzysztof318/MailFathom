// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Reads the owners this deployment holds records for.</summary>
/// <remarks>
/// It answers about the owner records themselves rather than about anything they own, which is why it is the whole of
/// this port: what an owner's mail accounts are is the account catalog's question, and what an owner is called is their
/// own record's. The one thing a deployment has to be able to establish before it serves a request is how many owners
/// there are, because everything admitted for one is admitted for a named owner.
/// </remarks>
public interface IMailOwnerDirectory
{
    /// <summary>Reads the owners this deployment holds, at most as many as asked for.</summary>
    /// <param name="limit">The greatest number of owners to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owners, in a stable order, and no more than <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The bound is the caller's, and it exists so that "more than one" can be established without reading a deployment's
    /// whole roster to discover it. The order is stable so that a caller asking for one owner twice is answered about the
    /// same owner both times.
    /// </remarks>
    Task<IReadOnlyList<MailOwnerId>> ReadOwnersAsync(int limit, CancellationToken cancellationToken);
}
