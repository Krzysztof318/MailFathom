// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Identifies why configured certificate material produced no usable certificate.</summary>
/// <remarks>
/// The identity is the whole failure vocabulary a diagnostic may carry. A trust anchor is public material, so its
/// subject and thumbprint may be logged once it loads, but nothing here may carry the reference target, the bundle
/// password, or any part of the material that failed to load.
/// </remarks>
public enum CertificateMaterialFailure
{
    /// <summary>No material is configured at all.</summary>
    MaterialMissing = 0,

    /// <summary>The configured reference, or the reference to the bundle password, produced no material.</summary>
    /// <remarks>The resolution failure itself is reported separately by the startup check that walks every secret-bearing setting, which is where an operator reads the exact cause.</remarks>
    SecretNotResolvable = 1,

    /// <summary>The material matches none of the supported certificate encodings.</summary>
    EncodingNotRecognized = 2,

    /// <summary>Binary material was supplied inline, where only PEM has a faithful representation.</summary>
    InlineEncodingNotSupported = 3,

    /// <summary>The material has a supported encoding but does not parse.</summary>
    MaterialNotReadable = 4,

    /// <summary>The bundle parsed but carries no certificate.</summary>
    BundleCarriesNoCertificate = 5,

    /// <summary>The certificate carries a private key, which a trust anchor must not.</summary>
    TrustAnchorCarriesPrivateKey = 6,

    /// <summary>The bundle is protected and no nested password block was configured for it.</summary>
    /// <remarks>An unprotected bundle is a legitimate file, so this is reported only when opening the bundle without a password failed.</remarks>
    BundlePasswordMissing = 7,

    /// <summary>The bundle did not open with the password the nested block supplied.</summary>
    /// <remarks>The platform reports a wrong password and corrupt bundle contents identically; the configured password is named as the likelier cause because it is the part an operator controls.</remarks>
    BundlePasswordIncorrect = 8,

    /// <summary>A PKCS#12 bundle and separate PEM material were both configured, so which one supplies the identity is undecidable.</summary>
    MaterialKindAmbiguous = 9,

    /// <summary>The material carries no private key, so it identifies a server it cannot prove it is.</summary>
    PrivateKeyMissing = 10,

    /// <summary>The private-key material does not parse, or the password configured for it did not open it.</summary>
    /// <remarks>The two are one identity because the platform reports them identically and neither may be narrowed by disclosing which part of the material was rejected.</remarks>
    PrivateKeyNotReadable = 11,

    /// <summary>The private key parsed but belongs to a different certificate than the leaf it was configured beside.</summary>
    PrivateKeyDoesNotMatchCertificate = 12,

    /// <summary>The certificate's validity period has not started yet.</summary>
    CertificateNotYetValid = 13,

    /// <summary>The certificate's validity period has ended.</summary>
    CertificateExpired = 14,

    /// <summary>No subject alternative name of the certificate covers the configured domain.</summary>
    /// <remarks>The common name is deliberately not consulted: every current client ignores it, so honoring it here would accept material no client will.</remarks>
    DomainNotCoveredBySubjectAlternativeName = 15,

    /// <summary>The certificate's extended key usage excludes server authentication.</summary>
    ServerAuthenticationNotPermitted = 16,

    /// <summary>The supplied chain material carries more than one certificate that could be the leaf.</summary>
    /// <remarks>A chain states one identity followed by the authorities that issued it; a second key-bearing or repeated leaf makes which identity is presented depend on parse order rather than on what an operator provisioned.</remarks>
    ChainCarriesSeveralLeaves = 17,

    /// <summary>The material parsed, but its encoding cannot serve the role the setting configured it for.</summary>
    /// <remarks>A PEM chain placed in the bundle setting and a PKCS#12 bundle placed in the chain setting are both this failure; each is a legitimate file in the other setting.</remarks>
    EncodingNotSupportedForRole = 18,

    /// <summary>The certificate's key usage excludes <c>digitalSignature</c>, which every negotiable handshake needs it to permit.</summary>
    /// <remarks>TLS 1.3 authenticates a server by having it sign the transcript, and the key exchanges TLS 1.2 still negotiates do the same, so a certificate limited to <c>keyEncipherment</c> completes no handshake this endpoint offers.</remarks>
    DigitalSignatureNotPermitted = 19,

    /// <summary>A certificate supplied after the leaf is not a certificate authority, so it can issue nothing.</summary>
    /// <remarks>A second end-entity certificate pasted into a chain file is the usual cause; it is presented to every client and issues none of the certificates before it.</remarks>
    ChainCarriesNonAuthorityCertificate = 20,

    /// <summary>A certificate supplied after the leaf is outside its own validity period.</summary>
    /// <remarks>The leaf's period is reported separately, because an expired intermediate is renewed from the authority that issued it while an expired leaf is reissued for the domain.</remarks>
    ChainCertificateNotCurrentlyValid = 21,

    /// <summary>A certificate supplied after the leaf issues neither the leaf nor another supplied certificate.</summary>
    /// <remarks>It therefore takes no part in the path a client builds, and its presence means the chain that was provisioned is not the chain the leaf belongs to.</remarks>
    ChainCarriesUnrelatedCertificate = 22,
}
