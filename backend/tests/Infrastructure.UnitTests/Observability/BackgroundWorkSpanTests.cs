// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Vectorization;
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
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
        {
            attempt.Ended(Attempt(JobExecutionOutcome.Succeeded, attemptCount: 3));
        }

        // Assert
        var span = this.Published(JobQueueTelemetry.AttemptSpanName, JobQueueTelemetry.AttemptNumberTagName, 3);

        Assert.Equal(JobType.ClassifyEmailSpam.Name, span.GetTagItem(JobQueueTelemetry.JobTypeTagName));
        Assert.Equal("succeeded", span.GetTagItem(JobQueueTelemetry.OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>
    /// The queue is a break in a trace rather than a tree, so the attempt is its own trace with a link back to the
    /// work that enqueued it — which is a cause hours earlier reached in one step rather than searched for in logs.
    /// </summary>
    [Fact]
    public void BeginAttempt_AJobEnqueuedInsideATrace_LinksTheAttemptToThatTrace()
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();
        var enqueued = JobTraceContext.FromTraceParent(
            "00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01",
            traceState: null);

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueued))
        {
            attempt.Ended(Attempt(JobExecutionOutcome.Succeeded, attemptCount: 21));
        }

        // Assert
        var span = this.Published(JobQueueTelemetry.AttemptSpanName, JobQueueTelemetry.AttemptNumberTagName, 21);
        var link = Assert.Single(span.Links);

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", link.Context.TraceId.ToHexString());
        Assert.Equal("1a2b3c4d5e6f7081", link.Context.SpanId.ToHexString());
        Assert.NotEqual(link.Context.TraceId, span.TraceId);
    }

    /// <summary>Every row written before the column existed carries none, and an attempt at one is a span without a link.</summary>
    [Fact]
    public void BeginAttempt_AJobWhoseRowRecordsNoTrace_PublishesTheAttemptWithNoLink()
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
        {
            attempt.Ended(Attempt(JobExecutionOutcome.Succeeded, attemptCount: 22));
        }

        // Assert
        Assert.Empty(this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.AttemptNumberTagName,
            22).Links);
    }

    /// <summary>An attempt the work itself failed is an error on the span, whichever of the three endings it reached.</summary>
    [Theory]
    [InlineData(JobExecutionOutcome.HandlerFailed, "handler_failed", 11)]
    [InlineData(JobExecutionOutcome.HandlerMissing, "handler_missing", 12)]
    [InlineData(JobExecutionOutcome.TimedOut, "timed_out", 13)]
    public void BeginAttempt_AnAttemptTheWorkFailed_PublishesTheEndingItReachedAsAnError(
        JobExecutionOutcome outcome,
        string expected,
        int attemptCount)
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
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

    /// <summary>An attempt that stopped without finishing carries the ending and no error.</summary>
    /// <remarks>
    /// A rolling deployment releases every attempt in flight and a reclaimed lease ends one the same way, so marking
    /// either as an error would put a wave of failed job traces in front of an operator on every ordinary restart. Which
    /// of the two it was is on the outcome tag, where a query can still find them.
    /// </remarks>
    [Theory]
    [InlineData(JobExecutionOutcome.ReleasedForShutdown, "released_for_shutdown", 14)]
    [InlineData(JobExecutionOutcome.LeaseLost, "lease_lost", 15)]
    public void BeginAttempt_AnAttemptThatStoppedWithoutFinishing_PublishesTheEndingWithoutAnError(
        JobExecutionOutcome outcome,
        string expected,
        int attemptCount)
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
        {
            attempt.Ended(Attempt(outcome, attemptCount));
        }

        // Assert
        var span = this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.AttemptNumberTagName,
            attemptCount);

        Assert.Equal(expected, span.GetTagItem(JobQueueTelemetry.OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
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
        using (telemetry.BeginAttempt(JobType.RunScheduledMailRules, enqueuedTrace: null))
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

    /// <summary>The attempt that gave up on a job carries what the queue classified the failure as.</summary>
    /// <remarks>
    /// The classification is what separates a defect somebody has to fix from a dependency that stayed broken, and the
    /// attempt that dead-lettered the job is the one span where an operator can still read it — nothing afterwards
    /// attempts the job again.
    /// </remarks>
    [Theory]
    [InlineData(JobFailureClassification.Transient, "transient", 21)]
    [InlineData(JobFailureClassification.Permanent, "permanent", 22)]
    public void BeginAttempt_AnAttemptThatDeadLetteredTheJob_PublishesTheClassificationThatEndedIt(
        JobFailureClassification classification,
        string expected,
        int attemptCount)
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
        {
            attempt.Ended(Attempt(
                JobExecutionOutcome.HandlerFailed,
                attemptCount,
                new JobAttemptFailure(
                    JobFailureRecord.Create(classification, "PayloadUnreadable"),
                    JobFailureDisposition.DeadLettered)));
        }

        // Assert
        var span = this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.AttemptNumberTagName,
            attemptCount);

        Assert.Equal(expected, span.GetTagItem(JobQueueTelemetry.FailureTagName));
    }

    /// <summary>A failure the queue will attempt again carries no classification, which is what makes the tag mean something.</summary>
    /// <remarks>
    /// The control for the theory above: a tag written on every failed attempt would say nothing about which job the
    /// queue has stopped working on, and an assertion that it is present would pass just as well.
    /// </remarks>
    [Fact]
    public void BeginAttempt_AFailureTheQueueWillTryAgain_PublishesNoClassification()
    {
        // Arrange
        var telemetry = new JobQueueTelemetry();

        // Act
        using (var attempt = telemetry.BeginAttempt(JobType.ClassifyEmailSpam, enqueuedTrace: null))
        {
            attempt.Ended(Attempt(
                JobExecutionOutcome.HandlerFailed,
                attemptCount: 23,
                new JobAttemptFailure(
                    JobFailureRecord.Create(JobFailureClassification.Transient, "TransportFailure"),
                    JobFailureDisposition.RetryScheduled)));
        }

        // Assert
        var span = this.Published(
            JobQueueTelemetry.AttemptSpanName,
            JobQueueTelemetry.AttemptNumberTagName,
            23);

        Assert.Null(span.GetTagItem(JobQueueTelemetry.FailureTagName));
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

    /// <summary>A turn the worker abandoned says so, rather than being published as an ordinary ending.</summary>
    /// <remarks>
    /// The worker isolates a cancellation on shutdown, a concurrency conflict its retries could not resolve, and an
    /// unexpected failure so the messages behind this one are still embedded, and none of those reaches an outcome the
    /// telemetry could name. What separates them from a turn that finished is the status alone.
    /// </remarks>
    [Fact]
    public void BeginMessage_ATurnThatNeverReportedAResult_PublishesAnErrorAndNoOutcome()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();

        // Act
        using (telemetry.BeginMessage())
        {
            // Disposed without a result, which is what each of the worker's three catch paths produces.
        }

        // Assert
        var span = this.Abandoned(EmailEmbeddingTelemetry.MessageSpanName, EmailEmbeddingTelemetry.OutcomeTagName);

        Assert.Null(span.GetTagItem(EmailEmbeddingTelemetry.PassageCountTagName));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>A pass the worker abandoned says so, on the same terms as a turn.</summary>
    [Fact]
    public void BeginPass_APassThatNeverReportedAResult_PublishesAnErrorAndNoOutcome()
    {
        // Arrange
        var telemetry = new EmailEmbeddingBackfillTelemetry();

        // Act
        using (telemetry.BeginPass())
        {
            // Disposed without a result, which is what each of the worker's three catch paths produces.
        }

        // Assert
        var span = this.Abandoned(
            EmailEmbeddingBackfillTelemetry.PassSpanName,
            EmailEmbeddingBackfillTelemetry.OutcomeTagName);

        Assert.Null(span.GetTagItem(EmailEmbeddingBackfillTelemetry.PassageCountTagName));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
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

    private static JobExecutionResult Attempt(
        JobExecutionOutcome outcome,
        int attemptCount,
        JobAttemptFailure? failure = null)
    {
        return new JobExecutionResult(
            JobId.Create(Guid.CreateVersion7()),
            JobType.ClassifyEmailSpam,
            attemptCount,
            outcome,
            TimeSpan.FromSeconds(1))
        {
            AttemptFailure = failure,
        };
    }

    private static StoredEmailEmbeddingBackfillResult Sweep(
        StoredEmailEmbeddingBackfillOutcome outcome,
        int embeddedChunkCount) => new(
        outcome,
        ChunkedEmailCount: 2,
        EmbeddedEmailCount: 2,
        embeddedChunkCount,
        CallBudgetExhaustedEmailCount: 0,
        OwnerSpendCeilingEmailCount: 0,
        OwnerSpendPeriodEndsAt: null,
        OutstandingEmailCountAtSweepStart: 9,
        Failure: null,
        SpendPeriodEndsAt: null);

    /// <summary>Selects this test's own span out of whatever the shared source published while it ran.</summary>
    private Activity Published(string spanName, string tagName, object expected) => Assert.Single(
        this.published,
        activity => StringComparer.Ordinal.Equals(activity.OperationName, spanName)
            && Equals(activity.GetTagItem(tagName), expected));

    /// <summary>Selects the one span of its name that reported no outcome, which is what an abandoned unit of work leaves.</summary>
    /// <remarks>
    /// An abandoned span carries no tag to select it by, so the absence of the outcome is what identifies it — and one
    /// test per span name reaches this, which is what keeps the selection single.
    /// </remarks>
    private Activity Abandoned(string spanName, string outcomeTagName) => Assert.Single(
        this.published,
        activity => StringComparer.Ordinal.Equals(activity.OperationName, spanName)
            && activity.GetTagItem(outcomeTagName) is null);
}
