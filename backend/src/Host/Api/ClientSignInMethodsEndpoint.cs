// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;

namespace MailFathom.Host.Api;

/// <summary>Publishes what a sign-in screen may draw, to a browser that is holding nothing yet.</summary>
/// <remarks>
/// <para>
/// A client reaching a deployment for the first time can already discover a great deal: the session route's challenge
/// says whether a password is offered, and the RFC 9728 document beside it names the issuers, the resource, and the
/// scopes. Neither answers what a screen is actually composed from — whether to draw a password form, what words go on
/// a provider's button, and the client identifier an authorization request has to start with — so a client built
/// against those two alone can only offer a password, whatever the deployment runs.
/// </para>
/// <para>
/// Served unauthenticated, which is the only way it can be served and the same reason
/// <see cref="ProtectedResourceMetadataEndpoint" /> is: its reader has nothing to authenticate with, and a document
/// saying where to obtain a credential that itself required one would answer nobody. Nothing in it is a secret —
/// <see cref="PublishedSignInMethod" /> holds why each field is a public name rather than a disclosure.
/// </para>
/// <para>
/// Mapped beneath the client route prefix but outside the group carrying the endpoint's authorization requirement, and
/// under the same CORS policy the rest of the surface answers under: a document a browser is refused permission to read
/// is a client that cannot find out how to sign in, which is the one thing it needed before it could hold any
/// credential at all. Sitting beneath the prefix is also what confines it to the client listeners, since surface
/// isolation reads the path — so a deployment serving the MCP endpoint on a socket of its own answers <c>404</c> there.
/// </para>
/// </remarks>
internal static class ClientSignInMethodsEndpoint
{
    /// <summary>The route the document answers on, relative to the client prefix.</summary>
    internal const string Route = "/sign-in-methods";

    /// <summary>Maps the document, composed once at startup from what the endpoint accepts.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="published">What the endpoint's entries offer a browser, composed once and shared with the policy the page is served under.</param>
    /// <returns>The mapped route, so the caller can attach the CORS policy the surface answers under.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> or <paramref name="published" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The composed answer is passed in rather than read here, because the content security policy the client's page is
    /// served under is composed from the same value: a route that read the configuration a second time would be a
    /// second reading free to offer a server whose origin the page may not call.
    /// </remarks>
    internal static RouteHandlerBuilder MapClientSignInMethods(
        this IEndpointRouteBuilder endpoints,
        PublishedSignInMethods published)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(published);

        var document = new ClientSignInMethodsResponse(
            published.AcceptsPassword,
            [.. published.AuthorizationServers.Select(ClientSignInMethodResponse.For)]);

        // TypedResults rather than Results, so the response type reaches the endpoint's metadata and the generated
        // OpenAPI document describes what this answers with rather than an untyped 200.
        return endpoints.MapGet(ClientEndpointOptions.RoutePrefix + Route, () => TypedResults.Ok(document));
    }
}

/// <summary>What a client may offer somebody signing in to this deployment.</summary>
/// <param name="AcceptsPassword">Whether a user name and a password may be presented, which is what decides the password form is drawn at all.</param>
/// <param name="AuthorizationServers">The servers a person may sign in through, which is what decides every other control on the screen.</param>
/// <remarks>
/// The three postures a sign-in screen has are three readings of this one answer rather than one posture with variants:
/// a deployment's own provider, a third party's, and a password are each drawn only where this says so, and a
/// deployment offering none of them is a screen that says so instead of a form nobody can submit.
/// </remarks>
internal sealed record ClientSignInMethodsResponse(
    bool AcceptsPassword,
    IReadOnlyList<ClientSignInMethodResponse> AuthorizationServers);

/// <summary>One authorization server offered on the sign-in screen.</summary>
/// <param name="Issuer">The issuer identifier, which the client reads the authorize and token endpoints from through the server's own discovery document.</param>
/// <param name="Name">What this deployment calls the server: <c>self</c> for its own identity provider, and otherwise a name the client matches against the provider marks it ships.</param>
/// <param name="DisplayName">The words the control carries.</param>
/// <param name="ClientId">The identifier the authorization request is started with.</param>
internal sealed record ClientSignInMethodResponse(string Issuer, string Name, string DisplayName, string ClientId)
{
    /// <summary>Describes one published server on the wire.</summary>
    /// <param name="server">The server the endpoint publishes.</param>
    /// <returns>The wire shape of it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="server" /> is <see langword="null" />.</exception>
    internal static ClientSignInMethodResponse For(PublishedSignInMethod server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new ClientSignInMethodResponse(server.Issuer, server.Name, server.DisplayName, server.ClientId);
    }
}
