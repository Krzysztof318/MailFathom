// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ThreadStates;

/// <summary>Covers the invariants a statement about a conversation is admitted under.</summary>
public sealed class ThreadStateEntryTests
{
    private static readonly StoredEmailId Source = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void Create_AStatementRestingOnAMessage_KeepsWhatItWasGiven()
    {
        // Arrange
        var due = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

        // Act
        var entry = ThreadStateEntry.Create(
            ThreadStateAspect.Commitment,
            "Karolina sends the revised figures.",
            [Source],
            "Karolina",
            due);

        // Assert
        Assert.Equal(ThreadStateAspect.Commitment, entry.Aspect);
        Assert.Equal("Karolina sends the revised figures.", entry.Text);
        Assert.Equal("Karolina", entry.OwedBy);
        Assert.Equal(due, entry.DueAt);
        Assert.Equal([Source], entry.Sources);
    }

    /// <summary>The thing the record exists to rule out: a statement about somebody's mail that cites none of it.</summary>
    [Fact]
    public void Create_AStatementCitingNothing_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() =>
            ThreadStateEntry.Create(ThreadStateAspect.Agreement, "They settled the price.", []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_AStatementSayingNothing_IsRefused(string text)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() =>
            ThreadStateEntry.Create(ThreadStateAspect.Agreement, text, [Source]));
    }

    [Fact]
    public void Create_AnAspectThisSystemDoesNotDerive_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThreadStateEntry.Create((ThreadStateAspect)42, "They settled the price.", [Source]));
    }

    /// <summary>An owner and a date belong to a commitment, so an agreement carrying one is a producer answering wrongly.</summary>
    [Fact]
    public void Create_AnAgreementNamingAnOwner_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() =>
            ThreadStateEntry.Create(ThreadStateAspect.Agreement, "They settled the price.", [Source], "Karolina"));
    }

    [Fact]
    public void Create_AnOpenQuestionCarryingADate_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() =>
            ThreadStateEntry.Create(
                ThreadStateAspect.OpenQuestion,
                "The upper limit is unsettled.",
                [Source],
                dueAt: new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>A commitment the conversation made without naming anybody is kept rather than refused.</summary>
    [Fact]
    public void Create_ACommitmentNamingNobody_KeepsTheStatementWithNoOwner()
    {
        // Act
        var entry = ThreadStateEntry.Create(
            ThreadStateAspect.Commitment,
            "The revised figures are sent this week.",
            [Source],
            "   ");

        // Assert
        Assert.Null(entry.OwedBy);
    }

    /// <summary>
    /// A long sentence shortens rather than losing the whole derivation, and it is bounded by what it says rather than
    /// by how the producer laid it out.
    /// </summary>
    [Fact]
    public void Create_ALongStatement_CollapsesItsWhitespaceAndShortensToTheBound()
    {
        // Arrange
        var wrapped = "They\n  settled  " + new string('a', ThreadStateEntry.MaximumTextLength);

        // Act
        var entry = ThreadStateEntry.Create(ThreadStateAspect.Agreement, wrapped, [Source]);

        // Assert
        Assert.Equal(ThreadStateEntry.MaximumTextLength, entry.Text.Length);
        Assert.StartsWith("They settled ", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ALongOwnerName_ShortensToItsOwnBound()
    {
        // Act
        var entry = ThreadStateEntry.Create(
            ThreadStateAspect.Commitment,
            "The figures are sent.",
            [Source],
            new string('n', ThreadStateEntry.MaximumOwedByLength + 40));

        // Assert
        Assert.Equal(ThreadStateEntry.MaximumOwedByLength, entry.OwedBy!.Length);
    }

    /// <summary>A producer citing the whole conversation is kept to what it relied on most.</summary>
    [Fact]
    public void Create_MoreSourcesThanAStatementMayCite_KeepsTheLeadingOnes()
    {
        // Arrange
        var sources = Enumerable
            .Range(0, ThreadStateEntry.MaximumSourceCount + 3)
            .Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))
            .ToArray();

        // Act
        var entry = ThreadStateEntry.Create(ThreadStateAspect.Agreement, "They settled the price.", sources);

        // Assert
        Assert.Equal(ThreadStateEntry.MaximumSourceCount, entry.Sources.Count);
        Assert.Equal(sources.Take(ThreadStateEntry.MaximumSourceCount), entry.Sources);
    }
}
