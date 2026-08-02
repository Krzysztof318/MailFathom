# Configuration sources

<!-- describes: src/Host/Configuration/** -->

MailFathom reads its settings through the ordinary .NET configuration pipeline, plus one addition: a deployment may name a directory or a file of JSON configuration that it provisioned outside the application's own content root. That addition is what makes a Kubernetes ConfigMap mounted as a volume ordinary configuration rather than a shape the host cannot see.

Secrets are a separate contract and stay one. A secret-bearing setting holds a reference rather than material, whichever source the setting itself arrived from; [secret provisioning](secret-provisioning.md) is that contract, and the [Kubernetes mapping](#kubernetes) below states how the two meet.

## Precedence

Highest precedence first. Everything except the provisioned layer is the default .NET order.

| # | Source | Set by |
| --- | --- | --- |
| 1 | Command-line arguments | `--MailboxSearch:SnippetsPerEmail=3` |
| 2 | Environment variables | `MailboxSearch__SnippetsPerEmail=3` |
| 3 | **Provisioned file**, when `ConfigurationSources:File` names one | A mounted file, a systemd drop-in |
| 4 | **Provisioned directory**, when `ConfigurationSources:Directory` names one | A ConfigMap mounted as a volume |
| 5 | User secrets, in the `Development` environment only | `dotnet user-secrets` |
| 6 | `appsettings.{Environment}.json` | The image or the checkout |
| 7 | `appsettings.json` | The image or the checkout |

The provisioned layer sits below the environment block on purpose. A file states what the deployment configured and an environment variable overrides it, which is the direction an operator can act on: injecting one variable changes one setting for one pod without editing a shared object. Layering the files on top instead would let a ConfigMap that nobody remembered to update silently beat a value injected beside it, and nothing about the running process would show which of the two won.

Within the provisioned layer, the single file wins over the directory, so a deployment that mounts a shared ConfigMap and then names one file of its own gets the specific value rather than an order decided by how the two happen to sort.

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

What reloads is the **content of the files that existed when the host started**. Each of those gets a watched provider, so a setting group classified reloadable in [ADR 0002](../decisions/0002-configuration-reading-mapping-and-reload-boundary.md) picks up an edited ConfigMap key without a restart, through the same validated-snapshot path every other source uses. A candidate snapshot that fails validation is rejected and the last known good one stays active.

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
          # Substitute the image your deployment uses. MailFathom publishes none yet; the image contract is
          # tracked separately and this page does not define it.
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
