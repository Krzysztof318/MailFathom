// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Where one conversation stands, as this deployment derived it and wrote it down.</summary>
/// <remarks>
/// <para>
/// Derived once and stored, which is the whole of what makes it a part of the application rather than a chat reply
/// somebody has to ask for again. A summary produced on demand and discarded is a message with better placement; a
/// state that is derived, kept, updated when the conversation changes, and cited is an object a screen can be built on
/// and a later stage can read.
/// </para>
/// <para>
/// A record with no statements at all is a real outcome and not a failure. It says a derivation ran and found nothing
/// to put beside the conversation — two messages arranging a time, a receipt and its acknowledgement — and storing it
/// is what stops the same nothing being paid for again. A conversation no derivation has reached carries no record at
/// all, which is what a client draws as *not derived yet* rather than as *nothing to say*.
/// </para>
/// <para>
/// It carries no participant list, and that is deliberate rather than missing. Who is taking part in a conversation is
/// something the correspondence states exactly — the authors of its messages — and the thread route already publishes
/// it from the mail itself. Asking a model for it would store a second, worse copy of a fact the mailbox holds, and a
/// screen showing the two side by side would have to decide which one is right.
/// </para>
/// </remarks>
/// <param name="ThreadId">The conversation this state is about, which is always the surviving thread of a merge.</param>
/// <param name="Coverage">How much of the conversation the derivation was shown.</param>
/// <param name="Entries">What was derived, in the order the producer named it, and empty where there was nothing to say.</param>
/// <param name="DerivedFrom">The shape of the conversation the derivation was made from.</param>
/// <param name="DerivedAt">When the derivation ran.</param>
public sealed record EmailThreadState(
    EmailThreadId ThreadId,
    ThreadStateCoverage Coverage,
    IReadOnlyList<ThreadStateEntry> Entries,
    ThreadStateRevision DerivedFrom,
    DateTimeOffset DerivedAt)
{
    /// <summary>Gets the statements of one aspect, in the order the producer named them.</summary>
    /// <param name="aspect">The aspect to read.</param>
    /// <returns>The statements, which is empty where the derivation produced none of that aspect.</returns>
    public IReadOnlyList<ThreadStateEntry> Of(ThreadStateAspect aspect) =>
        [.. this.Entries.Where(entry => entry.Aspect == aspect)];
}
