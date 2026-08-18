// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Carries one submission's report from the call that starts it to the answer that ends it.</summary>
/// <remarks>
/// <para>
/// The scope reports a failure unless <see cref="Completed" /> was called, so a submission that raised anywhere inside
/// it is still measured rather than dropping out of the record entirely — which matters more here than anywhere else,
/// because the submission that raised is the one whose outcome nobody knows.
/// </para>
/// <para>
/// What the span never carries is which recipients were offered, how many there were, or what the server wrote. A
/// recipient count is a fact about one person's correspondence when it is one, and the question this span answers —
/// how long the exchange with the provider took — needs none of it.
/// </para>
/// </remarks>
public sealed class MailDeliveryScope : IDisposable
{
    private readonly MailDeliveryTelemetry telemetry;
    private readonly MailAccountId accountId;
    private readonly Activity? activity;
    private readonly long startingTimestamp;

    private bool accepted;
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
    public void Completed()
    {
        this.accepted = true;
        this.activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.reported)
        {
            return;
        }

        this.reported = true;

        if (!this.accepted)
        {
            this.activity?.SetStatus(ActivityStatusCode.Error);
        }

        this.telemetry.RecordSubmission(this.accountId, this.telemetry.ElapsedSince(this.startingTimestamp));
        this.activity?.Dispose();
    }
}
