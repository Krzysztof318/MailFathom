# Automatic embedding

<!-- describes: src/Application/Emails/Embeddings/Generation/**, src/Infrastructure/Embeddings/**, src/Infrastructure/Persistence/Embeddings/**, src/Host/Hosting/Workers/MailEmbeddingWorker.cs -->

[Message chunks](message-chunks.md) cuts a message into passages and [embedding generation](embedding-generation.md)
turns a passage into a vector. This page is the part between them: what decides that a passage should be embedded, when
it happens, and what an operator sees when it falls behind.

Nothing here is a command anybody runs. Mail that arrives is embedded because it arrived.

## What makes an instance embed

An active embedding profile, and nothing else. There is no `Enabled` flag: activating a profile is what starts
generation, and an instance with no active profile stores its passages and produces no vectors —
[ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
records why the switch is an operation rather than a setting.

That state is read per message rather than once at startup, so activating a profile takes effect on the next message
without a restart.

One thing narrows what an active profile reaches: what is cut into passages. Mail of a folder mapped with
`GenerateEmbeddings: false`, and mail of a folder no mapping names at all, is cut into none, so there is nothing to embed
and nothing of it is ever sent to a provider.
[What a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states that switch beside the other two, and [message chunks](message-chunks.md#when-chunking-runs) what happens to
passages cut before it was set.

A second narrowing applies wherever spam classification is switched on: junk is never embedded and its content never
reaches a provider, and a message no verdict has been reached about is neither cut nor offered here while it waits. The
account's own next run is what reaches such a message once a verdict admits it or its wait runs out — the same two
steps in the same order, one interval later — and the [backfill](embedding-backfill.md) sweep is what reaches whatever
a run's batch budget left behind.
[Junk is kept out of what a deployment derives from mail](spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
holds the whole of it, and none of it applies with classification off.

## The backlog between synchronization and the provider

A message is offered for embedding by the last local step of its account's synchronization run — the step that cuts its
passages, after classification and after the owner's rules have had their turn — and never inside the transaction that
stored the message. [The arrival pipeline](../architecture/arrival-pipeline.md) draws that order in full. Two things
follow from it, and both are the reason for it:

- **A provider outage cannot stall a mailbox fetch.** The embedding worker consumes committed local state, so the
  slowest thing it can make slower is itself.
- **No MCP request waits on any of it.** A read is answered from what is stored, whatever the backlog is doing.

The backlog is bounded by `Embeddings:MaxQueuedEmails`, and the bound refuses rather than waits. An initial
synchronization of a large mailbox produces work faster than any provider will accept it, so a full backlog is the
expected outcome rather than a fault: the messages it turns away are already stored with their passages, and the
[embedding backfill](embedding-backfill.md) is what reaches mail the live path did not.

The backlog lives in the process and is deliberately not durable. Losing it at shutdown loses no work, because what is
outstanding is decided by asking the database which passages have no vector under the active profile — the same
question a restart, a repeat, and the backfill all ask.

## Embedding one message

The worker takes one message at a time and, for that message:

1. reads the generation searches are answered from, and stops if there is none;
2. refuses to write if the configured model is not the one that was activated — see below;
3. reads the passages that have no vector under that generation, at most one provider call's worth;
4. asks the generator for their vectors;
5. commits those vectors, and repeats from step 3 until nothing is outstanding or the turn's calls run out.

Each call's vectors are committed together, so a crash leaves a whole page of passages embedded or none of it — never a
message that looks finished and is not. Nothing about the message is remembered between turns, which is what makes
offering one twice free: a message already current reads no passages, calls no provider, and writes nothing.

**While a model change is under way, this path keeps writing into the generation that is serving** rather than into the
one being built, so mail arriving during a reindex is searchable the moment it is stored. The reindex reaches the same
message for the new generation before the count that completes it can read zero, so nothing is lost by leaving that to
the sweep. [Changing the embedding model](../operations/embedding-profiles.md) describes the rest of the sequence.

One turn is allowed a bounded number of provider calls. The bound exists so that a store reporting passages as
outstanding and then storing nothing for them ends the turn instead of spending in a loop, and it is not claimed to be
out of reach: how many passages a message yields is decided by the chunking rules, and how many of them one call
carries is `Embeddings:MaxPassagesPerRequest`, so a batch size far below what a long message holds can reach it. A turn
that does is reported as its own outcome and warned about, never as a message that is finished — the passages it did not
reach stay outstanding, and reporting them as embedded would be invisible afterwards, because a partly embedded message
is still retrievable and simply answers worse.

One message at a time is a decision. Concurrency here would multiply against the provider's own rate limit and against
the resilience pipeline's concurrency budget; the bound that decides whether the worker keeps up with arriving mail is
how many passages one request carries, which
[embedding generation](embedding-generation.md#bounds-every-call-carries) already applies.

## What a reached ceiling does

Before every provider call the turn asks what the current budget period still admits, and a period that admits nothing
ends the turn without sending anything. The worker then **pauses until that period rolls over** rather than carrying on
to the next message: without the pause it would take every waiting message in turn, learn the same thing from the same
read, and drain a backlog at the speed of a database query. What it waits for is the roll-over instant the turn itself
reported, so it neither polls a ceiling already known to bind nor sleeps past the moment it lifts.

Nothing is dropped by waiting, and nothing has to be done to release it. The message whose turn met the ceiling keeps
its outstanding passages, which is the condition the [backfill](embedding-backfill.md#why-it-sweeps-rather-than-finishes)
selects on; the messages behind it stay in the backlog until its bound turns one away, at which point the same promise
covers those too. The pause is logged as a warning naming how long it will last and which key raises the ceiling, and
it is counted like any other outcome.

[Embedding generation](embedding-generation.md#what-an-instance-is-willing-to-spend) states the three ceilings, what
each is counted in, and why a batch that crosses one is paid for whole.

## An edited declaration that nobody activated

Configuration can be changed to name a different model without activating it, and the vectors already stored belong to
the model that *was* activated. Writing new vectors under the active profile with a different model would put two
geometries in one space — which makes retrieval slightly worse rather than failing, and is the hardest kind of defect
to attribute.

So the worker compares the two before it spends anything, and refuses the message when they differ. The refusal is a
warning naming both directions an operator can take it: activate the current declaration, or restore the one the stored
vectors belong to. It is not a provider failure and it costs no call.

`mfctl embedding status` reports the same disagreement without waiting for a message to arrive, and
`mfctl embedding activate` is the first of those two directions;
[administering the embedding profile](../operations/admin-endpoint.md#administering-the-embedding-profile) holds both.

## What a failure does

A provider call that ends without vectors ends that message's turn. The classification decides what an operator does
about it, and [embedding generation](embedding-generation.md#what-a-failing-call-is-classified-as) holds the table.

The worker does not repeat the call. The provider adapter already runs every call under a named resilience pipeline
with bounded, jittered attempts, and a second layer around it would multiply the two attempt counts against a provider
that is already refusing — the same single-layer rule
[outbound resilience](../architecture/outbound-resilience.md#the-single-layer-rule) states for every other outbound
call.

Passages a failed turn did not reach keep having no vector, which is exactly the condition the
[embedding backfill](embedding-backfill.md#why-it-sweeps-rather-than-finishes) selects on. So nothing is lost by
declining to try again here.

## What an operator can see

| Signal | What it answers |
| --- | --- |
| `mailfathom.embedding.backlog.depth` | How far behind embedding is right now |
| `mailfathom.embedding.backlog.refused` | How many messages the bound turned away, which the backfill will have to reach |
| `mailfathom.embedding.messages` | Messages taken from the backlog, by outcome — embedded, no active profile, declaration disagrees, provider failed, one turn's calls exhausted, or the spend ceiling reached |
| `mailfathom.embedding.message.duration` | How long embedding one message took, by the same outcome |
| `mailfathom.embedding.passages` | Passages given a vector |
| `mailfathom.embedding.budget.consumed` | Characters sent to a provider and charged against the spend ceiling |
| `mailfathom.embedding.input.truncated` | Messages the per-message ceiling cut short |
| `mailfathom.embedding.input.omitted` | Characters that ceiling left out of the passages it cut |

Falling behind is therefore visible as a rising depth rather than as search results that quietly stay lexical. On a
deployment with no metrics backend to read them from, `mfctl embedding status` answers the same question from the
database directly: how much of the mailbox each generation covers, what the provider last did, and what the period has
spent.

Nothing on that list is derived from mail. The tags are an outcome name and a failure classification, both of them
MailFathom's own closed sets; no message identity, passage, or vector reaches a log, a metric, or a trace, and a log
line about an embedded message carries counts alone.
