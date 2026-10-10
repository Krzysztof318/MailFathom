// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>Covers which users, organizations, and mail accounts an administrative listing keeps for the scopes a caller holds its permission at.</summary>
/// <remarks>
/// The predicate is evaluated over rows held in memory, which settles which rows it keeps; that the provider translates
/// it at all is asked of the composed statement, because a predicate it cannot translate raises rather than answering.
/// </remarks>
public sealed class ListingScopeTests
{
    private static readonly Guid OneOrganization = new("0198f0aa-0000-7000-8000-0000000002a1");

    private static readonly Guid AnotherOrganization = new("0198f0aa-0000-7000-8000-0000000002a2");

    private static readonly Guid Member = new("0198f0aa-0000-7000-8000-0000000002b1");

    private static readonly Guid Outsider = new("0198f0aa-0000-7000-8000-0000000002b2");

    private static readonly Guid Unaffiliated = new("0198f0aa-0000-7000-8000-0000000002b3");

    private static readonly UserAccountEntity[] Users =
    [
        UserIn(Member, OneOrganization),
        UserIn(Outsider, AnotherOrganization),
        UserIn(Unaffiliated, organizationId: null),
    ];

    private static readonly OrganizationEntity[] Organizations =
    [
        OrganizationOf(OneOrganization),
        OrganizationOf(AnotherOrganization),
    ];

    private static readonly Guid HeldAlone = new("0198f0aa-0000-7000-8000-0000000002c1");

    private static readonly Guid Shared = new("0198f0aa-0000-7000-8000-0000000002c2");

    private static readonly Guid Unassigned = new("0198f0aa-0000-7000-8000-0000000002c3");

    private static readonly Guid Elsewhere = new("0198f0aa-0000-7000-8000-0000000002c4");

    private static readonly Guid InNoOrganization = new("0198f0aa-0000-7000-8000-0000000002c5");

    private static readonly MailAccountRecordEntity[] MailAccounts =
    [
        MailAccountIn(HeldAlone, OneOrganization),
        MailAccountIn(Shared, OneOrganization),
        MailAccountIn(Unassigned, OneOrganization),
        MailAccountIn(Elsewhere, AnotherOrganization),
        MailAccountIn(InNoOrganization, organizationId: null),
    ];

    private static readonly MailAccountAssignmentEntity[] Assignments =
    [
        Assigned(HeldAlone, Member),
        Assigned(Shared, Member),
        Assigned(Shared, Outsider),
        Assigned(Elsewhere, Outsider),
        Assigned(InNoOrganization, Unaffiliated),
    ];

    [Fact]
    public void Covering_UsersWithinTheDeploymentScope_KeepsEveryUser()
    {
        // Act
        var kept = ListingScope.Covering(Users.AsQueryable(), Scopes(AssignmentScope.Deployment));

        // Assert
        Assert.Equal([Member, Outsider, Unaffiliated], kept.Select(user => user.Id));
    }

    [Fact]
    public void Covering_UsersWithinOneOrganizationsScope_KeepsItsMembersAlone()
    {
        // Act
        var kept = ListingScope.Covering(Users.AsQueryable(), Scopes(AssignmentScope.Organization(OneOrganization)));

        // Assert
        Assert.Equal([Member], kept.Select(user => user.Id));
    }

    /// <summary>A user scope reaches its user wherever they belong, including nowhere.</summary>
    [Fact]
    public void Covering_UsersWithinAnOrganizationAndAUserScope_KeepsTheMembersAndTheUserNamed()
    {
        // Act
        var kept = ListingScope.Covering(
            Users.AsQueryable(),
            Scopes(AssignmentScope.Organization(OneOrganization), AssignmentScope.User(UserId.Create(Unaffiliated))));

        // Assert
        Assert.Equal([Member, Unaffiliated], kept.Select(user => user.Id));
    }

    [Fact]
    public void Covering_UsersWithinNoScope_KeepsNobody()
    {
        // Act
        var kept = ListingScope.Covering(Users.AsQueryable(), Scopes());

        // Assert
        Assert.Empty(kept);
    }

    [Fact]
    public void Covering_OrganizationsWithinTheDeploymentScope_KeepsEveryOrganization()
    {
        // Act
        var kept = ListingScope.Covering(Organizations.AsQueryable(), Scopes(AssignmentScope.Deployment));

        // Assert
        Assert.Equal([OneOrganization, AnotherOrganization], kept.Select(organization => organization.Id));
    }

    /// <summary>A user scope covers no organization, so a caller holding the permission only over people lists none.</summary>
    [Fact]
    public void Covering_OrganizationsWithinAnOrganizationAndAUserScope_KeepsThatOrganizationAlone()
    {
        // Act
        var kept = ListingScope.Covering(
            Organizations.AsQueryable(),
            Scopes(AssignmentScope.Organization(OneOrganization), AssignmentScope.User(UserId.Create(Member))));

        // Assert
        Assert.Equal([OneOrganization], kept.Select(organization => organization.Id));
    }

    [Fact]
    public void Covering_MailAccountsWithinTheDeploymentScope_KeepsEveryAccount()
    {
        // Act
        var kept = ListingScope.Covering(MailAccounts.AsQueryable(), Assignments.AsQueryable(), Scopes(AssignmentScope.Deployment));

        // Assert
        Assert.Equal([HeldAlone, Shared, Unassigned, Elsewhere, InNoOrganization], kept.Select(account => account.Id));
    }

    /// <summary>An organization covers every account that belongs to it, however many people read it and whether anybody does.</summary>
    [Fact]
    public void Covering_MailAccountsWithinOneOrganizationsScope_KeepsTheAccountsThatBelongToIt()
    {
        // Act
        var kept = ListingScope.Covering(
            MailAccounts.AsQueryable(),
            Assignments.AsQueryable(),
            Scopes(AssignmentScope.Organization(OneOrganization)));

        // Assert
        Assert.Equal([HeldAlone, Shared, Unassigned], kept.Select(account => account.Id));
    }

    /// <summary>A mailbox somebody else reads as well is not one person's to administer, so a user scope leaves it out.</summary>
    [Fact]
    public void Covering_MailAccountsWithinAUserScope_KeepsTheAccountAssignedToThatUserAlone()
    {
        // Act
        var kept = ListingScope.Covering(
            MailAccounts.AsQueryable(),
            Assignments.AsQueryable(),
            Scopes(AssignmentScope.User(UserId.Create(Member))));

        // Assert
        Assert.Equal([HeldAlone], kept.Select(account => account.Id));
    }

    /// <summary>An account in no organization is the deployment's alone, even for a scope naming the one user it is assigned to.</summary>
    [Fact]
    public void Covering_MailAccountsInNoOrganizationWithinTheScopeOfTheirOnlyUser_KeepsNone()
    {
        // Act
        var kept = ListingScope.Covering(
            MailAccounts.AsQueryable(),
            Assignments.AsQueryable(),
            Scopes(AssignmentScope.User(UserId.Create(Unaffiliated))));

        // Assert
        Assert.Empty(kept);
    }

    [Fact]
    public void Covering_MailAccountsWithinNoScope_KeepsNone()
    {
        // Act
        var kept = ListingScope.Covering(MailAccounts.AsQueryable(), Assignments.AsQueryable(), Scopes());

        // Assert
        Assert.Empty(kept);
    }

    [Fact]
    public void Covering_MailAccountsWithinAnOrganizationAndAUserScope_ComposesAStatementTheProviderTranslates()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = ListingScope.Covering(
                context.MailAccountRecords,
                context.MailAccountAssignments,
                Scopes(AssignmentScope.Organization(OneOrganization), AssignmentScope.User(UserId.Create(Member))))
            .ToQueryString();

        // Assert
        Assert.Contains("\"OrganizationId\"", sql, StringComparison.Ordinal);
        Assert.Contains(MailAccountAssignmentEntity.TableName, sql, StringComparison.Ordinal);
        Assert.Contains("ANY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Covering_UsersWithinAnOrganizationAndAUserScope_ComposesAStatementTheProviderTranslates()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = ListingScope.Covering(
                context.UserAccounts,
                Scopes(AssignmentScope.Organization(OneOrganization), AssignmentScope.User(UserId.Create(Unaffiliated))))
            .ToQueryString();

        // Assert
        Assert.Contains("\"OrganizationId\"", sql, StringComparison.Ordinal);
        Assert.Contains("ANY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Covering_OrganizationsWithinTheDeploymentScope_ComposesNoPredicate()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = ListingScope.Covering(context.Organizations, Scopes(AssignmentScope.Deployment)).ToQueryString();

        // Assert
        Assert.DoesNotContain("WHERE", sql, StringComparison.Ordinal);
    }

    private static HashSet<AssignmentScope> Scopes(params AssignmentScope[] scopes) => [.. scopes];

    private static UserAccountEntity UserIn(Guid id, Guid? organizationId) => new()
    {
        Id = id,
        DisplayName = $"user-{id:D}",
        Document = "{}",
        OrganizationId = organizationId,
    };

    private static MailAccountRecordEntity MailAccountIn(Guid id, Guid? organizationId) => new()
    {
        Id = id,
        DisplayName = $"account-{id:D}",
        Document = "{}",
        OrganizationId = organizationId,
    };

    private static MailAccountAssignmentEntity Assigned(Guid account, Guid user) => new()
    {
        MailAccountId = account,
        UserId = user,
    };

    private static OrganizationEntity OrganizationOf(Guid id) => new()
    {
        Id = id,
        DisplayName = $"organization-{id:D}",
        ShortName = $"ORG{id.ToString("N")[^4..]}",
    };

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
