// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the one move of stored content a deployment may have, and how far it has come.</summary>
/// <remarks>
/// The check constraint is what makes "one move per deployment" a property of the schema rather than of the one store
/// that writes the row. Nothing else in this system may hold a second move under a name of its own, which is the
/// invariant every reader here assumes and none of them re-checks.
/// </remarks>
internal sealed class ContentMoveRunConfiguration : IEntityTypeConfiguration<ContentMoveRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentMoveRunEntity> entity)
    {
        entity.ToTable(
            "content_move_runs",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.ContentMoveRunSingletonCheckConstraintName,
                $"\"Name\" = '{ContentMoveRunEntity.DeploymentName}'"));
        entity.HasKey(run => run.Name)
            .HasName(PersistenceConstraintNames.ContentMoveRunPrimaryKeyConstraintName);
        entity.Property(run => run.Name).HasMaxLength(ContentMoveRunEntity.MaximumNameLength);

        // Written as text for the reason the content backend is: a state read out of a database by an operator answering
        // a question at three in the morning should say what it is rather than which ordinal it was declared at.
        entity.Property(run => run.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(run => run.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
    }
}
