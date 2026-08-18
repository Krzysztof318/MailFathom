// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reads the trace an enqueue is happening inside, in the form a job row can keep.</summary>
/// <remarks>
/// <para>
/// The ambient span is read here rather than passed in, for the reason it is read anywhere else: what enqueued the job
/// is a property of the flow the enqueue happens on, and a parameter for it would put a telemetry value on the whole
/// enqueue contract to say what the flow already knows.
/// </para>
/// <para>
/// It reads the W3C form of the context rather than the identifiers, because that is the form a link is rebuilt from
/// and the form a reader recognizes in a stored row. A process running no trace at that moment, or one whose sampler
/// dropped it, captures nothing — which is the same answer as a row written before the column existed.
/// </para>
/// </remarks>
internal static class EnqueuedTraceCapture
{
    /// <summary>Reads the current trace as the context to keep on the job being enqueued.</summary>
    /// <returns>The context, or <see langword="null" /> when nothing was being traced.</returns>
    internal static JobTraceContext? Current()
    {
        if (Activity.Current is not { IdFormat: ActivityIdFormat.W3C } activity)
        {
            return null;
        }

        return JobTraceContext.FromTraceParent(activity.Id, activity.TraceStateString);
    }
}
