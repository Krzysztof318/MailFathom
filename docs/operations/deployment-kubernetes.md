# Deploying to Kubernetes

<!-- describes: deploy/helm/** -->

`deploy/helm/mailfathom/` is the chart. It installs MailFathom, the objects around it, and — unless you tell it
otherwise — the PostgreSQL server it stores mail in. It deliberately installs no Secret: credentials belong to whoever
operates the cluster, and the chart is written so that it cannot pretend otherwise.

| It renders | It does not render |
| --- | --- |
| Deployment, Service, ConfigMap, ServiceAccount | Any `Secret` |
| A PostgreSQL StatefulSet, its Service, and its initialization script, unless `database.deploy.enabled` is false | Any certificate material |
| A personal-data analyzer Deployment and Service, and a SpamAssassin Deployment and Service, only when the section that owns each is enabled and left to deploy its own | Any schema step |
| A Silo object-store StatefulSet, its claim, and its Service, only when `contentStorage.objectStorage.deploy.enabled` is true | Any bucket, or any access key inside one |
| A Valkey StatefulSet and two Services for the signal backplane, only when `signalBackplane.enabled` and `.valkey.deploy` are both true | |
| An optional Ingress | |

## What you supply

Two things have no default and the chart refuses to render without them. The third has one, and choosing it is the
decision this section is mostly about.

**An image.** Released images are on both registries under the same digest, and the chart still defaults to none of
them: a default would pin every install to whichever version this chart happened to name, and a moving one would let a
cluster follow a version nobody chose. You name the immutable reference your deployment runs.

**A Secret.** The chart names one rather than creating it.

**A database is the third thing, and it has a default.** The chart runs PostgreSQL with the `vector` extension as a
single-replica StatefulSet on a retained PersistentVolumeClaim, from the `pgvector/pgvector` image the Compose
deployment and the local orchestration pin to the same version. It is not a subchart: the templates are in this
repository and change in the diff that changes them.

That default is the smaller of two arrangements, and it is worth knowing which one you are choosing. A claim gives the
data a lifetime longer than the pod's and nothing else — no backup schedule, no failover, no point-in-time recovery,
and no upgrade path across a PostgreSQL major. Point the chart at a server you already operate once any of those is
somebody's job:

```yaml
database:
  deploy:
    enabled: false
  host: postgres.databases.svc.cluster.local
```

The two are exclusive and the chart says so rather than preferring one: `database.host` is required when
`deploy.enabled` is false, and refused when it is true, where the address is derived from the release name. A
deployed server is reached at `<release>-postgres` in the release's own namespace.

The role MailFathom connects as is never a superuser, in either arrangement. When the chart deploys the server, its
initialization script runs once on the empty data directory, creates the role that owns the database, and installs the
`vector` extension while a superuser is still connected — which is the same script, and the same reasoning, the Compose
deployment uses.

That is why a deployed database needs **two** Secrets rather than one more key. `secrets.existingSecret` is mounted
whole into the application pod, because the keys MailFathom reads are the ones your own configuration names and the
chart cannot enumerate them; a superuser credential placed there would be readable by the process that serves the
network and parses untrusted mail, which is precisely the boundary the unprivileged role exists to draw. So the
superuser password lives in a Secret of its own, the application never mounts it, and the chart refuses a values
document that names one Secret for both. The database pod, in turn, sees exactly one key of the application's Secret —
the password it must create MailFathom's role with — and nothing else. Compose separates the same two credentials the
same way, by leaving the superuser password off the `mailfathom` service's own secret list.

```bash
kubectl create namespace mailfathom

kubectl --namespace mailfathom create secret generic mailfathom-secrets \
  --from-literal=mailfathom-database-password='…' \
  --from-literal=admin-api-key='…' \
  --from-file=imap-primary-password=./imap-primary-password \
  --from-file=mailfathom-data-key=./mailfathom-data-key

# Only for a database the chart deploys, and named by database.deploy.superuserPasswordSecret.
kubectl --namespace mailfathom create secret generic mailfathom-postgres-superuser \
  --from-literal=postgres-superuser-password='…'
```

Both database passwords are applied by `initdb` on the first start and never again, so changing either in its Secret
afterwards changes what is presented rather than what the server accepts — rotate them in the server as well, which
[secret rotation](secret-rotation.md) covers.

The Secret is mounted read-only at `/etc/mailfathom/secrets`, one file per key, so every credential is a `file:`
reference — the same path and the same references the Compose deployment uses.

The administrative key is in that list because a deployment that cannot be administered cannot be given a mailbox:
every mail account belongs to a user's record, and [recording the
mailbox](#recording-the-mailbox) below is the write that puts one there. The mailbox password beside it is
what that record's declaration will reference; nothing in the ConfigMap names it.

**The encrypted systemd credentials the native installation uses do not reach a pod**, and they would work against this
shape if they did: nothing schedules a systemd unit here, and that encryption binds material to one machine while every
replica has to open what any other replica sealed. What protects these at rest is the cluster's own Secret encryption,
which is configured on the API server rather than here and is absent until the cluster enables it — upstream Kubernetes
stores a Secret's values unencrypted in etcd without an `EncryptionConfiguration`.
[What an encrypted credential is bound to](secret-provisioning.md#what-an-encrypted-credential-is-bound-to) states the
binding that makes it a poor fit here.

The last entry is the data-encryption key, and it belongs in this Secret rather than in a chart value: the chart creates
no Secret and generates nothing, deliberately, because a Helm-generated key would be replaced on any upgrade that did
not guard it with `lookup` — and `lookup` returns nothing during `helm template`, during a dry run, and under Argo CD.
Every value already sealed would stop opening. Generate it once with `openssl rand -base64 32`; the material decodes to
exactly 32 bytes and startup refuses any other length. Drop the line when no account authenticates with OAuth, since a
deployment that seals nothing needs no key.
[The data-encryption key](secret-provisioning.md#the-data-encryption-key) states the rest, including why it is backed
up with the database and never regenerated.

A Secrets Store CSI driver works, with one step this chart does not take for you. The pod mounts a Kubernetes `secret`
volume and exposes no CSI volume of its own, so configure the driver's `secretObjects` to **synchronize** into the
Secret named by `secrets.existingSecret`; the chart then mounts it like any other. Mounting the CSI volume directly
would need `extraVolumes` and `extraVolumeMounts` values, which the chart deliberately does not have — an arbitrary
volume list is how a chart stops being able to say what its pod reads.

## Installing

```yaml
# values.yaml
image:
  registry: ghcr.io             # or docker.io; both carry the same digest
  repository: krzysztof318/mailfathom
  digest: sha256:…              # or an immutable tag

database:
  name: mailfathom
  user: mailfathom
  deploy:
    # The chart deploys the database, so it names the Secret holding the superuser password — the second one
    # created above. Replace this block with `deploy: {enabled: false}` and a `host:` to use your own server.
    superuserPasswordSecret: mailfathom-postgres-superuser

secrets:
  existingSecret: mailfathom-secrets

config:
  files:
    10-mailfathom.json: |
      {
        "MailSynchronization": {
          "Enabled": true
        },
        "AdminEndpoint": {
          "Enabled": true,
          "Authentication": [
            { "ApiKey": { "Name": "admin", "SecretReference": "file:/etc/mailfathom/secrets/admin-api-key" } }
          ]
        },
        "McpEndpoint": {
          "Enabled": true,
          "Authentication": [
            { "Method": "api-key" }
          ]
        }
      }
```

```bash
helm install mailfathom oci://ghcr.io/krzysztof318/charts/mailfathom \
  --version <x.y.z> --namespace mailfathom --values values.yaml
kubectl --namespace mailfathom rollout status deployment/mailfathom
```

The chart is published to GHCR as an OCI artifact by the release that publishes the image, under the same version. Its
`appVersion` is that release, so a chart says which application version it deploys without being unpacked:

```bash
helm show chart oci://ghcr.io/krzysztof318/charts/mailfathom --version <x.y.z>
gh attestation verify oci://ghcr.io/krzysztof318/charts/mailfathom:<x.y.z> --repo Krzysztof318/MailFathom
```

The chart is on GHCR alone, where the image is on both registries. Docker Hub's namespace is `namespace/name` and
nothing deeper, so a chart pushed there would land in the repository the image already occupies and collide with its
tags. It is also listed on [Artifact Hub](https://artifacthub.io/packages/helm/mailfathom/mailfathom).

That listing is rendered entirely from the chart package, which is why `Chart.yaml` carries more than the fields Helm
requires. Its `description` is the summary the listing shows, so it opens with what the product is and names the
protocol last; Artifact Hub imposes no length there. Its `keywords` are what a search there matches, narrowed to terms
an operator would search for and to capabilities this release implements — a keyword is a claim about the artifact it
is attached to, so the roadmap's terms are absent from it. `artifacthub.io/category` is stated rather than omitted,
because Artifact Hub otherwise predicts a category from those keywords with a machine-learning model. The overview
below all of it is `deploy/helm/mailfathom/README.md`, a committed page written for the reader a chart listing has —
somebody who has already chosen Kubernetes and Helm — rather than for the reader deciding whether to adopt the project,
which is what the root README is for. Nothing is substituted at package time, so a listing renders the page reviewed in
the diff that changed it, and it is the same page an operator browsing the chart directory reads.

Installing the chart directory out of a checkout is the development path and stays available:

```bash
helm install mailfathom deploy/helm/mailfathom --namespace mailfathom --values values.yaml
```

An unpackaged directory states no `appVersion`, because it is not a release of anything, so the version-drift check
below stands down for it.

The notes an install prints carry the documentation for the version they installed, as a `Docs:` line naming that
version's own directory on the documentation site — `https://krzysztof318.github.io/MailFathom/v<version>/`, or
`latest` on the nightly channel, which is what a nightly actually carries. It is an address rather than a repository
path because somebody reading `helm install` output has no checkout to resolve one against, which is why the notes'
pointer to [applying the schema](database-schema.md) is an address as well. The unpackaged directory states no version
to name a directory from, so it prints no `Docs:` line and its schema pointer is the site's version-agnostic address
instead.

A digest is preferred over a tag: it is the only reference a registry cannot repoint, so a rollback goes back to the
same bytes. `values.schema.json` rejects `latest` and the other moving tags outright.

Nothing in the ConfigMap may be a credential — it is readable by anything holding `get` on it, and it is reached by
neither the at-rest encryption a cluster can enable for Secrets nor the auditing a Secret gets. The chart puts no credential there and none in the rendered Deployment; the
verification script asserts that on every change.

## Applying the schema

MailFathom verifies the schema while starting and refuses to serve against one it does not recognize. The first install
therefore does *not* become ready, and its log says why:

```
DatabaseSchemaOutOfDateException: The database has not applied 1 migration(s) this build defines: 20260731132336_Initial.
```

That is the design, and the chart deliberately renders nothing that answers it: a Job carrying a Helm hook would be the
automatic migration this whole arrangement exists to prevent, and an `initContainer` would run one apply per replica.

The answer is `mailfathom-schema-<version>.sql`, attached to every release. Take a backup, read the SQL, and run it
from wherever the database is already reachable:

```bash
kubectl --namespace databases port-forward service/postgres 5432:5432 &

psql "postgresql://mailfathom_migrator@127.0.0.1:5432/mailfathom" \
  --set ON_ERROR_STOP=on \
  --file 'mailfathom-schema-<version>.sql'
```

A database this chart deployed is reached through its own pod, and the extension half of the paragraph below is already
done there — the initialization script installed `vector` while a superuser was connected, so the script's
`CREATE EXTENSION IF NOT EXISTS vector` finds it present:

```bash
kubectl --namespace mailfathom exec -i statefulset/mailfathom-postgres -- \
  psql --username mailfathom --dbname mailfathom \
    --set ON_ERROR_STOP=on < 'mailfathom-schema-<version>.sql'
```

That applies the DDL as `database.user`, which then owns every object it created and needs no grants afterwards. Back
the database up the same way, with `pg_dump` through the pod, rather than by copying the claim's files: a file copy of
a running server's data directory is not a backup of it.

The role that applies it needs privileges `database.user` does not — the `vector` extension is one an ordinary role may
not create — and PostgreSQL leaves whoever ran the DDL owning every object it created, so `database.user` needs grants
rather than a transfer of ownership. [Applying the database schema](database-schema.md) states both in full, along with
the locks the script takes and what each startup failure means.

## Recording the mailbox

A started deployment holds no user and reads no mailbox, and no ConfigMap entry changes that: who a deployment serves
and which mailboxes it reads are rows it keeps rather than settings it reads. Each is recorded over the administrative
endpoint, which is why the values above turn it on — reach it with a port-forward and record them:

```bash
kubectl --namespace mailfathom port-forward service/mailfathom 8080:8080 &

mfctl login --endpoint http://127.0.0.1:8080
mfctl user add --display-name Alex
mfctl user account add --from-file mailbox.json
```

`mailbox.json` is the JSON object one mail account is declared as, and its `Secrets.Password` reference names the same
mounted path every other credential here does — `file:/etc/mailfathom/secrets/imap-primary-password`, one of the keys
of the Secret above. The mailbox is served from the moment the write commits, without a rollout. `mfctl user account add`
names no user, because the deployment then holds exactly one; once `mfctl user add` records a second person, `--user`
says whose record a command writes.
[Getting started § write down the mailbox](../users/getting-started.md#2-write-down-the-mailbox) is what goes in the
file.

**Every replica picks the record up.** A write reaches the replica that served the request at once; the others read it
on their next user write or restart, so a deployment scaled past one serves a newly recorded mailbox from one replica
first rather than from all of them at once.

## TLS and reaching it

MailFathom speaks plain HTTP inside the cluster and terminates no TLS of its own. **The chart never issues, templates, or
stores certificate material** — `secretName` names a Secret the cluster already holds, whether an operator created it
or cert-manager did.

```yaml
ingress:
  enabled: true
  className: nginx
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt
  hosts:
    - host: mailfathom.example.test
      paths:
        - path: /mcp
          pathType: Prefix
  tls:
    - secretName: mailfathom-tls
      hosts:
        - mailfathom.example.test
```

**Each path belongs to a surface the application serves only if its own section enabled it**: `/mcp` to `McpEndpoint`,
`/api/admin` to `AdminEndpoint`, and `/api/client` to `ClientEndpoint`. Publishing a path whose section is off routes to
a listener that answers `404`, and enabling a section without publishing its path leaves it reachable inside the cluster
alone — which is a legitimate posture for the administrative surface and never one for
[the client surface](client-endpoint.md), whose caller is somebody's own machine. All three default to container port
8080, so one Service and one backend carry however many of them a deployment enabled.

An Ingress without a `tls` entry hands the API key and every message served to anything on the network path. The chart
renders it, because there are networks where that is a real choice, and warns in its notes.

Without an Ingress the Service is reachable only inside the cluster:

```bash
kubectl --namespace mailfathom port-forward service/mailfathom 8080:8080
```

## What the pod serves by default

Plain HTTP on port 8080, with no authentication, no CORS gate, no mTLS, and no rate limiting. That is the usual
Kubernetes arrangement — an ingress or a service mesh in front of the workload owns TLS termination and whatever
client authentication the cluster imposes — and it is why the chart neither templates certificate material nor asks
for a credential to start.

Every one of those is a MailFathom setting rather than a chart value, so turning one on is a ConfigMap entry under
`config.files` and nothing else changes:

| To turn on | Configure | Reference |
| --- | --- | --- |
| API keys | `McpEndpoint:Authentication`, which names the method; each user's key is minted with [`mfctl credential create`](admin-endpoint.md#user-credentials) rather than mounted as a Secret | [Authentication](mcp-endpoint.md#authentication) |
| An `Origin` gate | `McpEndpoint:Cors` | [CORS and the `Origin` header](mcp-endpoint.md#cors-and-the-origin-header) |
| Reading the public scheme and host from the ingress alone | `ReverseProxy:TrustedProxies` | [Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) |
| TLS terminated by the pod itself | `McpEndpoint:Https:Endpoints` | [HTTPS and your own domain](mcp-endpoint.md#https-and-your-own-domain) |
| Client certificates | `McpEndpoint:ClientCertificateProfiles` | [Client certificates](mcp-endpoint.md#client-certificates) |
| Rate limits | `McpEndpoint:RateLimiting`, and `AdminEndpoint:RateLimiting` or `ClientEndpoint:RateLimiting` for the other surfaces | [Rate limiting](mcp-endpoint.md#rate-limiting) |
| The surface the MailFathom client reaches | `ClientEndpoint`, whose `Cors:AllowedOrigins` names the origin every client but this deployment's own page calls from, a downloaded head included | [The client endpoint](client-endpoint.md) |
| The client itself, served as a page | `client.enabled`, which is a chart value rather than a ConfigMap entry — see [serving the client](#serving-the-client) | [Serving the client from the deployment](client-endpoint.md#serving-the-client-from-the-deployment) |

The ingress row is the one an OAuth deployment should not skip, and it narrows rather than enables. The controller
terminates TLS and dials the pod over plain HTTP under the Service name, and MailFathom reads the forwarded scheme and
host from any peer until you say otherwise — so discovery completes out of the box, and until `TrustedProxies` names
the ingress, anything else that can reach the pod can set those headers too. Name the pod CIDR the ingress controller
runs in, which `kubectl cluster-info dump | grep -m1 cluster-cidr` reports on most distributions. A `ClusterIP`
Service is not a substitute: it keeps the pod off the cluster's edge, not away from every other pod.

Configuring `Https:Endpoints` with a TLS `Transport` moves where the endpoint answers, so the chart's `service.port` and the `http`
container port have to match what the profiles bind. The probe listener is unaffected and keeps its own transport. That is a deliberate step rather than the default: in a cluster, TLS at the ingress is
usually what an operator already has.

The credentials any of them reads stay `file:` references into the mounted Secret. Keep them out of `config.files` and
out of `config.extraEnvironment`; the values schema rejects an environment name that reads like a credential, because
an environment block is visible to anything that can read `/proc` and cannot be erased from process memory.

## Serving the client

**The client travels inside the image**, so serving it renders no second workload, publishes no second host, and
pulls nothing:

```yaml
client:
  enabled: true
```

This is the only chart value in this section rather than a ConfigMap entry, because the chart has to write more than
one setting for it and one of those depends on what the chart can see. `client.enabled` writes
`ClientEndpoint__Application__Enabled`, and — only where `ingress.enabled` and `ingress.tls` are both set — it also
writes `ClientEndpoint__Application__AllowClearText`. That second key is a declaration that something in front of this
pod terminates TLS, which the application requires before it will serve a page over a clear-text socket: the page, and
every token a browser then sends back, cross whatever hop is between it and the person, and nothing inside the pod can
tell an ingress from a network. Where TLS terminates somewhere the chart cannot see — a service mesh, a gateway, an
external load balancer — MailFathom refuses to start and names the setting; add it to `config.extraEnvironment`
yourself once you have confirmed the hop a browser actually makes is HTTPS.

Two more things belong to the same decision:

- `ClientEndpoint:Enabled` in `config.files`, because the page is served on that surface's listeners and calls its
  routes. The page turned on while the endpoint is off is refused at startup, naming both; the reverse — the
  endpoint serving its routes with no page in front of them — is an ordinary deployment and starts.
- A `/` path in `ingress.hosts[].paths`, beside the `/api/client` the page calls, if the ingress is what people reach
  it at.

**Authorization is unchanged.** A browser is an untrusted client wherever it was served from, so whatever
`ClientEndpoint:Authentication` requires is still required of it. Serving the page grants nobody anything; what it
exposes is the application's own code, which is published under AGPL-3.0-only in this repository.

## Running more than one replica

`replicaCount` is a value like any other, and raising it is supported:

```yaml
replicaCount: 3
```

**What it gives is capacity and an upgrade with nothing down, and never a second copy of any work.** Every request, tool
call, and page is answered by whichever replica the Service routed it to, and that is the whole of the gain on the
serving side. On the working side a scope is held by one replica at a time under a lease row — one mail account's
synchronization, one deployment-wide sweep, one operator-asked re-derivation, the stored-content move — so three
replicas divide the accounts between themselves rather than each synchronizing all of them.
[ADR 0031](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)
is that mechanism, and the administrative surface answers who holds what.

**What one replica does that several do not** is worth reading before raising the number, because two of the three are
not about MailFathom at all:

- **A single account is not synchronized faster.** Its supervision is one lease, so the parallelism is between accounts
  and never inside one. A deployment with one large mailbox gains nothing here.
- **The database is not made available.** One the chart deploys is a single-replica StatefulSet on a ReadWriteOnce
  claim whatever this number says, so a node taking that pod takes the deployment with it. Point the chart at a server
  somebody operates before the replica count is what your availability rests on.
- **Every ceiling worded as one process's multiplies.** `Jobs:MaxConcurrentJobs`,
  `MailSynchronization:MaxInFlightRawMimeBytes`, `Embeddings:MaxQueuedEmails`,
  `Resilience:AiProviderInvocation:ConcurrencyLimit`, and every endpoint's `RateLimiting` are each replica's own,
  deliberately: what they bound is that
  process's threads, memory, and sockets. A deployment of *n* replicas runs up to *n* times them, so a provider's own
  concurrency is respected by dividing the figure rather than by restating it. Every ceiling worded as the
  deployment's — the two content-storage ceilings, the paced request rates, the answering spend — is a row each replica
  shares and stays the figure it names. The [configuration reference](configuration-reference.md) says which of the two
  each setting is, per setting.

**No guarantee is weakened by several replicas, and one budget is multiplied by them.** A client assertion is spendable
once for the whole deployment, because the record of a spent identifier is a row under a unique constraint rather than a
dictionary in one process — so an identifier any replica served is refused by every replica, and there is no window to
route around. It is a shared store rather than affinity for a reason worth stating: affinity is honoured by the client,
and the client here is whoever captured the assertion.
[The assertions this deployment has already served](../architecture/stored-email-schema.md#the-assertions-this-deployment-has-already-served)
is the table.

**The endpoint rate limiters are the exception, and they are a bound rather than a guarantee.**
`McpEndpoint:RateLimiting`, `AdminEndpoint:RateLimiting`, and `ClientEndpoint:RateLimiting` each count in one process,
so a deployment of *n* replicas admits up to *n* times what one of them declares. What `TokenCapacity` is the burst of
differs by surface: one caller's, at one replica, on the MCP and client surfaces, and **the whole endpoint's** on the
administrative one, where the credential is judged behind the limiter so an administrative request is still anonymous
when it is counted and every caller shares one bucket. That is the surface where this matters most, because the limiter
is what stands between an API key and unbounded guessing — so divide the figure when the replica count is raised rather
than leaving it as it was sized for one process.
[Rate limiting](configuration-endpoints.md#rate-limiting) is every value and which of the two it is, and
[the administrative endpoint's own](admin-endpoint.md#rate-limiting) is why its bucket is not partitioned.

**A signed-in session needs nothing either.** It is a row in PostgreSQL, so every replica accepts a session any other
replica minted and honours its revocation the moment it is written;
[the session token routes](client-endpoint.md#the-session-token-routes) is that half. What a client's live updates need
is [the backplane below](#signals-between-replicas), which is the one thing above one replica the chart refuses to
install without.

### What your load balancer owes the client's connection

The signal channel is a WebSocket and it never negotiates a fallback, so whatever routes traffic to these pods owes it
three things. They are the ingress controller's, the mesh's, or the cloud load balancer's settings rather than chart
values, which is why they are stated here and templated nowhere:

- **Pass the WebSocket upgrade.** A proxy that strips `Connection` and `Upgrade`, or answers the handshake itself,
  leaves the client with no channel and a screen that updates only on its own re-read.
- **Keep no idle timeout shorter than a standing connection.** The connection carries a keep-alive frame rather than
  traffic, so a timeout measured on bytes closes a healthy channel; the client reconnects and re-reads, which costs a
  request per timeout and a gap per reconnect.
- **Use no session affinity.** It is not needed for anything — the session is a row, the ticket redeeming the handshake
  is a row, and a signal crosses replicas over the backplane — and turning it on concentrates connections rather than
  balancing them.

### What the chart renders for the rollout and the drain

Two objects an operator would otherwise have to add themselves, both from values:

```yaml
strategy:
  type: RollingUpdate
  rollingUpdate:
    maxUnavailable: 0
    maxSurge: 1

podDisruptionBudget:
  enabled: true
  maxUnavailable: 1
```

**The rollout keeps a pod serving at every instant**, because `maxUnavailable: 0` starts the replacement and waits for
its probes before anything is taken away. That is what makes the schema order above hold on an upgrade as well as on an
install: a new pod that refuses a schema behind it never becomes ready, and the pods already serving are still there
rather than having been replaced by it. It also means **two versions run together for the length of a rollout**, which
is safe rather than tolerated — a claim reaches only the job types the claiming replica's own build registers a handler
for, so work a newer replica introduced waits for one instead of failing on an older one; two replicas reaching one
scheduled occasion compose the same idempotency key; and a leased scope is released by the pod being stopped and
claimed by whichever replica takes it next. `Recreate` is the other type Kubernetes accepts, and the chart
renders no `rollingUpdate` block beside it, so choosing it produces a Deployment the API server accepts.

Either half of `rollingUpdate` may be zero and both may not, and the chart refuses that pair rather than letting the
API server reject the Deployment while a release is being applied. The refusal exists because a values document is
merged into the chart's defaults rather than replacing them: writing `maxSurge: 0` alone — which is what a tight quota
asks for — keeps `maxUnavailable: 0` underneath, and a rollout allowed neither a pod below the replica count nor a pod
above it can never replace anything.

**A rolling upgrade hands leased work over rather than parking it.** A pod stopping releases every lease it holds, so
the account it was synchronizing is claimable immediately. The release is an optimization of the ordinary case and
nothing rests on it: a replica that was killed rather than stopped leaves its scopes held until they expire, which is
`MailSynchronization:LeaseDuration` for an account and two minutes by default. Raise
`terminationGracePeriodSeconds` and `MailSynchronization:ShutdownDrainTimeout` together if a drain has to wait for
longer work — the grace period shorter than the drain kills the process with the drain still running.

**The PodDisruptionBudget is what makes a node drain pace itself.** Without one, a drain reaching two nodes evicts every
replica on them at once, and each scope those replicas held then waits out its expiry with nothing running it — which
for a deployment carrying leases is an outage of synchronization rather than a handover. It is expressed as how many
pods may be missing rather than how many must remain, which reads the same at every replica count: a budget demanding
one pod remain would refuse every drain of the single-replica default, and a cluster upgrade that never finishes
reports itself as a node that will not cordon. Turn it off where something else in your cluster owns disruption policy
for this workload.

## Signals between replicas

A client's live updates arrive over one connection, held by whichever replica answered its handshake. Above one replica
that is routinely not the replica that synchronized the account something happened in, and a signal raised there reaches
only the connections that replica holds. What carries it across is a RESP pub/sub endpoint, and the chart either deploys
one or is pointed at one:

```yaml
replicaCount: 2

signalBackplane:
  enabled: true
```

That renders a one-instance Valkey StatefulSet on no claim, a ClusterIP Service naming it, a headless Service giving
each instance a name of its own, and the three keys the application reads. Point it at an endpoint you already operate
instead with `signalBackplane.valkey.deploy: false`, and nothing of a workload is rendered while the application is
configured identically. The difference between the two is what is deployed and nothing the application reads, because
the connection string names the endpoint either way.

**Which server answers is yours, and the contract is the protocol.** MailFathom asks the endpoint for `PUBLISH`,
`SUBSCRIBE`, and `PSUBSCRIBE` and nothing else, so Redis, Garnet, Valkey, or a managed RESP endpoint all serve it
equally. Valkey is what this chart deploys, pins by digest, and is tested against — a default rather than a
requirement, and `signalBackplane.valkey.deploy` is where the choice is made.

**The chart refuses to render `replicaCount` above 1 with the client surface served and no backplane configured.** It is
the one combination here that installs, starts, and answers every request while doing none of what it was configured to
do: nothing fails, no probe goes red, and the clients are simply never told anything. Everything else about it looks
healthy, which is why it is refused at `helm install` rather than left to be found from a mailbox that stopped updating.
A deployment that serves no client surface is never refused over this, whatever its replica count, because nothing there
raises a signal. What "served" means is read from three places, since the surface is a configuration key rather than a
chart value: `client.enabled`, any `config.files` document whose `ClientEndpoint.Enabled` is true, and
`ClientEndpoint__Enabled` in `config.extraEnvironment`.

**The connection string is yours to write in both shapes**, as a key inside `secrets.existingSecret` beside the database
password. That is not an omission: MailFathom reads one connection string, it carries the password, and this chart
templates no credential and creates no Secret — for the reason [what you supply](#what-you-supply) gives about the
data-encryption key, which is that a Helm-generated value is replaced on any upgrade not guarded by `lookup`, and
`lookup` returns nothing under `helm template`, under a dry run, and under Argo CD. For a Valkey the chart runs, what to
write is the Service it rendered and the password you gave the server. That Service is the release's full name with
`-valkey` appended, so the example below reads `mailfathom-valkey` because the release is installed as `mailfathom`; a
release named `prod` gets `prod-mailfathom-valkey` instead, and the install notes print whichever it is. It is the
same string whether or not the server is [replicated](#replicating-the-backplane), because that Service names one
instance either way:

```bash
kubectl --namespace mailfathom create secret generic mailfathom-secrets \
  --from-literal=mailfathom-signal-backplane-password='…' \
  --from-literal=mailfathom-signal-backplane-connection-string='mailfathom-valkey:6379,password=…' \
  … every other key this deployment reads
```

Two keys rather than one, because the server reads a bare password and MailFathom reads a connection string, and those
are the two forms the two programs accept. Both are in the Secret the application pod mounts, which is the opposite of
the database superuser password and the object store's root credential — and for a reason those two do not have: this
password is one the application pod already holds, inside the connection string. Keeping it out of that Secret would
protect nothing and would add a second Secret to create. The server's own pod reads that one key and mounts nothing else
of it. The install notes print the exact connection string to write.

**Write both keys before turning the block on**, because neither pod starts without them. MailFathom proves the
connection string's reference while the host is built and refuses to start when the mounted file is absent, and the
kubelet cannot create the backplane containers at all without the password key, so a rollout begun without them never
completes. That is the reference being provable rather than the endpoint answering, and the two are not the same
refusal: an endpoint that does not answer fails nothing, which is what losing the backplane below is about.

**Rotating that password is a short signal outage rather than a rolling one**, which is the one place a Valkey the chart
runs differs from what [rotating the connection string](secret-rotation.md#rotating-the-signal-backplanes-connection-string)
describes. That procedure keeps the old credential accepted at the server while the replicas restart, and a server
started with one `--requirepass` accepts exactly one. So the order here is: write both keys, roll the backplane
StatefulSet, then roll MailFathom's Deployment. Between those two rolls no signal crosses, and every client falls
back on the re-read it already does — which is why this is a few minutes to schedule rather than an outage.

**Anyone who can subscribe on that endpoint reads every signal of every user of this deployment** — account and folder
aliases, stored identities, flags, and a raised notification's two lines. No subject, address, body fragment, or
attachment name crosses it, and nothing rests there at all: pub/sub delivers to whoever is subscribed at that moment and
keeps nothing, which is why the Valkey the chart runs is given no claim and nothing that outlives its pod — its two
volumes are memory-backed scratch the server needs, and every asset switches its scheduled snapshot off rather than
leaving it at the upstream default — and why no retention, export, or erasure obligation
reaches it. What does reach it is confidentiality, so the endpoint belongs inside the same boundary as the database. An
endpoint somebody else operates owes four things, and the connection string is where the first two are written:

- **`ssl=true`**, because the hop is no longer inside one cluster's network.
- **A credential of its own**, and where the endpoint supports per-channel permissions, one limited to publishing and
  subscribing under this deployment's prefix.
- **A channel prefix no other deployment on that endpoint uses**, which is `signalBackplane.channelPrefix`. Two
  MailFathom deployments sharing one endpoint at the default prefix do not exchange a user's signals — a group is named
  from an identifier each deployment generated for itself — but they do receive each other's traffic on the backplane's
  own fixed channels, which names connections and the groups holding user identifiers.
- **A recipient entry in your own processing record**, where the endpoint is a managed service somebody else runs, since
  signals are then disclosed to that processor.

**Losing the backplane costs signals and never mail.** It is logged at `Warning` and counted, and it does not fail
readiness: a pod pulled from service over a lost optimization is a worse outage than a list a few minutes stale. The
guarantee is the client's own re-read, which happens after every reconnect and every five minutes while its window is
visible, so the worst a lost signal costs somebody watching the screen is that interval.
[The signal channel](client-endpoint.md#the-signal-channel) is what a client does about it, and
[`SignalBackplane`](configuration-endpoints.md#signalbackplane) is every key the application reads.

**This is what a client's live updates need above one replica, and signing in is no longer beside it.** A signed-in
session lives in PostgreSQL, so every replica accepts one any other replica minted and honours its revocation, and the
session routes need no affinity at any replica count. A Discover run is still the replica's own, so a client reattaching
to one across replicas is answered as though it had ended; that is tracked separately and the chart says nothing about
it either.
[Sessions live in PostgreSQL](client-endpoint.md#the-session-token-routes) is where the session half is stated, and
there is nothing in it for an operator to configure.

### Replicating the backplane

One value, off, and the rendering an unchanged `values.yaml` gives is the one instance described above:

```yaml
signalBackplane:
  enabled: true
  valkey:
    replication:
      enabled: true
      replicas: 1
```

**Read what this is worth before turning it on.** [ADR 0032](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md)
makes the client's own re-read the guarantee a signal is not, so losing the backplane is already a delay rather than a
loss. **Replication shortens the part of that delay you spend bringing a second instance up. It does not make a signal
reliable**, and nothing here changes what a signal is: pub/sub delivers to whoever is subscribed at that moment and
keeps nothing, so a statement raised while the primary is being replaced is gone in every arrangement on this page.

**What it adds is standbys, and nothing that promotes one.** The StatefulSet grows to `replicas + 1` instances, ordinal
0 is the one the others follow, and the ClusterIP Service keeps naming ordinal 0. A RESP server carries a publication
from the primary down to its replicas but never back up, so a `PUBLISH` issued against a replica reaches neither the
primary nor the other replicas — and since each MailFathom replica publishes and subscribes on the one connection it
holds, a Service spreading those connections across the set would strand what some of them published.

**A promotion here is yours to make and yours to undo.** Losing ordinal 0 is a failure; recovering from it is the
operator's act: `REPLICAOF NO ONE` against a standby, and that Service's selector repointed at the pod it was run on. **Neither half survives the chart.**
The selector is rendered from the templates, so the next `helm upgrade` — an image bump, a values change, anything —
puts it back on ordinal 0, and the role is in memory only, so a reschedule of the promoted pod re-applies the
`--replicaof` its template carries and demotes it while the Service may still name it. Both failures look exactly like
the ordinary lost-signal case this page describes above, and nothing counts them separately. So treat a promotion as a
state to leave: return the set to its rendered shape — ordinal 0 the primary, the selector on ordinal 0 — as the step
that ends it, and put a returned pod 0 back as a replica of whoever holds the role before letting it serve, because two
primaries behind one name is a split fan-out rather than a promotion.

**The connection string does not change shape**, because the Service names an instance either way:

```bash
kubectl --namespace mailfathom create secret generic mailfathom-secrets \
  --from-literal=mailfathom-signal-backplane-password='…' \
  --from-literal=mailfathom-signal-backplane-connection-string='mailfathom-valkey:6379,password=…' \
  … every other key this deployment reads
```

**Sentinel is not offered, and the reason is the client rather than the server.** Valkey ships `valkey-sentinel` in the
same binary and it does promote a standby correctly; what cannot reach it is MailFathom. StackExchange.Redis — the RESP
client `Microsoft.AspNetCore.SignalR.StackExchangeRedis` resolves, and its current release as well — decides an endpoint
is a Sentinel by reading a `redis_mode` line out of its `INFO` reply, and Valkey writes `server_mode` there instead. A
connection string carrying `serviceName=` against a Valkey Sentinel therefore fails at connect with *The
ConnectionMultiplexer is not a Sentinel connection. Detected as: Standalone*, before any signal is published. Issue 1917
measured that against three Sentinels watching a replicated pair, on the image this chart pins, with the current
client release as well as the one the backplane package resolves. Issue 1924 records what would turn it on. Until then,
a deployment that wants automatic promotion points `valkey.deploy: false` at an endpoint it operates itself, where the
failover arrangement is its own to choose.

**One password covers all of it.** The instances use it both to authenticate a client and to authenticate the
replication link, so the single key `signalBackplane.valkey.passwordSecretKey` names is what the whole set runs on.
Rotating it is the short outage described above.

**Each instance is reachable at a name of its own**, `<release>-mailfathom-valkey-<ordinal>.<release>-mailfathom-valkey-peers`,
from the headless Service the chart renders. That is what a replica follows the primary by, rather than a pod address a
rescheduling changes — and it is the address to point a
[backplane scrape](telemetry.md#what-the-server-itself-reports-and-how-to-collect-it) at when you want an instance
rather than whichever one the Service names.

**Nothing here is persisted.** `--save ""` is passed explicitly and appending is off, so an instance writes nothing and
a restart brings nothing back; the two volumes each pod carries are memory-backed scratch the server needs.

**What this does not give you.** Valkey Cluster is not offered and is not on the roadmap here: the backplane holds
subscriptions rather than a keyspace, so there is nothing to shard. Neither PostgreSQL nor the object store the chart
runs is made highly available by any of this; both stay one instance under the posture their own sections state.

## Security defaults

The defaults satisfy the **Restricted** Pod Security Standard, and the schema keeps the load-bearing ones from being
switched off by accident: `runAsNonRoot` must be `true`, `readOnlyRootFilesystem` must be `true`,
`allowPrivilegeEscalation` must be `false`, `capabilities.drop` must contain `ALL`, `capabilities.add` must be empty,
and `seccompProfile.type` must be `RuntimeDefault`. The pod runs as UID 1654 and mounts an in-memory `emptyDir` at
`/tmp`, which is the only path the runtime writes to.

`automountServiceAccountToken` is `false`. MailFathom calls no Kubernetes API, so a projected token would be a credential
with nothing to authenticate to and one more thing to steal.

## Probes

| Probe | Path | Consults |
| --- | --- | --- |
| Startup | `/started` | The host's own startup gates: every secret reference resolved, the database schema verified, and — only with `spamScanning.enabled` — the daemon naming the corpus it scores under. Its budget is what a slow first start is allowed, and it holds liveness off until it succeeds. |
| Readiness | `/health` | The dependencies a request needs: the database, and — only with `personalDataScanning.enabled` — the analyzer answering for every configured category. A pod that cannot serve leaves the Service's endpoints. |
| Liveness | `/alive` | The process alone, so a database outage never becomes a restart loop that cannot fix it. |

All three are served on a container port of their own — `probes.port`, `8081` by default — which sets both the port the
kubelet dials and the `HealthEndpoints:Port` the host binds, so the two cannot drift. The Service publishes 8080 alone,
so nothing outside the node reaches the probe listener: the probes answer without a credential, and which network their
port is on is what controls who may ask them. Setting `probes.port` to 8080 is refused, and so is a probe pointed at
another endpoint's path — pointing liveness at `/health` is exactly the mistake that turns an outage into a crash loop,
and pointing startup at `/alive` ends the startup grace period while the pod is still coming up.

[The health endpoints](health-endpoints.md) states what each probe consults and how a deployment turns the surface off
or serves it over TLS.

## Configuration reload

A ConfigMap edit reaches the running process for the settings MailFathom classifies reloadable, and
`DOTNET_USE_POLLING_FILE_WATCHER` is set by default because `FileSystemWatcher` does not observe the symbolic-link swap
the kubelet performs. Two things are restart-required and no setting changes that: **adding or removing a ConfigMap
key**, and a `subPath` mount, which never updates at all. The chart mounts the whole volume and never uses `subPath`.
[Configuration sources](configuration-sources.md#reload) states the full behavior and the kubelet's own delay.

The Deployment carries a checksum of the rendered ConfigMap, so a `helm upgrade` that changes configuration restarts
the pods — which is what makes an added or removed key take effect.

## Personal-data scanning

`personalDataScanning.enabled` is off, and off means the chart renders nothing for it: no Deployment, no Service, and no
configuration key in the application's environment. An opt-in nobody took pulls no image and holds no memory. [The
personal-data scanner](../features/sensitive-content-scanning.md#the-personal-data-scanner) records what the feature
hides and what each category costs retrieval.

The block follows `database`'s shape, because it is the same decision: one value decides whether the chart runs the
dependency, and the address is either derived from the release or stated, never both.

```yaml
personalDataScanning:
  enabled: true
  # analyzer.deploy defaults to true: the chart runs the analyzer and points MailFathom at its own Service.
```

That renders a single-replica Deployment and a ClusterIP Service, and writes `SensitiveContent__Pii__Enabled`, the
derived endpoint, the languages, and the confidence floor into the application's environment — one decision in one place
rather than a value here and a configuration file that could disagree. The schema refuses every one of those settings in
`config.extraEnvironment` for that reason: an address stated there would send mail content somewhere else while the pod
the release installed sat idle.

To use an analyzer you already operate:

```yaml
personalDataScanning:
  enabled: true
  analyzer:
    deploy: false
    endpoint: http://presidio-analyzer.privacy.svc.cluster.local:3000
```

The chart refuses `deploy: true` together with an `endpoint`, `deploy: false` without one, an endpoint that is not an
absolute `http` or `https` address, and an endpoint set while the scanner is off — each with a message naming what to do.
Keep the address **inside the cluster**: the point of scanning is that content is inspected before it leaves the trust
boundary, and the feature page states what pointing it outside gives up.

The analyzer's Service is ClusterIP with no value to change it, no ingress rule is rendered for it, and its pod mounts no
service-account token. It is the pod in the release that reads mail content in the clear.

**The languages are a property of the image, not of the chart.** `personalDataScanning.analyzer.languages` lists the
codes every scan is made in — the chart writes one indexed environment entry per code, numbered from zero — and the
pinned image is built for English alone, one model and a recognizer registry declaring English. Naming another code
leaves the pod unready rather than scanning in that language, and the readiness log names the code that answered
nothing. The analyzer Deployment mounts no configuration and takes no analyzer environment of its own, deliberately: a
value per Presidio setting would be this chart publishing a partial copy of a third party's configuration schema. A
second language is therefore an image of your own in `analyzer.image`, or `analyzer.deploy: false` and an analyzer you
operate — [the analyzer's languages](personal-data-analyzer-languages.md) records what building one takes and which
identifiers each language reaches.

The schema accepts one to eight codes of two lowercase letters each and no repeats. Each is one more analyzer request
inside a scan's single `SensitiveContent:ScanTimeout` budget, which is what the ceiling is about; the readiness probe
judges a switched-on category across the whole set, so adding a language never turns a ready pod unready.

**Resources and readiness.** The analyzer requests a gigabyte of memory and is limited to two, because it loads a language
model before it serves anything and holds it for the life of the pod; below roughly a gigabyte it is killed while loading.
Its startup probe allows five minutes of that. MailFathom itself comes up regardless and reports **unready** until the
analyzer answers, so on a first install the application pod is started, stays out of the Service, and joins it when the
analyzer is ready — no restart, and no ordering for the operator to arrange. `resources`, `nodeSelector`, `tolerations`,
`affinity`, and both security contexts are values under `personalDataScanning.analyzer`.

## Spam scanning

`spamScanning.enabled` is off, and off means the chart renders nothing for it: no Deployment, no Service, and no
`SpamClassification__*` key in the application's environment. It follows `personalDataScanning`'s shape exactly, because
it is the same decision — one value decides whether the chart runs the dependency, and the address is either derived
from the release or stated, never both. [Spam classification](../features/spam-classification.md) records what a
classification holds and what the scanner adds to it.

```yaml
spamScanning:
  enabled: true
  # scanner.deploy defaults to true: the chart runs Apache SpamAssassin and points MailFathom at its own Service.
```

That renders a single-replica Deployment and a ClusterIP Service, and writes `SpamClassification__Enabled`,
`SpamClassification__UseScanner`, the derived host and port, and the three bounds into the application's environment.
To use a daemon you already operate:

```yaml
spamScanning:
  enabled: true
  scanner:
    deploy: false
    host: spamassassin.mailfathom.svc.cluster.local
```

The chart refuses `deploy: true` together with a `host`, `deploy: false` without one, and a host set while spam scanning
is off. Keep the address **inside the cluster**: the daemon is sent whole messages unredacted, and the feature page
states what pointing it outside gives up.

The scanner's Service is ClusterIP with no ingress rule and its pod mounts no service-account token. It is the second
pod in the release that reads mail content in the clear.

> [!IMPORTANT]
> **The scanner pod needs a `baseline` namespace.** It starts as root to bind its port and drops to an unprivileged
> account for every scan, which is what parses the mail, so it needs `SETUID` and `SETGID` back after dropping all
> capabilities and cannot run under `restricted` Pod Security Standards. MailFathom's own pod is unaffected and stays
> `restricted`-compatible; if your namespace enforces `restricted`, the scanner belongs in a namespace of its own with
> `deploy: false` pointing at it.

**Rule updates and DNS.** `DNS_CHECKS` is off, so the daemon runs local rules and sends nothing derived from the
user's mail to a third-party blocklist. Whether it can fetch rule updates is your cluster's egress policy rather than a
chart value; a corpus frozen at the image's build scores today's mail worse than a fresh one, and the feature page states
that trade.

**Resources and readiness.** The daemon compiles its rule corpus before it listens, so its startup probe allows time for
that and MailFathom's own startup gate refuses to come up while the daemon is not answering — on a first install the
application pod may restart a few times before the scanner is ready. `resources`, `nodeSelector`, `tolerations`,
`affinity`, both security contexts, and the digest-pinned image are values under `spamScanning.scanner`.

## Storing message content in a bucket

The raw MIME of every message lives in PostgreSQL beside the metadata unless `contentStorage.backend` says otherwise.
Setting it to `objectStorage` writes new payloads into an S3-compatible bucket instead; the metadata, the indexes, the
embeddings, and every job still run through PostgreSQL, so this is a decision about payload bytes and about nothing
else. The endpoint is one you operate or rent, or — since the chart can run one beside MailFathom — the one
[the next section](#running-the-object-store-beside-mailfathom) installs. Either way the bucket exists before
MailFathom writes to it: nothing in the chart creates one.

Off is the default, and the chart writes nothing at all on it — a rendering that sets none of this is byte for byte a
rendering from a chart that never carried the block, which `deploy/helm/mailfathom/ci/golden/` records.

```yaml
contentStorage:
  backend: objectStorage
  objectStorage:
    endpoint: https://objects.example.test
    bucket: mailfathom-content
    keyPrefix: production/
    region: eu-central-1
    accessKeyIdSecretKey: mailfathom-object-storage-access-key-id
    secretAccessKeySecretKey: mailfathom-object-storage-secret-access-key
```

**The credential is two keys inside `secrets.existingSecret`, never a value here.** The chart templates no credential
and creates no Secret, so what those two settings name is a file in the Secret the pod already mounts, and MailFathom
reads each as `file:<secrets.mountPath>/<key>` before every request — which is what lets a key rotated behind an
unchanged mount reach the next call with no restart to schedule. The access key identifier is a secret like the secret
beside it: it names an identity at the endpoint, and it is one half of what an attacker needs.

`helm install` refuses the object backend without an address, a bucket, and both credential keys, and refuses an
endpoint that is not `https` — a request carries a signature and, on a write, the message itself. It refuses an endpoint
or a bucket named while the backend is still `database` as well, because nothing would read it and the values file would
read as though mail were going somewhere it is not. An endpoint whose certificate the cluster's own trust store does not
answer for takes `trustAnchorSecretKey`, a third key in the same Secret; there is no setting anywhere that turns
validation off instead, which [platform TLS policy](platform-tls-policy.md) covers.

`keyPrefix` is what makes a bucket MailFathom shares with something else safe, and nothing in the chart or the
application can check that two deployments sharing one arranged disjoint prefixes. The reclamation sweep lists beneath
that prefix and nowhere else. The timeouts, the sweep itself, and the bounds on moving what is already stored are
tuning rather than deployment shape and belong in
`config.files`; [`ContentStorage`](configuration-runtime.md#contentstorage) holds every key, and
[where a payload is kept](../features/email-content.md#where-a-payload-is-kept) states what a content row records.

**Switching is a move, not a setting.** The value decides only where the next write goes: every content row names the
store holding its own payload, so turning the object backend on moves nothing already stored and turning it back off
re-encodes nothing. Carrying what a mailbox has accumulated into the bucket is an operator's act with its own controls
and its own bounds — [moving stored content into the bucket](moving-stored-content.md) is that operation. Until it has
run, and after a partial run, the deployment reads from both stores and keeps needing both: a bucket this release no
longer names leaves the pod unready rather than the mail unreadable, which is the honest outcome and what
[health endpoints](health-endpoints.md) reports.

### Running the object store beside MailFathom

An operator who wants payload bytes out of PostgreSQL and does not already run object storage has nothing to point the
setting above at. `contentStorage.objectStorage.deploy.enabled` is that answer: the chart installs one
[Silo](https://github.com/pgsty/silo) node on a retained claim, with a Service of its own, and derives the endpoint from
it. Silo is PGSTY's maintained fork of the open-source MinIO server, keeping one release line alive after upstream ended
community distribution — the same image, at the same pin, that the Compose deployment, the Quadlet units, and the
[integration suite](local-development.md#the-object-storage-endpoint) use, so what runs here is the server the S3
adapter was verified against.

**One node, one volume, and that is the whole of it.** No erasure coding, no second node, no replication, no failover.
What it protects against is a pod being replaced; a disk that fails takes the payloads with it, which is what makes
[the backup order below](#what-you-now-back-up-and-in-which-order) the thing standing between this and losing them. Once
durability, replication, or a growth path past one volume is somebody's job, this is the arrangement to leave behind —
exactly as `database.deploy.enabled` is for PostgreSQL.

```yaml
contentStorage:
  backend: objectStorage
  objectStorage:
    bucket: mailfathom-content
    trustAnchorSecretKey: mailfathom-object-storage-trust-anchor
    deploy:
      enabled: true
      tls:
        existingSecret: mailfathom-silo-tls
      rootCredentialSecret: mailfathom-silo-root
      persistence:
        size: 200Gi
```

`endpoint` is left unset and refused if it is not: the chart derives `https://<release>-silo:<service.port>` from the
Service it installs, for the reason `database.host` is derived from the release name. `usePathStyleAddressing` has to
stay on, because virtual-hosted addressing puts the bucket in the host name and a Service has neither a wildcard DNS
record nor a certificate that would match one.

**It answers over TLS, and there is no way to run it otherwise.** MailFathom refuses a plain `http` endpoint — a request
carries a signature and, on a write, the message itself — and it validates what it is presented, with no setting
anywhere that turns that off. So the store terminates TLS with a certificate you supply, covering the name the endpoint
is derived from:

| Name | Reached from |
| --- | --- |
| `<release>-silo` | The release's own namespace, which is where MailFathom is |
| `<release>-silo.<namespace>` and `<release>-silo.<namespace>.svc` | Anywhere else in the cluster |

No public authority issues a certificate for a cluster-internal name, so this one is signed by an authority of your own
— cert-manager with a private issuer is the ordinary arrangement, and a `Certificate` naming those three
`dnsNames` produces a `kubernetes.io/tls` Secret the chart mounts with no key names changed. That same authority is what
`trustAnchorSecretKey` names, as a key inside `secrets.existingSecret`, which is why the chart requires it here and
leaves it optional for a hosted endpoint. The chart issues, templates, and stores no certificate material of its own.

**The store's root credential lives in a Secret of its own, and MailFathom never holds it** — the line the PostgreSQL
superuser password is on the other side of, for the same reason. `secrets.existingSecret` is mounted whole into the
application pod, because the keys MailFathom reads are named by your configuration rather than by the chart, so a
credential placed there is readable by the process that parses untrusted mail. The root credential creates buckets,
issues access keys, and reads every object; what MailFathom presents is an access key scoped to the one bucket. The
chart refuses a values document naming one Secret for both.

```bash
# The store's own, mounted by the store alone. The secret half must be at least eight characters, which is the
# server's own rule.
kubectl --namespace mailfathom create secret generic mailfathom-silo-root \
  --from-literal=silo-root-access-key-id="$(openssl rand -hex 12)" \
  --from-literal=silo-root-secret-access-key="$(openssl rand -base64 32)"
```

**Neither the bucket nor that scoped access key is created by the chart**, so both are provisioned once after the store
becomes ready, with the management client the image already carries. Until they exist MailFathom reports itself unready
rather than storing mail: its startup probe writes and deletes one object of its own, so read permission alone is not
enough to become ready.

```bash
access_key_id="$(openssl rand -hex 12)"
secret_access_key="$(openssl rand -base64 32)"

kubectl --namespace mailfathom exec -i statefulset/mailfathom-silo -- sh -s "$access_key_id" "$secret_access_key" <<'PROVISION'
set -eu

# --insecure throughout: the call is to this pod's own loopback address, which the certificate names nowhere. What it
# names is the Service, and that is the name MailFathom validates.
mcli --insecure alias set store https://127.0.0.1:9000 \
  "$(cat /etc/silo/credentials/silo-root-access-key-id)" \
  "$(cat /etc/silo/credentials/silo-root-secret-access-key)"

mcli --insecure mb --ignore-existing store/mailfathom-content

# The four operations the adapter performs and nothing else: it lists beneath its prefix to reclaim released objects,
# and it gets, puts, and deletes one object at a time. It never creates a bucket and never touches another one.
cat > /tmp/mailfathom-content.json <<'POLICY'
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": [ "s3:ListBucket" ], "Resource": [ "arn:aws:s3:::mailfathom-content" ] },
    { "Effect": "Allow", "Action": [ "s3:GetObject", "s3:PutObject", "s3:DeleteObject" ], "Resource": [ "arn:aws:s3:::mailfathom-content/*" ] }
  ]
}
POLICY

mcli --insecure admin policy create store mailfathom-content /tmp/mailfathom-content.json
mcli --insecure admin user add store "$1" "$2"
mcli --insecure admin policy attach store mailfathom-content --user "$1"
PROVISION

kubectl --namespace mailfathom patch secret mailfathom-secrets --type merge --patch "$(
  printf '{"stringData":{"mailfathom-object-storage-access-key-id":"%s","mailfathom-object-storage-secret-access-key":"%s"}}' \
    "$access_key_id" "$secret_access_key"
)"
```

Both halves reach MailFathom as files under the mounted Secret and are read before every request, so rotating the key
in the store and replacing the two values takes effect on the next call with no restart to schedule.

**The console is not served.** It is a management interface over every object the store holds — bucket contents, access
keys, the server's own configuration — so it is a second surface onto the mail rather than a convenience, and by default
the server never starts that listener and no Service port routes to one.
`contentStorage.objectStorage.deploy.console.enabled` starts it and adds a port to the same ClusterIP Service. It is
still not published: the chart renders no ingress for it, so reaching it is a deliberate
`kubectl port-forward service/<release>-silo 9001:9001`, and anything you put in front of it instead is yours to
authenticate. What it authenticates with is the root credential above, which is the whole server rather than the one
bucket.

**The pod meets the restricted Pod Security Standard**, unlike the spam scanner: `runAsNonRoot`, `readOnlyRootFilesystem`,
`allowPrivilegeEscalation: false`, `capabilities.drop: [ALL]` with nothing added, and `seccompProfile: RuntimeDefault`,
with the schema keeping the load-bearing ones from being switched off. The image declares no account of its own, so the
chart states one — uid and gid `1000`, with `fsGroup` making the claim writable by it. No service-account token is
mounted; the store calls no Kubernetes API. Its only writable paths are the claim and an `emptyDir` at `/tmp`, which is
where the image points `HOME`.

**Nothing of Silo is in this chart, in any MailFathom image, or in this repository.** The chart names an image your
cluster pulls from PGSTY's own registry. Silo is AGPL-3.0-or-later, which
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) records
together with the reading it is used under. Its own lifecycle — upgrades, its configuration, its users beyond the one
above — is yours; MailFathom manages none of it and the chart runs no job against it.

### What you now back up, and in which order

**A `pg_dump` stops being a complete backup the moment the object backend is on.** The rows point at objects in the
bucket by a locator nothing recomputes, so the database and the bucket are one backup taken in two places, and a restore
brings back both.

1. **Back up the database**, exactly as before.
2. **Back up the bucket** — everything beneath `keyPrefix`, or the whole bucket where MailFathom has it to itself. Your
   provider's own tooling does this; nothing in MailFathom or the chart takes a bucket backup.
3. **Restore the database first, then the bucket.** In that order the window between them is a database pointing at
   objects not back yet, which reads as content temporarily unavailable. The other order leaves objects nothing points
   at, which the reclamation sweep is entitled to delete once they are older than `MinimumObjectAge` — so restoring the
   bucket first can destroy part of what you are restoring.

A store the chart runs is no different in that order, and the second step is a copy out of it rather than a provider's
console. It is the S3 API that answers, so the same client does both directions, and copying the claim's files
underneath a running server is not a backup — the pool has its own metadata in `.minio.sys` and a copy taken while the
server is writing is a copy of neither state:

```bash
kubectl --namespace mailfathom port-forward service/mailfathom-silo 9000:9000 &

# The scoped key, read back out of the Secret the provisioning step patched rather than from shell variables, which
# belonged to that session and are gone by the time a backup runs.
access_key_id="$(kubectl --namespace mailfathom get secret mailfathom-secrets \
  --output jsonpath='{.data.mailfathom-object-storage-access-key-id}' | base64 --decode)"
secret_access_key="$(kubectl --namespace mailfathom get secret mailfathom-secrets \
  --output jsonpath='{.data.mailfathom-object-storage-secret-access-key}' | base64 --decode)"

mc --insecure alias set backup https://127.0.0.1:9000 "$access_key_id" "$secret_access_key"
mc --insecure mirror --remove backup/mailfathom-content ./mailfathom-content-backup   # out
mc --insecure mirror ./mailfathom-content-backup backup/mailfathom-content            # back in
```

Any S3 client does this — `mc`, `rclone`, `aws s3` — and it runs on the machine the backup lands on rather than inside
the cluster, which is what the forwarded port is for. `--insecure` for the reason the provisioning commands need it: the
certificate names the Service, and a forwarded port answers on `127.0.0.1`. The scoped access key is enough for both
directions, which is why the root credential does not appear here. `--remove` on the way out makes the copy match the
bucket rather than accumulate objects the sweep has already reclaimed; deliberately not on the way back in, where it
would delete from the store instead.

Take both from the same moment where you can. A row whose object is missing is not a corrupt deployment and not a
mystery: the read reports that message's content as unavailable and names it, every other message keeps working, and
[when the local copy is unusable](../features/email-content.md#when-the-local-copy-is-unusable) states what a client
sees. A message whose object is missing is re-fetchable from the mail server like any other; what is not is anything
derived — the answering audit trail and the embeddings.

## Scheduling, resources, and placement

`nodeSelector`, `tolerations`, `affinity`, `topologySpreadConstraints`, `priorityClassName`, `resources`,
`podAnnotations`, `podLabels`, `service.type`, `service.annotations`, and `terminationGracePeriodSeconds` are all
values. Nothing requires editing a template. `strategy` and `podDisruptionBudget` are values as well, and
[what the chart renders for the rollout and the drain](#what-the-chart-renders-for-the-rollout-and-the-drain) is what
each of them decides.

`terminationGracePeriodSeconds` defaults to 60 against a 10-second `MailSynchronization:ShutdownDrainTimeout`. Raise
them together: a grace period shorter than the drain kills the process with the drain still running.

## Upgrading, rolling back, and uninstalling

Back up the database — and the bucket beside it where `contentStorage.backend` is `objectStorage`, for the reason
[what you now back up](#what-you-now-back-up-and-in-which-order) gives — and apply the new release's
`mailfathom-schema-<version>.sql` **before** the upgrade. The new pod
refuses to start against a schema that is behind it, and the old pod keeps serving against a schema that is ahead — so
that order is the one with no window in which nothing serves.

```bash
helm upgrade mailfathom deploy/helm/mailfathom --namespace mailfathom --values values.yaml
helm rollback mailfathom <revision> --namespace mailfathom
helm uninstall mailfathom --namespace mailfathom
```

`helm rollback` returns the workload to a previous image. **It does not return the schema.** A migration only moves
forward, so returning to the earlier schema means restoring the database from the backup taken before the migration;
[rolling back](database-schema.md#rolling-back) states when that is necessary and when rolling only the image back is
enough.

Uninstalling removes every object the chart owns. It removes **no** data: `helm uninstall` deletes no StatefulSet claim,
so a deployed database's volume and a deployed object store's volume both survive it, and every Secret was created
outside the release and stays. Deleting either store is a deliberate `kubectl delete pvc` afterwards — and deleting only
one of them leaves the other holding half of what a message is.

### Upgrading the object store beneath a running deployment

A store the chart runs is a dependency the deployment operates, so its version moves when you move it and never on its
own: the image is pinned to an exact tag, the pod carries no update mechanism, and nothing here follows a moving one.
Moving it is one values change and one rollout, and it is deliberately not part of a MailFathom upgrade — the two have
no version relationship, and doing both at once makes a failure ambiguous.

```yaml
contentStorage:
  objectStorage:
    deploy:
      image:
        tag: RELEASE.<newer>
```

Three things are worth knowing before the rollout. **It is a StatefulSet with one replica on a ReadWriteOnce claim**, so
the old pod is terminated before the new one starts: the store is unreachable for the length of that, and MailFathom
reports itself unready and stores nothing new for the same window rather than failing a read of what is already in
PostgreSQL. **Back the bucket up first**, for the reason any upgrade of a store holding data is preceded by a backup —
the on-disk format is the server's, and a version that will not start against it leaves the payloads reachable only by
going back. And **going back is a values change and a rollback of the same shape**, which works because nothing in the
release ties the store's version to MailFathom's; what would not work is going back after a newer server has rewritten
the pool's own metadata, which is what the backup covers.

Read the upstream release notes between the two tags before moving one. Silo's release line is a MinIO one, named by
timestamp rather than by version, and what a given release changes is stated there and nowhere here.

### Chart version and application version

**They are one number.** `Chart.yaml` carries `version: 0.0.0` as a placeholder and no `appVersion` at all, and the
release run supplies both from the `VersionPrefix` in `Version.props` that is the only file in the repository
carrying an application version:

```bash
version="$(bash scripts/read-declared-version.sh)"
helm package deploy/helm/mailfathom --version "$version" --app-version "$version"
```

Omitting `--version` packages the placeholder. [Where the version is
observable](release-procedure.md#where-the-version-is-observable) is where that rule and its reasoning live.

A **packaged** chart therefore always states the application version it deploys, and refuses an install whose
`image.tag` disagrees with it unless `image.allowVersionMismatch` says the combination is deliberate. Two cases carry
nothing to compare and are not refusals: a deployment naming the image by `image.digest`, which publishes no version,
and the unpackaged chart directory, which states none because it is not a release of anything.

### Nightly builds

`image.channel: nightly` deploys unsupported development output. It requires
`image.nightlyAcknowledgement: i-understand-this-is-unsupported`, requires an `image.tag` that carries a `-nightly.`
identifier and rejects one on the release channel — the channel is decided by what the reference calls itself, not by
the registry it came from, because both registries carry both channels —
labels every rendered object `io.mailfathom/release-channel: nightly` — the same value the image carries as
`io.mailfathom.release-channel`, spelled the way a Kubernetes label prefix has to be — prints a warning in the chart's
notes, and labels the workload with the nightly identifier rather than with `appVersion` — so a nightly is never
indistinguishable from a release in a query that reads that label.

[What a nightly build risks](container-image.md#what-a-nightly-build-risks) states what the acknowledgement is
acknowledging: a schema that can be ahead of any published migration, no upgrade path in either direction, four public
surfaces that move without notice, and a tag that is deleted once thirty newer nightlies exist. Name the exact
`-nightly.<n>-<short revision>` identifier or a digest rather than the moving `nightly` tag. The package is public, so
the cluster needs no pull secret to reach it; `image.pullSecrets` stays in the chart for a mirror or a private registry
an operator pulls through instead.

**The nightly channel has no chart of its own.** Install the most recent released chart and point it at the nightly
image, which is what the values above are for; publishing a chart per nightly would fill the chart's version list with
references deleted a month later.

## Verification

Reading the chart needs only Helm, and one script is what a change here is reviewed with:

```bash
scripts/render-helm-manifests.sh            # lint, render, and compare against the committed manifests
scripts/render-helm-manifests.sh --update   # take an intended change into them
```

It runs `helm lint --strict` and `helm template` against every values document under `deploy/helm/mailfathom/ci/`, and
holds each rendering against the manifest committed beside it under `ci/golden/`. Those values documents are excluded
from the packaged chart and name no real image and no real database; the manifests they render are excluded with them,
and exist so that a change in what the chart produces appears in a diff rather than only as a failure to produce
anything. A rendering is normalized before it is compared — trailing whitespace and the blank lines Helm leaves between
documents go — so the Helm version a machine happens to carry does not decide the verdict.

Eight values documents are what the chart is held against, and each renders a shape the others do not.
`release-values.yaml` names an external database, an external analyzer, and an external spam scanner, and turns the
ingress on. `nightly-values.yaml` selects the unsupported channel with its acknowledgement and renders the analyzer and
the scanner the chart deploys itself. `content-storage-values.yaml` selects the object backend against an endpoint
somebody else operates, and `object-store-values.yaml` selects it against the store the chart runs itself — between them
both branches of `contentStorage.objectStorage.deploy.enabled`, with the console off in the second, which is what makes
the committed manifest the record that no console listener is produced by default.
Three documents do the same for the backplane, all at two replicas with the client surface served:
`signal-backplane-valkey-values.yaml` renders the one instance the chart runs by default,
`signal-backplane-external-values.yaml` renders no workload at all while configuring the application identically, and
`signal-backplane-replication-values.yaml` renders the set with two standbys beside that instance — so the difference
between the first and the last is exactly what the one availability value costs. Two documents under `ci/refusals/`
cover the two ways that value can be asked for with nothing to apply it to. `defaults-values.yaml` is
`values.yaml` plus only what the chart refuses to default — an image reference, the Secret the pod mounts, and the
Secret holding the database superuser password, which the chart requires whenever it deploys the database itself and so
by default — meaning it renders the shape an operator following the quick start gets. That last one is also what keeps
the chart's own defaults inside schema validation: Helm validates each values document coalesced with `values.yaml`
against `values.schema.json` during both the lint and the render, and a default the schema would reject is overridden by
the others.

Some values documents are supposed to be refused rather than rendered, and a rendering cannot record that: a
combination the chart accepts by accident produces a plausible manifest and no golden file shows anything. Those live
under `ci/refusals/`, each carrying on a `# refuses:` line the wording its refusal has to contain, and the same script
requires the chart to refuse each one and to name the setting while doing so. Five are there today. Three of them are
one refusal reached three ways — more than one replica serving the page, more than one serving the client surface from
a configuration file, and more than one serving it from the environment block — because what the chart reads to decide
that is three different values, and a refusal walkable around by configuring the same thing another way is not one. The
other two are replication asked for with nothing to apply it to: over an endpoint the chart does not run, which would
render exactly what the external document renders, and with the backplane off altogether, which would render exactly
what the defaults render. Each leaves an operator having written down an arrangement the deployment does not have.

The `Helm chart` job of `CI` runs the same script on every pull request that touches `deploy/helm/`, which is where a
chart that stopped rendering is now found. The release run lints and renders again before it publishes anything, so a
chart that does not lint or render is never published; it additionally renders the packaged chart against the digest
the release published and refuses one that would deploy anything else.

Installing the chart into a real cluster and asserting what only a running deployment can answer — that the pod reaches
the database through the chart's own wiring and then refuses to serve until the release's schema artifact has been
applied — is still not done anywhere. The repository runs no cluster of its own for it.

## Related

- [Applying the database schema](database-schema.md) — the release artifact, the privileges it needs, and the three
  startup failures it answers
- [The container image](container-image.md) — what is inside it, how it runs, and why it carries no schema tool
- [Docker Compose](deployment-compose.md) — the same contract in the other shape
- [Podman Quadlet](deployment-quadlet.md) — the single-machine shape that provisions secrets as systemd credentials
- [The platform TLS policy](platform-tls-policy.md) — for a mail server whose handshake the pod's own OpenSSL refuses;
  `config.extraEnvironment` names the file, and the chart currently has no hook for mounting it
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
