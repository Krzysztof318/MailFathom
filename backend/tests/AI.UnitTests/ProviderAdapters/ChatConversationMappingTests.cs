// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Chat;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one place this system's conversation becomes the client library's, where both sets of names are in scope at once.</summary>
public sealed class ChatConversationMappingTests
{
    [Theory]
    [InlineData(ChatRole.System)]
    [InlineData(ChatRole.User)]
    [InlineData(ChatRole.Assistant)]
    public void ToProviderConversation_ATurnOfText_CarriesItsRoleAndNothingElse(ChatRole role)
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new(role, "what did they say")];

        // Act
        var mapped = ChatConversationMapping.ToProviderConversation(conversation);

        // Assert
        var turn = Assert.Single(mapped);

        Assert.Equal(role.ToString(), turn.Role.Value, ignoreCase: true);
        Assert.Equal("what did they say", turn.Text);
        Assert.Single(turn.Contents);
    }

    /// <summary>A turn carrying a picture becomes text first and the octets second, which is the order the model is meant to read them in.</summary>
    [Fact]
    public void ToProviderConversation_ATurnCarryingAPicture_PutsTheTextInFrontOfTheOctets()
    {
        // Arrange
        byte[] octets = [0x89, 0x50, 0x4E, 0x47];
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.User, "describe this", new ChatImage("image/png", octets)),
        ];

        // Act
        var mapped = ChatConversationMapping.ToProviderConversation(conversation);

        // Assert
        var contents = Assert.Single(mapped).Contents;

        Assert.Equal(2, contents.Count);

        var text = Assert.IsType<Microsoft.Extensions.AI.TextContent>(contents[0]);

        Assert.Equal("describe this", text.Text);

        var image = Assert.IsType<Microsoft.Extensions.AI.DataContent>(contents[1]);

        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(octets, image.Data.ToArray());
    }

    /// <summary>Order is what a conversation is, so the mapping preserves it rather than rebuilding a set.</summary>
    [Fact]
    public void ToProviderConversation_SeveralTurns_KeepsTheOrderTheyWereGivenIn()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation =
        [
            new(ChatRole.System, "answer briefly"),
            new(ChatRole.User, "first"),
            new(ChatRole.Assistant, "second"),
        ];

        // Act
        var mapped = ChatConversationMapping.ToProviderConversation(conversation);

        // Assert
        Assert.Equal(["answer briefly", "first", "second"], mapped.Select(turn => turn.Text));
    }

    /// <summary>A role this boundary does not send is refused rather than mapped to whichever provider role sorts nearest.</summary>
    [Fact]
    public void ToProviderConversation_ARoleThisBoundaryDoesNotSend_IsRefused()
    {
        // Arrange
        IReadOnlyList<ChatMessage> conversation = [new((ChatRole)9, "from nowhere")];

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChatConversationMapping.ToProviderConversation(conversation));
    }
}
