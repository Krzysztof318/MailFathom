// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the rules the run's own instruction has to carry, which are the whole of what decides them.</summary>
/// <remarks>
/// A rule stated only in the instruction text is a rule nothing else in the process implements, so the text is where it
/// is asserted. What each assertion protects is a behavior an operator would meet as a wrong answer rather than as a
/// failure: an answer written in the language of the mail instead of the language of the question, or a mailbox
/// reported as holding nothing because every lookup was worded in a language it does not carry.
/// </remarks>
public sealed class MailAnsweringInstructionsTests
{
    /// <summary>The instruction as one line, so an assertion is about what it says rather than about where it wraps.</summary>
    private static readonly string Instruction = MailAnsweringInstructions.Text.ReplaceLineEndings(" ");

    /// <summary>The language of an answer follows the question, because that is the one thing the caller did ask for.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAnAnswerIsWrittenInTheLanguageOfTheQuestion()
    {
        // Assert
        Assert.Contains(
            "in the language the question was asked in, whatever language the mail is in",
            Instruction,
            StringComparison.Ordinal);
    }

    /// <summary>A quotation checked against the mail it came from has to be the words the mail carried.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatQuotedMailKeepsItsOwnWording()
    {
        // Assert
        Assert.Contains("in its own wording", Instruction, StringComparison.Ordinal);
        Assert.Contains("rendering into the question's language", Instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lexical half of retrieval matches a word against a word, so a lookup worded in the language of the question
    /// reaches mail written in another language only as far as the vector half happens to carry it.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_StatesThatALookupIsWordedInTheLanguageTheMailIsLikelyWrittenIn()
    {
        // Assert
        Assert.Contains(ScopedMailKnowledgeRetrieval.QueryArgumentName, Instruction, StringComparison.Ordinal);
        Assert.Contains(
            "in the language that mail is likely written in, which need not be the language you were asked in",
            Instruction,
            StringComparison.Ordinal);
    }

    /// <summary>One empty lookup is not a mailbox without an answer, and another language is one of the things to try before saying it is.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAnEmptyLookupIsRetriedInAnotherLanguage()
    {
        // Assert
        Assert.Contains("another language this mailbox plausibly holds", Instruction, StringComparison.Ordinal);
    }
}
