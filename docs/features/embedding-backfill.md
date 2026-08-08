# Embedding backfill

<!-- describes: src/Application/Emails/Embeddings/Backfill/**, src/Infrastructure/Persistence/Embeddings/StoredEmailEmbeddingBackfillStore.cs, src/Infrastructure/Observability/EmailEmbeddingBackfillTelemetry.cs, src/Host/Configuration/Embeddings/EmbeddingBackfillOptions.cs, src/Host/Hosting/Workers/MailEmbeddingBackfillWorker.cs -->

[Automatic embedding](automatic-embedding.md) covers mail as it arrives. An instance that has been synchronizing for
months has everything else, and for that mail the live path never runs at all — nothing revisits a message that was
stored before a profile was activated, or before passages existed, or while the queue between synchronization and the
provider was full.

This is the walk that reaches it. Nothing here is a command anybody runs either: activating a profile is what starts
it, the same fact that starts the live path.

## What it reaches, and in which order

Two conditions select a message, and they are the two halves of what a pre-existing mailbox looks like.

- **A message with extracted text and no passages** was stored before [message chunks](message-chunks.md) existed.
  Nothing can be embedded for it until they are cut, so the walk cuts them first — from the text an earlier extraction
  already stored, which costs a database write and no provider call and reaches no mail server. The rules applied are
  the ones synchronization applies, so a message this walk reaches ends up cut exactly as the same message arriving
  today would be.
- **A message with a passage that carries no vector** under the active profile was stored before the profile was
  activated, or was turned away by `Embeddings:MaxQueuedEmails`, or was left part-way through by a provider call that
  failed.

A message a tombstone hides is in neither group. Vectors nothing may retrieve are a provider bill with no reader. A
message whose local copy was deliberately kept after MailFathom deleted it on the server is not that: nothing may
retrieve it from the server any more, and everything may still retrieve it here, so the walk reaches it like any other.

Once a message is selected, what happens to it is exactly what happens to a newly synchronized one — the same unit of
work, described under [embedding one message](automatic-embedding.md#embedding-one-message). Nothing is remembered
about it between runs, so what is outstanding is decided by asking the database rather than by trusting a record of
what was done.

## Why it sweeps rather than finishes

The walk is ordered by the stored-message identifier and commits its position after each message, so an interrupted run
resumes at the next message instead of paying for the same one twice. When it reaches the end, it removes that position
rather than parking at it, and the next run starts again from the beginning.

That is deliberate, and it is what makes a promise the live path relies on true. A message the queue's bound turned
away is stored with its passages and nothing else will offer it again; a message whose turn a provider refused keeps
the passages that call did not reach; and in both cases the position has already stepped past them. A walk that
finished once would be announcing that those are reached later and not reaching them.

Stepping past a message the provider refused is the same decision the [extraction
backfill](imap-synchronization.md#backfilling-messages-stored-earlier) makes about a message nobody can parse:
nothing may block the walk. The run itself ends there — a provider that has just refused is not one to spend the rest
of a batch against — and the next run resumes past it.

A message that spends every provider call [one turn is
allowed](automatic-embedding.md#embedding-one-message) is treated differently, because it says something about that
message's length rather than about the provider: the walk counts it, warns, and carries on to the next message. The
passages that turn did not reach stay without a vector and a later sweep takes them, so such a message finishes across
several sweeps rather than in one — which is exactly what the count and the warning exist to make visible, since the
walk steps past it and no other number here would show it. A message needing that many calls means
`Embeddings:MaxPassagesPerRequest` is far below what one message of its length carries.

How long the next run waits after a *provider failure* depends on which refusal it was, read from the classification
[embedding generation](embedding-generation.md#what-a-failing-call-is-classified-as) assigns. A rate limit, a timeout,
and a transport fault are remote conditions to wait out, so the short interval follows and is the backoff. A rejected
credential, a refused request, and a vector the declared geometry does not describe are terminal: repeating them buys
the same answer at the same price against the account's request budget, so the long interval follows instead and the
warning in the log is what an operator acts on.

## What one run costs, and how to slow it down

A run reaches at most `EmbeddingBackfill:BatchSize` × `EmbeddingBackfill:MaxBatchesPerRun` messages, and every message
it reaches is a provider call or several. That product is therefore the most one run may spend, and
`EmbeddingBackfill:Interval` is how often it is paid.

`EmbeddingBackfill:IdleSweepInterval` is the longer pause taken instead once a sweep has reached the end. A completed
sweep means every message is current, so the only reason to start another is to pick up what a refused call or a full
queue left behind — worth asking regularly, and not worth asking every interval, because the question is a scan across
every passage the instance holds.

`EmbeddingBackfill:Enabled` stops the spending within one interval and loses nothing. What has been embedded stays
embedded, and what has not is found again by the same question whenever it is turned back on.
[Configuration reference](../operations/configuration-reference.md#embeddingbackfill) holds the defaults and the ranges.

An instance that has activated no profile runs the walk and reaches no mail: there is no vector space for a passage to
be missing from, so the run ends before it reads a message. An instance whose configured model is not the one the
active profile records writes nothing and warns, for the reason
[an edited declaration](automatic-embedding.md#an-edited-declaration-that-nobody-activated) gives.

## What an operator can see

| Signal | What it answers |
| --- | --- |
| `mailfathom.embedding.backfill.outstanding` | How many messages awaited embedding when the current sweep began |
| `mailfathom.embedding.backfill.runs` | Bounded passes, by how each ended — budget spent, sweep completed, no active profile, declaration disagrees, or provider failed |
| `mailfathom.embedding.backfill.chunked` | Messages that had to be cut into passages first, which is how much of the mailbox predates chunking |
| `mailfathom.embedding.backfill.messages` | Messages brought up to date with the active profile |
| `mailfathom.embedding.backfill.passages` | Passages given a vector |
| `mailfathom.embedding.backfill.exhausted` | Messages left part-way through because one turn spent every provider call it is allowed |

The outstanding count is measured once at the start of a sweep and held until the next sweep measures again, so the
gauge is a figure a sweep established rather than a live one. That is deliberate: an exact live count is an unbounded
scan over every passage, and making it a gauge would put that scan on whatever interval a collector happened to be
configured with. The counters beside it are what move in between, so progress is read as those rising against the
figure the sweep started from.

Nothing on that list is derived from mail. The tags are an outcome name and a failure classification, both of them
MailFathom's own closed sets; no message identity, passage, or vector reaches a log, a metric, or a trace, and a log
line about a finished run carries counts alone.
