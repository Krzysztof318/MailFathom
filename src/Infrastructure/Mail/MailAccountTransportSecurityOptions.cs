// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Infrastructure.Mail;

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

    /// <summary>Gets or sets the secret block referencing deployment-provisioned trust anchor material.</summary>
    /// <remarks>
    /// The block carries a reference such as a credential name under the default interpretation mode, and the PEM text
    /// itself under an inline one — a trust anchor is a public certificate, so writing one into configuration leaks
    /// nothing. Its nested <see cref="ConfiguredSecret.Password" /> supplies the password of a protected PKCS#12
    /// bundle. A block present with a blank <see cref="ConfiguredSecret.SecretReference" /> reads as an absent anchor,
    /// so <c>"TrustedCertificateAuthority": {}</c> fails the domain rule that requires one instead of passing it and
    /// then failing later with a confusing missing-material error.
    /// </remarks>
    public ConfiguredSecret? TrustedCertificateAuthority { get; set; }

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
        this.ConfiguredTrustAnchorReference);

    /// <summary>Loads the certificate the account's server must chain to, when it trusts an additional authority.</summary>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a certificate.</param>
    /// <param name="cancellationToken">Cancels the retrieval of the material and of a bundle password.</param>
    /// <returns>
    /// The load outcome, which the caller owns and must dispose, or <see langword="null" /> when the account validates
    /// against the system trust store alone and no anchor applies.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchorLoader" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Unusable material is returned as a named failure rather than thrown, so startup can report every account's
    /// unusable anchor at once. A caller that is about to connect must treat that failure as fatal for the connection:
    /// continuing without the anchor would validate the private server against the system trust store and fail, or
    /// worse, invite an operator to look for a way to disable validation.
    /// </remarks>
    public async Task<TrustAnchorLoadResult?> LoadTrustedCertificateAuthorityAsync(
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustAnchorLoader);

        if (this.CertificateTrust != MailServerCertificateTrust.AdditionalTrustedAuthority)
        {
            return null;
        }

        return await trustAnchorLoader.LoadAsync(this.TrustedCertificateAuthority, cancellationToken);
    }

    /// <summary>Finds every unsupported mechanism name and every violated domain transport security rule.</summary>
    /// <returns>The errors to report at startup, empty when the settings are safe.</returns>
    /// <remarks>
    /// Descriptions name the setting and the rule only. The user name, password, and trust anchor reference stay out
    /// of them, because startup validation output reaches operator consoles and logs.
    /// </remarks>
    public IReadOnlyList<MailAccountTransportSecurityConfigurationError> FindConfigurationErrors()
    {
        var permittedMechanisms = this.ParsePermittedMechanisms(out var unsupportedMechanismNames);

        var violations = MailTransportSecurityPolicy.FindViolations(
            this.ConnectionSecurity,
            permittedMechanisms,
            this.AllowInsecureConnection,
            this.AllowClearTextAuthenticationOverUnencryptedConnection,
            this.CertificateTrust,
            this.ConfiguredTrustAnchorReference);

        return
        [
            .. unsupportedMechanismNames.Select(unsupportedMechanismName => new MailAccountTransportSecurityConfigurationError(
                nameof(this.PermittedAuthenticationMechanisms),
                $"SASL mechanism '{unsupportedMechanismName}' is not supported.",
                Violation: null)),
            .. violations.Select(violation => new MailAccountTransportSecurityConfigurationError(
                SettingFor(violation),
                DescribeViolation(violation),
                violation)),
        ];
    }

    /// <summary>The stand-in the domain receives for a configured value that is not a reference, such as inline PEM.</summary>
    private const string ConfiguredButUnparsedTrustAnchor = "***";

    /// <summary>Gets the masked trust anchor value, or <see langword="null" /> when no anchor is configured.</summary>
    /// <remarks>
    /// <para>
    /// The block is a configuration-adapter shape and must not cross into <c>Domain</c>, which keeps taking a nullable
    /// string. An empty reference inside a present block is an absent anchor here, so the domain rule reports the
    /// missing anchor the operator actually has to fix.
    /// </para>
    /// <para>
    /// The domain reads this for presence only, so presence is what it must answer: a non-blank configured value is an
    /// anchor the operator supplied, whether or not it is a <c>&lt;scheme&gt;:&lt;target&gt;</c> reference. Under an
    /// inline interpretation mode the value is the PEM text itself and parses as no reference at all; deciding
    /// presence by parsing would report a missing anchor for the one deployment shape that supplies it directly, and
    /// the host would refuse to start on a correct configuration.
    /// </para>
    /// <para>
    /// What crosses is never the operator's raw value. A parsed reference crosses as
    /// <see cref="SecretReference.ToString" /> and anything else as a fixed mask, because this is a public property of
    /// a record whose synthesized printing reaches any log line the policy appears in, and the raw value is a
    /// credential name, a file path, or the certificate itself. Whether the material is usable is the loader's
    /// question, not this one: it reports a named failure that names the encoding or the parse error, which is a
    /// better diagnostic than a presence rule could give.
    /// </para>
    /// </remarks>
    private string? ConfiguredTrustAnchorReference
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.TrustedCertificateAuthority?.SecretReference))
            {
                return null;
            }

            return SecretReference.TryParse(this.TrustedCertificateAuthority.SecretReference, out var reference, out _)
                ? reference.ToString()
                : ConfiguredButUnparsedTrustAnchor;
        }
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
            or MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable => nameof(TrustedCertificateAuthority),
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
            "Trusting an additional certificate authority requires a TrustedCertificateAuthority secret reference.",
        MailTransportSecurityViolation.TrustedCertificateAuthorityReferenceNotApplicable =>
            "TrustedCertificateAuthority applies only when CertificateTrust is AdditionalTrustedAuthority.",
        MailTransportSecurityViolation.ConnectionSecurityNotSupported =>
            "ConnectionSecurity must name one of the supported connection security modes.",
        MailTransportSecurityViolation.CertificateTrustNotSupported =>
            "CertificateTrust must name one of the supported certificate trust sources.",
        _ => "The transport security policy is not supported.",
    };
}
