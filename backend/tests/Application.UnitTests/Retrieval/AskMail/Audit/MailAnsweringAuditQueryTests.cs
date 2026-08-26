// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail.Audit;

/// <summary>Covers what a request has to name to be served a page, and what it is refused for.</summary>
public sealed class MailAnsweringAuditQueryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A caller that names no size is served a bounded page rather than the whole record.</summary>
    [Fact]
    public void Create_ARequestNamingNoPageSize_IsServedTheDefault()
    {
        // Act
        var result = MailAnsweringAuditQuery.Create(Account, null, null, null, null);

        // Assert
        Assert.Equal(MailAnsweringAuditQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(MailAnsweringAuditQuery.DefaultPageSize, result.Query?.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailAnsweringAuditQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideWhatIsServed_IsRefused(int pageSize)
    {
        // Act
        var result = MailAnsweringAuditQuery.Create(Account, null, null, pageSize, null);

        // Assert
        Assert.Equal(MailAnsweringAuditQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>A range that names no instants is a mistake in the request rather than a page that happens to be empty.</summary>
    [Fact]
    public void Create_ATimeRangeThatEndsAtOrBeforeItBegins_IsRefused()
    {
        // Act
        var result = MailAnsweringAuditQuery.Create(Account, Noon, Noon, null, null);

        // Assert
        Assert.Equal(MailAnsweringAuditQueryOutcome.TimeRangeEmpty, result.Outcome);
    }

    /// <summary>A boundary names a page edge only inside the filtered set it was computed for.</summary>
    [Fact]
    public void Create_ACursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var issued = MailAnsweringAuditQuery.Create(Account, Noon.AddHours(-1), null, null, null).Query!;
        var cursor = MailAnsweringAuditCursor.After(
            Noon,
            MailAnsweringAuditEntryId.Create(Guid.CreateVersion7()),
            issued.FilterFingerprint);

        // Act
        var result = MailAnsweringAuditQuery.Create(Account, Noon.AddHours(-2), null, null, cursor);

        // Assert
        Assert.Equal(MailAnsweringAuditQueryOutcome.CursorFilterMismatch, result.Outcome);
    }

    /// <summary>The page size is not part of the fingerprint, so a caller may pace a walk it is already on.</summary>
    [Fact]
    public void Create_ACursorPresentedWithADifferentPageSize_IsAccepted()
    {
        // Arrange
        var issued = MailAnsweringAuditQuery.Create(Account, Noon.AddHours(-1), null, 10, null).Query!;
        var cursor = MailAnsweringAuditCursor.After(
            Noon,
            MailAnsweringAuditEntryId.Create(Guid.CreateVersion7()),
            issued.FilterFingerprint);

        // Act
        var result = MailAnsweringAuditQuery.Create(Account, Noon.AddHours(-1), null, 25, cursor);

        // Assert
        Assert.Equal(MailAnsweringAuditQueryOutcome.Accepted, result.Outcome);
    }

    /// <summary>Two accounts are two walks, so a boundary from one never continues the other.</summary>
    [Fact]
    public void FilterFingerprint_TwoAccounts_Differ()
    {
        // Act
        var work = MailAnsweringAuditQuery.Create(Account, null, null, null, null).Query!;
        var personal = MailAnsweringAuditQuery
            .Create(MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal")), null, null, null, null)
            .Query!;

        // Assert
        Assert.NotEqual(work.FilterFingerprint, personal.FilterFingerprint);
    }

    [Fact]
    public void Refused_AnAcceptance_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MailAnsweringAuditQueryResult.Refused(MailAnsweringAuditQueryOutcome.Accepted));
    }
}
