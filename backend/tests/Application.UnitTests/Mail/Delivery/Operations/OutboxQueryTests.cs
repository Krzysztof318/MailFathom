// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Operations;

/// <summary>Covers what a request for a page of an outbox is accepted as, and what it is refused for.</summary>
/// <remarks>
/// The refusals are the subject. A page is bounded whatever a caller asks for, a stage filter names a stage a send can
/// actually stand at, and a cursor belongs to the walk it was issued for — the last one is what stops a boundary from
/// one set of filters silently skipping records under another.
/// </remarks>
public sealed class OutboxQueryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    [Fact]
    public void Create_ARequestNamingNothing_IsServedTheDefaultPageOverEveryAccountAndStage()
    {
        // Act
        var result = OutboxQuery.Create(account: null, stage: null, pageSize: null, cursor: null);

        // Assert
        Assert.Equal(OutboxQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(OutboxQuery.DefaultPageSize, result.Query!.PageSize);
        Assert.Null(result.Query.AccountId);
        Assert.Null(result.Query.Stage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(OutboxQuery.MaximumPageSize + 1)]
    public void Create_APageSizeOutsideTheServedRange_IsRefusedWithoutAQuery(int pageSize)
    {
        // Act
        var result = OutboxQuery.Create(account: null, stage: null, pageSize, cursor: null);

        // Assert
        Assert.Equal(OutboxQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>A stage nothing declares would filter on a value no row carries, which is an empty page rather than the mistake the caller made.</summary>
    [Fact]
    public void Create_AStageNoSendCanStandAt_IsRefusedWithoutAQuery()
    {
        // Act
        var result = OutboxQuery.Create(
            account: null,
            (OutgoingEmailStage)int.MaxValue,
            pageSize: null,
            cursor: null);

        // Assert
        Assert.Equal(OutboxQueryOutcome.StageUnknown, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>A cursor is a position in one reading, so continuing a different reading with it would skip records nobody saw.</summary>
    [Fact]
    public void Create_ACursorIssuedForOtherFilters_IsRefusedWithoutAQuery()
    {
        // Arrange
        var issuedFor = OutboxQuery.Create(Account, stage: null, pageSize: null, cursor: null).Query!;
        var cursor = OutboxCursor.After(
            new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero),
            OutgoingEmailId.Create(Guid.CreateVersion7()),
            issuedFor.FilterFingerprint);

        // Act
        var result = OutboxQuery.Create(account: null, stage: null, pageSize: null, cursor);

        // Assert
        Assert.Equal(OutboxQueryOutcome.CursorFilterMismatch, result.Outcome);
        Assert.Null(result.Query);
    }

    /// <summary>Pacing is not a filter, so a caller may lengthen or shorten a page while continuing the same walk.</summary>
    [Fact]
    public void Create_ACursorPresentedWithADifferentPageSize_ContinuesTheSameWalk()
    {
        // Arrange
        var issuedFor = OutboxQuery.Create(Account, OutgoingEmailStage.Recorded, pageSize: 10, cursor: null).Query!;
        var cursor = OutboxCursor.After(
            new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero),
            OutgoingEmailId.Create(Guid.CreateVersion7()),
            issuedFor.FilterFingerprint);

        // Act
        var result = OutboxQuery.Create(Account, OutgoingEmailStage.Recorded, pageSize: 25, cursor);

        // Assert
        Assert.Equal(OutboxQueryOutcome.Accepted, result.Outcome);
        Assert.Equal(25, result.Query!.PageSize);
    }

    /// <summary>Two filter sets that differ anywhere are different walks, which is the whole of what the fingerprint promises.</summary>
    [Fact]
    public void FilterFingerprint_TwoQueriesNarrowedDifferently_DoNotShareAFingerprint()
    {
        // Arrange
        var byAccount = OutboxQuery.Create(Account, stage: null, pageSize: null, cursor: null).Query!;
        var byStage = OutboxQuery.Create(Account, OutgoingEmailStage.Refused, pageSize: null, cursor: null).Query!;

        // Assert
        Assert.NotEqual(byAccount.FilterFingerprint, byStage.FilterFingerprint);
    }

    /// <summary>A refusal names what to write instead, so the stages it lists are the ones a caller may actually narrow to.</summary>
    [Fact]
    public void DeclaredStages_Always_NamesEveryStageASendCanStandAt()
    {
        // Act
        var declared = OutboxQuery.DeclaredStages();

        // Assert
        Assert.All(
            Enum.GetValues<OutgoingEmailStage>(),
            stage => Assert.Contains(stage.ToString(), declared, StringComparison.Ordinal));
    }
}
