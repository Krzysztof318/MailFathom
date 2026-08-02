// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.AppHost;
using MailFathom.TestSupport;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The certificates the mutual-TLS host is served with and judged by, issued once per run.</summary>
/// <remarks>
/// <para>
/// Everything here is built in memory when the suite starts and lives only as long as it does. No certificate, private
/// key, or trust anchor is committed, which is what keeps a repository that proves a security control from also being
/// the place its material can be read out of.
/// </para>
/// <para>
/// One authority signs both the server identity and the client certificate a profile accepts, because the deployment
/// shape this stands in for is a private authority issuing for both ends. The second authority exists to be untrusted:
/// a certificate it signed is well-formed, current, and names the right client, so refusing it can only be the chain
/// check the profile applies.
/// </para>
/// <para>
/// The three refused certificates are one per rule a profile enforces that a certificate carries on its face — the
/// wrong authority, the wrong name, the wrong usage. Which rule each breaks is stated by its name rather than by the
/// test that presents it, so a handshake failing for the wrong reason is visible where the material is described.
/// </para>
/// </remarks>
public sealed class OrchestratedMutualTlsCertificates : IDisposable
{
    private readonly X509Certificate2 issuingAuthority;
    private readonly X509Certificate2 unrelatedAuthority;
    private readonly X509Certificate2 serverIdentity;

    /// <summary>Issues the authorities, the server identity, and every client certificate the suite presents.</summary>
    public OrchestratedMutualTlsCertificates()
    {
        this.issuingAuthority = TestCertificates.CreateCertificateAuthority("MailFathom Integration Tests Authority");
        this.unrelatedAuthority = TestCertificates.CreateCertificateAuthority("MailFathom Integration Tests Outsider");
        this.serverIdentity = TestCertificates.IssueServerIdentity(
            this.issuingAuthority,
            OrchestrationContract.MutualTlsHostDomain);

        this.ServerTrustAnchor = TestCertificates.WithoutPrivateKey(this.issuingAuthority);
        this.AcceptedClientIdentity = TestCertificates.IssueClientIdentity(
            this.issuingAuthority,
            OrchestrationContract.MutualTlsClientDnsName);
        this.ClientIdentityFromAnUntrustedAuthority = TestCertificates.IssueClientIdentity(
            this.unrelatedAuthority,
            OrchestrationContract.MutualTlsClientDnsName);
        this.ClientIdentityNamingAnotherClient = TestCertificates.IssueClientIdentity(
            this.issuingAuthority,
            "another-client.mailfathom.test");

        // Issued by the trusted authority and carrying the expected name, so the only thing left for a profile to
        // object to is the usage: the same authority commonly issues both kinds, which is why one must not pass as the
        // other.
        this.ClientIdentityLimitedToServerAuthentication = TestCertificates.IssueServerIdentity(
            this.issuingAuthority,
            OrchestrationContract.MutualTlsClientDnsName);
    }

    /// <summary>Gets the authority a client validates the served identity against, its private key dropped.</summary>
    public X509Certificate2 ServerTrustAnchor { get; }

    /// <summary>Gets the client certificate the configured profile accepts.</summary>
    public X509Certificate2 AcceptedClientIdentity { get; }

    /// <summary>Gets a client certificate no configured anchor can build a path to.</summary>
    public X509Certificate2 ClientIdentityFromAnUntrustedAuthority { get; }

    /// <summary>Gets a client certificate from the trusted authority that names a client this deployment does not serve.</summary>
    public X509Certificate2 ClientIdentityNamingAnotherClient { get; }

    /// <summary>Gets a certificate the trusted authority issued for the right client and the wrong purpose.</summary>
    public X509Certificate2 ClientIdentityLimitedToServerAuthentication { get; }

    /// <summary>Gets the PEM chain the host presents, which is the leaf alone because its authority is the client's anchor.</summary>
    public string ServerCertificateChainPem => TestCertificates.ToCertificateChainPem(this.serverIdentity);

    /// <summary>Gets the PEM private key the host signs the handshake with.</summary>
    public string ServerPrivateKeyPem => TestCertificates.ToPrivateKeyPem(this.serverIdentity);

    /// <summary>Gets the PEM trust anchor the configured profile chains a presented certificate to.</summary>
    /// <remarks>A certificate exported on its own carries no private key, which is what a deployment provisions and what the loader requires of an anchor.</remarks>
    public string ClientTrustAnchorPem => TestCertificates.ToCertificateChainPem(this.issuingAuthority);

    /// <inheritdoc />
    public void Dispose()
    {
        this.issuingAuthority.Dispose();
        this.unrelatedAuthority.Dispose();
        this.serverIdentity.Dispose();
        this.ServerTrustAnchor.Dispose();
        this.AcceptedClientIdentity.Dispose();
        this.ClientIdentityFromAnUntrustedAuthority.Dispose();
        this.ClientIdentityNamingAnotherClient.Dispose();
        this.ClientIdentityLimitedToServerAuthentication.Dispose();
    }
}
