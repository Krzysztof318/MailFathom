// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Carries one submission's report from the call that starts it to the answer that ends it.</summary>
/// <remarks>
/// <para>
/// The scope reports a failure unless <see cref="Completed" /> or <see cref="Abandoned" /> was called, so a submission
/// that raised anywhere inside it is still measured rather than dropping out of the record entirely — which matters
/// more here than anywhere else, because the submission that raised is the one whose outcome nobody knows.
/// </para>
/// <para>
/// What the span never carries is which recipients were offered, how many there were, or what the server wrote. A
/// recipient count is a fact about one person's correspondence when it is one, and the question this span answers —
/// how long the exchange with the provider took — needs none of it.
/// </para>
/// </remarks>
internal sealed class MailDeliveryScope : IDisposable
{
    private readonly MailDeliveryTelemetry telemetry;
    private readonly MailAccountId accountId;
    private readonly Activity? activity;
    private readonly long startingTimestamp;

    private bool settled;
    private bool reported;

    internal MailDeliveryScope(
        MailDeliveryTelemetry telemetry,
        MailAccountId accountId,
        Activity? activity,
        long startingTimestamp)
    {
        this.telemetry = telemetry;
        this.accountId = accountId;
        this.activity = activity;
        this.startingTimestamp = startingTimestamp;
    }

    /// <summary>Marks the submission as one the server answered, whatever it said.</summary>
    /// <remarks>
    /// A refusal completes the span rather than failing it. The server answered, so the exchange did what it was for;
    /// what the answer was belongs to the send's own record, and marking a refused message as a failed span would make
    /// a mistyped address read as an unhealthy provider.
    /// </remarks>
    internal void Completed()
    {
        this.settled = true;
        this.activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>Marks the submission as one nobody waited for the end of, which is not the provider failing.</summary>
    /// <remarks>
    /// A caller that stopped waiting and a host that is shutting down both reach here, and neither says anything about
    /// the server. Leaving the span unset rather than failed is what keeps a rolling restart from reading as an
    /// unhealthy provider on the error rate an operator alerts on; what such a submission cost the message is decided
    /// from its own durable record, which is the only place that question can be answered at all.
    /// </remarks>
    internal void Abandoned() => this.settled = true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.reported)
        {
            return;
        }

        this.reported = true;

        if (!this.settled)
        {
            this.activity?.SetStatus(ActivityStatusCode.Error);
        }

        this.telemetry.RecordSubmission(this.accountId, this.telemetry.ElapsedSince(this.startingTimestamp));
        this.activity?.Dispose();
    }
}
