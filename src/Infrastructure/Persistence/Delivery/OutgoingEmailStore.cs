// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
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
    /// <inheritdoc />
    public async Task<OutgoingEmailRecord> OpenAsync(
        IPersistenceSession session,
        OutgoingEmailRequest request,
        long mimeByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mimeByteLength);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        var existing = await FindByIdentityAsync(writeContext, request, cancellationToken);
        if (existing is not null)
        {
            // The change-tracker pass can answer with a record this session loaded by key for another write, and such a
            // record carries no recipients until they are asked for. Rebuilding it without them would report a send
            // addressed to nobody rather than the one that is already there.
            await LoadRecipientsAsync(session, existing, cancellationToken);

            return OutgoingEmailRecordMapping.ToRecord(existing);
        }

        var recordedAt = timeProvider.GetUtcNow();
        var entity = new OutgoingEmailEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            MailboxAccountId = request.AccountId.Value,
            RequesterOrigin = request.Requester.Origin,
            RequesterIdentity = request.Requester.Identity,
            Stage = OutgoingEmailStage.Recorded,
            MimeByteLength = mimeByteLength,
            AttemptCount = 0,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
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
                Role = recipient.Role,
                Status = OutgoingRecipientStatus.Pending,
            });
        }

        writeContext.OutgoingEmails.Add(entity);

        return OutgoingEmailRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await readContext.OutgoingEmails
            .AsNoTracking()
            .Include(message => message.Recipients)
            .SingleOrDefaultAsync(message => message.Id == outgoingEmailId.Value, cancellationToken);

        return entity is null ? null : OutgoingEmailRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stored MIME is deliberately not included. Listing what is queued must not pull every queued message's bytes
    /// into memory, and an attempt that is going to transmit one reads it through the content store by identifier.
    /// </remarks>
    public async Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = accountId.Value;

        var entities = await readContext.OutgoingEmails
            .AsNoTracking()
            .Include(message => message.Recipients)
            .Where(message => message.MailboxAccountId == accountValue
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
    public async Task<int> CountAttemptAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

        RequireNotTerminal(entity);

        entity.AttemptCount++;

        return entity.AttemptCount;
    }

    /// <inheritdoc />
    public async Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

        if (entity.Stage != OutgoingEmailStage.Recorded)
        {
            throw new InvalidOperationException(
                $"Outgoing message record {entity.Id} is at stage {entity.Stage}, and a transmission begins from {OutgoingEmailStage.Recorded}.");
        }

        entity.Stage = OutgoingEmailStage.TransmissionBegun;
        entity.StageChangedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        IPersistenceSession session,
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
                "An outgoing message is advanced to a terminal stage; every other stage has a transition of its own.");
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

        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

        RequireNotTerminal(entity);
        RequireReachable(entity, stage);

        entity.Stage = stage;
        entity.StageChangedAt = timeProvider.GetUtcNow();

        if (replyCode is not null)
        {
            entity.LastReplyCode = replyCode;
        }
    }

    /// <inheritdoc />
    public async Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

        await LoadRecipientsAsync(session, entity, cancellationToken);

        foreach (var outcome in outcomes)
        {
            Apply(entity, outcome);
        }
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, outgoingEmailId, cancellationToken);

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
                $"Outgoing message record {entity.Id} was answered about a recipient it does not name.");
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
                $"Outgoing message record {entity.Id} is at stage {entity.Stage} and cannot be moved to {stage}.");
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
                $"Outgoing message record {entity.Id} is at terminal stage {entity.Stage} and is never attempted again.");
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
        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
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

    private static async Task<OutgoingEmailEntity?> FindByIdentityAsync(
        MailFathomDbContext writeContext,
        OutgoingEmailRequest request,
        CancellationToken cancellationToken)
    {
        var accountValue = request.AccountId.Value;
        var origin = request.Requester.Origin;
        var identity = request.Requester.Identity;

        // Looked up by the idempotency identity rather than by the key, so the change-tracker pass is explicit: a
        // request opened earlier in this same uncommitted session would be invisible to a query. The database pass
        // carries the recipients, because a record read back is a record about to be returned whole.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.OutgoingEmails,
            writeContext.OutgoingEmails.Include(message => message.Recipients),
            message => message.MailboxAccountId == accountValue
                && message.RequesterOrigin == origin
                && message.RequesterIdentity == identity,
            cancellationToken);
    }

    private static async Task<OutgoingEmailEntity> RequireEntityAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // A primary-key lookup, so FindAsync already resolves an insert this session may still be holding.
        return await writeContext.OutgoingEmails.FindAsync([outgoingEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No outgoing message record carries the identifier {outgoingEmailId}.");
    }
}
