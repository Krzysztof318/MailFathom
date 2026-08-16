// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the model the schema is generated from. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so this states what the indexes are declared to be; whether PostgreSQL then
/// plans a query against them is an integration question the integration suite answers.
/// </summary>
public sealed class StoredEmailModelTests
{
    private static readonly string[] AccountTimelineColumns = ["MailboxAccountId", "ReceivedAt", "Id"];

    private static readonly string[] FolderTimelineColumns = ["MailFolderId", "ReceivedAt", "Id"];

    [Theory]
    [InlineData(MailFathomDbContext.StoredEmailAccountTimelineIndexName)]
    [InlineData(MailFathomDbContext.StoredEmailFolderTimelineIndexName)]
    public void StoredEmailModel_TimelineIndex_OrdersByTheReceivedTimestampDescendingWithTheIdentifierAsTiebreaker(string indexName)
    {
        // Arrange
        var expectedColumns = indexName == MailFathomDbContext.StoredEmailAccountTimelineIndexName
            ? AccountTimelineColumns
            : FolderTimelineColumns;

        // Act
        var index = FindStoredEmailIndex(indexName);

        // Assert
        Assert.Equal(expectedColumns, index.Properties.Select(property => property.Name));
        Assert.Equal([false, true, true], index.IsDescending);
    }

    /// <summary>
    /// PostgreSQL orders nulls first under <c>DESC</c>, so an index that says nothing would sort every message nobody
    /// could date above the newest mail. The contract puts them last and the index has to spell that out.
    /// </summary>
    [Theory]
    [InlineData(MailFathomDbContext.StoredEmailAccountTimelineIndexName)]
    [InlineData(MailFathomDbContext.StoredEmailFolderTimelineIndexName)]
    public void StoredEmailModel_TimelineIndex_SortsAnUnknownReceivedTimestampLast(string indexName)
    {
        // Act
        var index = FindStoredEmailIndex(indexName);

        // Assert
        Assert.Equal(
            [NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified],
            index.GetNullSortOrder());
    }

    [Fact]
    public void StoredEmailModel_RemoteOccurrenceIdentity_IsUniqueOnFolderUidValidityAndUid()
    {
        // Act
        var index = FindStoredEmailIndex(MailFathomDbContext.StoredEmailOccurrenceUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal(["MailFolderId", "UidValidity", "Uid"], index.Properties.Select(property => property.Name));
    }

    /// <summary>A recipient filter is a containment test over an array, which only a GIN index can serve.</summary>
    [Theory]
    [InlineData(MailFathomDbContext.StoredEmailToAddressesIndexName, "ToAddresses")]
    [InlineData(MailFathomDbContext.StoredEmailCcAddressesIndexName, "CcAddresses")]
    [InlineData(MailFathomDbContext.StoredEmailReplyToAddressesIndexName, "ReplyToAddresses")]
    public void StoredEmailModel_RecipientArrayIndex_UsesTheInvertedIndexMethod(string indexName, string columnName)
    {
        // Act
        var index = FindStoredEmailIndex(indexName);

        // Assert
        Assert.Equal([columnName], index.Properties.Select(property => property.Name));
        Assert.Equal("GIN", index.GetMethod());
    }

    [Fact]
    public void StoredEmailModel_SenderIndex_CoversTheComparisonFormRatherThanTheWrittenAddress()
    {
        // Act
        var index = FindStoredEmailIndex(MailFathomDbContext.StoredEmailSenderIndexName);

        // Assert
        Assert.Equal(["SenderNormalizedAddress"], index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The order a conversation is published in is computed on every read, so the index is what a read assembles the
    /// raw set from: the conversation, then the identity, which is the total order it bounds and pages by.
    /// </summary>
    [Fact]
    public void StoredEmailModel_ThreadIndex_CoversTheConversationAndTheIdentityItIsAssembledBy()
    {
        // Act
        var index = FindStoredEmailIndex(MailFathomDbContext.StoredEmailThreadIndexName);

        // Assert
        Assert.Equal(["EmailThreadId", "Id"], index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// Erasing one message must not take a conversation or a reply with it. Both references clear the column instead,
    /// so a message whose parent was erased becomes a root and a conversation removed leaves its mail in place.
    /// </summary>
    [Theory]
    [InlineData("EmailThreadId")]
    [InlineData("ParentStoredEmailId")]
    public void StoredEmailModel_ThreadReference_ClearsTheColumnRatherThanErasingTheMessage(string columnName)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = StoredEmailEntityType(context)
            .GetForeignKeys()
            .FirstOrDefault(key => key.Properties.Select(property => property.Name).SequenceEqual([columnName]));

        // Assert
        Assert.NotNull(reference);
        Assert.Equal(DeleteBehavior.SetNull, reference.DeleteBehavior);
    }

    /// <summary>Raw MIME lives in its own table so that no mailbox query can pull a <c>bytea</c> value into the change tracker.</summary>
    [Fact]
    public void StoredEmailModel_MetadataTable_HoldsNoBinaryColumn()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var storedEmail = context.Model.FindEntityType(typeof(StoredEmailEntity))!;

        // Assert
        Assert.DoesNotContain(
            storedEmail.GetProperties(),
            property => property.ClrType == typeof(byte[]) || property.ClrType == typeof(ReadOnlyMemory<byte>));
    }

    private static IIndex FindStoredEmailIndex(string indexName)
    {
        using var context = CreateContext();

        var index = StoredEmailEntityType(context)
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the index configuration a schema is generated from.
    /// </summary>
    private static IEntityType StoredEmailEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredEmailEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
