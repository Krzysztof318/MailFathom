// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the spans opened by the work no request causes, which is what keeps it out of a trace as orphans.</summary>
/// <remarks>
/// <para>
/// A job attempt, a message being embedded, and a backfill pass are each dispatched by an interval or by a queue rather
/// than by a caller. Without a span apiece, everything they issue — the database commands, and for the two embedding
/// ones the provider call as well — reaches a trace store with no parent, which is the shape this class exists to stop
/// regressing. So what is asserted is the span's name, the tags that make it readable, and the status that separates a
/// unit of work that ended from one that never reported.
/// </para>
/// <para>
/// The listener is the real activity source narrowed to the three names, because the rule under test is about what an
/// exporter would receive. Each span is then selected out of what the shared source published by a value this test
/// supplied, since another class publishing the same span at the same moment would otherwise decide the assertion.
/// </para>
/// </remarks>
public sealed class BackgroundWorkSpanTests : IDisposable
{
    private static readonly string[] WatchedSpanNames =
    [
        JobQueueTelemetry.AttemptSpanName,
        EmailEmbeddingTelemetry.MessageSpanName,
        EmailEmbeddingBackfillTelemetry.PassSpanName,
    ];

    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public BackgroundWorkSpanTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (WatchedSpanNames.Contains(activity.OperationName, StringComparer.Ordinal))
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>An attempt is published under its own name with the job type, the attempt number, and how it ended.</summary>
    [Fact]
    public void BeginAttempt_AnAttemptThatSucceeded_PublishesTheJobTypeTheAttemptNumberAndTheOutcome()
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam))
        {
            attempt.Ended(Attempt(JobExecutionOutcome.Succeeded, attemptCount: 3));
        }

        // Assert
        var span = this.Published(JobQueueTelemetry.AttemptSpanName, JobQueueTelemetry.AttemptNumberTagName, 3);

        Assert.Equal(JobType.ClassifyEmailSpam.Name, span.GetTagItem(JobQueueTelemetry.JobTypeTagName));
        Assert.Equal("succeeded", span.GetTagItem(JobQueueTelemetry.OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>An attempt that did not succeed is an error on the span, whichever of the endings it reached.</summary>
    [Theory]
    [InlineData(JobExecutionOutcome.HandlerFailed, "handler_failed", 11)]
    [InlineData(JobExecutionOutcome.HandlerMissing, "handler_missing", 12)]
    [InlineData(JobExecutionOutcome.TimedOut, "timed_out", 13)]
    [InlineData(JobExecutionOutcome.ReleasedForShutdown, "released_for_shutdown", 14)]
    [InlineData(JobExecutionOutcome.LeaseLost, "lease_lost", 15)]
    public void BeginAttempt_AnAttemptThatDidNotSucceed_PublishesTheEndingItReachedAsAnError(
        JobExecutionOutcome outcome,
        string expected,
        int attemptCount)
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam))
        {
            attempt.Ended(Attempt(outcome, attemptCount));
        }

        // Assert
        var span = this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.AttemptNumberTagName,
            attemptCount);

        Assert.Equal(expected, span.GetTagItem(JobQueueTelemetry.OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>An attempt that reached no result at all says so, rather than being published as an ordinary ending.</summary>
    /// <remarks>
    /// The one case with no outcome to name: every word this queue publishes is a decision the executor reached, so a
    /// dispatch that never got that far is an error and no outcome rather than an invented one.
    /// </remarks>
    [Fact]
    public void BeginAttempt_AnAttemptThatNeverReportedAResult_PublishesAnErrorAndNoOutcome()
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (telemetry.BeginAttempt(JobType.RunScheduledMailRules))
        {
            // Disposed without a result, which is what a dispatch that could not be composed produces.
        }

        // Assert
        var span = this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.JobTypeTagName,
            JobType.RunScheduledMailRules.Name);

        Assert.Null(span.GetTagItem(JobQueueTelemetry.OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>One message's turn at being embedded carries how it ended and how many passages it gave a vector.</summary>
    [Fact]
    public void BeginMessage_ATurnThatEmbedded_PublishesTheOutcomeAndThePassageCount()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();

        // Act
        using (var turn = telemetry.BeginMessage())
        {
            turn.Ended(StoredEmailEmbeddingRun.Embedded(embeddedChunkCount: 41));
        }

        // Assert
        var span = this.Published(
            EmailEmbeddingTelemetry.MessageSpanName,
            EmailEmbeddingTelemetry.PassageCountTagName,
            41);

        Assert.Equal("embedded", span.GetTagItem(EmailEmbeddingTelemetry.OutcomeTagName));
        Assert.Equal("none", span.GetTagItem(EmailEmbeddingTelemetry.FailureTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>A provider that refused is the one ending that makes the turn an error, and it names the refusal.</summary>
    [Fact]
    public void BeginMessage_ATurnAProviderRefused_PublishesTheFailureAsAnError()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();

        // Act
        using (var turn = telemetry.BeginMessage())
        {
            turn.Ended(StoredEmailEmbeddingRun.ProviderFailed(
                embeddedChunkCount: 43,
                EmbeddingGenerationFailure.RateLimited));
        }

        // Assert
        var span = this.Published(
            EmailEmbeddingTelemetry.MessageSpanName,
            EmailEmbeddingTelemetry.PassageCountTagName,
            43);

        Assert.Equal("provider_failed", span.GetTagItem(EmailEmbeddingTelemetry.OutcomeTagName));
        Assert.Equal("rate_limited", span.GetTagItem(EmailEmbeddingTelemetry.FailureTagName));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>A backfill pass carries what it moved and whether it completed a generation while it ran.</summary>
    [Fact]
    public void BeginPass_APassThatSweptAndSwitched_PublishesWhatItMovedAndThatItSwitched()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();

        // Act
        using (var pass = telemetry.BeginPass())
        {
            pass.Ended(new EmbeddingGenerationUpkeepResult(
                Sweep(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, embeddedChunkCount: 47),
                EmbeddingGenerationTransition.Switched,
                RemovedSupersededVectorCount: 5));
        }

        // Assert
        var span = this.Published(
            EmailEmbeddingBackfillTelemetry.PassSpanName,
            EmailEmbeddingBackfillTelemetry.PassageCountTagName,
            47);

        Assert.Equal("sweep_completed", span.GetTagItem(EmailEmbeddingBackfillTelemetry.OutcomeTagName));
        Assert.Equal(2, span.GetTagItem(EmailEmbeddingBackfillTelemetry.ChunkedEmailCountTagName));
        Assert.Equal(2, span.GetTagItem(EmailEmbeddingBackfillTelemetry.EmbeddedEmailCountTagName));
        Assert.Equal(true, span.GetTagItem(EmailEmbeddingBackfillTelemetry.GenerationSwitchedTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>None of the three spans is named after anything a mailbox holds, whatever the work touched.</summary>
    /// <remarks>
    /// The names are asserted rather than only the tags, because a span name is where a subsystem would most plausibly
    /// have reached for the identity of the thing it was working on — a job's key, or the message being embedded.
    /// </remarks>
    [Fact]
    public void EveryBackgroundSpan_WhateverTheWorkTouched_IsNamedAfterTheOperationAlone()
    {
        // Arrange

        // Act

        // Assert
        Assert.Equal("run_job", JobQueueTelemetry.AttemptSpanName);
        Assert.Equal("embed_stored_email", EmailEmbeddingTelemetry.MessageSpanName);
        Assert.Equal("backfill_email_embeddings", EmailEmbeddingBackfillTelemetry.PassSpanName);
    }

    private static JobExecutionResult Attempt(JobExecutionOutcome outcome, int attemptCount) => new(
        JobId.Create(Guid.CreateVersion7()),
        JobType.ClassifyEmailSpam,
        attemptCount,
        outcome,
        TimeSpan.FromSeconds(1));

    private static StoredEmailEmbeddingBackfillResult Sweep(
        StoredEmailEmbeddingBackfillOutcome outcome,
        int embeddedChunkCount) => new(
        outcome,
        ChunkedEmailCount: 2,
        EmbeddedEmailCount: 2,
        embeddedChunkCount,
        CallBudgetExhaustedEmailCount: 0,
        OutstandingEmailCountAtSweepStart: 9,
        Failure: null,
        SpendPeriodEndsAt: null);

    /// <summary>Selects this test's own span out of whatever the shared source published while it ran.</summary>
    private Activity Published(string spanName, string tagName, object expected) => Assert.Single(
        this.published,
        activity => StringComparer.Ordinal.Equals(activity.OperationName, spanName)
            && Equals(activity.GetTagItem(tagName), expected));
}
