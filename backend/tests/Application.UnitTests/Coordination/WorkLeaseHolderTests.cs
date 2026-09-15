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

    [Fact]
    public void Build_AHoldTakenByABuildThatStampsOne_NamesThatBuild()
    {
        // Act
        var holder = WorkLeaseHolder.ForBuild("0.8.0");

        // Assert
        Assert.Equal("0.8.0", holder.Build);
    }

    [Fact]
    public void ForBuild_TwoHoldsTakenByOneBuild_AreStillDifferentHolds()
    {
        // Act
        var holder = WorkLeaseHolder.ForBuild("0.8.0");
        var secondHolder = WorkLeaseHolder.ForBuild("0.8.0");

        // Assert
        Assert.NotEqual(holder, secondHolder);
    }

    /// <summary>A build that does not stamp one is what the custody switch refuses on, so it has to read as absent.</summary>
    [Fact]
    public void Build_AHoldTakenByABuildThatStampsNone_NamesNoBuild()
    {
        // Act
        var holder = WorkLeaseHolder.NewHold();

        // Assert
        Assert.Null(holder.Build);
    }

    /// <summary>
    /// An older build taking a scope over replaces the whole holder rather than one part of it, so a lease it holds
    /// carries its own unstamped value and reads as the unknown build it is.
    /// </summary>
    [Fact]
    public void Build_AHoldAnOlderBuildTookOverFromAStampedOne_NamesNoBuild()
    {
        // Arrange
        var stamped = WorkLeaseHolder.ForBuild("0.8.0");

        // Act
        var tookOver = WorkLeaseHolder.Create(Guid.CreateVersion7().ToString());

        // Assert
        Assert.NotNull(stamped.Build);
        Assert.Null(tookOver.Build);
    }

    [Fact]
    public void ForBuild_AStampedHold_FitsTheColumnItIsStoredIn()
    {
        // Act
        var holder = WorkLeaseHolder.ForBuild(new string('v', 64));

        // Assert
        Assert.InRange(holder.Value.Length, 1, WorkLeaseHolder.MaximumLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ForBuild_ABlankBuild_IsRefusedSoAHoldCannotLookStampedWithNothing(string build)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkLeaseHolder.ForBuild(build));
    }

    /// <summary>The separator is what splits the two halves, so a build carrying one could name a build it is not.</summary>
    [Fact]
    public void ForBuild_ABuildCarryingTheSeparator_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() =>
            WorkLeaseHolder.ForBuild($"0.8.0{WorkLeaseHolder.BuildSeparator}9.9.9"));
    }

    [Fact]
    public void ForBuild_ABuildLongerThanTheStampAllows_IsRefused()
    {
        // Arrange
        var tooLong = new string('v', 65);

        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkLeaseHolder.ForBuild(tooLong));
    }
}
