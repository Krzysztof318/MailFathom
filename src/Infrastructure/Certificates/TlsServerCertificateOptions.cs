// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Configures where the TLS identity MailFathom presents to a client comes from.</summary>
/// <remarks>
/// <para>
/// Two provisioning shapes exist because both occur: a certificate authority hands an operator a PEM chain beside a
/// separate PEM key, and a platform or an export tool hands them one PKCS#12 bundle holding both. Exactly one of the
/// two is configured; supplying both leaves which material states the identity undecidable, so it is rejected rather
/// than resolved by a precedence rule nobody would remember.
/// </para>
/// <para>
/// Every part binds to <see cref="ConfiguredSecret" />, including the certificate chain, which is public material. The
/// block is the reference mechanism rather than a claim about secrecy: binding the chain to it is what lets an operator
/// point at a file, a systemd credential, or an environment variable with the same grammar the private key uses, and
/// what makes <see cref="ConfiguredSecretDiscovery" /> prove at startup that each part is reachable. The consequence
/// that matters is the one the default interpretation mode enforces: under <see cref="SecretValueInterpretation.ReferenceOnly" />
/// a private key or a bundle password written straight into configuration fails startup instead of being used.
/// </para>
/// </remarks>
public sealed class TlsServerCertificateOptions
{
    /// <summary>Gets or sets the PKCS#12/PFX bundle holding the leaf, its private key, and any intermediates, whose nested password block opens it when it is protected.</summary>
    public ConfiguredSecret? Bundle { get; set; }

    /// <summary>Gets or sets the PEM certificate material, whose first certificate is the leaf and whose remaining certificates are the chain presented after it.</summary>
    public ConfiguredSecret? CertificateChain { get; set; }

    /// <summary>Gets or sets the PEM private key belonging to the leaf, whose nested password block decrypts it when it is encrypted.</summary>
    public ConfiguredSecret? PrivateKey { get; set; }

    /// <summary>Gets whether any material is configured at all.</summary>
    /// <remarks>It answers "did the operator provision an identity here", which is what a listener serving no TLS asks before refusing material nothing would present.</remarks>
    public bool IsConfigured => IsBlockConfigured(this.Bundle)
        || IsBlockConfigured(this.CertificateChain)
        || IsBlockConfigured(this.PrivateKey);

    /// <summary>Finds everything an operator must fix before this material can be loaded.</summary>
    /// <param name="configurationPath">The configuration path of this block, which prefixes every reported error.</param>
    /// <returns>One message per faulty setting, empty when the block names material that could be loaded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Only the shape is decided here: which of the two provisioning kinds the block names, and whether it names one of
    /// them completely. Whether the referenced material resolves, parses, carries a matching private key, is currently
    /// valid, and covers the domain is <see cref="TlsServerCertificateLoader" />'s question, answered against real
    /// material before any listener is opened.
    /// </para>
    /// <para>
    /// The rule lives with the type it constrains rather than with either endpoint that binds it, because it is a
    /// property of the material contract: every listener MailFathom terminates TLS on provisions its identity the same
    /// way, and a second copy of these four rules would be a second place for them to drift.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors(string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        var bundleConfigured = IsBlockConfigured(this.Bundle);
        var chainConfigured = IsBlockConfigured(this.CertificateChain);
        var privateKeyConfigured = IsBlockConfigured(this.PrivateKey);

        if (bundleConfigured && (chainConfigured || privateKeyConfigured))
        {
            return [$"{configurationPath} — a PKCS#12 bundle and separate PEM material are both configured; state one or the other, because which of them supplies the identity would otherwise be decided by nothing an operator wrote."];
        }

        if (bundleConfigured)
        {
            return [];
        }

        if (!chainConfigured && !privateKeyConfigured)
        {
            return [$"{configurationPath} — a TLS listener must state where its certificate comes from: a '{nameof(this.Bundle)}' holding a PKCS#12 bundle, or a '{nameof(this.CertificateChain)}' beside its '{nameof(this.PrivateKey)}'. There is no development-certificate fallback and no self-signed one, because a listener answering on a port an operator believed was TLS is worse than one that does not answer."];
        }

        if (!chainConfigured)
        {
            return [$"{configurationPath}:{nameof(this.CertificateChain)} — a private key is configured with no certificate to pair it with."];
        }

        return privateKeyConfigured
            ? []
            : [$"{configurationPath}:{nameof(this.PrivateKey)} — a certificate is configured with no private key, so the listener could name the domain but not prove it is the domain."];
    }

    private static bool IsBlockConfigured(ConfiguredSecret? block) =>
        block is not null && !string.IsNullOrWhiteSpace(block.SecretReference);
}
