// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers the instruction the Agent is composed with.</summary>
public sealed class AgentConversationInstructionsTests
{
    /// <summary>Every language the deployment writes in has an instruction, and it names that language as the one the Agent's own words are in.</summary>
    [Fact]
    public void TextFor_EveryLanguage_NamesThatLanguageForTheAgentsOwnWords()
    {
        // Act
        var texts = Enum.GetValues<UserLanguage>().Select(static language => (language, AgentConversationInstructions.TextFor(language)));

        // Assert
        Assert.All(texts, static pair => Assert.Contains($"Write your own words — your answer, a summary, a paraphrase — in {pair.language}.", pair.Item2, StringComparison.Ordinal));
    }

    /// <summary>A day and an hour written the way the mail and the calendar write them are the ones the person recognises.</summary>
    [Fact]
    public void TextFor_TheInstruction_StatesHowADateAndATimeAreWritten()
    {
        // Act
        var text = string.Join(' ', AgentConversationInstructions.TextFor(UserLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("Write a date as the day before the month's name", text, StringComparison.Ordinal);
        Assert.Contains("a time on the 24-hour clock", text, StringComparison.Ordinal);
    }

    /// <summary>A quotation keeps the mail's language, so an answer that is only a quotation is not written in the person's.</summary>
    [Fact]
    public void TextFor_EveryLanguage_FramesAQuotationInThatLanguage()
    {
        // Act
        var texts = Enum.GetValues<UserLanguage>().Select(static language => (language, string.Join(' ', AgentConversationInstructions.TextFor(language).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

        // Assert
        Assert.All(texts, static pair => Assert.Contains($"A quotation never stands alone as your answer: even when the person asks for the exact words, say in {pair.language} what they are", pair.Item2, StringComparison.Ordinal));
    }
}
