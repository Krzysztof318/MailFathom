// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

/// <summary>Covers the trace a job carries from the enqueue to the attempt that runs it hours later.</summary>
public sealed class JobTraceContextTests
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01";

    [Fact]
    public void FromTraceParent_APropagatedContext_KeepsBothValues()
    {
        // Arrange

        // Act
        var context = JobTraceContext.FromTraceParent(TraceParent, "vendor=state");

        // Assert
        Assert.NotNull(context);
        Assert.Equal(TraceParent, context.TraceParent);
        Assert.Equal("vendor=state", context.TraceState);
    }

    /// <summary>Every row written before the column existed answers this way, and so does an untraced enqueue.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromTraceParent_NothingRecorded_ReportsNoContextToLinkTo(string? traceParent)
    {
        // Arrange

        // Act
        var context = JobTraceContext.FromTraceParent(traceParent, traceState: null);

        // Assert
        Assert.Null(context);
    }

    /// <summary>A value longer than the specification allows is something else in the column, and an attempt links to nothing rather than failing.</summary>
    [Fact]
    public void FromTraceParent_AValueLongerThanTheSpecificationAllows_ReportsNoContext()
    {
        // Arrange
        var overlong = new string('0', JobTraceContext.MaximumTraceParentLength + 1);

        // Act
        var context = JobTraceContext.FromTraceParent(overlong, traceState: null);

        // Assert
        Assert.Null(context);
    }

    /// <summary>
    /// The specification's own longest value is a value, so both ceilings are exclusive of nothing: a context of
    /// exactly the maximum length is the ordinary case rather than the one the bound was written against, and an
    /// off-by-one in either check would silently stop linking every attempt.
    /// </summary>
    [Fact]
    public void FromTraceParent_ValuesOfExactlyTheMaximumLength_KeepsBoth()
    {
        // Arrange
        var longestTraceParent = new string('0', JobTraceContext.MaximumTraceParentLength);
        var longestTraceState = new string('a', JobTraceContext.MaximumTraceStateLength);

        // Act
        var context = JobTraceContext.FromTraceParent(longestTraceParent, longestTraceState);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(longestTraceParent, context.TraceParent);
        Assert.Equal(longestTraceState, context.TraceState);
    }

    /// <summary>The link survives without the vendor list, so an oversized one is dropped rather than truncated into a malformed one.</summary>
    [Fact]
    public void FromTraceParent_AnOversizedTraceState_KeepsTheContextAndDropsTheState()
    {
        // Arrange
        var overlong = new string('a', JobTraceContext.MaximumTraceStateLength + 1);

        // Act
        var context = JobTraceContext.FromTraceParent(TraceParent, overlong);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(TraceParent, context.TraceParent);
        Assert.Null(context.TraceState);
    }

    /// <summary>A blank vendor list is no list, and is carried through as absence rather than as an empty string.</summary>
    [Fact]
    public void FromTraceParent_ABlankTraceState_ReportsNone()
    {
        // Arrange

        // Act
        var context = JobTraceContext.FromTraceParent(TraceParent, "  ");

        // Assert
        Assert.NotNull(context);
        Assert.Null(context.TraceState);
    }
}
