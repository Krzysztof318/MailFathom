// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.Application.EmailContent.Cleaning;
using Xunit;

namespace MailFathom.AI.UnitTests.BodyCleanup;

/// <summary>Covers the one turn an outline is put to the agent as, and what the instruction is allowed to ask for.</summary>
public sealed class MailBodyCleanupInstructionsTests
{
    [Fact]
    public void ComposeOutlineTurn_AnOutline_WritesTheEnvelopeAboveOneNumberedLinePerBlock()
    {
        // Arrange
        var body = new CleanableMailBody(
            "Your receipt",
            "The Shop",
            [
                new CleanableMailBlock(0, "paragraph", LinkCount: 0, "View this in your browser"),
                new CleanableMailBlock(1, "table", LinkCount: 4, "Shop Deals Account Help"),
            ]);

        // Act
        var turn = MailBodyCleanupInstructions.ComposeOutlineTurn(body);

        // Assert
        Assert.Equal(
            """
            Envelope sender: The Shop
            Subject: Your receipt

            Blocks: 2
            0 paragraph links=0 | View this in your browser
            1 table links=4 | Shop Deals Account Help

            """.ReplaceLineEndings("\n"),
            turn);
    }

    /// <summary>The envelope is what the rule about a pasted header block is decided against, so its absence is stated rather than left blank.</summary>
    [Fact]
    public void ComposeOutlineTurn_AMessageNamingNeitherSenderNorSubject_SaysSoRatherThanLeavingTheLinesEmpty()
    {
        // Arrange
        var body = new CleanableMailBody(
            Subject: null,
            SenderName: null,
            [new CleanableMailBlock(0, "paragraph", LinkCount: 0, "Anything")]);

        // Act
        var turn = MailBodyCleanupInstructions.ComposeOutlineTurn(body);

        // Assert
        Assert.Contains("Envelope sender: (unnamed)", turn, StringComparison.Ordinal);
        Assert.Contains("Subject: (none)", turn, StringComparison.Ordinal);
    }

    /// <summary>
    /// The instruction asks for ranges and names the two keywords the reading accepts, which is the one place the prose
    /// and the reading have to agree: a renamed keyword would be asked for and then refused on arrival.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_AsksForTheTwoKeywordsTheReadingAccepts()
    {
        // Assert
        Assert.Contains($"\"{MailBodyCleanupInstructions.KeepAction}\"", MailBodyCleanupInstructions.Text, StringComparison.Ordinal);
        Assert.Contains($"\"{MailBodyCleanupInstructions.DropAction}\"", MailBodyCleanupInstructions.Text, StringComparison.Ordinal);
    }
}
