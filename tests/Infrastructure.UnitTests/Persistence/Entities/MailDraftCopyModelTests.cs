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
/// Asserts the shape of the model the draft copies table is generated from. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so this states what the key is declared to be; what PostgreSQL then
/// does when two writers reach it is an integration question.
/// </summary>
public sealed class MailDraftCopyModelTests
{
    /// <summary>
    /// The key is what refuses a second append of one revision, and a lost race is only recognized as one where the
    /// constraint carries a name the conflict predicate can name back.
    /// </summary>
    [Fact]
    public void MailDraftCopyModel_TheKeyThatRefusesASecondAppendOfOneRevision_CarriesTheNameTheConflictPredicateRecognizes()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var key = CopyEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["MailDraftId", "Revision"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.MailDraftCopyPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType CopyEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MailDraftCopyEntity))!;
}
