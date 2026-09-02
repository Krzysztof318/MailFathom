// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>Serves each HTTP route only to a caller whose grant carries the permission that route is published under.</summary>
/// <remarks>
/// <para>
/// Two halves, and they belong together because one is meaningless without the other.
/// <see cref="RequirePermission" /> is how a route states its decision, next to the mapping that creates it, and
/// <see cref="RefusingUnpermitted" /> is the one filter a whole group carries, which reads that decision back per
/// request. A route mapped into the group without stating anything is refused rather than served, so a route added
/// later fails closed instead of inheriting whatever the group happened to allow.
/// </para>
/// <para>
/// One mechanism serves both HTTP surfaces, and what it takes from each is which half of the published set that
/// surface's grants are drawn from. A route bounded by a permission the other half owns is refused for every caller,
/// exactly as a route that decided nothing is: no grant an operator could write on that surface carries the name, so
/// the route reaches nobody either way and the remedy is a defect report rather than a wider grant.
/// </para>
/// <para>
/// The refusal names the one permission that would have sufficed, and nothing else: no route inventory, no other
/// credential, and nothing about how this deployment is configured. That is the opposite of what the MCP surface tells
/// a refused caller, and ADR 0012 records why for the administrative one — the caller there is <c>mfctl</c> in the
/// operator's own hands, so a refusal they can act on is worth more than a refusal that discloses nothing. The client
/// surface answers the same way for a reason of its own: its caller is a page holding this person's own credential, and
/// its session route already answers that same caller with the whole of its own grant, so naming what is missing from
/// that list discloses nothing the caller cannot already read about itself.
/// </para>
/// <para>
/// This refuses cheaply and is not the authority. The use case behind each route asks for the same permission on its
/// own, with the transport absent, so an entrypoint added later cannot widen the surface by forgetting a filter. Where
/// the two disagree the use case wins: a refusal it raises is answered here in the same shape rather than reaching the
/// caller as a fault in the deployment.
/// </para>
/// </remarks>
internal static class RouteAuthorization
{
    /// <summary>The member of the problem document that carries the permission on its own, beside the sentence.</summary>
    /// <remarks>
    /// Written for the caller to act on — <c>mfctl</c> turns it into what an operator has to grant, and a client turns
    /// it into whether to ask for a new token. Reading the name back out of the sentence would make the wording a
    /// contract, and the sentence is written for a person.
    /// </remarks>
    internal const string PermissionExtension = "permission";

    /// <summary>The member of the problem document that carries the failure's own code, beside the sentence.</summary>
    /// <remarks>
    /// Written for the same reason the permission above is: a caller matches a code it can act on, and reading the
    /// failure back out of a sentence written for a person would make the wording a contract.
    /// </remarks>
    internal const string ErrorCodeExtension = "errorCode";

    /// <summary>The one name a refusal on an endpoint carrying no route pattern is recorded under.</summary>
    internal const string UnroutedOperationName = "(unrouted)";

    /// <summary>States the one permission a route is published under.</summary>
    /// <param name="route">The route being mapped.</param>
    /// <param name="permission">The capability a caller must hold to reach it.</param>
    /// <returns>The route, so a mapping reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the permission names nothing published.</exception>
    internal static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder route,
        MailFathomPermission permission)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.WithMetadata(RoutePermission.Requiring(permission));
    }

    /// <summary>States that a route requires no permission, which on each surface is its session read and nothing else.</summary>
    /// <param name="route">The route being mapped.</param>
    /// <returns>The route, so a mapping reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route" /> is <see langword="null" />.</exception>
    /// <remarks>Written rather than left out, because leaving it out is what a route whose grant nobody decided looks like, and that is refused.</remarks>
    internal static RouteHandlerBuilder RequireNoPermission(this RouteHandlerBuilder route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return route.WithMetadata(RoutePermission.None);
    }

    /// <summary>Builds the filter a route group carries, bound to the half of the published set that group's grants come from.</summary>
    /// <param name="surface">The surface the group is served on.</param>
    /// <returns>The filter, which refuses a request whose caller does not hold what the route it reached is published under.</returns>
    /// <remarks>
    /// The surface belongs to the group rather than to a route, so it is stated once where the group is composed. It
    /// decides two things: which permissions a route on this group may legitimately be published under, and which
    /// surface a refusal is recorded against.
    /// </remarks>
    internal static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RefusingUnpermitted(
        ProtectedSurface surface) =>
        (context, next) => RefuseUnpermittedAsync(context, next, surface);

    /// <summary>Refuses a request whose caller does not hold what the route it reached is published under.</summary>
    /// <param name="context">The request being served.</param>
    /// <param name="next">The rest of the route's pipeline.</param>
    /// <param name="surface">The surface the route is served on, which bounds the permissions it may be published under.</param>
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
    /// A route is served only where it published exactly one decision, and where that decision belongs to this surface.
    /// Reading the last of several would make a route that decided twice take whichever declaration came last — which
    /// may be the one requiring nothing — so a route deciding twice is refused for the same reason as one that decided
    /// not at all: nobody can say what reaching it requires. A route publishing the other surface's permission is
    /// refused beside them, because no credential this surface admits can carry that name.
    /// </para>
    /// </remarks>
    internal static async ValueTask<object?> RefuseUnpermittedAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        ProtectedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<RoutePermission>() is not [{ } published]
            || (published.Permission.IsSpecified && published.Permission.Surface != surface))
        {
            RecordRefusal(context.HttpContext, surface, default);

            return Undeclared();
        }

        if (published.Permission.IsSpecified
            && !context.HttpContext.RequestServices.GetRequiredService<AccessAuthorization>().Permits(published.Permission))
        {
            RecordRefusal(context.HttpContext, surface, published.Permission);

            return Refused(published.Permission);
        }

        try
        {
            return await next(context);
        }
        catch (PrincipalNotAuthorizedException refusal)
        {
            RecordRefusal(context.HttpContext, surface, refusal.RequiredPermission);

            return Refused(refusal.RequiredPermission);
        }
        catch (DeploymentMailOwnerUnresolvedException refusal)
        {
            return Unattributable(refusal);
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
    private static void RecordRefusal(
        HttpContext context,
        ProtectedSurface surface,
        MailFathomPermission requiredPermission) =>
        context.RequestServices.GetRequiredService<IAuthorizationRefusalTelemetry>().RecordRefusal(
            surface,
            OperationOf(context),
            requiredPermission,
            context.RequestServices.GetRequiredService<AccessAuthorization>().PrincipalIdentity);

    /// <summary>Names the route that was refused, as this repository wrote it.</summary>
    /// <remarks>
    /// An endpoint that is not a routed one carries no pattern to name, which no mapped route on either surface is; it
    /// is recorded under a fixed name rather than left out, so the refusal is still counted and still says which
    /// deployment produced it.
    /// </remarks>
    private static string OperationOf(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } pattern }
            ? pattern
            : UnroutedOperationName;

    /// <summary>Writes the refusal a caller reads, which names the permission and nothing beside it.</summary>
    /// <remarks>
    /// A refusal that named no permission would leave a caller with nothing to act on, so the one case that produces
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

    /// <summary>Writes the answer an act reached by a credential naming no owner receives where the deployment serves several.</summary>
    /// <remarks>
    /// <para>
    /// Answered here rather than route by route for the reason the permission is: which acts resolve the owner a
    /// caller acts for is a property of the use cases behind the group rather than of any one mapping, so a route
    /// added later that resolves one cannot forget to answer this. It is the second failure this filter turns into an
    /// answer, and both are the same shape of thing — a use case refusing a request the filter admitted, over who the
    /// caller is rather than over what the request said.
    /// </para>
    /// <para>
    /// The status is a conflict rather than a refusal, because no grant would have helped and nothing about the
    /// request is wrong: the deployment holds a roster this act has no single answer over. The message is the
    /// failure's own, which is written to be read by an operator, and the code travels beside it so a caller matches
    /// the failure rather than parsing the sentence.
    /// </para>
    /// <para>
    /// It is reachable from outside this filter for the one route mapped outside every group — the attachment
    /// download, whose capability is a signed ticket rather than a credential and which therefore resolves the owner
    /// itself. Composing that answer here rather than there is what keeps the two the same status, the same message,
    /// and the same code.
    /// </para>
    /// </remarks>
    internal static ProblemHttpResult Unattributable(DeploymentMailOwnerUnresolvedException refusal) =>
        TypedResults.Problem(
            refusal.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ErrorCodeExtension] = refusal.ErrorCode.Value,
            });

    /// <summary>Writes the answer a route that published no single decision this surface can carry is refused with.</summary>
    /// <remarks>
    /// It names no permission, because there is none to name: nobody decided what reaching this route requires, two
    /// decisions were published and neither is the route's, or the one that was published belongs to the other surface.
    /// In each case no grant an operator could write would make it reachable. Refusing rather than serving is what makes
    /// such a route one that answers nobody instead of one that answers everybody, and it is stated plainly because the
    /// remedy is a defect report rather than a wider grant.
    /// </remarks>
    private static ProblemHttpResult Undeclared() => TypedResults.Problem(
        "This deployment publishes no permission for that operation, so no credential reaches it.",
        statusCode: StatusCodes.Status403Forbidden);
}
