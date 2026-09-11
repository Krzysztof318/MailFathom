// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Sessions;

/// <summary>Where the deployment holds the sessions its clients signed in with, so every replica accepts one and every replica honours its revocation.</summary>
/// <remarks>
/// <para>
/// The port exists because the property it carries is the deployment's rather than a process's. A session held by the
/// replica that minted it is a session roughly <c>(N−1)/N</c> of a client's requests are refused by, which the client
/// meets as being signed out; and an operator ending somebody's access would end it on the one replica their
/// administrative request happened to reach.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md">ADR 0033</see>
/// records both halves of that, and why nothing caches what this answers.
/// </para>
/// <para>
/// <b>Two acts are absent from it deliberately.</b> Ending every session of a credential an operator disabled is not a
/// call on this port: it commits with the disable itself, so no exchange can write a session between the two. Ending
/// every session of a deleted credential or an erased user is not one either: the rows reference both, cascading, so an
/// erasure reaches a session through the user directly even where no credential stands behind it.
/// </para>
/// <para>
/// What reaches an implementation is an identifier and a digest, never a presented token: the shape of a value a caller
/// wrote is judged before any statement runs, and what is stored is a digest, so a row read out of the database, a
/// dump, or a backup is not a session.
/// </para>
/// </remarks>
public interface IClientSessionStore
{
    /// <summary>Holds a new session for what a credential admitted, while the user and the credential behind it still admit one and the deployment is under its own bound.</summary>
    /// <param name="row">The identifier, the digest, and the expiry the caller drew for this session.</param>
    /// <param name="grant">What the exchange established, which the session admits until it is renewed, revoked, or expires.</param>
    /// <param name="mostLiveSessions">The most sessions the deployment holds at once, counted by the statement that writes this one.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the session is held, and where it is not, which of the two refusals it was.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the sessions could not be reached, which the caller answers as unavailable rather than by minting a session nothing holds.</exception>
    /// <remarks>
    /// It takes the user row and then the credential row before it writes, in that order and holding both until it
    /// commits, which is what makes an operator's act order against an exchange in flight rather than race it. The
    /// order is the same one an erasure takes by construction, so what a contending pair costs is a wait rather than
    /// one of the two being aborted.
    /// </remarks>
    Task<ClientSessionMintOutcome> TryMintAsync(
        MintedClientSessionRow row,
        ClientSessionGrant grant,
        int mostLiveSessions,
        CancellationToken cancellationToken);

    /// <summary>Reports what the deployment holds under one identifier, without judging it.</summary>
    /// <param name="identifier">The public half of a presented token.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The session, or <see langword="null" /> where the deployment holds none under that identifier.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the sessions could not be reached, which the caller answers as unavailable rather than as unauthenticated.</exception>
    /// <remarks>The one operation on the request path, and one indexed read by the key. Nothing caches it, because a cached session is a revoked session that goes on working for the cache's own window on every replica that had already read it.</remarks>
    Task<HeldClientSession?> FindAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Replaces a live session with a new one, carrying its grant forward, so one sign-in holds one live token however often a client renews.</summary>
    /// <param name="identifier">The public half of the presented token.</param>
    /// <param name="secretDigest">The digest of the presented secret, which the removal is guarded by.</param>
    /// <param name="replacement">The identifier, the digest, and the expiry the caller drew for the replacement.</param>
    /// <param name="renewableFrom">The instant the presented session must not already have expired at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the replacement admits, or <see langword="null" /> where the presented session no longer authenticates or the credential behind it no longer admits one.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the sessions could not be reached, which the caller answers as unavailable rather than by refusing the renewal.</exception>
    /// <remarks>
    /// <para>
    /// Removing the presented row and inserting the replacement in one transaction is what leaves exactly one live
    /// session where two requests present one token, whichever replica each of them reaches — settled by PostgreSQL
    /// rather than by a check either of them makes between two statements.
    /// </para>
    /// <para>
    /// The removal carries the digest in its own condition rather than removing the row and judging the secret
    /// afterwards, because a session survives being presented: a removal keyed on the identifier alone would let
    /// anybody writing the half of a token that is not a secret end somebody else's session.
    /// </para>
    /// </remarks>
    Task<ClientSessionGrant?> RenewAsync(
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        MintedClientSessionRow replacement,
        DateTimeOffset renewableFrom,
        CancellationToken cancellationToken);

    /// <summary>Ends one session, so the token it was signed in with is refused on the next request rather than at its expiry.</summary>
    /// <param name="identifier">The public half of the presented token.</param>
    /// <param name="secretDigest">The digest of the presented secret, which the removal is guarded by.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when a session was ended by this call.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the sessions could not be reached.</exception>
    /// <remarks>Guarded by the digest for the reason the renewal's removal is: the identifier is the half of a token that is not a secret, and a removal keyed on it alone would be a sign-out anybody could perform.</remarks>
    Task<bool> RevokeAsync(string identifier, ReadOnlyMemory<byte> secretDigest, CancellationToken cancellationToken);

    /// <summary>Removes the sessions that can no longer authenticate anything.</summary>
    /// <param name="removableFrom">The instant a session must have expired before to be removed, exclusive because a session expiring at it still authenticates.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the sessions could not be reached, which reaches the operation that triggered the removal rather than being swallowed.</exception>
    /// <remarks>What keeps the bound below counting live sessions rather than history, and what keeps a row naming a user and a credential from standing for a month past the point it authenticated anything.</remarks>
    Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken);
}
