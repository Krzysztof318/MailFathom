// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Appends one finished mutation to its account's audit trail, and never lets that append cost the mutation.</summary>
/// <remarks>
/// <para>
/// The append is a commit of its own, made after the mutation's terminal stage is already durable. That ordering is what
/// the guarantee rests on: by the time this runs, somebody's mailbox has already been changed and the record already
/// says so, so nothing here can roll a change back and nothing here may fail the operation that made it.
/// </para>
/// <para>
/// A failure is therefore swallowed, and swallowing it is only defensible because it is reported — which
/// <see cref="MailboxMutationAuditTelemetry" /> is what does. A cancellation the caller raised is reported the same way
/// and then travels on, because the entry it left unwritten is just as missing as one a database refused and the stage
/// it was owed for is terminal, so no later attempt reaches this point again.
/// </para>
/// <para>
/// What is deliberately not attempted is a retry loop of its own. The optimistic concurrency policy already repeats a
/// conflicted commit, and anything beyond that would be a second retry policy wrapped around a write whose caller is
/// finishing a mail-server operation.
/// </para>
/// </remarks>
public sealed class MailboxMutationAuditTrail : IMailboxMutationAuditTrail
{
    private readonly IMailboxMutationAuditEntryStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly MailboxMutationAuditTelemetry telemetry;

    /// <summary>Initializes the trail from the store it appends to and the channels a refused append is reported on.</summary>
    /// <param name="store">Keeps the entries.</param>
    /// <param name="commitPolicy">Commits the append in a transaction of its own, retrying an optimistic conflict.</param>
    /// <param name="timeProvider">Stamps the instant the mutation ended.</param>
    /// <param name="telemetry">Reports an append that did not happen.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailboxMutationAuditTrail(
        IMailboxMutationAuditEntryStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        MailboxMutationAuditTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.store = store;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.telemetry = telemetry;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The mutation has already changed a remote mailbox and its record already says so; a trail that could fail the operation which produced it would be worse than a reported gap in the history.")]
    public async Task RecordAsync(
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(sourceFolder);

        if (!record.IsAudited)
        {
            return;
        }

        var completedAt = this.timeProvider.GetUtcNow();
        var entry = MailboxMutationAuditEntry.Of(
            MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(completedAt)),
            record,
            sourceFolder,
            completedAt);

        try
        {
            await this.commitPolicy.CommitAsync(
                (session, token) => this.store.AppendAsync(session, entry, token),
                cancellationToken);
        }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            this.telemetry.RecordRefusedAppend(entry, cancellation);

            throw;
        }
        catch (Exception failure)
        {
            this.telemetry.RecordRefusedAppend(entry, failure);
        }
    }
}
