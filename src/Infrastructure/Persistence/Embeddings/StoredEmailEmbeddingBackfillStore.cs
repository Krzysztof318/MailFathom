// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core state for the sweep that gives pre-existing mail its passages and its vectors.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailEmbeddingBackfillStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    EmailChunkWriter chunkWriter,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate)
    : IStoredEmailEmbeddingBackfillStore
{
    /// <inheritdoc />
    public async Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        var position = await dbContext.BackfillPositions
            .AsNoTracking()
            .Where(candidate => candidate.Name == BackfillPositionEntity.StoredEmailEmbeddingName)
            .Select(candidate => (Guid?)candidate.LastProcessedStoredEmailId)
            .SingleOrDefaultAsync(cancellationToken);

        return position is { } lastProcessed ? StoredEmailId.Create(lastProcessed) : null;
    }

    /// <inheritdoc />
    public Task<int> CountEmailsAwaitingEmbeddingAsync(
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) =>
        this.EmailsAwaitingEmbedding(profileId.Value, derivedWorkGate.ReadTerms()).CountAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Ordering by the primary key gives the keyset comparison an index to walk and a total order that no later write
    /// disturbs, and both the ordering and the comparison are evaluated by PostgreSQL, so the walk runs under that
    /// server's <c>uuid</c> ordering rather than having to agree with how the CLR compares two <see cref="Guid" />
    /// values.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingEmbedding>> GetEmailsAwaitingEmbeddingAsync(
        StoredEmailId? resumeAfter,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        // One snapshot for both halves. The predicate narrows the batch and the answer below names which of the gate's
        // decisions admitted each row, so a second reading taken microseconds later would let the query select a row
        // the answer then reported as still waiting.
        var terms = derivedWorkGate.ReadTerms();
        var candidates = await this.EmailsAwaitingEmbedding(profileId.Value, terms)
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new OutstandingEmailRow(
                email.Id,
                !email.Chunks.Any(),
                email.MailFolder.MailboxAccountId,
                email.MailFolder.Alias,
                email.StoredAt,
                email.ContentAvailability,
                email.SpamClassification == null ? null : email.SpamClassification.Verdict))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingEmbedding(
                StoredEmailId.Create(candidate.Id),
                candidate.RequiresChunking,
                AdmissionOf(terms, candidate))),
        ];
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    /// <remarks>
    /// The text comes from the search document rather than from the raw MIME, which is what keeps this a local write: an
    /// earlier extraction already read the message and stored both the trimmed and the untrimmed reading, and cutting
    /// the stored reading again produces exactly the passages the same message would have been given had chunking
    /// existed when it arrived. A message whose extraction produced no text is left as it is, and the walk steps past
    /// it.
    /// </remarks>
    public Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        chunkWriter.SaveFromStoredExtractionAsync(
            EfCorePersistenceSessionAccessor.DbContextOf(session),
            storedEmailId,
            cancellationToken);

    /// <inheritdoc />
    public async Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId? position,
        CancellationToken cancellationToken)
    {
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // FindAsync resolves a row this session already staged from the change tracker, so a run that commits several
        // positions through one session updates one row rather than inserting a second under the same key.
        var storedPosition = await sessionContext.BackfillPositions.FindAsync(
            [BackfillPositionEntity.StoredEmailEmbeddingName],
            cancellationToken);

        // The sweep ends by removing the row rather than by writing a sentinel into it, so "no position" has exactly one
        // representation and the reader that has never seen this backfill run cannot tell it apart from the one that
        // has finished a pass — which is correct, because both start at the beginning.
        if (position is not { } lastProcessed)
        {
            if (storedPosition is not null)
            {
                sessionContext.BackfillPositions.Remove(storedPosition);
            }

            return;
        }

        var recordedAt = timeProvider.GetUtcNow();

        if (storedPosition is null)
        {
            sessionContext.BackfillPositions.Add(new BackfillPositionEntity
            {
                Name = BackfillPositionEntity.StoredEmailEmbeddingName,
                LastProcessedStoredEmailId = lastProcessed.Value,
                UpdatedAt = recordedAt,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = lastProcessed.Value;
        storedPosition.UpdatedAt = recordedAt;
    }

    /// <summary>Selects the messages that are not current for one profile, whichever of the two reasons applies.</summary>
    /// <remarks>
    /// A message with a passage that carries no vector under this profile was stored before the profile existed, or was
    /// turned away by the live backlog's bound, or was left part-way through by a failed turn. A message with extracted
    /// text and no passages at all is one the arrival path did not cut — because it was stored before chunking existed,
    /// or because classification was holding it — and nothing can be embedded for it until they are cut. A message an
    /// expunge has been observed for is in neither group: vectors nothing may retrieve are a provider bill with no
    /// reader.
    /// </remarks>
    /// <remarks>
    /// The second group carries both of the conditions the account run's own cut waits for, because cutting is what
    /// this walk would do to it: the rule pass's stamp, and no relocation still converging.
    /// <see cref="MailAwaitingRelocation" /> holds the second and why a completed or abandoned one holds nothing
    /// back. The stamp is a
    /// correctness condition rather than a tidiness one: this sweep runs on its own interval while a run is still
    /// fetching a mailbox, so without it a first synchronization would have its mail cut here, by whichever of the two
    /// got there first, before the rules had read a single message. What the stamp costs is nothing — an unevaluated
    /// message is cut a sweep later, by which time the pass that may still move it has run.
    /// </remarks>
    /// <remarks>
    /// The classification narrowing keeps junk out of this walk entirely, and the rule stamp beside it means a message
    /// still waiting on a verdict cannot appear here at all: the rule pass is narrowed by the same admission, so such a
    /// message is never stamped. What releases it is therefore the next account run — its rule pass, and the cut one
    /// step behind that pass — rather than this sweep, which reaches what a run's own batch budget left behind.
    /// </remarks>
    /// <remarks>
    /// The walk is scoped to the folders a mapping admits to embedding, which is the same decision that stops their
    /// passages being cut and the same shape that decision takes everywhere: a folder configuration does not name at
    /// all is outside the walk rather than left in it by an exclusion that could not mention it. Both halves are needed.
    /// The cut is what stops the passages existing; this is what stops the walk finding those messages outstanding on
    /// every sweep for the rest of the deployment's life, since a message with a body and no passages is exactly what
    /// the second group selects.
    /// </remarks>
    private IQueryable<StoredEmailEntity> EmailsAwaitingEmbedding(
        Guid profileId,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            dbContext.StoredEmails
                .AsNoTracking()
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.Chunks.Any(chunk =>
                        !chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId))
                    || (!email.Chunks.Any()
                        && email.RulesEvaluatedAt != null
                        && email.SearchDocument != null
                        && email.SearchDocument.BodyText != null
                        // Composed inline rather than as a second Where, so it narrows the uncut group alone: a message
                        // whose passages already exist and are missing a vector is embedded wherever it is sitting,
                        // because the vectors hang on passages this walk is not deciding whether to derive.
                        && !email.Mutations.Any(mutation =>
                            mutation.Mutation == MailAwaitingRelocation.RelocateMutationName
                            && mutation.Stage != MailboxMutationStage.Completed
                            && mutation.Stage != MailboxMutationStage.Abandoned))),
            folderParticipation.FoldersGeneratingEmbeddings),
        terms);

    /// <summary>Names the answer the predicate above already reached about one selected row.</summary>
    /// <remarks>
    /// The query admits the message; this says which of the gate's answers admitted it, which is the only place a
    /// release is decidable per message. A withheld one never reaches here, so the answer is always an admitting one.
    /// </remarks>
    private static DerivedWorkAdmission AdmissionOf(DerivedWorkAdmissionTerms terms, OutstandingEmailRow candidate) =>
        DerivedWorkGate.Admit(
            terms,
            new DerivedWorkCandidate(
                MailAccountId.Create(candidate.MailboxAccountId),
                MailFolderAlias.Create(candidate.Alias),
                candidate.StoredAt,
                candidate.ContentAvailability,
                candidate.Verdict));

    /// <summary>One outstanding message, as the walk's projection returns it.</summary>
    private sealed record OutstandingEmailRow(
        Guid Id,
        bool RequiresChunking,
        string MailboxAccountId,
        string Alias,
        DateTimeOffset StoredAt,
        StoredEmailContentAvailability ContentAvailability,
        SpamVerdict? Verdict);
}
