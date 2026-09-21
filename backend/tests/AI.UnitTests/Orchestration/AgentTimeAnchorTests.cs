// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Search;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the one line every agent resolving a relative period states its anchor with.</summary>
/// <remarks>
/// The format is asserted literally rather than composed from the same expression the production code uses, because
/// what a model is being handed is the text itself: a reword that dropped the weekday or added the offset would pass a
/// test that rebuilt the line, and would change what every one of these agents resolves.
/// </remarks>
public sealed class AgentTimeAnchorTests
{
    private static readonly DateTimeOffset AskedAt = new(2026, 9, 9, 14, 30, 0, TimeSpan.FromHours(2));

    /// <summary>The wall clock and the weekday, and no offset, because the answer is asked for in local time as well.</summary>
    [Fact]
    public void Stated_AnInstant_NamesTheWallClockAndTheWeekdayWithoutAnOffset()
    {
        // Act
        var anchor = AgentTimeAnchor.Stated(AskedAt);

        // Assert
        Assert.Equal("Now, where this text was written: 2026-09-09T14:30 (Wednesday).", anchor);
    }

    /// <summary>The offset decides which day the anchor names, so the same instant in two zones is two anchors.</summary>
    [Fact]
    public void Stated_OneInstantReadInTwoZones_NamesEachZonesOwnDay()
    {
        // Arrange
        var instant = new DateTimeOffset(2026, 9, 9, 21, 30, 0, TimeSpan.Zero);

        // Act
        var warsaw = AgentTimeAnchor.Stated(instant.ToOffset(TimeSpan.FromHours(2)));
        var tokyo = AgentTimeAnchor.Stated(instant.ToOffset(TimeSpan.FromHours(9)));

        // Assert
        Assert.Equal("Now, where this text was written: 2026-09-09T23:30 (Wednesday).", warsaw);
        Assert.Equal("Now, where this text was written: 2026-09-10T06:30 (Thursday).", tokyo);
    }

    /// <summary>One wording rather than one per operation, so every agent resolving a period is told the same thing the same way.</summary>
    [Fact]
    public void Stated_TheOperationsThatAskForAnAnchor_AllStateTheSameLine()
    {
        // Arrange
        var expected = AgentTimeAnchor.Stated(AskedAt);

        // Act
        var turns = new[]
        {
            MailSearchPhraseInstructions.ComposeReadingTurn("mail from last week", AskedAt),
            CalendarEventExtractionInstructions.ComposeDescriptionTurn("lunch on Thursday", AskedAt),
            CalendarEventExtractionInstructions.ComposeMailTurn("Survey", AskedAt, ["Thursday at ten works."]),
            DiscoveryPlanningInstructions.ComposePlanningTurn(
                "what did we agree this week",
                MailboxScope.Create([MailAccountId.Create("primary")], []),
                AskedAt,
                EmailKnowledgeBounds.Default),
        };

        // Assert
        Assert.All(turns, turn => Assert.Contains(expected, turn, StringComparison.Ordinal));
    }
}
