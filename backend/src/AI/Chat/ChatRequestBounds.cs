// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// <summary>Refuses a conversation the model about to be asked could not carry, as that model's own failure.</summary>
    /// <param name="conversation">The turns the caller asked to send.</param>
    /// <param name="model">The model this attempt is against, whose own declared bounds decide.</param>
    /// <exception cref="ChatGenerationFailedException">Thrown when the conversation exceeds what this model declares.</exception>
    /// <remarks>
    /// Every model of a chain declares its own bounds, so a conversation the main model admits may be wider than a
    /// fallback behind it accepts. Checking again per attempt keeps <see cref="Require" />'s trade — refused rather
    /// than sent and billed for — and states the refusal as that model's failure rather than as an argument the caller
    /// got wrong, because by then the caller's own request was already admitted. <see cref="ChatGenerationFailure.RequestRefused" />
    /// is the classification a provider rejecting the same request would arrive as, and it is the one a chain does not
    /// fall through on: a second model is a second endpoint rather than a wider one.
    /// </remarks>
    public static void RequireForAttempt(IReadOnlyList<ChatMessage> conversation, ChatGenerationPlan model)
    {
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            Require(
                conversation,
                model.MaximumMessagesPerRequest,
                model.MaximumRequestCharacters,
                model.MaximumRequestImageOctets);
        }
        catch (ArgumentException refusal)
        {
            throw new ChatGenerationFailedException(
                model.Endpoint.Alias,
                ChatGenerationFailure.RequestRefused,
                refusal);
        }
    }

    /// <summary>Refuses a conversation that is empty, blank in part, or larger than one call sends.</summary>
    /// <param name="conversation">The turns the caller asked to send.</param>
    /// <param name="maximumMessages">The greatest number of turns one request carries.</param>
    /// <param name="maximumCharacters">The greatest number of characters those turns may add up to.</param>
    /// <param name="maximumImageOctets">The greatest number of octets the images of those turns may add up to, which zero states as no image at all.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the conversation is empty, holds a blank turn, or exceeds any bound.</exception>
    public static void Require(
        IReadOnlyList<ChatMessage> conversation,
        int maximumMessages,
        int maximumCharacters,
        int maximumImageOctets)
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

        // Refused rather than dropped, and named apart from the octet ceiling below, because an endpoint declaring no
        // image budget is a text-only model: sending it a picture is a request it rejects, and silently sending the
        // turn without its picture would ask the model about something it was never shown.
        if (maximumImageOctets == 0 && conversation.Any(turn => turn.Image is not null))
        {
            throw new ArgumentException("This endpoint is declared to carry no image.", nameof(conversation));
        }

        var imageOctetCount = conversation.Sum(turn => (long)(turn.Image?.Content.Length ?? 0));

        if (imageOctetCount > maximumImageOctets)
        {
            // A size, exactly as the character count below is, and safe to name for the same reason: it says how much
            // was carried without saying any of what was in it.
            throw new ArgumentException(
                $"A call sends at most {maximumImageOctets} image octets, and this one carries {imageOctetCount}.",
                nameof(conversation));
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
