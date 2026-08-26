// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the re-derivation run one scope of one account is walked by, and what it found.</summary>
/// <remarks>
/// Keyed by the scope an operator named, exactly as the cursor beside it is, so a whole-account run and a run over one
/// folder of the same account are two rows. The run outlives its own ending, which is what lets a request be answered
/// with "the previous run finished and here is what it found" rather than with silence.
/// </remarks>
internal sealed class MailRederivationRunConfiguration : IEntityTypeConfiguration<MailRederivationRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRederivationRunEntity> entity)
    {
        entity.ToTable("mail_rederivation_runs");
        // The owner leads the key, because the account identifier after it names one mailbox within that owner and a
        // different one within the next: two owners re-deriving the same folder of their own `work` are two runs.
        entity.HasKey(run => new { run.OwnerId, run.MailboxAccountId, run.FolderAlias })
            .HasName(PersistenceConstraintNames.MailRederivationRunPrimaryKeyConstraintName);
        entity.Property(run => run.MailboxAccountId).HasMaxLength(128);
        entity.Property(run => run.FolderAlias).HasMaxLength(128);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
    }
}
