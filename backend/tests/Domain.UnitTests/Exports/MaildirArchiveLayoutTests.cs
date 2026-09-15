// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Exports;
using Xunit;

namespace MailFathom.Domain.UnitTests.Exports;

public sealed class MaildirArchiveLayoutTests
{
    [Fact]
    public void FolderDirectory_Inbox_IsTheArchiveRoot()
    {
        // Arrange
        string[] segments = ["INBOX"];

        // Act
        var directory = MaildirArchiveLayout.FolderDirectory(segments, isInbox: true);

        // Assert
        Assert.Equal(string.Empty, directory);
    }

    [Fact]
    public void FolderDirectory_NestedFolder_JoinsEncodedSegmentsUnderOneMaildirPlusPlusDirectory()
    {
        // Arrange
        string[] segments = ["Projects", "MailFathom"];

        // Act
        var directory = MaildirArchiveLayout.FolderDirectory(segments, isInbox: false);

        // Assert
        Assert.Equal(".Projects.MailFathom", directory);
    }

    [Fact]
    public void FolderDirectory_NoSegmentsAndNotTheInbox_ThrowsArgumentException()
    {
        // Arrange
        string[] segments = [];

        // Act
        var refusal = Record.Exception(() => MaildirArchiveLayout.FolderDirectory(segments, isInbox: false));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    [Theory]
    [InlineData(".", "%2E")]
    [InlineData("..", "%2E%2E")]
    [InlineData("cur", "cur")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a\\b", "a%5Cb")]
    [InlineData("50% off", "50%25%20off")]
    [InlineData("Zamówienia", "Zam%C3%B3wienia")]
    [InlineData("a\0b", "a%00b")]
    public void EncodeSegment_NameCarryingStructure_WritesEveryReservedByteAsAnEscape(string segment, string expected)
    {
        // Arrange, Act
        var encoded = MaildirArchiveLayout.EncodeSegment(segment);

        // Assert
        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void FolderDirectory_SegmentsNamingDotAndCur_CannotProduceAMaildirSubdirectoryName()
    {
        // Arrange
        string[] segments = ["..", "cur"];

        // Act
        var directory = MaildirArchiveLayout.FolderDirectory(segments, isInbox: false);

        // Assert
        Assert.Equal(".%2E%2E.cur", directory);
        Assert.StartsWith(".", directory, StringComparison.Ordinal);
    }

    [Fact]
    public void MessagePath_Inbox_WritesUnderTheRootMaildirsDeliveredSubdirectory()
    {
        // Arrange
        var fileName = "1700000000.1.mailfathom,S=10:2,S";

        // Act
        var path = MaildirArchiveLayout.MessagePath(string.Empty, fileName);

        // Assert
        Assert.Equal("cur/1700000000.1.mailfathom,S=10:2,S", path);
    }

    [Fact]
    public void MessagePath_NamedFolder_WritesUnderThatFoldersDeliveredSubdirectory()
    {
        // Arrange
        var fileName = "1700000000.2.mailfathom,S=10:2,";

        // Act
        var path = MaildirArchiveLayout.MessagePath(".Archive", fileName);

        // Assert
        Assert.Equal(".Archive/cur/1700000000.2.mailfathom,S=10:2,", path);
    }

    [Fact]
    public void MessageFileName_NoFlags_CarriesTheArrivalTheOrdinalAndTheSize()
    {
        // Arrange
        var receivedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000);

        // Act
        var fileName = MaildirArchiveLayout.MessageFileName(receivedAt, ordinal: 7, byteLength: 4096, MaildirFlagSet.None);

        // Assert
        Assert.Equal("1700000000.7.mailfathom,S=4096:2,", fileName);
    }

    [Fact]
    public void MessageFileName_EveryFlag_WritesTheLettersInAscendingAsciiOrder()
    {
        // Arrange
        var receivedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var flags = MaildirFlagSet.Seen | MaildirFlagSet.Answered | MaildirFlagSet.Flagged | MaildirFlagSet.Draft;

        // Act
        var fileName = MaildirArchiveLayout.MessageFileName(receivedAt, ordinal: 1, byteLength: 1, flags);

        // Assert
        Assert.EndsWith(":2,DFRS", fileName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MaildirFlagSet.Seen, "S")]
    [InlineData(MaildirFlagSet.Flagged, "F")]
    [InlineData(MaildirFlagSet.Answered, "R")]
    [InlineData(MaildirFlagSet.Draft, "D")]
    [InlineData(MaildirFlagSet.Seen | MaildirFlagSet.Flagged, "FS")]
    public void MessageFileName_FlagSet_WritesTheStandardMaildirLetters(MaildirFlagSet flags, string expected)
    {
        // Arrange
        var receivedAt = DateTimeOffset.FromUnixTimeSeconds(1);

        // Act
        var fileName = MaildirArchiveLayout.MessageFileName(receivedAt, ordinal: 0, byteLength: 0, flags);

        // Assert
        Assert.EndsWith($":2,{expected}", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void MessageFileName_NegativeOrdinal_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var receivedAt = DateTimeOffset.FromUnixTimeSeconds(1);

        // Act
        var refusal = Record.Exception(() =>
            MaildirArchiveLayout.MessageFileName(receivedAt, ordinal: -1, byteLength: 0, MaildirFlagSet.None));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }
}
