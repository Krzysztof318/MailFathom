# Changing the embedding model without a search outage

<!-- describes: src/Application/Emails/Embeddings/Generations/**, src/Infrastructure/Persistence/Embeddings/EmbeddingGenerationStore.cs -->

Re-embedding a mailbox takes as long as it takes and costs what it costs. If activating a new model invalidated the
vectors already stored, semantic search would be degraded for the whole run, and an operator who changed model on a
Tuesday would still be answering for it on Wednesday.

So it does not. A new model becomes a **generation that is built** beside the one still answering searches, and it
takes over in a single transition once it is complete. This page is what an operator sees while that happens, what it
costs, and what stopping or reversing it means.

## A generation is a profile row

There is no counter and no second record. [ADR
0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
makes the profile the generation, so an instance in the middle of a model change holds two rows in
`embedding_profiles`, and every stored vector names the one it belongs to. Each row is in one of three states:

| State | What it means |
|---|---|
| **Building** | Vectors are being produced under it. Nothing reads it, and at most one row is here |
| **Active** | The one generation searches are answered from. At most one row is here |
| **Superseded** | Replaced by a later generation, or abandoned before it ever served. Its vectors are being removed |

"At most one" is the database's answer rather than a rule the code remembers: `ix_embedding_profiles_lifecycle_state`
is unique over the state and partial to the first two. [Stored email
schema](../architecture/stored-email-schema.md#embedding-profiles) holds the columns.

## The sequence, and what an operator sees at each stage

Configuration declares the model, and editing it starts nothing —
[embedding generation](../features/embedding-generation.md#declaring-is-free-activating-is-what-spends) is where that
split is argued. Activation is the explicit act that takes the declaration up, and from there the deployment runs the
rest on its own.

1. **Activation registers the generation.** The declared geometry is fingerprinted and resolved against
   `embedding_profiles`: an identity already registered resolves to the row that exists, and a new one is inserted. The
   row starts as *building*, and its approximate vector index is created immediately — while the generation is empty,
   which is the cheapest moment it can be built. Activating what is already serving reports that and spends nothing;
   activating while a *different* generation is being built is refused rather than started beside it, because one walk
   between two partial generations would finish neither.
2. **The reindex fills it.** The same bounded, resumable sweep that backfills mail now walks towards the new
   generation, at the pace `EmbeddingBackfill:*` sets. [Embedding backfill](../features/embedding-backfill.md) describes
   the walk, what one pass costs, and how to slow it down. Searches are answered from the old generation throughout,
   and mail arriving meanwhile is embedded into that old generation by the live path, so nothing becomes unsearchable
   while the run is on.
3. **The switch happens once, when nothing is outstanding.** A completed sweep is not enough on its own — a message a
   provider refused stays outstanding behind the walk's position — so the deployment counts what remains and switches
   only at zero. Promoting the new generation and superseding the old one is one transaction, reported as
   `The generation being built is complete and is now the one searches are answered from` and counted by
   `mailfathom.embedding.generation.switches`.
4. **The superseded vectors are removed in bounded batches.** The old generation's index is dropped as soon as it
   stops being read, and its vectors go a batch per pass, counted by `mailfathom.embedding.generation.removed`. The
   profile row itself survives: it is what a vector was once attributable to, and its identity may never move.

While the reindex is running, `mailfathom.embedding.backfill.outstanding` is how much of the mailbox the new
generation is still missing, and the counters beside it are what move in between.
[What an operator can see](../features/embedding-backfill.md#what-an-operator-can-see) lists all of them.

## What it costs

**A full re-embed of the mailbox**, at whatever the declared provider charges per passage. Every passage of every
message that a search may reach is sent again, because a vector of the old space says nothing about where that passage
lands in the new one. Nothing about the switch is free either way: the old generation goes on occupying storage until
its removal finishes, so an instance is briefly holding two generations of vectors at once.

Nothing else changes. Chunking is not repeated — the passages are already cut and their boundary rules are not part of
a profile's identity — so a model change pays for provider calls and local writes, not for re-reading mail.

## Stopping one, and going back

**Cancelling a reindex** abandons the generation being built and leaves the one serving exactly where it is. The
abandoned row becomes superseded, its index is dropped, and whatever partial vectors it accumulated are removed in the
same bounded batches. What was spent on those vectors is spent; nothing about the search results changes, because that
generation was never read.

A cancellation that arrives after the reindex has already completed reports that nothing was being built, and changes
nothing: the generation it names is the one searches are now answered from, and this command never takes that out of
service. Going back to the previous model from there is the rollback below.

**Rolling back after a switch is activating the previous model again.** It is unambiguous — the row is still there and
its identity was fixed when it was registered, so the declaration resolves to it rather than to a duplicate — and it is
**paid**: the superseded generation's vectors were removed rather than retained, so coming back means embedding the
mailbox a second time. ADR 0006 takes that deliberately. The alternative is doubling vector storage indefinitely for
derived personal data whose purpose ended at the switch, and an instance that carries two full generations forever
because nobody ran a cleanup.

The one case where rolling back costs less is a rollback that catches its own removal part-way through: a generation
activated again stops being superseded, so whatever vectors it still holds are kept and the reindex only has to produce
the rest.

## What is never affected

- **Retrieval reads exactly one generation.** A partially built generation is never mixed with the one still serving,
  which is what the lifecycle states and the index over them are for.
- **No mail is re-read.** A reindex reaches no IMAP server, so it cannot touch a remote `\Seen` flag however long it
  runs.
- **Nothing is logged about the mail.** Every line and every metric here carries counts, a state name, and a profile
  identifier; no subject, address, passage, or vector reaches any of them.
