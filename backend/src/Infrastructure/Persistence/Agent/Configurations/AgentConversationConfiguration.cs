// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Agent.Configurations;

/// <summary>Declares the conversations the deployment's people are holding with its agent.</summary>
/// <remarks>
/// <para>
/// The identifier is the key, because a client reads a conversation by the identifier it was handed and every other
/// statement reaches one the same way. The column names are the entity's own constants because every statement is
/// composed, so the statements and this mapping name the same things by construction.
/// </para>
/// <para>
/// <strong>One index beside the key, over the person and the instant their history is ordered by.</strong> It serves
/// the only query this table has beside reading one conversation — a person's history, most recently active first —
/// and because the person leads it, it is also the index the foreign key and an erasure of that person reach the rows
/// by. A second index on the person alone would be that one's prefix and would earn nothing.
/// </para>
/// </remarks>
internal sealed class AgentConversationConfiguration : IEntityTypeConfiguration<AgentConversationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AgentConversationEntity> entity)
    {
        entity.ToTable(AgentConversationEntity.TableName);
        entity.HasKey(conversation => conversation.Id);
        entity.Property(conversation => conversation.Id)
            .HasColumnName(AgentConversationEntity.IdColumnName)
            .ValueGeneratedNever();
        entity.Property(conversation => conversation.UserId)
            .HasColumnName(AgentConversationEntity.UserIdColumnName);
        entity.Property(conversation => conversation.Title)
            .HasColumnName(AgentConversationEntity.TitleColumnName)
            .HasMaxLength(AgentConversationEntity.TitleLengthLimit);
        entity.Property(conversation => conversation.StartedAt)
            .HasColumnName(AgentConversationEntity.StartedAtColumnName);
        entity.Property(conversation => conversation.LastActivityAt)
            .HasColumnName(AgentConversationEntity.LastActivityAtColumnName);
        entity.Property(conversation => conversation.Sequence)
            .HasColumnName(AgentConversationEntity.SequenceColumnName)
            .ValueGeneratedNever();
        entity.Property(conversation => conversation.ComposingMessageId)
            .HasColumnName(AgentConversationEntity.ComposingMessageIdColumnName);

        entity.HasIndex(conversation => new { conversation.UserId, conversation.LastActivityAt })
            .IsDescending(false, true)
            .HasDatabaseName(PersistenceConstraintNames.AgentConversationHistoryIndexName);

        // Cascade rather than a statement in the erasure walk, for the reason the Discover run's row carries one: a
        // conversation is one person's questions about their own mail and names nobody else, so it goes when they do
        // without an erasure having to know this table exists — and what goes with it is everything the conversation
        // held, through the entries' own cascade from here.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(conversation => conversation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
