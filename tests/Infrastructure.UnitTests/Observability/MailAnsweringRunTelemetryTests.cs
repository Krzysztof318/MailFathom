// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the span one answering run publishes: what it is called, what it carries, and what it never carries.</summary>
/// <remarks>
/// It listens to the real activity source, because the rule under test is about what an exporter would receive. The
/// listener is narrowed to this span's own name, so a run published by another test class at the same moment is not
/// mistaken for this one — the source is the process's and is shared by everything MailFathom publishes.
/// </remarks>
public sealed class MailAnsweringRunTelemetryTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public MailAnsweringRunTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == MailAnsweringRunTelemetry.SpanName)
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>What a clean run publishes: the counts either side of the filter, what reached the model, and how it ended.</summary>
    [Fact]
    public void BeginRun_ARunThatAnswered_PublishesItsCountsAndItsEnding()
    {
        // Arrange
        var telemetry = new MailAnsweringRunTelemetry();
        var observation = Run(
            new MailAnsweringRetrievalReport([Passage(), Passage()], 9, 4, MailAnsweringRunDegradation.None),
            MailAnsweringRunOutcome.Answered);

        // Act
        using (telemetry.BeginRun(observation))
        {
        }

        // Assert
        var span = this.PublishedRun(observation);

        Assert.Equal(
            [
                ("mailfathom.answering.endpoint", "answering"),
                ("mailfathom.answering.instructions_version", "0a1b2c3d4e5f"),
                ("mailfathom.answering.candidates", "9"),
                ("mailfathom.answering.candidates.relevant", "4"),
                ("mailfathom.answering.passages", "2"),
                ("mailfathom.answering.outcome", "Answered"),
                ("mailfathom.answering.degradation", "None"),
            ],
            span.TagObjects.Select(tag => (tag.Key, tag.Value?.ToString())));
    }

    /// <summary>A degraded run is told apart from a clean one by one bounded tag rather than by a log message.</summary>
    [Theory]
    [InlineData(MailAnsweringRunDegradation.RetrievalCeilingReached, "RetrievalCeilingReached")]
    [InlineData(MailAnsweringRunDegradation.RelevanceFilterFellBack, "RelevanceFilterFellBack")]
    [InlineData(
        MailAnsweringRunDegradation.RetrievalCeilingReached | MailAnsweringRunDegradation.RelevanceFilterFellBack,
        "RetrievalCeilingReached, RelevanceFilterFellBack")]
    public void BeginRun_ADegradedRun_PublishesWhichWaysItDegraded(
        MailAnsweringRunDegradation degradation,
        string expected)
    {
        // Arrange
        var telemetry = new MailAnsweringRunTelemetry();
        var observation = Run(
            new MailAnsweringRetrievalReport([Passage()], 1, 1, degradation),
            MailAnsweringRunOutcome.Answered);

        // Act
        using (telemetry.BeginRun(observation))
        {
        }

        // Assert
        Assert.Equal(expected, this.PublishedRun(observation).GetTagItem("mailfathom.answering.degradation"));
    }

    /// <summary>A run that failed is the one most worth attributing, so it is published exactly as one that answered.</summary>
    [Fact]
    public void BeginRun_ARunThatFailed_PublishesTheEndingItReached()
    {
        // Arrange
        var telemetry = new MailAnsweringRunTelemetry();
        var observation = Run(MailAnsweringRetrievalReport.Empty, MailAnsweringRunOutcome.ProviderFailed);

        // Act
        using (telemetry.BeginRun(observation))
        {
        }

        // Assert
        Assert.Equal("ProviderFailed", this.PublishedRun(observation).GetTagItem("mailfathom.answering.outcome"));
    }

    /// <summary>
    /// The one rule the telemetry page states as a cardinality rule as much as a privacy one: no message identifier and
    /// no mail reaches a span, whatever the run retrieved.
    /// </summary>
    [Fact]
    public void BeginRun_ARunThatRetrievedMail_PublishesNoIdentifierAndNoContent()
    {
        // Arrange
        var telemetry = new MailAnsweringRunTelemetry();
        var passage = Passage();
        var observation = Run(
            new MailAnsweringRetrievalReport([passage], 1, 1, MailAnsweringRunDegradation.None),
            MailAnsweringRunOutcome.Answered);

        // Act
        using (telemetry.BeginRun(observation))
        {
        }

        // Assert
        var values = this.PublishedRun(observation)
            .TagObjects
            .Select(tag => tag.Value?.ToString() ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(passage.StoredEmailId.Value.ToString(), values);
        Assert.DoesNotContain(passage.Text, values);
        Assert.DoesNotContain(passage.Subject, values);
        Assert.DoesNotContain(passage.FolderAlias.Value, values);
    }

    private static EmailKnowledgePassage Passage() => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = Account,
        FolderAlias = MailFolderAlias.Create("inbox"),
        Subject = "Quarterly invoice",
        ReceivedAt = StartedAt,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = "the invoice is attached",
    };

    private static MailAnsweringRunObservation Run(
        MailAnsweringRetrievalReport retrieval,
        MailAnsweringRunOutcome outcome)
    {
        var observation = new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7(StartedAt)),
            MailboxScope.Create([Account], []),
            StartedAt);

        observation.RecordComposition("answering", "0a1b2c3d4e5f");
        observation.RecordRetrieval(retrieval);
        observation.RecordOutcome(outcome, [], StartedAt.AddSeconds(9));

        return observation;
    }

    /// <summary>Selects this test's own run out of whatever the shared source published while it ran.</summary>
    /// <remarks>
    /// By the ending it recorded and the counts it carried rather than by being the only one, because another class
    /// publishing a run at the same moment would otherwise decide this assertion.
    /// </remarks>
    private Activity PublishedRun(MailAnsweringRunObservation observation) => Assert.Single(
        this.published,
        activity => (string?)activity.GetTagItem("mailfathom.answering.outcome") == observation.Outcome.ToString()
            && (int?)activity.GetTagItem("mailfathom.answering.candidates") == observation.Retrieval.CandidateCount
            && (string?)activity.GetTagItem("mailfathom.answering.degradation")
                == observation.Retrieval.Degradation.ToString());
}
