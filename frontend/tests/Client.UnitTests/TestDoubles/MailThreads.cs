// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Threads;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>The conversations a test arranges a thread out of, composed once rather than at each arrangement.</summary>
/// <remarks>
/// A conversation carries eight fields around a list of messages that carry seventeen each, and a test is usually about
/// one of them. Everything here is invented: no address, subject, or contribution below belongs to anybody.
/// </remarks>
internal static class MailThreads
{
    /// <summary>The conversation every arrangement here is of.</summary>
    internal static Guid Identity { get; } = Guid.ParseExact("00000000000000000000000000000099", "N");

    /// <summary>Composes one page of a conversation, defaulting everything a test is not about.</summary>
    /// <param name="messages">The messages the page carries, in the conversation's own order.</param>
    /// <param name="participants">Everybody the deployment names as having written in it.</param>
    /// <param name="messageCount">How many messages the whole conversation holds, which defaults to what the page carries.</param>
    /// <param name="moreMessagesNotAssembled">Whether it runs past what one read assembles.</param>
    /// <param name="moreParticipantsNotNamed">Whether it has authors the participants do not name.</param>
    /// <param name="nextCursor">The cursor continuing it, or <see langword="null" /> where it ends there.</param>
    /// <returns>The page.</returns>
    internal static DeploymentMailThreadPage Page(
        IReadOnlyList<DeploymentThreadMessage> messages,
        IReadOnlyList<DeploymentThreadParticipant>? participants = null,
        int? messageCount = null,
        bool moreMessagesNotAssembled = false,
        bool moreParticipantsNotNamed = false,
        string? nextCursor = null) =>
        new(
            Identity,
            messages,
            participants ?? [new DeploymentThreadParticipant("someone@example.test", "Someone", messages.Count)],
            messageCount ?? messages.Count,
            moreMessagesNotAssembled,
            moreParticipantsNotNamed,
            nextCursor,
            PageSize: 50);

    /// <summary>Composes one message of a conversation around the message the list would draw for it.</summary>
    /// <param name="number">What makes this message's identity its own.</param>
    /// <param name="position">Where it sits in the conversation's order, which defaults to its number.</param>
    /// <param name="answered">The message it answers, or <see langword="null" /> where it is a root of what is shown.</param>
    /// <param name="contribution">What it added, or <see langword="null" /> where nothing has extracted it.</param>
    /// <param name="sentAt">When it was written, or <see langword="null" /> where no header dated it.</param>
    /// <param name="senderDisplayName">The name the sender wrote, or <see langword="null" /> where the header carried none.</param>
    /// <param name="senderAddress">The address the sender wrote, or <see langword="null" /> where none was found.</param>
    /// <param name="recipients">Who it went to.</param>
    /// <param name="unread">Whether the mail server last reported it without <c>\Seen</c>.</param>
    /// <param name="subject">What it is about, or <see langword="null" /> where it carried no subject.</param>
    /// <returns>The message.</returns>
    internal static DeploymentThreadMessage Message(
        int number,
        int? position = null,
        Guid? answered = null,
        string? contribution = "What this one added",
        DateTimeOffset? sentAt = null,
        string? senderDisplayName = "Someone",
        string? senderAddress = "someone@example.test",
        IReadOnlyList<string>? recipients = null,
        bool unread = false,
        string? subject = "Quarterly review") =>
        new(
            position ?? number,
            answered,
            MailMessages.Message(
                number,
                sentAt,
                subject,
                senderDisplayName,
                senderAddress,
                recipients,
                unread,
                preview: contribution,
                threadId: Identity));

    /// <summary>Writes one page of a conversation as the deployment writes it on the wire.</summary>
    /// <param name="messages">How many messages the page carries, numbered from one.</param>
    /// <param name="nextCursor">The cursor continuing it, or <see langword="null" /> where it ends there.</param>
    /// <param name="from">The number the page's first message carries, so a second page follows the first.</param>
    /// <returns>The document.</returns>
    /// <remarks>
    /// Written as text rather than serialized from the record above, because what a test of the client's own reader is
    /// about is the wire format: a document composed through this client's serializer could not fail the way an answer
    /// naming a member differently would.
    /// </remarks>
    internal static string Document(int messages, string? nextCursor = null, int from = 1)
    {
        var written = Enumerable
            .Range(from, messages)
            .Select(number => string.Create(
                CultureInfo.InvariantCulture,
                $$"""
                  {
                    "position": {{number - 1}},
                    "answeredId": null,
                    "email": {
                      "id": "{{MailMessages.Identity(number)}}",
                      "account": "work",
                      "folder": "INBOX",
                      "threadId": "{{Identity}}",
                      "subject": "Quarterly review",
                      "receivedAt": "2026-08-15T09:58:00+00:00",
                      "sentAt": "2026-08-15T09:57:12+00:00",
                      "senderAddress": "someone@example.test",
                      "senderDisplayName": "Someone",
                      "toAddresses": [ "owner@example.test" ],
                      "unread": false,
                      "flagged": false,
                      "answered": false,
                      "hasAttachments": false,
                      "attachmentCount": 0,
                      "sizeOctets": 1024,
                      "preview": "What this one added"
                    }
                  }
                  """));

        var cursor = nextCursor is null ? "null" : $"\"{nextCursor}\"";

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              {
                "threadId": "{{Identity}}",
                "messages": [ {{string.Join(',', written)}} ],
                "participants": [
                  { "address": "someone@example.test", "displayName": "Someone", "messageCount": {{messages}} }
                ],
                "messageCount": {{messages}},
                "moreMessagesNotAssembled": false,
                "moreParticipantsNotNamed": false,
                "nextCursor": {{cursor}},
                "pageSize": 50
              }
              """);
    }
}
