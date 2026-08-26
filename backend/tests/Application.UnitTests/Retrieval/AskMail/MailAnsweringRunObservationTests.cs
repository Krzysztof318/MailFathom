// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers the record a run fills in as it goes, and what it says before it has.</summary>
/// <remarks>
/// The defaults are the part worth proving: an observation nobody completed is read by the span and by the durable
/// record exactly as one that was, so what it says about a run that ended in a way nothing named has to be honest.
/// </remarks>
public sealed class MailAnsweringRunObservationTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A run nobody completed describes one that ended in a way nothing named, and read nothing.</summary>
    [Fact]
    public void Constructor_ARunThatHasNotBegun_ReportsAnUnnamedEndingAndNoRetrieval()
    {
        // Act
        var observation = Observation();

        // Assert
        Assert.Equal(
            (MailAnsweringRunOutcome.Failed, MailAnsweringRetrievalReport.Empty, StartedAt, string.Empty),
            (observation.Outcome, observation.Retrieval, observation.CompletedAt, observation.ChatEndpointAlias));
        Assert.Empty(observation.CitedEmailIds);
    }

    [Fact]
    public void RecordComposition_AProfileAndAPolicy_KeepsBoth()
    {
        // Arrange
        var observation = Observation();

        // Act
        observation.RecordComposition("answering", "0a1b2c3d4e5f");

        // Assert
        Assert.Equal(("answering", "0a1b2c3d4e5f"), (observation.ChatEndpointAlias, observation.InstructionsVersion));
    }

    /// <summary>A record naming no profile or no policy would explain an answer by naming nothing that produced it.</summary>
    [Theory]
    [InlineData("", "a-version")]
    [InlineData("   ", "a-version")]
    [InlineData("an-endpoint", "")]
    [InlineData("an-endpoint", "   ")]
    public void RecordComposition_ABlankHalf_IsRefused(string endpointAlias, string instructionsVersion)
    {
        // Arrange
        var observation = Observation();

        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            observation.RecordComposition(endpointAlias, instructionsVersion));
    }

    [Fact]
    public void RecordOutcome_AnEnding_KeepsItWithWhatTheAnswerCited()
    {
        // Arrange
        var observation = Observation();
        var cited = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        observation.RecordOutcome(MailAnsweringRunOutcome.Answered, [cited], StartedAt.AddSeconds(9));

        // Assert
        Assert.Equal(
            (MailAnsweringRunOutcome.Answered, StartedAt.AddSeconds(9)),
            (observation.Outcome, observation.CompletedAt));
        Assert.Equal([cited], observation.CitedEmailIds);
    }

    [Fact]
    public void Constructor_WithoutAScope_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7()),
            null!,
            StartedAt));
    }

    private static MailAnsweringRunObservation Observation() => new(
        MailAnsweringRunId.Create(Guid.CreateVersion7(StartedAt)),
        MailboxScope.Create(SyntheticMailOwner.Deployment, [MailAccountId.Create("work")], []),
        StartedAt);
}
