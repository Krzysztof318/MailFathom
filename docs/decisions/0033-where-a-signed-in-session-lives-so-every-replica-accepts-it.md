---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-11
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep a signed-in client's session in one PostgreSQL table every replica reads, verify it with one indexed read and no cache, refuse an unreachable store rather than signing anybody out, and let a foreign key and a share lock on the credential row carry the revocation the mint barrier carried

<!-- describes: backend/src/Host/Security/Sessions/**, backend/src/Host/Api/ClientSessionTokenEndpoints.cs -->

## Context and Problem Statement

[ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) divided the work no two replicas may do at once, and [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) settled the two things a client's live updates needed: a signal ticket any replica redeems, and a fan-out that reaches a connection another replica holds. ADR 0032 named a third store on the same surface that it deliberately did not reach, and this is it. `ClientSessionTokens` holds every session a sign-in minted in a `Dictionary` under a lock, behind a singleton registration, together with the barriers that end every session of a credential or of a user.

Above one replica a client's bearer token is known only to the replica that minted it. The load balancer sends each request wherever it likes, so roughly (N−1)/N of a signed-in client's requests are answered `401`, which the client meets as being signed out. A revocation is worse than best effort: disabling a credential ends its sessions on the replica that received the administrative request and leaves them working on every other one, so an operator told that somebody's access is gone is told something false. Nothing here degrades quietly in the way a lost signal does — it is a client that cannot stay signed in and an operator's act that does not take effect.

The decision question is where a session lives so that any replica accepts it and any replica honours its revocation, and what an authenticated request pays for that. [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) records the in-process store as a deliberate trade and already names this as its own revisit trigger: *if the service starts holding sessions somewhere that survives a restart, which would remove the last consequence above without changing anything a head keeps.* This record is that revisit, and it is a new record rather than an amendment because ADR 0023's question is where each **head** puts the credential it holds, which nothing here touches. It produces no code. Issue 1900 implements it.

## Decision Drivers

- **A revocation has to be an act rather than a routing outcome.** Disabling a credential, deleting it, and erasing a user each end the sessions behind them, and an operator told that has to be told the truth whichever replica served the request.
- **Scaling out has to be a replica count.** An operator raising `replicaCount` should not configure affinity, and should not learn a requirement from clients being signed out.
- **An authentication path must not depend on an optional component.** This is ADR 0032's driver, unchanged. PostgreSQL is the one store every deployment runs; the [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) key ring is optional, and a deployment with none is supported.
- **What the sign-in screen promises has to be what the deployment keeps.** The screen tells somebody who asked to stay signed in that the device stays signed in for thirty days. Today thirty days is the most a session lasts, because a restart ends every one of them.
- **An outage must not be a sign-out.** A client meets `401` by asking for a password, so a store that answers `401` when it cannot answer at all would turn a database blip into a deployment-wide sign-out and lose every session the outage covered.
- **What the exchange exists to remove is the derivation, not a read.** Issue 1746 moved the client off HTTP Basic on every request because re-deriving a PBKDF2 record cost around half a second of the process's own processor per call. One indexed read is not that cost and must not be argued against as though it were.
- **A session row is personal data.** It names a user and a credential, so storage limitation, erasure, and confidentiality reach it like every other row, and nothing about the device belongs in it.

## Considered Options

Two questions are answered together, because the answer to the first decides whether the second exists.

**Where does a session live?**

1. One table in PostgreSQL that every replica reads and writes, in the shape issue 1878 gave `ClientSignalTicketStore`.
2. The backplane's own key space above one replica, where a deployment runs Garnet for the signal surface anyway, and the process's memory at one replica.
3. A self-contained token any replica verifies without a read, from key material every replica holds.
4. Per process as today, with session affinity for the client surface at the load balancer.
5. Per process as today, with the client surface held at one replica and the chart saying so.

**What does an authenticated request pay?**

1. One indexed read, on every authenticated request, with nothing in front of it.
2. One indexed read behind a short per-process cache, so most requests pay nothing.

## Decision Outcome

Chosen options: **one session table in PostgreSQL**, and **one indexed read on every authenticated request with no cache** — because it is the only pair that makes a revocation mean the same thing on every replica at the instant it is performed, keeps authentication on the component every deployment already runs, and needs one implementation at every replica count. The read it costs is on a path that already reads mail out of the same database, and the cache that would remove it would reintroduce exactly the window the barriers in the current store exist to close.

### The session becomes a row, in the shape the signal ticket already has

Issue 1878 landed `ClientSignalTicketStore`: bare commands over the data source rather than EF Core queries, statements composed from the mapped entity's own column constants, every value a parameter, each command bounded by the caller's cancellation token, and a driver failure translated at the application port rather than absorbed. A session store is the same kind of thing reached from the same kind of caller — an authentication handler that is inside no unit of work and must not join one — so it takes that shape rather than inventing a second one.

**The row keeps the secret half only as a digest.** A token is a sixteen-byte identifier and a thirty-two-byte secret written together after the `mfs_` prefix, and only the identifier is looked up. What the table holds is a SHA-256 digest of the secret, compared with `CryptographicOperations.FixedTimeEquals`, exactly as the ticket's is. The secret is thirty-two bytes from the platform's cryptographically secure generator, so it needs no key stretching: there is nothing to guess faster than the generator. The consequence is the one that matters operationally — **a row read out of the database, a dump, or a backup is not a session**, which is not true of a store that kept the presented value.

**What else the row holds is what the dictionary entry holds**, and nothing more: the credential the exchange authenticated, the user it resolved, what that credential grants, and the instant the session expires. No address, no user agent, no device name, and no record of which replica minted it. None of those has a purpose here, and the ticket store keeps none either — a row says the deployment holds the session, which is the fact the deployment promises rather than a fact about a process.

**Not the backplane, although a deployment above one replica already runs one.** Garnet is there, credentialed, and inside the confidentiality boundary, and it offers a better store than PostgreSQL on the two counts a store is usually judged by: a `GET` is cheaper than a primary-key lookup, and an expiry set on the key retires the sweep. It is refused on four counts that outrank both, and each of them is about what happens when something goes wrong rather than about the happy path. Authentication would depend on a component whose outage today costs only signals, so losing Garnet would stop being a lost screen update and become every client of the deployment signed out. The endpoint runs with no volume because ADR 0032 gave it nothing to keep, so its restart would sign everybody out exactly as a service restart does today — which is the consequence this record exists to remove, moved rather than removed — and giving it a volume would make a pipe that keeps nothing a second durable store of personal data. The mint barrier would survive, because only PostgreSQL can order an operator's act on a credential row against an exchange in flight. And an erasure would stop being a foreign key and become a set maintained per user that can drift from the sessions it indexes, on the one path a GDPR obligation rests on. *Pros and Cons of the Options* holds the fifth count, which is that the replica count would choose between two implementations of an authentication store.

**The bound becomes the deployment's**, counted where the ticket's is: the insert carries its own count, so ten thousand live sessions is one number for the deployment rather than a number each replica finds room under separately. That is what issue 1293 asks of every ceiling whose wording names the deployment, and the wording on `docs/operations/client-endpoint.md` names the process today only because that was the truth. A statement counts what stands rather than what is live, so the sweep below runs first where the bound is what would refuse the mint — which is what keeps the number the one this paragraph calls it. Reaching it still refuses a new sign-in rather than ending somebody else's session, and still answers `503` rather than `401`, because the two are different things for a client to do.

**The sweep is a bounded delete against the index on the expiry**, opportunistic and in process, reaching only what has already expired and needing no lease, because removing a row twice removes it once. What it does **not** inherit is when the ticket and the assertion record run theirs. Both set that interval to their own lifetime, which is thirty seconds and a few minutes; a session's lifetime is thirty days, so the same term would have a process sweep thirty days after its own start, which is longer than most processes live. Two triggers instead, and the record names them because neither is the ticket's:

- **When the bound is what would refuse a mint**, re-checking after — which is the current store's own term, and what keeps the ceiling counting live sessions rather than rows that merely stand. Without it, ten thousand abandoned expired rows fill the bound and every new sign-in is refused `503` until something removes them.
- **Otherwise at most once a day**, which is short against the lifetime rather than equal to it. This is the trigger that makes storage limitation real in a deployment that never approaches the bound: without it, a row naming a user and a credential could stand for thirty days past the point it authenticated anything, which is a login history rather than a session store.

### Verification is one read, and nothing caches it

An authenticated request presents its token, and the handler does what it does today with one substitution: a lookup by identifier against the table rather than against a dictionary, then the same fixed-time comparison and the same expiry check. There is no join to the credential row, so what a session admits is still what the exchange admitted — which leaves ADR 0023's stated bound exactly where it is, that a grant narrowed after somebody signed in reaches them at their next sign-in.

**Nothing caches the row, and that is the decision rather than an omission.** A per-process cache would make most requests free and would make a revocation take effect after the cache's own window on every replica that had already read the session. The current store has two pieces of machinery whose whole purpose is to keep ending a session from being a race — the thirty-second mint barrier and the walk that removes a credential's sessions — so buying request cost with a revocation window would spend the property this record exists to establish. It is also the lazier answer: no invalidation, no second code path, no second thing to reason about when an operator asks why a disabled credential still worked.

What the read costs is one index lookup on a narrow table, on a request that is about to read mail out of the same database. The client opens about eight requests to draw one screen and every one of them already talks to PostgreSQL. This is measured rather than assumed before the cache is reconsidered — the revisit trigger below says so.

**An unreachable store refuses the request, and it refuses it as unavailable.** The answer this store gives is what decides whether a request is served, so a store that cannot answer is one whose caller must not serve; a driver failure is translated at the application port rather than reaching an authentication handler as an `NpgsqlException`, which is the arrangement `ClientAssertionSpendStore` and `ClientSignalTicketStore` are both under. Which status that becomes is the part specific to sessions: **`503`, never `401`**. A client meets `401` by clearing what it holds and asking for a password, so answering an outage with `401` would sign every signed-in client out of a deployment whose database came back a minute later.

### A renewal stays exactly one live token, settled by PostgreSQL

Renewal replaces the presented session so one sign-in is one live token however often a client renews. In the dictionary that is read-and-replace under one hold of the gate. As rows it is the ticket's own mechanism used for a different purpose: **delete the presented row and return what it held**, then insert the replacement, in one transaction. Two requests presenting the same token leave one with a row and one with nothing, settled by the database rather than by a check either of them makes between two statements — which is the same failure the single hold of the gate prevents today, in the only form that works when the two requests reach different replicas.

**The delete is guarded by the secret in the same statement, which is where the ticket's mechanism must not be copied literally.** `ClientSignalTickets` removes the row by identifier and only then compares the presented secret against the stored digest, which is right for a value that is spent by being presented at all. A session survives being presented, so a delete keyed on the identifier alone would let anybody writing the non-secret half of somebody's token, with any secret, end that person's session — the same denial the revocation below is guarded against, arriving through the renewal instead. So the digest is in the statement's own `WHERE`, and the row is removed only where the presented secret proves it. Comparing a digest there rather than in fixed time in the process is what this one case allows, because a partial match on the digest of a thirty-two-byte random secret is no step toward the secret; reading the row first and comparing in process before deleting is the two-statement shape that loses the single-use property this section is for.

**The renewal's transaction takes the same lock on the credential row as the mint's, and requires it to still be enabled.** It is the second path that writes a session row, so the check that makes a revocation an act has to be reachable on it too — and without the lock the mint's protection does not cover it. A renewal that has deleted the presented row and not yet committed blocks an administrative transaction's `DELETE` of that credential's sessions; the renewal then inserts the replacement and commits; the blocked delete resumes against the snapshot taken at its own statement start and never sees the row that arrived after it. The operator is told access ended, and a live session for a disabled credential stands for the rest of its thirty days. The replacement insert's own foreign-key check does not catch it, because a key-share lock on the parent does not conflict with an update that changes no key value, which is the same reason the mint needs the explicit lock. Today's store cannot reach this state, because `Renew` needs a live entry under the one hold of the gate that `RevokeEverythingMintedBy` has already removed under the same hold — so the lock is what carries that property across the split rather than an extra precaution.

A revocation is a delete keyed on the identifier and guarded by the secret, for the reason it is guarded today: the identifier is the half of a token that is not a secret, and a delete keyed on it alone would be a sign-out anybody could perform.

### A foreign key and a share lock replace the mint barrier

The barriers are the part of the current store that does not survive being split across replicas, and the answer is not to move them into a table. PostgreSQL already serializes what they were written to serialize.

**Deletion and erasure are closed by a foreign key.** The session row references the user credential row, `ON DELETE CASCADE`. Deleting a credential removes its sessions, and an erasure that removes a user's credential rows by cascade removes the sessions with them — which is what `RevokeEverythingMintedFor` walks the dictionary to do, and what it exists at all because an erasure never names the credentials it is deleting. An insert naming a credential that is gone is refused by the constraint. The race is closed as well as the act: the constraint check on the referencing side takes a key-share lock on the parent row, which blocks a concurrent delete of it, so a mint and a deletion arriving together serialize, and whichever loses fails in the safe direction — the insert to a constraint violation, or the delete waiting and then cascading the new row away.

**Disabling is closed by a share lock, and it is the case that needs one.** A key-share lock does not block an update that changes no key value, and disabling a credential is exactly that update. So **every transaction that writes a session row** — the exchange's mint and the renewal alike, which is the pair rather than the mint alone — first takes `FOR SHARE` on the credential row while requiring it to still be enabled, and only then inserts; and the administrative act disables the credential and deletes its sessions in one commit. PostgreSQL's documented row-lock conflicts and its `READ COMMITTED` behaviour make the two orders both correct. A plain update acquires `FOR NO KEY UPDATE`, which conflicts with `FOR SHARE`, so the two never proceed together. Where the mint holds the lock first, the disable waits and its own delete then removes the row the mint just wrote. Where the disable commits first, the mint's blocked lock acquisition re-evaluates its search condition against the updated row version, finds the credential no longer enabled, locks nothing, and refuses.

**The lock is what the check needs, and the statement that looks like it would do is the trap `backend/src/Infrastructure/AGENTS.md` names.** An insert whose own `WHERE EXISTS` asks whether the credential is enabled reads the state committed as of that statement's start, so a disable committing while the insert runs is invisible to it — and that disable's own delete has already passed over a row the insert is about to write, leaving exactly the live session the barrier existed to prevent. Read-then-write over a row another transaction is deciding about is the shape that looks safe and is not, whether the two statements are far apart or fused into one. What closes it is holding the row the decision depends on for as long as the decision needs it, which is the lock.

The derivation stays outside that transaction. The exchange reads the credential, spends its half second deriving, and only then opens the short transaction that locks, checks, and inserts — so nothing holds a row lock across a key derivation.

**What this removes is the thirty-second window itself.** The `MintBarrier` exists because a mint could land after the sweep meant to have ended it, and the price of covering that window is that a credential enabled again inside it is refused a sign-in. Under this record there is no window to cover and no such refusal: the two acts are ordered by the database rather than by a timestamp guessed to be longer than a derivation. That is the one place where moving the store out of the process makes the behaviour better rather than merely shared.

### A restart no longer signs everybody out

This is a consequence rather than a driver, and it is the reason ADR 0023 wrote the revisit trigger this record answers. Today a restart of the service ends every session it holds, a rolling upgrade ends all of them on every replica it replaces, and the thirty days the sign-in screen states is the most a session lasts rather than a length anybody can count on. With sessions as rows, a restart and a rollout end none of them, and thirty days becomes what the deployment keeps. Nothing a head stores changes, which is exactly what ADR 0023 predicted: the web head still keeps it for the tab unless somebody asked for longer, and the desktop head still keeps it in the operating system's keychain.

What still ends a session earlier is unchanged and unweakened: signing out, the expiry, and an operator ending the credential behind it — the last of which now takes effect on every replica rather than on one.

### What a session row owes under the privacy obligations

- **It is personal data**, naming a user and a credential, so it is classified with the rest of the client surface rather than treated as infrastructure state.
- **Storage limitation** is the expiry plus the sweep. A row past its expiry authenticates nothing and is removed by the next bounded delete, rather than kept as a record of who was signed in when — which is also why the row carries no address or device: there is no purpose it would serve, and keeping it would create a login history the deployment never promised.
- **Erasure** is stronger than it is today, and mechanically so. The cascade is the database's, so a session cannot outlive the user it acts for through a path that forgot to walk a dictionary.
- **Confidentiality** is the digest. Whoever reads the table, a dump, or a backup holds no session.
- **Nothing crosses a new boundary.** The row lives in the database the deployment already runs, so no new processor, recipient, or transfer enters the operator's processing record.

### What this does to the chart

**Nothing, and that is the point.** The store is PostgreSQL, which every deployment runs and which no deployment can turn off, so there is no configuration that looks healthy and silently cannot keep a session — and therefore nothing for the chart to refuse. ADR 0032's refusal stands exactly as written and covers what it covers: the chart refuses to render when `replicaCount` is above 1, the client surface is served, and no backplane is configured. No second refusal joins it.

Until issue 1900 lands, the chart is the record of a limit rather than a mechanism: a client served by more than one replica is signed out on most of its requests whatever the backplane, so ADR 0032 is necessary above one replica and not sufficient, and no page may call `replicaCount` safe to raise. Once it lands, both halves of the client surface work at any replica count, and issue 1295 is what says so where an operator reads it.

### Consequences

- Good, because a revocation is the deployment's act at the instant it is performed. Disabling a credential, deleting it, and erasing a user each end every session behind them on every replica, which is what an operator is already told happens.
- Good, because a signed-in client stays signed in whichever replica answers, so raising `replicaCount` needs no affinity and no load-balancer project.
- Good, because authentication stays on the one component every deployment runs, with one implementation at every replica count, and never reaches for the optional key ring.
- Good, because a restart and a rolling upgrade stop signing every client out, and the thirty days the sign-in screen states becomes a length the deployment keeps.
- Good, because the thirty-second mint barrier goes. The database orders an operator's act against an exchange in flight, so no window has to be guessed and a credential enabled again is signed in with rather than refused for half a minute.
- Good, because the erasure path becomes a foreign key rather than a walk somebody has to remember to call, and a row read out of a backup is not a session.
- Good, because the bound becomes the deployment's, which is what its own wording claims and what issue 1293 asks of every such ceiling.
- Neutral, because the store is the shape issue 1878 already landed for the signal ticket, so this is a second user of an understood arrangement rather than a new one.
- Neutral, because nothing a head keeps changes, and the client needs no change at all: the token it holds, the renewal an hour before expiry, and the `401` it already handles are all unchanged.
- Bad, because every authenticated request now costs one indexed read that a dictionary lookup did not. It is small against what the request goes on to do, and it is traffic on the database.
- Bad, because authentication now depends on the database being reachable. What softens it is that nothing on this surface works without the database anyway, and what it costs is answered by refusing as unavailable rather than as unauthenticated — but a client that could previously be told `401` for one reason can now be told `503` for another.
- Bad, because the schema gains a table holding personal data on a surface whose whole state used to be in memory, with the retention, erasure, and confidentiality obligations that follow. Every one of them is discharged above, and each is a thing to keep discharged.
- Bad, because both paths that write a session row gain a short transaction that takes a row lock, which is a lock an administrative act can wait on. Neither is held across a derivation or a network call, so what waits is bounded by one insert — but a client renewing is now a writer that can briefly hold up an operator disabling its credential, which the dictionary never did.
- Bad, because refusing a cache means the read is paid on every request rather than on the first. That is a deliberate purchase of an immediate revocation, and the measurement that would reopen it is named below.

## Validation

- **The store.** Unit tests over the session type require the same refusals it makes today: a malformed value refused before the store is asked, an expired session refused, a revoked session refused on the next request, a renewal replacing the presented token, and the deployment's bound refusing a new sign-in rather than ending a live one. Issue 1900 owns them.
- **The outage.** A unit test requires an unreachable store to answer `503` rather than `401`, on the exchange and on an authenticated request alike. This is the one that stops a database blip from becoming a sign-out, so it is named separately from the rest. Issue 1900 owns it.
- **The renewal's guard.** A unit test requires a renewal presenting a correct identifier with a wrong secret to leave the session live, which is the proof that the delete was guarded rather than performed and then judged. Issue 1900 owns it.
- **The sweep's own triggers.** Unit tests require a sweep before the bound refuses a mint, so a store full of expired rows serves the next sign-in rather than refusing it, and require the interval trigger not to be the lifetime. Issue 1900 owns them.
- **No cache.** The absence is validated by review against *Verification is one read, and nothing caches it*, because nothing mechanical distinguishes a cache from a field.
- **The revocation, against a real database, and across replicas.** The integration suite the owner runs starts two hosts against one database and proves that a session minted on one host authenticates on the other, that disabling a credential on one host refuses its sessions on the other, that deleting a credential and erasing a user each remove the sessions by cascade, that a renewal presented twice succeeds once, that a mint racing a disable ends with no live session, and — separately, because it is the path the lock was nearly left off — that a **renewal** racing a disable ends with no live session either. Those proofs belong there rather than in a unit suite, for the reason `ClientSignalTicketStore` and `ClientAssertionSpendStore` both carry `[RequiresIntegrationCoverage]`: the store is raw SQL and a row lock, only PostgreSQL settles what either does, and a fake would only prove itself. Issue 1900 owns them.
- **The migration.** The change adds a table and a foreign key and regenerates no baseline, which `$add-migration` and the `Pending model changes` job both hold.
- **The documentation.** `docs/operations/client-endpoint.md` § *Sessions live in the process's memory* is what an operator reads for all three of the consequences this record changes, and it moves in the same change set as the code rather than after it. Issue 1900 owns it.

## Pros and Cons of the Options

### One session table in PostgreSQL

- Good, because a revocation takes effect on every replica at the instant it is performed, which is the property the whole question is about.
- Good, because it is the store every deployment runs, so one implementation serves every replica count and authentication depends on nothing optional.
- Good, because it is the shape issue 1878 landed for the signal ticket, down to the digest, the parameterized statements, the deployment-wide bound in the insert, and the bounded sweep.
- Good, because a restart and a rollout stop ending sessions, and the foreign key makes erasure mechanical.
- Neutral, because the table exists in every deployment's schema, including those that serve no client surface and never write a row to it.
- Bad, because every authenticated request costs an indexed read, and the surface's availability now includes the database's.

### The backplane's key space above one replica, and the process's memory at one

A deployment above one replica already runs Garnet for the signal surface, so this asks for no component that is not there, and the store it offers is a good one on its own terms: a `SET` with an expiry retires the sweep, `SET NX` gives single use where it is wanted, a `DEL` is an immediate revocation on every replica, and a `GET` is cheaper than a primary-key lookup plus a pool acquisition. Those are real, and none of them is what decides this.

- Good, because a multi-replica deployment adds no infrastructure for it: the endpoint is already deployed, already credentialed, and already inside the confidentiality boundary.
- Good, because the store retires machinery rather than porting it — the expiry is the endpoint's own, so the bounded sweep has nothing to do.
- Good, because a read is cheaper than a database read, on a surface where the read is the only cost this record pays.
- Bad, because **authentication would depend on an optional component**, which is ADR 0032's driver and the reason it kept the signal ticket out of the same key space. Today losing the backplane costs signals and the client's five-minute re-read covers it. Under this option losing it signs every client of a multi-replica deployment out, or refuses every authenticated request until it returns, which is a far larger outage wearing the same configuration.
- Bad, because **a restart of the endpoint is the consequence this record exists to remove, moved rather than removed.** ADR 0032 runs Garnet with no volume precisely because it has nothing to keep. Keeping sessions there either leaves that shape, so a Garnet restart signs everybody out exactly as a service restart does today, or gives Garnet a volume — which turns a pipe that keeps nothing into a second durable store of personal data, with its own retention, backup, erasure, and confidentiality obligations, and makes an external managed endpoint a processor holding session credentials.
- Bad, because **the mint barrier survives it.** The race this record closes is between an operator's act on a PostgreSQL row and an exchange in flight, and only PostgreSQL can order those two. A store in another process cannot take a lock on the credential row, so the thirty-second window and its refusal of a credential enabled again inside it would have to be kept, and kept as a second key in a second store.
- Bad, because **an erasure stops being a constraint and becomes a second deletion path.** The foreign key is what makes a session unable to outlive the user it acts for. A key space has no secondary index, so ending every session of a credential or a user needs a set maintained per credential and per user, written on every mint and read on every act — a structure that can drift from the sessions it indexes, on the one path a GDPR obligation rests on. A drift there leaves a live session for an erased user and nothing notices.
- Bad, because **it is two implementations of an authentication store, and the replica count chooses between them.** A deployment scaling from one replica to two changes which store holds its sessions and signs every client out at that moment, and scaling back down does it again. The two would also answer *when does a revocation take effect* differently, so that answer would depend on `replicaCount` — and only one of the two would be the shape the default deployment exercises.
- Bad, because it reopens the boundary ADR 0032 drew around the broker exception — that the backplane carries client signals, coordinates nothing, and holds no state any part of MailFathom depends on finding there later. A session store depends on finding state there. That record says any further use is a decision of its own, which this would be; the point is that the decision costs the narrowness the exception was granted for.

### A self-contained token any replica verifies without a read

- Good, because verification stays in the process and costs no read at any replica count.
- Neutral, because the client would need no change: what it holds is opaque to it either way.
- Bad, because it needs key material every replica holds, and the ADR 0005 key ring is optional in a supported deployment. ADR 0032 refused sealing the signal ticket for exactly this reason, and a session is no less an authentication path.
- Bad, because revocation needs its own answer, and every answer is a shared store — a denial list read on each request, which is the read this option was chosen to avoid, or a short lifetime, which means a token stays good after an operator ended it.
- Bad, because it weakens what ADR 0023 says the client is keeping. That record rests on a stored value being refused the moment it is revoked; a self-contained token is good until it expires unless something is read.
- Bad, because it would put key rotation on the path that decides whether a signed-in client stays signed in.

### Per process, with session affinity

- Good, because it changes no server code and no schema.
- Neutral, because a stolen token presented to the wrong replica fails, which is the safe direction — the same observation ADR 0032 made about the ticket before refusing affinity anyway.
- Bad, because a session lives thirty days and affinity would have to hold for thirty days. No load balancer promises that, and every replacement of a pod breaks it.
- Bad, because a rolling upgrade replaces the replica holding the session, so the deployment would sign its clients out on exactly the operation replicas were added to survive.
- Bad, because a cookie pins every request a client makes and hashing the source address pins every client behind one address, so either distorts the load the replicas were added to share.
- Bad, because it makes an operator configure the load balancer after ADR 0032 removed every other reason to, which is what the operator's story refuses.
- Bad, because it answers nothing about revocation. An administrative request reaches whichever replica the balancer picked, which is not the one holding the session.

### Per process, with the client surface held at one replica

- Good, because it costs nothing and is honest about today.
- Neutral, because it is the state the chart and `docs/operations/client-endpoint.md` describe now, so nothing would have to be written down that is not already written down.
- Bad, because issue 1287's acceptance is that `replicaCount` is safe to raise, and this makes it permanently unsafe wherever the client surface is served.
- Bad, because a client-serving deployment could never survive the loss of a node or a rolling upgrade without signing every user out.
- Bad, because it would strand ADR 0032. The backplane, the WebSocket-only hub, and the shared ticket exist so the signal surface works above one replica, which a deployment held at one replica never reaches.

### One indexed read on every request, with nothing in front of it

- Good, because a revocation is immediate everywhere, with no window to document and none for an operator to discover.
- Good, because there is one code path, no invalidation, and nothing to reason about when a disabled credential is asked about.
- Neutral, because the read lands on a request that is about to read mail out of the same database, so it is a fraction of what the request already costs.
- Bad, because the cost is paid on every authenticated request rather than on the first of a burst.

### One indexed read behind a short per-process cache

- Good, because most requests would pay nothing, and the read would fall to roughly one per session per cache window per replica.
- Neutral, because the window could be made short enough that a person would rarely notice it.
- Bad, because a revoked session goes on working for the window on every replica that already read it, which is precisely what the mint barrier and the revocation walk exist to prevent — so it would buy request cost with the property this record is for.
- Bad, because an operator would have to be told a number, and "access ends within N seconds" is a worse thing to document than "access ends".
- Bad, because it is a second store of authentication state, with its own invalidation to get right and its own behaviour under a rollout.

## More Information

- **The issues.** Issue 1886 asks the question. Issue 1900 implements this record: the table and its migration, the port and the adapter, the mint's short transaction, the renewal's delete-and-return, the barriers' removal, the deployment-wide bound, the sweep, the `503`, the unit tests, the integration proofs across two hosts, and the page an operator reads. It is a child of issue 1287, waits on this decision and on nothing else, and issue 1295 waits on it — no page may call `replicaCount` safe to raise until it lands.
- **ADR 0023.** [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) decides where each head keeps the session the deployment minted, and nothing here changes any of it. It records the in-process store as the cost of that arrangement and names this record's subject as its own revisit trigger. The consequence it states — that the sessions the deployment holds do not survive a restart of it — is what issue 1900 removes, and the record is a new one rather than an amendment because the question it answers is about the client and this one is about the service.
- **ADR 0031 and ADR 0032.** [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) names this as a precondition it does not lift and is where the shape of the store, the refusal of the optional key ring, and the refusal of affinity are argued for the signal surface; this record reaches the same three conclusions for the session, from the same drivers. [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) divides the work between replicas and is untouched: a session is state every replica reads rather than work one of them must hold, so nothing here takes a lease.
- **ADR 0009 and the broker refusal.** [ADR 0009](0009-durable-job-store-and-execution-identity.md) refuses a message broker and a coordination service, and ADR 0032 made the single narrow exception to it. **This record takes no exception.** The session lives in PostgreSQL, it does not travel over the backplane, and a deployment running no backplane keeps its clients signed in exactly as one running Garnet does — which is also why the backplane's boundary, that it carries client signals and nothing else, is not reopened here. Using the endpoint as the session store was weighed rather than assumed away, on the strength of a multi-replica deployment already running one; *Pros and Cons of the Options* is where it is refused, and the refusal is what leaves that boundary where ADR 0032 drew it.
- **Out of scope.** When a narrowed grant reaches somebody who is already signed in, which stays what ADR 0023 records: their next sign-in rather than their next renewal. A bound counted per credential rather than per deployment. Anything about what a head stores. And the signal ticket and the backplane, which ADR 0032 settles.
- **The `describes:` marker.** It names the code this decision is about as that code exists today — the session store, its authentication handler, and the routes that mint, renew, and revoke. It gains the table's entity, its configuration, the port, and the adapter as issue 1900 lands them.
- **When to revisit.** Revisit the refusal of a cache when the read is *measured* as a cost on the client surface rather than suspected of being one, and revisit it with a number and a stated revocation window rather than with an argument. Revisit the backplane as the store if it ever stops being optional — a deployment that cannot run without Garnet loses the first of the four counts against it, and the remaining three are then what has to be answered rather than what settles it. Revisit the bound when a deployment legitimately needs more than ten thousand live sessions, which is the point at which counting per credential becomes worth its own read. Revisit the whole record if the client surface ever has to authenticate a request the database cannot be asked about, which is the one assumption every option above rests on.
