// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Configures where the TLS identity MailMcp presents to a client comes from.</summary>
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
}
