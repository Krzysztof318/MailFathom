// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using Xunit;

namespace MailFathom.Application.UnitTests.Coordination;

/// <summary>
/// The holder is what every conditional write compares against, so a value two holds could share would let a late write
/// from one of them land on the other's lease. That is what these cover.
/// </summary>
public sealed class WorkLeaseHolderTests
{
    [Fact]
    public void NewHold_TwoHolds_AreDifferentSoNeitherCanWriteOverTheOther()
    {
        // Act
        var holder = WorkLeaseHolder.NewHold();
        var secondHolder = WorkLeaseHolder.NewHold();

        // Assert
        Assert.NotEqual(holder, secondHolder);
    }

    /// <summary>The generated identity has to fit the column it is compared in, whatever else it is.</summary>
    [Fact]
    public void NewHold_AGeneratedHold_FitsTheColumnItIsStoredIn()
    {
        // Act
        var holder = WorkLeaseHolder.NewHold();

        // Assert
        Assert.InRange(holder.Value.Length, 1, WorkLeaseHolder.MaximumLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankIdentity_IsRefusedBecauseEveryRowWouldMatchIt(string value)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkLeaseHolder.Create(value));
    }

    [Fact]
    public void Create_AnIdentityLongerThanTheColumnHolds_IsRefused()
    {
        // Arrange
        var tooLong = new string('h', WorkLeaseHolder.MaximumLength + 1);

        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkLeaseHolder.Create(tooLong));
    }

    [Fact]
    public void Create_AnIdentityCarryingAControlCharacter_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkLeaseHolder.Create("hold\u0001one"));
    }

    [Fact]
    public void Create_SurroundingWhitespace_TrimsItSoOneHoldIsOneValue()
    {
        // Act
        var holder = WorkLeaseHolder.Create("  hold-one  ");

        // Assert
        Assert.Equal("hold-one", holder.Value);
    }
}
