// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Persistence.Entities;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailboxAccountEntity
{
    /// <summary>Gets or sets the account's generated identifier, which is the whole of what identifies the mailbox.</summary>
    /// <remarks>
    /// Unique across the deployment, so this row is the mailbox rather than one reader's copy of it: the mail
    /// beneath it is one copy whichever users are assigned the account, and whose mail those rows are follows from
    /// the assignments rather than from a column here.
    /// </remarks>
    public required string Id { get; set; }

    /// <summary>Gets or sets which copy of the mailbox is the truth, which is <see cref="MailAccountCustodyPhase.Mirrored" /> for every account nothing has switched.</summary>
    public MailAccountCustodyPhase CustodyPhase { get; set; }

    /// <summary>Gets or sets the custody an administrator last asked for, which is <see cref="MailAccountCustody.MirrorSource" /> for every account nothing has switched.</summary>
    /// <remarks>
    /// Separate from <see cref="CustodyPhase" /> because a switch in either direction is a period of background work:
    /// this column is what somebody asked for and that one is how far the work of granting it has got. Only the
    /// administrative command writes this one, and only the account's own supervision writes that one.
    /// </remarks>
    public MailAccountCustody RequestedCustody { get; set; }

    /// <summary>Gets or sets how far the restore has walked the account's mail writing its stored state onto the source, and <see langword="null" /> where it has walked none.</summary>
    /// <remarks>
    /// A position rather than a stamp on every message, because the walk is bounded per run and has to resume where it
    /// stopped: the alternative is a nullable column on the mailbox's largest table plus the filtered index that makes
    /// it readable, which every mirrored deployment would carry for a mode nobody turned on. The order walked is the
    /// message identity, which is total and which no later write disturbs.
    /// </remarks>
    public Guid? RestoreStatePosition { get; set; }

    /// <summary>Gets or sets which restore of this account is the current one, which every entry into <see cref="MailAccountCustodyPhase.Restoring" /> advances and which is zero for an account that has never restored.</summary>
    /// <remarks>
    /// It names one restore so that the mutation records that restore opens are its own. The identity of such a record
    /// is the occurrence, the requester, and the mutation together, and a completed record is never deleted, so a
    /// second restore naming what the first named would read the first restore's finished work as its own and carry
    /// none of the second hold's state. A count rather than a stamp, because what it has to be is different from every
    /// earlier value for this account, which a counter the account's own conditional phase write advances already is.
    /// </remarks>
    public int RestoreGeneration { get; set; }

    /// <summary>Gets or sets the revision of the account's local folder hierarchy, which every write to that hierarchy advances.</summary>
    /// <remarks>
    /// A concurrency token on the account rather than on each folder, because the rules a folder edit is decided by —
    /// no folder beneath itself, no two siblings of one name — are about the whole hierarchy, and two edits that each
    /// hold for the picture they read can together break one. Advancing the one row both read is what makes the second
    /// commit conflict and decide again.
    /// </remarks>
    public int LocalMailFoldersRevision { get; set; }

    public ICollection<MailFolderEntity> MailFolders { get; } = [];
}
