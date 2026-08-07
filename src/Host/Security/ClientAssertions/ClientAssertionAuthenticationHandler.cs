// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Authenticates a request against the client public keys configured for the surface it arrived on.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the credential out of the request, hands it to
/// <see cref="ClientAssertionAuthenticator" />, and turns the answer into the framework's own vocabulary. Every rule
/// worth asserting — which key verified it, what the assertion had to claim, whether its identifier had been spent —
/// lives below this boundary, where a test reaches it without a request pipeline.
/// </para>
/// <para>
/// One handler serves every surface, because which keys are verified against and which audience is required are the
/// scheme's own options rather than something the handler goes and reads. Two surfaces therefore register two schemes
/// over two key lists and share this code, and a key configured for one authenticates nothing on the other.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenge every other
/// method returns, whether the request presented nothing, presented something that is not an assertion, presented one
/// nobody's key signed, presented one claiming the wrong audience or too long a life, or presented one whose identifier
/// had already been spent. The failure message the framework records reaches the server log only, and even there it
/// names the reason rather than the credential.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class ClientAssertionAuthenticationHandler
    : AuthenticationHandler<ClientAssertionAuthenticationSchemeOptions>
{
    private readonly ClientAssertionAuthenticator authenticator;

    /// <summary>Initializes a new client assertion authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> is <see langword="null" />.</exception>
    public ClientAssertionAuthenticationHandler(
        IOptionsMonitor<ClientAssertionAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        ClientAssertionAuthenticator authenticator)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);

        this.authenticator = authenticator;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The header is read through <see cref="object.ToString" /> on the header values, which yields an empty string when
    /// the request carried none and a joined value when it carried several. Both reach the authenticator as something
    /// that is not one bearer credential, which is what they are, and both are refused identically.
    /// </remarks>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var result = await this.authenticator.AuthenticateAsync(
            [.. this.Options.PublicKeys],
            this.Options.Surface.ClientAssertionAudience,
            this.Request.Headers.Authorization.ToString(),
            this.Context.RequestAborted);

        if (result.AuthenticatedKeyName is not { } keyName)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClientAssertionAuthentication.KeyNameClaimType, keyName.Value!)],
            this.Options.Surface.ClientAssertionSchemeName,
            ClientAssertionAuthentication.KeyNameClaimType,
            ClientAssertionAuthentication.RoleClaimType);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The same bare challenge every other method on the surface produces, named through the API key scheme's constants because a client is told which protection space to hold a credential for and never which method judged it.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        this.Response.StatusCode = StatusCodes.Status401Unauthorized;
        this.Response.Headers.WWWAuthenticate =
            $"{ApiKeyAuthentication.HttpAuthenticationScheme} realm=\"{ApiKeyAuthentication.Realm}\"";

        return Task.CompletedTask;
    }
}
