// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ReplyDrafts;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers which language a drafting is told to write in, and what a turn carrying no correspondence says.</summary>
/// <remarks>
/// The two halves are one decision seen from either end. A reply is written in the conversation's language because the
/// person receiving it reads that; a message answering nothing has no such language, so the instruction names the one
/// the acting user's record holds — and an instruction the person typed outranks both, being the only thing they said
/// about the message themselves.
/// </remarks>
public sealed class ReplyDraftInstructionsTests
{
    /// <summary>A reply is read by the person it is sent to, so the conversation decides its language rather than the sender's record.</summary>
    [Theory]
    [InlineData(MailUserLanguage.English)]
    [InlineData(MailUserLanguage.Polish)]
    public void TextFor_AnyLanguage_StillWritesAReplyInTheConversationsOwnLanguage(MailUserLanguage language)
    {
        // Act
        var text = ReplyDraftInstructions.TextFor(language);

        // Assert
        Assert.Contains("the language the conversation is written in", text, StringComparison.Ordinal);
    }

    /// <summary>A message answering nothing has no conversation to take a language from, so the person's own is named.</summary>
    [Theory]
    [InlineData(MailUserLanguage.English)]
    [InlineData(MailUserLanguage.Polish)]
    public void TextFor_EachLanguage_NamesItForATurnCarryingNoConversation(MailUserLanguage language)
    {
        // Act
        var text = ReplyDraftInstructions.TextFor(language);

        // Assert
        Assert.Contains($"write it in {language}.", text, StringComparison.Ordinal);
    }

    /// <summary>What the person asked for is the one thing they said themselves, so it settles the language too.</summary>
    [Fact]
    public void TextFor_TheInstruction_LetsWhatThePersonAskedForOutrankBothLanguageRules()
    {
        // Act
        var text = ReplyDraftInstructions.TextFor(MailUserLanguage.English);

        // Assert
        Assert.Contains("an instruction asking for a particular language", text, StringComparison.Ordinal);
        Assert.Contains("outranks both", text, StringComparison.Ordinal);
    }

    /// <summary>A language this deployment does not write in is a composition mistake rather than a value to fall back from.</summary>
    [Fact]
    public void TextFor_ALanguageThisDeploymentDoesNotWriteIn_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplyDraftInstructions.TextFor((MailUserLanguage)97));

    /// <summary>Somebody starting a message is told what to do with the two arrays a conversation would have filled.</summary>
    [Fact]
    public void TextFor_TheInstruction_SaysWhatATurnWithNoConversationIs()
    {
        // Act
        var text = ReplyDraftInstructions.TextFor(MailUserLanguage.English);

        // Assert
        Assert.Contains("The turn may carry no conversation at all", text, StringComparison.Ordinal);
        Assert.Contains("leave both arrays empty", text, StringComparison.Ordinal);
    }

    /// <summary>An empty conversation section would read as an exchange whose text could not be fetched, which is a different case.</summary>
    [Fact]
    public void ComposeDraftingTurn_NoMessages_WritesNoConversationHeadingsAtAll()
    {
        // Act
        var turn = ReplyDraftInstructions.ComposeDraftingTurn(
            new GuardedDraftingTurn(
                Subject: null,
                [],
                [],
                [],
                Selection: null,
                "Ask Contoso for a 5% CPI cap."));

        // Assert
        Assert.DoesNotContain("The conversation", turn, StringComparison.Ordinal);
        Assert.DoesNotContain("People in this conversation", turn, StringComparison.Ordinal);
        Assert.DoesNotContain("Subject:", turn, StringComparison.Ordinal);
        Assert.Contains("Ask Contoso for a 5% CPI cap.", turn, StringComparison.Ordinal);
    }

    /// <summary>A conversation still reaches the turn under every heading a citation and a proposal are numbered against.</summary>
    [Fact]
    public void ComposeDraftingTurn_AConversation_WritesItUnderTheHeadingsThatNumberIt()
    {
        // Act
        var turn = ReplyDraftInstructions.ComposeDraftingTurn(
            new GuardedDraftingTurn(
                "The racking quotation",
                [new GuardedDraftingPerson(0, "Karolina")],
                [
                    new GuardedDraftingMessage(
                        0,
                        "Karolina",
                        new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                        "It is 4 200 zloty."),
                ],
                [],
                Selection: null,
                Instruction: null));

        // Assert
        Assert.Contains("Subject: The racking quotation", turn, StringComparison.Ordinal);
        Assert.Contains("Person 0: Karolina", turn, StringComparison.Ordinal);
        Assert.Contains("Message 0", turn, StringComparison.Ordinal);
    }
}
