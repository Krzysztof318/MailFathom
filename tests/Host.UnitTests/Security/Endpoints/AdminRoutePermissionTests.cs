// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers what a route may decide, which is refused while the routes are mapped rather than at a request.</summary>
/// <remarks>
/// Both refusals are defects in this repository rather than in anything an operator wrote, and both produce a route no
/// credential could ever reach. Startup is therefore where they are found: a deployment that would answer nobody on one
/// of its routes should not start at all.
/// </remarks>
public sealed class AdminRoutePermissionTests
{
    [Fact]
    public void Requiring_APublishedAdministrativePermission_CarriesIt()
    {
        // Act
        var published = AdminRoutePermission.Requiring(MailFathomPermission.AdminOperate);

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, published.Permission);
    }

    /// <summary>The struct default names nothing, so a route published under it would be a route bounded by nothing.</summary>
    [Fact]
    public void Requiring_TheUnspecifiedDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => AdminRoutePermission.Requiring(default));
    }

    /// <summary>A name from the other surface is one no administrative grant can carry, so the route would answer nobody.</summary>
    [Fact]
    public void Requiring_APermissionOfTheMailSurface_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => AdminRoutePermission.Requiring(MailFathomPermission.MailRead));
    }

    /// <summary>The route that requires none says so, rather than being a route whose decision is missing.</summary>
    [Fact]
    public void None_Always_NamesNoPermission()
    {
        // Act, Assert
        Assert.False(AdminRoutePermission.None.Permission.IsSpecified);
    }
}
