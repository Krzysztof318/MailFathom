// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Emails;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Turns what a reply-drafting agent wrote into a draft, keeping only what the conversation itself can name.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer, the messages the turn published, and the people it published,
/// which is what makes the cases a provider produces once in a thousand runs ordinary examples in a test rather than
/// something only a live endpoint reaches.
/// </para>
/// <para>
/// It drops a citation and a recipient it cannot resolve, and keeps the claim. A number outside the list the turn
/// composed names nobody and nothing, so it falls away — and a claim left with no number at all is exactly the claim
/// the instruction asks for: one the correspondence does not back, kept and marked, because the sentence is in the
/// body whatever happens to its citation.
/// </para>
/// <para>
/// An answer with no body is nothing rather than an empty draft. There is no honest half of a reply, and a composer
/// showing an empty one would have made somebody wait for a provider call to be handed the blank page they started
/// with.
/// </para>
/// </remarks>
internal static class ReplyDraftReading
{
    private const string JsonFence = "```";

    /// <summary>Reads the draft out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="messages">The messages the turn published, in the order it numbered them.</param>
    /// <param name="participants">The people the turn published, in the order it numbered them.</param>
    /// <returns>The draft, or <see cref="ReplyDraft.Nothing" /> where the answer carried no reply.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> or <paramref name="participants" /> is <see langword="null" />.</exception>
    internal static ReplyDraft Read(
        string? answerText,
        IReadOnlyList<ReplyDraftMessage> messages,
        IReadOnlyList<ReplyDraftParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(participants);

        if (ReadDocument(answerText) is not { } document || string.IsNullOrWhiteSpace(document.Body))
        {
            return ReplyDraft.Nothing;
        }

        return ReplyDraft.Written(
            document.Body,
            [.. Claims(document.Claims, messages)],
            [.. Recipients(document.Recipients, participants)]);
    }

    /// <summary>Reads the written assertions into claims, keeping the ones nothing supports and marking them by their empty sources.</summary>
    private static IEnumerable<ReplyDraftClaim> Claims(
        IReadOnlyList<ReplyDraftClaimDocument?>? written,
        IReadOnlyList<ReplyDraftMessage> messages) =>
        (written ?? [])
            .Where(static claim => !string.IsNullOrWhiteSpace(claim?.Text))
            .Select(claim => ReplyDraftClaim.Create(
                claim!.Text!,
                [
                    .. (claim.Messages ?? [])
                        .Where(position => position >= 0 && position < messages.Count)
                        .Distinct()
                        .Select(position => messages[position].StoredEmailId),
                ]))
            .Take(ReplyDraft.MaximumClaims);

    /// <summary>Resolves the proposed people into the addresses this deployment already holds for them.</summary>
    /// <remarks>
    /// A position rather than an address is what the model answered with, so this is a lookup rather than a parse: a
    /// number the turn never published resolves to nobody and is dropped, which is what makes a drafting unable to
    /// address a reply outside the conversation it was shown.
    /// </remarks>
    private static IEnumerable<EmailAddress> Recipients(
        IReadOnlyList<int>? proposed,
        IReadOnlyList<ReplyDraftParticipant> participants) =>
        (proposed ?? [])
            .Where(position => position >= 0 && position < participants.Count)
            .Distinct()
            .Select(position => participants[position].Address)
            .Take(ReplyDraft.MaximumProposedRecipients);

    private static ReplyDraftDocument? ReadDocument(string? answerText)
    {
        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, ReplyDraftJsonContext.Default.ReplyDraftDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the JSON object inside whatever the model wrote around it.</summary>
    /// <remarks>
    /// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough
    /// that treating any of those as a failed drafting would throw away a usable reply. The outermost braces are what
    /// is read; anything either side of them is discarded unexamined.
    /// </remarks>
    private static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }
}
