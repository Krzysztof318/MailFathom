---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-11
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Serve the signal hub over WebSockets alone with negotiation skipped, redeem its ticket from PostgreSQL on any replica, carry signals between replicas over an optional RESP backplane with Garnet as the deployed default, and make the client's own re-read the guarantee a signal is not

<!-- describes: backend/src/Host/Signals/**, frontend/src/Client.App/src/signals/signalChannel.ts, frontend/src/Client.Backend/src/signals.ts, frontend/src/Client.Backend/src/reconnection.ts -->

## Context and Problem Statement

[ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) settled how replicas divide work that must not run twice. It said nothing about the one surface where a replica has to reach something it does not hold: a running client's connection. The client's live updates arrive over one SignalR hub, and above one replica two things break. They break independently of each other, and both break silently.

**The connection ticket lives in one process.** `ClientSignalTickets` is a singleton dictionary under a lock. A client mints a ticket over an authenticated route and then opens the hub connection carrying it, and the load balancer routes that second request wherever it likes. A replica that did not mint the ticket has nothing to redeem, so it refuses the connection and reports the refusal only at `Debug`. At `replicaCount: N` a connection succeeds about one time in `N`.

**There is no backplane.** `AddSignalR()` is registered plain, so `IHubContext<ClientSignalHub>.Clients.Group(...)` reaches only the connections this process holds. Since issue 1290 gave each account to one replica, that replica raises every signal for the account, while the client's connection lives on whichever replica answered its handshake. Even an established connection therefore receives nothing for the accounts other replicas hold.

Neither failure corrupts anything and neither is visible: `SignalRClientSignalChannel` swallows a failed delivery by design. What is lost is the feature. Above one replica live updates are effectively off, and nothing tells an operator so. Issue 1870 measured one more fact that changes how much this costs. The client does not catch up on an interval of its own, as the remarks in both packages and `docs/operations/client-endpoint.md` promised. No periodic re-read of mail exists, nothing is re-read after a reconnect, and the stream stops reopening for good after about a minute of failed attempts.

**A third store on the client surface lives in one process as well, and this record does not reach it.** The signed-in session lives in `ClientSessionTokens`, in the process's memory, which is a trade ADR 0023 records. Above one replica, a client's token is known only to the replica that minted it, so the client is signed out on most of its requests. That is not a signal's problem, and issue 1886 owns it. *What a deployment pays* below says what it means for this record.

Issue 1287 refused a message broker or a coordination service for every part of running in more than one replica, and ADR 0009 states the same refusal for durable work. This record decides whether that refusal holds for this surface, and what replaces it where it does not. It produces no code. Issues 1877, 1878, 1879, 1880, and 1881 implement it.

## Decision Drivers

- **Scaling out has to be a replica count, not a load-balancer project.** An operator raising `replicaCount` should not have to configure session affinity, learn which requests belong together, or discover a requirement from a screen that stopped updating.
- **An authentication path must not depend on an optional component.** The ticket is how a connection is authenticated. Whatever stores it has to be present at every replica count, and its outage has to mean the same thing at every replica count.
- **A deployment that needs none of this pays nothing for it.** A deployment serving no client surface, or running one replica, should run no new process and open no new connection.
- **There is no first-party SignalR fan-out over PostgreSQL.** ASP.NET Core ships a backplane for Redis and a hosted service for Azure, and nothing else. Anything else is a `HubLifetimeManager` this project writes, owns, and keeps correct across SignalR's own releases.
- **A signal is already best effort, and nothing can make it otherwise.** The ASP.NET Core documentation states that the Redis backplane buffers nothing while the backplane is unreachable, and a group holding no connection delivers a message nowhere. So the guarantee a client is given has to be something that survives any gap, and a signal is not that thing.
- **Personal data in transit should rest nowhere.** A signal names account and folder aliases, stored identities, flags, and a notification's two lines. Whatever carries it between replicas should keep none of it.
- **The client already connects the way this needs.** `signalChannel.ts` opens the hub with `transport: HttpTransportType.WebSockets, skipNegotiation: true`. Both heads ADR 0021 ships — the web bundle and the Tauri desktop application — speak WebSockets, and so does the Android artifact, which ADR 0027 leaves supported by nothing.

## Considered Options

Three questions are answered together, because the answer to each constrains the other two.

**How does a connection stay on one replica?**

1. WebSockets alone, with negotiation skipped.
2. Every transport, with session affinity at the load balancer.

**How does any replica accept a ticket another one minted?**

1. A ticket store in PostgreSQL, in the shape issue 1835 gave the client-assertion spend store.
2. A ticket store in the backplane's own key space.
3. The ticket stays per process, and session affinity routes the connection to the replica that minted it.

**How does a signal reach a connection another replica holds?**

1. A RESP pub/sub backplane through `Microsoft.AspNetCore.SignalR.StackExchangeRedis`, with Garnet as the deployed default and any Redis-compatible endpoint accepted.
2. PostgreSQL `LISTEN`/`NOTIFY`, through a lifetime manager this project writes.
3. No backplane: accept that above one replica live updates are lost, and let the client's re-read carry the screen.

## Decision Outcome

Chosen options: **WebSockets alone with negotiation skipped**, **a ticket store in PostgreSQL**, and **an optional RESP pub/sub backplane with Garnet as the deployed default**. Together they remove every reason for session affinity, keep authentication on the one store every deployment already runs, and add a second kind of shared infrastructure only where it is needed: to carry client signals between replicas, and for nothing else.

### The hub serves WebSockets alone, and the client never negotiates

SignalR's default handshake is two requests. The first negotiates a transport and returns a connection token, and the second opens the transport carrying that token. Both must reach the same process, which is why the ASP.NET Core scale-out guidance requires sticky sessions. It names three exceptions: one process on one server, Azure SignalR Service, and every client configured to use WebSockets **only** with `SkipNegotiation` enabled. The guidance states that the third holds with the Redis backplane as well.

A connection that skips negotiation is one HTTP request, upgraded. It lives on whichever replica accepted it for as long as it stands, so there is nothing for affinity to keep together. The client already connects this way. The server is brought into line: the hub is mapped with `HttpTransportType.WebSockets` as its only transport, so a server-sent-events or long-polling connection is refused to any caller rather than served to one the client never is. The negotiate endpoint still answers, and the only transport it offers is WebSockets. Nothing here depends on it, because the client never calls it. A caller that does negotiate has to open its socket on the replica that answered, and arranging that is the caller's concern rather than the deployment's.

What this costs is stated here so it is not discovered later:

- **There is no fallback transport.** A network or a proxy that will not pass the WebSocket upgrade gives that client no live updates at all. It is not refused anything else: the client reads its screens over the ordinary routes and catches up on its own schedule, as described under *What a client is promised* below.
- **Stateful reconnect is unavailable.** SignalR's JavaScript client enables it only from the negotiate response, which a skipped negotiation never requests. It would not help here anyway, because its buffer lives in the replica that held the connection, and a reconnect may land on another replica.

### A ticket is redeemed from PostgreSQL, so any replica accepts the connection

The in-process dictionary becomes a table every replica shares, in the shape issue 1835 gave `ClientAssertionSpendStore`. Minting is one insert. Spending is one statement that deletes the row for the presented identifier and returns it only while it is unexpired, so exactly one presentation wins whichever replica each one reaches. A bounded sweep removes what can no longer be presented. The row keeps the secret half only as a digest, compared in constant time, so a row read out of the database or a backup is not a ticket. The 30-second lifetime, the single use, and the bound on outstanding tickets keep their meaning.

**The ticket does not live in the backplane.** The backplane is optional, so a ticket kept there would need a second implementation for the deployments that run none. Authentication would then depend on a component whose outage should cost only signals, and would instead refuse every connection. And the backplane would stop being a pipe that keeps nothing and become a store of authentication state. PostgreSQL is already on the minting path, because the route that mints a ticket has just authenticated a credential against it. One implementation serves every replica count.

**Affinity would work here, and it is still refused.** Issue 1835 refused affinity for the assertion replay store because the party choosing the route was the adversary: somebody replaying a captured assertion simply does not present the cookie. The ticket is the opposite case. The party who needs the route is the legitimate client, and a stolen ticket presented to the wrong replica fails, which is the safe direction. So affinity would keep this working. It is refused for three reasons:

- **WebSockets alone removed every other reason for it.** Keeping affinity for the ticket alone would make every operator configure the load balancer for the sake of one pair of requests, which is exactly what the operator's story refuses.
- **It pins more than the pair.** A cookie pins every request a client makes, and hashing the source address puts every client behind one NAT on one replica. Both distort the load the replicas were added to share.
- **It fails in a rollout.** The replica that minted the ticket is the one being replaced.

**Redemption is bounded where it happens, not by the minting route.** Minting costs one insert, on a route that has just authenticated a credential against the same database. Redeeming costs one delete, and it runs on the hub, which is mapped outside the client route group and has authenticated nothing when a ticket arrives. So redemption is bounded on the hub itself, and issue 1878 owns both bounds:

- **A value without the shape of a minted ticket is refused before any statement runs**: its length, its separator, and its encoding. Today's in-memory `Redeem` checks the length and the separator before its lookup, and the encoding only after it. That order moves, because under this record the lookup is the statement.
- **At most a fixed number of redemptions are in flight in each process**, sized well below the connection pool the rest of the deployment reads mail through. A handshake that arrives while the cap is full is refused without a statement, like any other refused connection, and its client retries on its backoff. A flood of handshakes can therefore hold at most that many connections, never the pool. What it can still do is crowd legitimate handshakes out of live updates while it lasts, which costs signals and never mail, because the client's re-read is the guarantee.

**Two other bounds were considered and refused.**

- **A rate limit counted per caller address.** Behind a proxy the deployment declared, the peer address is the proxy's, and this deployment deliberately reads no forwarded client address. A per-address limit would then be one bucket for every client. `BasicAuthenticationHandler` drops its source axis behind a declared proxy for the same reason.
- **Sealing the identifier under the key ring [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) provisions.** That would refuse a value this deployment never minted with no statement at all. But the ring is optional: a deployment with none is supported, and a capability that needs key material asks whether it is there. An authentication path must not depend on an optional component.

### Signals cross replicas through a RESP pub/sub backplane

`Microsoft.AspNetCore.SignalR.StackExchangeRedis` is the first-party backplane. Every replica subscribes. A message published to a user's group travels to whichever replica holds that user's connections, and that replica delivers it. The channel names carry a prefix, which is `mailfathom` unless the deployment sets one.

**The endpoint speaks RESP.** Where a deployment needs one of its own, the project deploys **Garnet**. Where the operator already runs a Redis-compatible endpoint — Redis, Valkey, or a managed cache — the deployment is pointed at it instead, and the operator runs one RESP service rather than two.

**Garnet over Redis** is the owner's choice, made so that the same component can later serve as a cache. That use is not taken here, as the next section says. Garnet documents its compatibility with StackExchange.Redis and supports `PUBLISH`, `SUBSCRIBE`, and `PSUBSCRIBE`, which is the whole of what the backplane uses. It is MIT-licensed. Redis 7.4 and later is offered under RSALv2 or SSPLv1, and Redis 8 adds AGPLv3 as a third choice, so Garnet is also the image with the simpler terms to deploy.

**A lost backplane is reported, and it does not take a replica out of rotation.** A backplane that drops or comes back is logged at `Warning` and counted. Losing it is exactly what silently turns live updates off, and that is the gap this record exists to close. Readiness does not fail over it: a signal is an optimization, and a pod pulled from service over one is a worse outage than a list five minutes stale.

### The backplane carries client signals and nothing else

ADR 0009 refuses a message broker until PostgreSQL-backed work demonstrates a concrete limitation, and issue 1287 carried that refusal over to running in more than one replica. A RESP pub/sub backplane is a broker in the ordinary sense of the word, so this is an exception, and its boundary is part of the decision:

- **It carries client signals, and nothing else.** The limitation it answers is concrete and narrow: no first-party SignalR fan-out runs over PostgreSQL.
- **It coordinates no work.** No job, lease, ceiling, ticket, or schedule goes through it. Every kind of work that must not run twice stays coordinated through PostgreSQL alone, as ADR 0031 decides.
- **Nothing reads back from it.** It holds no state that any part of MailFathom depends on finding there later.
- **Any later use is a decision of its own.** A cache — the reason Garnet was chosen — would need its own record, weighing what caching mail-derived data in a second store costs against the privacy obligations every derived copy inherits.

### Accepting the degradation lost, and so did `LISTEN`/`NOTIFY`

**Accepting the degradation** would make the client surface a feature that works at `replicaCount: 1` and silently stops above it. The chart would have to refuse more than one replica wherever the client is served, or ship a feature it knows is off. And the client would learn of new mail only through its five-minute safety net, while the product promises that a screen shows what changed when it changed.

**`LISTEN`/`NOTIFY`** looks like the answer that adds nothing to a deployment, and three things rule it out:

- **Each replica holds a listener connection out of the pool** for its lifetime, because a `LISTEN` belongs to the session that issued it. This is the pinned connection ADR 0031 refused for the lease, in a smaller form.
- **There is no first-party SignalR backplane over PostgreSQL.** Using it means writing a `HubLifetimeManager`: group membership across servers, addressing by user and connection, the acknowledgement protocol, and keeping all of that correct against SignalR's own releases. That is a distributed messaging component, owned by a project whose subject is mail.
- **A payload must be shorter than 8000 bytes.** A `mail.flags.changed` signal naming its hundred rows, serialized with the envelope the lifetime manager adds, would approach that limit. Anything over it needs a side table and a second read, which reintroduces storage for what pub/sub keeps nowhere.

It would buy no stronger delivery either: a notification reaches only sessions listening when it is sent, exactly as a pub/sub message does. And it could never become the cache the owner wants the same component to be.

### What crosses the backplane, and what securing an external endpoint takes

**What travels is the serialized hub invocation**: the signal's kind, an account alias, a folder alias, a count, up to a hundred stored identities with the two server flags of each, and a raised notification's kind and two lines. No subject, address, body fragment, or attachment name crosses, which is the vocabulary `docs/operations/client-endpoint.md` already fixes for the wire to the client. The channel a group's messages travel on is named from the group, so the user's identifier crosses as part of a channel name as well.

**That is personal data in transit, and it is stored nowhere.** Pub/sub delivers a message to whoever is subscribed at that moment and keeps nothing. A Garnet the deployment runs for itself runs with no volume, because it has nothing to keep. So no retention period, export, or erasure obligation reaches the backplane. What does reach it is confidentiality: **anyone who can subscribe on that endpoint reads every signal of every user of the deployment.** The endpoint therefore sits inside the deployment's confidentiality boundary, and an operator pointing MailFathom at an external one owes it what they owe the database:

- **Encryption in transit**, which StackExchange.Redis takes as `ssl=true` in the connection string.
- **A credential of its own**, held as a secret reference like every other credential the deployment carries. Where the endpoint supports per-channel permissions, an ACL user limited to publishing and subscribing under the deployment's prefix.
- **A channel prefix no other deployment on that endpoint uses.** A signal would not cross between two deployments at the same prefix, because a group is named from a user identifier each deployment generated for itself. What they would share are the backplane's two fixed channels: the one every replica listens on for messages to every connection, and the one carrying group management. A channel carrying returned results or acknowledgements is named from a server name each process generates for itself, so it is shared by nobody. Each deployment's replicas would receive the other's traffic on them, and that traffic names connections and the groups holding user identifiers. The default is the same string in every deployment, so a shared endpoint needs the prefix set explicitly.
- **Network reach limited to the replicas**, in the same data centre, which is also what the ASP.NET Core guidance asks of a Redis backplane for latency's sake.
- **A recipient entry in the operator's own processing record** where the endpoint is a managed service run by somebody else, since signals are then disclosed to that processor.

### What a client is promised

**A signal is best effort.** It is lost across any gap in the connection: a dropped network, a change of address, a replica restarting, or a rollout. It is also lost across any gap in the backplane, which buffers nothing while it is unreachable. Nothing in this record changes that, and nothing could without a durable queue per connection — which is a broker in the full sense this record refuses.

**The guarantee is the client's own re-read**, and issue 1877 delivers it:

- **After every reconnect**, each screen re-reads what it draws — the folder tree and its counts, the list's held pages, the open message, and the accounts — through the paths a signal already triggers.
- **Every five minutes while the window is visible, and never while it is hidden.** This safety net covers the one gap a reconnect cannot see: a backplane outage during which the socket itself stayed open.
- **The stream never stops reconnecting.** Past its attempt budget it keeps trying at its longest delay, and tries at once when the window becomes visible or the network comes back.

So the worst any lost signal costs a person looking at the screen is five minutes, and a reconnect repairs it at once. A signal decides how soon a screen catches up. Whether it catches up does not depend on one.

### What a deployment pays, and what an operator configures

- **A deployment serving no client surface** pays nothing: no hub, no ticket ever minted, and no backplane connection. The ticket table exists in its schema and stays empty.
- **A deployment running one replica** runs no backplane in any deployment kind. Its tickets go through PostgreSQL, one insert and one delete per connection, so one implementation serves every replica count.
- **The backplane is optional in every deployment kind**: the Helm chart, Compose, and Quadlet can each deploy Garnet or point at an external endpoint, and none of them does by default. Compose and Quadlet run one instance on one host today. They carry the option anyway, so the shape of a deployment is the same whichever kind an operator starts from, and a second host behind their own proxy is not refused a component the chart offers.
- **The chart refuses to render** when `replicaCount` is above 1, the client surface is served, and no backplane is configured. That is the one signal configuration that looks healthy and silently cannot do what it was configured for.
- **That refusal covers the signal surface alone.** Until the session store is shared, a client served by more than one replica is signed out on most of its requests, whatever the backplane. So this record is necessary above one replica, and it is not sufficient. [ADR 0033](0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md) is the answer issue 1886 produced, and issue 1900 carries it out. It needs no second refusal in the chart, because it puts the session in PostgreSQL rather than behind anything an operator can leave unconfigured.

Above one replica, and once issue 1900 has shared the session store, an operator configures four things for the signal surface:

- **A load balancer that passes the WebSocket upgrade**, meaning the `Upgrade` and `Connection` headers on the request.
- **No idle or connection timeout shorter than a connection meant to stand open.** The server sends a keep-alive every 15 seconds, so an idle timeout above that never fires. A cap on a connection's total duration drops it, and each drop costs a reconnect and a re-read.
- **No affinity.** None is required, and configuring it anyway gains nothing while pinning load.
- **A backplane**, which the chart enforces.

### Consequences

- Good, because scaling the signal surface out is a replica count and a backplane. No request pair depends on the load balancer remembering anything.
- Good, because authentication stays on the one store every deployment already runs, with one implementation at every replica count. An optional component's outage costs signals and never connections.
- Good, because a deployment that needs none of this runs none of it, and the chart refuses the one signal configuration that would fail silently.
- Good, because what carries personal data between replicas keeps none of it, and the requirements an external endpoint has to meet are written down here rather than left for an operator to infer.
- Good, because the client's promise no longer rests on the delivery of a signal. A lost signal, a dropped connection, and a backplane outage all end in the same re-read.
- Neutral, because the backplane is first-party code against a third-party endpoint. The package is Microsoft's, while the endpoint's behaviour belongs to whoever runs it — Garnet by default, or anything else speaking RESP.
- Neutral, because Compose and Quadlet gain an option that their single-host shape does not use today.
- Bad, because a second kind of shared infrastructure now sits beside PostgreSQL, with its own image, credential, network reach, and outage to operate.
- Bad, because there is no fallback transport. A network that blocks the WebSocket upgrade gets no live updates, and its client relies on the five-minute re-read.
- Bad, because a backplane outage loses every signal published while it lasts, and the socket shows no sign of it. The five-minute safety net is the only thing that notices.
- Bad, because the ticket, which lived in memory, becomes two database statements per connection. That is small against what a connection already costs, but it is not nothing, and it is traffic on the database.
- Bad, because a flood of handshakes can crowd legitimate ones out of live updates on a replica while it lasts. The in-flight cap keeps it off the connection pool, and the client's re-read keeps the screen current.
- Bad, because the broker refusal now has an exception. Its boundary has to be held by review, since nothing mechanical stops the next need from reaching for the same endpoint.

## Validation

- **The transport.** A unit test over the hub's mapping requires a server-sent-events connection and a long-polling connection to be refused. Issue 1878 owns it.
- **The ticket's bounds.** Unit tests over redemption require two things to be refused without the store being asked: a value without the shape of a minted ticket, and a redemption that arrives while the in-flight cap is full. Issue 1878 owns them.
- **The backplane.** Startup validation refuses a backplane section that names no connection, and a unit test requires nothing to be registered when the section is absent or the client surface is not served. Issue 1879 owns both.
- **The chart.** The golden manifests carry a values case for each mode — Garnet deployed, an external endpoint, and none. A case of `replicaCount: 2` with the client surface served and no backplane must fail to render, and `scripts/render-helm-manifests.sh` holds it. Issue 1880 owns this.
- **The client.** Unit tests require a re-read after every reconnect, and every five minutes while the window is visible and never while it is hidden. Issue 1877 owns them.
- **The ticket, against a real database, and across replicas.** The integration suite, which the owner runs, starts two hosts against one database and one Garnet. It proves that a ticket is spent once, that a second presentation and an expired ticket are refused, that a ticket minted on one host opens a connection on the other, and that a signal raised on one host reaches a connection the other holds. The ticket's proofs belong there rather than in a unit suite, because the store is raw SQL in the shape of `ClientAssertionSpendStore`, which carries `[RequiresIntegrationCoverage]` for the same reason: only PostgreSQL settles what that statement does, and a fake would only prove itself.
- **The boundary.** Every change that touches the backplane's registration is reviewed against *The backplane carries client signals and nothing else*.

## Pros and Cons of the Options

### WebSockets alone, with negotiation skipped

- Good, because a connection is one request and needs no affinity, with or without a backplane, as the ASP.NET Core scale-out guidance states.
- Good, because the client already connects this way, so the change is a server refusing what no client asks for.
- Good, because the one request that opens the connection is the one that presents the ticket, so there is no pair of requests for anything to route together.
- Neutral, because every supported head speaks WebSockets, so no supported client loses anything.
- Bad, because a network or proxy that blocks the upgrade has no fallback transport, and its client gets no live updates at all.
- Bad, because stateful reconnect cannot be enabled without negotiation.

### Every transport, with session affinity

- Good, because a network that blocks WebSockets could fall back to server-sent events or long polling.
- Neutral, because the client does not use any other transport today, so the fallback would be new client work.
- Bad, because every operator above one replica would have to configure affinity, and would learn that from a screen that stopped updating.
- Bad, because the fallback transports need negotiation, which brings back the pair of requests that must reach one process.

### A ticket store in PostgreSQL

- Good, because one implementation serves every replica count, on the store every deployment already runs.
- Good, because the shape is issue 1835's, which is already in production and already understood.
- Good, because authentication never depends on an optional component.
- Neutral, because the table exists in every deployment's schema, including those that never mint a ticket.
- Bad, because each connection costs an insert and a delete that the in-memory store did not, and a handshake nobody minted a ticket for can cost a statement on a path that has authenticated nothing, which only the in-flight cap bounds.

### A ticket store in the backplane's key space

- Good, because it adds no table.
- Bad, because the backplane is optional, so a second implementation would serve the deployments that run none.
- Bad, because a backplane outage would refuse every connection rather than only lose signals.
- Bad, because the backplane would become a store of authentication state, which breaks the boundary that it keeps nothing.

### The ticket stays per process, with affinity

- Good, because it changes no code on the server.
- Neutral, because unlike the replay window of issue 1835 it fails safe: a ticket presented to the wrong replica is refused.
- Bad, because it keeps affinity as an operator's requirement after WebSockets alone removed every other reason for it.
- Bad, because a cookie pins every request and source-address hashing pins every client behind one address, so either distorts how load is spread.
- Bad, because a rollout replaces the replica that minted the ticket.

### A RESP pub/sub backplane, Garnet by default

- Good, because the backplane is first-party and documented, and it is the scale-out ASP.NET Core recommends for self-hosted infrastructure.
- Good, because pub/sub keeps nothing, so personal data crossing it rests nowhere.
- Good, because an operator who already runs a Redis-compatible endpoint points at it rather than running a second one.
- Good, because Garnet is MIT-licensed, documents compatibility with StackExchange.Redis, and supports the pub/sub commands the backplane uses.
- Neutral, because the owner chose Garnet so the same component can later serve as a cache, a use this record does not take.
- Bad, because it is a second kind of shared infrastructure to deploy, secure, and watch.
- Bad, because it buffers nothing while unreachable, so an outage loses every signal published during it.

### PostgreSQL `LISTEN`/`NOTIFY`

- Good, because it adds nothing to a deployment.
- Neutral, because it delivers only to sessions listening when a notification is sent, so it is no more durable than pub/sub.
- Bad, because each replica pins a listener connection out of the pool for its lifetime.
- Bad, because there is no first-party SignalR backplane over it, so this project would write and own a `HubLifetimeManager`.
- Bad, because a payload must be shorter than 8000 bytes, and anything larger needs a side table that brings storage back.
- Bad, because it cannot serve as the cache the owner wants the same component to be.

### No backplane, and the degradation accepted

- Good, because it costs nothing and changes nothing.
- Bad, because above one replica live updates are effectively off and nothing reports it.
- Bad, because the chart would have to refuse more than one replica wherever the client is served, or ship a feature it knows does not work.
- Bad, because the client's five-minute safety net would become its only source of new mail, which is not the product it promises.

## More Information

- **The issues.** Issue 1838 asks the question. Issue 1870 is the plan and the measurements this record is written against. Issue 1878 delivers the WebSocket-only hub and the PostgreSQL ticket. Issue 1879 delivers the backplane. Issue 1880 puts Garnet or an external endpoint into the chart, together with the refusal. Issue 1881 does the same for Compose and Quadlet. Issue 1877 is the client's catch-up, which this record names as the guarantee. It waits on nothing here, because a single replica already loses signals across a dropped connection. Issue 1287 carries the exception to its broker refusal, and issues 1294 and 1295 wait on the four server-side children. Issue 1886 decided how a signed-in session survives more than one replica, which this record does not reach; [ADR 0033](0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md) is that answer, issue 1900 implements it, and issue 1295 waits on it as well.
- **ADR 0009 and ADR 0031.** [ADR 0009](0009-durable-job-store-and-execution-identity.md) states the refusal of a message broker that this record makes one exception to, and that refusal stands for everything else. [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) divides the work between replicas, and this record does not touch it. This is a new record rather than an amendment to ADR 0031, because it adds a second kind of shared infrastructure beside PostgreSQL rather than refining the lease. The three records are linked in both directions.
- **Other records.** [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs how the Garnet image and the backplane package are reviewed in `THIRD_PARTY_LICENSES.md`. [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) decides the two heads that ship, and [ADR 0027](0027-an-android-head-built-every-night-and-supported-by-nothing.md) the Android artifact beside them that nothing supports. All three speak WebSockets.
- **Out of scope.** Azure SignalR Service and every other hosted backplane, any change to what a signal carries, and any use of the RESP endpoint other than client signals.
- **The `describes:` marker.** It names the code this decision is about as that code exists today: the hub, the ticket, and the channel under `backend/src/Host/Signals/`, and the client's transport and reconnection. It gains the ticket store, the backplane's registration, and the deployment assets as issues 1878 to 1881 land them.
- **When to revisit.** Revisit when a supported head cannot speak WebSockets, or a deployment's network blocks the upgrade and needs a fallback. Revisit when the RESP endpoint is wanted for anything but client signals, which is a record of its own. Revisit when a first-party SignalR backplane over PostgreSQL appears. And revisit when signals lost during a backplane outage are measured as a problem the five-minute re-read does not cover.
