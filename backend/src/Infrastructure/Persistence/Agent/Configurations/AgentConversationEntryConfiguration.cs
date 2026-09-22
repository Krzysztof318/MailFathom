// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Agent.Configurations;

/// <summary>Declares what one conversation was written as, in the order it was written.</summary>
/// <remarks>
/// <para>
/// The conversation and the place together are the key, which is both the promise the record makes and the order its
/// main reader walks — everything of one conversation after a cursor. The cascade from the conversation is what makes
/// removing one a single statement against the conversations alone.
/// </para>
/// <para>
/// <strong>One further index, over the offers that have been answered</strong>, and it is partial because the rows
/// that answer an offer are a small minority of a conversation's entries. It is what the statement recording an answer
/// reads to find where the offer stands, which is the one decision here the database has to settle rather than a
/// caller.
/// </para>
/// <para>
/// <strong>The payload is <c>json</c> rather than <c>jsonb</c>, and the difference is the whole of the column.</strong>
/// Both refuse a value that is not a document, which is why the column is neither text nor a blob; only <c>json</c>
/// stores the one it was handed. <c>jsonb</c> parses the document and writes a normalized form back, ordering the keys
/// by length and then bytewise — and an entry is a polymorphic document whose type discriminator the serializer
/// refuses to read anywhere but first. Nothing queries inside the column, the entries being read back whole, so the
/// indexing <c>jsonb</c> buys is worth nothing against losing them on the way back.
/// </para>
/// </remarks>
internal sealed class AgentConversationEntryConfiguration : IEntityTypeConfiguration<AgentConversationEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AgentConversationEntryEntity> entity)
    {
        entity.ToTable(AgentConversationEntryEntity.TableName);
        entity.HasKey(written => new { written.ConversationId, written.Sequence })
            .HasName(PersistenceConstraintNames.AgentConversationEntryPrimaryKeyConstraintName);
        entity.Property(written => written.ConversationId)
            .HasColumnName(AgentConversationEntryEntity.ConversationIdColumnName);
        entity.Property(written => written.Sequence)
            .HasColumnName(AgentConversationEntryEntity.SequenceColumnName)
            .ValueGeneratedNever();
        entity.Property(written => written.Kind)
            .HasColumnName(AgentConversationEntryEntity.KindColumnName)
            .HasMaxLength(AgentConversationEntryEntity.KindLengthLimit)
            .IsRequired();
        entity.Property(written => written.Payload)
            .HasColumnName(AgentConversationEntryEntity.PayloadColumnName)
            .HasColumnType("json")
            .IsRequired();
        entity.Property(written => written.Visible)
            .HasColumnName(AgentConversationEntryEntity.VisibleColumnName);
        entity.Property(written => written.AnsweredProposalAt)
            .HasColumnName(AgentConversationEntryEntity.AnsweredProposalAtColumnName);
        entity.Property(written => written.ProposalState)
            .HasColumnName(AgentConversationEntryEntity.ProposalStateColumnName)
            .HasMaxLength(AgentConversationEntryEntity.ProposalStateLengthLimit);
        entity.Property(written => written.WrittenAt)
            .HasColumnName(AgentConversationEntryEntity.WrittenAtColumnName);

        entity.HasIndex(written => new { written.ConversationId, written.AnsweredProposalAt })
            .HasFilter($"\"{AgentConversationEntryEntity.AnsweredProposalAtColumnName}\" IS NOT NULL")
            .HasDatabaseName(PersistenceConstraintNames.AgentConversationAnsweredProposalIndexName);

        entity.HasOne<AgentConversationEntity>()
            .WithMany()
            .HasForeignKey(written => written.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
