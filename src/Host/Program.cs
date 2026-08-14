// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Provisioning;
using MailFathom.Host.Hosting;
using MailFathom.Host.Hosting.Startup;
using MailFathom.Host.Observability;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Mcp;
using MailFathom.Host.Security.Transport;
using MailFathom.Mcp;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// Composed before anything else, CreateBuilder included, so that a malformed appsettings.json, a failure during
// composition, and a failed host start are all reported rather than only printed. The pipeline the container owns
// does not exist until Build has returned, and on a startup failure it never flushes.
using var bootstrapLogger = BootstrapLogger.CreateFromEnvironment();
bootstrapLogger.RecordHostStarting();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Before anything reads configuration, so a mounted ConfigMap is ordinary configuration to every binding below
    // rather than a second source consulted afterwards. The files land beneath the environment-variable provider, which
    // keeps an environment variable an override of a mounted file rather than something a stale mount can beat.
    var provisionedConfigurationFileCount = builder.Configuration.AddProvisionedConfiguration();
    bootstrapLogger.RecordProvisionedConfigurationFiles(provisionedConfigurationFileCount);

    // Once every source exists and before anything binds one, because this asks whether a value can reach its reader at
    // all rather than what the value says. The few settings only the environment can deliver were read before this line
    // — by the pipeline above, by the OpenTelemetry exporter, by the .NET host, by OpenSSL — so a value that arrived
    // from anywhere else is refused here instead of being accepted and ignored.
    EnvironmentOnlySettings.RejectMisplacedValues(builder.Configuration, Environment.GetEnvironmentVariable);

    // Every service this process runs on, registered in one callable place rather than here, because top-level
    // statements cannot be called: a composition root written in them is one no test can build, and an unregistered
    // dependency then reaches an operator as an exception out of a worker instead of as a suite that failed.
    var composition = HostComposition.Compose(builder);

    var app = builder.Build();

    // Before the server starts rather than from a hosted service, because a hosted service could be started after the
    // web host and a certificate proven then would be proven after the listener was already open. A profile whose
    // material is missing, expired, or issued for another domain therefore fails startup with nothing listening.
    if (composition.Mcp.Enabled && composition.Mcp.TerminatesTls)
    {
        await app.Services.GetRequiredService<TransportServerCertificateStore>()
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    if (composition.Admin.Enabled && composition.Admin.TerminatesTls)
    {
        await app.Services.GetRequiredKeyedService<TransportServerCertificateStore>(HostComposition.AdminCertificateStoreKey)
            .LoadAsync(app.Lifetime.ApplicationStopping);
    }

    // For the same reason, and with the same outcome: a TLS transport whose material is unusable fails startup with
    // nothing listening rather than downgrading the probe port to clear text.
    await app.Services.GetRequiredService<HealthEndpointCertificate>()
        .LoadAsync(app.Lifetime.ApplicationStopping);

    // One authorization middleware serves every endpoint that requires it, and either endpoint may be the one that
    // adds it. Adding it twice would run every policy twice.
    var authorizationMiddlewareAdded = false;

    // The same holds for the rate limiter, which is one middleware serving whichever endpoints carry a policy. Adding
    // it twice would acquire a lease from both limiters twice and count one request as two.
    var rateLimiterMiddlewareAdded = false;

    // And for the request-timeout middleware, for the same reason again: it is one middleware reading whichever policy
    // the resolved endpoint names, and a second copy would start a second timer over the same request.
    var requestTimeoutMiddlewareAdded = false;

    // First, ahead of the exception handler and of every isolation check, so that nothing downstream ever reads a
    // scheme or host the proxy already corrected. Discovery, the challenge, and any address composed from a request
    // then agree with the name the client used, and a request from a peer this deployment does not trust passes
    // through with the scheme and host it arrived under.
    app.UseForwardedHeaders();

    app.UseExceptionHandler();

    // Composed from the sockets rather than from the surfaces, because a redirect is a property of the socket a request
    // arrived on. Two surfaces sharing one clear-text port contribute one listener between them, carrying the domains
    // both of them publish — which is what lets each redirect to an HTTPS port of its own from that shared socket, and
    // why one name published by both at different addresses is refused before composition reaches this.
    var clearTextRedirectListeners = composition.Listeners.Listeners
        .Where(static listener => listener.RedirectsClearText)
        .Select(static listener => new ClearTextRedirectListener(listener.Address.Port, listener.RedirectTargets))
        .ToArray();

    if (clearTextRedirectListeners.Length > 0)
    {
        // Ahead of the isolation middleware and every route, which is what makes a redirecting socket serve nothing but
        // the redirect. Behind it, an administrative path arriving on a redirect port would be answered by isolation
        // with a 404 — a listener refusing a path it does not serve — and the client would read the endpoint as gone
        // rather than as moved.
        app.UseClearTextRedirectToHttps(new ClearTextRedirectTargets(clearTextRedirectListeners));
    }

    // Ahead of everything any surface adds, so a request for a surface this listener does not serve is refused before
    // it reaches CORS, authentication, the client-certificate check, or the rate limiter — and a probe that arrived
    // where the probes are not served is refused before it can report dependency state to whoever can reach it.
    app.UseSurfaceIsolation(composition.Listeners.SurfacesByPort());

    if (composition.Health.Enabled)
    {
        // A probe reports healthy over no checks at all, because the aggregate of nothing is healthy. Asserting the
        // composed result rather than the wiring is what catches a tag that stopped matching: readiness answering
        // without consulting the database would keep an instance in traffic that cannot serve a request.
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

    if (composition.Mcp.Enabled)
    {
        // CORS first, so a browser's preflight is answered by the middleware that owns preflight rather than reaching a
        // check written for real requests. The origin check then runs ahead of authentication, because whether this
        // deployment serves a page's origin does not depend on which credential the page attached.
        app.UseCors();

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

        if (composition.Mcp.RequiresAuthentication)
        {
            // Authentication also serves the protected resource metadata document, which the MCP authentication scheme
            // publishes as a request handler rather than as a route, so the middleware runs whether or not the request
            // that follows carries a credential.
            //
            // Scoped away from the administrative routes rather than added globally. This middleware authenticates with
            // the application's default scheme, which HostComposition pins to the MCP surface's, so an administrative request
            // reaching it would have its credential compared against the MCP endpoint's keys before the administrative
            // policy ever ran. Nothing would be disclosed by that — the comparison is constant-time and the result is
            // discarded — but a credential provisioned for one surface must not be offered to the other's handlers.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments(AdminEndpointOptions.RoutePrefix),
                mcpBranch => mcpBranch.UseAuthentication());
        }

        if (composition.McpRequestTimeout is not null || composition.AdminRequestTimeout is not null)
        {
            // Ahead of the rate limiter, so the ceiling covers the time a request spends waiting for a lease as well as
            // the time it spends being served. That wait is nothing under the default queue limits of zero, and is the
            // whole point of the ordering under a configured queue: a request queued for a caller's tokens is already
            // holding a concurrency permit, so leaving it outside the ceiling would leave the one wait that can last
            // until the next replenishment unbounded.
            app.UseRequestTimeouts();
            requestTimeoutMiddlewareAdded = true;
        }

        if (composition.IsRateLimited)
        {
            // Behind authentication, so the MCP endpoint's per-client limit is counted under the identity that
            // established rather than under something the caller chose, and ahead of authorization, so a request that is
            // about to be refused for its credential still spends anonymous capacity — otherwise a flood of bad
            // credentials would be the one kind of traffic an endpoint served without limit. It is added here whenever
            // either endpoint is bounded, because this is the point that satisfies both orderings: an administrative
            // request reaches it as well, since what the branch above keeps away from those routes is the
            // authentication middleware rather than the pipeline.
            app.UseRateLimiter();
            rateLimiterMiddlewareAdded = true;
        }

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
            app.UseAuthorization();
            authorizationMiddlewareAdded = true;

            // On the endpoint rather than as a fallback policy, so the readiness response and the health endpoints keep
            // answering unauthenticated while everything the MCP route exposes is covered by the one requirement it
            // carries. Under the stateless transport that route is the post alone; a get or a delete is not mapped at
            // all, so there is no second entry into the protocol surface for a requirement to miss.
            mcpEndpoint.RequireAuthorization(TransportSurface.Mcp.AccessPolicyName);
        }
    }

    if (composition.Admin.Enabled)
    {
        // Ahead of the limiter for the reason it is ahead of it on the MCP branch, so a request queued for a lease is
        // inside the ceiling rather than outside it.
        if (composition.AdminRequestTimeout is not null && !requestTimeoutMiddlewareAdded)
        {
            app.UseRequestTimeouts();
        }

        // Ahead of the authorization middleware below, which is what judges this surface's credential, so a request
        // about to be refused for a wrong key has already spent capacity. That ordering is the whole point of bounding
        // this endpoint: unbounded key guessing is what it is exposed to, and the guesses are the traffic authorization
        // turns away.
        if (composition.AdminRateLimits is not null && !rateLimiterMiddlewareAdded)
        {
            app.UseRateLimiter();
        }

        // The administrative routes carry no authentication middleware of their own. That middleware authenticates with
        // the application's default scheme, which belongs to the MCP surface; the authorization middleware instead
        // authenticates with the schemes the policy names, which are this surface's. So requiring the policy is both
        // what admits a caller and what establishes who they are.
        //
        // That is also why this surface's per-caller partition is one bucket rather than one per key: there is no
        // identity to partition on until authorization has run, and running the limiter behind it would serve the
        // guessing this exists to bound. The bucket is the endpoint's, and its capacity is sized as such.
        if (composition.Admin.RequiresAuthentication && !authorizationMiddlewareAdded)
        {
            app.UseAuthorization();
        }

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
            app.MapAdminProtectedResourceMetadata(composition.Admin.OAuthMethods());
        }
    }

    await app.RunAsync();

    bootstrapLogger.RecordHostStopped();
}
catch (Exception exception)
{
    // The exception leaves unchanged, so the runtime still writes it to standard error and the exit code is the one
    // an unhandled failure produces. The catch exists only to add the record that survives the process ending.
    bootstrapLogger.RecordHostFailed(exception);

    throw;
}
