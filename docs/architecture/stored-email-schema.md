# Stored email schema

<!-- describes: src/Infrastructure/Persistence/**, src/Domain/Emails/**, src/Application/Emails/Embeddings/** -->

`stored_emails` holds the normalized metadata a mailbox timeline is read from. Its raw MIME lives in a separate one-to-one table, `email_message_contents`, and the text derived from that MIME lives in a third, `email_search_documents`, so nothing that lists or filters mail ever loads a `bytea` value, a body's worth of text, or a search vector — let alone tracks one in the change tracker.

This page describes the table as the EF Core model declares it and as the reviewed baseline migration creates it. PostgreSQL has had its say about that migration, so the types, constraints, and indexes below are the ones a schema dump reports rather than the ones a model was hoped to produce. How the schema reaches a database is at the end of this page.

## What a row records

The columns fall into groups, each answering a different question.

**Occurrence identity.** `mail_folder_id`, `uid_validity`, and `uid` are the stable remote identity of one message in one folder, and `id` is the local UUIDv7 that every other table references. `mailbox_account_id` is a copy of the owning folder's account: the account timeline index leads with it, and an index cannot span a join. Nothing repoints a folder at another account, so the copy is written with the row and never revised.

A row is therefore an occurrence and nothing above one. Two folders holding the same message — because the owner copied it, or because MailFathom did — are two rows, each with its own raw MIME, search document, chunks, and vectors, and no stored identity joins them; [ADR 0008](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md) records that decision and what it costs, and [what a message MailFathom copied becomes locally](../features/imap-synchronization.md#what-a-message-mailfathom-copied-becomes-locally) states it as behavior. The cascade below is what makes that safe rather than merely duplicated: each row erases its own derived data, so removing one copy reaches everything derived from it and touches nothing of the other.

**What the server reported.** `internet_message_id`, `subject`, `sent_at`, `size_octets`, and `content_availability` come from the envelope the IMAP server returned. For a message whose raw MIME was never fetched these are the only fields the row carries, and `content_availability` says which of the two reasons applies. `ExceededSizeLimit` is permanent — the message is above the configured per-message limit and will be on every later run — while `AwaitingStorageHeadroom` is a queue: content storage was at its configured ceiling when the message was discovered, and a later run with room fetches it and fills the rest of the row in. The column is text rather than an integer so that reason stays readable in an ad-hoc audit query and survives any reordering of the enum.

**What the stored MIME said.** `received_at`, the sender columns, the recipient arrays, the thread columns, and the attachment summary are read out of the raw MIME that this deployment actually stored. When that read succeeds it also replaces `subject` and `sent_at`, so one row stays consistent with one set of bytes rather than mixing two parsers' answers. `internet_message_id` is the exception: a message that carried no `Message-ID` keeps the identifier the envelope reported instead of losing it.

**The remote flag snapshot.** `remote_flags_observed_at` and the five boolean markers record what the server last said about `\Seen`, `\Answered`, `\Flagged`, `\Draft`, and `\Deleted`. The reconciliation pass writes them one bounded window per run, so a row nobody has reached yet still carries the never-observed value. The timestamp exists because no combination of the booleans can distinguish "the server reports none of these" from "nobody has looked yet", and it doubles as the reconciliation queue: `ix_stored_emails_reconciliation_queue` is `(mail_folder_id, remote_flags_observed_at, uid)` over the rows that are not tombstoned, which is what lets the pass advance without a cursor of its own. It states no null sort order on purpose — a window is read as two queries, one per group, so neither orders a null against a value and both take PostgreSQL's default.

The five columns are an observation and never an instruction. Reading mail cannot reach any of them, because no read path holds a session able to issue a `STORE` at all. `\Seen` is the one flag MailFathom can ask a server to move, and only as a change the mailbox owner authored — that request is written to `mailbox_mutations` and issued against the server, and it writes nothing here. The column changes when the reconciliation pass next reads the folder and finds the flag standing somewhere new, which is the same way it would change had the owner moved the flag in their own mail client. So `is_remotely_seen` has exactly one writer whoever moved the flag, and a row read between the command and the next window still reports the last value the server was seen to hold. `\Answered`, `\Flagged`, and `\Draft` are never written under any instruction, and `\Deleted` is written only as a step of removing a message rather than as a flag anything asks for. [ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md) records why the permitted set stops there.

**The tombstone.** `remote_expunge_observed_at` records when reconciliation found the message gone from its remote folder, and is null while the server still holds it. It is a different statement from `is_remotely_deleted`, which is the server reporting the `\Deleted` flag for a message the folder still holds and still serves; conflating the two would hide mail that is merely marked for deletion. An account configured to erase local copies has no tombstone at all, because its row is removed instead — and the three tables that reference this one all cascade, so the raw MIME, the search document, and any outstanding repair request go with it. [IMAP synchronization](../features/imap-synchronization.md#reconciling-against-the-server) describes when each happens.

**The local copy kept without a remote occurrence.** `is_retained_after_authored_delete` is what separates a row the server no longer holds from a row a reader may no longer see, and only a change MailFathom itself performed under `RetainLocalCopy` sets it — a delete, or a relocation into a folder nothing mirrors, which is the same loss of the occurrence and is answered by the same setting. Such a row carries the tombstone timestamp as well, because the server genuinely no longer holds the message and the reconciliation queue has to stop selecting it; the flag is what keeps it inside the mailbox queries the timestamp would otherwise take it out of. So a row is excluded from every mailbox query when `remote_expunge_observed_at` is set **and** this flag is false, while `ix_stored_emails_reconciliation_queue` filters on the timestamp alone. Nothing else sets it: a relocation between mirrored folders carries the row into the destination instead, a disappearance somebody else caused is answered by `RemotelyDeletedEmailDisposition`, which has no value that keeps the mail readable, and carrying the row onto a new occurrence clears the flag along with the timestamp. [What becomes of a message MailFathom deleted](../features/imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself) states which disposition produces which of the three outcomes.

**What the rules have seen.** `rules_evaluated_at` records when a rule pass last evaluated this message, and is null while none has. It is not only a record: its absence is the queue a pass reads, so writing it is what takes a message out of the mail rules apply to on arrival, and `ix_stored_emails_awaiting_rule_evaluation` is what makes reading that queue proportionate to it rather than to the mailbox. The migration that adds the column stamps every row already stored, which is the same statement made about mail that existed before rules ran at all — an upgrade must not hand a deployment's first rule set an entire mailbox's history as though it had just arrived. Re-evaluating mail that already carries a value is the whole-mailbox run below, and never something an edit sets off; [mail rules](../features/mail-rules.md#when-rules-run) states both halves as behaviour.

**When this deployment first held the message.** `StoredAt` is written once, when the row is inserted, and is never revised. It is deliberately neither `sent_at` nor `received_at`: both of those are facts about the message that a sender or a mail server decided, and can be years old on a mailbox being synchronized for the first time. This is a fact about this deployment, and it is what spam classification's ordering is measured against — how long a message has waited for a verdict before everything derived from it is released anyway, which [junk is kept out of what a deployment derives from mail](../features/spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail) states as behaviour. The migration that adds the column backfills every existing row with `-infinity` rather than with the instant of the upgrade: a message stored before the column existed has by definition waited longer than any wait a deployment can configure, so it is eligible immediately, while stamping it with the upgrade would hold a whole mailbox out of the index for one more wait apiece.

**Concurrency.** `ConcurrencyVersion` maps onto the PostgreSQL `xmin` system column rather than a column of its own, so PostgreSQL maintains the token and no writer has to.

### Sender and recipients

The sender is stored as three columns: the display name and address as the message wrote them, and the upper-cased comparison form that every filter and index matches on. The `From` header supplies it. `Sender` is the fallback and only stands in for a message that named no author at all, because it names whoever submitted a message written on someone else's behalf and therefore answers a different question.

Recipients are PostgreSQL `text[]` columns — `to_addresses`, `cc_addresses`, `reply_to_addresses` — rather than a join table, because every planned query tests containment rather than joining to recipient rows. They hold the comparison form only. A recipient's display name is mail content that no planned query filters or sorts on, and a second copy of it would widen the access, export, and erasure surface for nothing; a reader that needs the names re-derives them from the stored raw MIME, which the [email content](../features/email-content.md) read model parses anyway.

### Bounds on what a header may contribute

Nothing between the mail server and a row bounds a header's length or how many addresses it names. The MIME reader bounds a message's *structure* — part count and nesting depth — but not the width of a single header, so the persistence mapping applies its own ceilings: 320 octets per address, 998 per message identifier, 256 addresses per recipient array, and 64 thread ancestors.

A value over a ceiling is **dropped, not truncated**, and the row keeps the rest. Both halves of that are deliberate.

Letting the value through would be worse than losing it. The column would reject the write, the retry budget would run out, the folder checkpoint would never advance past the message, and every later run would stop on the same one — one malformed header would halt synchronization of the folder behind it.

Truncating would be worse still. A prefix of a message identifier is an identifier another message may legitimately carry, so a truncated one would assemble a thread out of unrelated conversations, and a truncated address would name a mailbox nobody wrote. The columns are a filter index over what a message said, not a second copy of it; the complete headers stay in the raw MIME the content read model parses.

Where a bound cuts a sequence, it keeps the end that answers the question. Recipients keep header order from the first, because that is the order a reader sees them in. Thread references keep the ancestors nearest to this message, because that is the end of the path a thread view walks first.

### Attachment summary

The row keeps the indexable part of what the MIME reader found and only that: `attachment_count`, `attachment_total_size_octets`, `inline_resource_count`, and the `is_encrypted`, `carries_unverified_signature`, and `contains_unexpanded_tnef_part` markers.

**The per-attachment list of file names, media types, and sizes is deliberately not persisted.** The same reasoning as for recipient display names applies, and one more: a second representation of the attachment list can drift from the raw MIME it was derived from, and re-deriving it costs nothing in a pass a body reader is already making. [Email content](../features/email-content.md#the-descriptions-are-re-derived-never-stored) is the read that makes it. The signature marker is named for presence rather than verification because nothing here verifies anything; a column called "signed" would be read as an authenticity result by every query that later touched it.

## The derived search document

`email_search_documents` is one-to-one with `stored_emails` and holds what lexical search reads: `subject_text`, `participant_addresses`, `body_text`, `body_text_before_trimming`, `text_source`, `extracted_at`, and the generated `search_vector`. [Body text and the lexical index](../features/imap-synchronization.md#body-text-and-the-lexical-index) describes how each of them is derived. Every stored email has one, including a message whose body was never read: that row carries the envelope's subject alone and records its text source as not extracted, so an oversized or unparseable message is still findable rather than absent from search entirely.

It is a table of its own for the reason raw MIME is. The text is large, only search reads it, and a timeline query that materialized a stored-email row would otherwise carry a body and its search vector through the change tracker on the way to a view that shows neither. Deleting a message cascades to it, so everything derived from a message is erased with the message.

`subject_text` and `participant_addresses` are bounded copies rather than references to the columns of the same name on `stored_emails`. A PostgreSQL generated column must be immutable and can only read its own row, and the array-to-text functions that would flatten the recipient arrays are merely stable — they call element output functions that need not be immutable. Copying the two values into this row at write time keeps the column's expression trivially immutable instead of requiring a custom SQL function, which no migration exists to create.

`SensitiveContentStamp` is `character(64)` and records the sensitive-content configuration this row's text was derived under: a digest of the scanners that ran, their corpus and profile revisions, the categories switched on, the rules suppressed, and the analyzed ceiling, which is the one input that belongs to no scanner and decides how much of a message was inspected at all. It stands for everything below the message as well as the row it sits on — the chunks cut from this text and the vectors built from those chunks descend from it, so one value answers for the whole derived tree and no write has to stamp a row whose content did not otherwise change. Null is the ordinary value: it means the text was derived while no scanner was on, and it is what every row holds on a deployment that scans nothing. [Derived data](../features/sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped) states what moves the value and what re-derives a row that no longer matches; `backfill_positions` carries a column of the same name for the same reason, so a walk resumes only within the configuration it was walking under.

It carries no index, deliberately. Both readers ask which rows are *not* stamped with the current configuration, and a B-tree operator class holds no inequality operator, so an index over the column could serve neither: the staleness count and the rebuilding walk both scan. That is a figure read once per start and a walk that reads whole rows anyway, against an index every synchronized and every re-derived message would otherwise maintain.

The copies are bounded more tightly than the columns they come from: 2000 characters of subject and 64 participant addresses in total. A `tsvector` cannot exceed one megabyte, and the whole document — subject, addresses, and up to `MaxExtractedTextCharacters` of body — shares that budget. Exceeding it would not degrade search; it would make the row unwritable, because the generated column is computed on every insert.

## Message chunks

`email_chunks` holds the passages a message's extracted text was cut into, many rows per stored email. Each carries its `Ordinal` in reading order, the `StartOffset` the passage begins at in the extracted text, the `Text` itself, a `ContentHash` of `character(64)`, the `RuleSetVersion` it was cut to, whether it `IsDerivedFromLossyHtml`, and when it was `DerivedAt`. [Message chunks](../features/message-chunks.md) describes the boundary rules and what the hash covers.

The key is a surrogate UUIDv7 rather than the email and the ordinal together, because a vector row will hang on one chunk and a composite key would put a re-cut message's ordinals into every table that references it. The pair is `ix_email_chunks_email_ordinal` instead, unique, which is what a reader of one message's passages orders by and what stops a re-cut from writing an ordinal twice.

`Text` is the one column here that no bound applies to. Its length is decided by the chunking rules rather than by a sender, and extraction has already bounded the text all of one message's chunks are cut from, so a column bound would add nothing except a write failure the first time those rules are tuned upwards. `ContentHash` is fixed-length text rather than `bytea`, unlike the raw MIME digest: this one is compared and read — re-chunking decides what to write by comparing digests — while the MIME digest only ever round-trips between one writer and one reader.

Only a message that yielded text has rows here, and deleting a message cascades to them.

`stored_emails.ChunkedTextTruncatedFromCharacterCount` is where the cut records what it left out: the length the extracted text had when the per-message ceiling stopped the cut short of its end, and null when no ceiling reached that message. It lives on the message rather than on a chunk because it is a fact about the message, and it is stored rather than inferred because a message cut whole and one cut to a ceiling are indistinguishable from their passages alone. [Message chunks](../features/message-chunks.md#the-per-message-ceiling) records what the ceiling is for.

## Embedding profiles

`embedding_profiles` holds one row per vector space this deployment has embedded into. The row is written by the activation that takes a declared model up and starts spending against it, and its columns are what a stored vector's attribution points at.

A profile is **the geometry of a vector space and nothing else**: `Provider`, `ModelIdentifier`, `ModelVersion` where the provider exposes one, `Dimension`, `DistanceMetric`, and the three columns that make up the input preparation — `InputCharacterLimit`, `PassageInstruction`, and `NormalizesVector`. Those are exactly the properties that decide whether two vectors can be compared. [ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md) is where each of them was argued.

Three things about that list are decisions rather than defaults.

**The chunk boundary rules are not part of it.** They belong to the chunk's own content hash, so a vector is attributable to both halves of what produced it — the chunk row says what text was embedded and under which rules, this row says what embedded it. Folding the rules in here would price a boundary change, which reaches no network, as a full paid re-embed of the mailbox. [Message chunks](../features/message-chunks.md#what-the-hash-covers) describes the half this table does not carry.

**`Provider` names the vendor whose model defines the space, not the endpoint it is reached through.** That is what lets one profile be served by a chain of endpoints offering the same model, where the endpoint can fail over without the vector space changing underneath vectors already stored. It is a vendor-supplied name stored verbatim, like `ModelIdentifier`, because neither set is MailFathom's to close.

**`Dimension` is the width the stored vectors actually have**, never the model's nominal one. Where a model is narrowed to what the database can index, the narrowed number is what belongs here; a profile claiming the nominal width would be describing vectors that do not exist.

`IdentityFingerprint` is a SHA-256 digest over every column above, written as sixty-four lowercase hexadecimal characters, and `ix_embedding_profiles_identity_fingerprint` is unique over it. That index is what makes activation idempotent: re-declaring a geometry that is already registered resolves to the row that exists rather than inserting a second one whose vectors would be produced from scratch for nothing, so returning to a previous model is a switch rather than a duplicate. Every field of the digest is length-prefixed and every number is big-endian, and an absent optional value is written as a presence marker rather than skipped, so the encoding is one-to-one.

What stays mutable is the lifecycle: `LifecycleState` — building, active, or superseded — with `RegisteredAt`, and `ActivatedAt` and `SupersededAt`, each null until its event has happened. **There is no generation counter.** The profile *is* the generation, so two generations coexisting while a new one is built are two rows, and no read path has a second field it must remember to consult.

`ix_embedding_profiles_lifecycle_state` is unique over `LifecycleState` and partial to the two states that admit one row each, which is how one index expresses both halves of that: at most one generation being built, and at most one being read. Superseded rows are outside its predicate, because a deployment accumulates one for every model it has ever used. It is enforced by the database rather than by the code that writes, for the same reason the vector's width is: two rows claiming to serve would leave retrieval reading whichever one a query returned, with half the vectors in the table unreachable and nothing about the answers saying so. The one consequence for a writer is that the switch has to supersede the old row before it promotes the new one, which is why those two statements are issued in order rather than staged together. [Changing the embedding model](../operations/embedding-profiles.md) is the operator's view of that transition.

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

## What a budget period has spent

`embedding_spend_periods` holds one row per budget period, keyed by `PeriodStartsAt` — the instant that period began, which every process derives from the configured period length and the Unix epoch rather than reading from anywhere. `ConsumedInputCharacterCount` is a `bigint` because a period of a mailbox's initial embedding passes a billion characters without difficulty. Nothing allocates a period: the first spend inside one inserts its row and every later spend adds to it.

The table exists rather than the count being derived from the stored vectors, which would need no table at all. A superseded generation has its vectors removed in bounded batches, so a count taken over them would erase the record of a spend that genuinely happened — and the period in which a model change is paid for is exactly the period an operator is watching. It is durable rather than held in memory for the reason the ceiling exists: a process crashing and restarting in a loop would otherwise begin every period again from zero and spend the whole ceiling on each attempt.

The one write is an increment issued as an upsert, in the same transaction as the vectors it paid for. That makes the charge and the vectors one durable fact, and it means two workers spending inside one period add to each other rather than each overwriting a total that was already stale when it was read — which is why the row carries no concurrency token. Nothing hangs off it and nothing cascades into it: a character count and an instant name no message, passage, or vector, so the record of a cost outlives every vector that cost paid for.

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

## What classification concluded about a message

`email_spam_classifications` is one-to-one with `stored_emails` and holds what spam classification concluded: the
`Verdict`, the stage that `DecidedBy`, the `Score` and the `Threshold` it was judged against, the `CorpusRevision` the
deciding stage ran under, the `Profile` it was reached under, and when it was `EvaluatedAt`. [Spam
classification](../features/spam-classification.md#what-a-classification-records) describes what produces each value.

`Profile` is a twelve-character digest of the settings the verdict rests on — whether a scanner was consulted and the
threshold its score was judged by — and it is nullable because a row written before the column existed names terms
nothing can compare. That is what it is for: a run over a whole mailbox reads it to decide whether a message has
already been decided under the settings now in force, and a row carrying none is scored again rather than skipped.

Keyed by the email rather than carrying an identity of its own, which is what makes one classification per message a
property of the schema instead of a rule somebody has to remember: classifying the same message twice reaches the same
row, so two runs asking together resolve to one record rather than to a history nobody asked for. The row carries `xmin`
as its concurrency token, because those two runs do exist — an arrival classifies a message while an operator's
reclassification replaces it — and the conflict is retried from a fresh read rather than resolved by whichever writer
was last.

The score and the threshold are two columns that are present or absent together: the same number is spam under one
configuration and ordinary mail under another, so a score whose threshold is missing cannot be read at all. Nothing in
the schema enforces the pairing; the domain value that builds them refuses to carry one without the other, and a row
holding half of one is read back as carrying no assessment.

It is a table rather than columns on `stored_emails` for the reason the repair request is: the rows are sparse — a
deployment with classification off has none — and a second table hangs off them.

`email_spam_classification_signals` is that table, one row per fact the verdict rests on: its `Kind`, its `Name`, what
was `Observation`-ed, and the `Source` and `Origin` it came from, numbered by `Ordinal`. A row per signal rather than one
opaque column, because the whole point of the record is that the facts stay separable — an operator diagnosing a wrong
verdict asks which authentication method failed and what the provider header said, and a serialized blob answers neither
without being parsed by hand. The ordinal is unique per classification and carries meaning: the deterministic stage's
facts are numbered first, so a record truncated at its bound kept the ones the verdict rests on rather than an arbitrary
subset.

Both cascades point one way. A classification is deleted with the email it describes, and its signals with the
classification, so whatever erasure and retention already reach a message reach everything derived from it — there is no
pass of its own to remember.

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

`AuditTrailEnabled` is the one column on this row that is about something other than performing the change: it is the
account's audit-trail setting as it stood when the intent was written down, and it decides whether the ending of this
mutation is kept in the table below. It is stored for the reason `LocalDisposition` is — a mutation ends in a later run,
and reading the setting there would apply whatever an operator had changed it to in the meantime, producing a history
whose gaps look like changes nobody made.

## The audit trail of finished mutations

`mailbox_mutation_audit_entries` holds one row per change MailFathom **finished** making to a remote mailbox, written
once when the mutation reaches `Completed` or `Abandoned`. It is a separate table from `mailbox_mutations` because the
two answer different questions with different lifetimes: that one is operational state a retry reads, and its useful
life ends when the change does; this one is written once, read by nothing the mechanism depends on, and its whole value
is that it is still there months later when somebody asks what happened to a message. One table for both would decide
two retention policies with one answer — operational state that is never pruned grows without bound, and history pruned
on completion answers nothing.

**Every identity on the row is a value rather than an association, and no foreign key leaves it.** That is the design
rather than an omission. If the trail inherited the deletion path of the email it describes, then erasing that message
would erase the record that MailFathom deleted it — which removes exactly the entry an audit of deletions exists to
hold. So `StoredEmailId`, `MailboxAccountId`, and both folder paths are stored as values, and the entry outlives every
one of them.

| Column | What it records |
|---|---|
| `MutationRecordId`, unique | Which mutation this was, while that record still exists; the uniqueness is what makes a repeated append leave one entry |
| `MailboxAccountId`, `StoredEmailId` | Whose mailbox and which local email, as values that survive both being removed |
| `Mutation` | The change, by the name a log line and a metric already use |
| `SourceFolderPath`, `SourceHierarchyDelimiter`, `SourceUidValidity`, `SourceUid` | The occurrence the IMAP command was aimed at, with the folder written as its remote path rather than as a key into a binding a later rebinding replaces |
| `DestinationFolderPath`, `DestinationHierarchyDelimiter` | Where a relocation or a copy was aimed, and null for every other mutation |
| `PlacementUidValidity`, `PlacementUid` | Where the server said it put the message, where it supplied `COPYUID` |
| `DesiredSeenState` | Which way a `\Seen` change was asked for, and null for every other mutation |
| `RequesterOrigin`, `RequesterIdentity` | Who asked — a rule together with the revision that matched, one invocation, or the profile a spam verdict was decided under |
| `RequestedAt`, `CompletedAt` | When it was asked for and when it ended |
| `Outcome`, `FailureCode` | `Performed`, or `Abandoned` with the five-digit code it was given up on for |

Nothing amends a row. An entry states an ending that has already happened, so the table only ever grows and shrinks and
carries no concurrency token; the two writes it takes are the append and the erasure that retention or a data-subject
request calls for.

No mail content is here either, and the omission is the same one the mutation record makes for the same reason: no
subject, no address, no body fragment, no filename. What makes this table's version of that promise worth stating
separately is that this one outlives the mail, so an entry that carried content would be content kept past the erasure
of the message it came from.

## The record of what a question read

`mail_answering_audit_entries` holds one row per account per `ask_mail` run, written once when the run has ended and its
answer has already been produced. It is the same artifact as the trail above for a different act — that one answers "why
is this message in this folder", this one answers "which of my messages did that answer come from" — and it is kept for
the same reason: an answer produced by a model is not reproducible, so the only way to explain one later is to have
recorded what produced it.

| Column | What it records |
|---|---|
| `RunId`, `MailboxAccountId`, unique together | Which question this was and whose mailbox it reached; the uniqueness is what makes a repeated append leave one entry, and the run identifier is what joins the entries a multi-account question left |
| `ChatEndpointAlias`, `InstructionsVersion` | The profile and the policy the answer was produced under, as MailFathom's own names for both |
| `StartedAt`, `CompletedAt` | When the run began and when it ended |
| `Outcome` | How it ended: answered, or one of the endings that published no answer |
| `Degradation` | Which ways it read less of the mailbox than an undegraded run of the same question would, as a set of names |

`mail_answering_audited_emails` holds one row per message an entry names, keyed by the entry and the message together —
one run names one message once, however many of its lookups found it. Beside that pair it carries `Position`, the order
the run first reached the message in, and `WasCited`, whether the published answer named it as a source. The difference
between the two is the point: retrieval is what the run *read*, a citation is what the response *published*, and a
response bounded to fewer citations than the run retrieved messages is exactly where the two diverge.

**This is the one place a record follows the mail it names rather than outliving it.** The row cascades from
`stored_emails`, so erasing a message erases it from every run that read it, and it cascades from the entry, so
retention erasing an entry takes its messages with it. That is the deliberate opposite of the mutation trail above,
where the act recorded may have *been* the deletion; nothing of the sort applies to reading a message, so a message
nobody may hold any more is not one this record goes on naming. The account stays a value rather than an association for
the reason it does there: a deployment that stopped serving an account still answered questions from it.

No mail content is here, and the omission is stronger than elsewhere on this page: no extract, no subject, no address,
and no query. A record that stored the retrieved passages would be a second copy of the mailbox with its own retention,
access, export, and erasure obligations — for the sake of a debugging convenience. What the identifier buys instead is
that the message can be fetched and read whole through the reads that already serve it.

## The whole-mailbox rule run an account has outstanding

`mail_rule_evaluation_runs` holds the one re-evaluation of an account's whole mailbox that somebody has asked for, and how far the account's synchronization runs have carried it. It is keyed by the account, which is what makes "one outstanding run per account" a property of the schema rather than a check somebody has to remember: two requests arriving together collide on the key, and the loser is recognized as the second caller learning the first got there instead of as a failure.

| Column | What it records |
|---|---|
| `mailbox_account_id` | The account whose mail the run walks, and the primary key |
| `requested_at` | When the run was asked for |
| `revision` | The rule set the run is bound to, null until the first pass picks it up. Bound when the run starts rather than when it is requested, because the set may reload between the two and what matters is the one in force when the first message is actually evaluated |
| `position` | The identity of the last message a batch committed, null while the run has committed none. Committed with the evaluations it accounts for, which is what makes the run resumable rather than merely restartable |
| `evaluated_email_count`, `matched_email_count`, `skipped_email_count` | What the run has done so far, across every account run that has carried it |
| `ended_at`, `ending` | When and how the run stopped being outstanding. `Completed` is the end of the account's mail; `Superseded` is the rule set having changed while the run was outstanding, which ends it rather than letting one walk apply two rule sets to one mailbox. The ending is text for the reason every other outcome here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
| `ConcurrencyVersion` | The `xmin` token again, because a pass committing a position and a request arriving can both reach this row |

The row survives the run it describes, holding the last ending until a new request replaces it, and there is no history behind it: one account has one row. Nothing cascades into it and it carries no foreign key onto `mailbox_accounts`, for the reason `mailbox_refresh_tokens` carries none — a run may be asked for before any folder of the account has been bound.

Nothing in it is personal data. An account alias, a derived rule set identity, a message identifier, three counts, and three instants are MailFathom's own names for things, which is what lets a run be explained without any of the mail it walked being copied.

## The whole-mailbox classification run an account has outstanding

`spam_classification_runs` holds the one classification of an account's whole mailbox that somebody has asked for, and how far the account's synchronization runs have carried it. It is keyed by the account for the reason the rule run above is, and answers the same way when two requests arrive together.

| Column | What it records |
|---|---|
| `mailbox_account_id` | The account whose mail the run walks, and the primary key |
| `requested_at` | When the run was asked for |
| `folder_aliases`, `posture`, `rescores` | The terms the operator asked for: which folders the walk covers, whether it writes down what its verdicts ask of the mailbox or only works it out, and whether mail already decided under its profile is scored again. Stored rather than read again per pass, because a walk that spans hours has to mean the same thing at its end as at its start |
| `profile` | The classification settings the run is bound to, null until the first pass picks it up. Bound when the run starts rather than when it is requested, for the reason the rule run's revision is; a profile that moves under an outstanding run ends it rather than being applied to the half of the mailbox that is left |
| `position` | The identity of the last occurrence a batch committed, null while the run has committed none. Committed with the counts it accounts for, which is what makes the run resumable rather than merely restartable |
| `classified_email_count`, `spam_email_count`, `undetermined_email_count`, `skipped_email_count`, `unclassifiable_email_count`, `acted_email_count` | What the run has found and done so far, across every account run that has carried it. `acted` is one count for both postures, because it means the same thing under each: this is the mail the switches reach |
| `ended_at`, `ending` | When and how the run stopped being outstanding — `Completed`, `Superseded` by a moved profile, or `Disabled` by classification being switched off under it. Text for the reason every other outcome here is |
| `ConcurrencyVersion` | The `xmin` token again, because a pass committing a position and a request arriving can both reach this row |

The row survives the run it describes and there is no history behind it, exactly as the rule run has none, and it carries no foreign key onto `mailbox_accounts` for the same reason. Nothing in it is personal data: an account alias, folder aliases, a derived settings identity, an occurrence identifier, six counts, and two instants are MailFathom's own names for things. What each of those counts is about per message is the classification records themselves, which is why no per-run history table exists.

## What each rule concluded, and what the conclusion asked for

`mail_rule_executions` holds one row per rule a pass reached per message it evaluated, and `mail_rule_executed_actions` holds the changes each of those rules declared and what became of each. The unit is the pair of a rule and a message because those are the two ways the record is arrived at — "what is this rule doing" and "why is this message here" — and a rule the pass never reached leaves no row at all, which is what keeps "did not match" and "was never asked" apart.

| Column | What it records |
|---|---|
| `Id` | What addresses the execution, and the primary key. A version-7 identifier, so the tie-breaker that orders two executions recorded in one instant rises with the recording instant and one batch's inserts land together in the index rather than scattering across it |
| `MailboxAccountId`, `StoredEmailId` | Whose mail was evaluated, and which message. The message is a foreign key onto `stored_emails` that cascades, which is the whole erasure story here: what a rule concluded about a message goes when the message does, without a rule anybody has to remember |
| `RuleName`, `Revision` | The rule, and the rule set it ran under. Together they are what the condition is retrievable from — the revision identifies the configuration the expression was read from, so the reasoning is reconstructible without the expression being copied here |
| `Trigger` | Which of the pass's two walks reached the message, `Arrival` or `RequestedRun`. Text for the reason every other outcome here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
| `Outcome`, `ConditionFailure` | What the condition concluded, and why it concluded nothing. The failure is present exactly when the outcome is `Failed`, which is what makes an expression that could not be evaluated distinguishable from one that evaluated to false |
| `ReadFacts` | The names of the facts the condition read, as `text[]`. **Names and never values** — the array holds `senderDomain`, never a domain — and read rather than referenced, so a fact a short-circuited clause named does not appear |
| `EvaluatedAt`, `Duration` | When the message was evaluated, and how long the condition took to answer including resolving the facts it read. The duration is what the evaluation timeout is spent against, so a rule creeping toward its bound is visible before it starts being recorded as timed out |

| Column of `mail_rule_executed_actions` | What it records |
|---|---|
| `MailRuleExecutionId`, `Position` | The execution the change belongs to and where the change sits in the order its own rule declares them, together the primary key. The position is the rule's own rather than the plan's, because a plan reorders across rules and the position is what names which of a rule's declared changes this was |
| `Mutation`, `Destination` | The change asked for, and the folder it named — the alias a requested change resolved to, and the text the rule wrote for one that never got that far, which is why the column is not named for an alias. Both are MailFathom's own configured names, which is what lets what a rule did to a mailbox be recorded without the record describing the mailbox |
| `Outcome`, `FailureReason` | `Requested` where a mutation record was opened, `Refused` with a classification where one could not be, and `Withheld` where another rule had already settled the message's fate. The three are kept apart because a change that was refused and a change nobody asked for read identically without it |
| `MutationRecordId` | The record carrying the request, present exactly for a `Requested` change. **Deliberately not a foreign key**: it is a pointer into the mutation's own trail, which has its own retention, and a constraint would tie this record's lifetime to one it does not own |

The row is never amended. An execution states a reading that has already happened, so the record only grows and shrinks — by the retention window `MailRules:HistoryRetention` declares, which the account's synchronization run erases against in bounded passes, and by the cascade from the message it names. That inheritance is the point rather than a convenience: the history is derived from mail content and carries the erasure obligations of the mail it describes whatever the window says.

Nothing in either table is personal data. Rule names, folder aliases, mutation names, fact names, a derived revision, two identifiers, an instant, and a duration are the whole of it, and none is derived from what a message said.

## Durable background work

`jobs` holds work that is enqueued now and done later: what it is, what it points at, who is holding it, and until when. Nothing in the running system enqueues into it yet, and no build registers a handler for a job type, so an instance runs no pass at all and says so once at startup — this is the record every consumer of durable background work is written against, and [ADR 0009](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0009-durable-job-store-and-execution-identity.md) is the decision it implements. What paces the worker over it is the [`Jobs`](../operations/configuration-reference.md#jobs) configuration section.

| Column | What it records |
|---|---|
| `Id` | What addresses the job, and the primary key. A version-7 identifier, so one batch's inserts land together in the index rather than scattering across it |
| `JobType` | The kind of work, held as the closed enumeration's own name. The name is the identity — the word in a log line, the name of a span, the dimension a counter is broken down by — and it names exactly one payload contract, which is what lets the document beside it be read back as the shape it was written as. A name the running build does not declare is left alone rather than failed, because a type an older replica has never heard of is a fact about the deployment and not about the work |
| `IdempotencyKey` | The identity of one execution, composed by whoever knows what the work is and opaque here. It is unique with the type across the whole table, which is the idempotency guarantee itself rather than a support for one |
| `Payload` | The references the work is described by, as one `jsonb` document. Nothing queries into it — the key, the type, the account, and the available instant are all columns — so it is a document rather than a schema, and it is bounded at the enqueue boundary so a payload that copied content into it is refused instead of stored |
| `MailboxAccountId` | The account the work belongs to, null when it belongs to none. A foreign key onto `mailbox_accounts` that cascades, so removing an account takes its queued work with it |
| `State` | `Pending` while the job is claimable, `Claimed` while an attempt holds it, `Succeeded` once it is done, `DeadLettered` once nothing will attempt it again, and `Dropped` once an operator has decided it never will. Text for the reason every other bounded value here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
| `AvailableAt`, `EnqueuedAt`, `StateChangedAt` | When the job becomes claimable, when it was written, and when its state last moved. The first is a column rather than a schedule elsewhere, because it is what the claim orders and selects on, and it is where a retry's backoff is expressed |
| `AttemptCount` | How many attempts have been handed out. Counted by the claim rather than by whatever runs the work, because a process that dies mid-execution never reaches a line that would have counted it and a crash loop would otherwise be invisible. A release gives one back, because a shutdown is not something the work did |
| `LastFailureClassification`, `LastFailureReason` | What the last failed attempt was classified as — `Transient` or `Permanent` — and the operator-safe name of what failed, both null until one has. The reason is a type name and a stable error code and never an exception message: a handler works on mail, and a library's message may quote a subject, an address, or a header into a column that outlives the run |
| `LeaseOwner`, `LeaseExpiresAt` | The attempt holding the job and the instant after which it is claimable again, both null while nothing holds it |

Enqueuing asks the table one question before it writes to it: whether this job type already has as many rows `Pending` as
[`Jobs:MaxQueueDepthPerType`](../operations/configuration-reference.md#jobs) allows. That runs on every enqueue rather
than occasionally, which makes it the second query this table runs at any volume — and it is bounded rather than
counted, because the read stops at the depth it is comparing against instead of totalling the backlog. A queue at its
depth refuses the enqueue and says so, so the caller slows down, asks again later, or stops producing; a request whose
work is already queued is still answered with that job rather than turned away. Two enqueuers meeting the bound together
can both pass it and the depth overshoots by as many as raced, because this is backpressure rather than an invariant.

A claim is one statement: it selects the oldest due row of a type the asking process runs, under `FOR UPDATE SKIP LOCKED`, and stamps it with a lease owner and an expiry in the same statement. Two things follow. Two workers claiming at the same moment take different jobs instead of waiting on each other, which is what makes the queue drainable by more than one process. And a job is due either because it is pending and its available instant has passed **or** because it is claimed under a lease that has run out — so work in flight when a process died is picked up without an operator doing anything, and nothing has to be told the process is gone.

Renewal, completion, a scheduled retry, a dead letter, and release are each a single update conditional on the lease owner still matching. An attempt that lost its lease, finished late, and tried to write its result finds the row owned by the attempt that replaced it and writes nothing. That compare-and-set is why the table carries no `xmin` token: the fact that decides whether a writer still owns the work is the lease, and a row version beside it would report a conflict for a renewal that changed nothing an attempt cares about.

Which of them a worker writes is decided by how the attempt ended, and they are deliberately not one. A handler that finished completes the job. A handler stopped because the host is shutting down releases it, so the job is claimable again at once, the attempt the claim counted is given back, and the deployment costs it nothing.

Anything else is a failure, and the failure is classified before the attempt budget is consulted. A failure that could clear on its own — a dependency whose resilience pipeline declined the work, a provider that said its answer was worth repeating, a connection that dropped, an execution that ran out of time — schedules another attempt: the row goes back to `Pending` with an available instant a jittered exponential delay ahead of now, so the backoff is the column rather than a timer anywhere. A failure that could not — a rejected credential, a refused request, anything whose meaning is unrecognized — ends the job on its first attempt, because repeating it could only reach the same answer, and so does a transient failure that has used up `Jobs:MaxAttempts`. Either way the row becomes a `DeadLettered` one that keeps its attempt count, its key, and the classification and reason it ended on. A dead letter is inert: the claim's predicate names the two claimable states, so nothing takes it again, one job that cannot succeed consumes no further attempts, and the jobs behind it are unaffected.

A dead letter is terminal but not permanent, because the two things that could resolve one are both an operator's. Returning it to the queue is a conditional update from `DeadLettered` back to `Pending` that hands the attempts back and makes the row due now — the same row, so the work runs under the identity it was already enqueued with rather than as a second job, and a retry of a job something else already dealt with changes nothing and says so. Writing it off is a conditional update to `Dropped`, which is a fifth state rather than a deletion: the row stays, keeps the failure that ended it, and goes on holding the key that refuses the same execution. Both are reached through [the administrative endpoint](../operations/admin-endpoint.md#reading-the-background-work-that-stopped-and-deciding-what-becomes-of-it) and neither reads the payload.

A terminal row keeps its key. That is what stops a repeating trigger enqueuing work that has already been done, and it is why a finished job stays in this table rather than moving to another one. It also makes pruning a correctness setting rather than housekeeping: erasing terminal rows is what ends the deduplication, so whichever change adds pruning inherits a retention floor of the longest window in which one trigger can legitimately fire again.

What the store delivers is at-least-once execution and nothing stronger. Uniqueness stops the same work being *enqueued* twice; only a handler can stop a re-run after a crash from having a second effect.

## Indexes

| Index | Columns | Purpose |
|---|---|---|
| `ix_stored_emails_folder_uidvalidity_uid` | `(mail_folder_id, uid_validity, uid)`, unique | Remote occurrence identity, which is what makes synchronization idempotent |
| `ix_stored_emails_account_timeline` | `(mailbox_account_id, received_at DESC NULLS LAST, id DESC)` | The account-wide timeline |
| `ix_stored_emails_folder_timeline` | `(mail_folder_id, received_at DESC NULLS LAST, id DESC)` | The per-folder timeline |
| `ix_stored_emails_awaiting_content` | `(mail_folder_id, uid_validity, uid)` over the rows whose `content_availability` is `AwaitingStorageHeadroom` | The queue of occurrences stored without their payload, which every folder run reads once. The filter is what keeps the index proportionate to that queue rather than to the mailbox: on a deployment that has never reached its storage ceiling the index is empty, and the read costs nothing instead of walking a folder's whole occurrence index to discover that no row qualifies |
| `ix_stored_emails_account_identity` | `(mailbox_account_id, id)` | The order a whole-mailbox rule run walks an account's mail in. The identity rather than the timeline, because a walk that has to resume needs a total order no later write disturbs and a position that is one column rather than a nullable timestamp paired with a tie-breaker |
| `ix_stored_emails_awaiting_rule_evaluation` | `(mailbox_account_id, id)` over the rows whose `rules_evaluated_at` is null | The queue of mail no rule pass has evaluated, read once per account run. The filter is the point: in steady state almost every row of an account has been evaluated, so without it the read would walk the account's whole index to find the handful that qualify, on every run of every account |
| `ix_stored_emails_sender` | `(sender_normalized_address)` | Filtering by who sent a message |
| `ix_stored_emails_to_addresses` | `(to_addresses)`, GIN | Containment tests over the `To` recipients |
| `ix_stored_emails_cc_addresses` | `(cc_addresses)`, GIN | Containment tests over the `Cc` recipients |
| `ix_stored_emails_reply_to_addresses` | `(reply_to_addresses)`, GIN | Containment tests over the `Reply-To` addresses |
| `ix_email_search_documents_search_vector` | `(search_vector)`, GIN | Lexical search over subject, participants, and body text |
| `ix_email_chunks_email_ordinal` | `(StoredEmailId, Ordinal)`, unique | One message's passages in reading order, and the constraint a re-cut cannot write an ordinal twice past |
| `ix_embedding_profiles_identity_fingerprint` | `(IdentityFingerprint)`, unique | One row per vector space, which is what makes activation idempotent |
| `ix_embedding_profiles_lifecycle_state` | `(LifecycleState)`, unique, where the state is building or active | At most one generation being built and at most one being read |
| `ix_email_embeddings_profile` | `(EmbeddingProfileId, Dimension)` | Reading a whole generation, which is how a superseded one is removed |
| `ix_mailbox_mutations_identity` | `(MailFolderId, UidValidity, Uid, RequesterOrigin, RequesterIdentity, Mutation)`, unique | A mutation's idempotency identity, which is what makes the same request twice perform one change |
| `ix_mailbox_mutations_outstanding` | `(MailboxAccountId, RecordedAt)` where the stage is not `Completed` | The changes an operator asks about: those in flight and those given up on |
| `ix_mailbox_mutations_placement` | `(MailboxAccountId, DestinationFolderPath, PlacementUidValidity, PlacementUid)` where `PlacementObservedAt` is null | The question the forward pass asks of every batch it discovers: is one of these UIDs where a relocation or a copy put an email |
| `ix_mailbox_mutation_audit_entries_mutation` | `(MutationRecordId)`, unique | One audit entry per mutation ending, whatever a repeated append attempts |
| `ix_mail_rule_executions_account_evaluated` | `(MailboxAccountId, EvaluatedAt, Id)` | An account's rule history newest first, and the retention pass that erases what has outlived its window. The identifier is in the key because two executions of one batch share an instant and a keyset page needs a total order to continue from |
| `ix_mail_rule_executions_account_rule_evaluated` | `(MailboxAccountId, RuleName, EvaluatedAt, Id)` | What one rule has been concluding, which is the question a rule that never seems to fire is investigated with |
| `ix_mail_rule_executions_email_evaluated` | `(StoredEmailId, EvaluatedAt, Id)` | Why one message is where it is, and the rows the cascade removes when that message is erased |
| `ix_mailbox_mutation_audit_entries_account_completed` | `(MailboxAccountId, CompletedAt, Id)` | The two ways the trail is worked: a keyset-paginated page of an account's history, and the retention pass that erases what ended before a cutoff |
| `ix_mail_answering_audit_entries_run_account` | `(RunId, MailboxAccountId)`, unique | One entry per run per account, whatever a repeated append attempts |
| `ix_mail_answering_audit_entries_account_completed` | `(MailboxAccountId, CompletedAt, Id)` | The same two readers the trail above has: a keyset-paginated page of an account's runs, and the retention pass beside it |
| `IX_mail_answering_audited_emails_StoredEmailId` | `(StoredEmailId)` | The foreign key back to the message, which is what makes erasing one reach the runs that read it without scanning the table |
| `ix_email_spam_classification_signals_classification_ordinal` | `(StoredEmailId, Ordinal)`, unique | One classification's signals in the order the stages produced them, and the constraint a replaced record cannot write an ordinal twice past |
| `ix_jobs_identity` | `(JobType, IdempotencyKey)`, unique | A job's idempotency identity, which is what makes the same execution enqueued twice one job. It spans terminal rows deliberately: a row that succeeded is what stops the same trigger asking again |
| `ix_jobs_claimable` | `(JobType, AvailableAt)` where the state is `Pending` or `Claimed` | Both of the queries this table runs at any volume: the claim, and the queue-depth check every enqueue makes. The filter keeps the index the size of the backlog rather than of the queue's whole history, and the claim repeats that same membership in its own predicate so PostgreSQL can prove the index applies to it rather than having to derive it through a disjunction. It names the two claimable states rather than excluding the terminal ones, so a job that reaches a terminal state leaves the index whichever one it reaches. The depth check reads the same leading column and rechecks `Pending` against the heap, because the index carries both claimable states rather than that one; what keeps that cheap is the bound on the read rather than the index |
| `ix_jobs_account` | `(MailboxAccountId, EnqueuedAt)` | An account's queued work, which is what erasure and any per-account bound reach a job by |
| `ix_jobs_dead_lettered` | `(StateChangedAt, Id)` where the state is `DeadLettered` | The operator's reading of what has stopped, newest first, keyed on the pair it pages by. The filter keeps the index the size of what is waiting for a person rather than of the table, and a row leaves it the moment the decision about it is taken |

The recipient and search-vector indexes are GIN rather than B-tree because both serve containment tests. A B-tree over an array column serves only equality against a whole array, and over a `tsvector` it serves nothing search asks for; a GIN index is what turns either into an index scan.

The partial indexes over remotely deleted messages that the architecture draft lists are still deliberately absent; they wait for the remote-expunge reconciliation that introduces the state they would filter on. The per-profile HNSW index is absent from the migrations for a different reason, and permanently.

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

`mailbox_mutation_audit_entries` is the one table on this page that deliberately does **not** inherit those obligations. It is derived personal data by the same reading — where a person's mail has been, when, and at whose instruction — and the reason it outlives the mail is that an audit of deletions whose entries are erased by the deletions they record holds nothing. What replaces the cascade is a bound of its own: the trail is off unless an account turns it on, each account states how long its entries are kept, and every account run erases what has outlived that window. A data-subject erasure reaches it separately for the same reason, and [Administrative endpoint](../operations/admin-endpoint.md#reading-what-mailfathom-changed) states that path.

`mail_answering_audit_entries` and `mail_answering_audited_emails` are the other table that needs an operator's decision before either holds anything, and the comparison between the two is the whole design. The entry is derived personal data of a different kind — what a person's mail was read for, and when — and it is bounded the same way: off unless an account turns it on, with a stated retention every account run erases against. Where it differs is the cascade, which it keeps: the messages an entry names hang on `stored_emails` and go with them, because recording that mail was *read* survives that mail's erasure no better than the extract itself would. Retention and the cascade therefore both reach it, and neither replaces the other.

The same holds for `email_chunks`, and one thing about it is deliberate: a chunk records the message it came from and the span inside it, and nothing else. The account, folder, sender, recipients, date, and subject a retrieval will want to cite are reached through that message rather than copied onto the passage, so cutting mail into chunks widens no access, export, or erasure surface — it only adds rows the same cascade erases.

`email_embeddings` is the same again, one step further out. A vector is derived from mail content and inherits the source message's classification, retention, access, export, and erasure obligations whole; nothing about being a list of numbers makes it a lesser copy of the words it stands for, and it is not anonymous because it cannot be read back by eye. It carries no text and no coordinates of its own — the chunk it hangs on answers both — and the cascade from that chunk is what makes erasure structural rather than a rule somebody has to remember. No vector, no chunk text, and no digest reaches a log, a metric, a trace, or an error message.

`embedding_spend_periods` holds no personal data either, and its shape is what makes that true rather than a claim about it: a character count and an instant say how much a deployment spent and when, and neither names a message, a passage, or a vector. That is also why it outlives what it recorded — nothing cascades into it, and erasing the mail a period paid to embed leaves the record that the period was paid for.

`email_spam_classifications` and `email_spam_classification_signals` are derived personal data of the same kind as a
chunk or a vector: what was concluded about somebody's mail, and from which of its headers. They inherit the source
message's classification, retention, access, export, and erasure obligations whole, and the cascade from `stored_emails`
is what makes that structural rather than a pass somebody has to remember. A signal names a header field, an
authentication outcome, or a rule — never the value of a header — and nothing in either table reaches a log, a metric, a
trace, or an error message.

`jobs` is derived personal data by the same reading as a chunk or a classification: a row says that something is to be done about somebody's message, and it points at that message by its occurrence identity. What keeps it a pointer rather than a copy is the payload contract — a document of references with no property a subject, an address, or a body could go in, bounded in size at the enqueue boundary so a payload that grew into a copy is refused instead of stored. The account column and the cascade from `mailbox_accounts` are what erasure reaches queued work by. The message the payload names is deliberately not a foreign key: the identity in the document is the remote occurrence rather than the local row, so there is nothing for a constraint to point at, and reaching the message is a lookup by that identity like every other read of it.

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
- Both guarantees the job store gets from PostgreSQL rather than from its own code: two callers racing to enqueue one execution produce one job, because the unique index refuses the second insert; and two workers claiming at the same moment take different jobs, because the claim selects and stamps under `FOR UPDATE SKIP LOCKED` in one statement. A lease that has run out is reclaimed with a second attempt counted, the attempt it was taken from writes nothing afterwards, a completed job keeps the key that refuses the same execution again, and a row whose type this build does not declare is left where it is. A dead letter is claimed by nothing and keeps the key and the recorded failure that ended it, a scheduled retry holds the job back until the instant it named, and a release hands the attempt back with the job.
- The same read model's vector half ranks the eligible mail by a correlated minimum over each message's own embedded passages, measured by the operator the active profile's metric names. Mail carrying no vector under that profile is absent from the ranking rather than distant, and the ordering is the part only a server can settle: whether the distance operator, the aggregate, and the caller's filters compose into one statement at all is a translation question rather than a compile-time one.
