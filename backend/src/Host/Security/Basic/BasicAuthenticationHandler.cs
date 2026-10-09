// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.Passwords;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Security.Basic;

/// <summary>Authenticates a request against the username-and-password credentials this deployment holds for its users.</summary>
/// <remarks>
/// <para>
/// The handler is the adapter and nothing more: it lifts the header out of the request, names the source the attempt
/// came from, hands both to <see cref="UserPasswordAuthenticator" />, asks <see cref="UserCredentialAdmission" /> whether
/// the credential is admitted on this surface, and turns the answer into the framework's own vocabulary. Every rule worth asserting — what a readable credential is, what a username folds to, how a password is
/// compared, how often one may be tried, and what a refusal is allowed to distinguish — lives below this boundary,
/// where a test reaches it without a request pipeline.
/// </para>
/// <para>
/// One handler serves every surface, because what differs between them is carried by the scheme's own options: the
/// attempt bucket, and the surface that judges whether the credential is admitted on it. The grant is not among them:
/// the narrowing arrives on the credential the password resolved and each surface keeps the user's grant to the half
/// it guards, so nothing here decides what an admitted user may do. A credential is the deployment's rather than a
/// surface's, so one password signs its user in wherever the credential lists — the client, the MCP endpoint, the
/// administrative one — and spends a separate bucket of attempts on each, which is what the surface in the partition
/// key buys.
/// </para>
/// <para>
/// Every refusal produces one indistinguishable answer: an empty <c>401</c> carrying the same challenges, whether
/// the request presented nothing, presented something that is not a Basic credential, presented a username nobody
/// holds, presented a wrong password, presented one for a credential somebody disabled, or has spent its attempts. The
/// reason the framework records reaches the server log only, and even there it names the rejection rather than the
/// credential.
/// </para>
/// <para>
/// <strong>The transport a request arrived over decides nothing here.</strong> A password is read, and the challenge
/// offered, on a clear-text hop exactly as on an encrypted one. This process reads the scheme of its own socket and
/// nothing beyond it, so the deployment that publishes on loopback for a proxy nobody had to declare and the one
/// exposing that socket to a network are one reading from here — and refusing on it refused the first as readily as
/// the second. Whether that hop is encrypted is the administrator's decision about their own deployment;
/// <see cref="Hosting.Warnings.PasswordClearTextTransportWarning" /> reports it at every startup, which is what an API
/// key crossing the same hop already gets. It is not what every credential gets: an OAuth bearer token on that hop is
/// still refused per request, before it is read, and this says nothing against that refusal.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The authentication framework materializes this handler for its registered scheme.")]
internal sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private readonly UserPasswordAuthenticator authenticator;
    private readonly IReadOnlyList<IPAddress> declaredProxyAddresses;
    private readonly IReadOnlyList<IPNetwork> declaredProxyNetworks;

    /// <summary>Initializes a new Basic authentication handler.</summary>
    /// <param name="schemeOptions">Which surface this registration protects and how often it lets a password be tried.</param>
    /// <param name="loggerFactory">The framework's own logging, which records the reason a refusal carried.</param>
    /// <param name="urlEncoder">The framework's own encoder, unused here and required by the base class.</param>
    /// <param name="authenticator">What judges the credential, below the request boundary.</param>
    /// <param name="reverseProxySettings">Whether the operator declared something in front, which decides whether the peer address distinguishes one caller from another.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authenticator" /> or <paramref name="reverseProxySettings" /> is <see langword="null" />.</exception>
    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory loggerFactory,
        UrlEncoder urlEncoder,
        UserPasswordAuthenticator authenticator,
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

    /// <summary>Reports whether a browser says a page made this request.</summary>
    /// <remarks>
    /// A browser holding a password it was prompted for on any path of this origin — the MCP endpoint's challenge, on
    /// the port every surface shares by default — attaches it to every later request to the origin, a cross-site form
    /// post included, and the administrative surface checks no origin of its own. <c>mfctl</c>, its only client, sends
    /// neither header read here, so a password arriving with one is one nobody chose to present here. The Fetch
    /// Metadata header reaches only a secure origin, so <c>Origin</c> is read beside it: a browser sends that on a
    /// cross-origin request whatever the scheme, which is what keeps a deployment serving this port in clear text
    /// covered. Asked before the store is, so such a request spends no attempt.
    /// </remarks>
    private static bool ABrowserPageMade(HttpRequest request) =>
        request.Headers.Origin.Count > 0
        || request.Headers["Sec-Fetch-Site"].Any(site => site is "cross-site" or "same-site");

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The header is read through <see cref="object.ToString" /> on the header values, which yields an empty string
    /// when the request carried none and a joined value when it carried several. Both reach the authenticator as
    /// something that is not one Basic credential, which is what they are, and both are refused identically — so a
    /// request that supplies the header twice is refused rather than having one of the two picked for it.
    /// </para>
    /// </remarks>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (this.Options.Surface == TransportSurface.Admin && ABrowserPageMade(this.Request))
        {
            return AuthenticateResult.Fail("A password is not accepted on the administrative endpoint from a request a browser page made.");
        }

        var result = await this.authenticator.AuthenticateAsync(
            this.Options.Surface.Name,
            this.Request.Headers.Authorization.ToString(),
            this.SourceToBoundBy(),
            this.Options.AttemptsPerMinute,
            this.Options.MaxConcurrentVerifications,
            this.Context.RequestAborted);

        if (result.Admitted is not { } admitted)
        {
            return AuthenticateResult.Fail("The request presented no usable credential.");
        }

        if (await UserCredentialAdmission.FindRefusalAsync(this.Context, this.Options.Surface, admitted) is { } refusal)
        {
            return AuthenticateResult.Fail(refusal);
        }

        var identity = TransportGrant.IdentityFor(
            admitted.CredentialId.ToString("D", CultureInfo.InvariantCulture),
            BasicAuthentication.CredentialIdClaimType,
            BasicAuthentication.RoleClaimType,
            this.Options.Surface.BasicSchemeName,
            admitted.Permissions);

        // The user is what separates this method from every other one: the credential named a person, so the principal
        // carries them rather than leaving the surface to answer for whose mail the request acts on. The credential
        // travels beside them because a session minted from this request outlives it, and disabling the credential is
        // what ends that session.
        identity.AddClaim(TransportCallerUser.ClaimFor(admitted.User));
        identity.AddClaim(TransportCallerCredential.ClaimFor(admitted.CredentialId));

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name));
    }

    /// <summary>Reports the address to bound this attempt by, or nothing where this peer tells two callers apart.</summary>
    /// <remarks>
    /// The address is the client's once the forwarded-headers policy has run: behind a proxy this deployment declared,
    /// it is the address that proxy forwarded in <c>X-Forwarded-For</c>, and a partition on it tells two callers apart
    /// as it does on a direct connection. What is left as a declared proxy's own address is a request that proxy
    /// forwarded no client for — no header at all — and a per-source partition on that would be one partition for the
    /// whole world, which a single guesser could empty and so close password sign-in for every user at once. The username bound is what holds there, and it holds per user rather than
    /// across all of them. Two further requests reach the same branch and keep only that bound: every request under a
    /// section trusting a range that covers a whole address family, which reads no forwarded address and so matches
    /// every peer as a declared proxy, and a forwarded client whose own address falls inside a declared network, as a
    /// client sharing the subnet of an ingress pool named as one does. A section declaring no proxy reads no forwarded
    /// address at all, so the peer there is the one that opened the connection and nothing a client wrote. A chain
    /// longer than <c>MaximumForwardedHops</c> is not among these: the policy applies the last hop it believes, and that
    /// hop is bounded as a client rather than dropped.
    /// <para>
    /// The question is asked of the peer rather than of the deployment, because <c>ReverseProxy</c> is one section for
    /// the whole process while a listener is not: a deployment declaring a proxy for one surface may serve another
    /// directly, and the peer arriving there is the real client and does tell two callers apart. Answering per process
    /// would drop the source axis on that listener too.
    /// </para>
    /// <para>
    /// The peer is read in its IPv4 form where it has one, because a dual-stack listener — the arrangement the endpoint
    /// options recommend — reports an IPv4 proxy as <c>::ffff:10.0.0.5</c> while the operator wrote <c>10.0.0.5</c>,
    /// and neither <see cref="IPAddress.Equals(object)" /> nor <see cref="IPNetwork.Contains" /> matches across
    /// address families. The forwarded-headers middleware maps the same peer down before it reads the same section, so
    /// reading it any other way here would leave the proxy's own scheme believed while the proxy went unrecognized as
    /// one — every request in the deployment sharing that one partition, and ten wrong passwords a minute from anybody
    /// behind it closing password sign-in for every user. Mapping is what keeps one configured list read one way.
    /// </para>
    /// </remarks>
    private string? SourceToBoundBy()
    {
        if (this.Context.Connection.RemoteIpAddress is not { } peer)
        {
            return null;
        }

        var address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;

        return this.DeclaredAsAProxy(address) ? null : address.ToString();
    }

    /// <summary>Reports whether the operator named this address, or a range holding it, as something standing in front of this process.</summary>
    private bool DeclaredAsAProxy(IPAddress peer) =>
        this.declaredProxyAddresses.Any(declared => declared.Equals(peer))
        || this.declaredProxyNetworks.Any(declared => declared.Contains(peer));

    /// <inheritdoc />
    /// <remarks>
    /// The bare bearer challenge every method on the surface produces, with the password challenge beside it, written
    /// where the constants they name live. It is the same challenge on every hop: a surface that reads a password over
    /// clear text and declines to ask for one would be a surface whose browser clients cannot sign in to it at all,
    /// which is a way of refusing the arrangement rather than of protecting it.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        BasicAuthentication.WriteChallenge(this.Context);

        return Task.CompletedTask;
    }
}
