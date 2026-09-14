// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>
/// Covers how the store tells a second account claiming an address apart from every other failed write. The index is
/// the one guarantee that an address is held once, so a violation of it is an answer to report rather than a fault.
/// </summary>
public sealed class PersistedMailAccountRecordStoreTests
{
    [Fact]
    public void ViolatesTheAddressIndex_TheAddressIndexRefusingASaveBeneathTheProvider_IsRecognized()
    {
        // Arrange
        var failure = new DbUpdateException(
            "save failed",
            UniqueViolation(PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName));

        // Act
        var violates = PersistedMailAccountRecordStore.ViolatesTheAddressIndex(failure);

        // Assert
        Assert.True(violates);
    }

    [Theory]
    [InlineData(PersistenceConstraintNames.MailAccountAssignmentUserForeignKeyName)]
    [InlineData("ix_some_other_unique_index")]
    public void ViolatesTheAddressIndex_AnotherConstraintRefusingTheSave_IsNotMistakenForAHeldAddress(string constraintName)
    {
        // Arrange
        var failure = new DbUpdateException("save failed", UniqueViolation(constraintName));

        // Act
        var violates = PersistedMailAccountRecordStore.ViolatesTheAddressIndex(failure);

        // Assert
        Assert.False(violates);
    }

    [Fact]
    public void ViolatesTheAddressIndex_AFailureTheServerNeverAnswered_IsNotMistakenForAHeldAddress()
    {
        // Arrange
        var failure = new DbUpdateException("save failed", new TimeoutException());

        // Act
        var violates = PersistedMailAccountRecordStore.ViolatesTheAddressIndex(failure);

        // Assert
        Assert.False(violates);
    }

    private static PostgresException UniqueViolation(string constraintName) => new(
        messageText: "duplicate key value violates unique constraint",
        severity: "ERROR",
        invariantSeverity: "ERROR",
        sqlState: PostgresErrorCodes.UniqueViolation,
        constraintName: constraintName);
}
