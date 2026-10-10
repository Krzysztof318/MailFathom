// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the commands that define roles, keep groups and their members, give and revoke roles, and explain a user's grant.</summary>
/// <remarks>
/// What these hold is the part of each act that lives in the command: which route and verb a write reaches and what its
/// body states, that an empty list, a group in no organization, and a deployment-wide scope are each stated rather than
/// reached by leaving something out, what a listing puts under which heading, and how a refusal reaches the operator.
/// </remarks>
public sealed class GrantCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly Guid Role = new("11111111-1111-4111-8111-111111111111");

    private static readonly Guid Group = new("22222222-2222-4222-8222-222222222222");

    private static readonly Guid User = new("33333333-3333-4333-8333-333333333333");

    private static readonly Guid Organization = new("44444444-4444-4444-8444-444444444444");

    private static readonly Guid Assignment = new("55555555-5555-4555-8555-555555555555");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The deployment answers in identifier order a page at a time, so the listing gathers every page and draws the roles by name.</summary>
    [Fact]
    public async Task RoleList_AListingAnsweredOnTwoPages_DrawsEveryRoleInNameOrder()
    {
        // Arrange
        var laterRole = new Guid("99999999-9999-4999-8999-999999999999");
        using var deployment = FakeGrantDeployment.AnsweringOnTwoPages(
            cursor => FakeGrantDeployment.RolePage(cursor, FakeGrantDeployment.Role(Role, "readers", "mailfathom.admin.read")),
            FakeGrantDeployment.RolePage(null, FakeGrantDeployment.Role(laterRole, "auditors")));

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Role", "Name", "Permissions", "Recorded");

        Assert.Equal(["auditors", "readers"], listing.Rows.Select(row => listing.Cell(row, "Name")));
        Assert.Equal(["nothing", "mailfathom.admin.read"], listing.Rows.Select(row => listing.Cell(row, "Permissions")));
    }

    /// <summary>A stored name the build does not publish grants nothing, so it is named beneath the listing rather than read as part of the role.</summary>
    [Fact]
    public async Task RoleList_ARoleStoringAnUnpublishedName_NamesItBeneathTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering(FakeGrantDeployment.RolePage(
            null,
            FakeGrantDeployment.RoleListing(Role, "readers", ["mailfathom.admin.read"], ["mailfathom.retired"])));

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var reported = Assert.Single(this.harness.Console.Lines, line => line.Contains("mailfathom.retired", StringComparison.Ordinal));

        Assert.Contains($"{Role:D}", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pattern is drawn in the listing as it was written and, beneath it, beside what the deployment says it reaches
    /// now, because that reach is the one part of a role the next release may add to without anybody writing to it.
    /// </summary>
    [Fact]
    public async Task RoleList_ARoleListingAPattern_DrawsItAsWrittenAndNamesWhatItReachesBeneathTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering(FakeGrantDeployment.RolePage(
            null,
            $$"""
              {"id":"{{Role:D}}","name":"writers","permissions":["mailfathom.admin.read","mailfathom.admin.*.write"],
               "patterns":[{"pattern":"mailfathom.admin.*.write","reaches":["mailfathom.admin.credentials.write","mailfathom.admin.roles.write"]}],
               "unpublished":[],"createdAt":"2026-10-10T12:00:00+00:00"}
              """));

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Role", "Name", "Permissions", "Recorded");

        Assert.Equal(
            "mailfathom.admin.read, mailfathom.admin.*.write",
            listing.Cell(Assert.Single(listing.Rows), "Permissions"));

        var reach = Assert.Single(this.harness.Console.Lines, line => line.Contains("(writers)", StringComparison.Ordinal));

        Assert.Contains($"{Role:D} (writers): mailfathom.admin.*.write reaches", reach, StringComparison.Ordinal);
        Assert.EndsWith("mailfathom.admin.credentials.write, mailfathom.admin.roles.write", reach, StringComparison.Ordinal);
    }

    /// <summary>A pattern is one more entry of the list, sent exactly as it was written for the deployment to judge.</summary>
    [Fact]
    public async Task RoleSetPermissions_APatternBesideAName_SendsBothAsWritten()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering("{}");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "role",
            "set-permissions",
            "--role",
            $"{Role:D}",
            "--permission",
            "mailfathom.admin.read",
            "--permission",
            "mailfathom.admin.*.write",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var request = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.RolePermissionsPath(Role)));
        using var body = JsonDocument.Parse(request.ContentAsUtf8String());

        Assert.Equal(
            ["mailfathom.admin.read", "mailfathom.admin.*.write"],
            body.RootElement.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()));
    }

    /// <summary>The identifier is the deployment's to mint, so the command sends the name and every permission named, and reports what came back.</summary>
    [Fact]
    public async Task RoleAdd_ANameAndTwoPermissions_SendsBothAndReportsTheIdentifierItMinted()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "role",
            "add",
            "--name",
            "readers",
            "--permission",
            "mailfathom.admin.read",
            "--permission",
            "mailfathom.mail.read",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RolesPath));
        using var body = JsonDocument.Parse(recording.ContentAsUtf8String());

        Assert.Equal("readers", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(
            ["mailfathom.admin.read", "mailfathom.mail.read"],
            body.RootElement.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{FakeGrantDeployment.RecordedId:D}", StringComparison.Ordinal));
    }

    /// <summary>A role granting nothing is stated with a flag of its own, so an invocation that forgot its permissions is not read as one.</summary>
    [Fact]
    public async Task RoleAdd_NeitherAPermissionNorTheEmptyList_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "add", "--name", "readers", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RolesPath));
    }

    [Fact]
    public async Task RoleRename_AName_SendsItToThatRolesNameRoute()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "role", "rename", "--role", $"{Role:D}", "--name", "auditors", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rename = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.RoleNamePath(Role)));
        using var body = JsonDocument.Parse(rename.ContentAsUtf8String());

        Assert.Equal("auditors", body.RootElement.GetProperty("name").GetString());
    }

    /// <summary>Replacing a role's list with nothing takes every name from everybody it is given to, so it is sent only when stated, and as an empty list rather than a missing one.</summary>
    [Fact]
    public async Task RoleSetPermissions_NoPermissions_SendsTheEmptyList()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "role", "set-permissions", "--role", $"{Role:D}", "--no-permissions", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var replacement = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.RolePermissionsPath(Role)));
        using var body = JsonDocument.Parse(replacement.ContentAsUtf8String());

        Assert.Equal(0, body.RootElement.GetProperty("permissions").GetArrayLength());
    }

    [Fact]
    public async Task RoleSetPermissions_PermissionsAndTheEmptyListAtOnce_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "role",
            "set-permissions",
            "--role",
            $"{Role:D}",
            "--permission",
            "mailfathom.admin.read",
            "--no-permissions",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.RolePermissionsPath(Role)));
    }

    [Fact]
    public async Task RoleRemove_AgreedUpFront_DeletesThatRole()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "remove", "--role", $"{Role:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.RolePath(Role)));
    }

    /// <summary>With nobody at the terminal and no stated agreement, nothing is removed.</summary>
    [Fact]
    public async Task RoleRemove_NobodyToAskAndNoAgreement_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "remove", "--role", $"{Role:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.RolePath(Role)));
    }

    /// <summary>The deployment states how many assignments still give the role, and that sentence is what the operator acts on.</summary>
    [Fact]
    public async Task RoleRemove_ARoleStillAssigned_RepeatsWhatTheDeploymentSaid()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.Conflict,
            "2 role assignment(s) still stand on it. Revoke each of them before removing it.");

        // Act
        var exitCode = await this.RunAsync(deployment, "role", "remove", "--role", $"{Role:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("2 role assignment(s) still stand", StringComparison.Ordinal));
    }

    /// <summary>A 404 on a role route means the role rather than the port, so the operator is sent to the listing rather than after a listener.</summary>
    [Fact]
    public async Task RoleRename_ARoleTheDeploymentDoesNotDefine_SaysSoAndNamesTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(HttpStatusCode.NotFound, "This deployment holds no such role.");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "role", "rename", "--role", $"{Role:D}", "--name", "auditors", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("'mfctl role list'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GroupList_AGroupInNoOrganizationAndOneInAnOrganization_DrawsEachUnderItsHeading()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering(
            $$"""{"groups":[{{FakeGrantDeployment.Group(Group, "operators", organization: null, members: 3)}},{{FakeGrantDeployment.Group(Assignment, "acme-staff", Organization, members: 1)}}],"nextCursor":null}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "group", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Group", "Name", "Organization", "Members", "Recorded");

        Assert.Equal(["acme-staff", "operators"], listing.Rows.Select(row => listing.Cell(row, "Name")));
        Assert.Equal([$"{Organization:D}", "none"], listing.Rows.Select(row => listing.Cell(row, "Organization")));
        Assert.Equal(["1", "3"], listing.Rows.Select(row => listing.Cell(row, "Members")));
    }

    /// <summary>A group of the deployment's own is stated rather than reached by leaving the organization out.</summary>
    [Fact]
    public async Task GroupAdd_NoOrganization_SendsTheDecisionToBelongToNone()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "add", "--name", "operators", "--no-organization", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.GroupsPath));
        using var body = JsonDocument.Parse(recording.ContentAsUtf8String());

        Assert.Equal("operators", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("organizationId").ValueKind);
        Assert.True(body.RootElement.GetProperty("none").GetBoolean());
    }

    [Fact]
    public async Task GroupAdd_AnOrganization_SendsItAndNotTheDecisionToBelongToNone()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "add", "--name", "acme-staff", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var recording = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.GroupsPath));
        using var body = JsonDocument.Parse(recording.ContentAsUtf8String());

        Assert.Equal(Organization, body.RootElement.GetProperty("organizationId").GetGuid());
        Assert.False(body.RootElement.GetProperty("none").GetBoolean());
    }

    [Fact]
    public async Task GroupAdd_NeitherAnOrganizationNorNone_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "group", "add", "--name", "operators", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.GroupsPath));
    }

    [Fact]
    public async Task GroupRename_AName_SendsItToThatGroupsNameRoute()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "rename", "--group", $"{Group:D}", "--name", "admins", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var rename = Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.GroupNamePath(Group)));
        using var body = JsonDocument.Parse(rename.ContentAsUtf8String());

        Assert.Equal("admins", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GroupRemove_AgreedAtThePrompt_DeletesThatGroup()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "group", "remove", "--group", $"{Group:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(this.harness.Console.Questions);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.GroupPath(Group)));
    }

    [Fact]
    public async Task GroupMembers_AGroupWithTwoMembers_DrawsEachMember()
    {
        // Arrange
        var anotherUser = new Guid("77777777-7777-4777-8777-777777777777");
        using var deployment = FakeGrantDeployment.Answering(
            $$"""{"group":"{{Group:D}}","members":["{{User:D}}","{{anotherUser:D}}"],"nextCursor":null}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "group", "members", "--group", $"{Group:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.GroupMembersPath(Group)));

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "User");

        Assert.Equal([$"{User:D}", $"{anotherUser:D}"], listing.Rows.Select(row => listing.Cell(row, "User")));
    }

    /// <summary>A 404 on the members route means the group lies outside the caller's scope, so the operator is sent to the listing rather than after a listener.</summary>
    [Fact]
    public async Task GroupMembers_AGroupOutsideTheScope_SaysSoAndNamesTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.NotFound,
            "This deployment holds no such group within your scope.");

        // Act
        var exitCode = await this.RunAsync(deployment, "group", "members", "--group", $"{Group:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("'mfctl group list'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GroupAddMember_AGroupAndAUser_PutsThatMembership()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "add-member", "--group", $"{Group:D}", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Put, AdminEndpointRoutes.GroupMemberPath(Group, User)));
    }

    /// <summary>The deployment refuses a member from another organization than the group's, and says why.</summary>
    [Fact]
    public async Task GroupAddMember_AUserOfAnotherOrganization_RepeatsWhatTheDeploymentSaid()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.Conflict,
            "The user belongs to a different organization from the group, which holds only its own organization's members.");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "add-member", "--group", $"{Group:D}", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("different organization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GroupRemoveMember_AGroupAndAUser_DeletesThatMembership()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "group", "remove-member", "--group", $"{Group:D}", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.GroupMemberPath(Group, User)));
    }

    [Fact]
    public async Task AssignmentList_AnAssignmentToAGroupAtAnOrganization_DrawsWhoAndWhere()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering(
            $$"""{"assignments":[{{FakeGrantDeployment.Assignment(Assignment, Role, ("group", Group), ("organization", Organization))}},{{FakeGrantDeployment.Assignment(User, Role, ("user", User), ("deployment", null))}}],"nextCursor":null}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "assignment", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Assignment", "Role", "Given to", "Scope", "Assigned");

        Assert.Equal([$"group {Group:D}", $"user {User:D}"], listing.Rows.Select(row => listing.Cell(row, "Given to")));
        Assert.Equal([$"organization {Organization:D}", "deployment"], listing.Rows.Select(row => listing.Cell(row, "Scope")));
    }

    [Fact]
    public async Task AssignmentAdd_AGroupAtAnOrganization_SendsThePrincipalAndTheScope()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "assignment",
            "add",
            "--role",
            $"{Role:D}",
            "--group",
            $"{Group:D}",
            "--organization",
            $"{Organization:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var assignment = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RoleAssignmentsPath));
        using var body = JsonDocument.Parse(assignment.ContentAsUtf8String());

        Assert.Equal(Role, body.RootElement.GetProperty("roleId").GetGuid());
        Assert.Equal("group", body.RootElement.GetProperty("principalKind").GetString());
        Assert.Equal(Group, body.RootElement.GetProperty("principalId").GetGuid());
        Assert.Equal("organization", body.RootElement.GetProperty("scopeKind").GetString());
        Assert.Equal(Organization, body.RootElement.GetProperty("scopeId").GetGuid());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains($"{FakeGrantDeployment.RecordedId:D}", StringComparison.Ordinal));
    }

    /// <summary>The deployment scope carries no identifier, and is sent only because it was stated.</summary>
    [Fact]
    public async Task AssignmentAdd_AUserAtTheDeployment_SendsTheDeploymentScopeWithNoIdentifier()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "add", "--role", $"{Role:D}", "--user", $"{User:D}", "--deployment", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var assignment = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RoleAssignmentsPath));
        using var body = JsonDocument.Parse(assignment.ContentAsUtf8String());

        Assert.Equal("user", body.RootElement.GetProperty("principalKind").GetString());
        Assert.Equal(User, body.RootElement.GetProperty("principalId").GetGuid());
        Assert.Equal("deployment", body.RootElement.GetProperty("scopeKind").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("scopeId").ValueKind);
    }

    [Fact]
    public async Task AssignmentAdd_AUserScopedToAnotherUser_SendsTheUserScope()
    {
        // Arrange
        var scopedUser = new Guid("88888888-8888-4888-8888-888888888888");
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "assignment",
            "add",
            "--role",
            $"{Role:D}",
            "--user",
            $"{User:D}",
            "--scope-user",
            $"{scopedUser:D}",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var assignment = Assert.Single(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RoleAssignmentsPath));
        using var body = JsonDocument.Parse(assignment.ContentAsUtf8String());

        Assert.Equal("user", body.RootElement.GetProperty("scopeKind").GetString());
        Assert.Equal(scopedUser, body.RootElement.GetProperty("scopeId").GetGuid());
    }

    /// <summary>A missing scope must never read as the deployment, which is the widest grant there is.</summary>
    [Fact]
    public async Task AssignmentAdd_NoScope_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "add", "--role", $"{Role:D}", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RoleAssignmentsPath));
    }

    [Fact]
    public async Task AssignmentAdd_BothAUserAndAGroup_IsRefusedWithoutARequest()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "assignment",
            "add",
            "--role",
            $"{Role:D}",
            "--user",
            $"{User:D}",
            "--group",
            $"{Group:D}",
            "--deployment",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Post, AdminEndpointRoutes.RoleAssignmentsPath));
    }

    /// <summary>The same role given twice to the same principal at the same scope is refused, and the operator is told so.</summary>
    [Fact]
    public async Task AssignmentAdd_AnAssignmentThatAlreadyStands_RepeatsWhatTheDeploymentSaid()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.Conflict,
            "The same role is already given to the same principal at the same scope.");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "add", "--role", $"{Role:D}", "--user", $"{User:D}", "--deployment", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("already given", StringComparison.Ordinal));
    }

    /// <summary>
    /// A role listing a pattern is the root's alone to give, so the refusal sends the operator to an administrator
    /// holding the root over the whole deployment instead of telling them to be granted a name they may already hold.
    /// </summary>
    [Fact]
    public async Task AssignmentAdd_ADeploymentRefusingARoleListingAPatternBelowTheRoot_SendsTheOperatorToTheRoot()
    {
        // Arrange
        using FakeHttpMessageHandler deployment = new((request, _) => Task.FromResult(
            FakeAdminEndpoint.AnswerSession(request)
            ?? FakeAdminEndpoint.Json(
                HttpStatusCode.Forbidden,
                """{"status":403,"detail":"A role this write gives lists a pattern.","permission":"mailfathom.admin.roles.write","widensOnUpgrade":true}""")));

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "add", "--role", $"{Role:D}", "--user", $"{User:D}", "--organization", $"{Organization:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("lists a pattern", StringComparison.Ordinal)
                && line.Contains("'mailfathom.admin.roles.write' over the whole deployment", StringComparison.Ordinal)
                && !line.Contains("provision a credential", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssignmentRevoke_AgreedUpFront_DeletesThatAssignment()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "revoke", "--assignment", $"{Assignment:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.RoleAssignmentPath(Assignment)));
    }

    [Fact]
    public async Task AssignmentRevoke_DeclinedAtThePrompt_SendsNothing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering();
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "revoke", "--assignment", $"{Assignment:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RequestsTo(HttpMethod.Delete, AdminEndpointRoutes.RoleAssignmentPath(Assignment)));
    }

    [Fact]
    public async Task AssignmentRevoke_AnAssignmentOutsideTheScope_SaysSoAndNamesTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.NotFound,
            "This deployment holds no such role assignment within your scope.");

        // Act
        var exitCode = await this.RunAsync(
            deployment, "assignment", "revoke", "--assignment", $"{Assignment:D}", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("'mfctl assignment list'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each permission is read beside the role, the group it arrives through or none, and the scope, a permission that
    /// reaches nothing at its scope is marked rather than left out, and each credential follows with its own narrowing.
    /// </summary>
    [Fact]
    public async Task UserPermissions_ADirectGrantAnInertOneAndACredential_ExplainsEachOfThem()
    {
        // Arrange
        var credential = new Guid("aaaaaaaa-0000-4000-8000-000000000001");
        var inheritedAssignment = new Guid("bbbbbbbb-0000-4000-8000-000000000002");
        using var deployment = FakeGrantDeployment.Answering(
            $$"""
              {"user":"{{User:D}}","sources":[
              {{FakeGrantDeployment.Source("mailfathom.admin.read", "readers", Assignment, group: null, ("deployment", null))}},
              {{FakeGrantDeployment.Source("mailfathom.admin.configuration.write", "operators", inheritedAssignment, "acme-staff", ("organization", Organization), inert: true, pattern: "mailfathom.admin.*.write")}}
              ],"credentials":[{{FakeUserCredentialDeployment.CredentialNamingNothing(credential, "mailfathom.admin.read")}}]}
              """);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "permissions", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(deployment.RequestsTo(HttpMethod.Get, AdminEndpointRoutes.UserPermissionsPath(User)));

        var sources = DrawnListing.ReadFrom(
            this.harness.Console.Lines, "Permission", "Role", "Listed as", "Through", "Scope", "Assignment", "Effect");

        Assert.Equal(["readers", "operators"], sources.Rows.Select(row => sources.Cell(row, "Role")));
        Assert.Equal(["the name", "pattern mailfathom.admin.*.write"], sources.Rows.Select(row => sources.Cell(row, "Listed as")));
        Assert.Equal(["directly", "group acme-staff"], sources.Rows.Select(row => sources.Cell(row, "Through")));
        Assert.Equal(["deployment", $"organization {Organization:D}"], sources.Rows.Select(row => sources.Cell(row, "Scope")));
        Assert.Equal(["held", "reaches nothing at this scope"], sources.Rows.Select(row => sources.Cell(row, "Effect")));

        var credentials = DrawnListing.ReadFrom(
            this.harness.Console.Lines,
            "Credential",
            "Method",
            "Resolved by",
            "Narrows to",
            "Holds",
            "Endpoints",
            "Accepted from",
            "State");
        var row = Assert.Single(credentials.Rows);

        Assert.Equal("everything the user holds", credentials.Cell(row, "Narrows to"));
        Assert.Equal("mailfathom.admin.read", credentials.Cell(row, "Holds"));
    }

    /// <summary>An explanation that left rows out says so, because a listing read as whole is how a grant is missed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserPermissions_AnExplanationCutAtItsCeiling_SaysTheListingIsAPartOfTheAnswer(bool truncated)
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Answering(
            $$"""
              {"user":"{{User:D}}","sources":[
              {{FakeGrantDeployment.Source("mailfathom.admin.read", "readers", Assignment, group: null, ("deployment", null))}}
              ],"sourcesTruncated":{{(truncated ? "true" : "false")}},"credentials":[]}
              """);

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "permissions", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(
            truncated,
            this.harness.Console.Lines.Any(line => line.Contains("a part of the answer", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UserPermissions_AUserTheDeploymentDoesNotHold_SaysSoAndNamesTheListing()
    {
        // Arrange
        using var deployment = FakeGrantDeployment.Refusing(
            HttpStatusCode.NotFound,
            "This deployment holds no such user within your scope.");

        // Act
        var exitCode = await this.RunAsync(deployment, "user", "permissions", "--user", $"{User:D}", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("'mfctl user list'", StringComparison.Ordinal));
    }

    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
