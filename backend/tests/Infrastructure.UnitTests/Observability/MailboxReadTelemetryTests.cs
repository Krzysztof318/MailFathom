// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the span a local read publishes: what it is called, where it sits, and what it carries.</summary>
/// <remarks>
/// It listens to the real activity source, because the rule under test is about what an exporter would receive. The
/// listener is narrowed to the five read span names, so a span published by another test class at the same moment is not
/// mistaken for one of these — the source is the process's and is shared by everything MailFathom publishes.
/// </remarks>
public sealed class MailboxReadTelemetryTests : IDisposable
{
    private static readonly string[] ReadSpanNames =
    [
        MailboxReadTelemetry.AccountDirectorySpanName,
        MailboxReadTelemetry.MailboxTimelineSpanName,
        MailboxReadTelemetry.MailboxSearchSpanName,
        MailboxReadTelemetry.EmailContentSpanName,
        MailboxReadTelemetry.EmailThreadSpanName,
        MailboxReadTelemetry.SearchRankingSpanName,
    ];

    private readonly ConcurrentBag<Activity> published = [];

    /// <summary>
    /// Stands in for a source somebody else owns, which is what the MCP SDK's own span arrives from.
    /// </summary>
    /// <remarks>
    /// It is an instance field so that it is constructed before the listener below, which is what makes the nesting
    /// test deterministic. As a static field it was initialized lazily, and the first thing to touch it was the
    /// delegate this listener registers with: the source was then constructed *during*
    /// <see cref="ActivitySource.AddActivityListener"/>, found no listener to attach to because this one was still
    /// being added, and was missed by the walk already in flight over the sources that existed before it. Nothing
    /// listened to it, <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returned <c>null</c>, and the
    /// test failed — but only when it was the first of this class to run, because every later instance found the
    /// source already there.
    /// </remarks>
    private readonly ActivitySource protocolBoundary = new("MailboxReadTelemetryTests.ProtocolBoundary");

    private readonly ActivityListener listener;

    public MailboxReadTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name || source == this.protocolBoundary,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (ReadSpanNames.Contains(activity.OperationName))
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose()
    {
        this.listener.Dispose();
        this.protocolBoundary.Dispose();
    }

    /// <summary>The span names an operator writes a filter against, one per use case the MCP surface reaches.</summary>
    [Theory]
    [InlineData(MailboxReadOperation.ReadAccountDirectory, "read_account_directory")]
    [InlineData(MailboxReadOperation.ListMailboxTimeline, "list_mailbox_timeline")]
    [InlineData(MailboxReadOperation.SearchMailbox, "search_mailbox")]
    [InlineData(MailboxReadOperation.ReadEmailContent, "read_email_content")]
    [InlineData(MailboxReadOperation.ReadEmailThread, "read_email_thread")]
    public void BeginRead_EachOperation_PublishesItUnderTheNameOfTheUseCase(
        MailboxReadOperation operation,
        string expectedSpanName)
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using (var read = telemetry.BeginRead(operation, TestContext.Current.CancellationToken))
        {
            read.Completed(0);
        }

        // Assert
        Assert.Equal(expectedSpanName, Assert.Single(this.published).OperationName);
    }

    /// <summary>
    /// The ranking sits between two library-spanned ends — a provider call and a set of queries — and neither of them
    /// says what the ranking as a whole cost, so it is published beneath the search that ran it.
    /// </summary>
    [Fact]
    public void BeginSearchRanking_ARankingInsideASearch_PublishesTheCandidateCountBeneathTheSearch()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using (var read = telemetry.BeginRead(MailboxReadOperation.SearchMailbox, TestContext.Current.CancellationToken))
        {
            using (var ranking = telemetry.BeginSearchRanking(TestContext.Current.CancellationToken))
            {
                ranking.Completed(20);
            }

            read.Completed(5);
        }

        // Assert
        var search = this.published.Single(span => span.OperationName == MailboxReadTelemetry.MailboxSearchSpanName);
        var rankingSpan = this.published.Single(
            span => span.OperationName == MailboxReadTelemetry.SearchRankingSpanName);

        Assert.Equal(search.SpanId, rankingSpan.ParentSpanId);
        Assert.Equal(20, rankingSpan.GetTagItem("mailfathom.mailbox.read.results"));
        Assert.Equal("succeeded", rankingSpan.GetTagItem("mailfathom.mailbox.read.outcome"));
    }

    /// <summary>What a completed read says: how much it returned, and that it returned it.</summary>
    [Fact]
    public void BeginRead_AReadThatReturnedResults_PublishesTheCountAndThatItSucceeded()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using (var read = telemetry.BeginRead(
            MailboxReadOperation.SearchMailbox,
            TestContext.Current.CancellationToken))
        {
            read.Completed(7);
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(
            [
                ("mailfathom.mailbox.read.results", "7"),
                ("mailfathom.mailbox.read.outcome", "succeeded"),
            ],
            span.TagObjects.Select(tag => (tag.Key, tag.Value?.ToString())));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>
    /// The nesting is the whole point of the span: it is started inside the protocol boundary's own span, so a trace
    /// leads from a tool call to the use case that served it rather than from a tool call to a set of database commands.
    /// </summary>
    [Fact]
    public void BeginRead_AReadInsideAProtocolSpan_PublishesBeneathIt()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using var protocolCall = this.protocolBoundary.StartActivity("tools/call");
        using (var read = telemetry.BeginRead(
            MailboxReadOperation.ListMailboxTimeline,
            TestContext.Current.CancellationToken))
        {
            read.Completed(1);
        }

        // Assert
        Assert.NotNull(protocolCall);
        Assert.Equal(protocolCall.Id, Assert.Single(this.published).ParentId);
    }

    /// <summary>A caller that walked away is ordinary traffic, so it is told apart from a read that broke.</summary>
    [Fact]
    public void BeginRead_AReadTheCallerCancelled_PublishesItAsCancelledRatherThanFailed()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();
        using var caller = new CancellationTokenSource();

        // Act
        using (telemetry.BeginRead(MailboxReadOperation.ReadEmailContent, caller.Token))
        {
            caller.Cancel();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("cancelled", span.GetTagItem("mailfathom.mailbox.read.outcome"));
        Assert.Null(span.GetTagItem("mailfathom.mailbox.read.results"));
    }

    /// <summary>A read that reported nothing and was not cancelled is a read that threw, and the span says so.</summary>
    [Fact]
    public void BeginRead_AReadThatReportedNothing_PublishesItAsFailed()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using (telemetry.BeginRead(MailboxReadOperation.SearchMailbox, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("failed", span.GetTagItem("mailfathom.mailbox.read.outcome"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    /// <summary>
    /// The rule the telemetry page states as a cardinality rule as much as a privacy one: a read publishes a count and
    /// an ending, so no query text, filter value, cursor, subject, address, or stored identity can reach a span store
    /// through it — there is nowhere on the span to put one.
    /// </summary>
    [Fact]
    public void BeginRead_AnyRead_PublishesNothingBeyondACountAndAnEnding()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        using (var read = telemetry.BeginRead(
            MailboxReadOperation.SearchMailbox,
            TestContext.Current.CancellationToken))
        {
            read.Completed(3);
        }

        // Assert
        Assert.Equal(
            ["mailfathom.mailbox.read.results", "mailfathom.mailbox.read.outcome"],
            Assert.Single(this.published).TagObjects.Select(tag => tag.Key));
    }

    /// <summary>An operation this adapter publishes no name for is a member added without a span, never an unnamed span.</summary>
    [Fact]
    public void BeginRead_AnOperationOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var telemetry = new MailboxReadTelemetry();

        // Act
        void BeginUnknownRead() =>
            telemetry.BeginRead((MailboxReadOperation)(-1), TestContext.Current.CancellationToken);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(BeginUnknownRead);
    }
}
