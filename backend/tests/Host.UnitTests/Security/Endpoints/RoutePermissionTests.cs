// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Endpoints;

/// <summary>Covers what a route may decide, which is refused while the routes are mapped rather than at a request.</summary>
/// <remarks>
/// A route bounded by nothing is a defect in this repository rather than in anything an operator wrote, and it produces
/// a route no credential could ever reach. Startup is therefore where it is found: a deployment that would answer nobody
/// on one of its routes should not start at all. Which surface a permission belongs to is the group's question rather
/// than the route's, and <see cref="RouteAuthorizationTests" /> covers it where the group's own half is known.
/// </remarks>
public sealed class RoutePermissionTests
{
    [Theory]
    [InlineData(nameof(MailFathomPermission.AdminOperate))]
    [InlineData(nameof(MailFathomPermission.MailRead))]
    public void Requiring_APublishedPermission_CarriesIt(string permissionName)
    {
        // Arrange
        var permission = permissionName == nameof(MailFathomPermission.AdminOperate)
            ? MailFathomPermission.AdminOperate
            : MailFathomPermission.MailRead;

        // Act
        var published = RoutePermission.Requiring(permission);

        // Assert
        Assert.Equal(permission, published.Permission);
    }

    /// <summary>The struct default names nothing, so a route published under it would be a route bounded by nothing.</summary>
    [Fact]
    public void Requiring_TheUnspecifiedDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => RoutePermission.Requiring(default));
    }

    /// <summary>The route that requires none says so, rather than being a route whose decision is missing.</summary>
    [Fact]
    public void None_Always_NamesNoPermission()
    {
        // Act, Assert
        Assert.False(RoutePermission.None.Permission.IsSpecified);
    }
}
