// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Sessions;

/// <summary>Mints, verifies, and revokes the tokens a signed-in client presents in place of its password.</summary>
/// <remarks>
/// <para>
/// <b>A password is derived once, at the exchange, and never again.</b> HTTP Basic carries no session, so every request
/// presenting one re-derived a PBKDF2 record sized for authenticating a person — around half a second of the process's
/// own processor per call, on a surface a client opens eight requests against to draw one screen. What that cost is for
/// is making a guessed password expensive, which is a property of the sign-in rather than of the eighth request after
/// it. So the sign-in exchanges the credential for one of these, and everything afterwards is judged here, where
/// verifying costs a dictionary lookup and one fixed-time comparison.
/// </para>
/// <para>
/// <b>What is kept is not a password.</b> A token names the credential that minted it and the user that credential
/// resolved, expires by itself, and is refused the moment it is revoked — so a head that keeps one keeps something with
/// a life the deployment controls, rather than a value that stays good until somebody changes a password. That is the
/// whole of what the exchange buys the client's storage, and <c>ADR 0023</c> is where it is recorded.
/// </para>
/// <para>
/// <b>The secret half is compared in constant time</b>, exactly as <see cref="Signals.ClientSignalTickets" /> compares
/// its own: a token is an identifier and a secret written together, only the identifier is looked up, and the secret is
/// compared with <see cref="CryptographicOperations.FixedTimeEquals" />. A dictionary keyed by the secret itself would
/// leak a prefix match through its own lookup.
/// </para>
/// <para>
/// <b>Unlike a signal ticket it survives being presented</b>, which is what makes it a session rather than a handshake:
/// it is removed by <see cref="Revoke" />, by <see cref="Renew" /> replacing it, by its own expiry, or by the process
/// ending. That last one is the cost of holding these in memory rather than in the database, and it is a deliberate
/// trade recorded in <c>ADR 0023</c>: a restart signs every client out, and what that costs is one sign-in, while a
/// persisted session would be a table, a migration, and a row read on every authenticated request — which is the cost
/// this whole type exists to remove.
/// </para>
/// </remarks>
internal sealed class ClientSessionTokens
{
    /// <summary>How long a minted token authenticates for.</summary>
    /// <remarks>
    /// A working day rather than a handshake, because this is what a head keeps between starts: a client reopened
    /// inside it is already signed in, which is the requirement the stored password used to meet. It is not longer,
    /// because a token that outlives the day it was minted on is a credential on somebody's machine that nobody
    /// remembers issuing — and a client left open renews rather than expiring, so the length bounds the abandoned case
    /// rather than the working one.
    /// </remarks>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <summary>What every token this deployment mints begins with.</summary>
    /// <remarks>
    /// The shape a minted API key already carries, with a different word inside it: short, lower-case, ending in an
    /// underscore, so a secret scanner recognizes one and an operator who finds a value they did not expect can tell
    /// which of the two it is. It is also what routes a presented bearer credential to this method rather than to the
    /// key comparison, which is why it is exact rather than decorative.
    /// </remarks>
    internal const string TokenPrefix = "mfs_";

    /// <summary>The most sessions this process holds before minting sweeps and then refuses.</summary>
    /// <remarks>A bound at a boundary, for the reason <see cref="Signals.ClientSignalTickets.MostOutstandingTickets" /> carries one: minting is behind this surface's authentication and its rate limiter, and this is what keeps a credential that is nonetheless spending both from growing the process's memory. Reaching it refuses rather than evicting, because evicting somebody else's live session would sign a stranger out.</remarks>
    internal const int MostLiveSessions = 10_000;

    /// <summary>How long after an operator ends somebody's sessions a mint naming them is refused.</summary>
    /// <remarks>
    /// The window an exchange can be in flight for, which is what makes ending a session an act rather than a race: a
    /// request authenticates against a row that is still enabled, spends half a second deriving the password, and
    /// would mint after the sweep that was supposed to have ended it — leaving a live session the operator was told
    /// was gone, and one that renews from what this store holds rather than from the row. Long enough to cover a
    /// derivation and the request around it, and short enough that a credential enabled again is signed in with
    /// rather than refused for a minute.
    /// </remarks>
    internal static readonly TimeSpan MintBarrier = TimeSpan.FromSeconds(30);

    /// <summary>How many bytes of the token name it, and how many prove it.</summary>
    private const int IdentifierByteCount = 16;
    private const int SecretByteCount = 32;

    /// <summary>What separates the two halves, chosen because it is absent from base64url.</summary>
    private const char Separator = '.';

    /// <summary>The most a presented value may be before it is refused unread.</summary>
    /// <remarks>A token this type mints is seventy characters, so anything past this is not one. Bounded here rather than left to whatever a listener or a proxy in front of it allows, neither of which is this type's to rely on.</remarks>
    internal const int LongestPresentedToken = 256;

    private readonly Dictionary<string, LiveSession> live = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, DateTimeOffset> endedCredentials = [];
    private readonly Dictionary<MailUserId, DateTimeOffset> endedUsers = [];
    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the store over the clock its lifetimes are measured against.</summary>
    /// <param name="timeProvider">Measures when a token was minted and whether it has expired.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public ClientSessionTokens(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Mints a token for what a credential admitted, or refuses where the bound is reached or the credential was just ended.</summary>
    /// <param name="admitted">The credential the exchange authenticated, the user it resolved, and what it grants.</param>
    /// <returns>The minted token and when it expires, or <see langword="null" /> where this store will not hold another session for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The barrier is read here rather than by the caller, because the caller cannot hold the gate: an exchange
    /// authenticates outside it, and a check made before this call would be a check the sweep can run between. Which
    /// of the two refusals it was is asked afterwards through <see cref="WasEndedRecently" />, which answers for as
    /// long as the barrier stands and therefore reports the same thing this refusal did.
    /// </remarks>
    internal MintedClientSessionToken? Mint(AdmittedUserCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        lock (this.gate)
        {
            return this.Barred(admitted) ? null : this.MintReplacing(admitted, replacing: null);
        }
    }

    /// <summary>Whether an operator has just ended what this credential admits, which is why a mint for it was refused.</summary>
    /// <param name="admitted">What the exchange authenticated.</param>
    /// <returns><see langword="true" /> while the barrier over that credential or that user stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    /// <remarks>What it separates is an authentication failure from a transient one: a credential an operator ended is answered by asking for a sign-in, and a store already holding as many sessions as it will hold is answered by asking again in a moment.</remarks>
    internal bool WasEndedRecently(AdmittedUserCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        lock (this.gate)
        {
            return this.Barred(admitted);
        }
    }

    /// <summary>Reports what a presented token admits, without deriving anything.</summary>
    /// <param name="presented">What the request carried, which is whatever a caller wrote and is therefore untrusted.</param>
    /// <returns>What the session admits, or <see langword="null" /> where the token is malformed, unknown, expired, or revoked.</returns>
    /// <remarks>
    /// The one operation on the request path, and the reason this type exists: a lookup and a fixed-time comparison,
    /// with no key derivation, no database read, and no allocation past what reading the header already cost. An
    /// expired session is refused here and left for a later sweep rather than removed, so that verifying stays a read.
    /// </remarks>
    internal AdmittedUserCredential? Verify(string? presented)
    {
        lock (this.gate)
        {
            return this.LiveSessionFor(presented, out _)?.Admitted;
        }
    }

    /// <summary>Replaces a live session with a fresh token, so a client renews without anybody typing a password.</summary>
    /// <param name="presented">The token the renewing request carried.</param>
    /// <returns>The new token, or <see langword="null" /> where the presented one no longer authenticates or the bound refuses another.</returns>
    /// <remarks>
    /// The presented token stops working the moment this answers, which is what keeps one sign-in to one live token
    /// however often a client renews: a renewal that left the old one alive would leave a trail of valid credentials
    /// behind a session nobody could count. What it carries forward is what the credential admitted at the exchange —
    /// so a grant narrowed after somebody signed in reaches them at their next sign-in rather than at their next
    /// renewal, which is the bound this type does not close and <c>ADR 0023</c> records.
    /// </remarks>
    internal MintedClientSessionToken? Renew(string? presented)
    {
        // Read and replaced under one hold of the gate, because the two are one step: two requests presenting the same
        // token would otherwise both read it as live, both mint, and leave two sessions behind one sign-in — the trail
        // of valid credentials that replacing the presented token exists to prevent.
        lock (this.gate)
        {
            return this.LiveSessionFor(presented, out var identifier) is { } session
                ? this.MintReplacing(session.Admitted, identifier)
                : null;
        }
    }

    /// <summary>Ends a session, so the token it was signed in with is refused on the next request rather than at expiry.</summary>
    /// <param name="presented">The token the request carried.</param>
    /// <returns><see langword="true" /> when a live session was ended by this call.</returns>
    /// <remarks>
    /// The secret is proved before anything is removed, so a caller writing an identifier it guessed cannot sign
    /// somebody else out — the identifier is the half of a token that is not a secret, and a removal keyed on it alone
    /// would be a denial anybody could perform.
    /// </remarks>
    internal bool Revoke(string? presented)
    {
        if (!TrySplit(presented, out var identifier, out var proof))
        {
            return false;
        }

        lock (this.gate)
        {
            return this.live.TryGetValue(identifier, out var session)
                && Proves(session, proof)
                && this.live.Remove(identifier);
        }
    }

    /// <summary>Ends every session one credential minted, which is what disabling or deleting that credential means here.</summary>
    /// <param name="credentialId">The credential an operator acted on.</param>
    /// <returns>How many sessions were ended.</returns>
    /// <remarks>
    /// Without this, revoking somebody's credential would leave whatever they were signed in with working until it
    /// expired, because a token is verified against this store rather than against the row it came from — which is the
    /// same trade that makes verifying cheap. Walking the store is what closes that, and it is walked only when an
    /// operator acts on a credential rather than on any request path.
    /// </remarks>
    internal int RevokeEverythingMintedBy(Guid credentialId)
    {
        lock (this.gate)
        {
            this.BarMinting(this.endedCredentials, credentialId);

            return this.RevokeEverything(session => session.Admitted.CredentialId == credentialId);
        }
    }

    /// <summary>Ends every session held for one user, which is what erasing that user means here.</summary>
    /// <param name="user">The user this deployment no longer holds.</param>
    /// <returns>How many sessions were ended.</returns>
    /// <remarks>
    /// By the user rather than by their credentials, because an erasure removes those rows by cascade and never names
    /// them: a caller that walked the credentials it was about to delete would be reading a list the database is in
    /// the middle of removing. A session names the user it acts for, which is the fact that outlived the rows.
    /// </remarks>
    internal int RevokeEverythingMintedFor(MailUserId user)
    {
        lock (this.gate)
        {
            this.BarMinting(this.endedUsers, user);

            return this.RevokeEverything(session => session.Admitted.User == user);
        }
    }

    /// <summary>Ends every session an operator's act invalidated.</summary>
    /// <remarks>Walked only when an operator acts on a credential or a user, never on a request path — which is what keeps the cost of holding sessions in a dictionary off the requests that read one.</remarks>
    private int RevokeEverything(Func<LiveSession, bool> invalidated)
    {
        lock (this.gate)
        {
            var ended = this.live
                .Where(session => invalidated(session.Value))
                .Select(static session => session.Key)
                .ToArray();

            foreach (var identifier in ended)
            {
                this.live.Remove(identifier);
            }

            return ended.Length;
        }
    }

    /// <summary>Whether a barrier over this credential or this user still stands, which is what refuses a mint across an operator's act.</summary>
    /// <remarks>Called under <c>gate</c>, so that reading the barrier and minting against it are one step rather than two a sweep can run between.</remarks>
    private bool Barred(AdmittedUserCredential admitted)
    {
        var now = this.timeProvider.GetUtcNow();

        return (this.endedCredentials.TryGetValue(admitted.CredentialId, out var credentialBarrier)
                && now <= credentialBarrier)
            || (this.endedUsers.TryGetValue(admitted.User, out var userBarrier) && now <= userBarrier);
    }

    /// <summary>The session a presented token names and proves, or <see langword="null" /> where it names none.</summary>
    /// <param name="presented">What the request carried.</param>
    /// <param name="identifier">The half of the token the store is keyed by, so a caller replacing the session knows what to replace.</param>
    /// <returns>The live session, or <see langword="null" /> where the token is malformed, unknown, expired, or revoked.</returns>
    /// <remarks>Called under <c>gate</c>, which every caller holds because reading a session and acting on what it says are one step wherever the acting is a write.</remarks>
    private LiveSession? LiveSessionFor(string? presented, out string identifier) =>
        TrySplit(presented, out identifier, out var proof)
        && this.live.TryGetValue(identifier, out var session)
        && Proves(session, proof)
        && this.timeProvider.GetUtcNow() <= session.ExpiresAt
            ? session
            : null;

    /// <summary>Writes a session into the store, replacing the one a renewal presented where there is one.</summary>
    /// <param name="admitted">What the session admits, which a renewal carries forward from the exchange.</param>
    /// <param name="replacing">The session this one takes the place of, or <see langword="null" /> for a sign-in.</param>
    /// <returns>The minted token and when it expires, or <see langword="null" /> when the bound refuses it.</returns>
    /// <remarks>
    /// A replacement never meets the bound, because it does not grow the store: the bound refuses a new sign-in rather
    /// than ending a live session, and a renewal refused for capacity would end every live session at its own expiry
    /// for as long as the process stayed full. The presented session is removed only once the replacement is certain,
    /// so a refused mint leaves the caller holding exactly what it presented.
    /// </remarks>
    private MintedClientSessionToken? MintReplacing(AdmittedUserCredential admitted, string? replacing)
    {
        var replacesALiveSession = replacing is not null && this.live.ContainsKey(replacing);

        // Swept only where the bound is what would refuse this mint, for the reason the ticket store sweeps there: the
        // difference between a live session and an expired one matters at exactly that moment, and walking the whole
        // store on every sign-in would be a cost paid on the ordinary path to tidy state the bound limits.
        if (!replacesALiveSession && this.live.Count >= MostLiveSessions)
        {
            this.SweepExpired();

            if (this.live.Count >= MostLiveSessions)
            {
                return null;
            }
        }

        var identifier = RandomText(IdentifierByteCount);
        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);
        var expiresAt = this.timeProvider.GetUtcNow() + Lifetime;

        if (replacing is not null)
        {
            this.live.Remove(replacing);
        }

        this.live[identifier] = new LiveSession(admitted, secret, expiresAt);

        return new MintedClientSessionToken(
            string.Concat(TokenPrefix, identifier, Separator.ToString(), Base64Url.EncodeToString(secret)),
            expiresAt);
    }

    /// <summary>Splits a presented value into the half that is looked up and the half that proves it.</summary>
    /// <remarks>Bounded before it is walked, which is the order every other untrusted length on this surface is read in: a value past the bound costs no index, no slice, and no decode.</remarks>
    private static bool TrySplit(string? presented, out string identifier, out ReadOnlyMemory<char> proof)
    {
        identifier = string.Empty;
        proof = default;

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

        identifier = presented[TokenPrefix.Length..separator];
        proof = presented.AsMemory(separator + 1);

        return true;
    }

    /// <summary>Whether the presented half proves this session, compared in a time that does not depend on how much of it matched.</summary>
    private static bool Proves(LiveSession session, ReadOnlyMemory<char> proof) =>
        Base64Url.IsValid(proof.Span)
        && CryptographicOperations.FixedTimeEquals(Base64Url.DecodeFromChars(proof.Span), session.Secret);

    private static string RandomText(int byteCount) =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteCount));

    /// <summary>Removes what can no longer authenticate, so the bound above measures live sessions rather than history.</summary>
    /// <remarks>Called under <c>gate</c>, which is what lets it enumerate the store while removing from it.</remarks>
    private void SweepExpired()
    {
        var now = this.timeProvider.GetUtcNow();
        var expired = this.live
            .Where(session => session.Value.ExpiresAt < now)
            .Select(static session => session.Key)
            .ToArray();

        foreach (var identifier in expired)
        {
            this.live.Remove(identifier);
        }

        SweepBarriers(this.endedCredentials, now);
        SweepBarriers(this.endedUsers, now);
    }

    /// <summary>Bars minting against what an operator's act invalidated, and prunes what no exchange can still be in flight across.</summary>
    /// <remarks>
    /// Both barrier dictionaries are swept here rather than only beside the sessions, because the session sweep runs
    /// only where the store is full: a deployment that never reaches <see cref="MostLiveSessions" /> would otherwise
    /// keep one entry per act an operator ever performed for the life of the process.
    /// </remarks>
    private void BarMinting<TKey>(Dictionary<TKey, DateTimeOffset> barriers, TKey key)
        where TKey : notnull
    {
        var now = this.timeProvider.GetUtcNow();

        barriers[key] = now + MintBarrier;

        SweepBarriers(this.endedCredentials, now);
        SweepBarriers(this.endedUsers, now);
    }

    /// <summary>Removes the barriers an exchange can no longer have been in flight across.</summary>
    /// <remarks>Called under <c>gate</c>, which is what lets it enumerate a dictionary while removing from it.</remarks>
    private static void SweepBarriers<TKey>(Dictionary<TKey, DateTimeOffset> barriers, DateTimeOffset now)
        where TKey : notnull
    {
        var stood = barriers
            .Where(barrier => barrier.Value < now)
            .Select(static barrier => barrier.Key)
            .ToArray();

        foreach (var key in stood)
        {
            barriers.Remove(key);
        }
    }

    private sealed record LiveSession(AdmittedUserCredential Admitted, byte[] Secret, DateTimeOffset ExpiresAt);
}
