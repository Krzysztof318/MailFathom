// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One <c>APPEND</c> the restore issued to put a held message back onto its source.</summary>
/// <remarks>
/// A row exists from before the command goes out until the server has named where the copy went, which is what stops a
/// second copy: a process that died in between left a row saying the copy may be there, and nothing appends again on
/// the strength of it. A row an operator settled as appended stays for the life of the message, because the message has
/// no occurrence to record and the row is the only thing saying its copy is already on the source.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxRestoreAppendEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the account whose mailbox the copy goes back into, copied from the message because the query an
    /// operator's reading and every pass issue leads with it and an index cannot span a join.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the message the append carried, which a read loads and a write need not.</summary>
    public StoredEmailEntity? StoredEmail { get; set; }

    /// <summary>Gets or sets MailFathom's own name for the folder the copy was appended into.</summary>
    /// <remarks>What an operator needs from the row is which of their folders to look in, which the alias is.</remarks>
    public required string FolderAlias { get; set; }

    /// <summary>Gets or sets which binding of that alias the command went out against.</summary>
    /// <remarks>
    /// Recorded because the occurrence written from a placement names the binding as well as the identity inside it,
    /// and an alias is repointed by a rewritten mapping or by discovery matching a different advertised folder. Two
    /// unrelated remote folders may advertise the same UIDVALIDITY, so a placement carried under a generation that has
    /// moved would write an occurrence naming the new binding with the old folder's identity. The row outlives the
    /// binding either way; what the column decides is whether a later pass may carry the placement or has to leave the
    /// row for an operator.
    /// </remarks>
    public int FolderGeneration { get; set; }

    /// <summary>Gets or sets when the command went out, which is how long an unanswered record has been standing.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY the source named for the copy, and <see langword="null" /> until it answers.</summary>
    /// <remarks>
    /// Written on its own, between a fully answered <c>APPEND</c> and the occurrence that answer justifies, because
    /// those two cannot commit together: one comes from a mail server and the other is written here. A row carrying
    /// it is an append whose outcome is completely known and whose occurrence the next pass writes, which is a
    /// different thing from a row carrying neither — that one is an outcome nobody knows.
    /// </remarks>
    public uint? AppendedUidValidity { get; set; }

    /// <summary>Gets or sets the UID the source named inside that folder, absent exactly where <see cref="AppendedUidValidity" /> is.</summary>
    public uint? AppendedUid { get; set; }

    /// <summary>Gets or sets when the copy's fate stopped being open, and <see langword="null" /> while it still is.</summary>
    /// <remarks>
    /// The one column that tells an append still holding the account in its phase from one that has stopped. An
    /// operator sets it by declaring the copy to be on the source; the restore sets it itself for a message whose
    /// stored payload cannot be served, which has no bytes to append and never will. Either way the row stays for the
    /// life of the message, because the message has no occurrence and the row is the only thing saying its copy may
    /// already be on the source.
    /// </remarks>
    public DateTimeOffset? SettledAt { get; set; }
}
