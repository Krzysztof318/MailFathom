// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Cuts one stored email's extracted text into passages and writes them inside the caller's open session.</summary>
/// <remarks>
/// Both writers of extracted text use this, for the reason both use the search-document writer: synchronization has
/// just read a message it fetched, the backfill re-derives from raw MIME stored before extraction existed, and one
/// writer is what stops the two paths from storing passages built to different rules. Deriving in the same session as
/// the metadata is what makes a message and its passages durable together, so nothing downstream has to handle a
/// message that is committed and has not been cut.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailChunkWriter(
    IEmailTextChunker chunker,
    EmailChunkingRules rules,
    EmbeddingInputBound inputBound,
    EmailEmbeddingTelemetry telemetry,
    IMailFolderParticipationReader folderParticipation,
    TimeProvider timeProvider)
{
    /// <summary>Saves the passages one extraction yields, leaving an unchanged message's rows untouched.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmail">The email the passages belong to, tracked or already persisted.</param>
    /// <param name="text">The text extraction derived from the message's body.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the write has been issued or staged, or immediately when nothing changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// What decides is the hash. A message whose text and rules have not moved yields the identical ordinals and
    /// digests, and this returns having written nothing — which is what keeps a restart, a repair, or a backfill from
    /// re-doing work already paid for, and what keeps whatever hangs on a passage hanging on the same row. Anything
    /// else replaces the message's passages whole rather than reconciling them one by one, because a boundary change
    /// shifts every ordinal after the first difference and a row-by-row merge would only make that look survivable.
    /// </remarks>
    public async Task SaveAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        ExtractedEmailText text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);
        ArgumentNullException.ThrowIfNull(text);

        // Every path that cuts passages arrives here, which is what makes one check enough: a folder configured not to
        // embed keeps no passages, and a message with no passages reaches no embedding provider however it was stored.
        if (!await this.FolderGeneratesEmbeddingsAsync(dbContext, storedEmail, cancellationToken))
        {
            return;
        }

        var cut = chunker.DeriveChunks(text, rules, inputBound);
        var chunks = cut.Chunks;

        // Recorded before the passages are compared, because the two answers are independent: text that grew past the
        // ceiling while everything up to it stayed identical yields the same passages and a different truncation, and a
        // record written only where rows changed would go on reporting the length the text used to have. Assigning an
        // unchanged value marks nothing modified, so the promise below — that an unchanged message writes nothing at
        // all — survives it.
        this.RecordTruncation(storedEmail, cut);

        // The change-tracker pass comes first for the reason the search document's does: passages staged earlier in
        // this same uncommitted session are invisible to a set-based delete, and inserting beside them would violate
        // the ordinal index at commit rather than replace them.
        var staged = FindStaged(dbContext, storedEmail.Id);
        if (staged.Length > 0)
        {
            if (Matches(staged, chunks))
            {
                return;
            }

            dbContext.EmailChunks.RemoveRange(staged);
            this.Insert(dbContext, storedEmail, cut);

            return;
        }

        var storedIdentities = await dbContext.EmailChunks
            .Where(candidate => candidate.StoredEmailId == storedEmail.Id)
            .OrderBy(candidate => candidate.Ordinal)
            .Select(candidate => new StoredChunkIdentity(candidate.Ordinal, candidate.ContentHash))
            .ToArrayAsync(cancellationToken);

        if (Matches(storedIdentities, chunks))
        {
            return;
        }

        if (storedIdentities.Length > 0)
        {
            // A set-based delete, so re-cutting a message never reads a mailbox's worth of passage text back into
            // memory to decide that it is about to be replaced.
            await dbContext.EmailChunks
                .Where(candidate => candidate.StoredEmailId == storedEmail.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        this.Insert(dbContext, storedEmail, cut);
    }

    /// <summary>Reports whether the folder this message was read from is one an operator asked to have embedded.</summary>
    /// <remarks>
    /// The excluded set is read first so a deployment that excludes nothing — which is every deployment that has not
    /// configured a folder otherwise — pays neither a lookup nor a query. Where something is excluded, the binding is
    /// read from the message's own row: the live path arrives with it loaded, and both backfills reach a message through
    /// its primary key alone, so the alias has to be fetched rather than assumed present.
    /// </remarks>
    private async Task<bool> FolderGeneratesEmbeddingsAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        CancellationToken cancellationToken)
    {
        var excluded = folderParticipation.FoldersWithoutEmbeddings;
        if (excluded.Count == 0)
        {
            return true;
        }

        var folderAlias = storedEmail.MailFolder is { } loadedFolder
            ? loadedFolder.Alias
            : await dbContext.MailFolders
                .Where(folder => folder.Id == storedEmail.MailFolderId)
                .Select(folder => folder.Alias)
                .SingleAsync(cancellationToken);

        return !ExcludedMailFolders.Contains(excluded, storedEmail.MailboxAccountId, folderAlias);
    }

    private static EmailChunkEntity[] FindStaged(MailFathomDbContext dbContext, Guid storedEmailId) =>
        [.. dbContext.EmailChunks.Local
            .Where(candidate => candidate.StoredEmailId == storedEmailId)
            .OrderBy(candidate => candidate.Ordinal)];

    private static bool Matches(IReadOnlyList<EmailChunkEntity> stored, IReadOnlyList<EmailTextChunk> derived) =>
        Matches([.. stored.Select(chunk => new StoredChunkIdentity(chunk.Ordinal, chunk.ContentHash))], derived);

    private static bool Matches(IReadOnlyList<StoredChunkIdentity> stored, IReadOnlyList<EmailTextChunk> derived) =>
        stored.Count == derived.Count
            && stored.Zip(derived).All(pair =>
                pair.First.Ordinal == pair.Second.Ordinal
                && string.Equals(pair.First.ContentHash, pair.Second.ContentHash.Value, StringComparison.Ordinal));

    /// <summary>Records what this cut left out of the message, on the message.</summary>
    /// <remarks>
    /// Written on the row beside its passages rather than reported only as a metric, because a counter says how often
    /// the ceiling bound across a deployment and this says which message it bound on. It is written on every
    /// derivation, including as a clearing of a value a previous cut left behind: a message whose text shrank, or one
    /// re-cut after the ceiling was raised past it, is no longer truncated and its row must not go on saying it is.
    /// </remarks>
    private void RecordTruncation(StoredEmailEntity storedEmail, EmailChunkingResult cut)
    {
        storedEmail.ChunkedTextTruncatedFromCharacterCount = cut.TruncatedFromCharacterCount;

        // Measured against the ceiling rather than against the text that was kept, which the passages overlap and
        // therefore cannot be summed to. The cut lands on the nearest text-element boundary at or below the ceiling, so
        // the figure is short by at most one grapheme — irrelevant beside a message this ceiling reaches at all.
        if (cut.TruncatedFromCharacterCount is { } truncatedFrom)
        {
            telemetry.RecordTruncatedEmbeddingInput(truncatedFrom - inputBound.MaximumCharacterCount);
        }
    }

    private void Insert(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        EmailChunkingResult cut)
    {
        var derivedAt = timeProvider.GetUtcNow();

        dbContext.EmailChunks.AddRange(cut.Chunks.Select(chunk => new EmailChunkEntity
        {
            // Version 7 for the reason every other identifier MailFathom generates is: the passages of one message are
            // written together, so an identifier ordered by creation time keeps them on neighbouring index pages.
            Id = Guid.CreateVersion7(derivedAt),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            Ordinal = chunk.Ordinal,
            StartOffset = chunk.StartOffset,
            Text = chunk.Text,
            ContentHash = chunk.ContentHash.Value,
            RuleSetVersion = chunk.RuleSetVersion,
            IsDerivedFromLossyHtml = chunk.IsDerivedFromLossyHtml,
            DerivedAt = derivedAt,
        }));
    }

    /// <summary>What a stored passage has to report for re-chunking to decide it is unchanged.</summary>
    private sealed record StoredChunkIdentity(int Ordinal, string ContentHash);
}
