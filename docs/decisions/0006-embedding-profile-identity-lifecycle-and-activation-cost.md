---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-05
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Identify an embedding profile by the geometry of its vector space, keep that identity immutable, and make activation state what it is about to spend

<!-- describes: backend/src/AI/Chunking/**, backend/src/AI/Embeddings/**, backend/src/AI/ProviderAdapters/**, backend/src/AI/Providers/** -->

## Context and Problem Statement

MailFathom extracts plain text from every synchronized message and indexes it as a `tsvector`, which is what `search_emails` queries today. Nothing derives a vector from that text: `backend/src/AI/Embeddings` and `backend/src/AI/Chunking` are empty directories, and no `embedding_profiles` or `email_embeddings` table exists. The one piece already in place is the `vector` extension, which `MailFathomDbContext` installs so a column of that type can be declared when one is finally added.

Section 9.3 of the architecture draft settles the column: pgvector's dimensionless `vector` type paired with an `embedding_profile_id` and an explicit dimension check, so several profiles coexist and each gets its own partial index. What it does not settle is what a profile *is*. Provider, model, dimension, and distance metric are obvious members of that identity; whether the chunk boundary rules belong there is not. Nor does it settle whether a profile is edited or replaced, where the authoritative record of one lives, or what an operator pays at the moment they turn one on — which for an instance holding months of mail is a provider bill and a full re-embed.

Deciding this before the tables exist is the point. Migrations here are append-only, so the first migration freezes the columns and the uniqueness constraints, and both wrong answers are the kind that cannot be corrected afterwards: an identity that cannot attribute a stored vector to what produced it never regains the ability, and a schema that lets a profile be edited in place has already made re-embedding unobservable.

Recorded on issue 424. Issues 425, 426, 430, 433, and 435 are the parts that cannot be written until it is answered, and issue 436 is the parent that owns all of them. No numbered specification under `specs/` backs it; specification 08 defines the extracted text this derives from, and specification 10 defines the deletion path every derived artifact has to stay reachable from.

## Decision Drivers

- Embedding is **the first thing MailFathom does that costs money per unit of mail**. Everything before it was bounded by the operator's own hardware, so a runaway loop cost CPU. A runaway embedding loop is an invoice that arrives a month later, and that changes what is allowed to happen by default — in the running service and in the test suite alike.
- A stored vector must be **attributable to exactly what produced it**. Without that, nothing downstream can tell a comparable vector from an incomparable one, and every later claim about retrieval quality rests on a guess.
- **Comparability is a property of the vector space, not of the text that entered it.** Two vectors from one model over differently-bounded chunks are points in the same space and the distance between them means what it always meant. What differently-bounded chunks change is coverage and attribution. A decision that conflates the two prices a free local change as a paid one.
- Migrations are **append-only**, so the columns and constraints chosen here are permanent, and a mistake in them is corrected by adding rather than by repairing.
- **One concept, one mechanism.** The repository refuses a second thing that owns what something already owns, and a generation counter beside a profile identifier is exactly that shape.
- Changing model must not be **an outage**. Draft section 12.2 already states the guarantee: a new generation is built in the background and the old vectors keep serving until it is complete.
- Vectors, chunks, and snippets **inherit the classification of the mail they derive from**. A superseded generation is not spare capacity; it is personal data with no remaining purpose, and storage limitation applies to it.
- The operator must be able to **see the cost before agreeing to it**, not read about it afterwards in a metric.
- A deployment must be able to **describe itself**. Which model an instance embeds with is a property of that deployment, and a property nobody can see in configuration is one nobody reviews, charts, or diffs.
- **pgvector indexes less than it stores.** In 0.8.2 a `vector` column holds up to 16000 dimensions but HNSW indexes only 2000 of them, and `halfvec` reaches 4000 at half precision. Model width is therefore a database constraint rather than a provider detail, and it has to be decided rather than discovered.

## Considered Options

The decision has five independent axes. An option on one constrains no option on another, which is why they are listed apart rather than as five bundled proposals.

**A — what identifies a profile:**

1. Geometry and the chunk boundary rules together, in one identity.
2. Geometry alone; the boundary rules belong to the chunk's own identity.
3. Geometry alone, with the boundary rules recorded nowhere.

**B — whether a profile is edited or replaced:**

1. Identity columns immutable, lifecycle state mutable.
2. The whole row immutable, lifecycle included.
3. The row mutable, with a generation counter distinguishing what was produced when.

**C — where the authoritative record of a profile lives:**

1. The database, written only through the administrative endpoint.
2. The database, with configuration allowed to register and activate the first profile on an instance that has none.
3. Configuration declares the available profiles; activation stays an imperative act.
4. Configuration is authoritative and startup materializes the row.

**D — what an operator sees before activation spends:**

1. A computed estimate, an explicit confirmation, and the ceiling from issue 434 refusing a run that exceeds the budget.
2. A computed estimate and an explicit confirmation, with the ceiling limiting rate during the run only.
3. A warning in the documentation and in the log.

**E — what happens to the previous generation after the switch:**

1. Removed in bounded batches immediately.
2. Kept for a configured window, then removed.
3. Kept until the operator removes it.

## Decision Outcome

Chosen options: **A2**, **B1**, **C3**, **D1**, and **E1**.

### A profile describes the geometry of a vector space and nothing else

Its identity is the provider, the provider's model identifier, the model version where the provider exposes one, the dimension, the distance metric, and the input preparation — how text exceeding the model's context window is cut down before it is sent, the instruction or prefix template a model requires of a passage, and whether the vector is normalized. Those are exactly the properties that decide whether two vectors can be compared, and the dimension belongs among them rather than being derivable from the model, because for some providers it is a request parameter rather than a property of the model. Where the vector-width decision below shortens it, the identity records the width the stored vectors actually have, not the model's nominal one.

The input preparation is not the same thing as the input ceiling issue 434 configures, and the two are easy to confuse because both are a limit on text. Preparation is what the model sees and therefore changes the vector: cutting one way rather than another produces a different point in the space. The ceiling is what the instance is willing to spend on one message, so it changes how many vectors exist, never what any of them means.

What is deliberately not part of it: the chunk boundary rules, the credential, the endpoint address, and every rate and batch limit. None of them changes what a vector means.

### The chunk carries the chunking rules, and attribution runs on two axes

The chunk's content hash covers the boundary rules — the target size, the overlap, the separators, and whether it derives from the form specification 08 trimmed of quoted history or from the original kept beside it — in addition to the text itself. A change to those rules therefore produces different chunk rows, and since a vector row is keyed on the chunk it belongs to, a vector is always attributable to both halves of what produced it: the chunk row says what text was embedded and under which rules, the profile row says what embedded it.

That is what makes the two costs separable. Re-chunking is a pure local derivation that reaches no network and pays no provider, so a boundary change re-embeds only the chunks that actually changed. Under option A1 the same change would be a new profile and a full paid re-embed of the mailbox, for a tuning step that never touched the model.

A chunk also records its rule-set version as a column of its own, beside the hash. Nothing depends on it for correctness — the hash already covers it — but backfill and reindex have to answer *how much is still on the old rules*, and a question asked of a hash has no answer.

### Identity is immutable; operation is not

The identity columns above are fixed at insertion and covered by a fingerprint with a unique index, so activating a declaration whose identity already exists resolves to the existing profile instead of writing a second one that would be re-embedded for nothing. What the row also holds, and what stays mutable, is the lifecycle state and the progress of the generation it names — building, active, superseded — because those describe where the profile is rather than what its vectors mean.

Everything operational is configuration and never reaches this row: the endpoint address, the credential, the batch size, the request rate, the concurrency, the ceilings issue 434 owns. That is what makes option B2's cost disappear rather than having to be argued away — rotating a provider key or raising a rate limit does not touch the profile at all, so nothing about a wholly immutable identity forces a re-embed of vectors that are still perfectly valid. The two classes of column that remain are an identity that may never move and a state that must.

### The profile is the generation

There is no generation counter. Issue 433 needs two generations to coexist while a new one is built, and under an immutable identity that is two profile rows — one serving, one building — with a single recorded transition between them. Retrieval reads exactly one profile at a time, and a partially built one is never mixed with the one still serving.

A counter beside the identifier would be a second mechanism owning the same concept, and it would carry the failure mode that every read path must remember to read it: forget once and two incomparable vectors are compared under one identifier, silently and with a plausible-looking number as the result.

### Configuration declares the model and the provider; activation is what materializes it and spends

The provider and the model are configuration values, as draft section 12.2 always said, and so is everything else the vector space is made of: the dimension, the metric, the input preparation, and the vector-trimming decision below. Configuration is the declarative half — it says what this deployment intends to embed with, it is reviewed the way every other setting is reviewed, and a deployment that describes itself in `values.yaml` describes this part of itself there too.

What configuration does not do is start spending. Activation is a separate, explicit act through `mfctl` and the administrative endpoint: it reads the declared settings, computes the identity fingerprint from them, states the estimate below, takes the confirmation, and only then writes the immutable profile row and begins building. Nothing about editing a configuration file re-embeds a mailbox, and nothing at startup does either.

That split is what makes both halves true at once. The declaration is where a reviewer, a chart, and a `git diff` can see which model an instance uses; the profile row is where a stored vector's attribution lives, because a vector must remain attributable to what produced it long after the configuration that declared it was edited. Option C4 collapses the two and loses the second: a `values.yaml` edit would silently re-embed a mailbox at the next restart, and no stored vector could be attributed to anything, because the only record of what produced it would be a file that has since changed.

Two consequences follow from the declaration living in configuration. A declared identity that matches an already-registered profile activates that profile rather than creating a second one, which is what the fingerprint's unique index is for — so returning to a previous model is a switch rather than a duplicate. And a declaration that no activation has taken up yet is a valid state, not a startup failure: the instance serves lexical search, exactly as issue 432 describes, until someone activates.

**Authentication is configuration and is never part of the profile.** It has two shapes and a deployment carries whichever applies: an API key as a `SecretReference` in the sense ADR 0005 established, or a non-interactive Microsoft Entra credential where the endpoint is Azure OpenAI, where there is no secret to provision at all. The profile row holds neither, because the connection is a property of the deployment rather than of the vector space — which is what lets a key be rotated, or a deployment moved from a key to a managed identity, without a single vector becoming unattributable.

### A fallback is another way to reach one vector space, never a second one

Configuration declares an ordered chain of provider-and-model pairs rather than a single one, and the process falls to the next when the one before it is unreachable. What every pair in a chain must share is the identity above: the same model, the same dimension, the same metric, the same input preparation. OpenAI and Azure OpenAI both serving `text-embedding-3-small` is the case this exists for — the endpoint fails, the vector space does not — and a chain whose pairs disagree is refused at startup, naming the two pairs and the property they differ on.

The refusal is the point rather than a restriction to work around. A fallback with a different model does not produce a degraded vector; it produces a vector in a different space, and a distance computed against it is a number with no meaning. Silently writing those under the active profile would corrupt the index in a way that surfaces as slightly worse results rather than as an error, which is the hardest possible failure to attribute. Writing them under a profile of their own instead yields two partial generations while retrieval reads one, so mail embedded by the fallback would simply not be found.

An operator who genuinely wants a different model when the first is unavailable is asking for a second profile and a switch between them, which is issue 433's operation and is deliberately not automatic. While the whole chain is unreachable, embedding work waits in its bounded queue and retrieval degrades to lexical, as issue 432 already provides.

### The database's indexable dimension is a real ceiling, and trimming past it is an explicit decision

pgvector 0.8.2 stores a `vector` of up to 16000 dimensions but indexes only 2000 of them with HNSW; `halfvec` raises the indexable ceiling to 4000 at half precision. So a model is not merely large or small — it is indexable, or it is stored and searched exactly, which draft section 9.3 already establishes as correct but slower.

An `AllowTrimVectors` setting, declared beside the model, is what decides which. It is off by default, and with it off a declared model whose dimension exceeds what the column can index is refused at activation, naming the dimension and the ceiling, rather than quietly producing an instance whose semantic search never becomes fast. With it on, the profile's dimension is reduced to a supported one and that reduced number is what the profile's identity records — because a trimmed vector occupies a different space than the full one, and a profile that claimed the model's nominal dimension would be describing vectors that do not exist.

Where the provider can shorten natively, that is used in preference to trimming a returned vector. OpenAI's third-generation embedding models accept a requested dimension and return a shortened, already-normalized vector, which is a property of how those models were trained rather than an approximation of one; cutting a returned vector down in the adapter is the fallback for providers that offer nothing equivalent, and it renormalizes rather than leaving a vector whose length has quietly changed.

`halfvec` is not adopted here. It would let a 3072-dimension model stay indexable at full width, and that is a genuine option — but it is a second vector column, a second index path, and a second set of distance operators, and the case for it is one model rather than a class of them. It is recorded as the first thing to revisit if a model between 2001 and 4000 dimensions becomes the one the project wants.

### Activation counts before it spends

Activating a profile on an instance with stored mail computes what it is about to cost — the number of chunks and the summed input length, expressed as an approximate token count — reports it, and requires explicit confirmation. The count is a `COUNT` and a `SUM` over a table that already exists, so it is cheap enough to be unconditional. Where the estimate exceeds the ceiling from issue 434, activation is refused rather than started and throttled: a budget that only slows a run down is not a budget, it is a schedule.

### The previous generation is removed in bounded batches

Once the switch happens, the superseded vectors are deleted in bounded batches. Rolling back means activating the previous profile again — its row still exists and its identity is immutable, so what it means is unambiguous — and paying for that generation a second time.

Keeping the old generation would make rollback free, and it is refused because the thing being kept is personal data whose purpose ended at the switch. Doubling vector storage indefinitely to buy back a decision the operator confirmed against an estimate is the wrong side of storage limitation, and option E3's failure mode is an instance carrying two full generations forever because nobody ran the cleanup.

### A paid call is never the default, in production or in verification

The same reasoning reaches the test suite, where it has a specific consequence. Almost everything this feature needs proven — the dimension check, the uniqueness constraint, the per-profile index, the idempotent upsert, two generations coexisting, the switch, the bounded deletion — is proven against a real PostgreSQL and a deterministic in-repository embedding generator, at zero provider cost. What only a real provider can prove is much smaller: that the adapter speaks the provider's actual protocol, authenticates, classifies its failures, and returns the dimension the profile claims.

Those tests exist and are skipped by default. xUnit v3's `SkipUnless` on a static property is the mechanism, and the property reads an environment variable that nothing sets by default, so a developer running the suite and a routine continuous-integration run both pay nothing. The `Integration tests` workflow gains an input, defaulting to off, that turns them on and supplies the credential. Asking for them without a credential configured fails rather than skipping: a run the operator explicitly requested and which then quietly proved nothing is worse than a run that never started.

### Consequences

- Good, because a stored vector is attributable to the exact geometry and the exact boundary rules that produced it, through two records that each own one half and neither of which can drift from the vectors it describes.
- Good, because tuning chunk boundaries costs local computation instead of a provider bill, which is what makes it a change anyone will actually make.
- Good, because there is one notion of a generation rather than two, so no read path has a second field it must remember to consult.
- Good, because rotating a credential or raising a rate limit is an edit rather than a re-embed, and touches no profile at all.
- Good, because which model an instance embeds with is visible where every other setting of that instance is visible, so a chart, a review, and a `git diff` all see it.
- Good, because an operator cannot start a spend without being shown a number first.
- Good, because a fallback that would silently change the meaning of a stored vector is a startup failure rather than a runtime surprise.
- Neutral, because two axes of versioning exist rather than one. They have to be named and documented, and this record is where that starts.
- Neutral, because the estimate is approximate. Token counts are provider-specific and the number shown is derived from input length, so it bounds the order of magnitude rather than predicting an invoice.
- Neutral, because a declared identity and a registered profile are two records of the same thing. They cannot drift — the second is computed from the first — but the distinction has to be explained rather than inferred.
- Bad, because editing configuration is not enough to change model: an activation must follow, and an operator who does not know that will believe a change took effect when it did not. The documentation and the status command carry the whole weight of that.
- Bad, because a model above what pgvector indexes is either narrowed or left to exact search, so choosing a large model is a performance decision the operator has to make knowingly rather than a detail the system absorbs.
- Bad, because rollback after a model change is paid, not free. That is the price of not retaining a superseded generation, and it is a cost the confirmation step exists to put in front of the decision rather than after it.
- Bad, because the provider-contract tests do not run unless somebody asks for them, so an adapter can break against the real provider and no ordinary run will say so. Making them default would mean every contributor's suite spends the maintainer's credit, which is the worse of the two.

## Validation

- The migration that creates `embedding_profiles` carries the unique index over the identity fingerprint, and `email_embeddings` carries the uniqueness constraint on the chunk and the profile together. A review of the generated SQL is part of adding it, as `$add-migration` requires.
- Unit tests prove that the chunk content hash changes when the rule-set version changes and the text does not, which is the property the whole attribution argument rests on.
- Unit tests prove that activating a declaration whose identity already exists resolves to the existing profile rather than inserting a second, and that an attempt to edit an identity column is refused.
- Startup validation refuses a fallback chain whose pairs differ in provider model, dimension, metric, or input preparation, naming both pairs and the property they differ on. Unit tests cover each differing property, and cover the accepted case of one model reached through two providers.
- Startup and activation validation refuse a declared dimension above what the column can index while `AllowTrimVectors` is off, naming the dimension and the ceiling; with it on, unit tests prove that the profile's recorded dimension is the narrowed one rather than the model's nominal one, and that a vector shortened in the adapter is renormalized.
- The integration suite proves the dimension check, the per-profile index, two generations coexisting, and the bounded removal, against a real PostgreSQL and the in-repository generator, with no provider call.
- The provider-contract tests prove the adapter against the real provider, skipped unless the `Integration tests` workflow is dispatched with its input turned on and the credential present, and failing rather than skipping when the input is on and the credential is absent.
- Unit tests prove that activation refuses when the estimate exceeds the configured ceiling, and that the confirmation is required rather than assumed.
- `docs/` documents the profile identity, the two axes of versioning, and the activation sequence with what it costs, on the pages whose `describes:` markers cover the persistence layer and the operational procedure.

## Pros and Cons of the Options

### A1. Geometry and the boundary rules in one identity

- Good, because there is exactly one record to read to know what produced a vector.
- Good, because it needs no rule-set version anywhere else.
- Bad, because every boundary change becomes a new profile and a full paid re-embed, which prices a free local derivation as a provider bill.
- Bad, because it says a profile describes its vectors while its identity mixes two things with entirely different costs, which makes the record harder to reason about rather than easier.

### A2. Geometry alone, with the boundary rules in the chunk's identity

- Good, because attribution is complete without duplication: the chunk says what text and which rules, the profile says what embedded it.
- Good, because the two costs stay separate, so a boundary change re-embeds only what changed.
- Good, because it needs no new column to be correct — the vector already hangs on the chunk.
- Neutral, because it introduces a second axis of versioning that has to be named in documentation and in the operator-facing status output.
- Bad, because the chunk hash acquires a responsibility that is easy to forget: a hash covering only the text would silently report unchanged chunks after a rule change, and nothing about the hash's name says otherwise.

### A3. Geometry alone, boundary rules recorded nowhere

- Good, because it is the smallest decision available today.
- Bad, because re-chunking then changes what the vectors describe while every record claims nothing changed, and no later work can distinguish the two states.
- Bad, because it forecloses any claim that a profile or a chunk describes its own vectors, which is the claim everything downstream is built on.

### B1. Immutable identity, mutable lifecycle state

- Good, because a stored vector's attribution can never be edited out from under it.
- Good, because everything that legitimately changes over a profile's life — where it is in building, serving, or being superseded — has somewhere to be recorded without touching what its vectors mean.
- Neutral, because the row has two classes of column, and which is which has to be enforced rather than assumed.
- Bad, because that enforcement is code and a constraint rather than a property of the schema alone.

### B2. Wholly immutable row, lifecycle included

- Good, because attribution needs no rule about which columns matter.
- Neutral, because moving the operational settings into configuration removed this option's usual cost: no credential rotation or limit change forces a new profile under it either.
- Bad, because the lifecycle then needs a second table to record what a profile is currently doing, which is a row per profile in a table that exists only to say what the profile row could have said.

### B3. Mutable row with a generation counter

- Good, because tuning needs no new profile row at all.
- Bad, because two vectors under one profile identifier may be incomparable unless every read path also reads the generation, and the failure when one does not is a plausible-looking wrong answer rather than an error.
- Bad, because it is a second mechanism owning what the profile identifier already owns.

### C1. The database is authoritative, written through the administrative endpoint

- Good, because exactly one thing can create a profile, and it is a deliberate act by an operator.
- Bad, because which model an instance embeds with becomes invisible to configuration review, to a chart, and to a `git diff`, which is where every other setting of this deployment is read.
- Bad, because a declarative deployment cannot describe its own embedding model at all.

### C2. The database is authoritative, with a configuration bootstrap on an empty instance

- Good, because the case where activation is genuinely free is the one case where automation is safe.
- Bad, because it is a second, conditional writer to the same table, and it still leaves the model invisible in configuration on every instance that is not empty.

### C3. Configuration declares, activation stays imperative

- Good, because the model, the provider, and the vector-width decision are declared where every other setting of the deployment is declared and reviewed.
- Good, because declaring is free and activating is not, so the split matches the costs exactly: an edit proposes, an activation spends.
- Good, because a declaration whose identity matches an existing profile activates that one, so returning to a previous model is a switch rather than a duplicate.
- Neutral, because a declaration and a registered profile are two records of the same identity. They cannot drift, because the fingerprint is computed from the declaration and the row is written from it, but a reader has to know which one is which.
- Bad, because an operator who edits configuration and expects the change to take effect has to be told that it did not, which is one more thing the documentation owes.

### C4. Configuration is authoritative

- Good, because it is the most declarative option and needs no imperative step at all.
- Bad, because editing one value in `values.yaml` silently re-embeds an entire mailbox at the next restart, which is the exact failure the cost driver exists to prevent.
- Bad, because a stored vector would then be attributable only to a file that has since been edited, so nothing could say what produced it.

### D1. Estimate, confirmation, and a refusing ceiling

- Good, because the operator sees a number before agreeing, and a budget they configured actually binds.
- Neutral, because the estimate is approximate and says so.
- Bad, because an operator who genuinely wants to exceed the ceiling must raise it first, which is one more step at exactly the moment they are impatient.

### D2. Estimate and confirmation, ceiling limits rate only

- Good, because it never blocks an operator who has decided.
- Bad, because the ceiling then bounds how fast the budget is spent rather than whether it is, which is not what a budget is.

### D3. A warning in documentation and logs

- Good, because it needs no machinery at all.
- Bad, because the full re-embed of a mailbox starts with no number in front of the person who started it.

### E1. Remove the superseded generation in bounded batches

- Good, because derived personal data does not outlive its purpose.
- Good, because vector storage stays proportional to one generation.
- Bad, because rollback is paid rather than free.

### E2. Keep it for a configured window

- Good, because rollback inside the window costs nothing.
- Bad, because it doubles vector storage for the window and needs a retention justification for personal data whose purpose has ended.

### E3. Keep it until the operator removes it

- Good, because nothing disappears without a decision.
- Bad, because the default outcome is an instance holding two full generations indefinitely, which is the state nobody chose.

## More Information

- Issue 424 records the decision. Issue 425 derives the chunks and owns the hash this depends on, issue 426 creates the two tables, issue 427 adds the generator port and the first adapters, issue 430 builds the per-profile index at activation, issue 433 performs the generation switch, issue 434 owns the ceilings, and issue 435 gives `mfctl` the commands. Issue 436 is the parent.
- Two questions are deliberately left open beside this record. Issue 478 decides what an embedding is derived from, and specifically whether attachment text ever joins message text. Issue 479 holds provider support beyond the two the first release takes; both are `Parked`.
- ADR 0001 governs the ports the profile and vector stores are reached through, ADR 0002 governs how the declaration, the endpoint, and the credential are bound and mapped, and ADR 0005 governs the `SecretReference` that carries an API key.
- Draft sections 9.3 and 12.1 describe the column shape and the chunk coordinates this builds on. Section 12.2's statement that the embedding model is a configuration value is upheld by this record, and narrowed: a configuration value declares the model, and an activation is what makes it the one vectors are produced under.
- The first release reaches OpenAI and Azure OpenAI through `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`, the abstraction Microsoft Agent Framework consumes, so the chat model the answering feature needs is served by the same provider wiring rather than a second one.
- Revisit this decision if a model between 2001 and 4000 dimensions becomes the one the project wants, which is what `halfvec` exists for and the first thing to reconsider; if a provider appears whose vectors are comparable across model versions; or if an evaluation capability makes retaining a superseded generation worth its storage and its retention justification.
