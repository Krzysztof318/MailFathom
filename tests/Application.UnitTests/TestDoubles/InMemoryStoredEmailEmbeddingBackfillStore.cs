// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the mail a sweep walks, and decides what is outstanding the way the real query decides it.</summary>
/// <remarks>
/// Hand-written rather than substituted, and deliberately backed by the same <see cref="InMemoryEmailEmbeddingStore" />
/// the generator writes through: what the walk reports as outstanding has to stop being outstanding once vectors have
/// been stored, and a substitute answering each call from a script could not be wrong about that. Cutting a message
/// into passages here is what registers them with the embedding store, so the ordering the backfill promises — passages
/// before vectors — is a fact of this world rather than an assertion about call order.
/// </remarks>
internal sealed class InMemoryStoredEmailEmbeddingBackfillStore : IStoredEmailEmbeddingBackfillStore
{
    private readonly InMemoryEmailEmbeddingStore embeddingStore;
    private readonly List<WalkedEmail> mail = [];
    private readonly List<StoredEmailId?> savedPositions = [];
    private readonly List<StoredEmailId?> requestedResumePositions = [];
    private StoredEmailId? resumePosition;

    /// <summary>Initializes a walk over mail whose vectors live in the given store.</summary>
    public InMemoryStoredEmailEmbeddingBackfillStore(InMemoryEmailEmbeddingStore embeddingStore) =>
        this.embeddingStore = embeddingStore;

    /// <summary>Gets the positions committed so far, a <see langword="null" /> entry being the end of a sweep.</summary>
    public IReadOnlyList<StoredEmailId?> SavedPositions => this.savedPositions;

    /// <summary>Gets the position each batch was asked to continue past, in order.</summary>
    public IReadOnlyList<StoredEmailId?> RequestedResumePositions => this.requestedResumePositions;

    /// <summary>Gets the messages this walk was asked to cut into passages, in order.</summary>
    public List<StoredEmailId> ChunkedEmails { get; } = [];

    /// <summary>Gets or sets a source cancelled once a position has been committed, so a test can stop a run the way a host does.</summary>
    public CancellationTokenSource? CancelWhenPositionSaved { get; set; }

    /// <summary>Adds a message stored before chunking existed: it has extracted text and no passages at all.</summary>
    /// <param name="storedEmailId">The message the walk will find.</param>
    /// <param name="passageCount">How many passages cutting it yields.</param>
    /// <param name="admission">Why the classification gate lets the walk cut it, the default being a deployment that classifies nothing.</param>
    public void AddEmailAwaitingChunking(
        StoredEmailId storedEmailId,
        int passageCount,
        DerivedWorkAdmission admission = DerivedWorkAdmission.Admitted) =>
        this.mail.Add(new WalkedEmail(storedEmailId, passageCount) { Admission = admission });

    /// <summary>Adds a message that already has its passages, none of which carries a vector yet.</summary>
    public void AddEmailAwaitingEmbedding(StoredEmailId storedEmailId, int passageCount)
    {
        this.mail.Add(new WalkedEmail(storedEmailId, passageCount) { IsChunked = true });
        this.embeddingStore.AddPassages(storedEmailId, CreatePassages(storedEmailId, passageCount));
    }

    /// <inheritdoc />
    public Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.resumePosition);
    }

    /// <inheritdoc />
    public async Task<int> CountEmailsAwaitingEmbeddingAsync(
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken)
    {
        var outstanding = 0;

        foreach (var email in this.mail)
        {
            if (await this.IsOutstandingAsync(email, profileId, cancellationToken))
            {
                outstanding++;
            }
        }

        return outstanding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEmailAwaitingEmbedding>> GetEmailsAwaitingEmbeddingAsync(
        StoredEmailId? resumeAfter,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();

        this.requestedResumePositions.Add(resumeAfter);

        var batch = new List<StoredEmailAwaitingEmbedding>(batchSize);

        // A loop rather than a query, because deciding whether a message is outstanding is an await per element and the
        // walk stops as soon as the batch is full.
        foreach (var email in this.mail.Skip(this.IndexAfter(resumeAfter)))
        {
            if (batch.Count == batchSize)
            {
                break;
            }

            if (await this.IsOutstandingAsync(email, profileId, cancellationToken))
            {
                batch.Add(new StoredEmailAwaitingEmbedding(
                    email.StoredEmailId,
                    !email.IsChunked,
                    email.Admission));
            }
        }

        return batch;
    }

    /// <inheritdoc />
    public Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = this.mail.Single(candidate => candidate.StoredEmailId == storedEmailId);
        this.ChunkedEmails.Add(storedEmailId);

        if (email.IsChunked)
        {
            return Task.CompletedTask;
        }

        email.IsChunked = true;
        this.embeddingStore.AddPassages(storedEmailId, CreatePassages(storedEmailId, email.PassageCount));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId? position,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.savedPositions.Add(position);
        this.resumePosition = position;
        this.CancelWhenPositionSaved?.Cancel();

        return Task.CompletedTask;
    }

    private static IReadOnlyList<EmailChunkAwaitingEmbedding> CreatePassages(
        StoredEmailId storedEmailId,
        int passageCount) =>
        [
            .. Enumerable.Range(0, passageCount).Select(ordinal => new EmailChunkAwaitingEmbedding(
                EmailChunkId.Create(Guid.CreateVersion7()),
                $"{storedEmailId.Value} passage {ordinal}")),
        ];

    /// <summary>Answers the two conditions the real query selects on, against the vectors that actually exist.</summary>
    private async Task<bool> IsOutstandingAsync(
        WalkedEmail email,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken)
    {
        if (!email.IsChunked)
        {
            return email.PassageCount > 0;
        }

        var outstanding = await this.embeddingStore.GetChunksAwaitingEmbeddingAsync(
            email.StoredEmailId,
            profileId,
            maxCount: 1,
            cancellationToken);

        return outstanding.Count > 0;
    }

    /// <summary>Finds where a batch continues from, the walk being ordered by the order mail was added.</summary>
    private int IndexAfter(StoredEmailId? resumeAfter) => resumeAfter is { } position
        ? this.mail.FindIndex(candidate => candidate.StoredEmailId == position) + 1
        : 0;

    /// <summary>One stored message, and whether anything has cut it into passages yet.</summary>
    private sealed record WalkedEmail(StoredEmailId StoredEmailId, int PassageCount)
    {
        public bool IsChunked { get; set; }

        public DerivedWorkAdmission Admission { get; init; }
    }
}
