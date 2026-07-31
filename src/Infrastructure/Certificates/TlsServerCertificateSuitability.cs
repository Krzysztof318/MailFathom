// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Decides whether a parsed certificate can actually serve a configured domain, and what is presented after it.</summary>
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

    private const string KeyUsageOid = "2.5.29.15";

    private const string BasicConstraintsOid = "2.5.29.19";

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

        if (!PermitsHandshakeSignature(leaf))
        {
            return CertificateMaterialFailure.DigitalSignatureNotPermitted;
        }

        return CoversDomain(leaf, domain)
            ? null
            : CertificateMaterialFailure.DomainNotCoveredBySubjectAlternativeName;
    }

    /// <summary>Orders the certificates supplied beside a leaf into the sequence that chains towards a root.</summary>
    /// <param name="leaf">The certificate the endpoint presents first.</param>
    /// <param name="supplied">The remaining certificates the configured material carried, in whatever order it carried them.</param>
    /// <param name="evaluatedAt">The instant each validity period is read against.</param>
    /// <returns>The ordered intermediates, or the reason the supplied certificates form no chain.</returns>
    /// <remarks>
    /// <para>
    /// Ordering is done here rather than trusted from the material, because a PKCS#12 bundle states no order at all and
    /// a PEM file states only the order whoever concatenated it chose. What a client receives has to lead from the leaf
    /// to its issuer, so the sequence is rebuilt from the issuer each certificate names.
    /// </para>
    /// <para>
    /// Signatures are deliberately not verified. Proving one would mean building a path to a trust anchor, and the
    /// anchor is exactly what a chain file omits — the root lives in the client's store, not in the deployment's
    /// material. What is provable without it is checked instead: that every supplied certificate is an authority, that
    /// it is currently valid, and that it issues something in the chain rather than sitting beside it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static TlsServerCertificateChainOrder OrderTowardsRoot(
        X509Certificate2 leaf,
        IReadOnlyList<X509Certificate2> supplied,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(supplied);

        if (FindChainUnsuitability(leaf, supplied, evaluatedAt) is { } failure)
        {
            return TlsServerCertificateChainOrder.Unusable(failure);
        }

        return TlsServerCertificateChainOrder.Ordered(IssuerSequenceFrom(leaf, supplied));
    }

    /// <summary>Finds the first reason the supplied certificates cannot be the chain of this leaf.</summary>
    private static CertificateMaterialFailure? FindChainUnsuitability(
        X509Certificate2 leaf,
        IReadOnlyList<X509Certificate2> supplied,
        DateTimeOffset evaluatedAt)
    {
        if (supplied.Any(static candidate => !IsCertificateAuthority(candidate)))
        {
            return CertificateMaterialFailure.ChainCarriesNonAuthorityCertificate;
        }

        if (supplied.Any(candidate => evaluatedAt < candidate.NotBefore.ToUniversalTime()
            || evaluatedAt > candidate.NotAfter.ToUniversalTime()))
        {
            return CertificateMaterialFailure.ChainCertificateNotCurrentlyValid;
        }

        // Membership rather than position, so a chain carrying both sides of a cross-signed authority stays usable:
        // two certificates may legitimately issue the same subject, and neither is unrelated for it.
        var relatedToTheChain = supplied.All(candidate => Issued(candidate, leaf)
            || supplied.Any(issued => !ReferenceEquals(issued, candidate) && Issued(candidate, issued)));

        return relatedToTheChain
            ? null
            : CertificateMaterialFailure.ChainCarriesUnrelatedCertificate;
    }

    /// <summary>Walks from the leaf to its issuer and onwards, leaving alternative issuers of a chained name behind the certificate they duplicate.</summary>
    private static X509Certificate2[] IssuerSequenceFrom(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> supplied)
    {
        var remaining = supplied.ToList();
        var ordered = new List<X509Certificate2>(supplied.Count);
        var issued = leaf;

        while (remaining.FirstOrDefault(candidate => Issued(candidate, issued)) is { } issuer)
        {
            ordered.Add(issuer);
            remaining.Remove(issuer);
            issued = issuer;
        }

        ordered.AddRange(remaining);

        return [.. ordered];
    }

    /// <summary>Matches an issuer's subject name against the issuer name a certificate carries, byte for byte.</summary>
    /// <remarks>The encoded form is compared rather than the printable one, because a distinguished name has several textual spellings and only one encoding, and a genuine issuer's is the one its subject copied into the certificate it signed.</remarks>
    private static bool Issued(X509Certificate2 issuer, X509Certificate2 subject) =>
        issuer.SubjectName.RawData.AsSpan().SequenceEqual(subject.IssuerName.RawData);

    /// <summary>Reads the basic constraints, treating their absence as a certificate that is not an authority.</summary>
    /// <remarks>RFC 5280 makes the extension what states the role, so a certificate without it issues nothing as far as every client is concerned, whatever else it carries.</remarks>
    private static bool IsCertificateAuthority(X509Certificate2 certificate)
    {
        var declaredConstraints = certificate.Extensions[BasicConstraintsOid];

        return declaredConstraints is not null
            && new X509BasicConstraintsExtension(declaredConstraints, declaredConstraints.Critical)
                .CertificateAuthority;
    }

    /// <summary>Reads the key usage, treating its absence as permission rather than as refusal.</summary>
    /// <remarks>
    /// An absent extension leaves the key unconstrained, which is what a private authority commonly issues. A present
    /// one is binding: a certificate that permits <c>keyEncipherment</c> alone was issued for a key exchange no current
    /// client offers, and the endpoint would bind and then fail every handshake.
    /// </remarks>
    private static bool PermitsHandshakeSignature(X509Certificate2 leaf)
    {
        var declaredUsage = leaf.Extensions[KeyUsageOid];

        if (declaredUsage is null)
        {
            return true;
        }

        var keyUsage = new X509KeyUsageExtension(declaredUsage, declaredUsage.Critical);

        return keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature);
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
