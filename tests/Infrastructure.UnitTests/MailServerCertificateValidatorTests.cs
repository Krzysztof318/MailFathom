// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class MailServerCertificateValidatorTests
{
    [Fact]
    public void IsServerCertificateTrusted_ServerCertificateChainingToTheConfiguredAnchor_IsTrusted()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        using var serverCertificate = TestCertificates.IssueServerCertificate(authority, "imap.example.test");
        using var handshakeChain = BuildHandshakeChain(serverCertificate);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(trusted);
    }

    /// <summary>A private authority is not a licence to accept a certificate issued for another host.</summary>
    [Fact]
    public void IsServerCertificateTrusted_NameMismatch_IsRejectedEvenThoughTheChainWouldValidate()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        using var serverCertificate = TestCertificates.IssueServerCertificate(authority, "imap.example.test");
        using var handshakeChain = BuildHandshakeChain(serverCertificate);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsServerCertificateTrusted_NoCertificatePresented_IsRejected()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate: null,
            platformChain: null,
            SslPolicyErrors.RemoteCertificateNotAvailable);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsServerCertificateTrusted_CertificateFromAnotherAuthority_IsRejected()
    {
        // Arrange
        using var configuredAuthority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(configuredAuthority);
        using var otherAuthority = TestCertificates.CreateCertificateAuthority("Some Other Root");
        using var serverCertificate = TestCertificates.IssueServerCertificate(otherAuthority, "imap.example.test");
        using var handshakeChain = BuildHandshakeChain(serverCertificate);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(trusted);
    }

    /// <summary>An intermediate is often reachable only from the handshake, and discarding it would reject a correctly provisioned server.</summary>
    [Fact]
    public void IsServerCertificateTrusted_CertificateSignedByAnIntermediateTheServerSupplied_ChainsToTheAnchor()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        using var intermediate = TestCertificates.IssueIntermediateAuthority(authority, "MailFathom Test Issuing CA");
        using var serverCertificate = TestCertificates.IssueServerCertificate(intermediate, "imap.example.test");
        using var publicIntermediate = TestCertificates.WithoutPrivateKey(intermediate);
        using var handshakeChain = BuildHandshakeChain(serverCertificate, publicIntermediate);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(trusted);
    }

    /// <summary>An intermediate the server supplies is a path-building candidate, never a root MailFathom trusts.</summary>
    [Fact]
    public void IsServerCertificateTrusted_IntermediateOfferedAsTheOnlyRoot_GrantsItNoTrust()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var unrelatedAuthority = TestCertificates.CreateCertificateAuthority("Unrelated Root");
        using var anchor = TestCertificates.WithoutPrivateKey(unrelatedAuthority);
        using var serverCertificate = TestCertificates.IssueServerCertificate(authority, "imap.example.test");
        using var authorityWithoutKey = TestCertificates.WithoutPrivateKey(authority);
        using var handshakeChain = BuildHandshakeChain(serverCertificate, authorityWithoutKey);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(trusted);
    }

    /// <summary>A chain error also covers a usage rejection, so the rebuild must not treat every one as an untrusted root.</summary>
    [Fact]
    public void IsServerCertificateTrusted_CertificateUsableOnlyForClientAuthentication_IsRejected()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        using var clientCertificate = TestCertificates.IssueClientAuthenticationCertificate(authority, "imap.example.test");
        using var handshakeChain = BuildHandshakeChain(clientCertificate);

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            clientCertificate,
            handshakeChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(trusted);
    }

    [Fact]
    public void IsServerCertificateTrusted_PlatformValidationSucceeded_NeedsNoRebuild()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        using var serverCertificate = TestCertificates.IssueServerCertificate(authority, "imap.example.test");

        // Act
        var trusted = MailServerCertificateValidator.IsServerCertificateTrusted(
            anchor,
            serverCertificate,
            platformChain: null,
            SslPolicyErrors.None);

        // Assert
        Assert.True(trusted);
    }

    /// <summary>A private server is supported by trusting an authority, so no setting may switch validation off.</summary>
    [Fact]
    public void MailAccountTransportSecurityOptions_ExposesNoCertificateSwitch_SoValidationCannotBeDisabled()
    {
        // Act
        var certificateSwitches = typeof(MailAccountTransportSecurityOptions)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(bool)
                && property.Name.Contains("Certificate", StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.Empty(certificateSwitches);
    }

    [Fact]
    public void MailServerCertificateTrust_OffersOnlyValidatingSources()
    {
        // Act
        var trustSources = Enum.GetValues<MailServerCertificateTrust>();

        // Assert
        Assert.Equal(
            [MailServerCertificateTrust.SystemTrustStore, MailServerCertificateTrust.AdditionalTrustedAuthority],
            trustSources);
    }

    /// <summary>Models what the runtime hands a validation callback: the leaf plus whatever the server sent with it.</summary>
    private static X509Chain BuildHandshakeChain(
        X509Certificate2 serverCertificate,
        X509Certificate2? suppliedIntermediate = null)
    {
        var handshakeChain = new X509Chain();
        handshakeChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (suppliedIntermediate is not null)
        {
            handshakeChain.ChainPolicy.ExtraStore.Add(suppliedIntermediate);
        }

        handshakeChain.Build(serverCertificate);

        return handshakeChain;
    }
}
