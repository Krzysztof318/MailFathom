// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Decides whether a parsed certificate can actually serve a configured domain.</summary>
/// <remarks>
/// <para>
/// Parsing proves only that material is a certificate. Everything a client will check afterwards is checked here
/// instead, at startup, because the alternative is an endpoint that binds successfully and then fails every handshake
/// with a message only the client sees. An operator who provisioned yesterday's certificate, or the certificate for
/// the other host name, learns it from the startup log rather than from a user.
/// </para>
/// <para>
/// The checks are deliberately the client's, not a superset of them: the subject common name is never consulted,
/// because current clients ignore it, and a certificate accepted here on the strength of its common name would still
/// be refused by everything that connects.
/// </para>
/// </remarks>
internal static class TlsServerCertificateSuitability
{
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    private const string ExtendedKeyUsageOid = "2.5.29.37";

    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";

    /// <summary>Finds the first reason a certificate cannot serve a domain.</summary>
    /// <param name="leaf">The parsed leaf certificate.</param>
    /// <param name="domain">The exact DNS domain the endpoint publishes, in its ASCII form.</param>
    /// <param name="evaluatedAt">The instant the validity period is read against.</param>
    /// <returns>The failure, or <see langword="null" /> when the certificate can serve the domain.</returns>
    /// <remarks>
    /// One reason is reported rather than all of them, in the order an operator would fix them: a certificate with no
    /// private key is unusable whatever its names say, and an expired certificate has to be replaced before its names
    /// are worth checking.
    /// </remarks>
    internal static CertificateMaterialFailure? FindUnsuitability(
        X509Certificate2 leaf,
        string domain,
        DateTimeOffset evaluatedAt)
    {
        if (!leaf.HasPrivateKey)
        {
            return CertificateMaterialFailure.PrivateKeyMissing;
        }

        if (evaluatedAt < leaf.NotBefore.ToUniversalTime())
        {
            return CertificateMaterialFailure.CertificateNotYetValid;
        }

        if (evaluatedAt > leaf.NotAfter.ToUniversalTime())
        {
            return CertificateMaterialFailure.CertificateExpired;
        }

        if (!PermitsServerAuthentication(leaf))
        {
            return CertificateMaterialFailure.ServerAuthenticationNotPermitted;
        }

        return CoversDomain(leaf, domain)
            ? null
            : CertificateMaterialFailure.DomainNotCoveredBySubjectAlternativeName;
    }

    /// <summary>Reads the extended key usage, treating its absence as permission rather than as refusal.</summary>
    /// <remarks>
    /// An absent extension means the certificate is unconstrained, which is what a private authority commonly issues,
    /// so refusing it would reject working material. <c>anyExtendedKeyUsage</c> is honored for the same reason: it is
    /// the explicit spelling of the same statement.
    /// </remarks>
    private static bool PermitsServerAuthentication(X509Certificate2 leaf)
    {
        var declaredUsages = leaf.Extensions[ExtendedKeyUsageOid];

        if (declaredUsages is null)
        {
            return true;
        }

        var extendedKeyUsage = new X509EnhancedKeyUsageExtension(declaredUsages, declaredUsages.Critical);

        return extendedKeyUsage.EnhancedKeyUsages
            .OfType<Oid>()
            .Any(static usage => usage.Value is ServerAuthenticationOid or AnyExtendedKeyUsageOid);
    }

    /// <summary>Matches the configured domain against the certificate's DNS subject alternative names.</summary>
    /// <remarks>
    /// The extension is rebuilt from its raw data rather than cast, because the platform surfaces a certificate's
    /// extensions as the base type unless it recognizes them, and this one is not among the recognized set.
    /// </remarks>
    private static bool CoversDomain(X509Certificate2 leaf, string domain)
    {
        var declaredNames = leaf.Extensions[SubjectAlternativeNameOid];

        if (declaredNames is null)
        {
            return false;
        }

        var subjectAlternativeName = new X509SubjectAlternativeNameExtension(
            declaredNames.RawData,
            declaredNames.Critical);

        return subjectAlternativeName.EnumerateDnsNames()
            .Any(declaredName => Covers(declaredName, domain));
    }

    /// <summary>Matches one declared DNS name against the domain, honoring the single left-most wildcard label clients accept.</summary>
    /// <remarks>
    /// RFC 6125 admits <c>*.example.com</c> for one label and no more, so it covers <c>mail.example.com</c> and neither
    /// <c>example.com</c> nor <c>a.b.example.com</c>. Wildcard certificates are ordinary purchases, so refusing them
    /// would reject material an operator is entitled to use; matching more than clients do would accept material they
    /// will refuse.
    /// </remarks>
    private static bool Covers(string declaredName, string domain)
    {
        if (string.Equals(declaredName, domain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!declaredName.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var coveredParent = declaredName[2..];
        var firstLabelEnd = domain.IndexOf('.', StringComparison.Ordinal);

        return firstLabelEnd > 0
            && string.Equals(domain[(firstLabelEnd + 1)..], coveredParent, StringComparison.OrdinalIgnoreCase);
    }
}
