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
        Assert.Matches(@"\w+\.""Id"" = \w+\.""OrganizationId""|\w+\.""OrganizationId"" = \w+\.""Id""", sql);
        Assert.DoesNotContain("\"OrganizationId\" IS NULL", sql, StringComparison.Ordinal);
    }

    /// <summary>The ordinary rows: a method this build knows, scoped to an organization it reads or to none at all.</summary>
    [Theory]
    [InlineData("password", null)]
    [InlineData("password", "TESTFIRMA")]
    [InlineData("api-key", null)]
    public void IsListable_ARowThisBuildReads_PublishesIt(string method, string? organizationShortName)
    {
        // Act
        var listable = PersistedUserCredentials.IsListable(method, organizationShortName);

        // Assert
        Assert.True(listable);
    }

    /// <summary>
    /// A scope this build will not read leaves that one credential out and costs the user nothing else — the listing
    /// used to raise over it, which took every other credential they hold with it.
    /// </summary>
    [Theory]
    [InlineData("test firma")]
    [InlineData("TEST/FIRMA")]
    [InlineData("")]
    public void IsListable_AScopeThisBuildWillNotRead_LeavesThatRowOut(string organizationShortName)
    {
        // Act
        var listable = PersistedUserCredentials.IsListable("password", organizationShortName);

        // Assert
        Assert.False(listable);
    }

    /// <summary>A method this release does not publish is left out on the same terms, which is the rule the scope joined.</summary>
    [Fact]
    public void IsListable_AMethodThisBuildDoesNotKnow_LeavesThatRowOut()
    {
        // Act
        var listable = PersistedUserCredentials.IsListable("no-such-method", null);

        // Assert
        Assert.False(listable);
    }

    /// <summary>
    /// A row naming nothing is read back as naming nothing for an administrator, and admitted kept to every mail name
    /// this build publishes — which takes nothing from its user's grant, so the user's roles alone decide.
    /// </summary>
    [Fact]
    public void Narrowing_ARowNamingNothing_IsReportedAsNoneAndAdmitsTheWholeMailHalf()
    {
        // Act
        var reported = PersistedUserCredentials.NarrowingOf(null);
        var admitted = PersistedUserCredentials.NarrowingAdmittedBy(null);

        // Assert
        Assert.Null(reported);
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), admitted);
    }

    /// <summary>A row naming some keeps them in the published order, drops a name this build no longer publishes, and keeps the empty list empty.</summary>
    [Fact]
    public void Narrowing_ARowNamingSome_KeepsThePublishedNamesInThePublishedOrder()
    {
        // Act
        var named = PersistedUserCredentials.NarrowingAdmittedBy(
            [MailFathomPermission.MailSend.Name, "mailfathom.mail.retired", MailFathomPermission.MailRead.Name]);
        var empty = PersistedUserCredentials.NarrowingAdmittedBy([]);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.MailSend], named);
        Assert.Empty(empty);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
