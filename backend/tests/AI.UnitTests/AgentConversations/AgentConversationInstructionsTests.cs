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
}
