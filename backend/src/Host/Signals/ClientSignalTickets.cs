// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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
/// <b>A ticket authenticates one connection and authorizes nothing.</b> It names the owner the credential behind it
/// already named, it is drawn from a cryptographically secure source, it lives for <see cref="Lifetime" />, and it is
/// removed the first time it is presented — so a ticket read out of a log or a browser history is a ticket that has
/// already been spent or has already expired.
/// </para>
/// <para>
/// <b>The secret half is compared in constant time.</b> A ticket is an identifier and a secret written together, and
/// only the identifier is looked up: the secret is compared with <see cref="CryptographicOperations.FixedTimeEquals" />,
/// so the comparison spends the same time on a value that shares a prefix with a live ticket as on one that does not.
/// A dictionary keyed by the secret itself would leak that through its own lookup.
/// </para>
/// <para>
/// Kept in memory rather than in the database, because the thing it authenticates is a connection to this process and a
/// deployment behind a load balancer needs sticky routing for the connection itself regardless. A restart therefore
/// costs whatever tickets were outstanding, which is a client reconnecting a moment later.
/// </para>
/// </remarks>
internal sealed class ClientSignalTickets
{
    /// <summary>How long a minted ticket may be presented for.</summary>
    /// <remarks>Long enough for a client to mint one and open a connection with it, including a slow network, and short enough that one left in an access log or a browser's own history is worthless by the time anybody reads it.</remarks>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>The most outstanding tickets before minting sweeps and then refuses.</summary>
    /// <remarks>A bound at a boundary: minting is an authenticated route behind this surface's rate limiter, and this is what keeps a credential that is nonetheless spending it from growing the process's memory. Reaching it is a refusal rather than an eviction, because evicting somebody else's live ticket would turn one caller's noise into another caller's failed connection.</remarks>
    internal const int MostOutstandingTickets = 10_000;

    /// <summary>How many bytes of the ticket name it, and how many prove it.</summary>
    private const int IdentifierByteCount = 16;
    private const int SecretByteCount = 32;

    /// <summary>What separates the two halves, chosen because it is safe in a query string and absent from base64url.</summary>
    private const char Separator = '.';

    private readonly ConcurrentDictionary<string, OutstandingTicket> outstanding = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the store over the clock its lifetimes are measured against.</summary>
    /// <param name="timeProvider">Measures when a ticket was minted and whether it has expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public ClientSignalTickets(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Mints a ticket for one owner, or reports that too many stand outstanding.</summary>
    /// <param name="owner">The owner the credential that reached the minting route named.</param>
    /// <returns>The minted ticket and when it expires, or <see langword="null" /> when the bound is reached.</returns>
    internal MintedClientSignalTicket? Mint(MailOwnerId owner)
    {
        // Swept only where the bound is what would refuse this mint, because that is the only moment the difference
        // between a live ticket and a spent one matters. Sweeping on every mint would walk the whole store per
        // connection for a store that is nearly always tiny, which is a cost paid on the ordinary path to tidy state
        // the bound below already limits.
        if (this.outstanding.Count >= MostOutstandingTickets)
        {
            this.SweepExpired();
        }

        if (this.outstanding.Count >= MostOutstandingTickets)
        {
            return null;
        }

        var identifier = RandomText(IdentifierByteCount);
        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);
        var expiresAt = this.timeProvider.GetUtcNow() + Lifetime;

        this.outstanding[identifier] = new OutstandingTicket(owner, secret, expiresAt);

        return new MintedClientSignalTicket(
            string.Concat(identifier, Separator.ToString(), Base64Url.EncodeToString(secret)),
            expiresAt);
    }

    /// <summary>Spends a presented ticket, reporting the owner it named.</summary>
    /// <param name="presented">What the connection carried, which is whatever a caller wrote and is therefore untrusted.</param>
    /// <returns>The owner, or <see langword="null" /> where the ticket is malformed, unknown, expired, or already spent.</returns>
    /// <remarks>Removing before comparing is what makes a ticket single-use even against two connections presenting it at once: the loser finds nothing to remove and is refused, whichever of them wrote the value first.</remarks>
    internal MailOwnerId? Redeem(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return null;
        }

        var separator = presented.IndexOf(Separator, StringComparison.Ordinal);

        if (separator <= 0 || separator == presented.Length - 1)
        {
            return null;
        }

        if (!this.outstanding.TryRemove(presented[..separator], out var ticket))
        {
            return null;
        }

        var proof = presented.AsSpan(separator + 1);

        if (!Base64Url.IsValid(proof)
            || !CryptographicOperations.FixedTimeEquals(Base64Url.DecodeFromChars(proof), ticket.Secret))
        {
            return null;
        }

        return this.timeProvider.GetUtcNow() <= ticket.ExpiresAt ? ticket.Owner : null;
    }

    private static string RandomText(int byteCount) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteCount));

    /// <summary>Removes what can no longer be presented, so the bound above measures live tickets rather than history.</summary>
    private void SweepExpired()
    {
        var now = this.timeProvider.GetUtcNow();

        foreach (var (identifier, ticket) in this.outstanding)
        {
            if (ticket.ExpiresAt < now)
            {
                this.outstanding.TryRemove(identifier, out _);
            }
        }
    }

    private sealed record OutstandingTicket(MailOwnerId Owner, byte[] Secret, DateTimeOffset ExpiresAt);
}
