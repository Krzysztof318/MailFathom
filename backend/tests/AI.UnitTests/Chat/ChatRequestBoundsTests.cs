// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Application.Chat;
using Xunit;

namespace MailFathom.AI.UnitTests.Chat;

/// <summary>Covers the bound on what leaves the deployment, which is checked before anything is sent.</summary>
public sealed class ChatRequestBoundsTests
{
    private const int MaximumMessages = 4;
    private const int MaximumCharacters = 100;

    [Fact]
    public void Require_AConversationInsideBothBounds_IsAccepted()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.System, "answer briefly"),
            new(ChatRole.User, "what did they say"),
        ];

        // Act
        var refusal = Record.Exception(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters));

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public void Require_AnEmptyConversation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require([], MaximumMessages, MaximumCharacters));
    }

    [Fact]
    public void Require_NoConversation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ChatRequestBounds.Require(null!, MaximumMessages, MaximumCharacters));
    }

    [Fact]
    public void Require_MoreTurnsThanOneCallSends_IsRefused()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            .. Enumerable
                .Range(0, MaximumMessages + 1)
                .Select(turn => new ChatMessage(ChatRole.User, $"turn {turn}")),
        ];

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters));
    }

    /// <summary>A provider bills for the tokens around a blank turn and the model is left guessing what it meant.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_ABlankTurn_IsRefused(string text)
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new(ChatRole.User, text)];

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters));
    }

    /// <summary>The ceiling is on the whole conversation rather than on any one turn, so several small turns reach it too.</summary>
    [Fact]
    public void Require_MoreCharactersThanOneCallSends_IsRefused()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            .. Enumerable
                .Range(0, MaximumMessages)
                .Select(_ => new ChatMessage(ChatRole.User, new string('a', (MaximumCharacters / MaximumMessages) + 1))),
        ];

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters));
    }

    /// <summary>The refusal reaches a log, so it carries the size of the conversation and none of its text.</summary>
    [Fact]
    public void Require_AnOversizedConversation_NamesTheSizeAndNoneOfTheText()
    {
        // Arrange
        const string secret = "the quarterly figures nobody was meant to see";
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.User, secret + new string('a', MaximumCharacters)),
        ];

        // Act
        var refusal = Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters));

        // Assert
        Assert.DoesNotContain(secret, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(MaximumCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture), refusal.Message, StringComparison.Ordinal);
    }
}
