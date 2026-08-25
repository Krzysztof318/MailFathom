# Configuration sources

<!-- describes: backend/src/Host/Configuration/** -->

MailFathom reads its settings through the ordinary .NET configuration pipeline, plus two additions. A deployment may name a directory or a file of JSON configuration that it provisioned outside the application's own content root, which is what makes a Kubernetes ConfigMap mounted as a volume ordinary configuration rather than a shape the host cannot see. And the deployment's own persisted settings — one document in PostgreSQL — are layered in above those files, so a setting that was changed while the deployment was running binds and validates exactly as one that came from a file.

Secrets are a separate contract and stay one. A secret-bearing setting holds a reference rather than material, whichever source the setting itself arrived from; [secret provisioning](secret-provisioning.md) is that contract, and the [Kubernetes mapping](#kubernetes) below states how the two meet.

**No file MailFathom reads is ever written back.** The file you provisioned is the file in force: it can be reviewed, diffed, and restored as the truth about what the *deployment* configured, and nothing in the process edits it, writes a value into it, or rewrites an environment variable. What the service itself has to modify lives in PostgreSQL instead, which is where the **root settings layer** below comes from — one persisted document, read as an ordinary configuration source between the deployment's files and the operator's overrides. A mailbox refresh token is the older example of the same rule: it is stored sealed in the database rather than written back into the secret reference it arrived through. [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md) records the decision, and its second amendment records the layer.

Reading a setting from the database does not make your files editable by the process, and it does not make the persisted layer a second configuration system: it is one more source in the ordinary .NET order, so binding, object composition, indexed arrays, validation, and reload tokens stay one mechanism rather than acquiring a parallel one.

## Precedence

Highest precedence first. Everything except the provisioned and persisted layers is the default .NET order.

| # | Source | Set by |
| --- | --- | --- |
| 1 | Command-line arguments | `--MailboxSearch:SnippetsPerEmail=3` |
| 2 | Environment variables | `MailboxSearch__SnippetsPerEmail=3` |
| 3 | User secrets, in the `Development` environment only | `dotnet user-secrets` |
| 4 | **Root settings**, the persisted configuration document | The `settings_root` row in PostgreSQL |
| 5 | **Provisioned file**, when `ConfigurationSources:File` names one | A mounted file, a systemd drop-in |
| 6 | **Provisioned directory**, when `ConfigurationSources:Directory` names one | A ConfigMap mounted as a volume |
| 7 | `appsettings.{Environment}.json` | The image or the checkout |
| 8 | `appsettings.json` | The image or the checkout |

Everything MailFathom adds sits below the three sources an operator reaches for when a deployment is wrong. That direction is the one an operator can act on: injecting one variable changes one setting for one process without editing a shared object and without first reaching the database, which is what makes a bad persisted value repairable. Layering either of them on top instead would let a ConfigMap nobody remembered to update, or a row somebody wrote months ago, silently beat a value injected beside it, and nothing about the running process would show which of the two won.

Within the provisioned layer, the single file wins over the directory, so a deployment that mounts a shared ConfigMap and then names one file of its own gets the specific value rather than an order decided by how the two happen to sort.

The persisted layer sits above both of them, because it is the later decision: the files state what the deployment provisioned, and a persisted value is what was chosen against that afterwards.

This order governs every MailFathom setting. A short list of platform variables sits outside it entirely — the OpenTelemetry exporter's `OTEL_*` family, the host's `ASPNETCORE_*` and `DOTNET_*` names, and `OPENSSL_CONF` — because each is read before this composition exists or by a library that never consults it. Writing one of them into a file or a command-line argument fails startup rather than being accepted and ignored; [environment-only settings](configuration-reference.md#environment-only-settings) is the list and the reasoning.

## Naming the provisioned sources

Two keys, both unset by default. A deployment that names neither keeps exactly the default source list.

| Key | Environment form | Names |
| --- | --- | --- |
| `ConfigurationSources:Directory` | `ConfigurationSources__Directory` | A directory whose `*.json` files are layered in |
| `ConfigurationSources:File` | `ConfigurationSources__File` | One JSON file layered above that directory |

They are read from configuration rather than from the environment directly, so the same setting arrives as an environment variable in a container, as `--ConfigurationSources:Directory=/etc/mailfathom/config` under systemd, and from `appsettings.json` during local development, without a second mechanism per deployment shape. A blank value reads as unset, because templating a manifest routinely emits an empty string for a setting the operator left alone.

The section takes those two settings and nothing else. A third one — a misspelled `Directroy`, a key from an older draft — fails startup naming it, for the same reason every other security-sensitive section in MailFathom is bound strictly: a setting that bound nothing would leave the host running on defaults while the operator believed their mount was in force.

Both settings are **restart-required**. They decide which sources exist, which is settled while the host is being composed, so repointing `ConfigurationSources:Directory` at a different mount takes effect on the next start. What the named sources *contain* is a different question, and the answer is below.

### What a directory contributes

- Files matching `*.json`, and nothing else. A `notes.txt` or a `settings.json.bak` left beside them is ignored rather than parsed.
- Ordered by file name, compared ordinally, so the same ConfigMap layers the same way on every machine that mounts it. Later names win: `20-persistence.json` overrides `10-defaults.json`.
- Top-level entries only. Subdirectories are not searched.
- Entries beginning with `..` are skipped. Kubernetes updates a mounted volume by writing a new timestamped directory and repointing the `..data` symbolic link at it, which is what makes the update atomic; both entries live beside the keys and neither is configuration.

An existing but empty directory is permitted and contributes nothing. A ConfigMap with no keys is a legitimate state during a rollout, and the startup record below reports the count so the case is visible rather than silent.

## The persisted layer

`settings_root` in PostgreSQL holds one row, and that row's `jsonb` document is this layer. It is read once while the host composes its configuration, flattened into ordinary colon-delimited keys by the framework's own JSON parser, and inserted at the position the table above gives. Nothing about it is a second configuration system: a persisted `MailboxSearch:SnippetsPerEmail` is the same key, bound by the same options class, and validated by the same validator as one written into a file.

**The document is sparse, and a key it does not carry is inherited rather than blank.** Persisting one setting therefore does not mean restating everything the deployment provisioned beside it. Objects compose by child key and array elements override by their own numeric index, which is the ordinary .NET provider behaviour and not a merge rule of this layer's own: a persisted value at index `1` replaces index `1`, and the indexes the document omits stay visible from the file underneath.

**One read, not one query per setting.** The row is loaded as a single document snapshot, so reading a configuration property costs nothing at the database.

**The document is read at startup, and editing the row takes effect at the next start.** Nothing watches `settings_root` and nothing polls it, so a row edited directly in the database — with `psql`, or by anything other than MailFathom — changes no setting in the running process. Restart the host to compose over it.

### What it may not carry

Everything needed to open the database is read from the sources beneath this layer, because a persisted value for one of them could not be read without first reading it. That is the whole of the list, and a document carrying one of these keys — or anything nested beneath it — is **refused**, under error code `12004`, naming the keys:

- `ConnectionStrings:mailfathom`, `Persistence:ConnectionString`, and `Persistence:Password` — where the database is and how to authenticate to it.
- `Secrets:Interpretation` — how the secret reference carrying that credential is read.
- `ConfigurationSources:Directory` and `ConfigurationSources:File` — which sources exist at all, which is settled before this one is composed.

Refused rather than ignored, because ignoring would be the dangerous half of the two. The layer is composed above every file, so a persisted `Persistence:Password` that reached the published snapshot would leave the bootstrap read authenticating with the file's credential while the connection pool and every worker used the persisted one, with nothing in the running process reporting the disagreement; and `Secrets:Interpretation` decides whether a plain-text value written where a reference belongs fails startup or is accepted, for the whole process, which would make this layer a way to relax the terms it is itself trusted under. Dropping the keys silently would leave an operator believing they had configured something. Configure all six where the bootstrap read takes them from: a file, the environment, or a command-line argument.

Secret material is not read from it either, and for a different reason: a secret-bearing setting holds a *reference* rather than material whichever source supplied it, so the persisted document carries the same references a file would and the material stays wherever [secret provisioning](secret-provisioning.md) puts it.

### Startup, and a reload that fails

**The host fails to start when this layer cannot be read.** A database that cannot be reached, a database that refuses the configured credential, a database whose serving role holds no privilege on the table, a schema that does not carry the table, a row that is not there, and a document that is not a JSON object of configuration keys are one failure to the process: the layer between the deployment's files and the operator's overrides cannot say what it contributes, and starting anyway would serve whichever values the files beneath it happen to carry with nothing saying that a layer was missing. All six carry error code `12003` and stop the process before any endpoint opens, and each message names which of them happened — a rejected credential sends the operator to the secret block and a refused privilege to the grant, rather than either to the network. A document that carries a key [the layer may not carry](#what-it-may-not-carry) stops the process too, under `12004` rather than `12003`, because it is a document MailFathom read perfectly well and refused. A database that has not had this release's migrations applied is the ordinary cause, and the message says so; [the database schema](database-schema.md) states the order a deployment applies them in.

Every start records the version it composed itself over, at `Information`:

```
Host MailFathom.Host composed its settings over persisted configuration version 4.
```

That number is the only record of which document the process actually read — the files are in the repository and the environment is in the manifest, and what the row held at that moment is otherwise unrecoverable from the running process.

**The host carries a republish path, and nothing in this release drives it.** Republishing a later document to everything bound to it is what a committed configuration write ends with, and the write itself is not part of this release. What the path guarantees when something does drive it: a candidate that cannot be read leaves the deployment exactly as it was, one that reads but is not a configuration document is rejected *by version* with the record naming both the version that did not take and the version still serving, and a fall back to the files beneath this layer never happens — those never carried the persisted values, so reverting to them would quietly change settings the deployment had already adopted.

## Failure and startup behavior

A configured path that does not exist fails startup, naming the configuration key and the path:

```
The configuration directory named by ConfigurationSources:Directory does not exist: /etc/mailfathom/config.
```

So does a setting the section does not define:

```
ConfigurationSources carries settings MailFathom does not define: Directroy. The section defines Directory and File.
```

Both are deliberate and are the point of the feature. A host that ignored an absent mount or a misspelled key would report success while serving configuration nobody wrote, and the divergence would only surface later, through behavior. Both carry error code `12001` and end the process through the bootstrap logging pipeline described in [host startup telemetry](host-startup-telemetry.md).

Every start records how many provisioned files were layered in, at `Information`:

```
Host MailFathom.Host layered 3 deployment-provisioned configuration files below the environment.
```

A `0` on a deployment that mounts a ConfigMap means the mount is empty or did not arrive where the key says it did.

## Reload

This section is about the files. The persisted layer is not reloaded at all in this release: it is composed once at startup, as [the persisted layer](#the-persisted-layer) above states, and a row edited directly takes effect at the next start.

What reloads is the **content of the files that existed when the host started**. Each of those gets a watched provider, so a setting group classified reloadable in [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md) picks up an edited ConfigMap key without a restart, through the same validated-snapshot path every other source uses. A candidate snapshot that fails validation is rejected and the last known good one stays active.

**Adding or removing a ConfigMap key is restart-required.** The directory is enumerated once, while the host is composing itself, and each file found becomes its own provider; nothing watches the directory for membership. A key added to a mounted ConfigMap therefore produces a file no provider reads, and a key removed empties its provider rather than removing the layer. Restart the pod after changing which keys a ConfigMap holds. Editing the value inside a key that already existed needs no restart.

Two caveats decide whether even a content change actually arrives, and neither is MailFathom's to fix:

- **A `subPath` mount never updates.** The kubelet updates a mounted ConfigMap by swapping the volume's `..data` link, and a `subPath` mount bypasses that entirely — the file the container sees is the one that existed when the pod started. Mount the whole volume and use `ConfigurationSources:Directory` when reload matters; use `subPath` only where a restart on change is acceptable and say so in the deployment.
- **Change detection on a mounted volume needs polling.** `FileSystemWatcher` does not reliably observe the symbolic-link swap that an atomic update performs. Setting `DOTNET_USE_POLLING_FILE_WATCHER=1` makes the file provider poll every four seconds instead; the interval is not configurable. Microsoft documents this for container and network-share mounts generally, not only for Kubernetes.

Two further properties belong to Kubernetes rather than to the watcher: an update reaches the container after the kubelet's sync period plus its cache TTL, up to about a minute by default, and an `immutable: true` ConfigMap never updates at all.

## Kubernetes

Nothing here needs a Kubernetes-specific scheme or provider. A mounted ConfigMap is a directory of files and a mounted Secret is a file, which is why `ConfigurationSources:Directory` and the `file:` secret scheme serve both without either one naming Kubernetes.

### Which construct carries what

| MailFathom input | Kubernetes construct | How MailFathom reads it |
| --- | --- | --- |
| Non-secret settings, in bulk | ConfigMap mounted as a volume | `ConfigurationSources:Directory` |
| Non-secret settings, one file | ConfigMap mounted with `subPath` | `ConfigurationSources:File`, without reload |
| A per-pod override of one setting | ConfigMap key in the environment block | The environment provider, which outranks both |
| Credential and certificate material | Secret mounted as a volume | `file:/…` in the setting's `SecretReference` |
| Credential material, without a volume | Secret key in the environment block | `env:…`, subject to the caveats below |
| Material from an external store | Secrets Store CSI driver | `file:/…`, because the driver mounts files |

A Secret needs no MailFathom support beyond what already exists: `FileSecretReferenceResolver` performs exactly the read a `kubernetes-secret:` scheme would, which is why no such scheme exists. Material is resolved per use and erased immediately, so a Secret rotated behind an unchanged mount path reaches the next IMAP connection or the next database connection without a restart and without a configuration reload — the reference did not change, only what it points at.

`env:` is the exception on both counts. The platform hands the value over as an immutable `string` that cannot be erased from process memory, and a Secret projected into the environment block is fixed for the life of the pod, so rotating it requires a restart. It is documented for non-production automation and is not recommended in production; [secret provisioning](secret-provisioning.md#secret-material-in-process-memory) states the full reasoning.

### A worked deployment

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: mailfathom-config
data:
  10-mail.json: |
    {
      "MailSynchronization": {
        "Accounts": [
          {
            "AccountId": "primary",
            "DisplayName": "Personal mail",
            "Host": "imap.example.test",
            "Port": 993,
            "UserName": "mailfathom@example.test",
            "Secrets": {
              "Password": { "Name": "primary-imap-password", "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password" }
            },
            "TransportSecurity": { "ConnectionSecurity": "TlsOnConnect" },
            "Folders": [ { "Alias": "inbox", "SpecialUse": "Inbox" } ]
          }
        ]
      }
    }
  20-persistence.json: |
    {
      "Persistence": {
        "Password": { "Name": "postgres", "SecretReference": "file:/etc/mailfathom/secrets/postgres-password" }
      }
    }
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mailfathom
  labels:
    app.kubernetes.io/name: mailfathom
spec:
  replicas: 1
  selector:
    matchLabels:
      app.kubernetes.io/name: mailfathom
  template:
    metadata:
      labels:
        app.kubernetes.io/name: mailfathom
    spec:
      containers:
        - name: mailfathom
          # Substitute the image your deployment uses. This page does not define the image contract;
          # docs/operations/container-image.md names the published references and the tags they carry.
          image: mailfathom:replace-me
          env:
            - name: ConfigurationSources__Directory
              value: /etc/mailfathom/config
            # Polling is what makes a ConfigMap change reach the running process; see the reload caveats above.
            - name: DOTNET_USE_POLLING_FILE_WATCHER
              value: "1"
          volumeMounts:
            - name: config
              mountPath: /etc/mailfathom/config
              readOnly: true
            - name: secrets
              mountPath: /etc/mailfathom/secrets
              readOnly: true
      volumes:
        - name: config
          configMap:
            name: mailfathom-config
        - name: secrets
          secret:
            secretName: mailfathom-secrets
```

The ConfigMap carries the settings and the secret *references*; the Secret carries the material. That split is the property the reference indirection exists for: this ConfigMap is safe to commit, review, and diff, because a copy of it yields credential paths rather than credentials.

`Secrets:Interpretation` stays at its `ReferenceOnly` default here, so a plain-text password pasted where a reference belongs fails startup instead of authenticating. Read [secret provisioning](secret-provisioning.md#interpretation-modes) before changing it.
