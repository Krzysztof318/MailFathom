// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;

namespace MailFathom.Host.Security.Sessions;

/// <summary>Mints, verifies, renews, and revokes the tokens a signed-in client presents in place of its password.</summary>
/// <remarks>
/// <para>
/// <b>A password is derived once, at the exchange, and never again.</b> HTTP Basic carries no session, so every request
/// presenting one re-derived a PBKDF2 record sized for authenticating a person — around half a second of the process's
/// own processor per call, on a surface a client opens eight requests against to draw one screen. What that cost is for
/// is making a guessed password expensive, which is a property of the sign-in rather than of the eighth request after
/// it. So the sign-in exchanges the credential for one of these, and everything afterwards is judged here, where
/// verifying costs one indexed read and one fixed-time comparison.
/// </para>
/// <para>
/// <b>What is kept is not a password.</b> A token names the credential that minted it and the user that credential
/// resolved, expires by itself, and is refused the moment it is revoked — so a head that keeps one keeps something with
/// a life the deployment controls, rather than a value that stays good until somebody changes a password. That is the
/// whole of what the exchange buys the client's storage, and <c>ADR 0023</c> is where it is recorded.
/// </para>
/// <para>
/// <b>The session lives in PostgreSQL rather than in this process, which is what lets a deployment scale out.</b> A
/// load balancer places every request independently, so a session held by the replica that minted it would be refused
/// by roughly <c>(N−1)/N</c> of them — which a client meets as being signed out — and an operator ending somebody's
/// access would end it on one replica only. It does not live in the optional signal backplane either, because an
/// authentication path must not depend on a component a deployment may not run.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md">ADR 0033</see>
/// records all of that, including why nothing caches what the store answers.
/// </para>
/// <para>
/// <b>The secret half is proved against a digest, in constant time.</b> A token is an identifier and a secret written
/// together, and only the identifier is looked up: what the deployment holds is a SHA-256 digest of the secret, so a
/// row read out of the database, a dump, or a backup is not a session. The presented secret is hashed and the two
/// digests compared with <see cref="CryptographicOperations.FixedTimeEquals" />, which spends the same time on a value
/// sharing a prefix with a live session as on one that does not. The renewal and the revocation compare theirs inside
/// the removing statement instead, which <see cref="IClientSessionStore" /> holds the reasoning for.
/// </para>
/// <para>
/// <b>An operator's act is not a call on this type.</b> Disabling a credential ends its sessions in the same commit
/// that disables it, and deleting a credential or erasing a user ends theirs by cascade — so there is no barrier here
/// to guess a window for, and a credential enabled again is signed in with immediately rather than refused for the
/// length of one.
/// </para>
/// </remarks>
internal sealed class ClientSessionTokens
{
    /// <summary>How long a minted token authenticates for.</summary>
    /// <remarks>
    /// <para>
    /// A month rather than a working day, because this is what a head keeps between starts and the client's own screen
    /// states the number: somebody who asked to be kept signed in is told this device stays signed in for thirty days,
    /// and a deployment minting a shorter session would make that screen say something it does not keep. A client left
    /// open renews rather than expiring, so what this length actually bounds is the abandoned case — a token nobody
    /// presented again, on a machine nobody came back to.
    /// </para>
    /// <para>
    /// It was twelve hours until <c>#1844</c>, which is the same reasoning against a smaller promise: the screen said
    /// nothing about a duration then, so a working day was the whole requirement. What did not change is that it is
    /// not configurable and that ending the credential behind a session ends it whatever is left of this. What did
    /// change is that thirty days is now a length the deployment keeps rather than the most a session could last: a
    /// restart and a rolling upgrade both leave every session standing.
    /// </para>
    /// </remarks>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>What every token this deployment mints begins with.</summary>
    /// <remarks>
    /// The shape a minted API key already carries, with a different word inside it: short, lower-case, ending in an
    /// underscore, so a secret scanner recognizes one and an operator who finds a value they did not expect can tell
    /// which of the two it is. It is also what routes a presented bearer credential to this type rather than to the
    /// key comparison, which is why it is exact rather than decorative.
    /// </remarks>
    internal const string TokenPrefix = "mfs_";

    /// <summary>The most sessions the deployment holds before minting sweeps and then refuses.</summary>
    /// <remarks>
    /// A bound at a boundary, for the reason <see cref="Signals.ClientSignalTickets.MostOutstandingTickets" /> carries
    /// one, and the deployment's rather than a process's: it is counted in the same statement that writes the session,
    /// so raising the replica count does not multiply it. Minting is behind this surface's authentication and its rate
    /// limiter, and this is what keeps a credential that is nonetheless spending both from growing the table. Reaching
    /// it refuses rather than evicting, because evicting somebody else's live session would sign a stranger out.
    /// </remarks>
    internal const int MostLiveSessions = 10_000;

    /// <summary>How long the deployment may go without removing the sessions that can no longer authenticate anything.</summary>
    /// <remarks>
    /// Deliberately not the lifetime, which is what the signal ticket and the anti-replay record both use: theirs are
    /// seconds and minutes, and a sweep due one lifetime after a process started would here be a sweep due thirty days
    /// after it started, which is longer than most processes live. A day is short against the lifetime rather than
    /// equal to it, and it is what reaches a deployment that never approaches <see cref="MostLiveSessions" /> — without
    /// it, a row naming a user and a credential could stand for a month past the point it authenticated anything,
    /// which is a login history rather than a session store.
    /// </remarks>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

    /// <summary>How many bytes of the token name it, and how many prove it.</summary>
    private const int IdentifierByteCount = 16;
    private const int SecretByteCount = 32;

    /// <summary>What separates the two halves, chosen because it is absent from base64url.</summary>
    private const char Separator = '.';

    private static readonly SearchValues<char> SecretAlphabet = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_=");

    /// <summary>The most a presented value may be before it is refused unread.</summary>
    /// <remarks>A token this type mints is seventy characters, so anything past this is not one. Bounded here rather than left to whatever a listener or a proxy in front of it allows, neither of which is this type's to rely on.</remarks>
    internal const int LongestPresentedToken = 256;

    private readonly IClientSessionStore store;
    private readonly TimeProvider timeProvider;

    private long nextSweepTicks;

    /// <summary>Initializes the sessions over where the deployment holds them and the clock their lifetimes are measured against.</summary>
    /// <param name="store">Where the deployment holds the sessions its clients signed in with.</param>
    /// <param name="timeProvider">Measures when a session expires and when the next sweep is due.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ClientSessionTokens(IClientSessionStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.timeProvider = timeProvider;
        this.nextSweepTicks = (timeProvider.GetUtcNow() + SweepInterval).UtcTicks;
    }

    /// <summary>Mints a token for what a credential admitted, or reports which of the two refusals applies.</summary>
    /// <param name="admitted">The credential the exchange authenticated, the user it resolved, and what it grants.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the exchange did, and the token where it minted one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached, which the route answers as unavailable rather than by minting a session nothing holds.</exception>
    /// <remarks>
    /// Whether the user and the credential still admit a session is decided by the store inside the transaction that
    /// writes the row, holding both while it does, rather than by a check this type makes first. A check here would be
    /// one an operator's act could commit between, which is the race the barrier in the previous store existed to
    /// paper over.
    /// </remarks>
    internal async Task<ClientSessionMint> MintAsync(
        AdmittedUserCredential admitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        await this.SweepExpiredIfDueAsync(cancellationToken);

        var drawn = this.Draw();
        var grant = GrantOf(admitted);
        var outcome = await this.store.TryMintAsync(drawn.Row, grant, MostLiveSessions, cancellationToken);

        // Swept only where the bound is what would refuse this mint, and then tried once more, for the reason the
        // statement counts what stands rather than what is live: without it, ten thousand abandoned expired rows would
        // fill the bound and every new sign-in would be refused until something else removed them.
        if (outcome == ClientSessionMintOutcome.BoundReached)
        {
            await this.SweepAsync(cancellationToken);

            outcome = await this.store.TryMintAsync(drawn.Row, grant, MostLiveSessions, cancellationToken);
        }

        return new ClientSessionMint(
            outcome,
            outcome == ClientSessionMintOutcome.Minted
                ? new MintedClientSessionToken(drawn.Token, drawn.Row.ExpiresAt)
                : null);
    }

    /// <summary>Reports what a presented token admits, without deriving anything.</summary>
    /// <param name="presented">What the request carried, which is whatever a caller wrote and is therefore untrusted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the session admits, or <see langword="null" /> where the token is malformed, unknown, expired, or revoked.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached, which the caller answers as unavailable rather than as unauthenticated.</exception>
    /// <remarks>
    /// The one operation on the request path: the shape is judged before any statement runs, then one indexed read and
    /// one fixed-time comparison, with no key derivation and nothing cached in front of it. An expired session is
    /// refused here and left for the sweep rather than removed, so that verifying stays a read.
    /// </remarks>
    internal async Task<AdmittedUserCredential?> VerifyAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TrySplit(presented, out var identifier, out var secret))
        {
            return null;
        }

        // The sweep hangs off this path as well as the minting one, unlike the signal ticket's, because abandoned
        // sessions accumulate where nobody is signing in: the read is the operation every authenticated request
        // performs, so any client traffic at all sweeps.
        await this.SweepExpiredIfDueAsync(cancellationToken);

        if (await this.store.FindAsync(identifier, cancellationToken) is not { } held
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(secret), held.SecretDigest.Span)
            || this.timeProvider.GetUtcNow() > held.ExpiresAt)
        {
            return null;
        }

        return AdmittedBy(held.Grant);
    }

    /// <summary>Replaces a live session with a fresh token, so a client renews without anybody typing a password.</summary>
    /// <param name="presented">The token the renewing request carried.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The new token, or <see langword="null" /> where the presented one no longer authenticates or the credential behind it no longer admits a session.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached, which the route answers as unavailable rather than by refusing the renewal.</exception>
    /// <remarks>
    /// The presented token stops working the moment this answers, which is what keeps one sign-in to one live token
    /// however often a client renews: a renewal that left the old one alive would leave a trail of valid credentials
    /// behind a session nobody could count. Two requests presenting one token leave exactly one live session, whichever
    /// replica each of them reaches, because the removal and the replacement are one transaction. What it carries
    /// forward is what the credential admitted at the exchange — so a grant narrowed after somebody signed in reaches
    /// them at their next sign-in rather than at their next renewal, which is the bound <c>ADR 0023</c> records.
    /// </remarks>
    internal async Task<MintedClientSessionToken?> RenewAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TrySplit(presented, out var identifier, out var secret))
        {
            return null;
        }

        var drawn = this.Draw();

        var renewed = await this.store.RenewAsync(
            identifier,
            SHA256.HashData(secret),
            drawn.Row,
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        return renewed is null ? null : new MintedClientSessionToken(drawn.Token, drawn.Row.ExpiresAt);
    }

    /// <summary>Ends a session, so the token it was signed in with is refused on the next request rather than at expiry.</summary>
    /// <param name="presented">The token the request carried.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when a live session was ended by this call.</returns>
    /// <exception cref="ClientSessionStoreUnavailableException">Thrown when the deployment's sessions could not be reached.</exception>
    /// <remarks>
    /// The secret is proved inside the statement that removes the row, so a caller writing an identifier it guessed
    /// cannot sign somebody else out — the identifier is the half of a token that is not a secret, and a removal keyed
    /// on it alone would be a denial anybody could perform.
    /// </remarks>
    internal async Task<bool> RevokeAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!TrySplit(presented, out var identifier, out var secret))
        {
            return false;
        }

        return await this.store.RevokeAsync(identifier, SHA256.HashData(secret), cancellationToken);
    }

    /// <summary>Draws the identifier, the secret, and the expiry one session is written with.</summary>
    /// <returns>The row the store is asked to hold, and the token the client is answered with.</returns>
    /// <remarks>The secret is thirty-two bytes from the platform's cryptographically secure generator, so it needs no key stretching: there is nothing to guess faster than the generator. Only its digest leaves this method for the store.</remarks>
    private (MintedClientSessionRow Row, string Token) Draw()
    {
        var identifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(IdentifierByteCount));
        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);

        return (
            new MintedClientSessionRow(
                identifier,
                SHA256.HashData(secret),
                this.timeProvider.GetUtcNow() + Lifetime),
            string.Concat(TokenPrefix, identifier, Separator.ToString(), Base64Url.EncodeToString(secret)));
    }

    /// <summary>Reads a presented value as the two halves a minted token is written as, refusing anything else.</summary>
    /// <returns><see langword="true" /> when the value has the shape of a token this deployment mints.</returns>
    /// <remarks>Bounded before it is walked, which is the order every other untrusted length on this surface is read in: a value past the bound costs no index, no slice, no decode, and no statement.</remarks>
    private static bool TrySplit(string? presented, out string identifier, out byte[] secret)
    {
        identifier = string.Empty;
        secret = [];

        if (presented is null
            || presented.Length <= TokenPrefix.Length
            || presented.Length > LongestPresentedToken
            || !presented.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = presented.IndexOf(Separator, StringComparison.Ordinal);

        if (separator <= TokenPrefix.Length || separator == presented.Length - 1)
        {
            return false;
        }

        var proof = presented.AsSpan(separator + 1);

        // Base64Url.IsValid ignores whitespace, so a value carrying some would decode and reach the store as a lookup.
        if (proof.ContainsAnyExcept(SecretAlphabet) || !Base64Url.IsValid(proof))
        {
            return false;
        }

        identifier = presented[TokenPrefix.Length..separator];
        secret = Base64Url.DecodeFromChars(proof);

        return true;
    }

    /// <summary>Reads what an exchange established as what the session row will hold.</summary>
    /// <remarks>The empty identifier becomes an absent credential, because the column holding it is a foreign key: a client endpoint requiring no credential reports that case as an identifier matching no row the deployment could hold, and a sentinel cannot survive a constraint.</remarks>
    private static ClientSessionGrant GrantOf(AdmittedUserCredential admitted) => new(
        admitted.User,
        admitted.CredentialId == Guid.Empty ? null : admitted.CredentialId,
        admitted.Permissions);

    /// <summary>Reads what a held session admits back into what every surface downstream of authentication expects.</summary>
    /// <remarks>The absent credential becomes the empty identifier again, which is what the claim on such a request has always carried and what an operator reading one is already told matches no credential.</remarks>
    private static AdmittedUserCredential AdmittedBy(ClientSessionGrant grant) => new(
        grant.CredentialId ?? Guid.Empty,
        grant.User,
        grant.Permissions);

    /// <summary>Removes what can no longer authenticate anything, at most once per <see cref="SweepInterval" />.</summary>
    /// <remarks>
    /// On the store's own operations rather than on a timer, for the reason the signal ticket sweeps that way: a table
    /// nothing is reading or writing needs no sweeping, and a background timer would keep a process awake to prove it.
    /// The interval is claimed with one atomic exchange, so concurrent requests produce one sweep rather than one each,
    /// and it stays a process's own interval above one replica — a bounded delete issued by each of a few replicas once
    /// a day costs less than anything that would have to coordinate them. A removal that fails takes the operation that
    /// triggered it with it, because the statement about to follow reaches the same database over the same pool.
    /// </remarks>
    private async Task SweepExpiredIfDueAsync(CancellationToken cancellationToken)
    {
        var due = Interlocked.Read(ref this.nextSweepTicks);

        if (this.timeProvider.GetUtcNow().UtcTicks < due)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref this.nextSweepTicks, this.NextSweepAfterNow(), due) != due)
        {
            return;
        }

        await this.store.RemoveExpiredAsync(this.timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>Removes what can no longer authenticate anything, whether or not the interval was due.</summary>
    /// <remarks>Reached only where the bound is what would refuse a mint, which is the moment the difference between a live session and an expired one decides whether somebody signs in. It moves the interval on as well, so a deployment sweeping at its bound does not sweep again a moment later for the interval.</remarks>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref this.nextSweepTicks, this.NextSweepAfterNow());

        await this.store.RemoveExpiredAsync(this.timeProvider.GetUtcNow(), cancellationToken);
    }

    private long NextSweepAfterNow() => (this.timeProvider.GetUtcNow() + SweepInterval).UtcTicks;
}
