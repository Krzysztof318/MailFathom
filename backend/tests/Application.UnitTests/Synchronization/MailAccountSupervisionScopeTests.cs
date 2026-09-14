// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization;

/// <summary>
/// The name two callers have to agree on: the coordinator takes an account's hold under it and the administrative
/// surface asks the lease table who is holding that same name. A second spelling would report every account as
/// unsupervised while every account was in fact being supervised.
/// </summary>
public sealed class MailAccountSupervisionScopeTests
{
    /// <summary>An operator reading the lease table finds the account under the identifier the deployment gave it.</summary>
    [Fact]
    public void For_AccountIdentifierFits_NamesTheAccountAndNobodyBeside()
    {
        // Act
        var scope = MailAccountSupervisionScope.For(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal("mail-synchronization/primary", scope.Value);
    }

    /// <summary>
    /// One mailbox assigned to several people is supervised once. The scope names the account alone, so every replica
    /// configured with it contends for the same hold however many users reach the mailbox — a scope naming a user
    /// would have the same mailbox fetched once per assignment.
    /// </summary>
    [Fact]
    public void For_TheSameAccount_NamesOneUnitOfWorkHoweverManyUsersReachIt()
    {
        // Arrange
        var account = MailAccountId.Create("shared");

        // Act
        var asOneReaderSeesIt = MailAccountSupervisionScope.For(account);
        var asAnotherSeesIt = MailAccountSupervisionScope.For(account);

        // Assert
        Assert.Equal(asOneReaderSeesIt, asAnotherSeesIt);
    }

    /// <summary>Two accounts are two units of work, so neither is supervised by the replica holding the other.</summary>
    [Fact]
    public void For_TwoAccounts_NamesThemApart()
    {
        // Act
        var mine = MailAccountSupervisionScope.For(MailAccountId.Create("primary"));
        var theirs = MailAccountSupervisionScope.For(MailAccountId.Create("secondary"));

        // Assert
        Assert.NotEqual(mine, theirs);
    }

    /// <summary>
    /// Configuration accepts account identifiers far longer than a scope may be, and a scope that could not be composed
    /// would end supervision for every account on the replica; such an account is named by its digest instead.
    /// </summary>
    [Fact]
    public void For_AccountIdentifierTooLongForAScope_NamesEachAccountByItsOwnDigest()
    {
        // Arrange
        var first = MailAccountId.Create(new string('a', 4000));
        var second = MailAccountId.Create(new string('b', 4000));

        // Act
        var firstScope = MailAccountSupervisionScope.For(first);
        var secondScope = MailAccountSupervisionScope.For(second);

        // Assert
        Assert.StartsWith("mail-synchronization/sha256-", firstScope.Value, StringComparison.Ordinal);
        Assert.True(firstScope.Value.Length <= WorkScope.MaximumLength);
        Assert.Equal(firstScope, MailAccountSupervisionScope.For(first));
        Assert.NotEqual(firstScope, secondScope);
    }

    /// <summary>
    /// A scope carries no control character, and configuration rejects only a blank account identifier, so a short one
    /// holding a control character reaches this the same way an overlong one does and is named by its digest too.
    /// </summary>
    [Fact]
    public void For_AccountIdentifierHoldingAControlCharacter_NamesTheAccountByItsDigest()
    {
        // Arrange
        var account = MailAccountId.Create("primary");

        // Act
        var scope = MailAccountSupervisionScope.For(account);

        // Assert
        Assert.StartsWith("mail-synchronization/sha256-", scope.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(scope.Value, char.IsControl);
        Assert.Equal(scope, MailAccountSupervisionScope.For(account));
    }
}
