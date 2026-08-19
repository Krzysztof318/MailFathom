// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Operations;

/// <summary>Covers what the counts by stage report, above all about the stages nothing stands at.</summary>
/// <remarks>
/// A stage that vanished when it emptied would make a healthy outbox and a build that no longer records that stage look
/// identical, so zero is an answer here rather than an absence — and the depth an operator alerts on is the two stages a
/// send can still move from rather than everything ever sent.
/// </remarks>
public sealed class OutboxSummaryTests
{
    [Fact]
    public void Of_CountsForOneStageAlone_ReportsEveryDeclaredStageInDeclarationOrder()
    {
        // Act
        var summary = OutboxSummary.Of([new OutboxStageCount(OutgoingEmailStage.Sent, Count: 7)]);

        // Assert
        Assert.Equal(Enum.GetValues<OutgoingEmailStage>(), summary.Stages.Select(stage => stage.Stage));
        Assert.Equal(7, summary.CountOf(OutgoingEmailStage.Sent));
        Assert.Equal(0, summary.CountOf(OutgoingEmailStage.Recorded));
    }

    /// <summary>The terminal stages are history rather than backlog, so an instance that has sent a great deal is not one that is stuck.</summary>
    [Fact]
    public void OutstandingCount_ASummaryHoldingBothTerminalAndUnfinishedSends_CountsOnlyTheUnfinishedOnes()
    {
        // Act
        var summary = OutboxSummary.Of(
        [
            new OutboxStageCount(OutgoingEmailStage.Recorded, Count: 2),
            new OutboxStageCount(OutgoingEmailStage.TransmissionBegun, Count: 1),
            new OutboxStageCount(OutgoingEmailStage.Sent, Count: 900),
            new OutboxStageCount(OutgoingEmailStage.Refused, Count: 4),
            new OutboxStageCount(OutgoingEmailStage.Cancelled, Count: 3),
        ]);

        // Assert
        Assert.Equal(3, summary.OutstandingCount);
    }

    [Fact]
    public void Of_NothingCountedAtAll_ReportsZeroAtEveryStage()
    {
        // Act
        var summary = OutboxSummary.Of([]);

        // Assert
        Assert.All(summary.Stages, stage => Assert.Equal(0, stage.Count));
        Assert.Equal(0, summary.OutstandingCount);
    }

    /// <summary>Two counts for one stage describe no outbox, so the composition refuses rather than picking one of them.</summary>
    [Fact]
    public void Of_TwoCountsNamingOneStage_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => OutboxSummary.Of(
        [
            new OutboxStageCount(OutgoingEmailStage.Recorded, Count: 1),
            new OutboxStageCount(OutgoingEmailStage.Recorded, Count: 2),
        ]));
    }
}
