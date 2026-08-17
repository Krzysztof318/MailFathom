# Administering a deployment

<!-- describes: src/Host/Configuration/Endpoints/AdminEndpointOptions.cs, src/Host/Api/Admin*.cs, src/Host/Api/Contact*.cs, src/Host/Api/Embedding*.cs, src/Host/Api/Job*.cs, src/Host/Api/Mail*.cs, src/Host/Api/Spam*.cs, src/Host/Hosting/Startup/SurfaceIsolation.cs, src/Host/Hosting/Warnings/AdminTransportSecurityWarning.cs, src/Host/Hosting/Warnings/TransportGrantStartupReport.cs, src/Domain/Access/MailFathomPermission.cs, src/Host/Security/Endpoints/TransportListenerBinder.cs, src/Host/Security/Transport/TransportRateLimiting.cs, src/Cli/**, scripts/install-mfctl.sh -->

How the `mfctl` command reaches a running deployment, and what that deployment has to have enabled before it will
answer.

MailFathom is administered over HTTP. The command never reads the service's configuration, never opens its database, and
never touches its secret store — every operation it performs is a request to the administrative endpoint. That is what
lets it run on your own machine, on Linux or Windows, against a deployment running somewhere else entirely.

## The endpoint is off unless you turn it on

A deployment that configures nothing serves no administrative surface. Enabling it opens a listener of its own:

```jsonc
{
  "AdminEndpoint": {
    "Enabled": true,
    "BindAddress": "127.0.0.1",
    "Port": 8090,
    "Authentication": [
      { "ApiKey": { "Name": "workstation", "SecretReference": "systemd-credential:admin-workstation-key" } }
    ]
  }
}
```

**The listener is its own, and that is the point.** Administrative routes answer on the administrative listener and
nowhere else, and nothing else answers on it — a request for `/mcp` that arrives on the administrative port is refused
before it reaches the protocol surface, and a request for `/api/admin` that arrives on the MCP port is refused before it
reaches any credential check. Both are answered `404`, because the honest answer is that nothing is served there.

A port another listener in this process already binds fails startup naming the section, rather than failing later with
an address-in-use error that names a socket.

## Credentials do not cross surfaces

An API key configured under `McpEndpoint` authenticates nothing here, and one configured here authenticates nothing
there. Reading a mailbox and administering the service that reads it are different authorities, and the separation is
mechanical rather than conventional: each endpoint registers its own authentication schemes and its own authorization
policy, and a policy consults only its own schemes.

`Authentication` takes the same entries `McpEndpoint:Authentication` takes — one entry per credential, each carrying an
`ApiKey` block, a `PublicKey` block, an `OAuth` block, or any combination of them — and every one of them is this
endpoint's own. A misspelled key fails startup rather than binding a default. Each method is documented once, under
[the MCP endpoint](mcp-endpoint.md#authentication): what a key is, what a
[key pair](mcp-endpoint.md#key-pairs) is and what a client signs to present one, and what a token must prove. The
difference here is the audience an assertion names — `urn:mailfathom:admin` rather than `urn:mailfathom:mcp` — which is
what keeps a credential minted to read a mailbox from administering the service even where one client is registered on
both.

**With an `OAuth` entry configured, every one of them must name a `Resource` ending in `/api/admin`** — the path these routes answer
beneath. Startup refuses anything else, naming the setting. The reason is discovery rather than OAuth: `mfctl` is handed
a host and a port and finds the metadata document by appending that prefix, which reaches the document's RFC 9728
location exactly when the resource names the same one. A deployment whose resource said something else would publish a
document nothing could find, and OAuth sign-in would be unreachable for a reason no refusal would explain. Behind a
reverse proxy, write the public URL and keep the path: `https://mail.example.test/api/admin`.

> **Every authenticated caller may still perform every administrative operation.** A grant can now be written on an
> entry — see below — and nothing on this surface varies by it yet, so the credential remains what bounds access.
> Provision one per client and rotate it like any other secret.
>
> Weigh that against what the operations are. The endpoint serves reads — who a credential makes the caller, two
> records of what a mailbox has had done to it and what has been read from it, where semantic search stands, and what
> synchronization is doing — and
> writes that store a mailbox refresh token, start a provider bill, and erase what a folder has stored. Any credential
> that can do the first can therefore do all of them, so an administrative key is as sensitive as the mailbox
> credentials it can place, the histories it can read, the spend it can begin, and the mail it can dispose of.

## What a credential may do

Each entry states what the credentials it admits may do, as `Permissions`. This surface's half of the published set is
six names, allocated so that the separations an operator would plausibly want to make are the ones they can:

| Permission | What it covers |
| --- | --- |
| `mailfathom.admin.read` | The reads reporting the deployment's own state and no mail: what synchronization is doing per account and per folder, embedding status and the activation preview, the loaded rules, a run's progress, the stopped-job list |
| `mailfathom.admin.audit.read` | The per-account records derived from mail: the mailbox-mutation audit, the answering audit, the rules history, the spam classifications |
| `mailfathom.admin.operate` | Asking the deployment to do work it can already do: running rules over an account, classifying an account, retrying or dropping a stopped job, cancelling a reindex |
| `mailfathom.admin.credentials.write` | Storing a mailbox refresh token |
| `mailfathom.admin.spend` | Activating the declared embedding model, which is the one operation that starts a provider bill |
| `mailfathom.admin.erase` | Erasing the mail stored for a folder an account no longer mirrors |

No permission implies another, so a credential that needs to read state and to run rules is granted both names. A name
nothing publishes fails startup, naming the entry and the position in the list, so a misspelling is refused rather than
read as a narrower grant than you meant; so is a name the same grant already carries. A `mailfathom.mail.*` name is
refused for a second reason as well — it belongs to the MCP surface and would grant nothing on this one.

`GET /api/admin/session` sits outside the model and needs no permission. It reports the credential the caller already
presented and the version this deployment already publishes, so it discloses nothing a caller did not bring — and it is
what every command reads first, `mfctl login` included. Requiring a permission for it would make that permission a
component of every administrative grant.

The rest of the reading is the same on both surfaces and is written once, under
[what a credential may do](mcp-endpoint.md#what-a-credential-may-do): the grant belongs to the entry rather than to the
block inside it, an absent `Permissions` key leaves the entry holding everything this surface publishes while
`Permissions: []` grants nothing, an endpoint with no entry at all grants everything to every caller it serves, and
`PermissionsFromTokenScopes` turns the list into a ceiling a token's own scopes narrow.

Startup records what every entry resolved to, one line per entry, and names the ones that wrote no grant:

```text
info: MailFathom.Host.Hosting.Warnings.TransportGrantStartupReport
      The administrative endpoint entry AdminEndpoint:Authentication:0 grants mailfathom.admin.read,
      mailfathom.admin.operate to every credential it admits. No route here consults a permission yet, so a grant on
      this surface states what a credential is meant to reach rather than what it currently reaches.
```

Every line closes with what a grant on that surface does, which is not the same on both: the MCP endpoint's lines say
that a caller is served only the tools its grant permits, because there it is enforced. Nothing in those lines names a
key, a public key, a token, an authorization server, or a subject: a grant is what the deployment wrote, never who
presented something.

**No name covers the contact book.** The six above were allocated against the routes that existed when they were, and
the book's own routes — reading it, writing to it, exporting a person, erasing one — fall under none of them. Nothing
follows from that today, because of the paragraph below; what it means is that a grant narrowing this surface does not
narrow the book, and allocating names for it is a decision about the published permission set rather than about these
routes.

**Nothing on this surface varies by the grant today.** The permissions are read, validated, carried on the authenticated
caller, and reported at startup; every route still serves any authenticated caller, which is what the note above says.

## What the endpoint serves

| Route | What it does |
| --- | --- |
| `GET /api/admin/session` | Reports the credential that authenticated and the running version. `login` and `status` report what it answers; every other command reads it first to [check the two versions against each other](#take-the-command-from-the-deployments-own-release-line). |
| `POST /api/admin/mailbox/refresh-token` | Stores a mailbox refresh token for one configured account, sealed under the deployment's data-encryption key. This is what [`mfctl mailbox authorize --account`](mailbox-oauth.md#sending-the-token-to-the-deployment) sends. |
| `GET /api/admin/mailbox/synchronization` | Reports what synchronization is doing, per account and per mapped folder. This is what [`mfctl mailbox status`](#reading-what-synchronization-is-doing) asks. |
| `GET /api/admin/mailbox/rewind` | Reports how much mail discarding an account's synchronization progress would have fetched again, discarding nothing. |
| `POST /api/admin/mailbox/rewind` | Discards it, so the next runs read the scope's folders from the start of the account's window. **This is the one route that makes a deployment pull a mailbox over IMAP again.** |
| `POST /api/admin/mailbox/rederivation` | Re-reads one bounded pass of the raw MIME already stored, into the properties a newer release records from it. Opens no mailbox session. |
| `GET /api/admin/mailbox/mutations/audit` | Reads one account's record of the changes MailFathom made to its mailbox, where that account [keeps one](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default). |
| `GET /api/admin/answering/audit` | Reads one account's record of the questions answered from its mailbox, where that account [keeps one](../features/mail-answering.md#an-account-can-keep-a-record-of-what-a-question-read-and-none-does-by-default). |
| `GET /api/admin/embeddings` | Reports whether semantic search is working and how far behind it is. This is what [`mfctl embedding status`](#administering-the-embedding-profile) asks. |
| `GET /api/admin/embeddings/activation` | Reports what activating the declared model would do and what it would cost, writing nothing. |
| `POST /api/admin/embeddings/activation` | Takes up the declared model and begins embedding under it. **This is the one route that starts a provider bill.** |
| `POST /api/admin/embeddings/reindex/cancellation` | Stops the reindex under way, leaving the generation that is serving where it is. |
| `GET /api/admin/rules` | Reports the [mail rules](../features/mail-rules.md) this deployment has loaded, in the order they run, and whether the configuration as it now stands is the one they were read from. |
| `POST /api/admin/rules/runs` | Asks for one account's rules to be run over every message already stored for it, and answers with the run already under way where there is one. |
| `GET /api/admin/rules/runs` | Reports where that run has got to, or how the last one ended. |
| `GET /api/admin/rules/history` | Reads one account's record of what its rules concluded and what those conclusions asked for. |
| `POST /api/admin/spam/runs` | Asks for every message already stored for one account to be [classified](../features/spam-classification.md), and answers with the run already under way where there is one. It is a dry run unless the body asks to apply. |
| `GET /api/admin/spam/runs` | Reports where that run has got to, or how the last one ended. |
| `GET /api/admin/spam/classifications` | Reads one account's classifications, newest first, and the changes each verdict asked the mailbox for. |
| `GET /api/admin/jobs/dead-letters` | Reads the background work that stopped and will not be attempted again, newest first, with what ended each piece of it. |
| `POST /api/admin/jobs/dead-letters/retry` | Returns one stopped job to the queue under the identity it was enqueued with. |
| `POST /api/admin/jobs/dead-letters/drop` | Decides one stopped job will never run, keeping the record of it. |
| `POST /api/admin/folders/erasure` | Erases one bounded pass of the mail stored for a folder the account no longer mirrors. **This is the one route that disposes of mail.** |
| `GET /api/admin/contacts` | Reads one bounded, keyset-paginated page of the [contact book](../features/contacts.md), optionally narrowed to one origin. |
| `POST /api/admin/contacts` | Records a person the book does not yet hold, as a contact this deployment's owner asserted. |
| `GET /api/admin/contacts/by-address` | Reads whoever uses one address, in whichever casing the book recorded it. |
| `GET /api/admin/contacts/{id}` | Reads one contact by the identity the book gave it. |
| `PUT /api/admin/contacts/{id}` | Amends one contact to the whole record the body states. |
| `POST /api/admin/contacts/{id}/promotion` | Takes on a contact the deployment collected, so it becomes one the owner asserted. |
| `DELETE /api/admin/contacts/{id}` | Erases one person and everything the book derived from them. **This is the one route that disposes of a contact, and it cannot be undone.** |
| `GET /api/admin/contacts/{id}/export` | Produces everything the book holds about one person, as of the instant it was taken. |

The write route's body carries a long-lived credential for a named mailbox owner, which is what makes the clear-text
warning below matter more here than it does for a session probe. It refuses, with `400` and a sentence naming what was
wrong, an account this deployment does not configure and a body missing either field; a second grant for the same
account replaces the first rather than adding to it. It reads at most 16 KB, which is far more than any authorization
server's refresh token and far less than the server's own default. It answers with no body at all, so nothing it stores
can be read back out through it.

Storing seals the token under the deployment's [data-encryption key](secret-provisioning.md). A deployment that
configures no key ring cannot store one, and the route answers `500` rather than a refusal it can explain, because
nothing about the request was wrong.

### Reading what synchronization is doing

`mfctl mailbox status` is the command to run when mail is not arriving. Nothing else a deployment ships answers that
question: a run that is failing, backing off, or standing still on one folder is visible in telemetry and in the log,
and without a metrics stack it reaches you as a mailbox that looks empty rather than as a worker that has stopped.

```console
$ mfctl mailbox status
Deployment:       production (https://mail.example.test:8443)
Synchronization:  on

Account:   work
Phase:     waiting; next run due at 2026-08-15 12:20:00Z
Backoff:   4 runs failed in a row, which is what the wait above was grown from
Last run:  failed at 2026-08-15 11:55:00Z; 1 of 2 folders failed

Folder   Progress                                                         Last run
INBOX    UID 12,410 in UIDVALIDITY 3, last moved at 2026-08-15 11:55:00Z  synchronized at 2026-08-15 11:55:00Z; stored 0, 0 oversized, 0 unreadable, more to fetch: False
archive  UID 6,997 in UIDVALIDITY 3, last moved at 2026-08-14 09:00:00Z   at 2026-08-15 11:55:00Z, failed unexpectedly; the deployment's log holds what happened
```

**The two folder columns only mean something together.** `Progress` is the durable checkpoint: how far the forward pass
has committed, and when it last moved. `Last run` is what happened the last time a run took that folder in hand. A
folder whose progress stopped a day ago and whose last run succeeded has nothing left to fetch; a folder whose progress
stopped a day ago and whose runs keep ending is stuck, and only the pair distinguishes them. That is the reading this
output exists for — a folder that raises before its checkpoint commits reads the same batch on every run, the account's
backoff grows behind it, and each individual run still reports itself as finished.

**A folder's outcome names its remedy where it has one.** An alias matching no advertised folder and an alias matching
several are both corrected by editing the mapping rather than by waiting, so both lines say so. A deferral after the
mail server stopped answering, and one after a concurrency conflict, are waited out. An unexpected failure is the one
that sends you to the log. A folder interrupted because the deployment was shutting down is none of those: the restart
ended its turn, its checkpoint holds whatever that turn had already committed, and the first run after the deployment
comes back resumes from there.

**`Phase` says which of three things the account is doing** — running now, ready to run and waiting for one of the
`MailSynchronization:MaxConcurrentAccounts` slots, or waiting out the delay its last run chose. The instant is the
deployment's clock rather than yours. `Backoff` is the consecutive failure count that delay was grown from, so a wait
far longer than `MailSynchronization:Interval` is explained rather than merely observed.

**A folder the account maps and no longer mirrors is listed and marked**, rather than left out, so a folder whose
mirroring was switched off never reads as a folder that vanished. `Synchronization: off` on the first line says the
whole deployment fetches nothing, which is what makes every figure below it still.

The account state is what the running process is doing, so a restart resets it: the phase reads as not started and the
last run as none until the account runs again, which happens within one interval. The folder progress is a durable row
and survives, which is deliberate — the half that tells a stalled folder from an idle one is the half that outlives the
process, and the half a restart clears is the backoff a restart genuinely clears.

Nothing in the answer is mail. Configured account identifiers, folder aliases, a phase, counts, UIDs, and timestamps are
the whole of it: no subject, no address, no remote folder path, and no exception detail.

### Reading what MailFathom changed

The audit route serves one bounded, keyset-paginated page of one account's finished changes, newest first. The account
is required rather than optional, and that is deliberate: the answer says where a person's mail has been and at whose
instruction, so a caller names whose history they are reading rather than asking for a deployment-wide list.

| Query parameter | What it does |
| --- | --- |
| `account` | Required. The configured identifier of the account whose trail is read. |
| `mutation` | Narrows to one change: `relocate`, `delete`, `set-seen`, or `copy`. |
| `from`, `before` | Narrows to entries that ended within a range; `from` is inclusive and `before` is exclusive. |
| `pageSize` | Between 1 and 200; 50 when omitted. |
| `cursor` | The `nextCursor` the previous page returned. |

```console
$ curl -sS -H "X-API-Key: $MAILFATHOM_ADMIN_KEY" \
    "http://127.0.0.1:8090/api/admin/mailbox/mutations/audit?account=work&mutation=delete&pageSize=2"
```

The response carries the entries and, while more remain, the cursor the next page is asked with. **A walk ends when no
cursor comes back**, never by comparing a short page against the size you asked for. A cursor names a boundary within
the filters it was issued for, so presenting one alongside different filters is refused with `400`; changing only the
page size is not, because pacing is not a filter. Every other refusal is `400` too, with a sentence naming what to
change: an account this deployment does not configure, a mutation name that is not one of the four, a page size outside
the range, a range that ends where it begins, and a cursor this deployment did not issue.

Nothing in the answer is mail. Folder paths, UIDs, the local email identifier, the requester, the two timestamps, and
the outcome are what an entry holds, which is what makes the route readable without exposing the message it is about.

An entry a later build wrote and this one cannot interpret — one naming a change this version does not permit — is left
out of the page rather than failing it, and a warning names the account and how many were left out. The rows stay in the
trail and a build that permits the change reads them; what the warning exists for is that a page quietly short of
entries would be worse than one that says so, on a surface whose whole value is being complete.

**Erasing entries for a data-subject request.** Retention erases what has outlived each account's configured window, and
that is the ordinary path. A request that reaches further — erase everything held about one person's mail now — is
answered against the table directly, because the trail deliberately survives the deletion of the mail it describes and
therefore has no cascade to ride:

```sql
DELETE FROM mailbox_mutation_audit_entries
WHERE "MailboxAccountId" = 'work'
  AND "StoredEmailId" = ANY($1);
```

The identifiers are the local email identifiers the entries name, which the same account's mailbox queries return for
the messages in scope; erasing the whole of one account's trail is the same statement without the second predicate.
Take it as a deliberate administrative act on a database you have a backup of: nothing here replays an erasure, and the
entries it removes are the accountability evidence for the changes they recorded.

### Reading what a question read

The answering route is the same shape for the other half of the question an operator has. The mutation trail answers
"why is this message in this folder"; this answers "why did it answer that" — which messages one `ask_mail` run
retrieved from an account, and which of them the response went on to cite.

| Query parameter | What it does |
| --- | --- |
| `account` | Required. The configured identifier of the account whose record is read. |
| `from`, `before` | Narrows to runs that ended within a range; `from` is inclusive and `before` is exclusive. |
| `pageSize` | Between 1 and 100; 50 when omitted. |
| `cursor` | The `nextCursor` the previous page returned. |

```console
$ curl -sS -H "X-API-Key: $MAILFATHOM_ADMIN_KEY" \
    "http://127.0.0.1:8090/api/admin/answering/audit?account=work&pageSize=2"
```

The page is smaller than the mutation trail's because an entry here carries a list rather than a fixed set of columns:
one row per message the run read, each with the position it was reached at and whether the answer cited it. There is no
narrowing filter beside the account and the range, because the questions worth asking of this record are about a
mailbox and a period rather than about a kind of run.

An entry names the run it belongs to, the chat endpoint alias the run was conducted through, the version of the
instruction it was conducted under, when it began and ended, how it ended, and how it degraded. A question asked across
two accounts leaves one entry per account, sharing a `runId` and each naming only its own account's mail.

Nothing in the answer is mail. There is no question, no answer, no retrieved extract, and no subject — the identifiers
are what a reader fetches the messages themselves with, through the reads that already serve them, and storing anything
more would make this record a second copy of the mailbox with its own retention.

The same refusals apply, for the same reasons: an account this deployment does not configure, a page size outside the
range, a range that ends where it begins, and a cursor this deployment did not issue are each `400` with a sentence
naming what to change. An entry a later build wrote and this one cannot interpret — one naming an ending or a
degradation this version does not declare — is left out of the page rather than failing it, and a warning names the
account and how many were left out.

**Erasing entries for a data-subject request.** Unlike the mutation trail, this record follows the mail it names:
erasing a message erases it from the runs that read it, through the same cascade every other derived row rides. That is
the difference between recording an act performed on mail and recording that mail was read. Retention erases whole
entries at each account's configured window, and a request that reaches further is the same statement the mutation
trail takes:

```sql
DELETE FROM mail_answering_audit_entries
WHERE "MailboxAccountId" = 'work';
```

Take it as a deliberate administrative act on a database you have a backup of.

### Administering the embedding profile

Three commands, and none of them takes a model, a provider, or a vector width as an argument. Configuration declares
what this deployment embeds with and [ADR
0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
leaves these the imperative half: the act that takes a declaration up, the act that stops it, and the reading that says
where it got to. Editing a configuration file costs nothing; activating is the first thing MailFathom does that costs
money per unit of mail.

```console
$ mfctl embedding status
Deployment:  production (https://mail.example.test:8443)
Declared:    openai text-embedding-3-small, 1536 dimensions, Cosine
Serving:     openai text-embedding-3-small, 1536 dimensions, Cosine — 4,120 of 4,120 messages embedded; nothing outstanding
Reindex:     none running.
Next pass:   due at 2026-08-08 12:14:30Z
Provider:    Serving, as of 2026-08-08 11:59:00Z
Spend:       1,200 of 50,000,000 characters; the period rolls over at 2026-08-09 00:00:00Z
```

**`mfctl embedding status` is the command to run when semantic search is not returning what you expected.** It answers
that question six ways at once, because it has six answers that look nothing alike: no provider declared, a
declaration nobody activated, a provider refusing the credential, a reindex still running, a budget period spent, and a
walk whose next pass is simply not due yet. The line to read first is `Declared`, which says so outright when an
activation is outstanding — an edited configuration file changes nothing until one happens, and this is where you find
that out rather than from search results that stayed the same.

`Next pass` is the line for the minutes just after an activation, when a deployment that is waiting and one that is
failing read identically everywhere else: nothing serving, nothing embedded, and a provider nothing has been asked of.
An activation asks for a pass immediately, so the instant it names is normally the one now or a moment away rather than
the end of an interval an earlier pass chose; `none scheduled` means the deployment has only just started, or that
`EmbeddingBackfill:Enabled` is `false` and it walks no stored mail at all. [Embedding
backfill](../features/embedding-backfill.md#an-operators-act-does-not-wait-for-the-pause-to-expire) holds the pacing
this line reports.

**`mfctl embedding activate` reads the estimate, states it, and asks.** The deployment counts the passages the run
would send and expresses them as characters and approximate tokens, weighs them against `Embeddings:MaxInputCharactersPerPeriod`,
and the command puts both numbers on the screen before the question:

```console
$ mfctl embedding activate
Declared:  openai text-embedding-3-small, 1536 dimensions, Cosine
Forecast:  This deployment is not embedding under that model, so activating starts a reindex.
Estimate:  41,208 passages to send (18,700,412 characters, roughly 4,675,103 tokens).
Spend:     0 of 50,000,000 characters; the period rolls over at 2026-08-09 00:00:00Z
Embed the mailbox under that model? [y/N]
```

The estimate is on standard output and the question is on standard error, which is the split every command here keeps:
what a redirected invocation captures is the reading, and the person who started it still reads the question, the
guidance, and the failures. Colour marks a failure and a caution, and on a reading it marks a column heading and a label
naming a value; guidance carries none, and neither does anything else. A run whose output is redirected or whose
environment sets `NO_COLOR` is written without escape sequences at all.

The prompt is the default and `--yes` is the exception, for scripted use. An invocation whose input is redirected and
which passes no flag is refused rather than answered out of whatever was piped in. Activating what is already serving
spends nothing and is performed without a question; activating while a *different* reindex is running is refused with
`409`, naming the cancellation as what makes it possible.

**An estimate above the ceiling is refused outright, with `409` naming both numbers.** It is not started and paced:
ADR 0006 takes a budget that only slows a run down to be a schedule rather than a budget. Raising
`Embeddings:MaxInputCharactersPerPeriod`, or setting it to zero to declare no ceiling at all, is what gets past it.

**`mfctl embedding cancel-reindex` stops a run you have changed your mind about.** The generation being built is
abandoned and its partial vectors are removed; nothing about search results changes, because that generation was never
read. A cancellation arriving after the run finished reports that nothing was building and changes nothing — this
command never takes a serving generation out of service. [Changing the embedding model](embedding-profiles.md) is the
whole procedure these three commands drive, including what a switch and a rollback cost.

Nothing any of the four routes answers with is mail: model names, counts, character totals, timestamps, and a profile
identifier are the whole of it.

### Reading the rules, running them, and finding out what they did

Five commands, and none of them writes a rule. A rule is authored in configuration, where an edit is reviewable in a
diff before it reaches a mailbox, so `mfctl` runs the rules and reads what they concluded and never creates, edits,
enables, disables, or deletes one. [`MailRules`](configuration-reference.md#mailrules) is the section they are declared
in, and [mail rules](../features/mail-rules.md) is the whole authoring surface.

**`mfctl rules list` is the command to run after editing rules.** A reload whose rules do not validate is refused and
leaves the previous set running, which reaches the deployment's log and nothing else — so an edited file and an
unchanged deployment read identically until this is asked:

```console
$ mfctl rules list
production — rule set a1b2c3d4e5f6
Configuration: accepted. What is running is what the file says.

Rule                     Applies to     Runs on                                              A match
file-invoices            work           Arrival                                              relocate → archive; ends the pass
retire-old-newsletters   every account  nothing automatically; 'mfctl rules run' applies it  setSeen → read
archive-old-newsletters  every account  Schedule (daily:03:00:Europe/Warsaw)                 relocate → archive
```

The order is the answer as much as the rules are: which rule reaches a message first is a property of the set, and a
rule above another that ends the pass is why the one below it never runs. `mfctl rules show <name>` reports one rule in
full, including the facts its condition can read. Neither prints the condition an operator wrote — a compiled rule
carries no text, which is what keeps an address somebody typed into a condition out of every record naming the rule.

`Runs on` is the [triggers](../features/mail-rules.md#which-triggers-run-a-rule) the rule declares, and the second
rule above is what a rule naming none reads as — whether it writes `"Triggers": []` or leaves the key out, which say
the same thing. Such a rule is bound, validated, and applied by a whole-mailbox run like any other, and no arriving
message reaches it — so the wording says what does run it rather than reporting an empty list, because a rule nothing
fires by itself and a rule that never matches look identical in a history that records neither. A rule declaring
`Schedule` names its occasions beside the trigger, in the canonical form the deployment read them as — which is how an
operator tells a schedule that was accepted as written from one they meant to write.

**`mfctl rules run --account <id>` applies the rules to mail that arrived before them.** It returns as soon as the
deployment has written the request down and never waits for the walk; the pass is a step of the account's
synchronization run, so this terminal is not what keeps it alive and closing it cannot cancel one. Asking twice is
asking once, and the command says which of the two happened:

```console
$ mfctl rules run --account work
A rule run over work has been asked for.
Progress:  0 evaluated, 0 matched, 0 skipped
The run is carried by the account's synchronization runs. Watch it with 'mfctl rules run-status --account work'.
```

`mfctl rules run-status --account <id>` is where it is watched from, and an account nobody has ever asked for a run is
an answer rather than an error. A run under way says what started it — `under way, started by RequestedRun` for one this
command asked for, `started by ScheduledRun` for one [a rule's own
schedule](../features/mail-rules.md#running-a-rule-on-a-schedule) asked for — because the two walk the same mail and
reach different rules. [Running the rules over mail you already
have](../features/mail-rules.md#running-the-rules-over-mail-you-already-have) holds what a run guarantees, including
what happens to one when the rules change under it.

**`mfctl rules history --account <id>` answers what a rule did, and why a message is where it is.** Narrowing to a rule
with `--rule` answers "what is this rule doing", including the case where the answer is that it is evaluated constantly
and never matches; narrowing to a message with `--email` answers "why is this message here". `--page-size` and
`--cursor` walk it, newest first:

```console
$ mfctl rules history --account work --rule file-invoices
Evaluated             Rule           Outcome  Message                               Rule set                             Read                           Asked
2026-08-08 11:59:00Z  file-invoices  Matched  0199c3d0-0000-7000-8000-000000000002  a1b2c3d4e5f6 (RequestedRun, 4.0 ms)  senderDomain, attachmentCount  relocate → archive: Requested
```

Each row is one rule's conclusion about one message. A rule that was reached and answered no is recorded as
`NotMatched`, a rule that could not answer at all is `Failed` with its
[reason](../features/mail-rules.md#when-a-condition-cannot-answer) beside it, and a rule the pass never reached leaves
nothing at all — which is what tells "never matches" from "never asked". A change the rule asked for is `Requested`
when a mutation record was opened for it, `Refused` with a classification where one could not be, and `Withheld` where
another rule had already settled the message's fate.

**The facts are names and never values.** `senderDomain` says the condition read the sender's domain; what that domain
was is not recorded, and neither is a subject, a matched span, or any other value the mailbox supplied. What the
condition compared is retrievable from the rule set revision printed beside it, which identifies the configuration the
expression was read from — so the reasoning is reconstructible without the record becoming a second copy of the
mailbox. The same holds for what the run asked for: the record points at the mutation it opened rather than restating
what happened on the server, which [the mutation
trail](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)
answers.

The history is held for [`MailRules:HistoryRetention`](configuration-reference.md#mailrules) and is erased with the mail
it describes, whichever comes first. Nothing any of these four routes answers with is mail: rule names, folder aliases,
special-use roles, mutation names, fact names, counts, instants, and identifiers are the whole of it.

Both views call the folder `destination`, and each answers it to the depth its own reader can use. A **declared**
rule's action carries the text the rule wrote — an alias, or a role as `role:Junk` — because that view answers *what
does this deployment's configuration say*. A **history** entry carries the alias the run resolved to, because that view
answers *what happened to this message*. An action the run never requested — refused, or withheld because another rule
had already settled the message — carries what the rule wrote instead, since a role that reached no folder has no alias
to name and the rule's own words are what an operator has to correct.

### Classifying the mail you already have, and reading what was concluded

Three commands, and none of them writes a setting. Whether mail is classified at all, what a scanner is judged by, and
what happens to junk are configuration for the reason a rule is, so `mfctl` applies them to the mail a deployment
already holds and reads what was decided. [`SpamClassification`](configuration-reference.md#spamclassification) is the
section, and [spam classification](../features/spam-classification.md) is what the feature does.

**`mfctl spam run --account <id>` is a dry run unless you add `--apply`.** It returns as soon as the deployment has
written the request down and never waits for the walk; the run is carried by the account's synchronization runs, so
this terminal is not what keeps it alive and closing it cannot cancel one:

```console
$ mfctl spam run --account work
A classification run over work has been asked for.
Folders:   INBOX
Acting:    no — this is a dry run; it records verdicts and leaves the mailbox alone. Add --apply to carry out what the switches ask for.
Progress:  0 scored, 0 already decided, 0 unreadable
The run is carried by the account's synchronization runs. Watch it with 'mfctl spam run-status --account work'.
```

`--folder` narrows the walk and is repeatable; it narrows *within* the configured scope, and a folder outside it is
refused naming the section to edit, because a run over a folder nobody classifies would read the whole of it and record
nothing. `--rescore` scores mail again even where its verdict was already reached under the settings now in force,
which is the one form of the run that costs a scanner call per message however recently it was decided.

Asking twice is asking once, and the command says which of the two happened — including that the terms the second
request carried were not applied to the walk under way.

**`mfctl spam run-status --account <id>` is where the run is watched from**, and where the answer to *what would it do*
is read:

```console
$ mfctl spam run-status --account work
Account:    work — Completed at 2026-08-12 11:30:00Z
Requested:  2026-08-12 11:00:00Z
Folders:    INBOX
Acting:     no — dry run
Rescoring:  no
Profile:    a1b2c3d4e5f6
Progress:   1240 scored, 0 already decided, 3 unreadable
Found:      37 junk, 4 undetermined, 37 would be acted on
```

`Found` is what an operator is deciding on: the junk the run reached, and how much of it the switches would act on.
An account nobody has ever asked for a run is an answer rather than an error, and a run that ended an hour ago is still
reported — *it completed* and *you never asked* are different answers. `Superseded` is a run the settings moved under,
and `Disabled` one that was switched off under it.

**`mfctl spam classifications --account <id>` answers why a message was filed.** Narrowing to a message with `--email`
answers "why is this in junk"; narrowing with `--verdict` answers "what would this run file". `--page-size` and
`--cursor` walk it, newest first:

```console
$ mfctl spam classifications --account work --verdict Spam
Evaluated             Verdict                Message                               Folder  Under                                                     Signals                  Asked
2026-08-12 11:04:11Z  Spam (Scanner 15.2/5)  0199c3d0-0000-7000-8000-000000000002  INBOX   a1b2c3d4e5f6, scanner corpus spamassassin.4.0.2+20260801  X-Spam-Status, BAYES_99  relocate (0199c3d0-0000-7000-8000-000000000009)
```

**The signals are names and never values.** `X-Spam-Status` says the verdict rests on that header; what the header said
is not recorded here, and neither is a subject, an address, or a sending domain. `Under` is the profile the verdict was
reached under, which is what a run compares before scoring a message again. `Asked` names the change and the mutation
record carrying it rather than restating what happened on the server, which [the mutation
trail](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)
answers.

Nothing any of these three routes answers with is mail: counts, verdicts, scores, signal names, folder aliases,
mutation names, instants, and identifiers are the whole of it. Every refusal is `400` naming what to change, including
an account this deployment does not configure, and the run route reads at most 8 KB of body.

### Reading the background work that stopped, and deciding what becomes of it

Three routes, and the first of them is the only way a dead letter becomes visible without a database client. Work that
has stopped is claimed by nobody and delays nothing, so it is invisible everywhere else: nothing retries it, no queue
grows because of it, and the only signal it produces at all is
[`mailfathom.jobs.dead_letters`](telemetry.md#durable-background-work). [Deciding what becomes of stopped
work](../users/administering.md#background-work-that-stopped) is what the three commands do with them.

The reading is deployment-wide unless a filter narrows it, because *what has stopped* is one question about the instance
rather than one per configured mailbox. It serves one bounded, keyset-paginated page, newest first:

| Query parameter | What it does |
| --- | --- |
| `type` | Narrows to one kind of work, by the job type's own name. A name this build does not run is refused. |
| `account` | Narrows to work belonging to one configured account. |
| `pageSize` | Between 1 and 200; 50 when omitted. |
| `cursor` | The `nextCursor` the previous page returned. A cursor issued under different filters is refused. |

```console
$ mfctl jobs dead-letters
Stopped               Job                                   Kind                 Failed                                          Work                                                              Queued
2026-08-13 09:30:00Z  0199c3d0-0000-7000-8000-000000000002  classify-email-spam  Permanent PayloadUnreadable after 5 attempt(s)  account:work|email:0199c3d0-0000-7000-8000-000000000001 for work  2026-08-13 09:00:00Z

Run one again with 'mfctl jobs retry --job <id>', or write it off with 'mfctl jobs drop --job <id>'.
```

**Nothing any of the three routes answers with is mail.** A job's payload names a message occurrence, and it is never
read: the reading projects the identity, the kind of work, the attempts spent, the failure classification and its
recorded reason, and two instants. The idempotency key is the one field composed from a folder alias and a message
identifier, and it is reported because a retry runs under it — an operator deciding whether to run something again is
told which piece of work it is, never what the message said.

A row naming a job type this build does not run is left out rather than reported. A rolling deployment leaves rows
written by a build declaring more types than this one, and offering a retry no worker could ever claim would be worse
than an absence.

Both decisions name one job, read at most 4 KB of body, and answer `200` with what happened. `Accepted` is the decision
having taken effect; `JobUnknown` and `JobNotDeadLettered` are outcomes rather than refusals, because two operators — or
one operator and a list a few minutes old — reach them ordinarily and the caller asked a question the deployment could
answer. `400` is kept for a body naming no job at all.

Retrying returns the same row to the queue with its attempts given back, so the work runs under the identity it was
enqueued with rather than as a second piece of work; that is safe because a handler is registered on the promise that
running it twice with one payload is the same as running it once. Dropping removes nothing: the row stays terminal,
keeps the failure that ended it, and goes on holding the identity that stops the same trigger enqueuing the work again.

### Erasing a folder you have stopped mirroring

**`mfctl folder erase --account <id> --folder <alias>` is the only thing in MailFathom that takes a folder's local mail
away.** Nothing else does, deliberately: switching a folder's `Synchronize` off keeps what it stored, and removing its
mapping leaves the rows where they are, so that [editing a configuration
file](../features/imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is) can never dispose of
somebody's mail. That leaves an operator who means it with nothing to ask, and this is the ask.

```console
$ mfctl folder erase --account work --folder archive
500 stored emails erased so far
1000 stored emails erased so far
1043 stored emails erased from ARCHIVE under work. The folder holds none, and its checkpoint went with them, so
mirroring it again starts from the beginning rather than resuming.
```

The row goes and PostgreSQL takes its raw MIME, its search document, its passages, their vectors, and any outstanding
repair request with it — the same deletion path an erasing disposition already uses rather than a second one. The
folder's checkpoint goes too, in the pass that empties it, which is what makes a folder erased and then switched back
on mirror from the start instead of resuming in front of mail that is no longer there. **The alias survives**: its
binding stays, so the folder goes on resolving and goes on being somewhere a rule can file mail into.

**A folder the account still mirrors is refused**, naming the alias and saying what makes it erasable. Erasing one
would open a hole the next run silently refills, so the two ways to mean it are to switch the folder's `Synchronize`
off or to remove its mapping — and an alias no mapping names at all is accepted rather than refused, because a mapping
somebody withdrew is exactly the case that needs erasing and the one case no configuration value can express.

One request is one bounded pass, and the command repeats it until the deployment reports nothing left, printing a
running total as it goes. That is what makes an interrupted erasure resumable rather than a folder in a state nothing
can finish: a pass either committed or did not, so interrupting the command leaves the rest where it was and running it
again continues from there. Running it against a folder that already holds nothing succeeds having removed nothing,
which is the ordinary end of every erasure.

### Bringing stored mail up to a later release

Two routes beneath `/api/admin/mailbox`, and which one a property needs is decided by where its value comes from. A
release adds properties to stored mail and nothing in a running deployment fills them in for the mail already mirrored,
because a run resumes from the UID its folder's checkpoint records. [Bringing stored mail up to a later
release](../features/imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) is the whole of what each one
refreshes and what neither touches; this is what the endpoint serves.

**`mfctl mailbox rederive` is the cheap one, and the one to reach for first.** Everything the stored payload itself
carries — the sender identity the receiving server authenticated is today's example — is already on the deployment's
own disk, so it is a local read, a parse, and an update of the message's own columns. It opens no mailbox session, so
it cannot set `\Seen`, and it rewrites no stored content:

```console
$ mfctl mailbox rederive --account work
500 stored emails re-read so far
1,000 stored emails re-read so far
1,043 stored emails re-read for every folder under work.
2 stored emails carried MIME no reader could parse and kept what was already recorded for them.
```

One request is one bounded pass and the command repeats it until the deployment reports nothing left, printing a
running total as it goes. Interrupting it is safe: what a batch committed stays committed, the deployment remembers
where the walk got to, and running the same command again continues from there rather than starting the scope over. A
walk that reaches the end forgets its position, so asking again after the next release re-reads the scope from the
beginning — which is what the command exists for.

**`mfctl mailbox rewind` is the expensive one, and the only answer for a property the mail server alone knows** — a
flag, a keyword, the internal date. It discards the durable synchronization progress of the scope's folder bindings, so
the next runs read them from the first UID inside the account's window and everything the server knows is read again:

```console
$ mfctl mailbox rewind --account work
Scope:  every folder under work
Cost:   22,500 stored emails would be fetched from the mail server, re-read, and stored again.
Rewind that scope? [y/N] y
Rewound:  ARCHIVE
          INBOX
Each reads from the first UID inside the account's synchronization window on its next run. Nothing was erased, and a
run already under way is refused its next advance rather than corrupting this.
```

**The cost is read before it is performed**, which is one path answering `GET` with what the scope holds and `POST`
with what was discarded — the same arrangement the embedding activation uses, and for the same reason: the figure an
operator agrees to and the figure the deployment acts on have to be one figure. `--yes` states the agreement in the
command, which is what a scripted rewind needs; an invocation with input redirected and no flag is refused rather than
reading an answer out of whatever was piped in. A scope the assessment counted nothing in is asked about like any
other: the count is the mail the deployment stores rather than what a run would fetch, and a folder whose local copies
are all tombstoned counts nothing while its bindings still hold the progress the rewind takes away.

**Nothing is erased and nothing is duplicated.** A rewind removes one row of progress per binding; the mail, its raw
MIME, its passages, and their vectors stay where they are, and re-reading an occurrence stores over the local email
already at that identity. A synchronization run already under way loses the race safely — it decided from progress that
no longer exists, so its own advance is refused rather than written over the rewind — and the answer names the folders
whose bindings held progress, which is what says the removal won.

Both take `--account` and an optional `--folder`; without it they cover every folder the account holds mail in,
including one whose mapping was withdrawn. Each write reads at most 4 KB of body — the assessment is a `GET` and names
its scope in the query string — and every refusal is `400` naming what to
change: an account this deployment does not configure, and text that is not a folder alias. A folder named blank is an
omission rather than a refusal, because a caller writing a URL cannot express the difference.

**Neither route touches embeddings, and neither re-runs classification for a verdict already recorded.** Chunks and
vectors stay the [embedding profile's](embedding-profiles.md) business, so a refresh cannot quietly spend the provider
budget an operator has not asked to spend.

### Administering the contact book

`mfctl contact` is where the [contact book](../features/contacts.md) is maintained: people, the addresses they use, and
what you recorded about them. The book's own rules — what identifies a person, when two addresses are the same address,
who may change what — are that page's; this is the command group over them.

```console
$ mfctl contact create --name "Anna Kowalska" --address anna@example.test --note "Met at the conference."
Recorded contact 018f2b1c-9b3a-7c41-8f7d-2c6a5e9d10ab.
Contact:    018f2b1c-9b3a-7c41-8f7d-2c6a5e9d10ab
Name:       Anna Kowalska
Origin:     Asserted
Addresses:  anna@example.test  (preferred)
Note:       Met at the conference.
Recorded:   2026-08-16 09:00:00Z
Amended:    2026-08-16 09:00:00Z
```

| Command | What it does |
| --- | --- |
| `contact create` | Records a person. `--address` is repeated for each address they use, and `--preferred` says which to use by default — required as soon as there is more than one, because that is your choice rather than an ordering accident |
| `contact show` | Shows one person, by `--id` or by `--address`. Naming both, or neither, is refused |
| `contact list` | Reads one page, optionally narrowed with `--origin`. `--page-size` bounds it and `--cursor` continues it |
| `contact update` | Corrects `--name`, `--note`, `--preferred`, or the whole `--address` set. What you do not name is kept; `--clear-note` holds no note afterwards |
| `contact add-address` | Adds one address, keeping the rest. `--preferred` names which address to use by default afterwards |
| `contact remove-address` | Takes one address off. `--preferred` is required when the one being removed is the default |
| `contact promote` | Takes on a contact the deployment collected, so it becomes one you asserted |
| `contact delete` | Erases the person. **This cannot be undone**; see below |
| `contact export` | Writes everything held about the person to standard output, as JSON |

**Amendments state the whole record.** The book replaces what it is given rather than merging a difference, so
`update`, `add-address`, and `remove-address` each read the contact first and send back what it is to become. Two
operators editing one contact at once are therefore last-writer-wins; an edit racing an erasure is not, and is answered
as a contact the book does not hold rather than putting the person back.

**A contact the deployment collected is not amended in place.** Collection writes into its own origin and an owner does
not edit those records directly — `contact promote` is the act of taking one on, after which every other command here
works on it. Amending one without promoting it is refused, and the refusal says so.

**`mfctl contact delete` is the contact book's erasure path.** It removes the person and their addresses from the
database rather than marking them, and nothing in MailFathom can put the record back. The command shows the record and
then asks, and `--yes` is how a scripted erasure states the agreement instead; an invocation with nobody at the terminal
and no flag is refused rather than having an agreement read out of whatever was piped in. It answers with what went —
the identity and how many addresses — and never with the person. Erasing somebody the book does not hold succeeds
having removed nothing, because that is the state you asked for.

**`mfctl contact export` is the access path**, and it writes one JSON document on standard output so it redirects into
a file you can hand to the person who asked. It carries the complete record and the instant the export was taken;
everything else the command prints goes to standard error.

**The listing is bounded and there is no command that prints the whole book.** A page holds 50 contacts unless you ask
for fewer and never more than 200, ordered by the name's comparison form and then by identity. That order is total, so
walking a page at a time serves every contact exactly once. A page that has more behind it prints the cursor the next
one is asked with.

Every refusal names the rule rather than the value: a malformed address is reported as an address that is not usable and
never echoed, and no name, address, or note reaches a log line, a problem document, a trace, or a failing command's
output. What a failure names is the contact's identifier, which is the one part of the record that is not personal data.

## Rate limiting

An enabled endpoint is bounded, whether or not anyone wrote a number. That is what stops an administrative surface
reachable from a network from serving unbounded API-key guessing, which is the attack it is most exposed to and the one
where a successful guess is worth the most.

`AdminEndpoint:RateLimiting` is the same section `McpEndpoint:RateLimiting` is, with the same keys, the same product
defaults, and the same validation. [Rate limiting](mcp-endpoint.md#rate-limiting) is where the settings, the ranges, the
reasoning, and what a refused request receives are recorded in full;
[configuration reference](configuration-reference.md#rate-limiting--mcpendpointratelimiting-and-adminendpointratelimiting)
is the key table.

Two things differ here, and both follow from where the credential is judged:

- **The burst is the endpoint's, not one caller's.** These routes carry no authentication middleware of their own — the
  credential is judged by the authorization middleware, which runs *behind* the limiter so that a request about to be
  refused for a wrong key has still spent capacity. There is therefore no identity to partition on when the limiter
  counts, and every administrative caller shares one bucket. Size `TokenCapacity` as what the whole endpoint may burst
  to rather than what one operator may.
- **Neither endpoint's traffic reaches the other's limits.** The partitions are keyed per surface, so a key spelled the
  same way under both sections is two independent buckets, and an agent that exhausted the MCP endpoint's capacity has
  taken nothing from the surface you would use to stop it.

The two endpoints' concurrency limits are separate for the same reason: a runaway agent saturating `/mcp` must not lock
you out of `/api/admin`.

Turning the limits off is an explicit value and costs one startup warning, as it does on the MCP endpoint.

## Request timeouts

`AdminEndpoint:RequestTimeout` bounds how long one administrative request may run before it is abandoned, answering
`504` and releasing the concurrency permit it held. It is the same section the MCP endpoint carries, with the same keys
and the same ten-minute default, configured independently.
[Request timeouts](mcp-endpoint.md#request-timeouts) records the settings and the reasoning in full.

**This is the endpoint whose default is worth narrowing.** The ten minutes are sized for the MCP surface, where an
`ask_mail` run can legitimately spend minutes against an AI provider; no administrative route reaches a provider at all.
Their work is a bounded database read or a configuration inspection, so a ceiling of a minute or less costs these routes
nothing and shortens how long a stalled request can hold one of this endpoint's twenty permits.

That matters more here than on the MCP surface for the reason the shared bucket does: the permits an administrative
caller holds are the ones you need free to reach `/api/admin` while something else is going wrong.

## Four postures the endpoint warns about

None is refused, because each is legitimate somewhere and only you know which you have.

| Startup warning | What it means |
| --- | --- |
| No authentication method turned on | Anything that can reach the address can administer the service. Right only for a loopback bind or a network you control. |
| Served in clear text | Any credential a client presents is readable on the path. Right only behind a TLS-terminating reverse proxy, or on a loopback bind. |
| `AdminEndpoint:RateLimiting:Enabled` set to `false` | Nothing bounds how fast a caller may present wrong credentials. Right only where something in front of the process already bounds the traffic reaching it. |
| `AdminEndpoint:RequestTimeout:Enabled` set to `false` | Nothing bounds how long one administrative request may hold a concurrency permit. Right only where something in front of the process already abandons a stalled request. |

Configure `AdminEndpoint:Https:Endpoints` to have Kestrel terminate TLS itself. It takes the same profile shape the MCP
endpoint's does, including `HttpProtocols`, which defaults to HTTP/1.1 and HTTP/2. Naming any profile binds those
listeners and no clear-text one stays open behind them serving these routes.

### Redirecting `mfctl` after you configure TLS

A profile also binds one clear-text listener whose only answer is a `308` to the address the profiles are served at, on
**port 8091** unless you state another. It is what keeps an `mfctl` profile that still holds an `http://` endpoint from
failing as though the deployment were down:

```console
$ curl -i http://admin.example.com:8091/api/admin/session
HTTP/1.1 308 Permanent Redirect
Location: https://admin.example.com:8543/api/admin/session
```

**Repoint the profile rather than relying on it.** An administrative API key sent in clear text was on the wire before
anything answered, and this route stores mailbox credentials — a redirect protects the next request and never the one that
arrived. `mfctl login --endpoint https://admin.example.com:8543` writes the corrected address; see
[working with more than one deployment](#working-with-more-than-one-deployment).

That listener maps no route. No administrative operation, no session probe, and no protected-resource metadata document is
reachable over it, and no credential check runs for a request that arrived on it — every path gets the same redirect, and a
`Host` header naming no configured domain gets `400`. The port is checked against every other listener in the process, so a
port the MCP surface or the probes also bind is shared rather than refused. What the two surfaces must then agree about is the socket itself — the scheme, the redirect, the client-certificate question — while their credentials, limits, and HTTPS ports stay their own; [which settings a shared socket couples](configuration-reference.md#which-settings-a-shared-socket-couples) is the table.

Turn it off with `AdminEndpoint:Https:Redirect:Enabled` set to `false`, which is what a deployment behind a proxy that
already answers the clear-text port wants. The setting shape and every refusal are the MCP endpoint's, documented once in
[redirecting a client still pointed at `http://`](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http); only the
default port differs, so enabling TLS on both surfaces opens two clear-text ports that do not collide.

## Behind a TLS-terminating reverse proxy

If a proxy holds your certificate, the request states the public name it arrived under, which is what lets
the endpoint's OAuth discovery complete over a proxied address. `ReverseProxy:TrustedProxies` is what limits who may
state it; left empty it is anybody.
[Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) documents that in
full, including what the unnamed default gives up; three things are worth stating from this endpoint's side.

- **It is one process-wide setting, not one per endpoint.** This surface is a separate listener over the same request
  pipeline, so naming your proxy once covers it along with the MCP and probe listeners. There is no
  `AdminEndpoint:ReverseProxy`, deliberately.
- **The OAuth entry's `Resource` is unaffected.** It stays the value you wrote, still ends in `/api/admin`, and is
  still what a token's audience is compared against. The mode never derives it from a header.
- **A proxy that authenticates its own callers is not this endpoint's authentication.** `AdminEndpoint:Authentication`
  still decides who may administer the service, and the clear-text warning above still fires, because the hop between
  the proxy and this process is still clear text.

Whether the proxy publishes this listener at all is your decision: the administrative port is separate from the
application port, so a deployment can proxy the MCP surface publicly and keep this one on a network you control.

## Getting the command

Each release attaches a self-contained binary per platform, plus one checksum file covering all of them.
[The install script](#on-linux-with-the-install-script) is one command that performs the whole of what follows on Linux;
this is what it performs, and what to do on Windows.

Download the one for the machine you administer *from* — the command talks to a deployment over HTTP, so it does not
have to run where the service runs.

| Platform | Asset |
| --- | --- |
| Linux, x86-64 | `mfctl-<version>-linux-x64` |
| Linux, ARM64 | `mfctl-<version>-linux-arm64` |
| Windows, x86-64 | `mfctl-<version>-win-x64.exe` |
| Windows, ARM64 | `mfctl-<version>-win-arm64.exe` |

Nothing needs installing beside it: the .NET runtime is inside the file.

**No binary is signed**, on any platform, so Windows warns about an unknown publisher when you run one and the checksum
file is the only thing that distinguishes a genuine download from a tampered one. Check it in the directory you
downloaded into, before running anything:

```bash
sha256sum --check --ignore-missing 'mfctl-<version>.sha256'
```

`--ignore-missing` is what lets one file cover four binaries: it checks the ones present and says nothing about the
three platforms you did not download. **`<version>` is the release you downloaded** — substitute it, and note that the
name is quoted so a line pasted without that substitution fails with a missing file rather than with a redirection.

The command binaries carry no build provenance attestation either. That is the other question worth asking about a
download — the checksum says the bytes are the ones published, and an attestation would say which workflow and commit
produced them — and the image and the chart are where this repository answers it.
[The container image](container-image.md#published-images) records how.

### On Linux, with the install script

Everything above as one command. It resolves the newest release, downloads the binary for the architecture it is
running on, checks it against that release's own checksum file, and installs it as `~/.local/bin/mfctl`:

```bash
curl -fsSL https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/scripts/install-mfctl.sh | bash
```

**Pass the version when the deployment is not on the newest release**, because the two have to agree on `major.minor`
— which is the next section. `--directory` chooses where it goes. `MFCTL_VERSION` and `MFCTL_INSTALL_DIR` set the same
two things, so a shell profile can carry the answer instead of the command line, and an argument wins over either:

```bash
curl -fsSL https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/scripts/install-mfctl.sh \
  | bash -s -- --version 0.5.0 --directory ~/bin
```

It installs into your own directory and never runs `sudo`; a system-wide installation is `--directory /usr/local/bin`
under a `sudo` you write yourself. If the directory it installed into is not on your `PATH`, it says so and prints the
line that fixes it rather than leaving you to find out at the next prompt. Re-running it installs over what is there,
which is how a version is changed.

**Nothing is installed that could not be verified.** The checksum check above is the script's, not a step it saves you:
a download that does not match what the release publishes stops it, and the file it downloaded goes with the temporary
directory. What the script does not do is tell you whether the release is the one you meant — it is fetched over HTTPS
from this repository, and reading it before running it is a reasonable thing to do, which is why it is one short file
with no second script behind it.

Windows has no equivalent. Take the `.exe` from the table above and check it as the section above describes.

### Take the command from the deployment's own release line

**`mfctl` and the deployment it administers have to agree on `major.minor`.** Every command that reaches a deployment
reads `GET /api/admin/session` before it asks for anything else, compares the version that comes back with its own, and
stops there when the two name different release lines:

```console
$ mfctl embedding status
mfctl is 0.5.0 and the deployment is 0.4.2. A minor release is permitted to change the administrative contract, so a
command is refused rather than sent to a deployment from another release line. Run the mfctl published with that
deployment's release, or upgrade the deployment to this one.
```

Nothing is sent when that happens, which is the point: the refusal lands before the request it is protecting, so a
command that would have started a provider bill starts none.

The rule follows the version's own promise rather than adding one. Within `0.x` a minor release may change any public
surface and a patch may change none, so the release line is the whole of what the two builds have to share —
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
records that policy.

Everything below the line is a difference the command reports and carries on past:

| The pair | What happens |
| --- | --- |
| Identical versions | Nothing is said. |
| Same `major.minor`, different patch — `0.5.0` and `0.5.1` | The command runs, and writes one line to standard error saying the builds differ and problems may occur. |
| Same `major.minor`, one of them a nightly — `0.5.0` and `0.5.0-nightly.41` | The same. A nightly is a preview of the release it will become, not a line of its own. |
| A version either side cannot read | The command runs and says which of the two it could not read. A build reporting `unknown` is an unstamped one rather than an incompatible one, so it is never refused on that alone. |

The warning is written once per command rather than once per request, and it goes to standard error, so a command whose
output you redirect still captures the result alone.

## Signing in

`--mode` chooses how the credential is produced, and it is stated rather than guessed — guessing would put a machine
with no browser on a redirect that can never arrive.

| Mode | What it does | When |
| --- | --- | --- |
| `key` (default) | Reads one credential from standard input | An API key, or an access token you obtained elsewhere |
| `keypair` | Signs each request with a private key on this machine | A scheduled job, or anywhere a stored credential is one too many |
| `interactive` | Opens a browser here and catches the redirect | You are at the machine you are administering from |
| `device` | Prints a code to enter on another device | A jump host, or anything without a browser |

### With an API key

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production
Administrative credential (an API key, or an access token from the configured authorization server):
Signed in to https://mail.example.test:8443 as 'workstation' (MailFathom 0.2.0), saved as profile 'production' and selected.
```

The credential is read from standard input rather than taken as an argument, because an argument reaches the shell
history, the process list, and any log of either. A script pipes it in instead:

```console
$ printf '%s' "$MAILFATHOM_KEY" | mfctl login --endpoint https://mail.example.test:8443
```

### With a key pair

Generate a pair on the machine that will run the command, and give the deployment the public half only:

```console
$ openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out ~/.config/MailFathom/production.key
$ chmod 600 ~/.config/MailFathom/production.key
$ openssl pkey -in ~/.config/MailFathom/production.key -pubout
```

Register that public key under `AdminEndpoint:Authentication` as a `PublicKey` entry — see
[Key pairs](mcp-endpoint.md#key-pairs) for the block and what it accepts — then sign in:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production --mode keypair \
    --private-key ~/.config/MailFathom/production.key
Signed in to https://mail.example.test:8443 as 'reporting-job' (MailFathom 0.4.0), saved as profile 'production' and selected.
No credential was stored. Every command signs a short-lived assertion with the key at
/home/you/.config/MailFathom/production.key, so keep that file readable by this account alone and the sign-in lasts as
long as the deployment accepts its public half.
```

**Nothing presentable is written down.** The profile records where the key lives and no credential at all, and every
later command reads that key and signs a fresh assertion that expires within the minute. A credentials file that leaves
this machine — in a backup, a synced folder, a support bundle — therefore carries nothing anyone could present, which is
the difference from every other mode. The key itself is never copied into the store: it stays where you generated it,
under the permissions you gave it.

The path is made absolute when it is stored, because a scheduled job rarely runs from the directory you signed in from.
Move the key and sign in again; there is nothing to revoke in between, because nothing was issued.

This mode needs no browser, no authorization server, and no interactive step, so it is the one to reach for in a cron
entry or a systemd timer. Signing in is still verified against the deployment, which is what proves it holds the matching
public half before the first real command runs.

### With OAuth

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production --mode interactive --client-id mfctl

A browser has been opened for you. If it did not appear, open this address yourself:

  https://sso.example.test/realms/mailfathom/protocol/openid-connect/auth?client_id=mfctl&response_type=code&...

Waiting for the sign-in to come back to http://127.0.0.1:8765/...
Signed in to https://mail.example.test:8443 as 'kasia' (MailFathom 0.2.0), saved as profile 'production' and selected.
The access token is renewed for you until the refresh token expires or is revoked, and the sign-in ends when it does.
```

**Only `--client-id` is configured.** Which authorization server to use, the resource the token must be issued for, and
the scopes to ask for all come from the deployment: it publishes an [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728)
metadata document at `/.well-known/oauth-protected-resource/api/admin`, and the server it names publishes where to
authorize. Nothing is transcribed, so nothing is transcribed wrongly.

**The scope list is taken verbatim, and that includes `offline_access`.** A refresh token is what makes the sign-in
outlive its first access token, and a client is issued one by naming that scope — so the entry serving this endpoint has
to advertise it, in
[`AdvertisedScopes`](mcp-endpoint.md#scopes-you-advertise-but-do-not-require) rather than in `RequiredScopes`. Without
it the sign-in is refused where the token is issued rather than an hour later, naming what to do:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --mode interactive --client-id mfctl
The authorization server issued no refresh token, so the sign-in would end within the hour. Grant the client offline
access at the authorization server and have the deployment advertise 'offline_access', or sign in with an API key
instead.
```

Register the command as a **public client** with an authorization-code grant, PKCE required, and the redirect address
`http://127.0.0.1:8765/`. It ships as a binary anyone can download, so it holds no client secret and presents none.
Pass `--redirect-uri` if you registered a different loopback port, and `--issuer` if the deployment accepts tokens from
more than one authorization server — with several configured, the command asks rather than picking one, because they
are separate populations of people.

`--mode device` needs none of that redirect machinery:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --mode device --client-id mfctl

Open this address on any device with a browser:
  https://sso.example.test/device

and enter the code: WDJB-MJHT
The code expires at 2026-08-03 12:10:00Z. Waiting for the sign-in to complete...
```

It requires the authorization server to publish a device authorization endpoint; one that does not is reported as that
rather than left polling.

### What happens either way

**The credential is verified before it is stored.** A deployment that refuses it, an address serving no administrative
endpoint, and a host that answers with something that is not MailFathom all fail here rather than at some later command.

`--name` is what the deployment is remembered as; without it the profile takes the host name. Signing in also selects
the profile, because it is the deployment you just chose to work with.

When a deployment issues a new credential, sign in again by profile name rather than by address — `mfctl login
--endpoint production` — and the address it already holds is reused.

## When the connection is weaker than the default

A deployment on an internal host commonly serves a certificate no workstation trusts — self-signed, or issued by an
authority only your organization carries — and some are reached over `http://` at all. Neither is refused outright and
neither is waved through: `login` asks about it once, records the answer on the profile, and no later command asks
again. Both questions default to no, and refusing either stores nothing and signs in to nothing.

### A certificate this machine does not trust

Nothing happens for a deployment whose certificate validates on its own; the question exists only where it does not.

```console
$ mfctl login --endpoint https://mail.internal.example:8443 --name internal

https://mail.internal.example:8443 presented a certificate this machine does not trust:

  Subject:     CN=mail.internal.example
  Issuer:      CN=Example internal authority
  Fingerprint: 3B:9A:1C:…:7F
  Valid:       2026-01-04 09:12:00Z to 2027-01-04 09:12:00Z
  Not trusted: this machine does not trust the chain it was presented with (UntrustedRoot)

Accepting it stores this fingerprint on the profile. Every later command then accepts this certificate and refuses any other,
so a deployment that renews or replaces its certificate is signed in to again rather than trusted silently.

Trust this certificate for this profile? [y/N]: y
Signed in to https://mail.internal.example:8443 as 'workstation' (MailFathom 0.5.0), saved as profile 'internal' and selected. The connection is protected by a pinned certificate rather than by a chain this machine trusts; the profile now accepts 3B:9A:1C:…:7F and refuses any other.
```

Read the fingerprint against the deployment's own before answering — `openssl x509 -in server.crt -noout -fingerprint
-sha256` prints it in the same form. Nothing is sent until you answer: the handshake was refused, so the credential was
never on the wire.

**A pin is stricter than what it replaces, not weaker.** Ordinary chain validation accepts any certificate a trusted
authority signed; a pinned profile accepts one certificate and refuses every other, including one your machine would
have trusted on its own. That is what makes accepting a self-signed certificate once safe to live with — a later
substitution fails as loudly as an untrusted certificate does today, naming both fingerprints.

The consequence is that a **renewed certificate ends the profile's connection until you accept the new one**. Run
`mfctl login --endpoint internal` again: the sign-in starts from ordinary validation, presents whatever the deployment
now serves, and asks again. `mfctl logout` removes the pin with the profile.

The pin covers the deployment and nothing else. An OAuth sign-in reaches an authorization server as well, and every
request to it goes out under ordinary chain validation, because a fingerprint taken at your deployment says nothing
about the machine your identity platform runs on.

### An endpoint reached over `http://`

An address is taken as written and no scheme is guessed onto a bare host, so `http://` is a decision — one that is easy
to make out of habit:

```console
$ mfctl login --endpoint http://mail.internal.example:8090 --name internal

http://mail.internal.example:8090 is an HTTP address, so nothing protects this connection.
The credential you are about to present, and every later request from this profile, cross the network in clear text.
A redirect the deployment might send to an https:// address would not change that: the credential is already on the wire by then.

Sign in over an unprotected connection anyway? [y/N]:
```

The redirect sentence is the part worth taking seriously. `mfctl` never follows a redirect — that is what stops a
request carrying a bearer credential from being moved to an address you did not name — and
[the redirect this endpoint serves](#redirecting-mfctl-after-you-configure-tls) protects the *next* request rather than
the one that arrived. So the question is asked from the address alone, before anything is sent, and a deployment that
would have answered `308` never gets to answer it. Sign in to the `https://` address instead wherever there is one.

Accepting is recorded on the profile and widens nothing else: a clear-text profile that later answers over HTTPS with
an untrusted certificate is still refused.

### Signing in with nobody at the terminal

`--mode key` reads the credential from standard input, so a piped sign-in has no terminal to read an answer from. Both
questions are therefore stated up front instead, and a sign-in that needed one and did not get it fails naming the
switch rather than prompting into the pipe:

```console
$ printf '%s' "$MAILFATHOM_KEY" | mfctl login \
    --endpoint https://mail.internal.example:8443 --trust-untrusted-certificate
```

| Switch | What it accepts |
| --- | --- |
| `--trust-untrusted-certificate` | Whatever certificate the deployment presents at this sign-in. It is pinned to the profile exactly as an interactively accepted one is, so the switch weakens the one sign-in rather than the profile it produces. |
| `--allow-clear-text` | That an `http://` endpoint carries the credential and every later request unprotected. |

There is deliberately no fingerprint to pass: somebody who had to obtain the fingerprint first could have installed the
certificate instead. Neither switch has any effect on a deployment whose transport is already protected.

Nothing on the service side changes for any of this, and no configuration key turns certificate validation or
clear-text protection off globally. These are the client's decisions about one deployment.

## How long an OAuth sign-in lasts

An access token is typically minted for an hour, and you should never notice. Every command checks the stored token
before it sends anything and exchanges the refresh token for a new one when it is within a minute of expiring, which is
what keeps that hour from being an hourly interruption.

**Whether there is a refresh token at all is the deployment's decision**, taken by advertising `offline_access` on the
OAuth entry serving this endpoint — see [with OAuth](#with-oauth) above. The command asks for exactly the scopes the
metadata document lists and adds nothing of its own, so a deployment that advertises the scope gives every client a
renewable session and one that does not gives none of them one.

**The refresh token itself is never renewed, and a rotated one is not adopted.** When the authorization server answers a
renewal with a new refresh token, the command keeps the one issued at sign-in and discards the new one. That is
deliberate: adopting it would make your session last as long as you kept using it, and revoking your access at the
authorization server would then take effect only whenever you happened to stop.

The service does the opposite with *its* OAuth credentials, and the difference is the point rather than an
inconsistency. A synchronizing account is a headless process that must keep reading a mailbox indefinitely with nobody
there to sign it in, so it [follows a rotated refresh token](mailbox-oauth.md) and stores it. A `mfctl` session belongs
to a person who is present, can sign in again in seconds, and whose access someone may need to revoke — so it ends.

The cost is worth stating plainly, because it depends on a setting that is not MailFathom's. **On an authorization
server that invalidates the old refresh token when it rotates one — Keycloak and Entra ID do this by default — the
session ends at the second renewal rather than at the refresh token's own expiry.** It ends cleanly, naming what
happened:

```console
$ mfctl status
The sign-in has ended: the authorization server no longer accepts the stored refresh token ('invalid_grant').
Run 'mfctl login --endpoint <address>' to sign in again.
```

If that is too short for how you work, turn refresh-token rotation off for this client at the authorization server. The
session then runs to the refresh token's configured lifetime, which is the length your identity platform already
governs — and which is the only place that decision belongs, since MailFathom issues no tokens at all.

## Working with more than one deployment

Every profile is a deployment you are signed in to, and one of them is the one commands act on.

```console
$ mfctl profiles
In use  Profile     Endpoint                           Credential
*       production  https://mail.example.test:8443     workstation
        staging     https://staging.example.test:8443  workstation

$ mfctl switch staging
Now acting on 'staging' (https://staging.example.test:8443) as 'workstation'.
```

`--endpoint` overrides the selection for one invocation without changing it, and takes either a profile name or an
address:

```console
$ mfctl status --endpoint production
'production' (https://mail.example.test:8443) accepts the stored credential as 'workstation' (MailFathom 0.2.0).
Documentation for that version: https://krzysztof318.github.io/MailFathom/v0.2.0/
```

The order is the option, then `MAILFATHOM_ENDPOINT`, then the profile last switched to: what you typed beats what your
shell was told, and both beat what you chose last time. `status` is what asks a deployment whether the stored credential
still works, which is how a revoked or expired key is distinguished from an unreachable host.

The documentation line names the **deployment's** version rather than the command's. The two are separate builds — that
is the whole reason `status` reports the deployment's version at all — and a command from one release line pointed at a
deployment on another would otherwise name pages for something nobody is running. A deployment on the nightly channel
resolves to `latest`, which is what a nightly carries, and a deployment reporting a version the command cannot read is
told nothing about documentation at all: that is the same absence of evidence the version check warns on rather than
acts on, and naming a directory for it would be a guess printed as a fact.

`mfctl logout` forgets one profile — the selected one, or whichever `--endpoint` names. It does not revoke anything: the
credential stays valid until the deployment stops accepting it. Forgetting the selected profile leaves none selected
rather than promoting a neighbour, so the next command asks which deployment you mean instead of quietly reaching a
different one.

Every command that needs a credential and has none says so, and says what to run:

```console
$ mfctl status
Not signed in. Run 'mfctl login --endpoint https://host:port' first.
```

## Where the credential is kept

| Platform | Path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/MailFathom/credentials.json`, or `~/.config/MailFathom/credentials.json` |
| Windows | `%APPDATA%\MailFathom\credentials.json` |

One entry per profile, keyed by the name rather than by the address, so a deployment that moves port or gains a domain
keeps its profile instead of becoming a second entry. On Linux the file and its directory are created owner-only, and
created that way rather than tightened afterwards — a file created readable and corrected later is readable for the
moment in between.

**Tokens are encrypted in the file** with AES-256-GCM, under a random key generated on first use and kept beside the
store as `credentials.key`; on Windows that key file's contents are additionally wrapped with DPAPI under the current
user. Each token is bound to its own endpoint, so a value moved between entries does not decrypt.

An OAuth profile holds a refresh token as well, sealed the same way and bound to the same endpoint — it is the
longer-lived of the two secrets, so anything weaker would be a regression in the value most worth protecting. Beside it
sit the values a renewal needs and that are not secrets: the token endpoint, the issuer, the client identifier, the
resource, the scopes, and when the access token expires. They are recorded rather than rediscovered because a renewal
happens on a command somebody is waiting on, and re-reading two discovery documents to spend a refresh token would put
two more round trips in front of every expired session. A deployment that moves one of them is answered by signing in
again.

**A key-pair profile stores no credential at all.** It records the absolute path of the private key and nothing else, so
there is no sealed token in the file and nothing an attacker could present even if the key file's protection failed. The
path is not a secret and is stored in clear; what it names is, and it is protected by that file's own permissions rather
than by anything here.

**A profile that accepted something about its transport records that too**, beside the endpoint and in clear: the
pinned certificate's SHA-256 fingerprint, and whether the connection is unprotected. Neither is a secret — a fingerprint
is what the deployment presents to anybody who connects — and what they protect is that the profile keeps talking to the
same deployment. A profile that accepted nothing beyond the default records nothing, so the presence of the entry is
itself the statement that something was accepted, and a file written before the entry existed reads as an ordinary
profile rather than failing.

Be clear about what that buys. A credentials file that leaves the machine — in a backup, a synced folder, a support
bundle, a screenshot of a directory listing — discloses nothing on its own. Someone already able to read your files on
your machine can read the key too, and on Linux nothing prevents that; the file mode is what answers that case, and the
encryption answers the copy. Holding the credential in the platform's own secret service is tracked as
[#318](https://github.com/Krzysztof318/MailFathom/issues/318).

## What the command records about itself

Every invocation appends one line to a log beside the credential store, and that file is the only durable record of
what `mfctl` did. The command holds no exporter and opens no span, so once your terminal's scrollback is gone nothing
else on the machine answers *what did I run against that deployment, when, and how did it end*.

| Platform | Path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/MailFathom/mfctl.log`, or `~/.config/MailFathom/mfctl.log` |
| Windows | `%APPDATA%\MailFathom\mfctl.log` |

One record per line, as JSON, so `tail`, `grep`, and `jq` each work on it without a parser that spans lines:

```console
$ tail -1 ~/.config/MailFathom/mfctl.log
{"at":"2026-08-17T09:41:22.184+00:00","command":"mfctl contact delete","outcome":"Failed","durationMilliseconds":412,"exitCode":1,"deployment":"production","failure":"The deployment answered 404 rather than a contact."}
```

`command` is the path of names `mfctl` declares and `deployment` is your own name for the profile the command settled
on. A field the invocation has nothing for is left out rather than written as a null.

`outcome` is one of four, and the last two are what makes this file worth having when something goes wrong:

| `outcome` | What happened | What else the record carries |
| --- | --- | --- |
| `Completed` | The command did what you asked | `exitCode` `0` |
| `Failed` | Something you can act on, already printed to your terminal | `exitCode` `1`, and `failure` with that same line |
| `Faulted` | The command raised something that is a defect rather than your mistake | `fault` with the type of it, and no exit code |
| `Cancelled` | You stopped it before it finished | no exit code |

`fault` is the type's name and nothing else about it — not the message, which is written for whoever will fix the
defect and quotes what the code was working on, and not the stack, which is frames rather than data but would end the
one-record-per-line shape. The stack itself went to your terminal, so what the log adds is that the crash happened at
all, when, under which command, and what kind it was.

**No credential and no mail is in it.** A credential never reaches a failure message in the first place, because those
are written to be shown on your terminal; and a contact, an address, or a subject a command printed for you is the
answer to that command rather than a fact about running it, so none of it is offered to the log at all.

**Your own deployment can be named in it.** `command` carries no argument value, but the other two fields are not blind
to where a deployment is. `deployment` is your name for the profile, and a sign-in that passed no `--name` is named
after the deployment's own host. `failure` is the line the command already printed, and several of those quote the
address or the alias you typed — `Not signed in to https://…` is the common one. Scrubbing both was considered and
rejected: this file sits beside `credentials.json`, which records every profile's endpoint in clear, so a log naming
none of them would be protecting an address the same directory already holds, at the cost of the field you read the log
for. Treat the file as you treat that directory, which is to say read it before you paste it anywhere.

It is created readable by its owner alone, on the same terms and for the same reason the credential store is, and it is
bounded at one mebibyte: past that the current file becomes `mfctl.log.1`, replacing whatever was there, and a new one
starts — so the log occupies at most two mebibytes however long you administer a deployment for. A recorded failure is
bounded as well, so one record stays one line. There is no retention policy beyond that, because retention for files on
your own machine is yours to decide rather than this command's.

Turn it off for one invocation with `--no-log`, which is accepted after the subcommand as well, and for a shell session
with `MAILFATHOM_LOG=off`. What you typed beats what your shell was told, and the default is on; every other value of
the variable leaves the log on rather than failing a command over a typo in it.

A record that cannot be written — a read-only home directory, a full disk — is reported as one line on standard error
and changes nothing else. The command's exit code and its own output stay exactly what they would have been, because
the command's job is the command. Deleting the directory is not one of those cases: every append recreates it, so
removing the log is a way to start a new one rather than a way to turn it off.

## Troubleshooting

| What you see | What it means |
| --- | --- |
| `Not signed in.` | No profile exists yet. Run `mfctl login --endpoint https://host:port`. |
| `No default profile is set.` | Profiles exist but none is selected, which is what forgetting the selected one leaves behind. Run `mfctl switch <name>`. |
| `There is no profile named …` | A typo, or a profile that was never created. The message lists the ones that exist. |
| `Not signed in to https://…` | `--endpoint` named an address no profile serves. Sign in to it, or name a profile instead. |
| `The deployment refused the credential.` | The key is not one this endpoint is configured with, or its lifetime has ended. Note that an MCP API key is not one of them. |
| `answered 429` | The endpoint refused the request for its rate limit rather than for its credential. `Retry-After` on the response says when capacity returns where the limiter can compute one. The whole endpoint shares one bucket, so another caller's burst — including somebody guessing keys — is enough to cause this. |
| `serves no administrative endpoint at /api/admin/…` | The address answered, but on a listener that serves something else. Check the port, and check that `AdminEndpoint:Enabled` is true. |
| `This deployment configures no mail account named …` | `mailbox authorize --account` named an identifier no `MailSynchronization:Accounts` entry carries, or you are signed in to the wrong deployment. Nothing was stored. |
| `is still mirrored, so erasing it would only cost a remirror` | `folder erase` named a folder the account still synchronizes, and nothing was erased. Switch that folder's `Synchronize` off, or remove its mapping, and ask again. |
| `before the erasure was interrupted` | `folder erase` was stopped part way. What it reported erasing is gone and the rest is still there; run the same command again to continue from where it stopped. |
| `The deployment refused the grant without saying why.` | The request was refused with no reason in the answer, which is what something in front of the endpoint answering `400` looks like. Check that `--endpoint` reaches the deployment itself. |
| `rather than storing the token` | The endpoint answered with neither an acceptance nor an explained refusal. The token was not stored and the account is unchanged. A `500` here is most often a deployment with no `DataEncryption` key ring, which is what a stored token is sealed under; its own log names the cause. |
| `did not identify itself as MailFathom` | Something else is answering on that port — a proxy, or another service. |
| `refused rather than sent to a deployment from another release line` | The command and the deployment are from different `major.minor` releases, and [nothing was sent](#take-the-command-from-the-deployments-own-release-line). Take the command from the deployment's own release, or upgrade the deployment. |
| `not the same build and problems may occur` | The two share a release line and so agree on the administrative contract, but are different builds of it — a patch apart, or one of them a nightly. The command ran. |
| `is unchecked` | One of the two reported a version that could not be read, which is what an unstamped or locally built binary looks like. The command ran, and whether the two agree is unknown. |
| `could not be reached` | Nothing is listening, or a firewall is in the way. The endpoint binds only what `BindAddress` names; `127.0.0.1` is unreachable from another machine by design. |
| `presented a certificate this machine does not trust` | On `login`, the question described in [when the connection is weaker than the default](#when-the-connection-is-weaker-than-the-default). On any other command, a profile that holds no pin met a certificate that stopped validating — sign in again to review it. Nothing was sent either way. |
| `presented a certificate this profile has not pinned` | The deployment's certificate is not the one this profile accepted. Both fingerprints are named. A renewal is the ordinary cause and `mfctl login` is the answer; anything else is worth finding out about before you accept it. |
| `The deployment's certificate was refused` | You answered no. Nothing was signed in to and nothing was stored. |
| `Transport protection was refused` | You answered no to the clear-text question. Sign in to the `https://` address, or accept the unprotected connection. |
| `there is no terminal to ask on` | A piped or non-interactive `login` met one of the two questions. Pass `--trust-untrusted-certificate` or `--allow-clear-text`, whichever the message names. |
| `did not answer in time` | The connection was accepted and no answer arrived within 30 seconds, so the address and the port are right and the deployment is what to look at — an overloaded host, a stalled process, or a firewall that drops rather than refuses. |
| `The stored credential could not be read.` | The credentials file and the key that opens it no longer match, which is what a store copied from another machine or another user looks like. Sign in again to replace it. |
| `No deployment was named.` | `login` needs an address the first time. Pass `--endpoint`, or set `MAILFATHOM_ENDPOINT`. |
| `The sign-in has ended` | The refresh token expired, was revoked, or was invalidated by a server that rotates them. Run `login` again. This names the *stored* token, so it only ever appears on a command that had a session; a `login` that fails names what it presented instead. |
| `did not accept the code the redirect carried` | The authorization code was already redeemed or had expired by the time it was exchanged, which is what a redirect answered twice or approved long after it was opened looks like. Run `login` again. |
| `The device code is no longer valid` | Nobody finished at the verification address before the code expired, or the authorization server withdrew it. Run `login --mode device` again. |
| `not a usable web address` | The authorization server published a `verification_uri` that is not an absolute `http` or `https` address, so there is nothing to put in front of the person signing in. This is a fault at the authorization server rather than in its configuration here. |
| `publishes no OAuth metadata` | The endpoint accepts API keys only. Sign in with one, or ask the operator to add an `OAuth` entry to `AdminEndpoint:Authentication`. |
| `accepts tokens from several authorization servers` | More than one is configured and only you know which population you belong to. Name it with `--issuer`. |
| `issued no refresh token` | Nothing asked for offline access, so the session would end within the hour. Two settings can be missing: the deployment advertising `offline_access` in [`AdvertisedScopes`](mcp-endpoint.md#scopes-you-advertise-but-do-not-require), and the client being granted the scope at the authorization server. Check the metadata document first — if `scopes_supported` does not list it, nothing asked. |
| `no device authorization endpoint` | That authorization server offers no device grant. Sign in from a machine with a browser. |

## Related

- [MCP endpoint](mcp-endpoint.md) — the other protected surface, and the one this is deliberately separate from
- [Secret provisioning](secret-provisioning.md) — how an API key reference is backed by material
- [Configuration reference](configuration-reference.md) — every key in the `AdminEndpoint` block
