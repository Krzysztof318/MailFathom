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
    /// <remarks>
    /// The alias rather than the binding, because the row outlives the folder generation it was written under and what
    /// an operator needs from it is which of their folders to look in.
    /// </remarks>
    public required string FolderAlias { get; set; }

    /// <summary>Gets or sets when the command went out, which is how long an unanswered record has been standing.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Gets or sets when an operator declared the copy to be on the source, and <see langword="null" /> while nobody has.</summary>
    /// <remarks>
    /// The one column that tells an append whose outcome is unknown from one whose outcome an operator established.
    /// A settled row no longer holds the account in its phase and still keeps the message from being appended again.
    /// </remarks>
    public DateTimeOffset? SettledAt { get; set; }
}
