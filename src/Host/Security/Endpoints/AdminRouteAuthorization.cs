// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Serves each administrative route only to a caller whose grant carries the permission that route is published under.</summary>
/// <remarks>
/// <para>
/// Two halves, and they belong together because one is meaningless without the other.
/// <see cref="RequirePermission" /> is how a route states its decision, next to the mapping that creates it, and
/// <see cref="RefuseUnpermittedAsync" /> is the one filter the whole group carries, which reads that decision back per
/// request. A route mapped into the group without stating anything is refused rather than served, so a route added
/// later fails closed instead of inheriting whatever the group happened to allow.
/// </para>
/// <para>
/// The refusal names the one permission that would have sufficed, and nothing else: no route inventory, no other
/// credential, and nothing about how this deployment is configured. That is the opposite of what the MCP surface tells a
/// refused caller, and ADR 0012 records why — the caller here is <c>mfctl</c> in the operator's own hands, so a refusal
/// they can act on is worth more than a refusal that discloses nothing.
/// </para>
/// <para>
/// This refuses cheaply and is not the authority. The use case behind each route asks for the same permission on its
/// own, with the transport absent, so an entrypoint added later cannot widen the surface by forgetting a filter. Where
/// the two disagree the use case wins: a refusal it raises is answered here in the same shape rather than reaching the
/// caller as a fault in the deployment.
/// </para>
/// </remarks>
internal static class AdminRouteAuthorization
{
    /// <summary>The member of the problem document that carries the permission on its own, beside the sentence.</summary>
    /// <remarks>
    /// Written for <c>mfctl</c>, which turns a refusal into what an operator has to grant. Reading the name back out of
    /// the sentence would make the wording a contract, and the sentence is written for a person.
    /// </remarks>
    internal const string PermissionExtension = "permission";

    /// <summary>The one name a refusal on an endpoint carrying no route pattern is recorded under.</summary>
    internal const string UnroutedOperationName = "(unrouted)";

    /// <summary>States the one permission a route is published under.</summary>
    /// <param name="route">The route being mapped.</param>
    /// <param name="permission">The capability a caller must hold to reach it.</param>
    /// <returns>The route, so a mapping reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the permission names nothing published, or belongs to another surface.</exception>
    internal static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder route,
        MailFathomPermission permission)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.WithMetadata(AdminRoutePermission.Requiring(permission));
    }

    /// <summary>States that a route requires no permission, which on this surface is the session read and nothing else.</summary>
    /// <param name="route">The route being mapped.</param>
    /// <returns>The route, so a mapping reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route" /> is <see langword="null" />.</exception>
    /// <remarks>Written rather than left out, because leaving it out is what a route whose grant nobody decided looks like, and that is refused.</remarks>
    internal static RouteHandlerBuilder RequireNoPermission(this RouteHandlerBuilder route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.WithMetadata(AdminRoutePermission.None);
    }

    /// <summary>Refuses a request whose caller does not hold what the route it reached is published under.</summary>
    /// <param name="context">The request being served.</param>
    /// <param name="next">The rest of the route's pipeline.</param>
    /// <returns>What the route answered, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> or <paramref name="next" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// It runs as one filter over the whole group rather than as a filter per route, so the enforcement cannot be the
    /// half of the arrangement a new route forgets — what a route supplies is the decision, and this reads it. The
    /// grant is asked of <see cref="AccessAuthorization" /> rather than of the claims on the request, so the transport
    /// and the use case behind it cannot come to disagree about what holding a permission means.
    /// </para>
    /// <para>
    /// A route is served only where it published exactly one decision. Reading the last of several would make a route
    /// that decided twice take whichever declaration came last — which may be the one requiring nothing — so a route
    /// deciding twice is refused for the same reason as one that decided not at all: nobody can say what reaching it
    /// requires, and that is a defect in this repository rather than something a grant could resolve.
    /// </para>
    /// </remarks>
    internal static async ValueTask<object?> RefuseUnpermittedAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<AdminRoutePermission>() is not [{ } published])
        {
            RecordRefusal(context.HttpContext, default);

            return Undeclared();
        }

        if (published.Permission.IsSpecified
            && !context.HttpContext.RequestServices.GetRequiredService<AccessAuthorization>().Permits(published.Permission))
        {
            RecordRefusal(context.HttpContext, published.Permission);

            return Refused(published.Permission);
        }

        try
        {
            return await next(context);
        }
        catch (PrincipalNotAuthorizedException refusal)
        {
            RecordRefusal(context.HttpContext, refusal.RequiredPermission);

            return Refused(refusal.RequiredPermission);
        }
    }

    /// <summary>Records the refusal beside the answer the caller receives, which is what makes a rate of them readable.</summary>
    /// <remarks>
    /// <para>
    /// Recorded once per refused request, wherever the refusal was decided. The filter refuses before the use case is
    /// reached, so a request stopped here never produces a second record from the authority behind it, and a use case
    /// refusing a request the filter admitted is recorded here because that is where the refusal becomes an answer.
    /// </para>
    /// <para>
    /// The operation is the route's own pattern rather than the address the caller sent: a pattern is written in this
    /// repository and bounded by it, while a path carries whatever the request put in each segment. A route that
    /// published no single decision is recorded under a permission of none, because none would have helped — the remedy
    /// is a defect report rather than a wider grant, and a refusal nobody counted is the one nobody finds.
    /// </para>
    /// </remarks>
    private static void RecordRefusal(HttpContext context, MailFathomPermission requiredPermission) =>
        context.RequestServices.GetRequiredService<IAuthorizationRefusalTelemetry>().RecordRefusal(
            ProtectedSurface.Administration,
            OperationOf(context),
            requiredPermission,
            context.RequestServices.GetRequiredService<AccessAuthorization>().PrincipalIdentity);

    /// <summary>Names the route that was refused, as this repository wrote it.</summary>
    /// <remarks>
    /// An endpoint that is not a routed one carries no pattern to name, which no mapped administrative route is; it is
    /// recorded under a fixed name rather than left out, so the refusal is still counted and still says which
    /// deployment produced it.
    /// </remarks>
    private static string OperationOf(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } pattern }
            ? pattern
            : UnroutedOperationName;

    /// <summary>Writes the refusal a caller reads, which names the permission and nothing beside it.</summary>
    /// <remarks>
    /// A refusal that named no permission would leave an operator with nothing to grant, so the one case that produces
    /// one — a use case refusing over the kind of principal that reached it rather than over a grant — says that instead
    /// of naming something that would not have helped, and carries no <see cref="PermissionExtension" /> member either.
    /// </remarks>
    private static ProblemHttpResult Refused(MailFathomPermission required) => TypedResults.Problem(
        required.IsSpecified
            ? $"The credential is not granted '{required.Name}'."
            : "The credential was not admitted to this operation.",
        statusCode: StatusCodes.Status403Forbidden,
        extensions: required.IsSpecified
            ? new Dictionary<string, object?>(StringComparer.Ordinal) { [PermissionExtension] = required.Name }
            : null);

    /// <summary>Writes the answer a route that published no single decision is refused with.</summary>
    /// <remarks>
    /// It names no permission, because there is none to name: either nobody decided what reaching this route requires or
    /// two decisions were published and neither is the route's, so no grant an operator could write would make it
    /// reachable. Refusing rather than serving is what makes such a route one that answers nobody instead of one that
    /// answers everybody, and it is stated plainly because the remedy is a defect report rather than a wider grant.
    /// </remarks>
    private static ProblemHttpResult Undeclared() => TypedResults.Problem(
        "This deployment publishes no permission for that operation, so no credential reaches it.",
        statusCode: StatusCodes.Status403Forbidden);
}
