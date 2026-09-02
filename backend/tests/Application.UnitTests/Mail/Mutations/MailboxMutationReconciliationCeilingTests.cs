// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

/// <summary>Covers the budget the attribution read applies, which belongs to one value of one occurrence.</summary>
/// <remarks>
/// A record this budget drops is a change MailFathom made that reconciliation then credits to the mailbox owner and
/// reacts to, which is the loop the read exists to prevent. The number is stated here rather than derived from
/// anything, because everything it could be derived from has a different meaning: it is neither the size of a window
/// nor the count of the values a <c>FLAGS</c> response reports, and expressing it as either is what let the two earlier
/// shapes of this ceiling starve a record that explained something.
/// <see cref="Synchronization.Reconciliation.MailboxReconcilerTests" /> covers what
/// spending it in the wrong terms costs; what is stated here is the number.
/// </remarks>
public sealed class MailboxMutationReconciliationCeilingTests
{
    /// <summary>A reading is settled against the newest store of that value, with room for the ones behind it.</summary>
    [Fact]
    public void MaximumFlagChangeRecordsPerValue_IsTheRecentHistoryOfOneValue()
    {
        // Act
        var perValue = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerValue;

        // Assert
        Assert.Equal(5, perValue);
    }
}
