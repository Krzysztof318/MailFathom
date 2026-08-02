// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Configuration;
using MailFathom.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security;

/// <summary>Authenticates an MCP request against the configured API keys.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the credential out of the request, hands it to
/// <see cref="McpApiKeyAuthenticator" />, and turns the answer into the framework's own vocabulary. Every rule worth
/// asserting — which keys match, how they are compared, when a lifetime has ended — lives below this boundary, where a
/// test reaches it without a request pipeline.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenge, whether the
/// request presented nothing, presented a credential in the wrong shape, presented one that matches no key, or
/// presented one whose lifetime has ended. The failure message the framework records reaches the server log only, and
/// even there it names the reason rather than the credential.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class McpApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly McpApiKeyAuthenticator authenticator;
    private readonly McpEndpointOptions endpointSettings;

    /// <summary>Initializes a new API key authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> or <paramref name="endpointSettings" /> is <see langword="null" />.</exception>
    public McpApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        McpApiKeyAuthenticator authenticator,
        IOptions<McpEndpointOptions> endpointSettings)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(endpointSettings);

        this.authenticator = authenticator;
        this.endpointSettings = endpointSettings.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The header is read through <see cref="object.ToString" /> on the header values, which yields an empty string
    /// when the request carried none and a joined value when it carried several. Both reach the authenticator as
    /// something that is not one bearer credential, which is what they are, and both are refused identically.
    /// </remarks>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await this.authenticator.AuthenticateAsync(
            [.. this.endpointSettings.ApiKeys],
            this.Request.Headers.Authorization.ToString(),
            this.Context.RequestAborted);

        if (result.AuthenticatedKeyName is not { } keyName)
        {
            return AuthenticateResult.Fail("The request presented no usable MCP credential.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(McpApiKeyAuthentication.ApiKeyNameClaimType, keyName.Value!)],
            McpApiKeyAuthentication.SchemeName,
            McpApiKeyAuthentication.ApiKeyNameClaimType,
            McpApiKeyAuthentication.RoleClaimType);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The challenge names the scheme and the protection space and nothing else, because an error code or a description would begin to describe which credential was wrong.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        this.Response.StatusCode = StatusCodes.Status401Unauthorized;
        this.Response.Headers.WWWAuthenticate =
            $"{McpApiKeyAuthentication.HttpAuthenticationScheme} realm=\"{McpApiKeyAuthentication.Realm}\"";

        return Task.CompletedTask;
    }
}
