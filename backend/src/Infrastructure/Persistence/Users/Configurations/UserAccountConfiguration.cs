// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the user row a mail account belongs to, and the document that user is configured by.</summary>
/// <remarks>
/// The table is named for the configuration route that owns its document rather than for the mail graph, because the
/// user record is a settings aggregate first: one row per user, holding the declarations of every mail account they
/// own. What the mail graph takes from it is the identifier alone, through the foreign key on <c>mailbox_accounts</c>.
/// </remarks>
internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserAccountEntity> entity)
    {
        entity.ToTable(UserAccountEntity.TableName);
        entity.HasKey(user => user.Id);

        // Provisioned rather than generated on insert: a user's identifier is decided by whoever provisions the
        // user — the migration that carries an upgraded deployment's existing accounts, and the administrative
        // surface after it — so the model never mints one behind a caller that meant to state it.
        entity.Property(user => user.Id).ValueGeneratedNever();

        // The label an administrator reads a list of users by, unique across the deployment and bounded, because a
        // column nothing bounds is one an administrative surface could be handed a page of text for.
        entity.Property(user => user.DisplayName)
            .HasMaxLength(UserAccountEntity.MaximumDisplayNameLength)
            .IsRequired();
        entity.HasIndex(user => user.DisplayName)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.UserAccountDisplayNameUniqueIndexName);

        // A document rather than a schema, for the reason the job payload is one: nothing here queries into it, and
        // what it holds is decided by the configuration layer that writes it.
        entity.Property(user => user.Document).HasColumnType("jsonb").IsRequired();

        // The version is the document's own rather than PostgreSQL's row version, because a writer has to be able to
        // state which version it read, be refused by number, and report the version it was refused against.
        entity.Property(user => user.Version).IsConcurrencyToken();
    }
}
