// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers how a resolved grant travels on the principal the transport policy judges.</summary>
/// <remarks>
/// The grant is decided while the host is composed and read again on every request, so what is asserted here is the
/// round trip: what a scheme writes is exactly what a surface enforcing the grant later reads, and nothing else is.
/// </remarks>
public sealed class TransportGrantTests
{
    [Fact]
    public void PermissionsCarriedBy_APrincipalCarryingWhatClaimsForWrote_ReportsTheSameGrant()
    {
        // Arrange
        MailFathomPermission[] granted = [MailFathomPermission.MailRead, MailFathomPermission.MailAsk];
        var principal = PrincipalCarrying(TransportGrant.ClaimsFor(granted));

        // Act
        var permissions = TransportGrant.PermissionsCarriedBy(principal);

        // Assert
        Assert.Equal(granted.ToHashSet(), permissions);
    }

    /// <summary>An entry that granted nothing produces a principal holding nothing, which is the posture an emptied grant states.</summary>
    [Fact]
    public void PermissionsCarriedBy_APrincipalCarryingAnEmptyGrant_ReportsNothing()
    {
        // Arrange
        var principal = PrincipalCarrying(TransportGrant.ClaimsFor([]));

        // Act, Assert
        Assert.Empty(TransportGrant.PermissionsCarriedBy(principal));
    }

    /// <summary>The claim carries the published name, because that is the identity an operator wrote and an authorization server minted.</summary>
    [Fact]
    public void ClaimsFor_AGrant_WritesOneClaimPerPublishedName()
    {
        // Act
        var claims = TransportGrant.ClaimsFor([MailFathomPermission.AdminSpend]);

        // Assert
        var claim = Assert.Single(claims);
        Assert.Equal(TransportGrant.PermissionClaimType, claim.Type);
        Assert.Equal("mailfathom.admin.spend", claim.Value);
    }

    /// <summary>
    /// Reading a name nothing publishes as a permission is the one outcome that would grant something, so a claim
    /// naming a value a later release retired is dropped rather than reported.
    /// </summary>
    [Fact]
    public void PermissionsCarriedBy_AClaimNamingNoPublishedPermission_IsDropped()
    {
        // Arrange
        var principal = PrincipalCarrying(
            [
                new Claim(TransportGrant.PermissionClaimType, "mailfathom.mail.write"),
                new Claim(TransportGrant.PermissionClaimType, MailFathomPermission.MailRead.Name),
            ]);

        // Act
        var permissions = TransportGrant.PermissionsCarriedBy(principal);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], permissions);
    }

    private static ClaimsPrincipal PrincipalCarrying(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
