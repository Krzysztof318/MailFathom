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
        var reference = Assert.Single(
            EntityTypeOf<EmailThreadEntity>(context).GetForeignKeys(),
            candidate => candidate.Properties.Any(property => property.Name == "MailboxAccountId"));

        // Assert
        Assert.Equal(["MailboxAccountId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// A merged conversation is still reached by the identifier a tool published before the merge, and the walk that
    /// resolves it ends at whatever this column names — so the column is constrained rather than trusted.
    /// </summary>
    [Fact]
    public void EmailThreadModel_Survivor_PointsAtAConversationTheTableHolds()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(
            EntityTypeOf<EmailThreadEntity>(context).GetForeignKeys(),
            candidate => candidate.Properties.Any(property => property.Name == "MergedIntoEmailThreadId"));

        // Assert
        Assert.Equal(typeof(EmailThreadEntity), reference.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, reference.DeleteBehavior);
    }

    /// <summary>
    /// A merge amends the row in place, and two arrivals reach it at once, so the row's version is what stops one of
    /// them writing a survivor the other had already folded into a third. It is the `xmin` system column rather than one
    /// of the row's own, which is why the assertion is on the token rather than on a column that was added.
    /// </summary>
    [Fact]
    public void EmailThreadModel_Version_IsTheRowVersionAMergeIsSettledBy()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var version = EntityTypeOf<EmailThreadEntity>(context).FindProperty("ConcurrencyVersion");

        // Assert
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
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
