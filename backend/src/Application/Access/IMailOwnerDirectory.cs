// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

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

    /// <summary>Reads the envelope of one owner this deployment holds.</summary>
    /// <param name="owner">The owner whose envelope is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owner's envelope, or <see langword="null" /> when this deployment holds no such owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// It stands beside the roster rather than being composed from it, because the two answer for different callers.
    /// An owner-facing surface asks what this deployment records about the person who authenticated, and reading the
    /// roster to filter it down to one would compose a deployment-wide catalog of people to answer a question about
    /// one of them. The label is what such a surface is after: it is the one thing the envelope holds that a person is
    /// shown, and it is not in the document beside it.
    /// </remarks>
    Task<MailOwnerRecord?> ReadOwnerAsync(MailOwnerId owner, CancellationToken cancellationToken);
}
