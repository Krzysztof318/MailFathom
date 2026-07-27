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
}
