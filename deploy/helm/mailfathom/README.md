<img src="https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/icon-900.png" alt="MailFathom logo" width="120">

# MailFathom

**A brain for your mail — self-hosted, AI-native, and yours alone.**

This page describes the Helm chart. **[github.com/Krzysztof318/MailFathom](https://github.com/Krzysztof318/MailFathom) is the project**, and [deploying on Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html) is where everything below is stated in full.

MailFathom synchronizes your IMAP accounts into a PostgreSQL database you run, indexes that copy, and serves it to AI agents as tools over the [Model Context Protocol](https://modelcontextprotocol.io/). Nothing depends on somebody else's service: the copy is yours, the database is yours, and the deployment is yours. Reading is local, so a read answers from your copy and never contacts a mail server; synchronization never sets the remote `\Seen` flag, so mail MailFathom has copied still shows as unread in your own mail client. An agent gets twenty-one tools: five that read your mail, six over MailFathom's own contact book, eight whose effect reaches a mail server — `set_mail_flags`, which marks, stars, and labels one message; `send_email`, `reply_to_email`, and `forward_email`, which send one, answer one you already hold, and pass one on; and `save_draft`, `update_draft`, `delete_draft`, and `send_draft`, which write a message into your own Drafts folder, edit it, take it back out, and send it — and two that answer for a send rather than performing one, `get_outgoing_email` and `cancel_outgoing_email`, which report what became of a queued message and stop one that has not left yet. None of the eight waits for mail to be delivered, each holds a grant that reading mail does not carry, and a deployment that grants none of them is read-only end to end — [using the tools](https://krzysztof318.github.io/MailFathom/users/usage.html) is what each one answers.

## What this chart renders

| It renders | It does not render |
| --- | --- |
| Deployment, Service, ConfigMap, ServiceAccount | Any `Secret` |
| A PostgreSQL StatefulSet, its Service, and its initialization script, unless `database.deploy.enabled` is false | Any certificate material |
| A personal-data analyzer Deployment and Service, only when `personalDataScanning.enabled` and `.analyzer.deploy` are both true | Any schema step |
| A SpamAssassin Deployment and Service, only when `spamScanning.enabled` and `.scanner.deploy` are both true | |
| A Silo object-store StatefulSet, its claim, and its Service, only when `contentStorage.objectStorage.deploy.enabled` is true | Any bucket, or any access key inside one |
| An optional Ingress | |

Both scanners are off, and off means nothing is rendered for them: an opt-in nobody took pulls no image and holds no memory. Take either deliberately — they are the two pods in this release that receive mail content in the clear, and the spam scanner's container adds `SETUID` and `SETGID` back to the capabilities the application pod drops entirely. Neither is given a service-account token.

It installs no Secret deliberately: credentials belong to whoever operates the cluster, and the chart is written so that it cannot pretend otherwise. It carries **no subchart** either — the PostgreSQL templates are MailFathom's own, so nothing puts another project's values and release cadence between this chart and the store holding every synchronized message.

## What you supply

**An image.** The chart defaults to none: a default would pin every install to whichever version the chart happened to name. Released images are on `ghcr.io/krzysztof318/mailfathom` and `docker.io/krzysztof318/mailfathom` under the same digest, and `values.schema.json` rejects `latest` and the other moving tags outright.

**A Secret.** It is mounted read-only at `/etc/mailfathom/secrets`, one file per key, so every credential in your configuration is a `file:` reference. A database the chart deploys needs a *second* Secret for the PostgreSQL superuser password, which the application pod never mounts — and the chart refuses a values document naming one Secret for both.

**A database, which is the one with a default.** The chart runs PostgreSQL with the `vector` extension as a single-replica StatefulSet on a retained claim. That gives the data a lifetime longer than the pod's and nothing else — no backup schedule, no failover, no point-in-time recovery, and no upgrade path across a PostgreSQL major — so point the chart at a server you already operate once any of those is somebody's job:

```yaml
database:
  deploy:
    enabled: false
  host: postgres.databases.svc.cluster.local
```

**Optionally, a bucket.** `contentStorage.backend` decides where the raw MIME of each message is written: PostgreSQL beside the metadata by default, or an S3-compatible endpoint. The chart renders nothing at all on the default, so a values document that never mentions this installs exactly what it always did. Selecting the bucket requires an `https` endpoint, a bucket name, and both halves of a credential — as *keys inside the Secret above*, never as values here — and it changes what a backup is: the rows then point at objects in the bucket, so the database and the bucket are backed up together and restored database-first. Switching it is a move rather than a setting, because every stored message records which store holds its own content. [Storing message content in a bucket](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html#storing-message-content-in-a-bucket) is the page.

**And optionally the store itself.** `contentStorage.objectStorage.deploy.enabled` runs one [Silo](https://github.com/pgsty/silo) node — a maintained fork of the open-source MinIO server — on a retained claim beside MailFathom, for a deployment that wants payload bytes out of PostgreSQL without adopting a cloud provider. The endpoint is then derived from its Service rather than named, its console is not served by default, and its root credential lives in a Secret of its own that the application pod never mounts. It is one node and one volume: no erasure coding, no replication, and no failover. Because MailFathom refuses a plain `http` endpoint, the store terminates TLS with a certificate you supply for its Service name, and the authority that signed it is the `trustAnchorSecretKey` beside the credential. Nothing of Silo is in this chart or in any MailFathom image — the cluster pulls it from PGSTY's own registry, under AGPL-3.0-or-later, which [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) records. [Running the object store beside MailFathom](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html#running-the-object-store-beside-mailfathom) is the page.

## Installing

```bash
helm install mailfathom oci://ghcr.io/krzysztof318/charts/mailfathom \
  --version <x.y.z> --namespace mailfathom --values values.yaml
```

```yaml
# values.yaml
image:
  registry: ghcr.io             # or docker.io; both carry the same digest
  repository: krzysztof318/mailfathom
  digest: sha256:…              # or an immutable tag

database:
  deploy:
    superuserPasswordSecret: mailfathom-postgres-superuser

secrets:
  existingSecret: mailfathom-secrets

config:
  files:
    10-mailfathom.json: |
      { "MailSynchronization": { "Enabled": true, "Accounts": [ … ] } }
```

[Deploying on Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html) carries the whole values document, both `kubectl create secret` commands, and what each key is for.

## The first install does not become ready, and that is the design

MailFathom verifies the database schema while starting and refuses to serve against one it does not recognize:

```
DatabaseSchemaOutOfDateException: The database has not applied 1 migration(s) this build defines: 20260731132336_Initial.
```

The chart renders nothing that answers it. A Job carrying a Helm hook would be the automatic migration this arrangement exists to prevent, and an `initContainer` would run one apply per replica. The answer is the idempotent `mailfathom-schema-<version>.sql` attached to every [release](https://github.com/Krzysztof318/MailFathom/releases): take a backup, read the SQL, and apply it yourself. [Applying the database schema](https://krzysztof318.github.io/MailFathom/operations/database-schema.html) states the privileges it needs and what each startup failure means.

Upgrading takes the same step **before** the new pods roll: the new pod refuses a schema behind it, and the old pod keeps serving against one ahead of it.

## What the pod is

| Property | Value |
| --- | --- |
| Pod Security Standard | **Restricted**, and the schema keeps the load-bearing settings from being switched off — `runAsNonRoot`, `readOnlyRootFilesystem`, `allowPrivilegeEscalation: false`, `capabilities.drop: [ALL]`, `seccompProfile: RuntimeDefault` |
| User | `1654`, the unprivileged `app` account — never root |
| Writable paths | An in-memory `emptyDir` at `/tmp`, which is the only path the runtime writes to |
| Service | Port `8080`, over plain HTTP. It carries whichever surfaces the configuration enabled — `/mcp`, `/api/admin`, and `/api/client` all default to that container port — and answers `404` on the prefix of one that is off |
| Probes | `/started`, `/health`, and `/alive` on `probes.port`, `8081` by default, which the Service never publishes |
| Service account token | Not mounted. MailFathom calls no Kubernetes API. |

**The pod terminates no TLS and asks for no credential to start.** An ingress or a service mesh in front of it owns TLS termination and whatever client authentication the cluster imposes. Every gate MailFathom has of its own — API keys, OAuth, an `Origin` gate, client certificates, rate limits — is a ConfigMap entry under `config.files` rather than a chart value; [the MCP endpoint](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html) is the page. Which surfaces are served is the same kind of entry: `ClientEndpoint` is what a MailFathom client reaches and is off unless a deployment writes it, and publishing it through the ingress means adding `/api/client` to `ingress.hosts[].paths` and naming the page's origin in `ClientEndpoint:Cors:AllowedOrigins` — [the client endpoint](https://krzysztof318.github.io/MailFathom/operations/client-endpoint.html) is that page. An OAuth deployment should not skip `ReverseProxy:TrustedProxies`, because the public scheme and host are read from any peer until you name your ingress.

**MailFathom's own client travels inside the image**, so `client.enabled` serves it and pulls nothing. It is the one thing in this section that is a chart value rather than a ConfigMap entry, because the chart writes more than one setting for it. Turning it on serves the bundle from the same listeners `ClientEndpoint` already answers on: no second workload is rendered, no second host is published, and nothing is pulled. It needs `ClientEndpoint:Enabled` in `config.files` beside it, and a `/` path in `ingress.hosts[].paths` if the ingress is what people reach it at. What it exposes is the application's own code, published under Apache-2.0; **authorization is unchanged**, because a browser is an untrusted client wherever it was served from and whatever `ClientEndpoint:Authentication` requires is still required of it. The transport is the one part that is refused rather than warned about: the pod speaks plain HTTP, so the chart states the operator's permission for that hop only where it can see `ingress.enabled` and `ingress.tls` both set, and a deployment terminating TLS somewhere the chart cannot see must add `ClientEndpoint__Application__AllowClearText: "true"` to `config.extraEnvironment` itself. [Serving the client from the deployment](https://krzysztof318.github.io/MailFathom/operations/client-endpoint.html#serving-the-client-from-the-deployment) is the page.

## The chart version and the application version are one number

`Chart.yaml` here carries `version: 0.0.0` and no `appVersion` at all, because an unpackaged chart directory is not a release of anything. The release run supplies both from the single application version this repository declares, so a **packaged** chart always states which release it deploys — and refuses an install whose `image.tag` disagrees with it unless `image.allowVersionMismatch` says the combination is deliberate.

The nightly channel publishes no chart. Install the most recent released chart and point it at a nightly image through `image.channel: nightly` and its acknowledgement value; [what a nightly build risks](https://krzysztof318.github.io/MailFathom/operations/container-image.html#what-a-nightly-build-risks) states what that acknowledges.

## Verification

```bash
helm show chart oci://ghcr.io/krzysztof318/charts/mailfathom --version <x.y.z>
gh attestation verify oci://ghcr.io/krzysztof318/charts/mailfathom:<x.y.z> --repo Krzysztof318/MailFathom
```

Every published chart carries a signed build provenance statement tying its digest to the commit and the workflow that produced it, exactly as the image does. Before it is pushed it is linted and rendered against every values document the repository verifies it with, and the packaged chart is rendered once more against the digest that release actually published — so a chart that would deploy a different artifact than its own release is never published.

The chart lives at [`deploy/helm/mailfathom/`](https://github.com/Krzysztof318/MailFathom/tree/main/deploy/helm/mailfathom) and installs from a checkout as readily as from the registry, which is the development path.

## Where to go next

| | |
| --- | --- |
| [Deploying on Kubernetes](https://krzysztof318.github.io/MailFathom/operations/deployment-kubernetes.html) | This page's subject, in full |
| [Installing MailFathom](https://krzysztof318.github.io/MailFathom/users/installation.html) | Which deployment shape fits, and what each one needs |
| [Getting started](https://krzysztof318.github.io/MailFathom/users/getting-started.html) | From an installed instance to a first successful tool call |
| [Configuration reference](https://krzysztof318.github.io/MailFathom/operations/configuration-reference.html) | Every user-settable option, grouped by what it configures, with its default and whether changing it needs a restart |
| [The container image](https://krzysztof318.github.io/MailFathom/operations/container-image.html) | What this chart deploys, and how it runs |
| [Changelog](https://krzysztof318.github.io/MailFathom/CHANGELOG.html) | What each release promises across the four public surfaces |

## Security and license

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](https://github.com/Krzysztof318/MailFathom/blob/main/SECURITY.md) rather than in a public issue.

MailFathom is licensed under the [Apache License, Version 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE), SPDX identifier `Apache-2.0`. Every third-party component it ships beside is registered in [THIRD_PARTY_LICENSES.md](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md). The software is provided without warranty and without contributor liability, under sections 7 and 8 of that license.
