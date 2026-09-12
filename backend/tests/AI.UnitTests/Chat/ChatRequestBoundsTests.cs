// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using Xunit;

namespace MailFathom.AI.UnitTests.Chat;

/// <summary>Covers the bounds on what leaves the deployment, both of which are checked before anything is sent.</summary>
public sealed class ChatRequestBoundsTests
{
    private const int MaximumMessages = 4;
    private const int MaximumCharacters = 100;
    private const int MaximumImageOctets = 64;

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
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public void Require_AnEmptyConversation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require([], MaximumMessages, MaximumCharacters, MaximumImageOctets));
    }

    [Fact]
    public void Require_NoConversation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ChatRequestBounds.Require(null!, MaximumMessages, MaximumCharacters, MaximumImageOctets));
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
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));
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
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));
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
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));
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
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));

        // Assert
        Assert.DoesNotContain(secret, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(MaximumCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Require_AnImageInsideTheOctetCeiling_IsAccepted()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.User, "describe this", new ChatImage("image/png", new byte[MaximumImageOctets])),
        ];

        // Act
        var refusal = Record.Exception(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public void Require_MoreImageOctetsThanOneCallSends_IsRefused()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.User, "describe this", new ChatImage("image/png", new byte[MaximumImageOctets + 1])),
        ];

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, MaximumImageOctets));
    }

    [Fact]
    public void Require_AnImageWhereTheEndpointCarriesNone_IsRefused()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.User, "describe this", new ChatImage("image/png", new byte[1])),
        ];

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ChatRequestBounds.Require(conversation, MaximumMessages, MaximumCharacters, maximumImageOctets: 0));
    }

    /// <summary>A model wide enough for the conversation is asked, which is the ordinary attempt and has to cost nothing.</summary>
    [Fact]
    public void RequireForAttempt_AConversationTheModelCanCarry_IsAccepted()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new(ChatRole.User, "what did they say")];

        // Act
        var refusal = Record.Exception(
            () => ChatRequestBounds.RequireForAttempt(conversation, ChatDeclarations.Plan()));

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>
    /// A fallback declared narrower than the model in front of it is the case this exists for: the conversation was
    /// admitted against the main model and is too wide for this one, so it is refused here rather than sent and paid for.
    /// </summary>
    [Fact]
    public void RequireForAttempt_AConversationWiderThanTheModelDeclares_IsRefusedAsThatModelsFailure()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new(ChatRole.User, new string('a', 200))];
        var narrow = ChatDeclarations.Plan(
            ChatDeclarations.Endpoint("standby"),
            maximumRequestCharacters: 100);

        // Act
        var refusal = Assert.Throws<ChatGenerationFailedException>(
            () => ChatRequestBounds.RequireForAttempt(conversation, narrow));

        // Assert
        Assert.Equal(ChatGenerationFailure.RequestRefused, refusal.Failure);
        Assert.Contains("standby", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The refusal keeps what was measured, so an operator reads which bound was exceeded rather than only that one was.</summary>
    [Fact]
    public void RequireForAttempt_AConversationWiderThanTheModelDeclares_KeepsTheMeasurementAsTheCause()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new(ChatRole.User, new string('a', 200))];
        var narrow = ChatDeclarations.Plan(maximumRequestCharacters: 100);

        // Act
        var refusal = Assert.Throws<ChatGenerationFailedException>(
            () => ChatRequestBounds.RequireForAttempt(conversation, narrow));

        // Assert
        var cause = Assert.IsAssignableFrom<ArgumentException>(refusal.InnerException);
        Assert.Contains("200", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireForAttempt_WithoutAModel_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => ChatRequestBounds.RequireForAttempt([new(ChatRole.User, "ask")], null!));
    }
}
