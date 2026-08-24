// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation;
using Microsoft.UI.Xaml;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>What turns "the session offers this" into a control being on the screen or absent from it.</summary>
public sealed class OfferedVisibilityConverterTests
{
    private readonly OfferedVisibilityConverter converter = new();

    /// <summary>An outright yes is the only thing that puts a capability in front of somebody.</summary>
    [Fact]
    public void Convert_ACapabilityTheSessionOffers_PutsTheControlOnTheScreen()
    {
        // Act
        var visibility = this.converter.Convert(true, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Visible, visibility);
    }

    /// <summary>
    /// Everything else collapses, and the unknown case is the reason that matters: a feed carrying no value yet — the
    /// session is still being fetched, or the fetch failed — reaches a binding as nothing at all, and reading that as
    /// "offer it" would put a space in front of somebody before anything said they may use it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData("true")]
    public void Convert_AnythingThatIsNotAnOutrightYes_LeavesTheControlAbsent(object? value)
    {
        // Act
        var visibility = this.converter.Convert(value, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Collapsed, visibility);
    }

    /// <summary>The control that explains an absence appears where the session outright refused the capability.</summary>
    [Fact]
    public void Convert_WithheldOverACapabilityTheSessionRefuses_PutsTheExplanationOnTheScreen()
    {
        // Arrange
        var withheld = new OfferedVisibilityConverter { Withheld = true };

        // Act
        var visibility = withheld.Convert(false, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Visible, visibility);
    }

    /// <summary>
    /// The two sides are never both on the screen, and neither is there while the session is unknown: an explanation
    /// shown before anything refused anything would report a fetch still under way as a capability taken away.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    [InlineData("false")]
    public void Convert_WithheldOverAnythingThatIsNotAnOutrightNo_LeavesTheExplanationAbsent(object? value)
    {
        // Arrange
        var withheld = new OfferedVisibilityConverter { Withheld = true };

        // Act
        var visibility = withheld.Convert(value, typeof(Visibility), null, null);

        // Assert
        Assert.Equal(Visibility.Collapsed, visibility);
    }

    /// <summary>What the client offers is read from the session, so a control's visibility is never written back to it.</summary>
    [Fact]
    public void ConvertBack_AnyVisibility_IsRefused()
    {
        // Act, Assert
        Assert.Throws<NotSupportedException>(
            () => this.converter.ConvertBack(Visibility.Visible, typeof(bool), null, null));
    }
}
