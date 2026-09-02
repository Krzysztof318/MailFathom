// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Actions;

public sealed class SpamActionResultTests
{
    private static readonly MailboxMutationRecordId Record =
        MailboxMutationRecordId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000f00d"));

    [Fact]
    public void NotActedOn_TheRequestedOutcome_IsRefusedBecauseNothingWasRequested()
    {
        // Act
        var refusal = () => SpamActionResult.NotActedOn(SpamActionOutcome.Requested);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(refusal);
    }

    [Fact]
    public void NotActedOn_AReason_CarriesNoRecord()
    {
        // Act
        var result = SpamActionResult.NotActedOn(SpamActionOutcome.PreviouslyFiled);

        // Assert
        Assert.Equal(SpamActionOutcome.PreviouslyFiled, result.Outcome);
        Assert.Null(result.MarkedReadRecordId);
        Assert.Null(result.FiledRecordId);
    }

    [Fact]
    public void Requested_OneOfTheTwoChanges_ReportsThatChangeAndNoOther()
    {
        // Act
        var result = SpamActionResult.Requested(markedReadRecordId: null, Record);

        // Assert
        Assert.Equal(SpamActionOutcome.Requested, result.Outcome);
        Assert.Equal(Record, result.FiledRecordId);
        Assert.Null(result.MarkedReadRecordId);
    }

    [Fact]
    public void Requested_NeitherChange_IsRefusedBecauseThatIsNothingToChange()
    {
        // Act
        var refusal = () => SpamActionResult.Requested(markedReadRecordId: null, filedRecordId: null);

        // Assert
        Assert.Throws<ArgumentException>(refusal);
    }
}
