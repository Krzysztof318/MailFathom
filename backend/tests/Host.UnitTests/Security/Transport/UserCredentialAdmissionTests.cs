// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Grants;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Covers what admits a credential a method already resolved, which every user-facing method asks before it builds an identity.</summary>
public sealed class UserCredentialAdmissionTests
{
    private static readonly UserId User = UserId.Create(Guid.Parse("0f4c1a7e-3b2d-4e5f-8a9b-1c2d3e4f5a6b"));

    private static readonly IPAddress Office = IPAddress.Parse("10.20.30.40");

    [Fact]
    public async Task FindRefusalAsync_ADefaultCredentialOnTheMcpEndpoint_IsAdmitted()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.None);

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Mcp, Credential());

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>The administrative endpoint is never in the default, so a credential nobody wrote it onto stays a mail credential even for an administrator.</summary>
    [Fact]
    public async Task FindRefusalAsync_ADefaultCredentialOfAnAdministratorOnTheAdministrativeEndpoint_IsRefused()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.AtDeployment([MailFathomPermission.AdminRead]));

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Admin, Credential());

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task FindRefusalAsync_ACredentialListingOnlyTheAdministrativeEndpoint_IsRefusedOnTheMcpEndpoint()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.None);
        var credential = Credential(new UserCredentialReach([UserCredentialSurface.Administration], []));

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Mcp, credential);

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task FindRefusalAsync_AnAdministrativeCredentialOfAUserHoldingAnAdministrativePermission_IsAdmitted()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.AtDeployment([MailFathomPermission.AdminRead]));

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Admin, AdministrativeCredential());

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>The credential is the way in and the assignment is what there is to come in for, so a user granted only mail is no administrator whatever their credential lists.</summary>
    [Fact]
    public async Task FindRefusalAsync_AnAdministrativeCredentialOfAUserHoldingOnlyMailPermissions_IsRefused()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.AtDeployment([MailFathomPermission.MailRead]));

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Admin, AdministrativeCredential());

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task FindRefusalAsync_ACredentialRestrictedToANetworkTheRequestCameFrom_IsAdmitted()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.None);
        var credential = Credential(UserCredentialReach.Default with { AllowedSourceNetworks = [IPNetwork.Parse("10.20.0.0/16")] });

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Mcp, credential);

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public async Task FindRefusalAsync_ACredentialRestrictedToAnotherNetwork_IsRefused()
    {
        // Arrange
        var context = RequestFrom(IPAddress.Parse("192.0.2.7"), ScopedGrant.None);
        var credential = Credential(UserCredentialReach.Default with { AllowedSourceNetworks = [IPNetwork.Parse("10.20.0.0/16")] });

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Mcp, credential);

        // Assert
        Assert.NotNull(refusal);
    }

    /// <summary>A dual-stack listener reports an IPv4 client mapped into IPv6, and the restriction has to read through that.</summary>
    [Fact]
    public async Task FindRefusalAsync_AnIpv4ClientArrivingMappedIntoIpv6_IsComparedAsIpv4()
    {
        // Arrange
        var context = RequestFrom(Office.MapToIPv6(), ScopedGrant.None);
        var credential = Credential(UserCredentialReach.Default with { AllowedSourceNetworks = [IPNetwork.Parse("10.20.0.0/16")] });

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Mcp, credential);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>A narrowing is the operator's statement of what one credential may do, so the most privileged surface honours it rather than handing the user's whole grant through.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindRefusalAsync_AnAdministrativeCredentialNarrowedToNoAdministrativeName_IsRefusedHoweverMuchItsUserHolds(bool keepsAMailName)
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.AtDeployment(MailFathomPermission.All));
        IReadOnlyList<MailFathomPermission> narrowing = keepsAMailName ? [MailFathomPermission.MailRead] : [];

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(context, TransportSurface.Admin, AdministrativeCredential(narrowing));

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task FindRefusalAsync_AnAdministrativeCredentialNarrowedToANameItsUserHolds_IsAdmitted()
    {
        // Arrange
        var context = RequestFrom(Office, ScopedGrant.AtDeployment(MailFathomPermission.All));

        // Act
        var refusal = await UserCredentialAdmission.FindRefusalAsync(
            context,
            TransportSurface.Admin,
            AdministrativeCredential([MailFathomPermission.AdminRead]));

        // Assert
        Assert.Null(refusal);
    }

    private static AdmittedUserCredential Credential(UserCredentialReach? reach = null) =>
        Credential(reach ?? UserCredentialReach.Default, [MailFathomPermission.MailRead]);

    private static AdmittedUserCredential Credential(UserCredentialReach reach, IReadOnlyList<MailFathomPermission> narrowing) =>
        new(Guid.Parse("7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"), User, narrowing, UserEndpointAccess.Everywhere)
        {
            Reach = reach,
        };

    private static AdmittedUserCredential AdministrativeCredential(IReadOnlyList<MailFathomPermission>? narrowing = null) =>
        Credential(new UserCredentialReach([UserCredentialSurface.Administration], []), narrowing ?? MailFathomPermission.All);

    private static DefaultHttpContext RequestFrom(IPAddress source, ScopedGrant grant)
    {
        var store = Substitute.For<IGrantStore>();
        store.ReadGrantOfAsync(User, Arg.Any<CancellationToken>()).Returns(grant);

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(new UserGrantResolver(store, new UserGrantCache()))
                .BuildServiceProvider(),
        };

        context.Connection.RemoteIpAddress = source;

        return context;
    }
}
