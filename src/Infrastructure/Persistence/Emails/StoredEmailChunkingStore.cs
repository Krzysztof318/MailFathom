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
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
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
    /// <summary>The stored discriminator of the one mutation that can still change which folder a message is in.</summary>
    private static readonly string RelocateMutationName = MailboxMutation.Relocate.Name;

    /// <inheritdoc />
    /// <remarks>
    /// Ordering is by the primary key, which is total, stable, and already indexed. No resume position travels with the
    /// batch, because cutting a message is what takes it out of this query: a pass that repeats the read after
    /// committing a batch sees the next messages rather than the ones it just cut.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingChunking>> GetEmailsAwaitingChunkingAsync(
        MailAccountId accountId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var mailboxAccountId = accountId.Value;
        // One snapshot for both halves, exactly as the embedding sweep reads it: the predicate narrows the batch and the
        // answer below names which of the gate's decisions admitted each row, so a second reading taken microseconds
        // later could let the query select a row the answer then reported as still waiting.
        var terms = derivedWorkGate.ReadTerms();

        var candidates = await Selecting(
                dbContext.StoredEmails.AsNoTracking(),
                mailboxAccountId,
                folderParticipation.FoldersGeneratingEmbeddings,
                terms)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new OutstandingEmailRow(
                email.Id,
                email.MailFolder.MailboxAccountId,
                email.MailFolder.Alias,
                email.StoredAt,
                email.ContentAvailability,
                email.SpamClassification == null ? null : email.SpamClassification.Verdict))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingChunking(
                StoredEmailId.Create(candidate.Id),
                AdmissionOf(terms, candidate))),
        ];
    }

    /// <inheritdoc />
    public Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) =>
        chunkWriter.SaveFromStoredExtractionAsync(
            EfCorePersistenceSessionAccessor.DbContextOf(session),
            storedEmailId,
            cancellationToken);

    /// <summary>Narrows stored mail to the messages the arrival pipeline still owes passages for.</summary>
    /// <param name="emails">The emails to narrow.</param>
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
    /// Only a relocation is read. A copy leaves this message where it is and its own row is discovered in the
    /// destination and walks the whole pipeline itself, and a pending delete costs at most one cut whose passages the
    /// deletion then cascades away — neither derives anything under the wrong mapping.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> embeddedFolders,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Admitting(
            emails
                .Where(StoredEmailTombstone.IsNotTombstoned)
                .Where(email => email.MailboxAccountId == mailboxAccountId
                    && email.RulesEvaluatedAt != null
                    && !email.Chunks.Any()
                    && email.SearchDocument != null
                    && email.SearchDocument.BodyText != null)
                .Where(email => !email.Mutations.Any(mutation =>
                    mutation.Mutation == RelocateMutationName
                    && mutation.Stage != MailboxMutationStage.Completed
                    && mutation.Stage != MailboxMutationStage.Abandoned)),
            embeddedFolders),
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

    /// <summary>One message awaiting the cut, as the walk's projection returns it.</summary>
    private sealed record OutstandingEmailRow(
        Guid Id,
        string MailboxAccountId,
        string Alias,
        DateTimeOffset StoredAt,
        StoredEmailContentAvailability ContentAvailability,
        SpamVerdict? Verdict);
}
