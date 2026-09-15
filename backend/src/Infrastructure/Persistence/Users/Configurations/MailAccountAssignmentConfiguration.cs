// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the many-to-many relation between the users a deployment serves and the mail accounts it holds.</summary>
internal sealed class MailAccountAssignmentConfiguration : IEntityTypeConfiguration<MailAccountAssignmentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAccountAssignmentEntity> entity)
    {
        entity.ToTable(MailAccountAssignmentEntity.TableName);
        entity.HasKey(assignment => new { assignment.UserId, assignment.MailAccountId });

        // Cascading from both sides: erasing a user ends what they were assigned, and erasing an account ends every
        // assignment to it. What becomes of an account left with nobody is the erasure's decision rather than a cascade's.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.UserId)
            .HasConstraintName(PersistenceConstraintNames.MailAccountAssignmentUserForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<MailAccountRecordEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.MailAccountId)
            .HasConstraintName(PersistenceConstraintNames.MailAccountAssignmentAccountForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // Not unique: an account is assigned to as many users as an administrator assigns it to, and the mail graph
        // is keyed by the account alone, so one mailbox is one copy of the mail however many people read it. What the
        // index is for is the reverse read — which users an account serves — which a fan-out and an erasure both ask.
        entity.HasIndex(assignment => assignment.MailAccountId)
            .HasDatabaseName(PersistenceConstraintNames.MailAccountAssignmentAccountIndexName);
    }
}
