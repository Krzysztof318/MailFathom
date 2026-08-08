# Stored email schema

<!-- describes: src/Infrastructure/Persistence/**, src/Domain/Emails/**, src/Application/Emails/Embeddings/** -->

`stored_emails` holds the normalized metadata a mailbox timeline is read from. Its raw MIME lives in a separate one-to-one table, `email_message_contents`, and the text derived from that MIME lives in a third, `email_search_documents`, so nothing that lists or filters mail ever loads a `bytea` value, a body's worth of text, or a search vector — let alone tracks one in the change tracker.

This page describes the table as the EF Core model declares it and as the reviewed baseline migration creates it. [Specification 19](https://github.com/Krzysztof318/MailFathom/blob/main/specs/19-ef-core-migration-baseline-and-apply-policy.md) generated that migration, so PostgreSQL has now had its say: the types, constraints, and indexes below are the ones a schema dump reports rather than the ones a model was hoped to produce. How the schema reaches a database is at the end of this page.

## What a row records

The columns fall into five groups, each answering a different question.

**Occurrence identity.** `mail_folder_id`, `uid_validity`, and `uid` are the stable remote identity of one message in one folder, and `id` is the local UUIDv7 that every other table references. `mailbox_account_id` is a copy of the owning folder's account: the account timeline index leads with it, and an index cannot span a join. Nothing repoints a folder at another account, so the copy is written with the row and never revised.

A row is therefore an occurrence and nothing above one. Two folders holding the same message — because the owner copied it, or because MailFathom did — are two rows, each with its own raw MIME, search document, chunks, and vectors, and no stored identity joins them; [ADR 0008](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md) records that decision and what it costs, and [what a message MailFathom copied becomes locally](../features/imap-synchronization.md#what-a-message-mailfathom-copied-becomes-locally) states it as behavior. The cascade below is what makes that safe rather than merely duplicated: each row erases its own derived data, so removing one copy reaches everything derived from it and touches nothing of the other.

**What the server reported.** `internet_message_id`, `subject`, `sent_at`, `size_octets`, and `content_availability` come from the envelope the IMAP server returned. For a message whose raw MIME was never fetched — one that exceeded the configured size limit — these are the only fields the row will ever carry.

**What the stored MIME said.** `received_at`, the sender columns, the recipient arrays, the thread columns, and the attachment summary are read out of the raw MIME that this deployment actually stored. When that read succeeds it also replaces `subject` and `sent_at`, so one row stays consistent with one set of bytes rather than mixing two parsers' answers. `internet_message_id` is the exception: a message that carried no `Message-ID` keeps the identifier the envelope reported instead of losing it.

**The remote flag snapshot.** `remote_flags_observed_at` and the five boolean markers record what the server last said about `\Seen`, `\Answered`, `\Flagged`, `\Draft`, and `\Deleted`. The reconciliation pass writes them one bounded window per run, so a row nobody has reached yet still carries the never-observed value. The timestamp exists because no combination of the booleans can distinguish "the server reports none of these" from "nobody has looked yet", and it doubles as the reconciliation queue: `ix_stored_emails_reconciliation_queue` is `(mail_folder_id, remote_flags_observed_at, uid)` over the rows that are not tombstoned, which is what lets the pass advance without a cursor of its own. It states no null sort order on purpose — a window is read as two queries, one per group, so neither orders a null against a value and both take PostgreSQL's default.

The five columns are an observation and never an instruction. Reading mail cannot reach any of them, because no read path holds a session able to issue a `STORE` at all. `\Seen` is the one flag MailFathom can ask a server to move, and only as a change the mailbox owner authored — that request is written to `mailbox_mutations` and issued against the server, and it writes nothing here. The column changes when the reconciliation pass next reads the folder and finds the flag standing somewhere new, which is the same way it would change had the owner moved the flag in their own mail client. So `is_remotely_seen` has exactly one writer whoever moved the flag, and a row read between the command and the next window still reports the last value the server was seen to hold. `\Answered`, `\Flagged`, and `\Draft` are never written under any instruction, and `\Deleted` is written only as a step of removing a message rather than as a flag anything asks for. [ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md) records why the permitted set stops there.

**The tombstone.** `remote_expunge_observed_at` records when reconciliation found the message gone from its remote folder, and is null while the server still holds it. It is a different statement from `is_remotely_deleted`, which is the server reporting the `\Deleted` flag for a message the folder still holds and still serves; conflating the two would hide mail that is merely marked for deletion. An account configured to erase local copies has no tombstone at all, because its row is removed instead — and the three tables that reference this one all cascade, so the raw MIME, the search document, and any outstanding repair request go with it. [IMAP synchronization](../features/imap-synchronization.md#reconciling-against-the-server) describes when each happens.

**The local copy kept without a remote occurrence.** `is_retained_after_authored_delete` is what separates a row the server no longer holds from a row a reader may no longer see, and only a delete MailFathom itself performed under `RetainLocalCopy` sets it. Such a row carries the tombstone timestamp as well, because the server genuinely no longer holds the message and the reconciliation queue has to stop selecting it; the flag is what keeps it inside the mailbox queries the timestamp would otherwise take it out of. So a row is excluded from every mailbox query when `remote_expunge_observed_at` is set **and** this flag is false, while `ix_stored_emails_reconciliation_queue` filters on the timestamp alone. Nothing else sets it: a disappearance somebody else caused is answered by `RemotelyDeletedEmailDisposition`, which has no value that keeps the mail readable, and carrying the row onto a new occurrence clears the flag along with the timestamp. [What becomes of a message MailFathom deleted](../features/imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself) states which disposition produces which of the three outcomes.

**Concurrency.** `ConcurrencyVersion` maps onto the PostgreSQL `xmin` system column rather than a column of its own, so PostgreSQL maintains the token and no writer has to.

### Sender and recipients

The sender is stored as three columns: the display name and address as the message wrote them, and the upper-cased comparison form that every filter and index matches on. The `From` header supplies it. `Sender` is the fallback and only stands in for a message that named no author at all, because it names whoever submitted a message written on someone else's behalf and therefore answers a different question.

Recipients are PostgreSQL `text[]` columns — `to_addresses`, `cc_addresses`, `reply_to_addresses` — rather than a join table, because every planned query tests containment rather than joining to recipient rows. They hold the comparison form only. A recipient's display name is mail content that no planned query filters or sorts on, and a second copy of it would widen the access, export, and erasure surface for nothing; a reader that needs the names re-derives them from the stored raw MIME, which [specification 14](https://github.com/Krzysztof318/MailFathom/blob/main/specs/14-email-content-read-model.md) parses anyway.

### Bounds on what a header may contribute

Nothing between the mail server and a row bounds a header's length or how many addresses it names. The MIME reader bounds a message's *structure* — part count and nesting depth — but not the width of a single header, so the persistence mapping applies its own ceilings: 320 octets per address, 998 per message identifier, 256 addresses per recipient array, and 64 thread ancestors.

A value over a ceiling is **dropped, not truncated**, and the row keeps the rest. Both halves of that are deliberate.

Letting the value through would be worse than losing it. The column would reject the write, the retry budget would run out, the folder checkpoint would never advance past the message, and every later run would stop on the same one — one malformed header would halt synchronization of the folder behind it.

Truncating would be worse still. A prefix of a message identifier is an identifier another message may legitimately carry, so a truncated one would assemble a thread out of unrelated conversations, and a truncated address would name a mailbox nobody wrote. The columns are a filter index over what a message said, not a second copy of it; the complete headers stay in the raw MIME that specification 14 reads.

Where a bound cuts a sequence, it keeps the end that answers the question. Recipients keep header order from the first, because that is the order a reader sees them in. Thread references keep the ancestors nearest to this message, because that is the end of the path a thread view walks first.

### Attachment summary

The row keeps the indexable part of what the MIME reader found and only that: `attachment_count`, `attachment_total_size_octets`, `inline_resource_count`, and the `is_encrypted`, `carries_unverified_signature`, and `contains_unexpanded_tnef_part` markers.

**The per-attachment list of file names, media types, and sizes is deliberately not persisted.** The same reasoning as for recipient display names applies, and one more: a second representation of the attachment list can drift from the raw MIME it was derived from, and re-deriving it costs nothing in a pass a body reader is already making. [Email content](../features/email-content.md#the-descriptions-are-re-derived-never-stored) is the read that makes it. The signature marker is named for presence rather than verification because nothing here verifies anything; a column called "signed" would be read as an authenticity result by every query that later touched it.

## The derived search document

`email_search_documents` is one-to-one with `stored_emails` and holds what lexical search reads: `subject_text`, `participant_addresses`, `body_text`, `body_text_before_trimming`, `text_source`, `extracted_at`, and the generated `search_vector`. [Body text and the lexical index](../features/imap-synchronization.md#body-text-and-the-lexical-index) describes how each of them is derived. Every stored email has one, including a message whose body was never read: that row carries the envelope's subject alone and records its text source as not extracted, so an oversized or unparseable message is still findable rather than absent from search entirely.

It is a table of its own for the reason raw MIME is. The text is large, only search reads it, and a timeline query that materialized a stored-email row would otherwise carry a body and its search vector through the change tracker on the way to a view that shows neither. Deleting a message cascades to it, so everything derived from a message is erased with the message.

`subject_text` and `participant_addresses` are bounded copies rather than references to the columns of the same name on `stored_emails`. A PostgreSQL generated column must be immutable and can only read its own row, and the array-to-text functions that would flatten the recipient arrays are merely stable — they call element output functions that need not be immutable. Copying the two values into this row at write time keeps the column's expression trivially immutable instead of requiring a custom SQL function, which no migration exists to create.

The copies are bounded more tightly than the columns they come from: 2000 characters of subject and 64 participant addresses in total. A `tsvector` cannot exceed one megabyte, and the whole document — subject, addresses, and up to `MaxExtractedTextCharacters` of body — shares that budget. Exceeding it would not degrade search; it would make the row unwritable, because the generated column is computed on every insert.

## Message chunks

`email_chunks` holds the passages a message's extracted text was cut into, many rows per stored email. Each carries its `Ordinal` in reading order, the `StartOffset` the passage begins at in the extracted text, the `Text` itself, a `ContentHash` of `character(64)`, the `RuleSetVersion` it was cut to, whether it `IsDerivedFromLossyHtml`, and when it was `DerivedAt`. [Message chunks](../features/message-chunks.md) describes the boundary rules and what the hash covers.

The key is a surrogate UUIDv7 rather than the email and the ordinal together, because a vector row will hang on one chunk and a composite key would put a re-cut message's ordinals into every table that references it. The pair is `ix_email_chunks_email_ordinal` instead, unique, which is what a reader of one message's passages orders by and what stops a re-cut from writing an ordinal twice.

`Text` is the one column here that no bound applies to. Its length is decided by the chunking rules rather than by a sender, and extraction has already bounded the text all of one message's chunks are cut from, so a column bound would add nothing except a write failure the first time those rules are tuned upwards. `ContentHash` is fixed-length text rather than `bytea`, unlike the raw MIME digest: this one is compared and read — re-chunking decides what to write by comparing digests — while the MIME digest only ever round-trips between one writer and one reader.

Only a message that yielded text has rows here, and deleting a message cascades to them.

## Embedding profiles

`embedding_profiles` holds one row per vector space this deployment has embedded into. Nothing writes one yet — the row is created by the activation that takes a declared model up and starts spending against it — but the columns are what a stored vector's attribution will point at, and they are permanent from this migration onwards.

A profile is **the geometry of a vector space and nothing else**: `Provider`, `ModelIdentifier`, `ModelVersion` where the provider exposes one, `Dimension`, `DistanceMetric`, and the three columns that make up the input preparation — `InputCharacterLimit`, `PassageInstruction`, and `NormalizesVector`. Those are exactly the properties that decide whether two vectors can be compared. [ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md) is where each of them was argued.

Three things about that list are decisions rather than defaults.

**The chunk boundary rules are not part of it.** They belong to the chunk's own content hash, so a vector is attributable to both halves of what produced it — the chunk row says what text was embedded and under which rules, this row says what embedded it. Folding the rules in here would price a boundary change, which reaches no network, as a full paid re-embed of the mailbox. [Message chunks](../features/message-chunks.md#what-the-hash-covers) describes the half this table does not carry.

**`Provider` names the vendor whose model defines the space, not the endpoint it is reached through.** That is what lets one profile be served by a chain of endpoints offering the same model, where the endpoint can fail over without the vector space changing underneath vectors already stored. It is a vendor-supplied name stored verbatim, like `ModelIdentifier`, because neither set is MailFathom's to close.

**`Dimension` is the width the stored vectors actually have**, never the model's nominal one. Where a model is narrowed to what the database can index, the narrowed number is what belongs here; a profile claiming the nominal width would be describing vectors that do not exist.

`IdentityFingerprint` is a SHA-256 digest over every column above, written as sixty-four lowercase hexadecimal characters, and `ix_embedding_profiles_identity_fingerprint` is unique over it. That index is what makes activation idempotent: re-declaring a geometry that is already registered resolves to the row that exists rather than inserting a second one whose vectors would be produced from scratch for nothing, so returning to a previous model is a switch rather than a duplicate. Every field of the digest is length-prefixed and every number is big-endian, and an absent optional value is written as a presence marker rather than skipped, so the encoding is one-to-one.

What stays mutable is the lifecycle: `LifecycleState` — building, active, or superseded — with `RegisteredAt`, and `ActivatedAt` and `SupersededAt`, each null until its event has happened. **There is no generation counter.** The profile *is* the generation, so two generations coexisting while a new one is built are two rows, and no read path has a second field it must remember to consult.

Nothing operational reaches this table. The endpoint address, the credential, the batch size, the request rate, the concurrency, and the spending ceilings are configuration, which is what makes rotating a key or raising a rate limit an edit rather than a re-embed — and what means no column here can be edited into disagreeing with the vectors it describes.

## Stored vectors

`email_embeddings` holds one vector per chunk per profile. Its key is `(EmailChunkId, EmbeddingProfileId)`, named `pk_email_embeddings`, because that pair is what a vector *is*: re-embedding a passage under the profile already serving it replaces the row rather than adding one, and the constraint is what an idempotent upsert conflicts on. Nothing references a vector row, so unlike a chunk it needs no identifier of its own.

`Embedding` is pgvector's **dimensionless `vector`** rather than `vector(N)`. That is what lets two profiles of different widths share one table, each reachable by an expression index built when that profile is activated; declaring a width on the column would fix the whole table to one profile's geometry. Before such an index exists, exact vector search remains correct and slower.

Dropping the width from the column does not drop it from the schema, and the pair of constraints that replaces it is the point of the table's shape:

| Constraint | What it refuses |
|---|---|
| `ck_email_embeddings_dimension` | A vector whose actual length disagrees with the `Dimension` column beside it |
| `fk_email_embeddings_embedding_profiles` on `(EmbeddingProfileId, Dimension)` → `ak_embedding_profiles_id_dimension` | A `Dimension` the named profile never declared |

Neither half works alone. PostgreSQL evaluates a check constraint against one row, so without the foreign key the check would only prove that a vector agrees with a number nobody constrained; without the check, the foreign key would only prove that a number matches a profile. Together they mean a provider returning a vector of an unexpected width fails at the write instead of corrupting a search. That is deliberately enforced by the database rather than by the code that writes, because a wrong-length vector is a defect no query would report — it would return a plausible-looking distance rather than an error.

The two references behave differently on delete, and that is the whole erasure story. **The chunk cascades**: a vector hangs on a chunk, a chunk hangs on a stored email, so deleting a message reaches every vector derived from it without a rule anybody has to remember. **The profile restricts**: a profile is what a stored vector's attribution points at, so the schema refuses to remove one while a vector still names it. `ix_email_embeddings_profile` is what a whole generation is read by when a superseded one is removed in bounded batches; without it that read would scan every vector in the table.

`GeneratedAt` records when the vector was produced, which tells a re-embed from an original one apart.

## Outstanding content repair requests

`email_content_repair_requests` is one-to-one with `stored_emails` and exists only while a read has found an email's
stored content unusable. It records the `Defect` — missing content, a byte length or SHA-256 digest that disagrees with
what was written, or a payload nothing can parse — together with when the defect was first and last seen and how many
reads have hit it. [Email content](../features/email-content.md#when-the-local-copy-is-unusable) describes what produces
each value.

It is a table rather than four columns on `stored_emails` because the rows are sparse, they are read as a work list, and
a repair that succeeds deletes a row instead of nulling columns on a row it must not otherwise touch. The write is a
single `INSERT ... ON CONFLICT ("StoredEmailId") DO UPDATE`, so the idempotency the read path needs is the primary key's
rather than a retry's: two readers meeting the same damaged message concurrently leave one row and one accurate count.
Nothing drains the table yet — performing the repair belongs to the synchronizer — and the cascade from `stored_emails`
removes a request with the email it is about.

## Recorded mailbox mutations

`mailbox_mutations` holds one row per change MailFathom has been asked to make to a remote mailbox, written **before**
the first IMAP command is issued and advanced as the sequence proceeds. A move over IMAP without the `MOVE` extension is
three commands — copy, flag deleted, expunge — and a process can die between any two of them; retrying from the
beginning would put a second copy in the destination folder, and skipping the retry would leave the message in both.
Neither state is recoverable by looking at the mailbox afterwards, because both are indistinguishable from a move
somebody made by hand. Writing the intent down first is what makes the sequence resumable.

Each row carries the local email, the source occurrence it was aimed at — the folder binding with its `UidValidity` and
`Uid` — the `Mutation` by name, the requester, the parameters that mutation takes, and how far it has got. The source
occurrence is stored beside the `StoredEmailId` rather than read back through it, because the email moves: the command
that was issued was aimed at one folder and one UID, and a record that followed the email would stop describing it.

`LocalDisposition` is one of those parameters, and the only one a delete takes: it names what becomes of the local copy
once the server no longer holds the message, and is null for every other mutation. It is stored rather than read where
the delete finishes because those are different runs — the deletion is issued now and the local copy is disposed of by
the synchronization run that later sees the message gone — so reading the account's configuration there would apply
whatever an operator had changed it to in the meantime. Writing it with the row is what makes a setting changed
mid-flight govern the deletes authored after the change and leave one already begun exactly as it was. A row this
column is missing from is refused on the way back rather than read as some fallback, because every value destroys
something a different one keeps.

`Stage` is the sequence's own vocabulary rather than a generic pending and done, because it is what a retry resumes
from:

| Stage | What it says | Which mutations reach it |
|---|---|---|
| `Recorded` | The intent is durable and nothing has reached the server | all four |
| `PlacementIssued` | The command that would place the email has gone out and its answer was never read | relocate, copy |
| `PlacementConfirmed` | The server acknowledged the placement, and named it where it supplied `COPYUID` | relocate, copy |
| `SourceFlaggedDeleted` | The source carries `\Deleted` and only the expunge remains | relocate over the fallback, delete |
| `Completed` | The change is made, and asking again performs nothing | all four |
| `Abandoned` | Nothing will attempt it again, and `LastFailureCode` says what ended it | all four |

`PlacementIssued` is the one stage a retry may not act on. A `COPY` issued twice is a second message rather than a
repeat of the first, so a mutation found there is reported as an unknown outcome, has
`MailboxMutationOutcomeUnknown` (25002) written to `LastFailureCode` so an operator reading the row sees why it is
stuck, and is left for a person to resolve. Every other stage resumes: a relocation found at `PlacementConfirmed`
removes its source without copying again, and a delete found at `SourceFlaggedDeleted` reissues only the expunge. A
`\Seen` change never leaves `Recorded` until it completes — the store is idempotent on the wire, and its record exists
for provenance rather than for retry safety.

`RequiresSourceRemoval` is what makes that resumption safe, and it is written together with `PlacementIssued` rather
than worked out later. `MOVE` removes the source as part of the same command and a copy does not, so
`PlacementConfirmed` means opposite things depending on which ran — and the connection a retry lands on is not required
to be the one that answered the first. A fallback relocation resumed against a server that now advertises `MOVE` would
otherwise be read as already finished, leaving the email in both folders permanently with nothing left to surface it.
That is the duplication the record exists to prevent, so the answer is durable rather than inferred. It is a fact about
the sequence rather than a name for the operation: which protocol path carried a relocation still reaches no log above
`Debug`, no span, and no metric dimension, exactly as [ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
requires.

`AttemptCount` is written before each attempt rather than after it, which is what makes an attempt that kills the
process count against `MailSynchronization:MaxMutationAttempts`. Spending that bound moves the row to `Abandoned`, and a
row that is not `Completed` stays in the answer an operator reads — being given up on is what stops a change being
retried, and it would be worth nothing if it also stopped the change being seen. Two other things reach `Abandoned`
without spending the bound: a refusal the server has already given once and will give again, and an unacknowledged
placement that outlasts `MailSynchronization:UnknownMutationOutcomeGrace` without the mailbox settling it.

Every row that is not `Completed` is read once per account run, oldest first and bounded by
`MailSynchronization:MaxMutationsPerConvergencePass`, and each is carried further or given up on. The same rows are
counted by stage in one grouped query per run, which is what the outstanding-mutation gauges report; the partial index
below is what makes both cheap, because it holds what is outstanding rather than the mailbox's whole mutation history.

`PlacementObservedAt` and `SourceRemovalObservedAt` are what synchronization has since seen come back, and they are
separate facts from `Stage` because they answer a different question and are written by a different run. The stage says
what the server acknowledged when the command went out; these say that an ordinary synchronization run has met the
occurrences the change moved and recognized them as this record's own. A relocation reaching `Completed` still arrives
in the destination folder later as a discovery something has to join to the email already stored, and its source
occurrence still vanishes from the folder it left — and left alone, that pair is a new message and a deleted one.

Both are written once and never moved afterwards, and that is what scopes an arrival's provenance to the one change the
record describes: a record whose placement has been met answers for no later discovery at the same UID.

A `\Seen` store carries no such column and needs none, because it moves no occurrence for a run to come back and meet.
Its provenance is settled against the stored email's own `remote_flags_observed_at` instead: a store accounts for a flag
reading only while that column still predates the moment the store completed. Every window advances it for every
occurrence it asked about, so the first reading after the store is the whole of what the record can answer for. A column
here would answer only for the readings that happened to *differ* — and an owner who reverted the flag before the first
reading would leave it unwritten and have their own later change silenced by it.

The join is the server's own `COPYUID` answer for the placement and the record's own source occurrence for the
disappearance. A relocation whose server named no placement matches no discovery at all, and `PlacementObservedAt` stays
null instead: finding the message by searching the destination folder for something that looks like it would replace a
fact with a guess, and a header a provider rewrote on copy or a `Message-ID` that legitimately appears twice is wrong in
both directions. The disappearance needs no `COPYUID`, because the source occurrence was written down before the first
command was issued.

The source half is settled by carrying the row across where nothing has settled it already, because that is what takes
the email out of the source folder locally: no later reconciliation window can select it there, so no later run could
observe the disappearance the record would otherwise keep waiting for. A row whose halves are both accounted for leaves
the candidates a later discovery is matched against; a relocation still owing one is where an operator looks when a
message moved and the local mailbox has not caught up.

No mail content is here. A folder path, a UID, a mutation name, and a requester identity are the server's own or
MailFathom's own names for things, and a failure is kept as its five-digit code rather than as the message text
assembled at the failure site.

## Indexes

| Index | Columns | Purpose |
|---|---|---|
| `ix_stored_emails_folder_uidvalidity_uid` | `(mail_folder_id, uid_validity, uid)`, unique | Remote occurrence identity, which is what makes synchronization idempotent |
| `ix_stored_emails_account_timeline` | `(mailbox_account_id, received_at DESC NULLS LAST, id DESC)` | The account-wide timeline |
| `ix_stored_emails_folder_timeline` | `(mail_folder_id, received_at DESC NULLS LAST, id DESC)` | The per-folder timeline |
| `ix_stored_emails_sender` | `(sender_normalized_address)` | Filtering by who sent a message |
| `ix_stored_emails_to_addresses` | `(to_addresses)`, GIN | Containment tests over the `To` recipients |
| `ix_stored_emails_cc_addresses` | `(cc_addresses)`, GIN | Containment tests over the `Cc` recipients |
| `ix_stored_emails_reply_to_addresses` | `(reply_to_addresses)`, GIN | Containment tests over the `Reply-To` addresses |
| `ix_email_search_documents_search_vector` | `(search_vector)`, GIN | Lexical search over subject, participants, and body text |
| `ix_email_chunks_email_ordinal` | `(StoredEmailId, Ordinal)`, unique | One message's passages in reading order, and the constraint a re-cut cannot write an ordinal twice past |
| `ix_embedding_profiles_identity_fingerprint` | `(IdentityFingerprint)`, unique | One row per vector space, which is what makes activation idempotent |
| `ix_email_embeddings_profile` | `(EmbeddingProfileId, Dimension)` | Reading a whole generation, which is how a superseded one is removed |
| `ix_mailbox_mutations_identity` | `(MailFolderId, UidValidity, Uid, RequesterOrigin, RequesterIdentity, Mutation)`, unique | A mutation's idempotency identity, which is what makes the same request twice perform one change |
| `ix_mailbox_mutations_outstanding` | `(MailboxAccountId, RecordedAt)` where the stage is not `Completed` | The changes an operator asks about: those in flight and those given up on |
| `ix_mailbox_mutations_placement` | `(MailboxAccountId, DestinationFolderPath, PlacementUidValidity, PlacementUid)` where `PlacementObservedAt` is null | The question the forward pass asks of every batch it discovers: is one of these UIDs where a relocation or a copy put an email |

The recipient and search-vector indexes are GIN rather than B-tree because both serve containment tests. A B-tree over an array column serves only equality against a whole array, and over a `tsvector` it serves nothing search asks for; a GIN index is what turns either into an index scan.

The partial indexes over remotely deleted messages that the architecture draft lists are still deliberately absent; they wait for specification 10, which introduces the state they would filter on. The per-profile HNSW index is absent from the migrations for a different reason, and permanently.

### The index no migration creates

An approximate index over `Embedding` covers one width and one generation, and neither is known when a migration runs. So `email_embeddings` carries one per profile, built and removed as a profile's lifecycle asks rather than when the table is created:

```sql
CREATE INDEX IF NOT EXISTS "ix_email_embeddings_hnsw_0198f3d24b6a7c1e9f042a5b8c7d6e10"
ON email_embeddings USING hnsw (("Embedding"::vector(1536)) vector_cosine_ops)
WHERE "EmbeddingProfileId" = '0198f3d2-4b6a-7c1e-9f04-2a5b8c7d6e10'::uuid
```

Both unusual halves follow from the dimensionless column. The cast is what gives HNSW a width to index; the predicate is what keeps this index over the rows that have that width, so a second generation is served by an index of its own instead of colliding with this one. The operator class follows the profile's metric — `vector_cosine_ops`, `vector_ip_ops`, or `vector_l2_ops` — because a space indexed under a distance it was not built for returns a plausible number rather than an error.

The name is the profile's identifier written as thirty-two hexadecimal digits, which does two things. It keeps the whole name inside the sixty-three bytes PostgreSQL keeps of an identifier, where truncation would let two profiles share one index. And because a profile's identity is immutable, an index already carrying the name *is* the index a repeated build would have produced — which is what makes `IF NOT EXISTS` safe here, given that PostgreSQL does not compare an existing index against the one asked for.

**Nothing in either statement comes from a caller.** PostgreSQL accepts no parameter in a utility statement, so the width, the operator class, the predicate value, and the name are all part of the text; each is read from the registered profile's own immutable columns or chosen by a closed mapping over an enum. The provider and model names a profile carries never enter it at all.

Building one is therefore an administrative act rather than a schema migration, and it is the one place MailFathom changes the schema outside the artifact an operator applies. [Applying the database schema](../operations/database-schema.md#the-one-index-mailfathom-creates-itself) states what that costs a deployment that separates its migrating role from its serving one. Before an index exists, a vector search over that profile is exact — correct, and linear in the number of vectors — which is what makes a failure to build one a performance finding rather than a wrong answer.

## The timeline ordering contract

Keyset pagination needs a total order, not merely a sort: two pages are contiguous only when every row falls on exactly one side of the key the previous page ended on. `EmailTimelinePosition` in `Domain` is the single statement of that order, and the two timeline indexes reproduce it column for column. A change to either is a change to both.

The order is: **received timestamp descending, with an unknown timestamp last, then the local identifier descending.**

Two parts of that are decisions rather than defaults.

A message can carry no usable received timestamp, and PostgreSQL orders nulls first under `DESC`. Inheriting that default would float every message nobody could date above the newest mail, on every page, forever — so both indexes spell out `NULLS LAST` and the in-memory comparer treats an unknown timestamp as older than every known one.

The tiebreaker compares identifiers as the sixteen big-endian octets PostgreSQL orders a `uuid` column by, written out rather than delegated to `Guid.CompareTo`. That method agrees with the octet order on the current runtime, but it documents only that its result is suitable for sorting — not that it is the order a `uuid` column uses. A page boundary computed in memory is resumed from by a query the index plans, so the tiebreaker has to be the index's order by construction.

Unit tests pin the contract, including where an undated message lands. That the server then returns rows in the same order, and plans a timeline query against the index rather than sorting the table, is verified against real PostgreSQL — see [What the integration suite proves](#what-the-integration-suite-proves) below.

## Privacy classification

Participants, subject, and thread identifiers are personal data. They are stored because a timeline and a filter cannot work without them, and the columns above are the minimum that supports the planned queries — which is why display names other than the sender's, and the attachment list, are not among them. No projection introduced here selects raw MIME, and the content relationship stays unloaded by default so a mailbox query cannot pull a `bytea` value into the change tracker by accident.

The derived search document is not a lesser classification of the same data. Body text, the copied subject and addresses, and the search vector built from them are mail content, and none of them is anonymous merely because it was derived; they inherit the retention, access, export, and erasure obligations of the message they came from. The cascade from `stored_emails` is what makes that structural rather than a rule somebody has to remember.

`mailbox_mutations` is derived personal data too, and for a reason worth stating plainly: a mutation history says where a person's mail has been and what was done to it. It therefore inherits the retention and deletion obligations of the email it describes rather than outliving it, and the cascade from `stored_emails` is what makes that structural — including where the recorded mutation was the deletion itself.

The same holds for `email_chunks`, and one thing about it is deliberate: a chunk records the message it came from and the span inside it, and nothing else. The account, folder, sender, recipients, date, and subject a retrieval will want to cite are reached through that message rather than copied onto the passage, so cutting mail into chunks widens no access, export, or erasure surface — it only adds rows the same cascade erases.

`email_embeddings` is the same again, one step further out. A vector is derived from mail content and inherits the source message's classification, retention, access, export, and erasure obligations whole; nothing about being a list of numbers makes it a lesser copy of the words it stands for, and it is not anonymous because it cannot be read back by eye. It carries no text and no coordinates of its own — the chunk it hangs on answers both — and the cascade from that chunk is what makes erasure structural rather than a rule somebody has to remember. No vector, no chunk text, and no digest reaches a log, a metric, a trace, or an error message.

`embedding_profiles` is the exception on this page: it holds no personal data at all. It describes a model, and the credential that reaches that model is configuration rather than a column here, so nothing in this table is a secret or is derived from anybody's mail.

## How this schema reaches a database

One reviewed migration, `Initial`, creates all of it. There is no bootstrap that builds the schema from the model at startup any more: the host reads the migration history, and refuses to start when the database has not applied every migration the running build defines.

Locally the AppHost's `mailfathom-migrations` resource applies it before the host starts. Elsewhere applying it is an explicit deployment step. [Local development](../operations/local-development.md) documents both. Every migration is permanent: a model change appends one and never regenerates this baseline, and the `add-migration` skill is that workflow.

Not everything in this database is mail. `mailbox_refresh_tokens` holds one sealed OAuth refresh token per account, added by the `AddMailboxRefreshTokens` migration, and it is documented where the credential it holds is — [mailbox OAuth](../operations/mailbox-oauth.md#rotation). It is named here so a reader of a schema dump knows which page owns it, and because it is the one table nothing on this page cascades from: it carries no foreign key onto `mailbox_accounts`, since a token has to be able to exist for an account that has never synchronized.

`uid_validity` and `uid` are modelled as CLR `uint` because that is the IMAP wire type, and PostgreSQL has no native unsigned 32-bit integer. The generated migration maps both to `bigint`, which represents the whole unsigned 32-bit range exactly, so the unique index on `(mail_folder_id, uid_validity, uid)` and the checkpoint comparisons order the same way the IMAP values do.

Table names are the snake_case ones above. Column names are not: the model renames tables and leaves columns as it names the properties, so the physical columns are `"UidValidity"`, `"ReceivedAt"`, and so on, and hand-written SQL against them has to quote that casing. The names in this page are the schema's concepts rather than a transcription of the DDL; the migration is the transcription.

## What the integration suite proves

Every claim on this page that is a claim about PostgreSQL rather than about the model is verified by `tests/IntegrationTests` against the orchestrated server, because a unit test cannot reach any of them. The classes involved carry `[RequiresIntegrationCoverage]` for exactly that reason, and [local development](../operations/local-development.md) describes how the suite runs.

- The baseline migration applies to an empty database and leaves no migration pending, and the text search configuration the generated column was built with is read back out of PostgreSQL's own catalogue rather than from the model — which is what lets the startup gate refuse a database whose lexical index disagrees with the running host.
- The unique index refuses a duplicate occurrence that neither writer could have seen, which is the PostgreSQL-side half of idempotent synchronization: two overlapping runs each stage an insert, and the database rather than the application decides that only one lands.
- An occurrence identified by the largest UID IMAP can hand out round-trips through its `bigint` columns unchanged.
- Raw MIME round-trips through `bytea` with its recorded length and SHA-256 intact, including a payload large enough that PostgreSQL stores it out of line, and re-storing an occurrence replaces the one existing row rather than reading its payload back into memory.
- The transaction a persistence session opens covers SQL the provider had already executed: a set-based update issued and then abandoned without a commit leaves the earlier payload in place.
- A losing writer is reported rather than raised where the constraint says a race happened — a second binding of the same alias generation, and a stored email whose `xmin` token another committed transaction made stale — and is raised where it says the data is already there.
- The timeline indexes return rows in the order `EmailTimelinePosition.NewestFirst` describes, over data with shared and absent timestamps, and a keyset walk over that order visits every row exactly once. The `uuid` tiebreaker is the part only a server can settle.
- The [mailbox listing read model](../features/mailbox-queries.md) issues that walk over the same data in both directions and gets the same order back, every one of its filters translates to SQL — including the array containment a recipient filter needs and the escaped pattern a subject fragment needs — and its projection leaves the change tracker empty. A predicate that does not translate is a runtime failure rather than a compiler error, which is why the read model's queries belong here as well as in the unit suite.
- The generated search vector is computed by PostgreSQL from the subject, participants, and body beside it; the GIN index serves the query shape search issues; and query text carrying SQL statements and `tsquery` operators is read as words, matching documents whose text holds those words and leaving the table intact.
- A stored vector cannot disagree with its profile: a vector whose length differs from the `Dimension` beside it, and a `Dimension` the named profile never declared, are both refused at the write, while the matching width is stored. Two profiles of different widths coexist in the one dimensionless column, re-registering a geometry already present is refused by the fingerprint index, and deleting a message erases the vectors derived from it while the profile they named survives.
- The [search read model](../features/email-search.md) composes that vector, `websearch_to_tsquery`, `ts_rank`, and `ts_headline` into commands PostgreSQL accepts — a malformed headline option list is a runtime failure rather than a compiler error — ranks the window it returns, cuts snippets inside the configured bounds, and leaves the change tracker empty across every query it issues.
- The same read model's vector half ranks the eligible mail by a correlated minimum over each message's own embedded passages, measured by the operator the active profile's metric names. Mail carrying no vector under that profile is absent from the ranking rather than distant, and the ordering is the part only a server can settle: whether the distance operator, the aggregate, and the caller's filters compose into one statement at all is a translation question rather than a compile-time one.
