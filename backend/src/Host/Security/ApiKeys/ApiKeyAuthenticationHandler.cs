// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.ApiKeys;

/// <summary>Authenticates a request against the API keys configured for the surface it arrived on.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the credential out of the request, hands it to
/// <see cref="ApiKeyAuthenticator" />, and turns the answer into the framework's own vocabulary. Every rule worth
/// asserting — which keys match, how they are compared, when a lifetime has ended — lives below this boundary, where a
/// test reaches it without a request pipeline.
/// </para>
/// <para>
/// One handler serves every surface, because which keys are compared is the scheme's own option rather than something
/// the handler goes and reads. Two surfaces therefore register two schemes over two key lists and share this code, and
/// a key configured for one authenticates nothing on the other.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenge, whether the
/// request presented nothing, presented a credential in the wrong shape, presented one that matches no key, or
/// presented one whose lifetime has ended. The failure message the framework records reaches the server log only, and
/// even there it names the reason rather than the credential.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ApiKeyAuthenticator authenticator;

    /// <summary>Initializes a new API key authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> is <see langword="null" />.</exception>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        ApiKeyAuthenticator authenticator)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);

        this.authenticator = authenticator;
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
            [.. this.Options.ApiKeys],
            this.Request.Headers.Authorization.ToString(),
            this.Context.RequestAborted);

        if (result.AuthenticatedKeyName is not { } keyName)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        // The grant was resolved from the entry that carries this key while the host was composed, so what a caller may
        // do travels on the principal rather than being looked up behind it.
        var grantedPermissions = this.Options.GrantsByKeyName.TryGetValue(keyName.Value!, out var permissions)
            ? permissions
            : [];

        var identity = TransportGrant.IdentityFor(
            keyName.Value!,
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType,
            this.Options.Surface.ApiKeySchemeName,
            grantedPermissions);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The bare challenge every method on the surface produces, written where the constants it names live.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        ApiKeyAuthentication.WriteBareChallenge(this.Response);

        return Task.CompletedTask;
    }
}
