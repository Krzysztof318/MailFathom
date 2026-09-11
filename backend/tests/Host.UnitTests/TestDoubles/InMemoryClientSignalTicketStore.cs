// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The deployment's unspent signal tickets, held in this process so a test can reach them.</summary>
/// <remarks>
/// It models the three properties the real table has and nothing else: an identifier is held once, a spend removes and
/// returns in one step so only the first of two presentations wins, and a removal drops exactly what has expired.
/// Whether PostgreSQL settles a concurrent pair the way this dictionary does is not a claim a unit test can make — the
/// orchestrated suite is where the composed statements are proven — so what this double buys is the rules the minting
/// above the port applies: the shape it refuses unread, the digest it compares, the expiry it judges, and the sweep it
/// throttles.
/// </remarks>
internal sealed class InMemoryClientSignalTicketStore : IClientSignalTicketStore
{
    private readonly ConcurrentDictionary<string, RedeemedClientSignalTicket> outstanding = new(StringComparer.Ordinal);

    /// <summary>Gets how many removals the minting above this one has asked for.</summary>
    /// <remarks>The sweep is throttled rather than issued per mint, and nothing else observes that: the tickets it drops could not have been presented anyway.</remarks>
    internal int RemovalCount { get; private set; }

    /// <summary>Gets how many identifiers the deployment currently holds.</summary>
    internal int OutstandingCount => this.outstanding.Count;

    /// <inheritdoc />
    public Task<bool> TryMintAsync(
        string identifier,
        MailUserId user,
        ReadOnlyMemory<byte> secretDigest,
        DateTimeOffset expiresAt,
        int mostOutstanding,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            this.outstanding.Count < mostOutstanding
            && this.outstanding.TryAdd(identifier, new RedeemedClientSignalTicket(user, secretDigest, expiresAt)));

    /// <inheritdoc />
    public Task<RedeemedClientSignalTicket?> RedeemAsync(string identifier, CancellationToken cancellationToken) =>
        Task.FromResult(this.outstanding.TryRemove(identifier, out var ticket) ? ticket : null);

    /// <inheritdoc />
    public Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken)
    {
        this.RemovalCount++;

        foreach (var ticket in this.outstanding.Where(ticket => ticket.Value.ExpiresAt <= removableFrom))
        {
            this.outstanding.TryRemove(ticket);
        }

        return Task.CompletedTask;
    }
}
