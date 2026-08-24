---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-23
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Select the content backend once per deployment, write the object before the unit of work that points at it, and name an object by the write that produced it

<!-- describes: backend/src/Application/EmailContent/Storage/**, backend/src/Infrastructure/Persistence/Emails/**, backend/src/Infrastructure/ObjectStorage/** -->

## Context and Problem Statement

`IEmailContentStore` was written so that a second implementation would be possible, and its own remarks say the store is expected to move from a PostgreSQL table to object storage without a use case noticing. What they do not settle is the part that makes the move hard, and it is not the client library.

Every write on that port takes the caller's `IPersistenceSession`, and the port says why: a content write commits or rolls back together with the metadata row it belongs to. A bucket cannot join that transaction. [ADR 0001](0001-application-owned-repositories-for-persistence-ports.md) names object storage explicitly among the things a transaction must not span, and prescribes durable state transitions, idempotency, and compensating operations instead. Removing the transaction therefore removes the only mechanism that currently keeps a payload and its row one fact, and something has to be put in its place before any code is written against the assumption.

The decision question is eight questions that an implementation would otherwise answer by accident, in a way that is expensive to reverse once mail is in a bucket: what selects the backend, what replaces the shared transaction, how an object is named, whether the port stays byte-based, whether the payload is encrypted before it leaves the process, whether both stores may be authoritative at once, whether erasure reaches the bucket synchronously, and which client reaches the endpoint.

Recorded on issue [#1124](https://github.com/Krzysztof318/MailFathom/issues/1124), which gates every other child of its parent: [#1125](https://github.com/Krzysztof318/MailFathom/issues/1125) through [#1132](https://github.com/Krzysztof318/MailFathom/issues/1132). No numbered specification backs it; an earlier design described the staged move, and this record corrects it where it described a port that was never built.

## Decision Drivers

- **The invariant that must never break is that a committed row points at a readable object.** Its violation is mail that cannot be read, and for an outgoing message or a draft nothing else holds the bytes. Every other failure this design can produce is recoverable; that one is not.
- **The privacy obligations in root `AGENTS.md` are least tolerant of mail content outliving the record that said it could be held.** Whatever replaces the cascade delete has to be a mechanism rather than a convention.
- **No use case, tool, or domain type may learn which store answered.** The port's contract is the same for both backends, including the four payload kinds' differing write semantics.
- **ADR 0001 forbids a database transaction held open across a network call.** A design that satisfies the first driver by widening the transaction has answered the wrong question.
- **The recorded byte length and SHA-256 are already the integrity contract**, and [#1126](https://github.com/Krzysztof318/MailFathom/issues/1126), [#1128](https://github.com/Krzysztof318/MailFathom/issues/1128), and [#1129](https://github.com/Krzysztof318/MailFathom/issues/1129) all rest on them. Anything that makes the recorded digest stop describing the stored object breaks three children at once.
- **An operator must be able to judge the exposure before switching**, which means the confidentiality posture is stated rather than implied.
- **The deployment this project recommends is self-hosted** — [#1132](https://github.com/Krzysztof318/MailFathom/issues/1132) ships a single-node object store beside the product — so no answer here may assume a managed cloud provider's ambient identity, region, or key management.

## Considered Options

The eight axes are independent, and an option on one does not constrain an option on another.

1. **What selects the backend:** one setting per deployment; one setting per mail account or owner.
2. **What replaces the shared transaction:** write the object first and reclaim an orphan; write the row first and repair a missing object.
3. **How an object is named:** content-addressed by the recorded SHA-256; addressed by the identity of the row that owns it; minted by the write that produces it.
4. **Whether the port stays byte-based:** keep `ReadOnlyMemory<byte>`; change the port to streaming put and open-read.
5. **Whether the payload is encrypted before it leaves the process:** client-side under the [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) key ring; server-side by the bucket; neither.
6. **Whether both stores may be authoritative at once:** one authoritative store per deployment; one authoritative store per payload.
7. **Whether erasure deletes the object synchronously:** with the transaction; after it commits; by a sweeper alone.
8. **Which client reaches the endpoint:** `AWSSDK.S3`; `Minio`; SigV4 written by hand over `HttpClient`.

## Decision Outcome

### 1. One backend per deployment, selected for new writes only

The backend is one `ContentStorage` setting for the whole deployment, defaulting to the database, as [#1125](https://github.com/Krzysztof318/MailFathom/issues/1125) already assumes. It is not selectable per mail account or per owner.

[ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) hangs ownership on the mail account and answers a second tenancy with a second instance; a per-account backend would open a second axis of storage tenancy that decision closed. The process-wide bounds say the same thing more concretely: `StoredContentCeiling` bounds *the whole deployment's* stored content and `RawMimeMemoryBudget` bounds the sum across concurrent work units, and neither number names one thing any more once two accounts store into two different places. `#1125`'s health check reports one bucket, and `#1127`'s reclamation sweeps one key space.

**The selection may change at any time, in either direction, and it decides only where the next write goes.** It never describes what is already stored, because the row does — the discriminator [#1126](https://github.com/Krzysztof318/MailFathom/issues/1126) adds is the authority for each payload. Changing the setting is therefore inert with respect to existing content: nothing moves, nothing is re-encoded, and every existing payload is still read from the store its own row names. Moving what already exists is the deliberate, separately gated work of [#1128](https://github.com/Krzysztof318/MailFathom/issues/1128) and [#1129](https://github.com/Krzysztof318/MailFathom/issues/1129) — which is what [#1130](https://github.com/Krzysztof318/MailFathom/issues/1130) means by switching being a move rather than a setting.

The one thing an operator may not do is take the endpoint configuration away while object-backed rows exist. That is not a configuration error a binder can catch, because it depends on the database; it is a **readiness condition** the health check reports, so a deployment whose bucket has been removed or become unreachable is unhealthy rather than silently unable to read a share of its own mail. Startup does not query for it: the host must not depend on a migrated database to start.

### 2. The object is written first; the orphan is the accepted failure

A content write puts the object, and only then commits the row that points at it:

1. The caller hands the payload to the port **before it opens its unit of work**, and receives back what was stored: the backend, the locator, the byte length, and the SHA-256.
2. The adapter computes the byte length and the SHA-256 and hands the object to the endpoint, with that digest sent as the request's own checksum so the endpoint rejects a corrupted upload rather than storing one. **This happens with no database transaction open across it**, which step 1 is what guarantees rather than hopes for.
3. The caller stages its unit of work through `IPersistenceSession` as it does today and passes what step 1 returned to the port's write method. The row — the discriminator, the locator, the length, the digest — is staged and committed with the rest of that unit of work.

Crash outcomes, which are the same for every payload kind:

| Crash point | Bucket | Database | Outcome |
|---|---|---|---|
| Before or during the put | no object | no row | Nothing stored. The caller sees the write fail, and its whole unit of work rolls back, so the metadata row does not exist either. For incoming mail the next synchronization run fetches the occurrence again. |
| After the put, before the commit | object | no row | An orphan. No reader can observe it, because nothing points at it. Reclaimed by [#1127](https://github.com/Krzysztof318/MailFathom/issues/1127) once it is older than the configured age floor. |
| After the commit | object | row | Correct. |

**There is no window in which a committed row points at an absent object**, which is the invariant, and it is the direction the ordering is chosen for. The reverse ordering trades a recoverable failure for an unrecoverable one: a row committed before its object leaves a period during which a crash produces mail that cannot be read, and for an outgoing message or a draft nothing else in the system holds those bytes.

The constraint in step 2 is the sharp one, and the split in step 1 is what this record had to add to satisfy it. [#84](https://github.com/Krzysztof318/MailFathom/issues/84) settled the first half: a session now opens no transaction at `BeginSessionAsync`, and the first write to join it opens one instead, so a caller with work to finish outside the database does that work before the first join. **#84 remains a prerequisite of #1126** for that reason.

What it cannot deliver on its own is the ordering *within* a unit of work, and the reason is that every one of the four callers has the same shape: the repository that owns the payload runs first because it is what mints the identity, and the port is called afterwards with the identity in hand. `MailboxSynchronizer` calls `UpsertMetadataAsync` before `SaveContentAsync`, `MailOutbox` calls `OpenAsync` before `SaveOutgoingContentAsync`, `RecurringMailSubmission` calls `DeclareAsync` before `SaveRecurringSendDraftAsync`, and `MailDraftBook` calls `OpenAsync` or `ReviseAsync` before `SaveMailDraftContentAsync`. Each of those joins the session, so a transaction is open by the time the port is reached — and for the incoming path it is open for a reason nothing here may take away, because `UpsertMetadataAsync` writes the search document with a set-based update that exists precisely to keep an existing body out of memory, and that update is atomic with the rest only inside the caller's transaction. Scoping the session's transaction to `SaveChanges` would therefore fix three paths and break the one that carries the most mail.

So the port is split rather than the transaction: a preparation the caller performs **before** it opens its unit of work, and a write that stages a row from what the preparation returned. That is the shape step 1 describes, and it is the reason §3 no longer names an object after the row that owns it — at preparation time no row exists yet. The same split answers the caution this paragraph used to carry: `SaveContentAsync`'s `ExecuteUpdateAsync` on the database backend stays exactly as it is, inside the caller's transaction, because the database backend's preparation touches no store at all and its write is the only half that runs.

The four payload kinds keep the write semantics the port already states, and the split is what makes each of them fall out rather than each of them need a rule.

**One preparation writes one object under one key that nothing else will ever be written to.** The key is minted at preparation time and is unique to that preparation, so no two payloads and no two attempts ever contend for it. The put is still **conditional on the object not existing**, but as a bound on the design rather than as its mechanism: it is the assertion that a key was in fact fresh, and an endpoint that refuses one is telling this system something it believes to be impossible. That refusal is a failure and is reported as one.

`OptimisticConcurrencyRetryPolicy` replays the caller's whole unit of work, and the split is what takes that replay off the object path entirely: the preparation ran before the policy was entered, so every attempt stages the same locator over the same object and no attempt writes to the endpoint at all. This is the property the earlier shape had to buy by treating a refused put as success, and it is now had by construction — nothing repeats, so nothing has to be forgiven for repeating.

- **Incoming (`SaveContentAsync`)** — idempotent, and stays so: re-synchronizing an occurrence prepares a fresh object and repoints the row at it. The superseded object becomes an orphan, which is the same outcome #1127 already sweeps.
- **Outgoing (`SaveOutgoingContentAsync`) and the recurring send's draft (`SaveRecurringSendDraftAsync`)** — write-once, and the port says why: a retry must transmit the bytes an earlier attempt may already have begun transmitting. The write leaves an existing row exactly as it is, so a repeated request that resolves to a record already carrying a message keeps that message and abandons the object its own preparation wrote. That abandoned object is an orphan by design, and it is doubly safe: nothing ever pointed at it, so nothing can ever read it.
- **The mail draft (`SaveMailDraftContentAsync`)** — the one payload that overwrites, and it needs no special rule any more. Every revision is prepared under a key of its own because every preparation is, so a failed commit leaves the row pointing at the previous revision's object, which is intact, and a committed one leaves the superseded object an orphan. This is what the earlier shape had to state as an exception for the draft alone, and it is now the behaviour of all four kinds.

The cost of the split is stated rather than argued away: **an abandoned preparation is an orphan on a path that used to produce none.** The earlier shape produced an orphan only when a crash fell between the put and the commit; this one also produces one whenever a unit of work is abandoned or resolves to an existing payload. Every one of them is invisible to every reader, bounded by the same age floor, and removed by the same sweep, so what changes is #1127's expected volume rather than its correctness.

### 3. An object is named by the write that produced it, never by its content and never by the row that ends up pointing at it

The key is `<configured prefix>/<payload kind>/<identifier minted for this write>`, and the identifier is a version 7 UUID. It is not the SHA-256, and it is not the identity of the owning row.

Naming an object after its row is the shape this record originally took, and §2 is why it could not survive: the identity of the row is minted by the repository that owns it, the repository runs inside the caller's unit of work, and by the time it has run a transaction is open — which is exactly what the object write may not happen inside. A key that needs the row's identity forces the put after the row exists; a key minted by the write itself is free of it, which is what lets the put move in front of the whole unit of work. The property that made row-addressing attractive is kept regardless, because it never came from the *shape* of the key: erasure deletes the object the row names, and reclamation is still a set difference over a prefix, both of which read the locator rather than derive it.

Content addressing would make two identical payloads share one object, and that is what rules it out: erasure could then no longer delete an object when it deletes a row. It would first have to prove that no other row points at the same content, which is a durable reference count that must stay correct across two stores and across every crash the previous section enumerates. That converts the most privacy-critical operation in the system into the most fragile one, and it makes #1127's sweep undecidable from a listing — "an object no row points at" becomes "an object no row points at and none ever will". What content addressing buys in exchange is deduplication that is worth very little here: [ADR 0008](0008-copied-message-local-identity.md) already makes the same message in two folders two rows, and ADR 0014's single tenancy means near-duplicate mail across accounts is uncommon.

Two properties follow and are part of the decision:

- **The row stores the locator the adapter produced, and the adapter has no way to recompute one.** Under row-addressing this was a discipline worth keeping; here it is arithmetic, because nothing about the row determines the key. The scheme above is therefore an adapter detail in the strongest sense — changing it costs nothing for content already stored, since every existing row carries its own key and no reader ever derives one.
- **The key prefix is the reclaimer's whole authority.** #1127 lists and deletes within the configured prefix and never outside it. Two deployments may therefore share one bucket only under disjoint prefixes, and MailFathom cannot verify that they do: a shared prefix means one deployment's reclamation deletes the other's mail. This is an operator obligation that #1130 and #1132 state plainly rather than a configuration MailFathom validates.

### 4. The port stays byte-based

`ReadOnlyMemory<byte>` in and `StoredEmailContent` out, unchanged, for both backends.

Streaming the put would not remove the buffering it appears to remove. A payload is held whole today between the fetch that reads it and the commit that stores it, because IMAP delivers a message as one message and the digest is computed over all of it; `MailboxSynchronizer` is already holding those bytes when it calls the port. What actually bounds that memory is `RawMimeMemoryBudget`, and it is the better instrument, because it bounds the *sum* across concurrent work units in fair order — which streaming does not do, and which is the number that would otherwise scale with configured concurrency.

Where streaming genuinely pays is a read path that serves a whole message to a network client without buffering it, and no such path exists: nothing today streams raw MIME outward. **This is the condition that reopens the question** — an IMAP gateway or an attachment download that serves bytes straight to a socket earns a streaming *read* overload at that point, and the write half stays as it is regardless, because the digest and the length must both be known before the row is written.

An earlier design described the port as offering streaming put, open-read, existence, and delete operations. It never did, and this record states what the port is rather than leaving that description standing to be read as a plan.

### 5. Neither backend encrypts mail content in MailFathom's own process

The object is written as the same bytes the `bytea` column holds. Confidentiality in the bucket rests on transport TLS to the endpoint, on the endpoint's own server-side encryption where it offers one, and on a credential scoped to the one bucket and prefix.

The decisive reason is the integrity contract rather than a judgement about ciphers. #1126 verifies an object against the SHA-256 the row already records, #1128 refuses to repoint a row at an object it could not verify against that digest, and #1129 keeps the digest on a released row so the object stays checkable after the original bytes are gone. Client-side encryption makes the recorded digest stop describing the stored object, so all three would need a second digest and a second verification path — and the first thing that goes wrong in that design is that a plaintext digest and a ciphertext digest get compared. Encrypting only the object backend would additionally make the two backends differ in confidentiality, which contradicts the port's promise that no caller learns which store answered.

The honest cost is stated rather than argued away: **a deployment that points this at a hosted object store hands that provider readable mail**, which is a different exposure from a self-hosted PostgreSQL volume, and server-side encryption under a provider-held key does not change who can read it. That is why [#1132](https://github.com/Krzysztof318/MailFathom/issues/1132) ships a self-hostable object store beside the product, and why #1130's documentation says what an operator is choosing.

The seam is kept rather than closed. If mail content is ever sealed at rest, it is sealed in **both** backends at once, as one decision under the ADR 0005 key ring covering the `bytea` column and the object alike, and it records the ciphertext digest beside the plaintext one so the plaintext digest stays the verification contract. That is a separate ADR and this one does not take it. It is unrelated to [#75](https://github.com/Krzysztof318/MailFathom/issues/75), which is about mail that *arrives* encrypted: MailFathom stores that ciphertext as it received it under either backend, so #75 is indifferent to this decision.

### 6. One authoritative store per payload, and a deployment may hold both indefinitely

There is no deployment-wide answer to which store is authoritative, and no deadline by which a mixture must be resolved. Each payload has exactly one authoritative store and its own row names it.

The one duplicated state is the one #1129 defines: after the copy has verified an object and repointed the row, the `bytea` payload is still present but is no longer authoritative. It is a retained duplicate held for the configured safety interval, and it is read only as a fallback.

What a read does when the object is absent but the `bytea` payload is not: **it serves the `bytea` payload, records the fallback, and raises a repair request.** Refusing would be a self-inflicted outage over bytes the deployment still has. Once the payload has been released, the same situation returns the existing content-unavailable outcome, which the port already grades per payload kind — an ordinary answer for incoming mail, a defect for an outgoing record or a draft. The inverse case needs no rule: an object whose row says the payload is database-backed is an orphan, and no read looks for it.

### 7. Erasure deletes the object after the transaction commits, and the sweeper is the guarantee

Both mechanisms exist and they answer different failures. A deliberate deletion path — retention, an owner erasure, an authored delete, a tombstoned occurrence — deletes the object **after** the transaction that removed the row has committed. A failure to delete it is recorded rather than swallowed, and the object is then an orphan that #1127's bounded, resumable reclamation removes.

Deleting inside the transaction is impossible under ADR 0001 and would be wrong anyway: deleting before the commit destroys mail whose deletion then rolls back, which is irreversible loss on a transient failure. A sweeper alone is a weaker promise than [#131](https://github.com/Krzysztof318/MailFathom/issues/131) and [#170](https://github.com/Krzysztof318/MailFathom/issues/170) make and than an operator answering a data subject can repeat.

**What this costs those two issues is the shape of the promise, and it has to be documented as such:** the record is gone with the transaction, and the bytes are gone immediately afterwards in the ordinary case and within one reclamation interval in every other. That makes the reclamation interval and #1127's age floor privacy-relevant configuration rather than housekeeping, and the retention documentation states the bound rather than implying that deletion is instantaneous.

One consequence is sharper than the rest and belongs to #1127. `EmailMessageContentConfiguration` declares `DeleteBehavior.Cascade`, so deleting a stored email removes its payload row today without any application code running. A cascade removes the *pointer* to an object without the deletion path ever seeing the locator. So: **a deliberate erasure collects the locators inside the transaction, before the rows go, and deletes the objects after it commits.** A cascade reached by any other path is permitted to leave its object to the sweeper — that is what the sweeper is for — but no path that exists to erase mail may rely on it.

### 8. `AWSSDK.S3`, with every ambient resolution disabled

Chosen on shape, and the dependency graph settled it in the opposite direction from the intuition about SDK size. `AWSSDK.S3` is Apache-2.0 and declares exactly one dependency, `AWSSDK.Core`, also Apache-2.0 — two packages from one vendor, under a licence the acceptance policy in `THIRD_PARTY_LICENSES.md` already allows. `Minio` is Apache-2.0 as well but brings `System.Reactive`, `CommunityToolkit.HighPerformance`, `System.IO.Hashing`, and the Microsoft logging and dependency-injection abstractions with it, and nothing else in this repository takes a Reactive Extensions dependency. Writing SigV4 by hand adds no package at all and is refused for the opposite reason: request signing is security-sensitive code this project would own for ever, and getting it wrong is an authentication failure rather than a bug.

The deciding argument beyond the graph is what "S3-compatible" means. Compatibility is defined by what the reference client does, so a disagreement between a third-party client and the AWS client is a defect in MailFathom against every vendor. [#1131](https://github.com/Krzysztof318/MailFathom/issues/1131) verifies the adapter against Silo, and that verification is worth more when the client is the one compatibility is claimed against.

Every property #1125 requires is available on the configuration surface and each was checked against the package rather than remembered: `ServiceURL` and `ForcePathStyle` for a custom endpoint with path-style addressing, `Timeout`, `MaxErrorRetry` and `RetryMode` to switch the SDK's own retry off so nothing nests over the repository's bounded retry, `HttpClientFactory` to supply the client registered under the lifetime and bounds `backend/src/AGENTS.md` fixes, `AuthenticationRegion` for an endpoint with no AWS region, `PutObjectRequest.IfNoneMatch` for the conditional write §2 requires, `PutObjectRequest.ChecksumSHA256` for the checksum the endpoint verifies, and `ListObjectsV2` for the paged listing #1127 sweeps through.

**The client is constructed with explicit credentials and an explicit endpoint, and never through a constructor that resolves either from the environment.** `FallbackCredentialsFactory` and `DefaultInstanceProfileAWSCredentials` reach environment variables, a shared credentials file, and the EC2 instance metadata service; a deployment that forgot to configure a credential must fail, not quietly acquire the host's identity and reach a metadata endpoint. That is a supply-chain and privacy property rather than a convenience, and it is an acceptance item on #1125.

The version is pinned centrally by #1125 with the lock files regenerated in the same change, and the licence review is recorded in `THIRD_PARTY_LICENSES.md` there. This record names the package, not a version.

### Consequences

- Good, because the one unrecoverable failure — a committed row pointing at mail that is not there — is designed out rather than made unlikely, and every remaining failure is an orphan a bounded job removes.
- Good, because the port's four payload kinds, their write semantics, and its result type are unchanged, so no use case, tool, or domain type learns which store answered.
- Neutral, because the port gains a preparation step and its four callers each gain one line, which is the price of keeping the object write out of a transaction; what a caller learns from it is that a payload was stored, never where.
- Good, because the recorded length and SHA-256 keep describing the stored object under both backends, which is what #1126's verification, #1128's refusal to repoint, and #1129's post-release checkability all rest on.
- Good, because a key nothing derives makes erasure a delete of the locator the row carries and reclamation a set difference over a prefix, rather than a reference count that has to survive every crash.
- Bad, because an abandoned unit of work now leaves an orphan where the earlier shape left none, so #1127 sweeps more objects than it would have — the same sweep, on the same terms, over a larger set.
- Neutral, because a deployment may hold both backends for as long as it likes; convergence is an operator's decision, not a deadline this design imposes.
- Neutral, because deduplication is given up, and near-duplicate mail is uncommon under ADR 0014's single tenancy.
- Bad, because erasure of the bytes becomes bounded rather than immediate, which weakens what #131 and #170 promise from "with the transaction" to "within one reclamation interval", and makes that interval a privacy setting.
- Bad, because mail content leaves the process unencrypted, so a hosted endpoint can read it and the documentation has to say so rather than let an operator assume otherwise.
- Bad, because #84 becomes a prerequisite of #1126 rather than an independent improvement, which lengthens the critical path of the parent by one issue.
- Bad, because a shared bucket is safe only under disjoint prefixes and nothing here can verify that an operator arranged them.

## Validation

- #1126 proves the ordering by exercising each crash point through the seam rather than against a live bucket, and the integration suite runs the port's contract against both backends.
- #1131 names the S3 operations and behaviours the adapter depends on in one place and exercises each against Silo, including the conditional write §2 rests on and the paged listing §7 sweeps.
- The boundary rules already assert that no provider type crosses out of `Infrastructure`; the S3 client types are covered by that rule without a new one.
- The ambient-resolution refusal in §8 is an acceptance item on #1125 and is assertable: a client built with no configured credential fails rather than succeeding.
- The `describes:` marker above is what tells a later pull request that a change under the port or its adapters obliges a reader to this record.

## Pros and Cons of the Options

### Write the object first, and reclaim the orphan

- Good, because the failure it produces is invisible to every reader and is removed by a job that has to exist anyway.
- Good, because it needs no repair path that re-derives mail, which for an outgoing message or a draft does not exist.
- Neutral, because it costs storage between the crash and the next reclamation run.
- Bad, because it forces the object write out of the caller's transaction, which #84 has to settle first.

### Write the row first, and repair the missing object

- Good, because the write is over when the transaction commits, and the object write can be retried by a job.
- Bad, because it creates a period during which a committed row points at absent mail, which is exactly the outcome the first driver forbids.
- Bad, because "repair" has no meaning for an outgoing message or a draft: nothing else holds those bytes, so the repair is data loss with a job attached.

### Content-addressed keys

- Good, because two identical payloads occupy one object.
- Bad, because erasure needs a durable reference count correct across two stores and every crash, which makes the most privacy-critical operation the most fragile one.
- Bad, because reclamation stops being decidable from a listing.

### Keys addressed by the owning row

- Good, because deleting a row tells you exactly which object to delete, and reclamation is a set difference over a prefix.
- Neutral, because the locator is stored rather than recomputed, so the scheme can change later without touching existing rows.
- Bad, because identical mail is stored twice, and because the mail draft needs its revision in the key to keep the write-then-commit ordering correct.
- Bad, and decisively, because the row's identity does not exist until the repository that mints it has run, and that repository has opened a transaction by the time it returns — so a key derived from it can only be written inside the transaction §2 forbids writing inside.

### Keys minted by the write itself

- Good, because the key is known before any unit of work is open, which is what lets the put run in front of one and is the whole reason this option wins.
- Good, because no attempt of a replayed unit of work writes to the endpoint, so the write-once kinds need no forgiveness for a repeated put and the draft needs no rule of its own.
- Neutral, because it keeps everything row-addressing was chosen for: erasure deletes the locator the row carries, and reclamation stays a set difference over a prefix.
- Bad, because an abandoned unit of work leaves an orphan where row-addressing left none, which raises what #1127 sweeps without changing how it sweeps.

### `AWSSDK.S3`

- Good, because it is the client "S3-compatible" is defined against, and it carries exactly one dependency.
- Good, because every property #1125 requires is on its configuration surface, including the conditional write and the server-verified checksum.
- Bad, because it must be explicitly prevented from resolving credentials, regions, and instance metadata from the environment, and the parameterless constructor is a hazard a reviewer has to keep watching for.

### `Minio`

- Good, because it is built for custom endpoints and path-style addressing without cloud assumptions.
- Bad, because it brings `System.Reactive` and four other package families into a graph that has no other use for them.
- Bad, because a disagreement with the reference client's behaviour would surface as a MailFathom defect against every vendor.

### SigV4 written by hand

- Good, because it adds no dependency and gives complete control over the request shape.
- Bad, because request signing is security-sensitive code with no upstream to inherit fixes from, and an error in it is an authentication failure rather than a bug.

## More Information

Relationship to the issues #1124 names, stated per issue:

- [#301](https://github.com/Krzysztof318/MailFathom/issues/301) — **complementary, and upstream.** It decides whether MailFathom keeps mail bodies at rest at all; this decides where they go if it does. An answer there of "keep nothing but embeddings" would make this record moot rather than wrong, and nothing here presumes its outcome.
- [#131](https://github.com/Krzysztof318/MailFathom/issues/131) and [#170](https://github.com/Krzysztof318/MailFathom/issues/170) — **complementary.** They own what is deleted and when; §7 states what carrying their decisions through to a bucket costs, which is that the bytes go within a bounded interval rather than with the transaction. Both need that bound written into their operator-facing promise.
- [#75](https://github.com/Krzysztof318/MailFathom/issues/75) — **complementary and unaffected.** It concerns mail that arrives encrypted, which is stored as received under either backend. §5 keeps the separate seam for sealing content at rest and does not take that decision.
- [#84](https://github.com/Krzysztof318/MailFathom/issues/84) — **complementary, and promoted to a prerequisite.** §2 requires the object write to happen with no database transaction open, and #84 already argues that the explicit session transaction earns little. #1126 cannot land before that question is settled one way or the other.

What would revisit this record: a read path that serves raw MIME outward without buffering, which reopens §4 for the read half; a decision to seal mail content at rest, which supersedes §5 and adds a second recorded digest; and an answer to #301 that stops storing bodies, which retires the question this record answers.

The safety interval #1129 defines is the sanctioned answer to an operator regretting the move, which is the input that issue needs when it states whether a reverse move is supported; this record does not decide that.
