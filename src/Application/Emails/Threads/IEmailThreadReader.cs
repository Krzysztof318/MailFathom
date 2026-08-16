// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>Reads the messages one conversation holds, from the local mailbox copy.</summary>
public interface IEmailThreadReader
{
    /// <summary>The greatest number of messages one read assembles out of a conversation.</summary>
    /// <remarks>
    /// <para>
    /// The bound on the query rather than on what a tool publishes, and the two are different numbers on purpose. This
    /// one exists because nothing about a mailing list bounds how long one exchange runs, and a read that walked a
    /// thread of ten thousand would spend a protocol call's memory on messages nobody is going to be shown.
    /// </para>
    /// <para>
    /// It is applied on the identity, so a conversation longer than this is cut at the same place on every read rather
    /// than at whichever rows the database happened to return.
    /// </para>
    /// </remarks>
    const int MaximumAssembledMessages = 500;

    /// <summary>Reads the messages of one conversation, whatever folders of the account they sit in.</summary>
    /// <param name="threadId">The conversation to read, which may be one a merge has since folded into another.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The conversation's messages in no particular order, bounded by <see cref="MaximumAssembledMessages" />, and empty
    /// when the identifier names no conversation this deployment holds.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A merged conversation resolves to the one it was merged into, so a thread identifier a tool published before a
    /// merge still reaches the conversation it named instead of answering not-found.
    /// </para>
    /// <para>
    /// Tombstoned messages are excluded, which is what makes a deleted message leave the thread it was in. Folder
    /// visibility is not applied here and is the caller's: it is one decision, made in the use case that already makes
    /// it for every other read, rather than a rule this query and that one would each have to keep.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<EmailThreadMessage>> ReadMessagesAsync(
        EmailThreadId threadId,
        CancellationToken cancellationToken);
}
