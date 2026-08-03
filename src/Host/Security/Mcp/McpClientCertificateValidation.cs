// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.Mcp;

namespace MailFathom.Host.Security.Mcp;

/// <summary>Refuses an MCP request whose client certificate no configured trust profile accepts.</summary>
/// <remarks>
/// <para>
/// The certificate is taken from the TLS connection and from nowhere else. A header naming a certificate — however it
/// is spelled, and whichever proxy is in the habit of setting it — is written by whoever sent the request, so trusting
/// one would turn client authentication into a value a client fills in for itself. Terminating TLS at a proxy and
/// forwarding what it saw is a design with its own trust boundary and is deliberately not this one.
/// </para>
/// <para>
/// It runs behind the origin check and ahead of authentication. A certificate identifies the client application, a key
/// identifies the deployment's own credential, and neither answer depends on the other; running this first means a
/// request from a program this deployment does not serve is turned away before any credential is read.
/// </para>
/// <para>
/// The refusal is a <c>403</c> with no body, as the origin refusal is. There is nothing safe to add: a client learning
/// which profile objected, and why, would learn what to present next.
/// </para>
/// </remarks>
internal static class McpClientCertificateValidation
{
    /// <summary>Applies the configured trust profiles to every request addressed to the MCP endpoint.</summary>
    /// <param name="app">The application being composed.</param>
    /// <param name="trustProfiles">The profiles composition mapped from configuration.</param>
    /// <returns>The application, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> or <paramref name="trustProfiles" /> is <see langword="null" />.</exception>
    internal static WebApplication UseMcpClientCertificateValidation(
        this WebApplication app,
        IReadOnlyList<McpClientCertificateTrustProfile> trustProfiles)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(trustProfiles);

        var authenticator = app.Services.GetRequiredService<McpClientCertificateAuthenticator>();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(McpEndpointRoute.Path),
            mcpEndpoint => mcpEndpoint.Use((context, next) =>
                ServeWhenTheConnectionCertificateIsAcceptedAsync(context, next, authenticator, trustProfiles)));

        return app;
    }

    /// <summary>Judges one request's connection certificate and serves it only when a profile accepted it.</summary>
    /// <param name="context">The request being judged, whose connection carries the certificate.</param>
    /// <param name="next">The rest of the pipeline, reached only when the certificate was accepted.</param>
    /// <param name="authenticator">The authenticator that judges the certificate against the profiles.</param>
    /// <param name="trustProfiles">The profiles composition mapped from configuration.</param>
    /// <returns>A task that completes when the request has been refused or served.</returns>
    /// <remarks>
    /// The certificate is read from the connection rather than awaited from it, because Kestrel is configured to ask
    /// for one during the handshake. Awaiting would invite a renegotiation on the request thread for a client that
    /// simply has no certificate, which is an ordinary case here rather than something to recover from. No header takes
    /// any part in this, which is what a test on this method rather than on the pipeline is able to state.
    /// </remarks>
    internal static async Task ServeWhenTheConnectionCertificateIsAcceptedAsync(
        HttpContext context,
        RequestDelegate next,
        McpClientCertificateAuthenticator authenticator,
        IReadOnlyList<McpClientCertificateTrustProfile> trustProfiles)
    {
        var result = await authenticator.AuthenticateAsync(
            trustProfiles,
            context.Connection.ClientCertificate,
            context.RequestAborted);

        if (!result.Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            return;
        }

        // Published for the rate limiter, which keeps a client application's capacity apart under an authentication mode
        // that establishes no other identity. Set only when a profile actually matched, because a request served on the
        // strength of every profile being content without a certificate identifies nobody.
        if (result.MatchedProfileName is { } matchedProfileName)
        {
            context.Features.Set(new McpClientCertificateIdentity(matchedProfileName));
        }

        await next(context);
    }
}
