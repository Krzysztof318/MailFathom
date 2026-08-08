// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;
using MailFathom.Domain.Mutations.Audit;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports an audit entry a finished mutation owed and the trail could not be given.</summary>
/// <remarks>
/// <para>
/// It has exactly one thing to say, and that narrowness is what it is for. Writing an entry may never fail the mutation
/// that produced it — the change has already been made to somebody's mailbox by the time the append runs — so the
/// failure is swallowed, and swallowing it is only defensible while it is visible. A deployment that undertook to hold
/// this history can see the moment it stops holding it, on both channels at once.
/// </para>
/// <para>
/// Nothing recorded here is mail. The mutation name, the account, and the mutation record's identifier are MailFathom's
/// own names for things; no subject, address, folder path, or UID reaches a log, a span, or an exporter.
/// </para>
/// </remarks>
public sealed partial class MailboxMutationAuditTelemetry
{
    private const string MutationTagName = "mailfathom.mailbox.mutation";
    private const string AccountTagName = "mailfathom.mail.account";

    private readonly ILogger<MailboxMutationAuditTelemetry> logger;
    private readonly Counter<long> refusedAppendCount;

    /// <summary>Initializes the instruments a refused append is reported through.</summary>
    /// <param name="logger">Records the entry that was not kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailboxMutationAuditTelemetry(ILogger<MailboxMutationAuditTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.refusedAppendCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.mutation.audit.refused_appends",
            unit: "{entry}",
            description: "Audit entries a finished mutation owed and the trail could not be given, by mutation.");
    }

    /// <summary>Says on both channels that a history somebody asked to keep was not kept.</summary>
    /// <param name="entry">The entry the trail was not given.</param>
    /// <param name="failure">What stopped the append.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    internal void RecordRefusedAppend(MailboxMutationAuditEntry entry, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(failure);

        this.refusedAppendCount.Add(
            1,
            new KeyValuePair<string, object?>(MutationTagName, entry.Mutation.Name),
            new KeyValuePair<string, object?>(AccountTagName, entry.AccountId.Value));

        this.LogAuditEntryRefused(
            failure,
            entry.Mutation.Name,
            entry.AccountId.Value,
            entry.MutationRecordId.Value);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The audit trail did not keep the {Mutation} mutation {MutationRecordId} of {AccountId}; the change was made and this history of it is missing.")]
    private partial void LogAuditEntryRefused(
        Exception failure,
        string mutation,
        string accountId,
        Guid mutationRecordId);
}
