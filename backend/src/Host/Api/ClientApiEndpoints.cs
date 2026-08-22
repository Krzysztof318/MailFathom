// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Versioning;

namespace MailFathom.Host.Api;

/// <summary>Maps the routes the MailFathom client reaches.</summary>
/// <remarks>
/// <para>
/// One route, and deliberately one. A transport surface is where a misordered middleware, a scheme that authenticates
/// the wrong caller, or a listener bound where it should not be fails silently and arrives as a working deployment
/// answering the wrong way, so the surface is published with nothing on it but proof of life: the decisions can then be
/// reviewed and tested for what they are rather than underneath six mail-reading routes. The routes the client actually
/// needs are a separate change on top of this one.
/// </para>
/// <para>
/// The route answers what a client needs before it has drawn a single message: that this is MailFathom rather than
/// something else answering the port, which version it is, and what the credential the client just presented is allowed
/// to do. That last part is what lets sign-in be built and proven end to end before a screen exists — a client that
/// reached here with a token it had just been issued knows the token works, and knows what the rest of the surface will
/// serve it.
/// </para>
/// <para>
/// Every route is mapped into one group so the requirement the endpoint attaches covers everything the surface serves,
/// including a route added later.
/// </para>
/// </remarks>
internal static class ClientApiEndpoints
{
    /// <summary>The route reporting what the deployment grants the caller, relative to the client prefix.</summary>
    /// <remarks>It is what a client reads first, and the only thing this surface answers today.</remarks>
    internal const string SessionRoute = "/session";

    /// <summary>Maps the client routes beneath the endpoint's route prefix.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The mapped group, so the caller can attach the requirement the endpoint carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteGroupBuilder MapClientApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapGroup(ClientEndpointOptions.RoutePrefix);

        api.MapGet(SessionRoute, (IAuthorizedPrincipalSource principals) =>
            Results.Ok(ClientSessionResponse.For(principals.Current)));

        return api;
    }
}

/// <summary>What the client endpoint reports back about an authenticated caller.</summary>
/// <param name="Service">The product this is, so a client can tell it reached MailFathom rather than something else answering the port.</param>
/// <param name="Version">The running version, which is what tells a client which contract it is talking to.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, in the order this repository publishes them, and empty for a credential granted nothing.</param>
/// <remarks>
/// <para>
/// It names no credential, which is the one way it differs from what the administrative surface answers. That surface's
/// reader is <c>mfctl</c> in an operator's own hands, and the deployment's configured name for the credential that
/// authenticated is what tells them which of their own entries let them in. This surface's reader is a page holding a
/// token, which brought no name and has nothing to do with one — and a response echoing a deployment's own configured
/// identity for a credential would be a way to read configuration back out of the service from a browser.
/// </para>
/// <para>
/// The grant is reported because it is the caller asking what it may do, which is the one question a caller may always
/// ask about itself. A request that established no principal reports an empty grant rather than failing, which is the
/// accurate answer to what such a caller may do.
/// </para>
/// </remarks>
internal sealed record ClientSessionResponse(
    string Service,
    string Version,
    IReadOnlyList<string> Permissions)
{
    /// <summary>Describes what the credential that reached this route was granted.</summary>
    /// <param name="principal">What the application layer was told admitted this request, or nothing where the transport established none.</param>
    /// <returns>The response body.</returns>
    internal static ClientSessionResponse For(AuthorizedPrincipal? principal) => new(
        "MailFathom",
        StampedAssemblyVersion.ReadFrom(typeof(ClientSessionResponse).Assembly).Version,
        GrantOf(principal));

    /// <summary>Names what the caller holds, in the order this repository publishes the set.</summary>
    /// <remarks>The published order rather than the grant's own, so two credentials granted the same permissions are reported identically whichever order an operator wrote them in.</remarks>
    private static IReadOnlyList<string> GrantOf(AuthorizedPrincipal? principal) => principal is null
        ? []
        : [.. MailFathomPermission.All.Where(principal.Holds).Select(permission => permission.Name)];
}
