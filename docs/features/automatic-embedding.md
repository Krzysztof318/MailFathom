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

## The backlog between synchronization and the provider

A message is offered for embedding **after** it and its passages are committed, never inside the transaction that
stored them. Two things follow from that ordering, and both are the reason for it:

- **A provider outage cannot stall a mailbox fetch.** The embedding worker consumes committed local state, so the
  slowest thing it can make slower is itself.
- **No MCP request waits on any of it.** A read is answered from what is stored, whatever the backlog is doing.

The backlog is bounded by `Embeddings:MaxQueuedEmails`, and the bound refuses rather than waits. An initial
synchronization of a large mailbox produces work faster than any provider will accept it, so a full backlog is the
expected outcome rather than a fault: the messages it turns away are already stored with their passages, and the
backfill is what reaches mail the live path did not.

The backlog lives in the process and is deliberately not durable. Losing it at shutdown loses no work, because what is
outstanding is decided by asking the database which passages have no vector under the active profile — the same
question a restart, a repeat, and the backfill all ask.

## Embedding one message

The worker takes one message at a time and, for that message:

1. reads the active profile, and stops if there is none;
2. refuses to write if the configured model is not the one that was activated — see below;
3. reads the passages that have no vector under that profile, at most one provider call's worth;
4. asks the generator for their vectors;
5. commits those vectors, and repeats from step 3 until nothing is outstanding.

Each call's vectors are committed together, so a crash leaves a whole page of passages embedded or none of it — never a
message that looks finished and is not. Nothing about the message is remembered between turns, which is what makes
offering one twice free: a message already current reads no passages, calls no provider, and writes nothing.

One message at a time is a decision. Concurrency here would multiply against the provider's own rate limit and against
the resilience pipeline's concurrency budget; the bound that decides whether the worker keeps up with arriving mail is
how many passages one request carries, which
[embedding generation](embedding-generation.md#bounds-every-call-carries) already applies.

## An edited declaration that nobody activated

Configuration can be changed to name a different model without activating it, and the vectors already stored belong to
the model that *was* activated. Writing new vectors under the active profile with a different model would put two
geometries in one space — which makes retrieval slightly worse rather than failing, and is the hardest kind of defect
to attribute.

So the worker compares the two before it spends anything, and refuses the message when they differ. The refusal is a
warning naming both directions an operator can take it: activate the current declaration, or restore the one the stored
vectors belong to. It is not a provider failure and it costs no call.

## What a failure does

A provider call that ends without vectors ends that message's turn. The classification decides what an operator does
about it, and [embedding generation](embedding-generation.md#what-a-failing-call-is-classified-as) holds the table.

The worker does not repeat the call. The provider adapter already runs every call under a named resilience pipeline
with bounded, jittered attempts, and a second layer around it would multiply the two attempt counts against a provider
that is already refusing — the same single-layer rule
[outbound resilience](../architecture/outbound-resilience.md#the-single-layer-rule) states for every other outbound
call.

Passages a failed turn did not reach keep having no vector, which is exactly the condition the backfill selects on. So
nothing is lost by declining to try again here.

## What an operator can see

| Signal | What it answers |
| --- | --- |
| `mailfathom.embedding.backlog.depth` | How far behind embedding is right now |
| `mailfathom.embedding.backlog.refused` | How many messages the bound turned away, which the backfill will have to reach |
| `mailfathom.embedding.messages` | Messages taken from the backlog, by outcome — embedded, no active profile, declaration disagrees, provider failed |
| `mailfathom.embedding.message.duration` | How long embedding one message took, by the same outcome |
| `mailfathom.embedding.passages` | Passages given a vector |

Falling behind is therefore visible as a rising depth rather than as search results that quietly stay lexical.

Nothing on that list is derived from mail. The tags are an outcome name and a failure classification, both of them
MailFathom's own closed sets; no message identity, passage, or vector reaches a log, a metric, or a trace, and a log
line about an embedded message carries counts alone.
