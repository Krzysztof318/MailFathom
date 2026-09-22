// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Agent;

/// <summary>
/// Holds the two conversation rows to the obligations that are the schema's rather than any code path's: that a
/// conversation leaves with the person whose it is and takes everything said in it, that the place an entry holds
/// cannot be said twice, that the payload is stored as it was written, and that the two indexes are the ones the
/// store's statements read.
/// </summary>
/// <remarks>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. That a running PostgreSQL then performs the cascade, refuses the second
/// insert, and uses these indexes is an integration question and is measured there.
/// </remarks>
public sealed class AgentConversationModelTests
{
    /// <summary>A conversation is one person's questions about their own mail, so erasing them erases it.</summary>
    [Fact]
    public void Model_ThePersonWhoseConversationItIs_TakesTheConversationWithThemWhenTheyAreErased()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ConversationType(context)
            .GetForeignKeys()
            .Single(candidate => candidate.Properties
                .Any(property => property.Name == nameof(AgentConversationEntity.UserId)));

        // Assert
        Assert.Equal(nameof(UserAccountEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Everything said in a conversation goes with it, which is the whole of removing one.</summary>
    [Fact]
    public void Model_TheConversationAnEntryBelongsTo_TakesTheEntryWithItWhenItIsRemoved()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntryType(context)
            .GetForeignKeys()
            .Single(candidate => candidate.Properties
                .Any(property => property.Name == nameof(AgentConversationEntryEntity.ConversationId)));

        // Assert
        Assert.Equal(nameof(AgentConversationEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>A place starts at one and never skips, and the key is what makes two entries under one number impossible.</summary>
    [Fact]
    public void Model_TheKeyAnEntryIsWrittenUnder_IsTheConversationAndThePlaceTogether()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntryType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(
            PersistenceConstraintNames.AgentConversationEntryPrimaryKeyConstraintName,
            key.GetName());
        Assert.Equal(
            [
                nameof(AgentConversationEntryEntity.ConversationId),
                nameof(AgentConversationEntryEntity.Sequence),
            ],
            key.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// An entry is a polymorphic document whose discriminator the serializer refuses to read anywhere but first, and
    /// <c>jsonb</c> reorders keys by length and then bytewise — so a column that normalized what it stored would hand
    /// the reader a document it cannot identify.
    /// </summary>
    [Fact]
    public void Model_ThePayloadColumn_StoresTheDocumentItWasHandedRatherThanANormalizedForm()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var payload = EntryType(context).FindProperty(nameof(AgentConversationEntryEntity.Payload));

        // Assert
        Assert.NotNull(payload);
        Assert.Equal("json", payload.GetColumnType());
        Assert.False(payload.IsNullable);
    }

    /// <summary>A conversation exists before the agent has composed a name for it.</summary>
    [Fact]
    public void Model_WhatAConversationIsCalled_IsOptionalAndBoundedByTheContract()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var title = ConversationType(context).FindProperty(nameof(AgentConversationEntity.Title));

        // Assert
        Assert.NotNull(title);
        Assert.True(title.IsNullable);
        Assert.Equal(AgentConversationEntity.TitleLengthLimit, title.GetMaxLength());
    }

    /// <summary>A person's history and the foreign key's own lookup are one order, so they are one index.</summary>
    /// <remarks>
    /// The columns and their order are what this states. Which way round the instant runs is not readable here — the
    /// read-optimized model refuses that property outright — and it is stated by the migration, where the index is
    /// generated as descending on the instant.
    /// </remarks>
    [Fact]
    public void Model_TheHistoryIndex_LeadsWithThePersonAndThenTheInstant()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = ConversationType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.AgentConversationHistoryIndexName);

        // Assert
        Assert.Equal(
            [
                nameof(AgentConversationEntity.UserId),
                nameof(AgentConversationEntity.LastActivityAt),
            ],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The rows answering an offer are a small minority of a conversation's entries, so the index over them is partial
    /// — an index over all of them would be rewritten on every block a run composes to serve a query only a press
    /// makes.
    /// </summary>
    [Fact]
    public void Model_TheAnsweredOfferIndex_CoversOnlyTheEntriesThatAnswerOne()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntryType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.AgentConversationAnsweredProposalIndexName);

        // Assert
        Assert.Equal(
            [
                nameof(AgentConversationEntryEntity.ConversationId),
                nameof(AgentConversationEntryEntity.AnsweredProposalAt),
            ],
            index.Properties.Select(property => property.Name));
        Assert.Equal(
            $"\"{AgentConversationEntryEntity.AnsweredProposalAtColumnName}\" IS NOT NULL",
            index.GetFilter());
    }

    /// <summary>
    /// The words a search matches are generated by PostgreSQL from the payload and stored, so no writer can forget them,
    /// and they are indexed for the one operator the search applies.
    /// </summary>
    [Fact]
    public void Model_TheSearchVector_IsStoredFromThePayloadAndIndexedForMatching()
    {
        // Arrange
        using var context = CreateContext();
        var entryType = EntryType(context);

        // Act
        var searchVector = entryType.FindProperty(nameof(AgentConversationEntryEntity.SearchVector))!;
        var index = entryType
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.AgentConversationEntrySearchVectorIndexName);

        // Assert
        Assert.True(searchVector.GetIsStored());
        Assert.Contains($"\"{AgentConversationEntryEntity.PayloadColumnName}\"", searchVector.GetComputedColumnSql(), StringComparison.Ordinal);
        Assert.Equal("GIN", index.GetMethod());
    }

    /// <summary>A message's vector goes with the entry it was placed from, which is what erases it with the conversation.</summary>
    [Fact]
    public void Model_TheEntryAVectorWasPlacedFrom_TakesTheVectorWithItWhenItIsRemoved()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = context.Model.FindEntityType(typeof(AgentConversationEmbeddingEntity))!
            .GetForeignKeys()
            .Single(candidate => candidate.GetConstraintName()
                == PersistenceConstraintNames.AgentConversationEmbeddingEntryForeignKeyName);

        // Assert
        Assert.Equal(nameof(AgentConversationEntryEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static IEntityType ConversationType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(AgentConversationEntity))
            ?? throw new InvalidOperationException("The model holds no Agent conversation row.");

    private static IEntityType EntryType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(AgentConversationEntryEntity))
            ?? throw new InvalidOperationException("The model holds no Agent conversation entry row.");

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
