// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the operator-asked walk that re-reads a scope's stored MIME.</summary>
/// <remarks>
/// The walk is ordered by the stored email's primary key, which is the only ordering that is total, stable, and already
/// indexed. Both the ordering and the keyset comparison are evaluated by PostgreSQL, so the walk runs under that
/// server's <c>uuid</c> ordering and never has to agree with how the CLR compares two <see cref="Guid" /> values.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailRederivationStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    EmailThreadAssembly threadAssembly)
    : IStoredMailRederivationStore
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public async Task<StoredEmailId?> FindResumePositionAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var owner = scope.Account.Owner.Value;
        var account = scope.Account.Id.Value;
        var folder = KeyedFolderOf(scope);

        var recorded = await dbContext.MailRederivationPositions
            .AsNoTracking()
            .Where(position => position.OwnerId == owner
                && position.MailboxAccountId == account
                && position.FolderAlias == folder)
            .Select(position => (Guid?)position.LastProcessedStoredEmailId)
            .SingleOrDefaultAsync(cancellationToken);

        return recorded is { } lastProcessed ? StoredEmailId.Create(lastProcessed) : null;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize" /> is not positive.</exception>
    public async Task<IReadOnlyList<StoredMailAwaitingRederivation>> GetEmailsToRederiveAsync(
        StoredMailScope scope,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        var candidates = await StoredMailInScope
            .Within(dbContext.StoredEmails.AsNoTracking(), scope)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available)
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(StoredEmailOccurrenceRow.Projection)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredMailAwaitingRederivation(
                StoredEmailId.Create(candidate.Id),
                candidate.ToOccurrenceId())),
        ];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    /// <remarks>
    /// The thread assignment is the one write this pass makes outside the row's own columns, and it is deliberate rather
    /// than an oversight in a contract that is otherwise "re-read the row's MIME into the row". A thread membership
    /// cannot be a column: it is decided from this row's identifiers and recorded as a relation other rows share, so a
    /// mailbox stored before threading existed becomes threaded only if re-derivation is allowed to write it. Everything
    /// the assignment touches is still derived from stored MIME and from nothing a mail server would have to be asked
    /// for.
    /// </remarks>
    public async Task ApplyRederivedMetadataAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "Re-derived metadata cannot be applied to a stored email that no longer exists.");

        StoredEmailMetadataMapping.ApplyExtractedMetadata(storedEmail, metadata);

        await threadAssembly.AssembleAsync(
            session,
            MailAccountIdentity.Create(
                MailOwnerId.Create(storedEmail.OwnerId),
                MailAccountId.Create(storedEmail.MailboxAccountId)),
            ThreadedEmails.Of(storedEmail),
            storedEmail.EmailThreadId is { } currentThreadId ? EmailThreadId.Create(currentThreadId) : null,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public async Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredMailScope scope,
        StoredEmailId position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var account = scope.Account.Id.Value;
        var folder = KeyedFolderOf(scope);

        // FindAsync resolves a row this session already staged from the change tracker, so a pass that commits several
        // batches through one session updates one row rather than inserting a second under the same key.
        var recorded = await sessionContext.MailRederivationPositions.FindAsync(
            [account, folder],
            cancellationToken);

        if (recorded is null)
        {
            sessionContext.MailRederivationPositions.Add(new MailRederivationPositionEntity
            {
                MailboxAccountId = account,

                // Written from the scope the request resolved, which named the owner beside the identifier.
                OwnerId = scope.Account.Owner.Value,
                FolderAlias = folder,
                LastProcessedStoredEmailId = position.Value,
                UpdatedAt = timeProvider.GetUtcNow(),
            });

            return;
        }

        recorded.LastProcessedStoredEmailId = position.Value;
        recorded.UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public async Task ClearResumePositionAsync(
        IPersistenceSession session,
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var recorded = await sessionContext.MailRederivationPositions.FindAsync(
            [scope.Account.Id.Value, KeyedFolderOf(scope)],
            cancellationToken);

        // A scope whose walk finished in one invocation never recorded a position, and clearing one that is not there
        // is the ordinary end of a small mailbox rather than anything to report.
        if (recorded is not null)
        {
            sessionContext.MailRederivationPositions.Remove(recorded);
        }
    }

    /// <summary>Reads the folder value the scope's row is keyed by, which a whole-account walk has its own value for.</summary>
    private static string KeyedFolderOf(StoredMailScope scope) =>
        scope.Folder?.Value ?? MailRederivationPositionEntity.WholeAccountFolder;
}
