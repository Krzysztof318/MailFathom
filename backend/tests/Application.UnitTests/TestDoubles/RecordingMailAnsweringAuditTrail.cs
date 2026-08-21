// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for the durable record, keeping what each finished run was reported as having read.</summary>
/// <remarks>
/// It snapshots the parts of the observation an entry is built from rather than keeping the observation itself, for the
/// reason the run telemetry double does: the observation is mutable, and what an entry states is what it said when the
/// record was written. Which accounts owe an entry, and how one is shaped, belong to the adapter that writes it.
/// </remarks>
internal sealed class RecordingMailAnsweringAuditTrail : IMailAnsweringAuditTrail
{
    private readonly List<RecordedRun> runs = [];

    /// <summary>Gets every run this record was asked to keep, in order.</summary>
    public IReadOnlyList<RecordedRun> Runs => this.runs;

    /// <inheritdoc />
    public Task RecordAsync(MailAnsweringRunObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        this.runs.Add(new RecordedRun(
            observation.RunId,
            [.. observation.Scope.AccountIds.Select(static accountId => accountId.Value)],
            [.. observation.Retrieval.Passages.Select(static passage => passage.StoredEmailId)],
            observation.CitedEmailIds,
            observation.ChatEndpointAlias,
            observation.InstructionsVersion,
            observation.Outcome,
            observation.Retrieval.Degradation,
            observation.StartedAt,
            observation.CompletedAt));

        return Task.CompletedTask;
    }

    /// <summary>What one finished run was reported as having done.</summary>
    /// <param name="RunId">What identifies the run.</param>
    /// <param name="AccountIds">The accounts the run was allowed to read.</param>
    /// <param name="RetrievedEmailIds">The emails the run retrieved, in the order it retrieved them.</param>
    /// <param name="CitedEmailIds">The emails the published answer named.</param>
    /// <param name="ChatEndpointAlias">The endpoint the run was conducted through.</param>
    /// <param name="InstructionsVersion">The instruction the run was conducted under.</param>
    /// <param name="Outcome">How the run ended.</param>
    /// <param name="Degradation">The ways the run read less than an undegraded run of the same question would.</param>
    /// <param name="StartedAt">When the run began.</param>
    /// <param name="CompletedAt">When the run reached that ending.</param>
    internal sealed record RecordedRun(
        MailAnsweringRunId RunId,
        IReadOnlyList<string> AccountIds,
        IReadOnlyList<StoredEmailId> RetrievedEmailIds,
        IReadOnlyList<StoredEmailId> CitedEmailIds,
        string ChatEndpointAlias,
        string InstructionsVersion,
        MailAnsweringRunOutcome Outcome,
        MailAnsweringRunDegradation Degradation,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt);
}
