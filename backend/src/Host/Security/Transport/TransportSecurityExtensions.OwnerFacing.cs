// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Basic;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Infrastructure.Security.OAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MailFathom.Host.Security.Transport;

/// <summary>Registers the methods a mail-serving surface accepts, every one of which resolves the owner it acts for.</summary>
/// <remarks>
/// <para>
/// The schemes, the routing, and the policy are the same shape the configured surface registers, and one difference
/// runs through all of them: nothing here is handed a credential or a grant. A key, a public key, a password, and a
/// validated subject each resolve a record beside an owner row, and the owner and the permissions both arrive from that
/// record — so what a registration carries is which methods the deployment turned on and nothing that could
/// authenticate anybody.
/// </para>
/// <para>
/// A credential that resolves no record is refused exactly as one nobody holds, which is why every method's authenticator
/// answers one indistinguishable refusal rather than reporting that a row was missing. The policy asks for the owner
/// claim on top of that, so a principal that reached it without one — which no scheme here produces — is refused rather
/// than served against whoever the surface would otherwise have answered for.
/// </para>
/// </remarks>
internal static partial class TransportSecurityExtensions
{
    /// <summary>Adds one mail-serving surface's authentication schemes and its authorization requirement.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="surface">The surface being protected, which names every scheme and the policy.</param>
    /// <param name="methods">The methods the surface accepts, in configuration order.</param>
    /// <param name="challengeSchemeName">The scheme answering a request that presented no credential this surface can place, which is both what authenticates it and what challenges it.</param>
    /// <returns>The authentication builder, so a surface can add schemes only it needs.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="surface" /> is the struct default.</exception>
    internal static AuthenticationBuilder AddOwnerFacingTransportAuthentication(
        this IServiceCollection services,
        TransportSurface surface,
        IReadOnlyList<OwnerFacingAuthenticationOptions> methods,
        string challengeSchemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(challengeSchemeName);

        if (!surface.IsSpecified)
        {
            throw new ArgumentException("A transport surface is required to name the schemes and the policy.", nameof(surface));
        }

        var acceptsApiKey = OwnerFacingAuthenticationConfiguration.Accepts(methods, OwnerCredentialMethod.ApiKey);
        var acceptsPublicKey = OwnerFacingAuthenticationConfiguration.Accepts(methods, OwnerCredentialMethod.PublicKey);
        var basicMethod = OwnerFacingAuthenticationConfiguration.BasicMethodIn(methods);
        var oauthMethods = OwnerFacingAuthenticationConfiguration.OAuthMethodsIn(methods);

        var authentication = services.AddAuthentication();

        AddRoutingScheme(
            authentication,
            surface,
            OAuthSchemesByIssuer(surface, oauthMethods),
            acceptsApiKey ? surface.ApiKeySchemeName : null,
            acceptsPublicKey ? surface.ClientAssertionSchemeName : null,
            basicMethod is null ? null : surface.BasicSchemeName,
            challengeSchemeName);

        if (basicMethod is not null)
        {
            // The bound is the entry's own and the block is optional, so an entry naming the method and writing nothing
            // takes the product default rather than registering a scheme that would refuse every request.
            authentication.AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>(
                surface.BasicSchemeName,
                schemeOptions =>
                {
                    schemeOptions.Surface = surface;
                    schemeOptions.AttemptsPerMinute =
                        basicMethod.Basic?.AttemptsPerMinute ?? BasicAuthenticationOptions.DefaultAttemptsPerMinute;
                });
        }

        if (acceptsApiKey)
        {
            authentication.AddScheme<OwnerApiKeyAuthenticationSchemeOptions, OwnerApiKeyAuthenticationHandler>(
                surface.ApiKeySchemeName,
                schemeOptions => schemeOptions.Surface = surface);
        }

        if (acceptsPublicKey)
        {
            // The replay store is one for the process, as it is on the configured axis: an identifier spent on either
            // surface is spent, which is the safe direction and costs a client nothing, since one is minted per request.
            // The authenticator is scoped rather than shared, because the credential store it reads through is.
            services.TryAddSingleton<ClientAssertionReplayStore>();
            services.TryAddScoped<OwnerClientAssertionAuthenticator>();
            authentication.AddScheme<OwnerClientAssertionAuthenticationSchemeOptions, OwnerClientAssertionAuthenticationHandler>(
                surface.ClientAssertionSchemeName,
                schemeOptions => schemeOptions.Surface = surface);
        }

        if (oauthMethods.Count > 0)
        {
            AddMetadataBackchannel(services);
        }

        // Each entry's own servers are registered against that entry's resource, which is what makes an entry the unit
        // a token is judged by. What the entry no longer carries is who those servers may speak for: the subject a
        // validated token names resolves a record, and a token naming one nothing maps fails authentication here.
        foreach (var method in methods.Where(method => method.OAuth is not null))
        {
            var oauthMethod = method.OAuth!;
            var narrowedByTokenScopes = method.PermissionsFromTokenScopes;

            foreach (var authorizationServer in oauthMethod.AuthorizationServers)
            {
                var schemeName = surface.OAuthSchemeNameFor(authorizationServer.Name!);

                authentication.AddJwtBearer(schemeName);
                services.AddOptions<JwtBearerOptions>(schemeName)
                    .Configure<IHttpClientFactory>((jwtOptions, transportFactory) =>
                        ConfigureAuthorizationServer(
                            jwtOptions,
                            authorizationServer,
                            oauthMethod,
                            transportFactory,
                            context => ResolveTheOwnerAValidatedTokenNames(context, narrowedByTokenScopes)));
            }
        }

        AddOwnerFacingAuthorizationPolicy(services, surface, oauthMethods);

        return authentication;
    }

    /// <summary>Reduces a validated token to the owner its subject resolves, and writes the grant that owner's record holds.</summary>
    /// <remarks>
    /// <para>
    /// The identity is minimised first, exactly as it is on the configured axis, so nothing downstream can read a name,
    /// an address, or a group the authorization server chose to include. What is added afterwards is what this axis
    /// establishes: the owner the request acts for, and the permissions recorded beside them.
    /// </para>
    /// <para>
    /// A token whose subject no enabled record maps fails authentication rather than being admitted with an empty
    /// grant. Admitting it would produce a caller acting for nobody, and the surfaces answer for whose mail a request
    /// reaches by reading the owner claim — so the refusal here is the same refusal an unknown API key meets, arrived
    /// at one step later because a signature had to be checked first.
    /// </para>
    /// <para>
    /// The resolver is taken from the request's own services rather than captured, because it reads through the
    /// credential store, which is scoped to the request like every other database access in the process.
    /// </para>
    /// </remarks>
    private static async Task ResolveTheOwnerAValidatedTokenNames(
        TokenValidatedContext context,
        bool narrowedByTokenScopes)
    {
        var identity = context.Principal is { } validatedPrincipal
            ? OAuthIdentity.FromValidatedToken(validatedPrincipal.Claims, context.Scheme.Name)
            : null;

        if (identity is null || !OAuthIdentity.TryReadIssuerAndSubject(identity, out var issuer, out var subject))
        {
            context.Fail("The validated token names no subject.");

            return;
        }

        var admitted = await context.HttpContext.RequestServices
            .GetRequiredService<OwnerOAuthSubjectResolver>()
            .ResolveAsync(issuer, subject, context.HttpContext.RequestAborted);

        if (admitted is null)
        {
            context.Fail("The validated token names no owner this deployment serves.");

            return;
        }

        identity.AddClaims(
            TransportGrant.ClaimsFor(GrantHeldByToken(identity, admitted.Permissions, narrowedByTokenScopes)));
        identity.AddClaim(TransportCallerOwner.ClaimFor(admitted.Owner));

        context.Principal = new ClaimsPrincipal(identity);
    }

    /// <summary>Registers the requirement this mail-serving surface's routes carry.</summary>
    /// <remarks>
    /// It names only this surface's routing scheme, for the reason the configured policy does. What it asks beyond that
    /// is the owner claim rather than a list of authorized identities: who this deployment serves is a set of records
    /// rather than a configured set of subjects, so the question the policy can still usefully ask is whether the
    /// credential resolved one at all.
    /// </remarks>
    private static void AddOwnerFacingAuthorizationPolicy(
        IServiceCollection services,
        TransportSurface surface,
        IReadOnlyList<OAuthValidationOptions> oauthMethods)
    {
        var requiredScopesByIssuer = TransportAuthenticationConfiguration.RequiredScopesByIssuer(oauthMethods);

        services.AddAuthorization(authorizationOptions => authorizationOptions.AddPolicy(
            surface.AccessPolicyName,
            policy => policy
                .AddAuthenticationSchemes(surface.RoutingSchemeName)
                .RequireAssertion(context =>
                    TransportAccessPolicy.IsOwnerAuthorized(context.User, requiredScopesByIssuer))));
    }
}
