// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers which certificate one connection accepts, and what it records about the one it refused.</summary>
/// <remarks>
/// The claim worth defending is that a pin narrows rather than widens: a profile that pinned a certificate accepts that
/// one and refuses every other, including one this machine would have trusted on its own. Without that, accepting a
/// self-signed certificate once would leave the profile accepting anything for as long as it exists.
/// </remarks>
public sealed class ServerCertificatePolicyTests : IDisposable
{
    private readonly X509Certificate2 authority = TestCertificates.CreateCertificateAuthority("MailFathom test authority");

    private readonly X509Certificate2 deploymentCertificate;

    private readonly X509Certificate2 replacementCertificate;

    public ServerCertificatePolicyTests()
    {
        this.deploymentCertificate = TestCertificates.IssueServerCertificate(this.authority, "mail.example.test");
        this.replacementCertificate = TestCertificates.IssueServerCertificate(this.authority, "mail.example.test");
    }

    [Fact]
    public void Accepts_AChainThisMachineTrustsAndNoPin_IsAcceptedAndNothingIsRecorded()
    {
        // Arrange
        ServerCertificatePolicy policy = new(pinnedCertificateFingerprint: null);

        // Act
        var accepted = policy.Accepts(this.deploymentCertificate, chain: null, SslPolicyErrors.None);

        // Assert
        Assert.True(accepted);
        Assert.Null(policy.Refused);
    }

    /// <summary>What a self-signed deployment looks like on the first connection, and the whole input to the question the operator is asked.</summary>
    [Fact]
    public void Accepts_AnUntrustedCertificateAndNoPin_IsRefusedAndRecordedWithWhatAnOperatorHasToRead()
    {
        // Arrange
        ServerCertificatePolicy policy = new(pinnedCertificateFingerprint: null);

        // Act
        var accepted = policy.Accepts(
            this.deploymentCertificate,
            chain: null,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(accepted);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.deploymentCertificate),
            policy.Refused?.Fingerprint);
        Assert.Equal(this.deploymentCertificate.Subject, policy.Refused?.Subject);
        Assert.Equal(this.deploymentCertificate.Issuer, policy.Refused?.Issuer);
        Assert.Contains("does not trust the chain", policy.Refused!.ValidationFailure, StringComparison.Ordinal);
    }

    /// <summary>The point of the feature: the certificate the operator accepted is accepted afterwards, chain or no chain.</summary>
    [Fact]
    public void Accepts_ThePinnedCertificate_IsAcceptedEvenThoughItsChainIsStillUntrusted()
    {
        // Arrange
        ServerCertificatePolicy policy = new(PresentedCertificate.FingerprintOf(this.deploymentCertificate));

        // Act
        var accepted = policy.Accepts(
            this.deploymentCertificate,
            chain: null,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(accepted);
        Assert.Null(policy.Refused);
    }

    /// <summary>
    /// A pin is not "trust this one as well". A different certificate is refused even where this machine would have
    /// accepted it on its own, because a profile that pinned one said which deployment it is talking to.
    /// </summary>
    [Fact]
    public void Accepts_ADifferentCertificateUnderAPin_IsRefusedEvenWhenItValidatesOnItsOwn()
    {
        // Arrange
        ServerCertificatePolicy policy = new(PresentedCertificate.FingerprintOf(this.deploymentCertificate));

        // Act
        var accepted = policy.Accepts(this.replacementCertificate, chain: null, SslPolicyErrors.None);

        // Assert
        Assert.False(accepted);
        Assert.Equal(
            PresentedCertificate.FingerprintOf(this.replacementCertificate),
            policy.Refused?.Fingerprint);
    }

    [Fact]
    public void Accepts_NoCertificateAtAll_IsRefused()
    {
        // Arrange
        ServerCertificatePolicy policy = new(pinnedCertificateFingerprint: null);

        // Act
        var accepted = policy.Accepts(certificate: null, chain: null, SslPolicyErrors.RemoteCertificateNotAvailable);

        // Assert
        Assert.False(accepted);
        Assert.Contains("presented no certificate", policy.Refused!.ValidationFailure, StringComparison.Ordinal);
    }

    /// <summary>The platform reports a refused handshake as an ordinary connection failure, so the fingerprint change has to be named here or nowhere.</summary>
    [Fact]
    public void DescribeRefusal_ACertificateChangeUnderAPin_NamesBothFingerprints()
    {
        // Arrange
        var pinned = PresentedCertificate.FingerprintOf(this.deploymentCertificate);
        ServerCertificatePolicy policy = new(pinned);
        policy.Accepts(this.replacementCertificate, chain: null, SslPolicyErrors.None);

        // Act
        var described = policy.DescribeRefusal(new Uri("https://mail.example.test:8443"));

        // Assert
        Assert.Contains(pinned, described!, StringComparison.Ordinal);
        Assert.Contains(
            PresentedCertificate.FingerprintOf(this.replacementCertificate),
            described!,
            StringComparison.Ordinal);
        Assert.Contains("mail.example.test:8443", described!, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRefusal_AConnectionThatRefusedNothing_ReportsNoCertificateReason()
    {
        // Arrange
        ServerCertificatePolicy policy = new(pinnedCertificateFingerprint: null);

        // Act
        var described = policy.DescribeRefusal(new Uri("https://mail.example.test:8443"));

        // Assert
        Assert.Null(described);
    }

    /// <summary>A stored fingerprint is a line in a file, so a spelling an operator pasted from elsewhere still has to name the same certificate.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamesTheSameCertificate_TheSameHashSpelledDifferently_Matches(bool withoutSeparators)
    {
        // Arrange
        var fingerprint = PresentedCertificate.FingerprintOf(this.deploymentCertificate);
        var spelling = withoutSeparators
            ? fingerprint.Replace(":", string.Empty, StringComparison.Ordinal)
            : new string([.. fingerprint.Select(char.ToLowerInvariant)]);

        // Act
        var matches = PresentedCertificate.NamesTheSameCertificate(spelling, fingerprint);

        // Assert
        Assert.True(matches);
    }

    /// <summary>The bounds every request to a deployment goes out under, which nothing above the transport can restate.</summary>
    [Fact]
    public void Open_ATransportAimedAtADeployment_CarriesTheBoundsEveryRequestGoesOutUnder()
    {
        // Act
        using var transport = DeploymentTransport.Open(
            new Uri("https://mail.example.test:8443"),
            StoredTransportTrust.Protected);

        // Assert
        Assert.Equal(new Uri("https://mail.example.test:8443"), transport.Client.BaseAddress);
        Assert.Equal(DeploymentTransport.RequestTimeout, transport.Client.Timeout);
        Assert.Equal(DeploymentTransport.ResponseSizeLimitInBytes, transport.Client.MaxResponseContentBufferSize);
        Assert.Null(transport.RefusedCertificate);
    }

    [Fact]
    public void FingerprintOf_ACertificate_IsTheColonSeparatedSha256()
    {
        // Act
        var fingerprint = PresentedCertificate.FingerprintOf(this.deploymentCertificate);

        // Assert
        Assert.Equal(95, fingerprint.Length);
        Assert.Equal(
            this.deploymentCertificate.GetCertHashString(HashAlgorithmName.SHA256),
            fingerprint.Replace(":", string.Empty, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        this.authority.Dispose();
        this.deploymentCertificate.Dispose();
        this.replacementCertificate.Dispose();
    }
}
