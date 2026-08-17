// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Mutations;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Mutations;

/// <summary>Covers what a stored mutation row states about the local copy of the mail it moved or removed.</summary>
/// <remarks>
/// The disposition is written when the change is authored and read back by a later run, so a value the mapping drops is
/// a decision nobody can act on afterwards: reconciliation would meet the source occurrence gone, find nothing to apply,
/// and leave mail behind that the owner had asked to have disposed of.
/// </remarks>
public sealed class MailboxMutationRecordMappingTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A relocation out of the mirrored mailbox decided what becomes of the local copy, and a later run applies it.</summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public void ToRecord_ARelocationCarryingALocalDisposition_RestoresIt(AuthoredDeleteEmailDisposition disposition)
    {
        // Arrange
        var entity = StoredRelocation();
        entity.LocalDisposition = disposition;

        // Act
        var record = MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder);

        // Assert
        Assert.Equal(disposition, record.Request.LocalDisposition);
    }

    /// <summary>A relocation into a mirrored folder decides nothing locally, because its row is carried into the destination.</summary>
    [Fact]
    public void ToRecord_ARelocationDecidingNothingLocally_RestoresNoDisposition()
    {
        // Arrange
        var entity = StoredRelocation();

        // Act
        var record = MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder);

        // Assert
        Assert.Null(record.Request.LocalDisposition);
    }

    /// <summary>Every disposition destroys what another keeps, so a delete that recorded none is refused rather than defaulted.</summary>
    [Fact]
    public void ToRecord_ADeleteNamingNoLocalDisposition_IsRefused()
    {
        // Arrange
        var entity = StoredRelocation();
        entity.Mutation = MailboxMutation.Delete.Name;
        entity.DestinationFolderPath = null;
        entity.DestinationHierarchyDelimiter = null;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(
            () => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder));
    }

    /// <summary>
    /// Both directions of the star are one mutation, so a mapping that always restored the same one would turn a rule
    /// asking for the flag to be cleared into one asking for it to be set, on a resumed attempt nobody watched.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToRecord_AFlaggedStateChange_RestoresTheDirectionItWasStoredWith(bool isFlagged)
    {
        // Arrange
        var entity = StoredRelocation();
        entity.Mutation = MailboxMutation.SetFlagged.Name;
        entity.DestinationFolderPath = null;
        entity.DestinationHierarchyDelimiter = null;
        entity.RequiresSourceRemoval = false;
        entity.DesiredFlaggedState = isFlagged;

        // Act
        var record = MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder);

        // Assert
        Assert.Equal(isFlagged, record.Request.DesiredFlaggedState);
        Assert.Null(record.Request.DesiredSeenState);
    }

    /// <summary>A stored keyword change has to come back naming the same keywords, or a resumed attempt writes another label.</summary>
    [Fact]
    public void ToRecord_AKeywordChange_RestoresTheKeywordsAsTheyWereStored()
    {
        // Arrange
        var entity = StoredKeywordChange(MailboxMutation.AddKeywords, ["$Todo", "$Invoice"]);

        // Act
        var record = MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder);

        // Assert
        Assert.Equal(AuthoredMailKeywords.Create(["$Invoice", "$Todo"]), record.Request.Keywords);
    }

    /// <summary>
    /// A replacement naming no keyword is a request to clear them all, so the empty array has to survive the read as
    /// itself. Reading it as absence would turn a stored change into a mutation that names no keywords at all and
    /// therefore cannot be performed.
    /// </summary>
    [Fact]
    public void ToRecord_AReplacementNamingNoKeyword_RestoresTheEmptySetRatherThanNothing()
    {
        // Arrange
        var entity = StoredKeywordChange(MailboxMutation.SetKeywords, []);

        // Act
        var record = MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder);

        // Assert
        Assert.NotNull(record.Request.Keywords);
        Assert.True(record.Request.Keywords.IsEmpty);
    }

    /// <summary>A mutation that names no keywords carries a null column, which must not read as a replacement clearing them.</summary>
    [Fact]
    public void ToRecord_AMutationThatNamesNoKeywords_RestoresNoKeywordSet()
    {
        // Act
        var record = MailboxMutationRecordMapping.ToRecord(StoredRelocation(), StoredRelocation().MailFolder);

        // Assert
        Assert.Null(record.Request.Keywords);
        Assert.Null(record.Request.DesiredFlaggedState);
    }

    /// <summary>
    /// A row can only name an unstorable keyword by having been edited outside this system, and issuing the subset that
    /// happens to be usable would be a narrower change than the one written down. Failing visibly is what an operator
    /// can act on.
    /// </summary>
    [Fact]
    public void ToRecord_AStoredKeywordNoServerCouldStore_IsRefused()
    {
        // Arrange
        var entity = StoredKeywordChange(MailboxMutation.AddKeywords, ["$Todo", "two words"]);

        // Act, Assert
        Assert.Throws<InvalidOperationException>(
            () => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder));
    }

    private static MailboxMutationEntity StoredKeywordChange(MailboxMutation mutation, string[] keywords)
    {
        var entity = StoredRelocation();

        entity.Mutation = mutation.Name;
        entity.DestinationFolderPath = null;
        entity.DestinationHierarchyDelimiter = null;
        entity.RequiresSourceRemoval = false;
        entity.Keywords = keywords;

        return entity;
    }

    private static MailboxMutationEntity StoredRelocation()
    {
        var folder = new MailFolderEntity
        {
            MailboxAccountId = "primary",
            Alias = "INBOX",
            ResolutionGeneration = 1,
            RemotePath = "INBOX",
            MailboxAccount = new MailboxAccountEntity { Id = "primary" },
        };

        return new MailboxMutationEntity
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StoredEmailId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StoredEmail = new StoredEmailEntity { MailboxAccountId = "primary", MailFolder = folder },
            MailboxAccountId = "primary",
            MailFolder = folder,
            UidValidity = 1,
            Uid = 7,
            Mutation = MailboxMutation.Relocate.Name,
            RequesterOrigin = MailboxMutationOrigin.Rule,
            RequesterIdentity = "file-invoices",
            DestinationFolderPath = "Archive",
            DestinationHierarchyDelimiter = "/",
            Stage = MailboxMutationStage.Recorded,
            RequiresSourceRemoval = true,
            RecordedAt = RecordedAt,
            StageChangedAt = RecordedAt,
        };
    }
}
