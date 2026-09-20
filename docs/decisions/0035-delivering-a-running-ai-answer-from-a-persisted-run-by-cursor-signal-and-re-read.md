---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-20
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Write a running AI answer into PostgreSQL as it is composed, announce over the hub only the run and the sequence it reached, let the client's own re-read from a cursor carry the answer, and keep starting, posting, steering, and stopping ordinary HTTP routes

<!-- describes: backend/src/Application/Discovery/Streaming/**, backend/src/Application/Discovery/Runs/**, backend/src/Host/Api/ClientDiscoveryRunEndpoints.cs, backend/src/Host/Signals/**, frontend/src/Client.Backend/src/signals.ts -->

## Context and Problem Statement

Two surfaces of this product produce an answer while a person waits. The Discover run delivers one today over
Server-Sent Events, from the process that started it. The Agent will deliver one, and the surface that carries it —
issues 1573 to 1576 — is being written now, so how it delivers is still open. Both are the same problem: an answer
composed in pieces, watched by a person, on a deployment that may be running several replicas. Deciding it twice is how
two surfaces start disagreeing about what a client is promised, so it is decided once, here, before the second one is
built.

**Today's shape does not survive more than one replica.** `DiscoveryRunRegistry` is a
`ConcurrentDictionary<DiscoveryRunId, HeldRun>` held in one process, and its own remarks say that is deliberate: *what
it protects is a reconnection inside minutes, and a restart ends every run it was holding anyway.* That reasoning held
while a deployment ran one replica. `ClientDiscoveryRunEndpoints` answers `404` where **this process** holds no such run
for this user, and `429` where **this process** already holds as many as it may. So at `replicaCount: N` a client
reading its own run reaches the replica holding it about one time in `N`, and the refusal is indistinguishable from a
run that never existed. The concurrency bound is per process rather than per deployment or per person, so what one
person may start is whatever the load balancer happens to spread. It is also why a run cannot be picked up by a second
screen of the same person, and why nothing survives a rolling upgrade. Nothing reports any of it:
`docs/operations/deployment-kubernetes.md` § *Running more than one replica* does not mention either bound.

[ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) already found and closed
this class of break, for the connection ticket and for the signal fan-out, and its two load-bearing sentences are the
whole of the design here: **a signal is best effort**, and **the guarantee is the client's own re-read**. This record
applies that settlement to a run's output rather than to a mail signal, once, for both AI surfaces. It produces no code.
Issues 2100 and 2101 deliver it for Discover and for the client, and issues 1573, 1574, 1575, and 1576 carry it into the
Agent.

## Decision Drivers

- **A run's output has to be reachable from every replica**, because the client reading it is routed wherever the load
  balancer likes and its screen may be a second one the same person opened.
- **ADR 0032's backplane vocabulary is fixed, and widening it has a cost outside this repository.** That record admits
  *no subject, address, body fragment, or attachment name* to the backplane, and it permits an operator to point the
  backplane at a managed service somebody else runs. An answer is mail-derived in every part, so anything that pushed it
  through the hub would move that endpoint from seeing aliases and counts to seeing mail, and change what an operator
  owes their own processing record.
- **Stopping must work when the hub does not.** A run in flight is the moment spending most needs to be stoppable, and a
  control that travels over an optional component is a control that is absent exactly when it is wanted.
- **A screen must draw with no live connection at all.** [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)
  already makes the client honest about a deployment out of reach; a surface that showed nothing until a signal arrived
  would be the silent failure ADR 0032 exists to close, reintroduced one layer up.
- **Two surfaces, one mechanism.** Whatever is decided here is implemented once and called twice, on the server and in
  `Client.Backend`, or the two screens drift.
- **A run already outlives the connection that asked for it**, which is why Discover has three routes rather than one,
  and that property must not be traded away.

## Considered Options

Three questions are answered together, because the answer to each constrains the other two.

**Where does a run's output live while it is being composed?**

1. In the process, published as it is produced and kept until the run is forgotten — today's `DiscoveryRunJournal`.
2. In the process, held until the run ends and returned as one answer.
3. In PostgreSQL, written as each part is composed, each part carrying a sequence monotonic within the run.

**How does a client learn that more of it exists?**

1. An HTTP stream from the replica holding the run, resumed with `Last-Event-ID` — today's shape.
2. The answer itself pushed over the signal hub.
3. A signal naming the run and how far it has got, and a re-read from the client's own cursor over an ordinary route.
4. The client's own poll alone, with no signal at all.

**How does a client start, post to, steer, and stop a run?**

1. Ordinary HTTP routes under the client endpoint.
2. Hub methods, making the connection bidirectional.

## Decision Outcome

Chosen options: **the run writes its output to PostgreSQL as it is composed**, **the hub carries a signal naming the run
and the sequence it reached while the client re-reads the tail from its cursor**, and **every control a client uses
stays an ordinary HTTP route**. Together they make the durable record the guarantee and the signal an optimization,
which is ADR 0032's own settlement rather than a second one.

### The run writes its output as it is composed

This is the condition everything else rests on, so it is stated as one. Each part of an answer — a block, a declared
source, a retrieval or spend advance, the ending — is written where it is produced, with a sequence that starts at `1`,
never skips, and is monotonic within the run. An answer held in memory until the run ends makes the connection that
watched it the only route to it again, and every other decision below collapses back into today's.

What is written is mail-derived and is classified as sensitive personal data by the same default every AI derivation in
this product carries: the same seams for a future access, export, and erasure workflow, and no part of it reaching a log
or telemetry. The retention each surface already fixes is what sweeps it — for Discover, five minutes after the last
read and the ten-minute ceiling its page states; for the Agent, the conversation's own lifetime, which is a durable
store by design.

### The hub carries the run and the sequence it reached, and no part of the answer

One new kind of signal joins the six ADR 0032 fixed. It names the run — and, for the Agent, the conversation it belongs
to — and how far that run has got, and it carries **nothing of the answer**: no block, no citation, no retrieval count,
no spend figure, no model name, no alias. It is published to the owner's group, so every connection that person holds is
told rather than only the one that asked.

**Why the content may not travel with it** is not a preference. ADR 0032 § *What crosses the backplane, and what
securing an external endpoint takes* fixes the vocabulary — no subject, address, body fragment, or attachment name — and
§ *What a deployment pays* permits the endpoint to be a managed service somebody else runs, which that record already
treats as a disclosure to a processor. An answer quotes mail by construction. Pushing it through the hub would therefore
widen the vocabulary to the one category it excludes, hand mail to whoever can subscribe on that endpoint, and change
what an operator owes their own processing record — for a saving of one read. [Amendment 3](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md#amendment-3-a-running-ai-answer-announces-only-how-far-it-has-got)
to ADR 0032 admits this one kind and holds it to every bullet under *The backplane carries client signals and nothing
else*.

### A client re-reads the tail from its cursor, over an ordinary route

A client holds the last sequence it has seen and reads everything after it, from any replica, over an ordinary route
beneath `/api/client`. One route serves both the first read and every later one: the cursor omitted means from the
beginning.

**That replaces `Last-Event-ID`, and is stronger than it was.** The stream's resumption addressed a buffer in the
process holding the run, so it was exact only while the client landed back on that replica; the cursor addresses rows
every replica can read. A gap in what a client holds is closed by the next ordinary read rather than by re-fetching the
run from its beginning, and a duplicate or out-of-order announcement changes nothing, because the cursor only ever moves
forward.

### Every control a client uses is an ordinary HTTP route

Starting a run, posting a message, steering one without interrupting it, and stopping it are HTTP routes. **Nothing
travels up over the hub**, which stays a fan-out with no methods. Three reasons, stated rather than assumed:

- **A dead hub must not be able to prevent stopping a run.** That is the moment spending most needs to be stopped, and
  the hub rests on a component ADR 0032 deliberately made optional.
- **HTTP already has the vocabulary a refusal needs.** A spend ceiling is a status rather than an invented payload, and
  ADR 0022 already decides what such a refusal says and what it may not disclose about anybody else's spending.
- **A hub method is mapped outside the client route group.** It would need its own authentication, bounds, and audit
  beside the ones the routes already carry, which is a second security surface for a control the first one serves.

### A client never waits for the hub

It reads on mount unconditionally, on every advance, and after every reconnect — the same re-read every other screen in
this client already performs. A screen is fully drawn with the hub absent, and a run that has already finished needs the
hub for nothing at all.

**There is no fallback transport**, which is ADR 0032's own stated cost and is unchanged here. What this record adds is
what a client does about it: a poll bounded to the run it is showing, armed by the client's own observation that a run
is in flight and has not advanced — **never** by the hub reporting itself disconnected. The hub can be connected and
silent whenever the backplane is unreachable, which ADR 0032 states plainly: the backplane buffers nothing while it is
down, and losing it is logged without taking a replica out of rotation. A fallback armed on the connection's reported
state would therefore stay disarmed through exactly the outage it exists for. The poll stops the moment a read returns a
terminal state.

### Both surfaces take this shape, and the event stream is retired

Discover's `GET /api/client/discovery/runs/{runId}/events` goes away rather than standing beside the new route, together
with the `System.Net.ServerSentEvents` composition and every `Last-Event-ID` path behind it. The Agent is built this way
from the start. Keeping the stream for one surface would leave two mechanisms to hold correct, and the one that survives
above a single replica is not the stream.

### Block granularity is the floor

Nothing streams below a composed block. A block is written when it is composed and announced when it is written, and a
person watching sees an answer assemble block by block rather than word by word.

Going below that is named here rather than left open, because the two ways to it both cost something this record
declines to spend now:

- **An amendment widening ADR 0032's vocabulary**, so that answer fragments cross the backplane — which is the
  disclosure and the processing-record consequence above, taken for a smoother animation.
- **A direct stream from the replica holding the run**, which is today's shape and brings back with it every defect this
  record closes: a client that must reach one replica, and an answer that exists only where it is being composed.

A third possibility — announcing a token-level cursor while the tokens themselves stay in PostgreSQL — is not refused in
principle, but it is a read per token per watching client, which is a different cost question and belongs to whoever
measures it.

### An interrupted run is reported rather than resumed

The run still executes on the replica that started it. A run interrupted by a restart, a rollout, or a replica going
away is journalled as interrupted — for Discover, `failed` with `Stopped`, which its page already defines as *the
deployment shut down while it was executing* — and the person retries it.

Resuming it is refused. Resuming means persisting the run's intermediate state — the model's own conversation, the
retrieval it had done, the plan it was composing — for a case the durable conversation already softens: what was
composed before the interruption is on the record and is still drawn, and the Agent's history keeps the question so
retrying it is one press rather than retyping. Persisting the rest would be a second durable shape, carrying more
mail-derived material for longer, to save that press.

### What this costs, stated rather than discovered

- **A round trip per advance** instead of a push. It self-coalesces: a read returns everything after the cursor, so a
  burst of advances costs one read, and an advance arriving while a read is in flight starts no second one.
- **Discover gains a table and a sweep** it does not have today. The bounds it needs already exist as prose on its own
  page — five minutes of retention after the last read, and the ten-minute ceiling — so what is new is where they are
  enforced rather than what they are.
- **A run's output becomes rows rather than objects in memory**, which is traffic and storage on the database for
  material that used to cost neither, and one more place holding mail-derived data under this product's retention and
  erasure obligations.

### Consequences

- Good, because a run is reachable from every replica, by every screen the person has open, and across a rolling
  upgrade. The defect this record opens with is closed rather than documented.
- Good, because the guarantee a client is given is the one ADR 0032 already gives every other screen, so there is one
  promise across the client rather than one per surface.
- Good, because the backplane's vocabulary is unwidened, so no mail-derived content reaches the endpoint an operator may
  point at a managed service, and no operator's processing record changes.
- Good, because stopping a run works when the hub does not, and a screen draws with no hub at all.
- Good, because the cursor is exact where `Last-Event-ID` was exact only per replica, and a gap closes on the next
  ordinary read.
- Neutral, because the concurrency bound a run is subject to is now a decision to state rather than an accident of which
  replica answered; issue 2100 states it, either as a bound per person across the deployment or as a per-process bound
  the page admits to.
- Bad, because every advance costs a read that a push did not, so a watched run is more traffic on the routes and on the
  database than a stream was.
- Bad, because a run's output is now persisted mail-derived data with a retention, a sweep, and an erasure obligation,
  where it used to be memory that a restart cleared.
- Bad, because nothing streams below a block, and a person watching a long block sees one working state rather than
  words arriving.
- Bad, because an interrupted run is lost work the person pays for again, and this record accepts that rather than
  closing it.

## Validation

- **The cursor.** Unit tests require a read from a cursor to return only the tail, a read to be answered by a replica
  that did not start the run, and a sequence that never skips across a dropped and re-established read. Issue 2100 owns
  them for Discover, issues 1573 and 1574 for the Agent.
- **The signal carries nothing of the answer.** A unit test asserts the published payload against the vocabulary above —
  no block, citation, count, spend figure, or model name. Issues 2100 and 1574 own it, and every change touching what a
  signal carries is reviewed against ADR 0032 § *The backplane carries client signals and nothing else* and its
  Amendment 3.
- **The client never waits for the hub.** Unit tests require a first read performed with no hub at all, a connected but
  silent hub arming the fallback poll, the poll disarming on a terminal state, a duplicate and an out-of-order advance
  changing nothing, and two advances during one in-flight read producing one further read. Issue 2101 owns them.
- **The interruption.** A unit test requires a run whose execution ends abruptly to be journalled as interrupted rather
  than left pending. Issue 2100 owns it.
- **The retired stream.** The route, the Server-Sent Events composition, and the `Last-Event-ID` handling are gone, and
  the pages naming them are corrected in the same change; issue 2100 owns both halves.

## Pros and Cons of the Options

### The run writes its output to PostgreSQL as it is composed

- Good, because every replica can answer for a run, which is the whole defect.
- Good, because a second screen of the same person, and the same client after a reconnect, reach the same answer.
- Neutral, because the sequence it needs is something both surfaces wanted anyway to render in arrival order.
- Bad, because it is mail-derived data at rest, with the retention, sweep, and erasure obligations that brings.
- Bad, because it is database traffic during a run, on a path that previously touched nothing.

### The output stays in the process, published as it is produced

- Good, because it costs nothing and is what is built today.
- Bad, because a client reaches the replica holding the run about one time in `N`, and the refusal looks like a run that
  never existed.
- Bad, because the bound on concurrent runs is per process, so what a person may start depends on the load balancer.
- Bad, because a second screen, a reconnect onto another replica, and a rolling upgrade all lose the run.

### The output is held until the run ends

- Good, because it is the simplest thing that could work, and a finished answer is one read.
- Bad, because nothing can be drawn while a run is in flight, which is the whole of what both screens are for.
- Bad, because it makes the connection that waited the only route to the answer again.

### A signal naming the run and its sequence, with the client re-reading from its cursor

- Good, because the durable record is the guarantee and the signal is an optimization, which is ADR 0032's settlement
  unchanged.
- Good, because the backplane's vocabulary stays as it is, so no mail-derived content reaches an endpoint outside the
  deployment's own processing.
- Good, because advances coalesce: a read returns everything after the cursor.
- Neutral, because it needs a fallback poll for a hub that is connected and silent, which the client owns.
- Bad, because each advance costs a round trip that a push did not.

### The answer itself pushed over the hub

- Good, because a client would need no read at all, and the screen would advance with the push.
- Bad, because it widens ADR 0032's fixed vocabulary to mail-derived content, and discloses mail to whoever can
  subscribe on the backplane endpoint, which an operator is permitted to run elsewhere.
- Bad, because the backplane buffers nothing, so an answer pushed during an outage is simply gone — and with no durable
  record behind it there is nothing to re-read.
- Bad, because it makes the hub load-bearing for the product's answer, when ADR 0032 deliberately made it optional.

### An HTTP stream from the replica holding the run, resumed with `Last-Event-ID`

- Good, because it is in the protocol, a browser's `EventSource` resumes it unaided, and it is what is built today.
- Good, because it is the lowest-latency delivery of the three, with no read behind it.
- Bad, because resumption addresses a buffer in one process, so it is exact only while the client lands on the replica
  that holds it.
- Bad, because it keeps a run reachable at one replica, which is the defect this record closes.

### The client polls, with no signal at all

- Good, because it needs no hub, no backplane, and no new signal kind.
- Neutral, because it is exactly the fallback this record keeps for a hub that is connected and silent.
- Bad, because every watching client pays a read per interval for the whole of a run, whether anything advanced or not.
- Bad, because an answer advances on the poll's schedule rather than when it was composed, which is what the signal
  buys.

### Hub methods for starting, posting, steering, and stopping

- Good, because a client already holds the connection, so a control would need no second request.
- Bad, because stopping a run would depend on an optional component, at the moment spending most needs stopping.
- Bad, because a hub is mapped outside the client route group and would need its own authentication, bounds, and audit.
- Bad, because a refusal would need an invented payload where HTTP already has a status.

## More Information

- **The issues.** Issue 2099 asks the question and is this record. Issue 2100 applies it to the Discover run, which is
  where the defect is live today, and retires the event stream. Issue 2101 is the client half — the read on mount, the
  re-read on an advance, and the fallback for a hub that is connected and silent — written once in `Client.Backend` so
  both screens follow a run through the same code. The Agent's own children carry it into that surface: issue 1573 holds
  the sequence and the read from a cursor, issue 1574 is the route surface, issue 1575 persists a block as it is
  composed, and issue 1576 draws a screen that never waits for a live connection.
- **What becomes wrong on merge.** `docs/features/discovery-run.md` § *A run is watched rather than waited for* and its
  route table describe the retired stream, and § *Why Server-Sent Events, and why not SignalR* states the reason this
  record overturns: *a run has no use for either* is true of a run addressed by an identifier on one replica and false
  of one addressed by a cursor on any of them. `docs/operations/client-endpoint.md` and
  `docs/operations/permissions.md` name the retired route, and `docs/operations/deployment-kubernetes.md` § *Running
  more than one replica* gains what it can say once the defect is gone. Issue 2100 corrects all of them in the change
  that makes them wrong; nothing is corrected here, because this record changes no behaviour.
- **Other records.** [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) is the
  settlement this record applies, and its Amendment 3 admits the one new signal kind.
  [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) divides work between replicas and is
  untouched: a run is not singleton work, and it still executes where it started.
  [ADR 0009](0009-durable-job-store-and-execution-identity.md) refuses a message broker, and this record takes no new
  exception to it — the backplane use here is the one ADR 0032 already made.
  [ADR 0022](0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md) decides what a run reports about its
  own spending, its cancellation, and the model that answered, and is unchanged: what moves is where a client reads
  those, never what they say.
  [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md) is why a screen that
  draws nothing without a live connection would be the wrong client.
- **Out of scope.** Any code, which the issues above deliver. Resuming an interrupted run, refused above and needing a
  record of its own to revisit. Token-level streaming, whose two routes are named above. Any change to what the six
  existing signals carry. And what a run may spend, which ADR 0022 and the answering ceilings already decide.
- **The `describes:` marker.** It names the code this decision is about as that code exists today: the Discover run's
  streaming and composition, its routes, the signal hub, and the client's signal channel. It gains the persisted run,
  the new route, and the Agent's own store and routes as issues 2100, 2101, and 1573 to 1576 land them.
- **When to revisit.** Revisit when a token-level stream is wanted and somebody has measured what a cursor per token
  would cost. Revisit when a run has to survive the replica that started it, which is the one thing refused here.
  And revisit if the fallback poll turns out to carry the product — that would mean the hub is unavailable often enough
  to be worth replacing rather than falling back from.
