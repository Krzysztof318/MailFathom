// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers how an erasure keeps this replica's synchronization off the accounts it is deciding about.</summary>
public sealed class WithheldMailAccountsTests
{
    private static readonly MailAccountId Shared = MailAccountId.Create("0199a0c0-0000-7000-8000-00000000000a");

    private static readonly MailAccountId Own = MailAccountId.Create("0199a0c0-0000-7000-8000-00000000000b");

    /// <summary>Two erasures naming one account release it only once both have ended, so neither hands it back early.</summary>
    [Fact]
    public void Withhold_TwoErasuresNamingOneAccount_ReleasesItOnlyOnceBothEnded()
    {
        // Arrange
        var withheld = new WithheldMailAccounts();
        var first = withheld.Withhold([Shared, Own]);
        var second = withheld.Withhold([Shared]);

        // Act
        first.Dispose();
        var sharedAfterFirst = withheld.IsWithheld(Shared);
        var ownAfterFirst = withheld.IsWithheld(Own);
        second.Dispose();

        // Assert
        Assert.True(sharedAfterFirst);
        Assert.False(ownAfterFirst);
        Assert.False(withheld.IsWithheld(Shared));
    }

    /// <summary>A withholding disposed twice releases once, so it cannot hand back an account another erasure still holds.</summary>
    [Fact]
    public void Withhold_DisposedTwice_ReleasesOnlyItsOwnHold()
    {
        // Arrange
        var withheld = new WithheldMailAccounts();
        var repeated = withheld.Withhold([Shared]);
        using var other = withheld.Withhold([Shared]);

        // Act
        repeated.Dispose();
        repeated.Dispose();

        // Assert
        Assert.True(withheld.IsWithheld(Shared));
    }

    /// <summary>Withholding and releasing each change the token, which is what wakes the coordinator to act on either.</summary>
    [Fact]
    public void GetChangeToken_AccountWithheldAndReleased_ChangesEachTime()
    {
        // Arrange
        var withheld = new WithheldMailAccounts();
        var beforeWithholding = withheld.GetChangeToken();

        // Act
        var withholding = withheld.Withhold([Own]);
        var beforeRelease = withheld.GetChangeToken();
        withholding.Dispose();

        // Assert
        Assert.True(beforeWithholding.HasChanged);
        Assert.True(beforeRelease.HasChanged);
        Assert.False(withheld.GetChangeToken().HasChanged);
    }
}
