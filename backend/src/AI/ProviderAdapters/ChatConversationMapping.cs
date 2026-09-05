// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns the conversation this system publishes into the one the client library sends.</summary>
/// <remarks>
/// <para>
/// A file of its own because it is the one place where both <c>ChatMessage</c> types and both <c>ChatRole</c> types are
/// in scope at once, and every name here is therefore written out in full on both sides. Keeping that in the adapter
/// would have spread the qualification across a class that mostly has nothing to do with it.
/// </para>
/// <para>
/// The mapping is total in one direction only. Every role this system publishes has a provider counterpart, and the
/// provider's own extra roles have none here — a tool turn in particular, which this boundary neither sends nor accepts.
/// </para>
/// <para>
/// A turn carrying a picture becomes two content parts rather than one, text first, because that is the order the model
/// reads them in: the instruction is what the picture is being shown for, and a provider given the octets first is
/// given them before it has been told what to do with them.
/// </para>
/// </remarks>
internal static class ChatConversationMapping
{
    /// <summary>Maps a conversation into the provider's own message type, preserving order.</summary>
    /// <param name="conversation">The turns to send, oldest first.</param>
    /// <returns>The same turns as the client library's messages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a turn names a role this boundary does not send.</exception>
    public static IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> ToProviderConversation(
        IReadOnlyList<Application.Chat.ChatMessage> conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return [.. conversation.Select(static turn => ToProviderMessage(turn))];
    }

    private static Microsoft.Extensions.AI.ChatMessage ToProviderMessage(Application.Chat.ChatMessage turn)
    {
        var role = ToProviderRole(turn.Role);

        if (turn.Image is not { } image)
        {
            return new Microsoft.Extensions.AI.ChatMessage(role, turn.Text);
        }

        // The media type is the one read from the octets by whoever composed the turn, never the one an attachment
        // declared, so the provider is told what it is actually about to decode.
        return new Microsoft.Extensions.AI.ChatMessage(
            role,
            [
                new Microsoft.Extensions.AI.TextContent(turn.Text),
                new Microsoft.Extensions.AI.DataContent(image.Content, image.MediaType),
            ]);
    }

    private static Microsoft.Extensions.AI.ChatRole ToProviderRole(Application.Chat.ChatRole role) => role switch
    {
        Application.Chat.ChatRole.System => Microsoft.Extensions.AI.ChatRole.System,
        Application.Chat.ChatRole.User => Microsoft.Extensions.AI.ChatRole.User,
        Application.Chat.ChatRole.Assistant => Microsoft.Extensions.AI.ChatRole.Assistant,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "The role names no turn this boundary sends."),
    };
}
