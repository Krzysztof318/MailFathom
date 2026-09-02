// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails;

public sealed class MailTextBoundsTests
{
    [Fact]
    public void TruncateAtTextElementBoundary_TextWithinTheBound_KeepsItUnchanged()
    {
        // Act
        var bounded = MailTextBounds.TruncateAtTextElementBoundary("Quarterly report", 32);

        // Assert
        Assert.Equal("Quarterly report", bounded);
    }

    /// <summary>The cut leaves a string every consumer can write, which a cut through a surrogate pair would not.</summary>
    [Fact]
    public void TruncateAtTextElementBoundary_BoundFallingInsideASurrogatePair_CutsBeforeIt()
    {
        // Arrange
        const string textEndingInAnEmoji = "ab\U0001F600";

        // Act
        var bounded = MailTextBounds.TruncateAtTextElementBoundary(textEndingInAnEmoji, 3);

        // Assert
        Assert.Equal("ab", bounded);
    }

    /// <summary>A combining sequence is one character to a reader, so the cut keeps it whole or drops it whole.</summary>
    [Fact]
    public void TruncateAtTextElementBoundary_BoundFallingInsideACombiningSequence_CutsBeforeIt()
    {
        // Arrange
        const string textEndingInACombiningSequence = "aé";

        // Act
        var bounded = MailTextBounds.TruncateAtTextElementBoundary(textEndingInACombiningSequence, 2);

        // Assert
        Assert.Equal("a", bounded);
    }

    /// <summary>Nothing left is reported as nothing, so the caller decides what an unusable value means.</summary>
    [Fact]
    public void TruncateAtTextElementBoundary_FirstElementLongerThanTheBound_YieldsNothing()
    {
        // Act
        var bounded = MailTextBounds.TruncateAtTextElementBoundary("\U0001F600tail", 1);

        // Assert
        Assert.Equal(string.Empty, bounded);
    }

    [Fact]
    public void TruncateAtTextElementBoundary_NegativeBound_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailTextBounds.TruncateAtTextElementBoundary("text", -1));
    }

    [Fact]
    public void TruncateAtTextElementBoundary_NoText_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => MailTextBounds.TruncateAtTextElementBoundary(null!, 8));
    }
}
