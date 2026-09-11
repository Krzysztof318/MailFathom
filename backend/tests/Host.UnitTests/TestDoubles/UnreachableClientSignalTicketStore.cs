// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A ticket store that cannot answer, which is what a database the deployment cannot reach looks like from above the port.</summary>
/// <remarks>
/// It exists so the store's one security property can fail a test rather than only be asserted in prose: a store that
/// cannot say whether a ticket stands must leave the hub unable to admit, and a <c>catch</c> added later that answered
/// with a user would otherwise pass every test written against a store that always answers.
/// </remarks>
internal sealed class UnreachableClientSignalTicketStore : IClientSignalTicketStore
{
    /// <inheritdoc />
    public Task<bool> TryMintAsync(
        string identifier,
        MailUserId user,
        ReadOnlyMemory<byte> secretDigest,
        DateTimeOffset expiresAt,
        int mostOutstanding,
        CancellationToken cancellationToken) =>
        throw Unavailable("The ticket could not be held because this test's store cannot answer.");

    /// <inheritdoc />
    public Task<RedeemedClientSignalTicket?> RedeemAsync(string identifier, CancellationToken cancellationToken) =>
        throw Unavailable("The ticket could not be spent because this test's store cannot answer.");

    /// <inheritdoc />
    public Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken) =>
        throw Unavailable("The expired tickets could not be removed because this test's store cannot answer.");

    private static ClientSignalTicketStoreUnavailableException Unavailable(string message) =>
        new(message, new InvalidOperationException("The database is unreachable."));
}
