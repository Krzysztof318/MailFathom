// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an enqueue writes down about the trace it is happening inside.</summary>
/// <remarks>
/// The source is this class's own rather than MailFathom's, because what is under test is reading the ambient span
/// rather than which registry it came from — and a source of its own keeps the assertion free of whatever another
/// test class is publishing at the same moment.
/// </remarks>
public sealed class EnqueuedTraceCaptureTests : IDisposable
{
    private readonly ActivitySource enqueuingWork = new("EnqueuedTraceCaptureTests.EnqueuingWork");
    private readonly ActivityListener listener;

    public EnqueuedTraceCaptureTests()
    {
        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source == this.enqueuingWork,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    public void Dispose()
    {
        this.listener.Dispose();
        this.enqueuingWork.Dispose();
    }

    /// <summary>The W3C form is what a link is rebuilt from, so it is the form the row keeps.</summary>
    [Fact]
    public void Current_AnEnqueueInsideATrace_CapturesTheAmbientSpanInItsPropagatedForm()
    {
        // Arrange
        using var folderRun = this.enqueuingWork.StartActivity("synchronize_folder");

        // Act
        var captured = EnqueuedTraceCapture.Current();

        // Assert
        Assert.NotNull(folderRun);
        Assert.NotNull(captured);
        Assert.Equal(folderRun.Id, captured.TraceParent);
    }

    /// <summary>The vendor list travels with the context, because it is part of what a downstream reader correlates by.</summary>
    [Fact]
    public void Current_ATraceCarryingVendorState_CapturesItBesideTheParent()
    {
        // Arrange
        using var folderRun = this.enqueuingWork.StartActivity("synchronize_folder");
        folderRun!.TraceStateString = "vendor=state";

        // Act
        var captured = EnqueuedTraceCapture.Current();

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("vendor=state", captured.TraceState);
    }

    /// <summary>A pass nothing is recording writes no trace down, which is the same answer an older row gives.</summary>
    [Fact]
    public void Current_AnEnqueueOutsideAnyTrace_CapturesNothing()
    {
        // Arrange
        Activity.Current = null;

        // Act
        var captured = EnqueuedTraceCapture.Current();

        // Assert
        Assert.Null(captured);
    }
}
