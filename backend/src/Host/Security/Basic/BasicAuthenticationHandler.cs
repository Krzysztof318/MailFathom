// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Basic;

/// <summary>Authenticates a request against the username-and-password credentials this deployment holds for its owners.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the header out of the request, names the source the attempt
/// came from, hands both to <see cref="OwnerPasswordAuthenticator" />, and turns the answer into the framework's own
/// vocabulary. Every rule worth asserting — what a readable credential is, what a username folds to, how a password is
/// compared, how often one may be tried, and what a refusal is allowed to distinguish — lives below this boundary,
/// where a test reaches it without a request pipeline.
/// </para>
/// <para>
/// One handler serves every surface, because what differs between two surfaces is the grant and the bound, and both
/// are the scheme's own options. A credential is the deployment's rather than a surface's, so the same owner signs in
/// to the client and to the MCP endpoint with one password — and spends a separate bucket of attempts on each, which
/// is what the surface in the partition key buys.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same two challenges, whether
/// the request presented nothing, presented something that is not a Basic credential, presented a username nobody
/// holds, presented a wrong password, presented one for a credential somebody disabled, or has spent its attempts. The
/// reason the framework records reaches the server log only, and even there it names the rejection rather than the
/// credential.
/// </para>
/// <para>
/// <strong>A request this process sees as clear text is refused before the header is read</strong>, exactly as the
/// token schemes refuse one. Startup already refuses a surface that answers its routes on an unencrypted socket with
/// no proxy declared in front, but the arrangement it permits instead — a clear-text listener behind a named
/// TLS-terminating proxy — is one where the socket is still open: a request reaching it from anywhere but that proxy
/// arrives without the forwarded scheme, so <c>IsHttps</c> stays false and this is the check that catches it. The
/// startup refusal decides which deployments may accept a password at all, and this decides which requests may carry
/// one.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private readonly OwnerPasswordAuthenticator authenticator;

    /// <summary>Initializes a new Basic authentication handler.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> is <see langword="null" />.</exception>
    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        OwnerPasswordAuthenticator authenticator)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);

        this.authenticator = authenticator;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A request this process sees as clear text is refused first, without the header being read at all, for the reason
    /// the type's own remarks give. It answers <see cref="AuthenticateResult.NoResult" /> rather than a failure, which
    /// is what the token schemes answer for the same case: the credential was not judged, so the surface's challenge is
    /// what the caller meets.
    /// </para>
    /// <para>
    /// The header is read through <see cref="object.ToString" /> on the header values, which yields an empty string
    /// when the request carried none and a joined value when it carried several. Both reach the authenticator as
    /// something that is not one Basic credential, which is what they are, and both are refused identically — so a
    /// request that supplies the header twice is refused rather than having one of the two picked for it.
    /// </para>
    /// </remarks>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!this.Request.IsHttps)
        {
            return AuthenticateResult.NoResult();
        }

        var result = await this.authenticator.AuthenticateAsync(
            this.Options.Surface.Name,
            this.Request.Headers.Authorization.ToString(),
            this.Context.Connection.RemoteIpAddress?.ToString(),
            this.Options.AttemptsPerMinute,
            this.Context.RequestAborted);

        if (result.AuthenticatedCredentialId is not { } credentialId)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        var identity = TransportGrant.IdentityFor(
            credentialId.ToString("D", CultureInfo.InvariantCulture),
            BasicAuthentication.CredentialIdClaimType,
            BasicAuthentication.RoleClaimType,
            this.Options.Surface.BasicSchemeName,
            this.Options.Grant);

        // The owner is what separates this method from every other one: the credential named a person, so the principal
        // carries them rather than leaving the surface to answer for whose mail the request acts on.
        identity.AddClaim(TransportCallerOwner.ClaimFor(result.Owner));

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <inheritdoc />
    /// <remarks>The bare bearer challenge every method on the surface produces, with the password challenge beside it, written where the constants they name live.</remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        BasicAuthentication.WriteChallenge(this.Response);

        return Task.CompletedTask;
    }
}
