// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the two tables a vector's meaning rests on. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so this states what the columns, keys, and constraints are declared to be;
/// whether PostgreSQL then refuses a vector of the wrong width is an integration question.
/// </summary>
public sealed class EmbeddingSchemaModelTests
{
    /// <summary>
    /// The column is dimensionless so two profiles of different widths coexist in one table, each reachable by an
    /// expression index of its own. Declaring <c>vector(N)</c> would fix the whole table to one profile's geometry.
    /// </summary>
    [Fact]
    public void EmailEmbeddingModel_EmbeddingColumn_IsTheDimensionlessVectorType()
    {
        // Act
        var embedding = EmbeddingProperty(nameof(EmailEmbeddingEntity.Embedding));

        // Assert
        Assert.Equal("vector", embedding.GetColumnType());
        Assert.False(embedding.IsNullable);
    }

    /// <summary>
    /// A width dropped from the column is not a width dropped from the schema. The check ties the stored vector's own
    /// length to the number beside it, and the composite foreign key below ties that number to the profile's.
    /// </summary>
    [Fact]
    public void EmailEmbeddingModel_StoredVector_IsCheckedAgainstTheRecordedDimension()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var check = Assert.Single(EntityType<EmailEmbeddingEntity>(context).GetCheckConstraints());

        // Assert
        Assert.Equal(PersistenceConstraintNames.EmailEmbeddingDimensionCheckConstraintName, check.Name);
        Assert.Equal(
            $"vector_dims(\"{nameof(EmailEmbeddingEntity.Embedding)}\") = \"{nameof(EmailEmbeddingEntity.Dimension)}\"",
            check.Sql);
    }

    /// <summary>
    /// PostgreSQL evaluates a check against one row, so the dimension the check reads has to be pinned to the profile's
    /// own by a foreign key. Without it the check would only prove a vector agrees with a number nobody constrained.
    /// </summary>
    [Fact]
    public void EmailEmbeddingModel_ProfileReference_CarriesTheDimensionItIsCheckedAgainst()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var profileReference = Assert.Single(
            EntityType<EmailEmbeddingEntity>(context).GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(EmbeddingProfileEntity));

        // Assert
        Assert.Equal(
            [nameof(EmailEmbeddingEntity.EmbeddingProfileId), nameof(EmailEmbeddingEntity.Dimension)],
            profileReference.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(EmbeddingProfileEntity.Id), nameof(EmbeddingProfileEntity.Dimension)],
            profileReference.PrincipalKey.Properties.Select(property => property.Name));
    }

    /// <summary>A profile is what a stored vector's attribution points at, so it cannot be removed while one names it.</summary>
    [Fact]
    public void EmailEmbeddingModel_ProfileReference_RefusesToCascade()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var profileReference = Assert.Single(
            EntityType<EmailEmbeddingEntity>(context).GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(EmbeddingProfileEntity));

        // Assert
        Assert.Equal(DeleteBehavior.Restrict, profileReference.DeleteBehavior);
    }

    /// <summary>
    /// Deleting a message deletes everything derived from it. The chunk already cascades from the stored email, so this
    /// is the last link of the path erasure reaches a vector through.
    /// </summary>
    [Fact]
    public void EmailEmbeddingModel_OwningChunkDeleted_CascadesToTheVector()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var chunkReference = Assert.Single(
            EntityType<EmailEmbeddingEntity>(context).GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(EmailChunkEntity));

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, chunkReference.DeleteBehavior);
    }

    /// <summary>The chunk and the profile together are what a vector is, and what an idempotent upsert conflicts on.</summary>
    [Fact]
    public void EmailEmbeddingModel_Key_IsTheChunkAndTheProfileTogether()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntityType<EmailEmbeddingEntity>(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(
            [nameof(EmailEmbeddingEntity.EmailChunkId), nameof(EmailEmbeddingEntity.EmbeddingProfileId)],
            key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>A superseded generation is deleted in bounded batches read by profile, which without this scans the table.</summary>
    [Fact]
    public void EmailEmbeddingModel_Generation_IsReachableByItsProfile()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType<EmailEmbeddingEntity>(context)
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.EmailEmbeddingProfileIndexName);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(
            [nameof(EmailEmbeddingEntity.EmbeddingProfileId), nameof(EmailEmbeddingEntity.Dimension)],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The unique index is what makes activation idempotent: re-declaring a geometry already registered resolves to that
    /// row instead of inserting a second one whose vectors would be produced from scratch for nothing.
    /// </summary>
    [Fact]
    public void EmbeddingProfileModel_Identity_IsUniqueOnItsFingerprint()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType<EmbeddingProfileEntity>(context)
            .GetIndexes()
            .FirstOrDefault(candidate =>
                candidate.GetDatabaseName() == PersistenceConstraintNames.EmbeddingProfileFingerprintUniqueIndexName);

        // Assert
        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(EmbeddingProfileEntity.IdentityFingerprint)],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// Two rows claiming to serve would leave retrieval reading whichever one a query returned, with half the vectors
    /// in the table unreachable and nothing about the answers saying so. The index is partial to the two states that
    /// admit one row each, because a deployment accumulates one superseded row per model it has ever used.
    /// </summary>
    [Fact]
    public void EmbeddingProfileModel_Lifecycle_AdmitsOneGenerationBeingBuiltAndOneBeingRead()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType<EmbeddingProfileEntity>(context)
            .GetIndexes()
            .FirstOrDefault(candidate =>
                candidate.GetDatabaseName() == PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName);

        // Assert
        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(EmbeddingProfileEntity.LifecycleState)],
            index.Properties.Select(property => property.Name));

        // The filter names the values the column stores, which are the enum member names rather than their numbers.
        Assert.Equal(
            "\"LifecycleState\" IN ('Building', 'Active')",
            index.GetFilter());
    }

    /// <summary>The fingerprint column is the digest's own shape, so a value of any other width cannot be written at all.</summary>
    [Fact]
    public void EmbeddingProfileModel_Fingerprint_IsFixedAtTheDigestLength()
    {
        // Act
        var fingerprint = ProfileProperty(nameof(EmbeddingProfileEntity.IdentityFingerprint));

        // Assert
        Assert.Equal(EmbeddingProfileFingerprint.Length, fingerprint.GetMaxLength());
        Assert.True(fingerprint.IsFixedLength());
        Assert.False(fingerprint.IsNullable);
    }

    /// <summary>Each identity name is bounded by the same constant the declaration is refused against.</summary>
    [Theory]
    [InlineData(nameof(EmbeddingProfileEntity.Provider), EmbeddingProfileIdentity.MaximumProviderLength)]
    [InlineData(nameof(EmbeddingProfileEntity.ModelIdentifier), EmbeddingProfileIdentity.MaximumModelIdentifierLength)]
    [InlineData(nameof(EmbeddingProfileEntity.ModelVersion), EmbeddingProfileIdentity.MaximumModelVersionLength)]
    [InlineData(
        nameof(EmbeddingProfileEntity.PassageInstruction),
        EmbeddingInputPreparation.MaximumPassageInstructionLength)]
    public void EmbeddingProfileModel_IdentityName_IsBoundedWhereTheDeclarationIs(string propertyName, int maximumLength)
    {
        // Act
        var property = ProfileProperty(propertyName);

        // Assert
        Assert.Equal(maximumLength, property.GetMaxLength());
    }

    /// <summary>The state a profile is in stays readable in an audit query and survives any later reordering of the enum.</summary>
    [Theory]
    [InlineData(nameof(EmbeddingProfileEntity.LifecycleState))]
    [InlineData(nameof(EmbeddingProfileEntity.DistanceMetric))]
    public void EmbeddingProfileModel_NamedValue_IsStoredAsText(string propertyName)
    {
        // Act
        var property = ProfileProperty(propertyName);

        // Assert
        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.False(property.IsNullable);
    }

    /// <summary>A profile whose vectors are still being produced has no activation moment yet, and that is a state rather than a gap.</summary>
    [Theory]
    [InlineData(nameof(EmbeddingProfileEntity.ActivatedAt))]
    [InlineData(nameof(EmbeddingProfileEntity.SupersededAt))]
    public void EmbeddingProfileModel_LifecycleMoment_IsAbsentUntilItHappens(string propertyName)
    {
        // Act
        var property = ProfileProperty(propertyName);

        // Assert
        Assert.True(property.IsNullable);
    }

    /// <summary>
    /// Nothing operational reaches this row. The endpoint, the credential, and every rate or batch limit are
    /// configuration, so rotating a key or raising a limit can never be edited into disagreeing with stored vectors.
    /// </summary>
    [Fact]
    public void EmbeddingProfileModel_Row_HoldsOnlyIdentityAndLifecycle()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var columns = EntityType<EmbeddingProfileEntity>(context)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            [
                nameof(EmbeddingProfileEntity.ActivatedAt),
                nameof(EmbeddingProfileEntity.Dimension),
                nameof(EmbeddingProfileEntity.DistanceMetric),
                nameof(EmbeddingProfileEntity.Id),
                nameof(EmbeddingProfileEntity.IdentityFingerprint),
                nameof(EmbeddingProfileEntity.InputCharacterLimit),
                nameof(EmbeddingProfileEntity.LifecycleState),
                nameof(EmbeddingProfileEntity.ModelIdentifier),
                nameof(EmbeddingProfileEntity.ModelVersion),
                nameof(EmbeddingProfileEntity.NormalizesVector),
                nameof(EmbeddingProfileEntity.PassageInstruction),
                nameof(EmbeddingProfileEntity.Provider),
                nameof(EmbeddingProfileEntity.RegisteredAt),
                nameof(EmbeddingProfileEntity.SupersededAt),
            ],
            columns);
    }

    /// <summary>Chunk text and vectors are the two large derived values, and neither belongs on a row a timeline reads.</summary>
    [Fact]
    public void EmbeddingSchema_Vectors_LiveInATableOfTheirOwn()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var storedEmail = EntityType<StoredEmailEntity>(context);

        // Assert
        Assert.DoesNotContain(storedEmail.GetProperties(), property => property.ClrType == typeof(Vector));
        Assert.Equal("email_embeddings", EntityType<EmailEmbeddingEntity>(context).GetTableName());
        Assert.Equal("embedding_profiles", EntityType<EmbeddingProfileEntity>(context).GetTableName());
    }

    private static IProperty EmbeddingProperty(string propertyName) =>
        PropertyOf<EmailEmbeddingEntity>(propertyName);

    private static IProperty ProfileProperty(string propertyName) =>
        PropertyOf<EmbeddingProfileEntity>(propertyName);

    private static IProperty PropertyOf<TEntity>(string propertyName)
        where TEntity : class
    {
        using var context = CreateContext();

        var property = EntityType<TEntity>(context).FindProperty(propertyName);

        Assert.NotNull(property);

        return property;
    }

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the index and constraint configuration a schema is generated from.
    /// </summary>
    private static IEntityType EntityType<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
