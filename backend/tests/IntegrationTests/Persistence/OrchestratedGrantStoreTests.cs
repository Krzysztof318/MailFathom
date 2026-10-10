// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Application.Access.Grants;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Migrations;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
    /// <summary>A well-formed pattern over a prefix no build publishes anything beneath.</summary>
    private const string DormantPattern = "mailfathom.retired.*";

    /// <summary>How many names the seeding migration wrote into <c>Administrator</c>, which is what its rewrite restores on the way down.</summary>
    private const int SeededAdministratorNames = 21;

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
    /// The two lists written out by name are fixed by the migration that wrote them, so they are stated here as the names
    /// it wrote rather than read from what this build publishes: a later release publishing a permission adds it to
    /// neither, and a name this build does not publish yet is still on the row, reported rather than granted.
    /// <c>Administrator</c> is the one the later migration rewrote, and what proves the rewrite ran is the entry as it is
    /// stored: the single pattern, which reaches everything this build publishes rather than a list that happens to.
    /// </summary>
    [Fact]
    public async Task ReadRolesAsync_TheSeededRoles_ListWhatTheMigrationsWrote()
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
        var administrator = Assert.Single(roles, role => role.Name == "Administrator").Permissions;

        Assert.Equal(["*"], administrator.Written);
        Assert.Equal(MailFathomPermission.All, administrator.Granted);
        Assert.Empty(administrator.Unpublished);
    }

    /// <summary>
    /// The rewrite of the seeded <c>Administrator</c> list is run here as the migration states it, against the list in
    /// each shape an operator may have left it, because a guard that let one through would hand every holder of a
    /// deliberately narrowed role every permission on upgrade — and would do it without a failure anywhere. The first
    /// step is the control: the list exactly as seeded is rewritten, so the lists left alone are left alone by the guard
    /// rather than by a statement that does nothing. Everything runs in one transaction that is rolled back, since the
    /// row is the one the default administrator holds.
    /// </summary>
    [Fact]
    public async Task RewriteAdministratorRoleAsAPattern_AListSomebodyChanged_IsLeftAsItStandsWhileTheSeededOneIsRewritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var lists = await services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var administrator = PersistedDefaultAdministrator.AdministratorRoleId;
                var rewrite = dbContext.GetService<IMigrationsAssembly>().CreateMigration(
                    typeof(RewriteAdministratorRoleAsAPattern).GetTypeInfo(),
                    dbContext.Database.ProviderName!);
                var up = Assert.Single(rewrite.UpOperations.OfType<SqlOperation>()).Sql;
                var down = Assert.Single(rewrite.DownOperations.OfType<SqlOperation>()).Sql;

                Task<string[]> ListAsync() => dbContext.RolePermissions
                    .AsNoTracking()
                    .Where(listed => listed.RoleId == administrator)
                    .Select(listed => listed.Permission)
                    .ToArrayAsync(token);

                await using var transaction = await dbContext.Database.BeginTransactionAsync(token);

                await dbContext.Database.ExecuteSqlRawAsync(down, token);
                var seeded = await ListAsync();
                await dbContext.Database.ExecuteSqlRawAsync(up, token);
                var rewritten = await ListAsync();

                await dbContext.Database.ExecuteSqlRawAsync(down, token);
                await dbContext.Database.ExecuteSqlAsync(
                    $"""DELETE FROM role_permissions WHERE "RoleId" = {administrator} AND "Permission" = 'mailfathom.admin.spend'""",
                    token);
                await dbContext.Database.ExecuteSqlRawAsync(up, token);
                var narrowed = await ListAsync();

                await dbContext.Database.ExecuteSqlAsync(
                    $"""INSERT INTO role_permissions ("RoleId", "Permission") VALUES ({administrator}, 'mailfathom.mail.*')""",
                    token);
                await dbContext.Database.ExecuteSqlRawAsync(up, token);
                var exchanged = await ListAsync();

                await dbContext.Database.ExecuteSqlAsync(
                    $"""DELETE FROM role_permissions WHERE "RoleId" = {administrator}""",
                    token);
                await dbContext.Database.ExecuteSqlRawAsync(up, token);
                var emptied = await ListAsync();

                await transaction.RollbackAsync(CancellationToken.None);

                return (Seeded: seeded, Rewritten: rewritten, Narrowed: narrowed, Exchanged: exchanged, Emptied: emptied);
            },
            cancellationToken);

        // Assert
        Assert.Equal(SeededAdministratorNames, lists.Seeded.Length);
        Assert.DoesNotContain("*", lists.Seeded);
        Assert.Equal(["*"], lists.Rewritten);
        Assert.Equal(lists.Seeded.Length - 1, lists.Narrowed.Length);
        Assert.DoesNotContain("*", lists.Narrowed);
        Assert.DoesNotContain("mailfathom.admin.spend", lists.Narrowed);
        Assert.Equal(lists.Seeded.Length, lists.Exchanged.Length);
        Assert.DoesNotContain("*", lists.Exchanged);
        Assert.Contains("mailfathom.mail.*", lists.Exchanged);
        Assert.Empty(lists.Emptied);
    }

    /// <summary>
    /// A stored pattern reaching nothing in this build still makes its role one a release can widen, which is decided
    /// after the statement rather than in it: the entry is told from a name by its wildcard and from a malformed entry
    /// by its syntax, never by what it grants today. A decision made on today's reach would let an organization's
    /// administrator add members to a group whose role a later release then activates.
    /// </summary>
    [Fact]
    public async Task HoldsWideningRoleAsync_ARoleWhoseOnlyPatternReachesNothingInThisBuild_StillCountsForEveryPrincipalHoldingIt()
    {
        // Arrange
        var dormantPattern = DormantPattern;
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);
        await ProvisionUserAsync(services, member, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $"""INSERT INTO role_permissions ("RoleId", "Permission") VALUES ({role}, {dormantPattern})""",
                    token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organizationId: null, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(member), RecordedAt, token),
                cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.User(UserId.Create(user)), cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken);

            // Act
            var stored = Assert.Single(await ReadEveryRoleAsync(services, cancellationToken), candidate => candidate.Id == role);
            var held = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(user), token), cancellationToken);
            var directly = await HoldsWideningRoleAsync(services, AssignmentPrincipal.User(UserId.Create(user)), cancellationToken);
            var throughTheGroup = await HoldsWideningRoleAsync(services, AssignmentPrincipal.User(UserId.Create(member)), cancellationToken);
            var theGroup = await HoldsWideningRoleAsync(services, AssignmentPrincipal.Group(group), cancellationToken);

            // Assert
            Assert.Equal([DormantPattern], stored.Permissions.Unpublished);
            Assert.Equal(MailFathomPermission.MailRead, Assert.Single(held.Permissions));
            Assert.True(directly);
            Assert.True(throughTheGroup);
            Assert.True(theGroup);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await OrchestratedForeignUser.EraseAsync(services, member);
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// A pattern is one row holding what was written, and everything a grant is read by resolves it: what the holder
    /// holds at the assignment's scope, which entry each permission is traced to, and that the role is one a release
    /// can widen — asked of a user holding it directly, of one holding it only through a group, and of the group.
    /// </summary>
    [Fact]
    public async Task CreateRoleAsync_AListCarryingAPattern_IsStoredAsWrittenAndResolvedWhereverAGrantIsRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();
        var bystander = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var namedRole = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var assignment = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);
        await ProvisionUserAsync(services, member, cancellationToken);
        await ProvisionUserAsync(services, bystander, cancellationToken);
        Assert.True(RolePermissions.TryCreate(["mailfathom.mail.read", "mailfathom.mail.contacts.*"], out var listed, out _));

        try
        {
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateRoleAsync(role, $"role-{role:N}", listed!, RecordedAt, token),
                cancellationToken);
            await CreateRoleAsync(services, namedRole, $"role-{namedRole:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organizationId: null, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(member), RecordedAt, token),
                cancellationToken);
            await AssignAsync(services, assignment, role, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.User(UserId.Create(user)), cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken);
            await AssignAsync(services, namedRole, AssignmentPrincipal.User(UserId.Create(bystander)), AssignmentScope.Deployment, cancellationToken);

            // Act
            var stored = Assert.Single(await ReadEveryRoleAsync(services, cancellationToken), candidate => candidate.Id == role);
            var held = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(user), token), cancellationToken);
            var sources = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGrantSourcesOfAsync(UserId.Create(user), GrantListingReach.Deployment, 100, token),
                cancellationToken);
            var bounded = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGrantSourcesOfAsync(UserId.Create(user), GrantListingReach.Deployment, 2, token),
                cancellationToken);
            var directly = await HoldsWideningRoleAsync(services, AssignmentPrincipal.User(UserId.Create(user)), cancellationToken);
            var throughTheGroup = await HoldsWideningRoleAsync(services, AssignmentPrincipal.User(UserId.Create(member)), cancellationToken);
            var theGroup = await HoldsWideningRoleAsync(services, AssignmentPrincipal.Group(group), cancellationToken);
            var byNameAlone = await HoldsWideningRoleAsync(services, AssignmentPrincipal.User(UserId.Create(bystander)), cancellationToken);

            // Assert
            Assert.Equal(["mailfathom.mail.read", "mailfathom.mail.contacts.*"], stored.Permissions.Written);
            Assert.Equal(
                new HashSet<MailFathomPermission>
                {
                    MailFathomPermission.MailRead, MailFathomPermission.MailContactsRead, MailFathomPermission.MailContactsWrite,
                },
                held.Permissions.ToHashSet());
            Assert.Equal([AssignmentScope.User(UserId.Create(user))], held.ScopesOf(MailFathomPermission.MailContactsWrite));
            Assert.Equal(
                new HashSet<(string, string?, Guid)>
                {
                    ("mailfathom.mail.read", null, assignment),
                    ("mailfathom.mail.contacts.read", "mailfathom.mail.contacts.*", assignment),
                    ("mailfathom.mail.contacts.write", "mailfathom.mail.contacts.*", assignment),
                },
                sources.Select(source => (source.Permission.Name, source.Pattern, source.AssignmentId)).ToHashSet());
            Assert.Equal(2, bounded.Count);
            Assert.True(directly);
            Assert.True(throughTheGroup);
            Assert.True(theGroup);
            Assert.False(byNameAlone);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await OrchestratedForeignUser.EraseAsync(services, member);
            await OrchestratedForeignUser.EraseAsync(services, bystander);
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, role);
            await DeleteRoleAsync(services, namedRole);
        }
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
    /// does not belong to gives them nothing. The roles read through the same assignments name each role once per scope,
    /// a role given both directly and through a group included, in name order.
    /// </summary>
    [Fact]
    public async Task ReadGrantOfAsync_AUserAssignedDirectlyAndThroughAGroup_HoldsTheUnionAtEachAssignmentsScopeAndNamesEachRole()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var bystander = Guid.CreateVersion7();
        var mailRole = Guid.CreateVersion7();
        var groupRole = Guid.CreateVersion7();
        var groupOnlyRole = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);
        await ProvisionUserAsync(services, bystander, cancellationToken);

        try
        {
            await CreateRoleAsync(services, mailRole, $"role-{mailRole:N}", cancellationToken);
            await CreateRoleAsync(services, groupOnlyRole, $"role-{groupOnlyRole:N}", cancellationToken);
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
            await AssignAsync(services, groupRole, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.Deployment, cancellationToken);
            await AssignAsync(services, groupOnlyRole, AssignmentPrincipal.Group(group), AssignmentScope.User(UserId.Create(bystander)), cancellationToken);

            // Act
            var held = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(user), token), cancellationToken);
            var bystanderHeld = await services.InScopeAsync((scope, token) => Store(scope).ReadGrantOfAsync(UserId.Create(bystander), token), cancellationToken);
            var roles = await services.InScopeAsync((scope, token) => Store(scope).ReadRolesHeldByAsync(UserId.Create(user), token), cancellationToken);
            var bystanderRoles = await services.InScopeAsync((scope, token) => Store(scope).ReadRolesHeldByAsync(UserId.Create(bystander), token), cancellationToken);

            // Assert
            Assert.Equal(
                new HashSet<AssignmentScope>
                {
                    AssignmentScope.User(UserId.Create(user)),
                    AssignmentScope.Deployment,
                    AssignmentScope.User(UserId.Create(bystander)),
                },
                held.ScopesOf(MailFathomPermission.MailRead));
            Assert.Equal([AssignmentScope.Deployment], held.ScopesOf(MailFathomPermission.AdminRead));
            Assert.Equal(2, held.Permissions.Count);
            Assert.Empty(bystanderHeld.Permissions);
            HeldRole[] expectedRoles =
            [
                new($"role-{mailRole:N}", AssignmentScope.User(UserId.Create(user))),
                new($"role-{groupRole:N}", AssignmentScope.Deployment),
                new($"role-{groupOnlyRole:N}", AssignmentScope.User(UserId.Create(bystander))),
            ];
            Assert.Equal(expectedRoles.OrderBy(role => role.Name, StringComparer.Ordinal), roles);
            Assert.Empty(bystanderRoles);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await OrchestratedForeignUser.EraseAsync(services, bystander);
            await RevokeEveryAssignmentOfRoleAsync(services, groupRole);
            await RevokeEveryAssignmentOfRoleAsync(services, groupOnlyRole);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, mailRole);
            await DeleteRoleAsync(services, groupRole);
            await DeleteRoleAsync(services, groupOnlyRole);
        }
    }

    /// <summary>
    /// A grant this process remembered is forgotten by the revocation that ends it, so the next request reads what was
    /// committed rather than what the cache held — the store announcing its own write is what makes that so.
    /// </summary>
    [Fact]
    public async Task UserGrantResolver_AnAssignmentRevokedAfterTheGrantWasRemembered_ResolvesWithoutIt()
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
            await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.User(UserId.Create(user)), cancellationToken);
            var before = await ResolveAsync(services, user, cancellationToken);

            // Act
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            var after = await ResolveAsync(services, user, cancellationToken);

            // Assert
            Assert.Equal([MailFathomPermission.MailRead], before.Permissions);
            Assert.Empty(after.Permissions);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// Narrowing a role and ending a membership each take a permission away, so each forgets what this process
    /// remembered — a write that kept the grant cached would go on serving the removed name until the interval.
    /// </summary>
    [Fact]
    public async Task UserGrantResolver_ARoleNarrowedThenAMembershipEnded_ResolvesWithoutWhatEachTookAway()
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
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateRoleAsync(
                    role,
                    $"role-{role:N}",
                    RolePermissions.Of([MailFathomPermission.MailRead, MailFathomPermission.MailAsk]),
                    RecordedAt,
                    token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organizationId: null, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(user), RecordedAt, token),
                cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken);
            var before = await ResolveAsync(services, user, cancellationToken);

            // Act
            await services.InScopeAsync(
                (scope, token) => Store(scope).ReplaceRolePermissionsAsync(role, ReadMail, token),
                cancellationToken);
            var afterNarrowing = await ResolveAsync(services, user, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).RemoveGroupMemberAsync(group, UserId.Create(user), token),
                cancellationToken);
            var afterLeaving = await ResolveAsync(services, user, cancellationToken);

            // Assert
            Assert.Equal(
                new HashSet<MailFathomPermission> { MailFathomPermission.MailRead, MailFathomPermission.MailAsk },
                before.Permissions);
            Assert.Equal([MailFathomPermission.MailRead], afterNarrowing.Permissions);
            Assert.Empty(afterLeaving.Permissions);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await RevokeEveryAssignmentOfRoleAsync(services, role);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// Moving a user out of an organization leaves their memberships of its groups behind, and those grant nothing once
    /// the user is no longer in it — the move also forgets what this process remembered, so the next request reads that.
    /// </summary>
    [Fact]
    public async Task UserGrantResolver_AUserMovedOutOfTheOrganizationWhoseGroupGrantedThem_ResolvesWithoutIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(UserId.Create(user), organization, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(group, $"group-{group:N}", organization, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).AddGroupMemberAsync(group, UserId.Create(user), RecordedAt, token),
                cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.Group(group), AssignmentScope.Organization(organization), cancellationToken);
            var before = await ResolveAsync(services, user, cancellationToken);

            // Act
            var moved = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(UserId.Create(user), null, token),
                cancellationToken);
            var after = await ResolveAsync(services, user, cancellationToken);

            // Assert
            Assert.Equal([MailFathomPermission.MailRead], before.Permissions);
            Assert.Equal(OrganizationWriteOutcome.Written, moved.Outcome);
            Assert.Empty(after.Permissions);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// An organization's administrator lists what is inside their organization and nothing beside it: an assignment
    /// counts only when both what it is given to and where it applies are inside, so the same member's deployment-wide
    /// assignment is left out, an outsider's is left out whether it applies to themselves or inside the organization, and
    /// so is a group in no organization.
    /// </summary>
    [Fact]
    public async Task ReadWithinAReach_OneOrganization_ListsTheGroupsAndAssignmentsInsideItAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var member = Guid.CreateVersion7();
        var outsider = Guid.CreateVersion7();
        var role = Guid.CreateVersion7();
        var organizationGroup = Guid.CreateVersion7();
        var deploymentGroup = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        await ProvisionUserAsync(services, member, cancellationToken);
        await ProvisionUserAsync(services, outsider, cancellationToken);

        try
        {
            await CreateRoleAsync(services, role, $"role-{role:N}", cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(UserId.Create(member), organization, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(organizationGroup, $"group-{organizationGroup:N}", organization, RecordedAt, token),
                cancellationToken);
            await services.InScopeAsync(
                (scope, token) => Store(scope).CreateGroupAsync(deploymentGroup, $"group-{deploymentGroup:N}", organizationId: null, RecordedAt, token),
                cancellationToken);
            var inside = Guid.CreateVersion7();
            await AssignAsync(services, inside, role, AssignmentPrincipal.User(UserId.Create(member)), AssignmentScope.Organization(organization), cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(member)), AssignmentScope.Deployment, cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(outsider)), AssignmentScope.User(UserId.Create(outsider)), cancellationToken);
            await AssignAsync(services, role, AssignmentPrincipal.User(UserId.Create(outsider)), AssignmentScope.Organization(organization), cancellationToken);
            var reach = GrantListingReach.Within([AssignmentScope.Organization(organization)]);

            // Act
            var assignments = await AssignmentsOfRoleWithinAsync(services, role, reach, cancellationToken);
            var groups = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGroupsAsync(FirstPage, reach, token),
                cancellationToken);
            var placement = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadUserPlacementAsync(UserId.Create(member), token),
                cancellationToken);

            // Assert
            Assert.Equal(inside, Assert.Single(assignments).Id);
            Assert.Equal([organizationGroup], groups.Entries.Select(group => group.Id));
            Assert.Equal(new UserPlacement(UserId.Create(member), organization), placement);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, member);
            await OrchestratedForeignUser.EraseAsync(services, outsider);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(deploymentGroup, token), CancellationToken.None);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
            await DeleteRoleAsync(services, role);
        }
    }

    /// <summary>
    /// Each name a user holds is traced to the role and the assignment that gave it, and to the group it came through
    /// where it came through one. A reader covering the user alone reads the assignment made at the user's own scope and
    /// nothing a deployment's group gave, and a limit is the most rows any reader is answered with.
    /// </summary>
    [Fact]
    public async Task ReadGrantSourcesOfAsync_AUserAssignedDirectlyAndThroughAGroup_NamesEverySourceWithinTheReachUpToTheLimit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var mailRole = Guid.CreateVersion7();
        var groupRole = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        await ProvisionUserAsync(services, user, cancellationToken);

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
            var direct = Guid.CreateVersion7();
            var throughGroup = Guid.CreateVersion7();
            await AssignAsync(services, direct, mailRole, AssignmentPrincipal.User(UserId.Create(user)), AssignmentScope.User(UserId.Create(user)), cancellationToken);
            await AssignAsync(services, throughGroup, groupRole, AssignmentPrincipal.Group(group), AssignmentScope.Deployment, cancellationToken);

            // Act
            var sources = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGrantSourcesOfAsync(UserId.Create(user), GrantListingReach.Deployment, 100, token),
                cancellationToken);
            var withinTheUser = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGrantSourcesOfAsync(
                    UserId.Create(user),
                    GrantListingReach.Within([AssignmentScope.User(UserId.Create(user))]),
                    100,
                    token),
                cancellationToken);
            var bounded = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadGrantSourcesOfAsync(UserId.Create(user), GrantListingReach.Deployment, 2, token),
                cancellationToken);

            // Assert
            Assert.Equal(
                new HashSet<(string, Guid, Guid, Guid?, AssignmentScope)>
                {
                    ("mailfathom.mail.read", mailRole, direct, null, AssignmentScope.User(UserId.Create(user))),
                    ("mailfathom.mail.read", groupRole, throughGroup, group, AssignmentScope.Deployment),
                    ("mailfathom.admin.read", groupRole, throughGroup, group, AssignmentScope.Deployment),
                },
                sources.Select(source => (source.Permission.Name, source.RoleId, source.AssignmentId, source.GroupId, source.Scope)).ToHashSet());
            Assert.Equal(direct, Assert.Single(withinTheUser).AssignmentId);
            Assert.Equal(2, bounded.Count);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await RevokeEveryAssignmentOfRoleAsync(services, groupRole);
            await services.InScopeAsync((scope, token) => Store(scope).DeleteGroupAsync(group, token), CancellationToken.None);
            await DeleteRoleAsync(services, mailRole);
            await DeleteRoleAsync(services, groupRole);
        }
    }

    private static Task<ScopedGrant> ResolveAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<UserGrantResolver>().ResolveAsync(UserId.Create(user), token),
            cancellationToken);

    private static IGrantStore Store(IServiceProvider scope) => scope.GetRequiredService<IGrantStore>();

    private static Task<bool> HoldsWideningRoleAsync(
        OrchestratedMailFathomServices services,
        AssignmentPrincipal principal,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => Store(scope).HoldsWideningRoleAsync(principal, token),
            cancellationToken);

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

    private static Task<IReadOnlyList<RoleAssignment>> AssignmentsOfRoleAsync(
        OrchestratedMailFathomServices services,
        Guid role,
        CancellationToken cancellationToken) =>
        AssignmentsOfRoleWithinAsync(services, role, GrantListingReach.Deployment, cancellationToken);

    private static async Task<IReadOnlyList<RoleAssignment>> AssignmentsOfRoleWithinAsync(
        OrchestratedMailFathomServices services,
        Guid role,
        GrantListingReach reach,
        CancellationToken cancellationToken)
    {
        List<RoleAssignment> assignments = [];
        Guid? after = null;

        do
        {
            var query = AdministrativeListingQuery.Create(AdministrativeListingQuery.MaximumPageSize, after)!;
            var page = await services.InScopeAsync(
                (scope, token) => Store(scope).ReadAssignmentsAsync(query, reach, token),
                cancellationToken);
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
        CancellationToken cancellationToken) =>
        AssignAsync(services, Guid.CreateVersion7(), role, principal, scope, cancellationToken);

    private static Task<GrantWriteResult> AssignAsync(
        OrchestratedMailFathomServices services,
        Guid assignment,
        Guid role,
        AssignmentPrincipal principal,
        AssignmentScope scope,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (serviceScope, token) => Store(serviceScope).AssignAsync(assignment, role, principal, scope, RecordedAt, token),
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
