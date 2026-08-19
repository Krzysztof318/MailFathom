// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

/// <summary>Covers the ceiling the attribution read applies, which follows the window rather than a constant.</summary>
/// <remarks>
/// A record dropped by this ceiling is a change MailFathom made that reconciliation then credits to the mailbox owner
/// and reacts to, which is the loop the read exists to prevent. So what the number has to guarantee is room for every
/// value the window's occurrences could carry; a constant would have had to be right for a window of ten and for the
/// ten thousand the configuration permits.
/// </remarks>
public sealed class MailboxMutationReconciliationCeilingTests
{
    [Fact]
    public void MaximumFlagChangeRecordsFor_AWindowOfOneOccurrence_LeavesRoomForEveryValueItCouldCarry()
    {
        // Act
        var ceiling = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsFor(1);

        // Assert
        Assert.Equal(MailboxMutation.FlagWriting.Count, ceiling);
    }

    /// <summary>The largest window an account may be configured with still gets one record per value per occurrence.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(10000)]
    public void MaximumFlagChangeRecordsFor_AnyPermittedWindow_ScalesWithIt(int changedOccurrenceCount)
    {
        // Act
        var ceiling = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsFor(changedOccurrenceCount);

        // Assert
        Assert.Equal(changedOccurrenceCount * MailboxMutation.FlagWriting.Count, ceiling);
        Assert.True(ceiling >= changedOccurrenceCount);
    }

    /// <summary>A window that found nothing moved asks nothing, so the ceiling it would apply is nothing too.</summary>
    [Fact]
    public void MaximumFlagChangeRecordsFor_AWindowWhereNothingMoved_IsZero()
    {
        // Act
        var ceiling = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsFor(0);

        // Assert
        Assert.Equal(0, ceiling);
    }
}
