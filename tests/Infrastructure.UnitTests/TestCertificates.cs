// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Builds the certificates a trust-anchor test needs, entirely in memory.</summary>
/// <remarks>
/// Nothing here touches a certificate store, a file, or a network. Validity is expressed relative to the current
/// instant because chain building compares against the system clock, which no injected time provider reaches; the
/// window is wide enough that no test depends on when it runs.
/// </remarks>
internal static class TestCertificates
{
    private static readonly DateTimeOffset ValidFrom = DateTimeOffset.UtcNow.AddDays(-1);
    private static readonly DateTimeOffset ValidUntil = DateTimeOffset.UtcNow.AddDays(30);

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
        Issue(issuer, dnsName, certificateAuthority: false, dnsName, keepPrivateKey: false);

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

    private static X509Certificate2 Issue(
        X509Certificate2 issuer,
        string commonName,
        bool certificateAuthority,
        string? dnsName,
        bool keepPrivateKey)
    {
        using var subjectKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", subjectKey, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        if (dnsName is not null)
        {
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(dnsName);
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        }

        var issued = request.Create(issuer, ValidFrom, ValidUntil, RandomNumberGenerator.GetBytes(16));
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
