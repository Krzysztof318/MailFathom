// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>Covers the collision a move is refused for, as the statement that finds it.</summary>
/// <remarks>
/// A move is refused when the target scope already holds one of the user's usernames for somebody else. The scope may be
/// none, and a comparison written as plain equality against a null scope would match nothing and let the move reach the
/// unique index instead of the refusal that names the username.
/// </remarks>
public sealed class PersistedOrganizationsTests
{
    private static readonly Guid User = new("11111111-1111-4111-8111-111111111111");

    private static readonly Guid Organization = new("22222222-2222-4222-8222-222222222222");

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

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
