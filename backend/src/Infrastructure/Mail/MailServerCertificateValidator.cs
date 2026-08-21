// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Mail;

/// <summary>Decides whether a mail server certificate is trusted once a deployment-provisioned authority is configured.</summary>
/// <remarks>
/// <para>
/// This runs only for an account whose certificate trust names an additional authority. Every other account keeps the
/// mail client's own validating default, and no configuration path anywhere turns validation off: a private server is
/// supported by supplying an anchor, never by accepting an error.
/// </para>
/// <para>
/// Trust is decided by rebuilding the chain against the configured anchor rather than by forgiving the error the
/// platform reported. A name mismatch and an unavailable certificate are refused outright, because neither has
/// anything to do with which authority signed the certificate, and forgiving them would turn the private-authority
/// path into the validation bypass this design exists to avoid.
/// </para>
/// </remarks>
internal static class MailServerCertificateValidator
{
    /// <summary>The extended key usage a certificate must carry to authenticate a TLS server, <c>id-kp-serverAuth</c>.</summary>
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>Reports whether the server certificate chains to the configured trust anchor.</summary>
    /// <param name="trustAnchor">The deployment-provisioned authority, which is the only root the rebuild trusts.</param>
    /// <param name="serverCertificate">The certificate the server presented, or <see langword="null" /> when it presented none.</param>
    /// <param name="platformChain">The chain the platform built, whose intermediates are reused as path-building candidates.</param>
    /// <param name="platformErrors">What the platform's own validation objected to.</param>
    /// <returns><see langword="true" /> when the certificate is trusted; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchor" /> is <see langword="null" />.</exception>
    internal static bool IsServerCertificateTrusted(
        X509Certificate2 trustAnchor,
        X509Certificate? serverCertificate,
        X509Chain? platformChain,
        SslPolicyErrors platformErrors)
    {
        ArgumentNullException.ThrowIfNull(trustAnchor);

        if (platformErrors == SslPolicyErrors.None)
        {
            return true;
        }

        if (platformErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)
            || platformErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return false;
        }

        if (ReportsAVerdictTheRebuildCannotReach(platformChain))
        {
            return false;
        }

        // Only a certificate the platform already parsed can be rebuilt against another root. Every TLS handshake the
        // runtime completes supplies one, so this guard covers a caller that fabricated a chain rather than a
        // deployment shape an operator can reach.
        return serverCertificate is X509Certificate2 presentedCertificate
            && ChainsToTrustAnchor(trustAnchor, presentedCertificate, platformChain);
    }

    /// <summary>Reports a platform verdict that a rebuild against another root could not have re-derived.</summary>
    /// <remarks>
    /// <c>RemoteCertificateChainErrors</c> is not only "untrusted root". Revocation and explicit distrust are verdicts
    /// about the certificate itself rather than about which authority signed it, and the rebuild deliberately checks
    /// no revocation, so it would silently overturn them. They are refused instead.
    /// </remarks>
    private static bool ReportsAVerdictTheRebuildCannotReach(X509Chain? platformChain)
    {
        const X509ChainStatusFlags VerdictsAboutTheCertificateItself =
            X509ChainStatusFlags.Revoked | X509ChainStatusFlags.ExplicitDistrust;

        return platformChain?.ChainStatus.Any(status => (status.Status & VerdictsAboutTheCertificateItself) != 0) == true;
    }

    private static bool ChainsToTrustAnchor(
        X509Certificate2 trustAnchor,
        X509Certificate2 serverCertificate,
        X509Chain? platformChain)
    {
        using var rebuiltChain = new X509Chain();

        rebuiltChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        rebuiltChain.ChainPolicy.CustomTrustStore.Add(trustAnchor);

        // A chain error also covers a certificate rejected for its usage, so the rebuild has to re-apply the usage
        // requirement the platform applied. Without this, a client-authentication certificate the same private
        // authority issued would rebuild cleanly and be accepted as the server's.
        rebuiltChain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthenticationOid));

        // The handshake already supplied every intermediate this rebuild is meant to use. Leaving downloads enabled
        // would let an incomplete, server-chosen chain send this synchronous callback to a URL of the server's
        // choosing, with no caller cancellation reaching it.
        rebuiltChain.ChainPolicy.DisableCertificateDownloads = true;

        // A private authority typically publishes neither a CRL distribution point nor an OCSP responder, so an online
        // check would fail every connection to the server this feature exists to support. Compromise of a
        // deployment-provisioned anchor is handled by replacing the provisioned material, which rotation now makes
        // possible without a restart.
        rebuiltChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Pinned rather than left at its default, so no later edit can relax expiry or basic-constraint checking
        // without deleting a line that says what it is doing.
        rebuiltChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        AddHandshakeIntermediatesAsPathCandidates(rebuiltChain, serverCertificate, platformChain);

        return rebuiltChain.Build(serverCertificate);
    }

    /// <summary>Offers the certificates the server sent as path-building candidates without granting them any trust.</summary>
    /// <remarks>
    /// A private server whose certificate is signed by an intermediate rather than directly by the configured root is
    /// an ordinary deployment, and that intermediate is often reachable only from the handshake — not from a machine
    /// store and not from an AIA location. Discarding it would reject a correctly provisioned server. Candidates in
    /// the extra store complete a path; only the custom trust store decides whether the path is trusted.
    /// </remarks>
    private static void AddHandshakeIntermediatesAsPathCandidates(
        X509Chain rebuiltChain,
        X509Certificate2 serverCertificate,
        X509Chain? platformChain)
    {
        if (platformChain is null)
        {
            return;
        }

        foreach (var element in platformChain.ChainElements)
        {
            if (!element.Certificate.Equals(serverCertificate))
            {
                rebuiltChain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }
    }
}
