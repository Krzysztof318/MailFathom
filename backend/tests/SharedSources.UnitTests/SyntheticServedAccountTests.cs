// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the helper several suites arrange their served accounts with.</summary>
/// <remarks>
/// A fault here reports a false result in every suite that uses it rather than failing where the fault is, which is why
/// a helper this small is covered at all.
/// </remarks>
public sealed class SyntheticServedAccountTests
{
    [Fact]
    public void Of_AnAccountIdentifier_ServesItUnderTheDerivedDisplayNameAndPolls()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");

        // Act
        var account = SyntheticServedAccount.Of(accountId);

        // Assert
        Assert.Equal(accountId, account.Id);
        Assert.Equal(SyntheticServedAccount.DisplayNameOf(accountId), account.DisplayName);
        Assert.Equal(MailSynchronizationMode.Polling, account.SynchronizationMode);
    }

    [Fact]
    public void Of_TheTextOfAnIdentifier_ServesTheSameAccountAsTheIdentityOverload()
    {
        // Arrange, Act
        var fromText = SyntheticServedAccount.Of("primary");

        // Assert
        Assert.Equal(SyntheticServedAccount.Of(MailAccountId.Create("primary")), fromText);
    }

    /// <summary>The derived name is deliberately not the identifier, so a test cannot pass while only one of the two spellings resolves.</summary>
    [Fact]
    public void DisplayNameOf_AnAccountIdentifier_IsNotTheIdentifierItself()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");

        // Act
        var displayName = SyntheticServedAccount.DisplayNameOf(accountId);

        // Assert
        Assert.NotEqual(accountId.Value, displayName.Value);
        Assert.Contains(accountId.Value, displayName.Value, StringComparison.Ordinal);
    }

    /// <summary>Two accounts never share a derived name, so a suite arranging several of them stays resolvable.</summary>
    [Fact]
    public void DisplayNameOf_TwoAccounts_AreDistinctNames()
    {
        // Arrange, Act
        var first = SyntheticServedAccount.DisplayNameOf(MailAccountId.Create("acct-1"));
        var second = SyntheticServedAccount.DisplayNameOf(MailAccountId.Create("acct-2"));

        // Assert
        Assert.NotEqual(first, second);
    }
}
