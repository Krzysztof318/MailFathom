// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Spam.Configurations;

/// <summary>Declares the outstanding whole-mailbox classification run of one account.</summary>
/// <remarks>
/// One row per account, which is what makes "one outstanding whole-mailbox classification run" a property of the key
/// rather than of a check. The scope is a text array because it is read back whole and never filtered on: the run
/// states which folders it walks, and nothing asks the database which runs walk one folder.
/// </remarks>
internal sealed class SpamClassificationRunConfiguration : IEntityTypeConfiguration<SpamClassificationRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpamClassificationRunEntity> entity)
    {
        entity.ToTable("spam_classification_runs");
        // One row per account, and an account is its owner and its identifier together: the key leads with the owner
        // so that one person's outstanding run does not stand in for another person's mailbox of the same name.
        entity.HasKey(run => new { run.OwnerId, run.MailboxAccountId })
            .HasName(PersistenceConstraintNames.SpamClassificationRunPrimaryKeyConstraintName);
        entity.Property(run => run.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
        entity.Property(run => run.FolderAliases).IsRequired();
        entity.Property(run => run.Posture).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(run => run.Profile)
            .HasMaxLength(SpamClassificationRunEntity.ProfileLength)
            .IsFixedLength();
        entity.Property(run => run.Ending).HasConversion<string>().HasMaxLength(64);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
    }
}
