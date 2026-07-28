# Stored email schema

`stored_emails` holds the normalized metadata a mailbox timeline is read from. Its raw MIME lives in a separate one-to-one table, `email_message_contents`, and the text derived from that MIME lives in a third, `email_search_documents`, so nothing that lists or filters mail ever loads a `bytea` value, a body's worth of text, or a search vector — let alone tracks one in the change tracker.

This page describes the table as the EF Core model declares it today. No migration exists yet — [specification 19](../../specs/19-ef-core-migration-baseline-and-apply-policy.md) generates the reviewed baseline — so the schema a developer runs against comes from the Development-only bootstrap described at the end of this page, and PostgreSQL has not yet had the chance to disagree with any of it.

## What a row records

The columns fall into five groups, each answering a different question.

**Occurrence identity.** `mail_folder_id`, `uid_validity`, and `uid` are the stable remote identity of one message in one folder, and `id` is the local UUIDv7 that every other table references. `mailbox_account_id` is a copy of the owning folder's account: the account timeline index leads with it, and an index cannot span a join. Nothing repoints a folder at another account, so the copy is written with the row and never revised.

**What the server reported.** `internet_message_id`, `subject`, `sent_at`, `size_octets`, and `content_availability` come from the envelope the IMAP server returned. For a message whose raw MIME was never fetched — one that exceeded the configured size limit — these are the only fields the row will ever carry.

**What the stored MIME said.** `received_at`, the sender columns, the recipient arrays, the thread columns, and the attachment summary are read out of the raw MIME that this deployment actually stored. When that read succeeds it also replaces `subject` and `sent_at`, so one row stays consistent with one set of bytes rather than mixing two parsers' answers. `internet_message_id` is the exception: a message that carried no `Message-ID` keeps the identifier the envelope reported instead of losing it.

**The remote flag snapshot.** `remote_flags_observed_at` and the five boolean markers record what the server last said about `\Seen`, `\Answered`, `\Flagged`, `\Draft`, and `\Deleted`. Nothing writes them yet; [specification 10](../../specs/10-remote-expunge-and-flag-reconciliation.md) introduces the reconciliation pass that does, so every row currently carries the never-observed value. The timestamp exists because no combination of the booleans can distinguish "the server reports none of these" from "nobody has looked yet". The snapshot is an observation only — MailMcp reads mail read-only, and no application path turns any of these into an IMAP `STORE`.

**Concurrency.** `ConcurrencyVersion` maps onto the PostgreSQL `xmin` system column rather than a column of its own, so PostgreSQL maintains the token and no writer has to.

### Sender and recipients

The sender is stored as three columns: the display name and address as the message wrote them, and the upper-cased comparison form that every filter and index matches on. The `From` header supplies it. `Sender` is the fallback and only stands in for a message that named no author at all, because it names whoever submitted a message written on someone else's behalf and therefore answers a different question.

Recipients are PostgreSQL `text[]` columns — `to_addresses`, `cc_addresses`, `reply_to_addresses` — rather than a join table, because every planned query tests containment rather than joining to recipient rows. They hold the comparison form only. A recipient's display name is mail content that no planned query filters or sorts on, and a second copy of it would widen the access, export, and erasure surface for nothing; a reader that needs the names re-derives them from the stored raw MIME, which [specification 14](../../specs/14-email-content-read-model.md) parses anyway.

### Bounds on what a header may contribute

Nothing between the mail server and a row bounds a header's length or how many addresses it names. The MIME reader bounds a message's *structure* — part count and nesting depth — but not the width of a single header, so the persistence mapping applies its own ceilings: 320 octets per address, 998 per message identifier, 256 addresses per recipient array, and 64 thread ancestors.

A value over a ceiling is **dropped, not truncated**, and the row keeps the rest. Both halves of that are deliberate.

Letting the value through would be worse than losing it. The column would reject the write, the retry budget would run out, the folder checkpoint would never advance past the message, and every later run would stop on the same one — one malformed header would halt synchronization of the folder behind it.

Truncating would be worse still. A prefix of a message identifier is an identifier another message may legitimately carry, so a truncated one would assemble a thread out of unrelated conversations, and a truncated address would name a mailbox nobody wrote. The columns are a filter index over what a message said, not a second copy of it; the complete headers stay in the raw MIME that specification 14 reads.

Where a bound cuts a sequence, it keeps the end that answers the question. Recipients keep header order from the first, because that is the order a reader sees them in. Thread references keep the ancestors nearest to this message, because that is the end of the path a thread view walks first.

### Attachment summary

The row keeps the indexable part of what the MIME reader found and only that: `attachment_count`, `attachment_total_size_octets`, `inline_resource_count`, and the `is_encrypted`, `carries_unverified_signature`, and `contains_unexpanded_tnef_part` markers.

**The per-attachment list of file names, media types, and sizes is deliberately not persisted.** The same reasoning as for recipient display names applies, and one more: a second representation of the attachment list can drift from the raw MIME it was derived from, and re-deriving it costs nothing in a pass a body reader is already making. The signature marker is named for presence rather than verification because nothing here verifies anything; a column called "signed" would be read as an authenticity result by every query that later touched it.

## The derived search document

`email_search_documents` is one-to-one with `stored_emails` and holds what lexical search reads: `subject_text`, `participant_addresses`, `body_text`, `body_text_before_trimming`, `text_source`, `extracted_at`, and the generated `search_vector`. [Body text and the lexical index](../features/imap-synchronization.md#body-text-and-the-lexical-index) describes how each of them is derived.

It is a table of its own for the reason raw MIME is. The text is large, only search reads it, and a timeline query that materialized a stored-email row would otherwise carry a body and its search vector through the change tracker on the way to a view that shows neither. Deleting a message cascades to it, so everything derived from a message is erased with the message.

`subject_text` and `participant_addresses` are bounded copies rather than references to the columns of the same name on `stored_emails`. A PostgreSQL generated column must be immutable and can only read its own row, and the array-to-text functions that would flatten the recipient arrays are merely stable — they call element output functions that need not be immutable. Copying the two values into this row at write time keeps the column's expression trivially immutable instead of requiring a custom SQL function, which no migration exists to create.

The copies are bounded more tightly than the columns they come from: 2000 characters of subject and 64 participant addresses in total. A `tsvector` cannot exceed one megabyte, and the whole document — subject, addresses, and up to `MaxExtractedTextCharacters` of body — shares that budget. Exceeding it would not degrade search; it would make the row unwritable, because the generated column is computed on every insert.

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

The recipient and search-vector indexes are GIN rather than B-tree because both serve containment tests. A B-tree over an array column serves only equality against a whole array, and over a `tsvector` it serves nothing search asks for; a GIN index is what turns either into an index scan.

One index the architecture draft lists is still deliberately absent: the partial indexes excluding remotely deleted messages wait for specification 10, which introduces the state they would filter on.

## The timeline ordering contract

Keyset pagination needs a total order, not merely a sort: two pages are contiguous only when every row falls on exactly one side of the key the previous page ended on. `EmailTimelinePosition` in `Domain` is the single statement of that order, and the two timeline indexes reproduce it column for column. A change to either is a change to both.

The order is: **received timestamp descending, with an unknown timestamp last, then the local identifier descending.**

Two parts of that are decisions rather than defaults.

A message can carry no usable received timestamp, and PostgreSQL orders nulls first under `DESC`. Inheriting that default would float every message nobody could date above the newest mail, on every page, forever — so both indexes spell out `NULLS LAST` and the in-memory comparer treats an unknown timestamp as older than every known one.

The tiebreaker compares identifiers as the sixteen big-endian octets PostgreSQL orders a `uuid` column by, written out rather than delegated to `Guid.CompareTo`. That method agrees with the octet order on the current runtime, but it documents only that its result is suitable for sorting — not that it is the order a `uuid` column uses. A page boundary computed in memory is resumed from by a query the index plans, so the tiebreaker has to be the index's order by construction.

Unit tests pin the contract, including where an undated message lands. Whether PostgreSQL then plans a timeline query against these indexes is an integration question that [specification 20](../../specs/20-aspire-integration-test-foundation.md) answers.

## Privacy classification

Participants, subject, and thread identifiers are personal data. They are stored because a timeline and a filter cannot work without them, and the columns above are the minimum that supports the planned queries — which is why display names other than the sender's, and the attachment list, are not among them. No projection introduced here selects raw MIME, and the content relationship stays unloaded by default so a mailbox query cannot pull a `bytea` value into the change tracker by accident.

The derived search document is not a lesser classification of the same data. Body text, the copied subject and addresses, and the search vector built from them are mail content, and none of them is anonymous merely because it was derived; they inherit the retention, access, export, and erasure obligations of the message they came from. The cascade from `stored_emails` is what makes that structural rather than a rule somebody has to remember.

## The Development-only schema bootstrap

Migrations are deliberately deferred until the schema that specifications 07 through 12 grow has settled, which would otherwise leave a developer with no way to create the tables the host reads and writes at all. `Persistence:CreateSchemaFromModelOnStartup` closes that gap: when it is on and the environment is Development, startup creates the schema from the EF Core model.

It is off by default, and turning it on in any other environment **fails startup** rather than creating anything. The environment is checked rather than trusted to deployment discipline, because the failure it guards against is silent: a schema created from whatever the model happened to say that day looks like a working database until the first migration tries to reconcile with it.

The underlying call creates tables only in a database that has none. It does not compare an existing database against the model and it drops nothing, so a developer whose database predates a model change recreates it themselves.

This is temporary scaffolding with a single owner. Specification 19 generates the reviewed baseline migration and removes the setting, the hosted service, the `IDevelopmentSchemaCreator` port, and its implementation together.

One open item belongs to that same review: `uid_validity` and `uid` are modelled as CLR `uint` because that is the IMAP wire type, and PostgreSQL has no native unsigned 32-bit integer. The mapping has never been validated against a real database. Specification 19 confirms it when the first migration is generated, and the same review has to confirm that the unique index on `(mail_folder_id, uid_validity, uid)` and the checkpoint comparisons still order correctly under whichever column type is chosen.
