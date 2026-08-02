# Deploying to Kubernetes

<!-- describes: deploy/helm/** -->

`deploy/helm/mailfathom/` is the chart. It installs MailFathom and the objects around it, and deliberately installs neither a
database nor a Secret: both belong to whoever operates the cluster, and the chart is written so that it cannot pretend
otherwise.

| It renders | It does not render |
| --- | --- |
| Deployment, Service, ConfigMap, ServiceAccount | Any `Secret` |
| An optional Ingress | Any certificate material |
|  | Any database, and any schema step |

## What you supply

Two things have no default and the chart refuses to render without them.

**An image.** Released images are on both registries under the same digest, and the chart still defaults to none of
them: a default would pin every install to whichever version this chart happened to name, and a moving one would let a
cluster follow a version nobody chose. You name the immutable reference your deployment runs.

**A database.** PostgreSQL with the `vector` extension. A store holding every synchronized message needs backup,
durability, and an upgrade path that a subchart cannot own.

A Secret is the third thing, and the chart names it rather than creating one.

```bash
kubectl create namespace mailfathom

kubectl --namespace mailfathom create secret generic mailfathom-secrets \
  --from-literal=mailfathom-database-password='…' \
  --from-file=imap-primary-password=./imap-primary-password \
  --from-file=mcp-workstation-key=./mcp-workstation-key
```

It is mounted read-only at `/etc/mailfathom/secrets`, one file per key, so every credential is a `file:` reference — the
same path and the same references the Compose deployment uses.

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
  host: postgres.databases.svc.cluster.local
  name: mailfathom
  user: mailfathom

secrets:
  existingSecret: mailfathom-secrets

config:
  files:
    10-mailfathom.json: |
      {
        "MailSynchronization": {
          "Enabled": true,
          "Accounts": [
            {
              "AccountId": "primary",
              "Host": "imap.example.test",
              "Port": 993,
              "UserName": "you@example.test",
              "Secrets": {
                "Password": {
                  "Name": "imap-primary-password",
                  "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password"
                }
              },
              "TransportSecurity": { "ConnectionSecurity": "TlsOnConnect" },
              "Folders": [ { "Alias": "inbox", "SpecialUse": "Inbox" } ]
            }
          ]
        },
        "McpEndpoint": {
          "Enabled": true,
          "Authentication": "ApiKey",
          "ApiKeys": [
            { "Name": "workstation", "SecretReference": "file:/etc/mailfathom/secrets/mcp-workstation-key" }
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
below all of it is the repository's root `README.md`, copied into the package at release time so there is one overview
rather than two that drift.

Installing the chart directory out of a checkout is the development path and stays available:

```bash
helm install mailfathom deploy/helm/mailfathom --namespace mailfathom --values values.yaml
```

An unpackaged directory states no `appVersion`, because it is not a release of anything, so the version-drift check
below stands down for it.

A digest is preferred over a tag: it is the only reference a registry cannot repoint, so a rollback goes back to the
same bytes. `values.schema.json` rejects `latest` and the other moving tags outright.

Nothing in the ConfigMap may be a credential — it is readable by anything holding `get` on it and is neither encrypted
at rest nor audited like a Secret. The chart puts no credential there and none in the rendered Deployment; the
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
  --file mailfathom-schema-0.1.0.sql
```

The role that applies it needs privileges `database.user` does not — the `vector` extension is one an ordinary role may
not create — and PostgreSQL leaves whoever ran the DDL owning every object it created, so `database.user` needs grants
rather than a transfer of ownership. [Applying the database schema](database-schema.md) states both in full, along with
the locks the script takes and what each startup failure means.

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
| API keys | `McpEndpoint:Authentication` and `McpEndpoint:ApiKeys` | [Authentication](mcp-endpoint.md#authentication) |
| An `Origin` gate | `McpEndpoint:Cors` | [CORS and the `Origin` header](mcp-endpoint.md#cors-and-the-origin-header) |
| TLS terminated by the pod itself | `McpEndpoint:Https:Endpoints` | [HTTPS and your own domain](mcp-endpoint.md#https-and-your-own-domain) |
| Client certificates | `McpEndpoint:ClientCertificateProfiles` | [Client certificates](mcp-endpoint.md#client-certificates) |
| Rate limits | `McpEndpoint:RateLimiting` | [Rate limiting](mcp-endpoint.md#rate-limiting) |

Configuring `Https:Endpoints` takes over the host's application listener, so the chart's `service.port` and the `http`
container port have to match what the profiles bind. The probe listener is unaffected and keeps its own transport. That is a deliberate step rather than the default: in a cluster, TLS at the ingress is
usually what an operator already has.

The credentials any of them reads stay `file:` references into the mounted Secret. Keep them out of `config.files` and
out of `config.extraEnvironment`; the values schema rejects an environment name that reads like a credential, because
an environment block is visible to anything that can read `/proc` and cannot be erased from process memory.

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
| Startup | `/started` | The host's own startup gates: every secret reference resolved, the database schema verified. Its budget is what a slow first start is allowed, and it holds liveness off until it succeeds. |
| Readiness | `/health` | The dependencies a request needs, the database included. A pod that cannot serve leaves the Service's endpoints. |
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

## Scheduling, resources, and placement

`nodeSelector`, `tolerations`, `affinity`, `topologySpreadConstraints`, `priorityClassName`, `resources`,
`podAnnotations`, `podLabels`, `service.type`, `service.annotations`, and `terminationGracePeriodSeconds` are all
values. Nothing requires editing a template.

`terminationGracePeriodSeconds` defaults to 60 against a 10-second `MailSynchronization:ShutdownDrainTimeout`. Raise
them together: a grace period shorter than the drain kills the process with the drain still running.

## Upgrading, rolling back, and uninstalling

Back up the database and apply the new release's `mailfathom-schema-<version>.sql` **before** the upgrade. The new pod
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

Uninstalling removes every object the chart owns. It removes **no** data: the database is not the chart's, and the
Secret was created outside it and stays.

### Chart version and application version

`Chart.version` moves whenever anything under the chart directory changes; `Chart.appVersion` is the application
version the chart is written against. They are separate, and a values default corrected without touching the image is a
chart release on its own.

`Chart.yaml` carries no `appVersion`, deliberately: the release run supplies it when it packages the chart, from the
`VersionPrefix` in `Directory.Build.props` that is the only file in the repository carrying an application version.

```bash
helm package deploy/helm/mailfathom --app-version "$(bash scripts/read-declared-version.sh)"
```

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

Reading the chart needs only Helm, and it is what a change here is reviewed with:

```bash
helm lint     deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/release-values.yaml
helm template verification deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/nightly-values.yaml
```

`deploy/helm/mailfathom/ci/` holds those two values files. They are excluded from the packaged chart and name no real
image and no real database.

The release run performs the same two commands as a gate before it publishes anything, against both values files, so a
chart that does not lint or render is never published. It additionally renders the packaged chart against the digest
the release published and refuses one that would deploy anything else.

Installing the chart into a real cluster and asserting what only a running deployment can answer — that the pod reaches
the database through the chart's own wiring and then refuses to serve until the release's schema artifact has been
applied — is still not done anywhere. The repository runs no cluster of its own for it.

## Related

- [Applying the database schema](database-schema.md) — the release artifact, the privileges it needs, and the three
  startup failures it answers
- [The container image](container-image.md) — what is inside it, how it runs, and why it carries no schema tool
- [Docker Compose](deployment-compose.md) — the same contract in the other shape
- [The platform TLS policy](platform-tls-policy.md) — for a mail server whose handshake the pod's own OpenSSL refuses;
  `config.extraEnvironment` names the file, and the chart currently has no hook for mounting it
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
