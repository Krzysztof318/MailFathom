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
/// Asserts the shape the conversation tables are declared with. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so what this states is the declaration a schema is generated from.
/// </summary>
public sealed class EmailThreadModelTests
{
    /// <summary>
    /// The key is the whole row bar the conversation it points at, which is what makes assembly idempotent without a
    /// read-then-write: an arrival re-registering an identifier it already registered is refused by the key rather than
    /// duplicated, and a genuine race between two first arrivals is reported as the conflict it is.
    /// </summary>
    [Fact]
    public void EmailThreadIdentifierModel_Key_IsTheAccountAndTheDigestUnderTheNameAConflictIsRecognizedBy()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntityTypeOf<EmailThreadIdentifierEntity>(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["MailboxAccountId", "IdentifierHash"], key.Properties.Select(property => property.Name));
        Assert.Equal(MailFathomDbContext.EmailThreadIdentifierPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>
    /// The identifier is stored as a bounded string rather than a blank-padded fixed-length one: a PostgreSQL
    /// <c>character(n)</c> compares by its own rules, and the width is guaranteed by the digest that produces the value.
    /// </summary>
    [Fact]
    public void EmailThreadIdentifierModel_Digest_IsBoundedRatherThanFixedLength()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var digest = EntityTypeOf<EmailThreadIdentifierEntity>(context).FindProperty("IdentifierHash");

        // Assert
        Assert.NotNull(digest);
        Assert.Equal(EmailThreadIdentifierEntity.IdentifierHashLength, digest.GetMaxLength());
        Assert.NotEqual(true, digest.IsFixedLength());
    }

    /// <summary>The identifiers of one conversation are what a merge repoints, so they are reachable by it.</summary>
    [Fact]
    public void EmailThreadIdentifierModel_ThreadIndex_CoversTheConversationAMergeRepoints()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityTypeOf<EmailThreadIdentifierEntity>(context)
            .GetIndexes()
            .FirstOrDefault(candidate =>
                candidate.GetDatabaseName() == MailFathomDbContext.EmailThreadIdentifierThreadIndexName);

        // Assert
        Assert.NotNull(index);
        Assert.Equal(["EmailThreadId"], index.Properties.Select(property => property.Name));
    }

    /// <summary>A conversation is an assembly of one account's mail and outlives none of it.</summary>
    [Fact]
    public void EmailThreadModel_Account_ErasesTheConversationsWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(EntityTypeOf<EmailThreadEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["MailboxAccountId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>An identifier means nothing without the conversation it names, so it goes when that conversation does.</summary>
    [Fact]
    public void EmailThreadIdentifierModel_Conversation_ErasesItsIdentifiersWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(EntityTypeOf<EmailThreadIdentifierEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["EmailThreadId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the index configuration a schema is generated from.
    /// </summary>
    private static IEntityType EntityTypeOf<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
