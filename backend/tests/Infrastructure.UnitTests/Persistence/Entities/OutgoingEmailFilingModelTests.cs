// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the model the filing table's schema is generated from. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so this states what the constraint and the two indexes are declared
/// to be; whether PostgreSQL then plans the join against them is an integration question.
/// </summary>
public sealed class OutgoingEmailFilingModelTests
{
    /// <summary>
    /// The key is what refuses a second copy of one send into one place, and a lost race is only recognized as one
    /// where the constraint carries a name the conflict predicate can name back.
    /// </summary>
    [Fact]
    public void OutgoingEmailFilingModel_TheKeyThatRefusesASecondCopy_CarriesTheNameTheConflictPredicateRecognizes()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = FilingEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["OutgoingEmailId", "Filing"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.OutgoingEmailFilingPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>
    /// All three read the copies a folder may still hold, so all three carry the same filter and it is the stage alone.
    /// Dropping the stage would leave every mirror withdrawn before a run saw it, and every append the server never
    /// answered, sitting in all of them for the life of the deployment; adding the meeting back would drop a copy out
    /// of them the moment it was recognized, which is what would leave the second occurrence of the same message — the
    /// one a provider filed itself — read as mail somebody sent the user.
    /// </summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingPlacementIndexName, "PlacementUid")]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingMessageIdIndexName, "InternetMessageId")]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingRecentByFilingIndexName, "OutgoingEmailId")]
    public void OutgoingEmailFilingModel_TheFilingIndexes_AreFilteredToTheCopiesAFolderMayStillHold(
        string indexName,
        string expectedLastColumn)
    {
        // Act
        var index = FindFilingIndex(indexName);

        // Assert — the account a filing names is the pair, so every one of them narrows on the user before the
        // identifier.
        Assert.Equal(["UserId", "MailboxAccountId"], index.Properties.Take(2).Select(property => property.Name));
        Assert.Equal(expectedLastColumn, index.Properties[^1].Name);
        Assert.Equal("\"Stage\" = 'Confirmed'", index.GetFilter());
    }

    /// <summary>
    /// The sweep for a copy the provider duplicated reads one account's standing copies of one kind, oldest append
    /// first, and takes a few. The order of the key is what lets that be read rather than scanned and sorted: the
    /// filing before the instant, because the filing is an equality and the instant is a range.
    /// </summary>
    [Fact]
    public void OutgoingEmailFilingModel_TheRecentByFilingIndex_LeadsWithTheFilingAndThenTheAppend()
    {
        // Act
        var index = FindFilingIndex(PersistenceConstraintNames.OutgoingEmailFilingRecentByFilingIndexName);

        // Assert
        Assert.Equal(
            ["UserId", "MailboxAccountId", "Filing", "AppendedAt", "OutgoingEmailId"],
            index.Properties.Select(property => property.Name));
    }

    private static IIndex FindFilingIndex(string indexName)
    {
        using var context = CreateContext();

        var index = FilingEntityType(context)
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType FilingEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutgoingEmailFilingEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
