// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>Covers which password credential a login resolves, as the statement a sign-in sends.</summary>
/// <remarks>
/// The rows a statement matches need a real server, but what it compares does not: a login naming no organization must
/// never reach an organization's <c>jan</c>, and a login naming one must reach it through the organization's current
/// short name rather than through anything the credential stored — which is what lets a renamed short name carry every
/// member's login with it.
/// </remarks>
public sealed class PersistedUserCredentialsTests
{
    [Fact]
    public void PasswordCredentialsAnswering_ALoginNamingNoOrganization_MatchesOnlyCredentialsScopedToNone()
    {
        // Arrange
        using var context = DesignTimeContext();
        UserCredentialLogin.TryRead("jan", out var login);

        // Act
        var sql = PersistedUserCredentials.PasswordCredentialsAnswering(context, login).ToQueryString();

        // Assert
        Assert.Contains("\"OrganizationId\" IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("organizations", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordCredentialsAnswering_ALoginNamingAnOrganization_ResolvesItByItsCurrentShortNameInTheSameStatement()
    {
        // Arrange
        using var context = DesignTimeContext();
        UserCredentialLogin.TryRead("TESTFIRMA/jan", out var login);

        // Act
        var sql = PersistedUserCredentials.PasswordCredentialsAnswering(context, login).ToQueryString();

        // Assert
        Assert.Contains("FROM organizations", sql, StringComparison.Ordinal);
        Assert.Contains("\"ShortName\" = ", sql, StringComparison.Ordinal);
        Assert.Contains("\"OrganizationId\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OrganizationId\" IS NULL", sql, StringComparison.Ordinal);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
