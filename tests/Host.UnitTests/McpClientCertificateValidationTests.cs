// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography.X509Certificates;
using MailMcp.Host.Security;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Secrets;
using MailMcp.Infrastructure.Security;
using MailMcp.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailMcp.Host.UnitTests;

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
            NullLogger<McpClientCertificateAuthenticator>.Instance);
    }

    private static McpClientCertificateTrustProfile RequiredProfile() =>
        McpClientCertificateTrustProfile.Create(
            "client",
            McpClientCertificateRequirement.Required,
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
