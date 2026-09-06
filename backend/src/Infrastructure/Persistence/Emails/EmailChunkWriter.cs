// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Cuts one stored email's extracted text into passages and writes them inside the caller's open session.</summary>
/// <remarks>
/// Every path that produces passages arrives here, which is what stops them from storing passages built to different
/// rules. They arrive by two routes. The extraction backfill has just read a message's raw MIME and cuts what it
/// derived, in the transaction that writes the extraction. The account run's own cut and the embedding sweep read the
/// stored reading back through <see cref="SaveFromStoredExtractionAsync" /> and cut it in a transaction of their own,
/// one step behind the stages that may still redact that text or move the message out of the folder its passages would
/// describe. So a committed message is one whose passages a later step produces: storing a message no longer derives
/// them, and the steps that do are what a message committed and not yet cut is waiting for.
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
    /// <summary>Saves the passages the extraction already stored for one message yields, reading that message as it goes.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmailId">The message to cut.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the passages are staged, or immediately when there is no text to cut.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the message disappeared between its selection and this write.</exception>
    /// <remarks>
    /// The text comes from the search document rather than from the raw MIME, which is what keeps this a local write: an
    /// earlier extraction already read the message and stored both the trimmed and the untrimmed reading, redacted by
    /// whatever scanners were switched on, and cutting the stored reading again produces exactly the passages that
    /// message would have been given had it been cut when the reading was taken. A message whose extraction produced no
    /// text is left as it is, and the caller steps past it.
    /// </remarks>
    public async Task SaveFromStoredExtractionAsync(
        MailFathomDbContext dbContext,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var storedEmail = await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Passages cannot be derived for a stored email that no longer exists.");

        var extraction = await dbContext.EmailSearchDocuments
            .Where(document => document.StoredEmailId == storedEmailId.Value)
            .Select(document => new StoredExtractionRow(
                document.TextSource,
                document.BodyTextBeforeTrimming,
                document.BodyText))
            .SingleOrDefaultAsync(cancellationToken);

        if (extraction is null || RestoreExtractedText(extraction) is not { } text)
        {
            return;
        }

        await this.SaveAsync(dbContext, storedEmail, text, cancellationToken);
    }

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

        await this.ReplaceAsync(dbContext, storedEmail, attachmentPosition: null, chunks, cancellationToken);
    }

    /// <summary>Saves the passages one message's attachments yield, leaving its body's passages and its unchanged attachments alone.</summary>
    /// <param name="dbContext">The context whose transaction this write joins.</param>
    /// <param name="storedEmail">The email the passages belong to, tracked or already persisted.</param>
    /// <param name="attachmentTexts">What each attachment yielded, in walk order.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the writes have been issued or staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Each attachment is reconciled against its own stored passages rather than the message's whole set, which is what
    /// makes an attachment arriving after the body cut cost nothing already paid for: the body's digests and ordinals
    /// are untouched, so no vector hanging on them moves. Within one attachment the rule is the message's — an
    /// unchanged reading cut to unchanged rules writes nothing at all, and anything else replaces that attachment's
    /// passages whole, because a boundary change shifts every ordinal after the first difference.
    /// </para>
    /// <para>
    /// An attachment that yielded no words has its passages removed rather than left behind. That is the path a
    /// re-derivation takes when a document that used to parse no longer does, and passages nothing can be traced back
    /// to are exactly what
    /// <see cref="IEmailChunkStore" /> exists to prevent elsewhere.
    /// </para>
    /// </remarks>
    public async Task SaveAttachmentChunksAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        IReadOnlyList<DerivedAttachmentText> attachmentTexts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storedEmail);
        ArgumentNullException.ThrowIfNull(attachmentTexts);

        if (!await this.FolderGeneratesEmbeddingsAsync(dbContext, storedEmail, cancellationToken))
        {
            return;
        }

        foreach (var attachment in attachmentTexts)
        {
            var chunks = attachment.Text is { } text
                ? chunker.DeriveAttachmentChunks(
                    text,
                    rules,
                    inputBound,
                    EmailChunkAttachmentSource.Create(attachment.Position, attachment.DeclaredMediaType)).Chunks
                : [];

            await this.ReplaceAsync(dbContext, storedEmail, attachment.Position, chunks, cancellationToken);
        }
    }

    /// <summary>Replaces the passages of one of a message's texts, writing nothing where they are already what they should be.</summary>
    /// <remarks>
    /// The change-tracker pass comes first for the reason the search document's does: passages staged earlier in this
    /// same uncommitted session are invisible to a set-based delete, and inserting beside them would violate the
    /// ordinal index at commit rather than replace them.
    /// </remarks>
    private async Task ReplaceAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        int? attachmentPosition,
        IReadOnlyList<EmailTextChunk> chunks,
        CancellationToken cancellationToken)
    {
        var staged = FindStaged(dbContext, storedEmail.Id, attachmentPosition);
        if (staged.Length > 0)
        {
            if (Matches(staged, chunks))
            {
                return;
            }

            dbContext.EmailChunks.RemoveRange(staged);
            this.Insert(dbContext, storedEmail, attachmentPosition, chunks);

            return;
        }

        var storedIdentities = await dbContext.EmailChunks
            .Where(candidate => candidate.StoredEmailId == storedEmail.Id
                && candidate.AttachmentPosition == attachmentPosition)
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
            // memory to decide that it is about to be replaced. Narrowed to one text's passages, because the body and
            // each attachment are cut by different passes against different texts: replacing them together would
            // orphan vectors the cut in hand knows nothing about and could not rebuild.
            await dbContext.EmailChunks
                .Where(candidate => candidate.StoredEmailId == storedEmail.Id
                    && candidate.AttachmentPosition == attachmentPosition)
                .ExecuteDeleteAsync(cancellationToken);
        }

        this.Insert(dbContext, storedEmail, attachmentPosition, chunks);
    }

    /// <summary>Reports whether the folder this message was read from is one an operator asked to have embedded.</summary>
    /// <remarks>
    /// The question is which folders a mapping admits rather than which ones it withheld, so a message stored under an
    /// alias configuration does not name is not embedded — the answer an exclusion could never give, since no list of
    /// names carries a folder nobody named. That is also why the alias is always resolved rather than skipped where a
    /// deployment withholds nothing: there is no longer a set whose emptiness means everything. The binding is read from
    /// the message's own row, since the live path arrives with it loaded while both backfills reach a message through
    /// its primary key alone.
    /// </remarks>
    private async Task<bool> FolderGeneratesEmbeddingsAsync(
        MailFathomDbContext dbContext,
        StoredEmailEntity storedEmail,
        CancellationToken cancellationToken)
    {
        var admitted = folderParticipation.FoldersGeneratingEmbeddings;
        if (admitted.Count == 0)
        {
            return false;
        }

        var folderAlias = storedEmail.MailFolder is { } loadedFolder
            ? loadedFolder.Alias
            : await dbContext.MailFolders
                .Where(folder => folder.Id == storedEmail.MailFolderId)
                .Select(folder => folder.Alias)
                .SingleAsync(cancellationToken);

        return AccountScopedMailFolders.Contains(admitted, storedEmail.MailboxAccountId, folderAlias);
    }

    private static EmailChunkEntity[] FindStaged(
        MailFathomDbContext dbContext,
        Guid storedEmailId,
        int? attachmentPosition) =>
        [.. dbContext.EmailChunks.Local
            .Where(candidate => candidate.StoredEmailId == storedEmailId
                && candidate.AttachmentPosition == attachmentPosition)
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
        int? attachmentPosition,
        IReadOnlyList<EmailTextChunk> chunks)
    {
        var derivedAt = timeProvider.GetUtcNow();

        dbContext.EmailChunks.AddRange(chunks.Select(chunk => new EmailChunkEntity
        {
            // Version 7 for the reason every other identifier MailFathom generates is: the passages of one message are
            // written together, so an identifier ordered by creation time keeps them on neighbouring index pages.
            Id = Guid.CreateVersion7(derivedAt),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            Ordinal = chunk.Ordinal,
            AttachmentPosition = attachmentPosition,
            StartOffset = chunk.StartOffset,
            Text = chunk.Text,
            ContentHash = chunk.ContentHash.Value,
            RuleSetVersion = chunk.RuleSetVersion,
            IsDerivedFromLossyHtml = chunk.IsDerivedFromLossyHtml,
            DerivedAt = derivedAt,
        }));
    }

    /// <summary>Rebuilds the extraction the chunker reads from the two readings the search document stored.</summary>
    /// <remarks>
    /// Only the two sources that produced words can be restored, and both readings have to be there: the chunking rules
    /// choose between the trimmed and the untrimmed form, so restoring one of them and inventing the other would cut a
    /// message differently from the same message cut at the moment it was extracted.
    /// </remarks>
    private static ExtractedEmailText? RestoreExtractedText(StoredExtractionRow extraction)
    {
        if (extraction.BodyTextBeforeTrimming is not { } originalText || extraction.BodyText is not { } trimmedText)
        {
            return null;
        }

        return extraction.TextSource switch
        {
            ExtractedEmailTextSource.PlainTextBodyPart => ExtractedEmailText.FromPlainTextBody(originalText, trimmedText),
            ExtractedEmailTextSource.DerivedFromHtmlBodyPart => ExtractedEmailText.DerivedFromHtmlBody(originalText, trimmedText),
            _ => null,
        };
    }

    /// <summary>What a stored passage has to report for re-chunking to decide it is unchanged.</summary>
    private sealed record StoredChunkIdentity(int Ordinal, string ContentHash);

    /// <summary>The stored reading of one message's body, as the chunking projection returns it.</summary>
    private sealed record StoredExtractionRow(
        ExtractedEmailTextSource TextSource,
        string? BodyTextBeforeTrimming,
        string? BodyText);
}
