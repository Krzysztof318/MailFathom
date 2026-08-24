// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence;

/// <summary>The database names of the keys, indexes, and constraints the model declares rather than leaves to convention.</summary>
/// <remarks>
/// <para>
/// A name is here because something outside the mapping reads it, or because the name EF Core's convention would
/// compose is not one the schema can keep. <c>PersistenceConcurrencyConflicts</c> reads the first group: it recognizes
/// a losing writer by the constraint its insert violated, so a name only the convention knew about would turn a
/// resolvable race into a provider failure that ends the run carrying it. The second is stated because PostgreSQL
/// truncates an identifier at 63 characters and would leave a composed name permanently ending in a tilde, or because
/// an index that would otherwise be identified by the properties it covers has to be told apart from another covering
/// the same three. The model tests read both groups by name.
/// </para>
/// <para>
/// They live beside the model rather than on it, because a name is a fact about the schema that the mapping, the
/// conflict predicate, and the model tests each read independently. Adding one is an append here and a reference from
/// the one configuration that declares it.
/// </para>
/// </remarks>
internal static class PersistenceConstraintNames
{
    /// <summary>The mailbox account primary key, kept at the name EF Core's own convention gave the applied baseline.</summary>
    /// <remarks>
    /// Stated by the mapping rather than left implicit, because a losing writer is recognized by the constraint its
    /// insert violated and a rename that only the convention knew about would silently turn a resolvable race into a
    /// failure. The value is the conventional one so the model states the name without asking the schema to change.
    /// </remarks>
    internal const string MailboxAccountPrimaryKeyConstraintName = "PK_mailbox_accounts";

    internal const string SynchronizationCheckpointPrimaryKeyConstraintName = "pk_synchronization_checkpoints";

    internal const string MailFolderBindingUniqueIndexName = "ix_mail_folders_account_alias_generation";

    internal const string StoredEmailOccurrenceUniqueIndexName = "ix_stored_emails_folder_uidvalidity_uid";

    internal const string StoredEmailAccountTimelineIndexName = "ix_stored_emails_account_timeline";

    internal const string StoredEmailFolderTimelineIndexName = "ix_stored_emails_folder_timeline";

    internal const string StoredEmailReconciliationQueueIndexName = "ix_stored_emails_reconciliation_queue";

    internal const string StoredEmailAwaitingContentIndexName = "ix_stored_emails_awaiting_content";

    /// <summary>The order a requested whole-mailbox rule run walks an account's mail in.</summary>
    internal const string StoredEmailAccountIdentityIndexName = "ix_stored_emails_account_identity";

    /// <summary>The queue of mail no rule pass has evaluated, which is read once per account run and is usually empty.</summary>
    internal const string StoredEmailAwaitingRuleEvaluationIndexName = "ix_stored_emails_awaiting_rule_evaluation";

    internal const string StoredEmailSenderIndexName = "ix_stored_emails_sender";

    internal const string StoredEmailToAddressesIndexName = "ix_stored_emails_to_addresses";

    internal const string StoredEmailCcAddressesIndexName = "ix_stored_emails_cc_addresses";

    internal const string StoredEmailReplyToAddressesIndexName = "ix_stored_emails_reply_to_addresses";

    internal const string StoredEmailRemoteKeywordsIndexName = "ix_stored_emails_remote_keywords";

    internal const string EmailSearchDocumentVectorIndexName = "ix_email_search_documents_search_vector";

    internal const string EmailChunkOrdinalUniqueIndexName = "ix_email_chunks_email_ordinal";

    /// <summary>The unique index over an embedding profile's identity, which is what makes activation idempotent.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two operators activating the
    /// same declaration is a race that resolves to the profile already registered, not a failure to report.
    /// </remarks>
    internal const string EmbeddingProfileFingerprintUniqueIndexName = "ix_embedding_profiles_identity_fingerprint";

    /// <summary>The index that admits one generation being built and one being read, and no second of either.</summary>
    /// <remarks>
    /// The guarantee is structural because the failure it prevents is silent: two rows claiming to serve would leave
    /// retrieval reading whichever one a query happened to return, with half the vectors in the table unreachable and
    /// nothing about the answers saying so. Superseded rows are outside the filter, because a deployment accumulates
    /// one per model it has ever used.
    /// </remarks>
    internal const string EmbeddingProfileLifecycleUniqueIndexName = "ix_embedding_profiles_lifecycle_state";

    /// <summary>The alternate key a vector row's dimension is checked against.</summary>
    internal const string EmbeddingProfileDimensionAlternateKeyName = "ak_embedding_profiles_id_dimension";

    /// <summary>The key an idempotent vector upsert conflicts on.</summary>
    internal const string EmailEmbeddingPrimaryKeyConstraintName = "pk_email_embeddings";

    /// <summary>The constraint that ties a stored vector's length to the width its profile declares.</summary>
    internal const string EmailEmbeddingDimensionCheckConstraintName = "ck_email_embeddings_dimension";

    /// <summary>The composite foreign key that refuses a width the named profile never declared.</summary>
    /// <remarks>
    /// Named because EF's convention would compose one from both column names and PostgreSQL would truncate it at 63
    /// characters, leaving a permanent identifier ending in a tilde.
    /// </remarks>
    internal const string EmailEmbeddingProfileForeignKeyName = "fk_email_embeddings_embedding_profiles";

    /// <summary>The index a whole generation is read by when it is removed.</summary>
    internal const string EmailEmbeddingProfileIndexName = "ix_email_embeddings_profile";

    internal const string MailboxRefreshTokenKeyIndexName = "ix_mailbox_refresh_tokens_data_encryption_key";

    /// <summary>The key that keeps one classification per occurrence, and which a second concurrent run is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: an arrival classifies an occurrence while a
    /// reclassification replaces it, one of them violates this key, and the retry reads back the row the winner wrote —
    /// which is how classifying twice produces one record.
    /// </remarks>
    internal const string EmailSpamClassificationPrimaryKeyConstraintName = "pk_email_spam_classifications";

    /// <summary>The order one classification's signals are read back in, and what stops an ordinal being written twice.</summary>
    internal const string EmailSpamClassificationSignalOrdinalUniqueIndexName =
        "ix_email_spam_classification_signals_classification_ordinal";

    /// <summary>The foreign key that removes a classification's signals with the classification.</summary>
    /// <remarks>
    /// Named because EF's convention composes one from both table names and PostgreSQL truncates an identifier at 63
    /// characters, which would leave a permanent constraint whose name ends in a tilde.
    /// </remarks>
    internal const string EmailSpamClassificationSignalForeignKeyName =
        "fk_email_spam_classification_signals_classifications";

    /// <summary>The key that keeps one whole-mailbox rule run per account, and which a second request is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: two requests for one account's first run
    /// reach the database together, one of them violates this key, and the retry reads back the run the winner asked
    /// for — which is exactly how asking twice produces one walk of one mailbox.
    /// </remarks>
    internal const string MailRuleEvaluationRunPrimaryKeyConstraintName = "pk_mail_rule_evaluation_runs";

    /// <summary>The key that keeps one whole-mailbox classification run per account, and which a second request is recognized by.</summary>
    /// <remarks>
    /// Named for the reason the rule run's key is: two requests for one account's first run reach the database together,
    /// one of them violates this key, and the retry reads back the run the winner asked for — which is how asking twice
    /// produces one walk of one mailbox rather than two.
    /// </remarks>
    internal const string SpamClassificationRunPrimaryKeyConstraintName = "pk_spam_classification_runs";

    /// <summary>The key that keeps one re-derivation cursor per scope, and which a second walk of it is recognized by.</summary>
    /// <remarks>
    /// Named because the first batch of a scope nobody has walked is a check-then-insert over this key: two invocations
    /// asked for at once, or one request retried, both read no row and both insert. Losing that race is the mechanism
    /// rather than a fault — the retry reads back the position the winner wrote and moves it on from there, which is
    /// what makes asking twice walk the scope once instead of ending the pass on a provider failure.
    /// </remarks>
    internal const string MailRederivationPositionPrimaryKeyConstraintName = "pk_mail_rederivation_positions";

    /// <summary>The key that keeps one re-derivation run per scope, and which a second request for it is recognized by.</summary>
    /// <remarks>
    /// Named for the reason the classification run's key is: two requests for one scope's first run reach the database
    /// together, one of them violates this key, and the retry reads back the run the winner asked for — which is how
    /// asking twice produces one walk of one mailbox rather than two.
    /// </remarks>
    internal const string MailRederivationRunPrimaryKeyConstraintName = "pk_mail_rederivation_runs";

    /// <summary>The constraint a mutation's idempotency identity is enforced by, and which a losing writer is recognized from.</summary>
    /// <remarks>
    /// Named because the name is how the same request arriving twice is told apart from a genuine failure. Two callers
    /// asking for the same change reach the database together and one of them violates this index; that is the second
    /// caller learning the first got there, not a fault, and the session translates it into the conflict the retry
    /// policy loops on.
    /// </remarks>
    internal const string MailboxMutationIdentityUniqueIndexName = "ix_mailbox_mutations_identity";

    internal const string MailboxMutationOutstandingIndexName = "ix_mailbox_mutations_outstanding";

    internal const string MailboxMutationPlacementIndexName = "ix_mailbox_mutations_placement";

    /// <summary>The constraint that keeps one audit entry per mutation ending, whatever a repeated append attempts.</summary>
    internal const string MailboxMutationAuditEntryMutationUniqueIndexName =
        "ix_mailbox_mutation_audit_entries_mutation";

    /// <summary>The index the trail is both read and aged through.</summary>
    internal const string MailboxMutationAuditEntryTimelineIndexName =
        "ix_mailbox_mutation_audit_entries_account_completed";

    /// <summary>The constraint that keeps one answering entry per run per account, whatever a repeated append attempts.</summary>
    internal const string MailAnsweringAuditEntryRunUniqueIndexName = "ix_mail_answering_audit_entries_run_account";

    /// <summary>The index the answering record is both read and aged through.</summary>
    internal const string MailAnsweringAuditEntryTimelineIndexName =
        "ix_mail_answering_audit_entries_account_completed";

    /// <summary>The index the rule history is walked and aged through, which is its unfiltered page and its retention.</summary>
    internal const string MailRuleExecutionTimelineIndexName = "ix_mail_rule_executions_account_evaluated";

    /// <summary>The index that answers what one rule has been doing, which is the history's second question.</summary>
    internal const string MailRuleExecutionRuleIndexName = "ix_mail_rule_executions_account_rule_evaluated";

    /// <summary>The index that answers why one message was filed, which is the history's first question.</summary>
    internal const string MailRuleExecutionEmailIndexName = "ix_mail_rule_executions_email_evaluated";

    /// <summary>The order the contact book is listed and paginated in.</summary>
    internal const string ContactListingIndexName = "ix_contacts_display_name_sort_key_id";

    /// <summary>The constraint that keeps one address in one person's hands, across the whole book.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two callers claiming one
    /// address is a race to resolve into the answer that names its holder, not a failure to report. It is also what the
    /// lookup from an address to a person is answered from.
    /// </remarks>
    internal const string ContactAddressUniqueIndexName = "ix_contact_addresses_normalized_address";

    /// <summary>The constraint an outgoing email's idempotency identity is enforced by, and which a losing writer is recognized from.</summary>
    /// <remarks>
    /// It is the mutation identity's case with the consequence raised. Two callers asking for the same send reach the
    /// database together and one of them violates this index; the retry then finds the winner's record and delivers
    /// nothing further, which is the whole of what stops one authored request putting two copies of a message in
    /// somebody's mailbox — a duplication that, unlike a local one, cannot be withdrawn afterwards.
    /// </remarks>
    internal const string OutgoingEmailIdentityUniqueIndexName = "ix_outgoing_emails_identity";

    /// <summary>The index the outbox is read through, filtered to the sends that have not finished.</summary>
    internal const string OutgoingEmailOutstandingIndexName = "ix_outgoing_emails_outstanding";

    /// <summary>Names the index one claim over the outbox reads, which is the only query on its hot path.</summary>
    internal const string OutgoingEmailClaimableIndexName = "ix_outgoing_emails_claimable";

    /// <summary>Names the index a period's send ceilings are counted through.</summary>
    /// <remarks>
    /// Unfiltered, unlike the two above it, because what a ceiling counts is every message a period was asked for
    /// whatever became of it: a send that was refused by the server, abandoned, or cancelled was still mail this
    /// deployment tried to put on the network, and a structure that forgot those would let a period that failed
    /// entirely be spent twice. It leads on the instant so the deployment-wide count is a range read, and carries the
    /// account after it so one account's count reads the same range without visiting a record.
    /// </remarks>
    internal const string OutgoingEmailPeriodUsageIndexName = "ix_outgoing_emails_period_usage";

    /// <summary>The foreign key that removes an outgoing email's recipients with the record.</summary>
    /// <remarks>
    /// Named because EF's convention composes one from both table names and PostgreSQL truncates an identifier at 63
    /// characters, which would leave a permanent constraint whose name ends in a tilde.
    /// </remarks>
    internal const string OutgoingEmailRecipientForeignKeyName = "fk_outgoing_email_recipients_emails";

    /// <summary>The foreign key that removes the stored MIME with the record that says who it was for.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string OutgoingEmailContentForeignKeyName = "fk_outgoing_email_contents_emails";

    /// <summary>The foreign key that removes the record of what was filed where with the record it was filed from.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string OutgoingEmailFilingForeignKeyName = "fk_outgoing_email_filings_emails";

    /// <summary>The composite key one filing row is refused a duplicate of, named so a lost race is recognized as one.</summary>
    internal const string OutgoingEmailFilingPrimaryKeyConstraintName = "pk_outgoing_email_filings";

    /// <summary>The index a batch of discovered mail is joined to the copies this deployment filed through.</summary>
    /// <remarks>
    /// Filtered to the filings synchronization has not met yet, which is what keeps it proportional to what is in
    /// flight rather than to everything the deployment has ever sent: a copy is looked for once, and every mailbox this
    /// system has been filing into for a year is otherwise in the structure that lookup reads.
    /// </remarks>
    internal const string OutgoingEmailFilingPlacementIndexName = "ix_outgoing_email_filings_placement";

    /// <summary>The index the same join falls back to where the server named no placement to look the copy up by.</summary>
    internal const string OutgoingEmailFilingMessageIdIndexName = "ix_outgoing_email_filings_message_id";

    /// <summary>The uniqueness a declaration that a message repeats rests on, which is the same identity a send's is.</summary>
    /// <remarks>
    /// A caller that retried the command it declared a repetition with reads back the declaration it already made,
    /// rather than declaring a second one that would send everything twice for as long as both stood.
    /// </remarks>
    internal const string RecurringSendIdentityUniqueIndexName = "ix_recurring_sends_identity";

    /// <summary>The index the recurring dispatch reads the declarations through, filtered to the ones still producing occurrences.</summary>
    internal const string RecurringSendActiveIndexName = "ix_recurring_sends_active";

    /// <summary>The foreign key that removes a declaration's recipients with the declaration.</summary>
    /// <remarks>Named for the reason the outgoing keys are: EF's composed name would be truncated at 63 characters and permanent.</remarks>
    internal const string RecurringSendRecipientForeignKeyName = "fk_recurring_send_recipients_sends";

    /// <summary>The foreign key that removes the stored draft with the declaration it belongs to.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string RecurringSendDraftForeignKeyName = "fk_recurring_send_drafts_sends";

    /// <summary>The order an account's drafts are read in, which is the order their owner last touched them.</summary>
    /// <remarks>
    /// Unfiltered, unlike the outbox's, because what a pass over drafts looks for cannot be written as a predicate on
    /// this table: whether a draft owes the mail server anything is decided by the copy rows beside it. The structure
    /// is proportional to the drafts a mailbox holds, which is what a person keeps rather than what a deployment has
    /// ever done.
    /// </remarks>
    internal const string MailDraftAccountIndexName = "ix_mail_drafts_account_revised";

    /// <summary>The index a delivered send is turned back into the draft it came from through.</summary>
    /// <remarks>
    /// Filtered to the drafts a promotion actually wrote a record for, which is a small part of a mailbox's drafts and
    /// none at all in a deployment where nothing is ever promoted.
    /// </remarks>
    internal const string MailDraftPromotedIndexName = "ix_mail_drafts_promoted";

    /// <summary>The foreign key that removes a draft's recipients with the draft.</summary>
    /// <remarks>
    /// Named because EF's convention composes one from both table names and PostgreSQL truncates an identifier at 63
    /// characters, which would leave a permanent constraint whose name ends in a tilde.
    /// </remarks>
    internal const string MailDraftRecipientForeignKeyName = "fk_mail_draft_recipients_drafts";

    /// <summary>The foreign key that removes the record of what was appended where with the draft it was appended for.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string MailDraftCopyForeignKeyName = "fk_mail_draft_copies_drafts";

    /// <summary>The revision of one draft only one writer may record an append for, kept at EF Core's conventional name.</summary>
    /// <remarks>
    /// Stated by the mapping for the reason the account's key is: a losing writer is recognized by the constraint its
    /// insert violated, and a name only the convention knew about would turn a resolvable race into a failure. Two
    /// passes settling one account can both read that a revision has no copy row and both insert one, which is exactly
    /// the race the retry converges on.
    /// </remarks>
    internal const string MailDraftCopyPrimaryKeyConstraintName = "PK_mail_draft_copies";

    /// <summary>The foreign key that removes the stored MIME with the draft it is the current revision of.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string MailDraftContentForeignKeyName = "fk_mail_draft_contents_drafts";

    /// <summary>The uniqueness a job's idempotency rests on, which spans every state a row can reach.</summary>
    internal const string JobIdentityUniqueIndexName = "ix_jobs_identity";

    /// <summary>The index the claim statement drains the queue through, filtered to the rows a claim can still take.</summary>
    internal const string JobClaimIndexName = "ix_jobs_claimable";

    /// <summary>The index an account's jobs are erased and aged through.</summary>
    internal const string JobAccountIndexName = "ix_jobs_account";

    /// <summary>The index an enqueue reads one owner's latest turn from, filtered to the work that still holds one.</summary>
    /// <remarks>
    /// Beside the account index rather than folded into it, because the two are read for opposite reasons and are
    /// proportional to different things. That one answers what belongs to an account across everything the queue has
    /// ever done; this one answers where an owner's waiting work has reached, which is a backlog rather than a history,
    /// and it is read on every enqueue.
    /// </remarks>
    internal const string JobAccountTurnIndexName = "ix_jobs_account_turn";

    /// <summary>The index an operator reads what has stopped through, filtered to the one state that waits for them.</summary>
    internal const string JobDeadLetterIndexName = "ix_jobs_dead_lettered";

    /// <summary>The key that binds one message identifier of one account to exactly one thread.</summary>
    /// <remarks>
    /// Named because it is what a losing writer is recognized by. Two arrivals referring to the same identifier for the
    /// first time both find nothing and both insert, so the second one violates this key — a race to resolve by
    /// re-reading what the winner assembled, rather than a failure to report.
    /// </remarks>
    internal const string EmailThreadIdentifierPrimaryKeyConstraintName = "pk_email_thread_identifiers";

    /// <summary>The index a thread's identifiers are repointed through when two threads merge.</summary>
    internal const string EmailThreadIdentifierThreadIndexName = "ix_email_thread_identifiers_thread";

    /// <summary>The index a thread's messages are read and repointed through.</summary>
    /// <remarks>
    /// The one index every thread read runs on: assembling an arrival resolves its parent and its already-stored
    /// children within the thread, and publishing a thread reads its members. It carries the identity beside the thread
    /// so the read that orders a conversation arrives already sorted on the tie-breaker the order ends with.
    /// </remarks>
    internal const string StoredEmailThreadIndexName = "ix_stored_emails_thread";

    /// <summary>The index over the object key a payload row names, one per table that holds raw MIME.</summary>
    /// <remarks>
    /// <para>
    /// Four partial indexes filtered to the object backend, and partial for the reason that answers the cost: the
    /// stored default names the database, so on a deployment that configured no endpoint every one of them is empty and
    /// both readers below meet four empty indexes rather than sequentially scanning four tables of mail.
    /// </para>
    /// <para>
    /// Two readers, and the second is what puts the key in the index rather than the discriminator. The readiness
    /// census asks whether any object-backed row exists at all, which the index's own filter answers on every scrape.
    /// The sweep for objects nothing points at asks whether any row names each of a listed page of keys, and it asks
    /// that once per page for as long as the bucket takes — so the difference between a keyed lookup and a scan of
    /// every object-backed row is the difference between a sweep an operator can run hourly and one they cannot.
    /// </para>
    /// <para>
    /// Unique, because a key is minted by the write that produced it and no two rows can name one: content addressing
    /// was refused precisely so that removing an object could never take a payload another row still points at. A
    /// violation would mean that assumption had stopped holding, which is a defect rather than a race to retry, so it
    /// is deliberately absent from the conflict predicate.
    /// </para>
    /// </remarks>
    internal const string EmailMessageContentObjectLocatorUniqueIndexName =
        "ix_email_message_contents_object_locator";

    /// <inheritdoc cref="EmailMessageContentObjectLocatorUniqueIndexName" />
    internal const string OutgoingEmailContentObjectLocatorUniqueIndexName =
        "ix_outgoing_email_contents_object_locator";

    /// <inheritdoc cref="EmailMessageContentObjectLocatorUniqueIndexName" />
    internal const string MailDraftContentObjectLocatorUniqueIndexName = "ix_mail_draft_contents_object_locator";

    /// <inheritdoc cref="EmailMessageContentObjectLocatorUniqueIndexName" />
    internal const string RecurringSendDraftObjectLocatorUniqueIndexName = "ix_recurring_send_drafts_object_locator";

    /// <summary>The key of the one row a deployment's move of its stored content is kept in.</summary>
    internal const string ContentMoveRunPrimaryKeyConstraintName = "pk_content_move_runs";

    /// <summary>The constraint that admits the one key a move is written under, and therefore one move per deployment.</summary>
    /// <remarks>
    /// The invariant is structural because every reader of that row assumes it and none of them re-checks: the control
    /// answers a second request with the move already under way, and a pass commits its counts onto whatever it finds.
    /// A second row under a name of its own would give a deployment two moves that each believed themselves to be the
    /// one.
    /// </remarks>
    internal const string ContentMoveRunSingletonCheckConstraintName = "ck_content_move_runs_singleton";
}
