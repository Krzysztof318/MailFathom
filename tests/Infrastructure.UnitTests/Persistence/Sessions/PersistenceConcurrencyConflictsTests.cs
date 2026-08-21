// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Sessions;

/// <summary>Covers which unique violations are a race a retry can resolve.</summary>
public sealed class PersistenceConcurrencyConflictsTests
{
    /// <summary>Every constraint a competing writer can legitimately win.</summary>
    public static TheoryData<string> RecognizedConstraints =>
    [
        PersistenceConstraintNames.SynchronizationCheckpointPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailFolderBindingUniqueIndexName,
        PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailboxMutationIdentityUniqueIndexName,
        PersistenceConstraintNames.MailboxMutationAuditEntryMutationUniqueIndexName,
        PersistenceConstraintNames.EmbeddingProfileFingerprintUniqueIndexName,
        PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName,
        PersistenceConstraintNames.MailRuleEvaluationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.SpamClassificationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailSpamClassificationPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName,
        PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailRederivationPositionPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailRederivationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.ContactAddressUniqueIndexName,
        PersistenceConstraintNames.MailDraftCopyPrimaryKeyConstraintName,
    ];

    [Theory]
    [MemberData(nameof(RecognizedConstraints))]
    public void IsConcurrencyConflict_UniqueViolationOnARecognizedConstraint_IsAConflict(string constraintName)
    {
        // Arrange
        var exception = CreateUniqueViolation(constraintName);

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.True(isConflict);
    }

    /// <summary>An unlisted constraint stays a failure, because retrying it would repeat a write that cannot succeed.</summary>
    [Fact]
    public void IsConcurrencyConflict_UniqueViolationOnAnUnlistedConstraint_IsNotAConflict()
    {
        // Arrange
        var exception = CreateUniqueViolation("ix_something_this_repository_does_not_retry");

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    /// <summary>A recognized constraint name does not make a different provider error a race.</summary>
    [Fact]
    public void IsConcurrencyConflict_ForeignKeyViolationOnARecognizedConstraint_IsNotAConflict()
    {
        // Arrange
        var exception = new DbUpdateException(
            "save failed",
            CreatePostgresException(
                PostgresErrorCodes.ForeignKeyViolation,
                PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName));

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    /// <summary>A failure that never reached PostgreSQL carries no constraint to classify.</summary>
    [Fact]
    public void IsConcurrencyConflict_InnerExceptionIsNotAProviderFailure_IsNotAConflict()
    {
        // Arrange
        var exception = new DbUpdateException("save failed", new InvalidOperationException("something else"));

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    private static DbUpdateException CreateUniqueViolation(string constraintName) =>
        new("save failed", CreatePostgresException(PostgresErrorCodes.UniqueViolation, constraintName));

    private static PostgresException CreatePostgresException(string sqlState, string constraintName) =>
        new(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            constraintName: constraintName);
}
