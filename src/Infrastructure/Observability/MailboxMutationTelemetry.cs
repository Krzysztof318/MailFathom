// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports a mailbox mutation as the one operation somebody asked for, on all three channels at once.</summary>
/// <remarks>
/// <para>
/// The three channels are here together because the rule they enforce is about their agreement rather than about any
/// one of them. A relocation carried by RFC 6851 <c>MOVE</c> and a relocation carried by copy, flag, and expunge are
/// the same operation, so they produce the same log message, the same span name, and the same counter dimension.
/// Splitting the emission across three call sites is how they would drift, and the drift has a shape worth naming: a
/// fallback that announces a copy and a delete turns a missing server extension into something an operator has to
/// interpret, and the support question it produces is about mail that was copied and deleted instead of moved — asked
/// about an operation that did exactly what was asked of it.
/// </para>
/// <para>
/// Which path carried the change, and each command the fallback issued, are recorded at <see cref="LogLevel.Debug" />
/// and nowhere else. That is not secrecy: a genuinely broken fallback is diagnosed from which of the three commands was
/// reached, so the detail has to be complete where somebody is looking for it, and absent where somebody is only
/// reading what happened.
/// </para>
/// <para>
/// Nothing recorded here is mail. The account alias, the folder alias, the mutation name, and an IMAP command name are
/// MailFathom's own configured or protocol-registered names; no subject, address, body, remote folder path, UID, or
/// credential reaches a log, a span, or an exporter.
/// </para>
/// </remarks>
public sealed partial class MailboxMutationTelemetry
{
    private const string MutationTagName = "mailfathom.mailbox.mutation";
    private const string AccountTagName = "mailfathom.mail.account";
    private const string FolderAliasTagName = "mailfathom.mail.folder_alias";
    private const string OutcomeTagName = "mailfathom.mailbox.mutation.outcome";

    private readonly ILogger<MailboxMutationTelemetry> logger;
    private readonly TimeProvider timeProvider;
    private readonly Counter<long> mutationCount;
    private readonly Histogram<double> mutationDuration;

    /// <summary>Initializes the instruments every mailbox mutation reports through.</summary>
    /// <param name="logger">Records what happened, and the debug detail of how.</param>
    /// <param name="timeProvider">Measures how long a mutation took.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxMutationTelemetry(ILogger<MailboxMutationTelemetry> logger, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.logger = logger;
        this.timeProvider = timeProvider;
        this.mutationCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mailbox.mutations",
            unit: "{mutation}",
            description: "Changes MailFathom made to a remote mailbox, by mutation and outcome.");
        this.mutationDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mailbox.mutation.duration",
            unit: "s",
            description: "How long a change to a remote mailbox took, by mutation and outcome.");
    }

    /// <summary>Begins reporting one mutation, and returns the scope that finishes the report.</summary>
    /// <param name="mutation">The change the caller asked for, which names the span, the log line, and the counter dimension.</param>
    /// <param name="accountId">The account whose mailbox is being changed.</param>
    /// <param name="folderAlias">The folder the change is performed in.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="MailboxMutationScope.Completed" /> reports a failure.</returns>
    internal MailboxMutationScope Begin(MailboxMutation mutation, MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.BeginFiling(mutation.Name, accountId, folderAlias);

    /// <summary>Begins reporting one operation that is not a mutation of an existing message, under its own name.</summary>
    /// <param name="operationName">The name of the operation, which names the span, the log line, and the counter dimension.</param>
    /// <param name="accountId">The account whose mailbox is being changed.</param>
    /// <param name="folderAlias">The folder the operation is performed in.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="MailboxMutationScope.Completed" /> reports a failure.</returns>
    /// <remarks>
    /// Filing a copy of an outgoing message and taking that copy back out are changes to a mailbox and belong in the
    /// same record as the mutations — an operator asking what MailFathom changed wants one answer rather than two
    /// dashboards. They carry no <see cref="MailboxMutation" /> because they are not one: the permitted mutations are a
    /// closed set of changes to a message that is already there, and neither of these is that.
    /// </remarks>
    internal MailboxMutationScope BeginFiling(
        string operationName,
        MailAccountId accountId,
        MailFolderAlias folderAlias)
    {
        var activity = Telemetry.ActivitySource.StartActivity(operationName, ActivityKind.Client);
        activity?.SetTag(MutationTagName, operationName);
        activity?.SetTag(AccountTagName, accountId.Value);
        activity?.SetTag(FolderAliasTagName, folderAlias.Value);

        this.LogMutationStarted(operationName, accountId.Value, folderAlias.Value);

        return new MailboxMutationScope(
            this,
            operationName,
            accountId,
            folderAlias,
            activity,
            this.timeProvider.GetTimestamp());
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mailbox mutation {Mutation} started for {AccountId}/{FolderAlias}.")]
    private partial void LogMutationStarted(string mutation, string accountId, string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mailbox mutation {Mutation} completed for {AccountId}/{FolderAlias} in {ElapsedMilliseconds} ms.")]
    private partial void LogMutationCompleted(
        string mutation,
        string accountId,
        string folderAlias,
        double elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Mailbox mutation {Mutation} failed for {AccountId}/{FolderAlias} after {ElapsedMilliseconds} ms.")]
    private partial void LogMutationFailed(
        string mutation,
        string accountId,
        string folderAlias,
        double elapsedMilliseconds);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Mailbox mutation {Mutation} for {AccountId}/{FolderAlias} is being carried by the {ProtocolPath} path.")]
    private partial void LogProtocolPathChosen(
        string mutation,
        string accountId,
        string folderAlias,
        string protocolPath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Mailbox mutation {Mutation} for {AccountId}/{FolderAlias} issued {ImapCommand}.")]
    private partial void LogCommandIssued(
        string mutation,
        string accountId,
        string folderAlias,
        string imapCommand);

    internal void RecordProtocolPath(
        string operationName,
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        string protocolPath) =>
        this.LogProtocolPathChosen(operationName, accountId.Value, folderAlias.Value, protocolPath);

    internal void RecordCommand(
        string operationName,
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        string imapCommand) =>
        this.LogCommandIssued(operationName, accountId.Value, folderAlias.Value, imapCommand);

    internal void RecordOutcome(
        string operationName,
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        bool succeeded,
        TimeSpan elapsed)
    {
        var outcome = succeeded ? "success" : "failure";
        var tags = new TagList
        {
            { MutationTagName, operationName },
            { AccountTagName, accountId.Value },
            { FolderAliasTagName, folderAlias.Value },
            { OutcomeTagName, outcome },
        };

        this.mutationCount.Add(1, tags);
        this.mutationDuration.Record(elapsed.TotalSeconds, tags);

        if (succeeded)
        {
            this.LogMutationCompleted(operationName, accountId.Value, folderAlias.Value, elapsed.TotalMilliseconds);
        }
        else
        {
            this.LogMutationFailed(operationName, accountId.Value, folderAlias.Value, elapsed.TotalMilliseconds);
        }
    }

    internal TimeSpan ElapsedSince(long startingTimestamp) =>
        this.timeProvider.GetElapsedTime(startingTimestamp);
}
