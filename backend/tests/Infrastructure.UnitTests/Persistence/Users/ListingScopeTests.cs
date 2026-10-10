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

/// <summary>Covers which users and organizations an administrative listing keeps for the scopes a caller holds its permission at.</summary>
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
