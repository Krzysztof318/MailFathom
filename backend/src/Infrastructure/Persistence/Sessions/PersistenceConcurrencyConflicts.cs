// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// The next two are the mutation identity's case once more, for the account's whole-mailbox runs — the rule run
    /// and the classification run, each keyed by the account because an account has one of either outstanding. Two
    /// requests for an account that has never had one reach the database together, and the retry reads back the run
    /// the winner asked for instead of the second caller starting a second walk of one mailbox. The two are listed
    /// together because they are one shape: a run table keyed by the account is exactly the write that loses this
    /// race, so the next one added belongs here on the day its key does.
    /// </para>
    /// <para>
    /// The next is the one classification an occurrence carries, and it is that convergence a message at a time: an
    /// arrival classifies an occurrence while a run's pass or a reclassification reaches the same one, both read that
    /// nothing is recorded, and the loser violates the key. The retry re-reads, finds the winner's record, and
    /// replaces it in place — which is how classifying one message twice leaves one verdict beside the signals it
    /// rests on rather than a provider failure ending the pass that lost.
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
    /// The next is the recurring send's identity, which is that same case for a message somebody asked to have sent
    /// again rather than for one send. Two callers declaring the same repetition reach the database together, one is
    /// refused here, and the retry reads back the declaration the winner wrote — so one authored act leaves one
    /// declaration rather than two that would each produce a message on every occasion the schedule names.
    /// </para>
    /// <para>
    /// The next is one copy of one outgoing message filed into one role by two passes. Filing guards the key with a
    /// read before it writes, so two passes reading the same record before either commits both find nothing and both
    /// insert; the loser violates the key. Recognizing it is what makes that guard sound rather than decorative — the
    /// retry re-reads, finds the row the winner issued, and files nothing — and leaving it unrecognized would put a
    /// second copy of the owner's own send in front of them, which is the one duplication no local correction can
    /// withdraw.
    /// </para>
    /// <para>
    /// The next is one revision of one draft appended by two passes, which is the filing case for a message nobody is
    /// sending. Recording the append guards the revision with a read before it writes, and two passes settling one
    /// account — a worker's interval and that account's own run — read the same draft before either commits. The retry
    /// re-reads, finds the row the winner issued for that revision, and appends nothing, which is what keeps the guard
    /// sound; unrecognized, the loser would report a filing that failed while the owner's drafts folder is exactly
    /// right.
    /// </para>
    /// <para>
    /// The next is the one move of stored content a deployment has, which is the whole-mailbox run's case for a
    /// deployment rather than for an account: two operators asking to carry the content across, or one request
    /// retried, both read that no move exists and both insert the singleton row. The retry reads back the move the
    /// winner asked for, which is what makes asking twice asking once — and leaving it unrecognized would answer the
    /// second operator with a provider failure while the move they asked for is in fact under way.
    /// </para>
    /// <para>
    /// The next is one owner's named stored secret created by two administrative requests. Both requests read that the
    /// identity is absent and propose a different generated reference; the loser violates the owner-and-name index.
    /// The retry reads the winner's row, seals the submitted material against that row's reference, and returns it, so
    /// both answers name one rotatable secret rather than exposing a persistence constraint.
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
            ConstraintName: PersistenceConstraintNames.SynchronizationCheckpointPrimaryKeyConstraintName
                or PersistenceConstraintNames.MailFolderBindingUniqueIndexName
                or PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName
                or PersistenceConstraintNames.MailboxMutationIdentityUniqueIndexName
                or PersistenceConstraintNames.MailboxMutationAuditEntryMutationUniqueIndexName
                or PersistenceConstraintNames.EmbeddingProfileFingerprintUniqueIndexName
                or PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName
                or PersistenceConstraintNames.MailRuleEvaluationRunPrimaryKeyConstraintName
                or PersistenceConstraintNames.SpamClassificationRunPrimaryKeyConstraintName
                or PersistenceConstraintNames.EmailSpamClassificationPrimaryKeyConstraintName
                or PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName
                or PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName
                or PersistenceConstraintNames.MailRederivationPositionPrimaryKeyConstraintName
                or PersistenceConstraintNames.MailRederivationRunPrimaryKeyConstraintName
                or PersistenceConstraintNames.ContactAddressUniqueIndexName
                or PersistenceConstraintNames.OutgoingEmailIdentityUniqueIndexName
                or PersistenceConstraintNames.RecurringSendIdentityUniqueIndexName
                or PersistenceConstraintNames.OutgoingEmailFilingPrimaryKeyConstraintName
                or PersistenceConstraintNames.MailDraftCopyPrimaryKeyConstraintName
                or PersistenceConstraintNames.ContentMoveRunPrimaryKeyConstraintName
                or PersistenceConstraintNames.StoredSecretOwnerNameUniqueIndexName
                or PersistenceConstraintNames.EmailThreadIdentifierPrimaryKeyConstraintName,
        };
}
