// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the spans a folder run's stages are published as: their names, where they sit, and how each ends.</summary>
/// <remarks>
/// The activity source is the process's, so a span another test class published at the same moment reaches this
/// listener too. Every assertion therefore reads the spans beneath one folder run this test opened, which no other
/// class can produce.
/// </remarks>
public sealed class MailSynchronizationPhaseSpanTests : IDisposable
{
    private const string OutcomeTagName = "mailfathom.mail.sync.outcome";

    private readonly MailSynchronizationTelemetry telemetry = new(new FakeTimeProvider());
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public MailSynchronizationPhaseSpanTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = this.published.Add,
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>The name an operator writes a filter against, one per stage a folder run passes through.</summary>
    [Theory]
    [InlineData(MailSynchronizationPhase.ResolveFolder, "resolve_mail_folder")]
    [InlineData(MailSynchronizationPhase.OpenSession, "open_mailbox_session")]
    [InlineData(MailSynchronizationPhase.DiscoverEmails, "discover_mailbox_emails")]
    [InlineData(MailSynchronizationPhase.FetchEmailBatch, "fetch_email_batch")]
    [InlineData(MailSynchronizationPhase.ReconcileFolder, "reconcile_mailbox_folder")]
    [InlineData(MailSynchronizationPhase.RefillDeferredContent, "refill_deferred_content")]
    public void BeginPhase_EachStage_PublishesItUnderTheNameOfTheWork(
        MailSynchronizationPhase phase,
        string expectedSpanName)
    {
        // Arrange
        var account = MailAccountId.Create("phase-name");

        // Act
        using var folderRun = this.telemetry.BeginFolderRun(account);
        using (var stage = this.telemetry.BeginPhase(phase, TestContext.Current.CancellationToken))
        {
            stage.Completed();
        }

        // Assert
        Assert.Equal(expectedSpanName, this.OnlyChildOfTheFolderRun().OperationName);
    }

    /// <summary>The nesting is what the stages are for: a run that got slower is attributable to the stage it slowed in.</summary>
    [Fact]
    public void BeginPhase_AStageInsideAFolderRun_PublishesBeneathIt()
    {
        // Arrange
        var account = MailAccountId.Create("phase-nesting");

        // Act
        Activity? folderRunSpan;

        using (var folderRun = this.telemetry.BeginFolderRun(account))
        {
            folderRunSpan = Activity.Current;

            using (var stage = this.telemetry.BeginPhase(
                MailSynchronizationPhase.OpenSession,
                TestContext.Current.CancellationToken))
            {
                stage.Completed();
            }

            folderRun.Synchronized("INBOX", storedEmailCount: 0, skippedEmailCount: 0);
        }

        // Assert
        Assert.NotNull(folderRunSpan);
        Assert.Equal(folderRunSpan.SpanId, this.OnlyChildOfTheFolderRun().ParentSpanId);
    }

    /// <summary>A stage that ran to its end says so, and says nothing about the account the folder run above it names.</summary>
    [Fact]
    public void BeginPhase_AStageThatCompleted_PublishesItAsSucceededAndCarriesNoAliasOfItsOwn()
    {
        // Arrange
        var account = MailAccountId.Create("phase-succeeded");

        // Act
        using var folderRun = this.telemetry.BeginFolderRun(account);
        using (var stage = this.telemetry.BeginPhase(
            MailSynchronizationPhase.ReconcileFolder,
            TestContext.Current.CancellationToken))
        {
            stage.Completed();
        }

        // Assert
        var stageSpan = this.OnlyChildOfTheFolderRun();

        Assert.Equal("succeeded", stageSpan.GetTagItem(OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Ok, stageSpan.Status);
        Assert.Null(stageSpan.GetTagItem("mailfathom.mail.account"));
    }

    /// <summary>A stage that threw reported nothing, and a failed stage is what an operator has to be able to see.</summary>
    [Fact]
    public void BeginPhase_AStageThatReportedNothing_PublishesItAsFailed()
    {
        // Arrange
        var account = MailAccountId.Create("phase-failed");

        // Act
        using var folderRun = this.telemetry.BeginFolderRun(account);
        using (this.telemetry.BeginPhase(
            MailSynchronizationPhase.DiscoverEmails,
            TestContext.Current.CancellationToken))
        {
        }

        // Assert
        var stageSpan = this.OnlyChildOfTheFolderRun();

        Assert.Equal("failed", stageSpan.GetTagItem(OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Error, stageSpan.Status);
    }

    /// <summary>Shutdown is not something the work did, so a stage the host stopped is interrupted rather than failed.</summary>
    [Fact]
    public void BeginPhase_AStageTheHostStopped_PublishesItAsInterrupted()
    {
        // Arrange
        var account = MailAccountId.Create("phase-interrupted");
        using var shutdown = new CancellationTokenSource();

        // Act
        using var folderRun = this.telemetry.BeginFolderRun(account);
        using (this.telemetry.BeginPhase(MailSynchronizationPhase.RefillDeferredContent, shutdown.Token))
        {
            shutdown.Cancel();
        }

        // Assert
        var stageSpan = this.OnlyChildOfTheFolderRun();

        Assert.Equal("interrupted", stageSpan.GetTagItem(OutcomeTagName));
        Assert.Equal(ActivityStatusCode.Unset, stageSpan.Status);
    }

    /// <summary>A stage the adapter publishes no name for is a member added without one, which fails rather than being invented.</summary>
    [Fact]
    public void BeginPhase_AStageThisAdapterPublishesNoNameFor_Throws()
    {
        // Arrange
        var undeclared = (MailSynchronizationPhase)int.MaxValue;

        // Act

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => this.telemetry.BeginPhase(undeclared, TestContext.Current.CancellationToken));
    }

    /// <summary>Reads the one stage this test published beneath the folder run it opened.</summary>
    /// <remarks>
    /// Found by having a parent rather than by name, because the folder-run span itself also reaches the listener and
    /// so does anything another test class published while this one ran.
    /// </remarks>
    private Activity OnlyChildOfTheFolderRun() =>
        Assert.Single(this.published, span => span.Parent is { OperationName: "synchronize_folder" });
}
