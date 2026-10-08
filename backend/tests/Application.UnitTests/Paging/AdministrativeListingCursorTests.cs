// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using Xunit;

namespace MailFathom.Application.UnitTests.Paging;

/// <summary>Covers the cursor the administrative listings continue from: what it carries back, and which cursors it refuses.</summary>
public sealed class AdministrativeListingCursorTests
{
    private static readonly Guid LastRow = new("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b2f");

    [Theory]
    [InlineData(AdministrativeListing.Organizations)]
    [InlineData(AdministrativeListing.MailAccounts)]
    [InlineData(AdministrativeListing.Users)]
    public void TryDecode_ACursorTheSameListingIssued_ReadsBackTheRowItContinuesAfter(AdministrativeListing listing)
    {
        // Arrange
        var cursor = AdministrativeListingCursor.Encode(listing, LastRow);

        // Act
        var read = AdministrativeListingCursor.TryDecode(cursor, listing, out var after);

        // Assert
        Assert.True(read);
        Assert.Equal(LastRow, after);
    }

    /// <summary>A cursor carried from one listing to another names no position in it, so it is refused rather than followed.</summary>
    [Fact]
    public void TryDecode_ACursorAnotherListingIssued_IsRefused()
    {
        // Arrange
        var cursor = AdministrativeListingCursor.Encode(AdministrativeListing.Users, LastRow);

        // Act
        var read = AdministrativeListingCursor.TryDecode(cursor, AdministrativeListing.MailAccounts, out var after);

        // Assert
        Assert.False(read);
        Assert.Equal(Guid.Empty, after);
    }

    /// <summary>A cursor naming an instant was issued by a dated reading, which these listings are not.</summary>
    [Fact]
    public void TryDecode_ACursorCarryingAPosition_IsRefused()
    {
        // Arrange
        var cursor = KeysetCursorPayload.At(
                new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
                LastRow,
                PageFilterFingerprint.Of(nameof(AdministrativeListing.Users)))
            .Encode();

        // Act
        var read = AdministrativeListingCursor.TryDecode(cursor, AdministrativeListing.Users, out _);

        // Assert
        Assert.False(read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    public void TryDecode_TextThatIsNoCursor_IsRefused(string? text)
    {
        // Act
        var read = AdministrativeListingCursor.TryDecode(text, AdministrativeListing.Organizations, out _);

        // Assert
        Assert.False(read);
    }
}
