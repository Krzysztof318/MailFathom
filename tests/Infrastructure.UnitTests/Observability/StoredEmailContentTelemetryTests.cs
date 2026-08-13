// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the span a read of stored raw MIME publishes, which is the one place a read's size is visible.</summary>
/// <remarks>
/// It listens to the real activity source and narrows to this span's own name, so a span published by another test class
/// at the same moment is not mistaken for one of these.
/// </remarks>
public sealed class StoredEmailContentTelemetryTests : IDisposable
{
    private readonly ConcurrentBag<Activity> published = [];
    private readonly ActivityListener listener;

    public StoredEmailContentTelemetryTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == StoredEmailContentTelemetry.ReadSpanName)
                {
                    this.published.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose() => this.listener.Dispose();

    /// <summary>What a read that found content says: that it found some, and how many bytes of it there were.</summary>
    [Fact]
    public void BeginRead_ContentThatWasFound_PublishesItsSize()
    {
        // Arrange
        var telemetry = new StoredEmailContentTelemetry();

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Found(42_000);
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal("read_stored_email_content", span.OperationName);
        Assert.Equal(
            [
                ("mailfathom.mail.content.found", "True"),
                ("mailfathom.mail.content.bytes", "42000"),
            ],
            span.TagObjects.Select(tag => (tag.Key, tag.Value?.ToString())));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>An email this deployment holds no content for is an answer rather than a failure.</summary>
    [Fact]
    public void BeginRead_ContentThatIsNotStored_PublishesTheAbsenceWithoutASize()
    {
        // Arrange
        var telemetry = new StoredEmailContentTelemetry();

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Absent();
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(false, span.GetTagItem("mailfathom.mail.content.found"));
        Assert.Null(span.GetTagItem("mailfathom.mail.content.bytes"));
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
    }

    /// <summary>A read that reported neither outcome is one that threw, and the span says so rather than staying silent.</summary>
    [Fact]
    public void BeginRead_AReadThatReportedNothing_PublishesItAsAnError()
    {
        // Arrange
        var telemetry = new StoredEmailContentTelemetry();

        // Act
        using (telemetry.BeginRead())
        {
        }

        // Assert
        var span = Assert.Single(this.published);

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Empty(span.TagObjects);
    }

    /// <summary>
    /// The payload this span describes is a whole message, so what it may say about it is a size and nothing else: no
    /// stored identity, no account, no folder, and no part of the mail itself.
    /// </summary>
    [Fact]
    public void BeginRead_AnyRead_PublishesNothingBeyondASizeAndWhetherAnythingWasThere()
    {
        // Arrange
        var telemetry = new StoredEmailContentTelemetry();

        // Act
        using (var read = telemetry.BeginRead())
        {
            read.Found(1_024);
        }

        // Assert
        Assert.Equal(
            ["mailfathom.mail.content.found", "mailfathom.mail.content.bytes"],
            Assert.Single(this.published).TagObjects.Select(tag => tag.Key));
    }
}
