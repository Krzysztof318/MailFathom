// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;
using MailFathom.Mcp.Tools;

namespace MailFathom.Mcp.Observability;

/// <summary>Publishes what every tool call cost and how it ended, as an aggregate rather than as a record apiece.</summary>
/// <remarks>
/// <para>
/// A span answers why one call was slow and a log line answers what happened to it. Neither answers how often a tool is
/// called, how its duration is distributed, or whether either of those is moving — which is the question a regression in
/// a tool arrives as, long before anybody correlates the complaints it produces. The measurement already exists: the
/// reporter times every call and classifies its ending, so what this adds is a second destination for a figure that was
/// being computed and then thrown away.
/// </para>
/// <para>
/// The two dimensions are the tool and the outcome, and both are closed. The outcome is one of this type's own words.
/// The tool is the name the caller sent only when a published tool answers to it, and one fixed placeholder otherwise —
/// which is the difference between this and the log line beside it, where the same name is recorded whenever its shape
/// is safe. A log line costs what it is written to; a dimension costs a time series that never goes away, so a client
/// calling <c>list_email</c> in a loop must not be able to mint one.
/// </para>
/// <para>
/// Nothing else about a call reaches either instrument. Not an argument, not a filter value, not a mailbox, not a
/// result — a tool call is the one place in this process where a caller's own text arrives, and none of it belongs in a
/// dimension.
/// </para>
/// </remarks>
internal sealed class McpToolCallTelemetry
{
    internal const string ToolTagName = "mailfathom.mcp.tool";
    internal const string OutcomeTagName = "mailfathom.mcp.tool.outcome";

    /// <summary>Names a call that answered, whatever the answer said.</summary>
    internal const string SucceededOutcomeName = "succeeded";

    /// <summary>Names a call a tool answered with an error result of its own.</summary>
    internal const string ToolErrorOutcomeName = "tool_error";

    /// <summary>Names a call the caller abandoned, which is ordinary traffic rather than a failure.</summary>
    internal const string CancelledOutcomeName = "cancelled";

    internal const string ProtocolErrorOutcomeName = "protocol_error";

    /// <summary>Names a call refused with a MailFathom error code the caller can act on.</summary>
    internal const string RefusedOutcomeName = "refused";

    /// <summary>Names a call that ended in a way nothing anticipated and was answered with the generic code.</summary>
    internal const string FailedOutcomeName = "failed";

    private readonly Counter<long> calls;
    private readonly Histogram<double> callDuration;

    /// <summary>Initializes the instruments every tool call is published through.</summary>
    public McpToolCallTelemetry()
    {
        this.calls = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mcp.tool.calls",
            unit: "{call}",
            description: "Tool calls this surface served, by tool and by how each one ended.");
        this.callDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mcp.tool.call.duration",
            unit: "s",
            description: "How long one tool call took, by tool and by how it ended.");
    }

    /// <summary>Records a call a tool answered, and whether the answer it gave was an error.</summary>
    /// <param name="requestedToolName">The tool name the request carried.</param>
    /// <param name="isError">Whether the tool answered with an error result of its own.</param>
    /// <param name="duration">How long the call took.</param>
    /// <remarks>
    /// A tool's own error result is a separate outcome rather than a success, because it is an answer the caller asked
    /// for that the deployment could not give — and it is separate from a failure, because a tool that reports one has
    /// diagnosed it.
    /// </remarks>
    public void RecordCompleted(string? requestedToolName, bool isError, TimeSpan duration) =>
        this.Record(requestedToolName, isError ? ToolErrorOutcomeName : SucceededOutcomeName, duration);

    /// <summary>Records a call the caller cancelled.</summary>
    /// <param name="requestedToolName">The tool name the request carried.</param>
    /// <param name="duration">How long the call ran before it was abandoned.</param>
    public void RecordCancelled(string? requestedToolName, TimeSpan duration) =>
        this.Record(requestedToolName, CancelledOutcomeName, duration);

    /// <summary>Records a call that ended as a JSON-RPC error the transport has to report.</summary>
    /// <param name="requestedToolName">The tool name the request carried.</param>
    /// <param name="duration">How long the call ran.</param>
    public void RecordProtocolFailure(string? requestedToolName, TimeSpan duration) =>
        this.Record(requestedToolName, ProtocolErrorOutcomeName, duration);

    /// <summary>Records a call refused with a MailFathom error code.</summary>
    /// <param name="requestedToolName">The tool name the request carried.</param>
    /// <param name="duration">How long the call ran before it was refused.</param>
    /// <remarks>
    /// The code itself is deliberately not a dimension. Which refusal a caller met is on the log line and on the answer
    /// the caller already holds, and multiplying every tool's series by the codes it can raise buys a breakdown nobody
    /// reads a rate from.
    /// </remarks>
    public void RecordRefused(string? requestedToolName, TimeSpan duration) =>
        this.Record(requestedToolName, RefusedOutcomeName, duration);

    /// <summary>Records a call that failed for a reason nothing diagnosed.</summary>
    /// <param name="requestedToolName">The tool name the request carried.</param>
    /// <param name="duration">How long the call ran before it failed.</param>
    public void RecordUnexpectedFailure(string? requestedToolName, TimeSpan duration) =>
        this.Record(requestedToolName, FailedOutcomeName, duration);

    private void Record(string? requestedToolName, string outcome, TimeSpan duration)
    {
        var tags = new TagList
        {
            { ToolTagName, PublishedTools.MeasurableName(requestedToolName) },
            { OutcomeTagName, outcome },
        };

        this.calls.Add(1, tags);
        this.callDuration.Record(duration.TotalSeconds, tags);
    }
}
