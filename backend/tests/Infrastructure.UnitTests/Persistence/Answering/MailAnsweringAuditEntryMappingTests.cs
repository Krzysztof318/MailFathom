// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Answering;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Answering;

/// <summary>Covers the row one answering entry is kept as, and what a row this build cannot interpret costs.</summary>
/// <remarks>
/// The unreadable case is the reason the mapping reports rather than throws: this record is read a page at a time and
/// paginated by position, so a row a later build wrote would otherwise fail the page holding it and every page after it.
/// </remarks>
public sealed class MailAnsweringAuditEntryMappingTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToEntity_AnEntry_RoundTripsBackIntoTheSameEntry()
    {
        // Arrange
        var entry = Entry(
            MailAnsweringRunOutcome.Answered,
            MailAnsweringRunDegradation.RetrievalCeilingReached | MailAnsweringRunDegradation.RelevanceFilterFellBack);

        // Act
        var rebuilt = MailAnsweringAuditEntryMapping.TryToEntry(
            MailAnsweringAuditEntryMapping.ToEntity(entry),
            out var read);

        // Assert
        Assert.True(rebuilt);
        Assert.NotNull(read);

        // The emails are compared apart from the entry, because a record's generated equality compares the list by
        // reference and would pass whatever it held.
        Assert.Equal(entry with { Emails = [] }, read with { Emails = [] });
        Assert.Equal(entry.Emails, read.Emails);
    }

    /// <summary>The emails come back in the order the run reached them however the rows arrive.</summary>
    [Fact]
    public void TryToEntry_RowsOutOfOrder_ReadsTheEmailsBackInThePositionOrder()
    {
        // Arrange
        var entity = MailAnsweringAuditEntryMapping.ToEntity(
            Entry(MailAnsweringRunOutcome.Answered, MailAnsweringRunDegradation.None));
        var reversed = entity.Emails.Reverse().ToArray();
        entity.Emails.Clear();

        foreach (var email in reversed)
        {
            entity.Emails.Add(email);
        }

        // Act
        MailAnsweringAuditEntryMapping.TryToEntry(entity, out var read);

        // Assert
        Assert.Equal([0, 1], read!.Emails.Select(email => email.Position));
    }

    /// <summary>An ending this build declares no member for is a row a later build wrote, and is left out rather than guessed at.</summary>
    [Theory]
    [InlineData("EndedSomehow", "None")]
    [InlineData("9", "None")]
    [InlineData("Answered", "ReadNothingAtAll")]
    [InlineData("Answered", "8")]
    public void TryToEntry_ARowNamingValuesThisBuildDoesNotDeclare_IsRefused(string outcome, string degradation)
    {
        // Arrange
        var entity = MailAnsweringAuditEntryMapping.ToEntity(
            Entry(MailAnsweringRunOutcome.Answered, MailAnsweringRunDegradation.None));
        entity.Outcome = outcome;
        entity.Degradation = degradation;

        // Act
        var rebuilt = MailAnsweringAuditEntryMapping.TryToEntry(entity, out var read);

        // Assert
        Assert.False(rebuilt);
        Assert.Null(read);
    }

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");

    private static MailAnsweringAuditEntry Entry(
        MailAnsweringRunOutcome outcome,
        MailAnsweringRunDegradation degradation) => new()
        {
            Id = MailAnsweringAuditEntryId.Create(Guid.CreateVersion7(StartedAt)),
            RunId = MailAnsweringRunId.Create(Guid.CreateVersion7(StartedAt)),
            Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
            Emails =
            [
                new MailAnsweringAuditedEmail(StoredEmailId.Create(EmailIdentityAt(1)), 0, WasCited: false),
                new MailAnsweringAuditedEmail(StoredEmailId.Create(EmailIdentityAt(2)), 1, WasCited: true),
            ],
            ChatEndpointAlias = "answering",
            InstructionsVersion = "0a1b2c3d4e5f",
            StartedAt = StartedAt,
            CompletedAt = StartedAt.AddSeconds(9),
            Outcome = outcome,
            Degradation = degradation,
        };
}
