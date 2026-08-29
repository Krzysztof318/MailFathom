# Configuration sources

<!-- describes: backend/src/Application/Configuration/**, backend/src/Host/Configuration/**, backend/src/Infrastructure/Persistence/Settings/**, backend/src/Infrastructure/Persistence/Owners/**, backend/src/Cli/Commands/Configuration/**, backend/src/Host/Hosting/Startup/ServedMailOwnersStartupGate.cs, backend/src/Application/Access/DeploymentMailOwnerUnresolvedException.cs -->

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

**A row's document is bound at startup and after each accepted write through MailFathom, for an owner who has taken their record over and for no other.** Each row holds the declarations and the owner-level settings that are one person's own, and [the owners a deployment serves](#the-owners-a-deployment-serves) below is which of the two sources each owner is read from and how an owner moves between them. Until an owner is adopted their document is not read at all — their mail accounts come from configuration — so a `settings_accounts` row written by hand for an owner still read from their file changes nothing and is neither judged nor refused.

Binding is strict, so a property nothing binds is a refusal rather than a value dropped, and the record is then judged by every rule a mail account is declared under. The account identifier and the published name are unique *within the owner*, which is the rule the document binder applies — but a second, deployment-wide bound narrows it, and [the owners a deployment serves](#the-owners-a-deployment-serves) states it: no two owners this deployment serves may name a mail account alike, wherever each of them is read from. A write refuses a name another owner of the settled roster already answers to; a start refuses a roster in which one name reaches two owners, which is where a collision two writes made in one process run is first visible, because each of those writes was judged against a roster the other had not moved. So `work` under two owners is refused whether it was written into a file or into two records. The document may carry no secret material: a mailbox password is a `<scheme>:<target>` reference naming where the material is kept, exactly as `settings_root` requires, and a value carrying the material itself is refused. Runtime-created material is sealed in `stored_secrets` and the document carries only its `database:` reference. None of it is a configuration layer — the record shadows no deployment setting, and a value that would need to is a deployment setting written into the wrong document.

What the read does enforce is size. The row is measured by PostgreSQL in the statement that reads it, and a document past what this build binds is refused under error code `12012` rather than transferred, so a row something else wrote too large stops that request instead of the process.

MailFathom writes `settings_root` and no other store: a write naming a path the catalog routes to the owner-accounts store is **refused** under error code `12006`, naming the store, because that store's document is provisioned rather than written. The settings [`settings_root` may not carry](#what-it-may-not-carry) are refused under the same code and for the reason that section gives.

### Startup, and a reload that fails

**The host fails to start when this layer cannot be read.** A database that cannot be reached, a server that carries no database of the configured name, a database that refuses the configured credential, a database whose authorization rules admit no connection for the configured role, host, and database at all, a database whose serving role holds no privilege on the table, a schema that does not carry the table, a row that is not there, and a document that is not a JSON object of configuration keys are one failure to the process: the layer between the deployment's files and the operator's overrides cannot say what it contributes, and starting anyway would serve whichever values the files beneath it happen to carry with nothing saying that a layer was missing. All eight carry error code `12003` and stop the process before any endpoint opens, and each message names which of them happened — a database the server does not carry sends the operator to the provisioning that never created it, a rejected credential to the secret block, a refused authorization to the server's own rules, and a refused privilege to the grant, rather than any of the four to the network. A statement that outran `Persistence:CommandTimeoutSeconds` carries the same code and says so as its own outcome, because the server answered everything up to it. A document that carries a key [the layer may not carry](#what-it-may-not-carry) stops the process too, under `12004` rather than `12003`, because it is a document MailFathom read perfectly well and refused, and one carrying a setting [another store owns](#which-store-a-setting-is-persisted-in) stops it under `12005` for the same reason. A database that has not had this release's migrations applied is the ordinary cause, and the message says so; [the database schema](database-schema.md) states the order a deployment applies them in.

Every start records the version it composed itself over, at `Information`:

```
Host MailFathom.Host composed its settings over persisted configuration version 4.
```

That number is the only record of which document the process actually read — the files are in the repository and the environment is in the manifest, and what the row held at that moment is otherwise unrecoverable from the running process.

**A committed write republishes the layer, and a republish that fails changes nothing.** Republishing a later document to everything bound to it is what [a committed write](#changing-a-persisted-setting) ends with. What the path guarantees: a candidate that cannot be read leaves the deployment exactly as it was, one that reads but is not a configuration document — or carries a setting this layer may not or does not hold — is rejected *by version* with the record naming both the version that did not take and the version still serving, and a fall back to the files beneath this layer never happens — those never carried the persisted values, so reverting to them would quietly change settings the deployment had already adopted.

## The owners a deployment serves

Every mail account, every stored message, and every job belongs to an **owner**, and `settings_accounts` holds one row per owner because the mail graph's foreign key is relational rather than a predicate over a document. What that row holds is a different question from whether it exists: the envelope — the identifier, the label, the version, the timestamps — is always the row's, and the *content* — the owner's mail accounts and the settings that are theirs — comes from configuration until that owner is explicitly handed over to their document.

**A deployment may therefore keep its whole configuration outside the database, owners included.** The rows exist so the graph resolves; the file is still the truth about what is served.

### Declaring an owner

Owners are the top-level `Accounts` collection. It is **not** `MailSynchronization:Accounts`, which is the deployment's own mailbox section and is [described elsewhere](configuration-mail.md); the two carry the same word and are different collections.

```json
{
  "Accounts": [
    {
      "Id": "3f2b8c14-6d5a-4e9f-8b70-1c2d3e4f5a60",
      "DisplayName": "alex",
      "MailAccounts": [
        {
          "AccountId": "alex-work",
          "DisplayName": "Alex at work",
          "Host": "imap.example.test",
          "UserName": "alex@example.test",
          "Secrets": { "Password": { "SecretReference": "file:/run/secrets/alex-work-password" } }
        }
      ]
    }
  ]
}
```

| Key | Required | What it is |
| --- | --- | --- |
| `Accounts:<n>:Id` | Yes | The identifier every mail account, every stored message, and every job of this owner hangs on, written as a UUID |
| `Accounts:<n>:DisplayName` | Yes | The label an administrator tells owners apart by, at most 128 characters and unique across the deployment |
| `Accounts:<n>:MailAccounts` | No | The mail accounts this owner owns, each declared exactly as one in `MailSynchronization:Accounts` is |

An owner declaring no mailbox is an ordinary state rather than an unfinished one: an owner exists before their first mailbox does, and one whose last mailbox is withdrawn is still an owner. Binding is strict, so a property nothing binds — a `DisplayNames` where `DisplayName` belongs — fails the start naming it rather than leaving the host running on a default.

At most **256** owners may be declared. A file past that was generated rather than written, which is worth stopping for on its own.

### The identifier is yours to generate

**MailFathom does not invent it for a *declared* owner.** Nothing in a file could derive an identifier that is the same across restarts and across replicas, and one invented per start would attach a deployment's stored mail to a person who existed for one process. So the operator states it, and it is a **version 4 UUID** — deliberately unlike the version 7 identifiers the rest of persistence mints, because an owner identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish when each owner was created and in what order.

[`mfctl owner add`](admin-endpoint.md#owners-and-their-records) mints one under exactly that rule, and reports it, because an owner recorded through the administrative endpoint is recorded once against a database that will keep it. An owner recorded that way is declared in no file and is read from their own record from the start; there is no identifier for an operator to state and none to keep in step.

Produce one with whatever is already on the machine:

```sh
uuidgen                                   # util-linux, macOS
python3 -c 'import uuid; print(uuid.uuid4())'
cat /proc/sys/kernel/random/uuid          # Linux, no tools at all
```

A value that is not a well-formed UUID stops the start, and so does the all-zero UUID, which is what a template emits for a field nobody filled in and which names nobody. Two owners declared under one identifier stop it too: an identifier names one person, and everything either of them owned would be recorded against the same row.

**Never change it afterwards.** A declaration whose identifier has moved for an owner the database already holds under that label stops the start naming them, rather than orphaning every message that hangs on the old value. Restore the identifier the deployment holds, and rename the owner instead if the label is what you meant to change — a changed label is applied to the row and is not a refusal. [`mfctl owner rename`](admin-endpoint.md#owners-and-their-records) changes one over the endpoint, and is what renames an owner nothing declares; for a declared owner the file is still where the label is decided, because every start puts the declared one back.

A label is applied only where nobody else holds it. A label declared for one owner while another owner the deployment holds still carries it stops the start too, because a label names one owner and the column that stores it is unique. That makes two owners exchanging labels two starts rather than one: free the label in the first — relabel or remove whoever holds it — and declare it for its new owner in the second.

### A deployment that declares no owner

Today's shape keeps working and **no file has to change**. A deployment that declares no owner at all serves exactly one: the row the release's migration provisioned, or — where the deployment holds none — one identifier generated once and recorded, reported at `Information`:

```
This deployment declared no owner and held none, so one has been recorded for the mail accounts it is configured with.
```

Every account in `MailSynchronization:Accounts` belongs to that sole owner. Once owners *are* declared there is no sole owner for that section's accounts to belong to, so declaring both is **refused**: move each of those accounts under the owner who owns it, as an entry of that owner's `MailAccounts`.

The same section is **refused** once nobody reads it. An adoption copies the accounts into the owner's own record and leaves the section where it was. The published owner document takes precedence in the replica that handled the write immediately; clear `MailSynchronization:Accounts` afterwards so the next start does not meet an account section that belongs to nobody. A start that meets it refuses and names the section.

Two further bounds hold while owners are declared. Only one owner may be served whenever an **owner-facing** surface — the MCP endpoint or the client endpoint — admits a caller that names no owner, because such a caller is composed against whichever owner the deployment happens to hold, and a second owner would leave that surface serving one person another person's mail. Every credential these two surfaces admit is a record naming the owner it belongs to, whichever method presents it, so the one way a caller arrives naming nobody is a surface requiring no authentication at all. A deployment serving several with either of those surfaces in that state is refused, and the message names the correction: require a credential, or switch the surface off. **The administrative endpoint is deliberately outside that bound** — an administrator acts for the deployment rather than for a person, so a caller there is admitted for no owner and every owner-scoped route names the owner it is for, which is what makes recording a second owner something an operator can do at all. And no two owners this deployment serves may name a mail account alike — this release resolves an account's settings by its identifier alone, so a name two owners shared would reach whichever declaration the lookup met first. Give each mailbox a name no other owner uses. The bound holds over the whole roster rather than over the file: a start reads every served owner's mail accounts, from their declaration or from their own record, and refuses a start in which one name reaches two of them, naming the names to change.

### What a start reports

Every start records the roster, at `Information`:

```
This deployment serves 3 owners: 2 read from configuration and 1 from their own document.
```

and then one line per owner whose source is not their file:

```
The owner labelled morgan is read from their own document; no configuration source reaches their mail accounts. Change them with mfctl.
```

An owner the database holds and no file declares is served from their own record where they have one, which is what an owner recorded through [`mfctl owner add`](admin-endpoint.md#owners-and-their-records) always has: nothing in a file reaches them, and a deployment that held a row it never served would be one where recording somebody did nothing. They are served after every owner a file names, because the roster's order is the operator's own reading of their configuration and an owner outside it has no place in that order to take.

An owner the database holds, no file declares, and who has **no record of their own** is **neither deleted nor stripped of their mail**. They stop being served — their mail is kept, and neither read nor refreshed — and the start says so at `Warning`:

```
The owner labelled sam is held by this deployment and declared nowhere, so they are not served. Their mail is kept and neither read nor refreshed; removing them is an explicit act through mfctl.
```

### The handover, and what it costs

**The handover is per owner and never happens by itself.** A start reads each row's runtime-written marker and serves that owner from whichever source it names — their declaration while the marker is unset, their document once it is set. Nothing in a start sets it: no upgrade, no import, and no first start adopts anybody. What sets it is [`mfctl owner adopt`](admin-endpoint.md#owners-and-their-records), which an operator runs for one owner at a time, having been shown what it would move and having said yes. An owner recorded through `mfctl owner add` was never read from a file and is read from their own record from the start.

**`mfctl config` never writes an owner's mail accounts, adopted or not.** They live in a store of their own rather than in the deployment's document, so a change naming one is **refused** there and the refusal names both ways they are actually changed:

```
MailFathom persists Accounts:0:MailAccounts:0:Host in the owner-accounts store rather than in the deployment's own
document, so this is not where it is changed. An owner still read from a configuration source is changed in the
declaration that supplies them — the owner's own section of the top-level Accounts collection — and served from it at
the next restart; an owner who has been adopted is changed with 'mfctl owner account add' and 'mfctl owner account
remove'.
```

The owner routes are the ones that do write that store, and until an owner is adopted **they refuse too** — through the administrative record routes and through the client's own alike — because a write against an empty document would silently drop every mailbox the file was supplying. That refusal names `mfctl owner adopt`, which is the one act that moves them.

**Once an owner is adopted the change is permanent for them, and no configuration source reaches their mail accounts at all** — not the provisioned file, and not an environment variable or a command-line argument either. Those accounts have stopped being configuration keys rather than merely losing precedence, so the precedence table at the top of this page has nothing to say about them. `mfctl` over the administrative port is what changes them afterwards, and what repairs a deployment whose file no longer reaches an owner it used to.

This is the one place the page's standing claim needs reading carefully. **No file MailFathom reads is ever written back** — that still holds, and adoption writes nothing into anybody's file. What it does is stop MailFathom reading one owner's section out of it, which the file itself cannot show; the startup line naming that owner is what says so, and it is worth reading after any adoption.

## Changing a persisted setting

**`mfctl config` is the surface that drives the writer, and it is the only one.** No MCP tool changes a setting, and no agent can: the commands reach the administrative endpoint under a permission of their own, and [the commands](#reading-and-changing-settings-from-mfctl) below is what an operator runs. Editing the `settings_root` row by hand still works and still needs a restart, for the reason [the persisted layer](#the-persisted-layer) gives; what the commands add is that the change is proved before it commits and takes effect without one.

**A setting is changed through one writer, and that writer proves the configuration before it commits.** Nothing in MailFathom assigns a configuration value in place: `configuration["key"] = value` mutates one process's copy, takes effect having been proved by nothing, and is gone at the next reload. What a change is instead is a sequence, and each step exists to keep the next from being reached with something it could not undo.

1. **Where it lands is resolved first**, against the catalog above. A path MailFathom persists nowhere is refused here, before anything is read.
2. **A candidate document is built** by applying the change to the document in force. A change names an ordinary configuration path and an ordinary configuration value, so `MailRules:Rules:1:Name` reaches the second declared rule and is written as the index-keyed property this page already asks an operator to write by hand. A change may also *remove* a setting, which is what stops the layer carrying it and lets the source beneath supply it again — writing an empty value would shadow that source instead.
3. **The complete effective configuration is composed**, with the candidate in the layer's own place and every other source where it always is — including the three that outrank it. A setting is judged as the deployment would read it rather than on its own, so a persisted value an environment variable beats is judged as the value that variable supplies.
4. **The binding and the validators a start runs are run against it**, and they are literally the same ones: the same sections, the same strict binding, the same data annotations, the same custom validators. That includes the rules a start takes *before* its container exists — which sockets would be opened, whether any surface is served at all, and whether every declared mail rule compiles — because those refuse a start exactly as a validator does. A section deliberately outside the startup gate is outside this one too.
5. **Only then does it commit**, as one statement guarded by the version the change was authored over.
6. **Only after the commit is durable does the reload token rise.** Every options snapshot that reloads then observes one coherent version, and a version a failed commit was about to take back is never published. Two writes finish in whatever order their commits and their republishes interleave, which is not the order they committed in, so a republish carrying a version the process has already passed publishes nothing rather than stepping it back to the document it read.

**Two administrators editing at once: the second write is refused rather than applied.** A change states the version it was authored over, and a change composed over a version the document has already passed is refused under `12008`, naming the version now in force. Read the configuration as it stands and decide again against it — merging the two silently is the one outcome that would lose a change nobody was told about.

**Nothing is written until everything has passed.** A refused write leaves the document, the version, and the published configuration exactly as they were, whichever step refused it, and the last valid snapshot goes on serving.

**A change the boundary itself will not accept is a caller's mistake rather than one of the codes below.** A write states at least one change and at most a thousand, each naming a path of at most 512 characters with no empty segment and, where it sets a value, a value of at most 8 KiB. Neither half may carry a NUL character, and that is refused where the change is stated rather than at the commit because PostgreSQL text holds no NUL at all — a segment becomes a property name and a key is text exactly as a value is, so a document composed from one would compose, validate, and then be refused by the server on every attempt, with nothing but a state number to say which change did it.

**Two failures are the exception, and they are the two that share one cause: the statement had already been sent.** A statement that outran `Persistence:CommandTimeoutSeconds` was accepted by the server, which then stopped answering, so whether the commit applied is not known to the process that issued it; a connection lost while the statement was in flight says exactly as little, because the server may have applied and committed it before the socket died. A database that could not be reached at all is neither of them, even though it expires the same way and breaks the same way: nothing was sent, so the row certainly stood still, and the message says so. The version now in force is what settles both: read the configuration, and attempt the write again over the version it was composed on — the version guard refuses the retry if the first attempt did commit, so no change is applied twice.

| Code | The write named | What to do |
| --- | --- | --- |
| `12006` | A setting MailFathom persists nowhere — one [the layer is itself read through](#what-it-may-not-carry), or one [another store owns](#which-store-a-setting-is-persisted-in) | Configure it where MailFathom actually reads it from |
| `12007` | A configuration that does not bind or validate — an unknown property, a segment that is not the array position it was written as, a value a validator refused, a surface that would serve nothing, a rule condition that will not compile, a resilience section naming no outbound dependency class | Correct the value. A validator refusal names every refused setting at once; a *binder* refusal — an unknown property, or a segment that is not the position it was written as — stops the pass at the first section that will not bind, so a second misspelled key is reported only once the first is corrected |
| `12011` | Changes that compose a document past the megabyte the layer composes settings from | Persist fewer settings, or remove the ones the deployment no longer configures. The size is measured as the database stores it, which is larger than the document as it was written |
| `12008` | A version the document has already passed | Read the current configuration and decide again against it |
| `12013` | A setting a source above the persisted layer already supplies, so the write would commit and change nothing this deployment reads | Change the value where it is actually decided — the refusal names the source. Where the persisted value is being staged beneath an override about to be removed, state that with `--even-if-shadowed` |
| `12009` | Secret material where a reference belongs | Provision the secret and persist the `<scheme>:<target>` reference to it |
| `12010` | Nothing, or nothing that is known — the statement did not commit | The message names which: a refused privilege sends you to the `UPDATE` grant on `settings_root`, a read-only session to the connection, a refused credential to the secret block, and a server that could not be reached at all to the network. A state the message names verbatim is a server that answered and refused the statement, so what to correct is the statement's own subject rather than the connection. A command timeout and a connection lost while the statement was in flight are the two that cannot say the row stood still — read the version now in force, then retry over the version the write was composed on |

**Secret material never enters the document, whatever `Secrets:Interpretation` says.** A setting whose name announces a secret must carry a reference naming where the material is kept, so a bare password and the inline `plaintext:` scheme are both refused under `12009`. This is stricter than the rule for a file deliberately: under an inline interpretation a value in a file is material the operator put in their own file, while a write is MailFathom putting it into an unsealed `jsonb` column of its own database. The refusal names the setting and repeats neither the value nor its length, and nothing about a refused write reaches the log but its code, the version still in force, and how many settings it named.

## Reading and changing settings from mfctl

Six commands, all under `mfctl config`, all reaching [the administrative endpoint](admin-endpoint.md). Reading is published under `mailfathom.admin.read`; every one of the four that writes is published under **`mailfathom.admin.configuration.write`**, which is a name of its own rather than a route under the operating one. A persisted setting decides what the deployment *is* rather than what it does next: the same write that corrects a search bound can widen a credential's grant or repoint a model provider, so a credential granted the ordinary operating work must not thereby be able to redefine the deployment. [Permissions](permissions.md) is the vocabulary.

| Command | Answers |
| --- | --- |
| `mfctl config get <path>` | One setting as the deployment reads it, and which layer decided it |
| `mfctl config show [prefix]` | A section as a tree, each leaf carrying its value and its source |
| `mfctl config set <path> <value>` | Persists one setting |
| `mfctl config unset <path>` | Stops the document carrying one setting, so the source beneath decides it again |
| `mfctl config edit` | Opens the persisted document in `$VISUAL` or `$EDITOR` and commits what was saved, as one change |
| `mfctl config adopt <prefix>` | Copies what the deployment's files decide beneath a path into the persisted document |

**Every reading names the source, and the source is half the answer.** A deployment composes its settings from files, this layer, and the three sources an operator reaches for when something is wrong, so "what does this setting say" and "where would I change it" are one question. A reading reports `command-line`, `environment-variable`, `user-secrets`, `persisted-layer`, or `file` — and for a file, which file:

```
$ mfctl config get MailboxSearch:SnippetsPerEmail
Setting: MailboxSearch:SnippetsPerEmail
Value:   3
Source:  file (10-deployment.json)
```

**A write to a setting an outranking source supplies is refused rather than committed.** The refusal is `12013` and it exists because such a write would succeed, spend a version, and change nothing the deployment reads — which reads as a setting that will not take. What to do is almost always to change the value where it is decided; the exception is staging a value beneath an override that is about to be removed, which is stated with `--even-if-shadowed` and is the only case in which persisting a value nothing currently reads is right. The flag is stated on `set`, `unset`, `edit`, and `adopt` alike, and the deployment rather than the command is what applies it.

**A secret-bearing setting reads back as `(redacted)` everywhere, and the rule is the one a write is refused by.** A value a write would refuse to persist as material must not be a value a read hands back, so `get`, `show`, and the editing buffer all replace it. That covers both halves of what a write refuses rather than only the first: a setting whose name announces a secret, and a setting on the [bootstrap-only list](#what-it-may-not-carry) the persisted layer is itself reached through — `ConnectionStrings:mailfathom` above all, an orchestrator-injected connection string whose password a deployment may have written inline under a key no naming rule recognizes. A reading enumerates every key the deployment composed, so without the second half the weaker of the two permissions this surface publishes would hand back the database credential. The marker carries no colon, so it is a reference to no scheme: left in a buffer that is saved, it leaves the setting exactly as it was, and written into a secret-bearing setting by a keyed change it is refused under `12009` as material rather than persisted as something that looks deliberate. What a persisted secret-bearing setting holds is still the `<scheme>:<target>` reference — never material — which is what [secret provisioning](secret-provisioning.md) puts in place. A reading withholds one further class of value, by a rule about names rather than about secrets: the framework composes an unprefixed environment provider, so every variable of the host process is a configuration path the deployment technically carries — and a value one of them supplies at a path no MailFathom section names reports the marker too, with the path and the source still named. Those are a neighbouring process's business rather than this deployment's settings, and a naming rule knows nothing about a name this project never chose. A variable that *does* name a MailFathom setting — `MailboxSearch__SnippetsPerEmail`, and every override an operator writes on purpose — is reported in full like any other value.

**`mfctl config edit` is one transaction over the document.** `set` and `unset` each name one path, so a change spanning half a section is a run of commands each committing a version of its own, every intermediate one a configuration the deployment briefly ran on. The editing session fetches the document with its version, opens it, and commits what was saved against that version, so it is accepted whole or refused whole. Three things the buffer is not: it is not the deployment's whole configuration, because the layer is sparse and what is absent is inherited; it carries no secret material, for the reason above; and it is not a file the deployment reads — nothing here edits a configuration file, and what was saved is committed through the same writer every other change goes through. An emptied buffer abandons the session and a buffer saved unchanged writes nothing; both are reported as what they are. A session refused under `12008` is told which settings differ between the version it was opened over and the version now in force, so the operator can decide again against it — and nothing of the abandoned session is applied on top, because merging two edits neither author saw is the outcome the version guard exists to prevent. One further refusal belongs to the marker: it stands for whatever the document held at the path it was saved at, so a save that changed what stands around it is refused under `12007` rather than committed. What the marker is judged against is the block the secret belongs to — the mail account, the model provider, whatever the credential is presented to — and everything at or beneath that block has to be saved as the buffer was opened. Two things that block catches. An array position moves: deleting the first of two mail accounts leaves the second one's marker standing where the first one's stood, so a save adding or removing an element of a secret-bearing array is refused naming the element. And a credential can be repointed without moving at all: a save changing an account's host, or a model provider's address, while leaving its credential at the marker would present the provisioned material to whatever was written there, so that save is refused too, naming the block. Neither is a dead end — changing a neighbouring setting of a secret-bearing block is done with `mfctl config set`, which names one path and never rewrites a reference.

**`mfctl config adopt` is the one thing in MailFathom that moves a decision out of a file and into the database.** No upgrade, no import, and no first start does it, so a deployment that never runs it keeps its files as the whole truth about its own configuration — which is what makes a committed ConfigMap reviewable as the thing actually in force. It is previewed and then confirmed because of what it costs afterwards: the settings it copies stop being decided by the files, and editing the file one came from no longer changes what the deployment does. The preview names every setting and the file behind it, which is the moment to notice that a path covers more than was meant; `--yes` states the agreement where nobody is at the terminal. A setting the persisted layer already carries is not offered, because adopting it would replace a value somebody persisted deliberately with the file's — changing a persisted value is what `set` is for. A setting on the [bootstrap-only list](#what-it-may-not-carry) is not offered either, for the stronger reason that the commit behind the preview refuses it: those settings are how the layer is reached and are not the layer's to carry, so `mfctl config adopt Persistence` previews what the files decide beneath it *except* those. `mfctl config unset` is what gives a setting back to its file.

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
