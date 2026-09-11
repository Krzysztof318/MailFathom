// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
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
    private static readonly MailUserId User = MailUserId.Create(new Guid("0197c0de-0000-7000-8000-000000001290"));

    /// <summary>An operator reading the lease table finds the account under the user and the name the configuration gave it.</summary>
    [Fact]
    public void For_AccountIdentifierFits_NamesTheUserAndTheAccount()
    {
        // Act
        var scope = MailAccountSupervisionScope.For(MailAccountIdentity.Create(User, MailAccountId.Create("primary")));

        // Assert
        Assert.Equal("mail-synchronization/0197c0de-0000-7000-8000-000000001290/primary", scope.Value);
    }

    /// <summary>Two users naming an account alike are two units of work, so neither is supervised by the replica holding the other.</summary>
    [Fact]
    public void For_TwoUsersNamingAnAccountAlike_NamesThemApart()
    {
        // Arrange
        var second = MailUserId.Create(new Guid("0197c0de-0000-7000-8000-000000001291"));
        var alias = MailAccountId.Create("primary");

        // Act
        var mine = MailAccountSupervisionScope.For(MailAccountIdentity.Create(User, alias));
        var theirs = MailAccountSupervisionScope.For(MailAccountIdentity.Create(second, alias));

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
        var first = MailAccountIdentity.Create(User, MailAccountId.Create(new string('a', 4000)));
        var second = MailAccountIdentity.Create(User, MailAccountId.Create(new string('b', 4000)));

        // Act
        var firstScope = MailAccountSupervisionScope.For(first);
        var secondScope = MailAccountSupervisionScope.For(second);

        // Assert
        Assert.StartsWith("mail-synchronization/0197c0de-0000-7000-8000-000000001290/sha256-", firstScope.Value, StringComparison.Ordinal);
        Assert.True(firstScope.Value.Length <= WorkScope.MaximumLength);
        Assert.Equal(firstScope, MailAccountSupervisionScope.For(first));
        Assert.NotEqual(firstScope, secondScope);
    }
}
