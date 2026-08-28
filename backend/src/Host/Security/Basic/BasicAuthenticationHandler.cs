// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.ApiKeys;
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
/// <para>
/// <c>IsHttps</c> is read after the forwarded-headers middleware, which is deliberate and is the only reading that
/// answers the question: it is true where this process terminated TLS, and where a proxy this deployment named said
/// the client's own hop was encrypted. Reading the connection's TLS feature instead would be unspoofable and wrong,
/// because it is absent by construction in the second arrangement — every request behind a terminating proxy would be
/// refused. What makes the forwarded reading safe here is which peers it is believed from:
/// <see cref="TrustedReverseProxyExtensions" /> clears the framework's default trust and repopulates it from
/// <c>ReverseProxy:TrustedProxies</c> alone, so a scheme forwarded by anything else is discarded before this runs. The
/// one shape in which that section believes every peer is the one naming no proxy, or naming a range covering every
/// address — and <see cref="Configuration.Access.PasswordTransportConfidentiality" /> refuses a password on a
/// clear-text-serving surface in exactly those shapes, so the deployment where a client could forward its own scheme
/// to this handler is one that does not start.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private readonly OwnerPasswordAuthenticator authenticator;
    private readonly IReadOnlyList<IPAddress> declaredProxyAddresses;
    private readonly IReadOnlyList<IPNetwork> declaredProxyNetworks;

    /// <summary>Initializes a new Basic authentication handler.</summary>
    /// <param name="schemeOptions">What this surface's registration granted and how often it lets a password be tried.</param>
    /// <param name="loggerFactory">The framework's own logging, which records the reason a refusal carried.</param>
    /// <param name="urlEncoder">The framework's own encoder, unused here and required by the base class.</param>
    /// <param name="authenticator">What judges the credential, below the request boundary.</param>
    /// <param name="reverseProxySettings">Whether the operator declared something in front, which decides whether the peer address distinguishes one caller from another.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> or <paramref name="reverseProxySettings" /> is <see langword="null" />.</exception>
    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        OwnerPasswordAuthenticator authenticator,
        IOptions<ReverseProxyOptions> reverseProxySettings)
        : base(schemeOptions, loggerFactory, urlEncoder)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(reverseProxySettings);

        this.authenticator = authenticator;

        // Read once rather than per request, the section being restart-scoped like every other listener setting. A
        // deployment naming no proxy yields no addresses here, which leaves every peer a caller this bound applies to.
        var reverseProxy = reverseProxySettings.Value;
        this.declaredProxyAddresses = reverseProxy.NamesAProxy ? reverseProxy.ToTrustedProxyAddresses() : [];
        this.declaredProxyNetworks = reverseProxy.NamesAProxy ? reverseProxy.ToTrustedProxyNetworks() : [];
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
            this.SourceToBoundBy(),
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

    /// <summary>Reports the address to bound this attempt by, or nothing where this peer tells two callers apart.</summary>
    /// <remarks>
    /// The peer is the client except when the peer is itself a proxy this deployment declared. Every request through
    /// such a proxy arrives from its address — <c>X-Forwarded-For</c> is deliberately never read, so the peer this
    /// process observes stays the one that opened the connection — and a per-source partition on it would be one
    /// partition for the whole world, which a single guesser could empty and so close password sign-in for every owner
    /// at once. The username bound is what holds there, and it holds per owner rather than across all of them.
    /// <para>
    /// The question is asked of the peer rather than of the deployment, because <c>ReverseProxy</c> is one section for
    /// the whole process while a listener is not: a deployment declaring a proxy for one surface may serve another
    /// directly, and the peer arriving there is the real client and does tell two callers apart. Answering per process
    /// would drop the source axis on that listener too.
    /// </para>
    /// </remarks>
    private string? SourceToBoundBy()
    {
        if (this.Context.Connection.RemoteIpAddress is not { } peer)
        {
            return null;
        }

        return this.DeclaredAsAProxy(peer) ? null : peer.ToString();
    }

    /// <summary>Reports whether the operator named this address, or a range holding it, as something standing in front of this process.</summary>
    private bool DeclaredAsAProxy(IPAddress peer) =>
        this.declaredProxyAddresses.Any(declared => declared.Equals(peer))
        || this.declaredProxyNetworks.Any(declared => declared.Contains(peer));

    /// <inheritdoc />
    /// <remarks>
    /// The bare bearer challenge every method on the surface produces, with the password challenge beside it, written
    /// where the constants they name live — <strong>except on a request this process sees as clear text</strong>, which
    /// gets the bearer challenge alone. Inviting a password there would be worse than reading one: the handler above
    /// refuses to judge the credential, but a browser meeting <c>WWW-Authenticate: Basic</c> prompts for one and sends
    /// it, so the password crosses the unencrypted hop before anything here can decline it. The refusal and this are
    /// therefore one decision made twice — do not read a password over that hop, and do not ask for one.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (this.Request.IsHttps)
        {
            BasicAuthentication.WriteChallenge(this.Response);
        }
        else
        {
            ApiKeyAuthentication.WriteBareChallenge(this.Response);
        }

        return Task.CompletedTask;
    }
}
