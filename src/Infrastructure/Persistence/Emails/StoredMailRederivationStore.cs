// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
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
internal sealed class StoredMailRederivationStore(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IStoredMailRederivationStore
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public async Task<StoredEmailId?> FindResumePositionAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var account = scope.Account.Value;
        var folder = KeyedFolderOf(scope);

        var recorded = await dbContext.MailRederivationPositions
            .AsNoTracking()
            .Where(position => position.MailboxAccountId == account && position.FolderAlias == folder)
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
            .Select(email => new
            {
                email.Id,
                email.MailFolder.MailboxAccountId,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.UidValidity,
                email.Uid,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredMailAwaitingRederivation(
                StoredEmailId.Create(candidate.Id),
                EmailOccurrenceId.Create(
                    MailAccountId.Create(candidate.MailboxAccountId),
                    new MailFolderResolutionId(
                        MailFolderAlias.Create(candidate.Alias),
                        MailFolderResolutionGeneration.Create(candidate.ResolutionGeneration)),
                    ImapUidValidity.Create(candidate.UidValidity),
                    ImapUid.Create(candidate.Uid)))),
        ];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    public async Task ApplyRederivedMetadataAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "Re-derived metadata cannot be applied to a stored email that no longer exists.");

        StoredEmailMetadataMapping.ApplyExtractedMetadata(storedEmail, metadata);
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

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var account = scope.Account.Value;
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

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var recorded = await sessionContext.MailRederivationPositions.FindAsync(
            [scope.Account.Value, KeyedFolderOf(scope)],
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
