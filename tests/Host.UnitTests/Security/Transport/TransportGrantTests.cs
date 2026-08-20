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

    /// <summary>What a scheme's success produces is the credential's name and its grant, in the shape the surface reads back.</summary>
    [Fact]
    public void IdentityFor_AnAuthenticatedCredential_CarriesItsNameAndItsGrant()
    {
        // Arrange
        MailFathomPermission[] granted = [MailFathomPermission.MailRead, MailFathomPermission.AdminSpend];

        // Act
        var identity = TransportGrant.IdentityFor(
            "operations-key",
            "urn:mailfathom:api-key-name",
            "urn:mailfathom:api-key-role",
            "MailFathom:Mcp:ApiKey",
            granted);

        // Assert
        Assert.Equal("MailFathom:Mcp:ApiKey", identity.AuthenticationType);
        Assert.Equal("operations-key", identity.Name);
        Assert.Equal(granted.ToHashSet(), TransportGrant.PermissionsCarriedBy(new ClaimsPrincipal(identity)));
    }

    /// <summary>
    /// A role claim type nothing issues is what makes a role check answer no. Left unstated the identity reverts to the
    /// framework's default type, and a mapping that wrote one would then be read as a role this system never grants.
    /// </summary>
    [Fact]
    public void IdentityFor_AnAuthenticatedCredential_IsInNoRole()
    {
        // Arrange, Act
        var identity = TransportGrant.IdentityFor(
            "operations-key",
            "urn:mailfathom:client-assertion-key-name",
            "urn:mailfathom:client-assertion-role",
            "MailFathom:Admin:ClientAssertion",
            [MailFathomPermission.MailRead]);

        // Assert
        Assert.Equal("urn:mailfathom:client-assertion-role", identity.RoleClaimType);
        Assert.False(new ClaimsPrincipal(identity).IsInRole(MailFathomPermission.MailRead.Name));
    }

    /// <summary>An entry that granted nothing still authenticates, and the identity it produces holds only the name.</summary>
    [Fact]
    public void IdentityFor_ACredentialGrantedNothing_CarriesItsNameAlone()
    {
        // Arrange, Act
        var identity = TransportGrant.IdentityFor(
            "operations-key",
            "urn:mailfathom:api-key-name",
            "urn:mailfathom:api-key-role",
            "MailFathom:Mcp:ApiKey",
            []);

        // Assert
        var claim = Assert.Single(identity.Claims);
        Assert.Equal("urn:mailfathom:api-key-name", claim.Type);
        Assert.Equal("operations-key", claim.Value);
    }

    private static ClaimsPrincipal PrincipalCarrying(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
