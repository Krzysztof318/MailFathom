# What a semantic search costs

<!-- describes: backend/src/Infrastructure/Persistence/Embeddings/EmailVectorSearchIndexReader.cs, backend/src/Infrastructure/Persistence/Embeddings/Configurations/EmailEmbeddingConfiguration.cs -->

MailFathom ranks mail by vector distance exactly: every eligible message's nearest embedded passage is measured against
the query, and nothing approximates that. [Email search](../features/email-search.md#hybrid-retrieval) states the
behaviour and why the caller's filters join the ranking rather than trailing it. This page states what that costs on a
mailbox the size an owner is expected to reach, measured rather than reasoned, and what was decided against the numbers.

## What was measured, and against what

A corpus was built on this repository's own schema — every migration applied whole — and populated to the scale
[ADR 0014](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
names as the one where an account predicate stops being selective on its own:

| | |
|---|---|
| Deployment | 3 owners, 4 accounts, 16 folders, 150 000 messages |
| The measured owner | 2 accounts, **100 000 messages**, **259 034 vectors** |
| Passages | 389 396 chunks, one vector each, 1536 dimensions, cosine |
| `email_embeddings` | 35 MB heap, **3081 MB TOAST**, 3137 MB in total |
| Server | `pgvector/pgvector:0.8.6-pg18`, PostgreSQL 18, pgvector 0.8.6 — the image the Compose deployment and the Helm chart both name |
| Settings | the server's own defaults, which is what every deployment asset here ships: `shared_buffers` 128MB, `work_mem` 4MB, `maintenance_work_mem` 64MB, `hnsw.ef_search` 40, `hnsw.iterative_scan` off |

Two things about that corpus are worth saying plainly. The vectors are synthetic — each is the normalized sum of three
of 512 random directions, so they are clustered on the unit sphere rather than uniformly random — which makes them a
fair load for a scan, whose cost does not depend on what a vector contains, and an optimistic one for an approximate
index, which does better on clustered data than on random. And messages carry 2.6 passages on average against the live
deployment's observed 1.6, so the corpus is heavier per message than the mailbox it models.

**Buffer counts are the figure to read, not the timings.** The machine was under heavy concurrent load throughout, so
every duration here is an upper band; two independent rounds agreed on buffers to within half a percent while their
timings differed by up to a factor of two.

A buffer count is a page *pin* rather than a distinct page, which is worth reading correctly before the tables below.
A 6152-byte vector is four TOAST chunks, each fetched through its own descent of the TOAST index, so one vector costs
several pins of which nearly all find their page already in the 128 MB pool — the four chunks share one page, and the
index above them is small enough to stay resident. What crossed that boundary in the full-mailbox search is the `read`
half of its 2 152 957 pins: **275 986 pages, or 2.2 GB** — the owner's share of the 3081 MB of out-of-line vectors,
fetched once. That 2.2 GB is served by the operating system's page cache on this host rather than by the device, which
is what the concurrency section below turns on.

## What the exact ranking costs

The query is the one `EmailVectorSearchIndexReader` composes, taken from the provider verbatim, with the owner's
accounts, the folders configuration admits, and the junk folder withheld.

| The caller asked for | Messages ranked | Buffers | Time |
|---|---|---|---|
| everything the owner may read | 85 715 | 2 152 957 | 7.9 s |
| one folder and a three-month range | 11 856 | 390 689 | 0.96 s |
| one sender | 200 | 8 332 | 0.04 s |
| *the same, over every owner's mail* | 128 573 | 3 259 018 | 14.3 s |

Three readings come out of that table.

**An unfiltered semantic search over a full mailbox is seconds, not milliseconds.** Eight seconds and two million
buffers is not a latency a caller waits on; it is a latency a caller times out on.

**Ownership narrowing helps exactly as much as it removes mail, and no more.** The last row is the first one with the
owner predicate taken out and nothing else changed. The narrowed query moves 0.661 of the buffers the unnarrowed one
does, where the owner's share of the eligible vectors is 0.665 — proportional, which is another way of saying the
predicate changes the size of the scan and not its shape.

**The caller's own filters are the effective lever.** A folder and a date range — an ordinary way to ask — is five
times cheaper than no filter at all, and a sender is two hundred and fifty times cheaper. What is expensive is the
request that narrows nothing.

## Where the cost is, which is not where it looks

A 1536-dimension `vector` is 6152 bytes, so PostgreSQL stores every one of them out of line: the table is 35 MB of rows
and 3081 MB of TOAST. Separating the two halves of the work says where the time goes:

| | Buffers | Time |
|---|---|---|
| read all 389 396 rows, vector untouched | 377 | 0.05 s |
| the same rows with every distance measured | 1 582 146 | 2.2 s |

Reading the rows is free. Fetching the vectors is the whole of it — and the arithmetic is not the cost either: 389 396
cosine distances at 1536 dimensions is under a GFLOP, which is tenths of a second of one core. **An exact vector
ranking is an I/O problem.**

That has a consequence for the query rather than for the feature. The ranking's ordering key is a correlated subquery
re-entered once per candidate message; the same exact answer, written as one join and a `min()` grouped by message,
reads each vector once:

| The same result, two shapes | Buffers | Time |
|---|---|---|
| the correlated minimum the reader composes today | 2 152 957 | 7.9 s |
| one pass, grouped by message | 921 874 | 1.7 s |

**4.7 times the speed for an identical answer**, and near enough the floor above. That is a defect in the query rather
than in exactness, and it is not fixed here: the flat form is the projection
`EmailVectorSearchIndexReader`'s own remarks record the provider refusing to translate, so reaching it means writing the
statement by hand. [Issue #1252](https://github.com/Krzysztof318/MailFathom/issues/1252) carries it.

## What an approximate path would buy, and what it would cost

An HNSW index was then built over the same corpus, partial on the serving profile exactly as MailFathom built one until
this change, and every question below was asked with it in place.

**The reader's own query never chose it.** With the index present the plan for a full-mailbox search does not mention
it — the same nested loop, 2 175 814 buffers and 7.6 s, against 2 152 957 and 7.9 s without. That is the claim
`EmailVectorSearchIndexReader` has made in prose since it was written, now measured at this scale rather than at the six
hundred rows a unit test could afford.

**The shape the index does serve is a different query.** Asked for the fifty nearest vectors on `email_embeddings`
alone, with no join and no filter, it answers in 22 ms from 1297 buffers. It answers with forty-one rows rather than
fifty, because `hnsw.ef_search` is 40 and a window cannot be wider than the candidate list the scan kept; all forty-one
are in the exact top fifty.

**Post-filtering that window is where the path fails.** The caller's structural filters have to join the ranking rather
than trail it, and an index that ranks the whole table can only be made to obey them by ranking first and filtering
after. Over ten query vectors, each asking for a five-hundred-row window:

| Of one window | median | worst |
|---|---|---|
| vectors returned | 40 | 40 |
| left after the owner predicate | 27 | 24 |
| left after owner, `INBOX`, and a three-month range | 4 | 0 |
| left after owner and one sender | **0** | 0 |

A search that answers fifty results unfiltered and four once the caller says *in my inbox, this summer* is not an
approximation of the exact ranking; it is a different feature. This is exactly the failure the reader's remarks
predicted — fewest results returned precisely where the caller narrowed most.

**`hnsw.iterative_scan` removes that failure and hands the planner the choice.** With `relaxed_order` and a
twenty-thousand-tuple scan bound, pgvector keeps producing candidates until the filter is satisfied, and the plan for a
weakly filtered join — this owner, not the junk folder — does drive from the index: 200 passages in 249 ms from 10 733
buffers, against the 7.9 s the exact ranking spends on the comparable one. That is a real win, and it is the only
one.

It stops at the first selective filter. The moment a predicate is worth driving from, the planner drives from it and
leaves the index alone: one folder and a three-month range became a parallel scan and a top-N sort, 440 ms from 307 691
buffers, and one sender drove from `ix_stored_emails_sender` and finished in 23 ms from 5008. So the index is chosen for
the request that asked for the least, and not chosen at all for the requests a caller actually makes.

And `relaxed_order` means what it says: the rows arrive approximately sorted. The two rankings are fused by rank
position, so a distance order that is nearly right is a fused order that is quietly wrong, and two identical requests
need not agree.

**What keeping the index costs is conditional on none of that.**

| Keeping one profile-partial HNSW index | |
|---|---|
| index size | 3073 MB beside a 3137 MB table — `email_embeddings` nearly doubles, to 6250 MB |
| build, at the 64 MB `maintenance_work_mem` every deployment asset here ships | **45 min 20 s**, the graph spilling out of memory after 9218 of 389 396 tuples |
| one embedding insert, no index present | 0.17 ms |
| one embedding insert, index present | **20.4 ms** |

The last row is the one that decides it. Every vector a backfill writes pays it, and a reindex writes one per passage:
on this corpus the 389 396 inserts move from 68 seconds of database time to two and a quarter hours — on the path
[ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
already names as the expensive act of changing model.

## Several owners searching at once

The reader's query was then run for sixty seconds at one, two, and four concurrent clients, each transaction drawing one
of sixty query vectors and one of the three owners at random.

| Clients | Searches completed | Mean latency | Throughput |
|---|---|---|---|
| 1 | 14 | 4.7 s | 0.21/s |
| 2 | 21 | 5.9 s | 0.33/s |
| 4 | 56 | 4.5 s | 0.83/s |

The latency is a blend and the deviation is as wide as the mean, because the mix draws the three owners equally often
and only one of them holds 100 000 messages: a run is a mixture of eight-second and two-second searches rather than a
sample of one number.

What the table says is that concurrency is not where this breaks. Four searches at once completed four times as many
searches in the same wall clock at the same latency each. The 2.2 GB the scan actually reads fits in this host's page
cache, so four processes reading the same pages cost four times the CPU and no more device I/O. So the figure to carry
forward is the per-query one — **an unfiltered semantic search is seconds, and four at once are still seconds each**.

**That last sentence holds only while the vectors fit in memory, and it is the one boundary worth re-measuring at** —
not a number of callers. Where it lies follows from the measured size rather than from a second measurement: 3081 MB of
TOAST over 389 396 vectors is about 8 KB of table per vector, so a deployment's embedding table is roughly its vector
count times that, and everything else on the host competes for the same cache. Past that point each search reads from
the device instead of from memory, the reads are chunk-at-a-time through the TOAST index rather than one sequential
stream, and concurrent searches begin contending for one queue — so latency stops being flat in the number of callers,
which is the property this section measured on the near side of the line. The levers there are fewer bytes per vector
before anything else: `halfvec` halves the width and a narrower model quarters it, and both go straight at the only
cost an exact ranking has.

## The decision

**The ranking stays exact, and MailFathom builds no vector index.** Four measured grounds, in the order they settle it:

1. **Nothing read the index.** The reader's plan does not mention it at this scale, and its cost with the index present
   is its cost without, to within one percent.
2. **The approximate path a caller would actually take does not work.** Post-filtering a window returns a median of four
   results for an ordinary folder-and-date request and none at all for a sender.
3. **The path that does work is chosen only where nothing was asked, and it costs determinism.** `relaxed_order` wins on
   the weakly filtered request, loses the planner's choice on every selective one, and returns rows the fused ordering
   cannot rely on.
4. **Keeping it costs a hundredfold on every embedding write**, three gigabytes of storage, and three quarters of an
   hour of blocked writes per activation, for the query in point 1.

One thing pulls the other way and is recorded rather than argued away. The exact ranking as written is slower than the
exact ranking needs to be: the same answer, read in one pass instead of as a correlated minimum per message, is 4.7
times faster and needs no index. An approximate path should therefore be judged against 1.7 s rather than against
7.9 s, which makes its remaining win narrower still. That rewrite is a change of its own, carried by
[issue #1252](https://github.com/Krzysztof318/MailFathom/issues/1252).

What would reopen this: a mailbox whose embedding table no longer fits the deployment's page cache, a pgvector release
in which a filtered index scan keeps a total order rather than a relaxed one, or a decision that the semantic half of
the fusion may be non-deterministic. None of those is true today.

## What the removal costs the profile lifecycle

[ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
makes activation the act that materializes a declared profile and pays for it, and building the profile's index was part
of that act: activation created it, cancelling a reindex dropped the abandoned generation's, and upkeep dropped a
superseded one's. Four things follow from taking it out, and the last is not settled here.

- **Activation writes a row and asks for a backfill pass, and nothing else.** It no longer has a step that can fail
  against the database, so the error code that named that failure — `33001`, embedding vector index unavailable — is
  gone with it. The number is retired rather than reused, as every allocated code is.
- **Cancelling a reindex and superseding a generation remove vectors and nothing else.** Both were already removing the
  vectors; only the index removal is gone.
- **The serving role no longer has to own `email_embeddings`.** Creating and dropping an index at runtime was the whole
  reason it did, so this narrows what a deployment has to grant. [The database schema](../operations/database-schema.md)
  is where that grant is stated, and it changes there.
- **The `AllowTrimVectors` ceiling loses what it protected.** ADR 0006 refuses a declared dimension above 2000 unless
  the deployment accepts a narrowed vector, because 2000 is what an HNSW index covers; a `vector` column stores 16 000.
  With no index built, a 3072-dimension model is stored and searched exactly as a 1536-dimension one is, and the refusal
  now declines a model the deployment could run in exchange for nothing. Deciding what replaces it amends ADR 0006,
  which this change does not do; [issue #1253](https://github.com/Krzysztof318/MailFathom/issues/1253) carries it.

## Partitioning `email_embeddings` by owner stays deferred

[ADR 0014](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
deferred partitioning by owner against a measurement that did not exist, and named what deferring costs: under the
append-only migration rule, partitioning afterwards is a table rewrite rather than a migration. The measurement is here
now, and it says the deferral holds.

Partition pruning would remove the rows of owners the query is not for, before they are read. Those rows are the free
half: reading all 389 396 rows of the profile without their vectors is 377 buffers. What costs 1 582 146 buffers is
fetching a vector, and a vector is fetched because its message survived the owner predicate — so a partition boundary
would not remove a single one of them. The owner predicate already delivers what pruning would: with it applied the
query moves 0.661 of the buffers, where the owner's share of the eligible vectors is 0.665.

Partitioning therefore stays deferred, now against a number rather than against the absence of one, and the number is
that it would save single-digit megabytes on a query that moves gigabytes.
