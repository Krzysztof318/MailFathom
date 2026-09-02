// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using MailKit.Security;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Maps the domain transport security policy onto the MailKit client contract.</summary>
/// <remarks>
/// This is the only place that translates policy into MailKit vocabulary. Nothing here can weaken the policy: it
/// selects the socket option the mode demands and narrows the advertised SASL mechanism set, never widens it, and
/// never touches certificate validation, which stays at MailKit's validating default.
/// </remarks>
internal static class MailKitTransportSecurityMapping
{
    /// <summary>Maps a connection security mode to the equivalent MailKit socket option.</summary>
    /// <param name="connectionSecurity">The configured connection security mode.</param>
    /// <returns>The socket option MailKit connects with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="connectionSecurity" /> is not a defined member.</exception>
    internal static SecureSocketOptions ToSecureSocketOptions(this MailConnectionSecurity connectionSecurity) => connectionSecurity switch
    {
        MailConnectionSecurity.Auto => SecureSocketOptions.Auto,
        MailConnectionSecurity.TlsOnConnect => SecureSocketOptions.SslOnConnect,
        MailConnectionSecurity.StartTlsRequired => SecureSocketOptions.StartTls,
        MailConnectionSecurity.StartTlsWhenAvailable => SecureSocketOptions.StartTlsWhenAvailable,
        MailConnectionSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(connectionSecurity), connectionSecurity, "The connection security mode is not supported."),
    };

    /// <summary>Removes every mechanism the policy does not permit from the server's advertised set.</summary>
    /// <param name="advertisedMechanisms">The mechanism set MailKit populated while connecting.</param>
    /// <param name="authentication">The policy that decides which mechanisms may be negotiated.</param>
    /// <param name="accountId">The account identifier used in the failure message.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="advertisedMechanisms" /> or <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <exception cref="MailAuthenticationMechanismUnavailableException">Thrown when no permitted mechanism survives and the policy permits no clear-text credentials either.</exception>
    /// <remarks>
    /// <para>
    /// MailKit chooses a mechanism from whatever remains in this set, so restricting it before authenticating is what
    /// makes the allow-list binding. The choice among the survivors is left to MailKit's strength ranking rather than
    /// to the configured order, because an allow-list bounds what is acceptable while the client is better placed to
    /// pick the strongest acceptable mechanism the server actually advertises. Nothing removed here is ever restored:
    /// widening the set after a failed authentication would let a server downgrade the account to a mechanism the
    /// operator refused.
    /// </para>
    /// <para>
    /// An emptied set is not an error on its own. A server is not required to advertise <c>AUTH=</c> at all, and RFC
    /// 3501 leaves the <c>LOGIN</c> command as the last resort a client falls back to; MailKit implements exactly that
    /// when this set is empty, and still refuses when the server advertises <c>LOGINDISABLED</c>. That command hands
    /// over the reusable password in clear text, which is the same exposure
    /// <see cref="MailAuthenticationMechanism.Plain" /> and <see cref="MailAuthenticationMechanism.Login" /> carry, so
    /// it is permitted exactly when the operator already accepted that exposure through the allow-list — and, on an
    /// unencrypted channel, through the separate opt-in <see cref="MailTransportSecurityPolicy" /> requires before the
    /// policy can be built at all. An allow-list of challenge-response mechanisms alone is a statement that the
    /// password must never travel in clear text, so it still ends the attempt here.
    /// </para>
    /// </remarks>
    internal static void RestrictAdvertisedMechanisms(
        ISet<string> advertisedMechanisms,
        MailAuthenticationPolicy authentication,
        string accountId)
    {
        var permittedNames = NarrowToPermitted(advertisedMechanisms, authentication);

        if (advertisedMechanisms.Count == 0 && !authentication.PermitsClearTextCredentials)
        {
            throw new MailAuthenticationMechanismUnavailableException(accountId, [.. permittedNames.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>Narrows a submission server's advertised set to the allow-list, requiring that something survives.</summary>
    /// <param name="advertisedMechanisms">The mechanism set MailKit populated while connecting.</param>
    /// <param name="authentication">The policy that decides which mechanisms may be negotiated.</param>
    /// <param name="accountId">The account identifier used in the failure message.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="advertisedMechanisms" /> or <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <exception cref="MailAuthenticationMechanismUnavailableException">Thrown when no permitted mechanism survives.</exception>
    /// <remarks>
    /// The narrowing is the same and the empty case is not, which is why this is a method of its own rather than the
    /// one above. IMAP has a <c>LOGIN</c> command a client falls back to when a server advertises no <c>AUTH</c> at
    /// all, and RFC 4954 gives SMTP no equivalent: an emptied set here means there is nothing left to authenticate
    /// with. Reaching the mail library with it would produce a refusal about an unsupported client feature, which
    /// names neither the account nor the mechanisms the operator permitted, so the account's own coded failure is
    /// raised instead.
    /// </remarks>
    internal static void RestrictAdvertisedSubmissionMechanisms(
        ISet<string> advertisedMechanisms,
        MailAuthenticationPolicy authentication,
        string accountId)
    {
        var permittedNames = NarrowToPermitted(advertisedMechanisms, authentication);

        if (advertisedMechanisms.Count == 0)
        {
            throw new MailAuthenticationMechanismUnavailableException(accountId, [.. permittedNames.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>Removes every mechanism the policy does not permit and reports the allow-list that decided it.</summary>
    private static HashSet<string> NarrowToPermitted(
        ISet<string> advertisedMechanisms,
        MailAuthenticationPolicy authentication)
    {
        ArgumentNullException.ThrowIfNull(advertisedMechanisms);
        ArgumentNullException.ThrowIfNull(authentication);

        var permittedNames = authentication.PermittedMechanisms
            .Select(mechanism => mechanism.SaslName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rejectedNames = advertisedMechanisms
            .Where(advertisedName => !permittedNames.Contains(advertisedName))
            .ToArray();

        foreach (var rejectedName in rejectedNames)
        {
            advertisedMechanisms.Remove(rejectedName);
        }

        return permittedNames;
    }

    /// <summary>Chooses the token-bearing mechanism to authenticate with, from what survived the allow-list.</summary>
    /// <param name="advertisedMechanisms">The mechanism set left after <see cref="RestrictAdvertisedMechanisms" /> narrowed it for a mailbox connection, or <see cref="RestrictAdvertisedSubmissionMechanisms" /> for a submission one.</param>
    /// <param name="authentication">The policy that decides which mechanisms may be negotiated.</param>
    /// <param name="mechanism">The chosen mechanism when one applies; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the connection authenticates with an access token rather than a password.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="advertisedMechanisms" /> or <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The server's advertisement decides between the two, which is why this reads the set rather than a setting.
    /// <c>OAUTHBEARER</c> is preferred where both are offered because it is the registered, standards-track mechanism
    /// and <c>XOAUTH2</c> is a vendor extension that predates it; where only one is offered there is nothing to
    /// prefer.
    /// </para>
    /// <para>
    /// An emptied set never selects a token mechanism, however token-bearing the allow-list is. The fallback an empty
    /// set permits is the IMAP <c>LOGIN</c> command, which carries a password and cannot carry a bearer token, so a
    /// token-only account facing a server that advertises nothing has already been refused by
    /// <see cref="RestrictAdvertisedMechanisms" /> — and on the submission path by
    /// <see cref="RestrictAdvertisedSubmissionMechanisms" />, which refuses an emptied set whatever the allow-list
    /// permits, because SMTP has no such fallback for any account.
    /// </para>
    /// </remarks>
    internal static bool TrySelectAccessTokenMechanism(
        ISet<string> advertisedMechanisms,
        MailAuthenticationPolicy authentication,
        out MailAuthenticationMechanism mechanism)
    {
        ArgumentNullException.ThrowIfNull(advertisedMechanisms);
        ArgumentNullException.ThrowIfNull(authentication);

        mechanism = authentication.PermittedMechanisms
            .Where(candidate => candidate.AuthenticatesWithAccessToken && advertisedMechanisms.Contains(candidate.SaslName))
            .OrderBy(candidate => candidate == MailAuthenticationMechanism.OAuthBearer ? 0 : 1)
            .FirstOrDefault();

        return mechanism.IsSpecified;
    }

    /// <summary>Creates the MailKit SASL context that presents an access token.</summary>
    /// <param name="mechanism">The mechanism chosen from the server's advertised set.</param>
    /// <param name="userName">The mailbox the token acts for.</param>
    /// <param name="accessToken">The bearer token to present.</param>
    /// <returns>The SASL context MailKit authenticates with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mechanism" /> does not authenticate with an access token.</exception>
    internal static SaslMechanism ToSaslMechanism(
        MailAuthenticationMechanism mechanism,
        string userName,
        string accessToken)
    {
        if (mechanism == MailAuthenticationMechanism.OAuthBearer)
        {
            return new SaslMechanismOAuthBearer(userName, accessToken);
        }

        return mechanism == MailAuthenticationMechanism.XOAuth2
            ? new SaslMechanismOAuth2(userName, accessToken)
            : throw new ArgumentOutOfRangeException(
                nameof(mechanism),
                mechanism,
                "The mechanism does not authenticate with an access token.");
    }
}
