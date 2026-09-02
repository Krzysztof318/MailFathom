// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>How much stored mail content one owner holds, kept as a figure rather than recomputed.</summary>
/// <remarks>
/// <para>
/// One row per owner. It is the only figure in this schema that duplicates something derivable from the rows beneath
/// it, and the duplication is the point: the derivation is a sum over one person's whole mailbox, and the ceiling it
/// serves is consulted before every message. What is stored here is the payload bytes, which is what a re-derivation
/// would produce, and not what the table occupies on disk — that answer exists only for the table as a whole.
/// </para>
/// <para>
/// It is moved inside the transaction that stores or removes the payload, so a crash cannot leave a message stored and
/// uncounted or counted and unstored. Two runs storing at once add to each other rather than replacing a total each of
/// them read, which is what makes the write an increment issued as an upsert.
/// </para>
/// <para>
/// Unlike the spend ledger beside it, this cascades from the owner record: it describes mail that owner holds rather
/// than money this deployment spent, so erasing them takes it with everything else derived from their mail.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OwnerStoredContentEntity
{
    /// <summary>The table these rows live in, named here because the increment is a composed statement.</summary>
    internal const string TableName = "owner_stored_content";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string OwnerIdColumnName = "OwnerId";

    /// <summary>The counted column, named here for the same reason the table is.</summary>
    internal const string StoredContentByteCountColumnName = "StoredContentByteCount";

    /// <summary>Gets or sets the owner this figure belongs to.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets what that owner's stored payloads hold, in bytes.</summary>
    /// <remarks>
    /// Sixty-four bits because one mailbox reaches a billion bytes without difficulty, and signed because the column is
    /// an accumulator: a decrement that arrived before the increment it belongs to would otherwise wrap rather than be
    /// visible as the defect it is.
    /// </remarks>
    public long StoredContentByteCount { get; set; }
}
