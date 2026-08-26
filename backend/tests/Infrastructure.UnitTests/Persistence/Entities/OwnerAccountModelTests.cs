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
/// Asserts the shape the owner axis is declared with. The model is built in memory by the real PostgreSQL provider and
/// no connection is opened, so what this states is the declaration a schema is generated from — which is where every
/// one of these properties has to be true, because each of them is a guarantee the database gives rather than one the
/// application keeps.
/// </summary>
public sealed class OwnerAccountModelTests
{
    /// <summary>The owner is a relational column, so ownership never depends on reading a document.</summary>
    [Fact]
    public void MailboxAccountModel_Owner_IsARequiredKeyThatErasesTheMailboxWithTheOwner()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(EntityTypeOf<MailboxAccountEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["OwnerId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(typeof(OwnerAccountEntity), reference.PrincipalEntityType.ClrType);
        Assert.True(reference.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// The account is keyed by its owner and the identifier its operator chose, so the identifier names one mailbox
    /// within its owner and nothing across the deployment.
    /// </summary>
    /// <remarks>
    /// The key is also the structure the read that used to need an index of its own is served from — which mail
    /// accounts one owner owns, which erasing an owner asks before taking the rows no cascade reaches — so the model
    /// declares no separate index over the owner and this asserts that it does not.
    /// </remarks>
    [Fact]
    public void MailboxAccountModel_Key_IsTheOwnerAndTheIdentifierAndCoversTheOwnersOwnAccounts()
    {
        // Arrange
        using var context = CreateContext();
        var entityType = EntityTypeOf<MailboxAccountEntity>(context);

        // Act
        var key = entityType.FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["OwnerId", "Id"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName, key.GetName());
        Assert.Empty(entityType.GetIndexes());
    }

    /// <summary>One row per owner, keyed by an identity whoever provisions the owner decides.</summary>
    [Fact]
    public void OwnerAccountModel_Key_IsTheProvisionedOwnerIdentity()
    {
        // Arrange
        using var context = CreateContext();
        var entityType = EntityTypeOf<OwnerAccountEntity>(context);

        // Act
        var key = entityType.FindPrimaryKey();

        // Assert
        Assert.Equal("settings_accounts", entityType.GetTableName());
        Assert.NotNull(key);
        Assert.Equal(["Id"], key.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, key.Properties[0].ValueGenerated);
    }

    /// <summary>The owner's configurable record is one document, and the schema says nothing about what is in it.</summary>
    [Fact]
    public void OwnerAccountModel_Document_IsARequiredJsonbDocument()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var document = EntityTypeOf<OwnerAccountEntity>(context).FindProperty("Document");

        // Assert
        Assert.NotNull(document);
        Assert.Equal("jsonb", document.GetColumnType());
        Assert.False(document.IsNullable);
    }

    /// <summary>
    /// The version is the document's own rather than PostgreSQL's <c>xmin</c>, because a writer has to be able to state
    /// the version it read, be refused by number, and report the version it was refused against — none of which a token
    /// the database generates behind the write can answer.
    /// </summary>
    [Fact]
    public void OwnerAccountModel_Version_IsAWrittenNumberRatherThanTheRowVersion()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var version = EntityTypeOf<OwnerAccountEntity>(context).FindProperty("Version");

        // Assert
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, version.ValueGenerated);
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
