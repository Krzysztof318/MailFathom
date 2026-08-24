// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>Covers how the object-storage transport decides whether the endpoint's certificate is trusted.</summary>
public sealed class ObjectStorageTransportTrustTests
{
    private const string AnchorReference = "file:/run/secrets/object-storage-ca.pem";

    /// <summary>
    /// A deployment reaching a hosted endpoint configures nothing here, and must get exactly the validation it would
    /// have got with no callback installed at all.
    /// </summary>
    [Fact]
    public void IsServerCertificateTrusted_NoConfiguredAuthority_AnswersWhatThePlatformAnswered()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Object Storage Root");
        using var endpointCertificate = TestCertificates.IssueServerCertificate(authority, "objects.example.test");
        using var handshakeChain = BuildHandshakeChain(endpointCertificate);
        using var trust = new ObjectStorageTransportTrust(
            configuredAnchor: null,
            new TrustAnchorLoader(new ProvisionedMaterialResolver()));

        // Act
        var acceptedByThePlatform = trust.IsServerCertificateTrusted(
            endpointCertificate,
            handshakeChain,
            SslPolicyErrors.None);
        var rejectedByThePlatform = trust.IsServerCertificateTrusted(
            endpointCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(acceptedByThePlatform);
        Assert.False(rejectedByThePlatform);
    }

    /// <summary>An endpoint the operator runs themselves is reached by supplying its authority, which is the only supported way.</summary>
    [Fact]
    public async Task IsServerCertificateTrusted_AConfiguredAuthority_TrustsWhatThatAuthoritySigned()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Object Storage Root");
        using var endpointCertificate = TestCertificates.IssueServerCertificate(authority, "objects.example.test");
        using var handshakeChain = BuildHandshakeChain(endpointCertificate);
        using var trust = await StartedTrustOver(authority);

        // Act
        var trusted = trust.IsServerCertificateTrusted(
            endpointCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(trusted);
    }

    /// <summary>There is no setting anywhere that turns validation off, so a certificate no configured authority signed stays refused.</summary>
    [Fact]
    public async Task IsServerCertificateTrusted_ACertificateAnotherAuthoritySigned_IsRefused()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Object Storage Root");
        using var otherAuthority = TestCertificates.CreateCertificateAuthority("Somebody Else's Root");
        using var endpointCertificate = TestCertificates.IssueServerCertificate(
            otherAuthority,
            "objects.example.test");
        using var handshakeChain = BuildHandshakeChain(endpointCertificate);
        using var trust = await StartedTrustOver(authority);

        // Act
        var trusted = trust.IsServerCertificateTrusted(
            endpointCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(trusted);
    }

    /// <summary>
    /// Loading at start-up is what buys a failure naming the configuration key, at the moment the host comes up, rather
    /// than a handshake failure per request afterwards.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnAuthorityThatCannotBeLoaded_FailsTheHostNamingTheKey()
    {
        // Arrange
        using var trust = new ObjectStorageTransportTrust(
            Reference(AnchorReference),
            new TrustAnchorLoader(new ProvisionedMaterialResolver()));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => trust.StartAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:TrustAnchor", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AnchorReference, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A deployment that configured no authority has nothing to load, and start-up must not refuse it for that.</summary>
    [Fact]
    public async Task StartAsync_NoConfiguredAuthority_LoadsNothingAndStarts()
    {
        // Arrange
        using var trust = new ObjectStorageTransportTrust(
            configuredAnchor: null,
            new TrustAnchorLoader(new ProvisionedMaterialResolver()));

        // Act
        await trust.StartAsync(TestContext.Current.CancellationToken);
        await trust.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(trust.IsServerCertificateTrusted(
            serverCertificate: null,
            platformChain: null,
            SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    public void Construction_NoAnchorLoader_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageTransportTrust(configuredAnchor: null, trustAnchorLoader: null!));
    }

    private static async Task<ObjectStorageTransportTrust> StartedTrustOver(X509Certificate2 authority)
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.Provision(AnchorReference, TestCertificates.ToPem(authority));

        var trust = new ObjectStorageTransportTrust(Reference(AnchorReference), new TrustAnchorLoader(resolver));
        await trust.StartAsync(TestContext.Current.CancellationToken);

        return trust;
    }

    private static X509Chain BuildHandshakeChain(X509Certificate2 endpointCertificate)
    {
        var handshakeChain = new X509Chain();
        handshakeChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        handshakeChain.Build(endpointCertificate);

        return handshakeChain;
    }

    private static ConfiguredSecret Reference(string secretReference) =>
        new() { Name = "object-storage-trust-anchor", SecretReference = secretReference };
}
