// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Api;
using MailFathom.Host.Api.Documentation;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace MailFathom.Host;

/// <summary>Puts the middleware and the routes of every surface this deployment serves in front of the request.</summary>
/// <remarks>
/// <para>
/// Callable for the reason <see cref="HostComposition" /> is: top-level statements cannot be called, so a pipeline
/// written in them is one no test can build. What that costs is different here and worse. A misordered middleware
/// fails nothing at startup and nothing in a container check — it changes what a later middleware sees, so the defect
/// arrives as a working deployment answering the wrong way, and the case this file was extracted for is exactly that
/// shape: authentication placed where it ran before the forwarded scheme was applied, refusing every proxied token as
/// though it had crossed the network in clear text.
/// </para>
/// <para>
/// The order below is the contract and each step says what depends on standing where it does. Two of them also decide
/// what the framework does *around* this pipeline: minimal hosting inserts an authentication and an authorization
/// middleware of its own, ahead of everything here, unless the application adds each explicitly — which is why both
/// are added on the application itself rather than inside a branch, and why <c>UseWhen</c> is the wrong tool for
/// either. A branch builds a nested application, and what the framework reads is the outer one.
/// </para>
/// </remarks>
internal static class HostPipeline
{
    /// <summary>Composes the request pipeline the process serves every request through.</summary>
    /// <param name="app">The built application, whose own pipeline this fills in.</param>
    /// <param name="composition">What the service composition settled about which surfaces are served and what bounds them.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> or <paramref name="composition" /> is <see langword="null" />.</exception>
    /// <exception cref="OptionsValidationException">Thrown when a configured health probe is answered by no registered check.</exception>
    internal static void Compose(WebApplication app, ComposedHostSurfaces composition)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(composition);

        // A surface authenticates when it is served and configures a credential, and the two conditions are read
        // together everywhere below because either one alone answers a different question: settings carrying a
        // credential for a surface nobody enabled register nothing, and an enabled surface with no credential
        // configured registers nothing either.
        var mcpAuthenticates = composition.Mcp is { Enabled: true, RequiresAuthentication: true };
        var adminAuthenticates = composition.Admin is { Enabled: true, RequiresAuthentication: true };
        var clientAuthenticates = composition.Client is { Enabled: true, RequiresAuthentication: true };
        var anySurfaceAuthenticates = mcpAuthenticates || adminAuthenticates || clientAuthenticates;

        // First, ahead of the exception handler and of every isolation check, so that nothing downstream ever reads a
        // scheme or host the proxy already corrected. Discovery, the challenge, any address composed from a request,
        // and the authentication below then agree with the name the client used, and a request from a peer this
        // deployment does not trust passes through with the scheme and host it arrived under.
        app.UseForwardedHeaders();

        app.UseExceptionHandler();

        AddClearTextRedirect(app, composition);

        if (composition.Client is { Enabled: true, Application.Enabled: true })
        {
            // Ahead of the isolation below rather than behind it, and that ordering is the whole design. The bundle's
            // own paths are named by the client's build, so the isolation rule cannot list them and would have to admit
            // the unclaimed remainder on a client listener — which is the remainder the MCP surface owns, so a
            // deployment serving the two on separate sockets would find /mcp answering on the client's. Serving the
            // files first leaves that rule untouched: a request the bundle answers never reaches it, and one it does
            // not is judged exactly as before.
            //
            // It is behind the clear-text redirect above, so a socket that only redirects serves no page, and ahead of
            // authentication, which is what the page needs: a browser holds no credential until it has loaded the
            // application that obtains one.
            AddClientApplication(app, composition);
        }

        // Ahead of everything any surface adds, so a request for a surface this listener does not serve is refused
        // before it reaches CORS, authentication, the client-certificate check, or the rate limiter — and a probe that
        // arrived where the probes are not served is refused before it can report dependency state to whoever can
        // reach it.
        app.UseSurfaceIsolation(composition.Listeners.SurfacesByPort(), app.Environment);

        MapHealthProbes(app, composition);

        // Outside every surface branch below, because the document describes two of them and belongs to neither, and
        // because it is mapped on whether this is a development process rather than on what an operator enabled. It
        // maps nothing outside Development, so the routes then answer 404 for the honest reason that no endpoint
        // exists — a documentation surface that refused a credential would still confirm the catalogue is there.
        app.MapApiDocumentation(app.Environment);

        if (composition.Mcp.Enabled || composition.Client.Enabled)
        {
            // One CORS middleware for every surface that answers a browser, added here rather than inside either
            // surface's own composition: a second copy would run every policy twice, and the policy a request is
            // judged by comes from the endpoint it matched rather than from where the middleware was added. It stands
            // ahead of authentication, because whether this deployment serves a page's origin does not depend on which
            // credential the page attached, and ahead of the MCP origin check below so a preflight is answered by the
            // middleware that owns preflight rather than reaching a check written for real requests.
            app.UseCors();
        }

        if (composition.Mcp.Enabled)
        {
            ComposeMcpSurface(app, composition);
        }

        if (anySurfaceAuthenticates)
        {
            // One authentication middleware, on the application rather than in a branch, and here rather than earlier
            // or later. Behind the forwarded headers, so a credential is judged against the scheme the client actually
            // used. Behind the MCP checks above, because whether this deployment serves a page's origin and whether it
            // serves the calling program at all do not depend on which credential was attached. Ahead of the rate
            // limiter, because the MCP endpoint's per-caller bucket partitions on the identity this establishes.
            //
            // What it authenticates is the application's default scheme, which belongs to no surface and pre-authenticates
            // only the requests that need an identity this early; DefaultTransportAuthentication holds that decision and
            // why the administrative surface is deliberately not one of them. Adding it whenever any surface
            // authenticates is also what keeps the framework from adding one: minimal hosting inserts its own ahead of
            // this whole pipeline otherwise, which is where the forwarded scheme has not been applied yet.
            app.UseAuthentication();
        }

        if (composition.McpRequestTimeout is not null
            || composition.AdminRequestTimeout is not null
            || composition.ClientRequestTimeout is not null)
        {
            // Ahead of the rate limiter, so the ceiling covers the time a request spends waiting for a lease as well as
            // the time it spends being served. That wait is nothing under the default queue limits of zero, and is the
            // whole point of the ordering under a configured queue: a request queued for a caller's tokens is already
            // holding a concurrency permit, so leaving it outside the ceiling would leave the one wait that can last
            // until the next replenishment unbounded.
            app.UseRequestTimeouts();
        }

        if (composition.IsRateLimited)
        {
            // Behind authentication, so the MCP endpoint's per-client limit is counted under the identity that
            // established rather than under something the caller chose, and ahead of authorization, so a request that
            // is about to be refused for its credential still spends anonymous capacity — otherwise a flood of bad
            // credentials would be the one kind of traffic an endpoint served without limit. One middleware serves
            // whichever endpoints carry a policy, and a second copy would take a lease from both limiters and count one
            // request as two.
            app.UseRateLimiter();
        }

        if (anySurfaceAuthenticates)
        {
            // One authorization middleware serves every endpoint that requires it, whichever surface asked for it;
            // adding it twice would run every policy twice. It is what judges the administrative and the client
            // credential, which is why those surfaces' limiters above run in front of it and why each one's callers
            // share a bucket.
            app.UseAuthorization();
        }

        if (composition.Admin.Enabled)
        {
            ComposeAdminSurface(app, composition);
        }

        if (composition.Client.Enabled)
        {
            ComposeClientSurface(app, composition);
        }
    }

    /// <summary>Answers every request on a redirecting socket with the address of the encrypted one.</summary>
    /// <remarks>
    /// Composed from the sockets rather than from the surfaces, because a redirect is a property of the socket a
    /// request arrived on. Two surfaces sharing one clear-text port contribute one listener between them, carrying the
    /// domains both of them publish — which is what lets each redirect to an HTTPS port of its own from that shared
    /// socket, and why one name published by both at different addresses is refused before composition reaches this.
    /// </remarks>
    private static void AddClearTextRedirect(WebApplication app, ComposedHostSurfaces composition)
    {
        var clearTextRedirectListeners = composition.Listeners.Listeners
            .Where(static listener => listener.RedirectsClearText)
            .Select(static listener => new ClearTextRedirectListener(listener.Address.Port, listener.RedirectTargets))
            .ToArray();

        if (clearTextRedirectListeners.Length == 0)
        {
            return;
        }

        // Ahead of the isolation middleware and every route, which is what makes a redirecting socket serve nothing but
        // the redirect. Behind it, an administrative path arriving on a redirect port would be answered by isolation
        // with a 404 — a listener refusing a path it does not serve — and the client would read the endpoint as gone
        // rather than as moved.
        app.UseClearTextRedirectToHttps(new ClearTextRedirectTargets(clearTextRedirectListeners));
    }

    /// <summary>Serves the client's browser head, having proved this deployment carries one.</summary>
    /// <remarks>
    /// The assertion is the same shape as the probe one below and exists for the same reason: an enabled setting whose
    /// subject is absent is a deployment somebody configured and nobody can use. The bundle is copied into the image at
    /// build time rather than published by anything here, so a process started from a build that never carried one — a
    /// service run straight from the sources, an image built before the client existed — is exactly the case an
    /// operator has to be told about at startup rather than through a page of 404s.
    /// </remarks>
    private static void AddClientApplication(WebApplication app, ComposedHostSurfaces composition)
    {
        if (!ClientApplicationFiles.BundleIsPresent(app.Environment))
        {
            throw new OptionsValidationException(
                ClientEndpointOptions.SectionName,
                typeof(ClientEndpointOptions),
                [
                    $"{ClientEndpointOptions.SectionName}:{nameof(ClientEndpointOptions.Application)}:{nameof(ClientApplicationOptions.Enabled)} is set, but this deployment carries no client to serve: '{ClientApplicationOptions.EntryDocument}' is absent from '{app.Environment.WebRootPath}'. The bundle travels inside the MailFathom container image; a host started from anything else serves the API surfaces alone.",
                ]);
        }

        app.UseClientApplication(composition.Client.ListenerPorts);
    }

    /// <summary>Maps the liveness and readiness routes, having proved each configured probe is answered by something.</summary>
    /// <remarks>
    /// A probe reports healthy over no checks at all, because the aggregate of nothing is healthy. Asserting the
    /// composed result rather than the wiring is what catches a tag that stopped matching: readiness answering without
    /// consulting the database would keep an instance in traffic that cannot serve a request.
    /// </remarks>
    private static void MapHealthProbes(WebApplication app, ComposedHostSurfaces composition)
    {
        if (!composition.Health.Enabled)
        {
            return;
        }

        var unansweredProbes = HealthProbeEndpoints.FindUnansweredProbes(
            app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        if (unansweredProbes.Count > 0)
        {
            throw new OptionsValidationException(
                HealthEndpointOptions.SectionName,
                typeof(HealthEndpointOptions),
                unansweredProbes);
        }

        app.MapHealthProbes();
    }

    /// <summary>Adds the checks the protocol surface runs in front of a credential, and maps the routes it serves.</summary>
    /// <remarks>
    /// Everything here precedes the one authentication middleware, and each step says why it has to. What follows the
    /// mapping is the requirement each route carries, which is metadata rather than middleware: the endpoint is
    /// selected before this pipeline runs at all, so where a convention is applied decides nothing about ordering.
    /// </remarks>
    private static void ComposeMcpSurface(WebApplication app, ComposedHostSurfaces composition)
    {
        if (composition.Mcp.AllowsOAuth)
        {
            // The policy reaches the MCP route as endpoint metadata, and the protected resource metadata document has
            // none to carry it: the authentication handler publishes it instead of a mapped route. A browser client
            // reads that document before it holds any credential, so without the policy applied to its path the one
            // response that says where to authorize is the one a page cannot read.
            var protectedResourceMetadataPath = ProtectedResourceMetadataAddress.PathFor(
                composition.Mcp.OAuthMethods()[0].CanonicalResource());

            app.UseWhen(
                context => context.Request.Path.Equals(protectedResourceMetadataPath, StringComparison.OrdinalIgnoreCase),
                metadataDocument => metadataDocument.UseCors(McpTransportSecurityExtensions.CorsPolicyName));
        }

        app.UseMcpOriginValidation();

        if (composition.Mcp.ClientCertificateProfiles.Count > 0)
        {
            // Ahead of authentication, because which client application is calling and which credential it presents are
            // separate questions: a request from a program this deployment does not serve is turned away before any
            // credential is read.
            app.UseMcpClientCertificateValidation(composition.Mcp.ToClientCertificateTrustProfiles());
        }

        var mcpEndpoint = app
            .MapMcp(McpEndpointRoute.Path)
            .RequireCors(McpTransportSecurityExtensions.CorsPolicyName);

        // Mapped with the MCP surface because it belongs to it: the links it answers are minted by an MCP tool, and
        // serving it here gives it that endpoint's transport, its rate limits, and its enablement without a listener of
        // its own. What it does not inherit is what the two middlewares above scope to the protocol path themselves —
        // the origin allow-list and the client-certificate profiles — and that is the right side of the line: both ask
        // which program is calling, which is the question this route deliberately does not have an answer to. It
        // carries no authorization either, since the signed capability in the URL is what admits a request and the
        // things that fetch files cannot attach an MCP credential, which is why it is mapped outside the group the
        // access policy is applied to below.
        var attachmentDownload = app.MapEmailAttachmentDownload();

        if (composition.McpRateLimits is not null)
        {
            // On the endpoint for the same reason authorization is: the readiness and liveness endpoints have to keep
            // answering while this one is refusing. The process-wide half of the policy cannot be attached here, because
            // an endpoint resolves one limiter, so it rides on the global limiter and excludes every other route itself.
            mcpEndpoint.RequireRateLimiting(TransportSurface.Mcp.RateLimitingPolicyName);

            // The same per-caller policy on the download route, which admits no credential and therefore spends the
            // surface's shared anonymous bucket. That is the point rather than a limitation: an unauthenticated route
            // serving mail content is exactly the one that must not be unbounded. The process-wide half reaches it as
            // well, because the surface names this prefix among the ones it serves, so a redemption takes a permit
            // from the same concurrency limiter the protocol route does rather than opening an unbounded second door
            // onto the same message store and MIME parser.
            attachmentDownload.RequireRateLimiting(TransportSurface.Mcp.RateLimitingPolicyName);
        }

        if (composition.McpRequestTimeout is not null)
        {
            // On the endpoint rather than as the default policy, for the reason the limiter is: the probes answer on
            // routes this ceiling must never reach.
            mcpEndpoint.WithRequestTimeout(TransportSurface.Mcp.RequestTimeoutPolicyName);

            // And on the download route, for the reason the rate limit is on it: it takes a permit from the same
            // concurrency limiter, and it is the one route here that holds a response stream open for as long as its
            // reader takes. A client reading just above Kestrel's minimum response rate is the case the ceiling exists
            // for, and it needs no credential to be that client.
            attachmentDownload.WithRequestTimeout(TransportSurface.Mcp.RequestTimeoutPolicyName);
        }

        if (composition.Mcp.RequiresAuthentication)
        {
            // On the endpoint rather than as a fallback policy, so the readiness response and the health endpoints keep
            // answering unauthenticated while everything the MCP route exposes is covered by the one requirement it
            // carries. Under the stateless transport that route is the post alone; a get or a delete is not mapped at
            // all, so there is no second entry into the protocol surface for a requirement to miss.
            //
            // It is also what the application's default authentication reads to recognize a protected MCP request, so
            // this line decides both what admits a caller and what establishes who they are before the limiter counts
            // them.
            mcpEndpoint.RequireAuthorization(TransportSurface.Mcp.AccessPolicyName);
        }
    }

    /// <summary>Maps the administrative routes and the requirement they carry.</summary>
    /// <remarks>
    /// The administrative routes are pre-authenticated by nothing. The authorization middleware authenticates with the
    /// schemes this surface's policy names, so requiring the policy is both what admits a caller and what establishes
    /// who they are — and because that runs behind the limiter, every administrative caller shares one partition until
    /// its credential has been judged. That is the stronger bound for the threat the limit exists against, which is
    /// unbounded key guessing, and it is why the burst is the endpoint's rather than one caller's.
    /// </remarks>
    internal static void ComposeAdminSurface(IEndpointRouteBuilder app, ComposedHostSurfaces composition)
    {
        var adminApi = app.MapAdminApi();

        if (composition.AdminRateLimits is not null)
        {
            adminApi.RequireRateLimiting(TransportSurface.Admin.RateLimitingPolicyName);
        }

        if (composition.AdminRequestTimeout is not null)
        {
            adminApi.WithRequestTimeout(TransportSurface.Admin.RequestTimeoutPolicyName);
        }

        if (composition.Admin.RequiresAuthentication)
        {
            adminApi.RequireAuthorization(TransportSurface.Admin.AccessPolicyName);
        }

        if (composition.Admin.AllowsOAuth)
        {
            // Outside the group the requirement was attached to, and deliberately: its reader is a client that has no
            // credential yet and is reading this to find out where to obtain one.
            app.MapProtectedResourceMetadata(
                [.. composition.Admin.Authentication],
                AdminEndpointOptions.GrantedSurface);
        }
    }

    /// <summary>Maps the client routes, the CORS policy they answer under, and the requirement they carry.</summary>
    /// <remarks>
    /// The client routes are pre-authenticated by nothing, exactly as the administrative ones are: the authorization
    /// middleware authenticates with the schemes this surface's policy names, so requiring the policy is both what
    /// admits a caller and what establishes who they are — and because that runs behind the limiter, every client
    /// caller shares one partition until its credential has been judged. That is the stronger bound for the threat the
    /// limit exists against, which is unbounded key guessing.
    /// <para>
    /// The CORS policy is required on the endpoints rather than applied as a default, for the reason the limits and the
    /// ceiling are: the probes and the other surfaces answer on routes this policy must never decide anything about.
    /// </para>
    /// </remarks>
    internal static void ComposeClientSurface(IEndpointRouteBuilder app, ComposedHostSurfaces composition)
    {
        var clientApi = app
            .MapClientApi()
            .RequireCors(ClientTransportSecurityExtensions.CorsPolicyName);

        if (composition.ClientRateLimits is not null)
        {
            clientApi.RequireRateLimiting(TransportSurface.Client.RateLimitingPolicyName);
        }

        if (composition.ClientRequestTimeout is not null)
        {
            clientApi.WithRequestTimeout(TransportSurface.Client.RequestTimeoutPolicyName);
        }

        if (composition.Client.RequiresAuthentication)
        {
            clientApi.RequireAuthorization(TransportSurface.Client.AccessPolicyName);
        }

        if (composition.Client.AllowsOAuth)
        {
            // Outside the group the requirement was attached to, for the reason the administrative document is. It
            // carries the CORS policy of its own, because unlike that surface's reader this one is a page: a document
            // a browser is refused permission to read is a client that cannot discover where to authorize, which is the
            // one thing it needed before it could hold any credential at all.
            app.MapProtectedResourceMetadata(
                    [.. composition.Client.Authentication],
                    ClientEndpointOptions.GrantedSurface)
                .RequireCors(ClientTransportSecurityExtensions.CorsPolicyName);
        }
    }
}
