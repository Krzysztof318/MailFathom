// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailboxAccountEntity
{
    public required string Id { get; set; }

    /// <summary>The owner this mailbox belongs to, which leads the key and the axis every read of its mail is narrowed by.</summary>
    /// <remarks>
    /// A relational column rather than a value inside the owner's document, so ownership, lookup, uniqueness, and
    /// cascade erasure are decided by the database rather than by a predicate over JSON. It is half of what identifies
    /// the account: <see cref="Id" /> alone names one mailbox within this owner and a different one within another.
    /// </remarks>
    public required Guid OwnerId { get; set; }

    public ICollection<MailFolderEntity> MailFolders { get; } = [];
}
