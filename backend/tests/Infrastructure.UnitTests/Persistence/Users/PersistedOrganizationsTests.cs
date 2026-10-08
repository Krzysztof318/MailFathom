// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>Covers what a move is refused for, as the statements that find it.</summary>
/// <remarks>
/// A move of a user is refused when the target scope already holds one of their usernames for somebody else, and a move
/// of a user or a mail account is refused while it would leave an assignment straddling two organizations. Either scope
/// may be none, and a comparison written as plain SQL equality against a null scope would match nothing — letting a move
/// reach the unique index instead of the refusal that names the username, or letting an assignment straddle "none" and
/// an organization unseen.
/// </remarks>
public sealed class PersistedOrganizationsTests
{
    private static readonly Guid User = new("11111111-1111-4111-8111-111111111111");

    private static readonly Guid Organization = new("22222222-2222-4222-8222-222222222222");

    private static readonly Guid Account = new("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void CollidingUsernames_AMoveOutOfEveryOrganization_ComparesTheHeldScopeAsNone()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.CollidingUsernames(context, User, organizationId: null).ToQueryString();

        // Assert
        Assert.Contains("\"OrganizationId\" IS NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CollidingUsernames_AMoveIntoAnOrganization_ComparesOnlyOtherUsersPasswordsInThatScope()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.CollidingUsernames(context, User, Organization).ToQueryString();

        // Assert
        Assert.Contains("EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("\"UserId\" <> ", sql, StringComparison.Ordinal);
        Assert.Contains("\"Lookup\" = ", sql, StringComparison.Ordinal);
        Assert.Matches(@"\w+\.""OrganizationId"" = @\w+", sql);
        Assert.DoesNotMatch(@"\w+\.""OrganizationId"" = \w+\.""OrganizationId""", sql);
    }

    /// <summary>A user moving out of every organization leaves outside it exactly the accounts that are in one, so an account in none must not count.</summary>
    [Fact]
    public void AccountsAssignedOutside_AMoveOutOfEveryOrganization_CountsOnlyTheAccountsInOne()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.AccountsAssignedOutside(context, User, organizationId: null).ToQueryString();

        // Assert
        Assert.Matches(@"\w+\.""OrganizationId"" IS NOT NULL", sql);
        Assert.Contains("\"MailAccountId\" = ", sql, StringComparison.Ordinal);
        Assert.Contains("\"UserId\" = @", sql, StringComparison.Ordinal);
    }

    /// <summary>A user moving into an organization leaves outside it every other organization's accounts and every account in none; a plain inequality would miss the second, because it is never true of a null.</summary>
    [Fact]
    public void AccountsAssignedOutside_AMoveIntoAnOrganization_CountsAnAccountInNoneAsOutside()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.AccountsAssignedOutside(context, User, Organization).ToQueryString();

        // Assert
        Assert.Matches(@"\w+\.""OrganizationId"" <> @\w+ OR \w+\.""OrganizationId"" IS NULL", sql);
    }

    [Fact]
    public void UsersAssignedOutside_AMoveOutOfEveryOrganization_CountsOnlyTheUsersInOne()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.UsersAssignedOutside(context, Account, organizationId: null).ToQueryString();

        // Assert
        Assert.Matches(@"\w+\.""OrganizationId"" IS NOT NULL", sql);
        Assert.Contains("\"MailAccountId\" = @", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void UsersAssignedOutside_AMoveIntoAnOrganization_CountsAUserInNoneAsOutside()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedOrganizations.UsersAssignedOutside(context, Account, Organization).ToQueryString();

        // Assert
        Assert.Matches(@"\w+\.""OrganizationId"" <> @\w+ OR \w+\.""OrganizationId"" IS NULL", sql);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
