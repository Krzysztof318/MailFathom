// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers the two rules about attachment passages that PostgreSQL enforces rather than a writer.</summary>
/// <remarks>
/// The model is built from the design-time factory, which reaches no database: what is asserted is the mapping the
/// migration is generated from. Whether PostgreSQL accepts the resulting data definition is the migration's own claim,
/// and whether a row behaves as the mapping says is the integration suite's.
/// </remarks>
public sealed class EmailAttachmentTextMappingTests
{
    /// <summary>
    /// ADR 0030's exclusion, decided in the database rather than in a writer that could forget it. The column produces
    /// a vector for a document's own words and nothing at all for a description, so a word a model chose can never be
    /// matched as though somebody had written it — whatever a later writer does with the row.
    /// </summary>
    [Fact]
    public void SearchVector_TheGeneratedColumn_IsProducedForADocumentAndForNothingElse()
    {
        // Act
        var expression = Property(nameof(EmailAttachmentTextEntity.SearchVector)).GetComputedColumnSql();

        // Assert
        Assert.Contains("CASE WHEN \"Kind\" = 'Document'", expression, StringComparison.Ordinal);
        Assert.Contains("to_tsvector", expression, StringComparison.Ordinal);
        Assert.True(Property(nameof(EmailAttachmentTextEntity.SearchVector)).GetIsStored());
    }

    /// <summary>
    /// A lexical match has to name the file as well as the words, because the file name is frequently the only thing a
    /// mailbox owner remembers about a contract.
    /// </summary>
    [Fact]
    public void SearchVector_TheGeneratedColumn_ReadsTheFileNameBesideTheText()
    {
        // Act
        var expression = Property(nameof(EmailAttachmentTextEntity.SearchVector)).GetComputedColumnSql();

        // Assert
        Assert.Contains("\"FileName\"", expression, StringComparison.Ordinal);
        Assert.Contains("\"Text\"", expression, StringComparison.Ordinal);
    }

    /// <summary>The words a lexical query reads are unreachable without the index that answers it.</summary>
    [Fact]
    public void SearchVector_TheGeneratedColumn_IsIndexed()
    {
        // Act
        var indexes = EntityType(typeof(EmailAttachmentTextEntity)).GetIndexes();

        // Assert
        Assert.Contains(
            indexes,
            index => index.GetDatabaseName() == PersistenceConstraintNames.EmailAttachmentTextVectorIndexName);
    }

    /// <summary>Deleting a message takes every reading of its attachments with it, which erasure depends on.</summary>
    [Fact]
    public void EmailAttachmentTexts_TheMessageTheyBelongTo_DeletesThemWithIt()
    {
        // Act
        var foreignKey = Assert.Single(EntityType(typeof(EmailAttachmentTextEntity)).GetForeignKeys());

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>
    /// PostgreSQL treats two nulls as distinct, so one index over the message, the attachment, and the ordinal would
    /// enforce nothing at all for a body passage — every one of which carries no attachment. Two filtered indexes are
    /// what keeps both kinds unique, and losing either would let a re-cut write a passage twice.
    /// </summary>
    [Fact]
    public void EmailChunks_TheOrdinalOfAPassage_IsUniqueWithinItsOwnKindOfText()
    {
        // Act
        var indexes = EntityType(typeof(EmailChunkEntity))
            .GetIndexes()
            .ToDictionary(index => index.GetDatabaseName()!);

        // Assert
        var body = indexes[PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName];
        var attachment = indexes[PersistenceConstraintNames.EmailChunkAttachmentOrdinalUniqueIndexName];

        Assert.True(body.IsUnique);
        Assert.True(attachment.IsUnique);
        Assert.Equal("\"AttachmentPosition\" IS NULL", body.GetFilter());
        Assert.Equal("\"AttachmentPosition\" IS NOT NULL", attachment.GetFilter());
        Assert.Contains(
            nameof(EmailChunkEntity.AttachmentPosition),
            attachment.Properties.Select(property => property.Name));
    }

    private static IProperty Property(string name) =>
        EntityType(typeof(EmailAttachmentTextEntity)).FindProperty(name)
        ?? throw new InvalidOperationException($"The model holds no {name} property.");

    private static IEntityType EntityType(Type clrType)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"The model holds no {clrType.Name}.");
    }
}
