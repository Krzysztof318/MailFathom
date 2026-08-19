// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Records every message an owner asked to have sent again, in PostgreSQL, and what each one last did.</summary>
/// <remarks>
/// <para>
/// The write paths use the context enlisted in the caller's session, so a declaration and the draft it points at are
/// one write; the read paths use the scoped context, because they join no transaction.
/// </para>
/// <para>
/// The idempotency identity is not checked and then written. The check exists — a request that already has a
/// declaration reads it back rather than inserting a second — but it is the unique index that decides, because two
/// callers can pass any application-level check between reading and writing. What a duplicate would cost here is more
/// than a row: two declarations of one repetition send everything twice, every week, until somebody notices.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class RecurringSendStore(MailFathomDbContext readContext, TimeProvider timeProvider)
    : IRecurringSendStore
{
    /// <inheritdoc />
    public async Task<RecurringSend> DeclareAsync(
        IPersistenceSession session,
        RecurringSendRequest request,
        long draftByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(draftByteLength);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        var existing = await FindByIdentityAsync(writeContext, request, cancellationToken);
        if (existing is not null)
        {
            await LoadRecipientsAsync(writeContext, existing, cancellationToken);

            return RecurringSendMapping.ToDeclaration(existing);
        }

        var declaredAt = timeProvider.GetUtcNow();
        var entity = new RecurringSendEntity
        {
            Id = Guid.CreateVersion7(declaredAt),
            MailboxAccountId = request.AccountId.Value,
            RequesterOrigin = request.Requester.Origin,
            RequesterIdentity = request.Requester.Identity,
            Schedule = request.Schedule,
            DraftByteLength = draftByteLength,
            DeclaredAt = declaredAt,
        };

        // Added through the navigation rather than through their own set, so the recipients are inserted with the
        // declaration they belong to and a declaration can never be committed without them.
        foreach (var (recipient, ordinal) in request.Recipients.Select((recipient, ordinal) => (recipient, ordinal)))
        {
            entity.Recipients.Add(new RecurringSendRecipientEntity
            {
                RecurringSendId = entity.Id,
                RecurringSend = entity,
                Ordinal = ordinal,
                Address = recipient.Address.Address,
                ContactId = recipient.Contact?.Value,
                Role = recipient.Role,
            });
        }

        writeContext.RecurringSends.Add(entity);

        return RecurringSendMapping.ToDeclaration(entity);
    }

    /// <inheritdoc />
    public async Task<RecurringSend?> FindAsync(
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken)
    {
        var entity = await readContext.RecurringSends
            .AsNoTracking()
            .Include(declaration => declaration.Recipients)
            .SingleOrDefaultAsync(declaration => declaration.Id == recurringSendId.Value, cancellationToken);

        return entity is null ? null : RecurringSendMapping.ToDeclaration(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Neither the recipients nor the stored draft is read. Both belong to the occasion that composes a message rather
    /// than to the decision that an occasion has come, and this decision is taken on every pass of the job worker — so
    /// the query projects the three columns it reads and joins nothing.
    /// </remarks>
    public async Task<IReadOnlyList<RecurringSendDeclaration>> ReadActiveAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await readContext.RecurringSends
            .AsNoTracking()
            .Where(declaration => declaration.CancelledAt == null)
            .OrderBy(declaration => declaration.DeclaredAt)
            .ThenBy(declaration => declaration.Id)
            .Take(limit)
            .Select(declaration => new
            {
                declaration.Id,
                declaration.MailboxAccountId,
                declaration.Schedule,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new RecurringSendDeclaration(
                RecurringSendId.Create(row.Id),
                MailAccountId.Create(row.MailboxAccountId),
                row.Schedule)),
        ];
    }

    /// <inheritdoc />
    public async Task<RecurringSendCancellation> CancelAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var entity = await writeContext.RecurringSends.FindAsync([recurringSendId.Value], cancellationToken);

        if (entity is null)
        {
            return RecurringSendCancellation.NotFound;
        }

        if (entity.CancelledAt is not null)
        {
            return RecurringSendCancellation.AlreadyCancelled;
        }

        entity.CancelledAt = timeProvider.GetUtcNow();

        return RecurringSendCancellation.Cancelled;
    }

    /// <inheritdoc />
    public async Task RecordOccurrenceAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        DateTimeOffset occurrenceAt,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var entity = await writeContext.RecurringSends.FindAsync([recurringSendId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"Recurring send {recurringSendId} carries no declaration to record an occasion against.");

        // A dispatch that ran late must not take the declaration back to an occasion a later one already passed, and
        // two instances reaching one occasion write the same values — so the occasion only ever moves forwards.
        if (entity.LastOccurrenceAt is { } lastOccurrenceAt && lastOccurrenceAt > occurrenceAt)
        {
            return;
        }

        entity.LastOccurrenceAt = occurrenceAt;
        entity.LastOccurrenceEmailId = outgoingEmailId.Value;
    }

    /// <summary>Finds the declaration this identity already has, looking at what this session is inserting as well.</summary>
    /// <remarks>
    /// The change-tracker pass is what makes a second declaration in one uncommitted session read back the first,
    /// which is the same two-pass shape every alternate-key lookup in this schema uses.
    /// </remarks>
    private static async Task<RecurringSendEntity?> FindByIdentityAsync(
        MailFathomDbContext writeContext,
        RecurringSendRequest request,
        CancellationToken cancellationToken)
    {
        var accountValue = request.AccountId.Value;
        var origin = request.Requester.Origin;
        var identity = request.Requester.Identity;

        bool MatchesIdentity(RecurringSendEntity candidate) =>
            candidate.MailboxAccountId == accountValue
            && candidate.RequesterOrigin == origin
            && candidate.RequesterIdentity == identity;

        var pending = writeContext.RecurringSends.Local.FirstOrDefault(MatchesIdentity);
        if (pending is not null)
        {
            return pending;
        }

        return await writeContext.RecurringSends.SingleOrDefaultAsync(
            candidate => candidate.MailboxAccountId == accountValue
                && candidate.RequesterOrigin == origin
                && candidate.RequesterIdentity == identity,
            cancellationToken);
    }

    /// <summary>Makes the recipient rows available on a declaration the session may have loaded without them.</summary>
    /// <remarks>
    /// A declaration this session inserted carries its recipients already, and one resolved by key from the database
    /// does not, because <c>FindAsync</c> cannot eager-load. Rebuilding one without them would report a repetition
    /// addressed to nobody.
    /// </remarks>
    private static async Task LoadRecipientsAsync(
        MailFathomDbContext writeContext,
        RecurringSendEntity entity,
        CancellationToken cancellationToken)
    {
        if (writeContext.Entry(entity).State == EntityState.Added || entity.Recipients.Count > 0)
        {
            return;
        }

        await writeContext.Entry(entity)
            .Collection(declaration => declaration.Recipients)
            .LoadAsync(cancellationToken);
    }
}
