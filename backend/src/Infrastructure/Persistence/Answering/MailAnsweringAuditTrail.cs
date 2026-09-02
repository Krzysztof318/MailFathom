// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.Persistence.Answering;

/// <summary>Writes what one finished answering run read, and never lets that write cost the answer it describes.</summary>
/// <remarks>
/// <para>
/// The append happens after the run has ended and after the answer has been produced. That ordering is what the
/// guarantee rests on: by the time this runs there is nothing left to roll back, so nothing here may fail the question
/// that produced it.
/// </para>
/// <para>
/// A failure is therefore swallowed, and swallowing it is only defensible because it is reported — which
/// <see cref="MailAnsweringAuditTelemetry" /> is what does. A cancellation the caller raised is reported the same way
/// and then travels on, because the record it left unwritten is just as missing as one a database refused and no later
/// attempt reaches this point again.
/// </para>
/// <para>
/// What is deliberately not attempted is a retry loop of its own. The optimistic concurrency policy already repeats a
/// conflicted commit, and anything beyond that would be a second retry policy wrapped around a write whose caller is
/// finishing somebody's request.
/// </para>
/// </remarks>
public sealed class MailAnsweringAuditTrail : IMailAnsweringAuditTrail
{
    private readonly IMailAnsweringAuditSettingsReader settingsReader;
    private readonly IMailAnsweringAuditEntryStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailAnsweringAuditTelemetry telemetry;

    /// <summary>Initializes the record from the settings that decide who owes one and the store it appends to.</summary>
    /// <param name="settingsReader">Answers which accounts asked for a record to be kept.</param>
    /// <param name="store">Keeps the entries.</param>
    /// <param name="commitPolicy">Commits the append in a transaction of its own, retrying an optimistic conflict.</param>
    /// <param name="telemetry">Reports an append that did not happen.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailAnsweringAuditTrail(
        IMailAnsweringAuditSettingsReader settingsReader,
        IMailAnsweringAuditEntryStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailAnsweringAuditTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.settingsReader = settingsReader;
        this.store = store;
        this.commitPolicy = commitPolicy;
        this.telemetry = telemetry;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The run is over and the answer has already been produced; a record that could fail the question which produced it would be worse than a reported gap in the history.")]
    public async Task RecordAsync(MailAnsweringRunObservation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var entries = this.OwedEntries(observation);

        if (entries.Count is 0)
        {
            return;
        }

        try
        {
            // One transaction for the whole run rather than one per account, so a question asked across two mailboxes
            // is recorded for both or for neither. A record that held half a run would be worse than none, because
            // nothing about the entries that landed would say the others were missing.
            await this.commitPolicy.CommitAsync(
                async (session, token) =>
                {
                    foreach (var entry in entries)
                    {
                        await this.store.AppendAsync(session, entry, token);
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            this.telemetry.RecordRefusedAppend(observation, entries.Count, cancellation);

            throw;
        }
        catch (Exception failure)
        {
            this.telemetry.RecordRefusedAppend(observation, entries.Count, failure);
        }
    }

    /// <summary>Builds the entry each account in the run's scope is owed, and nothing for the accounts that asked for none.</summary>
    /// <remarks>
    /// Every account in scope rather than only the ones the run drew mail from, because "this question was asked of my
    /// mailbox and took nothing out of it" is a recorded fact rather than a missing one — and a record that appeared
    /// only when mail was found could not answer whether a mailbox had been queried at all.
    /// </remarks>
    private IReadOnlyList<MailAnsweringAuditEntry> OwedEntries(MailAnsweringRunObservation observation)
    {
        var recordingAccountIds = observation.Scope.AccountIds
            .Where(accountId => this.settingsReader.GetAnsweringAuditSettings(accountId).IsEnabled)
            .ToArray();

        if (recordingAccountIds.Length is 0)
        {
            return [];
        }

        var citedEmailIds = observation.CitedEmailIds.ToHashSet();
        var retrievedByAccount = observation.Retrieval.Passages

            // One row per message however many of the run's lookups found it, because the record names what was read
            // rather than how often it came back.
            .DistinctBy(static passage => passage.StoredEmailId)
            .GroupBy(static passage => passage.AccountId)
            .ToDictionary(
                static byAccount => byAccount.Key,
                byAccount => Audited(byAccount, citedEmailIds));

        return
        [
            .. recordingAccountIds.Select(accountId => new MailAnsweringAuditEntry
            {
                Id = MailAnsweringAuditEntryId.Create(Guid.CreateVersion7(observation.CompletedAt)),
                RunId = observation.RunId,
                Account = MailAccountIdentity.Create(observation.Scope.Owner, accountId),
                Emails = retrievedByAccount.GetValueOrDefault(accountId, []),
                ChatEndpointAlias = observation.ChatEndpointAlias,
                InstructionsVersion = observation.InstructionsVersion,
                StartedAt = observation.StartedAt,
                CompletedAt = observation.CompletedAt,
                Outcome = observation.Outcome,
                Degradation = observation.Retrieval.Degradation,
            }),
        ];
    }

    /// <summary>Numbers one account's retrieved emails in the order the run reached them, and marks the ones it cited.</summary>
    /// <remarks>
    /// The position is within this account's own list rather than across the run, so the numbers an entry carries are
    /// contiguous when it is written — which is what makes a gap in them afterwards mean that a message was erased.
    /// </remarks>
    private static IReadOnlyList<MailAnsweringAuditedEmail> Audited(
        IEnumerable<EmailKnowledgePassage> retrieved,
        HashSet<StoredEmailId> citedEmailIds) =>
    [
        .. retrieved.Select((passage, position) => new MailAnsweringAuditedEmail(
            passage.StoredEmailId,
            position,
            citedEmailIds.Contains(passage.StoredEmailId))),
    ];
}
