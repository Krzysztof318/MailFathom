// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>
/// Covers what a stored organization row costs when this build will not read it. Every short name is judged by the route
/// that writes it, so a row reaching this state was written by an older build, edited in the database, or restored from
/// a backup — and what matters is that it costs itself and nothing else.
/// </summary>
public sealed class PersistedOrganizationListingTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 9, 14, 7, 0, 0, TimeSpan.Zero);

    /// <summary>The ordinary listing: every row reads, and each is published with the canonical upper-case short name.</summary>
    [Fact]
    public void ListingOf_EveryRowReadable_PublishesEachOfThem()
    {
        // Arrange
        var rows = new[] { Row("ACME", "Acme"), Row("BETA", "Beta", key: 2) };

        // Act
        var listing = PersistedOrganizations.ListingOf(rows);

        // Assert
        Assert.Equal(["ACME", "BETA"], listing.Organizations.Select(organization => organization.ShortName.Value));
        Assert.Empty(listing.Unreadable);
    }

    /// <summary>
    /// The guarantee this exists for: one row this build will not read leaves every other organization listed, rather
    /// than raising through the listing an operator would repair it from.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("ACME/SUB")]
    [InlineData("ACME:SUB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void ListingOf_ARowThisBuildWillNotRead_ListsEveryOtherOrganization(string storedShortName)
    {
        // Arrange
        var broken = Row(storedShortName, "Broken");
        var rows = new[] { broken, Row("BETA", "Beta", key: 2) };

        // Act
        var listing = PersistedOrganizations.ListingOf(rows);

        // Assert
        Assert.Equal("BETA", Assert.Single(listing.Organizations).ShortName.Value);
        var unreadable = Assert.Single(listing.Unreadable);
        Assert.Equal(broken.Id, unreadable.Id);
        Assert.Equal("Broken", unreadable.DisplayName);
    }

    /// <summary>What the row must become is said in the listing, because the identifier alone does not tell an operator what to write.</summary>
    [Fact]
    public void ListingOf_ARowThisBuildWillNotRead_SaysWhatAShortNameMayHold()
    {
        // Act
        var listing = PersistedOrganizations.ListingOf([Row("has space", "Broken")]);

        // Assert
        Assert.Contains("'A' to 'Z'", Assert.Single(listing.Unreadable).Correction, StringComparison.Ordinal);
    }

    /// <summary>The stored short name is the one value that failed every rule, so it is never repeated back.</summary>
    [Fact]
    public void ListingOf_ARowThisBuildWillNotRead_RepeatsNoneOfTheStoredShortName()
    {
        // Arrange
        const string stored = "sentinel\nforged line";

        // Act
        var listing = PersistedOrganizations.ListingOf([Row(stored, "Broken")]);

        // Assert
        var unreadable = Assert.Single(listing.Unreadable);
        Assert.DoesNotContain("sentinel", unreadable.Correction, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", unreadable.DisplayName, StringComparison.Ordinal);
    }

    /// <summary>A row keyed by a stated identifier, because a test telling two rows apart by a generated one would depend on the generator.</summary>
    private static StoredOrganizationRow Row(string shortName, string displayName, int key = 1) =>
        new(
            new Guid($"00000000-0000-0000-0000-{key:D12}"),
            displayName,
            shortName,
            Members: 0,
            Recorded);
}
