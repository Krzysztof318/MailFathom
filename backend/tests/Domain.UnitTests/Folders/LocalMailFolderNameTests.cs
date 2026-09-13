// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers what a local folder name may be, which is decided where the name is written.</summary>
public sealed class LocalMailFolderNameTests
{
    [Theory]
    [InlineData("Projects", "Projects")]
    [InlineData("  2026.Q1  ", "2026.Q1")]
    [InlineData("Faktury i rachunki", "Faktury i rachunki")]
    public void TryCreate_AName_KeepsItTrimmed(string written, string expected)
    {
        // Act
        var created = LocalMailFolderName.TryCreate(written, out var name);

        // Assert
        Assert.True(created);
        Assert.Equal(expected, name.Value);
    }

    /// <summary>A name is one level, so the delimiter would make it two; a control character is never a name a person reads.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Archive/2026")]
    [InlineData("Arch\tive")]
    [InlineData("Arch\0ive")]
    [InlineData("​")]
    [InlineData("Arch‮ive")]
    public void TryCreate_TextThatIsNotOneLevel_IsRefused(string? written)
    {
        // Act
        var created = LocalMailFolderName.TryCreate(written, out _);

        // Assert
        Assert.False(created);
    }

    [Fact]
    public void TryCreate_TheLongestNameAndOneCharacterMore_IsAcceptedAndRefusedRespectively()
    {
        // Arrange
        var longest = new string('a', LocalMailFolderName.MaximumLength);

        // Act
        var acceptedLongest = LocalMailFolderName.TryCreate(longest, out _);
        var acceptedLonger = LocalMailFolderName.TryCreate(longest + "a", out _);

        // Assert
        Assert.True(acceptedLongest);
        Assert.False(acceptedLonger);
    }

    [Fact]
    public void NamesSameFolderAs_TwoNamesDifferingOnlyInCase_NameTheSameFolder()
    {
        // Arrange
        var lower = LocalMailFolderName.Create("receipts");
        var mixed = LocalMailFolderName.Create("Receipts");

        // Act
        var same = lower.NamesSameFolderAs(mixed);

        // Assert
        Assert.True(same);
        Assert.Equal(lower.ComparisonKey, mixed.ComparisonKey);
    }

    [Theory]
    [InlineData("INBOX", true)]
    [InlineData("inbox", true)]
    [InlineData("Inbox archive", false)]
    public void IsReservedAtTopLevel_AName_IsReservedOnlyWhereItIsTheInboxName(string written, bool expected)
    {
        // Act
        var reserved = LocalMailFolderName.Create(written).IsReservedAtTopLevel;

        // Assert
        Assert.Equal(expected, reserved);
    }
}
