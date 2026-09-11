// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Signals;

/// <summary>Mints and spends the one-connection tickets the signal hub is opened against.</summary>
/// <remarks>
/// <para>
/// <b>A browser cannot put a header on a WebSocket.</b> The client signs in with a credential it sets on the
/// <c>Authorization</c> header, and neither the WebSocket nor the server-sent-events API in a browser carries one; what
/// the SignalR client offers instead is an <c>access_token</c> query parameter, and putting this surface's credential —
/// a password-derived one among them — into a query string writes it into every access log on the path. So the
/// connection is opened against a ticket instead: an authenticated route mints one, the client hands it to the
/// connection, and the hub spends it.
/// </para>
/// <para>
/// <b>A ticket authenticates one connection and authorizes nothing.</b> It names the user the credential behind it
/// already named, it is drawn from a cryptographically secure source, it lives for <see cref="Lifetime" />, and it is
/// removed the first time it is presented — so a ticket read out of a log or a browser history is a ticket that has
/// already been spent or has already expired.
/// </para>
/// <para>
/// <b>The secret half is proved against a digest, in constant time.</b> A ticket is an identifier and a secret written
/// together, and only the identifier is looked up: what the deployment holds is a SHA-256 digest of the secret, so a
/// row read out of the database or out of a backup opens no connection. The presented secret is hashed and the two
/// digests compared with <see cref="CryptographicOperations.FixedTimeEquals" />, which spends the same time on a value
/// that shares a prefix with a live ticket as on one that does not.
/// </para>
/// <para>
/// <b>The ticket lives in PostgreSQL rather than in this process, and that is what lets a deployment scale out.</b>
/// The minting route and the connection are two requests a load balancer places independently, and the connection
/// skips negotiation precisely so that nothing has to route them together — so a ticket held in the process that
/// minted it would be redeemable about one time in the replica count, silently. It does not live in the optional
/// signal backplane either, because an authentication path must not depend on a component a deployment may not run.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md">ADR 0032</see>
/// records both halves of that.
/// </para>
/// <para>
/// <b>What a connection can spend is bounded here rather than by the surface.</b> The hub is mapped outside the client
/// route group and has authenticated nothing when a ticket arrives, so a value without the shape of a minted ticket is
/// refused before any statement runs, and at most <see cref="MostRedemptionsInFlight" /> redemptions are in flight in
/// this process at once. A flood of handshakes therefore holds that many connections rather than the pool the rest of
/// the deployment reads mail through; what it can still do while it lasts is crowd legitimate handshakes out of live
/// updates, which costs signals and never mail, because the client's own re-read is the guarantee.
/// </para>
/// </remarks>
internal sealed class ClientSignalTickets
{
    /// <summary>How long a minted ticket may be presented for.</summary>
    /// <remarks>Long enough for a client to mint one and open a connection with it, including a slow network, and short enough that one left in an access log or a browser's own history is worthless by the time anybody reads it.</remarks>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>The most outstanding tickets the deployment holds before minting refuses.</summary>
    /// <remarks>
    /// A bound at a boundary, and the deployment's rather than a process's: it is counted in the same statement that
    /// writes the ticket, so raising the replica count does not multiply it. Minting is an authenticated route behind
    /// this surface's rate limiter, and this is what keeps a credential that is nonetheless spending it from growing
    /// the table. Reaching it is a refusal rather than an eviction, because evicting somebody else's live ticket would
    /// turn one caller's noise into another caller's failed connection.
    /// </remarks>
    internal const int MostOutstandingTickets = 10_000;

    /// <summary>The most redemptions this process has in flight at once before a handshake is refused unread.</summary>
    /// <remarks>
    /// Sized well below the connection pool the rest of the deployment reads mail through, because redemption runs on
    /// the hub, which has authenticated nothing: without this, a flood of handshakes would each hold a connection out
    /// of that pool. A handshake arriving while it is full is refused without a statement, exactly as any other
    /// unusable ticket is, and its client retries on the backoff it already has.
    /// </remarks>
    internal const int MostRedemptionsInFlight = 16;

    /// <summary>How many bytes of the ticket name it, and how many prove it.</summary>
    private const int IdentifierByteCount = 16;
    private const int SecretByteCount = 32;

    /// <summary>What separates the two halves, chosen because it is safe in a query string and absent from base64url.</summary>
    private const char Separator = '.';

    /// <summary>The most a presented value may be before it is refused unread.</summary>
    /// <remarks>
    /// A bound this boundary states itself rather than one it inherits. What arrives is a query-string parameter off a
    /// WebSocket handshake, so its length is bounded today by Kestrel's request-line limit and by whatever a reverse
    /// proxy in front of it allows — neither of which is this type's to rely on, and both of which an operator
    /// configures. A ticket this type minted is sixty-six characters, so anything past this is not one.
    /// </remarks>
    private const int LongestPresentedTicket = 256;

    private readonly IClientSignalTicketStore store;
    private readonly TimeProvider timeProvider;

    private long nextSweepTicks;
    private int redemptionsInFlight;

    /// <summary>Initializes the store over where tickets are held and the clock their lifetimes are measured against.</summary>
    /// <param name="store">Where the deployment holds the tickets it has minted and not yet spent.</param>
    /// <param name="timeProvider">Measures when a ticket was minted and whether it has expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ClientSignalTickets(IClientSignalTicketStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.timeProvider = timeProvider;
        this.nextSweepTicks = (timeProvider.GetUtcNow() + Lifetime).UtcTicks;
    }

    /// <summary>Mints a ticket for one user, or reports that the deployment holds as many as it will hold.</summary>
    /// <param name="user">The user the credential that reached the minting route named.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The minted ticket and when it expires, or <see langword="null" /> when the bound is reached.</returns>
    /// <exception cref="ClientSignalTicketStoreUnavailableException">Thrown when the deployment's tickets could not be reached, which the route answers rather than minting a ticket nothing holds.</exception>
    internal async Task<MintedClientSignalTicket?> MintAsync(MailUserId user, CancellationToken cancellationToken)
    {
        await this.SweepExpiredIfDueAsync(cancellationToken);

        var identifier = RandomText(IdentifierByteCount);
        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);
        var expiresAt = this.timeProvider.GetUtcNow() + Lifetime;

        var held = await this.store.TryMintAsync(
            identifier,
            user,
            SHA256.HashData(secret),
            expiresAt,
            MostOutstandingTickets,
            cancellationToken);

        return held
            ? new MintedClientSignalTicket(
                string.Concat(identifier, Separator.ToString(), Base64Url.EncodeToString(secret)),
                expiresAt)
            : null;
    }

    /// <summary>Spends a presented ticket, reporting the user it named.</summary>
    /// <param name="presented">What the connection carried, which is whatever a caller wrote and is therefore untrusted.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The user, or <see langword="null" /> where the ticket is malformed, unknown, expired, already spent, or arrived while this process was already redeeming as many as it will at once.</returns>
    /// <exception cref="ClientSignalTicketStoreUnavailableException">Thrown when the deployment's tickets could not be reached, which the hub answers by refusing the connection.</exception>
    /// <remarks>
    /// The shape is judged before the statement rather than after it — the length, the separator, and the encoding —
    /// so a value nothing minted costs this deployment no database work at all. Removing before comparing is what makes
    /// a ticket single-use even against two connections presenting it at once, whichever replica each of them reaches:
    /// the loser finds no row to remove and is refused.
    /// </remarks>
    internal async Task<MailUserId?> RedeemAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TrySplit(presented, out var identifier, out var secret))
        {
            return null;
        }

        if (Interlocked.Increment(ref this.redemptionsInFlight) > MostRedemptionsInFlight)
        {
            Interlocked.Decrement(ref this.redemptionsInFlight);

            return null;
        }

        try
        {
            if (await this.store.RedeemAsync(identifier, cancellationToken) is not { } ticket)
            {
                return null;
            }

            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(secret), ticket.SecretDigest.Span))
            {
                return null;
            }

            return this.timeProvider.GetUtcNow() <= ticket.ExpiresAt ? ticket.User : null;
        }
        finally
        {
            Interlocked.Decrement(ref this.redemptionsInFlight);
        }
    }

    /// <summary>Reads a presented value as the two halves a minted ticket is written as, refusing anything else.</summary>
    /// <returns><see langword="true" /> when the value has the shape of a ticket this deployment mints.</returns>
    private static bool TrySplit(string? presented, out string identifier, out byte[] secret)
    {
        identifier = string.Empty;
        secret = [];

        // Bounded before it is walked rather than after, which is the order every other untrusted length here is read
        // in: a value past this is refused without an index, a slice, or a decode having been spent on it.
        if (string.IsNullOrEmpty(presented) || presented.Length > LongestPresentedTicket)
        {
            return false;
        }

        var separator = presented.IndexOf(Separator, StringComparison.Ordinal);

        if (separator <= 0 || separator == presented.Length - 1)
        {
            return false;
        }

        var proof = presented.AsSpan(separator + 1);

        if (!Base64Url.IsValid(proof))
        {
            return false;
        }

        identifier = presented[..separator];
        secret = Base64Url.DecodeFromChars(proof);

        return true;
    }

    private static string RandomText(int byteCount) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteCount));

    /// <summary>Removes what can no longer be presented, at most once per ticket lifetime.</summary>
    /// <remarks>
    /// On the minting path rather than on a timer, for the reason the anti-replay record sweeps there: a table nothing
    /// is writing to needs no sweeping, and a background timer would keep a process awake to prove it. The interval is
    /// claimed with one atomic exchange, so concurrent mints produce one sweep rather than one each, and it stays a
    /// process's own interval above one replica — two replicas each issuing a bounded delete every thirty seconds
    /// costs less than anything that would have to coordinate them. A removal that fails takes the mint that triggered
    /// it with it, because the insert about to follow reaches the same database over the same pool.
    /// </remarks>
    private async Task SweepExpiredIfDueAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var due = Interlocked.Read(ref this.nextSweepTicks);

        if (now.UtcTicks < due)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref this.nextSweepTicks, (now + Lifetime).UtcTicks, due) != due)
        {
            return;
        }

        await this.store.RemoveExpiredAsync(now, cancellationToken);
    }
}
