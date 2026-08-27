// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Owners.Configurations;

/// <summary>Declares the owner row a mail account belongs to, and the document that owner is configured by.</summary>
/// <remarks>
/// The table is named for the configuration route that owns its document rather than for the mail graph, because the
/// owner record is a settings aggregate first: one row per owner, holding the declarations of every mail account they
/// own. What the mail graph takes from it is the identifier alone, through the foreign key on <c>mailbox_accounts</c>.
/// </remarks>
internal sealed class OwnerAccountConfiguration : IEntityTypeConfiguration<OwnerAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OwnerAccountEntity> entity)
    {
        entity.ToTable("settings_accounts");
        entity.HasKey(owner => owner.Id);

        // Provisioned rather than generated on insert: an owner's identifier is decided by whoever provisions the
        // owner — the migration that carries an upgraded deployment's existing accounts, and the administrative
        // surface after it — so the model never mints one behind a caller that meant to state it.
        entity.Property(owner => owner.Id).ValueGeneratedNever();

        // The label an administrator reads a list of owners by, unique across the deployment and bounded, because a
        // column nothing bounds is one an administrative surface could be handed a page of text for.
        entity.Property(owner => owner.DisplayName)
            .HasMaxLength(OwnerAccountEntity.MaximumDisplayNameLength)
            .IsRequired();
        entity.HasIndex(owner => owner.DisplayName)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.OwnerAccountDisplayNameUniqueIndexName);

        // A document rather than a schema, for the reason the job payload is one: nothing here queries into it, and
        // what it holds is decided by the configuration layer that writes it.
        entity.Property(owner => owner.Document).HasColumnType("jsonb").IsRequired();

        // The version is the document's own rather than PostgreSQL's row version, because a writer has to be able to
        // state which version it read, be refused by number, and report the version it was refused against.
        entity.Property(owner => owner.Version).IsConcurrencyToken();
    }
}
