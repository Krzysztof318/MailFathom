// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailFathom.AppHost;
using MailFathom.IntegrationTests.Orchestration;
using Xunit;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Proves the MCP client-certificate profiles against certificates a real TLS handshake carried.</summary>
/// <remarks>
/// <para>
/// Which certificates a profile accepts is unit-tested against <c>McpClientCertificateAuthenticator</c>, and that the
/// middleware reads the connection rather than a header is unit-tested against <c>McpClientCertificateValidation</c>.
/// Neither can reach the layer below both: that Kestrel asks for a certificate at all, that a client without one
/// completes the handshake instead of having it dropped, and that the certificate the endpoint judges is the one the
/// handshake produced. Every test here is about that layer, which is why each of them speaks TLS to a running host.
/// </para>
/// <para>
/// The host is served with a certificate the suite issued this run, and the client validates it against that authority
/// rather than accepting whatever is presented. A connection therefore establishes only if the HTTPS profile really
/// served the provisioned identity for the name it publishes, so no assertion below has to state it separately.
/// </para>
/// <para>
/// Its one trust profile is <c>Required</c>, and that decides what this class can prove. A request presenting no
/// certificate is refused rather than served, which is the refusal an operator reads in a log instead of meeting as a
/// handshake error. The mirror case — an <c>Optional</c> profile serving that same request — cannot be observed beside
/// it, because a certificate requirement is one answer for a whole process: the authenticator refuses a missing
/// certificate as soon as any profile requires one. That rule is a branch of the authenticator, covered where every
/// other branch of it is, and a second host started to compose two already-proven facts would buy the composition
/// alone.
/// </para>
/// <para>
/// Nothing here carries <c>[RequiresIntegrationCoverage]</c>, for the reason <see cref="ComposedMcpEndpointSecurityTests" />
/// states: what these exercise is either unit-covered already or belongs to <c>Host</c>, which is outside the coverage
/// denominator.
/// </para>
/// </remarks>
[Collection(MutualTlsHostCollectionDefinition.Name)]
public sealed class ComposedMcpClientCertificateHandshakeTests
{
    private const string ToolListedByTheProtocolSurface = "list_emails";

    /// <summary>The spellings a reverse proxy uses to describe a certificate it saw, none of which this endpoint reads.</summary>
    /// <remarks>The same four the middleware's own test uses, so a spelling added there is added here rather than diverging into two lists that each miss what the other covers.</remarks>
    private static readonly string[] CertificateLikeHeaderNames =
    [
        "X-Client-Cert",
        "X-SSL-Client-Cert",
        "X-Forwarded-Client-Cert",
        "X-ARR-ClientCert",
    ];

    private readonly MailFathomOrchestrationFixture orchestration;

    /// <summary>Initializes the tests against the assembly's orchestration.</summary>
    /// <param name="orchestration">The orchestration fixture, which starts the mutual-TLS host on first request.</param>
    public ComposedMcpClientCertificateHandshakeTests(MailFathomOrchestrationFixture orchestration) =>
        this.orchestration = orchestration;

    /// <summary>
    /// The connection carries the accepted certificate, and the request additionally names it in every certificate-like
    /// header, so that the refusal in the next test — the same request over a connection carrying nothing — can only be
    /// explained by the handshake.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_ConnectionCarryingACertificateTheProfileAccepts_ReachesTheProtocolSurface()
    {
        // Arrange
        var accepted = this.orchestration.MutualTlsCertificates.AcceptedClientIdentity;

        // Act
        var answer = await this.AnswerToAsync(accepted);

        // Assert
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Contains(ToolListedByTheProtocolSurface, answer.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The handshake asks for a certificate and this client has none, so what the refusal establishes is that the
    /// connection was established anyway: it arrives as a status code an operator can read, where a demanded certificate
    /// would have ended the connection with nothing either side could report. The accepted certificate still travels in
    /// the headers a proxy would set, which is what makes the outcome the connection's rather than theirs.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_ConnectionCarryingNoCertificate_IsRefusedByTheEndpointRatherThanByTheHandshake()
    {
        // Arrange, Act
        var answer = await this.AnswerToAsync(presentedCertificate: null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.Empty(answer.Body);
    }

    /// <summary>
    /// One test rather than three, because the three certificates differ only in which rule they break — the authority,
    /// the name, the usage — and each pays for its own connection either way. What is asserted of all of them is the
    /// same claim: the handshake completes, so the refusal is the endpoint's verdict on the certificate it received.
    /// </summary>
    [Fact]
    public async Task McpEndpoint_CertificatesNoProfileAccepts_AreRefusedOverAnEstablishedConnection()
    {
        // Arrange
        var certificates = this.orchestration.MutualTlsCertificates;
        X509Certificate2[] refusedCertificates =
        [
            certificates.ClientIdentityFromAnUntrustedAuthority,
            certificates.ClientIdentityNamingAnotherClient,
            certificates.ClientIdentityLimitedToServerAuthentication,
        ];

        // Act
        var answers = new List<HttpStatusCode>(refusedCertificates.Length);

        // A loop rather than a query: each step opens a connection of its own, presents one certificate over it, and
        // has to complete before the next begins.
        foreach (var refusedCertificate in refusedCertificates)
        {
            answers.Add((await this.AnswerToAsync(refusedCertificate)).StatusCode);
        }

        // Assert
        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden],
            answers);
    }

    /// <summary>Builds the tool listing, with the certificate a proxy would have described in every header this endpoint ignores.</summary>
    /// <remarks>
    /// The listing is the cheapest thing the protocol surface answers and it names a tool, so one body distinguishes
    /// "the surface answered" from "something in front of it did". Both content types the Streamable HTTP transport may
    /// reply with are accepted, because which one it chooses is not what these tests are about.
    /// </remarks>
    private static HttpRequestMessage ListToolsRequest(X509Certificate2 certificateNamedInHeaders)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/mcp", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
            }),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Base64 rather than the PEM the middleware's own test writes: an HTTP header value carries no line break, so
        // this is the spelling a proxy actually sets and the only one a request can reach the endpoint with at all.
        var declaredCertificate = Convert.ToBase64String(certificateNamedInHeaders.RawData);

        // Configuring a substitute is a side effect and stays a loop, and so does filling a header collection.
        foreach (var headerName in CertificateLikeHeaderNames)
        {
            request.Headers.Add(headerName, declaredCertificate);
        }

        return request;
    }

    /// <summary>Builds the TLS settings one connection is opened with.</summary>
    /// <param name="presentedCertificate">The certificate to present, or <see langword="null" /> to present none.</param>
    /// <param name="serverTrustAnchor">The authority the served identity is validated against.</param>
    /// <returns>The settings, ready to open one connection with.</returns>
    /// <remarks>
    /// <para>
    /// The served identity is validated against the suite's own authority rather than accepted unconditionally, so a
    /// host presenting anything else fails the connection instead of quietly passing the test. Revocation is not
    /// checked, because the authority publishes neither a list nor a responder — the posture a private authority has.
    /// </para>
    /// <para>
    /// Which certificate to present is stated rather than left to the platform's selection. The server sends no
    /// acceptable-issuers hint, so without the callback a client holding a deliberately unrelated certificate could
    /// present nothing at all — and "nothing at all" is a different test.
    /// </para>
    /// <para>
    /// The target host is the name the profile publishes, which the handshake carries as its server name. That name is
    /// how the endpoint selects an identity at all: a connection asking for something else is refused before any
    /// certificate is exchanged, so a test reaching the endpoint has already proved the profile was selected by name.
    /// </para>
    /// </remarks>
    private static SslClientAuthenticationOptions TlsSettingsPresenting(
        X509Certificate2? presentedCertificate,
        X509Certificate2 serverTrustAnchor)
    {
        var tlsSettings = new SslClientAuthenticationOptions
        {
            TargetHost = OrchestrationContract.MutualTlsHostDomain,
            CertificateChainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                CustomTrustStore = { serverTrustAnchor },
            },
        };

        if (presentedCertificate is not null)
        {
            tlsSettings.ClientCertificates = new X509Certificate2Collection { presentedCertificate };
            tlsSettings.LocalCertificateSelectionCallback = (_, _, _, _, _) => presentedCertificate;
        }

        return tlsSettings;
    }

    /// <summary>Opens one connection, presents one certificate over it, and reads what the endpoint answered.</summary>
    /// <param name="presentedCertificate">The certificate the connection carries, or <see langword="null" /> for a connection carrying none.</param>
    /// <returns>The status and body of the one answer.</returns>
    /// <remarks>
    /// Every test goes through here, so what one test differs from another by is the certificate on the connection and
    /// nothing else: the request is the same tool listing and names the accepted certificate in the same headers each
    /// time. A connection of its own per call is deliberate — a pooled one would carry the previous certificate.
    /// </remarks>
    private async Task<McpAnswer> AnswerToAsync(X509Certificate2? presentedCertificate)
    {
        var baseAddress = await this.orchestration.StartMutualTlsHostAsync(TestContext.Current.CancellationToken);
        var certificates = this.orchestration.MutualTlsCertificates;

        using var handler = new SocketsHttpHandler
        {
            SslOptions = TlsSettingsPresenting(presentedCertificate, certificates.ServerTrustAnchor),
        };
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress };
        using var request = ListToolsRequest(certificates.AcceptedClientIdentity);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return new McpAnswer(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>What one request is judged on, read before its response and its connection were released.</summary>
    private sealed record McpAnswer(HttpStatusCode StatusCode, string Body);
}
