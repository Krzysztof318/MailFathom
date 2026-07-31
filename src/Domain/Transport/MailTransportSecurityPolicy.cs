// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Domain.Transport;

/// <summary>Defines how one mail account connects, which certificate authorities it trusts, and how it authenticates.</summary>
/// <remarks>
/// The rules that reject an unsafe combination are pure policy, so they live here rather than in a configuration
/// validator. Every entry point that reaches a transport adapter builds this value first, which is what keeps a future
/// command-line or MCP-driven account from bypassing the rejection a host options validator performs at startup.
/// </remarks>
public sealed record MailTransportSecurityPolicy
{
    private MailTransportSecurityPolicy(
        MailConnectionSecurity connectionSecurity,
        MailAuthenticationPolicy authentication,
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference)
    {
        this.ConnectionSecurity = connectionSecurity;
        this.Authentication = authentication;
        this.CertificateTrust = certificateTrust;
        this.TrustedCertificateAuthorityReference = trustedCertificateAuthorityReference;
    }

    /// <summary>Gets the selected connection encryption mode.</summary>
    public MailConnectionSecurity ConnectionSecurity { get; }

    /// <summary>Gets the permitted authentication mechanisms and the accepted weakenings.</summary>
    public MailAuthenticationPolicy Authentication { get; }

    /// <summary>Gets which certificate authorities validate the server certificate.</summary>
    public MailServerCertificateTrust CertificateTrust { get; }

    /// <summary>Gets the reference to deployment-provisioned trust anchor material, when an additional authority is trusted.</summary>
    /// <remarks>The value is a reference such as a credential name, never certificate material and never a secret value.</remarks>
    public string? TrustedCertificateAuthorityReference { get; }

    /// <summary>Gets whether the connection mode guarantees that no traffic can travel unencrypted.</summary>
    /// <param name="connectionSecurity">The connection encryption mode.</param>
    /// <returns><see langword="true" /> when encryption is mandatory; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="connectionSecurity" /> is not a defined member.</exception>
    public static bool GuaranteesEncryptedChannel(MailConnectionSecurity connectionSecurity) => connectionSecurity switch
    {
        MailConnectionSecurity.TlsOnConnect or MailConnectionSecurity.StartTlsRequired => true,
        MailConnectionSecurity.Auto or MailConnectionSecurity.StartTlsWhenAvailable or MailConnectionSecurity.None => false,
        _ => throw new ArgumentOutOfRangeException(nameof(connectionSecurity), connectionSecurity, "The connection security mode is not supported."),
    };

    /// <summary>Creates a validated transport security policy.</summary>
    /// <param name="connectionSecurity">The connection encryption mode.</param>
    /// <param name="authentication">The authentication allow-list and accepted weakenings.</param>
    /// <param name="certificateTrust">The certificate authorities that validate the server certificate.</param>
    /// <param name="trustedCertificateAuthorityReference">The reference to additional trust anchor material, or <see langword="null" />.</param>
    /// <returns>A policy that satisfies every transport security rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authentication" /> is <see langword="null" />.</exception>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the combination weakens transport protection without the required opt-ins.</exception>
    public static MailTransportSecurityPolicy Create(
        MailConnectionSecurity connectionSecurity,
        MailAuthenticationPolicy authentication,
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        var violations = FindViolations(
            connectionSecurity,
            authentication.PermittedMechanisms,
            authentication.AllowInsecureConnection,
            authentication.AllowClearTextAuthenticationOverUnencryptedConnection,
            certificateTrust,
            trustedCertificateAuthorityReference);

        if (violations.Count > 0)
        {
            throw new MailTransportSecurityPolicyViolationException(violations);
        }

        return new MailTransportSecurityPolicy(
            connectionSecurity,
            authentication,
            certificateTrust,
            string.IsNullOrWhiteSpace(trustedCertificateAuthorityReference) ? null : trustedCertificateAuthorityReference.Trim());
    }

    /// <summary>Finds every transport security rule the supplied combination violates.</summary>
    /// <param name="connectionSecurity">The connection encryption mode.</param>
    /// <param name="permittedMechanisms">The permitted SASL mechanisms.</param>
    /// <param name="allowInsecureConnection">Whether a connection mode that can stay unencrypted is accepted.</param>
    /// <param name="allowClearTextAuthenticationOverUnencryptedConnection">Whether clear-text credentials on an unencrypted channel are accepted.</param>
    /// <param name="certificateTrust">The certificate authorities that validate the server certificate.</param>
    /// <param name="trustedCertificateAuthorityReference">The reference to additional trust anchor material, or <see langword="null" />.</param>
    /// <returns>The violated rules, empty when the combination is safe.</returns>
    /// <remarks>
    /// A configuration boundary calls this before building the policy so it can report every unsafe setting of an
    /// account at once instead of failing on the first one. The evaluation is total: an undefined enum value, which
    /// configuration can produce by binding a raw number, is reported as a violation instead of falling through the
    /// rules it cannot be evaluated against.
    /// </remarks>
    public static IReadOnlyList<MailTransportSecurityViolation> FindViolations(
        MailConnectionSecurity connectionSecurity,
        IReadOnlyList<MailAuthenticationMechanism>? permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection,
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference)
    {
        var violations = new List<MailTransportSecurityViolation>();

        if (permittedMechanisms is not { Count: > 0 })
        {
            violations.Add(MailTransportSecurityViolation.PermittedAuthenticationMechanismRequired);
        }

        violations.AddRange(FindConnectionSecurityViolations(
            connectionSecurity,
            permittedMechanisms,
            allowInsecureConnection,
            allowClearTextAuthenticationOverUnencryptedConnection));

        violations.AddRange(FindCertificateTrustViolations(certificateTrust, trustedCertificateAuthorityReference));

        return violations;
    }

    private static IEnumerable<MailTransportSecurityViolation> FindConnectionSecurityViolations(
        MailConnectionSecurity connectionSecurity,
        IReadOnlyList<MailAuthenticationMechanism>? permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection)
    {
        // An undefined mode cannot be classified as encrypted or unencrypted, so the remaining rules would silently
        // pass it. It is rejected outright instead.
        if (!Enum.IsDefined(connectionSecurity))
        {
            yield return MailTransportSecurityViolation.ConnectionSecurityNotSupported;

            yield break;
        }

        if (connectionSecurity == MailConnectionSecurity.None && !allowInsecureConnection)
        {
            yield return MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn;
        }

        // Auto and StartTlsWhenAvailable both complete the connection unencrypted when the server advertises no
        // encryption, so an operator who selects them accepts the same exposure as None and needs the same opt-in.
        if (connectionSecurity is MailConnectionSecurity.Auto or MailConnectionSecurity.StartTlsWhenAvailable && !allowInsecureConnection)
        {
            yield return MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn;
        }

        var permitsClearTextCredentials = permittedMechanisms?.Any(mechanism => mechanism.TransmitsCredentialsInClearText) == true;
        var acceptedClearTextExposure = allowInsecureConnection && allowClearTextAuthenticationOverUnencryptedConnection;
        if (!GuaranteesEncryptedChannel(connectionSecurity) && permitsClearTextCredentials && !acceptedClearTextExposure)
        {
            yield return MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection;
        }
    }

    private static IEnumerable<MailTransportSecurityViolation> FindCertificateTrustViolations(
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference)
    {
        if (!Enum.IsDefined(certificateTrust))
        {
            yield return MailTransportSecurityViolation.CertificateTrustNotSupported;

            yield break;
        }

        var hasReference = !string.IsNullOrWhiteSpace(trustedCertificateAuthorityReference);

        if (certificateTrust == MailServerCertificateTrust.AdditionalTrustedAuthority && !hasReference)
        {
            yield return MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceRequired;
        }

        if (certificateTrust == MailServerCertificateTrust.SystemTrustStore && hasReference)
        {
            yield return MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable;
        }
    }
}
