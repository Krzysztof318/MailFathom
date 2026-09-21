// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>The rule that no two live siblings of a held account's hierarchy share a name.</summary>
    internal const string LocalMailFolderSiblingNameUniqueIndexName = "ix_local_mail_folders_account_parent_name";

    /// <summary>The rule that a held account has one live folder per protected role.</summary>
    internal const string LocalMailFolderRoleUniqueIndexName = "ix_local_mail_folders_account_role";

    internal const string StoredEmailOccurrenceUniqueIndexName = "ix_stored_emails_folder_uidvalidity_uid";

    internal const string StoredEmailOccurrenceCompleteCheckConstraintName = "ck_stored_emails_occurrence_complete";

    /// <summary>The order every timeline is walked in, whether it names one of a user's accounts or all of them.</summary>
    /// <remarks>
    /// ADR 0014 names a user-led index beside this one as the candidate for <em>all of my mail</em>, on the reading
    /// that a request naming no account would otherwise be planned as a scan followed by a top-N sort. That read does
    /// not exist here: <c>StoredEmailTimelineReader</c> walks each account of the scope on its own and merges the
    /// walks, so every timeline query carries an equality on the account and this index is the plan for all of them.
    /// A user-led index would therefore be one nothing reads, paid for on every message stored.
    /// </remarks>
    internal const string StoredEmailAccountTimelineIndexName = "ix_stored_emails_account_timeline";

    internal const string StoredEmailFolderTimelineIndexName = "ix_stored_emails_folder_timeline";

    internal const string StoredEmailReconciliationQueueIndexName = "ix_stored_emails_reconciliation_queue";

    internal const string StoredEmailAwaitingContentIndexName = "ix_stored_emails_awaiting_content";

    /// <summary>The order a held account's source is emptied in, which is its oldest mail first.</summary>
    /// <remarks>
    /// Filtered to the rows a source copy is still expected for, which excludes what the drain has already cleared and
    /// what no server ever returned, so the index shrinks as a held account's source empties. That is what it buys:
    /// without the filter, every run of a held account whose source is already empty would walk that account's whole
    /// timeline to discover that no row qualifies. What it costs is the mirrored account, where nothing clears an
    /// occurrence and the index therefore covers substantially every row over the same columns as
    /// <see cref="StoredEmailAccountTimelineIndexName" />.
    /// </remarks>
    internal const string StoredEmailAwaitingDrainIndexName = "ix_stored_emails_awaiting_drain";

    /// <summary>The order a requested whole-mailbox rule run walks an account's mail in.</summary>
    internal const string StoredEmailAccountIdentityIndexName = "ix_stored_emails_account_identity";

    /// <summary>The queue of mail no rule pass has evaluated, which is read once per account run and is usually empty.</summary>
    internal const string StoredEmailAwaitingRuleEvaluationIndexName = "ix_stored_emails_awaiting_rule_evaluation";

    /// <summary>The sent copies a held account filed locally that no server has returned yet, found by their <c>Message-ID</c>.</summary>
    internal const string StoredEmailFiledSentCopyIndexName = "ix_stored_emails_filed_sent_copy";

    /// <summary>The queue of mail whose attachments nothing has read, which is read once per account run and is usually empty.</summary>
    /// <remarks>
    /// Filtered for the same reason the rule queue above is: in steady state every row of an account carries the stamp,
    /// so an unfiltered index would be walked in full, once per run, for ever, to return nothing.
    /// </remarks>
    internal const string StoredEmailAwaitingAttachmentTextIndexName = "ix_stored_emails_awaiting_attachment_text";

    internal const string StoredEmailSenderIndexName = "ix_stored_emails_sender";

    internal const string StoredEmailToAddressesIndexName = "ix_stored_emails_to_addresses";

    internal const string StoredEmailCcAddressesIndexName = "ix_stored_emails_cc_addresses";

    internal const string StoredEmailReplyToAddressesIndexName = "ix_stored_emails_reply_to_addresses";

    internal const string StoredEmailRemoteKeywordsIndexName = "ix_stored_emails_remote_keywords";

    internal const string EmailSearchDocumentVectorIndexName = "ix_email_search_documents_search_vector";

    internal const string EmailAttachmentTextVectorIndexName = "ix_email_attachment_texts_search_vector";

    /// <summary>The index covering every passage of one message, whichever text it was cut from.</summary>
    /// <remarks>
    /// Unfiltered, because the two indexes beside it are not: PostgreSQL uses a partial index only where the statement's
    /// own predicate implies the index predicate, and the deletion that follows a message — an expunge, a user
    /// erasure, a junk verdict — asks for one message's chunks without saying anything about the attachment position.
    /// Before the attachment split the unique pair covered the foreign key and EF's convention created no index of its
    /// own; this restores the cover the split removed.
    /// </remarks>
    internal const string EmailChunkEmailIndexName = "ix_email_chunks_email";

    /// <summary>The unique index over the ordinals of one message's body passages.</summary>
    /// <remarks>
    /// Filtered on the passages cut from the body, because a message with attachments has several texts and the
    /// ordinals of each run from zero: a single index over the pair would report the first passage of the first
    /// attachment as a duplicate of the first passage of the body.
    /// </remarks>
    internal const string EmailChunkOrdinalUniqueIndexName = "ix_email_chunks_email_ordinal";

    /// <summary>The unique index over the ordinals of the passages cut from one attachment.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two runs reading one
    /// message's attachments at once resolve to the readings one of them committed rather than to a failure.
    /// </remarks>
    internal const string EmailChunkAttachmentOrdinalUniqueIndexName = "ix_email_chunks_email_attachment_ordinal";

    /// <summary>The primary key over one message's attachment readings, one row per walk position.</summary>
    /// <remarks>
    /// Named for the reason the attachment ordinal index above is: the readings are replaced whole and re-inserted, so
    /// a competing run losing that race violates this key and is retried rather than ending the account's run.
    /// </remarks>
    internal const string EmailAttachmentTextPrimaryKeyName = "PK_email_attachment_texts";

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

    internal const string StoredSecretMaterialLengthCheckConstraintName = "ck_stored_secrets_material_length";

    internal const string StoredSecretUserForeignKeyName = "fk_stored_secrets_settings_accounts";

    internal const string StoredSecretUserNameUniqueIndexName = "ix_stored_secrets_user_name";

    internal const string StoredSecretKeyIndexName = "ix_stored_secrets_data_encryption_key";

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

    /// <summary>The key that keeps one derivation per occurrence, and which a second concurrent run is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: an account run derives from an occurrence
    /// while a later run reaches the same message, one of them violates this key, and the retry reads back the row the
    /// winner wrote — which is how deriving twice produces one record.
    /// </remarks>
    internal const string EmailEnrichmentPrimaryKeyConstraintName = "pk_email_enrichments";

    /// <summary>What stops one derivation carrying two answers to the same question.</summary>
    /// <remarks>
    /// Named because a writer losing this race is how one message ends up with one mark per aspect: two runs cannot see
    /// each other's uncommitted marks, so the index rather than the writer is what decides.
    /// </remarks>
    internal const string EmailEnrichmentMarkAspectUniqueIndexName =
        "ix_email_enrichment_marks_enrichment_aspect";

    /// <summary>The foreign key that removes a derivation's marks with the derivation.</summary>
    /// <remarks>
    /// Named for the reason the classification signals' one is: EF's convention composes one from both table names and
    /// PostgreSQL truncates an identifier at 63 characters.
    /// </remarks>
    internal const string EmailEnrichmentMarkForeignKeyName = "fk_email_enrichment_marks_enrichments";

    /// <summary>The key that keeps one state per conversation, and which a second concurrent run is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: an account run derives a conversation's
    /// state while a later run reaches the same conversation after a reply moved it back into the queue, one of them
    /// violates this key, and the retry reads back the row the winner wrote — which is how deriving twice produces one
    /// record.
    /// </remarks>
    internal const string EmailThreadStatePrimaryKeyConstraintName = "pk_email_thread_states";

    /// <summary>The foreign key that removes a conversation's statements with the state they belong to.</summary>
    /// <remarks>
    /// Named for the reason the enrichment marks' one is: EF's convention composes one from both table names and
    /// PostgreSQL truncates an identifier at 63 characters.
    /// </remarks>
    internal const string EmailThreadStateEntryForeignKeyName = "fk_email_thread_state_entries_states";

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

    internal const string MailboxMutationFlaggedDeleteIndexName = "ix_mailbox_mutations_flagged_delete";

    /// <summary>The occurrence a delete left flagged on its server, which is followed once however often its settlement is replayed.</summary>
    internal const string MailboxFlaggedDeleteOccurrenceUniqueIndexName = "ix_mailbox_flagged_deletes_occurrence";

    /// <summary>The order a folder's run asks about the occurrences its deletes left flagged.</summary>
    internal const string MailboxFlaggedDeleteQueueIndexName = "ix_mailbox_flagged_deletes_queue";

    /// <summary>The occurrence one erased message still occupies on its source, which is recorded once however often the erasure is attempted.</summary>
    internal const string MailboxSourceRemovalOccurrenceUniqueIndexName = "ix_mailbox_source_removals_occurrence";

    /// <summary>The order a held account's outstanding source removals are taken in.</summary>
    internal const string MailboxSourceRemovalQueueIndexName = "ix_mailbox_source_removals_queue";

    /// <summary>The one append record a message may carry, which is what keeps a restore from putting a second copy on the source.</summary>
    internal const string MailboxRestoreAppendEmailUniqueIndexName = "ix_mailbox_restore_appends_email";

    /// <summary>The order a restoring account's unanswered appends are read in.</summary>
    internal const string MailboxRestoreAppendUnansweredIndexName = "ix_mailbox_restore_appends_unanswered";

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

    /// <summary>The order one contact book is listed and paginated in.</summary>
    /// <remarks>
    /// The holder leads it because a listing is read over a handful of named books: leading with the name would make a
    /// page of a small book a walk of every book on the deployment, filtered afterwards.
    /// </remarks>
    internal const string ContactListingIndexName = "ix_contacts_book_holder_display_name_sort_key_id";

    /// <summary>The constraint that keeps one address in one person's hands, within one book.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two callers claiming one
    /// address is a race to resolve into the answer that names its holder, not a failure to report. It is also what the
    /// lookup from an address to a person is answered from. It leads with the book, which is what lets a user's own
    /// book and a mailbox's collected book each hold a record for one address while neither holds it twice.
    /// </remarks>
    internal const string ContactAddressUniqueIndexName = "ix_contact_addresses_book_holder_normalized_address";

    /// <summary>The constraint that keeps a contact's book holder, its origin, and the key it is filed under agreeing.</summary>
    /// <remarks>
    /// A contact belongs either to a user or to a mail account, and which it is decides its origin. Stating both in
    /// columns leaves three ways for them to disagree, so the database refuses a row where they do rather than every
    /// reader having to decide what such a row means.
    /// </remarks>
    internal const string ContactBookHolderCheckConstraintName = "ck_contacts_book_holder";

    /// <summary>The order one person's calendar is read in, and what a window over it is answered from.</summary>
    /// <remarks>
    /// The owner leads it because a read is always one person's, and the identity closes it so that two events
    /// beginning at the same instant have an order at all. Stated rather than left to the convention because an index
    /// over an owner and a start is what a second question about the calendar would also want, and two of them
    /// identified by the columns they cover are two nobody can tell apart in a plan.
    /// </remarks>
    internal const string CalendarEventWindowIndexName = "ix_calendar_events_user_starts_at_id";

    /// <summary>The constraint that keeps one calendar from holding an imported entry twice.</summary>
    /// <remarks>
    /// The whole of what makes importing one file a second time create nothing: the entry's own identifier is what
    /// says the calendar already holds it, and two imports running together both read nothing, so only this closes
    /// that window. It is partial, because nearly every event carries no such identifier at all.
    /// </remarks>
    internal const string CalendarEventImportedUidUniqueIndexName = "ix_calendar_events_user_imported_uid";

    /// <summary>The index the run that announces reminders reads, over every calendar at once.</summary>
    /// <remarks>
    /// The one question that is not one person's, so the owner is not in it: what the pass asks is which reminders
    /// across the deployment have come due and have not been announced. It is partial on the claim being absent,
    /// which leaves out every reminder of every event already past — nearly all of them once a calendar has history —
    /// and keeps a pass reading an index the size of what is still ahead.
    /// </remarks>
    internal const string CalendarEventReminderDueIndexName = "ix_calendar_event_reminders_due_at";

    /// <summary>The key one reminder on a calendar event is written under, kept at EF Core's conventional name.</summary>
    /// <remarks>
    /// Stated for the reason the draft copy's key is: a losing writer is recognized by the constraint its insert
    /// violated, and a name only the convention knew about would leave a resolvable race ending as a provider failure.
    /// Two revisions of one event arriving at once both read that a lead has no row and both insert one, which is
    /// exactly the race the retry converges on.
    /// </remarks>
    internal const string CalendarEventReminderPrimaryKeyConstraintName = "PK_calendar_event_reminders";

    /// <summary>The index the run that announces reminders reads over every task list at once.</summary>
    /// <remarks>
    /// The calendar's index with the same reasoning: the pass asks which reminders across the deployment have come
    /// due and have not been announced, so the owner is not in it, and it is partial on the claim being absent so it
    /// holds only what is still ahead. Whether the task is done is not in it either — that is read through the task,
    /// because a completion is a column on a row this index does not cover.
    /// </remarks>
    internal const string PersonalTaskReminderDueIndexName = "ix_task_reminders_due_at";

    /// <summary>The key one reminder on a task's due date is written under, kept at EF Core's conventional name.</summary>
    /// <remarks>
    /// The calendar reminder's key with the same reasoning and the same race, which reaches a task through a second
    /// path the calendar has no equivalent of: moving a due date rewrites every instant on the task, so two clients
    /// saving one task are two writers reaching the same lead rather than one writer repeating itself.
    /// </remarks>
    internal const string PersonalTaskReminderPrimaryKeyConstraintName = "PK_task_reminders";

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
    /// Filtered to the copies still standing, which is what keeps it proportional to what a mailbox holds rather than
    /// to everything the deployment has ever attempted: a mirror taken back out and an append the server never
    /// answered are rows the join can never match again, and they are otherwise in the structure that lookup reads.
    /// </remarks>
    internal const string OutgoingEmailFilingPlacementIndexName = "ix_outgoing_email_filings_placement";

    /// <summary>The index the same join falls back to where the server named no placement to look the copy up by.</summary>
    internal const string OutgoingEmailFilingMessageIdIndexName = "ix_outgoing_email_filings_message_id";

    /// <summary>The index the sweep for a copy the provider duplicated reads, which is bounded by when it was filed.</summary>
    /// <remarks>
    /// The two above lead with what a discovery is recognized by, so neither answers a question asked the other way
    /// round: which of this account's standing copies of one kind were appended recently enough to still be acted on.
    /// Without it that sweep reads every confirmed filing the account has ever kept, on every outbox pass, to find the
    /// few inside a fifteen-minute window.
    /// </remarks>
    internal const string OutgoingEmailFilingRecentByFilingIndexName = "ix_outgoing_email_filings_recent_by_filing";

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

    /// <summary>The order an account's drafts are read in, which is the order their user last touched them.</summary>
    /// <remarks>
    /// Unfiltered, unlike the outbox's, because what a pass over drafts looks for cannot be written as a predicate on
    /// this table: whether a draft owes the mail server anything is decided by the copy rows beside it. The structure
    /// is proportional to the drafts a mailbox holds, which is what a person keeps rather than what a deployment has
    /// ever done.
    /// </remarks>
    internal const string MailDraftAccountIndexName = "ix_mail_drafts_user_account_revised";

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

    /// <summary>The foreign key that removes the files staged against a draft with the draft.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string MailDraftAttachmentForeignKeyName = "fk_mail_draft_attachments_drafts";

    /// <summary>The index the files of one draft are read and composed in upload order through.</summary>
    /// <remarks>
    /// Unfiltered and leading with the draft, because every reading of it is already narrowed to one draft: what a
    /// listing wants is that draft's files in the order a composition attaches them, and what a composition wants is
    /// the same set with its octets.
    /// </remarks>
    internal const string MailDraftAttachmentDraftIndexName = "ix_mail_draft_attachments_draft_staged";

    /// <summary>The foreign key that removes a staged file's octets with the row describing the file.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string MailDraftAttachmentContentForeignKeyName = "fk_mail_draft_attachment_contents_attachments";

    /// <summary>The uniqueness a job's idempotency rests on, which spans every state a row can reach.</summary>
    internal const string JobIdentityUniqueIndexName = "ix_jobs_identity";

    /// <summary>The index the claim statement drains the queue through, filtered to the rows a claim can still take.</summary>
    internal const string JobClaimIndexName = "ix_jobs_claimable";

    /// <summary>The index an account's jobs are erased and aged through.</summary>
    internal const string JobAccountIndexName = "ix_jobs_account";

    /// <summary>The index an enqueue reads one account's latest turn from, filtered to the work that still holds one.</summary>
    /// <remarks>
    /// Beside the account index rather than folded into it, because the two are read for opposite reasons and are
    /// proportional to different things. That one answers what belongs to an account across everything the queue has
    /// ever done; this one answers where a mailbox's waiting work has reached, which is a backlog rather than a
    /// history, and it is read on every enqueue. So the latest turn is one descending step into this index, and the
    /// account leads it because a mailbox assigned to several users still carries one backlog.
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

    /// <inheritdoc cref="EmailMessageContentObjectLocatorUniqueIndexName" />
    internal const string StoredFileObjectLocatorUniqueIndexName = "ix_stored_files_object_locator";

    /// <summary>The foreign key that removes a user's stored files with the user.</summary>
    internal const string StoredFileUserForeignKeyName = "fk_stored_files_settings_accounts";

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

    /// <summary>The constraint that admits the one row the deployment's persisted configuration is written on.</summary>
    /// <remarks>
    /// The host reads that row before it composes anything, by key rather than by query, so a second row would be
    /// configuration nothing ever reads and an operator would have no way to tell which of the two the running process
    /// took its settings from.
    /// </remarks>
    internal const string RootSettingsSingletonCheckConstraintName = "ck_settings_root_singleton";

    /// <summary>The index that keeps one label to one user across the deployment.</summary>
    /// <remarks>
    /// Stated rather than left to convention because the label is what an administrator reads a list of users by, and
    /// the refusal an insert of a second row under one label produces is worth naming the index it came from.
    /// </remarks>
    internal const string UserAccountDisplayNameUniqueIndexName = "ix_settings_accounts_display_name";

    /// <summary>The index that keeps one mailbox address to one mail account across the deployment.</summary>
    /// <remarks>Stated rather than left to convention because the store reads it: a write that violates it is an address another account holds, which is a refusal rather than a failure.</remarks>
    internal const string MailAccountRecordAddressUniqueIndexName = "ix_settings_mail_accounts_normalized_email_address";

    /// <summary>The index that reads an account's assignments, which is how a mailbox names the users it serves.</summary>
    /// <remarks>Stated rather than left to convention because a migration drops the unique index this name used to carry and recreates it under the same name without the uniqueness, so the name is what ties the two together.</remarks>
    internal const string MailAccountAssignmentAccountIndexName = "ix_mail_account_assignments_mail_account_id";

    /// <summary>The foreign key that ends a user's assignments with the user.</summary>
    internal const string MailAccountAssignmentUserForeignKeyName = "fk_mail_account_assignments_settings_accounts";

    /// <summary>The foreign key that ends an account's assignments with the account.</summary>
    internal const string MailAccountAssignmentAccountForeignKeyName =
        "fk_mail_account_assignments_settings_mail_accounts";

    /// <summary>The index a count of one organization's members is answered from.</summary>
    internal const string UserAccountOrganizationIndexName = "ix_settings_accounts_organization";

    /// <summary>The index that keeps one short name to one organization across the deployment.</summary>
    /// <remarks>Stated because the store reads it: a write that violates it is a short name another organization signs in under, which an operator acts on rather than a provider failure.</remarks>
    internal const string OrganizationShortNameUniqueIndexName = "ix_organizations_short_name";

    /// <summary>The index that keeps one credential lookup to one user within its method and its organization.</summary>
    /// <remarks>
    /// Stated rather than left to convention because the store reads it: an insert that violates it is a lookup another
    /// credential already holds, which is an answer an operator acts on rather than a provider failure. It is scoped to
    /// the method because the four vocabularies are unrelated and to the organization because a password's username is
    /// unique only within one, with nulls not distinct so a credential scoped to no organization is unique among those.
    /// </remarks>
    internal const string UserCredentialLookupUniqueIndexName = "ix_user_credentials_method_organization_lookup";

    /// <summary>The constraint that keeps every method but a password scoped to no organization.</summary>
    internal const string UserCredentialOrganizationScopesPasswordCheckConstraintName = "ck_user_credentials_organization_scopes_password";

    /// <summary>The index every administrative listing of one user's credentials is answered from.</summary>
    /// <remarks>Stated because it covers the user and the provisioning instant together, which is the listing's own order, and a name composed from the two properties would say nothing about that being why.</remarks>
    internal const string UserCredentialUserIndexName = "ix_user_credentials_user_created_at";

    /// <summary>The constraint that keeps one unread notification per condition, whatever a repeated raise attempts.</summary>
    /// <remarks>
    /// It is partial rather than whole, which is the deduplication rule itself: a condition already standing unread is
    /// not said again, and one the person has read is free to be said again when it recurs. The store reads it, because
    /// a raise that loses the race to another writer is a condition already stated rather than a provider failure.
    /// </remarks>
    internal const string NotificationUnreadConditionUniqueIndexName = "ix_notifications_user_unread_condition";

    /// <summary>The index a person's notification centre is both read and aged through.</summary>
    /// <remarks>
    /// It covers the user and the instant together because both readers walk exactly that: the centre lists one
    /// person's notifications newest first, and retention erases the same person's oldest.
    /// </remarks>
    internal const string NotificationTimelineIndexName = "ix_notifications_user_occurred";

    /// <summary>The order the spent client assertions are aged out through.</summary>
    /// <remarks>
    /// Stated because nothing else says why the column is indexed at all: no query reads a spent assertion, and the one
    /// statement that touches it other than the insert is the removal of everything already expired. The index is what
    /// makes that removal proportional to what has expired rather than to everything ever spent.
    /// </remarks>
    internal const string SpentClientAssertionExpiryIndexName = "ix_spent_client_assertions_expires_at";

    /// <summary>The order the unspent signal tickets are aged out through.</summary>
    /// <remarks>
    /// Stated for the reason the index above it is: no query reads an unspent ticket, and the one statement that
    /// touches the table other than the insert and the spend is the removal of everything already expired. The index is
    /// what makes that removal proportional to what has expired rather than to every ticket the deployment holds.
    /// </remarks>
    internal const string ClientSignalTicketExpiryIndexName = "ix_client_signal_tickets_expires_at";

    /// <summary>The order the sessions past their expiry are removed through.</summary>
    /// <remarks>
    /// Stated for the reason the two indexes above it are: nothing queries a session by when it expires, and the sweep
    /// is the only path that reaches a row by its age at all. Every other one reaches a row by a key the table already
    /// carries — the mint, the renewal, the revocation, and the verification by the identifier, the disable by the
    /// credential, and a deleted credential or an erased user through the cascading foreign keys, whose own index
    /// entries EF Core derives. The index is what makes the sweep proportional to what has expired
    /// rather than to every session the deployment is holding, which matters more here than for either of those two —
    /// a session lives thirty days, so the table it walks is the deployment's whole signed-in population.
    /// </remarks>
    internal const string ClientSessionExpiryIndexName = "ix_client_sessions_expires_at";

    /// <summary>The key one event of a Discover run is written under.</summary>
    /// <remarks>
    /// Stated because it is the promise rather than the storage: a run's sequence starts at one and never skips, and a
    /// composite key over the run and the sequence is what makes a second row under one number impossible instead of
    /// unlikely. It is also the order a read walks — everything of one run after a cursor, in sequence — so the key
    /// serves the only query this table has.
    /// </remarks>
    internal const string DiscoveryRunEventPrimaryKeyConstraintName = "pk_discovery_run_events";

    /// <summary>The key one entry of an Agent conversation is written under.</summary>
    /// <remarks>
    /// Stated for the reason the Discover run's is: a conversation's places start at one and never skip, and a
    /// composite key over the conversation and the place is what makes a second row under one number impossible
    /// instead of unlikely. It is also the order the main read walks — everything of one conversation after a cursor.
    /// </remarks>
    internal const string AgentConversationEntryPrimaryKeyConstraintName = "pk_agent_conversation_entries";

    /// <summary>The index a person's conversation history is read by.</summary>
    /// <remarks>
    /// Stated because it carries two jobs rather than one. It orders the history the client draws — one person's
    /// conversations, most recently active first — and because the person leads it, it is also what the foreign key
    /// and an erasure of that person reach these rows by, so no second index on the person alone is declared.
    /// </remarks>
    internal const string AgentConversationHistoryIndexName = "ix_agent_conversations_user_last_activity";

    /// <summary>The partial index that finds where an offer a conversation made now stands.</summary>
    /// <remarks>
    /// Stated because of what rests on it: accepting a proposal is what permits an act with a side effect, so the
    /// statement recording an answer is conditional on where that offer already stands, and this is what it reads. It
    /// is partial because the rows answering an offer are a small minority of a conversation's entries, and an index
    /// over all of them would be rewritten on every block a run composes to serve a query only a press makes.
    /// </remarks>
    internal const string AgentConversationAnsweredProposalIndexName = "ix_agent_conversation_entries_answered";

    /// <summary>The key one export of a mailbox is written under.</summary>
    internal const string MailboxExportPrimaryKeyConstraintName = "pk_mailbox_exports";

    /// <summary>The foreign key that removes an account's exports with the account.</summary>
    /// <remarks>
    /// Stated because of what it disposes of: an export's record is what says an archive of somebody's whole mailbox
    /// exists and where it is kept, so an account erased while an export of it is downloadable must not leave that
    /// record behind. The object the row pointed at is removed by the erasure path, and anything that path misses is an
    /// orphan the content reclamation sweeps.
    /// </remarks>
    internal const string MailboxExportAccountForeignKeyName = "fk_mailbox_exports_mailbox_accounts";

    /// <summary>The order one account's exports are listed in, newest first.</summary>
    internal const string MailboxExportAccountTimelineIndexName = "ix_mailbox_exports_account_requested";

    /// <summary>The order the archives past their retention are deleted through.</summary>
    /// <remarks>
    /// Stated for the reason the session expiry index is: nothing queries an export by when its archive expires except
    /// the pass that deletes what has come due, and the index is what makes that pass proportional to what has expired
    /// rather than to every export the deployment has ever been asked for. It is partial because only an archive that
    /// exists has an expiry at all.
    /// </remarks>
    internal const string MailboxExportExpiryIndexName = "ix_mailbox_exports_expires_at";

    /// <summary>The order one person's task list is both read and drawn in.</summary>
    /// <remarks>
    /// Stated because the name a convention would compose says nothing about the ordering being the point: the list is
    /// one person's tasks soonest due first, which PostgreSQL leaves the undated ones at the end of, and the identifier
    /// is what keeps two tasks due on one day in a stable order across reads.
    /// </remarks>
    internal const string PersonalTaskDueOrderIndexName = "ix_tasks_user_due";
}
