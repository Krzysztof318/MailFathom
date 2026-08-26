// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.History;

/// <summary>Covers what a request has to name to reach a page, and what a cursor is checked against.</summary>
public sealed class SpamClassificationHistoryQueryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("acct-1"));

    private static readonly StoredEmailId Email =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000090a0"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NoPageSizeNamed_ServesTheDefaultOne()
    {
        // Arrange, Act
        var result = QueryOf();

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(SpamClassificationHistoryQuery.DefaultPageSize, result.Query?.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SpamClassificationHistoryQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideTheServedRange_IsRefused(int pageSize)
    {
        // Arrange, Act
        var result = QueryOf(pageSize: pageSize);

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Query);
    }

    [Fact]
    public void Create_AVerdictOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act
        var result = QueryOf(verdict: (SpamVerdict)42);

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.VerdictUnknown, result.Outcome);
    }

    [Fact]
    public void Create_ATimeRangeEndingWhereItBegins_IsRefused()
    {
        // Arrange, Act
        var result = QueryOf(evaluatedFrom: EvaluatedAt, evaluatedBefore: EvaluatedAt);

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.TimeRangeEmpty, result.Outcome);
    }

    /// <summary>A position names a page edge only within the filtered set it was computed for.</summary>
    [Fact]
    public void Create_ACursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var issued = QueryOf(verdict: SpamVerdict.Spam).Query!;
        var cursor = SpamClassificationHistoryCursor.After(EvaluatedAt, Email, issued.FilterFingerprint);

        // Act
        var result = QueryOf(verdict: SpamVerdict.NotSpam, cursor: cursor);

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.CursorFilterMismatch, result.Outcome);
    }

    [Fact]
    public void Create_ACursorIssuedForTheSameFilters_IsAccepted()
    {
        // Arrange
        var issued = QueryOf(verdict: SpamVerdict.Spam).Query!;
        var cursor = SpamClassificationHistoryCursor.After(EvaluatedAt, Email, issued.FilterFingerprint);

        // Act
        var result = QueryOf(verdict: SpamVerdict.Spam, cursor: cursor);

        // Assert
        Assert.Equal(SpamClassificationHistoryQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(cursor, result.Query?.Cursor);
    }

    /// <summary>Pacing is not a filter, so a caller may read the same walk in pages of another length.</summary>
    [Fact]
    public void FilterFingerprint_ThePageSizeChanged_IsUnchanged()
    {
        // Arrange, Act
        var small = QueryOf(pageSize: 10).Query!;
        var large = QueryOf(pageSize: 100).Query!;

        // Assert
        Assert.Equal(small.FilterFingerprint, large.FilterFingerprint);
    }

    [Fact]
    public void FilterFingerprint_AnotherAccount_IsAnotherFingerprint()
    {
        // Arrange
        var here = QueryOf().Query!;

        var elsewhere = SpamClassificationHistoryQuery.Create(
            MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("acct-2")),
            storedEmailId: null,
            verdict: null,
            evaluatedFrom: null,
            evaluatedBefore: null,
            pageSize: null,
            cursor: null).Query!;

        // Assert
        Assert.NotEqual(here.FilterFingerprint, elsewhere.FilterFingerprint);
    }

    private static SpamClassificationHistoryQueryResult QueryOf(
        StoredEmailId? storedEmailId = null,
        SpamVerdict? verdict = null,
        DateTimeOffset? evaluatedFrom = null,
        DateTimeOffset? evaluatedBefore = null,
        int? pageSize = null,
        SpamClassificationHistoryCursor? cursor = null) =>
        SpamClassificationHistoryQuery.Create(
            Account,
            storedEmailId,
            verdict,
            evaluatedFrom,
            evaluatedBefore,
            pageSize,
            cursor);
}
