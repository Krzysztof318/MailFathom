// Copyright © 2026 Krzysztof Kasprowicz

using MailKit.Security;
using MailMcp.Domain.Transport;

namespace MailMcp.Infrastructure.Mail.MailKit;

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

        if (advertisedMechanisms.Count == 0 && !authentication.PermitsClearTextCredentials)
        {
            throw new MailAuthenticationMechanismUnavailableException(accountId, [.. permittedNames.Order(StringComparer.Ordinal)]);
        }
    }
}
