// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Security.Endpoints;

/// <summary>What one administrative route decided a caller must hold before it is served.</summary>
/// <remarks>
/// <para>
/// The decision travels as endpoint metadata so that it is written beside the route it governs rather than in a list a
/// route added later can be left out of. What makes the omission safe is that the absence of this metadata is itself an
/// answer: <see cref="AdminRouteAuthorization" /> refuses a route carrying none, so forgetting to decide is a route
/// nobody reaches instead of a route everybody does.
/// </para>
/// <para>
/// <see cref="None" /> is the one route that requires no permission, and it is a stated value rather than the absence of
/// one for exactly that reason. ADR 0012 allocates it to the session read alone: that route reports the credential the
/// caller already presented and the version the deployment already publishes, and putting it behind a permission would
/// make that permission a component of every administrative grant.
/// </para>
/// </remarks>
internal sealed class AdminRoutePermission
{
    private AdminRoutePermission(MailFathomPermission permission) => this.Permission = permission;

    /// <summary>Gets the decision of a route that requires no permission at all.</summary>
    internal static AdminRoutePermission None { get; } = new(default);

    /// <summary>Gets the permission a caller must hold, unspecified where the route requires none.</summary>
    internal MailFathomPermission Permission { get; }

    /// <summary>States the one permission a route is published under.</summary>
    /// <param name="permission">The capability the route is reached with.</param>
    /// <returns>The metadata the route carries.</returns>
    /// <exception cref="ArgumentException">Thrown when the value names no published permission, or names one belonging to another surface.</exception>
    /// <remarks>
    /// Both refusals happen while the routes are mapped, which is startup, because either is a defect in this repository
    /// rather than in anything an operator wrote: a route bounded by a permission an administrative grant can never carry
    /// is a route no credential reaches, and one bounded by the struct default is a route bounded by nothing.
    /// </remarks>
    internal static AdminRoutePermission Requiring(MailFathomPermission permission)
    {
        if (!permission.IsSpecified)
        {
            throw new ArgumentException(
                "An administrative route must be published under a permission rather than the unspecified default.",
                nameof(permission));
        }

        if (permission.Surface != ProtectedSurface.Administration)
        {
            throw new ArgumentException(
                $"'{permission.Name}' belongs to another surface, so no administrative grant can carry it.",
                nameof(permission));
        }

        return new AdminRoutePermission(permission);
    }
}
