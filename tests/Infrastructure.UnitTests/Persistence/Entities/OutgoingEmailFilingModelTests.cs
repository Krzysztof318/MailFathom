// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// Both indexes serve one join, and the join matches a confirmed row nothing has met yet. Filtering on only half of
    /// that would leave every mirror withdrawn before a run saw it, and every append the server never answered, sitting
    /// in both structures for the life of the deployment — so their size would follow everything ever sent rather than
    /// what is in flight.
    /// </summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingPlacementIndexName, "PlacementUid")]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingMessageIdIndexName, "InternetMessageId")]
    public void OutgoingEmailFilingModel_TheJoinIndexes_AreFilteredToExactlyTheRowsTheJoinCanMatch(
        string indexName,
        string expectedLastColumn)
    {
        // Act
        var index = FindFilingIndex(indexName);

        // Assert
        Assert.Equal("MailboxAccountId", index.Properties[0].Name);
        Assert.Equal(expectedLastColumn, index.Properties[^1].Name);
        Assert.Equal("\"ObservedAt\" IS NULL AND \"Stage\" = 'Confirmed'", index.GetFilter());
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
