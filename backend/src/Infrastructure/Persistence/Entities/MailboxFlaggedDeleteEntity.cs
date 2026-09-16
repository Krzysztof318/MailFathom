// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class MailboxFlaggedDeleteEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the account the folder belongs to, copied from that folder so the record answers which mailbox it
    /// is about without a join.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    public long MailFolderId { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    /// <summary>Gets or sets the local email the delete kept, and <see langword="null" /> where it erased the local copy.</summary>
    /// <remarks>
    /// A foreign key that cascades, so an email erased afterwards — by retention, by a person's erasure, or with its
    /// account — takes this record with it, and a flag removed on the server later brings back nothing somebody asked
    /// MailFathom to forget.
    /// </remarks>
    public Guid? StoredEmailId { get; set; }

    /// <summary>Gets or sets when synchronization last saw the occurrence still flagged, which is the order a run asks about them in.</summary>
    public DateTimeOffset LastObservedAt { get; set; }
}
