// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.ThreadStates.Configurations;

/// <summary>Declares the statements one derivation produced about a conversation.</summary>
/// <remarks>
/// <para>
/// The statements cascade from the state rather than from the conversation, so replacing a state replaces its
/// statements in one statement. Keeping a superseded derivation's sentences beside the new ones would leave a record
/// nobody could read.
/// </para>
/// <para>
/// Nothing here is unique. A conversation carries any number of statements of one aspect, and two of them may
/// legitimately read alike — an agreement restated in a later message is still one agreement to a reader and two rows
/// to a producer that cited both. What keeps the record consistent is that a write replaces the whole set.
/// </para>
/// <para>
/// The aspect is stored as text for the reason every other stored outcome is: it stays readable in an ad-hoc query and
/// survives a later reordering of the enum. That matters most here, where the aspect is the column somebody reading the
/// table selects on.
/// </para>
/// </remarks>
internal sealed class EmailThreadStateEntryConfiguration : IEntityTypeConfiguration<EmailThreadStateEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailThreadStateEntryEntity> entity)
    {
        entity.ToTable("email_thread_state_entries");
        entity.HasKey(statement => statement.Id);
        entity.Property(statement => statement.Aspect).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(statement => statement.Text)
            .HasMaxLength(EmailThreadStateEntryEntity.MaximumTextLength)
            .IsRequired();
        entity.Property(statement => statement.OwedBy)
            .HasMaxLength(EmailThreadStateEntryEntity.MaximumOwedByLength);
        entity.Property(statement => statement.Sources).IsRequired();

        // The read is always "this conversation's statements, in the producer's order", so the index carries the order
        // as well as the identity and the read never sorts.
        entity.HasIndex(statement => new { statement.EmailThreadId, statement.Aspect, statement.Ordinal });

        entity.HasOne(statement => statement.ThreadState)
            .WithMany(state => state.Entries)
            .HasForeignKey(statement => statement.EmailThreadId)
            .HasConstraintName(PersistenceConstraintNames.EmailThreadStateEntryForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
