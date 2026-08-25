// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Settings.Configurations;

/// <summary>Declares the one row the deployment's persisted configuration lives in.</summary>
/// <remarks>
/// The table is named for the configuration layer that owns it, beside <c>settings_accounts</c>, which holds the same
/// shape per owner. The singleton is expressed in the schema rather than only in the code that reads it: a second row
/// would make "the effective configuration" a question about ordering, and no reader would report which row it lost.
/// </remarks>
internal sealed class RootSettingsConfiguration : IEntityTypeConfiguration<RootSettingsEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RootSettingsEntity> entity)
    {
        entity.ToTable(
            "settings_root",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.RootSettingsSingletonCheckConstraintName,
                $"\"Id\" = {RootSettingsEntity.SingletonId}"));
        entity.HasKey(settings => settings.Id);

        // Stated rather than generated, because the key is the constant the check constraint names: a database that
        // minted one would produce the second row the constraint exists to refuse.
        entity.Property(settings => settings.Id).ValueGeneratedNever();

        // A document rather than a schema, because what it holds is whatever configuration keys a deployment persisted
        // and the shape of those is decided by the sections they belong to.
        entity.Property(settings => settings.Document).HasColumnType("jsonb").IsRequired();

        // The version is the document's own rather than PostgreSQL's row version, because a writer has to be able to
        // state which version it read, be refused by number, and report the version it was refused against.
        entity.Property(settings => settings.Version).IsConcurrencyToken();
    }
}
