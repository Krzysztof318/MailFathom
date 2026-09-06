// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares what one attachment yielded and the lexical index built over the ones somebody wrote.</summary>
/// <remarks>
/// <para>
/// The search vector is a stored generated column exactly as the message's own is, and for the same reason: nothing —
/// no code path, no migration, no ad-hoc update — can leave a row whose vector describes words the row no longer holds.
/// What is new here is the condition in front of it. A row holding a model's description of a picture generates no
/// vector at all, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// enforced by the database rather than remembered by a writer: a lexical search must never return a picture because a
/// model guessed a word into it.
/// </para>
/// <para>
/// The expression is written out rather than composed by <c>HasGeneratedTsVectorColumn</c>, which has no place to put
/// the condition. Every function in it is immutable, which is what PostgreSQL requires of a generated column: the text
/// search configuration is named explicitly rather than taken from the session, and the two nullable text columns are
/// coalesced rather than concatenated, since a null anywhere in the expression would produce a null vector for a
/// document whose file name was merely absent.
/// </para>
/// </remarks>
internal sealed class EmailAttachmentTextConfiguration : IEntityTypeConfiguration<EmailAttachmentTextEntity>
{
    private readonly PostgresTextSearchConfiguration textSearchConfiguration;

    /// <summary>Initializes the mapping of what an attachment yielded.</summary>
    /// <param name="textSearchConfiguration">The validated text search configuration the lexical index is built with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="textSearchConfiguration" /> is <see langword="null" />.</exception>
    internal EmailAttachmentTextConfiguration(PostgresTextSearchConfiguration textSearchConfiguration)
    {
        ArgumentNullException.ThrowIfNull(textSearchConfiguration);

        this.textSearchConfiguration = textSearchConfiguration;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailAttachmentTextEntity> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.ToTable("email_attachment_texts");

        // The message and the walk position together, because that pair is the attachment's identity: nothing else
        // about a part is stable, and a surrogate key would let one attachment be read twice into two rows.
        entity.HasKey(text => new { text.StoredEmailId, text.AttachmentPosition })
            .HasName(PersistenceConstraintNames.EmailAttachmentTextPrimaryKeyName);

        entity.Property(text => text.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        entity.Property(text => text.DeclaredMediaType)
            .HasMaxLength(EmailAttachmentTextEntity.MaximumMediaTypeLength)
            .IsRequired();

        entity.Property(text => text.FileName).HasMaxLength(EmailAttachmentTextEntity.MaximumFileNameLength);

        entity.Property(text => text.Outcome)
            .HasMaxLength(EmailAttachmentTextEntity.MaximumOutcomeLength)
            .IsRequired();

        entity.Property(text => text.Segments).HasColumnType("jsonb");

        entity.Property(text => text.SensitiveContentStamp)
            .HasMaxLength(SensitiveContentDerivationStamp.Length)
            .IsFixedLength();

        entity.HasOne(text => text.StoredEmail)
            .WithMany(email => email.AttachmentTexts)
            .HasForeignKey(text => text.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.Property(text => text.SearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(this.SearchVectorExpression(), stored: true);

        entity.HasIndex(text => text.SearchVector)
            .HasDatabaseName(PersistenceConstraintNames.EmailAttachmentTextVectorIndexName)
            .HasMethod("GIN");
    }

    /// <summary>Composes the generated column that indexes a document's words and no description's.</summary>
    /// <remarks>
    /// The kind is compared against the name the conversion above stores, so the two are one decision: changing how the
    /// kind is persisted without changing this string would silently index nothing. The configuration name is the
    /// deployment's own validated setting, which is what makes it safe to write into the expression — nothing here is
    /// composed from a request.
    /// </remarks>
    private string SearchVectorExpression() => string.Format(
        CultureInfo.InvariantCulture,
        """CASE WHEN "Kind" = '{0}' THEN to_tsvector('{1}'::regconfig, coalesce("FileName", '') || ' ' || coalesce("Text", '')) END""",
        AttachmentTextKind.Document,
        this.textSearchConfiguration.Value);
}
