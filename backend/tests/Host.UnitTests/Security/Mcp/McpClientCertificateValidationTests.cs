// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;
using MailFathom.Host.Security.Mcp;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Mcp;

/// <summary>Covers where the certificate an MCP request is judged by comes from.</summary>
/// <remarks>
/// The rules a certificate is judged by belong to the authenticator and are covered there. What only a test at this
/// level can state is which certificate reaches it: the one the TLS connection carried, never one a request said it
/// carried. A header naming a certificate is written by whoever sent the request, so reading one would turn client
/// authentication into a value a client fills in for itself.
/// </remarks>
public sealed class McpClientCertificateValidationTests
{
    private const string ClientDnsName = "client.example.test";

    /// <summary>The instant the certificates here are judged at, inside the validity period the test certificates carry.</summary>
    private static readonly DateTimeOffset JudgedAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] CertificateLikeHeaderNames =
    [
        "X-Client-Cert",
        "X-SSL-Client-Cert",
        "X-Forwarded-Client-Cert",
        "X-ARR-ClientCert",
    ];

    /// <summary>The certificate the connection carried is what a profile judges, and it is what lets the request through.</summary>
    [Fact]
    public async Task ServeWhenTheConnectionCertificateIsAccepted_ACertificateOnTheConnection_ServesTheRequest()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Client Root");
        using var clientCertificate = TestCertificates.IssueClientAuthenticationCertificate(authority, ClientDnsName);
        var context = RequestCarrying(clientCertificate);
        var served = false;

        // Act
        await McpClientCertificateValidation.ServeWhenTheConnectionCertificateIsAcceptedAsync(
            context,
            _ =>
            {
                served = true;

                return Task.CompletedTask;
            },
            AuthenticatorTrusting(authority),
            [RequiredProfile()]);

        // Assert
        Assert.True(served);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    /// <summary>
    /// The matched profile is the only identity a deployment authenticating with no API key has, and the rate limiter
    /// reads it from this feature to keep one client application's capacity apart from another's. Nothing else carries
    /// it: the certificate is judged before authentication runs, and <c>UseAuthentication</c> replaces the principal, so
    /// a claim set here would be gone by the time the limiter looked.
    /// </summary>
    [Fact]
    public async Task ServeWhenTheConnectionCertificateIsAccepted_AMatchedProfile_PublishesTheClientItIdentified()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Client Root");
        using var clientCertificate = TestCertificates.IssueClientAuthenticationCertificate(authority, ClientDnsName);
        var context = RequestCarrying(clientCertificate);

        // Act
        await McpClientCertificateValidation.ServeWhenTheConnectionCertificateIsAcceptedAsync(
            context,
            _ => Task.CompletedTask,
            AuthenticatorTrusting(authority),
            [RequiredProfile()]);

        // Assert
        var identity = context.Features.Get<McpClientCertificateIdentity>();
        Assert.NotNull(identity);
        Assert.Equal(RequiredProfile().Name, identity.ProfileName);
    }

    /// <summary>
    /// A request served because every profile was content without a certificate identified nobody, so publishing a
    /// client would name one the deployment never saw — and would give unauthenticated traffic a partition of its own.
    /// </summary>
    [Fact]
    public async Task ServeWhenTheConnectionCertificateIsAccepted_NoCertificateAndNoProfileRequiringOne_PublishesNoClient()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Client Root");
        var context = RequestCarrying(connectionCertificate: null);

        // Act
        await McpClientCertificateValidation.ServeWhenTheConnectionCertificateIsAcceptedAsync(
            context,
            _ => Task.CompletedTask,
            AuthenticatorTrusting(authority),
            [OptionalProfile()]);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Features.Get<McpClientCertificateIdentity>());
    }

    /// <summary>The certificate a request claims in a header is not a certificate; it is a value the sender wrote.</summary>
    [Fact]
    public async Task ServeWhenTheConnectionCertificateIsAccepted_AValidCertificateOfferedInAHeader_RefusesTheRequest()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Client Root");
        using var clientCertificate = TestCertificates.IssueClientAuthenticationCertificate(authority, ClientDnsName);
        var context = RequestCarrying(connectionCertificate: null);

        foreach (var headerName in CertificateLikeHeaderNames)
        {
            context.Request.Headers[headerName] = clientCertificate.ExportCertificatePem();
        }

        var served = false;

        // Act
        await McpClientCertificateValidation.ServeWhenTheConnectionCertificateIsAcceptedAsync(
            context,
            _ =>
            {
                served = true;

                return Task.CompletedTask;
            },
            AuthenticatorTrusting(authority),
            [RequiredProfile()]);

        // Assert
        Assert.False(served);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>A refusal says nothing a client could act on, because what it could act on is what to present next.</summary>
    [Fact]
    public async Task ServeWhenTheConnectionCertificateIsAccepted_ACertificateNoProfileTrusts_RefusesWithNoBody()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("Client Root");
        using var strangerAuthority = TestCertificates.CreateCertificateAuthority("Stranger Root");
        using var strangerCertificate = TestCertificates.IssueClientAuthenticationCertificate(
            strangerAuthority,
            ClientDnsName);
        var context = RequestCarrying(strangerCertificate);

        // Act
        await McpClientCertificateValidation.ServeWhenTheConnectionCertificateIsAcceptedAsync(
            context,
            _ => Task.FromException(new InvalidOperationException("A refused request must not reach the endpoint.")),
            AuthenticatorTrusting(authority),
            [RequiredProfile()]);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(0, context.Response.ContentLength ?? 0);
    }

    private static DefaultHttpContext RequestCarrying(X509Certificate2? connectionCertificate)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<ITlsConnectionFeature>(new ConnectionCertificate(connectionCertificate));

        return context;
    }

    private static McpClientCertificateAuthenticator AuthenticatorTrusting(X509Certificate2 authority)
    {
        var resolver = new ProvisionedTrustAnchorResolver();
        using var publicAnchor = TestCertificates.WithoutPrivateKey(authority);
        resolver.Provision(TestCertificates.ToPem(publicAnchor));

        return new McpClientCertificateAuthenticator(
            new TrustAnchorLoader(resolver),
            new FakeTimeProvider(JudgedAt),
            NullLogger<McpClientCertificateAuthenticator>.Instance);
    }

    private static McpClientCertificateTrustProfile RequiredProfile() =>
        McpClientCertificateTrustProfile.Create(
            "client",
            McpClientCertificateRequirement.Required,
            [new ConfiguredSecret { Name = "client-ca", SecretReference = "file:/run/secrets/client-ca.pem" }],
            [ClientDnsName]);

    private static McpClientCertificateTrustProfile OptionalProfile() =>
        McpClientCertificateTrustProfile.Create(
            "client",
            McpClientCertificateRequirement.Optional,
            [new ConfiguredSecret { Name = "client-ca", SecretReference = "file:/run/secrets/client-ca.pem" }],
            [ClientDnsName]);

    /// <summary>Carries the certificate a TLS handshake produced, which is the only place this middleware reads one from.</summary>
    private sealed class ConnectionCertificate(X509Certificate2? clientCertificate) : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; } = clientCertificate;

        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(this.ClientCertificate);
    }

    /// <summary>Hands back one provisioned anchor whatever reference is asked for, because which reference resolves is not what these tests are about.</summary>
    private sealed class ProvisionedTrustAnchorResolver : ISecretReferenceResolver
    {
        private byte[] material = [];

        public void Provision(byte[] anchorMaterial) => this.material = anchorMaterial;

        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromBytes(this.material),
                SecretMaterialSource.SchemeAdapter));
    }
}
