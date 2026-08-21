// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the derived search document and the lexical index built over it.</summary>
/// <remarks>
/// <para>
/// The search vector is a stored generated column rather than a column MailFathom writes, so it cannot drift from the
/// text beside it: no code path, migration, or ad-hoc update can leave a row whose vector describes text the row no
/// longer holds. PostgreSQL requires such an expression to be immutable, which is why the text search configuration
/// is named explicitly and why the participant addresses are a text column here rather than the arrays on the
/// stored email — the array-to-text functions are only stable.
/// </para>
/// <para>
/// GIN is the index method a containment-style <c>tsvector</c> lookup needs; a B-tree over the column would serve
/// no query that search issues.
/// </para>
/// </remarks>
internal sealed class EmailSearchDocumentConfiguration : IEntityTypeConfiguration<EmailSearchDocumentEntity>
{
    private readonly PostgresTextSearchConfiguration textSearchConfiguration;

    /// <summary>Initializes the mapping of the derived search document.</summary>
    /// <param name="textSearchConfiguration">The validated text search configuration the lexical index is built with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="textSearchConfiguration" /> is <see langword="null" />.</exception>
    internal EmailSearchDocumentConfiguration(PostgresTextSearchConfiguration textSearchConfiguration)
    {
        ArgumentNullException.ThrowIfNull(textSearchConfiguration);

        this.textSearchConfiguration = textSearchConfiguration;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailSearchDocumentEntity> entity)
    {
        entity.ToTable("email_search_documents");
        entity.HasKey(document => document.StoredEmailId);
        entity.Property(document => document.StoredEmailId).ValueGeneratedNever();
        entity.Property(document => document.SubjectText)
            .HasMaxLength(EmailSearchDocumentEntity.MaximumIndexedSubjectLength);

        // Stored as text for the reason the content-availability reason is: the source stays readable in an audit
        // query and survives any later reordering of the enum.
        entity.Property(document => document.TextSource).HasConversion<string>().HasMaxLength(64).IsRequired();

        // Carried without an index of its own. Both readers of the column ask which rows are *not* stamped with the
        // current configuration, and a B-tree operator class holds no inequality operator, so nothing could use one;
        // the staleness count and the rebuilding walk scan, which is what a once-per-start figure and a walk that
        // reads whole rows anyway can afford.
        entity.Property(document => document.SensitiveContentStamp)
            .HasMaxLength(SensitiveContentDerivationStamp.Length)
            .IsFixedLength();

        entity.HasOne(document => document.StoredEmail)
            .WithOne(email => email.SearchDocument)
            .HasForeignKey<EmailSearchDocumentEntity>(document => document.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasGeneratedTsVectorColumn(
                document => document.SearchVector,
                this.textSearchConfiguration.Value,
                document => new { document.SubjectText, document.ParticipantAddresses, document.BodyText })
            .HasIndex(document => document.SearchVector)
            .HasDatabaseName(PersistenceConstraintNames.EmailSearchDocumentVectorIndexName)
            .HasMethod("GIN");
    }
}
