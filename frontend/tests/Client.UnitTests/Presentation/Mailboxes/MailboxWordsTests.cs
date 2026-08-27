// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Presentation.Mailboxes;

namespace MailFathom.Client.UnitTests.Presentation.Mailboxes;

/// <summary>The words the tree is written in, and the reading order the roles are offered in.</summary>
public sealed class MailboxWordsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The reading order is stated rather than derived, so a role added to the contract with no place in it would be
    /// dropped from every tree without anything saying so. This is the thing that says so.
    /// </summary>
    [Fact]
    public void RolesInReadingOrder_EveryRoleTheClientKnows_HasAPlaceInIt()
    {
        // Arrange
        var named = Enum
            .GetValues<MailFolderRole>()
            .Where(role => role is not (MailFolderRole.None or MailFolderRole.Unrecognized))
            .Order();

        // Act
        var offered = MailboxWords.RolesInReadingOrder.Order();

        // Assert
        Assert.Equal(named, offered);
    }

    /// <summary>The two readings that are not roles are not offered as ones, because neither names a folder to gather.</summary>
    [Fact]
    public void RolesInReadingOrder_TheTwoReadingsThatAreNotRoles_AreNotOffered()
    {
        // Act, Assert
        Assert.DoesNotContain(MailFolderRole.None, MailboxWords.RolesInReadingOrder);
        Assert.DoesNotContain(MailFolderRole.Unrecognized, MailboxWords.RolesInReadingOrder);
    }

    /// <summary>A remembered scope naming a role this build does not offer is forgotten rather than restored.</summary>
    [Fact]
    public void IsOfferedRole_ANameThisBuildDoesNotOffer_IsRefused()
    {
        // Act, Assert
        Assert.True(MailboxWords.IsOfferedRole("Inbox"));
        Assert.False(MailboxWords.IsOfferedRole("inbox"));
        Assert.False(MailboxWords.IsOfferedRole("None"));
        Assert.False(MailboxWords.IsOfferedRole("Whatever"));
        Assert.False(MailboxWords.IsOfferedRole(null));
    }

    /// <summary>A role's entry is named the same way whether the role arrived as a value or as the word a scope carries.</summary>
    [Fact]
    public void RoleResourceKeyFor_AValueAndThePublishedNameOfIt_NameOneEntry()
    {
        // Act, Assert
        Assert.Equal(
            MailboxWords.RoleResourceKeyFor(MailFolderRole.Sent),
            MailboxWords.RoleResourceKeyFor("Sent"));
    }

    /// <summary>Nothing ever taken in has no gap to state, which is its own band rather than the widest one.</summary>
    [Fact]
    public void GapAt_ACopyNothingHasEverSynchronized_IsItsOwnBand()
    {
        // Act, Assert
        Assert.Equal(FreshnessGap.Never, MailboxWords.GapAt(null, Now));
    }

    /// <summary>The bands are the ones a person decides on, measured from when the tree is being read.</summary>
    [Theory]
    [InlineData(1, FreshnessGap.WithinTheHour)]
    [InlineData(90, FreshnessGap.Today)]
    [InlineData(60 * 30, FreshnessGap.WithinTheWeek)]
    [InlineData(60 * 24 * 9, FreshnessGap.LongerAgo)]
    public void GapAt_AGapOfSoManyMinutes_FallsInTheBandItBelongsTo(int minutes, FreshnessGap expected)
    {
        // Act
        var gap = MailboxWords.GapAt(Now.AddMinutes(-minutes), Now);

        // Assert
        Assert.Equal(expected, gap);
    }

    /// <summary>
    /// A deployment's clock and a person's device disagreeing by a few seconds is ordinary, so an instant ahead of now
    /// reads as the narrowest band rather than as a negative gap.
    /// </summary>
    [Fact]
    public void GapAt_AnInstantAheadOfThisDevicesClock_ReadsAsTheNarrowestBand()
    {
        // Act
        var gap = MailboxWords.GapAt(Now.AddSeconds(20), Now);

        // Assert
        Assert.Equal(FreshnessGap.WithinTheHour, gap);
    }
}
