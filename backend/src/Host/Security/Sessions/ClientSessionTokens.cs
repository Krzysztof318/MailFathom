// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;

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

    /// <summary>How many bytes of the token name it, and how many prove it.</summary>
    private const int IdentifierByteCount = 16;
    private const int SecretByteCount = 32;

    /// <summary>What separates the two halves, chosen because it is absent from base64url.</summary>
    private const char Separator = '.';

    /// <summary>The most a presented value may be before it is refused unread.</summary>
    /// <remarks>A token this type mints is seventy characters, so anything past this is not one. Bounded here rather than left to whatever a listener or a proxy in front of it allows, neither of which is this type's to rely on.</remarks>
    private const int LongestPresentedToken = 256;

    private readonly Dictionary<string, LiveSession> live = new(StringComparer.Ordinal);
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

    /// <summary>Mints a token for what a credential admitted, or reports that too many sessions are live.</summary>
    /// <param name="admitted">The credential the exchange authenticated, the user it resolved, and what it grants.</param>
    /// <returns>The minted token and when it expires, or <see langword="null" /> when the bound is reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="admitted" /> is <see langword="null" />.</exception>
    internal MintedClientSessionToken? Mint(AdmittedUserCredential admitted)
    {
        ArgumentNullException.ThrowIfNull(admitted);

        var identifier = RandomText(IdentifierByteCount);
        var secret = RandomNumberGenerator.GetBytes(SecretByteCount);
        var expiresAt = this.timeProvider.GetUtcNow() + Lifetime;

        lock (this.gate)
        {
            // Swept only where the bound is what would refuse this mint, for the reason the ticket store sweeps there:
            // the difference between a live session and an expired one matters at exactly that moment, and walking the
            // whole store on every sign-in would be a cost paid on the ordinary path to tidy state the bound limits.
            if (this.live.Count >= MostLiveSessions)
            {
                this.SweepExpired();
            }

            if (this.live.Count >= MostLiveSessions)
            {
                return null;
            }

            this.live[identifier] = new LiveSession(admitted, secret, expiresAt);
        }

        return new MintedClientSessionToken(
            string.Concat(TokenPrefix, identifier, Separator.ToString(), Base64Url.EncodeToString(secret)),
            expiresAt);
    }

    /// <summary>Reports what a presented token admits, without deriving anything.</summary>
    /// <param name="presented">What the request carried, which is whatever a caller wrote and is therefore untrusted.</param>
    /// <returns>What the session admits, or <see langword="null" /> where the token is malformed, unknown, expired, or revoked.</returns>
    /// <remarks>
    /// The one operation on the request path, and the reason this type exists: a lookup and a fixed-time comparison,
    /// with no key derivation, no database read, and no allocation past what reading the header already cost. An
    /// expired session is refused here and left for a later sweep rather than removed under the read lock, so that
    /// verifying stays a read.
    /// </remarks>
    internal AdmittedUserCredential? Verify(string? presented)
    {
        if (!TrySplit(presented, out var identifier, out var proof))
        {
            return null;
        }

        LiveSession? session;

        lock (this.gate)
        {
            if (!this.live.TryGetValue(identifier, out session))
            {
                return null;
            }
        }

        return Proves(session, proof) && this.timeProvider.GetUtcNow() <= session.ExpiresAt ? session.Admitted : null;
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
        // The three steps are one step, and holding the gate across them is what makes them one: two requests
        // presenting the same token would otherwise both verify it, both mint, and leave two live sessions behind one
        // sign-in — which is exactly the trail of valid credentials replacing the presented token exists to prevent.
        // The lock is re-entrant, so the steps stay the three published operations rather than three copies of them.
        lock (this.gate)
        {
            var admitted = this.Verify(presented);

            if (admitted is null)
            {
                return null;
            }

            var renewed = this.Mint(admitted);

            if (renewed is not null)
            {
                this.Revoke(presented);
            }

            return renewed;
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
            var ended = this.live
                .Where(session => session.Value.Admitted.CredentialId == credentialId)
                .Select(static session => session.Key)
                .ToArray();

            foreach (var identifier in ended)
            {
                this.live.Remove(identifier);
            }

            return ended.Length;
        }
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
    }

    private sealed record LiveSession(AdmittedUserCredential Admitted, byte[] Secret, DateTimeOffset ExpiresAt);
}
