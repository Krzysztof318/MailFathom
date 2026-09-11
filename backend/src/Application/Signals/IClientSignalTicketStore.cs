// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Signals;

/// <summary>Where the deployment holds the unspent tickets its signal connections are opened against.</summary>
/// <remarks>
/// <para>
/// The port exists because a ticket is minted by whichever replica answered the minting route and presented to
/// whichever replica answered the connection, and nothing routes the two together: the connection skips negotiation, so
/// it is one request the load balancer places on its own. A ticket held in a process is therefore redeemable about one
/// time in the replica count, which is a live channel that silently stops working when an operator scales out.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md">ADR 0032</see>
/// records that and why the ticket does not live in the optional backplane instead.
/// </para>
/// <para>
/// What is held is a user, a digest of the secret half, and when the ticket stops being presentable. The secret itself
/// never reaches the store, so a row read out of the database or out of a backup is not a ticket.
/// </para>
/// </remarks>
public interface IClientSignalTicketStore
{
    /// <summary>Holds one minted ticket, unless the deployment already holds as many as it will hold.</summary>
    /// <param name="identifier">The public half of the ticket, which is what a presentation is looked up by.</param>
    /// <param name="user">The user the credential that reached the minting route named.</param>
    /// <param name="secretDigest">The digest of the secret half, which is what a presentation is compared against.</param>
    /// <param name="expiresAt">When presenting the ticket stops working.</param>
    /// <param name="mostOutstanding">The most tickets the deployment holds at once, counted across every replica.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the ticket was held; <see langword="false" /> when the bound refused it.</returns>
    /// <exception cref="ClientSignalTicketStoreUnavailableException">Thrown when the ticket could not be held, which the caller answers by refusing rather than by minting.</exception>
    /// <remarks>
    /// The bound is the caller's value and the deployment's count, in one statement: a count read and then written
    /// against is a bound several replicas each find room under and then all exceed.
    /// </remarks>
    Task<bool> TryMintAsync(
        string identifier,
        MailUserId user,
        ReadOnlyMemory<byte> secretDigest,
        DateTimeOffset expiresAt,
        int mostOutstanding,
        CancellationToken cancellationToken);

    /// <summary>Spends one ticket, reporting what it held.</summary>
    /// <param name="identifier">The public half a connection presented, which is whatever a caller wrote.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the ticket held, or <see langword="null" /> where no ticket stood under that identifier.</returns>
    /// <exception cref="ClientSignalTicketStoreUnavailableException">Thrown when the store could not answer, which the caller answers by refusing the connection rather than admitting it.</exception>
    /// <remarks>
    /// One statement that removes and returns together, so exactly one presentation of a ticket wins whichever replica
    /// each of them reaches. An implementation that read and then removed would admit both. The expiry travels back
    /// rather than being compared here, because a ticket presented past it is spent as surely as one presented in time.
    /// </remarks>
    Task<RedeemedClientSignalTicket?> RedeemAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Forgets the tickets that can no longer be presented.</summary>
    /// <param name="removableFrom">The instant expiry is judged against: a ticket is dropped once this has passed its recorded expiry.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the expired tickets are gone.</returns>
    /// <exception cref="ClientSignalTicketStoreUnavailableException">Thrown when the removal could not run, which reaches the mint that triggered it rather than being swallowed.</exception>
    /// <remarks>
    /// Bounded by what it can match rather than by a limit the caller passes: an implementation reaches the expired
    /// tickets through an ordering on the expiry, so the work is proportional to what has expired since the last
    /// removal rather than to every ticket ever minted.
    /// </remarks>
    Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken);
}

/// <summary>What a spent ticket held, handed back by the statement that removed it.</summary>
/// <param name="User">The user the ticket was minted for.</param>
/// <param name="SecretDigest">The digest of the secret half, which the presented secret is compared against in constant time.</param>
/// <param name="ExpiresAt">When presenting the ticket stopped working.</param>
public sealed record RedeemedClientSignalTicket(
    MailUserId User,
    ReadOnlyMemory<byte> SecretDigest,
    DateTimeOffset ExpiresAt);
