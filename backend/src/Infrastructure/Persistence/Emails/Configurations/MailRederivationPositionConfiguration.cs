// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares how far a re-derivation pass has walked one scope of one account's mail.</summary>
/// <remarks>
/// Keyed by the scope an operator named rather than by a constant, which is what keeps two accounts' walks
/// independent. No foreign key onto the account: the row is a cursor over rows that are already keyed to one, and
/// requiring the account row would make the walk depend on a table it never reads.
/// </remarks>
internal sealed class MailRederivationPositionConfiguration : IEntityTypeConfiguration<MailRederivationPositionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRederivationPositionEntity> entity)
    {
        entity.ToTable("mail_rederivation_positions");
        entity.HasKey(position => new { position.MailboxAccountId, position.FolderAlias })
            .HasName(PersistenceConstraintNames.MailRederivationPositionPrimaryKeyConstraintName);
        entity.Property(position => position.MailboxAccountId).HasMaxLength(128);
        entity.Property(position => position.FolderAlias).HasMaxLength(128);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(position => position.ConcurrencyVersion).IsRowVersion();
    }
}
