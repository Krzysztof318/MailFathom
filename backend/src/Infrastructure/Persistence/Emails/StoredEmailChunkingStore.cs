// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the cut the account run performs once the stages in front of it have finished.</summary>
/// <remarks>
/// The selection carries all four conditions at once, and each of them is one of the arrival pipeline's own orderings.
/// The message has to have been evaluated by the rules, because a rule may move it into a folder mapped differently
/// from the one it arrived in. The classification gate has to admit it, because junk is never derived from and a
/// message still waiting on a verdict is not derived from yet. Its folder has to be one an operator asked to have
/// embedded, which is the same admission every other path that produces passages applies. And it has to have extracted
/// text and no passages, which is what makes the cut the thing that removes it from this query.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailChunkingStore(
    MailFathomDbContext dbContext,
    EmailChunkWriter chunkWriter,
    IMailFolderParticipationReader folderParticipation,
    DerivedWorkGate derivedWorkGate)
    : IStoredEmailChunkingStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Ordering is by the primary key, which is total, stable, and already indexed. No resume position travels with the
    /// batch, because cutting a message is what takes it out of this query: a pass that repeats the read after
    /// committing a batch sees the next messages rather than the ones it just cut.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingChunking>> GetEmailsAwaitingChunkingAsync(
        MailAccountIdentity account,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var ownerId = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        // One snapshot for both halves, exactly as the embedding sweep reads it: the predicate narrows the batch and the
        // answer below names which of the gate's decisions admitted each row, so a second reading taken microseconds
        // later could let the query select a row the answer then reported as still waiting.
        var terms = derivedWorkGate.ReadTerms();

        var candidates = await Selecting(
                dbContext.StoredEmails.AsNoTracking(),
                ownerId,
                mailboxAccountId,
                folderParticipation.FoldersGeneratingEmbeddings,
                terms)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new OutstandingEmailRow(
                email.Id,
                new StoredDerivedWorkCandidateRow(
                    email.MailFolder.MailboxAccountId,
                    email.MailFolder.Alias,
                    email.StoredAt,
                    email.ContentAvailability,
                    email.SpamClassification == null ? null : email.SpamClassification.Verdict)))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(row => new StoredEmailAwaitingChunking(
                StoredEmailId.Create(row.Id),
                row.Candidate.AdmittedUnder(terms))),
        ];
    }

    /// <inheritdoc />
    public async Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        await chunkWriter.SaveFromStoredExtractionAsync(
            await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken),
            storedEmailId,
            cancellationToken);

    /// <summary>Narrows stored mail to the messages the arrival pipeline still owes passages for.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="ownerId">The owner whose account this pass belongs to, which is what the index leads with.</param>
    /// <param name="mailboxAccountId">The configured account this pass belongs to.</param>
    /// <param name="embeddedFolders">The folders a mapping admits to embedding, which is what decides the cut.</param>
    /// <param name="terms">The classification terms the whole batch is decided under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// Written as a composable predicate rather than inline, so what selects a message is one statement that can be
    /// asserted about directly. The clauses are the pipeline's own orderings: the message is still part of the local
    /// mailbox, the rules have finished with it and are not still moving it, its folder is one an operator asked to
    /// have embedded, and classification admits it. The last condition — extracted text present and no passages — is
    /// what the cut removes, which is why the pass needs no cursor.
    /// </para>
    /// <para>
    /// The relocation clause is what makes *the rules ran first* mean anything for a rule that files mail. A rule
    /// declares a move rather than performing one: the record is durable at once and the account's *next* run carries
    /// it to the mail server, so a message filed out of an embedded folder is still sitting in that folder when this
    /// run's cut comes round. Cutting it there would derive passages under the mapping it is leaving and leave them
    /// behind on the row that is carried to the destination. Waiting costs one interval, after which the message is
    /// selected under the mapping it actually ended up in. A relocation that has stopped converging — completed, or
    /// abandoned after its bounded attempts — holds nothing back, since neither will move the message again.
    /// </para>
    /// <para>
    /// <see cref="MailAwaitingRelocation" /> holds which records hold a cut back and which have stopped mattering, and
    /// is read by every path that cuts.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        Guid ownerId,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            emails
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.OwnerId == ownerId
                    && email.MailboxAccountId == mailboxAccountId
                    && !email.Chunks.Any()
                    && email.SearchDocument != null
                    && email.SearchDocument.BodyText != null)
                .Where(MailAwaitingRuleEvaluation.IsFinishedWith)
                .Where(MailAwaitingRelocation.IsSettledWhereItIs),
            embeddedFolders),
        terms);

    /// <summary>One message awaiting the cut, as the walk's projection returns it.</summary>
    private sealed record OutstandingEmailRow(Guid Id, StoredDerivedWorkCandidateRow Candidate);
}
