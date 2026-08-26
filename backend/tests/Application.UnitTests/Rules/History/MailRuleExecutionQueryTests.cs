// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers what a request for a page of the rule history is accepted with, and what it is refused for.</summary>
public sealed class MailRuleExecutionQueryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));
    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ARequestNamingNothingButAnAccount_ServesTheDefaultPage()
    {
        // Act
        var result = Create();

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(MailRuleExecutionQuery.DefaultPageSize, result.Query!.PageSize);
        Assert.Null(result.Query.RuleName);
        Assert.Null(result.Query.StoredEmailId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailRuleExecutionQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideWhatTheHistoryServes_IsRefused(int pageSize)
    {
        // Act
        var result = Create(pageSize: pageSize);

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>Present and blank is a caller who meant to name a rule, which is a different question from every rule.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ARuleFilterHoldingNoName_IsRefused(string ruleName)
    {
        // Act
        var result = Create(ruleName: ruleName);

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.RuleNameBlank, result.Outcome);
    }

    [Fact]
    public void Create_ARuleNameWithSurroundingSpace_ReadsTheNameItself()
    {
        // Act
        var result = Create(ruleName: "  file-invoices  ");

        // Assert
        Assert.Equal("file-invoices", result.Query!.RuleName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Create_ATimeRangeThatEndsAtOrBeforeItBegins_IsRefused(int minutes)
    {
        // Act
        var result = Create(evaluatedFrom: Noon, evaluatedBefore: Noon.AddMinutes(-minutes));

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.TimeRangeEmpty, result.Outcome);
    }

    /// <summary>A boundary names a page edge only within the filtered set it was computed for.</summary>
    [Fact]
    public void Create_ACursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var issued = Create(ruleName: "file-invoices").Query!;
        var cursor = MailRuleExecutionCursor.After(Noon, MailRuleExecutionId.New(), issued.FilterFingerprint);

        // Act
        var result = Create(ruleName: "drop-notifications", cursor: cursor);

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.CursorFilterMismatch, result.Outcome);
    }

    [Fact]
    public void Create_ACursorIssuedForTheSameFilters_ContinuesTheWalk()
    {
        // Arrange
        var issued = Create(ruleName: "file-invoices").Query!;
        var cursor = MailRuleExecutionCursor.After(Noon, MailRuleExecutionId.New(), issued.FilterFingerprint);

        // Act
        var result = Create(ruleName: "file-invoices", cursor: cursor);

        // Assert
        Assert.Equal(MailRuleExecutionQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(cursor, result.Query!.Cursor);
    }

    /// <summary>Pacing is not a filter, so a caller may lengthen or shorten a page without losing its place.</summary>
    [Fact]
    public void FilterFingerprint_TwoPageSizesOverOneFilterSet_IsTheSame()
    {
        // Act
        var shortPage = Create(ruleName: "file-invoices", pageSize: 10).Query!;
        var longPage = Create(ruleName: "file-invoices", pageSize: 100).Query!;

        // Assert
        Assert.Equal(shortPage.FilterFingerprint, longPage.FilterFingerprint);
    }

    /// <summary>Each filter is part of the boundary's identity, so moving any one of them retires the cursors issued under it.</summary>
    [Fact]
    public void FilterFingerprint_EachFilterInTurn_DistinguishesTheWalk()
    {
        // Arrange
        var baseline = Create().Query!.FilterFingerprint;

        // Act
        string[] varied =
        [
            Create(account: MailAccountIdentity.Create(
                SyntheticMailOwner.Deployment,
                MailAccountId.Create("personal"))).Query!.FilterFingerprint,
            Create(ruleName: "file-invoices").Query!.FilterFingerprint,
            Create(storedEmailId: StoredEmailId.Create(Guid.CreateVersion7())).Query!.FilterFingerprint,
            Create(evaluatedFrom: Noon).Query!.FilterFingerprint,
            Create(evaluatedBefore: Noon).Query!.FilterFingerprint,
        ];

        // Assert
        Assert.DoesNotContain(baseline, varied);
        Assert.Equal(varied.Length, varied.Distinct(StringComparer.Ordinal).Count());
    }

    private static MailRuleExecutionQueryResult Create(
        MailAccountIdentity? account = null,
        string? ruleName = null,
        StoredEmailId? storedEmailId = null,
        DateTimeOffset? evaluatedFrom = null,
        DateTimeOffset? evaluatedBefore = null,
        int? pageSize = null,
        MailRuleExecutionCursor? cursor = null) =>
        MailRuleExecutionQuery.Create(
            account ?? Account,
            ruleName,
            storedEmailId,
            evaluatedFrom,
            evaluatedBefore,
            pageSize,
            cursor);
}
