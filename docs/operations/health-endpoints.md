# The health endpoints and the listener they are served on

<!-- describes: backend/src/Host/Hosting/**, backend/src/Host/Configuration/Endpoints/HealthEndpointOptions.cs, backend/src/Host/Configuration/Endpoints/EndpointTransport.cs, backend/src/Host/Configuration/Endpoints/ListenerComposition.cs, backend/src/Host/Configuration/Endpoints/ServedSurfaces.cs, backend/src/Host/Security/Endpoints/Health*, backend/src/Infrastructure/SensitiveContent/PersonalData/PresidioAnalyzerProbe.cs, backend/src/Infrastructure/ObjectStorage/S3ObjectStorageEndpointProbe.cs -->

An orchestrator decides three things about a process by asking it: whether it has finished coming up, whether it can
serve a request right now, and whether it is still running rather than stuck. MailFathom answers those three questions on
three paths, served on a listener of their own. This page records what each one consults, which port it answers on, how
to turn the surface off, how to put TLS in front of it, and why it carries no credential and no rate limit.

## The probes are on their own port

```json
{
  "HealthEndpoints": {
    "Enabled": true,
    "BindAddress": "0.0.0.0",
    "Port": 8081,
    "Transport": "Http"
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Whether the probes are served at all. `false` maps no route and opens no listener |
| `BindAddress` | `0.0.0.0` | The IP address the probe listener binds. `127.0.0.1` restricts the probes to the machine, `::` binds IPv6 |
| `Port` | `8081` | The port the probes answer on. Under `HttpsOnly` this is the TLS port |
| `Transport` | `Http` | `Http`, `HttpAndHttps`, or `HttpsOnly` — see [Transports](#transports) |
| `HttpsPort` | none | The TLS port, required by `HttpAndHttps` and unused by the other two |
| `Domain` | none | The DNS domain the configured certificate covers, required by the two TLS transports |
| `ServerCertificate` | none | Where the certificate and its private key come from, on the same terms [the MCP endpoint](mcp-endpoint.md#https-and-your-own-domain) states them |

The section is bound strictly. A misspelled key fails startup instead of leaving the probes on a port an operator
believed they had moved, because a listener published to the wrong network is not a setting that failed to apply.

Every setting here is **restart-required**. The section decides which sockets are opened and which routes exist, and
both are settled while the host is composing itself, so an edited value takes effect on the next start. The material
behind a configured certificate reference is the exception the [secret machinery](secret-rotation.md) already covers:
rotating what a reference points at needs no configuration change, though this listener reads its certificate once,
while starting.

**The probe listener is never the MCP endpoint's.** A probe path asked on the port that serves `/mcp` is
answered with `404`, and `/mcp` asked on the probe port is answered with `404` as well. The decision is taken from the
port the connection arrived at — a property of the socket the operating system accepted it on, not a header the caller
wrote — so publishing the probe port to an orchestrator's network does not publish the mailbox to it, and publishing the
application port to clients does not hand them an unauthenticated dependency report. The one exception in either
direction is [the HTTP API document and the explorer](http-api-documentation.md), which belong to no surface and answer
on every bound port — and which a deployment never has, because they exist only in a `Development` process.

A probe port that collides with another surface fails startup with a message naming both, rather than failing
to bind with an address-in-use error that names a socket.

### Nothing changes with the environment

The endpoints behave identically in every environment. `ASPNETCORE_ENVIRONMENT` decides nothing here, so what a
developer tests is what runs in production, and the only difference between two deployments is what each one
configured. The service-defaults scaffold MailFathom started from served the probes in Development only, which left
every container and Kubernetes deployment with nothing to ask and every development run serving them on the listener
its MCP clients reach.

### The probe listener is one of four, and only one

Every surface this process serves binds its own listeners from its own section: the probes from `HealthEndpoints`, the
protocol surface from `McpEndpoint`, the administrative surface from `AdminEndpoint`, and the client surface from
`ClientEndpoint`. Nothing else opens a socket
here, and the host's own ways of naming one — `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`, and
`Kestrel:Endpoints` — are refused at startup rather than ignored, because Kestrel drops the first three as soon as a
listener is bound in code and binds the last beside them on a socket no section describes.

A probe port may be one another surface already binds. The socket is then bound once and serves both, and a probe path
asked on a port the probes are not on is still refused with a `404` — sharing widens what a listener answers and nothing
else. What it costs is exposure, which is the decision this section exists to give you: on a shared port the probes
answer wherever that port is published. Turning `HealthEndpoints:Enabled` off changes nothing about where anything else
is served, and turning every surface off is refused, because a process binding no listener at all would fall back to
Kestrel's own default address.

[Where each surface is served](configuration-endpoints.md#where-each-surface-is-served) records the whole arrangement.

## The three probes

| Probe | Path | Consults | A failure means |
|---|---|---|---|
| Startup | `/started` | The host's own startup gates: every secret reference the deployment's own sections carry resolved, the database schema verified, every owner the deployment serves reconciled against the rows it holds and their own mailboxes' secrets resolved with them, and — only where its own switch is on — the spam scanner naming the corpus it scores under | The process has not finished coming up; the grace period continues |
| Readiness | `/health` | The dependencies a request needs: the database, each declared AI provider, and — only where its own switch is on — the personal-data analyzer and the object-storage bucket, or, where no endpoint is configured, whether stored content points into one anyway | The instance stops receiving traffic; it is not restarted |
| Liveness | `/alive` | Process-local state only | The container is restarted |

One endpoint cannot answer all three, because the three have different consequences. Wiring liveness to the readiness
answer is the mistake that turns a database outage into a restart loop across every replica of a process that is
working correctly; the Helm chart's values schema refuses that configuration rather than discouraging it.

**Membership is decided by a health-check tag.** A check states which probes it belongs to once, where it is
registered — `live`, `ready`, `startup` — and a check that states none reaches no probe. That is deliberate: the
framework's default is to include every registered check in every endpoint, which is how a dependency check ends up
able to restart the process. Because a probe over no checks reports healthy, composition refuses to start when any of
the three would answer without consulting anything.

**A declared AI provider degrades the readiness probe rather than failing it.** An embedding provider and a chat
provider each register a check of their own — `ai-embedding-provider` and `ai-chat-provider` — when the deployment
declared one, and neither ever reports worse than degraded. Neither serves a request path: an instance whose embedding
provider is failing still answers every search lexically, and one whose chat provider is failing still answers every
search at all. So `/health` reads `Degraded` while a provider is unreachable, the instance keeps its traffic, and the
liveness probe is untouched — restarting the process could not fix a provider and would turn one outage into two.
Neither check calls a provider to find out; each reports the outcome of the last real call, which is what keeps a
health scrape from spending an operator's money. [Chat generation](../features/chat-generation.md#provider-health-is-tracked-per-provider)
records the states and what each asks of an operator.

**The personal-data analyzer is the opposite case.** With `SensitiveContent:Pii` switched on the
scanner fails closed, so an instance whose analyzer cannot answer refuses every read, derived write, and egress that
scanner guards — it is not serving a narrower service, it is serving nothing the scanner covers. So the
`personal-data-analyzer` check reports **unhealthy**, `/health` answers `503`, and the instance leaves the load balancer.
It never reaches the liveness probe: restarting this process cannot start the container beside it, and doing so would
turn one sidecar's outage into a restart loop.

Like the database check and unlike the two above it, this one reaches its dependency on each scrape. The analyzer is a
container with a lifetime of its own — it may become ready after MailFathom and may stop answering long afterwards — so a
verdict taken once would be wrong in both directions. That is also why an unreachable analyzer is not a startup failure: the host comes
up, reports unready, and becomes ready by itself when the analyzer does. The question asked is which entities the
analyzer recognises, asked once per configured language and answered from its own registry: a language that answers
nothing at all fails the check by name, and a switched-on category is judged across the languages together, so adding one
never turns a ready instance unready. The whole scrape runs under one `SensitiveContent:ScanTimeout` rather than each of
its requests under a timeout of its own, so naming more languages costs more requests and never a longer scrape — an
analyzer too slow to finish inside that budget reports the instance unready, which is what it would be anyway once a scan
missed the same budget. Each transition — into unavailability and back out of it — is written to the log,
at `Error` and `Information` respectively, because a probe response carries no reason and the log is where an operator
reads one.
[Sensitive-content scanning](../features/sensitive-content-scanning.md#the-analyzer-is-deployed-only-when-the-switch-is-on)
records what the scanner does with an analyzer that is not there.

**The object-storage bucket is the same case, for the same reason.** Where `ContentStorage:Backend` selects
`ObjectStorage`, an `object-storage` check joins the readiness probe alone and reports **unhealthy** when the bucket
does not answer. Unhealthy rather than degraded because a content backend is not a service a deployment can serve a
narrower version of: the bucket a deployment selected is where it means its message payloads to live, so an endpoint
that refuses a write is the store failing rather than one feature being unavailable. Reaching the liveness probe would
turn one endpoint's outage into a restart loop across every replica, since restarting this process cannot make a bucket
reachable.

It reaches the bucket on each scrape, and asks all three questions a payload needs answered: it lists beneath the
deployment's prefix, writes a zero-length object under a key of its own, and removes what it wrote. All three because
each fails on its own — an endpoint that answers a listing and refuses a write is a deployment that will accept mail and
be unable to store it — and they stop at the first failure, because reporting three would tell an operator that three
things are wrong. **The probe stores no mail to find out**: nothing about a message, an account, or a folder takes part
in it. A bucket that has been taken away is a readiness condition rather than a configuration error a binder could
catch, which is what
[ADR 0017](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md)
§ 1 decided; the host comes up, reports unready, and becomes ready by itself once the bucket answers. Each transition is
written to the log at `Error` and `Information` as the analyzer's are, with the classified failure behind it, because
the response carries no reason and the message names the configuration key rather than the address.

**The opposite case has a check of its own, and it is on where that one is off.** A deployment that selected the object
backend, stored messages through it, and then lost the configuration keeps every one of those rows intact and
unreadable — and nothing else notices, because the mailbox, the timeline, and the metadata all answer from the database.
So where `ContentStorage` names no endpoint at all, an `object-backed-content` check joins the readiness probe instead,
asks the four content tables on each scrape whether any row names the object backend, and reports **unhealthy** when one
does. The two are never both registered: where an endpoint is named, the check above already asks the sharper question,
about the endpoint rather than about the configuration.

It is a readiness condition rather than a startup gate for the reason the bucket is one, and for a second: the question
is asked of the database, so a host that answered it while composing would refuse to start against a database no
migration had reached yet. A read that fails is reported as this check failing rather than answered as an absence,
because a database nothing can be read from is not a deployment holding no object-backed content. The remedy is a
configuration key rather than anything that can be done to the process, which is what the `Error` record says: restore
the `ContentStorage:ObjectStorage` block the content was stored through, and the instance becomes ready by itself. **It
reads no mail to find out** — the query asks which backend a row names and nothing about the message, the account, or
the folder.

**The owner gate refuses to start rather than reporting unready.** A mail account says nothing about whose mail it
holds unless the owner who owns it declares it, so the gate settles the question before anything serves a request: it
reads the owners the file declares, gives each of them the `settings_accounts` row the mail graph's foreign keys hang
on, proves the secrets their own mailboxes carry, and reports which of them is read from configuration and which
from a document of their own. It runs behind the
schema gate, because that table is the schema's, and a deployment it cannot reconcile does not come up:

| What the gate found | What it means | What to do |
|---|---|---|
| Several owner records and no owner declared | The deployment has acquired owners while its mail accounts are still in `MailSynchronization:Accounts`, which says whose none of them are | Declare each owner in the top-level `Accounts` collection with the mail accounts they own, so every mailbox says whose it is |
| More owner records than a deployment serves | The roster is longer than the 256 one deployment may hold, which is a table something generated rather than provisioned | Find what wrote `settings_accounts`; nothing MailFathom ships writes a roster that long |
| A start that would leave more owner records than a deployment serves | The records held and the owners newly declared each fit within the 256, and only their sum does not — an owner the file no longer declares keeps their record | Nothing was written: remove the owner records this deployment no longer serves, then declare the new owners |
| An owner declared under an identifier the deployment does not hold them under | The identifier every mail account, stored message, and job of theirs hangs on was changed in the file | Restore the identifier the deployment holds, or give the new one a label of its own if it is meant to be a second person |
| A label declared for one owner while another still carries it | Two owners would be told apart by one label, which the unique index refuses | Relabel the owner holding it in one start and declare it for its new owner in the next; removing them from the file frees nothing, because a held owner keeps their record and their label |
| Several owners while `McpEndpoint`, `ClientEndpoint`, or `AdminEndpoint` is on | Nothing this release admits a caller with names the owner they act for, and an administrator's acts are the deployment's rather than one person's, so none of those surfaces could say whose mail or whose contacts an act is about | Serve one owner while any of those surfaces is enabled, or disable them on a deployment serving several |
| An owner's own document that will not bind | That owner is served from their document rather than from configuration, and it is not the settings a document holds | Repair the record; the refusal names each sentence of what must change |
| An owner's own mailbox whose secret reference or trust anchor cannot be resolved | An owner's mailboxes are declared outside the section the secret gate walks, so without this they would start clean and fail one connection at a time | Repair the reference or the anchor; the refusal names the owner and the `Accounts:<index>` path to it |

Every one is a refusal rather than degraded readiness, and deliberately: the alternative is a process that serves mail
while it cannot say whose mail it is serving.

**A deployment that declares no owner is the ordinary shape and is not refused.** Its mail accounts stay in
`MailSynchronization:Accounts` and belong to the one owner such a deployment holds — the row the release's schema
provisions, or one the gate records where the deployment holds none at all. An owner the deployment holds and the file
no longer declares is not refused either: they are kept, they are not served, and a warning names them, because their
mail is neither read nor refreshed while they stay that way.

The startup gates are reported rather than re-run. The probe reads a flag the gates set as they complete, so polling it
opens no connection and costs nothing, and once it turns healthy it stays healthy.

Only the secret-reference gate runs before the listener opens, because it is the one registered as a hosted *lifecycle*
service and the host runs that stage ahead of every hosted service's start. The schema gate, the owner gate, and the
spam-scanner gate are ordinary hosted services, and the web host registers its own while the builder runs, so it starts
first and the port is already accepting connections while those three run. That window is exactly what the probe is
for: hold traffic off an instance until `/started` succeeds rather than inferring readiness from the port answering.
A gate that refuses still aborts startup, so the process exits rather than serving with a gate unmet.

## What a response carries

The body is one word: `Healthy`, `Degraded`, or `Unhealthy`, with `200` for the first two and `503` for the last.

Check names, exception messages, stack traces, durations, connection strings, host names, and dependency descriptions
are all absent, and this is a security property rather than a minimalism preference. The endpoint answers without a
credential, so everything in the body is disclosed to whoever can reach the port; a check name says which dependencies
exist and a description says what is wrong with one. An orchestrator compares the one word.

Probe requests are excluded from tracing, and the host's own log configuration keeps `Microsoft.AspNetCore` at
`Warning`, so polling every few seconds for the lifetime of the process produces neither a span nor a log record per
request.

## No credential, no origin gate, no rate limit

The probe endpoints carry no authentication, no authorization, no API-key check, no CORS or `Origin` gate, and no rate
limiting. That is the stated posture, not an omission:

- An orchestrator holds no credential and has nowhere to get one. A probe that could be refused for its credential is a
  probe that reports a process as failed while it is working.
- A throttled probe fails, and a failed liveness probe restarts the container. A limiter on this listener would convert
  a burst of polling into an outage, so no endpoint's rate limits ever extend to it — the
  [MCP endpoint's](mcp-endpoint.md#rate-limiting), the [administrative endpoint's](admin-endpoint.md#rate-limiting), and
  the [client endpoint's](client-endpoint.md#what-bounds-the-traffic) are each attached to that surface's own routes
  rather than applied as the process's default policy, so a probe route carries none of them.

**One bound does reach this listener**, and it is the exception that proves how the rule above is drawn.
[`ConnectionLimits`](configuration-endpoints.md#connectionlimits) is a ceiling on connections rather than on requests,
and a connection is accepted before routing has decided which listener it was for — so unlike every limit named above,
it cannot recognize a probe and cannot be made to exempt one. The framework exposes no per-listener form of it.

What that means operationally: a flood that saturates the process-wide ceiling against `/mcp` leaves this listener
unable to accept, liveness stops answering, and the orchestrator restarts the container. Narrowing `ConnectionLimits`
brings that point closer; the ceiling is not what creates the failure, since a flood left unbounded exhausts the
process's file descriptors and produces the same outcome less predictably, but it is what decides the number at which it
happens. Size it against the connections a real client population holds open rather than against the request limits,
and publish the probe port on a network the flood cannot reach — which is the same control every other line in this
section rests on.

Exposure is controlled by which network the port is published on, and by the transport it is served under. Nothing
else on this listener depends on a credential, and the TLS listener asks for no client certificate even where the MCP
endpoint's own listeners ask every client for one.

## Transports

| `Transport` | Clear-text listener | TLS listener |
|---|---|---|
| `Http` | `Port` | none |
| `HttpAndHttps` | `Port` | `HttpsPort`, which must be stated |
| `HttpsOnly` | none | `Port` |

`Http` is the default and the whole first-release posture for most deployments: adopting the release costs no
certificate work, and a probe network that is already trusted needs none. TLS is a deliberate upgrade.

One socket serves one scheme, which is why `HttpAndHttps` needs a second port rather than a flag. The port is stated by
the operator rather than defaulted, because a default would pick a port nobody published and could collide with
something already listening. It exists for the interval in which a deployment moves from clear text to TLS and both
have to answer.

```json
{
  "HealthEndpoints": {
    "Port": 8081,
    "HttpsPort": 8443,
    "Transport": "HttpAndHttps",
    "Domain": "probe.example.test",
    "ServerCertificate": {
      "CertificateChain": {
        "Name": "probe-chain",
        "SecretReference": "file:/etc/mailfathom/tls/probe-fullchain.pem"
      },
      "PrivateKey": {
        "Name": "probe-key",
        "SecretReference": "file:/etc/mailfathom/tls/probe-privkey.pem"
      }
    }
  }
}
```

```json
{
  "HealthEndpoints": {
    "Port": 8443,
    "Transport": "HttpsOnly",
    "Domain": "probe.example.test",
    "ServerCertificate": {
      "Bundle": {
        "Name": "probe-bundle",
        "SecretReference": "file:/etc/mailfathom/tls/probe.pfx",
        "Password": {
          "Name": "probe-bundle-password",
          "SecretReference": "systemd-credential:mailfathom-probe-bundle-password"
        }
      }
    }
  }
}
```

### Certificate material

The material is resolved by the loader the MCP endpoint's HTTPS profiles use, through the same named-secret references
[secret provisioning](secret-provisioning.md) describes. Either a PKCS#12 bundle or a PEM chain beside its private key;
configuring both is refused, because which of them states the identity would otherwise be decided by nothing an
operator wrote.

`Domain` is what the material is proven against. An orchestrator dialling the pod's own address verifies nothing — the
kubelet's HTTPS probe skips certificate validation entirely — which does not make the claim optional: the operator
still says which certificate this is, and a certificate provisioned for another name is a mistake worth failing on.

It has to be a DNS name, and startup refuses anything else. **An IP address does not work here**, which is worth
stating because the probe listener is the one thing an orchestrator dials by address: matching is against the
certificate's DNS subject alternative names, and those never carry an address, so a certificate with an IP SAN would be
refused too. Wildcards belong in the certificate rather than in this setting — a certificate whose SAN is
`*.example.test` covers a `Domain` of `probe.example.test`, one label deep, exactly as a client would accept it.

**A TLS transport never downgrades.** Material that is missing, unresolvable, expired, or unusable fails startup with
nothing listening. There is no development-certificate fallback and no self-signed fallback, because a probe answering
on a port an operator believed was TLS is worse than one that does not answer.

The certificate is loaded before the server starts, so an expired one is a startup failure rather than a handshake
every probe meets afterwards. Startup also warns when the certificate expires within thirty days.

## Turning the probes off

```json
{
  "HealthEndpoints": {
    "Enabled": false
  }
}
```

No probe route is mapped and no probe listener is opened. Nothing else about the host changes, and no second setting
decides the same thing. A deployment behind something that already tracks process health, or one where the probe port
cannot be published to anything that would use it, is the case this exists for.

## Kubernetes

The chart wires all three probes to a container port of its own and never to the Service:

```yaml
probes:
  port: 8081
  startup:
    path: /started
    periodSeconds: 5
    failureThreshold: 30
  readiness:
    path: /health
    periodSeconds: 10
  liveness:
    path: /alive
    periodSeconds: 20
```

`probes.port` sets the container port the kubelet dials and the `HealthEndpoints__Port` the host binds, so the two
cannot drift. The kubelet reaches a container port on the pod's own address without the Service publishing it, which is
what keeps the probe listener off the network the MCP endpoint is served on. Setting it to `8080` is refused by the
values schema, and that refusal is the chart's alone — the host would share the socket, as [the probe listener is one of
four, and only one](#the-probe-listener-is-one-of-four-and-only-one) describes, which is exactly what publishing the
probes on the port the Service carries would then mean.

The rendered probes are the ones the schema pins: `/started` for startup, `/health` for readiness, `/alive` for
liveness. Each path is a `const` in the schema, so a probe pointed at the wrong endpoint is refused at install time
rather than producing a deployment whose restarts nobody can explain. See
[deploying to Kubernetes](deployment-kubernetes.md#probes).

## Docker Compose

The Compose deployment publishes the probe port to loopback:

```yaml
ports:
  - "127.0.0.1:8080:8080"
  - "127.0.0.1:8081:8081"
```

```bash
curl -fsS http://127.0.0.1:8081/started    # has it finished coming up
curl -fsS http://127.0.0.1:8081/health     # can it serve, database included
curl -fsS http://127.0.0.1:8081/alive      # is the process still running
```

`MAILFATHOM_HEALTH_BIND` and `MAILFATHOM_HEALTH_PORT` move the published address, the way `MAILFATHOM_HTTP_BIND` and
`MAILFATHOM_HTTP_PORT` do for the MCP endpoint. Keep it on loopback unless the machine asking is not this one.

[The Podman Quadlet deployment](deployment-quadlet.md) publishes the same two ports to the same address, through a
`PublishPort=` line in the unit rather than through a variable, so moving one is an edit to the unit followed by
`systemctl --user daemon-reload`.

The container declares no Docker `HEALTHCHECK`. Docker and Podman run one as a command *inside* the container, and the
image is chiseled: it carries no shell and no HTTP client for one to be written in. Adding either so the container could
ask an endpoint that is already reachable from outside would grow its attack surface for nothing. See
[the container image](container-image.md#the-health-endpoints).
