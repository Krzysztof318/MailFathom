// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Threads;

namespace MailFathom.Mcp.Tools.Content;

/// <summary>Publishes the conversation one read email belongs to.</summary>
/// <remarks>
/// It answers what a reader asks next — what else is in this exchange, and where does what I am reading sit in it —
/// without returning any of it. Only messages the caller may see are named, and the count is of those, so a folder
/// withheld from tools is not disclosed by a number that includes it.
/// </remarks>
[Description("The conversation this email belongs to: its identifier, where this email sits in it, and the other messages in it named rather than reproduced.")]
internal sealed record RetrievedEmailThread
{
    /// <summary>Gets the conversation's identifier.</summary>
    [Description("The identifier of the conversation. Pass it back as threadId to read the conversation's messages instead of naming them one by one.")]
    public required string ThreadId { get; init; }

    /// <summary>Gets where this email sits in the conversation's order, or <see langword="null" /> when it is unplaced.</summary>
    [Description("The zero-based place this email holds in the conversation's order, or null when the conversation was longer than one read assembles and this email fell outside what was assembled.")]
    public int? Position { get; init; }

    /// <summary>Gets the message this email answers, or <see langword="null" /> when it opens what is shown.</summary>
    [Description("The storedEmailId of the message this email answers, or null when it is a root of what you are shown. Null does not mean this email opened the conversation: a message whose parent is not held here, or sits in a folder withheld from tools, is published as a root.")]
    public string? InReplyToStoredEmailId { get; init; }

    /// <summary>Gets how many messages of the conversation the caller may see, this email included.</summary>
    [Description("How many messages of the conversation are readable here, this email included. Messages in folders withheld from tools are in neither this count nor the list below.")]
    public required int MessageCount { get; init; }

    /// <summary>Gets the conversation's other messages, in its own order.</summary>
    [Description("The conversation's other messages in its own order, without this one. Bounded: moreMessagesNotNamed says when the list stops short of the conversation.")]
    public required IReadOnlyList<NamedThreadedEmail> OtherMessages { get; init; }

    /// <summary>Gets whether the conversation holds messages this list does not name.</summary>
    [Description("Whether the conversation holds messages otherMessages does not name. When true, read the conversation itself by calling again with threadId.")]
    public required bool MoreMessagesNotNamed { get; init; }

    /// <summary>Publishes the conversation a read returned for one email.</summary>
    /// <param name="thread">The conversation to publish.</param>
    /// <returns>The wire representation of <paramref name="thread" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> is <see langword="null" />.</exception>
    public static RetrievedEmailThread From(ReadEmailThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        return new RetrievedEmailThread
        {
            ThreadId = thread.ThreadId.ToString(),
            Position = thread.Position,
            InReplyToStoredEmailId = thread.AnsweredStoredEmailId?.ToString(),
            MessageCount = thread.MessageCount,
            OtherMessages = [.. thread.OtherMessages.Select(NamedThreadedEmail.From)],
            MoreMessagesNotNamed = thread.MoreMessagesNotNamed,
        };
    }
}
