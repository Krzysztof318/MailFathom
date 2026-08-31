// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the span one guarded operation publishes: what it is called, what it carries, and how it ends.</summary>
/// <remarks>
/// The listener is narrowed to the one span name, because the activity source is the process's and everything else
/// MailFathom publishes reaches it too.
/// </remarks>
public sealed class SensitiveContentGuardSpanTests : IDisposable
{
    private readonly ConcurrentBag<Activity> published = [];

    /// <summary>Stands in for whichever read is publishing above the guard, which is a span this class alone owns.</summary>
    /// <remarks>
    /// A real <c>read_email_content</c> span would be the honest parent and is the wrong one to open here: it is
    /// published on the process-wide source that <see cref="MailboxReadTelemetryTests" /> listens to by name, and xUnit
    /// may run that class at the same moment. Parenting is decided by the ambient activity rather than by who published
    /// it, so a source of this class's own proves the nesting without publishing into another class's assertions.
    /// </remarks>
    private readonly ActivitySource readAboveTheGuard = new("SensitiveContentGuardSpanTests.ReadAboveTheGuard");

    private readonly ActivityListener listener;

    public SensitiveContentGuardSpanTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name || source == this.readAboveTheGuard,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == SensitiveContentEgressTelemetry.GuardedOperationSpanName)
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
        this.readAboveTheGuard.Dispose();
    }

    /// <summary>The duration is what a caller waited for, and the count beside it is what that duration was spent on.</summary>
    [Fact]
    public void BeginGuardedOperation_TextsScannedInsideIt_PublishesTheEgressPointAndTheCount()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        using (var operation = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken))
        {
            operation.TextGuarded();
            operation.TextGuarded();
            operation.TextGuarded();
            operation.Completed();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("scan_sensitive_content", span.OperationName);
        Assert.Equal("mcp_email_content", span.GetTagItem("mailfathom.sensitive_content.egress_point"));
        Assert.Equal(3, span.GetTagItem("mailfathom.sensitive_content.texts"));
        Assert.Equal("succeeded", span.GetTagItem("mailfathom.sensitive_content.outcome"));
    }

    /// <summary>
    /// Postures differ between the people one deployment serves, so a scan that cannot be attributed to one of them
    /// cannot be read against what that person asked for. The identifier is on the span and on none of the counters,
    /// which is what keeps every metric dimension a closed set.
    /// </summary>
    [Fact]
    public void BeginGuardedOperation_AnOperationPublishingOneOwnersMail_PublishesWhoseMailItWas()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        using (var operation = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            SyntheticMailOwner.Another,
            TestContext.Current.CancellationToken))
        {
            operation.TextGuarded();
            operation.Completed();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(SyntheticMailOwner.Another.Value.ToString(), span.GetTagItem("mailfathom.owner"));
    }

    /// <summary>A deployment that scans nobody opens no operation, so an unset owner is a flow that skipped resolving one.</summary>
    [Fact]
    public void BeginGuardedOperation_AnOperationNamingNoOwner_PublishesNoOwnerAttribute()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        using (var operation = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            default,
            TestContext.Current.CancellationToken))
        {
            operation.Completed();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Null(span.GetTagItem("mailfathom.owner"));
    }

    /// <summary>A refusal is an ending rather than an error, because the scanner stopped the egress on purpose.</summary>
    [Fact]
    public void BeginGuardedOperation_AScannerThatCouldNotAnswer_PublishesItAsRefused()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        using (var operation = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpSnippet,
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken))
        {
            operation.Refused();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("refused", span.GetTagItem("mailfathom.sensitive_content.outcome"));
        Assert.Equal(0, span.GetTagItem("mailfathom.sensitive_content.texts"));
    }

    /// <summary>The nesting is what makes a slow read attributable to the scan inside it rather than to the read.</summary>
    [Fact]
    public void BeginGuardedOperation_AnOperationInsideARead_PublishesBeneathIt()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        Activity? readSpan;

        using (readSpan = this.readAboveTheGuard.StartActivity("read_above_the_guard"))
        {
            using var operation = telemetry.BeginGuardedOperation(
                SensitiveContentEgressPoint.McpEmailContent,
                SyntheticMailOwner.Deployment,
                TestContext.Current.CancellationToken);

            operation.TextGuarded();
            operation.Completed();
        }

        // Assert
        Assert.NotNull(readSpan);
        Assert.Equal(readSpan.SpanId, Assert.Single(this.published).ParentSpanId);
    }

    /// <summary>A scan that stopped without a scanner refusing it is the scanner having faulted, which is not a success.</summary>
    [Fact]
    public void BeginGuardedOperation_AnOperationThatReportedNothing_PublishesItAsFailed()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();

        // Act
        using (var operation = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpSnippet,
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken))
        {
            operation.TextGuarded();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("failed", span.GetTagItem("mailfathom.sensitive_content.outcome"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(1, span.GetTagItem("mailfathom.sensitive_content.texts"));
    }

    /// <summary>A read the host stopped is not a scanner that broke, so a cancelled operation carries no error.</summary>
    [Fact]
    public void BeginGuardedOperation_AnOperationTheHostStopped_PublishesItAsCancelled()
    {
        // Arrange
        var telemetry = new SensitiveContentEgressTelemetry();
        using var shutdown = new CancellationTokenSource();

        // Act
        using (telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            SyntheticMailOwner.Deployment,
            shutdown.Token))
        {
            shutdown.Cancel();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("cancelled", span.GetTagItem("mailfathom.sensitive_content.outcome"));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }
}
