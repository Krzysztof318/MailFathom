// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Decides which unique violations are a race a retry can resolve.</summary>
/// <remarks>
/// This is a pure function over the exception a commit raised, deliberately separate from the session that observes it:
/// the list below is the whole of what makes a lost race retryable, and a constraint missing from it fails a deployment
/// rather than any gate. Keeping it reachable is what lets a test state the classification directly.
/// </remarks>
internal static class PersistenceConcurrencyConflicts
{
    /// <summary>Recognizes the inserts a competing writer can win, and nothing else.</summary>
    /// <param name="exception">The exception a commit raised.</param>
    /// <returns><see langword="true" /> when another writer got there first and the caller may retry from a fresh read.</returns>
    /// <remarks>
    /// <para>
    /// Each names a constraint whose violation means "another run got here first" rather than "this data is
    /// wrong": the first checkpoint of a folder, the first binding of an alias to a remote folder, and the account
    /// row that first binding creates on its way. The account is listed because it is part of that same insert:
    /// two runs binding an alias for the first time under an account nothing has stored yet collide on the account
    /// before they ever reach the alias, and reporting only one half of one race would leave the other half an
    /// unhandled failure. Every other unique violation stays a failure, because treating an unnamed collision as a
    /// race would retry a write that will never succeed.
    /// </para>
    /// <para>
    /// The fourth is the mutation identity, and it is the one where losing the race is the mechanism rather than an
    /// accident. Two callers asking for the same change reach the database together, one of them is refused here,
    /// and the retry reads back the record the winner wrote — which is exactly how the same request twice performs
    /// one mutation.
    /// </para>
    /// <para>
    /// The fifth is one audit entry per mutation ending, and it is listed for a reason the others do not have: what
    /// reads the answer is a trail that swallows a failed append and counts it. An append repeated after a commit
    /// whose answer was lost is the benign case that constraint exists for, and leaving it unrecognized would report
    /// it as an entry the trail could not keep — on the very counter that makes swallowing defensible — while the
    /// trail in fact holds exactly the one entry it should.
    /// </para>
    /// <para>
    /// The next two are the embedding profile's, and both are races between two activations. A collision on the
    /// identity fingerprint is the mutation identity's case again: the retry resolves to the profile the winner
    /// registered, which is what makes activating one declaration twice register one row. A collision on the
    /// lifecycle index is two activations of <em>different</em> geometries, where the retry cannot resolve it —
    /// what recognizing it buys is that the loser meets a first-party conflict rather than a provider exception
    /// crossing the application boundary, and the activation turns that into the answer the operator needs, which
    /// is that a different reindex is already running.
    /// </para>
    /// <para>
    /// The next is the mutation identity's case once more, for the account's whole-mailbox rule run: two requests
    /// for an account that has never had one reach the database together, and the retry reads back the run the
    /// winner asked for instead of the second caller starting a second walk of one mailbox.
    /// </para>
    /// <para>
    /// The last two are one message met by two writers, at the two stages that write per passage. The passage
    /// ordinal is two cutters: the account run's cut and the embedding sweep select the same rows and walk them in
    /// the same order, and the sweep runs on its own interval while a run is still fetching, so on a first
    /// synchronization both can reach one message. The embedding row is the same pair one stage later, over the
    /// passages that cut produced. Each reads inside its own transaction, so the one that commits second read
    /// nothing and writes a row the winner already wrote. The retry is exactly right for both: it re-reads the
    /// winner's rows in a fresh session, finds them identical to what it would have written, and updates in place
    /// rather than inserting again.
    /// </para>
    /// <para>
    /// The next is the re-derivation cursor of a scope nobody has walked, and it is the mutation identity's case a
    /// third time: two invocations of one refresh, or one request retried, both read no position and both insert it.
    /// The retry reads back what the winner recorded and moves the walk on from there, so the pass continues rather
    /// than ending on a unique violation the caller can do nothing about.
    /// </para>
    /// <para>
    /// The next is the re-derivation run beside that cursor, and it is the whole-mailbox rule run's case for a scope
    /// rather than for an account: two requests to re-read a scope that has never had a run reach the database
    /// together, and the retry reads back the run the winner asked for. That is what makes asking twice an answer —
    /// the second caller is told the walk is already under way instead of meeting a violation of a key it could not
    /// have known was about to exist.
    /// </para>
    /// <para>
    /// The next is one address claimed by two contacts. Both writers read that nobody holds it, both insert, and the
    /// loser violates the index — which is what makes the retry the answer rather than a repair: it re-reads, finds
    /// the winner's contact holding the address, and reports which contact that is instead of the second caller
    /// putting one person into the book twice.
    /// </para>
    /// <para>
    /// The next is the outgoing email identity, and it is the mutation identity's case with the most at stake. Two
    /// callers asking for the same send reach the database together, one is refused here, and the retry reads back the
    /// winner's record and the message already stored under it — which is how one authored request delivers once. A
    /// collision left unrecognized would surface as a provider failure and leave the caller free to enqueue again, and
    /// a second delivery is the one duplication in this system that cannot be withdrawn afterwards.
    /// </para>
    /// <para>
    /// The last is one message identifier bound to a thread by two arrivals. Two runs storing two messages of one
    /// conversation read that nothing binds the identifier yet, and each assembles a thread and binds it; the loser
    /// violates the key. The retry is what converges them: it re-reads, finds the winner's thread bound to the
    /// identifier, and joins it — so two halves of one conversation reach one thread rather than the second run
    /// failing on a violation it could not have avoided.
    /// </para>
    /// </remarks>
    internal static bool IsConcurrencyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: MailFathomDbContext.SynchronizationCheckpointPrimaryKeyConstraintName
                or MailFathomDbContext.MailFolderBindingUniqueIndexName
                or MailFathomDbContext.MailboxAccountPrimaryKeyConstraintName
                or MailFathomDbContext.MailboxMutationIdentityUniqueIndexName
                or MailFathomDbContext.MailboxMutationAuditEntryMutationUniqueIndexName
                or MailFathomDbContext.EmbeddingProfileFingerprintUniqueIndexName
                or MailFathomDbContext.EmbeddingProfileLifecycleUniqueIndexName
                or MailFathomDbContext.MailRuleEvaluationRunPrimaryKeyConstraintName
                or MailFathomDbContext.EmailChunkOrdinalUniqueIndexName
                or MailFathomDbContext.EmailEmbeddingPrimaryKeyConstraintName
                or MailFathomDbContext.MailRederivationPositionPrimaryKeyConstraintName
                or MailFathomDbContext.MailRederivationRunPrimaryKeyConstraintName
                or MailFathomDbContext.ContactAddressUniqueIndexName
                or MailFathomDbContext.OutgoingEmailIdentityUniqueIndexName
                or MailFathomDbContext.EmailThreadIdentifierPrimaryKeyConstraintName,
        };
}
