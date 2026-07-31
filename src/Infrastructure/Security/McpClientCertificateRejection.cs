// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Security;

/// <summary>Why a client certificate was not accepted by a trust profile.</summary>
/// <remarks>
/// Every value produces the same response, so this vocabulary exists for the server log and never for the caller. It is
/// safe to record in full: a certificate a client presented is public material, and the values here describe the
/// certificate rather than the configuration it was judged against.
/// </remarks>
public enum McpClientCertificateRejection
{
    /// <summary>The connection carried no client certificate while a profile requires one.</summary>
    CertificateMissing = 0,

    /// <summary>The certificate carries no extended key usage naming client authentication, so it is not a certificate for authenticating a client.</summary>
    ClientAuthenticationUsageMissing = 1,

    /// <summary>The certificate names none of the subject alternative names the profile expects.</summary>
    SubjectAlternativeNameMismatch = 2,

    /// <summary>None of the profile's trust anchors could be loaded, so no chain could be built at all.</summary>
    /// <remarks>This describes the deployment rather than the certificate, and it refuses the request for that reason: an anchor that has become unreadable must never widen what the profile accepts.</remarks>
    TrustAnchorUnavailable = 3,

    /// <summary>The certificate is outside its validity period.</summary>
    CertificateExpired = 4,

    /// <summary>The certificate does not chain to any of the profile's trust anchors.</summary>
    ChainNotTrusted = 5,
}
