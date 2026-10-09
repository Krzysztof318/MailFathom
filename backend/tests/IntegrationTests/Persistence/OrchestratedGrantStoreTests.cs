// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what the role, group, and assignment store's statements do against a real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every refusal here is a constraint rather than a read: the unique indexes on a role's and a group's name, the
/// assignment's unique index with nulls not distinct, the foreign keys naming what an assignment reaches, and the
/// cascades that take an assignment and a membership with the user or the organization they name. A substitute
/// settles none of them, and the seeded roles exist only where the migration ran.
/// </para>
/// <para>
/// This class shares a database with every other in the collection, so every name it records carries an identifier of
/// its own and every user it needs is provisioned for one test and erased in a <c>finally</c>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedGrantStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly RolePermissions ReadMail = RolePermissions.Of([MailFathomPermission.MailRead]);

    /// <summary>The mail half as the seeding migration wrote it, in ordinal order.</summary>
    private static readonly string[] MailHalf =
    [
        "mailfathom.mail.accounts.write", "mailfathom.mail.ask", "mailfathom.mail.contacts.read",
        "mailfathom.mail.contacts.write", "mailfathom.mail.delete", "mailfathom.mail.drafts.write",
        "mailfathom.mail.flags.write", "mailfathom.mail.folders.write", "mailfathom.mail.move",
        "mailfathom.mail.read", "mailfathom.mail.send",
    ];

    private static readonly AdministrativeListingQuery FirstPage =
        AdministrativeListingQuery.Create(AdministrativeListingQuery.MaximumPageSize, after: null)!;

    /// <summary>
    /// The seeded lists are fixed by the migration that wrote them, so they are stated here as the names it wrote rather
    /// than read from what this build publishes: a later release publishing a permission adds it to none of them, and a
    /// name this build does not publish yet is still on the row, reported rather than granted.
    /// </summary>
    [Fact]
    public async Task ReadRolesAsync_TheSeededRoles_ListExactlyTheNamesTheMigrationWrote()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var roles = await ReadEveryRoleAsync(services, cancellationToken);

        // Assert
        Assert.Equal(MailHalf, ListedNames(roles, "Mail user"));
        Assert.Equal(
            [
                "mailfathom.admin.audit.read", "mailfathom.admin.configuration.write",
                "mailfathom.admin.credentials.write", "mailfathom.admin.erase", "mailfathom.admin.operate",
                "mailfathom.admin.read", "mailfathom.admin.roles.write",
            ],
            ListedNames(roles, "Organization administrator"));
        Assert.Equal(
            [
                "mailfathom.admin.audit.read", "mailfathom.admin.configuration.write",
                "mailfathom.admin.credentials.write", "mailfathom.admin.custody.write", "mailfathom.admin.erase",
                "mailfathom.admin.export", "mailfathom.admin.operate", "mailfathom.admin.read",
                "mailfathom.admin.roles.write", "mailfathom.admin.spend", .. MailHalf,
            ],
            ListedNames(roles, "Administrator"));
    }

    [Fact]
    public async Task CreateRoleAsync_ANameAnotherRoleCarries_IsRefusedByTheIndex()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var name = $"role-{Guid.CreateVersion7():N}";
        var first = Guid.CreateVersion7();

        try
        {
            Assert.Equal(GrantWriteOutcome.Written, (await CreateRoleAsync(services, first, name, cancellationToken)).Outcome);

            // Act
            var second = await CreateRoleAsync(services, Guid.CreateVersion7(), name, cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.NameTaken, second.Outcome);
        }
        finally
        {
            await DeleteRoleAsync(services, first);
        }
    }

    /// <summary>The deployment scope sets neither scope column, so only nulls not distinct keep its assignment from being written twice.</summary>
    [Fact]
    public async Task AssignAsync_TheSameRoleToTheSameUserAtTheDeploymentTwice_IsHeldOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            var principal = AssignmentPrincipal.User(UserId.Create(user));
            var first = await AssignAsync(services, role, principal, AssignmentScope.Deployment, cancellationToken);

            // Act
            var second = await AssignAsync(services, role, principal, AssignmentScope.Deployment, cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.Written, first.Outcome);
            Assert.Equal(GrantWriteOutcome.AlreadyAssigned, second.Outcome);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await DeleteRoleAsync(services, role);
        }
    }

    [Fact]
    public async Task AssignAsync_AScopeThatDoesNotExist_IsRefusedNamingIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            var principal = AssignmentPrincipal.User(UserId.Create(user));

            // Act
            var unknownOrganization = await AssignAsync(
                services,
                role,
                principal,
                AssignmentScope.Organization(Guid.CreateVersion7()),
                cancellationToken);
            var unknownRole = await AssignAsync(
                services,
                Guid.CreateVersion7(),
                principal,
                AssignmentScope.Deployment,
                cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.UnknownOrganization, unknownOrganization.Outcome);
            Assert.Equal(GrantWriteOutcome.UnknownRole, unknownRole.Outcome);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await DeleteRoleAsync(services, role);
        }
    }

    [Fact]
    public async Task DeleteRoleAsync_ARoleStillAssigned_IsRefusedNamingHowManyAssignmentsStandInTheWay()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            await AssignAsync(
                services,
                role,
                AssignmentPrincipal.User(UserId.Create(user)),
                AssignmentScope.User(UserId.Create(user)),
                cancellationToken);

            // Act
            var refused = await services.InScopeAsync(
                (scope, token) => Store(scope).DeleteRoleAsync(role, token),
                cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.StillAssigned, refused.Outcome);
            Assert.Equal(1, refused.StandingAssignments);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>Erasing a user takes the assignments given to them, the assignments made at their scope, and their memberships in the statement that erases them.</summary>
    [Fact]
    public async Task EraseUser_AUserHoldingAssignmentsAndAMembership_LeavesNoneOfThemBehind()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", null, RecordedAt, token),
                cancellationToken);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(user), RecordedAt, token),
                    cancellationToken)).Outcome);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.Deployment, cancellationToken)).Outcome);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.User(UserId.Create(user)), cancellationToken)).Outcome);
            Assert.Equal(2, (await AssignmentsOfRoleAsync(services, role, cancellationToken)).Count);

            // Act
            await OrchestratedForeignUser.EraseAsync(services, user);

            // Assert
            Assert.Empty(await AssignmentsOfRoleAsync(services, role, cancellationToken));
            Assert.Empty((await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGroupMembersAsync(group, FirstPage, token),
                cancellationToken)).Entries);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// Removing a group still assigned would revoke its grant from every member as a side effect, and the foreign key
    /// cascades, so the count under the group's lock is the only thing refusing it.
    /// </summary>
    [Fact]
    public async Task DeleteGroupAsync_AGroupStillAssigned_IsRefusedAndKeepsTheAssignment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var role = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", null, RecordedAt, token),
                    cancellationToken)).Outcome);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken)).Outcome);

            // Act
            var refused = await services.InScopeAsync(
                (scope, token) => Store(scope).DeleteGroupAsync(group, token),
                cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.StillAssigned, refused.Outcome);
            Assert.Equal(1, refused.StandingAssignments);
            Assert.Single(await AssignmentsOfRoleAsync(services, role, cancellationToken));
        }
        finally
        {
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>A group in an organization holds only its members, and removing the organization takes the group and every assignment at its scope with it.</summary>
    [Fact]
    public async Task OrganizationGroups_AMemberAndAnOutsiderAndTheOrganizationsRemoval_AreAdmittedRefusedAndCascaded()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);
        await ProvisionUserAsync(services, colleague, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                        organization,
                        $"Organization {organization:N}",
                        OrganizationShortName.Create($"{organization:N}"),
                        RecordedAt,
                        token),
                    cancellationToken)).Outcome);
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(UserId.Create(colleague), organization, token),
                    cancellationToken)).Outcome);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organization, RecordedAt, token),
                    cancellationToken)).Outcome);
            Assert.Equal(
                GrantWriteOutcome.Written,
                (await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Organization(organization), cancellationToken)).Outcome);
            Assert.Single(await AssignmentsOfRoleAsync(services, role, cancellationToken));

            // Act
            var member = await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(colleague), RecordedAt, token),
                cancellationToken);
            var outsider = await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(user), RecordedAt, token),
                cancellationToken);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            var removed = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                cancellationToken);

            // Assert
            Assert.Equal(GrantWriteOutcome.Written, member.Outcome);
            Assert.Equal(GrantWriteOutcome.OutsideGroupOrganization, outsider.Outcome);
            Assert.Equal(OrganizationWriteOutcome.Written, removed.Outcome);
            Assert.Empty(await AssignmentsOfRoleAsync(services, role, cancellationToken));
            Assert.Equal(
                GrantWriteOutcome.UnknownGroup,
                (await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), cancellationToken)).Outcome);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// A user's grant is the union of what their own assignments and their groups' assignments give, each name at the
    /// scope of the assignment that gave it — so a name reached both ways is held at both scopes, and a group the user
    /// does not belong to gives them nothing.
    /// </summary>
    [Fact]
    public async Task ReadGrantOfAsync_AUserAssignedDirectlyAndThroughAGroup_HoldsTheUnionAtEachAssignmentsScope()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var bystander = Guid.CreateVersion7();
        var mailRole = Guid.CreateVersion7();
        var groupRole = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);
        await ProvisionUserAsync(services, bystander, cancellationToken);

        try
        {
            await CreateRoleAsync(services, mailRole, $"role-{mailRole:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateRoleAsync(
                    groupRole,
                    $"role-{groupRole:N}",
                    RolePermissions.Of([MailFathomPermission.MailRead, MailFathomPermission.AdminRead]),
                    RecordedAt,
                    token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organizationId: null, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(user), RecordedAt, token),
                cancellationToken);
            await AssignAsync(services, mailRole, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.User(UserId.Create(user)), cancellationToken);
            await AssignAsync(services, groupRole, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken);

            // Act
            var held = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(user), token), cancellationToken);
            var bystanderHeld = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(bystander), token), cancellationToken);

            // Assert
            Assert.Equal(
                new HashSet<AssignmentScope> { AssignmentScope.User(UserId.Create(user)), AssignmentScope.Deployment },
                held.ScopesOf(MailFathomPermission.MailRead));
            Assert.Equal([AssignmentScope.Deployment], held.ScopesOf(MailFathomPermission.AdminRead));
            Assert.Equal(2, held.Permissions.Count);
            Assert.Empty(bystanderHeld.Permissions);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await OrchestratedForeignUser.EraseAsync(services, bystander);
            await RevokeEveryAssignmentOfRoleAsync(services, groupRole);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, mailRole);
            await DeleteRoleAsync(services, groupRole);
        }
    }

    private static IGrantStore Store(IServiceProvider scope) => scope.GetRequiredService<IGrantStore>();

    private static string[] ListedNames(IReadOnlyList<Role> roles, string name)
    {
        var role = Assert.Single(roles, candidate => candidate.Name == name);

        return
        [
            .. role.Permissions.Granted
                .Select(permission => permission.Name)
                .Concat(role.Permissions.Unpublished)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static async Task<IReadOnlyList<Role>> ReadEveryRoleAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        List<Role> roles = [];
        Guid? after = null;

        do
        {
            var query = AdministrativeListingQuery.Create(AdministrativeListingQuery.MaximumPageSize, after)!;
            var page = await services.InScopeAsync((scope, token) => Store(scope).ReadRolesAsync(query, token), cancellationToken);
            roles.AddRange(page.Entries);
            after = page.ContinuesAfter;
        }
        while (after is not null);

        return roles;
    }

    private static async Task<IReadOnlyList<RoleAssignment>> AssignmentsOfRoleAsync(
        OrchestratedMailFathomServices services,
        Guid role,
        CancellationToken cancellationToken)
    {
        List<RoleAssignment> assignments = [];
        Guid? after = null;

        do
        {
            var query = AdministrativeListingQuery.Create(AdministrativeListingQuery.MaximumPageSize, after)!;
            var page = await services.InScopeAsync((scope, token) => Store(scope).ReadAssignmentsAsync(query, token), cancellationToken);
            assignments.AddRange(page.Entries.Where(assignment => assignment.RoleId == role));
            after = page.ContinuesAfter;
        }
        while (after is not null);

        return assignments;
    }

    private static async Task ProvisionUserAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken));

    private static Task<GrantWriteResult> CreateRoleAsync(
        OrchestratedMailFathomServices services,
        Guid role,
        string name,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => Store(scope).CreateRoleAsync(role, name, ReadMail, RecordedAt, token),
            cancellationToken);

    private static Task<GrantWriteResult> AssignAsync(
        OrchestratedMailFathomServices services,
        Guid role,
        AssignmentPrincipal principal,
        AssignmentScope scope,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (serviceScope, token) => Store(serviceScope).AssignAsync(Guid.CreateVersion7(), role, principal, scope, RecordedAt, token),
            cancellationToken);

    /// <summary>Uncancellable because it runs in a <c>finally</c>, for the reason <see cref="OrchestratedForeignUser.EraseAsync" /> is.</summary>
    private static async Task RevokeEveryAssignmentOfRoleAsync(OrchestratedMailFathomServices services, Guid role)
    {
        foreach (var assignment in await AssignmentsOfRoleAsync(services, role, CancellationToken.None))
        {
            await services.InScopeAsync((scope, token) => Store(scope).RevokeAsync(assignment.Id, token), CancellationToken.None);
        }
    }

    /// <summary>Uncancellable because it runs in a <c>finally</c>, for the reason <see cref="OrchestratedForeignUser.EraseAsync" /> is.</summary>
    private static Task<GrantWriteResult> DeleteRoleAsync(OrchestratedMailFathomServices services, Guid role) =>
        services.InScopeAsync((scope, token) => Store(scope).DeleteRoleAsync(role, token), CancellationToken.None);
}
