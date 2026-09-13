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
    public required string Id { get; set; }

    /// <summary>The user this mailbox belongs to, which leads the key and the axis every read of its mail is narrowed by.</summary>
    /// <remarks>
    /// A relational column rather than a value inside the user's document, so ownership, lookup, uniqueness, and
    /// cascade erasure are decided by the database rather than by a predicate over JSON. It is half of what identifies
    /// the account: <see cref="Id" /> alone names one mailbox within this user and a different one within another.
    /// </remarks>
    public required Guid UserId { get; set; }

    /// <summary>Gets or sets which copy of the mailbox is the truth, which is <see cref="MailAccountCustodyPhase.Mirrored" /> for every account nothing has switched.</summary>
    public MailAccountCustodyPhase CustodyPhase { get; set; }

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
