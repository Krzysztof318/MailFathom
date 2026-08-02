// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Identifies how deployment-provisioned certificate material is encoded.</summary>
/// <remarks>
/// The encoding is detected from the material rather than declared in configuration, because an operator who has to
/// name the encoding of a file a certificate authority handed them can name it wrongly, and the resulting failure
/// would describe a parse error instead of the mistake. It is a separate concept from the material's provenance: only
/// <see cref="Pem" /> can be supplied inline, so a loader needs both to decide whether the material is acceptable.
/// </remarks>
public enum CertificateMaterialEncoding
{
    /// <summary>Base64 text delimited by <c>-----BEGIN</c> and <c>-----END</c> markers.</summary>
    Pem = 0,

    /// <summary>A binary DER-encoded X.509 certificate.</summary>
    Der = 1,

    /// <summary>A binary PKCS#12 / PFX bundle, which may be protected by a password.</summary>
    Pkcs12 = 2,

    /// <summary>The material matches none of the supported encodings.</summary>
    Unrecognized = 3,
}
