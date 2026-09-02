// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>Reads the messages one conversation holds, from the local mailbox copy.</summary>
public interface IEmailThreadReader
{
    /// <summary>The greatest number of emails one read assembles out of a conversation.</summary>
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
    const int MaximumAssembledEmails = 500;

    /// <summary>Reads the emails of one conversation the caller may see, whatever readable folders they sit in.</summary>
    /// <param name="threadId">The conversation to read, which may be one a merge has since folded into another.</param>
    /// <param name="scope">The accounts and folders configuration admits to tools, which the query is narrowed by.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The conversation's messages in no particular order, at most <see cref="MaximumAssembledEmails" /> plus one, and
    /// empty when the identifier names no conversation this deployment holds.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The one row past the bound is what tells the caller the conversation runs further, and it is read rather than
    /// inferred: a conversation holding exactly <see cref="MaximumAssembledEmails" /> messages is complete, and a reader
    /// that stopped at the bound could not tell it from one that was cut. The extra row is a signal rather than content,
    /// so the caller drops it before anything is ordered, counted, or published.
    /// </para>
    /// <para>
    /// A merged conversation resolves to the one it was merged into, so a thread identifier a tool published before a
    /// merge still reaches the conversation it named instead of answering not-found.
    /// </para>
    /// <para>
    /// Tombstoned messages are excluded, which is what makes a deleted message leave the thread it was in.
    /// </para>
    /// <para>
    /// The scope narrows the query rather than the answer, which is what the bound above makes a correctness question
    /// instead of a preference. A conversation is threaded across every folder it reached, withheld ones included, so a
    /// bound applied before the withholding would spend its rows on mail the caller may never see and cut readable mail
    /// that sits behind it. The caller decides what a tool may read and hands that decision here, so the rows counted
    /// against the bound are the rows a caller could be shown.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ThreadedEmailSummary>> ReadEmailsAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken);
}
