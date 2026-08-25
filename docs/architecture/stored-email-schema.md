# Stored email schema

<!-- describes: backend/src/Infrastructure/Persistence/**, backend/src/Domain/Emails/**, backend/src/Domain/Delivery/**, backend/src/Application/Emails/Embeddings/** -->

`stored_emails` holds the normalized metadata a mailbox timeline is read from. Its raw MIME lives in a separate one-to-one table, `email_message_contents`, and the text derived from that MIME lives in a third, `email_search_documents`, so nothing that lists or filters mail ever loads a `bytea` value, a body's worth of text, or a search vector — let alone tracks one in the change tracker.

This page describes the table as the EF Core model declares it and as the reviewed baseline migration creates it. PostgreSQL has had its say about that migration, so the types, constraints, and indexes below are the ones a schema dump reports rather than the ones a model was hoped to produce. How the schema reaches a database is at the end of this page.

## Where a raw-MIME row keeps its payload

Four tables hold raw MIME — `email_message_contents` here, and `outgoing_email_contents`, `recurring_send_drafts`, and `mail_draft_contents` further down — and all four carry the same three columns beside whatever else they record, because a payload is in one of two places and the row is what says which.

| Column | Type | What it holds |
| --- | --- | --- |
| `Backend` | `character varying(64)` | `Database` or `ObjectStorage`. Text rather than an integer for the reason `content_availability` is, and `NOT NULL` with a stored default of `Database` — which is what makes every row written before the column existed read as the thing it is, and what keeps an ordinary database-backed insert from stating anything |
| `ObjectLocator` | `character varying(1024)` | The whole key an object was written under, and null for a database-backed row. Stored verbatim and never recomputed: the key is minted before the row that points at it exists, which is what lets the object be written outside the transaction that writes the row |
| the payload column | `bytea`, nullable | `RawMime`, or `DraftMime` on the recurring-send draft. It holds the bytes for a database-backed row and nothing for an object-backed one — never both, because a second copy would be mail nobody agreed to keep |

A `CHECK` constraint per table pairs all three, named `ck_<table>_backend_payload`: a `Database` row carries a payload and no locator, and an `ObjectStorage` row carries a locator and no payload. It is what makes the exclusivity a property of the schema rather than of whichever writer got there — a backfill, a hand-written migration, or a new caller that left a stale payload beside a locator is refused rather than stored as two copies of one message.

Each table also carries `ix_<table>_object_locator`, a unique partial index on `ObjectLocator` filtered to `Backend = 'ObjectStorage'`. Two readers share it. The readiness census asks whether this deployment holds mail in an endpoint it can still reach, and partial is what makes that census cheap: the stored default names the database, so on a deployment that configured no endpoint all four indexes are empty and the census reads four empty indexes instead of sequentially scanning four tables of mail on every scrape. [Reclaiming an object nothing points at](../features/email-content.md#an-object-nothing-points-at-is-reclaimed) asks the other question, of one listed page of keys at a time — which of these does a row still name — and it is the reader the index is keyed on `ObjectLocator` for, because a probe by key against an index over `Backend` would read every object-backed row in the table. Nothing else reads either column: a payload is reached through the row that owns it, never through its backend or its key.

The index is unique because a locator names one row. Every placement mints a fresh key rather than deriving one from the row that will point at it, so two rows sharing a locator would be two messages the reclamation cannot tell apart and one object two deletions would race over — and the constraint is the schema saying that content addressing was refused rather than a property somebody has to keep.

The byte length and the SHA-256 digest stay on the row under both backends, so the integrity check a read performs is one check rather than two. Under the object backend the digest is also what the endpoint was asked to verify the upload against.

**Nothing here is a per-account or per-owner decision.** Which backend a deployment writes to next is one process-wide setting, and this column is the same fact asked about a payload already stored. [ADR 0017](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md) is the decision, and [email content](../features/email-content.md#where-a-payload-is-kept) is what a reader of the behaviour reads.

## What a row records

The columns fall into groups, each answering a different question.

**Occurrence identity.** `mail_folder_id`, `uid_validity`, and `uid` are the stable remote identity of one message in one folder, and `id` is the local UUIDv7 that every other table references. `mailbox_account_id` is a copy of the owning folder's account: the account timeline index leads with it, and an index cannot span a join. Nothing repoints a folder at another account, so the copy is written with the row and never revised.

A row is therefore an occurrence and nothing above one. Two folders holding the same message — because the owner copied it, or because MailFathom did — are two rows, each with its own raw MIME, search document, chunks, and vectors, and no stored identity joins them; [ADR 0008](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md) records that decision and what it costs, and [what a message MailFathom copied becomes locally](../features/imap-synchronization.md#what-a-message-mailfathom-copied-becomes-locally) states it as behavior. The cascade below is what makes that safe rather than merely duplicated: each row erases its own derived data, so removing one copy reaches everything derived from it and touches nothing of the other.

**What the server reported.** `internet_message_id`, `subject`, `sent_at`, `size_octets`, and `content_availability` come from the envelope the IMAP server returned. For a message whose raw MIME was never fetched these are the only fields the row carries, and `content_availability` says which of the two reasons applies. `ExceededSizeLimit` is permanent — the message is above the configured per-message limit and will be on every later run — while `AwaitingStorageHeadroom` is a queue: content storage was at its configured ceiling when the message was discovered, and a later run with room fetches it and fills the rest of the row in. The column is text rather than an integer so that reason stays readable in an ad-hoc audit query and survives any reordering of the enum.

**What the stored MIME said.** `received_at`, the sender columns, the recipient arrays, the thread columns, and the attachment summary are read out of the raw MIME that this deployment actually stored. When that read succeeds it also replaces `subject` and `sent_at`, so one row stays consistent with one set of bytes rather than mixing two parsers' answers. `internet_message_id` is the exception: a message that carried no `Message-ID` keeps the identifier the envelope reported instead of losing it.

**The remote flag snapshot.** `remote_flags_observed_at` and the five boolean markers record what the server last said about `\Seen`, `\Answered`, `\Flagged`, `\Draft`, and `\Deleted`. The reconciliation pass writes them one bounded window per run, so a row nobody has reached yet still carries the never-observed value. The timestamp exists because no combination of the booleans can distinguish "the server reports none of these" from "nobody has looked yet", and it doubles as the reconciliation queue: `ix_stored_emails_reconciliation_queue` is `(mail_folder_id, remote_flags_observed_at, uid)` over the rows that are not tombstoned, which is what lets the pass advance without a cursor of its own. It states no null sort order on purpose — a window is read as two queries, one per group, so neither orders a null against a value and both take PostgreSQL's default.

**The keywords beside them.** `RemoteKeywords` is a `text[]` on the same row, holding the flags the protocol leaves to whoever set them — `$Junk`, `$Forwarded`, a label a mail client wrote — rather than the five it names. The reconciliation pass writes it with the booleans, from the same `FLAGS` answer, so an empty array means either that the server reported no keyword or that nobody has looked, and `remote_flags_observed_at` is what tells those apart here too. Flag names are compared without regard to case, so the values are held upper-cased, deduplicated, and ordered: two observations that found the same keywords write the same array whatever order the server listed them in. What one row keeps is bounded at 64 keywords of at most 64 characters each, and a server reporting more has the excess discarded rather than failing the window. The column sits here rather than in a table of its own so that every tombstone, retention, erasure, and export path already carrying this row carries the keywords with it.

These columns are an observation and never an instruction. Reading mail cannot reach any of them, because no read path holds a session able to issue a `STORE` at all. `\Seen`, `\Flagged`, and the keywords are what MailFathom can ask a server to move, and only as a change the mailbox owner authored — that request is written to `mailbox_mutations` and issued against the server, and it writes nothing here. A column changes when the reconciliation pass next reads the folder and finds the flag standing somewhere new, which is the same way it would change had the owner moved the flag in their own mail client. So each of them has exactly one writer whoever moved the flag, and a row read between the command and the next window still reports the last value the server was seen to hold. `\Answered` and `\Draft` are never written under any instruction, and `\Deleted` is written only as a step of removing a message rather than as a flag anything asks for. [ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md) records why the permitted set stops there.

**The tombstone.** `remote_expunge_observed_at` records when reconciliation found the message gone from its remote folder, and is null while the server still holds it. It is a different statement from `is_remotely_deleted`, which is the server reporting the `\Deleted` flag for a message the folder still holds and still serves; conflating the two would hide mail that is merely marked for deletion. An account configured to erase local copies has no tombstone at all, because its row is removed instead — and the three tables that reference this one all cascade, so the raw MIME, the search document, and any outstanding repair request go with it. [IMAP synchronization](../features/imap-synchronization.md#reconciling-against-the-server) describes when each happens.

**The local copy kept without a remote occurrence.** `is_retained_after_authored_delete` is what separates a row the server no longer holds from a row a reader may no longer see, and only a change MailFathom itself performed under `RetainLocalCopy` sets it — a delete, or a relocation into a folder nothing mirrors, which is the same loss of the occurrence and is answered by the same setting. Such a row carries the tombstone timestamp as well, because the server genuinely no longer holds the message and the reconciliation queue has to stop selecting it; the flag is what keeps it inside the mailbox queries the timestamp would otherwise take it out of. So a row is excluded from every mailbox query when `remote_expunge_observed_at` is set **and** this flag is false, while `ix_stored_emails_reconciliation_queue` filters on the timestamp alone. Nothing else sets it: a relocation between mirrored folders carries the row into the destination instead, a disappearance somebody else caused is answered by `RemotelyDeletedEmailDisposition`, which has no value that keeps the mail readable, and carrying the row onto a new occurrence clears the flag along with the timestamp. [What becomes of a message MailFathom deleted](../features/imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself) states which disposition produces which of the three outcomes. One read is stricter than that rule and says so where it is written: resolving the occurrence a caller's flag change is recorded against refuses such a row outright, because the mail is readable while the UID names a message the server expunged, and a change recorded against it could only be attempted and fail.

**What the rules have seen.** `rules_evaluated_at` records when a rule pass last evaluated this message, and is null while none has. It is not only a record: its absence is the queue a pass reads, so writing it is what takes a message out of the mail rules apply to on arrival, and `ix_stored_emails_awaiting_rule_evaluation` is what makes reading that queue proportionate to it rather than to the mailbox. The migration that adds the column stamps every row already stored, which is the same statement made about mail that existed before rules ran at all — an upgrade must not hand a deployment's first rule set an entire mailbox's history as though it had just arrived. Re-evaluating mail that already carries a value is the whole-mailbox run below, and never something an edit sets off; [mail rules](../features/mail-rules.md#when-rules-run) states both halves as behaviour.

**When this deployment first held the message.** `StoredAt` is written once, when the row is inserted, and is never revised. It is deliberately neither `sent_at` nor `received_at`: both of those are facts about the message that a sender or a mail server decided, and can be years old on a mailbox being synchronized for the first time. This is a fact about this deployment, and it is what spam classification's ordering is measured against — how long a message has waited for a verdict before everything derived from it is released anyway, which [junk is kept out of what a deployment derives from mail](../features/spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail) states as behaviour. The migration that adds the column backfills every existing row with `-infinity` rather than with the instant of the upgrade: a message stored before the column existed has by definition waited longer than any wait a deployment can configure, so it is eligible immediately, while stamping it with the upgrade would hold a whole mailbox out of the index for one more wait apiece.

**Concurrency.** `ConcurrencyVersion` maps onto the PostgreSQL `xmin` system column rather than a column of its own, so PostgreSQL maintains the token and no writer has to.

### Sender and recipients

The sender is stored as three columns: the display name and address as the message wrote them, and the upper-cased comparison form that every filter and index matches on. The `From` header supplies it. `Sender` is the fallback and only stands in for a message that named no author at all, because it names whoever submitted a message written on someone else's behalf and therefore answers a different question.

Recipients are PostgreSQL `text[]` columns — `to_addresses`, `cc_addresses`, `reply_to_addresses` — rather than a join table, because every planned query tests containment rather than joining to recipient rows. They hold the comparison form only. A recipient's display name is mail content that no planned query filters or sorts on, and a second copy of it would widen the access, export, and erasure surface for nothing; a reader that needs the names re-derives them from the stored raw MIME, which the [email content](../features/email-content.md) read model parses anyway.

### The sender-authentication verdict

Ten columns record what was established about who actually sent the message:
`SenderAuthenticationOutcome`, `SenderAuthenticationMethod`, `AuthenticatedSenderDomain`, `DkimSignerDomain`,
`SpfMailFromDomain`, `DmarcOutcome`, `AuthorAuthenticationOutcome`, `AuthenticatedAuthorDomain`,
`DisplayedAuthorDomain`, and `SenderAuthenticationSource`. The five enums are
text for the reason `content_availability` is, and the five domains are `character varying(253)` — the length a
resolver accepts, which the domain value already refuses to exceed, so no value ever reaches a column that would reject
it.

`SenderAuthenticationSource` is what makes the rest of the group readable, and it is the one column that is not about
the message: it names who reached the verdict. `ReceivingServer` says it was read back out of the header that server
wrote, which is also what a row carrying nothing established holds; `LocalVerification` says MailFathom verified the
message's own DKIM signatures, which it does only where no trusted header was found. The difference cannot be recovered
from anything else on the row, and it cannot be inferred from the account's configuration either, because that may have
changed since. On a `LocalVerification` row `SpfMailFromDomain` is empty and `DmarcOutcome` is `NotReported` by
construction rather than by outcome: after delivery there is no envelope to authenticate and no published policy is
resolved.

Three of them — `AuthorAuthenticationOutcome`, `AuthenticatedAuthorDomain`, and `DisplayedAuthorDomain` — are about the
author the message displays, which is a different question from the six naming the identity that handed the message
over: a relay, a mailing list, or a delivery provider authenticates as itself while carrying somebody else's `From`. `AuthenticatedAuthorDomain` is present exactly when the author
authenticated, and it is the domain a reader was shown rather than whichever identity established it.

`DisplayedAuthorDomain` is the domain the `From` header wrote, recorded whether or not anything held it, so the two
halves of the comparison exist on the row where they are most needed — the messages whose author was *not* established,
where `AuthenticatedAuthorDomain` is empty by construction. It overlaps that column and does not replace it: one says
what a trusted server stood behind, and this says what the message claimed. It is not derivable from the sender columns
either, because those fall back to `Sender` for a message that named no author while this is `From`'s alone. A message
stored before the column existed holds null in it and nothing fills that in by itself, because neither synchronization
pass re-reads mail that stayed where it was; [`mfctl mailbox
rederive`](../features/imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) is the pass that does, from
the raw MIME the deployment already holds.

They are columns on the row rather than a table hanging off it, unlike [what classification
concluded](#what-classification-concluded-about-a-message). The rows would not be sparse: every message whose MIME was
read carries a verdict, including the not-established one that a deployment whose provider publishes no results sees on
all of its mail. What reads the verdict is the arriving message's own presentation, one row at a time down a timeline,
so a join per row would buy a nullable association nothing is ever without.

Every enum column carries a database default naming what was true of a row written before it existed —
`NotEstablished`, `None`, `NotReported`, `NotEstablished` again for the author, and `ReceivingServer` for the source,
since every row written before this deployment verified anything itself came from the trusted-header reading whatever
it found — so the migration that adds a column fills every stored message in rather than leaving a value nothing
wrote. A domain column takes no default and is simply
absent on a row written before it existed, which reads the same as a message that wrote no such domain at all: the two
are indistinguishable from the row, and re-reading the message is what tells them apart. The whole group is written together on every extraction, so re-reading a
message after its account gained a trusted identifier replaces the verdict rather than leaving one column of the
previous reading behind. The domains are stored in the upper-cased comparison form, which is what a later reader matches
on; [sender authentication](../features/sender-authentication.md) states how the verdict is reached and which header it
is allowed to come from.

### What this deployment made of that author

Three more columns record what this deployment made of that author: `SenderTrustLevel`, `SenderTrustGrantedBy`, and
`SenderTrustPolicyRevision`. The two enums are text for the reason the four above are, each defaulting to the value that
recognizes nobody — `Unknown` and `None` — so the migration that adds them fills every stored message in with what was
true of it. The revision is `character varying(32)` and **nullable on purpose**: its absence is what separates a row no
policy ever judged, including one recorded from an envelope whose payload was never stored, from one a policy judged and
left unknown.

They are columns beside the authentication verdict rather than a group of their own because they are read together and
written together — a re-derivation replaces both, so an answer can never sit beside an identity a different list
judged. `SenderTrustLevel` has two values and nothing more, because the difference between an author nobody named and an
author nothing established is the authentication columns' to state. What the revision buys is that a change to a
trusted-sender list is legible rather than silent: a stored verdict keeps the answer it was given, and the revision says
which list gave it.
[Sender authentication](../features/sender-authentication.md#whether-the-author-is-one-this-deployment-recognizes)
states the rule the verdict follows.

### The machine-authorship reading

Four columns record how much the message's own text read as machine written: `MachineAuthorshipBand`,
`MachineAuthorshipLikelihood`, `MachineAuthorshipSignals`, and `MachineAuthorshipProfileRevision`. They sit on the email
rather than in a table of their own for the reason the group above does — every message extraction reached carries an
answer, including the not-assessed one every message of a deployment that turned the reading off carries, so a join per
row would buy a nullable association nothing is ever without.

`MachineAuthorshipBand` is text like every other enum here and defaults to `NotAssessed`, so the migration that adds the
columns fills every stored message in with what was true of it. `MachineAuthorshipLikelihood` is `double precision`
defaulting to `0`, which is deliberately indistinguishable from a text that was read and carried nothing: the band is
what separates the two, and a second column saying the same thing would be a second place to keep right.
`MachineAuthorshipProfileRevision` is `character varying(32)` and **nullable on purpose**, for the reason the trust
revision is — its absence is what says nothing assessed this row, whether because the reading was off, because the body
yielded no words, or because the row predates the columns.

`MachineAuthorshipSignals` is **the one enum here stored numerically** rather than as text, because it is a flag set
rather than a single value. A set written as text is a formatted list: no query can ask which rows carry one member of
it, and reading one back depends on the separator that wrote it. Its members are explicit powers of two that are never
reordered or reused, which is what makes the numeric form safe.

The whole group is written by extraction from the stored raw MIME and is re-derivable from it, and it is written whole on
every extraction so a re-derivation never leaves a likelihood beside signals a different profile found.
[Machine authorship](../features/machine-authorship.md#what-is-recorded) states what each value means and what the
reading deliberately does not claim.

### Bounds on what a header may contribute

Nothing between the mail server and a row bounds a header's length or how many addresses it names. The MIME reader bounds a message's *structure* — part count and nesting depth — but not the width of a single header, so the persistence mapping applies its own ceilings: 320 octets per address, 998 per message identifier, 256 addresses per recipient array, and 64 thread ancestors.

A value over a ceiling is **dropped, not truncated**, and the row keeps the rest. Both halves of that are deliberate.

Letting the value through would be worse than losing it. The column would reject the write, the retry budget would run out, the folder checkpoint would never advance past the message, and every later run would stop on the same one — one malformed header would halt synchronization of the folder behind it.

Truncating would be worse still. A prefix of a message identifier is an identifier another message may legitimately carry, so a truncated one would assemble a thread out of unrelated conversations, and a truncated address would name a mailbox nobody wrote. The columns are a filter index over what a message said, not a second copy of it; the complete headers stay in the raw MIME the content read model parses.

Where a bound cuts a sequence, it keeps the end that answers the question. Recipients keep header order from the first, because that is the order a reader sees them in. Thread references keep the ancestors nearest to this message, because that is the end of the path a thread view walks first.

### Attachment summary

The row keeps the indexable part of what the MIME reader found and only that: `attachment_count`, `attachment_total_size_octets`, `inline_resource_count`, and the `is_encrypted`, `carries_unverified_signature`, and `contains_unexpanded_tnef_part` markers.

**The per-attachment list of file names, media types, and sizes is deliberately not persisted.** The same reasoning as for recipient display names applies, and one more: a second representation of the attachment list can drift from the raw MIME it was derived from, and re-deriving it costs nothing in a pass a body reader is already making. [Email content](../features/email-content.md#the-descriptions-are-re-derived-never-stored) is the read that makes it. The signature marker is named for presence rather than verification because nothing here verifies anything; a column called "signed" would be read as an authenticity result by every query that later touched it.

## The owner a mailbox belongs to

`settings_accounts` holds one row per owner, and `mailbox_accounts` gains the owner every mailbox belongs to. This is the axis the whole graph above hangs on: a folder cascades from the account, a message from the folder, and everything derived from a message from the message, so an owner on the account row is an owner on all of it. [ADR 0014](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is the decision and says why no table of the mail graph gains an owner column of its own. Both tables of the [contact book](#the-contact-book) do, for two different reasons: it is an assembled record rather than mail an account brought in, so `contacts` hangs on nothing that could carry an owner and names one directly, and `contact_addresses` repeats it despite hanging on `contacts` through `(ContactId, OwnerId)`, because the reads it answers lead with the owner in an index and an index cannot reach through the contact to find one.

| Column of `settings_accounts` | What it records |
|---|---|
| `Id` | The stable owner identity, and the primary key. Generated rather than chosen, so no two deployments share one and nothing downstream can come to depend on a well-known value |
| `Document` | The owner's configurable record, as one `jsonb` document — their mail-account declarations and their owner-level settings. Nothing queries into it, which is what makes it a document rather than a schema: the configuration layer that writes it is what reads it back |
| `Version` | The version a write is accepted against. It is a number the writer states rather than the `xmin` token the rest of this page uses, because a rejected write has to be able to report which version it was refused against, and a token the database generates behind the write cannot be quoted back |
| `CreatedAt`, `UpdatedAt` | When the owner was provisioned, and when their document last changed — which is the provisioning instant until it does |

`mailbox_accounts` carries `Id` and `OwnerId` and nothing else. The mailbox identifier stays what it always was, so no row keyed by it is rewritten; the owner beside it is a `uuid` foreign key onto `settings_accounts` that cascades. Both are relational columns rather than values in a document, which is the point: ownership, lookup, uniqueness, and cascade erasure are then guarantees PostgreSQL gives rather than predicates somebody remembered to write. The one index on the table covers `OwnerId`, and the read it is there for is the one this column has — which mail accounts one owner owns, asked by the erasure below.

**A mailbox is bound to an owner that already exists.** The account row is created by whichever synchronization run first binds one of the account's folders, and that run reads the owner record rather than minting one: a run that invented an owner would be deciding whose mail it is while storing it, and the record it invented would be the boundary every later read of that mail is judged against. While mail accounts are declared in configuration a deployment therefore holds exactly one owner, because a configured account names none and nothing could decide which of several it meant; zero and several are both refused, and each says which of the two it was.

**Erasing an owner is one delete plus a named list.** The cascade above takes the accounts, the folders, the mail, and everything derived from it, and it takes the [contact book](#the-contact-book) beside them, which keys onto the owner rather than onto an account. What it does not reach is the tables that record a mail account as a plain identifier with nothing keying it onto one — `mail_answering_audit_entries`, `mail_drafts`, `mail_rederivation_positions`, `mail_rederivation_runs`, `mail_rule_evaluation_runs`, `mailbox_mutation_audit_entries`, `mailbox_refresh_tokens`, `outgoing_emails`, `recurring_sends`, and `spam_classification_runs` — and those are taken by statements of their own, in the same transaction, before the owner row goes. The list is derived from the model rather than maintained by hand, so a table added later that records an account without keying onto one is discharged the day it appears; a table that hangs off one of those, such as `outgoing_email_filings`, is left to its own cascade.

On a deployment storing payloads in a bucket the erasure reaches that store as well. The locators are read at the start of the same transaction, before any row goes, because the cascade is what takes the content rows and the locator on one is the only thing naming its object; the objects are then removed once the transaction has committed. [An object nothing points at is reclaimed](../features/email-content.md#an-object-nothing-points-at-is-reclaimed) states what happens where the endpoint refuses.

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

`Embedding` is pgvector's **dimensionless `vector`** rather than `vector(N)`. That is what lets two profiles of different widths share one table; declaring a width on the column would fix the whole table to one profile's geometry. Every read of it casts to the profile's own width, and the search over it is exact — [the vector index that is not there](#the-vector-index-that-is-not-there) is why nothing indexes the column.

Dropping the width from the column does not drop it from the schema, and the pair of constraints that replaces it is the point of the table's shape:

| Constraint | What it refuses |
|---|---|
| `ck_email_embeddings_dimension` | A vector whose actual length disagrees with the `Dimension` column beside it |
| `fk_email_embeddings_embedding_profiles` on `(EmbeddingProfileId, Dimension)` → `ak_embedding_profiles_id_dimension` | A `Dimension` the named profile never declared |

Neither half works alone. PostgreSQL evaluates a check constraint against one row, so without the foreign key the check would only prove that a vector agrees with a number nobody constrained; without the check, the foreign key would only prove that a number matches a profile. Together they mean a provider returning a vector of an unexpected width fails at the write instead of corrupting a search. That is deliberately enforced by the database rather than by the code that writes, because a wrong-length vector is a defect no query would report — it would return a plausible-looking distance rather than an error.

The two references behave differently on delete, and that is the whole erasure story. **The chunk cascades**: a vector hangs on a chunk, a chunk hangs on a stored email, so deleting a message reaches every vector derived from it without a rule anybody has to remember. **The profile restricts**: a profile is what a stored vector's attribution points at, so the schema refuses to remove one while a vector still names it. `ix_email_embeddings_profile` is what a whole generation is read by when a superseded one is removed in bounded batches; without it that read would scan every vector in the table.

`GeneratedAt` records when the vector was produced, which tells a re-embed from an original one apart.

## What a budget period has spent

`embedding_spend_periods` holds one row per budget period **and owner**, keyed by `PeriodStartsAt` and `OwnerId` in that order — the instant that period began, which every process derives from the configured period length and the Unix epoch rather than reading from anywhere, and the owner the spend was made for. The order is what lets one key answer both questions the ceilings ask: an exact lookup gives what one owner spent, and a range over the leading column alone gives what the deployment spent. `ConsumedInputCharacterCount` is a `bigint` because a period of a mailbox's initial embedding passes a billion characters without difficulty. Nothing allocates a period: the first spend inside one inserts its row and every later spend adds to it.

The table exists rather than the count being derived from the stored vectors, which would need no table at all. A superseded generation has its vectors removed in bounded batches, so a count taken over them would erase the record of a spend that genuinely happened — and the period in which a model change is paid for is exactly the period an operator is watching. It is durable rather than held in memory for the reason the ceiling exists: a process crashing and restarting in a loop would otherwise begin every period again from zero and spend the whole ceiling on each attempt.

The one write is an increment issued as an upsert, in the same transaction as the vectors it paid for. That makes the charge and the vectors one durable fact, and it means two workers spending inside one period add to each other rather than each overwriting a total that was already stale when it was read — which is why the row carries no concurrency token. Nothing hangs off it and nothing cascades into it — not even from the owner record, which is deliberate: a character count, an instant, and a generated owner identifier name no message, passage, or vector, so the record of a cost outlives every vector that cost paid for and outlives the erasure of the owner it was incurred for.

## What one owner's stored content holds

`owner_stored_content` holds one row per owner, keyed by `OwnerId`, with a `bigint` `StoredContentByteCount`. It is the only figure on this page that duplicates something derivable from the rows beneath it, and the duplication is the point: the derivation is a sum over one person's whole mailbox, and the per-owner storage ceiling it serves is consulted before every message. What it counts is the payload bytes, which is what a re-derivation produces — not what the table occupies on disk, an answer that exists only for the table as a whole and is what the deployment-wide ceiling reads instead.

Every movement is issued inside the transaction that stores or removes the payload, as a composed `INSERT … ON CONFLICT DO UPDATE` that adds a difference rather than writing a total, so a crash cannot leave a message stored and uncounted and two runs storing at once add to each other. Each statement resolves the owner by joining the message to its account, which is where `OwnerId` lives, so no caller passes one. An owner with no row has never had one written — a deployment upgraded before their first message, or an owner provisioned since — and their first read derives the figure once and adopts it. That derivation is the one operation here that writes a total rather than a difference, so it is also the only one that takes a transaction of its own: it claims the owner's row before it measures and holds it until it has written, and a store committing meanwhile is either counted by it or applied on top of it.

Unlike `embedding_spend_periods` beside it, this cascades from `settings_accounts`: it describes mail that owner holds rather than money this deployment spent, so erasing them takes it with everything else derived from their mail.

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

`DesiredSeenState`, `DesiredFlaggedState`, and `Keywords` are the other parameters, and each belongs to the mutations
that take it: the two booleans carry the direction a flag change asked for, and `Keywords` is a `text[]` carrying the
keywords an addition, a removal, or a replacement named. A row carries exactly the parameter its mutation takes and
null for the rest, which is what makes a resumed attempt ask for what was originally asked for rather than for whatever
a rule now says. The distinction between a null `Keywords` and an empty one is deliberate and load-bearing: null is a
mutation that takes no keywords at all, while an empty array is a replacement asking for every keyword to be cleared.

`Stage` is the sequence's own vocabulary rather than a generic pending and done, because it is what a retry resumes
from:

| Stage | What it says | Which mutations reach it |
|---|---|---|
| `Recorded` | The intent is durable and nothing has reached the server | every mutation |
| `PlacementIssued` | The command that would place the email has gone out and its answer was never read | relocate, copy |
| `PlacementConfirmed` | The server acknowledged the placement, and named it where it supplied `COPYUID` | relocate, copy |
| `SourceFlaggedDeleted` | The source carries `\Deleted` and only the expunge remains | relocate over the fallback, delete |
| `Completed` | The change is made, and asking again performs nothing | every mutation |
| `Abandoned` | Nothing will attempt it again, and `LastFailureCode` says what ended it | every mutation |

`PlacementIssued` is the one stage a retry may not act on. A `COPY` issued twice is a second message rather than a
repeat of the first, so a mutation found there is reported as an unknown outcome, has
`MailboxMutationOutcomeUnknown` (25002) written to `LastFailureCode` so an operator reading the row sees why it is
stuck, and is left for a person to resolve. Every other stage resumes: a relocation found at `PlacementConfirmed`
removes its source without copying again, and a delete found at `SourceFlaggedDeleted` reissues only the expunge. A
`\Seen` change never leaves `Recorded` until it completes — the store is idempotent on the wire, and its record exists
for provenance rather than for retry safety. A `\Flagged` change and every keyword change behave the same way, for the
same reason: `STORE +FLAGS` and `STORE -FLAGS` say what a message should carry rather than what to do to it, so issuing
one twice leaves the mailbox where issuing it once did.

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
| `Trigger` | What put the run there — `RequestedRun` for one an operator asked for, `ScheduledRun` for one a rule's schedule asked for. It is what the pass reads the walk's reach from, so a scheduled run reaches the rules declaring a schedule while a requested one reaches every rule; it is also what the run history's own trigger is recorded as. Text for the reason every other bounded value here is |
| `revision` | The rule set the run is bound to, null until the first pass picks it up. Bound when the run starts rather than when it is requested, because the set may reload between the two and what matters is the one in force when the first message is actually evaluated |
| `position` | The identity of the last message a batch committed, null while the run has committed none. Committed with the evaluations it accounts for, which is what makes the run resumable rather than merely restartable |
| `evaluated_email_count`, `matched_email_count`, `skipped_email_count` | What the run has done so far, across every account run that has carried it |
| `ended_at`, `ending` | When and how the run stopped being outstanding. `Completed` is the end of the account's mail; `Superseded` is the rule set having changed while the run was outstanding, which ends it rather than letting one walk apply two rule sets to one mailbox. The ending is text for the reason every other outcome here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
| `ConcurrencyVersion` | The `xmin` token again, because a pass committing a position and a request arriving can both reach this row |

The row survives the run it describes, holding the last ending until a new request replaces it, and there is no history behind it: one account has one row. Nothing cascades into it and it carries no foreign key onto `mailbox_accounts`, for the reason `mailbox_refresh_tokens` carries none — a run may be asked for before any folder of the account has been bound.

One row per account is also what settles the two triggers against each other. An operator's request replaces an outstanding scheduled run, because it reaches every rule and the scheduled one reaches only some of them; a schedule's occasion finding any run outstanding stands down and is counted as skipped rather than starting a second walk of one mailbox.

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
| `Trigger` | What reached the message: `Arrival` for a rule applied as mail arrived, and `RequestedRun` or `ScheduledRun` for one applied by a whole-mailbox walk, according to what asked for the walk. Text for the reason every other outcome here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
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

## The conversation a message belongs to

`email_threads` holds one row per conversation, and `email_thread_identifiers` one row per message identifier that
conversation answers to. Both are owned by exactly one mailbox account and cascade with it. On `stored_emails` two
nullable `uuid` columns carry the result: `EmailThreadId` names the conversation the message was placed in, and
`ParentStoredEmailId` names the stored message it answers.

| Column | What it records |
|---|---|
| `email_threads.Id` | The conversation's identity, a version-7 identifier, and the value every tool publishes. Minted when a message reaches no existing conversation |
| `email_threads.MailboxAccountId` | The account the conversation belongs to. Membership never spans two accounts, because a message identifier is only unique in the sense the sender's own system gave it, and one account's correspondence must not be assembled out of another's |
| `email_threads.AssembledAt` | When the conversation was started. It is what decides a merge: a message naming two conversations folds the later one into the earlier, so which of them survives is a fact about the mailbox rather than about the order somebody re-derived it in |
| `email_threads.MergedIntoEmailThreadId` | The conversation this one was folded into, null while it is its own. The row is kept rather than deleted, so an identifier a tool published before the merge still resolves — a read follows the chain to the survivor instead of answering that the conversation is gone |
| `email_threads.ConcurrencyVersion` | The `xmin` token, because the column above is amended in place and two arrivals can reach one row at once. Each reads a conversation, decides which of the two survives, and writes the merge; without the token the second write would settle a merge against a survivor that had already been folded into a third, and the chain a read follows would end at a row that is no longer anybody's survivor |
| `email_thread_identifiers.MailboxAccountId`, `IdentifierHash` | The primary key. The hash is the lower-case hexadecimal SHA-256 digest of the identifier as it was stored, `character varying(64)` |
| `email_thread_identifiers.EmailThreadId` | The conversation the identifier belongs to. Repointed rather than duplicated when two conversations merge |

**The identifier is stored as a digest and never as itself.** A header is bounded at the 998 octets RFC 5322 allows,
and a B-tree entry that wide is one PostgreSQL refuses at insert time — which would fail the arrival transaction rather
than lose a value. SHA-256 is chosen for collision resistance rather than for secrecy: nothing here is a secret and
nothing verifies one, and what the digest has to guarantee is that two identifiers never collapse into one conversation.
Nothing is folded, trimmed, or normalized before it is hashed, because the mail ecosystem compares an identifier octet
for octet. Storing no raw identifier is also the narrower record: the row says that *some* message named this
conversation without carrying the name a sender wrote.

**An identifier nobody stored is a row of its own.** Membership is decided from `internet_message_id`, `in_reply_to`,
and the thread references alone, so two replies to a message this deployment never fetched bind the same absent
identifier and land in one conversation. That is why the identifiers are a table rather than a lookup over
`stored_emails`: the conversation exists before, and often without, the message at its root.

`ParentStoredEmailId` is what makes a conversation a tree rather than a set, and it is a self-referencing foreign key on
`stored_emails` with `ON DELETE SET NULL` — as is the conversation column beside it. A message whose parent is erased
becomes a root rather than taking the erasure with it. Nothing stores an order: where a message sits in its conversation
is computed on every read, because storing it would mean rewriting every row of a conversation each time one message
arrived. [The MCP tools](../features/mcp-tools.md#the-conversation-a-message-belongs-to) describe what a caller sees of
all this, and [bringing stored mail up to a later
release](../features/imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) describes the one pass that
writes these columns outside the arrival transaction.

Nothing in either table is personal data as stored. An account alias, a UUID, an instant, and a digest of an identifier
are MailFathom's own names for things.

## The contact book

`contacts` holds one row per person the owner's book knows, and `contact_addresses` one row per address that person
uses. What reaches it comes from two places, and the `Origin` column below is which: a row the owner wrote down is the
one record on this page derived from no message at all, while a row
[collection](../features/contacts.md#collecting-contacts-from-arriving-mail) recorded is an address that arrived in mail,
written by the pass that stored the message rather than by a worker of its own.
[Contacts](../features/contacts.md) describes the rules every writer obeys; what the schema itself decides is below.

| Column | What it records |
|---|---|
| `Id` | MailFathom's own UUIDv7, minted when the contact is recorded. Never an address, because an address is a thing a person has rather than a thing they are |
| `OwnerId` | Whose book holds this person, keyed onto `settings_accounts` and cascading from it. Every read of the book carries it, and it leads the two indexes a read would otherwise walk the table for — the listing and the address uniqueness below — while the identity lookups seek on the key and carry the owner as a predicate beside it. It never changes: a contact is written in a book rather than moved between them |
| `DisplayName`, `DisplayNameSortKey` | The name as the owner wrote it, and the upper-cased comparison form the listing is ordered and paginated by. The form is stored rather than derived in the query and its column is pinned to the `C` collation, so the order is the ordinal one that form was derived to produce instead of whichever collation the database was created with |
| `PreferredNormalizedAddress` | Which of the person's addresses to use by default, as its comparison form. A column on the person rather than a flag on each address, because changing the choice is then one update instead of two that pass through a state where nobody, or everybody, is preferred |
| `Note` | What the owner wrote about the person, or null |
| `Origin` | `Asserted` where somebody wrote the person down, `Collected` where an address merely appeared in arriving mail. Held as its own name for the reason every bounded value on this page is |
| `RecordedAt`, `AmendedAt` | When the contact entered the book, and when it was last changed |
| `ConcurrencyVersion` | The `xmin` token, because a contact is amended in place. What it settles is a row that changed or disappeared between the read an amendment is applied to and the commit — above all an amendment racing an erasure, which then writes nothing rather than putting the person back |

`contact_addresses` carries the address as written beside its comparison form, the contact it belongs to, and the owner
that contact belongs to. It is rows rather than an array column, and that is what makes both rules over it structural.
The comparison form is unique **within one owner's book** rather than across the table, so one address stays in one
person's hands however many callers claim it at once — the loser of that race is recognized by the constraint it
violated and is answered with which contact holds the address — while two owners who each correspond with the same
person each hold their own record of them. Uniqueness across the table would instead let one owner's book decide what
another's may contain. And the foreign key cascades, so erasing a person takes every address row with them rather than
leaving a second statement to remember.

The owner is repeated on the address row rather than read through the contact, because an index cannot reach through a
foreign key to a column on the parent and the uniqueness above is over the owner and the address. What keeps the
repetition honest is the key itself: the foreign key is `(ContactId, OwnerId)` onto the alternate key `(Id, OwnerId)` on
`contacts`, so an address row can only carry the owner of the contact it hangs on — no other pair exists to point at.

The preferred address is deliberately **not** a foreign key onto the address row. The two tables already point one way,
and a key pointing back would make inserting either of them first impossible without deferring the constraint; that the
named address is one the contact holds is enforced by the domain when the record is written and again when it is read.

## Durable background work

`jobs` holds work that is enqueued now and done later: what it is, what it points at, who is holding it, and until when. A [rule's schedule](../features/mail-rules.md#running-a-rule-on-a-schedule) is what enqueues into it today, and the handler that runs one records a whole-mailbox rule run for an account; this is the record every consumer of durable background work is written against, and [ADR 0009](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0009-durable-job-store-and-execution-identity.md) is the decision it implements. What paces the worker over it is the [`Jobs`](../operations/configuration-runtime.md#jobs) configuration section.

| Column | What it records |
|---|---|
| `Id` | What addresses the job, and the primary key. A version-7 identifier, so one batch's inserts land together in the index rather than scattering across it |
| `JobType` | The kind of work, held as the closed enumeration's own name. The name is the identity — the word in a log line, the name of a span, the dimension a counter is broken down by — and it names exactly one payload contract, which is what lets the document beside it be read back as the shape it was written as. A name the running build does not declare is left alone rather than failed, because a type an older replica has never heard of is a fact about the deployment and not about the work |
| `IdempotencyKey` | The identity of one execution, composed by whoever knows what the work is and opaque here. It is unique with the type across the whole table, which is the idempotency guarantee itself rather than a support for one |
| `Payload` | The references the work is described by, as one `jsonb` document. Nothing queries into it — the key, the type, the account, and the available instant are all columns — so it is a document rather than a schema, and it is bounded at the enqueue boundary so a payload that copied content into it is refused instead of stored |
| `MailboxAccountId` | The account the work belongs to, null when it belongs to none. A foreign key onto `mailbox_accounts` that cascades, so removing an account takes its queued work with it |
| `State` | `Pending` while the job is claimable, `Claimed` while an attempt holds it, `Succeeded` once it is done, `DeadLettered` once nothing will attempt it again, and `Dropped` once an operator has decided it never will. Text for the reason every other bounded value here is: it stays readable in an ad-hoc query and survives a reordering of the enum |
| `AvailableAt`, `EnqueuedAt`, `StateChangedAt` | When the job becomes claimable, when it was written, and when its state last moved. The first is a column rather than a schedule elsewhere, because it is what the claim selects on, and it is where a retry's backoff is expressed |
| `TurnAt` | When this job's turn comes once its owner's queue is shared with everybody else's, and what the claim orders by. A virtual instant rather than a real one: the enqueue reads how far its owner's waiting work has already reached, adds a second, and takes the later of that and the instant the job becomes available — so an owner with nothing waiting lands on the moment its work is due, and an owner queueing a thousand jobs at once holds turns spread over the next thousand seconds instead of a thousand turns at one instant. That is the whole of what stops one person's backlog postponing another's due work, and it is decided here rather than in the claim so the claim stays one indexed statement. Never null, and never earlier than `AvailableAt` as written: the enqueue floors it there, and a retry and a returned dead letter each carry it forward to the instant they name, so no write leaves a job holding a turn it could not take. The two do diverge afterwards, and a release is where — it moves `AvailableAt` to now and leaves the turn alone, because the attempt gave the work back rather than failing at it, so a released job resumes the place it already had rather than going to the end of its owner's queue. Nothing reading a row may assume `TurnAt >= AvailableAt`. Rows written before the column existed carry the available instant they already held, which is the order the claim had been draining them in |
| `AttemptCount` | How many attempts have been handed out. Counted by the claim rather than by whatever runs the work, because a process that dies mid-execution never reaches a line that would have counted it and a crash loop would otherwise be invisible. A release gives one back, because a shutdown is not something the work did |
| `LastFailureClassification`, `LastFailureReason` | What the last failed attempt was classified as — `Transient` or `Permanent` — and the operator-safe name of what failed, both null until one has. The reason is a type name and a stable error code and never an exception message: a handler works on mail, and a library's message may quote a subject, an address, or a header into a column that outlives the run |
| `EnqueuedTraceParent`, `EnqueuedTraceState` | The W3C trace context of whatever was running at the enqueue, both null when nothing was being traced and on every row written before the columns existed. The one pair here that describes neither the work nor its state: a worker claims a job long after the span that caused it ended, so the attempt cannot be that span's child, and what these make possible instead is a link from the attempt's span back to the trace — a cause hours earlier reached in one step. Nothing queries by either, and a value the reader cannot use is treated as absent rather than failing the attempt. A trace identifier is a random number this process minted, which is what makes it safe to keep beside work that points at mail |
| `LeaseOwner`, `LeaseExpiresAt` | The attempt holding the job and the instant after which it is claimable again, both null while nothing holds it |

Enqueuing asks the table one question before it writes to it: whether this job type already has as many rows `Pending` as
[`Jobs:MaxQueueDepthPerType`](../operations/configuration-runtime.md#jobs) allows. That runs on every enqueue rather
than occasionally, which makes it the second query this table runs at any volume — and it is bounded rather than
counted, because the read stops at the depth it is comparing against instead of totalling the backlog. A queue at its
depth refuses the enqueue and says so, so the caller slows down, asks again later, or stops producing; a request whose
work is already queued is still answered with that job rather than turned away. Two enqueuers meeting the bound together
can both pass it and the depth overshoots by as many as raced, because this is backpressure rather than an invariant.

A claim is one statement: it selects the due rows of a type the asking process runs whose turn has come first, under `FOR UPDATE SKIP LOCKED`, and stamps them with a lease owner and an expiry in the same statement. Two things follow. Two workers claiming at the same moment take different jobs instead of waiting on each other, which is what makes the queue drainable by more than one process. And a job is due either because it is pending and its available instant has passed **or** because it is claimed under a lease that has run out — so work in flight when a process died is picked up without an operator doing anything, and nothing has to be told the process is gone.

The order is `TurnAt` rather than the instant a job became available, and that is what makes the claim fair across owners rather than first-in-first-out. Under one owner the two orders are the same, because an owner with nothing waiting takes the instant its work is due. Under several they are not: a person whose mailbox produced a large backlog holds turns stretching ahead of the clock, so somebody else's job — whose turn is the moment it arrived — is claimed between them instead of behind the whole backlog. Work belonging to no account is its own participant and is claimed on the instant it is due, so nothing that no caller requested is starved by the rule. Nothing ranks anything at claim time: the order is one column of one index, because a claim that had to rank a backlog could not also be one statement under `FOR UPDATE SKIP LOCKED` — PostgreSQL refuses to combine that clause with a window function. [ADR 0014](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is the decision this implements, and it fixes the property rather than the algorithm: no owner's backlog may postpone another owner's due work indefinitely.

Renewal, completion, a scheduled retry, a dead letter, and release are each a single update conditional on the lease owner still matching. An attempt that lost its lease, finished late, and tried to write its result finds the row owned by the attempt that replaced it and writes nothing. That compare-and-set is why the table carries no `xmin` token: the fact that decides whether a writer still owns the work is the lease, and a row version beside it would report a conflict for a renewal that changed nothing an attempt cares about.

Which of them a worker writes is decided by how the attempt ended, and they are deliberately not one. A handler that finished completes the job. A handler stopped because the host is shutting down releases it, so the job is claimable again at once, the attempt the claim counted is given back, and the deployment costs it nothing.

Anything else is a failure, and the failure is classified before the attempt budget is consulted. A failure that could clear on its own — a dependency whose resilience pipeline declined the work, a provider that said its answer was worth repeating, a connection that dropped, an execution that ran out of time — schedules another attempt: the row goes back to `Pending` with an available instant a jittered exponential delay ahead of now, so the backoff is the column rather than a timer anywhere. A failure that could not — a rejected credential, a refused request, anything whose meaning is unrecognized — ends the job on its first attempt, because repeating it could only reach the same answer, and so does a transient failure that has used up `Jobs:MaxAttempts`. Either way the row becomes a `DeadLettered` one that keeps its attempt count, its key, and the classification and reason it ended on. A dead letter is inert: the claim's predicate names the two claimable states, so nothing takes it again, one job that cannot succeed consumes no further attempts, and the jobs behind it are unaffected.

A dead letter is terminal but not permanent, because the two things that could resolve one are both an operator's. Returning it to the queue is a conditional update from `DeadLettered` back to `Pending` that hands the attempts back and makes the row due now — the same row, so the work runs under the identity it was already enqueued with rather than as a second job, and a retry of a job something else already dealt with changes nothing and says so. Writing it off is a conditional update to `Dropped`, which is a fifth state rather than a deletion: the row stays, keeps the failure that ended it, and goes on holding the key that refuses the same execution. Both are reached through [the administrative endpoint](../operations/admin-endpoint.md#reading-the-background-work-that-stopped-and-deciding-what-becomes-of-it) and neither reads the payload.

A terminal row keeps its key. That is what stops a repeating trigger enqueuing work that has already been done, and it is why a finished job stays in this table rather than moving to another one. It also makes pruning a correctness setting rather than housekeeping: erasing terminal rows is what ends the deduplication, so whichever change adds pruning inherits a retention floor of the longest window in which one trigger can legitimately fire again.

What the store delivers is at-least-once execution and nothing stronger. Uniqueness stops the same work being *enqueued* twice; only a handler can stop a re-run after a crash from having a second effect.

## What each recurring dispatch has already done

`job_schedules` holds one row per declared schedule: where its occasions are counted from, and which of them it has already turned into a job. It is what makes "a due time that passed is skipped rather than replayed" a property of the schema — the state says which occasion was last acted on, and everything before that is simply never asked about again.

| Column | What it records |
|---|---|
| `ScheduleId` | The schedule's identity, and the primary key. Composed of MailFathom's own names — for a rule's schedule, the account identifier and the rule name — so it survives a restart, means the same thing on every replica reading one configuration, and carries nothing out of a message. An identity that changed would be seeded afresh and would forget the occasion it last dispatched |
| `ObservedFrom` | When this instance first saw the schedule declared. The first pass to meet a schedule records this instead of dispatching, so a declaration added today does not owe every occasion since the epoch |
| `LastOccurrenceAt` | The occasion the dispatch last acted on, null until it has acted on one. Occasions are counted from this when it is set and from `ObservedFrom` when it is not, and it advances even where the occasion was passed over — which is exactly what makes a missed one skipped rather than owed |
| `LastDispatchedJobId` | The job the last dispatched occasion enqueued, null until one was, kept so an operator can follow a schedule to the work it produced |

There is no `xmin` token, because one writer decides about one schedule: the pass reads a state, decides, and writes the decision back, and a second instance reaching the same occasion is answered by the queue's own uniqueness rather than by this row — the job's idempotency key is the schedule's identity and the occasion's instant, so two instances dispatching one occasion produce one job.

The row carries no foreign key onto anything. A schedule is declared in configuration rather than stored, so there is no row for a constraint to point at, and a declaration withdrawn from the configuration leaves its state behind harmlessly: nothing reads a state whose schedule is no longer declared.

Nothing in it is personal data. An account alias, a rule name, two instants and a job identifier are MailFathom's own names for things.

## Where an operator's re-derivation has got to

`mail_rederivation_positions` holds one row per unfinished re-read of stored MIME — the walk [`mfctl mailbox
rederive`](../features/imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) drives when a release starts
recording something the payload already carried. It is what makes an interrupted invocation resume rather than restart.

| Column | What it records |
|---|---|
| `MailboxAccountId` | The account being walked, and the first half of the primary key |
| `FolderAlias` | The one folder the walk was narrowed to, and the second half of the key. The empty string is the whole-account walk: a primary key holds no null, and no folder can collide with it because an alias is validated non-blank wherever one is created |
| `LastProcessedStoredEmailId` | The last stored email a committed batch read. The walk is ordered by that identity, which is total, stable, and already indexed, so the next batch continues past this value |
| `UpdatedAt` | When the position last moved, which is what says whether a walk somebody abandoned is worth finishing |
| `ConcurrencyVersion` | The `xmin` token again. The insert of a scope's first position is settled by the key; the updates after it are the same race one row later, and without the token a slower walk's earlier position would be written over a faster one's later one and re-read the difference on every pass afterwards |

It is a table of its own rather than a row in `backfill_positions` because that walk is one per deployment and named by
a constant, while this one is one per scope an operator names — keying it by the scope is what lets two accounts be
refreshed independently. There is no foreign key onto the account: the row is a cursor over rows that are already keyed
to one, and requiring the account row would make the walk depend on a table it never reads.

A row exists only while a walk is unfinished. The segment that reaches the end of its scope removes it, so the same
scope asked for again after a later release starts at the beginning rather than behind where the last refresh stopped.

Nothing in it is personal data. An account alias, a folder alias, a local identifier and an instant are MailFathom's own
names for things.

`mail_rederivation_runs` holds the operator's half of the same walk, and it is a second table because the two answer
different questions and outlive each other differently. The position above is a cursor the walk consumes and deletes;
this is what an operator asked for and what has come of it, and it stays after the walk has finished so that
[`mfctl mailbox rederive-status`](../operations/admin-endpoint.md#bringing-stored-mail-up-to-a-later-release) can say
how the last run ended rather than that there was none.

| Column | What it records |
|---|---|
| `MailboxAccountId` | The account the run walks, and the first half of the primary key |
| `FolderAlias` | The one folder it was narrowed to, and the second half of the key, under the same empty-string convention the positions table uses. Two scopes are two runs, so a folder being re-derived says nothing about the account's own run |
| `RunId` | The run's own identity, a UUIDv7 generated from the instant it was asked for. It is in the job's idempotency key, which is what makes a run started after an earlier one finished a new piece of work rather than one the queue answers with a terminal row it already holds |
| `RequestedAt` | When the run was asked for |
| `SegmentCount` | How many attempts the walk has been handed to. It rises when an attempt hands the rest of the scope on, and it is in the idempotency key beside the run so the next segment is a job of its own rather than the one that just ended |
| `RederivedEmailCount`, `UnreadableEmailCount`, `MissingContentEmailCount` | What the run's passes have committed so far. Every pass adds its own figures to what the row holds rather than writing a total, because two attempts of one run can overlap and a total computed outside the transaction would lose whichever committed first |
| `EndedAt` | When the run reached the end of its scope, null while it has not. That is what "a run is outstanding" means, and it is what a second request for the same scope is answered with |
| `ConcurrencyVersion` | The `xmin` token, for the same reason the position row carries one: the counts are read, added to, and written back, and without the token the slower of two passes would write its own reading over the faster one's |

There is no foreign key onto the account, and none onto the position row either. The run is a record of something an
operator asked for, and the position is a cursor the walk keeps: erasing the mail behind a run leaves counts describing
work that was really done, and a run whose position row is gone is a finished run rather than a broken one.

Nothing in it is personal data. An account alias, a folder alias, an identifier this deployment generated, counts, and
two instants are MailFathom's own names for things and for its own work.

## Where the move of stored content has got to

`content_move_runs` holds the one move of database-held raw MIME into the object backend a deployment may have — the
walk [`mfctl content move`](../operations/moving-stored-content.md) drives after an operator selects
`ContentStorage:ObjectStorage` on a deployment whose mail is already stored. It is what makes the move resume rather
than restart across a restart, and what an operator reads its progress off.

| Column | What it records |
|---|---|
| `Name` | The primary key, and always `stored-content`. A check constraint pins it to that one value, which is how the table holds at most one row: a move is a decision about the whole deployment rather than about a scope somebody names, so there is nothing to key it by and a second row would be a second walk over the same payloads |
| `RequestedAt` | When the move was asked for. It is also the identity a pass commits against — a pass that finds a different instant on the row is writing into a move somebody replaced, and records nothing |
| `State` | `Running`, `Paused`, or `Completed`, written as its own name rather than an ordinal. Only the first is carried; the other two are what make a pass idle without cancelling anything |
| `Kind` | Which of the four raw-MIME tables the walk is on, written as its own name for the same reason. The walk moves to the next kind when a kind runs out, and past the last one it is finished |
| `ResumeAfter` | The payload identity the last committed pass reached inside that kind, null at the start of one. The walk is keyset-ordered by that identity, so the next pass continues past this value and re-copies nothing it verified |
| `CopiedPayloadCount`, `FailedPayloadCount`, `MovedByteCount` | What the move's passes have committed so far. Every pass adds its own figures to what the row holds rather than writing a total, for the reason the re-derivation run does |
| `EndedAt` | When the walk reached the end of the content, null while it has not |
| `ConcurrencyVersion` | The `xmin` token again. A pass reads the row, adds to it, and writes it back while an operator may be pausing it from a request, and without the token one of the two would be written over |

There is no foreign key onto anything, and no row per payload. What the move is doing to a payload is on the payload's
own row — the backend it names and the locator it carries — so this table holds the walk and nothing else, and a payload
repointed by a move that was later replaced is simply an object-backed payload.

A row survives the move it describes. `Completed` is what lets `mfctl content move-status` say how the last move ended
rather than that there was none, and asking for another move writes a fresh row over it, starting again at the first
kind — which is how payloads a move left in the database are reached once the reason has been repaired.

Nothing in it is personal data. A constant, two instants, a state, a payload kind, one local identifier, and three
counts are MailFathom's own names for its own work. The resume position is the one value that names a row holding mail,
which is why it is not served by the endpoint that reports the rest.

## The outgoing messages waiting to be sent

`outgoing_emails` holds one row per message MailFathom has been asked to send, written **before** the first SMTP
command is issued and advanced as the attempt proceeds. It is `mailbox_mutations` again in a second protocol, with the
consequence raised: a submission is the MIME being built, an intent being recorded, a connection being opened, each
recipient being offered, the body being transmitted, and the server answering — and a process can die between any two of
those. One of those windows is genuinely undecidable from outside. A crash immediately after the body went out and
immediately before the acknowledgement was recorded leaves an outbox that cannot say whether the message was delivered;
retrying sends it twice, and not retrying loses it. Unlike a duplicated local copy, a duplicated delivery cannot be
withdrawn from the mailbox it reached.

| Column | What it records |
|---|---|
| `Id` | The record's identity, and what the stored message and the recipients hang on. A UUIDv7 generated from the instant the intent was written, so the outbox's own order is the identifier's |
| `MailboxAccountId` | The account the message is submitted through and sent as. A plain column rather than a foreign key: the account row is created by the first folder binding synchronization writes, and an account configured only to send need never have synchronized anything |
| `RequesterOrigin`, `RequesterIdentity` | The authored act that asked, by kind and by the text two requests are compared by. A rule answers with its name, the revision it was evaluated at, and the email it acted on; somebody present answers with a key of their own |
| `PrincipalFingerprint` | A fixed-width digest of the identity the caller was admitted under, written by the outbox rather than stated by a caller. It is what confines a read or a withdrawal of a send to the caller that queued it, and a digest rather than the identity so the comparison is possible without a second copy of whatever a token asserted living at rest. A send a rule asked for carries the process identity's digest and is additionally excluded by its origin, so no caller reaches one; a row written before the column existed carries null and therefore matches nobody |
| `Stage` | How far the submission has durably got, in the vocabulary below |
| `MimeByteLength` | How many bytes of MIME were stored. Kept beside the record as well as on the message, so the size bound a submission server advertised can be compared against it — and the outbox listed — without reading a single queued message's `bytea` |
| `AttemptCount` | Counted by the claim itself rather than after the attempt, so an attempt that kills the process still counted |
| `AvailableAt` | The instant the record may next be claimed. It is the backoff written down: a transient failure moves it forward by the delay that attempt earned, a send nothing has deferred carries the instant it was recorded, and a send written to leave at a named time carries that time — which is the whole of what holding one costs, since the claim below already refuses a row whose availability has not arrived |
| `DueAt`, `DueZoneId` | The time the message was written to leave at and the zone whoever named it was thinking in, both null for a send that named none. Kept beside `AvailableAt` rather than derived from it, because a retry moves availability and lateness has to be measured from the authored time; the zone is kept because nine in the morning is nine in the morning on both sides of a daylight-saving transition and the offset alone would not say which nine was meant |
| `LeaseOwner`, `LeaseExpiresAt` | Who is attempting it and until when, both null while nobody is. The pair is what makes a crash recoverable without anything being told the process died |
| `RecordedAt`, `StageChangedAt` | When the intent was written, and when the record last moved. The second is what says how long a stuck send has been stuck |
| `LastFailureCode` | The code of the failure the last attempt ended in, null while none has. The code is kept and the message is not, because a failure message is assembled at the failure site and may repeat what a remote server wrote |
| `LastReplyCode` | The reply code the server last answered the transmission with, null while it has answered none. A different fact from the per-recipient codes: a server accepts or refuses each address separately and then answers once for the body |

`Stage` is the submission's own vocabulary rather than a generic queued and sent, because it is what a later attempt
reads:

| Stage | What it says |
|---|---|
| `Recorded` | The intent and the message are durable, and nothing has reached a submission server. It is also where a failed attempt that provably transmitted nothing is rewound to, so a deferred send waits here rather than in a stage of its own |
| `TransmissionBegun` | The body has begun to go out and the server's answer to it was never read |
| `Sent` | The server accepted the message for every recipient it had accepted |
| `Refused` | Nothing will offer it again, and `LastFailureCode` says what ended it |
| `Cancelled` | The send was withdrawn before anything was transmitted for it, which is what a message held until a named time is withdrawable for the whole of |

`TransmissionBegun` is written **before** the transmission rather than after it, which is the whole point: announcing it
afterwards would announce it only in the case where the crash it exists for did not happen. A record found there is not
re-sent. Two of the terminal stages are reachable from one stage only, which is that same window read from either end —
`Sent` follows only `TransmissionBegun`, so no row claims a delivery nothing could have produced, and `Cancelled`
follows only `Recorded`, so no row claims a withdrawal after bytes that may already have reached somebody.

**A withdrawal is one conditional statement rather than a read followed by a write.** It moves a row to `Cancelled` and
clears the lease columns only where the row is still at `Recorded` and no unexpired lease is held on it, which is the
same predicate the claim below applies — so a send an attempt is holding is left alone, an expired lease counts as free
in both, and the statement writing nothing is how a caller learns the send can no longer be withdrawn. Applied a second
time it matches nothing, because the row is no longer at the stage it names, which is what makes withdrawing twice one
withdrawal rather than two writes with the same result.

**A send stopped mid-transmission stays at `TransmissionBegun` and is never advanced by anything automatic.** Moving it
to `Refused` would be this system stating that nothing reached anybody, which is the one thing nobody can establish
about that window; moving it back would send it twice. So the stage is left where it is and `LastFailureCode` is
stamped with `28011`, which is what makes the row say *unknown* rather than *stuck*. It is claimed by nothing
afterwards — the claim below takes `Recorded` rows alone — so the only thing that moves it is a person deciding what
happened.

That is also why the rewind exists. An attempt that failed before any recipient was accepted has provably transmitted
no body, so its record goes back to `Recorded` and is attempted again; an attempt that failed after one was accepted
may have, so it stays where the paragraph above leaves it. Which of the two applies is decided from the replies the
server actually gave during that attempt rather than from the exception, because a body is only ever offered after at
least one `RCPT TO` was accepted.

A send that has not reached a terminal stage is what a restart reads, oldest first and bounded like every other public
query. A refused row stays in that answer for the reason an abandoned mutation does: being given up on is what stops a
send being attempted, and it would be worth nothing if it also stopped the send being seen.

`outgoing_email_recipients` holds one row per person the message is addressed to, keyed by the record and the position
in its recipient list. A message is offered per address and answered per address, so a mistyped address among five must
not stop the other four, and the four the message reached must not be offered it again when the fifth is retried. Each
row carries the `Address`, the `Role` the composed message names them in — `To`, `Cc`, or `Bcc`, which reach `RCPT TO`
identically — the `Status`, and the `LastReplyCode` and `AnsweredAt` of the last answer about them. It also carries a
nullable `ContactId` where the address came out of the contact book rather than from an author's own typing, which the
`AddOutgoingRecipientContact` migration adds. Both are kept because they answer different questions: the address is what
a resumed attempt offers, and the contact is which person the message was addressed by naming — a record holding only the
person would answer the wrong question a year later, since a message sent to somebody whose address changed afterwards
was sent to the address they had. It carries no foreign key onto `contacts` and no index, deliberately in both cases: a
contact amended, promoted, or erased later must not rewrite what was sent, and nothing ever looks a send up by contact.
It carries an `xmin`
token of its own rather than relying on the record's, because an attempt answers about one address without touching the
record above it: without one, two attempts settling the same recipient would be a last writer winning silently, and
settling a recipient is what decides whether anybody is offered the message again.

`Status` has exactly three values, and it answers exactly one question: is this recipient offered on the next attempt.
`Accepted` means an acknowledged transmission covered them, so nothing offers them again; `Refused` means a server
permanently refused them, so nothing offers them again and nothing reaches them; `Pending` is everybody else, which
includes a recipient a server temporarily rejected — the reply that deferred them is recorded beside the status rather
than encoded in it. A recipient already settled keeps the answer it has, so a late transient reply cannot undo a
delivery that already happened.

The ordinal keys the row rather than the address, for two reasons. An address is personal data and a key is repeated
into every index over a table; and the ordinal keeps the recipients in the order the request named them, which is the
order a composed message writes its headers in. The comparison form of the address is deliberately not stored beside it,
unlike a received message's participants: those are filtered and grouped by address in queries the database answers,
while these are read back with their record and compared in memory against the handful of answers one attempt produced.

`outgoing_email_contents` is the message itself, in the same one-to-one arrangement `email_message_contents` has with
`stored_emails` and for the same reason: keeping the `bytea` out of the record means listing what is queued never loads
a single message's bytes. It is written **once** and read back for every attempt rather than recomposed, which is not an
optimization — a message rebuilt between attempts carries a different `Message-ID` and would thread as a second message
in every recipient's client. A second enqueue of the same identity therefore leaves the stored payload exactly as it is.

### The copies of an outgoing message that are in the mailbox

`outgoing_email_filings` holds one row per place a copy of an outgoing message has been put, keyed by the record and
the filing — `draft`, `held`, or `sent` — so an account's own Sent folder and its outbox folder are two rows of one
send rather than two records. It exists because an `APPEND` cannot be corrected by repeating it: every mutation
elsewhere in this schema names a message the server already holds, while an append *creates* one, so a second attempt
is a second copy in somebody's folder that nothing afterwards can tell from the first.

That key is named `pk_outgoing_email_filings` for the reason `pk_email_embeddings` is: it is what two passes reaching
one send at once collide on, and a lost race is only recognized as one where the constraint has a name to recognize it
by. The loser retries from a fresh read, finds the row the winner issued, and appends nothing.

| Column | What it records |
|---|---|
| `OutgoingEmailId`, `Filing` | The send and which of its places this row is, which together are the key. A cascade from the record removes them with it |
| `MailboxAccountId` | The account whose mailbox holds the copy, carried here so the read that recognizes a copy coming back is answered without joining the record |
| `FolderAlias`, `FolderPath` | The folder the copy was appended to, as the alias an operator wrote and the path that alias named at the time. Both are kept because the alias is what a failure is reported by and the path is what a discovery is matched against |
| `Stage` | `Issued` before the command went out, `Confirmed` once the server answered, `Withdrawn` once the copy was taken back or given up on |
| `PlacementUidValidity`, `PlacementUid` | Where the server said it put the copy, both null on a server that advertises no RFC 4315 `UIDPLUS` and therefore said nothing |
| `InternetMessageId` | The identity read back off the appended bytes, which is what recognizes the copy where the server named no placement |
| `AppendedAt`, `ObservedAt`, `WithdrawnAt` | When the append was issued, when the copy was seen coming back through synchronization, and when it was removed. The second is what stops the recognizing read from going on looking for a copy already accounted for |
| `xmin` | The concurrency token, as everywhere else |

**A row at `Issued` is the undecidable window, and it is left undecided.** The row is written before the command goes
out precisely so a process that died in between leaves something behind; nothing appends again on the strength of it,
and nothing moves it forward either, because either choice would be this system claiming to know something about
somebody's folder that it cannot. `outgoing_emails.LastFilingFailureCode` carries the reason beside the send, which is
where an operator reads why a message they sent is not in their Sent folder. It is a column on the record rather than
on this table because a failure may have to be recorded where no row exists at all — a destination that resolves to
nothing is the ordinary case — and because it must never be mistaken for something about the delivery, which is
untouched by any of it.

`stored_emails.FiledFromOutgoingEmailId` is the other half of the same fact, written when a synchronization recognizes
a discovery as one of these copies. It is a plain column rather than a foreign key: the two rows have different
lifetimes, and a send whose record is erased must not take the message in somebody's folder with it. Everything
reacting to newly arrived mail filters on it — [the rule queue](../features/mail-rules.md#when-rules-run) both in its
predicate and in its own partial index — so a copy of what the owner just sent never reads as mail that arrived.

### The claim a delivery attempt holds

An attempt takes a batch of an account's outbox with a single statement: the rows are selected `FOR UPDATE SKIP LOCKED`
under the claimable index, oldest `AvailableAt` first, and the same statement stamps `LeaseOwner`, `LeaseExpiresAt`,
and the incremented `AttemptCount` onto them. Skipping locked rows rather than waiting is what makes two passes over
one account claim disjoint sets instead of queueing behind each other, and doing the selection and the stamp in one
statement is what leaves no instant in which a row is chosen but unheld.

What a row has to satisfy to be claimed is stated in the predicate rather than in the code around it: the stage is
`Recorded`, `AvailableAt` has passed, and either nothing holds it or what held it has expired. Every subsequent write
about that record carries the lease it was claimed under and is refused when the row no longer names that owner, which
is what stops an attempt whose lease ran out from recording an outcome over the attempt that took the message from it.
`AttemptTimeout` is configured below `LeaseDuration` precisely so that case stays rare rather than routine.

A lease is released by the attempt that ends: a settled send has no need of one, a deferred send gives it back with
`AvailableAt` moved forward, and a send the host stopped before it transmitted anything gives it back together with the
attempt it had counted. A send whose transmission had begun is the one case nothing releases.

## What an owner asked to be sent again

`recurring_sends` holds one row per message an owner wrote once and asked to be sent again on every occasion a schedule
names. It is a declaration rather than a queue: nothing in it is due and nothing in it is transmitted. Each occasion the
schedule reaches produces an ordinary `outgoing_emails` row with its own identity, its own attempts, and its own ending,
so one Monday's provider outage is not the next Monday's and a message refused for good stops one occurrence rather than
the declaration behind it.

It is a table rather than a section of the deployment's configuration for the reason [configuration is
read-only](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md): what repeats is a message somebody
wrote, at a moment they chose, and it is stopped by them.

| Column | What it records |
|---|---|
| `Id` | The declaration's identity, which every occurrence and every later act names it by. A cascade from it removes the recipients and the draft with it |
| `MailboxAccountId` | The account every occurrence is submitted through and sent as, a plain column for the reason the outgoing record's is |
| `RequesterOrigin`, `RequesterIdentity` | The authored act that asked, in the same vocabulary an outgoing record uses. It is the idempotency identity, so a retried command reads back the declaration it already made instead of doubling what a mailbox sends |
| `Schedule` | The repetition as it was written, in the syntax [a mail rule declares one in](../features/mail-rules.md). It is kept as text and parsed above this table, because the syntax belongs to the dispatch mechanism rather than to the mail domain — and a schedule that does not parse is refused where the declaration is made, so nothing durable states an occasion nothing can resolve |
| `DraftByteLength` | How many bytes of MIME were stored as the draft, kept beside the declaration so listing what repeats never loads a single draft's `bytea` |
| `DeclaredAt` | When the declaration was written down |
| `LastOccurrenceAt`, `LastOccurrenceEmailId` | The occasion this declaration last produced a message for and the message it produced, both null while it has produced none. The first is the occasion's own instant rather than the moment a dispatch noticed it, so a run that was late does not move the declaration off the schedule it declared; the second is what makes one occurrence at a time enforceable rather than assumed — the next occasion asks what became of that message and stands down while it is still queued |
| `CancelledAt` | When the declaration was stopped, null while it still produces occurrences. An instant rather than a flag, because stopping a repetition is an act somebody took and a mailbox that used to send something every week is worth reading in the order it happened |
| `xmin` | The concurrency token, as everywhere else |

A stopped declaration keeps its row. Deleting it would make a repetition somebody ran for a year indistinguishable from
one nobody ever declared, and the row is what an operator reads to see that it stopped and when.

`recurring_send_recipients` holds one row per person every occurrence is offered to, keyed by the declaration and the
position in its list, and carrying the `Address`, the `Role`, and the `ContactId` where the author named a contact. It
is the outgoing recipient table minus the state, and for a reason: a declaration transmits nothing, so nothing is ever
answered about one of its recipients. What the rows hold is the envelope every occasion is built from, in the order the
declaration named them.

`recurring_send_drafts` is the message itself, in the same one-to-one arrangement `outgoing_email_contents` has with
`outgoing_emails` and with the same `DraftMime`, `DraftByteLength`, `Sha256Hash`, and `StoredAt` columns. Every occasion
composes its own message from these bytes rather than reusing them: a repeated `Message-ID` would thread a year of
Mondays as one message in every recipient's client, so each occurrence is re-stamped with an identity and a date of its
own before it reaches the outbox.

## The drafts nothing will send

`mail_drafts` holds one row per message somebody is still writing — a message, rather than the reusable draft a
recurring send is declared with above, which is composed from once per occasion and sent every time. It is a table of
its own rather than a stage on
`outgoing_emails`, because the two records answer opposite questions: an outgoing record is an intent that a delivery
pass is claiming, retrying, and settling, and a draft is a message that has been offered to nobody and may be edited
for a week before it is offered to anybody. Giving a draft a stage on that table would put rows nothing may transmit
into the claim's own predicate, and would make the idempotency identity that stops one authored request being sent
twice mean something for a record that is not being sent at all.

| Column | What it records |
|---|---|
| `Id` | The draft's identity, and what the message, the recipients, and the copies hang on. A UUIDv7 again, so the order drafts were written in is the identifier's |
| `MailboxAccountId` | The account the draft belongs to and whose drafts folder holds its copy. A plain column for the reason an outgoing record's is: an account configured only to send need never have synchronized anything |
| `RequesterOrigin`, `RequesterIdentity` | The authored act that wrote it down, by kind and by text, exactly as an outgoing record carries them. There is no unique index over the pair here: a draft is not idempotent, because saving the same draft twice is what editing it *is* |
| `Revision` | Which version of the draft the row currently describes, counted from one. It is what a copy row is keyed by, so the record and the folder cannot disagree about which version is the standing one |
| `MimeByteLength` | How many bytes the current revision's MIME is, kept beside the record so a promotion can be refused against the deployment's size bound without reading the message |
| `ComposedAt`, `RevisedAt` | When the draft was first written and when it last changed. The second is the order an account's drafts are read in |
| `DiscardedAt` | When the draft was given up, null while it stands. It is written **before** anything is issued against the folder, which is what makes the removal resumable rather than a message nothing can name |
| `PromotedToOutgoingEmailId` | The send this draft became, null until it becomes one. A plain column rather than a foreign key, deliberately: the send outlives the draft, and erasing an outgoing record must not take a draft with it |
| `DivergenceReason`, `DivergenceObservedAt` | Why the tracked copy stopped being provably this deployment's own, and when that was seen. Both null while the record and the folder still follow each other |
| `LastFailureCode` | The code the last attempt to settle the folder ended in, null while none has. The code and not the message, for the reason an outgoing record keeps only the code |
| `xmin` | The concurrency token, as everywhere else |

**A draft has no stage column, and that is the point.** What stage it is at is read from its copies — nothing appended
yet, an append issued and unanswered, the current revision standing, a replacement owed, a removal owed, given up — so
there is no second place for the answer to be written and no window in which a column and the rows beneath it disagree.
The pass that finishes what a stopped process left owing asks that same question in SQL rather than reading a
denormalized flag.

`mail_draft_recipients` holds one row per person the draft names, keyed by the draft and the position in its list, and
carrying the `Address`, the `Role`, and the nullable `ContactId` an outgoing recipient carries for the same reason. It
carries no status and no reply code, because a draft has been offered to nobody. What it carries and an outgoing
recipient does not is `Provenance` — `NamedByCaller`, `ResolvedFromContactBook`, or `DerivedFromAnsweredEmail` — added
by the `AddMailDraftRecipientProvenance` migration and defaulting to `NamedByCaller`, which is the strict reading a row
written before the column existed gets. A send meets the caller-facing governance before its row is written, so nothing
about it has to survive; a draft meets that governance at the promotion, which has only this column to judge whether the
caller chose the address itself. A revision replaces the whole list
rather than amending it, so the row set is the composed message's own rather than everybody the draft was ever
addressed to — and a draft addressed to nobody at all is an empty set here rather than an invalid record, which is what
saving an unfinished message means.

`mail_draft_copies` holds one row per revision that has reached the owner's drafts folder, keyed by the draft and the
revision. **The key is what makes a replacement expressible.** IMAP has no command that changes a stored message, so a
new version is a new message beside the old one and the old one is removed afterwards — and between those two commands
the folder holds two copies that both belong to this draft. Keying by revision is also what makes the append idempotent
without a read-then-write: appending one revision twice is refused by the key rather than by a check two passes can
both pass.

| Column | What it records |
|---|---|
| `MailDraftId`, `Revision` | The draft and which of its versions this copy is, which together are the key. A cascade from the draft removes them with it |
| `FolderAlias`, `FolderPath` | The folder the copy was appended to, as the alias an operator wrote and the path that alias named at the time. The path is what a later removal compares against, so a role repointed since the append names another folder and is left alone |
| `Stage` | `Issued` before the command went out, `Standing` once the server answered, `Withdrawn` once the copy was taken back out, `Abandoned` where it is one nothing will touch again |
| `PlacementUidValidity`, `PlacementUid` | Where the server said it put the copy, both null on a server that advertises no RFC 4315 `UIDPLUS`. **The only occurrence a removal ever names is one of these**, which is what makes a draft the owner wrote in their own mail client unreachable by construction rather than refused by a check |
| `InternetMessageId` | The identity the appended bytes carry, minted per revision because two messages sharing one is what a client reads as one message it has seen twice |
| `AppendedAt`, `SettledAt` | When the append was issued, and when the copy stopped being one anything acts on |
| `xmin` | A token of its own rather than the draft's, because a copy is confirmed and withdrawn without the draft above it changing — and what that decides is whether a message is left in somebody's folder |

**A row at `Issued` is the same undecidable window an outgoing filing has, and it is left undecided for the same
reason.** Nothing appends that revision again on the strength of it, and nothing moves it forward either. `Abandoned`
is the other half of that discipline read from the folder's side: where the tracked copy stops being provably this
deployment's own — the role resolves elsewhere, the folder was recreated, the server named no placement, an append was
never answered — the message is left exactly where it is, the row says nothing will touch it again, and the reason is
written onto the draft. A draft this system did not create is never among them, because nothing here reaches a UID any
way but through a copy row an append of its own wrote.

`mail_draft_contents` is the message itself, in the same one-to-one arrangement the two other content tables have with
their records. It is the one raw-MIME row in this schema that is **rewritten** rather than written once: a send's
payload is fixed because a retry has to transmit the bytes an earlier attempt may already have begun transmitting,
while a draft's payload is what its author is still editing, and keeping a row per revision would hold a message per
keystroke for as long as the draft lives. The cascade is the erasure obligation — deleting the draft destroys the
message, the recipients, and the copy rows with it, so nothing about a draft can outlive the record that says whose it
is.

**A draft is derived personal data and carries the classification of the mail it was written from.** It is a message
addressed to people, composed in part from mail this deployment holds, and the retention and erasure that reach an
account reach these four tables through the same cascade every other table is reached by.

## Indexes

| Index | Columns | Purpose |
|---|---|---|
| `ix_stored_emails_folder_uidvalidity_uid` | `(mail_folder_id, uid_validity, uid)`, unique | Remote occurrence identity, which is what makes synchronization idempotent |
| `ix_stored_emails_account_timeline` | `(mailbox_account_id, received_at DESC NULLS LAST, id DESC)` | The account-wide timeline |
| `ix_stored_emails_folder_timeline` | `(mail_folder_id, received_at DESC NULLS LAST, id DESC)` | The per-folder timeline |
| `ix_stored_emails_awaiting_content` | `(mail_folder_id, uid_validity, uid)` over the rows whose `content_availability` is `AwaitingStorageHeadroom` | The queue of occurrences stored without their payload, which every folder run reads once. The filter is what keeps the index proportionate to that queue rather than to the mailbox: on a deployment that has never reached its storage ceiling the index is empty, and the read costs nothing instead of walking a folder's whole occurrence index to discover that no row qualifies |
| `ix_stored_emails_account_identity` | `(mailbox_account_id, id)` | The order a whole-mailbox rule run walks an account's mail in. The identity rather than the timeline, because a walk that has to resume needs a total order no later write disturbs and a position that is one column rather than a nullable timestamp paired with a tie-breaker |
| `ix_stored_emails_awaiting_rule_evaluation` | `(mailbox_account_id, id)` over the rows whose `rules_evaluated_at` **and** `FiledFromOutgoingEmailId` are both null | The queue of mail no rule pass has evaluated, read once per account run. The filter is the point: in steady state almost every row of an account has been evaluated, so without it the read would walk the account's whole index to find the handful that qualify, on every run of every account. The second clause is what keeps a copy of this deployment's own outgoing mail out of that queue permanently — such a row is never stamped as evaluated, because it never was, so excluding it anywhere else would leave it at the head of the queue for good |
| `ix_stored_emails_thread` | `(EmailThreadId, Id)` | One conversation's messages, in the total order a read assembles them from. The identity is in the key because the order a conversation is published in is computed rather than stored, and the read needs a stable one to bound and page the raw set by |
| `IX_stored_emails_ParentStoredEmailId` | `(ParentStoredEmailId)` | The self-referencing key back to the message a reply answers, which is what erasing a message reaches its replies by rather than scanning |
| `pk_email_thread_identifiers` | `(MailboxAccountId, IdentifierHash)`, unique | Which conversation an identifier belongs to, which is the question every arriving message asks once per identifier it names. Uniqueness is also the race: two messages binding one identifier at once leave one writer to retry against what the other wrote |
| `ix_email_thread_identifiers_thread` | `(EmailThreadId)` | The identifiers a merge has to repoint at the surviving conversation |
| `IX_mailbox_accounts_OwnerId` | `(OwnerId)` | Which mail accounts one owner owns, which is the one read this column has: erasing an owner asks it before taking the rows no cascade reaches |
| `IX_email_threads_MailboxAccountId` | `(MailboxAccountId)` | The key back to the owning account, which is what erasing one reaches its conversations by |
| `IX_email_threads_MergedIntoEmailThreadId` | `(MergedIntoEmailThreadId)` | The foreign key from a folded conversation to its survivor. EF Core indexes it because the constraint is checked from the other side too: erasing an account cascades to its conversations, and each delete asks whether any row still names the one going |
| `ix_stored_emails_sender` | `(sender_normalized_address)` | Filtering by who sent a message |
| `ix_stored_emails_to_addresses` | `(to_addresses)`, GIN | Containment tests over the `To` recipients |
| `ix_stored_emails_cc_addresses` | `(cc_addresses)`, GIN | Containment tests over the `Cc` recipients |
| `ix_stored_emails_reply_to_addresses` | `(reply_to_addresses)`, GIN | Containment tests over the `Reply-To` addresses |
| `ix_stored_emails_remote_keywords` | `(RemoteKeywords)`, GIN | Containment tests over the keywords a server reported |
| `ix_email_search_documents_search_vector` | `(search_vector)`, GIN | Lexical search over subject, participants, and body text |
| `ix_email_chunks_email_ordinal` | `(StoredEmailId, Ordinal)`, unique | One message's passages in reading order, and the constraint a re-cut cannot write an ordinal twice past |
| `ix_embedding_profiles_identity_fingerprint` | `(IdentityFingerprint)`, unique | One row per vector space, which is what makes activation idempotent |
| `ix_embedding_profiles_lifecycle_state` | `(LifecycleState)`, unique, where the state is building or active | At most one generation being built and at most one being read |
| `ix_email_embeddings_profile` | `(EmbeddingProfileId, Dimension)` | Reading a whole generation, which is how a superseded one is removed |
| `ix_mailbox_mutations_identity` | `(MailFolderId, UidValidity, Uid, RequesterOrigin, RequesterIdentity, Mutation)`, unique | A mutation's idempotency identity, which is what makes the same request twice perform one change |
| `ix_mailbox_mutations_outstanding` | `(MailboxAccountId, RecordedAt)` where the stage is not `Completed` | The changes an operator asks about: those in flight and those given up on |
| `ix_mailbox_mutations_placement` | `(MailboxAccountId, DestinationFolderPath, PlacementUidValidity, PlacementUid)` where `PlacementObservedAt` is null | The question the forward pass asks of every batch it discovers: is one of these UIDs where a relocation or a copy put an email |
| `ix_mailbox_mutation_audit_entries_mutation` | `(MutationRecordId)`, unique | One audit entry per mutation ending, whatever a repeated append attempts |
| `ix_outgoing_emails_identity` | `(MailboxAccountId, RequesterOrigin, RequesterIdentity)`, unique | An outgoing message's idempotency identity, which is what makes the same authored request twice one delivery. It spans terminal rows deliberately: a row that was sent is what stops the same request asking again |
| `ix_outgoing_emails_claimable` | `(MailboxAccountId, AvailableAt, Id)` where the stage is `Recorded` | The batch a delivery pass claims, oldest first. The filter is what keeps it proportionate to the outbox rather than to everything the deployment has ever sent: a claim reads only rows nothing has transmitted for, and in steady state that is almost none of the table. The identity is in the key because two sends recorded in one instant need a total order for the claim to be deterministic |
| `ix_outgoing_email_filings_placement` | `(MailboxAccountId, FolderPath, PlacementUidValidity, PlacementUid)` where `ObservedAt` is null and the stage is `Confirmed` | The question every synchronized batch asks: is one of these UIDs a copy this deployment filed. The filter is both halves of what that join can match, which is what keeps the index the size of the copies not yet seen coming back rather than of everything ever sent — a mirror withdrawn before any run saw it, and an append the server never answered, are rows nothing will ever match again |
| `ix_outgoing_email_filings_message_id` | `(MailboxAccountId, InternetMessageId)` where `ObservedAt` is null and the stage is `Confirmed` | The same question on a server that advertises no `UIDPLUS` and therefore named no placement, answered by the identity in the appended bytes, and bounded the same way |
| `ix_outgoing_emails_outstanding` | `(MailboxAccountId, RecordedAt)` where the stage is none of `Sent`, `Refused`, or `Cancelled` | The outbox a restart reads and an operator asks about: what is queued, what is in flight, and what has stopped. The filter names the three terminal stages rather than the successful one alone, so a refused send stays visible while the deployment's whole sending history does not |
| `ix_outgoing_emails_period_usage` | `(RecordedAt, MailboxAccountId)` | What a send ceiling counts: the messages one period was asked for, for one account and for the whole deployment. It carries no filter, because a period counts every message it was asked for whatever became of it, and the account is behind the instant rather than in front of it so one index answers both questions — the deployment's count reads a range of it and an account's count reads the same range narrowed |
| `ix_recurring_sends_identity` | `(MailboxAccountId, RequesterOrigin, RequesterIdentity)`, unique | A declaration's idempotency identity, which is what makes the same authored act twice one declaration. It spans stopped rows deliberately: a declaration that was stopped is what keeps the act that made it from quietly declaring a second one |
| `ix_recurring_sends_active` | `(DeclaredAt)` where `CancelledAt` is null | The declarations a dispatch pass reads, oldest first. The filter is what keeps that read proportionate to what still repeats rather than to every repetition the deployment has ever declared |
| `ix_mail_drafts_account_revised` | `(MailboxAccountId, RevisedAt)` | An account's drafts in the order the pass settles them, oldest change first. No filter, because the read that uses it is already narrowed by the copy predicates beneath it and a deployment's whole set of held drafts is what an owner is editing rather than a history that accumulates |
| `ix_mail_drafts_promoted` | `(PromotedToOutgoingEmailId)` where it is not null | Two questions with one answer: which draft, if any, a finished delivery came from, and which promoted drafts a pass still has to give up. Both read the same rows, and the filter is what keeps the index the size of the drafts that have been promoted rather than of every draft ever written, since a draft that was never promoted can never answer either |
| `ix_mail_rule_executions_account_evaluated` | `(MailboxAccountId, EvaluatedAt, Id)` | An account's rule history newest first, and the retention pass that erases what has outlived its window. The identifier is in the key because two executions of one batch share an instant and a keyset page needs a total order to continue from |
| `ix_mail_rule_executions_account_rule_evaluated` | `(MailboxAccountId, RuleName, EvaluatedAt, Id)` | What one rule has been concluding, which is the question a rule that never seems to fire is investigated with |
| `ix_mail_rule_executions_email_evaluated` | `(StoredEmailId, EvaluatedAt, Id)` | Why one message is where it is, and the rows the cascade removes when that message is erased |
| `ix_mailbox_mutation_audit_entries_account_completed` | `(MailboxAccountId, CompletedAt, Id)` | The two ways the trail is worked: a keyset-paginated page of an account's history, and the retention pass that erases what ended before a cutoff |
| `ix_mail_answering_audit_entries_run_account` | `(RunId, MailboxAccountId)`, unique | One entry per run per account, whatever a repeated append attempts |
| `ix_mail_answering_audit_entries_account_completed` | `(MailboxAccountId, CompletedAt, Id)` | The same two readers the trail above has: a keyset-paginated page of an account's runs, and the retention pass beside it |
| `IX_mail_answering_audited_emails_StoredEmailId` | `(StoredEmailId)` | The foreign key back to the message, which is what makes erasing one reach the runs that read it without scanning the table |
| `ix_email_spam_classification_signals_classification_ordinal` | `(StoredEmailId, Ordinal)`, unique | One classification's signals in the order the stages produced them, and the constraint a replaced record cannot write an ordinal twice past |
| `ix_contacts_owner_display_name_sort_key_id` | `(OwnerId, DisplayNameSortKey, Id)` | The one order the contact book is listed in and the one a keyset page continues from. The owner leads it because a page is always of one person's book, and the identity settles two people whose names compare equal, which is what makes the order total within a book and the walk terminate |
| `ix_contact_addresses_owner_normalized_address` | `(OwnerId, NormalizedAddress)`, unique | One address in one person's hands within one owner's book. It is also what the lookup from an address to a person is served from, rather than a scan, and leading with the owner is what makes that lookup read one book rather than the table |
| `IX_contact_addresses_ContactId_OwnerId` | `(ContactId, OwnerId)` | The foreign key back to the person, which is what erasing one reaches their addresses by. It carries the owner because the key does |
| `ix_jobs_identity` | `(JobType, IdempotencyKey)`, unique | A job's idempotency identity, which is what makes the same execution enqueued twice one job. It spans terminal rows deliberately: a row that succeeded is what stops the same trigger asking again |
| `ix_jobs_claimable` | `(JobType, TurnAt)` where the state is `Pending` or `Claimed` | Both of the queries this table runs at any volume: the claim, and the queue-depth check every enqueue makes. The second column is the order the claim drains the queue in, which is what the fairness across owners is. The filter keeps the index the size of the backlog rather than of the queue's whole history, and the claim repeats that same membership in its own predicate so PostgreSQL can prove the index applies to it rather than having to derive it through a disjunction. It names the two claimable states rather than excluding the terminal ones, so a job that reaches a terminal state leaves the index whichever one it reaches. The depth check reads the same leading column and rechecks `Pending` against the heap, because the index carries both claimable states rather than that one; what keeps that cheap is the bound on the read rather than the index |
| `ix_jobs_account` | `(MailboxAccountId, EnqueuedAt)` | An account's queued work, which is what erasure and any per-account bound reach a job by |
| `ix_jobs_account_turn` | `(MailboxAccountId, TurnAt)` where the state is `Pending` or `Claimed` | How far one mailbox's waiting work has reached, which is what every enqueue asks of each of the owner's mailboxes before it stamps a turn. Beside `ix_jobs_account` rather than folded into it because the two are proportional to different things: that one spans everything the queue has ever done, and this one only what is still claimable, which is what keeps the read a backward walk of a few index entries on a queue holding a backlog of any size |
| `ix_jobs_dead_lettered` | `(StateChangedAt, Id)` where the state is `DeadLettered` | The operator's reading of what has stopped, newest first, keyed on the pair it pages by. The filter keeps the index the size of what is waiting for a person rather than of the table, and a row leaves it the moment the decision about it is taken |

The recipient, keyword, and search-vector indexes are GIN rather than B-tree because all of them serve containment tests. A B-tree over an array column serves only equality against a whole array, and over a `tsvector` it serves nothing search asks for; a GIN index is what turns either into an index scan.

No partial index over remotely deleted messages exists, and its absence is deliberate; one waits for the remote-expunge reconciliation that introduces the state it would filter on. The absence of any index over `Embedding` is deliberate in a stronger sense, and permanent.

### The vector index that is not there

Nothing indexes `Embedding`. A semantic search measures every eligible message's nearest passage against the query, which is exact and linear in the number of vectors it reads; there is no approximate path beside it, and a profile's activation builds nothing in the database.

That is a measured decision rather than an omission. An approximate index over `Embedding` covers one width and one generation, so it could only ever have been a partial index per profile, built and dropped as a profile's lifecycle asked — and on a hundred-thousand-message mailbox at 1536 dimensions the ranking query never chose one, an approximate window kept a median of four of its fifty results once the caller's own folder and date filters were applied and none once they named a sender, while the index cost 3073 MB beside a 3137 MB table, three quarters of an hour of blocked writes to build, and a hundredfold on every embedding insert. [What a semantic search costs](semantic-ranking-cost.md) holds the measurement and what it decided.

Two things follow for this table. `ix_email_embeddings_profile` is the only index it carries beyond its keys, and it earns that on a different query — reading a whole generation in bounded batches when a superseded one is removed. And MailFathom now changes the schema nowhere outside the artifact an operator applies, which is what [Applying the database schema](../operations/database-schema.md#every-index-is-in-the-script) states for a deployment that separates its migrating role from its serving one.

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

`embedding_spend_periods` holds no personal data either, and its shape is what makes that true rather than a claim about it: a character count, an instant, and a generated owner identifier say how much a deployment spent, for whom, and when, and none of them names a message, a passage, or a vector. That is also why it outlives what it recorded — nothing cascades into it, not even from the owner record, and erasing the mail a period paid to embed leaves the record that the period was paid for.

`owner_stored_content` is read the other way round, and the difference is what its cascade records. A byte count against an owner names no message either, but what it describes is how much mail that person holds rather than what this deployment spent, so it carries the classification of the mail it counts: it cascades from `settings_accounts`, and erasing an owner takes the figure with everything else derived from their mail. Nothing in it reaches a log, a metric, a trace, or an exception message beyond the owner's own generated identifier.

`email_spam_classifications` and `email_spam_classification_signals` are derived personal data of the same kind as a
chunk or a vector: what was concluded about somebody's mail, and from which of its headers. They inherit the source
message's classification, retention, access, export, and erasure obligations whole, and the cascade from `stored_emails`
is what makes that structural rather than a pass somebody has to remember. A signal names a header field, an
authentication outcome, or a rule — never the value of a header — and nothing in either table reaches a log, a metric, a
trace, or an error message.

`jobs` is derived personal data by the same reading as a chunk or a classification: a row says that something is to be done about somebody's message, and it points at that message by its occurrence identity. What keeps it a pointer rather than a copy is the payload contract — a document of references with no property a subject, an address, or a body could go in, bounded in size at the enqueue boundary so a payload that grew into a copy is refused instead of stored. The account column and the cascade from `mailbox_accounts` are what erasure reaches queued work by. The message the payload names is deliberately not a foreign key: the identity in the document is the remote occurrence rather than the local row, so there is nothing for a constraint to point at, and reaching the message is a lookup by that identity like every other read of it.

`job_schedules` holds no personal data either, for the reason `embedding_spend_periods` does not: an identity composed of MailFathom's own configured names, two instants, and the identifier of a job say when a recurring dispatch last acted and on which occasion, and none of them names a message. That is also why nothing cascades into it — erasing an account's mail says nothing about when its rules are due to run again.

`mail_rederivation_positions` and `mail_rederivation_runs` hold no personal data either, and for the same reason `job_schedules` does not: an account alias, a folder alias, the local identifier of the last message a batch read, counts, and instants say how far an operator's refresh has come, and none of them is anything a message supplied. Nothing cascades into either, which is deliberate rather than an omission — a walk's position is about work an operator started, and erasing the mail behind that position leaves a cursor that the next batch simply steps past and counts that still describe work that was really done.

`contacts` and `contact_addresses` are the most concentrated personal data on this page: a name, the addresses somebody uses, and a note about them are an assembled record about an identified third party rather than mail that arrived. An asserted row is derived from nothing at all; a collected one is derived from a message, and still nothing cascades into either, because what a collected row holds is a claim that the owner corresponds with somebody rather than a copy of the message that named them — erasing that message says nothing about whether they still do. That is also why neither has a retention window: a contact is held until somebody erases it, and erasing one removes the person and every address row through the cascade above rather than marking either. What the collected half adds is an erasure of its own, which takes every row of that origin and leaves what the owner asserted exactly where it was. Erasing the owner takes the whole book: `contacts` keys onto `settings_accounts` and `contact_addresses` keys onto `contacts`, so the addresses are reached through the person holding them. Nothing in either table reaches a log, a metric, a trace, or an error message; the contact identifier and the owner's are what a failure names, and they are the two columns that are not personal data.

`outgoing_emails`, `outgoing_email_recipients`, and `outgoing_email_contents` are derived personal data of a kind
nothing else on this page holds: an outgoing record says who this mailbox's owner wrote to and when, and the recipients
it names are people other than the owner. They are stored because a send cannot be resumed without knowing who is still
owed it, and the columns above are the minimum that supports that — which is why a recipient's display name is not among
them, and why the record carries no subject, no body, and no header of its own. The message stays in
`outgoing_email_contents` and is reached by identifier, so listing the outbox, advancing a stage, or answering about a
recipient never loads it. The two cascades from `outgoing_emails` are what make erasure structural: deleting the
record destroys the recipients and the stored message with it, so an outgoing message cannot outlive the record that
says who it was for. A recipient's `ContactId` does not widen any of that: it is the one column of the three that is not
personal data, it is the same identifier a failure is allowed to name, and it points at `contacts` without a constraint
so erasing a contact leaves the send saying who it went to rather than rewriting it. Nothing in any of the three reaches
a log, a metric, a trace, or an exception message — an exception about a recipient names the record, and the position
where the row it was reading has one, rather than the address.

`recurring_sends`, `recurring_send_recipients`, and `recurring_send_drafts` inherit every word of that and add one fact
of their own: a declaration says not only who this mailbox's owner writes to but how often, which is a statement about a
relationship rather than about a message. They are classified, retained, exported, and erased exactly as the three above
are, and the same two cascades make erasure structural — deleting a declaration destroys its recipients and its draft
with it. Nothing in any of them reaches a log, a metric, a trace, or an exception message either; a failure about a
declaration names the declaration, and the schedule it carries is an operator's own phrase rather than anybody's data.

`mail_drafts`, `mail_draft_recipients`, `mail_draft_copies`, and `mail_draft_contents` are read exactly as the three
tables above are, and for a stronger reason: a draft is a message the owner is writing to somebody, and one drafted as
an answer to stored mail is composed in part from that mail. It is derived personal data of the same classification as
the message it came from and carries the same retention, access, export, and erasure obligations, which the cascades
from `mail_drafts` make structural — erasing the draft destroys the recipients, the copy rows, and the stored message
in one statement, and erasing the account reaches every draft of it. The copy rows are the one part that says anything
about a mailbox rather than about a message, and what they hold is a folder alias, a path, a UID, and an identity
MailFathom minted. Nothing in any of the four reaches a log, a metric, a trace, or an exception message: a failure
names the draft's identifier and the folder alias, never a subject, an address, or a line of what was written.

`settings_accounts` is personal data of a kind nothing else on this page holds: it is the record of a person rather than of their mail, and its document is what they configured MailFathom to do on their behalf. It is therefore the row a data-subject erasure is aimed at, and the cascade beneath it is what discharges the rest — which is also why the tables that record a mail account without keying onto one are taken by the same operation rather than left for somebody to remember. The contact book is reached by the cascade rather than by that derived list: `contacts` and `contact_addresses` record no mail account, but `contacts` keys onto the owner directly and `contact_addresses` keys onto `contacts` through `(ContactId, OwnerId)`, so erasing the owner takes the whole book with them in two hops — which is what an erasure request owes about an assembled record of third parties this owner wrote down. One row does stand outside, and for a reason of its own: the statements over the tables that record a mail account without keying onto one name the owner's accounts by subquery, so the sealed refresh token of an account this deployment authorized and never synchronized has no `mailbox_accounts` row to be found through and survives. An erasure request against the owner therefore discharges the rest of this page and leaves that one token to be answered separately. Nothing in the row reaches a log, a metric, a trace, or an exception message: a failure names the owner's identifier, which is a value MailFathom generated, and never the document beside it.

`embedding_profiles` is the exception on this page: it holds no personal data at all. It describes a model, and the credential that reaches that model is configuration rather than a column here, so nothing in this table is a secret or is derived from anybody's mail.

## How this schema reaches a database

One reviewed migration, `Initial`, creates all of it, and the migrations appended since add to it. One of them writes a row as well as tables: `AddOwnerAccounts` provisions the owner every mailbox is bound to, carries the mailboxes an upgraded deployment already holds onto that owner, and does neither again on a second apply. There is no bootstrap that builds the schema from the model at startup any more: the host reads the migration history, and refuses to start when the database has not applied every migration the running build defines.

Locally the AppHost's `mailfathom-migrations` resource applies it before the host starts. Elsewhere applying it is an explicit deployment step. [Local development](../operations/local-development.md) documents both. Every migration is permanent: a model change appends one and never regenerates this baseline, and the `add-migration` skill is that workflow.

Not everything in this database is mail. `mailbox_refresh_tokens` holds one sealed OAuth refresh token per account, added by the `AddMailboxRefreshTokens` migration, and it is documented where the credential it holds is — [mailbox OAuth](../operations/mailbox-oauth.md#rotation). It is named here so a reader of a schema dump knows which page owns it, and because nothing on this page cascades into it: it carries no foreign key onto `mailbox_accounts`, since a token has to be able to exist for an account that has never synchronized. It is one of the ten tables in that position, which is why [erasing an owner](#the-owner-a-mailbox-belongs-to) takes each of them by a statement of its own — and it is the table where the absent foreign key also bounds what that statement reaches. The delete names the accounts the owner holds a `mailbox_accounts` row for, so a token stored for an account that was authorized and has never synchronized has no such row and is not taken with the owner.

`uid_validity` and `uid` are modelled as CLR `uint` because that is the IMAP wire type, and PostgreSQL has no native unsigned 32-bit integer. The generated migration maps both to `bigint`, which represents the whole unsigned 32-bit range exactly, so the unique index on `(mail_folder_id, uid_validity, uid)` and the checkpoint comparisons order the same way the IMAP values do.

Table names are the snake_case ones above. Column names are not: the model renames tables and leaves columns as it names the properties, so the physical columns are `"UidValidity"`, `"ReceivedAt"`, and so on, and hand-written SQL against them has to quote that casing. The names in this page are the schema's concepts rather than a transcription of the DDL; the migration is the transcription.

## What the integration suite proves

Every claim on this page that is a claim about PostgreSQL rather than about the model is verified by `backend/tests/IntegrationTests` against the orchestrated server, because a unit test cannot reach any of them. The classes involved carry `[RequiresIntegrationCoverage]` for exactly that reason, and [local development](../operations/local-development.md) describes how the suite runs.

- Erasing an owner leaves no row naming a mailbox of theirs the database holds an account row for. The refresh token of an account that has only ever been authorized is neither arranged by the test nor reached by the statement, for the reason the paragraph on `mailbox_refresh_tokens` above gives. The claim is made against every table the model says records a mail account rather than against a list written beside it, so a table added later is counted by the same assertion, and the count taken before the erasure is asserted to hold no zero — a table nothing seeded would otherwise let the test pass while proving nothing about it. What survives is asserted the same way: a second mailbox is stored under the owner the rest of the suite's mail hangs on, with rows on both sides of the erasure — three tables the cascade reaches and three it does not — and every count beneath it is unchanged afterwards, down to the raw MIME of its message. Both owners are given a contact as well, which is the one part of the record that names no mail account at all and is therefore invisible to every count above: the erased owner's person and address row are gone, and the other owner's are where they were.
- The account row a first folder binding creates carries the owner the database already held, rather than one the run minted on its way past. Both refusals are proved beside it: a deployment holding a second owner record and one holding none each refuse the binding and say which of the two they were, arranged inside the binding's own transaction and rolled back with it.
- A mailbox stored before the owner axis existed is carried onto the owner the same script provisions. The chain is applied in two parts against a database of its own — up to the migration before `AddOwnerAccounts`, then the whole artifact — with the mailbox row written between them, which is the state an installation of the previous release is in on the day it takes this one and the only state that exercises the filling step at all.
- The baseline migration applies to an empty database and leaves no migration pending, and the text search configuration the generated column was built with is read back out of PostgreSQL's own catalogue rather than from the model — which is what lets the startup gate refuse a database whose lexical index disagrees with the running host.
- The unique index refuses a duplicate occurrence that neither writer could have seen, which is the PostgreSQL-side half of idempotent synchronization: two overlapping runs each stage an insert, and the database rather than the application decides that only one lands.
- The same holds for an outgoing message's identity, where losing the race is the mechanism rather than an accident: two callers asking for one send each stage an insert, the index refuses the second, and enqueuing the same authored request twice leaves one record carrying the message the first enqueue stored rather than a recomposed one. A send left at `TransmissionBegun` is found by a later scope reading the account's outbox and reads as undecidable there, a recipient the message reached is never offered again after a partial acceptance, and deleting the record erases the stored message and the recipients with it.
- An occurrence identified by the largest UID IMAP can hand out round-trips through its `bigint` columns unchanged.
- Raw MIME round-trips through `bytea` with its recorded length and SHA-256 intact, including a payload large enough that PostgreSQL stores it out of line, and re-storing an occurrence replaces the one existing row rather than reading its payload back into memory.
- The transaction a persistence session opens covers SQL the provider had already executed: a set-based update issued and then abandoned without a commit leaves the earlier payload in place.
- A losing writer is reported rather than raised where the constraint says a race happened — a second binding of the same alias generation, and a stored email whose `xmin` token another committed transaction made stale — and is raised where it says the data is already there.
- The timeline indexes return rows in the order `EmailTimelinePosition.NewestFirst` describes, over data with shared and absent timestamps, and a keyset walk over that order visits every row exactly once. The `uuid` tiebreaker is the part only a server can settle.
- The [mailbox listing read model](../features/mailbox-queries.md) issues that walk over the same data in both directions and gets the same order back, every one of its filters translates to SQL — including the array containment a recipient filter needs and the escaped pattern a subject fragment needs — and its projection leaves the change tracker empty. A predicate that does not translate is a runtime failure rather than a compiler error, which is why the read model's queries belong here as well as in the unit suite.
- The generated search vector is computed by PostgreSQL from the subject, participants, and body beside it; the GIN index serves the query shape search issues; and query text carrying SQL statements and `tsquery` operators is read as words, matching documents whose text holds those words and leaving the table intact.
- A stored vector cannot disagree with its profile: a vector whose length differs from the `Dimension` beside it, and a `Dimension` the named profile never declared, are both refused at the write, while the matching width is stored. Two profiles of different widths coexist in the one dimensionless column, re-registering a geometry already present is refused by the fingerprint index, and deleting a message erases the vectors derived from it while the profile they named survives.
- The [search read model](../features/email-search.md) composes that vector, `websearch_to_tsquery`, `ts_rank`, and `ts_headline` into commands PostgreSQL accepts — a malformed headline option list is a runtime failure rather than a compiler error — ranks the window it returns, cuts snippets inside the configured bounds, and leaves the change tracker empty across every query it issues.
- Both guarantees the job store gets from PostgreSQL rather than from its own code: two callers racing to enqueue one execution produce one job, because the unique index refuses the second insert; and two workers claiming at the same moment take different jobs, because the claim selects and stamps under `FOR UPDATE SKIP LOCKED` in one statement. A lease that has run out is reclaimed with a second attempt counted, the attempt it was taken from writes nothing afterwards, a completed job keeps the key that refuses the same execution again, and a row whose type this build does not declare is left where it is. A dead letter is claimed by nothing and keeps the key and the recorded failure that ended it, a scheduled retry holds the job back until the instant it named, and a release hands the attempt back with the job. And the claim is fair across owners: against one owner's backlog beside another owner's single due job, a claim bounded to two hands back one of each rather than two of the backlog.
- A schedule's durable state is written by one statement whether the row is there or not: a schedule seeded and then advanced leaves one row carrying the latest occasion, and a schedule nothing has written is absent from the read rather than answered with an empty state — which is the difference between seeding once and seeding on every pass.
- The contact book's eleven claims about PostgreSQL rather than about the model: erasing a person removes every address row through the cascade and frees those addresses for somebody else, two overlapping writers claiming one address leave the loser with a conflict rather than a provider failure, a keyset walk of the book serves every contact exactly once in the order the index is built in — which is also the only place the comparison translates to SQL at all — an amendment that stops naming an address deletes its row and releases it, a search selects the people whose stored name key or whose address key contains the text, which is the one contact predicate that reaches both tables at once and the one no index above answers, erasing the collected origin removes one owner's rows of it and every address row hanging off one in two set-based statements no change tracker ever sees, the threshold collection is held to counts what one address wrote out of the stored mail in a query whose two halves read a message stored in several folders as one message, two owners each recording the same correspondent both succeed because the unique index is over the owner and the address rather than over the address, a page of one owner's book is planned through the owner-leading index rather than scanned, at a volume where a sequential scan was the alternative, and a person only another owner wrote down is answered for by none of the four reads answered in batches — not by name, not by address, and not by identity — while the same four under the owner who holds them answer with them, and giving up on collection takes only the book it was asked of — the other owner's collected person and the address row beneath them survive it.
- A draft's whole life runs against the real database and the real mail server at once: it is written, appended to the folder playing the drafts role, revised, promoted, and given up when the send it became is delivered. What only PostgreSQL settles there is that the record and its message cross one transaction, that a revision rewrites the one payload row rather than adding a second, that the copy rows keyed by revision are what an edit's append and removal are read from, and that removing the draft takes its recipients, its copies, and its message with it.
- The same read model's vector half ranks the eligible mail by a correlated minimum over each message's own embedded passages, measured by the operator the active profile's metric names. Mail carrying no vector under that profile is absent from the ranking rather than distant, and the ordering is the part only a server can settle: whether the distance operator, the aggregate, and the caller's filters compose into one statement at all is a translation question rather than a compile-time one.
