// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Security.ClientCertificates;

/// <summary>Decides whether a client certificate chains to one of a profile's configured authorities.</summary>
/// <remarks>
/// <para>
/// Trust is decided by building the chain against the configured anchors and nothing else. The machine's own trust
/// store takes no part: a deployment that trusts the certificate authorities a server happens to ship would accept
/// every client certificate the public infrastructure has ever issued, which is the opposite of naming the client this
/// endpoint serves.
/// </para>
/// <para>
/// The server sees the leaf certificate alone. A TLS client sends its issuing chain, but the connection exposes only
/// the certificate that identifies it, so a client chaining through an intermediate is trusted by configuring that
/// intermediate as an anchor beside its root rather than by hoping the handshake supplied it. An intermediate
/// configured on its own completes no path to a root and therefore trusts nothing, which is the safe direction: it
/// refuses the client rather than trusting everything that authority ever signed for.
/// </para>
/// </remarks>
internal static class McpClientCertificateChainValidator
{
    /// <summary>The extended key usage a certificate must carry to authenticate a TLS client, <c>id-kp-clientAuth</c>.</summary>
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>The extended key usage extension, <c>id-ce-extKeyUsage</c>.</summary>
    private const string EnhancedKeyUsageExtensionOid = "2.5.29.37";

    /// <summary>The subject alternative name extension, <c>id-ce-subjectAltName</c>.</summary>
    private const string SubjectAlternativeNameExtensionOid = "2.5.29.17";

    /// <summary>Reports whether a certificate carries the extended key usage that makes it a client certificate.</summary>
    /// <param name="certificate">The certificate the connection presented.</param>
    /// <returns><see langword="true" /> when the certificate names client authentication; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="certificate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A certificate carrying no extended key usage at all is refused rather than read as unrestricted. The X.509 rule
    /// that absence means every usage is what would let a server certificate, or a signing certificate the same
    /// authority issued, be presented here; a profile that names client authentication asked for a certificate that
    /// says so.
    /// </remarks>
    internal static bool CarriesClientAuthenticationUsage(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return certificate.Extensions
            .Where(extension => extension.Oid?.Value == EnhancedKeyUsageExtensionOid)
            .SelectMany(DecodeUsages)
            .Any(usage => usage.Value == ClientAuthenticationOid);
    }

    /// <summary>Reads the DNS names a certificate carries as subject alternative names.</summary>
    /// <param name="certificate">The certificate the connection presented.</param>
    /// <returns>The DNS names, empty when the certificate carries no subject alternative name extension.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="certificate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The subject common name is deliberately not consulted. It has not identified a certificate's subject since
    /// RFC 2818 was superseded, no certificate authority is obliged to make it meaningful, and reading it would let a
    /// certificate be accepted for a name no authority ever attested to.
    /// </remarks>
    internal static IReadOnlyList<string> ReadSubjectAlternativeDnsNames(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return
        [
            .. certificate.Extensions
                .Where(extension => extension.Oid?.Value == SubjectAlternativeNameExtensionOid)
                .SelectMany(DecodeDnsNames),
        ];
    }

    /// <summary>Decodes one extension into the usages it names.</summary>
    /// <remarks>
    /// Decoded from the raw extension rather than read off a typed one the certificate handed back, because which
    /// extensions the platform materializes as typed instances is its own detail and a type filter would silently skip
    /// an extension it did not recognize. Malformed contents name no usage rather than throwing: this runs against
    /// whatever a stranger opened a connection with, so an unparseable extension has to end in an ordinary refusal
    /// instead of an unhandled failure in the request pipeline.
    /// </remarks>
    private static IReadOnlyList<Oid> DecodeUsages(X509Extension extension)
    {
        try
        {
            return [.. new X509EnhancedKeyUsageExtension(extension, extension.Critical).EnhancedKeyUsages.OfType<Oid>()];
        }
        catch (CryptographicException)
        {
            return [];
        }
    }

    /// <summary>Decodes one extension into the DNS names it carries, on the same terms as the usages above.</summary>
    private static IReadOnlyList<string> DecodeDnsNames(X509Extension extension)
    {
        try
        {
            return [.. new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical).EnumerateDnsNames()];
        }
        catch (CryptographicException)
        {
            return [];
        }
    }

    /// <summary>Builds the certificate's chain against a profile's anchors.</summary>
    /// <param name="trustAnchors">The anchors that were loaded for the profile, at least one.</param>
    /// <param name="certificate">The certificate the connection presented.</param>
    /// <param name="verificationTime">The instant every validity period in the chain is judged against, taken from the caller's injected clock.</param>
    /// <returns><see langword="null" /> when the certificate is trusted; otherwise why it is not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchors" /> or <paramref name="certificate" /> is <see langword="null" />.</exception>
    internal static McpClientCertificateRejection? FindChainRejection(
        IReadOnlyList<X509Certificate2> trustAnchors,
        X509Certificate2 certificate,
        DateTimeOffset verificationTime)
    {
        ArgumentNullException.ThrowIfNull(trustAnchors);
        ArgumentNullException.ThrowIfNull(certificate);

        using var chain = new X509Chain();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        foreach (var trustAnchor in trustAnchors)
        {
            chain.ChainPolicy.CustomTrustStore.Add(trustAnchor);
        }

        // Re-applied to the chain as well as checked outright, because a chain error also covers a certificate rejected
        // for its usage and leaving it out would trust an issuing certificate the same authority signed.
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));

        // A client certificate arrives without its issuing chain, so there is nothing to download that this deployment
        // configured. Leaving downloads enabled would let a certificate a stranger presented send this synchronous
        // validation to a URL of their choosing, on the request thread, with no cancellation reaching it.
        chain.ChainPolicy.DisableCertificateDownloads = true;

        // The authorities behind a client profile are commonly private and publish neither a revocation list nor a
        // responder, so an online check would refuse every client this feature exists to serve. Withdrawing a client is
        // therefore removing its profile or its anchor, which takes effect on the next request.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Pinned rather than left at its default, so no later edit can relax expiry or basic-constraint checking without
        // deleting a line that says what it is doing.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        // Supplied rather than left to the chain builder, which would otherwise read the machine's own clock — the one
        // ambient clock the analyzers cannot see. Setting it keeps the moment a certificate expires a decision of the
        // injected clock, which is what makes the boundary reachable from a test.
        chain.ChainPolicy.VerificationTime = verificationTime.UtcDateTime;

        return chain.Build(certificate) ? null : DescribeChainFailure(chain);
    }

    /// <summary>Names what the chain objected to, in the terms an operator reads.</summary>
    /// <remarks>
    /// Validity is separated from trust because the two are different operator problems: an expired certificate is a
    /// client that has to renew, while an untrusted one is a deployment that has to be told about an authority. Every
    /// other status collapses into the untrusted answer, because the response is the same and a finer vocabulary would
    /// only describe the platform rather than the deployment.
    /// </remarks>
    private static McpClientCertificateRejection DescribeChainFailure(X509Chain chain) =>
        chain.ChainStatus.Any(status => status.Status.HasFlag(X509ChainStatusFlags.NotTimeValid))
            ? McpClientCertificateRejection.CertificateExpired
            : McpClientCertificateRejection.ChainNotTrusted;
}
