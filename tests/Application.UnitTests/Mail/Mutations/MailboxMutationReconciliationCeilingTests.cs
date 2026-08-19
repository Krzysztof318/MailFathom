// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

/// <summary>Covers the budget the attribution read applies, which belongs to one occurrence rather than to a window.</summary>
/// <remarks>
/// A record this budget drops is a change MailFathom made that reconciliation then credits to the mailbox owner and
/// reacts to, which is the loop the read exists to prevent. What it has to guarantee is therefore room for every value
/// one occurrence could carry — and that the number says nothing about the window, since a budget spent across one
/// lets a message somebody starred and unstarred repeatedly take every slot from the message beside it.
/// <see cref="Synchronization.Reconciliation.MailboxReconcilerTests" /> covers that
/// consequence against a whole window; what is stated here is the number itself.
/// </remarks>
public sealed class MailboxMutationReconciliationCeilingTests
{
    /// <summary>An attribution settles each value against the newest store of it, so one per value is what it can use.</summary>
    [Fact]
    public void MaximumFlagChangeRecordsPerOccurrence_LeavesRoomForEveryValueAnOccurrenceCouldCarry()
    {
        // Act
        var perOccurrence = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerOccurrence;

        // Assert
        Assert.Equal(MailboxMutation.FlagWriting.Count, perOccurrence);
        Assert.True(perOccurrence > 0);
    }
}
