// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Spaces.Mail.Reading;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail.Reading;

/// <summary>The contrast floor a sender's colour clears before the pane draws it.</summary>
/// <remarks>
/// The floor is what keeps the pane's last word on legibility: a message is written for the background its author had
/// in mind, and taken verbatim a colour chosen for white paper is unreadable on a dark surface. What the drawing does
/// with the answer needs a loaded theme and a visual tree; the answer itself does not, and it is the half a defect
/// would live in.
/// </remarks>
public sealed class MailBodyDrawingTests
{
    /// <summary>A colour drawn on itself reads as nothing, whatever either of the two guards thought of it alone.</summary>
    /// <remarks>
    /// This is the middle grey that clears a light surface as ink and clears the theme's ink as a background, so a
    /// sender choosing it for a cell and for the words inside the cell cleared two guards that never spoke to each
    /// other and drew text at a contrast of one against its own background.
    /// </remarks>
    [Fact]
    public void Legible_AColourDrawnOnItself_IsRefused()
    {
        // Arrange
        var shade = Windows.UI.Color.FromArgb(byte.MaxValue, 0x76, 0x76, 0x76);

        // Act, Assert
        Assert.False(MailBodyDrawing.Legible(shade, shade));
    }

    /// <summary>Black on white and white on black are the two extremes, and both read.</summary>
    [Theory]
    [InlineData(0x00, 0xFF)]
    [InlineData(0xFF, 0x00)]
    public void Legible_TheFurthestApartAColourPairCanBe_IsAdmitted(byte ink, byte ground)
    {
        // Act, Assert
        Assert.True(MailBodyDrawing.Legible(Grey(ink), Grey(ground)));
    }

    /// <summary>The floor is WCAG's ratio for body text, so a pair just under it is refused and one just over admitted.</summary>
    /// <remarks>
    /// Measured against white: <c>#767676</c> is the darkest grey that clears 4.5 on it and <c>#777777</c> the
    /// lightest that does not, which is what pins the floor to a number rather than to whichever side of it the
    /// implementation happens to fall.
    /// </remarks>
    [Fact]
    public void Legible_EitherSideOfTheFloor_SeparatesThem()
    {
        // Arrange
        var white = Grey(0xFF);

        // Act, Assert
        Assert.True(MailBodyDrawing.Legible(Grey(0x76), white));
        Assert.False(MailBodyDrawing.Legible(Grey(0x77), white));
    }

    private static Windows.UI.Color Grey(byte value) =>
        Windows.UI.Color.FromArgb(byte.MaxValue, value, value, value);
}
