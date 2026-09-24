// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Descriptions;
using Xunit;

namespace MailFathom.AI.UnitTests.Descriptions;

/// <summary>Covers the rules a description of a picture has to follow, which the instruction is the whole of.</summary>
/// <remarks>
/// A description is searched by the characters it carries, so each rule protects a message that would otherwise be
/// unfindable by what the picture shows: a number missing a digit, a word read the wrong way round, a label translated
/// out of the language somebody searches in.
/// </remarks>
public sealed class ImageDescriptionInstructionsTests
{
    /// <summary>The instruction as one line, so an assertion is about what it says rather than about where it wraps.</summary>
    private static readonly string Instruction = ImageDescriptionInstructions.Text.ReplaceLineEndings(" ");

    /// <summary>One character changed is a search that no longer finds the message.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForTheWordsCharacterForCharacter()
    {
        // Assert
        Assert.Contains("Copy every word you write out of the picture character for character", Instruction, StringComparison.Ordinal);
        Assert.Contains("never correct a spelling, complete a word", Instruction, StringComparison.Ordinal);
    }

    /// <summary>The describer reads the kind of answer from its first line, so the instruction has to ask for exactly that line.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForTheKindOfAnswerOnItsFirstLine()
    {
        // Assert
        Assert.Contains(
            $"one line holding exactly {ImageDescriptionInstructions.TranscriptionMarker} or {ImageDescriptionInstructions.DescriptionMarker}",
            Instruction,
            StringComparison.Ordinal);
    }

    /// <summary>Characters a font draws alike are exactly the ones a guess gets wrong in a code.</summary>
    [Fact]
    public void Text_TheInstruction_NamesTheCharactersThatLookAlike()
    {
        // Assert
        Assert.Contains("0 and O, 1 and l and I", Instruction, StringComparison.Ordinal);
    }

    /// <summary>Text turned on the page is where a describer stops reading and starts describing.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForTextInAnyOrientationToBeRead()
    {
        // Assert
        Assert.Contains("sideways or upside down in its own direction", Instruction, StringComparison.Ordinal);
    }

    /// <summary>A mailbox is searched in the language its mail carries, so a translated label is one nobody finds.</summary>
    [Fact]
    public void Text_TheInstruction_KeepsTheWordsInTheLanguageTheyArePrintedIn()
    {
        // Assert
        Assert.Contains("with every accent and diacritic, and never translate them", Instruction, StringComparison.Ordinal);
    }
}
