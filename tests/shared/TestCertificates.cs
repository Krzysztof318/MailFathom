// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.TestSupport;

/// <summary>Builds the certificates a certificate test needs, entirely in memory.</summary>
/// <remarks>
/// <para>
/// Nothing here touches a certificate store, a file, or a network, and no value depends on when the test runs.
/// Validity is a fixed absolute interval rather than an offset from the current instant: chain building compares
/// against the system clock, which no injected time provider reaches, so the interval is made wide enough that the
/// comparison has one answer for the lifetime of this repository instead of one that a clock jump could change.
/// Serial numbers come from a counter for the same reason — they must be unique per issuer, not unpredictable.
/// </para>
/// <para>
/// The server-identity members below are the exception, and deliberately so. A certificate a server presents is judged
/// against an injected clock rather than against the system one, so those take their validity period from the test —
/// which is what lets an expired and a not-yet-valid certificate be built without waiting for either.
/// </para>
/// </remarks>
internal static class TestCertificates
{
    /// <summary><c>id-kp-serverAuth</c>, which a TLS server certificate has to permit and a client certificate must not be limited to.</summary>
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary><c>id-kp-clientAuth</c>, which a TLS client certificate has to permit and a server certificate must not be limited to.</summary>
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private static readonly DateTimeOffset ValidFrom = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiredBy = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

    /// <summary>Creates a self-signed certificate authority whose own validity ended long ago.</summary>
    /// <remarks>Its purpose is to prove that a chain is refused for an authority that has expired, which a client meets as a path it cannot build rather than as anything the leaf says.</remarks>
    internal static X509Certificate2 CreateExpiredCertificateAuthority(string commonName)
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

        return request.CreateSelfSigned(ValidFrom, ExpiredBy);
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

    /// <summary>Issues a certificate limited to server authentication, which a client profile must not accept.</summary>
    internal static X509Certificate2 IssueServerAuthenticationCertificate(X509Certificate2 issuer, string dnsName) =>
        Issue(
            issuer,
            commonName: dnsName,
            certificateAuthority: false,
            dnsName,
            keepPrivateKey: false,
            extendedKeyUsageOid: ServerAuthenticationOid);

    /// <summary>Issues a client certificate whose validity ended long ago, so no clock the test runs under revives it.</summary>
    internal static X509Certificate2 IssueExpiredClientAuthenticationCertificate(X509Certificate2 issuer, string dnsName) =>
        Issue(
            issuer,
            commonName: dnsName,
            certificateAuthority: false,
            dnsName,
            keepPrivateKey: false,
            extendedKeyUsageOid: ClientAuthenticationOid,
            validFrom: ValidFrom,
            validUntil: ExpiredBy);

    /// <summary>Strips the private key, which is what a deployment provisions as a trust anchor.</summary>
    internal static X509Certificate2 WithoutPrivateKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));

    /// <summary>Creates the identity a TLS server presents: the private key kept, the names carried, and the validity period the test chose.</summary>
    /// <param name="dnsNames">The subject alternative names the certificate covers.</param>
    /// <param name="notBefore">When the certificate becomes valid.</param>
    /// <param name="notAfter">When the certificate stops being valid.</param>
    /// <param name="issuer">The authority that signs it, or <see langword="null" /> for a self-signed certificate.</param>
    /// <param name="serverAuthentication">Whether the extended key usage permits server authentication or only client authentication.</param>
    /// <param name="keyUsage">The key usage the certificate declares, or <see langword="null" /> to declare none and leave the key unconstrained.</param>
    /// <returns>The certificate, with its private key, owned by the caller.</returns>
    /// <remarks>
    /// This differs from <see cref="IssueServerCertificate" /> in the two ways a presented identity differs from an
    /// observed one: the private key stays, because a server signs the handshake with it, and the validity period is
    /// the caller's, because that is what the expiry rules are asserted on.
    /// </remarks>
    internal static X509Certificate2 CreateServerIdentity(
        string[] dnsNames,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        X509Certificate2? issuer = null,
        bool serverAuthentication = true,
        X509KeyUsageFlags? keyUsage = null)
    {
        using var subjectKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN={dnsNames.FirstOrDefault() ?? "unnamed"}",
            subjectKey,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(serverAuthentication ? ServerAuthenticationOid : ClientAuthenticationOid)],
            critical: false));

        if (keyUsage is { } declaredKeyUsage)
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(declaredKeyUsage, critical: true));
        }

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();

        foreach (var dnsName in dnsNames)
        {
            subjectAlternativeNames.AddDnsName(dnsName);
        }

        request.CertificateExtensions.Add(subjectAlternativeNames.Build());

        if (issuer is null)
        {
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        using var issued = request.Create(issuer, notBefore, notAfter, NextSerialNumber());

        return issued.CopyWithPrivateKey(subjectKey);
    }

    /// <summary>Creates an identity whose only name is its subject common name.</summary>
    /// <param name="commonName">The common name, which is deliberately the only name it carries.</param>
    /// <param name="notBefore">When the certificate becomes valid.</param>
    /// <param name="notAfter">When the certificate stops being valid.</param>
    /// <returns>The certificate, with its private key, owned by the caller.</returns>
    /// <remarks>Its purpose is to prove that a common name is never read as a substitute for a subject alternative name.</remarks>
    internal static X509Certificate2 CreateIdentityWithoutSubjectAlternativeName(
        string commonName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var subjectKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", subjectKey, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(ServerAuthenticationOid)],
            critical: false));

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>Concatenates certificates as PEM, leaf first, the way a certificate authority delivers a chain.</summary>
    internal static string ToCertificateChainPem(params X509Certificate2[] certificates) =>
        string.Join('\n', certificates.Select(static certificate => certificate.ExportCertificatePem()));

    /// <summary>Exports a certificate's private key as an unencrypted PKCS#8 PEM.</summary>
    internal static string ToPrivateKeyPem(X509Certificate2 certificate)
    {
        using var privateKey = certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("The certificate carries no ECDSA private key.");

        return privateKey.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Exports a certificate's private key as a password-protected PKCS#8 PEM.</summary>
    /// <remarks>The iteration count is deliberately low: nothing here protects a real key, and a realistic count would cost every test that uses one a measurable delay.</remarks>
    internal static string ToEncryptedPrivateKeyPem(X509Certificate2 certificate, string password)
    {
        using var privateKey = certificate.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("The certificate carries no ECDSA private key.");

        return privateKey.ExportEncryptedPkcs8PrivateKeyPem(
            password,
            new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, iterationCount: 1));
    }

    /// <summary>Packs several certificates into one PKCS#12 bundle, their private keys travelling with them.</summary>
    internal static byte[] ToBundleOf(string? bundlePassword, params X509Certificate2[] certificates)
    {
        var bundle = new X509Certificate2Collection();
        bundle.AddRange(certificates);

        return bundle.Export(X509ContentType.Pkcs12, bundlePassword)
            ?? throw new InvalidOperationException("The certificate collection produced no bundle.");
    }

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
        string? extendedKeyUsageOid = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
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

        var issued = request.Create(
            issuer,
            validFrom ?? ValidFrom,
            validUntil ?? ValidUntil,
            NextSerialNumber());
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
