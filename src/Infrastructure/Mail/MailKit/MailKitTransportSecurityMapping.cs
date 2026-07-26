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
    /// <exception cref="MailAuthenticationMechanismUnavailableException">Thrown when the server advertises no permitted mechanism.</exception>
    /// <remarks>
    /// MailKit chooses a mechanism from whatever remains in this set, so restricting it before authenticating is what
    /// makes the allow-list binding. The failure is deliberately terminal: restoring removed mechanisms after a failed
    /// authentication would let a server downgrade the account to a mechanism the operator refused.
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

        if (advertisedMechanisms.Count == 0)
        {
            throw new MailAuthenticationMechanismUnavailableException(accountId, [.. permittedNames.Order(StringComparer.Ordinal)]);
        }
    }
}
