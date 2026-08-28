// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>Reads the owners this deployment holds records for.</summary>
/// <remarks>
/// It answers about the owner records themselves rather than about anything they own, which is why it is the whole of
/// this port: what an owner's mail accounts are is the account catalog's question, and what an owner has configured is
/// their own document's. What a start needs before it serves anything is the roster — who is on it, what each of them
/// is called, and which of them has written their own record — because every one of those decides whether a declaration
/// still reaches that owner.
/// </remarks>
public interface IMailOwnerDirectory
{
    /// <summary>Reads the owners this deployment holds, at most as many as asked for.</summary>
    /// <param name="limit">The greatest number of owners to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owners, in a stable order, and no more than <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The bound is the caller's, and it exists so that a roster longer than a deployment could plausibly hold is
    /// observable rather than read in full. The order is stable so that a caller asking for one owner twice is answered
    /// about the same owner both times.
    /// </remarks>
    Task<IReadOnlyList<MailOwnerRecord>> ReadOwnersAsync(int limit, CancellationToken cancellationToken);
}
