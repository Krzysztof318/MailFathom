// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Transport;

namespace MailMcp.Infrastructure.Mail;

/// <summary>Describes one configuration error in an account's transport security settings.</summary>
/// <param name="PropertyName">The setting the operator must correct.</param>
/// <param name="Description">A safe sentence naming the rule, free of credentials and secret references.</param>
/// <param name="Violation">
/// The domain rule the settings violate, or <see langword="null" /> when the error is a mechanism-name parse failure
/// rather than a rule violation. Carrying it keeps the stable machine-readable identity the domain already computed
/// available to the boundary that renders the operator message, so diagnostics can be matched on the rule instead of
/// on prose that is free to change.
/// </param>
public sealed record MailAccountTransportSecurityConfigurationError(
    string PropertyName,
    string Description,
    MailTransportSecurityViolation? Violation);

/// <summary>Binds an account's transport security settings and maps them onto the domain policy.</summary>
/// <remarks>
/// This is the configuration adapter for <see cref="MailTransportSecurityPolicy" />: it stays mutable and
/// binder-friendly, while every rule it reports comes from the domain policy rather than being restated here. The host
/// binds it, validates it before workers start, and turns the reported errors into startup failures.
/// </remarks>
public sealed class MailAccountTransportSecurityOptions
{
    private static readonly string[] DefaultPermittedAuthenticationMechanisms = ["PLAIN", "LOGIN"];

    /// <summary>Gets or sets how the connection is encrypted.</summary>
    /// <remarks>Only <c>TlsOnConnect</c> and <c>StartTlsRequired</c> guarantee encryption; every other mode requires <see cref="AllowInsecureConnection" />.</remarks>
    public MailConnectionSecurity ConnectionSecurity { get; set; } = MailConnectionSecurity.TlsOnConnect;

    /// <summary>Gets or sets the SASL mechanisms the account may authenticate with.</summary>
    /// <remarks>
    /// The list is an unordered allow-list: the adapter removes every other mechanism from the server's advertised
    /// set and lets the client pick the strongest mechanism that survives, so listing a weaker mechanism first does
    /// not make it preferred. When it
    /// is omitted, the post-binding default in <see cref="EffectivePermittedAuthenticationMechanisms" /> applies. That
    /// default must not be a property initializer, because the configuration binder appends bound entries to an
    /// existing list, which would silently keep the default mechanisms permitted alongside the configured ones.
    /// </remarks>
    public List<string> PermittedAuthenticationMechanisms { get; set; } = [];

    /// <summary>Gets or sets whether a connection mode that can leave the channel unencrypted is accepted.</summary>
    public bool AllowInsecureConnection { get; set; }

    /// <summary>Gets or sets whether sending a reusable password over an unencrypted channel is accepted.</summary>
    public bool AllowClearTextAuthenticationOverUnencryptedConnection { get; set; }

    /// <summary>Gets or sets which certificate authorities validate the server certificate.</summary>
    /// <remarks>Certificate validation itself cannot be disabled; a private server is supported by trusting an additional authority.</remarks>
    public MailServerCertificateTrust CertificateTrust { get; set; } = MailServerCertificateTrust.SystemTrustStore;

    /// <summary>Gets or sets the reference to deployment-provisioned trust anchor material.</summary>
    /// <remarks>The value is a reference such as a credential name, never certificate material and never a secret value.</remarks>
    public string? TrustedCertificateAuthorityReference { get; set; }

    /// <summary>Gets the configured SASL mechanisms or the post-binding default allow-list.</summary>
    /// <remarks>
    /// The default permits the two clear-text mechanisms every IMAP server implements, which is safe under the default
    /// <see cref="ConnectionSecurity" /> of <c>TlsOnConnect</c>. On a mode that can stay unencrypted the same default
    /// trips the clear-text rule, so an operator who weakens the transport still has to say so explicitly.
    /// </remarks>
    public IReadOnlyList<string> EffectivePermittedAuthenticationMechanisms =>
        this.PermittedAuthenticationMechanisms is not { Count: > 0 }
            ? DefaultPermittedAuthenticationMechanisms
            : this.PermittedAuthenticationMechanisms;

    /// <summary>Builds the validated domain policy for this account.</summary>
    /// <returns>The policy the mailbox adapter must obey.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured combination is unsafe.</exception>
    /// <exception cref="ArgumentException">Thrown when no supported SASL mechanism is configured.</exception>
    /// <remarks>
    /// Startup validation rejects these settings long before this runs, so a failure here means an unsafe policy
    /// reached the runtime through some other path. It fails closed rather than connecting.
    /// </remarks>
    public MailTransportSecurityPolicy CreatePolicy() => MailTransportSecurityPolicy.Create(
        this.ConnectionSecurity,
        MailAuthenticationPolicy.Create(
            this.ParsePermittedMechanisms(out _),
            this.AllowInsecureConnection,
            this.AllowClearTextAuthenticationOverUnencryptedConnection),
        this.CertificateTrust,
        this.TrustedCertificateAuthorityReference);

    /// <summary>Finds every unsupported mechanism name and every violated domain transport security rule.</summary>
    /// <returns>The errors to report at startup, empty when the settings are safe.</returns>
    /// <remarks>
    /// Descriptions name the setting and the rule only. The user name, password, and trust anchor reference stay out
    /// of them, because startup validation output reaches operator consoles and logs.
    /// </remarks>
    public IReadOnlyList<MailAccountTransportSecurityConfigurationError> FindConfigurationErrors()
    {
        var errors = new List<MailAccountTransportSecurityConfigurationError>();
        var permittedMechanisms = this.ParsePermittedMechanisms(out var unsupportedMechanismNames);

        foreach (var unsupportedMechanismName in unsupportedMechanismNames)
        {
            errors.Add(new MailAccountTransportSecurityConfigurationError(
                nameof(this.PermittedAuthenticationMechanisms),
                $"SASL mechanism '{unsupportedMechanismName}' is not supported.",
                Violation: null));
        }

        var violations = MailTransportSecurityPolicy.FindViolations(
            this.ConnectionSecurity,
            permittedMechanisms,
            this.AllowInsecureConnection,
            this.AllowClearTextAuthenticationOverUnencryptedConnection,
            this.CertificateTrust,
            this.TrustedCertificateAuthorityReference);

        errors.AddRange(violations.Select(violation => new MailAccountTransportSecurityConfigurationError(
            SettingFor(violation),
            DescribeViolation(violation),
            violation)));

        return errors;
    }

    private IReadOnlyList<MailAuthenticationMechanism> ParsePermittedMechanisms(out IReadOnlyList<string> unsupportedMechanismNames)
    {
        var parsedMechanisms = new List<MailAuthenticationMechanism>();
        var unsupportedNames = new List<string>();

        foreach (var configuredName in this.EffectivePermittedAuthenticationMechanisms)
        {
            if (MailAuthenticationMechanism.TryParseSaslName(configuredName, out var mechanism))
            {
                parsedMechanisms.Add(mechanism);
            }
            else
            {
                unsupportedNames.Add(configuredName ?? string.Empty);
            }
        }

        unsupportedMechanismNames = unsupportedNames;

        return MailAuthenticationPolicy.NormalizeMechanisms(parsedMechanisms);
    }

    private static string SettingFor(MailTransportSecurityViolation violation) => violation switch
    {
        MailTransportSecurityViolation.PermittedAuthenticationMechanismRequired => nameof(PermittedAuthenticationMechanisms),
        MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceRequired
            or MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable => nameof(TrustedCertificateAuthorityReference),
        MailTransportSecurityViolation.CertificateTrustNotSupported => nameof(CertificateTrust),
        _ => nameof(ConnectionSecurity),
    };

    private static string DescribeViolation(MailTransportSecurityViolation violation) => violation switch
    {
        MailTransportSecurityViolation.PermittedAuthenticationMechanismRequired =>
            "At least one supported SASL mechanism must be permitted.",
        MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn =>
            "An unencrypted connection requires AllowInsecureConnection.",
        MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn =>
            "A connection mode that continues unencrypted when the server offers no encryption requires AllowInsecureConnection.",
        MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection =>
            "A clear-text SASL mechanism on a channel that can stay unencrypted requires both AllowInsecureConnection and AllowClearTextAuthenticationOverUnencryptedConnection.",
        MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceRequired =>
            "Trusting an additional certificate authority requires TrustedCertificateAuthorityReference.",
        MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable =>
            "TrustedCertificateAuthorityReference applies only when CertificateTrust is AdditionalTrustedAuthority.",
        MailTransportSecurityViolation.ConnectionSecurityNotSupported =>
            "ConnectionSecurity must name one of the supported connection security modes.",
        MailTransportSecurityViolation.CertificateTrustNotSupported =>
            "CertificateTrust must name one of the supported certificate trust sources.",
        _ => "The transport security policy is not supported.",
    };
}
