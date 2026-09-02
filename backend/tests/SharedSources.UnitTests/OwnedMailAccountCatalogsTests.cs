// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the caller-scoped catalog several suites arrange the owner axis with.</summary>
/// <remarks>
/// Every test that asserts one owner cannot reach another's mailbox is arranged through this helper, so a helper that
/// answered with nothing whoever asked would make all of them pass while proving the opposite of what they claim. Both
/// answers are asserted here rather than in each suite for that reason.
/// </remarks>
public sealed class OwnedMailAccountCatalogsTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    [Fact]
    public void OwnedAccounts_ForTheOwnerEveryConfiguredAccountBelongsTo_AreTheAccountsServed()
    {
        // Arrange
        var catalog = OwnedMailAccountCatalogs.For(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment),
            SyntheticServedAccount.Of(Work));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Equal(Work, Assert.Single(owned).Id);
    }

    [Fact]
    public void OwnedAccounts_ForAnotherOwner_AreNone()
    {
        // Arrange
        var catalog = OwnedMailAccountCatalogs.For(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another),
            SyntheticServedAccount.Of(Work));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Empty(owned);
    }

    /// <summary>A principal acting for nobody is refused rather than answered, so the two never look alike in a test.</summary>
    [Fact]
    public void OwnedAccounts_ForAPrincipalActingForNoOwner_AreRefused()
    {
        // Arrange
        var catalog = OwnedMailAccountCatalogs.For(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.AdminOperate),
            SyntheticServedAccount.Of(Work));

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() => catalog.OwnedAccounts);
    }

    /// <summary>The switch is the deployment's and says nothing about who owns what, so it reaches every caller.</summary>
    [Fact]
    public void SynchronizationEnabled_WhicheverOwnerAsks_IsTheDeploymentsOwnAnswer()
    {
        // Arrange
        var catalog = OwnedMailAccountCatalogs.For(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another),
            SyntheticServedAccount.Of(Work));

        // Act & Assert
        Assert.True(catalog.SynchronizationEnabled);
    }

    /// <summary>The order is the deployment's own, so a scope a test resolves from this set is the canonical one.</summary>
    [Fact]
    public void OwnedAccounts_HoweverATestNamedThem_AreOrderedByIdentifier()
    {
        // Arrange
        var catalog = OwnedMailAccountCatalogs.For(
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment),
            SyntheticServedAccount.Of("private"),
            SyntheticServedAccount.Of("archive"));

        // Act
        var owned = catalog.OwnedAccounts;

        // Assert
        Assert.Equal(["archive", "private"], owned.Select(account => account.Id.Value));
    }
}
