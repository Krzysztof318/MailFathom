// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Signals;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Compresses what the client endpoint serves, in whichever encoding the caller offered.</summary>
/// <remarks>
/// <para>
/// The bodies this surface serves are JSON that repeats itself — a mail list is a hundred rows of the same field names
/// around a few kilobytes of subject and preview text — and it travelled uncompressed, so a page cost six times the
/// bytes it had to. Nothing in front of a deployment fixes that on its behalf: <c>deploy/</c> ships arrangements with
/// no reverse proxy at all, so a surface that does not compress its own responses is one that ships uncompressed.
/// </para>
/// <para>
/// <b>Why BREACH does not apply here, which is what
/// <see cref="Microsoft.AspNetCore.ResponseCompression.ResponseCompressionOptions.EnableForHttps" /> is off by
/// default for.</b> The attack recovers a secret from a compressed body by measuring how the body's length moves as an
/// attacker-chosen string is reflected into it, and it needs three things at once. It needs the victim's browser to
/// attach a credential to a request the attacker caused: this surface authenticates by header — a bearer token, an API
/// key, or a client assertion the page attaches itself — and never by cookie, so a cross-site request reaches it with
/// no credential and is answered as an anonymous one. It needs a caller-supplied value reflected into the same body as
/// the secret: no response composed here carries one, the search route being the closest and deliberately not echoing
/// the query it was asked. And the refusals that do quote a caller's value back are served as
/// <c>application/problem+json</c>, which is not among the media types compressed below.
/// </para>
/// <para>
/// What is compressed is the framework's default set — JSON, text, XML, CSS, JavaScript, and WebAssembly — which is
/// also what keeps an already-compressed payload from being compressed twice: an attachment download and a portrait
/// travel under the media type their content declares, and a JPEG, a PDF, or a ZIP is not in that set. The compression
/// level is the framework's default of <see cref="System.IO.Compression.CompressionLevel.Fastest" /> for both
/// providers rather than a tuned one, because the ratio past it is a few percent and the cost is the request's own
/// latency, which is the thing this exists to reduce.
/// </para>
/// <para>
/// The encoding is negotiated rather than fixed: the framework reads <c>Accept-Encoding</c>, serves <c>br</c> to a
/// caller that offered it and <c>gzip</c> to one that offered only that, and serves a caller that offered neither the
/// body unchanged.
/// </para>
/// </remarks>
internal static class ClientResponseCompression
{
    /// <summary>Registers the compression this surface's responses are served under.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Registered on whether this deployment serves the client surface, because it is the only surface that reaches
    /// the middleware — the MCP and administrative surfaces are a separate reading, and
    /// <see cref="ServedByThisSurface" /> is what keeps them out of it.
    /// </remarks>
    internal static IServiceCollection AddClientResponseCompression(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Over HTTPS as well as over clear text, on the reading above. Left at its default, the middleware would
        // compress nothing at all in the deployment shape this was written for, since a surface a browser reaches is
        // one an operator is told to serve over TLS.
        return services.AddResponseCompression(options => options.EnableForHttps = true);
    }

    /// <summary>Puts the compression in front of the routes this surface serves, and in front of nothing else.</summary>
    /// <param name="app">The application pipeline being composed.</param>
    /// <returns>The same application instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app" /> is <see langword="null" />.</exception>
    internal static IApplicationBuilder UseClientResponseCompression(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseWhen(
            static context => ServedByThisSurface(context.Request.Path),
            compressed => compressed.UseResponseCompression());
    }

    /// <summary>Whether a path is one whose response this compresses.</summary>
    /// <param name="path">The path the request asked for.</param>
    /// <returns><see langword="true" /> where the path is a client route other than the signal hub.</returns>
    /// <remarks>
    /// Matched by segment, so a path such as <c>/api/clients</c> is not mistaken for one of these, which is how the
    /// isolation middleware matches the same prefix.
    /// <para>
    /// The signal hub is excluded rather than left to the media types: what travels there is a connection rather than a
    /// response, and a transport SignalR negotiates down to — long polling answers <c>text/plain</c>, which is in the
    /// compressed set — would have each poll buffered for compression instead of delivered as it is written. Nothing
    /// is lost by leaving it out, a signal being a few dozen bytes. The route that mints the hub's tickets sits under
    /// the same prefix and is excluded with it, which is the right side to err on: it is the one answer on this surface
    /// that is itself a credential, and it is two hundred bytes.
    /// </para>
    /// </remarks>
    internal static bool ServedByThisSurface(PathString path) =>
        path.StartsWithSegments(ClientEndpointOptions.RoutePrefix)
        && !path.StartsWithSegments(ClientSignalEndpoints.HubPath);
}
