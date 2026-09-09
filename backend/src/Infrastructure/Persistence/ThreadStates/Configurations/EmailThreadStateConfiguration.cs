// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.ThreadStates.Configurations;

/// <summary>Declares where one conversation stands, as a derivation left it.</summary>
/// <remarks>
/// The table cascades from the conversation, which is what keeps derived data inside whatever erasure and retention
/// reach the mail it describes: nothing has to remember to delete a state, and nothing can leave one behind describing
/// a conversation that is gone.
/// </remarks>
internal sealed class EmailThreadStateConfiguration : IEntityTypeConfiguration<EmailThreadStateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailThreadStateEntity> entity)
    {
        entity.ToTable("email_thread_states");
        entity.HasKey(state => state.EmailThreadId)
            .HasName(PersistenceConstraintNames.EmailThreadStatePrimaryKeyConstraintName);
        entity.Property(state => state.EmailThreadId).ValueGeneratedNever();

        // Text for the reason every other stored outcome is: it stays readable in an ad-hoc query and survives a later
        // reordering of the enum.
        entity.Property(state => state.Coverage).HasConversion<string>().HasMaxLength(64).IsRequired();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(state => state.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(state => state.EmailThread)
            .WithOne()
            .HasForeignKey<EmailThreadStateEntity>(state => state.EmailThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
