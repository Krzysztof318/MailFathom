// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Grants;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the role, group, and assignment routes answer, and what each refusal names for an administrator to act on.</summary>
public sealed class GrantEndpointsTests
{
    private static readonly Guid RoleId = new("0198f0aa-0000-7000-8000-0000000005a1");

    private static readonly Guid GroupId = new("0198f0aa-0000-7000-8000-0000000005b1");

    private static readonly Guid AssignmentId = new("0198f0aa-0000-7000-8000-0000000005c1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateRoleAsync_ANameNothingPublishes_IsRefusedNamingItWithoutWriting()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await GrantEndpoints.CreateRoleAsync(
            new RoleProvisioningRequest("Readers", ["mailfathom.admin.read", "mailfathom.admin.everything"]),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains("'mailfathom.admin.everything'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task CreateRoleAsync_ATakenName_IsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Grants
            .CreateRoleAsync(Arg.Any<Guid>(), "Readers", Arg.Any<RolePermissions>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.NameTaken));

        // Act
        var result = await GrantEndpoints.CreateRoleAsync(
            new RoleProvisioningRequest("Readers", ["mailfathom.admin.read"]),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A body naming no list must not read as an empty one, which would take every name away from everybody the role is assigned to.</summary>
    [Fact]
    public async Task ReplaceRolePermissionsAsync_ABodyNamingNoList_IsRefusedWithoutWriting()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await GrantEndpoints.ReplaceRolePermissionsAsync(
            RoleId,
            new RolePermissionsRequest(null),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task ReplaceRolePermissionsAsync_DroppingTheLastRoot_IsAConflictNamingTheRoot()
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Grants.ReplaceRolePermissionsAsync(RoleId, Arg.Any<RolePermissions>(), Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.LastRoot));

        // Act
        var result = await GrantEndpoints.ReplaceRolePermissionsAsync(
            RoleId,
            new RolePermissionsRequest([]),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("'mailfathom.admin.roles.write'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteRoleAsync_AStillAssignedRole_IsAConflictNamingHowManyAssignmentsStandInTheWay()
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Grants.DeleteRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new GrantWriteResult(GrantWriteOutcome.StillAssigned, StandingAssignments: 4));

        // Act
        var result = await GrantEndpoints.DeleteRoleAsync(RoleId, harness.Administration, TestContext.Current.CancellationToken);

        // Assert
        var problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.StartsWith("4 role assignment(s)", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A group in no organization is stated rather than left out, because an omitted field must not be what chose the deployment's.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateGroupAsync_ABodyStatingNeitherOrBothOrganizationDecisions_IsRefusedWithoutWriting(bool both)
    {
        // Arrange
        var harness = new EndpointHarness();
        var request = both
            ? new GroupProvisioningRequest("Sales", Guid.NewGuid(), None: true)
            : new GroupProvisioningRequest("Sales", OrganizationId: null, None: null);

        // Act
        var result = await GrantEndpoints.CreateGroupAsync(request, harness.Administration, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task CreateGroupAsync_InNoOrganization_AnswersTheMintedIdentifier()
    {
        // Arrange
        var harness = new EndpointHarness();
        harness.Grants
            .CreateGroupAsync(Arg.Any<Guid>(), "Everyone", null, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await GrantEndpoints.CreateGroupAsync(
            new GroupProvisioningRequest("Everyone", OrganizationId: null, None: true),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(Guid.Empty, Assert.IsType<Ok<GrantRecordedResponse>>(result.Result).Value!.Id);
    }

    [Fact]
    public async Task RenameGroupAsync_AGroupNobodyHolds_IsNotFound()
    {
        // Arrange
        var harness = new EndpointHarness();

        // Act
        var result = await GrantEndpoints.RenameGroupAsync(
            GroupId,
            new GrantRecordNameRequest("Renamed"),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A cursor is a position among one group's members, so the one another group's listing returned is refused rather than followed.</summary>
    [Fact]
    public async Task ListGroupMembersAsync_ACursorAnotherGroupsListingIssued_IsRefusedWithoutReading()
    {
        // Arrange
        var harness = new EndpointHarness();
        var cursor = AdminListingRequest.NextCursor(
            AdministrativeListing.GroupMembers,
            SyntheticUser.Deployment.Value,
            within: new Guid("0198f0aa-0000-7000-8000-0000000005b2"));

        // Act
        var result = await GrantEndpoints.ListGroupMembersAsync(
            GroupId,
            pageSize: null,
            cursor,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    /// <summary>The caller asked for a state and has it, so a membership that already stood is a success like one just written.</summary>
    [Theory]
    [InlineData(GrantWriteOutcome.Written)]
    [InlineData(GrantWriteOutcome.Unchanged)]
    public async Task AddGroupMemberAsync_AMemberJustAddedOrAlreadyOne_IsAnsweredWithNoContent(GrantWriteOutcome outcome)
    {
        // Arrange
        var harness = new EndpointHarness();
        var user = SyntheticUser.Deployment;
        harness.Grants.ReadGroupAsync(GroupId, Arg.Any<CancellationToken>())
            .Returns(new UserGroup(GroupId, "Operators", null, 0, RecordedAt));
        harness.Grants.ReadUserPlacementAsync(user, Arg.Any<CancellationToken>()).Returns(new UserPlacement(user, null));
        harness.Grants.ReadGrantOfGroupAsync(GroupId, Arg.Any<CancellationToken>()).Returns(ScopedGrant.None);
        harness.Grants.AddGroupMemberAsync(GroupId, user, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(outcome));

        // Act
        var result = await GrantEndpoints.AddGroupMemberAsync(
            GroupId,
            user.Value,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    [Theory]
    [InlineData(null, "user", "deployment", false)]
    [InlineData("0198f0aa-0000-7000-8000-0000000005a1", "team", "deployment", false)]
    [InlineData("0198f0aa-0000-7000-8000-0000000005a1", "user", "tenant", false)]
    [InlineData("0198f0aa-0000-7000-8000-0000000005a1", "user", "deployment", true)]
    [InlineData("0198f0aa-0000-7000-8000-0000000005a1", "user", "organization", false)]
    public async Task AssignAsync_ABodyNamingNoRolePrincipalOrScope_IsRefusedWithoutWriting(
        string? roleId,
        string principalKind,
        string scopeKind,
        bool scopeIdBesideTheDeployment)
    {
        // Arrange
        var harness = new EndpointHarness();
        var request = new RoleAssignmentRequest(
            roleId is null ? null : new Guid(roleId),
            principalKind,
            SyntheticUser.Deployment.Value,
            scopeKind,
            scopeIdBesideTheDeployment ? SyntheticUser.Deployment.Value : null);

        // Act
        var result = await GrantEndpoints.AssignAsync(request, harness.Administration, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task AssignAsync_ToAUserAtTheirOwnScope_GivesTheRoleAndAnswersItsIdentifier()
    {
        // Arrange
        var harness = new EndpointHarness();
        var user = SyntheticUser.Deployment;
        harness.Grants.ReadUserPlacementAsync(user, Arg.Any<CancellationToken>()).Returns(new UserPlacement(user, null));
        harness.Grants.ReadRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new Role(RoleId, "Readers", RolePermissions.Of([MailFathomPermission.AdminRead]), RecordedAt));
        harness.Grants
            .AssignAsync(
                Arg.Any<Guid>(),
                RoleId,
                AssignmentPrincipal.User(user),
                AssignmentScope.User(user),
                RecordedAt,
                Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await GrantEndpoints.AssignAsync(
            new RoleAssignmentRequest(RoleId, "user", user.Value, "user", user.Value),
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(Guid.Empty, Assert.IsType<Ok<GrantRecordedResponse>>(result.Result).Value!.Id);
    }

    [Fact]
    public async Task RevokeAsync_TheLastRootAssignment_IsAConflict()
    {
        // Arrange
        var harness = new EndpointHarness();
        var user = SyntheticUser.Deployment;
        harness.Grants.ReadUserPlacementAsync(user, Arg.Any<CancellationToken>()).Returns(new UserPlacement(user, null));
        harness.Grants.ReadAssignmentAsync(AssignmentId, Arg.Any<CancellationToken>()).Returns(new RoleAssignment(
            AssignmentId,
            RoleId,
            AssignmentPrincipal.User(user),
            AssignmentScope.Deployment,
            RecordedAt));
        harness.Grants.RevokeAsync(AssignmentId, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.LastRoot));

        // Act
        var result = await GrantEndpoints.RevokeAsync(AssignmentId, harness.Administration, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>The explanation names the role and the group a permission came through, and marks a name that reaches nothing at its scope.</summary>
    [Fact]
    public async Task ExplainUserPermissionsAsync_AUserHoldingANameInertAtTheirScope_ReportsItAsInert()
    {
        // Arrange
        var harness = new EndpointHarness();
        var user = SyntheticUser.Deployment;
        harness.Grants.ReadUserPlacementAsync(user, Arg.Any<CancellationToken>()).Returns(new UserPlacement(user, null));
        harness.Grants.ReadGrantSourcesOfAsync(default, default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs(
        [
            new GrantSource(
                MailFathomPermission.AdminSpend,
                RoleId,
                "Administrator",
                AssignmentId,
                GroupId,
                "Operators",
                AssignmentScope.User(user)),
        ]);
        harness.Credentials.ReadForUserAsync(user, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await GrantEndpoints.ExplainUserPermissionsAsync(
            user.Value,
            harness.Administration,
            TestContext.Current.CancellationToken);

        // Assert
        var source = Assert.Single(Assert.IsType<Ok<UserPermissionsResponse>>(result.Result).Value!.Sources);
        Assert.Equal(
            ("mailfathom.admin.spend", "Administrator", "Operators", "user", (Guid?)user.Value, true),
            (source.Permission, source.Role, source.Group, source.Scope.Kind, source.Scope.Id, source.Inert));
    }

    private sealed class EndpointHarness
    {
        internal EndpointHarness()
        {
            this.Grants = Substitute.For<IGrantStore>();
            this.Credentials = Substitute.For<IUserCredentialStore>();

            this.Administration = new GrantAdministration(
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Caller(
                    "operations",
                    ScopedGrant.AtDeployment(MailFathomPermission.PublishedFor(ProtectedSurface.Administration)))),
                this.Grants,
                this.Credentials,
                Substitute.For<IGrantAuditor>(),
                new FakeTimeProvider(RecordedAt));
        }

        internal GrantAdministration Administration { get; }

        internal IGrantStore Grants { get; }

        internal IUserCredentialStore Credentials { get; }
    }
}
