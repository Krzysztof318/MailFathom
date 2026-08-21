// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;

namespace MailFathom.AI.Chat;

/// <summary>Checks what one chat call was asked to send, before anything is sent.</summary>
/// <remarks>
/// The bounds are refusals rather than truncations. Cutting a conversation down to fit would send the model a
/// different question from the one it was given and return an answer to that, which no caller could detect; refusing
/// hands the decision about what to drop back to whoever composed the conversation.
/// </remarks>
internal static class ChatRequestBounds
{
    /// <summary>Refuses a conversation that is empty, blank in part, or larger than one call sends.</summary>
    /// <param name="conversation">The turns the caller asked to send.</param>
    /// <param name="maximumMessages">The greatest number of turns one request carries.</param>
    /// <param name="maximumCharacters">The greatest number of characters those turns may add up to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the conversation is empty, holds a blank turn, or exceeds either bound.</exception>
    public static void Require(
        IReadOnlyList<ChatMessage> conversation,
        int maximumMessages,
        int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.Count == 0)
        {
            throw new ArgumentException("A call sends at least one turn.", nameof(conversation));
        }

        if (conversation.Count > maximumMessages)
        {
            throw new ArgumentException(
                $"A call sends at most {maximumMessages} turns, and this one names {conversation.Count}.",
                nameof(conversation));
        }

        // A blank turn is refused rather than sent, because a provider bills for the tokens around it and the model is
        // left to guess what an empty turn from that role was supposed to mean.
        if (conversation.Any(turn => string.IsNullOrWhiteSpace(turn.Text)))
        {
            throw new ArgumentException("A turn to send is not blank.", nameof(conversation));
        }

        var characterCount = conversation.Sum(turn => (long)turn.Text.Length);

        if (characterCount > maximumCharacters)
        {
            // The count is a size and says nothing about what was in the turns, which is why it is safe to name here:
            // this message reaches a log, and the conversation itself never may.
            throw new ArgumentException(
                $"A call sends at most {maximumCharacters} characters, and this one carries {characterCount}.",
                nameof(conversation));
        }
    }
}
