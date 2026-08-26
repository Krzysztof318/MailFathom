# Configuration sources

<!-- describes: backend/src/Application/Configuration/**, backend/src/Host/Configuration/**, backend/src/Infrastructure/Persistence/Settings/** -->

MailFathom reads its settings through the ordinary .NET configuration pipeline, plus two additions. A deployment may name a directory or a file of JSON configuration that it provisioned outside the application's own content root, which is what makes a Kubernetes ConfigMap mounted as a volume ordinary configuration rather than a shape the host cannot see. And the deployment's own persisted settings — one document in PostgreSQL, composed at startup like every other source — are layered in above those files, so a setting the deployment has persisted binds and validates exactly as one that came from a file. When an edit to that document takes effect is [its own section](#the-persisted-layer) below.

Secrets are a separate contract and stay one. A secret-bearing setting holds a reference rather than material, whichever source the setting itself arrived from; [secret provisioning](secret-provisioning.md) is that contract, and the [Kubernetes mapping](#kubernetes) below states how the two meet.

**No file MailFathom reads is ever written back.** The file you provisioned is the file in force: it can be reviewed, diffed, and restored as the truth about what the *deployment* configured, and nothing in the process edits it, writes a value into it, or rewrites an environment variable. What the service itself has to modify lives in PostgreSQL instead, which is where the **root settings layer** below comes from — one persisted document, read as an ordinary configuration source between the deployment's files and the operator's overrides, and the one place a setting is ever *changed* by MailFathom. A mailbox refresh token is the older example of the same rule: it is stored sealed in the database rather than written back into the secret reference it arrived through. [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md) records the decision, and its second amendment records the layer.

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

This order governs every MailFathom setting. A short list of platform variables sits outside it entirely — the OpenTelemetry exporter's `OTEL_*` family, the host's `ASPNETCORE_*` and `DOTNET_*` names, and `OPENSSL_CONF` — because each is read before this composition exists or by a library that never consults it. Writing one of them into a file, the persisted configuration document, or a command-line argument fails startup rather than being accepted and ignored; [environment-only settings](configuration-reference.md#environment-only-settings) is the list and the reasoning.

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

**Write an array element as an object keyed by its index, not as a JSON array.** The parser numbers a JSON array's elements by position from `0`, so `{ "Rules": [ { "Name": "persisted" } ] }` sets `Rules:0:Name` whatever you meant it to reach. Overriding the second rule alone is `{ "Rules": { "1": { "Name": "persisted" } } }`, which flattens to exactly the key the file underneath already has. Getting this wrong is silent — the wrong element is replaced, the intended one keeps the file's value, and nothing is logged — because both documents are valid configuration and the layer has no way to tell which element you meant.

**One read, not one query per setting.** The row is loaded as a single document snapshot, so reading a configuration property costs nothing at the database. That one statement is bounded by `Persistence:CommandTimeoutSeconds`, the same bound every other database command carries, because it runs before any endpoint is open and a server that accepts the connection and then answers nothing would otherwise hold the process at that line indefinitely with nothing able to report why.

**The document is read at startup, and a row edited behind MailFathom's back takes effect at the next start.** Nothing watches `settings_root` and nothing polls it, so a row edited directly in the database — with `psql`, or by anything other than MailFathom — changes no setting in the running process. Restart the host to compose over it. A change made [through MailFathom](#changing-a-persisted-setting) is the other case and needs no restart: the process that committed it republishes the layer itself.

### What it may not carry

Everything needed to open the database is read from the sources beneath this layer, because a persisted value for one of them could not be read without first reading it. That is the whole of the list, and a document carrying one of these keys — or anything nested beneath it — is **refused**, under error code `12004`, naming the keys:

- `ConnectionStrings:mailfathom`, `Persistence:ConnectionString`, and `Persistence:Password` — where the database is and how to authenticate to it.
- `Persistence:CommandTimeoutSeconds` — how long that read's one statement may run.
- `Secrets:Interpretation` — how the secret reference carrying that credential is read.
- `ConfigurationSources:Directory` and `ConfigurationSources:File` — which sources exist at all, which is settled before this one is composed.

Refused rather than ignored, because ignoring would be the dangerous half of the two. The layer is composed above every file, so a persisted `Persistence:Password` that reached the published snapshot would leave the bootstrap read authenticating with the file's credential while the connection pool and every worker used the persisted one, with nothing in the running process reporting the disagreement; `Persistence:CommandTimeoutSeconds` is that same split one turn later, bounding the pool and the schema gate with a value the read that fetched it never saw; and `Secrets:Interpretation` decides whether a plain-text value written where a reference belongs fails startup or is accepted, for the whole process, which would make this layer a way to relax the terms it is itself trusted under. Dropping the keys silently would leave an operator believing they had configured something. Configure all seven where the bootstrap read takes them from: a file, the environment, or a command-line argument. The same seven are the settings MailFathom persists nowhere: they are declared non-writable in the same place they are declared unreadable from here, so a write that targeted one is refused under `12006`, naming the setting, rather than committed into the layer it would have had to open. A write names a subtree rather than a value, so this reaches both directions — a write to `Persistence:Password:SecretReference` and a write to `Persistence` are each refused, the second because persisting the section would persist the credential inside it and the next start would then refuse the whole document. A narrower write beside a refused setting, such as `Persistence:MaximumConcurrencyCommitAttempts`, is unaffected.

Secret material is not read from it either, and for a different reason: under the default `ReferenceOnly` interpretation a secret-bearing setting holds a *reference* rather than material whichever source supplied it, so the persisted document carries the same references a file would and the material stays wherever [secret provisioning](secret-provisioning.md) puts it.

That is a property of the interpretation rather than of this layer. Under `ReferenceOrInline` or `InlineOnly` a configured secret-bearing value **is** the material, and a value persisted under either of those modes is material in an unsealed `jsonb` column exactly as the same value in a file would be material in that file. Nothing here inspects a document for it: the seven keys above are refused by name, and every other setting is carried whatever it holds. `Secrets:Interpretation` is itself one of the refused seven, so which of the three modes is in force is always the deployment's own file, environment, or command line — but a deployment that chose an inline mode has chosen it for the persisted layer too.

### Which store a setting is persisted in

**Where each setting lives is decided in compiled code, one entry per store, and by nothing an operator or a caller supplies.** A path no entry names is persisted in `settings_root`, which is almost every setting. A path an entry names is persisted in that entry's own store, and is then **excluded** from `settings_root`, so no setting is described by two rows and no reader has to decide which of the two the deployment meant. There is no configuration key that adds an entry, and no argument that names a table: a store MailFathom could be asked for at run time would be a relation nobody reviewed and a document nothing knows how to read back, so adding one is a change to the catalog, the projection that reads its document, and the migration that creates its table, reviewed together.

One entry exists in this release: the top-level `Accounts` collection of owner accounts, which is persisted per owner in the owner-accounts store rather than as a subtree of the deployment's document. It is **not** `MailSynchronization:Accounts` — the mailbox declarations carry the same word, are an ordinary deployment setting, and stay in `settings_root` with everything else.

A `settings_root` document carrying `Accounts`, or anything beneath it, is therefore **refused** under error code `12005`, naming the path. It is the same choice the refusal above makes and for the same reason: a row an operator wrote by hand is a mistake, and a mistake composed with the duplicate silently dropped is one they go on believing they fixed.

MailFathom writes `settings_root` and no other store: a write naming a path the catalog routes to the owner-accounts store is **refused** under error code `12006`, naming the store, because that store's document is provisioned rather than written. The settings [`settings_root` may not carry](#what-it-may-not-carry) are refused under the same code and for the reason that section gives.

### Startup, and a reload that fails

**The host fails to start when this layer cannot be read.** A database that cannot be reached, a server that carries no database of the configured name, a database that refuses the configured credential, a database whose authorization rules admit no connection for the configured role, host, and database at all, a database whose serving role holds no privilege on the table, a schema that does not carry the table, a row that is not there, and a document that is not a JSON object of configuration keys are one failure to the process: the layer between the deployment's files and the operator's overrides cannot say what it contributes, and starting anyway would serve whichever values the files beneath it happen to carry with nothing saying that a layer was missing. All eight carry error code `12003` and stop the process before any endpoint opens, and each message names which of them happened — a database the server does not carry sends the operator to the provisioning that never created it, a rejected credential to the secret block, a refused authorization to the server's own rules, and a refused privilege to the grant, rather than any of the four to the network. A statement that outran `Persistence:CommandTimeoutSeconds` carries the same code and says so as its own outcome, because the server answered everything up to it. A document that carries a key [the layer may not carry](#what-it-may-not-carry) stops the process too, under `12004` rather than `12003`, because it is a document MailFathom read perfectly well and refused, and one carrying a setting [another store owns](#which-store-a-setting-is-persisted-in) stops it under `12005` for the same reason. A database that has not had this release's migrations applied is the ordinary cause, and the message says so; [the database schema](database-schema.md) states the order a deployment applies them in.

Every start records the version it composed itself over, at `Information`:

```
Host MailFathom.Host composed its settings over persisted configuration version 4.
```

That number is the only record of which document the process actually read — the files are in the repository and the environment is in the manifest, and what the row held at that moment is otherwise unrecoverable from the running process.

**A committed write republishes the layer, and a republish that fails changes nothing.** Republishing a later document to everything bound to it is what [a committed write](#changing-a-persisted-setting) ends with. What the path guarantees: a candidate that cannot be read leaves the deployment exactly as it was, one that reads but is not a configuration document — or carries a setting this layer may not or does not hold — is rejected *by version* with the record naming both the version that did not take and the version still serving, and a fall back to the files beneath this layer never happens — those never carried the persisted values, so reverting to them would quietly change settings the deployment had already adopted.

## Changing a persisted setting

**A setting is changed through one writer, and that writer proves the configuration before it commits.** Nothing in MailFathom assigns a configuration value in place: `configuration["key"] = value` mutates one process's copy, takes effect having been proved by nothing, and is gone at the next reload. What a change is instead is a sequence, and each step exists to keep the next from being reached with something it could not undo.

1. **Where it lands is resolved first**, against the catalog above. A path MailFathom persists nowhere is refused here, before anything is read.
2. **A candidate document is built** by applying the change to the document in force. A change names an ordinary configuration path and an ordinary configuration value, so `MailRules:Rules:1:Name` reaches the second declared rule and is written as the index-keyed property this page already asks an operator to write by hand. A change may also *remove* a setting, which is what stops the layer carrying it and lets the source beneath supply it again — writing an empty value would shadow that source instead.
3. **The complete effective configuration is composed**, with the candidate in the layer's own place and every other source where it always is — including the three that outrank it. A setting is judged as the deployment would read it rather than on its own, so a persisted value an environment variable beats is judged as the value that variable supplies.
4. **The binding and the validators a start runs are run against it**, and they are literally the same ones: the same sections, the same strict binding, the same data annotations, the same custom validators. That includes the rules a start takes *before* its container exists — which sockets would be opened, whether any surface is served at all, and whether every declared mail rule compiles — because those refuse a start exactly as a validator does. A section deliberately outside the startup gate is outside this one too.
5. **Only then does it commit**, as one statement guarded by the version the change was authored over.
6. **Only after the commit is durable does the reload token rise.** Every options snapshot that reloads then observes one coherent version, and a version a failed commit was about to take back is never published.

**Two administrators editing at once: the second write is refused rather than applied.** A change states the version it was authored over, and a change composed over a version the document has already passed is refused under `12008`, naming the version now in force. Read the configuration as it stands and decide again against it — merging the two silently is the one outcome that would lose a change nobody was told about.

**Nothing is written until everything has passed.** A refused write leaves the document, the version, and the published configuration exactly as they were, whichever step refused it, and the last valid snapshot goes on serving.

| Code | The write named | What to do |
| --- | --- | --- |
| `12006` | A setting MailFathom persists nowhere — one [the layer is itself read through](#what-it-may-not-carry), or one [another store owns](#which-store-a-setting-is-persisted-in) | Configure it where MailFathom actually reads it from |
| `12007` | A configuration that does not bind or validate — an unknown property, a segment that is not the array position it was written as, a value a validator refused, a surface that would serve nothing, a rule condition that will not compile | Correct the value. A validator refusal names every refused setting at once; a *binder* refusal — an unknown property, or a segment that is not the position it was written as — stops the pass at the first section that will not bind, so a second misspelled key is reported only once the first is corrected |
| `12011` | Changes that compose a document past the megabyte the layer composes settings from | Persist fewer settings, or remove the ones the deployment no longer configures. The size is measured as the database stores it, which is larger than the document as it was written |
| `12008` | A version the document has already passed | Read the current configuration and decide again against it |
| `12009` | Secret material where a reference belongs | Provision the secret and persist the `<scheme>:<target>` reference to it |
| `12010` | Nothing — the database refused the statement | Check the serving role's `UPDATE` privilege on `settings_root` and the database's availability; the write is safe to attempt again |

**Secret material never enters the document, whatever `Secrets:Interpretation` says.** A setting whose name announces a secret must carry a reference naming where the material is kept, so a bare password and the inline `plaintext:` scheme are both refused under `12009`. This is stricter than the rule for a file deliberately: under an inline interpretation a value in a file is material the operator put in their own file, while a write is MailFathom putting it into an unsealed `jsonb` column of its own database. The refusal names the setting and repeats neither the value nor its length, and nothing about a refused write reaches the log but its code, the version still in force, and how many settings it named.

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

This section is about the files. The persisted layer reloads on one event and no other: [a write MailFathom committed](#changing-a-persisted-setting) republishes it, and nothing watches or polls the row, so a row edited behind MailFathom's back takes effect at the next start as [the persisted layer](#the-persisted-layer) above states.

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
