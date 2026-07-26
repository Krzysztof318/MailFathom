// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Transport;

/// <summary>Translates authentication mechanisms to and from their registered SASL names and classifies their safety.</summary>
/// <remarks>
/// SASL mechanism names are registry vocabulary rather than client-library vocabulary, so the domain owns the single
/// name table. Configuration binding and transport adapters both use it, which keeps an operator-facing name and the
/// name matched against a server's advertised mechanism set from drifting apart.
/// </remarks>
public static class MailAuthenticationMechanisms
{
    /// <summary>Gets the registered SASL name of a mechanism.</summary>
    /// <param name="mechanism">The permitted mechanism.</param>
    /// <returns>The uppercase SASL name used on the wire.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mechanism" /> is not a defined member.</exception>
    public static string ToSaslName(this MailAuthenticationMechanism mechanism) => mechanism switch
    {
        MailAuthenticationMechanism.Plain => "PLAIN",
        MailAuthenticationMechanism.Login => "LOGIN",
        MailAuthenticationMechanism.CramMd5 => "CRAM-MD5",
        MailAuthenticationMechanism.DigestMd5 => "DIGEST-MD5",
        MailAuthenticationMechanism.ScramSha1 => "SCRAM-SHA-1",
        MailAuthenticationMechanism.ScramSha1Plus => "SCRAM-SHA-1-PLUS",
        MailAuthenticationMechanism.ScramSha256 => "SCRAM-SHA-256",
        MailAuthenticationMechanism.ScramSha256Plus => "SCRAM-SHA-256-PLUS",
        MailAuthenticationMechanism.ScramSha512 => "SCRAM-SHA-512",
        MailAuthenticationMechanism.ScramSha512Plus => "SCRAM-SHA-512-PLUS",
        MailAuthenticationMechanism.Ntlm => "NTLM",
        _ => throw new ArgumentOutOfRangeException(nameof(mechanism), mechanism, "The mechanism is not a supported SASL mechanism."),
    };

    /// <summary>Parses an operator-supplied SASL name, ignoring case and surrounding whitespace.</summary>
    /// <param name="saslName">The configured mechanism name.</param>
    /// <param name="mechanism">The parsed mechanism when the name is supported.</param>
    /// <returns><see langword="true" /> when the name is a supported mechanism; otherwise <see langword="false" />.</returns>
    public static bool TryParseSaslName(string? saslName, out MailAuthenticationMechanism mechanism)
    {
        mechanism = default;
        if (string.IsNullOrWhiteSpace(saslName))
        {
            return false;
        }

        var normalizedName = saslName.Trim();
        foreach (var candidate in Enum.GetValues<MailAuthenticationMechanism>())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate.ToSaslName(), normalizedName))
            {
                mechanism = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>Gets whether the mechanism exposes the password to anyone able to read the channel.</summary>
    /// <param name="mechanism">The permitted mechanism.</param>
    /// <returns><see langword="true" /> when the password travels in clear text; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mechanism" /> is not a defined member.</exception>
    /// <remarks>
    /// Challenge-response mechanisms still leak the exchange to an attacker who can read the channel, but only a
    /// clear-text mechanism hands over the reusable password itself, which is why the policy singles them out.
    /// </remarks>
    public static bool TransmitsCredentialsInClearText(this MailAuthenticationMechanism mechanism) => mechanism switch
    {
        MailAuthenticationMechanism.Plain or MailAuthenticationMechanism.Login => true,
        MailAuthenticationMechanism.CramMd5
            or MailAuthenticationMechanism.DigestMd5
            or MailAuthenticationMechanism.ScramSha1
            or MailAuthenticationMechanism.ScramSha1Plus
            or MailAuthenticationMechanism.ScramSha256
            or MailAuthenticationMechanism.ScramSha256Plus
            or MailAuthenticationMechanism.ScramSha512
            or MailAuthenticationMechanism.ScramSha512Plus
            or MailAuthenticationMechanism.Ntlm => false,
        _ => throw new ArgumentOutOfRangeException(nameof(mechanism), mechanism, "The mechanism is not a supported SASL mechanism."),
    };
}
