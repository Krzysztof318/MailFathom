// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Web;
using MailFathom.TestSupport;

namespace MailFathom.Cli.UnitTests;

/// <summary>A deployment that accepts OAuth, and the authorization server it names, as the command meets them.</summary>
/// <remarks>
/// One double for both because an OAuth sign-in is one conversation across two hosts, and what is worth asserting is
/// the sequence: the command reads the deployment's metadata, finds the server's discovery document, exchanges a grant,
/// and then presents the issued token back to the deployment. Splitting it in two would let a test pass while the
/// command talked to the wrong one.
/// </remarks>
internal sealed class FakeOAuthDeployment
{
    internal const string DeploymentAddress = "https://mail.example.test:8443";

    internal const string Issuer = "https://sso.example.test/realms/mailfathom";

    internal const string Resource = $"{DeploymentAddress}/api/admin";

    internal const string TokenEndpoint = $"{Issuer}/protocol/openid-connect/token";

    internal const string DeviceAuthorizationEndpoint = $"{Issuer}/protocol/openid-connect/auth/device";

    private const string MetadataPath = "/.well-known/oauth-protected-resource/api/admin";

    private const string OAuthDiscoveryPath = "/.well-known/oauth-authorization-server/realms/mailfathom";

    /// <summary>Where an OpenID provider that predates RFC 8414 publishes, which is the third address tried.</summary>
    private const string OpenIdConnectDiscoveryPath = "/realms/mailfathom/.well-known/openid-configuration";

    private readonly List<string> issuedRefreshTokens = [];

    private FakeOAuthDeployment()
    {
    }

    /// <summary>Gets the refresh tokens the authorization server has been presented with, in order.</summary>
    /// <remarks>What proves whether a rotated token was adopted: the second renewal presents either the original or the one the first renewal returned.</remarks>
    internal IReadOnlyList<string> PresentedRefreshTokens => this.issuedRefreshTokens;

    /// <summary>Gets or sets the scopes the deployment publishes for a client to ask for.</summary>
    /// <remarks>This is the document's <c>scopes_supported</c> rather than what a token is checked against, so a deployment advertising offline access states it here and one that does not simply leaves it out.</remarks>
    internal IReadOnlyList<string> PublishedScopes { get; set; } = ["mailfathom.admin"];

    /// <summary>Gets or sets the issuers the deployment publishes, which is more than one where several are accepted.</summary>
    internal IReadOnlyList<string> Issuers { get; set; } = [Issuer];

    /// <summary>Gets or sets whether the authorization server publishes a device authorization endpoint.</summary>
    internal bool OffersDeviceGrant { get; set; } = true;

    /// <summary>Gets or sets the address the device authorization response tells a person to open.</summary>
    /// <remarks>Settable so a test can answer with something that is not a usable web address, which is what a misconfigured or misbehaving provider produces and what the command has to refuse rather than construct.</remarks>
    internal string VerificationUri { get; set; } = "https://sso.example.test/device";

    /// <summary>Gets or sets whether the authorization server publishes at the OAuth 2.0 address at all.</summary>
    /// <remarks>Turning it off leaves only the OpenID Connect address, which is what an OpenID provider predating RFC 8414 serves and what the candidate order exists to reach.</remarks>
    internal bool PublishesOAuthMetadataAddress { get; set; } = true;

    /// <summary>Gets or sets how the token endpoint answers, which is what a test varies.</summary>
    internal Func<IReadOnlyDictionary<string, string>, HttpResponseMessage> AnswerTokenRequest { get; set; } =
        _ => FakeAdminEndpoint.Json(HttpStatusCode.OK, TokenResponse("an-access-token", "a-refresh-token", expiresInSeconds: 3600));

    /// <summary>Gets the form the token endpoint was last posted, so a test can assert what the command asked for.</summary>
    internal IReadOnlyDictionary<string, string> LastTokenRequest { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the form the device authorization endpoint was last posted.</summary>
    /// <remarks>Recorded separately from the token endpoint's because a device sign-in asks for its scopes here, before any code exists to exchange — so a request that asked for the wrong ones would otherwise reach no assertion at all.</remarks>
    internal IReadOnlyDictionary<string, string> LastDeviceAuthorizationRequest { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Builds a deployment that publishes OAuth metadata and an authorization server that answers.</summary>
    /// <returns>The scenario, whose <see cref="Handler" /> the context is built over.</returns>
    internal static FakeOAuthDeployment Answering() => new();

    /// <summary>Builds a token endpoint response body.</summary>
    /// <param name="accessToken">The access token to issue.</param>
    /// <param name="refreshToken">The refresh token to issue, or <see langword="null" /> to issue none.</param>
    /// <param name="expiresInSeconds">The access token's stated lifetime.</param>
    /// <returns>The JSON body.</returns>
    internal static string TokenResponse(string accessToken, string? refreshToken, int expiresInSeconds)
    {
        var refresh = refreshToken is null ? string.Empty : $$""","refresh_token":"{{refreshToken}}" """.TrimEnd();

        return $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":{{expiresInSeconds}}{{refresh}}}""";
    }

    /// <summary>Builds a token endpoint response reporting an RFC 6749 error.</summary>
    /// <param name="errorCode">The <c>error</c> the server reports.</param>
    /// <returns>The response.</returns>
    internal static HttpResponseMessage Refusing(string errorCode) =>
        FakeAdminEndpoint.Json(HttpStatusCode.BadRequest, $$"""{"error":"{{errorCode}}"}""");

    /// <summary>Builds the handler every request in the scenario goes through.</summary>
    /// <returns>The handler; the caller disposes it.</returns>
    internal FakeHttpMessageHandler Handler() => new(this.AnswerAsync);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Every response is handed to the HttpClient that asked for it, which disposes it; disposing here would return a response whose body cannot be read.")]
    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        return path switch
        {
            MetadataPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, this.ProtectedResourceMetadata()),
            OAuthDiscoveryPath when this.PublishesOAuthMetadataAddress =>
                FakeAdminEndpoint.Json(HttpStatusCode.OK, this.AuthorizationServerMetadata()),
            OpenIdConnectDiscoveryPath => FakeAdminEndpoint.Json(HttpStatusCode.OK, this.AuthorizationServerMetadata()),
            "/realms/mailfathom/protocol/openid-connect/token" =>
                await this.AnswerTokenEndpointAsync(request, cancellationToken),
            "/realms/mailfathom/protocol/openid-connect/auth/device" =>
                await this.AnswerDeviceAuthorizationEndpointAsync(request, cancellationToken),
            "/api/admin/session" => FakeAdminEndpoint.Json(
                HttpStatusCode.OK,
                FakeAdminEndpoint.SessionBody("kasia", FakeAdminEndpoint.CommandVersion)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response is handed to the HttpClient that asked for it, which disposes it.")]
    private async Task<HttpResponseMessage> AnswerDeviceAuthorizationEndpointAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        this.LastDeviceAuthorizationRequest = await ReadFormAsync(request, cancellationToken);

        return FakeAdminEndpoint.Json(
            HttpStatusCode.OK,
            $$"""{"device_code":"a-device-code","user_code":"WDJB-MJHT","verification_uri":"{{this.VerificationUri}}","expires_in":600,"interval":1}""");
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The response is handed to the HttpClient that asked for it, which disposes it.")]
    private async Task<HttpResponseMessage> AnswerTokenEndpointAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(request, cancellationToken);

        this.LastTokenRequest = form;

        if (form.TryGetValue("refresh_token", out var presented))
        {
            this.issuedRefreshTokens.Add(presented);
        }

        return this.AnswerTokenRequest(form);
    }

    /// <summary>Reads the posted form the way an authorization server would.</summary>
    private static async Task<Dictionary<string, string>> ReadFormAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var parsed = HttpUtility.ParseQueryString(body);

        return parsed.AllKeys
            .Where(key => key is not null)
            .ToDictionary(key => key!, key => parsed[key] ?? string.Empty, StringComparer.Ordinal);
    }

    private string ProtectedResourceMetadata() =>
        $$"""
        {
          "resource": "{{Resource}}",
          "authorization_servers": [{{string.Join(", ", this.Issuers.Select(issuer => $"\"{issuer}\""))}}],
          "scopes_supported": [{{string.Join(", ", this.PublishedScopes.Select(scope => $"\"{scope}\""))}}],
          "bearer_methods_supported": ["header"],
          "resource_name": "MailFathom"
        }
        """;

    private string AuthorizationServerMetadata()
    {
        var deviceEndpoint = this.OffersDeviceGrant
            ? $$""","device_authorization_endpoint":"{{DeviceAuthorizationEndpoint}}" """.TrimEnd()
            : string.Empty;

        return $$"""
        {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/protocol/openid-connect/auth","token_endpoint":"{{TokenEndpoint}}"{{deviceEndpoint}}}
        """;
    }
}
