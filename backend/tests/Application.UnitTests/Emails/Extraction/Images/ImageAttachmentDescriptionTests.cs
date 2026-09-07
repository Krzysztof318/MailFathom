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

    /// <summary>
    /// A description a provider answered is what the description ceiling counts, so the refusals that never left this
    /// process must not be charged as calls: charging one would spend a period on work no provider ever saw.
    /// </summary>
    [Theory]
    [InlineData(ImageDescriptionRefusal.NotActivated)]
    [InlineData(ImageDescriptionRefusal.FormatNotSupported)]
    [InlineData(ImageDescriptionRefusal.FormatExcluded)]
    [InlineData(ImageDescriptionRefusal.ImageTooLarge)]
    [InlineData(ImageDescriptionRefusal.PixelGridTooLarge)]
    [InlineData(ImageDescriptionRefusal.ImageUnreadable)]
    public void ReachedProvider_ARefusalTakenBeforeTheCall_IsNotCountedAsOne(ImageDescriptionRefusal refusal)
    {
        // Act
        var description = ImageAttachmentDescription.Refused(refusal);

        // Assert
        Assert.False(description.ReachedProvider);
    }

    /// <summary>A refusal the provider itself produced cost a call, whatever it answered with.</summary>
    [Theory]
    [InlineData(ImageDescriptionRefusal.ProviderTimedOut)]
    [InlineData(ImageDescriptionRefusal.ProviderUnavailable)]
    [InlineData(ImageDescriptionRefusal.ProviderRefused)]
    public void ReachedProvider_ARefusalTheProviderAnswered_IsCountedAsACall(ImageDescriptionRefusal refusal)
    {
        // Act
        var description = ImageAttachmentDescription.Refused(refusal);

        // Assert
        Assert.True(description.ReachedProvider);
    }
}
