// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class MailboxSourceRemovalEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the account the folder belongs to, copied from that folder because the query that reads a held
    /// account's outstanding removals leads with it and an index cannot span a join.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    public long MailFolderId { get; set; }

    /// <summary>Gets or sets the binding the occurrence belongs to, which a read loads and a write need not.</summary>
    /// <remarks>
    /// Optional on the entity rather than required, because the erasure that writes this record works from stored-mail
    /// rows it read without the binding and has the foreign key in hand already. The column itself is not nullable.
    /// </remarks>
    public MailFolderEntity? MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    /// <summary>Gets or sets when the erasure that left this record was committed, which is the order the drain works in.</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
