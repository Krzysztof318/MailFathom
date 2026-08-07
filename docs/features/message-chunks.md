# Message chunks

<!-- describes: src/AI/Chunking/**, src/Application/Emails/Chunking/**, src/Infrastructure/Persistence/Emails/EmailChunkWriter.cs, src/Infrastructure/Persistence/Entities/EmailChunkEntity.cs -->

A message is often too big to be the unit a search answers with. A forwarded thread can carry twenty exchanges, and the
one paragraph that answers a question is somewhere inside it. MailFathom therefore cuts every message's extracted text
into overlapping passages — chunks — and stores each of them with the span it came from and a hash that identifies it.

Nothing retrieves chunks yet. They exist because the passage, not the message, is what a vector will later be produced
for, and because deriving them is free: chunking reaches no provider, opens no connection, and costs an instance with no
embedding provider configured nothing but the rows. [Body text and the lexical index](imap-synchronization.md#body-text-and-the-lexical-index)
describes the text they are cut from, and [Stored email schema](../architecture/stored-email-schema.md#message-chunks)
describes the table they are stored in.

## When chunking runs

A message is cut in the same database transaction that stores what was extracted from it, on both paths that extract:
the synchronization run that has just fetched a message, and the backfill that re-derives text for mail stored before
extraction existed. A committed message is therefore never one whose passages something else still has to produce.

A message that yielded no text is cut into nothing. That covers a body that carried no words and a body that arrived
encrypted: both keep the search document that makes them findable on their subject and participants, and neither gains a
chunk.

A run that could not read a message's body at all — because the raw MIME was never stored, or because nothing could parse
it — leaves the passages alone rather than removing them. A remote message is immutable, so a run that failed this time is
no reason to forget what a run that succeeded already cut, which is the same rule the search document follows.

## The boundary rules

| Rule | Value | What it decides |
|---|---|---|
| Source form | The trimmed text | Which of extraction's two readings is cut |
| Target | 1000 characters | The most a chunk may hold |
| Minimum | 250 characters | How far a chunk must reach before a separator may end it |
| Overlap | 200 characters | How far back into the previous chunk the next one starts |
| Separators | `\n\n`, then `\n`, then a space | Where a chunk is preferably ended |
| Rule-set version | 1 | The number the rules above are recorded under |

The cut walks the text once. Each window reaches at most the target; inside it, the **last** occurrence of the strongest
separator that still leaves a chunk of at least the minimum is where the chunk ends, and a window offering no qualifying
separator is cut at its own end. The separator belongs to the chunk it ends, so the offsets name a contiguous span: read
the extracted text from a chunk's start offset for the length of its text and you get that chunk back, character for
character.

Two of those numbers are choices worth stating. **The source form is the trimmed text** — the reading extraction
produced by removing quoted history and signatures — because that is what somebody actually wrote, and cutting the
untrimmed form would fill a mailbox with passages of repeated thread history. The untrimmed reading is kept beside it and
is reachable by a rule set that names it, so an over-aggressive trim is recoverable rather than permanent. **The minimum
exists** because a blank line early in a long paragraph would otherwise end a chunk after a handful of words; it shortens
no chunk, it only refuses a break that would.

Every boundary falls between text elements, so no chunk begins or ends inside a surrogate pair or a combining sequence. A
single text element longer than the whole window is the one case the target cannot bound: it is taken whole, overrunning
by a few characters, because refusing it would leave the walk with nowhere to go.

The cut consults no clock, no random source, and no culture, and every comparison is ordinal. The same text under the
same rules produces the same passages, with the same offsets and the same hashes, on any machine and in any order.

## What the hash covers

Each chunk carries a SHA-256 digest, written as sixty-four lowercase hexadecimal characters. It is computed over:

- the chunk's own text;
- every rule in the table above — the source form, the target, the minimum, the overlap, the separator ladder in order,
  and the rule-set version;
- whether the text was derived from HTML rather than read from a plain-text part.

Covering the rules as well as the text is the whole point. A digest over the text alone would report a message's chunks
as unchanged after the boundaries were tuned, and would leave whatever hangs on them attached to boundaries they no
longer describe. Because the rules are covered, a boundary change produces different chunks even where the words are
identical, and it is a local re-cut rather than anything a provider is paid for.

Fields are length-prefixed and numbers are written big-endian, so the encoding is one-to-one and the digest depends on
the values rather than on the machine that computed them.

## Re-chunking an unchanged message

Re-deriving a message compares what the chunker just produced against what the message already has — the ordinals and
the digests, never the stored text. Identical means **nothing is written at all**: no update, no delete, no insert. That
is what keeps a restart, a content repair, or a backfill from re-doing work already paid for, and what keeps whatever
later hangs on a passage hanging on the same row.

Anything else replaces the message's passages whole rather than reconciling them one by one, because a boundary change
shifts every ordinal after the first difference and a row-by-row merge would only make that look survivable.

## The rule-set version

Every chunk records the version of the rules it was cut to, in a column beside its hash. Nothing reads it to decide
correctness — the hash already covers the rules, so a change to them is a changed digest whatever the column says. It
exists so that a backfill can be asked how much of a mailbox is still cut to the previous rules, which is the one
question a hash has no answer to.

The version is not what makes a rule change take effect, and it is not checked against anything. It is a label, and it is
accurate only because changing any rule and incrementing it are one edit.

## Privacy

A chunk's text is mail content and personal data by default, and it inherits the source message's classification,
retention, access, export, and erasure obligations whole. Nothing about being derived makes it a lesser copy.

- **Deleting a message deletes its passages.** The chunk rows cascade from the stored email, so the deletion path that
  reaches a message reaches everything cut from it without a rule anybody has to remember.
- **No chunk text reaches a log, a metric, a trace, or an error message.** The ordinal, the offsets, and the length are
  the only things about a chunk that are safe to report; the digest identifies a passage and is treated the same way.
- **The chunk copies none of the message's other coordinates.** The account, the folder, the sender, the recipients, the
  date, and the subject are reached through the message a chunk belongs to rather than duplicated onto it, so this table
  never becomes a second searchable copy of who somebody corresponds with.
- **Deriving a chunk sends nothing anywhere.** It is a local computation over text the instance already holds.

## What a lossy reading carries forward

A message that offered only HTML has text that was inferred from markup, and extraction marks that reading as lossy.
Every chunk cut from such a text carries the marker forward, so a later ranking change can weigh a lossy passage
differently without walking every message's extraction again. The marker is part of the hash as well: the same words read
from markup and read from a plain-text part are worth different amounts, so they are not one passage under two names.

## What is not here

Producing an embedding of any kind. The table a vector hangs on exists — [Stored vectors](../architecture/stored-email-schema.md#stored-vectors) describes it, and a vector is keyed on the chunk it was produced for — but nothing fills it, no index is built over it, and no embedding provider is reached. Ranking of any kind, and any change to what `search_emails` returns. Chunking attachment payloads, which extraction never opens in the first place.
