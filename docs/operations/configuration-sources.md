# Configuration sources

<!-- describes: backend/src/Application/Configuration/**, backend/src/Host/Configuration/**, backend/src/Infrastructure/Persistence/Settings/**, backend/src/Infrastructure/Persistence/Users/**, backend/src/Cli/Commands/Configuration/**, backend/src/Cli/Editing/**, backend/src/Host/Hosting/Startup/ServedMailUsersStartupGate.cs, backend/src/Host/Hosting/Workers/ConfigurationConvergenceWorker.cs, backend/src/Host/Signals/ConfigurationChangeAnnouncements.cs, backend/src/Application/Access/DeploymentMailUserUnresolvedException.cs -->

MailFathom reads its settings through the ordinary .NET configuration pipeline, plus two additions. A deployment may name a directory or a file of JSON or YAML configuration that it provisioned outside the application's own content root, which is what makes a Kubernetes ConfigMap mounted as a volume ordinary configuration rather than a shape the host cannot see. And the deployment's own persisted settings — one document in PostgreSQL, composed at startup like every other source — are layered in above those files, so a setting the deployment has persisted binds and validates exactly as one that came from a file. When an edit to that document takes effect is [its own section](#the-persisted-layer) below.

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
| `ConfigurationSources:Directory` | `ConfigurationSources__Directory` | A directory whose `*.json`, `*.yaml`, and `*.yml` files are layered in |
| `ConfigurationSources:File` | `ConfigurationSources__File` | One JSON or YAML file layered above that directory |

They are read from configuration rather than from the environment directly, so the same setting arrives as an environment variable in a container, as `--ConfigurationSources:Directory=/etc/mailfathom/config` under systemd, and from `appsettings.json` during local development, without a second mechanism per deployment shape. A blank value reads as unset, because templating a manifest routinely emits an empty string for a setting the operator left alone.

The section takes those two settings and nothing else. A third one — a misspelled `Directroy`, a key from an older draft — fails startup naming it, for the same reason every other security-sensitive section in MailFathom is bound strictly: a setting that bound nothing would leave the host running on defaults while the operator believed their mount was in force.

Both settings are **restart-required**. They decide which sources exist, which is settled while the host is being composed, so repointing `ConfigurationSources:Directory` at a different mount takes effect on the next start. What the named sources *contain* is a different question, and the answer is below.

### What a directory contributes

- Files matching `*.json`, `*.yaml`, and `*.yml`, and nothing else. A `notes.txt` or a `settings.json.bak` left beside them is ignored rather than parsed.
- Ordered by the whole file name, compared ordinally, whatever format each file is written in, so the same ConfigMap layers the same way on every machine that mounts it. Later names win: `20-persistence.yaml` overrides `10-defaults.json`.
- No two names that differ only by extension. `10-mail.json` beside `10-mail.yaml` fails startup naming both, because their order would then be decided by how `.json` and `.yaml` sort rather than by a name anybody chose.
- Top-level entries only. Subdirectories are not searched.
- Entries beginning with `..` are skipped. Kubernetes updates a mounted volume by writing a new timestamped directory and repointing the `..data` symbolic link at it, which is what makes the update atomic; both entries live beside the keys and neither is configuration.

An existing but empty directory is permitted and contributes nothing. A ConfigMap with no keys is a legitimate state during a rollout, and the startup record below reports the count so the case is visible rather than silent.

### JSON and YAML

**The extension decides the format**, in a directory and for the single file alike: `.json` is read by the framework's own JSON provider, and `.yaml` or `.yml` by MailFathom's YAML reader. The single file must carry one of the three; any other extension fails startup naming the file rather than guessing. The two formats are one contract with two spellings, so a file can be rewritten from one to the other without a key changing:

- Mappings flatten to colon-delimited keys, and a sequence's elements are numbered from `0`, exactly as a JSON array's are. The [index-keyed override](#the-persisted-layer) works the same way: a mapping keyed `"1"` sets the second element and nothing else.
- An empty mapping and an empty sequence contribute what `{}` and `[]` do in JSON, and a key written with no value, `~`, or `null` is JSON's `null`.
- **Every other scalar is the text written.** YAML 1.1 would read `no` and `on` as booleans and `0x10` as sixteen; a binder reading `MailSynchronization:Enabled: no` would then see `False` while a reviewer saw a word. Nothing is reinterpreted here: the binder receives `no`, and a boolean setting written that way fails to bind, naming the key, instead of quietly taking a value. A quoted scalar is text even when it spells `null`.
- Comments are allowed and are ignored, and a file holding nothing but comments contributes nothing, like an empty ConfigMap key.

**What YAML adds beyond that is refused**, each naming the file and the line and column it was found at, and each stopping the start as a malformed JSON file does:

| Refused | Why |
| --- | --- |
| Anchors and aliases | A value defined elsewhere in the file is not the value a reviewer reads where it is used, and a diff changing the anchor changes every alias without showing any of them |
| Tags, `!!str` and custom ones alike | A tag changes how a value is read without that showing in the value |
| More than one document in a file | Which document wins would be a rule of its own; a configuration file is one document, and a second one is a second file |
| A key written twice in one mapping, compared without regard to case | JSON refuses it for the same reason: the configuration keys are case-insensitive, so the second would silently replace the first |
| A root that is not a mapping, and a mapping key that is not text | Neither names a setting |
| Nesting deeper than 64 levels | The JSON reader's own limit, so neither format admits a document the other would refuse |

A refusal reaches the log as the framework's `Failed to load configuration from file '<path>'`, carrying the reason:

```
Failed to load configuration from file '/etc/mailfathom/config/10-mail.yaml'.
 ---> System.FormatException: An alias is refused, because a value defined elsewhere in the file is not the value a reviewer reads where it is used, at line 4, column 13.
```

YAML is a format for the files a deployment provisions and nothing else. `appsettings.json`, the persisted document, and every request body stay JSON; [`--format yaml`](#editing-a-document-as-yaml) only *shows* the last two as YAML.

## The persisted layer

`settings_root` in PostgreSQL holds one row, and that row's `jsonb` document is this layer. It is read once while the host composes its configuration, flattened into ordinary colon-delimited keys by the framework's own JSON parser, and inserted at the position the table above gives. Nothing about it is a second configuration system: a persisted `MailboxSearch:SnippetsPerEmail` is the same key, bound by the same options class, and validated by the same validator as one written into a file.

**The document is sparse, and a key it does not carry is inherited rather than blank.** Persisting one setting therefore does not mean restating everything the deployment provisioned beside it. Objects compose by child key and array elements override by their own numeric index, which is the ordinary .NET provider behaviour and not a merge rule of this layer's own: a persisted value at index `1` replaces index `1`, and the indexes the document omits stay visible from the file underneath.

**Write an array element as an object keyed by its index, not as a JSON array.** The parser numbers a JSON array's elements by position from `0`, so `{ "Rules": [ { "Name": "persisted" } ] }` sets `Rules:0:Name` whatever you meant it to reach. Overriding the second rule alone is `{ "Rules": { "1": { "Name": "persisted" } } }`, which flattens to exactly the key the file underneath already has. Getting this wrong is silent — the wrong element is replaced, the intended one keeps the file's value, and nothing is logged — because both documents are valid configuration and the layer has no way to tell which element you meant.

**One read, not one query per setting.** The row is loaded as a single document snapshot, so reading a configuration property costs nothing at the database. That one statement is bounded by `Persistence:CommandTimeoutSeconds`, the same bound every other database command carries, because it runs before any endpoint is open and a server that accepts the connection and then answers nothing would otherwise hold the process at that line indefinitely with nothing able to report why.

**The document is read at startup, and every replica compares it with the row every thirty seconds.** A change made [through MailFathom](#changing-a-persisted-setting) needs no restart: the replica that committed it republishes the layer before the write answers, and every other replica republishes it within that interval, as [what reaches every replica](#what-reaches-every-replica) states. The comparison reads the row's version, so a row edited directly in the database — with `psql`, or by anything other than MailFathom — is picked up the same way only when the edit also raises `Version`; an edit that leaves it where it was changes no setting until the next start.

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

One entry exists in this release: the top-level `Accounts`, which is the collection a deployment once declared its users in. Nothing binds it any more — [the users a deployment serves](#the-users-a-deployment-serves) below is what replaced it — and the entry stays so that a write naming one of its paths is refused with the commands that record a user rather than persisted into a document nothing reads. `MailSynchronization:Accounts` carries the same word and is not this entry: it is the mailbox section a deployment once declared its own accounts in, it binds to nothing either, and a candidate carrying it is refused where every unbound key is rather than routed anywhere.

A `settings_root` document carrying `Accounts`, or anything beneath it, is therefore **refused** under error code `12005`, naming the path. It is the same choice the refusal above makes and for the same reason: a row an operator wrote by hand is a mistake, and a mistake composed with the duplicate silently dropped is one they go on believing they fixed.

**A row's document is bound at startup and after each accepted write through MailFathom, for every user the deployment holds, on every replica.** Each row holds the mail accounts and the user-level settings that are one person's own, and it is the only place either is stated: no configuration source reaches a user, so no row is left unread and none is superseded.

Binding is strict, so a property nothing binds is a refusal rather than a value dropped, and the record is then judged by every rule a mail account is declared under. The account identifier and the published name are unique *within the user*, which is the rule the document binder applies — and nothing narrows it across users, so two people may each name a mailbox `work`. What that still costs while an account's settings are resolved by the identifier alone is stated in [the users a deployment serves](#the-users-a-deployment-serves). The document may carry no secret material: a mailbox password is a `<scheme>:<target>` reference naming where the material is kept, exactly as `settings_root` requires, and a value carrying the material itself is refused. Runtime-created material is sealed in `stored_secrets` and the document carries only its `database:` reference. None of it is a configuration layer — the record shadows no deployment setting, and a value that would need to is a deployment setting written into the wrong document.

What the read does enforce is size. The row is measured by PostgreSQL in the statement that reads it, and a document past what this build binds is refused under error code `12012` rather than transferred, so a row something else wrote too large stops that request instead of the process.

MailFathom writes `settings_root` and no other store: a write naming a path the catalog routes to the user-accounts store is **refused** under error code `12006`, naming the store, because that store's document is provisioned rather than written. The settings [`settings_root` may not carry](#what-it-may-not-carry) are refused under the same code and for the reason that section gives.

### Startup, and a reload that fails

**The host fails to start when this layer cannot be read.** A database that cannot be reached, a server that carries no database of the configured name, a database that refuses the configured credential, a database whose authorization rules admit no connection for the configured role, host, and database at all, a database whose serving role holds no privilege on the table, a schema that does not carry the table, a row that is not there, and a document that is not a JSON object of configuration keys are one failure to the process: the layer between the deployment's files and the operator's overrides cannot say what it contributes, and starting anyway would serve whichever values the files beneath it happen to carry with nothing saying that a layer was missing. All eight carry error code `12003` and stop the process before any endpoint opens, and each message names which of them happened — a database the server does not carry sends the operator to the provisioning that never created it, a rejected credential to the secret block, a refused authorization to the server's own rules, and a refused privilege to the grant, rather than any of the four to the network. A statement that outran `Persistence:CommandTimeoutSeconds` carries the same code and says so as its own outcome, because the server answered everything up to it. A document that carries a key [the layer may not carry](#what-it-may-not-carry) stops the process too, under `12004` rather than `12003`, because it is a document MailFathom read perfectly well and refused, and one carrying a setting [another store owns](#which-store-a-setting-is-persisted-in) stops it under `12005` for the same reason. A database that has not had this release's migrations applied is the ordinary cause, and the message says so; [the database schema](database-schema.md) states the order a deployment applies them in.

Every start records the version it composed itself over, at `Information`:

```
Host MailFathom.Host composed its settings over persisted configuration version 4.
```

That number is the only record of which document the process actually read — the files are in the repository and the environment is in the manifest, and what the row held at that moment is otherwise unrecoverable from the running process.

**A committed write republishes the layer, and a republish that fails changes nothing.** Republishing a later document to everything bound to it is what [a committed write](#changing-a-persisted-setting) ends with. What the path guarantees: a candidate that cannot be read leaves the deployment exactly as it was, one that reads but is not a configuration document — or carries a setting this layer may not or does not hold — is rejected *by version* with the record naming both the version that did not take and the version still serving, and a fall back to the files beneath this layer never happens — those never carried the persisted values, so reverting to them would quietly change settings the deployment had already adopted.

## The users a deployment serves

Every mail account, every stored message, and every job belongs to an **user**, and `settings_accounts` holds one row per user because the mail graph's foreign key is relational rather than a predicate over a document. **A user is recorded rather than declared**: the envelope — the identifier, the label, the version, the timestamps — is the row's, and the content beside it — the settings that are theirs — is that user's own record. Their mail accounts are records of their own in `settings_mail_accounts`, assigned to them, and what a user is served is their record composed with every account assigned to them.

**No configuration source names a user, and none declares a mailbox.** Both collections that used to are gone: the top-level `Accounts` a deployment declared its users in, and `MailSynchronization:Accounts`, where it declared its own mail accounts. Neither binds, and a start that meets either stops rather than serving a roster or a mailbox its operator's file no longer describes.

### Recording a user

[`mfctl user add`](admin-endpoint.md#users-and-their-records) records one, mints their identifier, and reports it. [`mfctl account add`](admin-endpoint.md#mail-accounts-and-who-they-are-assigned-to) creates each of their mailboxes, credentials included, and assigns it to them, and [`mfctl credential create`](admin-endpoint.md#users-and-their-records) provisions a way for them to sign in. Nothing about a user is written into a file, and nothing has to be: the roster is a table an administrator maintains from a command line, and a change to it takes effect without a restart.

```sh
mfctl user add --display-name alex
mfctl account add --user <id> --from-file alex-work.json
```

The mailbox travels as a file rather than as a list of flags: a JSON object stating its `EmailAddress` and
`DisplayName` beside the keys [one account](configuration-mail.md#one-account--a-mailbox-in-a-users-record) lists.
`--user` may be left out on a deployment holding one person.

At most **256** users may be recorded. A roster that long was generated rather than provisioned, which is worth stopping for on its own.

### What a user's own record carries

`mfctl user add` provisions the record stating nothing at all, so a user is recorded and served without anybody
writing a line of it. What it can carry is one key, and everything else about how their mail is read belongs to the
mail accounts they are assigned:

| Key | Required | What it is |
| --- | --- | --- |
| `Portrait` | No | The identifier of the stored file this person is drawn by. [The portrait routes](client-endpoint.md#the-portrait-routes) write it; a record naming a file that is not a stored file of this same user is refused |

**The language is the mailbox's rather than the person's.** [The language this mailbox is read
in](configuration-mail.md#the-language-this-mailbox-is-read-in--language) holds the key, both refusals, and what an
account recorded before the property existed reads as. A user's record naming `Language` binds nothing and is refused
as a property nothing binds, exactly as any other unknown setting is.

### A configuration still declaring users

A deployment upgrading from a release that had the collection meets a refusal naming the section and what to run:

```
Accounts is no longer read: this deployment records the users it serves rather than declaring them, and nothing
imports what the collection declared. Record each of them with 'mfctl user add' and each of their mailboxes with
'mfctl account add', credentials included, then remove the section from your configuration.
```

**Nothing imports what the file declared**, and there is no route that would. The identifiers the file carried are the operator's own values rather than ones MailFathom mints, so a deployment moving across records its roster afresh and lets synchronization refill it — which is what [ADR 0014](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md) already says an operator does: *the operator's action is to provision what they had configured, and nothing imports it for them*.

### Scanning and classification are written on the mail account

**Neither `SensitiveContent` nor `SpamClassification` is a user's to state.** Both are blocks of a **mail account's**
record, one per mailbox: [scanning this account's
mail](configuration-mail.md#scanning-this-accounts-mail--sensitivecontent) holds every key of the first, its refusals,
and what stays the deployment's, and [classifying this account's
mail](configuration-mail.md#classifying-this-accounts-mail--spamclassification) the same for the second. A user's
record naming either binds nothing and is refused as a property nothing binds, exactly as any other unknown setting is:

```
The user record names 'SpamClassification', which is not a setting a user's record carries. Remove it, or correct the spelling of the setting it was meant to be.
```

**It is the mailbox rather than the person because the mail is one copy.** A verdict that files junk moves the message
on the mail server for everybody who reads that mailbox, and a redaction is written into the one derived text a message
has, so both questions are answered per account: a mailbox is classified once, under its own settings, and scanned once,
under its own posture, whoever it is assigned to. [Each account's own
posture](../features/sensitive-content-scanning.md#each-accounts-own-posture) is that rule for the scanning half, and
[spam classification](../features/spam-classification.md) for the other.

A user is still the scope of one thing, and it is what redacts a read: a search or a retrieval resolves the person
rather than the mailbox, so what is withheld from it is the strictest of the postures across the accounts that user is
assigned. That is the feature page's rule rather than a setting anybody writes.

### The identifier MailFathom mints

A user identifier is a **version 4 UUID** — deliberately unlike the version 7 identifiers the rest of persistence mints, because a user identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish when each user was created and in what order. [`mfctl user add`](admin-endpoint.md#users-and-their-records) mints it and reports it, and it is the row's from that moment: nothing an operator writes states one, and nothing changes one afterwards.

A label is applied only where nobody else holds it, because a label names one user and the column that stores it is unique. That makes two users exchanging labels two calls rather than one: free the label first — [`mfctl user rename`](admin-endpoint.md#users-and-their-records) whoever holds it — and give it to its new user afterwards.

### A deployment that records no user

**A fresh database holds no user.** A user is somebody an administrator records, so a first run serves nobody and reads no mail until `mfctl user add` records the first person and `mfctl account add` gives them a mailbox — without naming them, while they are the only one. The migration that creates the table of users writes a row of its own only where it upgrades a database already holding mail from a release before users were recorded, so that the stored mail has somebody to belong to: one user, labelled `user`, with a record stating nothing. `mfctl user rename` changes that label.

**A deployment holding no user starts, completes every startup gate, reports itself started, and serves nobody.** That is where every new deployment stands on its first start, and where one whose every user was erased returns to, and nothing about it is a failure: there is no roster to compose, no mailbox to read, and no surface answering for anybody. It says so once, at `Information`:

```
This deployment holds no user and therefore serves nobody. Record one with 'mfctl user add', then give them a mailbox with 'mfctl account add'.
```

Synchronization being switched on changes nothing about that. A deployment whose served users are assigned no mailbox has nothing to synchronize, which is **reported** rather than refused, for the same reason: it is the ordinary state of a deployment between its first start and its first mailbox.

```
Mail synchronization is switched on and no user this deployment serves is assigned a mail account, so there is nothing to synchronize. Record one with 'mfctl account add'.
```

**A user recorded at runtime is served without a restart**, mailboxes and all: a committed record or account write is published to the running roster by the write that committed it, so the next synchronization run picks the account up and every surface that replica serves answers for that user from that moment. Every other replica serves them within thirty seconds, or as soon as a [signal backplane](#what-reaches-every-replica) carries the announcement.

**A deployment whose file still carries `MailSynchronization:Accounts` does not start.** Nothing imports what the section declared, so a start that quietly ignored it would leave an operator believing mail was being read that nothing was reading. The refusal names the section and the two commands that replace it:

```
MailSynchronization:Accounts is no longer read: a mail account is a record of its own that this deployment holds and
assigns to the users it serves. Nothing imports what the section declared. Create each account with 'mfctl account add'
for a user this deployment already serves, recording anybody else with 'mfctl user add' first; then remove the section
from your configuration.
```

One bound holds while several users are served. Only one user may be served whenever an **user-facing** surface — the MCP endpoint or the client endpoint — admits a caller that names no user, because such a caller is composed against whichever user the deployment happens to hold, and a second user would leave that surface serving one person another person's mail. Every credential these two surfaces admit is a record naming the user it belongs to, whichever method presents it, so the one way a caller arrives naming nobody is a surface requiring no authentication at all. A deployment serving several with either of those surfaces in that state is refused, and the message names the correction: require a credential, or switch the surface off. **The administrative endpoint is deliberately outside that bound** — an administrator acts for the deployment rather than for a person, so a caller there is admitted for no user and every user-scoped route names the user it is for, which is what makes recording a second user something an operator can do at all.

**One mailbox is one account, whoever it serves.** An account's identifier is generated rather than typed and its address is unique across the deployment, so one mailbox is one account and one copy of its mail however many users are assigned to it. [Mail accounts and who they are assigned to](admin-endpoint.md#mail-accounts-and-who-they-are-assigned-to) holds the rules. An account holding no address is not served, and a start reports it at `Warning`:

```
The user labelled alex is assigned 1 mail accounts that hold no email address, so those mailboxes are not served. State each address with 'mfctl account edit'; 'mfctl account list' names the accounts.
```

**A mail rule naming a mailbox nobody records does not stop a start either.** A configuration write and a reload refuse one, but a mailbox can stop being served after the rule naming it was accepted — its last assignment ends, or it is erased — and a start that refused then could be undone only through the host it refused. A start reports each such claim at `Warning` instead, and the rule does nothing there until the account is assigned again or the rule is changed; [Mail rules](../features/mail-rules.md#which-accounts-a-rule-applies-to) has the rule itself:

```
A declared mail rule names something no record of a user this deployment serves provides, so the rule does nothing there until a record provides it or the rule is changed: MailRules:Rules:0:Accounts — no user this deployment serves records a mail account named '5b0c7d2e-8f41-4a7e-9c1d-2f6b3a9e4d10', so this rule would reach no mail.
```

### What a start reports

Every start records the roster, at `Information`:

```
This deployment serves 3 users, each read from their own record; no configuration source reaches anybody's mail accounts. Change them with mfctl.
```

Every user the deployment holds is served, in the order the rows were recorded in. A deployment holding none says so instead, in the line [a deployment that records no user](#a-deployment-that-records-no-user) above carries.

### A record this deployment will not read

Every record is judged before it is committed, so a stored one that no longer reads as a record was written by an older build, tightened by a newer one, edited in the database, or restored from a backup. **What such a record costs is exactly its own scope, and never anybody else's.**

| The record | What a document this build will not read costs |
| --- | --- |
| A user | That user is not served: no mailbox of theirs is synchronized and no surface answers for them. A start completes without them and serves every other user unchanged; a running replica keeps serving them from the last version that bound, and adopts the next version that does. |
| One of a user's mail accounts | That mailbox alone. The user's other mailboxes keep synchronizing, their record is published at the version the row holds, and everybody else is unaffected. |
| An organization | The prefix its members type in front of their username. Every other organization is listed, renamed, and removed exactly as before, and the members of the affected one keep every credential that is not a password scoped to it. |

**A conflict rejects the declaration that introduced it rather than both.** Two of one user's mail accounts cannot share a display name, so where a row holds two that do, the one recorded first is served and the second is what an operator is told to correct.

A refused user record and a refused mail account declaration are each recorded at `Error` by the replica that read them, naming the kind of record, its identifier, the label it carries, the version refused, and the settings to correct. A start and a convergence write the same sentence, so one search finds both:

```
A MailAccount record is held back by a document this build will not bind: 0197a3c0-0000-7000-8000-000000000001 labelled work, at version 2. It is served from the last version that bound, where there is one, and every other record is unaffected. Correct it: The user record names 'Nonsense', which is not a setting a user's record carries. Remove it, or correct the spelling of the setting it was meant to be.
```

**An unreadable organization row is logged by nothing, and is met on the administrative surface alone.** No roster binds an organization — a replica reads one when somebody asks for the listing rather than while it settles who it serves — so there is no reading of it to report, and an operator watching logs for one would wait forever. [`GET /api/admin/records/held-back`](admin-endpoint.md#records-this-deployment-will-not-read) answers all three kinds grouped by kind, and [`mfctl organization list`](admin-endpoint.md#organizations) names the organization half beneath the listing. For the two kinds above it is the second way to meet them rather than the only one.

### Which source reaches a user

**Every user is read from their own record, and no configuration source reaches any of them** — not the provisioned file, and not an environment variable or a command-line argument either. Their accounts are not configuration keys rather than merely losing precedence, so the precedence table at the top of this page has nothing to say about them. `mfctl` over the administrative port is what changes them, and a user's record is their own from the first moment [`mfctl user add`](admin-endpoint.md#users-and-their-records) records them.

**`mfctl config` never writes a record's mail accounts.** A record lives in a store of its own rather than in the deployment's document, so a change naming one of the withdrawn collection's paths is **refused** there and the refusal names how those mailboxes are actually changed:

```
MailFathom persists Accounts:0:MailAccounts:0:Host in the user-accounts store rather than in the deployment's own
document, so this is not where it is changed. A user's record is changed with 'mfctl user edit', and the mail accounts
they are served with 'mfctl account'.
```

A change naming `MailSynchronization:Accounts` is refused too, and by the binding rather than by the catalog: nothing in this release binds that section, so a candidate carrying it composes no configuration this deployment would accept.

This is the one place the page's standing claim needs reading carefully. **No file MailFathom reads is ever written back** — that still holds, and nothing here writes into anybody's file. What a file can no longer show is who this deployment serves or which mailboxes they own; the startup line above is what says so.

### Which endpoints one user is served on

A user's record carries an `EndpointAccess` block with two switches, `McpEndpoint` and `ClientEndpoint`, and a record
that states none of it reads as both on — which is every record written before the block existed.

```json
{
  "EndpointAccess": { "McpEndpoint": false, "ClientEndpoint": true }
}
```

`mfctl user endpoints` writes them one at a time and `mfctl user edit` writes them with the rest of the record; both
reach the same commit, which stores the record and copies the two switches onto the user's row in one statement. A
request is judged against that row rather than against the document, so authentication never reads a record. A user
saving their own record is refused a change to either switch; what the switches do is
[the administrative page's](admin-endpoint.md#users-and-their-records).

## Changing a persisted setting

**`mfctl config` is the surface that drives the writer, and it is the only one.** No MCP tool changes a setting, and no agent can: the commands reach the administrative endpoint under a permission of their own, and [the commands](#reading-and-changing-settings-from-mfctl) below is what an operator runs. Editing the `settings_root` row by hand still works, and is picked up without a restart only where the edit raises the row's version, for the reason [the persisted layer](#the-persisted-layer) gives; what the commands add is that the change is proved before it commits.

**A setting is changed through one writer, and that writer proves the configuration before it commits.** Nothing in MailFathom assigns a configuration value in place: `configuration["key"] = value` mutates one process's copy, takes effect having been proved by nothing, and is gone at the next reload. What a change is instead is a sequence, and each step exists to keep the next from being reached with something it could not undo.

1. **Where it lands is resolved first**, against the catalog above. A path MailFathom persists nowhere is refused here, before anything is read.
2. **A candidate document is built** by applying the change to the document in force. A change names an ordinary configuration path and an ordinary configuration value, so `MailRules:Rules:1:Name` reaches the second declared rule and is written as the index-keyed property this page already asks an operator to write by hand. A change may also *remove* a setting, which is what stops the layer carrying it and lets the source beneath supply it again — writing an empty value would shadow that source instead.
3. **The complete effective configuration is composed**, with the candidate in the layer's own place and every other source where it always is — including the three that outrank it. A setting is judged as the deployment would read it rather than on its own, so a persisted value an environment variable beats is judged as the value that variable supplies.
4. **The binding and the validators a start runs are run against it**, and they are literally the same ones: the same sections, the same strict binding, the same data annotations, the same custom validators. That includes the rules a start takes *before* its container exists — which sockets would be opened, whether any surface is served at all, and whether every declared mail rule compiles — because those refuse a start exactly as a validator does. A section deliberately outside the startup gate is outside this one too.
5. **Only then does it commit**, as one statement guarded by the version the change was authored over.
6. **Only after the commit is durable does the reload token rise.** Every options snapshot that reloads then observes one coherent version, and a version a failed commit was about to take back is never published. Two writes finish in whatever order their commits and their republishes interleave, which is not the order they committed in, so a republish carrying a version the process has already passed publishes nothing rather than stepping it back to the document it read.

Those six steps run in the replica that received the write. Every other replica takes the committed version through the same republish, within the bound [what reaches every replica](#what-reaches-every-replica) states.

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

### Editing a document as YAML

**`--format yaml` shows a document as YAML and commits it as JSON.** `mfctl config edit`, `mfctl account edit`, and `mfctl user edit` take `--format json|yaml`, and `mfctl user show` takes it too; `json` is the default. The buffer is then a `.yaml` file rendered from the JSON the deployment holds, and what was saved is converted back under the YAML 1.2 core schema before anything is sent, so a number, a boolean, and a `null` stay what they were, and a string that would read as one of them is written quoted. Anchors, aliases, tags, several documents, and a key written twice are [refused](#json-and-yaml) in the buffer as well, and so are `.inf` and `.nan`, which JSON has no number for: a refused buffer changes nothing, and the command names the reason and the path it kept the edit at, so the operator corrects it rather than retyping it. A buffer that describes the same document — only its layout or its comments differ — writes nothing, and comments are not carried back, because the document they would be carried into is JSON.

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

A single file with an extension that names neither format fails the same way, and so does a directory holding two names that [differ only by extension](#what-a-directory-contributes):

```
The configuration directory named by ConfigurationSources:Directory holds files whose names differ only by extension: 10-mail.json, 10-mail.yaml in /etc/mailfathom/config. Their order would be decided by the extension rather than by their names, so rename one of them.
```

Every start records which provisioned files were layered in, in the order they were layered, each with the format it was read in, at `Information`:

```
Host MailFathom.Host layered 2 deployment-provisioned configuration files below the environment: ["/etc/mailfathom/config/10-mail.json (Json)","/etc/mailfathom/config/20-search.yaml (Yaml)"].
```

A `0` on a deployment that mounts a ConfigMap means the mount is empty or did not arrive where the key says it did, and a file missing from the list means its extension is not one of the three.

## Reload

Most of this section is about the files. The persisted layer and the users' records reload on a committed change instead, and [what reaches every replica](#what-reaches-every-replica) at its end says how that change arrives in a replica that did not commit it.

What reloads is the **content of the files that existed when the host started**, whichever format each is written in. Each of those gets a watched provider, so a setting group classified reloadable in [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md) picks up an edited ConfigMap key without a restart, through the same validated-snapshot path every other source uses. A candidate snapshot that fails validation is rejected and the last known good one stays active.

**Adding or removing a ConfigMap key is restart-required.** The directory is enumerated once, while the host is composing itself, and each file found becomes its own provider; nothing watches the directory for membership. A key added to a mounted ConfigMap therefore produces a file no provider reads, and a key removed empties its provider rather than removing the layer. Restart the pod after changing which keys a ConfigMap holds. Editing the value inside a key that already existed needs no restart.

Two caveats decide whether even a content change actually arrives, and neither is MailFathom's to fix:

- **A `subPath` mount never updates.** The kubelet updates a mounted ConfigMap by swapping the volume's `..data` link, and a `subPath` mount bypasses that entirely — the file the container sees is the one that existed when the pod started. Mount the whole volume and use `ConfigurationSources:Directory` when reload matters; use `subPath` only where a restart on change is acceptable and say so in the deployment.
- **Change detection on a mounted volume needs polling.** `FileSystemWatcher` does not reliably observe the symbolic-link swap that an atomic update performs. Setting `DOTNET_USE_POLLING_FILE_WATCHER=1` makes the file provider poll every four seconds instead; the interval is not configurable. Microsoft documents this for container and network-share mounts generally, not only for Kubernetes.

Two further properties belong to Kubernetes rather than to the watcher: an update reaches the container after the kubelet's sync period plus its cache TTL, up to about a minute by default, and an `immutable: true` ConfigMap never updates at all.

### What reaches every replica

**A change committed through MailFathom reaches every replica within thirty seconds, without a restart.** That holds for both stores a program writes: the deployment's persisted document in `settings_root`, and each user's record in `settings_accounts` — recording a user, changing their record, and erasing them. The replica that committed the change republishes it before the write answers. Every other replica compares the version it bound with the version each row holds every thirty seconds, and republishes a newer one through the same path a write takes, so it is bound, validated, and refused by version exactly as it was on the replica that committed it. The interval is fixed rather than a setting, because it is the bound this page promises.

**Where a [signal backplane](configuration-endpoints.md#signalbackplane) is declared, a change arrives sooner.** The committing replica announces it over the backplane, and every replica listening compares the versions straight away rather than on its next interval. The announcement carries nothing — no setting, no user, no version — and losing it costs only the wait: a replica that missed one, and every replica of a deployment that declares no backplane, still converges on the interval. [ADR 0032](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md#amendment-2-a-configuration-change-is-announced-over-the-backplane) records that exception to what the backplane carries.

**A replica that cannot take a change reports it exactly as the replica that committed it would, and keeps what it bound.** A persisted document that does not bind or validate there is [rejected by version](#startup-and-a-reload-that-fails) with the same record, and the version it last bound goes on serving. A user's record that does not bind is logged at `Error` and reported on the administrative surface, naming the user's identifier, their label, the version, and the settings to correct, and that user goes on being served from the record bound before it; one mail account of theirs that does not bind is held back the same way and costs that mailbox alone, the rest of the record being republished at the version the row holds. Neither is reported again on every interval while the row stays at that version, and both stop being reported once the row is repaired. [A record this deployment will not read](#a-record-this-deployment-will-not-read) states what each kind costs. A deployment holding more users than one deployment may serve is logged at `Error` on every interval, and while it does, the replica takes no user's change at all — no newer record and no erasure — until the count is back within that bound, because a roster read short would drop whoever the reading left out. A reading that fails outright — a database out of reach — is logged at `Warning` and made again on the next interval:

```
This replica could not compare what it serves against the persisted configuration and the users' records, and reads them again in 00:00:30; what it bound stays in force.
```

**What still needs a restart:**

- **A row edited in the database without raising its version.** The comparison reads `Version`, so an edit made behind MailFathom that leaves it where it was is never seen by a running replica.
- **A setting classified restart-required**, and every setting [the persisted layer may not carry](#what-it-may-not-carry). A committed change republishes the layer, and what binds only at startup still binds only at startup.
- **Which keys a ConfigMap holds**, as the section above states.
- **A new label, on any replica.** Relabelling a user moves no record version and republishes nothing, so every replica — the one that relabelled them included — serves the label it last bound that user's record under until the record next changes or the replica restarts. The label is what an administrator selects a user by, and nothing a user is served changes with it.

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
      "MailSynchronization": { "Enabled": true }
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

No mailbox is in it, and none can be: a mail account is a record the deployment holds and assigns to the users it serves, created with `mfctl account add` against a running deployment. The reference it carries names this same mounted path, so the Secret above is still where a mailbox password lives — what moved is which artifact states the account, not where its credential is provisioned.

`Secrets:Interpretation` stays at its `ReferenceOnly` default here, so a plain-text password pasted where a reference belongs fails startup instead of authenticating. Read [secret provisioning](secret-provisioning.md#interpretation-modes) before changing it.
