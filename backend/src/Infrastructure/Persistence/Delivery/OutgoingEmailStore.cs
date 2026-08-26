// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Records every message MailFathom has been asked to send, in PostgreSQL, before anything sends it.</summary>
/// <remarks>
/// <para>
/// The write paths use the context enlisted in the caller's session, so a record is only ever written inside the
/// transaction the caller opened; the read paths use the scoped context, because they join no transaction.
/// </para>
/// <para>
/// <see cref="ClaimAsync" /> and <see cref="MarkUnknownOutcomesAsync" /> are the exception, and are given no session by
/// the port for that reason. Each is one self-contained statement that decides and writes in the same breath and
/// commits on its own — the claim because splitting it would open the window in which two workers take the same send,
/// the sweep because it is a set-based stamp over rows nobody holds. Enlisting either in a caller's transaction would
/// hold the rows it touched for as long as that caller ran, which is precisely what the statements are shaped to avoid.
/// </para>
/// <para>
/// The idempotency identity is not checked and then written. The check exists — a request that already has a record
/// reads it back rather than inserting a second — but it is the unique index that decides, because two callers can pass
/// any application-level check between reading and writing and only the constraint closes that window. A loser is
/// reported as an optimistic conflict, and the retry finds the winner's row. That matters more here than anywhere else
/// in this system: a duplicated local row can be reconciled afterwards, and a duplicated delivery cannot be withdrawn.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutgoingEmailStore(MailFathomDbContext readContext, TimeProvider timeProvider)
    : IOutgoingEmailStore
{
    /// <summary>The stages a send can still move from, which are the ones a level is measured over.</summary>
    /// <remarks>
    /// Written out rather than derived by excluding the terminal ones, so a stage added later has to be placed in this
    /// list deliberately: a new non-terminal stage that nobody counted would be a backlog invisible to every dashboard.
    /// </remarks>
    private static readonly OutgoingEmailStage[] NonTerminalStages =
        [OutgoingEmailStage.Recorded, OutgoingEmailStage.TransmissionBegun];

    /// <inheritdoc />
    public async Task<OpenedOutgoingEmail> OpenAsync(
        IPersistenceSession session,
        OutgoingEmailRequest request,
        OutgoingEmailPrincipal principal,
        long mimeByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mimeByteLength);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var existing = await FindByIdentityAsync(writeContext, request, cancellationToken);
        if (existing is not null)
        {
            // The change-tracker pass can answer with a record this session loaded by key for another write, and such a
            // record carries no recipients and no filings until they are asked for. Rebuilding it without them would
            // report a send addressed to nobody, and one whose copies were never filed — which is what a later pass
            // would act on by filing them a second time.
            await LoadRecipientsAsync(session, existing, cancellationToken);
            await LoadFilingsAsync(session, existing, cancellationToken);

            return OpenedOutgoingEmail.AlreadyRecorded(OutgoingEmailRecordMapping.ToRecord(existing));
        }

        var recordedAt = timeProvider.GetUtcNow();
        var entity = new OutgoingEmailEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            MailboxAccountId = request.Account.Id.Value,

            // Written from the identity the request carried, which the boundary resolved through the catalog before
            // anything was composed. A send belongs to the owner whose account it goes out as.
            OwnerId = request.Account.Owner.Value,
            RequesterOrigin = request.Requester.Origin,
            RequesterIdentity = request.Requester.Identity,
            PrincipalFingerprint = principal.Fingerprint,
            Stage = OutgoingEmailStage.Recorded,
            MimeByteLength = mimeByteLength,
            AttemptCount = 0,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,

            // The instant a claim may first take the record is the instant the author asked the message to leave at,
            // which is the whole of how a message is held: the claim already compares this column, so a send written
            // for Monday needs no state of its own and no second predicate to be skipped by every pass until Monday.
            AvailableAt = request.DueAt?.Instant ?? recordedAt,
            DueAt = request.DueAt?.Instant,
            DueZoneId = request.DueAt?.ZoneId,
        };

        // Added through the navigation rather than through their own set, so the recipients are inserted with the
        // record they belong to and a record can never be committed without them.
        foreach (var (recipient, ordinal) in request.Recipients.Select((recipient, ordinal) => (recipient, ordinal)))
        {
            entity.Recipients.Add(new OutgoingEmailRecipientEntity
            {
                OutgoingEmailId = entity.Id,
                OutgoingEmail = entity,
                Ordinal = ordinal,
                Address = recipient.Address.Address,
                ContactId = recipient.Contact?.Value,
                Role = recipient.Role,
                Status = OutgoingRecipientStatus.Pending,
            });
        }

        writeContext.OutgoingEmails.Add(entity);

        return OpenedOutgoingEmail.RecordedNow(OutgoingEmailRecordMapping.ToRecord(entity));
    }

    /// <inheritdoc />
    public async Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await readContext.OutgoingEmails
            .AsNoTracking()
            .Include(message => message.Recipients)
            .Include(message => message.Filings)
            .SingleOrDefaultAsync(message => message.Id == outgoingEmailId.Value, cancellationToken);

        return entity is null ? null : OutgoingEmailRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stored MIME is deliberately not included. Listing what is queued must not pull every queued message's bytes
    /// into memory, and an attempt that is going to transmit one reads it through the content store by identifier.
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        MailAccountIdentity account,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var ownerValue = account.Owner.Value;
        var accountValue = account.Id.Value;

        var entities = await readContext.OutgoingEmails
            .AsNoTracking()
            .Include(message => message.Recipients)
            .Include(message => message.Filings)
            .Where(message => message.OwnerId == ownerValue
                && message.MailboxAccountId == accountValue
                && message.Stage != OutgoingEmailStage.Sent
                && message.Stage != OutgoingEmailStage.Refused
                && message.Stage != OutgoingEmailStage.Cancelled)
            .OrderBy(message => message.RecordedAt)
            .ThenBy(message => message.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(OutgoingEmailRecordMapping.ToRecord)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Grouped in the database rather than counted here, so the rows themselves never leave it: what crosses is one
    /// number per stage. The predicate is the one the outstanding index is filtered on, which is what keeps the read
    /// off the history of everything this deployment has ever sent.
    /// </remarks>
    public async Task<IReadOnlyList<OutboxStageCount>> CountOutstandingByStageAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var ownerValue = account.Owner.Value;
        var accountValue = account.Id.Value;

        var counted = await readContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => message.OwnerId == ownerValue
                && message.MailboxAccountId == accountValue
                && message.Stage != OutgoingEmailStage.Sent
                && message.Stage != OutgoingEmailStage.Refused
                && message.Stage != OutgoingEmailStage.Cancelled)
            .GroupBy(message => message.Stage)
            .Select(group => new { Stage = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        var countsByStage = counted.ToDictionary(group => group.Stage, group => group.Count);

        // Every non-terminal stage is answered for, zeros included, because a stage that vanished when it emptied would
        // leave whoever publishes the level unable to tell a drained account from one nothing measured.
        return
        [
            .. NonTerminalStages.Select(stage => new OutboxStageCount(
                stage,
                countsByStage.TryGetValue(stage, out var count) ? count : 0)),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The statement stamps and the query that follows reads: the claim has already decided which rows are this
    /// attempt's, so reading them back needs no lock and joins no transaction. The lease returned is the one the
    /// statement wrote rather than one read back from the rows, because those are the same values and a second read
    /// would be a second chance for them to disagree.
    /// </remarks>
    public async Task<IReadOnlyList<ClaimedOutgoingEmail>> ClaimAsync(
        OutgoingEmailClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var claimedAt = timeProvider.GetUtcNow();

        var claimedIds = await readContext.Database
            .SqlQuery<Guid>(OutgoingEmailClaimStatement.Compose(request, claimedAt))
            .ToArrayAsync(cancellationToken);

        if (claimedIds.Length == 0)
        {
            return [];
        }

        var claimed = await readContext.OutgoingEmails
            .AsNoTracking()
            .Include(message => message.Recipients)
            .Include(message => message.Filings)
            .Where(message => claimedIds.Contains(message.Id))
            .OrderBy(message => message.AvailableAt)
            .ThenBy(message => message.Id)
            .ToArrayAsync(cancellationToken);

        var lease = new OutgoingEmailLease(request.Owner, claimedAt + request.LeaseDuration);

        return [.. claimed.Select(entity => new ClaimedOutgoingEmail(OutgoingEmailRecordMapping.ToRecord(entity), lease))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A set-based update rather than a read and a write, because it is a stamp on rows nobody is working on and the
    /// answer is a count rather than the records themselves.
    /// </para>
    /// <para>
    /// A record whose lease has not run out is deliberately left alone. An attempt at this stage is one transmitting
    /// right now, and stamping its record would race the answer that attempt is about to write onto it; the attempt is
    /// cancelled before its lease can expire, so a lease that has expired here belongs to nobody.
    /// </para>
    /// <para>
    /// The code a record already carries is overwritten unless it is this one. A failure recorded here belongs to an
    /// earlier attempt that ended and gave the record back, so leaving it would tell an operator that a message which
    /// may have been delivered was merely deferred; skipping the records already marked is what keeps the pass
    /// idempotent and its count honest.
    /// </para>
    /// </remarks>
    public Task<int> MarkUnknownOutcomesAsync(MailAccountIdentity account, CancellationToken cancellationToken)
    {
        var ownerValue = account.Owner.Value;
        var accountValue = account.Id.Value;
        var markedAt = timeProvider.GetUtcNow();
        var unknownOutcome = MailFathomErrorCode.OutgoingEmailOutcomeUnknown.Value;

        return readContext.OutgoingEmails
            .Where(message => message.OwnerId == ownerValue
                && message.MailboxAccountId == accountValue
                && message.Stage == OutgoingEmailStage.TransmissionBegun
                && (message.LastFailureCode == null || message.LastFailureCode != unknownOutcome)
                && (message.LeaseExpiresAt == null || message.LeaseExpiresAt <= markedAt))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.LastFailureCode,
                    MailFathomErrorCode.OutgoingEmailOutcomeUnknown.Value),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeferAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        DateTimeOffset availableAt,
        MailFathomErrorCode? failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        RequireNotTerminal(entity);

        // The one place a stage moves backwards. The caller reached here having established that the message reached
        // nobody it will be offered to again, which is what makes offering it again safe.
        entity.Stage = OutgoingEmailStage.Recorded;
        entity.StageChangedAt = timeProvider.GetUtcNow();
        entity.AvailableAt = availableAt;
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;

        if (failure is not null)
        {
            entity.LastFailureCode = failure.Value.Value;
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        RequireNotTerminal(entity);

        entity.Stage = OutgoingEmailStage.Recorded;
        entity.StageChangedAt = timeProvider.GetUtcNow();
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;

        // The attempt the claim counted never reached a submission server, so it is given back with the record. A
        // rolling restart would otherwise spend a send's whole budget on restarts rather than on failures.
        entity.AttemptCount = Math.Max(0, entity.AttemptCount - 1);
    }

    /// <inheritdoc />
    public async Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        if (entity.Stage != OutgoingEmailStage.Recorded)
        {
            throw new InvalidOperationException(
                $"Outgoing email record {entity.Id} is at stage {entity.Stage}, and a transmission begins from {OutgoingEmailStage.Recorded}.");
        }

        entity.Stage = OutgoingEmailStage.TransmissionBegun;
        entity.StageChangedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        OutgoingEmailStage stage,
        int? replyCode,
        CancellationToken cancellationToken)
    {
        if (stage is not (OutgoingEmailStage.Sent or OutgoingEmailStage.Refused or OutgoingEmailStage.Cancelled))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "An outgoing email is advanced to a terminal stage; every other stage has a transition of its own.");
        }

        // RFC 5321 makes every reply exactly three digits, so anything else is a value assembled wrongly rather than a
        // server this system has not met. It is refused before it is durable, because the record is read afterwards by
        // an operator deciding whether a send is worth looking into.
        if (replyCode is not null and (< 100 or > 599))
        {
            throw new ArgumentOutOfRangeException(
                nameof(replyCode),
                replyCode,
                "An SMTP reply code is a three-digit number.");
        }

        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        RequireNotTerminal(entity);
        RequireReachable(entity, stage);

        entity.Stage = stage;
        entity.StageChangedAt = timeProvider.GetUtcNow();

        // A finished send is claimed by nothing, so the lease it is released from is bookkeeping rather than safety —
        // and a terminal row still holding one would read as a send an attempt is working on.
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;

        if (replyCode is not null)
        {
            entity.LastReplyCode = replyCode;
        }
    }

    /// <inheritdoc />
    public async Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        // A record that has finished is answered about by nobody: a late or repeated reply reaching one would settle a
        // recipient on a send that stopped, and on a cancelled record it would claim an answer to a transmission the
        // stage says never began.
        RequireNotTerminal(entity);

        await LoadRecipientsAsync(session, entity, cancellationToken);

        foreach (var outcome in outcomes)
        {
            Apply(entity, outcome);
        }

        // An answer writes recipient rows and nothing on the record, so without this the record's own token would never
        // reach a `WHERE` clause and the stage read above would be all that guards the write — which a competing run
        // advancing the same record to a terminal stage can invalidate between that read and this commit. Marking a
        // column of the record modified puts its `xmin` into the update the same way every other write here does; the
        // value written is the one just read, and the loser meets the ordinary conflict instead of settling a recipient
        // on a send that has finished.
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        writeContext
            .Entry(entity)
            .Property(outgoingEmail => outgoingEmail.StageChangedAt)
            .IsModified = true;
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireLeasedEntityAsync(session, lease, outgoingEmailId, cancellationToken);

        // The failure a finished record carries is the one that finished it, so a later caller does not get to
        // overwrite what an operator reads as the reason this send ended.
        RequireNotTerminal(entity);

        entity.LastFailureCode = failure.Value;
    }

    /// <summary>Writes one answer onto the recipient it is about, leaving a recipient already settled alone.</summary>
    /// <remarks>
    /// Matched by the normalized address, which is what makes a server answering about <c>Anna@example.test</c> settle
    /// the recipient recorded as <c>anna@example.test</c>. An answer about somebody the record does not name is refused
    /// rather than added: the recipient list is what the message was authored for, and growing it here would offer the
    /// message to somebody nobody asked to write to.
    /// </remarks>
    private static void Apply(OutgoingEmailEntity entity, OutgoingRecipientOutcome outcome)
    {
        var normalizedAddress = outcome.Recipient.Address.NormalizedAddress;
        var recipient = entity.Recipients.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Address.ToUpperInvariant(),
                normalizedAddress,
                StringComparison.Ordinal));

        if (recipient is null)
        {
            // The address stays out of the message: it is personal data, and the record identifies the send exactly.
            throw new InvalidOperationException(
                $"Outgoing email record {entity.Id} was answered about a recipient it does not name.");
        }

        if (recipient.Status != OutgoingRecipientStatus.Pending)
        {
            return;
        }

        recipient.Status = outcome.Status;
        recipient.LastReplyCode = outcome.LastReplyCode;
        recipient.AnsweredAt = outcome.AnsweredAt;
    }

    /// <summary>Refuses a terminal stage that does not follow the stage the record actually reached.</summary>
    /// <remarks>
    /// Two of the three are reachable from one stage only, and both restrictions are the same guarantee read from
    /// either end of the unknown window. A send is <see cref="OutgoingEmailStage.Sent" /> only after a recorded
    /// transmission, so no record claims a delivery nothing could have produced; a send is
    /// <see cref="OutgoingEmailStage.Cancelled" /> only before one, so no record claims a withdrawal after bytes that
    /// may already have reached somebody. What is left for a message stopped mid-transmission is
    /// <see cref="OutgoingEmailStage.Refused" />, which says nothing more will be attempted and claims nothing about
    /// what the recipients received.
    /// </remarks>
    private static void RequireReachable(OutgoingEmailEntity entity, OutgoingEmailStage stage)
    {
        var isReachable = stage switch
        {
            OutgoingEmailStage.Sent => entity.Stage == OutgoingEmailStage.TransmissionBegun,
            OutgoingEmailStage.Cancelled => entity.Stage == OutgoingEmailStage.Recorded,
            _ => true,
        };

        if (!isReachable)
        {
            throw new InvalidOperationException(
                $"Outgoing email record {entity.Id} is at stage {entity.Stage} and cannot be moved to {stage}.");
        }
    }

    /// <summary>Refuses a write against a record nothing attempts again.</summary>
    private static void RequireNotTerminal(OutgoingEmailEntity entity)
    {
        if (entity.Stage is OutgoingEmailStage.Sent
            or OutgoingEmailStage.Refused
            or OutgoingEmailStage.Cancelled)
        {
            throw new InvalidOperationException(
                $"Outgoing email record {entity.Id} is at terminal stage {entity.Stage} and is never attempted again.");
        }
    }

    /// <summary>Makes the recipient rows available on a record the session may have loaded without them.</summary>
    /// <remarks>
    /// A record this session inserted carries its recipients already, and one resolved by key from the database does
    /// not, because <c>FindAsync</c> cannot eager-load. The pending record is recognized by its own state rather than
    /// by whether the collection happens to hold anything, so a record still being inserted is never queried for rows
    /// that cannot exist yet.
    /// </remarks>
    private static async Task LoadRecipientsAsync(
        IPersistenceSession session,
        OutgoingEmailEntity entity,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var entry = writeContext.Entry(entity);

        if (entry.State == EntityState.Added)
        {
            return;
        }

        var recipients = entry.Collection(message => message.Recipients);
        if (!recipients.IsLoaded)
        {
            await recipients.LoadAsync(cancellationToken);
        }
    }

    /// <summary>Makes the filing rows available on a record the session may have loaded without them.</summary>
    /// <remarks>
    /// The recipients' case exactly, one collection over: a record resolved from the change tracker carries whatever an
    /// earlier write in this session asked for, and a record mapped without its filings reports that nothing was ever
    /// filed for it. What acts on that answer is the pass deciding whether to append a copy, so the omission would put
    /// a second copy of one send in the owner's own mailbox rather than merely under-reporting.
    /// </remarks>
    private static async Task LoadFilingsAsync(
        IPersistenceSession session,
        OutgoingEmailEntity entity,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var entry = writeContext.Entry(entity);

        if (entry.State == EntityState.Added)
        {
            return;
        }

        var filings = entry.Collection(message => message.Filings);
        if (!filings.IsLoaded)
        {
            await filings.LoadAsync(cancellationToken);
        }
    }

    private static async Task<OutgoingEmailEntity?> FindByIdentityAsync(
        MailFathomDbContext writeContext,
        OutgoingEmailRequest request,
        CancellationToken cancellationToken)
    {
        var ownerValue = request.Account.Owner.Value;
        var accountValue = request.Account.Id.Value;
        var origin = request.Requester.Origin;
        var identity = request.Requester.Identity;

        // Looked up by the idempotency identity rather than by the key, so the change-tracker pass is explicit: a
        // request opened earlier in this same uncommitted session would be invisible to a query. The database pass
        // carries the recipients, because a record read back is a record about to be returned whole.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.OutgoingEmails,
            writeContext.OutgoingEmails
                .Include(message => message.Recipients)
                .Include(message => message.Filings),
            message => message.OwnerId == ownerValue
                && message.MailboxAccountId == accountValue
                && message.RequesterOrigin == origin
                && message.RequesterIdentity == identity,
            cancellationToken);
    }

    /// <summary>Loads the record and refuses the write when it is no longer this attempt's.</summary>
    /// <remarks>
    /// It is the compare half of the compare-and-set a lease is. The other half is that an attempt runs under a timeout
    /// strictly shorter than the lease it holds, so a live attempt is cancelled before its lease can expire; this is
    /// what catches the case where it did anyway — a paused process, a clock that moved — and stops a late writer from
    /// recording an outcome over the attempt that replaced it.
    /// </remarks>
    private static async Task<OutgoingEmailEntity> RequireLeasedEntityAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

        if (entity.LeaseOwner != lease.Owner)
        {
            throw new OutgoingEmailLeaseLostException(outgoingEmailId, lease.Owner);
        }

        return entity;
    }

    private static async Task<OutgoingEmailEntity> RequireEntityAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // A primary-key lookup, so FindAsync already resolves an insert this session may still be holding.
        return await writeContext.OutgoingEmails.FindAsync([outgoingEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No outgoing email record carries the identifier {outgoingEmailId}.");
    }
}
