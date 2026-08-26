// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Audit;

/// <summary>Covers what a request for one page of an audit trail is accepted and refused for.</summary>
public sealed class MailboxMutationAuditQueryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A request that names no page size is served the default rather than the whole trail.</summary>
    [Fact]
    public void Create_WithoutAPageSize_UsesTheDefault()
    {
        // Act
        var result = Create(pageSize: null);

        // Assert
        Assert.Equal(MailboxMutationAuditQuery.DefaultPageSize, result.Query?.PageSize);
    }

    /// <summary>A page size outside the served range is refused rather than clamped, so nothing is silently dropped.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailboxMutationAuditQuery.MaximumPageSize + 1)]
    public void Create_PageSizeOutsideTheServedRange_IsRefused(int pageSize)
    {
        // Act
        var result = Create(pageSize);

        // Assert
        Assert.Equal(MailboxMutationAuditQueryOutcome.PageSizeOutOfRange, result.Outcome);
    }

    /// <summary>A range that ends where it begins names no entries, and saying so beats serving an empty page.</summary>
    [Fact]
    public void Create_TimeRangeThatEndsWhereItBegins_IsRefused()
    {
        // Act
        var result = MailboxMutationAuditQuery.Create(
            Account,
            mutation: default,
            CompletedAt,
            CompletedAt,
            pageSize: null,
            cursor: null);

        // Assert
        Assert.Equal(MailboxMutationAuditQueryOutcome.TimeRangeEmpty, result.Outcome);
    }

    /// <summary>A cursor from one walk names no boundary in another, so presenting it to different filters is refused.</summary>
    [Fact]
    public void Create_CursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var issuingQuery = Create(pageSize: null).Query!;
        var cursor = MailboxMutationAuditCursor.After(Entry(), issuingQuery.FilterFingerprint);

        // Act
        var result = MailboxMutationAuditQuery.Create(
            Account,
            MailboxMutation.Delete,
            completedFrom: null,
            completedBefore: null,
            pageSize: null,
            cursor);

        // Assert
        Assert.Equal(MailboxMutationAuditQueryOutcome.CursorFilterMismatch, result.Outcome);
    }

    /// <summary>The same filters read under a different page size continue the same walk, because pacing is not a filter.</summary>
    [Fact]
    public void Create_CursorPresentedWithADifferentPageSize_IsAccepted()
    {
        // Arrange
        var issuingQuery = Create(pageSize: 10).Query!;
        var cursor = MailboxMutationAuditCursor.After(Entry(), issuingQuery.FilterFingerprint);

        // Act
        var result = MailboxMutationAuditQuery.Create(
            Account,
            mutation: default,
            completedFrom: null,
            completedBefore: null,
            pageSize: 25,
            cursor);

        // Assert
        Assert.Equal(MailboxMutationAuditQueryOutcome.Accepted, result.Outcome);
    }

    /// <summary>Two accounts never share a fingerprint, so a cursor cannot walk into another mailbox's history.</summary>
    [Fact]
    public void FilterFingerprint_DiffersByAccount()
    {
        // Arrange
        var mine = Create(pageSize: null).Query!;

        var theirs = MailboxMutationAuditQuery.Create(
            MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal")),
            mutation: default,
            completedFrom: null,
            completedBefore: null,
            pageSize: null,
            cursor: null).Query!;

        // Act
        var fingerprints = new[] { mine.FilterFingerprint, theirs.FilterFingerprint };

        // Assert
        Assert.Distinct(fingerprints);
    }

    private static MailboxMutationAuditQueryResult Create(int? pageSize) => MailboxMutationAuditQuery.Create(
        Account,
        mutation: default,
        completedFrom: null,
        completedBefore: null,
        pageSize,
        cursor: null);

    private static MailboxMutationAuditEntry Entry() => new()
    {
        Id = MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(CompletedAt)),
        MutationRecordId = MailboxMutationRecordId.Create(Guid.CreateVersion7(CompletedAt)),
        Owner = Account.Owner,
        AccountId = Account.Id,
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7(CompletedAt)),
        Mutation = MailboxMutation.Relocate,
        SourceFolderPath = RemoteFolderPath.Create("INBOX"),
        SourceUidValidity = ImapUidValidity.Create(1),
        SourceUid = ImapUid.Create(41),
        DestinationFolderPath = RemoteFolderPath.Create("Archive", '/'),
        Placement = RemoteEmailPlacement.NotReported(),
        DesiredSeenState = null,
        Requester = MailboxMutationRequester.Rule("file-newsletters", "3"),
        RequestedAt = CompletedAt.AddMinutes(-1),
        CompletedAt = CompletedAt,
        Outcome = MailboxMutationAuditOutcome.Performed,
        Failure = (MailFathomErrorCode?)null,
    };
}
