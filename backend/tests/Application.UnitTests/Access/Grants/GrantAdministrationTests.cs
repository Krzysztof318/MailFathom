// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Grants;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Grants;

/// <summary>Covers which scope admits each act on roles, groups, and assignments, and each way a write is refused for widening somebody past its writer.</summary>
public sealed class GrantAdministrationTests
{
    private static readonly Guid OwnOrganization = new("0198f0aa-0000-7000-8000-0000000004a1");

    private static readonly Guid OtherOrganization = new("0198f0aa-0000-7000-8000-0000000004a2");

    private static readonly UserId Colleague = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000004b1"));

    private static readonly UserId Stranger = UserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000004b2"));

    private static readonly Guid OwnGroup = new("0198f0aa-0000-7000-8000-0000000004c1");

    private static readonly Guid OtherGroup = new("0198f0aa-0000-7000-8000-0000000004c2");

    private static readonly Guid RoleId = new("0198f0aa-0000-7000-8000-0000000004d1");

    private static readonly Guid AssignmentId = new("0198f0aa-0000-7000-8000-0000000004e1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> RoleDefinitions => ["create", "rename", "replace", "delete"];

    /// <summary>A role is the deployment's vocabulary, so the writing grant held over one organization defines none.</summary>
    [Theory]
    [MemberData(nameof(RoleDefinitions))]
    public async Task RoleDefinition_ByAnOrganizationsAdministrator_IsRefusedWithoutTouchingTheStore(string act)
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);
        var cancellationToken = TestContext.Current.CancellationToken;
        var permissions = RolePermissions.Of([MailFathomPermission.AdminRead]);

        Func<Task> definition = act switch
        {
            "create" => () => harness.Administration.CreateRoleAsync("Readers", permissions, cancellationToken),
            "rename" => () => harness.Administration.RenameRoleAsync(RoleId, "Readers", cancellationToken),
            "replace" => () => harness.Administration.ReplaceRolePermissionsAsync(RoleId, permissions, cancellationToken),
            _ => () => harness.Administration.DeleteRoleAsync(RoleId, cancellationToken),
        };

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(definition);

        // Assert
        Assert.Equal(MailFathomPermission.AdminRolesWrite, refusal.RequiredPermission);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task CreateRoleAsync_ByTheRoot_RecordsTheTrimmedNameUnderAVersion7IdentifierAndAuditsIt()
    {
        // Arrange
        var harness = Harness.Root();
        var permissions = RolePermissions.Of([MailFathomPermission.AdminRead, MailFathomPermission.MailRead]);
        harness.Grants
            .CreateRoleAsync(Arg.Any<Guid>(), Arg.Any<string>(), permissions, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.CreateRoleAsync(
            "  Readers  ",
            permissions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((GrantWriteOutcome.Written, 7), (result.Outcome, result.RecordId.Version));
        await harness.Grants.Received(1).CreateRoleAsync(
            result.RecordId,
            "Readers",
            permissions,
            RecordedAt,
            Arg.Any<CancellationToken>());
        await harness.Auditor.Received(1).RecordGrantChangeAsync(
            Arg.Is<GrantChange>(change => change!.Act == GrantAct.RoleCreated
                && change.RecordId == result.RecordId
                && change.Permissions!.SequenceEqual(permissions.Written)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A role still assigned is refused naming how many assignments stand in the way, and nothing is audited.</summary>
    [Fact]
    public async Task DeleteRoleAsync_AStillAssignedRole_IsRefusedNamingTheStandingAssignments()
    {
        // Arrange
        var harness = Harness.Root();
        harness.Grants.DeleteRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new GrantWriteResult(GrantWriteOutcome.StillAssigned, StandingAssignments: 3));

        // Act
        var result = await harness.Administration.DeleteRoleAsync(RoleId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((GrantWriteOutcome.StillAssigned, 3), (result.Outcome, result.StandingAssignments));
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    /// <summary>The last root is the store's to protect, and its refusal reaches the caller unaudited.</summary>
    [Fact]
    public async Task ReplaceRolePermissionsAsync_DroppingTheLastRoot_IsRefused()
    {
        // Arrange
        var harness = Harness.Root();
        var permissions = RolePermissions.Of([MailFathomPermission.AdminRead]);
        harness.Grants.ReplaceRolePermissionsAsync(RoleId, permissions, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.LastRoot));

        // Act
        var result = await harness.Administration.ReplaceRolePermissionsAsync(
            RoleId,
            permissions,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.LastRoot, result.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    /// <summary>A group in no organization is the deployment's, so an organization's administrator may not record one.</summary>
    [Fact]
    public async Task CreateGroupAsync_InNoOrganizationByAnOrganizationsAdministrator_IsRefused()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.CreateGroupAsync("Everyone", organizationId: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRolesWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    /// <summary>Another organization is answered as one that does not exist, so naming it tells the caller nothing.</summary>
    [Fact]
    public async Task CreateGroupAsync_InAnotherOrganization_IsAnsweredAsAnUnknownOrganization()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.CreateGroupAsync(
            "Sales",
            OtherOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownOrganization, result.Outcome);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task CreateGroupAsync_InTheirOwnOrganization_RecordsItUnderAMintedIdentifier()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);
        harness.Grants
            .CreateGroupAsync(Arg.Any<Guid>(), "Sales", OwnOrganization, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.CreateGroupAsync(
            "Sales",
            OwnOrganization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
        Assert.NotEqual(Guid.Empty, result.RecordId);
    }

    [Fact]
    public async Task RenameGroupAsync_AGroupOfAnotherOrganization_IsAnsweredAsAnUnknownGroupWithoutWriting()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.RenameGroupAsync(
            OtherGroup,
            "Renamed",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownGroup, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().RenameGroupAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeleteGroupAsync_AGroupOfAnotherOrganization_IsAnsweredAsAnUnknownGroupWithoutWriting()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.DeleteGroupAsync(OtherGroup, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownGroup, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().DeleteGroupAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddGroupMemberAsync_AUserOutsideTheCallersScope_IsAnsweredAsAnUnknownUser()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.AddGroupMemberAsync(
            OwnGroup,
            Stranger,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownUser, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().AddGroupMemberAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Joining a group is receiving its assignments, so a name the group gives and the writer does not hold refuses the membership.</summary>
    [Fact]
    public async Task AddGroupMemberAsync_AGroupGivingANameTheWriterDoesNotHold_IsRefusedNamingIt()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);
        harness.GroupGives((MailFathomPermission.AdminErase, AssignmentScope.Organization(OwnOrganization)));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.AddGroupMemberAsync(OwnGroup, Colleague, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
        await harness.Grants.DidNotReceiveWithAnyArgs().AddGroupMemberAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Holding a name over one organization is not holding it over the deployment the group gives it at.</summary>
    [Fact]
    public async Task AddGroupMemberAsync_AGroupGivingANameWiderThanTheWriterHoldsIt_IsRefusedAsHeldTooNarrowly()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminRead);
        harness.GroupGives((MailFathomPermission.AdminRead, AssignmentScope.Deployment));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.AddGroupMemberAsync(OwnGroup, Colleague, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        Assert.Contains("narrower", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddGroupMemberAsync_AGroupGivingWhatTheWriterHoldsWhereTheyHoldIt_AddsTheMemberAndAuditsIt()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminRead);
        harness.GroupGives(
            (MailFathomPermission.AdminRead, AssignmentScope.Organization(OwnOrganization)),
            (MailFathomPermission.AdminRead, AssignmentScope.User(Colleague)));
        harness.Grants.AddGroupMemberAsync(OwnGroup, Colleague, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.AddGroupMemberAsync(
            OwnGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
        await harness.Auditor.Received(1).RecordGrantChangeAsync(
            Arg.Is<GrantChange>(change => change!.Act == GrantAct.GroupMemberAdded
                && change.RecordId == OwnGroup
                && change.Member == Colleague),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveGroupMemberAsync_AUserOutsideTheCallersScope_IsAnsweredAsAnUnknownUser()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.RemoveGroupMemberAsync(
            OwnGroup,
            Stranger,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownUser, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().RemoveGroupMemberAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveGroupMemberAsync_AGroupOfAnotherOrganization_IsAnsweredAsAnUnknownGroupWithoutWriting()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.RemoveGroupMemberAsync(
            OtherGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownGroup, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().RemoveGroupMemberAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveGroupMemberAsync_TheLastMemberHoldingTheRoot_IsRefusedUnaudited()
    {
        // Arrange
        var harness = Harness.Root();
        harness.Grants.RemoveGroupMemberAsync(OwnGroup, Colleague, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.LastRoot));

        // Act
        var result = await harness.Administration.RemoveGroupMemberAsync(
            OwnGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.LastRoot, result.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    /// <summary>A record says a membership changed, so a write that found it already as asked leaves none.</summary>
    [Fact]
    public async Task AddGroupMemberAsync_AUserWhoIsAlreadyAMember_IsAnsweredAsUnchangedAndUnaudited()
    {
        // Arrange
        var harness = Harness.Root();
        harness.Grants.AddGroupMemberAsync(OwnGroup, Colleague, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Unchanged));

        // Act
        var result = await harness.Administration.AddGroupMemberAsync(
            OwnGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Unchanged, result.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    [Fact]
    public async Task RemoveGroupMemberAsync_AUserWhoWasNeverAMember_IsAnsweredAsUnchangedAndUnaudited()
    {
        // Arrange
        var harness = Harness.Root();
        harness.Grants.RemoveGroupMemberAsync(OwnGroup, Colleague, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Unchanged));

        // Act
        var result = await harness.Administration.RemoveGroupMemberAsync(
            OwnGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Unchanged, result.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    [Fact]
    public async Task ReadGroupMembersAsync_AGroupOfAnotherOrganization_IsAnsweredAsNoGroupWithoutReading()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRead);
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

        // Act
        var members = await harness.Administration.ReadGroupMembersAsync(
            OtherGroup,
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(members);
        await harness.Grants.DidNotReceiveWithAnyArgs().ReadGroupMembersAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>An assignment at the deployment scope is the deployment's, so only the root makes one.</summary>
    [Fact]
    public async Task AssignAsync_AtTheDeploymentByAnOrganizationsAdministrator_IsRefused()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Deployment,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRolesWrite, refusal.RequiredPermission);
        Assert.True(refusal.RefusedForTheDeploymentAlone);
        Assert.Empty(harness.Grants.ReceivedCalls());
    }

    [Fact]
    public async Task AssignAsync_ToAUserOutsideTheCallersScope_IsAnsweredAsAnUnknownUser()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Stranger),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownUser, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().AssignAsync(default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AssignAsync_ToAGroupOfAnotherOrganization_IsAnsweredAsAnUnknownGroup()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.Group(OtherGroup),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownGroup, result.Outcome);
    }

    [Fact]
    public async Task AssignAsync_AtAnotherOrganization_IsAnsweredAsAnUnknownOrganization()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);

        // Act
        var result = await harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OtherOrganization),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownOrganization, result.Outcome);
    }

    /// <summary>Nobody grants more than they hold: a role listing a name the writer lacks is refused naming it.</summary>
    [Fact]
    public async Task AssignAsync_ARoleListingANameTheWriterDoesNotHold_IsRefusedNamingIt()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminRead);
        harness.RoleLists(MailFathomPermission.AdminRead, MailFathomPermission.AdminCredentialsWrite);

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        await harness.Grants.DidNotReceiveWithAnyArgs().AssignAsync(default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A mail name's scope is never read, so a writer holding it anywhere gives it at any scope; a name inert at the scope widens nobody and is not asked about.</summary>
    [Fact]
    public async Task AssignAsync_ARoleListingAMailNameHeldAnywhereAndANameInertAtTheScope_IsWrittenAndAudited()
    {
        // Arrange
        var harness = Harness.Granted(
            (MailFathomPermission.AdminRolesWrite, AssignmentScope.Organization(OwnOrganization)),
            (MailFathomPermission.AdminRead, AssignmentScope.Organization(OwnOrganization)),
            (MailFathomPermission.MailRead, AssignmentScope.User(Stranger)));
        harness.RoleLists(MailFathomPermission.AdminRead, MailFathomPermission.MailRead, MailFathomPermission.AdminSpend);
        harness.Grants
            .AssignAsync(
                Arg.Any<Guid>(),
                RoleId,
                AssignmentPrincipal.User(Colleague),
                AssignmentScope.User(Colleague),
                RecordedAt,
                Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.User(Colleague),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
        await harness.Auditor.Received(1).RecordGrantChangeAsync(
            Arg.Is<GrantChange>(change => change!.Act == GrantAct.RoleAssigned
                && change.RecordId == result.RecordId
                && change.Assignment!.Scope == AssignmentScope.User(Colleague)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A pattern holds whatever a later release publishes in its reach, so holding everything it reaches today bounds
    /// nothing: below the root the assignment is refused naming the root, whatever scope it is made at.
    /// </summary>
    [Fact]
    public async Task AssignAsync_ARoleListingAPatternByAnOrganizationsAdministratorHoldingAllItReaches_IsRefusedNamingTheRoot()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminAuditRead);
        harness.RoleListsAsWritten("mailfathom.admin.audit.*");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRolesWrite, refusal.RequiredPermission);
        Assert.True(refusal.IsOverAWideningGrant);
        Assert.False(refusal.IsHeldTooNarrowly);
        await harness.Grants.DidNotReceiveWithAnyArgs().AssignAsync(default, default, default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A stored pattern reaching nothing in this build still widens with the next one, so it is asked about exactly as one that reaches something.</summary>
    [Fact]
    public async Task AssignAsync_ARoleWhoseOnlyPatternReachesNothingByAnOrganizationsAdministrator_IsRefusedNamingTheRoot()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminRead);
        harness.RoleListsAsWritten("mailfathom.admin.read", "mailfathom.retired.*");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.True(refusal.IsOverAWideningGrant);
    }

    [Fact]
    public async Task AssignAsync_ARoleListingAPatternByTheRootBelowTheDeployment_IsWritten()
    {
        // Arrange
        var harness = Harness.Root();
        harness.RoleListsAsWritten("mailfathom.admin.*.write");
        harness.Grants
            .AssignAsync(
                Arg.Any<Guid>(),
                RoleId,
                AssignmentPrincipal.User(Colleague),
                AssignmentScope.Organization(OwnOrganization),
                RecordedAt,
                Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OwnOrganization),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
    }

    /// <summary>The root is asked beside the ordinary rule and never instead of it: what a pattern reaches today is still given only by somebody holding it.</summary>
    [Fact]
    public async Task AssignAsync_ARoleListingAPatternReachingANameTheRootDoesNotHold_IsRefusedNamingThatName()
    {
        // Arrange
        var harness = Harness.Root();
        harness.RoleListsAsWritten("*");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => harness.Administration.AssignAsync(
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Deployment,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        Assert.False(refusal.IsOverAWideningGrant);
    }

    /// <summary>Joining a group is receiving its assignments, a role listing a pattern among them, so below the root the membership is refused naming the root.</summary>
    [Fact]
    public async Task AddGroupMemberAsync_AGroupGivenARoleListingAPatternByAnOrganizationsAdministrator_IsRefusedNamingTheRoot()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite, MailFathomPermission.AdminRead);
        harness.GroupGives((MailFathomPermission.AdminRead, AssignmentScope.Organization(OwnOrganization)));
        harness.GroupIsGivenARoleListingAPattern();

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.AddGroupMemberAsync(OwnGroup, Colleague, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRolesWrite, refusal.RequiredPermission);
        Assert.True(refusal.IsOverAWideningGrant);
        await harness.Grants.DidNotReceiveWithAnyArgs().AddGroupMemberAsync(default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddGroupMemberAsync_AGroupGivenARoleListingAPatternByTheRoot_AddsTheMember()
    {
        // Arrange
        var harness = Harness.Root();
        harness.GroupGives((MailFathomPermission.AdminRead, AssignmentScope.Organization(OwnOrganization)));
        harness.GroupIsGivenARoleListingAPattern();
        harness.Grants.AddGroupMemberAsync(OwnGroup, Colleague, RecordedAt, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.AddGroupMemberAsync(
            OwnGroup,
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
    }

    /// <summary>The record is of what somebody wrote, so a pattern is audited as the pattern rather than as the names it reached that day.</summary>
    [Fact]
    public async Task ReplaceRolePermissionsAsync_AListCarryingAPattern_AuditsTheEntriesAsWritten()
    {
        // Arrange
        var harness = Harness.Root();
        Assert.True(RolePermissions.TryCreate(["mailfathom.admin.read", "mailfathom.admin.*.write"], out var permissions, out _));
        harness.Grants.ReplaceRolePermissionsAsync(RoleId, permissions!, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.Written));

        // Act
        var result = await harness.Administration.ReplaceRolePermissionsAsync(
            RoleId,
            permissions!,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.Written, result.Outcome);
        await harness.Auditor.Received(1).RecordGrantChangeAsync(
            Arg.Is<GrantChange>(change => change!.Act == GrantAct.RolePermissionsReplaced
                && change.Permissions!.SequenceEqual(permissions!.Written)
                && change.Permissions!.Contains("mailfathom.admin.*.write")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_AnAssignmentAtAnotherOrganization_IsAnsweredAsAnUnknownAssignmentWithoutRevoking()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);
        harness.Grants.ReadAssignmentAsync(AssignmentId, Arg.Any<CancellationToken>()).Returns(new RoleAssignment(
            AssignmentId,
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Organization(OtherOrganization),
            RecordedAt));

        // Act
        var result = await harness.Administration.RevokeAsync(AssignmentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.UnknownAssignment, result.Outcome);
        await harness.Grants.DidNotReceiveWithAnyArgs().RevokeAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RevokeAsync_TheLastRootAssignment_IsRefusedUnaudited()
    {
        // Arrange
        var harness = Harness.Root();
        harness.Grants.ReadAssignmentAsync(AssignmentId, Arg.Any<CancellationToken>()).Returns(new RoleAssignment(
            AssignmentId,
            RoleId,
            AssignmentPrincipal.User(Colleague),
            AssignmentScope.Deployment,
            RecordedAt));
        harness.Grants.RevokeAsync(AssignmentId, Arg.Any<CancellationToken>())
            .Returns(GrantWriteResult.Of(GrantWriteOutcome.LastRoot));

        // Act
        var result = await harness.Administration.RevokeAsync(AssignmentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(GrantWriteOutcome.LastRoot, result.Outcome);
        Assert.Empty(harness.Auditor.ReceivedCalls());
    }

    /// <summary>A listing never refuses over a scope; it answers within what the caller's scopes cover.</summary>
    [Fact]
    public async Task ReadGroupsAsync_ByAnOrganizationsAdministrator_ReadsWithinTheirOrganizationAlone()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRead);
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

        // Act
        await harness.Administration.ReadGroupsAsync(query, TestContext.Current.CancellationToken);

        // Assert
        await harness.Grants.Received(1).ReadGroupsAsync(
            query,
            Arg.Is<GrantListingReach>(reach => !reach!.WholeDeployment
                && reach.Organizations.SetEquals(new[] { OwnOrganization })
                && reach.Users.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadRolesAsync_ACallerHoldingTheReadingGrantAtNoScope_IsRefused()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRolesWrite);
        var query = AdministrativeListingQuery.Create(pageSize: null, after: null)!;

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            harness.Administration.ReadRolesAsync(query, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    [Fact]
    public async Task ExplainUserGrantAsync_AUserOutsideTheCallersScope_IsAnsweredAsNobody()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRead);

        // Act
        var explanation = await harness.Administration.ExplainUserGrantAsync(
            Stranger,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(explanation);
        await harness.Grants.DidNotReceiveWithAnyArgs().ReadGrantSourcesOfAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Covering a user is not covering what they hold elsewhere, so the rows are read within the reach a listing of assignments is.</summary>
    [Fact]
    public async Task ExplainUserGrantAsync_ByAnOrganizationsAdministrator_ReadsTheSourcesWithinTheirOrganizationAlone()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRead);
        harness.Grants.ReadGrantSourcesOfAsync(default, default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs([]);
        harness.Credentials.ReadForUserAsync(Colleague, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        await harness.Administration.ExplainUserGrantAsync(Colleague, TestContext.Current.CancellationToken);

        // Assert
        await harness.Grants.Received(1).ReadGrantSourcesOfAsync(
            Colleague,
            Arg.Is<GrantListingReach>(reach => !reach!.WholeDeployment
                && reach.Organizations.SetEquals(new[] { OwnOrganization })
                && reach.Users.Count == 0),
            UserGrantExplanation.MaximumSources + 1,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExplainUserGrantAsync_AUserHoldingMoreRowsThanOneExplanationCarries_AnswersTheCeilingAndSaysItIsAPart()
    {
        // Arrange
        var harness = Harness.Root();
        GrantSource[] stored =
        [
            .. Enumerable.Range(0, UserGrantExplanation.MaximumSources + 1).Select(index => new GrantSource(
                MailFathomPermission.AdminRead,
                Pattern: null,
                RoleId,
                $"Role {index:D4}",
                AssignmentId,
                GroupId: null,
                GroupName: null,
                AssignmentScope.Deployment)),
        ];
        harness.Grants.ReadGrantSourcesOfAsync(default, default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs(stored);
        harness.Credentials.ReadForUserAsync(Colleague, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var explanation = await harness.Administration.ExplainUserGrantAsync(
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(explanation);
        Assert.True(explanation.SourcesTruncated);
        Assert.Equal(stored[..UserGrantExplanation.MaximumSources], explanation.Sources);
    }

    [Fact]
    public async Task ExplainUserGrantAsync_AUserInTheCallersScope_ListsTheSourcesInPublishedOrderBesideTheirCredentials()
    {
        // Arrange
        var harness = Harness.OrganizationAdministrator(MailFathomPermission.AdminRead);
        var spend = new GrantSource(
            MailFathomPermission.AdminSpend,
            "mailfathom.admin.*",
            RoleId,
            "Administrator",
            AssignmentId,
            OwnGroup,
            "Sales",
            AssignmentScope.Organization(OwnOrganization));
        var read = spend with { Permission = MailFathomPermission.AdminRead, GroupId = null, GroupName = null };
        harness.Grants.ReadGrantSourcesOfAsync(default, default!, default, TestContext.Current.CancellationToken).ReturnsForAnyArgs([spend, read]);
        harness.Credentials.ReadForUserAsync(Colleague, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var explanation = await harness.Administration.ExplainUserGrantAsync(
            Colleague,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(explanation);
        Assert.False(explanation.SourcesTruncated);
        Assert.Equal([read, spend], explanation.Sources);
        Assert.Equal([false, true], explanation.Sources.Select(source => source.IsInert));
    }

    private sealed class Harness
    {
        private Harness(ScopedGrant grant)
        {
            this.Grants = Substitute.For<IGrantStore>();
            this.Credentials = Substitute.For<IUserCredentialStore>();
            this.Auditor = Substitute.For<IGrantAuditor>();

            this.Grants.ReadUserPlacementAsync(Colleague, Arg.Any<CancellationToken>())
                .Returns(new UserPlacement(Colleague, OwnOrganization));
            this.Grants.ReadUserPlacementAsync(Stranger, Arg.Any<CancellationToken>())
                .Returns(new UserPlacement(Stranger, OtherOrganization));
            this.Grants.ReadGroupAsync(OwnGroup, Arg.Any<CancellationToken>())
                .Returns(new UserGroup(OwnGroup, "Sales", OwnOrganization, 0, RecordedAt));
            this.Grants.ReadGroupAsync(OtherGroup, Arg.Any<CancellationToken>())
                .Returns(new UserGroup(OtherGroup, "Elsewhere", OtherOrganization, 0, RecordedAt));
            this.Grants.ReadGrantOfGroupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ScopedGrant.None);

            this.Administration = new GrantAdministration(
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Caller("operations", grant)),
                this.Grants,
                this.Credentials,
                this.Auditor,
                new FakeTimeProvider(RecordedAt));
        }

        internal GrantAdministration Administration { get; }

        internal IGrantStore Grants { get; }

        internal IUserCredentialStore Credentials { get; }

        internal IGrantAuditor Auditor { get; }

        internal static Harness Root() =>
            new(ScopedGrant.AtDeployment(MailFathomPermission.PublishedFor(ProtectedSurface.Administration)));

        internal static Harness OrganizationAdministrator(params MailFathomPermission[] permissions) =>
            new(ScopedGrant.Of(permissions.Select(permission =>
                (permission, AssignmentScope.Organization(OwnOrganization)))));

        internal static Harness Granted(params (MailFathomPermission Permission, AssignmentScope Scope)[] held) =>
            new(ScopedGrant.Of(held));

        internal void GroupGives(params (MailFathomPermission Permission, AssignmentScope Scope)[] given) =>
            this.Grants.ReadGrantOfGroupAsync(OwnGroup, Arg.Any<CancellationToken>()).Returns(ScopedGrant.Of(given));

        internal void RoleLists(params MailFathomPermission[] permissions) =>
            this.Grants.ReadRoleAsync(RoleId, Arg.Any<CancellationToken>())
                .Returns(new Role(RoleId, "Assigned", RolePermissions.Of(permissions), RecordedAt));

        internal void RoleListsAsWritten(params string[] entries) =>
            this.Grants.ReadRoleAsync(RoleId, Arg.Any<CancellationToken>())
                .Returns(new Role(RoleId, "Assigned", RolePermissions.Read(entries), RecordedAt));

        internal void GroupIsGivenARoleListingAPattern() =>
            this.Grants.HoldsWideningRoleAsync(AssignmentPrincipal.Group(OwnGroup), Arg.Any<CancellationToken>()).Returns(true);
    }
}
