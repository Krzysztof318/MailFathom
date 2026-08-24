// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>What one route decided a caller must hold before it is served.</summary>
/// <remarks>
/// <para>
/// The decision travels as endpoint metadata so that it is written beside the route it governs rather than in a list a
/// route added later can be left out of. What makes the omission safe is that the absence of this metadata is itself an
/// answer: <see cref="RouteAuthorization" /> refuses a route carrying none, so forgetting to decide is a route nobody
/// reaches instead of a route everybody does.
/// </para>
/// <para>
/// <see cref="None" /> is the route that requires no permission, and it is a stated value rather than the absence of one
/// for exactly that reason. Each HTTP surface allocates it to its own session read and nowhere else: that route reports
/// the credential the caller already presented and the version the deployment already publishes, and putting it behind a
/// permission would make that permission a component of every grant on the surface.
/// </para>
/// <para>
/// Which surface a route is served on is not recorded here, because a route does not choose it — the group it is mapped
/// into does, and that is where <see cref="RouteAuthorization" /> reads it from. What this carries is the one thing the
/// route itself decided.
/// </para>
/// </remarks>
internal sealed class RoutePermission
{
    private RoutePermission(MailFathomPermission permission) => this.Permission = permission;

    /// <summary>Gets the decision of a route that requires no permission at all.</summary>
    internal static RoutePermission None { get; } = new(default);

    /// <summary>Gets the permission a caller must hold, unspecified where the route requires none.</summary>
    internal MailFathomPermission Permission { get; }

    /// <summary>States the one permission a route is published under.</summary>
    /// <param name="permission">The capability the route is reached with.</param>
    /// <returns>The metadata the route carries.</returns>
    /// <exception cref="ArgumentException">Thrown when the value names nothing published.</exception>
    /// <remarks>
    /// The refusal happens while the routes are mapped, which is startup, because it is a defect in this repository
    /// rather than in anything an operator wrote: a route bounded by the struct default is a route bounded by nothing
    /// while looking as though somebody decided. A permission belonging to a surface other than the one the route is
    /// served on is the same kind of defect and is refused by <see cref="RouteAuthorization" />, which is the half that
    /// knows which surface that is.
    /// </remarks>
    internal static RoutePermission Requiring(MailFathomPermission permission)
    {
        if (!permission.IsSpecified)
        {
            throw new ArgumentException(
                "A route must be published under a permission rather than the unspecified default.",
                nameof(permission));
        }

        return new RoutePermission(permission);
    }
}
