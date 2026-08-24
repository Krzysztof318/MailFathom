# Embedding backfill

<!-- describes: backend/src/Application/Emails/Embeddings/Backfill/**, backend/src/Application/Emails/Embeddings/Generations/EmbeddingGenerationUpkeep.cs, backend/src/Infrastructure/Persistence/Embeddings/StoredEmailEmbeddingBackfillStore.cs, backend/src/Infrastructure/Observability/EmailEmbeddingBackfillTelemetry.cs, backend/src/Host/Configuration/Embeddings/EmbeddingBackfillOptions.cs, backend/src/Host/Hosting/Workers/MailEmbeddingBackfillWorker.cs -->

[Automatic embedding](automatic-embedding.md) covers mail as it arrives. An instance that has been synchronizing for
months has everything else, and for that mail the live path never runs at all — nothing revisits a message that was
stored before a profile was activated, or before passages existed, or while the queue between synchronization and the
provider was full.

This is the walk that reaches it. Nothing here is a command anybody runs either: activating a profile is what starts
it, the same fact that starts the live path.

## Which generation it walks towards

One, and the caller decides which: the generation being built where a model change is under way, and otherwise the one
searches are answered from. The same walk therefore does both jobs — filling the gaps in the generation that is serving,
and building a new one from nothing beside it — because they are the same question asked of two profiles.
[Changing the embedding model](../operations/embedding-profiles.md) is the operator's view of the second.

Two more things ride the same pass, in the same order every time it runs. When the sweep finishes and nothing is
outstanding for a generation that is being built, that generation becomes the one searches are answered from, in one
transaction. And a generation that a switch replaced has its vectors removed, a bounded batch per pass, until it holds
none.

## What it reaches, and in which order

Two conditions select a message, and they are the two halves of what a pre-existing mailbox looks like.

- **A message with extracted text and no passages that the owner's rules have finished with** was stored before
  [message chunks](message-chunks.md) existed, or was held back by spam classification while it waited for a verdict, or
  was simply left behind by one account run's batch budget. Nothing can be embedded for it until they are cut, so the
  walk cuts them first — from the text an earlier extraction already stored, which costs a database write and no
  provider call and reaches no mail server. The rules applied are the ones synchronization applies, so a message this
  walk reaches ends up cut exactly as the same message arriving today would be. Requiring that the rules have finished
  is part of that sameness rather than a narrowing beside it: this sweep runs on its own interval while an account run
  is still fetching a mailbox, so without it a first synchronization would have its mail cut here before a single rule
  had read it — which is the one order [the arrival pipeline](../architecture/arrival-pipeline.md) exists to fix. The
  stamp is written by an account run's rule pass and once besides, by the migration that added the column, which stamped
  every message the previous version had already stored. So **a deployment running with `MailSynchronization:Enabled`
  set to `false` still cuts and embeds the mail it upgraded with** — that mail carries the stamp already — and what the
  switch holds back here is mail a later run stored and no rule pass reached, which on such a deployment is none. A
  message a rule is still *moving* is passed over as well, however long it has been stamped: a rule declares a move
  rather than performing one, so until the account's next run carries the relocation to the mail server the message is
  sitting in a folder it is leaving, and passages cut there describe a mapping it is about to leave. The wait ends when
  that run converges the move, and a relocation that completed or was abandoned holds nothing back at all. Mail
  that already carries passages is embedded either way, since that is the other group and it waits on nothing.
- **A message with a passage that carries no vector** under the generation being walked towards was stored before that
  generation existed, or was turned away by `Embeddings:MaxQueuedEmails`, or was left part-way through by a provider
  call that failed. For a generation being built from nothing, that is every message the instance holds.

A message a tombstone hides is in neither group. Vectors nothing may retrieve are a provider bill with no reader. A
message whose local copy was deliberately kept after MailFathom deleted it on the server is not that: nothing may
retrieve it from the server any more, and everything may still retrieve it here, so the walk reaches it like any other.

The walk asks the query for the folders configuration maps and leaves embedded, so a message of a folder mapped with
`GenerateEmbeddings: false` is in neither group, and neither is a message of a folder **no mapping names**. The
narrowing is on the query rather than on what it does with a row: a folder whose passages are deliberately never cut
would otherwise look like the first group on every run forever, and mail retained under a removed mapping would be
re-embedded — paid for at a provider — for a folder this deployment no longer has. What was embedded before either
change stays and is retrieved like any other vector, subject to what the reading side admits.
[What a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states both cases.

**A message classification was holding is released by an account run rather than by this sweep.** The rule pass is
narrowed by the same admission, so a message waiting on a verdict is never stamped — and with the stamp required here,
such a message cannot appear in this query at all while it waits. What admits it is the next account run: its rule pass
stamps the message, and the cut one step later in that same run cuts it. This sweep still leaves out junk entirely and
still cannot reach a message no verdict has been reached about, so it never carries spam to a provider; what it reaches
is what a run's own batch budget left behind. Cutting a message's first passages is still where a release is countable
per message wherever this sweep is what performs it, which is what
`mailfathom.spam.derived_work.admissions` reports.
[Junk is kept out of what a deployment derives from mail](spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
holds the rule, and with classification off this query is exactly what it was.

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
[AI configuration](../operations/configuration-ai.md#embeddingbackfill) holds the defaults and the ranges.

An instance that has activated no profile reaches no mail: there is no vector space for a passage to be missing from,
so the pass ends before it reads a message. An instance whose configured model is not the one the generation records
writes nothing and warns, for the reason
[an edited declaration](automatic-embedding.md#an-edited-declaration-that-nobody-activated) gives.

Neither interval applies to a run the deployment's spend ceiling stopped. That ending names the instant it stops
applying — the moment the budget period rolls over — so the worker waits for exactly that: the short interval would
re-read a ceiling already known to bind, and the long one would leave a rolled-over period idle for as much as a quarter
of an hour. The run also stops **without advancing its position**, unlike a run a refused call ended, because a reached
ceiling says nothing about the message in hand and the passages it did not reach are the ones a fresh period should pay
for first. [Embedding generation](embedding-generation.md#what-an-instance-is-willing-to-spend) holds the ceiling
itself, and an initial backfill of a large mailbox is the case for raising it deliberately rather than being paced by
it.

**A per-owner ceiling ends nothing.** Where the turn reports that the message's owner has spent their share while the
deployment still has room, the walk commits its position and carries on to the next message. That is not a smaller
version of the ending above but the opposite of it: the walk visits the whole deployment's mail in identifier order and
owners interleave in it, so ending here would leave every other owner's mail unembedded until the period rolled over —
which is exactly the harm bounding spend per owner exists to prevent. The stepped-over message keeps its outstanding
passages, so the next sweep after the roll-over reaches it, and the run reports how many messages it stepped past that
way beside how many it embedded.

Because nothing ends, that fact is true of every pass until the period rolls over — and a busy instance sweeps on the
short interval. The warning is therefore written once per period rather than once per pass, so it does not bury the rest
of the log for as long as an owner has mail waiting, and the counter beside it is what carries every pass.

Nothing about a per-owner ceiling makes the resume position per owner, and that is a decision rather than a gap. One
walk serves every owner at once, so a cursor each would record the same walk several times over and the run would still
have to visit every message to decide which cursor to move.

The removal of a superseded generation's vectors is bounded too, and it is not a setting: it deletes rows nobody reads,
reaches no provider, and costs nothing an operator has to consent to, so what paces it is the interval between passes
rather than a number beside the ones above. A pass that removed a full batch is followed by the short interval, because
there is more of that generation behind it.

## An operator's act does not wait for the pause to expire

Every pause above is chosen by the pass that just ended, which means it was chosen without knowing what an operator
would do next. That is most visible on a first activation. Every pass before one ends with no generation to walk
towards, so the walk is always sleeping the long interval, and the row the activation commits is one the sleeping
worker has no way to observe — leaving the first vectors of a mailbox to arrive whenever a quarter-hour timer unrelated
to the activation happens to expire.

So the acts that create the work say so. **Activating a profile** and **cancelling a reindex** each ask for a pass now,
and a worker waiting out a pause takes it immediately; the second matters for the same reason the first does, because
what a cancellation leaves behind is a generation nothing reads whose partial vectors are personal data with no purpose
left. An act arriving while a pass is already running is held rather than dropped, so the pass after it is the one that
picks the work up, and two acts in a row ask for one pass rather than two. Nothing else brings a pass forward: a
message arriving is the [live path](automatic-embedding.md)'s, not this walk's.

The pause itself is unchanged and so is its purpose. `EmbeddingBackfill:IdleSweepInterval` still governs an instance
with nothing to do, which is exactly the instance that should stop asking the database about nothing.

**When the next pass is due is readable while it is being waited for.** `mfctl embedding status` reports it on the
`Next pass` line, and that line is what separates a deployment that is waiting from one that is failing — every other
reading in that output says the same thing during a pause as it does on a broken instance: nothing serving, nothing
embedded, and a provider nothing has been asked of. A deployment reporting no pass at all has scheduled none, which
happens for two reasons and neither is a fault: it has only just started, or `EmbeddingBackfill:Enabled` is `false` and
its worker reports outright that it will take no pass — which is also what keeps an activation on such a deployment
from recording a pass nothing would ever reach. The log says the same at `Debug` after every pass, and says at
`Information` when an act cut a pause short.

## What an operator can see

| Signal | What it answers |
| --- | --- |
| `mailfathom.embedding.backfill.outstanding` | How many messages awaited embedding when the current sweep began |
| `mailfathom.embedding.backfill.runs` | Bounded passes, by how each ended — batch budget spent, sweep completed, no active profile, declaration disagrees, provider failed, or the spend ceiling reached |
| `mailfathom.embedding.backfill.chunked` | Messages that had to be cut into passages first, which is how much of the mailbox predates chunking |
| `mailfathom.embedding.backfill.messages` | Messages brought up to date with the active profile |
| `mailfathom.embedding.backfill.passages` | Passages given a vector |
| `mailfathom.embedding.backfill.exhausted` | Messages left part-way through because one turn spent every provider call it is allowed |
| `mailfathom.embedding.backfill.owner_ceiling` | Messages the sweep stepped past because the owner they belong to had spent what one period admits for them |
| `mailfathom.embedding.generation.switches` | Generations that finished being built and became the one searches are answered from |
| `mailfathom.embedding.generation.removed` | Vectors of a superseded generation removed after a switch |

The outstanding count is measured once at the start of a sweep and held until the next sweep measures again, so the
gauge is a figure a sweep established rather than a live one. That is deliberate: an exact live count is an unbounded
scan over every passage, and making it a gauge would put that scan on whatever interval a collector happened to be
configured with. The counters beside it are what move in between, so progress is read as those rising against the
figure the sweep started from.

The switch is counted rather than published as a state, because what an operator asks of it afterwards is when it
happened and how many have. A gauge naming the generation now serving would be a dimension of unbounded cardinality for
a value the log line about the switch already carries.

Nothing on that list is derived from mail. The tags are an outcome name and a failure classification, both of them
MailFathom's own closed sets; no message identity, passage, or vector reaches a log, a metric, or a trace, and a log
line about a finished run carries counts alone.
