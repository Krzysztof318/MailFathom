# Deploying to Kubernetes

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

**An image.** MailFathom has published no release, so there is nothing to default to. A chart that guessed would deploy an
image nobody named.

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
  registry: docker.io
  repository: <namespace>/mailfathom
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
helm install mailfathom deploy/helm/mailfathom --namespace mailfathom --values values.yaml
kubectl --namespace mailfathom rollout status deployment/mailfathom
```

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
automatic migration this whole arrangement exists to prevent, and one without a hook would still need a schema artifact
the project has not published.

> **The schema artifact does not exist yet.** The reviewed, idempotent artifact and the step that applies it are
> tracked by [issue #126](https://github.com/Krzysztof318/MailFathom/issues/126). Until it ships, apply the schema to the
> database yourself before the pod can become ready — a `psql` Job of your own, a migration run from outside the
> cluster, or whatever your database's operations already use. Take a backup first, and read the SQL before applying
> it.

Two things about the role that applies it are worth knowing now, because they outlive whatever runs the SQL:

**It needs privileges the service's role does not.** The schema installs the `vector` extension, which PostgreSQL does
not permit an ordinary role to create. Either install the extension out of band, as the Compose deployment's
initialization script does, or run the schema step as a more privileged role.

**A separate role leaves ownership behind.** PostgreSQL makes whoever runs the DDL the **owner** of every table,
sequence, and index it creates, and ownership grants nothing to anybody else — so a schema applied by `mailfathom_migrator`
leaves MailFathom failing on permission errors against a schema that plainly exists. Grant `database.user` the DML
privileges it needs and set `ALTER DEFAULT PRIVILEGES` so later migrations are covered too. Grant rather than transfer
ownership: handing the tables over would leave the migrator unable to alter them next time.

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

```bash
helm upgrade mailfathom deploy/helm/mailfathom --namespace mailfathom --values values.yaml
helm rollback mailfathom <revision> --namespace mailfathom
helm uninstall mailfathom --namespace mailfathom
```

`helm rollback` returns the workload to a previous image. **It does not return the schema.** A migration only moves
forward, so rolling back to an image that expects an earlier schema means restoring the database from a backup taken
before the migration.

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

`image.channel: nightly` deploys unsupported GHCR development output. It requires
`image.nightlyAcknowledgement: i-understand-this-is-unsupported`, forces `ghcr.io` and rejects any other registry,
labels every rendered object `io.mailfathom/release-channel: ghcr-nightly-unsupported`, prints a warning in the chart's
notes, and labels the workload with the nightly identifier rather than with `appVersion` — so a nightly is never
indistinguishable from a release in a query that reads that label.

## Verification

Reading the chart needs only Helm, and it is what a change here is reviewed with:

```bash
helm lint     deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/release-values.yaml
helm template verification deploy/helm/mailfathom --values deploy/helm/mailfathom/ci/nightly-values.yaml
```

`deploy/helm/mailfathom/ci/` holds those two values files. They are excluded from the packaged chart and name no real
image and no real database.

Installing the chart into a real cluster and asserting what only a running deployment can answer — that the pod reaches
the database through the chart's own wiring and then refuses to serve against a schema no reviewed artifact has applied
— belongs to the release pipeline issue #156 owns, together with building and publishing the assets. The repository
runs no cluster of its own for it.

## Related

- [The container image](container-image.md) — what is inside it, how it runs, and the schema script
- [Docker Compose](deployment-compose.md) — the same contract in the other shape
- [Configuration sources](configuration-sources.md), [secret provisioning](secret-provisioning.md),
  [the MCP endpoint](mcp-endpoint.md)
