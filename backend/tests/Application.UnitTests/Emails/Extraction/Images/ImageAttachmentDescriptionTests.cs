// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Images;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction.Images;

/// <summary>Covers the guarantee a caller reads this result under: exactly one of the description and the reason is there.</summary>
public sealed class ImageAttachmentDescriptionTests
{
    [Fact]
    public void Described_APictureTheModelWroteAbout_CarriesTheWordsAndNoReason()
    {
        // Act
        var description = ImageAttachmentDescription.Described("A whiteboard covered in a roof plan.");

        // Assert
        Assert.Equal("A whiteboard covered in a roof plan.", description.Text);
        Assert.Null(description.Refusal);
    }

    /// <summary>A blank description is a call that produced nothing, so it is refused here rather than stored as an empty passage.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Described_NothingButWhitespace_IsRefused(string text)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ImageAttachmentDescription.Described(text));
    }

    [Fact]
    public void Refused_AReasonNothingWasDescribed_CarriesItAndNoWords()
    {
        // Act
        var description = ImageAttachmentDescription.Refused(ImageDescriptionRefusal.PixelGridTooLarge);

        // Assert
        Assert.Null(description.Text);
        Assert.Equal(ImageDescriptionRefusal.PixelGridTooLarge, description.Refusal);
    }

    /// <summary>A reason outside the closed set is refused, because a caller recording one would record a number nothing reads.</summary>
    [Fact]
    public void Refused_AReasonOutsideTheDeclaredSet_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ImageAttachmentDescription.Refused((ImageDescriptionRefusal)99));
    }
}
