// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>The trace an execution was enqueued inside, written down so the attempt hours later can point back at it.</summary>
/// <remarks>
/// <para>
/// A durable queue is a break in a trace rather than a tree. The work that enqueued a job has ended long before a
/// worker claims it, so an attempt cannot be that work's child — a span store would be asked to hold a trace open for
/// as long as a queue is deep. What survives instead is this: the W3C context of whatever was running at the enqueue,
/// kept on the job's own row and turned back into a link on the attempt's span.
/// </para>
/// <para>
/// It carries the two propagation values and nothing else. There is no place here for the job's payload, its key, or
/// its account — a trace identifier is a random number this process minted, which is the whole reason it is safe to
/// store beside work that points at mail.
/// </para>
/// <para>
/// <b>Absence is ordinary.</b> Every row written before this was recorded carries none, and so does every job enqueued
/// by a pass that no trace was being recorded for. An attempt at such a job opens the same span with no link on it.
/// </para>
/// </remarks>
public sealed record JobTraceContext
{
    /// <summary>The longest a W3C <c>traceparent</c> can be, which is the fixed form the specification defines.</summary>
    /// <remarks>Version, trace identifier, span identifier, and flags, separated by three hyphens.</remarks>
    public const int MaximumTraceParentLength = 55;

    /// <summary>The longest a <c>tracestate</c> this stores may be, which is the size the specification asks a system to accept.</summary>
    /// <remarks>
    /// A longer one is dropped rather than truncated or refused: a truncated list is a malformed one, and the vendor
    /// entries it carries are somebody else's correlation rather than this deployment's. The link survives without it,
    /// because the trace and span identifiers are in the value beside it.
    /// </remarks>
    public const int MaximumTraceStateLength = 512;

    private JobTraceContext(string traceParent, string? traceState)
    {
        this.TraceParent = traceParent;
        this.TraceState = traceState;
    }

    /// <summary>Gets the W3C <c>traceparent</c> of whatever enqueued the job.</summary>
    public string TraceParent { get; }

    /// <summary>Gets the W3C <c>tracestate</c> that accompanied it, or <see langword="null" /> when there was none.</summary>
    public string? TraceState { get; }

    /// <summary>Reads a stored or propagated pair of values as a context, or reports that there is none to link to.</summary>
    /// <param name="traceParent">The <c>traceparent</c> as it was captured or read back, which may be absent.</param>
    /// <param name="traceState">The <c>tracestate</c> that accompanied it, which may be absent.</param>
    /// <returns>The context, or <see langword="null" /> when no usable one was recorded.</returns>
    /// <remarks>
    /// Absence and an unusable value answer the same way on purpose. A row written before this column existed, a job
    /// enqueued outside any trace, and a value some other writer put in the column are all reasons to open the attempt's
    /// span without a link — and none of them is a reason to fail the attempt, which would trade work for telemetry.
    /// </remarks>
    public static JobTraceContext? FromTraceParent(string? traceParent, string? traceState) =>
        string.IsNullOrWhiteSpace(traceParent) || traceParent.Length > MaximumTraceParentLength
            ? null
            : new JobTraceContext(traceParent, WithinBounds(traceState));

    private static string? WithinBounds(string? traceState) =>
        string.IsNullOrWhiteSpace(traceState) || traceState.Length > MaximumTraceStateLength ? null : traceState;
}
