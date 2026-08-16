// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One conversation the stored mail of a single account was assembled into.</summary>
/// <remarks>
/// <para>
/// A table rather than a column on the email, because a thread is a relation: two messages belong together when an
/// identifier one of them carries names the other, and the message that arrives third can prove two threads already
/// stored were always one. A derived column would have to be recomputed across rows on every arrival to say that.
/// </para>
/// <para>
/// The row carries almost nothing of its own, which is deliberate. Everything a reader asks of a thread — which messages
/// are in it, what order they are in, which one answers which — is derived from the emails that reference it, so a
/// column here would be a second copy of an answer the emails already give. What the row is for is the identity: a
/// stable name a tool can publish and a caller can come back with.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailThreadEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the account whose mail this thread assembles.</summary>
    /// <remarks>
    /// A thread spans the account rather than the folder, because the reply this deployment sends lands in <c>Sent</c>
    /// while the message it answers sits in <c>INBOX</c>. It never spans two accounts: the same exchange held by two
    /// mailboxes is two conversations, each owned by the account that holds it.
    /// </remarks>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets when this deployment first assembled the thread.</summary>
    /// <remarks>
    /// What decides a merge. When a message proves two threads were always one, the earlier one survives, and the
    /// comparison is made on this value with the identity settling a tie — rather than on the identifier alone, whose
    /// ordering is a property of how a UUID happens to sort.
    /// </remarks>
    public DateTimeOffset AssembledAt { get; set; }

    /// <summary>
    /// Gets or sets the thread this one was merged into, or <see langword="null" /> while it is still its own.
    /// </summary>
    /// <remarks>
    /// The row outlives the merge for one reason: a tool may already have published this identifier, and a caller coming
    /// back with it must reach the conversation it named rather than be told no such thread exists. Nothing else reads
    /// it — every email of a merged thread is repointed at the survivor in the same transaction, so a membership query
    /// never has to follow this.
    /// </remarks>
    public Guid? MergedIntoEmailThreadId { get; set; }
}
