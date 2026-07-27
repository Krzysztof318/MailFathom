// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Builds the certificates a trust-anchor test needs, entirely in memory.</summary>
/// <remarks>
/// Nothing here touches a certificate store, a file, or a network, and no value depends on when the test runs.
/// Validity is a fixed absolute interval rather than an offset from the current instant: chain building compares
/// against the system clock, which no injected time provider reaches, so the interval is made wide enough that the
/// comparison has one answer for the lifetime of this repository instead of one that a clock jump could change.
/// Serial numbers come from a counter for the same reason — they must be unique per issuer, not unpredictable.
/// </remarks>
internal static class TestCertificates
{
    /// <summary><c>id-kp-clientAuth</c>, which a TLS server certificate must not be limited to.</summary>
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private static readonly DateTimeOffset ValidFrom = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static long issuedCount;

    /// <summary>Creates a self-signed certificate authority, private key included so it can issue.</summary>
    internal static X509Certificate2 CreateCertificateAuthority(string commonName)
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", signingKey, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        return request.CreateSelfSigned(ValidFrom, ValidUntil);
    }

    /// <summary>Issues an intermediate authority under an existing one, private key included so it can issue in turn.</summary>
    internal static X509Certificate2 IssueIntermediateAuthority(X509Certificate2 issuer, string commonName) =>
        Issue(issuer, commonName, certificateAuthority: true, dnsName: null, keepPrivateKey: true);

    /// <summary>Issues a server certificate for one host name, as the server presents it on the wire.</summary>
    internal static X509Certificate2 IssueServerCertificate(X509Certificate2 issuer, string dnsName) =>
        Issue(issuer, commonName: dnsName, certificateAuthority: false, dnsName, keepPrivateKey: false);

    /// <summary>Issues a certificate for the same host that is usable only for client authentication.</summary>
    /// <remarks>The same private authority commonly issues both kinds, which is why one must not be accepted as the other.</remarks>
    internal static X509Certificate2 IssueClientAuthenticationCertificate(X509Certificate2 issuer, string dnsName) =>
        Issue(
            issuer,
            commonName: dnsName,
            certificateAuthority: false,
            dnsName,
            keepPrivateKey: false,
            extendedKeyUsageOid: ClientAuthenticationOid);

    /// <summary>Strips the private key, which is what a deployment provisions as a trust anchor.</summary>
    internal static X509Certificate2 WithoutPrivateKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

    internal static byte[] ToPem(X509Certificate2 certificate) =>
        System.Text.Encoding.UTF8.GetBytes(certificate.ExportCertificatePem());

    internal static byte[] ToDer(X509Certificate2 certificate) => certificate.Export(X509ContentType.Cert);

    internal static byte[] ToBundle(X509Certificate2 certificate, string? bundlePassword = null) =>
        bundlePassword is null
            ? certificate.Export(X509ContentType.Pkcs12)
            : certificate.Export(X509ContentType.Pkcs12, bundlePassword);

    private static byte[] NextSerialNumber() => BitConverter.GetBytes(Interlocked.Increment(ref issuedCount));

    private static X509Certificate2 Issue(
        X509Certificate2 issuer,
        string commonName,
        bool certificateAuthority,
        string? dnsName,
        bool keepPrivateKey,
        string? extendedKeyUsageOid = null)
    {
        using var subjectKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", subjectKey, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        if (extendedKeyUsageOid is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid(extendedKeyUsageOid)],
                critical: true));
        }

        if (dnsName is not null)
        {
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(dnsName);
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        }

        var issued = request.Create(issuer, ValidFrom, ValidUntil, NextSerialNumber());
        if (!keepPrivateKey)
        {
            return issued;
        }

        using (issued)
        {
            return issued.CopyWithPrivateKey(subjectKey);
        }
    }
}
