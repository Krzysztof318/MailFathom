// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.AttachmentText;

/// <summary>Covers what one account run may spend reading attachments, and how a pass learns it has run out.</summary>
public sealed class EmailAttachmentTextRunBudgetTests
{
    /// <summary>A pass that has spent nothing may spend everything it was given.</summary>
    [Fact]
    public void TryReserve_OctetsWithinTheBudget_ChargesThemAndReportsTheRemainder()
    {
        // Arrange
        var budget = new EmailAttachmentTextRunBudget(1000);

        // Act
        var reserved = budget.TryReserve(400);

        // Assert
        Assert.True(reserved);
        Assert.Equal(600, budget.RemainingOctets);
        Assert.False(budget.IsExhausted);
    }

    /// <summary>Reserving exactly the remainder is affordable, and leaves the pass with nothing to spend afterwards.</summary>
    [Fact]
    public void TryReserve_ExactlyTheRemainder_IsChargedAndLeavesTheBudgetExhausted()
    {
        // Arrange
        var budget = new EmailAttachmentTextRunBudget(1000);

        // Act
        var reserved = budget.TryReserve(1000);

        // Assert
        Assert.True(reserved);
        Assert.Equal(0, budget.RemainingOctets);
        Assert.True(budget.IsExhausted);
    }

    /// <summary>
    /// A refusal ends the pass rather than only the attachment. Leaving the remainder in place would let the walk go on
    /// to smaller attachments behind the refused one, which spends a ceiling somebody set as a ceiling and reads the
    /// mailbox out of order; the configured ordering of the three ceilings is what makes zeroing safe, because a message
    /// can then never be permanently unaffordable to a run that starts full.
    /// </summary>
    [Fact]
    public void TryReserve_MoreOctetsThanRemain_IsRefusedAndEndsThePass()
    {
        // Arrange
        var budget = new EmailAttachmentTextRunBudget(1000);
        budget.TryReserve(900);

        // Act
        var reserved = budget.TryReserve(200);

        // Assert
        Assert.False(reserved);
        Assert.Equal(0, budget.RemainingOctets);
        Assert.True(budget.IsExhausted);
    }

    /// <summary>An empty part costs nothing, and asking for nothing must not be read as running out.</summary>
    [Fact]
    public void TryReserve_NoOctets_IsChargedAndLeavesTheBudgetWhereItWas()
    {
        // Arrange
        var budget = new EmailAttachmentTextRunBudget(1000);

        // Act
        var reserved = budget.TryReserve(0);

        // Assert
        Assert.True(reserved);
        Assert.Equal(1000, budget.RemainingOctets);
    }

    /// <summary>A deployment configured to read nothing reads nothing rather than reading the first attachment free.</summary>
    [Fact]
    public void IsExhausted_ABudgetOfNothing_IsExhaustedBeforeAnythingIsRead()
    {
        // Act
        var budget = new EmailAttachmentTextRunBudget(0);

        // Assert
        Assert.True(budget.IsExhausted);
        Assert.False(budget.TryReserve(1));
    }

    /// <summary>A negative ceiling and a negative cost are both arithmetic nobody meant.</summary>
    [Fact]
    public void Construction_ANegativeCeilingOrCost_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmailAttachmentTextRunBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmailAttachmentTextRunBudget(1000).TryReserve(-1));
    }
}
