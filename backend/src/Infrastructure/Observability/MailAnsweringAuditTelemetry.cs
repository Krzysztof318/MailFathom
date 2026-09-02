// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports an answering record a finished run owed and the store could not be given.</summary>
/// <remarks>
/// <para>
/// It has one thing to say, and that narrowness is what it is for. Writing an entry may never fail the question that
/// produced it — the answer has already been produced by the time the append runs — so the failure is swallowed, and
/// swallowing it is only defensible while it is visible. A deployment that undertook to explain its answers can see the
/// moment it stops being able to.
/// </para>
/// <para>
/// Nothing recorded here is mail, and nothing here is a question or an answer. The run identifier, this deployment's own
/// endpoint alias, and a count of entries are MailFathom's own names and numbers for things.
/// </para>
/// </remarks>
public sealed partial class MailAnsweringAuditTelemetry
{
    private const string EndpointTagName = "mailfathom.answering.endpoint";

    private readonly ILogger<MailAnsweringAuditTelemetry> logger;
    private readonly Counter<long> refusedAppendCount;

    /// <summary>Initializes the instruments a refused append is reported through.</summary>
    /// <param name="logger">Records the entries that were not kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailAnsweringAuditTelemetry(ILogger<MailAnsweringAuditTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.refusedAppendCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.answering.audit.refused_appends",
            unit: "{entry}",
            description: "Answering audit entries a finished run owed and the record could not be given, by endpoint.");
    }

    /// <summary>Says on both channels that a record somebody asked to keep was not kept.</summary>
    /// <param name="observation">The run whose entries the record was not given.</param>
    /// <param name="owedEntryCount">How many entries that run owed, which is one per account keeping a record.</param>
    /// <param name="failure">What stopped the append.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The entries are counted rather than the runs, because one run owes one entry per account keeping a record and
    /// what an operator lost is entries. The account is deliberately not a tag: an append is one transaction for the
    /// whole run, so every account it covered lost its entry together and a per-account breakdown would report the same
    /// event several times over.
    /// </remarks>
    internal void RecordRefusedAppend(
        MailAnsweringRunObservation observation,
        int owedEntryCount,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(failure);

        this.refusedAppendCount.Add(
            owedEntryCount,
            new KeyValuePair<string, object?>(EndpointTagName, observation.ChatEndpointAlias));

        this.LogAuditEntriesRefused(failure, observation.RunId.Value, owedEntryCount);
    }

    /// <summary>Says that a page left out entries this build cannot interpret.</summary>
    /// <param name="accountId">The account whose record was read.</param>
    /// <param name="unreadableCount">How many rows of the page were left out.</param>
    /// <remarks>
    /// The rows are still in the record and a later build reads them; what is reported is that this build's answer is
    /// short of them, because an audit page that quietly omits entries is worse than one that says it did.
    /// </remarks>
    internal void RecordUnreadableEntries(MailAccountId accountId, int unreadableCount) =>
        this.LogAuditEntriesUnreadable(accountId.Value, unreadableCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The answering record did not keep the {OwedEntryCount} entries run {RunId} owed; the question was answered and this history of what it read is missing.")]
    private partial void LogAuditEntriesRefused(Exception failure, Guid runId, int owedEntryCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Account {AccountId} holds {UnreadableCount} answering audit entries this build cannot interpret, which were left out of the page it served; a build that declares the values they name reads them.")]
    private partial void LogAuditEntriesUnreadable(string accountId, int unreadableCount);
}
