// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.ApiKeys;

/// <summary>Authenticates a request against the keys this deployment provisioned for its owners.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the credential out of the request, hands it to
/// <see cref="OwnerApiKeyAuthenticator" />, and turns the answer into the framework's own vocabulary. Every rule worth
/// asserting — what a key of this deployment's own looks like, what it is reduced to, which row it resolves — lives
/// below this boundary, where a test reaches it without a request pipeline.
/// </para>
/// <para>
/// It carries no key list, which is the whole difference between it and the handler beside it. A key an owner's client
/// presents resolves a credential row, so what the scheme has to know is which surface it protects and nothing else,
/// and the owner and the grant both arrive from the row rather than from a configuration entry the host resolved at
/// startup.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenge, whether the
/// request presented nothing, presented a credential in the wrong shape, presented one that resolves no credential, or
/// presented one whose credential is disabled. The failure message the framework records reaches the server log only,
/// and even there it names the reason rather than the credential.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class OwnerApiKeyAuthenticationHandler : AuthenticationHandler<OwnerApiKeyAuthenticationSchemeOptions>
{
    private readonly OwnerApiKeyAuthenticator authenticator;

    /// <summary>Initializes a new owner API key authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> is <see langword="null" />.</exception>
    public OwnerApiKeyAuthenticationHandler(
        IOptionsMonitor<OwnerApiKeyAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        OwnerApiKeyAuthenticator authenticator)
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
            this.Request.Headers.Authorization.ToString(),
            this.Context.RequestAborted);

        if (result.Admitted is not { } admitted)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        var identity = TransportGrant.IdentityFor(
            admitted.CredentialId.ToString("D", CultureInfo.InvariantCulture),
            ApiKeyAuthentication.ApiKeyNameClaimType,
            ApiKeyAuthentication.RoleClaimType,
            this.Options.Surface.ApiKeySchemeName,
            admitted.Permissions);

        // The owner is what the credential resolved, so the principal carries them rather than leaving the surface to
        // answer for whose mail the request acts on.
        identity.AddClaim(TransportCallerOwner.ClaimFor(admitted.Owner));

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
