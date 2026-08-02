// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers which client certificates a deployment's trust profiles accept, and what each refusal is called.</summary>
/// <remarks>
/// Every certificate here is built in memory, and the authorities are unrelated to anything the machine trusts, which is
/// the point: a profile decides by the anchors it names, so a test whose certificates chained to a real root would pass
/// while proving nothing about the configuration.
/// </remarks>
public sealed class McpClientCertificateAuthenticatorTests
{
    private const string ConnectorDnsName = "mtls.prod.connectors.openai.com";

    private const string ReportingDnsName = "reporting.example.test";

    private const string ConnectorAnchorReference = "file:/run/secrets/connector-ca.pem";

    private const string ReportingAnchorReference = "file:/run/secrets/reporting-ca.pem";

    /// <summary>The instant every certificate here is judged at, inside the validity period the test certificates carry.</summary>
    private static readonly DateTimeOffset JudgedAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Several profiles stand beside each other, so one client's authority says nothing about another's.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateOneProfileTrustsAndAnotherDoesNot_IsAcceptedByTheProfileThatDoes()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var connectorCertificate = harness.IssueConnectorCertificate();

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ReportingProfile(), harness.ConnectorProfile()],
            connectorCertificate);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("chatgpt-connector", result.MatchedProfileName);
    }

    [Fact]
    public async Task AuthenticateAsync_ACertificateFromAnAuthorityNoProfileNames_IsRefusedAsUntrusted()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var strangerAuthority = TestCertificates.CreateCertificateAuthority("Stranger Root");
        using var strangerCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            strangerAuthority,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], strangerCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.ChainNotTrusted, result.Rejection);
    }

    /// <summary>A profile that requires a certificate refuses the request that presents none, whatever else it carries.</summary>
    [Fact]
    public async Task AuthenticateAsync_NoCertificateAgainstARequiredProfile_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ConnectorProfile(McpClientCertificateRequirement.Required)],
            presentedCertificate: null);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.CertificateMissing, result.Rejection);
    }

    /// <summary>An optional profile is what stands beside another authentication mechanism, so a client without a certificate is served and simply identified by nothing.</summary>
    [Fact]
    public async Task AuthenticateAsync_NoCertificateAgainstOptionalProfilesOnly_IsServedAndIdentifiesNoClient()
    {
        // Arrange
        using var harness = new TrustProfileHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ConnectorProfile(), harness.ReportingProfile()],
            presentedCertificate: null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.MatchedProfileName);
    }

    /// <summary>One required profile is enough: the request carries no certificate at all, so no profile can be the one it was meant for.</summary>
    [Fact]
    public async Task AuthenticateAsync_NoCertificateAgainstOneRequiredProfileBesideAnOptionalOne_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ReportingProfile(), harness.ConnectorProfile(McpClientCertificateRequirement.Required)],
            presentedCertificate: null);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.CertificateMissing, result.Rejection);
    }

    /// <summary>The authority alone would accept every certificate it has ever issued, which is what naming the client prevents.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateTheAuthoritySignedForAnotherName_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var otherClientCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            harness.ConnectorAuthority,
            "someone-else.example.test");

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], otherClientCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.SubjectAlternativeNameMismatch, result.Rejection);
    }

    /// <summary>A host name is case-insensitive, so a certificate spelling it differently is the same client.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateNamingTheClientInAnotherCase_IsAccepted()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var connectorCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            harness.ConnectorAuthority,
            "MTLS.Prod.Connectors.OpenAI.com");

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], connectorCertificate);

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>The same authority issues server and client certificates, so one must never be presented as the other.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateThatIsNotForClientAuthentication_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var serverCertificate = TestCertificates.IssueServerAuthenticationCertificate(
            harness.ConnectorAuthority,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], serverCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.ClientAuthenticationUsageMissing, result.Rejection);
    }

    /// <summary>Absence of an extended key usage means every usage in X.509, which is exactly the certificate a client profile must not take on trust.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateCarryingNoExtendedKeyUsage_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var unrestrictedCertificate = TestCertificates.IssueServerCertificate(
            harness.ConnectorAuthority,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], unrestrictedCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.ClientAuthenticationUsageMissing, result.Rejection);
    }

    [Fact]
    public async Task AuthenticateAsync_AnExpiredCertificate_IsRefusedAsExpiredRatherThanAsUntrusted()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var expiredCertificate = TestCertificates.IssueExpiredClientAuthenticationCertificate(
            harness.ConnectorAuthority,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync([harness.ConnectorProfile()], expiredCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.CertificateExpired, result.Rejection);
    }

    /// <summary>
    /// The server sees the leaf alone, so an intermediate the client chained through has to come from configuration.
    /// Listing it beside its root is what completes the path; the root is still where trust comes from.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateIssuedByAnIntermediateListedBesideItsRoot_IsAccepted()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var intermediate = TestCertificates.IssueIntermediateAuthority(
            harness.ConnectorAuthority,
            "Connector Intermediate");
        harness.Provision("file:/run/secrets/connector-intermediate.pem", intermediate);
        using var certificateFromIntermediate = TestCertificates.IssueClientAuthenticationCertificate(
            intermediate,
            ConnectorDnsName);

        // Act
        var withoutTheIntermediate = await harness.AuthenticateAsync(
            [harness.ConnectorProfile()],
            certificateFromIntermediate);
        var withTheIntermediate = await harness.AuthenticateAsync(
            [
                harness.ConnectorProfile(anchorReferences:
                    [ConnectorAnchorReference, "file:/run/secrets/connector-intermediate.pem"]),
            ],
            certificateFromIntermediate);

        // Assert
        Assert.Equal(McpClientCertificateRejection.ChainNotTrusted, withoutTheIntermediate.Rejection);
        Assert.True(withTheIntermediate.Succeeded);
    }

    /// <summary>An intermediate is not a root: listing one on its own trusts nothing, rather than trusting everything under it.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnIntermediateListedWithoutItsRoot_IsRefused()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var intermediate = TestCertificates.IssueIntermediateAuthority(
            harness.ConnectorAuthority,
            "Connector Intermediate");
        harness.Provision("file:/run/secrets/connector-intermediate.pem", intermediate);
        using var certificateFromIntermediate = TestCertificates.IssueClientAuthenticationCertificate(
            intermediate,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ConnectorProfile(anchorReferences: ["file:/run/secrets/connector-intermediate.pem"])],
            certificateFromIntermediate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.ChainNotTrusted, result.Rejection);
    }

    /// <summary>The validity period is judged against the injected clock, not the machine's, which is what makes the expiry boundary reachable at all.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateJudgedBeyondItsValidityPeriod_IsRefusedAsExpired()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var connectorCertificate = harness.IssueConnectorCertificate();

        // Act
        var whileValid = await harness.AuthenticateAsync([harness.ConnectorProfile()], connectorCertificate);
        harness.Clock.SetUtcNow(new DateTimeOffset(2101, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var afterExpiry = await harness.AuthenticateAsync([harness.ConnectorProfile()], connectorCertificate);

        // Assert
        Assert.True(whileValid.Succeeded);
        Assert.False(afterExpiry.Succeeded);
        Assert.Equal(McpClientCertificateRejection.CertificateExpired, afterExpiry.Rejection);
    }

    /// <summary>
    /// A profile the certificate was never meant for objects that the name does not match, which says nothing about the
    /// certificate. Reporting that would send an operator to fix a name while the actual fault is elsewhere.
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_ANameMismatchFromAnotherProfile_ReportsWhatTheProfileNamingTheClientObjectedTo()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var expiredConnectorCertificate = TestCertificates.IssueExpiredClientAuthenticationCertificate(
            harness.ConnectorAuthority,
            ConnectorDnsName);

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ReportingProfile(), harness.ConnectorProfile()],
            expiredConnectorCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.CertificateExpired, result.Rejection);
    }

    /// <summary>A certificate no profile names is exactly what a name mismatch describes, so that is what is reported.</summary>
    [Fact]
    public async Task AuthenticateAsync_ACertificateNoProfileNames_ReportsTheNameMismatch()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var strangerCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            harness.ConnectorAuthority,
            "someone-else.example.test");

        // Act
        var result = await harness.AuthenticateAsync(
            [harness.ReportingProfile(), harness.ConnectorProfile()],
            strangerCertificate);

        // Assert
        Assert.Equal(McpClientCertificateRejection.SubjectAlternativeNameMismatch, result.Rejection);
    }

    /// <summary>Rotating an authority is an overlap: both are listed, both are accepted, and the predecessor is removed once clients have moved.</summary>
    [Fact]
    public async Task AuthenticateAsync_TwoAnchorsDuringARotation_AcceptsCertificatesFromBoth()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var successorAuthority = TestCertificates.CreateCertificateAuthority("Connector Root 2027");
        harness.Provision("file:/run/secrets/connector-ca-2027.pem", successorAuthority);
        var profile = harness.ConnectorProfile(
            anchorReferences: [ConnectorAnchorReference, "file:/run/secrets/connector-ca-2027.pem"]);

        using var certificateFromPredecessor = harness.IssueConnectorCertificate();
        using var certificateFromSuccessor = TestCertificates.IssueClientAuthenticationCertificate(
            successorAuthority,
            ConnectorDnsName);

        // Act
        var fromPredecessor = await harness.AuthenticateAsync([profile], certificateFromPredecessor);
        var fromSuccessor = await harness.AuthenticateAsync([profile], certificateFromSuccessor);

        // Assert
        Assert.True(fromPredecessor.Succeeded);
        Assert.True(fromSuccessor.Succeeded);
    }

    /// <summary>Half a rotation must not stop the certificates the other half still signs for.</summary>
    [Fact]
    public async Task AuthenticateAsync_OneAnchorThatCannotBeLoadedBesideOneThatCan_StillAcceptsWhatTheReadableAnchorSignedFor()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        var profile = harness.ConnectorProfile(
            anchorReferences: ["file:/run/secrets/absent-ca.pem", ConnectorAnchorReference]);
        using var connectorCertificate = harness.IssueConnectorCertificate();

        // Act
        var result = await harness.AuthenticateAsync([profile], connectorCertificate);

        // Assert
        Assert.True(result.Succeeded);
        var record = Assert.Single(harness.Logs.Records, entry => entry.Level == LogLevel.Error);
        Assert.Equal("chatgpt-connector", Assert.Contains("TrustProfileName", record.Properties));
        Assert.DoesNotContain("/run/secrets", record.Message, StringComparison.Ordinal);
    }

    /// <summary>An anchor that has become unreadable must never widen what a profile accepts, so the profile refuses instead of falling through.</summary>
    [Fact]
    public async Task AuthenticateAsync_AProfileWhoseAnchorsAllFailToLoad_RefusesRatherThanTrustingNothingSilently()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        var profile = harness.ConnectorProfile(anchorReferences: ["file:/run/secrets/absent-ca.pem"]);
        using var connectorCertificate = harness.IssueConnectorCertificate();

        // Act
        var result = await harness.AuthenticateAsync([profile], connectorCertificate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(McpClientCertificateRejection.TrustAnchorUnavailable, result.Rejection);
    }

    /// <summary>Nothing is cached, so an authority replaced behind an unchanged reference reaches the next request rather than the next restart.</summary>
    [Fact]
    public async Task AuthenticateAsync_AnAnchorReplacedBehindTheSameReference_TakesEffectOnTheNextRequest()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var successorAuthority = TestCertificates.CreateCertificateAuthority("Connector Root 2027");
        using var certificateFromSuccessor = TestCertificates.IssueClientAuthenticationCertificate(
            successorAuthority,
            ConnectorDnsName);
        var profile = harness.ConnectorProfile();

        // Act
        var beforeRotation = await harness.AuthenticateAsync([profile], certificateFromSuccessor);
        harness.Provision(ConnectorAnchorReference, successorAuthority);
        var afterRotation = await harness.AuthenticateAsync([profile], certificateFromSuccessor);

        // Assert
        Assert.False(beforeRotation.Succeeded);
        Assert.True(afterRotation.Succeeded);
    }

    /// <summary>A deployment that configured no profile has no certificate policy, so nothing is judged and nothing is identified.</summary>
    [Fact]
    public async Task AuthenticateAsync_NoProfilesAtAll_ServesTheRequest()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var connectorCertificate = harness.IssueConnectorCertificate();

        // Act
        var result = await harness.AuthenticateAsync([], connectorCertificate);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Null(result.MatchedProfileName);
    }

    /// <summary>A refusal is recorded by thumbprint, which is public material, and never by anything the deployment configured.</summary>
    [Fact]
    public async Task AuthenticateAsync_ARefusedCertificate_IsRecordedByThumbprintWithoutTheConfiguredReference()
    {
        // Arrange
        using var harness = new TrustProfileHarness();
        using var strangerAuthority = TestCertificates.CreateCertificateAuthority("Stranger Root");
        using var strangerCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            strangerAuthority,
            ConnectorDnsName);

        // Act
        await harness.AuthenticateAsync([harness.ConnectorProfile()], strangerCertificate);

        // Assert
        var record = Assert.Single(harness.Logs.Records, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(
            strangerCertificate.Thumbprint,
            Assert.Contains("ClientCertificateThumbprint", record.Properties));
        Assert.DoesNotContain("/run/secrets", record.Message, StringComparison.Ordinal);
    }

    /// <summary>Holds the two authorities a deployment might configure, and provisions their material the way a mounted file supplies it.</summary>
    private sealed class TrustProfileHarness : IDisposable
    {
        private readonly ProvisionedMaterialResolver resolver = new();
        private readonly ILoggerFactory loggerFactory;

        internal TrustProfileHarness()
        {
            this.ConnectorAuthority = TestCertificates.CreateCertificateAuthority("Connector Root");
            this.ReportingAuthority = TestCertificates.CreateCertificateAuthority("Reporting Root");
            this.Provision(ConnectorAnchorReference, this.ConnectorAuthority);
            this.Provision(ReportingAnchorReference, this.ReportingAuthority);

            this.Logs = new RecordingLoggerProvider();
            this.loggerFactory = LoggerFactory.Create(logging => logging
                .SetMinimumLevel(LogLevel.Debug)
                .AddProvider(this.Logs));
            this.Clock = new FakeTimeProvider(JudgedAt);
            this.Authenticator = new McpClientCertificateAuthenticator(
                new TrustAnchorLoader(this.resolver),
                this.Clock,
                this.loggerFactory.CreateLogger<McpClientCertificateAuthenticator>());
        }

        internal X509Certificate2 ConnectorAuthority { get; }

        internal X509Certificate2 ReportingAuthority { get; }

        internal RecordingLoggerProvider Logs { get; }

        internal FakeTimeProvider Clock { get; }

        internal McpClientCertificateAuthenticator Authenticator { get; }

        public void Dispose()
        {
            this.ConnectorAuthority.Dispose();
            this.ReportingAuthority.Dispose();
            this.loggerFactory.Dispose();
            this.Logs.Dispose();
        }

        internal void Provision(string secretReference, X509Certificate2 authority)
        {
            using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);

            this.resolver.Provision(secretReference, TestCertificates.ToPem(publicAnchor));
        }

        internal X509Certificate2 IssueConnectorCertificate() =>
            TestCertificates.IssueClientAuthenticationCertificate(this.ConnectorAuthority, ConnectorDnsName);

        internal McpClientCertificateTrustProfile ConnectorProfile(
            McpClientCertificateRequirement requirement = McpClientCertificateRequirement.Optional,
            IEnumerable<string>? anchorReferences = null) =>
            McpClientCertificateTrustProfile.Create(
                "chatgpt-connector",
                requirement,
                Anchors(anchorReferences ?? [ConnectorAnchorReference]),
                [ConnectorDnsName]);

        internal McpClientCertificateTrustProfile ReportingProfile() =>
            McpClientCertificateTrustProfile.Create(
                "reporting-service",
                McpClientCertificateRequirement.Optional,
                Anchors([ReportingAnchorReference]),
                [ReportingDnsName]);

        internal Task<McpClientCertificateAuthenticationResult> AuthenticateAsync(
            IReadOnlyList<McpClientCertificateTrustProfile> profiles,
            X509Certificate2? presentedCertificate) =>
            this.Authenticator.AuthenticateAsync(
                profiles,
                presentedCertificate,
                TestContext.Current.CancellationToken);

        private static IEnumerable<ConfiguredSecret> Anchors(IEnumerable<string> anchorReferences) =>
            anchorReferences.Select(reference => new ConfiguredSecret
            {
                Name = "connector-ca",
                SecretReference = reference,
            });
    }
}
