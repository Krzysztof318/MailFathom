// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>The mail a test arranges a list out of, composed once rather than at each arrangement.</summary>
/// <remarks>
/// A row of the timeline carries seventeen fields and a test is usually about one of them, so composing one by hand in
/// each arrangement would bury what the test is actually saying. Everything here is invented: no address, subject, or
/// preview below belongs to anybody.
/// </remarks>
internal static class MailMessages
{
    /// <summary>Composes one message, defaulting everything a test is not about.</summary>
    /// <param name="number">What makes this message's identity its own, written into the identifier.</param>
    /// <param name="receivedAt">When it arrived, or <see langword="null" /> where no header dated it.</param>
    /// <param name="subject">What it is about, or <see langword="null" /> where it carried no subject.</param>
    /// <param name="senderDisplayName">The name the sender wrote, or <see langword="null" /> where the header carried none.</param>
    /// <param name="senderAddress">The address the sender wrote, or <see langword="null" /> where none was found.</param>
    /// <param name="recipients">Who it went to.</param>
    /// <param name="unread">Whether the mail server last reported it without <c>\Seen</c>.</param>
    /// <param name="flagged">Whether it last reported it with <c>\Flagged</c>.</param>
    /// <param name="answered">Whether it last reported it with <c>\Answered</c>.</param>
    /// <param name="attachmentCount">How many attachments it carries.</param>
    /// <param name="preview">The opening of its own text, or <see langword="null" /> where nothing has extracted it.</param>
    /// <returns>The message.</returns>
    internal static DeploymentMailMessage Message(
        int number,
        DateTimeOffset? receivedAt = null,
        string? subject = "Quarterly review",
        string? senderDisplayName = "Someone",
        string? senderAddress = "someone@example.test",
        IReadOnlyList<string>? recipients = null,
        bool unread = false,
        bool flagged = false,
        bool answered = false,
        int attachmentCount = 0,
        string? preview = null) =>
        new(
            Identity(number),
            "work",
            "INBOX",
            ThreadId: null,
            subject,
            receivedAt,
            receivedAt,
            senderAddress,
            senderDisplayName,
            recipients ?? ["owner@example.test"],
            unread,
            flagged,
            answered,
            attachmentCount > 0,
            attachmentCount,
            SizeOctets: 1024,
            preview);

    /// <summary>Names the identity a numbered message carries, so a test can assert a row without repeating a value.</summary>
    /// <param name="number">The number the message was composed with.</param>
    /// <returns>The identifier.</returns>
    internal static Guid Identity(int number) =>
        Guid.ParseExact(number.ToString("D32", CultureInfo.InvariantCulture), "N");

    /// <summary>Names the key the list draws for a numbered message, which is its identity written out.</summary>
    /// <param name="number">The number the message was composed with.</param>
    /// <returns>The row key.</returns>
    internal static string Key(int number) => Identity(number).ToString("D", CultureInfo.InvariantCulture);
}
