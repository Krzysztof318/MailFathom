# Changelog

All notable changes to MailFathom are recorded here, in the format of
[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) as
[ADR 0004](docs/decisions/0004-versioning-and-release-policy.md) interprets it over MailFathom's four public surfaces:
the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

**It is written for whoever runs MailFathom** — the person installing it and the administrator keeping it running — and
every section answers the same question before an upgrade: what is new for you, what was fixed, what breaks, and what
you have to do about it. So what earns an entry is what you would notice: anything reaching one of the four surfaces, a
fixed defect that was observable from outside, and any change with a security consequence. A refactor, a test, a
continuous-integration adjustment, a documentation edit, and an internal rename earn none, and nothing below is written
in the terms of the code that produced it.

A breaking entry opens with `**Breaking (<surface>)**` and states the operator's action rather than only the fact. A
release that touches the database schema says whether a migration must be applied, whether it can be applied while the
previous version is still running, and whether the release can be deployed over the previous release's data at all.

MailFathom is pre-release. Within `0.x` a minor bump may break any of the four surfaces, and every break is named
below against the surface it breaks; a patch is compatible on all four. There is no `Unreleased` heading, and neither
nightly nor prerelease builds get a section of their own: what a nightly carries is, by definition, whatever has been
merged since the newest section below.

**This file is written by the release pull request and by nothing else.** Ordinary work does not touch it — not a
feature, not a fix, not a refactor — because a changelog is a statement about a *release*, and a release is what the
tagged and published pull request makes. `$prepare-release` composes each section from the work merged since the
previous tag, and that same pull request is the one whose merge commit is tagged and published to the container
registries. `CHANGELOG.md` is a protected path for the same reason: an edit to it outside that flow changes what a
release claims it shipped.

## [0.7.0] - 2026-08-20

The seventh release, and the first one that **sends mail**. Until now everything an agent could reach either read your
local copy or changed a flag on your own server; from here it can compose a message and have MailFathom offer it to a
submission server. It can answer mail you already hold, forward one on, write a message into your Drafts folder and
leave it there for you, and mark, star, or label mail. The tool surface goes from five to twenty-one, and MailFathom
stops being a read-only window on a mailbox.

**Everything that leaves the deployment is off until you turn it on.** Sending is per account and disabled everywhere by
default, an account enabled with no submission host fails startup, `Deployment:ReadOnly` refuses every send whatever any
account says, and a recipient policy and a send ceiling bound who may be written to and how much may leave in a period.
Nothing a caller supplies decides who a message is from, no send is performed while the caller waits — each is written
down, answered with a record identity, and offered by a delivery pass that survives a crash — and the seconds before it
leaves are the only window in which it can be withdrawn.

**The other half of the release is who may ask.** Every `Authentication` entry can now state what it grants, as a list of
named permissions, and the check runs in the use case rather than only at the door: a tool a caller cannot reach is not
even listed to it, and an administrative route refuses the same way. An endpoint can also publish only some kinds of
tool at all, so a deployment can offer reading and drafting while withholding sending from every credential.

**And MailFathom now records more about the mail it stores.** Who authenticated a message's sender and whether this
deployment recognizes that author, how much the message's own text reads as machine written, the keywords your server
holds for it, and the conversation it belongs to — all published on every read tool's result. Beside them is a contact
book of MailFathom's own: people rather than addresses, held in your database, filled by hand or from the mail an
account corresponds with.

**Three things need an edit before this release serves what `0.6.0` served, and every one of them fails quietly rather
than loudly.** **Write `Permissions` on every `Authentication` entry**, because an entry that names none holds
everything its surface publishes — so on upgrade it gains the sixteen new tools, sending among them, and a credential
you meant to be able to read your mail can now send from it to anybody. **Rename
`SpamClassification:Actions:FileInJunkFolder` to `MoveToJunkFolder`**, or junk stops being filed and nothing says so.
**Rewrite `SensitiveContent:PersonalDataAnalyzer:Language` as the list `Languages`** —
`personalDataScanning.languages` in the Helm chart, `SensitiveContent__PersonalDataAnalyzer__Languages__0` in Compose —
or the analyzer silently falls back to English.

**One default moved and asks nothing of you.** `SensitiveContent:ScanTimeout` now defaults to fifteen seconds where it
defaulted to five, which is a longer wait before a scan is refused and not a key to edit.

**Mail stored by an earlier release keeps the answers it was given.** The sender verdict, the trust verdict, the
machine-authorship reading, the keywords, and the threads are derived while a message is synchronized, so the migrations
fill every existing row with the not-established answer rather than a wrong one. `mfctl mailbox rederive` re-reads the
raw MIME already stored and writes the real answers back; until you run it, mail from before the upgrade reports as
nothing established.

**The database schema moves by twenty-three migrations** that add seventeen tables, thirty-six columns on tables that
already held data, and the indexes for both. Every added column carries a default, and nothing `0.6.0` reads changes —
so the schema step applies while `0.6.0` is still serving, `0.6.0` serves the result unchanged if you roll the image
back, and this release deploys over the previous release's data. Nothing else `0.6.0` promised is withdrawn: no tool was
removed, no tool argument was renamed, and every artifact a release publishes still publishes.

### Added

**Sending mail.** An account with a submission endpoint can be asked to send, and MailFathom writes the message down
before anything reaches a server: the record is created first, answered with an identity, and offered to the submission
server by a delivery pass that claims it under a lease, so a crash mid-flight leaves work that finishes by itself rather
than a message MailFathom believes it sent
([#891](https://github.com/Krzysztof318/MailFathom/pull/891),
[#900](https://github.com/Krzysztof318/MailFathom/pull/900),
[#942](https://github.com/Krzysztof318/MailFathom/pull/942)).
[Mail delivery](https://krzysztof318.github.io/MailFathom/features/mail-delivery.html) is the page, stage by stage.

- **`send_email` sends one message**, as the account it names, to the addresses the call names. An idempotency key the
  caller chooses is what makes a retry the same message rather than a second one
  ([#977](https://github.com/Krzysztof318/MailFathom/pull/977)).
- **`reply_to_email` and `forward_email` answer mail you already hold.** The caller names the message and writes the new
  words; the addressing, the subject, the headers that put it in the right conversation, the quoted original, and a
  forward's attachments are read from your stored copy rather than from anything a client supplies. Whether a reply
  answers the sender alone or everybody is required and never defaulted
  ([#920](https://github.com/Krzysztof318/MailFathom/pull/920),
  [#989](https://github.com/Krzysztof318/MailFathom/pull/989)).
- **`get_outgoing_email` and `cancel_outgoing_email` answer for a send rather than performing one** — where it has got
  to, how many attempts it has taken, what each recipient's server said, and the code it stopped on. Cancellation
  succeeds only before transmission begins, and each answers about the one identifier the caller was given: there is no
  listing of what a mailbox has sent on this surface, and another caller's send reads as not found
  ([#996](https://github.com/Krzysztof318/MailFathom/pull/996)).
- **The copy is filed where the account's own configuration says**, into the folder mapped as `Sent` where one is mapped
  ([#976](https://github.com/Krzysztof318/MailFathom/pull/976)).
- **The message is composed by MailFathom rather than by a caller.** The headers that establish identity, threading, and
  the message's own identifier are this system's; a caller supplies a subject, text, an optional HTML alternative, and
  recipients ([#911](https://github.com/Krzysztof318/MailFathom/pull/911)).
- **Sending is bounded on four independent axes.** `Delivery:Enabled` is off on every account; `Deployment:ReadOnly`
  refuses every send process-wide whatever an account holds; `MailDelivery:RecipientPolicy` names allowed and denied
  domains and addresses, and a message naming one refused recipient is refused whole rather than delivered to the rest;
  and `MailDelivery:SendCeilings` counts messages and recipients per account, per caller, and per deployment over a
  fixed period. Every ceiling is zero — no ceiling — by default
  ([#992](https://github.com/Krzysztof318/MailFathom/pull/992),
  [#998](https://github.com/Krzysztof318/MailFathom/pull/998)).
- **`MailDelivery:UnvouchedRecipients` can refuse an address nothing here vouches for.** Set to `Refuse`, an address
  the *caller* named that neither the contact book nor one of your own accounts holds refuses the whole message — which
  is the bound written for an agent that read a stranger's mail and was talked into forwarding a thread somewhere. Only
  what the caller named is judged, so a plain reply is unaffected and a forward is judged in full. It admits by default,
  because refusing by default would refuse the first message of every installation whose contact book is still empty
  ([#974](https://github.com/Krzysztof318/MailFathom/pull/974),
  [#998](https://github.com/Krzysztof318/MailFathom/pull/998)).
- **`mfctl outbox` operates it**: what stands at each stage, one bounded page of what has been queued, one message with
  who it was offered to and what each of them was told, withdrawing one, and offering one again. Delivery is
  instrumented as well — attempts, retries, outcomes, submission duration, and queue depth
  ([#995](https://github.com/Krzysztof318/MailFathom/pull/995)).

**The four draft tools — writing a message and leaving it for you.** `save_draft` writes a message into your own Drafts
folder and sends nothing; `update_draft` replaces the whole message and your folder ends up showing one draft rather
than one per edit; `delete_draft` gives one up and takes the copy back out; `send_draft` sends what a draft holds. A
draft can be a message of its own or an answer to mail you already hold, and one addressed to nobody is an ordinary
draft ([#1001](https://github.com/Krzysztof318/MailFathom/pull/1001),
[#1010](https://github.com/Krzysztof318/MailFathom/pull/1010)). **Drafting is granted apart from sending**, which is the
grant to reach for where you want a person between an agent and a recipient: a credential that can write, edit, and
delete a draft cannot make one leave.

**`set_mail_flags` — marking, starring, and labelling.** One tool marks a message read or unread, stars or unstars it,
and adds, removes, or replaces its keywords. Every value is optional and at least one is required, and the change is
written down and carried to your server by the same convergence pass every other mailbox change goes through
([#968](https://github.com/Krzysztof318/MailFathom/pull/968)). The keywords your server already holds are stored as
well, and `list_emails` and `search_emails` filter on them and on `\Flagged`
([#878](https://github.com/Krzysztof318/MailFathom/pull/878)). A rule can set them too, and can flag a message
([#924](https://github.com/Krzysztof318/MailFathom/pull/924)).

**Named permissions on every credential.** An `Authentication` entry states what it grants as `Permissions`, a list of
published names; the set is closed, so a misspelling fails startup rather than reading as a narrower grant than you
meant. A `*` written as a whole segment grants the subtree beneath it, at any position. `PermissionsFromTokenScopes`
turns the list into a ceiling instead, so an authorization server decides per subject within a bound the deployment
fixed. The check runs at the transport, in the use case behind every operation, and in what a caller is offered: a tool
it cannot reach is not listed to it. Startup records what every entry resolved to, one line each
([#874](https://github.com/Krzysztof318/MailFathom/pull/874),
[#882](https://github.com/Krzysztof318/MailFathom/pull/882),
[#904](https://github.com/Krzysztof318/MailFathom/pull/904),
[#912](https://github.com/Krzysztof318/MailFathom/pull/912),
[#925](https://github.com/Krzysztof318/MailFathom/pull/925),
[#969](https://github.com/Krzysztof318/MailFathom/pull/969),
[#994](https://github.com/Krzysztof318/MailFathom/pull/994)).
[Permissions](https://krzysztof318.github.io/MailFathom/operations/permissions.html) is the whole model.

- **Every administrative route is behind a grant too**, with the readings that touch mail separated from the readings
  that report the deployment's own state, and both from the operations that cause work
  ([#925](https://github.com/Krzysztof318/MailFathom/pull/925)).
- **A refusal is counted under the permission it was refused for**, and nothing about the caller is recorded beside it
  ([#939](https://github.com/Krzysztof318/MailFathom/pull/939)).

**An endpoint publishes only the kinds of tool it names.** `McpEndpoint:PublishedToolCategories` narrows the surface to
some of `mailbox`, `flags`, `sending`, `drafts`, `answering`, and `contacts`; naming none publishes every one, so a
deployment written before the setting keeps the surface it had. A category only ever takes away — it enables nothing and
widens no grant — and a connecting client may narrow further for its own session with the `MailFathom-Tool-Categories`
header ([#1011](https://github.com/Krzysztof318/MailFathom/pull/1011)).

**A contact book of MailFathom's own.** It holds people rather than addresses, so somebody who writes from three
addresses is one contact, and it lives in your database rather than at your mail provider. Six MCP tools read and write
it, `mfctl contacts` administers it including the export and the erasure, and switching `ContactCollection` on for an
account records the people that account corresponds with as its mail is synchronized — held to a message threshold you
set, and never a mailing list, a role mailbox, or an address you excluded. It is off on every account, and one command
takes back everything it collected. A message can be addressed by naming a contact, without that becoming a way around
the recipient policy ([#890](https://github.com/Krzysztof318/MailFathom/pull/890),
[#905](https://github.com/Krzysztof318/MailFathom/pull/905),
[#913](https://github.com/Krzysztof318/MailFathom/pull/913),
[#943](https://github.com/Krzysztof318/MailFathom/pull/943),
[#965](https://github.com/Krzysztof318/MailFathom/pull/965)).
[Contacts](https://krzysztof318.github.io/MailFathom/features/contacts.html) is the page.

**Who authenticated a message's sender, and whether you recognize them.** The verdict is read from the
`Authentication-Results` header of the one server you name as yours, in `TrustedAuthenticationServiceIdentifier`, and
from nowhere else — a header any hop could have written is not evidence. Where your server writes none,
`VerifyDkimLocally` checks the message's own signatures in this process instead, which is the one path that makes an
outbound DNS query. A second answer says whether the established author is one this deployment recognizes, from
`TrustedSenders` and from the domains your own accounts use. Both are published on every read tool's result
([#877](https://github.com/Krzysztof318/MailFathom/pull/877),
[#887](https://github.com/Krzysztof318/MailFathom/pull/887),
[#893](https://github.com/Krzysztof318/MailFathom/pull/893),
[#899](https://github.com/Krzysztof318/MailFathom/pull/899),
[#967](https://github.com/Krzysztof318/MailFathom/pull/967),
[#979](https://github.com/Krzysztof318/MailFathom/pull/979)).
[Sender authentication](https://krzysztof318.github.io/MailFathom/features/sender-authentication.html) is the page.

**How much a message's text reads as machine written.** A band and a likelihood, derived from characters in text this
deployment already holds — no model asked, no service consulted, no extra parse and no IMAP round trip — published
beside the sender verdict. **It is informational and it is not a safety signal**: nothing files, flags, hides, or refuses
a message because of it, and the only thing that can act on it is a rule you wrote.
`MailSynchronization:AssessMachineAuthorship` turns it off ([#927](https://github.com/Krzysztof318/MailFathom/pull/927)).
[Machine authorship](https://krzysztof318.github.io/MailFathom/features/machine-authorship.html) states what it weighs
and what it deliberately does not claim.

**The conversation a message belongs to.** Stored mail is assembled into threads from what was synchronized, and every
message a tool returns names its thread ([#906](https://github.com/Krzysztof318/MailFathom/pull/906)).

**Bringing stored mail up to a later release.** `mfctl mailbox rederive` re-reads the raw MIME already stored into the
properties a newer release records from it, as durable background work the deployment owns rather than a command holding
a terminal open; `mfctl mailbox rederive-status` says where it has got to
([#892](https://github.com/Krzysztof318/MailFathom/pull/892),
[#950](https://github.com/Krzysztof318/MailFathom/pull/950)). `mfctl mailbox rewind` discards an account's
synchronization progress so the next runs read its folders again, and reports what that would cost before you ask for it
([#892](https://github.com/Krzysztof318/MailFathom/pull/892)).

**`mfctl mailbox status`** reports what synchronization is doing, per account and per mapped folder
([#876](https://github.com/Krzysztof318/MailFathom/pull/876)). Its output is rendered with colour and structure
throughout ([#915](https://github.com/Krzysztof318/MailFathom/pull/915)), and what it did is recorded in a local log
beside its credentials ([#910](https://github.com/Krzysztof318/MailFathom/pull/910)).

**One command prepares a Compose deployment to evaluate with.** `scripts/quick-start-compose.sh` asks where your mailbox
lives, generates the credentials, writes the configuration, starts the stack, offers the schema step, and hands you the
address a chat client connects to. **What it prepares serves one machine over plain HTTP, keeps its credentials in files
under the checkout, and backs nothing up** — it prints that list when it finishes, and it is the quick way to try
MailFathom rather than the way to run it ([#971](https://github.com/Krzysztof318/MailFathom/pull/971)).

**A rule condition can read what the release added**: the keywords, both sender verdicts, and the machine-authorship
band ([#938](https://github.com/Krzysztof318/MailFathom/pull/938)).

**The personal-data analyzer is asked in every language your mailbox carries**, up to eight, rather than in one.
`SensitiveContent:PersonalDataAnalyzer:Languages` names them; a scan asks once per language and merges what came back,
inside the one timeout it is allowed. Every language named has to be one the analyzer image was built for
([#875](https://github.com/Krzysztof318/MailFathom/pull/875)).
[Analyzer languages](https://krzysztof318.github.io/MailFathom/operations/personal-data-analyzer-languages.html) states
what building such an image takes.

**A trace reads as a tree.** Every unit of work is spanned, the interior of a folder run and of an MCP read included, and
a job attempt is linked to the trace that enqueued it
([#907](https://github.com/Krzysztof318/MailFathom/pull/907),
[#932](https://github.com/Krzysztof318/MailFathom/pull/932)).

### Changed

- **Breaking (configuration schema)** — `SpamClassification:Actions:FileInJunkFolder` is now
  `SpamClassification:Actions:MoveToJunkFolder`. The old key is ignored rather than refused, so a file that keeps it
  stops filing junk and nothing reports it. **Rename the key**
  ([#956](https://github.com/Krzysztof318/MailFathom/pull/956)).
- **Breaking (configuration schema, deployment contract)** — `SensitiveContent:PersonalDataAnalyzer:Language` is now the
  list `SensitiveContent:PersonalDataAnalyzer:Languages`. The old key is ignored rather than refused, and an absent list
  yields English, so a deployment that scanned in another language silently stops. **Rewrite it as a list**:
  `personalDataScanning.languages` in the Helm chart, and
  `SensitiveContent__PersonalDataAnalyzer__Languages__0` in Compose, where a second language is a second indexed line
  rather than a value in `.env` ([#875](https://github.com/Krzysztof318/MailFathom/pull/875)).
- **Breaking (configuration schema)** — an `Authentication` entry that writes no `Permissions` key holds everything its
  surface publishes, so on upgrade it gains the sending grant, the drafting grant, the flag grant, and both contact
  grants. A written `mailfathom.mail.*` pattern gains them too. **Write the grant you mean on every entry**
  ([#882](https://github.com/Krzysztof318/MailFathom/pull/882),
  [#994](https://github.com/Krzysztof318/MailFathom/pull/994)).
- **The personal-data analyzer no longer has to answer before the process starts.** MailFathom starts, reports itself
  unready on `/health`, and refuses every read and derived write the scanner guards until the analyzer answers, instead
  of failing startup and restarting until a model has loaded. Naming no analyzer address at all still fails startup. In
  Kubernetes it is a readiness question rather than a liveness one, so a pod is not restarted for it
  ([#853](https://github.com/Krzysztof318/MailFathom/pull/853)).
- **`SensitiveContent:ScanTimeout` defaults to fifteen seconds** where it defaulted to five. The old default refused
  ordinary messages on ordinary hardware ([#866](https://github.com/Krzysztof318/MailFathom/pull/866)).
- **`ask_mail` answers in the language the question was asked in**, and looks mail up in the language it was written in
  ([#861](https://github.com/Krzysztof318/MailFathom/pull/861)).
- **Mail deleted at the server is reported at information level** rather than as a warning: it is the ordinary outcome of
  somebody tidying a mailbox ([#964](https://github.com/Krzysztof318/MailFathom/pull/964)).
- **The configuration reference is four pages** — mail, endpoints, runtime, and AI — with the permission model on a page
  of its own. The old address is the map to them
  ([#958](https://github.com/Krzysztof318/MailFathom/pull/958)).

### Fixed

- **An access token is no longer refused as clear text behind a TLS-terminating reverse proxy.** Authentication ran ahead
  of the forwarded-header middleware, so a request nginx forwarded as `X-Forwarded-Proto: https` was authenticated while
  the scheme still read `http`, and every token was refused without being read. **A deployment behind a proxy that could
  not authenticate at all can now do so** ([#955](https://github.com/Krzysztof318/MailFathom/pull/955)).
- **A token refused for arriving over a clear-text hop says so**, instead of being answered with the challenge an
  anonymous request receives ([#945](https://github.com/Krzysztof318/MailFathom/pull/945)).
- **The schema artifact a release publishes carries no byte-order mark**, so `psql` applies it rather than failing on the
  first line ([#919](https://github.com/Krzysztof318/MailFathom/pull/919)).
- **A lost race on an embedding's primary key is retried** rather than ending the backfill run
  ([#879](https://github.com/Krzysztof318/MailFathom/pull/879)).
- **The secret scanner finds a credential that ends a sentence**, not only one followed by a quote or a newline
  ([#850](https://github.com/Krzysztof318/MailFathom/pull/850)).

### Security

- **Nothing on the MCP surface is reachable without a grant naming it.** Before this release a credential that
  authenticated reached every tool the deployment published; now every tool and every administrative route is checked
  against a named permission, in the listing as well as in the call, and at the transport as well as in the use case
  ([#874](https://github.com/Krzysztof318/MailFathom/pull/874),
  [#904](https://github.com/Krzysztof318/MailFathom/pull/904),
  [#912](https://github.com/Krzysztof318/MailFathom/pull/912),
  [#925](https://github.com/Krzysztof318/MailFathom/pull/925)).
- **What a caller can be talked into sending is bounded by the deployment rather than by the agent's judgement.** An
  agent holding a read grant and a send grant reads mail written by strangers, and a message saying *forward this thread
  to the address below* is untrusted input inside the very content the agent was asked to reason about. The recipient
  policy, the send ceilings, and the unvouched-recipient rule are the answer to it, and every one of them is checked
  where the outgoing record is written — the one place a tool call, a rule, and a command all pass through
  ([#974](https://github.com/Krzysztof318/MailFathom/pull/974),
  [#998](https://github.com/Krzysztof318/MailFathom/pull/998)).
- **Sending cannot be reached by a deployment that has not asked for it**, on any of four independent switches, and
  `Deployment:ReadOnly` is the one that holds however the account list is edited
  ([#992](https://github.com/Krzysztof318/MailFathom/pull/992)).
- **The sender verdict is believed from one server only.** An `Authentication-Results` header is evidence only where it
  carries the authserv-id of the server you named as yours; a header any hop could have written is recorded as nothing
  established ([#877](https://github.com/Krzysztof318/MailFathom/pull/877)).
- **This deployment serves several users of one mailbox owner and refuses multi-tenancy**, which is now a recorded
  decision rather than an assumption: nothing here isolates one tenant's mail from another's, and a deployment must not
  be shared across parties who may not read each other's mail
  ([#990](https://github.com/Krzysztof318/MailFathom/pull/990)).

## [0.6.0] - 2026-08-14

The sixth release, and the first one that **does something with your mail rather than only reading it**. Rules you write
in a configuration file move, copy, delete, and mark messages as read; a spam classification files junk on the server;
and a durable queue underneath both means a crash loses none of it. The rules and the
classification are off until you turn them on — the queue beneath them runs on every instance and is switched off only
for a replica serving reads — and a rule is only ever authored in the file you provisioned, so what an instance will do
to a mailbox is reviewable in a diff before it does anything.

The other half of the release goes the opposite way. **A message's text can be redacted before anything is derived from
it and before any of that text leaves this deployment**: secrets are found in this process, personal data by an analyzer
you run beside it, and what MailFathom then chunks, embeds, retrieves, and returns is the redacted text. **An
attachment's content is not text a scan reaches** — the signed link serves the file exactly as it was stored, so a
credential inside an attached file is not covered by turning a scanner on. That is off by default as well, and off it
costs nothing at all — no container is started, no image is pulled, and no memory is held.

**Seven things need an edit before this release serves what `0.5.0` served.** The folder argument of `list_emails`,
`search_emails`, and `ask_mail` is `folders` where it was `folderAliases`, and the old spelling is ignored rather than
refused — so a client that keeps sending it reads every folder instead of the one it named. Every folder you want read
is now named in configuration, and mail under an alias your file no longer names is unreachable until an entry names it
again. `get_email_content` hands back a signed link per attachment instead of base64: a call asks for the links with
`includeAttachmentDownloadLinks` where `0.5.0` asked with `includeAttachmentContent`, the old name is ignored rather
than refused, and no link is issued at all unless `Deployment:PublicBaseAddress` is declared. **Delete
`EmailContent:MaxAttachmentBytes` and `EmailContent:MaxAttachmentBytesPerRead` from your configuration file**, or the
host refuses to start on a key it no longer knows. Mail in a folder your configuration maps as junk is withheld from
listing and search unless the call asks for it, and withheld from answering with no way to ask. And a folder whose
`Synchronize` you switch off now keeps its stored mail instead of erasing it.

**The database schema moves by twelve migrations** that add eight tables, four columns on three tables that already held
data, and the indexes for both, and that change nothing `0.5.0` reads — so the schema step applies while `0.5.0` is
still serving, `0.5.0` serves the result unchanged if you roll the image back, and this release deploys over the
previous release's data. Nothing else `0.5.0` promised is withdrawn: every setting not named below still means what it
meant, no tool was removed, and every artifact a release publishes still publishes — the image, the chart, the schema
script, and an `mfctl` binary per platform, Windows included. What is paused is the `winget` *submission*, which has
never produced a package: the two already open are waiting for the community repository's review.

### Added

**Mail rules — what should happen to a message, written in your configuration file and applied to your mailbox.** A
rule names the accounts it applies to, one condition over the message, and what a match asks for; a match moves the
message to a folder, copies it, deletes it, or marks it as read, and MailFathom's convergence pass carries the change
to the server the way every other change is carried, so a restart neither loses it nor repeats it
([#712](https://github.com/Krzysztof318/MailFathom/pull/712),
[#725](https://github.com/Krzysztof318/MailFathom/pull/725)).
[Mail rules](https://krzysztof318.github.io/MailFathom/features/mail-rules.html) is the page, condition by condition.

- **The condition is one expression over twenty-two facts about the message** — the account alias, the folder alias and
  the role that folder plays, the subject, the sender's address and domain, the recipient addresses and domains, when it
  was received and sent, its age, its size, the attachment count and bytes, seven flags the server or the extraction
  reported, and the body text — with seven functions and the ordinary operators. Anything outside that set is refused when the file is read rather than at
  the moment a message meets it, and a rule set with three mistakes reports all three at once
  ([#696](https://github.com/Krzysztof318/MailFathom/pull/696)).
- **A rule runs on the occasions it declares.** `MailRules:Rules:<n>:Triggers` names them: `Arrival` for mail as it is
  synchronized, `Schedule` with a `Schedule` beside it for a recurring pass, and neither for a rule only an operator
  starts ([#727](https://github.com/Krzysztof318/MailFathom/pull/727),
  [#820](https://github.com/Krzysztof318/MailFathom/pull/820)).
- **Each account states which of the four actions a rule may ask of it** under `RuleActions`, with deletion opt-in and
  the three reversible actions opt-out. A rule asking for a refused action fails startup naming the rule, the action,
  and the account ([#725](https://github.com/Krzysztof318/MailFathom/pull/725)).
- **No IMAP command a rule asks for leaves the pass, and the pass touches no `\Seen` flag itself.** Every change a match
  asks for is written down and carried by the account's convergence pass, the way every other change to a mailbox is.
  The one thing the pass does reach a mail server for is finding a destination folder the account maps and does not
  mirror, and only where a rule files into one; everything it reads about the mail was already stored, so no MCP read
  waits on it however long it takes. It is a step of the account's own synchronization run, after the classification and
  in front of the passages being cut.
- **`mfctl` runs the rules and explains what they did.** `mfctl rules list` and `mfctl rules show` state which rules are
  loaded, in the order they run, and what fires each; `mfctl rules run` applies them to mail that arrived before them
  and returns at once rather than holding the terminal open; `mfctl rules run-status` says where that run has got to;
  and `mfctl rules history` answers why a message is where it is, one row per rule per message, recording that a
  condition read `senderDomain` and never what the domain was
  ([#728](https://github.com/Krzysztof318/MailFathom/pull/728)).
- **An edit takes effect on reload, and an invalid one changes nothing** and is reported instead of disappearing. A run
  under way stays on the rule set it started with and reports itself superseded rather than half-applying two
  ([#682](https://github.com/Krzysztof318/MailFathom/pull/682)).

**Spam classification, and filing junk on the server.** The verdict is read from what the message already carries: the
provider's own `X-Spam-*` headers, and the folder it arrived in, which outranks them because it is a decision somebody
already acted on. **The authentication results, the ARC chain included, are recorded beside the verdict as signals
rather than judged from** — a DMARC failure is something your receiving server saw and chose to deliver anyway, so
turning it into a spam verdict here would file mail your own provider decided to accept
([#731](https://github.com/Krzysztof318/MailFathom/pull/731)). `SpamClassification:Enabled` turns it on, and it is off.
**It covers the folders `SpamClassification:ScannedFolders` names**, and where you name none, every account's inbox and
nothing else — so a deployment whose own filter delivers somewhere other than the inbox names that folder there, or
gets no verdict for the mail in it.
[Spam classification](https://krzysztof318.github.io/MailFathom/features/spam-classification.html) is the page.

- **An Apache SpamAssassin daemon beside the service scores what the headers cannot**, deployed only where
  `SpamClassification:UseScanner` is on: the Helm chart renders no workload for it, the Compose deployment keeps it
  behind an inactive profile, and the Quadlet unit is a file you never copy. Its DNS blocklists are **off**, because
  those rules send the sender addresses and link hosts out of your mail to third-party lists
  ([#777](https://github.com/Krzysztof318/MailFathom/pull/777)).
- **What a verdict may do is two switches, both off.** `SpamClassification:Actions:FileInJunkFolder` files the message
  into the account's junk folder and `MarkAsRead` marks it read, each through the same durable change record a rule
  uses ([#779](https://github.com/Krzysztof318/MailFathom/pull/779)).
- **Arriving mail is classified as it is stored**, as a queued job retried per message, so one unreachable scanner
  delays one message rather than the whole account ([#826](https://github.com/Krzysztof318/MailFathom/pull/826)).
- **`mfctl spam run` classifies a whole mailbox, and its default posture is a dry run** — the first thing you do with a
  scanner is find out what it would do. `mfctl spam run-status` follows the walk and `mfctl spam classifications` reads
  the verdicts back ([#795](https://github.com/Krzysztof318/MailFathom/pull/795)).
- **Junk is withheld from everything derived from it.** Where classification is on, a message it calls spam — and one the
  receiving server already filed in junk — is never cut into passages, never embedded, never sent to an embedding
  provider, and never offered to the rule set. **A message still waiting for a verdict is held back only until
  `SpamClassification:ClassificationWait` expires**, fifteen minutes unless you say otherwise, so a wedged scanner or a
  deep queue does not stall the index: past the wait the message is derived from like any other, and a spam verdict
  arriving afterwards discards the passages and vectors it produced, in the transaction that records the verdict. **A
  shorter wait therefore costs more**, since more unscored mail is embedded and then stripped — budget the provider spend
  accordingly if you lower it or run the scanner near its limit. Nothing else is written down,
  so dragging a message out of junk in any mail client is the whole of the correction
  ([#805](https://github.com/Krzysztof318/MailFathom/pull/805)).

**Sensitive-content scanning: mail redacted before it is derived from or handed out.** Two switches under
`SensitiveContent`, both off, and each finding is replaced by `[redacted:<category>]` — the category and nothing else,
so no length and no surviving prefix narrows what stood there
([#687](https://github.com/Krzysztof318/MailFathom/pull/687)).
[Sensitive-content scanning](https://krzysztof318.github.io/MailFathom/features/sensitive-content-scanning.html) states
the categories and what turning each on costs a search.

- **`Secrets` runs in this process**, over a 204-rule corpus assembled from `Microsoft.Security.Utilities.Core`, the
  gitleaks rule data, and three shapes both of those miss because both are written for source control: a database
  connection URI, a connection string's password, and a link whose query string is the credential
  ([#701](https://github.com/Krzysztof318/MailFathom/pull/701)).
- **`Pii` reaches a Presidio analyzer you deploy beside the service**, mapped onto eleven categories you can suppress by
  rule without switching a category off. Turning the switch on with nowhere to ask **fails startup** rather than running
  unprotected ([#724](https://github.com/Krzysztof318/MailFathom/pull/724)).
- **Everything stored that is derived from a body is derived from the redacted text**, and each derived row records a
  digest of the configuration it was built under — so switching a scanner on later is visible rather than silent, and a
  startup line names `SensitiveContent:RebuildStaleDerivedData` when rows predate the current settings. Stored raw MIME
  is never rewritten ([#790](https://github.com/Krzysztof318/MailFathom/pull/790)).
- **`get_email_content` is scanned on every call.** Both body representations, the subject, and participant display
  names are redacted in flight, nothing is rewritten in the store, and a body cut short by the scan's ceiling says so
  as `sensitiveContentScanCeiling` ([#803](https://github.com/Krzysztof318/MailFathom/pull/803)).

**A folder mapping now says how far into MailFathom a folder is admitted**, through three switches on the entry that
each default to `true`: `Synchronize` decides whether the folder is mirrored at all, `GenerateEmbeddings` whether what
is stored is ever cut into passages and sent to a provider, and `VisibleToTools` whether any tool lists, searches,
reads, or answers from it. A folder mapped with `Synchronize: false` is still a folder a rule can file mail *into*
([#706](https://github.com/Krzysztof318/MailFathom/pull/706)).

- **A mapping can ask for its folder to be created on the server.** `CreateIfMissing` defaults to `false` and applies
  only to an entry naming a `RemotePath`, so a mistyped path stays an unresolved alias instead of becoming a folder
  named after the mistake. Creation is issued where the alias is resolved, level by level, and the alias binds to the
  folder as the server advertises it ([#723](https://github.com/Krzysztof318/MailFathom/pull/723),
  [#726](https://github.com/Krzysztof318/MailFathom/pull/726)).
- **A mapping states a role independent of how the folder is found.** An entry may name both a `RemotePath` and a
  `SpecialUse`, a role is unique per account, and `role:Junk` is how a rule or the classification names a destination
  without knowing what the server calls it ([#729](https://github.com/Krzysztof318/MailFathom/pull/729)).
- **`mfctl folder erase` is the one thing in MailFathom that removes a folder's local copy**, one bounded pass per
  request, printing a running total and taking the raw MIME, the search document, the passages, their vectors, the spam
  verdicts and the signals behind them, the rule-execution history for that mail, and the checkpoint with the rows — so
  `mfctl spam classifications` and `mfctl rules history` stop answering for a folder you erase
  ([#789](https://github.com/Krzysztof318/MailFathom/pull/789)).

**Durable background work, with the queue an operator can see and act on.** Jobs are persisted, leased, and claimed one
statement at a time, so work in flight when a process dies is picked up again rather than stranded — **execution is
at-least-once**, and every handler is registered on the promise that running it twice with one payload is the same as
running it once, so the second attempt a crash produces is safe rather than absent. An attempt runs under a bounded
timeout with its lease renewed while it works ([#797](https://github.com/Krzysztof318/MailFathom/pull/797),
[#800](https://github.com/Krzysztof318/MailFathom/pull/800)).

- **Failure is classified rather than counted.** What is transient is retried with jittered backoff up to
  `Jobs:MaxAttempts`, what cannot succeed is dead-lettered at once, and the recorded reason carries nothing from the
  message ([#806](https://github.com/Krzysztof318/MailFathom/pull/806)).
- **Capacity is bounded at both ends.** `Jobs:MaxConcurrentJobs`, `Jobs:MaxConcurrentJobsPerType`, and
  `Jobs:MaxQueueDepthPerType` govern how much runs at once and refuse an enqueue past the depth rather than accepting
  work the deployment cannot reach ([#807](https://github.com/Krzysztof318/MailFathom/pull/807)).
- **`mfctl jobs dead-letters`, `mfctl jobs retry`, and `mfctl jobs drop`** list what has stopped, put one back, and give
  one up, and seven instruments publish what ran, how long it took, how much was repeated, what stopped, what is
  waiting, what a recurring dispatch decided, and how many of its occasions were skipped — the last of those being the
  one thing the others cannot show, since a skipped occasion enqueues nothing
  ([#814](https://github.com/Krzysztof318/MailFathom/pull/814),
  [#820](https://github.com/Krzysztof318/MailFathom/pull/820)).

**A model server on a private address, reached with no credential at all.** `Unauthenticated` is a third way to declare
what an endpoint presents, beside `ApiKey` and `EntraCredential`, and a plain `http` address is accepted for an endpoint
that declares it — which is the shape every local inference server has. Needing no credential is written rather than
inferred from the other two being absent, because an omission is what a forgotten key reference also looks like, and a
startup warning names each endpoint reached in the clear and what crosses it readable
([#695](https://github.com/Krzysztof318/MailFathom/pull/695)).
[Provider endpoints](https://krzysztof318.github.io/MailFathom/operations/provider-endpoints.html) records which
services were checked and what each check rests on ([#698](https://github.com/Krzysztof318/MailFathom/pull/698),
[#702](https://github.com/Krzysztof318/MailFathom/pull/702)).

**An inbound request now has an upper duration, and the process an upper connection count.** The MCP and administrative
endpoints each carry `RequestTimeout` beside their rate limiting, defaulted so enabling an endpoint bounds it — the
health probes stay outside it deliberately, since they have to keep answering while an endpoint is refusing — and
`ConnectionLimits` bounds what the machine accepts at all, the probe listener included, because the accept, the TLS
handshake, and the client certificate's chain building all happen before any rate limiter can see them
([#684](https://github.com/Krzysztof318/MailFathom/pull/684)).

**A Podman Quadlet deployment**, so a container can take the encrypted, machine-bound credentials systemd provisions.
`deploy/quadlet/` holds the unit sources for the application, PostgreSQL, the two networks, the volume, and the two
optional sidecars, and every secret reference in its configuration example is `systemd-credential:` rather than `file:`
([#704](https://github.com/Krzysztof318/MailFathom/pull/704)).
[The Quadlet deployment](https://krzysztof318.github.io/MailFathom/operations/deployment-quadlet.html) is the guide.

**Traces and metrics over the parts of MailFathom no library instruments.** A synchronization cycle opens a span per
account with one per folder beneath it, and eight instruments report how long a cycle took, what it stored and skipped,
what stopped a folder run, and how far behind an account is ([#817](https://github.com/Krzysztof318/MailFathom/pull/817)).
The local read path is spanned from the MCP call down to the content store
([#819](https://github.com/Krzysztof318/MailFathom/pull/819)); tool calls, the content store, the extraction backfill,
and database commits are metered ([#822](https://github.com/Krzysztof318/MailFathom/pull/822)); and every call to a
model opens a span measuring one attempt against the provider, with prompt and completion capture explicitly off
([#828](https://github.com/Krzysztof318/MailFathom/pull/828)). Tracing is parent-based always-on, and
`OTEL_TRACES_SAMPLER` still decides where you set it ([#832](https://github.com/Krzysztof318/MailFathom/pull/832)).
[Telemetry](https://krzysztof318.github.io/MailFathom/operations/telemetry.html) lists every span and instrument.

**An authorization server's document can advertise a scope this deployment does not require.**
`McpEndpoint:Authentication:<n>:OAuth:AdvertisedScopes` is published beside the required ones and never enforced, which
is what lets a client be told to ask for `offline_access` without every token lacking it being refused
([#818](https://github.com/Krzysztof318/MailFathom/pull/818)).

**`ask_mail` can narrow its lookups the way a search can.** The seven structured filters `search_emails` publishes are
now available to an answering run, validated by the same use case in the same words, and a filter it refuses is
reported back to the model rather than absorbed into an empty answer
([#686](https://github.com/Krzysztof318/MailFathom/pull/686)).

**Documentation for the two questions the previous release left you to work out on your own.**
[Configuring a mailbox at your provider](https://krzysztof318.github.io/MailFathom/users/mailbox-providers.html) states
the address, port, and credential kind each popular mail service publishes and what each does differently once
synchronization runs ([#703](https://github.com/Krzysztof318/MailFathom/pull/703)), and
[connecting the chat client you already use](https://krzysztof318.github.io/MailFathom/users/mcp-clients.html) says
where the dialog is in each one and which of them cannot present an API key at all
([#710](https://github.com/Krzysztof318/MailFathom/pull/710)). An
[MCP client OAuth connection](https://krzysztof318.github.io/MailFathom/operations/mcp-client-oauth.html) is documented
end to end from the identity provider's side ([#700](https://github.com/Krzysztof318/MailFathom/pull/700)).

**Every surface that knows a version now says where that version's documentation is** — the image's
`org.opencontainers.image.documentation` label, the chart's install notes, `mfctl status` for the version the
*deployment* reports, and the MCP server's own instructions to an initializing client
([#798](https://github.com/Krzysztof318/MailFathom/pull/798)). The site publishes each version's pages as artifacts an
AI agent can read as well: a map at the version's root, the Markdown source beside every documentation page the map
links — the generated API reference is published as pages only — and one file per reading path ([#793](https://github.com/Krzysztof318/MailFathom/pull/793)).

**`mfctl` installs on Linux with one command**, which fetches the binary for the platform, verifies it against the
checksum published beside it, and installs it into `~/.local/bin`. Where that directory is not already on your `PATH`
the script prints the `export PATH` line to add to a shell profile rather than editing one for you
([#794](https://github.com/Krzysztof318/MailFathom/pull/794)).

### Changed

- **Breaking (MCP tool contract)** — **`list_emails`, `search_emails`, and `ask_mail` take `folders` where they took
  `folderAliases`**, because the argument now accepts a role — `role:Junk` — as readily as an alias you chose. **An
  argument the tool does not declare is ignored rather than refused**, so a client still sending `folderAliases` is not
  stopped: its folder filter disappears and the call reads **every** folder in scope instead of the one it named.
  Update any stored prompt, tool description, or client configuration that spells the argument out. Naming a *role* no
  account in scope maps is now refused with `53003 MailFolderRoleUnmapped` rather than answered with an empty page; an
  *alias* nothing maps still selects nothing, because an alias is a name the caller chose while an empty page for a role
  would read as a folder holding no mail ([#729](https://github.com/Krzysztof318/MailFathom/pull/729)).
- **Breaking (MCP tool contract)** — **`get_email_content` returns a signed download link per attachment instead of
  base64 content, and asks for it with `includeAttachmentDownloadLinks` where `0.5.0` asked with
  `includeAttachmentContent`.** An argument the tool does not declare is ignored rather than refused, so a client that
  keeps sending the old name is not stopped: it receives every attachment described, no link, and no error. **Update
  every client that fetches attachment content before the upgrade.** Every attachment is still described in full — file
  name, media type, decoded size — and a call that asks for the files receives one `https` URL per attachment, valid for
  `EmailContent:AttachmentDownloads:LinkLifetime`
  and scoped to that one attachment. **Declare `Deployment:PublicBaseAddress`**, an absolute address with no path and
  `https` unless the host is loopback: a deployment that declares none serves every other part of the read and reports
  each attachment as `Unavailable`, and so does one that configures no data-encryption key ring, because the signing
  key is derived from that ring. Nothing composes the address from a request header, so it cannot be guessed on your
  behalf ([#679](https://github.com/Krzysztof318/MailFathom/pull/679)).
- **Breaking (configuration schema)** — **`EmailContent:MaxAttachmentBytes` and `EmailContent:MaxAttachmentBytesPerRead`
  are removed, and a configuration file still carrying either fails startup.** They bounded how many attachment octets
  one response and one call could carry, and no response carries an attachment's octets any more — what a caller
  receives is a link, and what bounds it is its lifetime rather than its size. The `EmailContent` section is bound
  strictly, so an unknown key is refused rather than ignored: a `0.5.0` file reaches this release and the host declines
  to start naming the key. **Delete both lines**, including the `MaxAttachmentBytes: 0` that `0.5.0` documented as the
  way to return no attachment content at all — a deployment that wants the metadata and nothing else now simply does not
  ask for links ([#679](https://github.com/Krzysztof318/MailFathom/pull/679)).
- **Breaking (MCP tool contract)** — **junk mail is withheld from listing, search, and answering by default.**
  `list_emails` and `search_emails` gain an optional `includeJunkMail`, defaulting to `false`, and every result of
  either carries a new required `includedJunkMail` field — so a client parsing a result strictly sees a new field, and
  one that names no new argument sees mail in a folder mapped as junk stop appearing. `ask_mail` excludes junk and
  offers no override, because its answer is composed by a model out of the mail it retrieved and content written to
  deceive a reader would arrive as ordinary correspondence. `get_email_content` is unaffected: a message reached by its
  identifier is one somebody already has in hand ([#731](https://github.com/Krzysztof318/MailFathom/pull/731)).
- **Breaking (configuration schema)** — **`MailSynchronization:Accounts:<n>:Folders` is now the whole of the folders the
  deployment has**, rather than a list of folders it treats specially. A folder no entry names is unreachable for every
  reader: no tool lists, searches, reads, or answers from it, no rule is evaluated against its mail, nothing embeds it,
  and no alias of it resolves as a destination. **Anyone whose deployment holds mail under an alias the current file
  does not name — a folder mapped once and later removed, or renamed in configuration — adds an entry for it to read
  that mail again.** Nothing is deleted: the rows stay and become readable the moment a mapping names them, with the
  folder resuming from its retained checkpoint. The default is untouched, so an account configuring no folder still
  mirrors its inbox by role ([#784](https://github.com/Krzysztof318/MailFathom/pull/784)).
- **Breaking (configuration schema)** — **switching a folder's `Synchronize` off now keeps the mail it had already
  stored**, where `0.5.0` erased it. The rows go on occupying the database while staying unreadable by every tool,
  query, embedding pass, and rule. **No configuration value erases them any more**; `mfctl folder erase` is what does,
  and an operator who switched the flag off expecting the storage back runs it
  ([#781](https://github.com/Krzysztof318/MailFathom/pull/781),
  [#789](https://github.com/Krzysztof318/MailFathom/pull/789)).
- **Breaking (configuration schema)** — **`SpamClassification:UseScanner` is read at startup, and a deployment that
  turns it on without a reachable daemon fails to start** instead of quietly classifying from headers alone. Name a
  daemon in `SpamClassification:Scanner:Host` and deploy one, or leave `UseScanner` off
  ([#777](https://github.com/Krzysztof318/MailFathom/pull/777)). The personal-data analyzer refuses startup on the same
  terms: `SensitiveContent:Pii` on with no analyzer that can answer for a switched-on category is a scanner that would
  find nothing, which is indistinguishable from clean mail
  ([#724](https://github.com/Krzysztof318/MailFathom/pull/724)).
- **Breaking (configuration schema)** — **a folder entry may now name both `RemotePath` and `SpecialUse`** where exactly
  one was required, and `CreateIfMissing` is refused only on an entry naming no `RemotePath`. Both are relaxations, so
  configuration `0.5.0` accepted still binds. What is new is that two folders of one account naming the same role, or an
  alias beginning `role:`, fail startup — neither of which a previous file could have relied on
  ([#729](https://github.com/Krzysztof318/MailFathom/pull/729)).
- **Twelve migrations**, adding eight tables for rules, their execution history, spam
  classifications and their signals, classification runs, jobs, and rule schedules; four columns on `stored_emails`,
  `email_search_documents`, and `backfill_positions`; and the indexes both need. One statement renames a column on a
  table this release itself introduced. **Nothing `0.5.0` reads changes shape**, so the schema step applies while
  `0.5.0` is still serving, this release deploys over the previous release's data, and rolling the image back leaves
  `0.5.0` serving the result unchanged. Apply it the way every release's schema is applied
  ([#797](https://github.com/Krzysztof318/MailFathom/pull/797),
  [#814](https://github.com/Krzysztof318/MailFathom/pull/814),
  [#820](https://github.com/Krzysztof318/MailFathom/pull/820)).
- **The release no longer submits `winget` manifests.** Two submissions are open
  against the community repository and neither has been reached, and it accepts exactly one pull request per package
  version — so submitting again could only queue a version behind them. No page offers `winget` as a way to get `mfctl`
  while that holds; take the binary from the release, or use the install script on Linux
  ([#794](https://github.com/Krzysztof318/MailFathom/pull/794)).
- **A message's passages are cut after classification and after the rules, not inside the transaction that stores it.**
  Cutting is now the account run's last local step, so a message is never chunked before the classification could
  withhold it or before a rule could file it somewhere mapped differently — and passages are not undone by a message
  moving afterwards ([#811](https://github.com/Krzysztof318/MailFathom/pull/811)).
  [The arrival pipeline](https://krzysztof318.github.io/MailFathom/architecture/arrival-pipeline.html) states the whole
  order.
- **The folder-count refusal on `list_emails`, `search_emails`, and `ask_mail` reads for the argument rather than for
  aliases.** All three bind `folders` through the same resolver, so all three raise it. The
  five-digit code `51002` is unchanged and its message and filter name are worded differently; a client that matched on
  the text rather than the code sees new text, which is what the code exists so it need not do
  ([#776](https://github.com/Krzysztof318/MailFathom/pull/776)).

### Fixed

- **A client following the published OAuth metadata was sent back through `/authorize` every time its token expired.**
  The document listed only the scopes a token would be refused for lacking, so `offline_access` could not appear in it —
  requiring it would refuse every token from an authorization server that grants offline access without echoing the
  value into the access token. Clients therefore asked for the published scopes, were issued no refresh token, and
  re-authorized on every expiry. Advertised and required scopes are two lists now, and `mfctl` no longer compensates by
  appending the value itself ([#818](https://github.com/Krzysztof318/MailFathom/pull/818)).
- **`ask_mail` answered worse than `search_emails` on questions a search alone handles.** An answering run could only
  rank free text across the whole scope, which is the one shape both lexical and vector similarity are weakest at; it
  can now narrow by sender, recipient, subject fragment, date bounds, read state, and attachments, and a filter it wrote
  badly is reported back to it instead of arriving as an empty mailbox
  ([#686](https://github.com/Krzysztof318/MailFathom/pull/686)).
- **A folder mapped but not mirrored could not be reached as a destination.** It is resolved on demand the first time
  something files mail into it, through the same resolver every other destination goes through — which also means
  `CreateIfMissing` reaches such a folder ([#778](https://github.com/Krzysztof318/MailFathom/pull/778)).

### Security

- **Mail can be redacted before it crosses out of the deployment, and the guard fails closed.** Four egress points are
  named and each is guarded: the question and the retrieved extracts sent to a chat endpoint, every passage sent to an
  embedding endpoint, the subjects, snippets, and answers the MCP tools return, and — for a message a client asked for by
  identifier — its body representations, its subject, and the display names its headers wrote. What is deliberately left
  as read at that fourth point is what a caller acts on rather than reads: the addresses, the sizes, the flags, and every
  attachment's file name. A detector that is unavailable, times out, or errors **fails the call** rather than serving
  unredacted text
  ([#772](https://github.com/Krzysztof318/MailFathom/pull/772),
  [#803](https://github.com/Krzysztof318/MailFathom/pull/803)).
- **No response carries an attachment's bytes any more.** A call asking for files receives a link per attachment,
  carrying an opaque capability signed with a key derived from the deployment's existing key ring under HMAC-SHA256 and
  compared in constant time, valid for minutes, scoped to one attachment, and resolved through the live mailbox so it
  dies with the message it points at. A rotation leaves outstanding links verifiable for the rest of their own lifetime
  and issues new ones under the new key ([#679](https://github.com/Krzysztof318/MailFathom/pull/679)).
- **Both scanners are meant to stay inside your trust boundary, and the deployment assets say so.** The personal-data
  analyzer and the spam daemon are reached over your own network by default, the analyzer's confidence floor is set
  where every measured false positive drops while every category stays detectable, and the daemon's DNS blocklists —
  which would send sender addresses and link hosts to third-party lists — are off unless you operate a resolver and
  accept what it sends ([#724](https://github.com/Krzysztof318/MailFathom/pull/724),
  [#777](https://github.com/Krzysztof318/MailFathom/pull/777)).
- **A slow or numerous caller can no longer hold a surface out of service.** Twenty concurrent requests with no upper
  duration was enough to do it, since a permit was held for as long as the request took; the MCP and administrative
  endpoints now carry a request timeout, the probes staying outside it, and a process-wide connection ceiling bounds
  what is accepted before any routing has happened
  ([#684](https://github.com/Krzysztof318/MailFathom/pull/684)).
- **Junk mail never reaches the model that answers a question.** `ask_mail` excludes it with no override, so a message
  written to deceive a reader cannot arrive as ordinary correspondence in the material an answer is composed from
  ([#731](https://github.com/Krzysztof318/MailFathom/pull/731)).
- **A telemetry record still carries no mail.** Every publisher of a span or a measurement in the deployment is held
  against the redaction contract — driven through a listener and judged on what it emitted where a test can drive it,
  and read from its declarations where it cannot, which is how a span a background worker opens is covered — so the rule
  is asserted over the whole surface rather than sampled where somebody remembered to check
  ([#832](https://github.com/Krzysztof318/MailFathom/pull/832)).

## [0.5.0] - 2026-08-10

The fifth release, and the one that lets a client **ask about your mail rather than only look through it**. `ask_mail`
answers a question in prose and cites the messages the answer was drawn from, and `search_emails` ranks semantically as
well as lexically. Both stay dark until you declare the AI endpoints they need, so a deployment that declares none
serves exactly what it served before — and pays exactly what it paid before, which for a feature that bills per call is
the more important half.

**Four things need an edit before this release starts, renders, or answers a client.** Every account states a
`DisplayName` now; two arguments on the MCP tools were renamed, and the previous spellings are ignored rather than
refused — so a client that keeps sending `accountIds` reads every account instead of being stopped; a Helm values
document that names `database.host` — which every `0.4.0` one does — needs `database.deploy.enabled: false` beside it,
or the chart refuses to render at all; and the database is **PostgreSQL 18**, which does not read a data directory
PostgreSQL 17 wrote. The last of those is the expensive one: an existing deployment moves its data across a dump
before the new image comes up, and
[upgrading a deployment that ran PostgreSQL 17](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html#upgrading-a-deployment-that-ran-postgresql-17)
is the procedure, command by command.

**The database schema moves as well**, by six migrations that add four tables, four columns on tables that already
held data, and two indexes on those, and that change nothing `0.4.0` reads — so the schema step applies while `0.4.0`
is still serving, `0.4.0` serves the result unchanged if you roll the image back, and this release deploys over the
previous release's data. Nothing else `0.4.0` promised is withdrawn: every setting not named below still means what it
meant, and no tool was removed.

**The defect `0.4.0` shipped with is gone, and it was the whole image.** The published `0.4.0` container could not
start: its base image sets `ASPNETCORE_HTTP_PORTS`, `0.4.0` is the release that began refusing that variable, and the
Dockerfile never cleared the inherited value — so every container built from that image failed startup on a setting
nobody wrote.

### Added

**`ask_mail` — a question about your mail, answered in prose and cited back to the messages it came from.** The
question is not a search query: its words are never matched against your mail, and the lookups behind it are written by
the model, which is what lets *did the supplier ever confirm the March delivery date* find the message that says so
without containing any of those words
([#579](https://github.com/Krzysztof318/MailFathom/pull/579)). Every answer carries `citations`, one entry per email
the run actually read, so nothing it says is un-checkable.

- **It is advertised only where it can work** — a declared `Chat` endpoint, and mail that is embedded. A server with
  neither does not publish the tool, so a client never sees an ability the deployment does not have. Called on a
  deployment that cannot answer, it fails with `56001` and the message says which half is missing.
- **Configuring it is one section.** `Chat:Alias`, `Chat:Model`, and one credential — an API key or a Microsoft Entra
  credential — declare the endpoint; `Chat:Api` states whether the deployment's server serves chat completions or the
  responses API, because the routed name is your own deployment's and nothing about it says which paths exist. A
  reasoning model states `Chat:ReasoningEffort` as the provider spells it, and unset sends no reasoning parameter at
  all ([#557](https://github.com/Krzysztof318/MailFathom/pull/557),
  [#624](https://github.com/Krzysztof318/MailFathom/pull/624)).
- **What one question may spend is bounded before it is asked, and what a period may spend is bounded above that.**
  `MailAnswering` sets how many passages a lookup draws on, how much of any one message it draws out, how much
  retrieved mail may leave the process for one question, how many provider calls and tokens one run may spend, and how
  many runs and tokens a period may. A run that reaches a ceiling stops with `57001` rather than continuing quietly,
  and a run that reaches only the retrieval ceiling answers from what it has and says the mailbox was not read in full
  ([#592](https://github.com/Krzysztof318/MailFathom/pull/592)).
- **An optional second filter judges the retrieved passages with the model** before they reach the answer, dropping
  what scores below `Chat:RelevanceFilter:MinimumRelevance`. It is off by default, because it is a second call per
  lookup ([#573](https://github.com/Krzysztof318/MailFathom/pull/573)).
- **An account can keep a record of what each question read**, off by default and enabled per account with
  `AnsweringAuditTrail:Enabled` and a retention window. It names the mail a run drew on and holds none of it, and
  `GET /api/admin/answering/audit` reads it back in bounded pages
  ([#610](https://github.com/Krzysztof318/MailFathom/pull/610)).
- The model is composed over Agent Framework and the mail it reads is fenced away from the instructions it follows, so
  an instruction written into a message is data rather than a command
  ([#564](https://github.com/Krzysztof318/MailFathom/pull/564),
  [#565](https://github.com/Krzysztof318/MailFathom/pull/565),
  [#603](https://github.com/Krzysztof318/MailFathom/pull/603)).
  [Mail answering](https://krzysztof318.github.io/MailFathom/features/mail-answering.html) is the page.

**`search_emails` ranks semantically as well as lexically.** Where an embedding profile is active, a search fuses the
two rankings with Reciprocal Rank Fusion and reports `retrievalMode: hybrid`; where none is, it answers exactly as
`0.4.0` did and says `lexical` ([#555](https://github.com/Krzysztof318/MailFathom/pull/555)). Every response also
carries `semanticSearch` — `inactive`, `available`, or `degraded` — so a client can tell a server that never embedded
anything from one whose provider is failing right now, which the two modes alone cannot distinguish
([#562](https://github.com/Krzysztof318/MailFathom/pull/562)).

- **A failing provider degrades the search rather than the deployment.** An unhealthy profile falls back to lexical
  ranking and says so, instead of failing the call.
- **Changing the model is a reindex with no search outage.** A new vector generation is built beside the one that is
  serving and takes over only when it is complete, and `POST /api/admin/embeddings/reindex/cancellation` stops one
  under way and leaves the serving generation where it is
  ([#570](https://github.com/Krzysztof318/MailFathom/pull/570)).
- **What embedding may cost is bounded before it is spent.** `Embeddings:MaxRequestsPerMinute` paces a provider whose
  quota is per minute, `Embeddings:MaxInputCharactersPerPeriod` and `Embeddings:SpendPeriod` cap what a fixed window
  may send, and `Embeddings:MaxCharactersPerEmail` bounds a single enormous message rather than refusing it
  ([#581](https://github.com/Krzysztof318/MailFathom/pull/581)).
- **`mfctl` administers it.** `mfctl embedding status` reports whether semantic search is working, how far behind it
  is, and when the next backfill pass is due; `mfctl embedding activate` forecasts what taking up the declared model
  would cost before it starts, and starting it wakes the backfill instead of leaving the deployment to look broken for
  up to fifteen minutes ([#593](https://github.com/Krzysztof318/MailFathom/pull/593),
  [#626](https://github.com/Krzysztof318/MailFathom/pull/626)).
  [Embedding profiles](https://krzysztof318.github.io/MailFathom/operations/embedding-profiles.html) states the whole
  lifecycle.

**`list_accounts`, so a client can find out what it may ask about.** It reports each account's identifier, the display
name you gave it, whether its next pass polls or listens, and one entry per folder with how fresh that folder's local
copy is — and deliberately publishes no address, no credential, and no server name
([#637](https://github.com/Krzysztof318/MailFathom/pull/637)). Every tool that takes accounts now accepts either the
identifier or the display name, so a person can say *work* where the configuration says `work-imap-01`.

**`get_email_content` returns attachment content.** Pass `includeAttachmentContent` and each attachment arrives as
base64, bounded by `EmailContent:MaxAttachmentBytes` per file and `EmailContent:MaxAttachmentBytesPerRead` across the
call; a file over the limit is described and not returned, never truncated
([#633](https://github.com/Krzysztof318/MailFathom/pull/633)). Setting `MaxAttachmentBytes` to `0` returns no
attachment content at all, which is the deployment that wants the metadata and nothing else.

**A record of every change MailFathom makes to a mailbox, off by default and enabled per account.** **Nothing in
`0.5.0` asks it to make one** — no tool on the MCP surface writes, and the first caller is the rule engine a later
release brings — so an account that turns the trail on today gets an empty page and keeps getting one until that
caller exists. What it buys now is that the decision is made and the storage is in place before the first write, and
that is the whole of it. `AuditTrail:Enabled` and `AuditTrail:Retention` turn it on per account, one entry is written
per finished change, and `GET /api/admin/mailbox/mutations/audit` reads it back filterable by account, by change, and
by time ([#568](https://github.com/Krzysztof318/MailFathom/pull/568)).

- **It holds no mail content and it outlives the mail.** Folder paths, identifiers, a five-digit failure code where
  there was one, and MailFathom's own configured names are all an entry carries — no subject, no address, no body
  fragment, no filename — and erasing the email leaves the entry standing, including where the change recorded *was*
  that deletion.
- Retention rides the account's own run and erases at most five thousand entries a pass, so shortening a long window
  clears the backlog over several runs rather than in one delete that locks the trail.
- `AuthoredDeleteEmailDisposition` decides what becomes of the local copy of mail MailFathom itself deleted —
  `RetainLocalCopy`, `RetainTombstone`, or `EraseLocalCopy` — separately from the setting that governs mail somebody
  else deleted, because a deletion of ours and a deletion of theirs are different facts
  ([#554](https://github.com/Krzysztof318/MailFathom/pull/554),
  [#561](https://github.com/Krzysztof318/MailFathom/pull/561),
  [#563](https://github.com/Krzysztof318/MailFathom/pull/563)).

**A ceiling on how much mail one deployment stores, and how much one run brings in.**
`MailSynchronization:MaxStoredContentBytes` is what stops a large mailbox from filling the volume: past it, ingestion
degrades to metadata only and keeps listing and searching rather than failing, and the messages it skipped are picked
up once there is room. `MaxContentBytesPerRun` ends a folder run at its checkpoint instead of at the end of the
mailbox, and `MaxInFlightRawMimeBytes` bounds what a run holds in memory at once
([#580](https://github.com/Krzysztof318/MailFathom/pull/580)).

**`mfctl` reaches a deployment whose certificate this machine does not trust, by asking once.** `mfctl login` shows
the fingerprint, asks, and pins what you accept to that profile, so a later renewal is a question rather than a silent
acceptance; `--trust-untrusted-certificate` and `--allow-clear-text` answer the same two questions where there is no
terminal to ask on ([#560](https://github.com/Krzysztof318/MailFathom/pull/560)).

**Every exported record names the build it came from.** `service.version` carries the semantic version and
`vcs.ref.head.revision` the commit, on every log record, metric, and span the host exports, so a report from a
deployment can be tied to the code that produced it ([#620](https://github.com/Krzysztof318/MailFathom/pull/620),
[#655](https://github.com/Krzysztof318/MailFathom/pull/655)).

### Changed

- **Breaking (deployment contract)** — **the database is PostgreSQL 18.4 with pgvector 0.8.6**, where `0.4.0` ran 17.
  PostgreSQL does not read a data directory an earlier major version wrote, so **bringing the new image up over an
  existing volume does not upgrade it** — the container exits `1` naming the data it found, the server never listens,
  and nothing that depends on it comes up. The attempt writes nothing, so the old directory is intact and still
  dumpable afterwards. Move the data across a dump from a PostgreSQL 17 server before the upgrade, or delete the volume
  and let synchronization refill it from IMAP — which costs the embeddings and the audit trails, since neither is in
  the mailbox ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).
  [Upgrading a deployment that ran PostgreSQL 17](https://krzysztof318.github.io/MailFathom/operations/deployment-compose.html#upgrading-a-deployment-that-ran-postgresql-17)
  is the sequence for Compose, and the same reasoning holds for a claim the chart wrote.
- **Breaking (deployment contract)** — **the Helm chart runs PostgreSQL itself unless you tell it not to**, where
  `0.4.0` installed none and required `database.host`. A values document that names a host now fails to render, because
  `database.host` is refused while `database.deploy.enabled` is on and the address is derived from the release name
  instead: two values naming one server is how a deployment ends up connecting somewhere it did not install. **Keep
  your own server by setting `database.deploy.enabled: false` beside the `host` you already have.** A deployment that
  takes the default instead names a second Secret in `database.deploy.superuserPasswordSecret` — separate from
  `secrets.existingSecret`, and refused if it is the same one — because the application's Secret is mounted whole into
  the pod that parses untrusted mail ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).
- **Breaking (deployment contract)** — **`mfctl` refuses a deployment from another release line before it sends
  anything.** A `0.4.x` command against a `0.5.x` deployment stops with a message naming both versions, because the
  administrative contract is what a minor may break and a command that guesses at it is worse than one that declines.
  Take `mfctl` from the deployment's own release; two builds that share a `major.minor` and differ otherwise warn and
  run, and a version that cannot be read warns and runs
  ([#628](https://github.com/Krzysztof318/MailFathom/pull/628)).
- **Breaking (configuration schema)** — **every account states a `DisplayName`**, and startup fails naming the account
  that has none. It is what a client sees and what a person names an account by, it is at most 128 characters, and it
  may not collide with another account's identifier or display name compared without regard to case. Add one line per
  account under `MailSynchronization:Accounts`
  ([#637](https://github.com/Krzysztof318/MailFathom/pull/637)).
- **Breaking (MCP tool contract)** — **`list_emails` and `search_emails` take `accounts` where they took
  `accountIds`.** The argument was renamed because it now accepts a display name as readily as an identifier. **An
  argument the tool does not declare is ignored rather than refused**, so a client still sending `accountIds` is not
  stopped — its account filter simply disappears, and the call reads **every** account the deployment serves instead
  of the one it named. Update every client that names accounts before the upgrade, and read `list_accounts` for the
  names ([#637](https://github.com/Krzysztof318/MailFathom/pull/637)).
- **Breaking (MCP tool contract)** — **`get_email_content` takes `includeAttachmentContent` where it took
  `includeAttachmentDetails`**, and every attachment's file name, media type, and decoded size are now returned
  whether or not the call asks for anything. The old argument bought the metadata; the new one buys the bytes, so a
  client that passed it to see what was attached needs to pass nothing at all. It is ignored the same way when it is
  still sent, which here costs nothing — the metadata arrives regardless, and no attachment content is returned
  without the new argument ([#633](https://github.com/Krzysztof318/MailFathom/pull/633)).
- **`search_emails` can report a `retrievalMode` it never reported before.** `lexical` was the only value `0.4.0`
  produced; `hybrid` is a second one, and a client matching on the field exactly rather than on the results should
  expect it ([#555](https://github.com/Krzysztof318/MailFathom/pull/555)).
- **Every SQL statement MailFathom runs is logged at `Debug` rather than `Information`.** A deployment at the default
  level no longer writes one log record per database command, which is where the bulk of its log volume was going —
  and those records carry the text of every query the mailbox is read with. Set
  `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Debug` to get them back
  ([#654](https://github.com/Krzysztof318/MailFathom/pull/654)).

### Fixed

- **The published `0.4.0` container could not start.** Its base image sets `ASPNETCORE_HTTP_PORTS`, `0.4.0` refuses
  that variable by design, and the Dockerfile did not clear the inherited value — so the image failed startup on a
  setting nobody had written, with a message naming a variable that is not in any configuration file
  ([#575](https://github.com/Krzysztof318/MailFathom/pull/575)).
- **An MCP endpoint configured with OAuth and nothing else threw on every request that carried no credential**,
  including the health probes, which are documented as carrying none. The endpoint now answers an uncredentialed
  request with the challenge it is supposed to, and the probes are served without one
  ([#577](https://github.com/Krzysztof318/MailFathom/pull/577)).
- **A `list_emails` date filter written at a non-zero UTC offset failed the whole listing.** `receivedOnOrAfter` and
  `receivedBefore` reached the database unconverted, and anything but `+00:00` was refused there — so a client in a
  time zone sent the value its clock produced and got a failure rather than a page. Both bounds are held as instants
  now, and every offset names the same moment ([#612](https://github.com/Krzysztof318/MailFathom/pull/612)).

### Security

- **A question and the mail that answered it leave no copy at the provider.** A chat endpoint declaring
  `Api: Responses` reached an API that retains what it is sent for thirty days by default and makes it readable in the
  provider's dashboard — so adopting that API would have placed the operator's correspondence in a third party's log
  because of a default nobody wrote. Every request states that it is stateless, and the model's reasoning is carried
  between turns as the encrypted content the provider returns rather than by leaving the conversation behind
  ([#636](https://github.com/Krzysztof318/MailFathom/pull/636)).
- **What leaves the process to answer one question is bounded and countable.** The passages a run may send, how much of
  any single message goes with them, and the total that may cross the boundary for one question are each configured and
  each enforced before the call rather than after it
  ([#592](https://github.com/Krzysztof318/MailFathom/pull/592)).
- **A database the chart deploys keeps its superuser credential out of the pod that parses mail.** The application's
  Secret is mounted whole, because MailFathom reads the keys your own configuration names, so the superuser password
  lives in a second Secret the application never mounts and the chart refuses a values document that names one Secret
  for both. The role MailFathom connects as is never a superuser in either arrangement
  ([#658](https://github.com/Krzysztof318/MailFathom/pull/658)).

## [0.4.0] - 2026-08-07

The fourth release, and the first that asks every deployment to edit its configuration before it will start. Two things
every installation states have moved: **where each surface is served, and how a credential is configured.** Neither
previous form is ignored — both fail startup naming what replaces them — so an upgrade that skips the edit stops rather
than quietly serving something you did not configure. **The database schema moves as well**, by five migrations that
add three tables and then refine one of the three, and that touch nothing `0.3.0` reads — so the schema step belongs to
this upgrade, it applies while `0.3.0` is still running, and `0.3.0` serves the result unchanged if you go back.

Nothing else `0.3.0` promised is withdrawn. The MCP tool contract is untouched — `list_emails`, `get_email_content`,
and `search_emails` answer exactly as they did — and every setting not named below still means what it meant.

**The defect `0.3.0` shipped with is gone.** A deployment that set `HealthEndpoints:Enabled` to `false` and enabled the
administrative endpoint lost its application listener and refused every MCP client. There is no application listener to
lose now, because every surface binds the socket its own section names.

### Added

**A key pair as a third way to authenticate, on both endpoints.** The client holds the private key and the deployment
holds only the public half, so nothing this host stores in order to verify a request is worth stealing from it — not
from the configuration, not from a backup of it, and not from the deployment tool that wrote it
([#527](https://github.com/Krzysztof318/MailFathom/pull/527)).

- Configure a `PublicKey` entry under `Authentication` exactly as you would a key: one named secret, reached through
  every reference scheme the deployment already has, with a `Name` diagnostics correlate on and a `Lifetime` that is
  enforced. Startup refuses material that is not a PEM public key, an RSA key below 2048 bits, a curve outside P-256,
  P-384, and P-521, and — explicitly — material carrying a private key.
- The client mints a short-lived JSON Web Token, signs it with the private half, and presents it as an ordinary bearer
  credential: the arrangement RFC 7523 describes and OpenID Connect deploys as `private_key_jwt`. It carries `typ:
  mailfathom-client-assertion+jwt`, an audience of `urn:mailfathom:mcp` or `urn:mailfathom:admin`, an expiry no more
  than five minutes ahead, and a fresh identifier the endpoint refuses to serve twice — so a captured assertion stops
  working on its own, and cannot be replayed even inside its remaining seconds.
- `mfctl login --mode keypair --private-key <file>` mints all of it and stores no credential; every command signs its
  own assertion.
- Rotating a key is an overlap with no secret to coordinate across two machines: add the new public key as a second
  entry, move the client to the new private key, remove the old entry.
  [Key pairs](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#key-pairs) is the page.

**`mfctl` from the Windows Package Manager.** Each release submits its own manifest, so `winget install
MailFathom.mfctl` becomes a packaged path beside the download and `winget upgrade` carries you to the next release
([#498](https://github.com/Krzysztof318/MailFathom/pull/498)). The manifest names the same release asset the releases
page does and carries the same hash the checksum file does, so both paths install the same bytes and check them the
same way. A version is offered a little after it is attached here, because the community repository reviews the
submission; until one is accepted, the releases page is where the command comes from on every platform.

**The metrics and traces the libraries underneath MailFathom already emit.** Where `OTEL_EXPORTER_OTLP_ENDPOINT` names
a destination, four more meters now reach it: `Npgsql` for connection-pool state and command durations and counts,
`Microsoft.EntityFrameworkCore` for contexts, queries, saves, compiled-query cache hits, and concurrency failures,
`Experimental.ModelContextProtocol` for MCP session duration and per-operation duration broken down by protocol method
and tool name, and `Polly` for every outbound pipeline's attempts, outcomes, timeouts, and circuit-breaker transitions
([#521](https://github.com/Krzysztof318/MailFathom/pull/521)). Database commands and MCP protocol operations are
spanned as well and correlated with the request that caused them; the probe paths stay untraced, because a probe
arrives every few seconds and says the same thing every time.

- Every tag on them is a bounded set — a protocol method, a transport, one of the three tool names, an outcome — so
  none of them opens a time series per message or per person.
- What MailFathom publishes under a name of its own goes under exactly one: **`MailFathom`**, serving as both activity
  source and meter, which is what a dashboard filters on to see this process and nothing a library emits
  ([#510](https://github.com/Krzysztof318/MailFathom/pull/510)).
  [Telemetry](https://krzysztof318.github.io/MailFathom/operations/telemetry.html) records each of them.

### Changed

- **Breaking (configuration schema)** — **every surface states where it is served, and the host's own ways of naming a
  listener are refused.** `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`, `--urls`, and any entry
  under `Kestrel:Endpoints` each fail startup with a message naming the setting that replaces them. Write
  `McpEndpoint:BindAddress`, `McpEndpoint:Port`, and `McpEndpoint:Transport`; the administrative endpoint and the probes
  take the same three. **A deployment of your own that sets `ASPNETCORE_HTTP_PORTS` sets `McpEndpoint__Port` instead** —
  the published image and the packaged chart already do, so an upgrade that takes both as they ship needs no edit here
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)). They are refused rather than ignored because ignoring
  them is silent: Kestrel drops URL-shaped addresses as soon as a listener is bound in code, which every surface now
  does, and a configured endpoint would otherwise be bound beside them on a socket no section describes and no
  credential guards. A deployment that enables no surface at all is refused for the same reason.
- **Breaking (configuration schema)** — **the administrative endpoint's default port is `8080`, the MCP endpoint's**,
  where `0.3.0` gave it `8090`. Two surfaces may deliberately share one socket now — the posture a single-node
  deployment behind one ingress wants — so a deployment that enabled the administrative endpoint without stating a port
  publishes it wherever `8080` is published rather than on a port of its own. State `AdminEndpoint:Port`, where `8090`
  restores what you had, unless sharing is what you want; the socket serves each surface's own paths either way, and a
  path a surface does not own is still refused there with a `404`
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **`Transport` decides what a surface's clear-text socket does**, where `0.3.0`
  inferred that from whether HTTPS profiles were configured. `Http` serves the routes and refuses profiles,
  `HttpAndHttps` binds the profiles and redirects the clear-text socket to them, and `HttpsOnly` does not open it at
  all. `Http` is the default, so adopting this release costs no certificate work
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **`Https:Redirect` no longer binds a port of its own.** `0.3.0` gave it `8080`
  beside the MCP profiles and `8091` beside the administrative ones; the redirect now answers on the surface's own
  `BindAddress` and `Port`. A deployment that published `8091` to reach the administrative redirect publishes that
  surface's own port instead ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **Breaking (configuration schema)** — **authentication is a list of the credentials an endpoint accepts**, where
  `0.3.0` named methods in a flag set and configured each in a sibling section. `McpEndpoint:Authentication` and
  `AdminEndpoint:Authentication` each take entries, and the block an entry carries is what selects the method that
  judges it — there is no setting naming the method any more. `Authentication: "ApiKey"` beside an `ApiKeys` list
  becomes one entry per key, each carrying an `ApiKey` block; `Authentication: "OAuth"` beside an `OAuth` section
  becomes an entry carrying that section. An entry carrying no block fails startup, named by its position
  ([#515](https://github.com/Krzysztof318/MailFathom/pull/515)).
  - **`RequiredScopes` is per entry** rather than per endpoint, so two authorization servers one endpoint accepts may
    demand different scopes. Every OAuth entry still names the same `Resource`, because the endpoint publishes one
    metadata document.
  - An empty list warns at startup exactly as `None` did, and a value written where the list belongs fails it rather
    than being read as a method name.
- **Breaking (configuration schema)** — **a setting only the process environment can deliver, written anywhere else,
  fails startup** naming every such variable at once, with error code `12002`. `OPENSSL_CONF`, `OTEL_SERVICE_NAME`, and
  every `OTEL_*`, `ASPNETCORE_*`, and `DOTNET_*` variable are read before MailFathom's configuration exists or by a
  library that never consults it, so a value written into an appsettings file, a provisioned configuration file, or a
  command-line argument reached nobody — while the file read it back happily and nothing said which of the two you were
  looking at. Set each on the host process, or remove it
  ([#509](https://github.com/Krzysztof318/MailFathom/pull/509)).
- **Every synchronized message is also cut into passages and stored**, in the same transaction that stores what was
  extracted from it, so a mailbox costs more storage per message than it did under `0.3.0` — roughly its extracted text
  again, in overlapping windows ([#488](https://github.com/Krzysztof318/MailFathom/pull/488)). A message that yielded no
  text is cut into nothing, mail stored before this release is not revisited, and nothing else in this release reads a
  passage.

### Removed

- **Breaking (deployment contract)** — **`GET /` no longer answers.** `0.3.0` served
  `{"service":"MailFathom","status":"ready"}` at the root of the application listener; the MCP endpoint's port serves
  `/mcp` and answers everything else with `404`. An external check pointed at `/` moves to the probes on their own
  listener — `/alive` for liveness, `/health` for readiness, `/started` for startup, on `HealthEndpoints:Port` unless
  you moved it ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

### Fixed

- **A `file:` secret reference pointing at a FIFO or a stalled mount hung the host indefinitely.** Opening the file is
  bounded now, so an unreachable mount is reported as one line of the startup failure report rather than as a process
  that never finishes starting and never says why
  ([#511](https://github.com/Krzysztof318/MailFathom/pull/511)).
- **The device sign-in prompt raced the rest of `mfctl`'s output.** Both device-code flows handed the verification
  address and the short code to the console through a type that marshals onto a synchronization context a console
  process does not have, so nothing ordered the printing of the code against the wait for you to type it. The prompt now
  reaches the terminal before polling begins, on the thread that asked for it
  ([#418](https://github.com/Krzysztof318/MailFathom/pull/418)).
- **`HealthEndpoints:Enabled: false` beside an enabled administrative endpoint no longer costs the application
  listener** — the defect `0.3.0`'s notes named as shipped with it
  ([#419](https://github.com/Krzysztof318/MailFathom/pull/419)), and one that cannot recur now that each surface binds
  its own socket ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

### Security

- **A key pair leaves nothing on the host worth stealing.** An API key is a shared secret, so a copy of every credential
  that reaches the mailbox sits in the configuration and in whatever produced it; a public key verifies the same client
  and is not a secret at all. It is the method for a scheduled job, which has no person to sign in as
  ([#527](https://github.com/Krzysztof318/MailFathom/pull/527)).
- **The administrative endpoint shares the MCP endpoint's port unless you say otherwise.** Administering the service is
  a different authority from reading the mailbox, and the probes answer without a credential, so putting either on the
  endpoint's port publishes it wherever that port is published. The ports exist so the decision is yours; take it rather
  than inherit it ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).
- **A listener nothing configured can no longer be bound.** Refusing the host's own address settings closes the case
  where a `Kestrel:Endpoints` entry survived beside a listener bound in code and served the routes on a socket no
  section describes, no credential guards, and no isolation middleware was composed for
  ([#459](https://github.com/Krzysztof318/MailFathom/pull/459)).

## [0.3.0] - 2026-08-04

The third release, and the first whose upgrade is a new image and nothing else: **the database schema does not move.**
No migration is added, so `0.3.0` serves the database `0.2.0` was serving, and `0.2.0` serves it again if you go back.
Nothing `0.1.0` or `0.2.0` promised is withdrawn either — the MCP tool contract is unchanged, and every configuration
key `0.2.0` accepted is still accepted and still means the same thing. There is no breaking entry below.

What is new stands in front of the service rather than inside it: what terminates TLS for it, and what bounds the
surface you administer it through. The pages describing all of it are now published as
[a documentation site](https://krzysztof318.github.io/MailFathom/), with search, an API reference generated from the
source, and a version selector; `0.3.0` is the first release it carries a version for.

**One caveat, and it is a defect this release ships with**: a deployment that sets `HealthEndpoints:Enabled` to `false`
*and* enables the administrative endpoint loses its application listener, because binding a socket in code makes
Kestrel ignore `ASPNETCORE_HTTP_PORTS` and only the probe path restates it. The process starts and serves the
administrative port alone, and every MCP client is refused.
[#395](https://github.com/Krzysztof318/MailFathom/issues/395) carries the fix. Until it lands, leave the probes
enabled — the default — or state the application listener as a `Kestrel:Endpoints` entry.

### Added

**A deployment behind a TLS-terminating reverse proxy.** When nginx, Traefik, or an ingress controller holds the
certificate, the request that reaches MailFathom arrives as `http` under an internal name, and the deployment's public
identity survives the hop only in two headers.

- `X-Forwarded-Proto` and `X-Forwarded-Host` are read and applied before anything else sees the request, so OAuth
  discovery, the `401` challenge, and every absolute address MailFathom writes carry your public name — the
  protected-resource metadata document included, which is what a proxied OAuth deployment needed
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
- `ReverseProxy:TrustedProxies` names the addresses or CIDR networks those headers are believed from, and
  `ReverseProxy:MaximumForwardedHops` (default `1`) how far back through each header a value is believed
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371),
  [#397](https://github.com/Krzysztof318/MailFathom/pull/397)). It is one section for the whole process rather than one
  per surface: a proxy's address is a network fact, so it is stated once and holds on every listener. What you name
  replaces the framework's loopback default rather than adding to it, and `10.0.0.5/24` is refused naming the
  `10.0.0.0/24` it would otherwise silently have become.
- `X-Forwarded-For` is never read, so the peer MailFathom observes stays the one that opened the connection, and
  `McpEndpoint:OAuth:Resource` stays a value you wrote rather than anything derived from a header
  ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
- Client certificates are unreachable in this posture, because the handshake ended at the proxy and no header is read
  as a substitute ([#371](https://github.com/Krzysztof318/MailFathom/pull/371)).
  [Behind a TLS-terminating reverse proxy](https://krzysztof318.github.io/MailFathom/operations/mcp-endpoint.html#behind-a-tls-terminating-reverse-proxy)
  is the page, including what the proxy owns and what MailFathom keeps owning.

**A clear-text listener that redirects to HTTPS.** A surface that terminates TLS also binds one listener whose only
answer is a `308` to the address its profiles are served at, so a client nobody repointed meets a redirect rather than a
refused connection indistinguishable from an outage ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).

- `McpEndpoint:Https:Redirect` binds port `8080` and `AdminEndpoint:Https:Redirect` port `8091` unless you state
  another, each taking `Enabled`, `BindAddress`, and `Port`. The defaults differ so terminating TLS on both surfaces
  opens two clear-text ports that do not collide.
- That listener maps no route. Every path is answered the same way, and no authentication, rate-limiting, CORS, or
  client-certificate handler runs for a request that arrived on it, so there is nothing reachable over it to protect.
- `308` rather than `301` or `302`, because the MCP transport is a `POST` the older codes permit a client to re-send as
  a `GET`. The path and query are preserved, each domain redirects to its own profile's port, a `Host` header naming no
  configured domain gets `400`, and `:443` is left out of the `Location`.
- Writing the section for a surface that terminates no TLS fails startup rather than being ignored, and a socket
  conflict with any other listener in the process is reported against the section that asked for it. The health probes
  keep their own listener and are never asked on this port, because a probe follows no redirect.

**Rate limiting on the administrative endpoint.** `AdminEndpoint:RateLimiting` is the section
`McpEndpoint:RateLimiting` is, with the same keys, the same product defaults, and the same validation
([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).

- The two are configured independently and partitioned per surface, so neither endpoint's traffic reaches the other's
  limits: an agent that exhausted `/mcp` has taken nothing from the surface you would use to stop it, and the
  concurrency limits are separate for the same reason.
- The burst is the endpoint's rather than one caller's. These routes carry no authentication middleware of their own —
  the credential is judged behind the limiter, so a request about to be refused for a wrong key has still spent
  capacity — and there is therefore no identity to partition on. Size `TokenCapacity` as what the whole endpoint may
  burst to rather than what one operator may.

### Changed

- **An enabled administrative endpoint is bounded whether or not you configure it**: 20 concurrent requests and a burst
  of 60 restored every minute, which are the MCP endpoint's defaults. `0.2.0` served it unbounded, so a deployment
  whose automation asks faster than that raises the numbers or sets `AdminEndpoint:RateLimiting:Enabled` to `false`,
  which costs one startup warning ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- **Configuring an HTTPS profile now also binds a clear-text port** — `8080` beside the MCP profiles, `8091` beside the
  administrative ones. Where a proxy in front of the process already answers that port, or something else on the host
  holds it, set `…:Https:Redirect:Enabled` to `false`. A conflict with another listener of this process is refused at
  startup naming the section that asked for it rather than failing later as an address-in-use error
  ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).
- **Startup now reports the rate limits once per enabled endpoint rather than once**, and under a different logger
  category: `MailFathom.Host.Hosting.Warnings.TransportRateLimitingStartupReport`, where `0.2.0` wrote
  `…McpRateLimitingStartupReport`. A log pipeline that matches on that category updates it, or it stops seeing the
  line ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- The clear-text transport warning describes the deployment you configured once a trusted proxy is named, rather than
  suggesting `McpEndpoint:Https:Endpoints` to a deployment whose certificate lives on the proxy
  ([#378](https://github.com/Krzysztof318/MailFathom/pull/378)).

### Security

- **A deployment that names no trusted proxy trusts every peer.** An OAuth access token is refused when the request did
  not arrive over transport encryption, and that check reads the scheme a forwarded header set — so with
  `ReverseProxy:TrustedProxies` left empty, anything that can open a connection sends `X-Forwarded-Proto: https` and
  has a reusable credential accepted over a clear-text hop, and `X-Forwarded-Host` is believed on the same terms. Name
  the addresses or CIDR networks your proxies actually use. Every startup running on the wide default logs one line
  naming what the deployment gave up ([#378](https://github.com/Krzysztof318/MailFathom/pull/378),
  [#397](https://github.com/Krzysztof318/MailFathom/pull/397)).
- The administrative endpoint is bounded by default, which is what stops a surface reachable from a network from
  serving unbounded API-key guessing — the attack it is most exposed to, and the one where a successful guess is worth
  the most ([#373](https://github.com/Krzysztof318/MailFathom/pull/373)).
- A redirect protects the next request and never the one that arrived: a credential sent in clear text was on the wire
  before anything answered. Treat the redirect as a way to find out that a client needs repointing rather than as a
  supported way to reach the endpoint ([#374](https://github.com/Krzysztof318/MailFathom/pull/374)).

## [0.2.0] - 2026-08-04

The second release, and the first that had a previous one to differ from. **Nothing `0.1.0` promised is withdrawn:**
the MCP tool contract is unchanged, every configuration key `0.1.0` accepted is still accepted and still means the
same thing, and both schema changes are additive. There is no breaking entry below, so an upgrade is the schema step
and a new image.

What is new is how a mailbox authenticates, how quickly a change on the mail server reaches the local copy, and a
second HTTP surface — an administrative endpoint with a command-line client of its own — that an operator reaches
without going through the MCP surface.

**The database schema.** Two migrations, both additive: one table and one nullable column
([#343](https://github.com/Krzysztof318/MailFathom/pull/343),
[#346](https://github.com/Krzysztof318/MailFathom/pull/346)). `0.2.0` refuses to serve until they are applied —
startup is gated on the migrations the binary carries and will not migrate a database out from under a running
process — but `0.1.0` neither reads nor writes what they add, so **they can be applied while `0.1.0` is still
serving**, and the release then deploys over `0.1.0`'s data unchanged. The gate reads only what is *pending*, so a
database already carrying both migrations still starts `0.1.0`: going back needs no schema step of its own.
[Applying the database schema](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/database-schema.md)
records the apply path and the ordering a deployment follows.

### Added

**Mailbox authentication.** An IMAP account can present an OAuth token instead of a password.

- `XOAUTH2` and `OAUTHBEARER` are accepted in
  `MailSynchronization:Accounts:<n>:TransportSecurity:PermittedAuthenticationMechanisms`, and naming either one turns
  on that account's `…:OAuth` block: the token endpoint, the client, the scope, and the grant — `refresh_token` or
  `client_credentials` — with the client secret and the refresh token supplied by reference like every other
  credential. Configuring the block for an account that authenticates with a password fails startup rather than
  provisioning something nothing can use ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).
  [Mailbox OAuth](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/mailbox-oauth.md) is the page,
  including where each value comes from for the providers this was verified against.
- Calls to an authorization server get a retry, timeout, circuit-breaker, and concurrency budget of their own, as the
  `Resilience:MailAuthorizationServerInvocation` class, rather than borrowing the mailbox session's
  ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).

**Continuous synchronization.** A folder change on the mail server can start a pass, instead of every change waiting
for the account's next interval.

- `MailSynchronization:Accounts:<n>:Mode` selects `Polling` — `0.1.0`'s behaviour, and still the default — or `Push`
  ([#339](https://github.com/Krzysztof318/MailFathom/pull/339)).
- Under `Push`, a server offering `NOTIFY` is watched over **one** connection per account covering every configured
  folder, and a server offering only `IDLE` over one connection per folder
  ([#339](https://github.com/Krzysztof318/MailFathom/pull/339),
  [#346](https://github.com/Krzysztof318/MailFathom/pull/346)). `MaxSubscribedFolders` (default `20`) bounds how many
  folders one subscription may name; the rest synchronize on the account's interval rather than being dropped.
- Where the server offers `CONDSTORE` and `QRESYNC`, a pass asks what changed since the modification sequence it last
  reconciled through instead of re-reading the folder, which is what the new nullable
  `synchronization_checkpoints.ReconciledThroughModSeq` column records
  ([#346](https://github.com/Krzysztof318/MailFathom/pull/346)).
- Push degrades to polling rather than stalling: `MaxConsecutivePushFailures` (default `3`) and
  `PushDegradationPeriod` (default `15 min`) decide when an account falls back and for how long, and
  `PushRenewalInterval` (default `20 min`) is the lifetime of one `IDLE` command — RFC 2177's ceiling, not a polling
  cycle ([#339](https://github.com/Krzysztof318/MailFathom/pull/339),
  [#341](https://github.com/Krzysztof318/MailFathom/pull/341)). Synchronization stays read-only throughout: a push
  pass sets the remote `\Seen` flag no more than a polled one does.

**An administrative endpoint, and the `mfctl` command that reaches it.**

- `AdminEndpoint` serves administrative routes beneath `/api/admin` on a listener, a credential set, and a set of
  authorization servers of its own. It is off by default, and a key or an issuer configured under `McpEndpoint`
  authenticates nothing here — the two surfaces are protected independently rather than sharing one policy
  ([#313](https://github.com/Krzysztof318/MailFathom/pull/313),
  [#317](https://github.com/Krzysztof318/MailFathom/pull/317)).
- **Each release now attaches `mfctl`**, a self-contained binary per platform — `linux-x64`, `linux-arm64`, `win-x64`,
  `win-arm64` — plus one checksum file covering all of them. It runs where you administer *from* rather than where the
  service runs, and needs no .NET installation
  ([#317](https://github.com/Krzysztof318/MailFathom/pull/317)).
- `mfctl login` signs in with an API key read from standard input, with a browser redirect caught locally, or with a
  device code entered elsewhere, and keeps the credential in a profile file of its own with the tokens encrypted at
  rest — the refresh token included, so a session outlives an access token's expiry rather than sending the operator
  back through the flow ([#348](https://github.com/Krzysztof318/MailFathom/pull/348)).
  [Administering your deployment](https://github.com/Krzysztof318/MailFathom/blob/main/docs/users/administering.md)
  states what that encryption protects and what it does not.
- `mfctl mailbox authorize` runs a mailbox's own OAuth flow from the operator's machine and **sends the resulting
  refresh token to the deployment**, which seals and stores it, instead of printing it for the operator to paste into
  a configuration file ([#356](https://github.com/Krzysztof318/MailFathom/pull/356)).
- [Administering a deployment](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/admin-endpoint.md)
  is the reference: every route, every `--mode`, the configuration, and what each failure means.

  **One caveat, and it is a defect this release ships with**: a deployment that sets `HealthEndpoints:Enabled` to
  `false` *and* enables this endpoint loses its application listener, because binding the administrative socket in
  code makes Kestrel ignore `ASPNETCORE_HTTP_PORTS` and only the probe path restates it. The process starts and serves
  the administrative port alone, and every MCP client is refused.
  [#325](https://github.com/Krzysztof318/MailFathom/issues/325) carries the fix. Until it lands, leave the probes
  enabled — the default — or state the application listener as a `Kestrel:Endpoints` entry.

**Encryption at rest.**

- `DataEncryption` configures a key ring: one active key, any number of retained ones, each 32 bytes of material
  supplied by reference like every other credential, under which MailFathom seals what it stores. An absent section is
  a valid deployment that seals nothing, and rotation is moving `ActiveKeyId` while leaving the previous key
  configured, so nothing already sealed becomes unopenable
  ([#338](https://github.com/Krzysztof318/MailFathom/pull/338)).
  [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md)
  records the decision.
- The refresh token an authorization server rotates is followed and stored sealed under that ring, in the new
  `mailbox_refresh_tokens` table, so a provider that issues a new refresh token on every exchange no longer strands an
  account at the next restart ([#343](https://github.com/Krzysztof318/MailFathom/pull/343)).
- Docker Compose, the Helm chart, and the native systemd unit each provision the key by the same mechanism they
  provision every other secret, and the guides state where the file goes in each
  ([#354](https://github.com/Krzysztof318/MailFathom/pull/354)).
  [Secret provisioning](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/secret-provisioning.md)
  is the contract. **Back the key up with the database**: nothing in MailFathom regenerates it, and a database
  restored without its key restores nothing that was sealed under it.

### Changed

- `MailSynchronization:Accounts:<n>:Secrets:Password` is required only when the account's permitted mechanisms include
  a password mechanism. It was unconditionally required in `0.1.0`, which every configuration written for `0.1.0`
  already satisfies; what changes is that an account authenticating with OAuth alone now configures no password at all
  ([#306](https://github.com/Krzysztof318/MailFathom/pull/306)).

### Security

- A mailbox refresh token is held sealed in the database under the deployment's key ring rather than sitting in a
  configuration file or a secret file that nothing rotates, and the rotation an authorization server performs is
  followed rather than lost ([#343](https://github.com/Krzysztof318/MailFathom/pull/343)).
- The refresh token an authorization flow produces never reaches the operator's terminal: `mfctl mailbox authorize`
  sends it to the deployment over the administrative endpoint, so it is not in scrollback, in a shell history, or in a
  file somebody has to remember to delete ([#356](https://github.com/Krzysztof318/MailFathom/pull/356)).
- The administrative endpoint carries its own credentials, its own authorization servers, and its own TLS profiles, so
  granting somebody administrative access does not grant them the MCP surface and the reverse holds
  ([#313](https://github.com/Krzysztof318/MailFathom/pull/313),
  [#317](https://github.com/Krzysztof318/MailFathom/pull/317)).

## [0.1.0] - 2026-08-02

The first public release, and the point at which MailFathom's four public surfaces begin to promise anything. There is
no earlier release for this one to have changed, so every entry below is an addition rather than a difference.

**What it is.** A Model Context Protocol server for your own mail. It synchronizes IMAP mailboxes read-only into a
local PostgreSQL copy and serves that copy to an MCP client as three tools, so a client can list, read, and search
mail without a request ever reaching a mail server and without a message being marked as read.

**The database schema.** This release creates it. One baseline migration
([#241](https://github.com/Krzysztof318/MailFathom/pull/241),
[#127](https://github.com/Krzysztof318/MailFathom/pull/127)) builds the whole schema on an empty database, so there is
no previous version to apply it beside and nothing of an earlier release's to deploy over. The migration must be
applied before the host will serve: startup is gated on the schema and refuses to start against a database that is
behind it, rather than migrating one out from under a running process.
[Applying the database schema](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/database-schema.md)
records the apply path and the ordering a deployment follows.

### Added

**Mail synchronization.**

- Read-only IMAP synchronization of configured accounts and folders into a local PostgreSQL copy. Synchronization
  never sets the remote `\Seen` flag, and that invariant is proven against a real IMAP server rather than asserted
  ([#13](https://github.com/Krzysztof318/MailFathom/pull/13),
  [#132](https://github.com/Krzysztof318/MailFathom/pull/132)).
- Each configured account synchronizes on a schedule of its own, and a failure is isolated to the account and the
  folder it happened in rather than stopping the rest ([#167](https://github.com/Krzysztof318/MailFathom/pull/167)).
- Remote deletions and flag changes are reconciled back onto the local copy
  ([#171](https://github.com/Krzysztof318/MailFathom/pull/171)).
- Synchronization is bounded by a configured earliest received date, so an established mailbox is not backfilled in
  full on first run ([#133](https://github.com/Krzysztof318/MailFathom/pull/133)).
- Senders, recipients, subjects, and dates are read out of each stored message and indexed, so listing a folder by date
  reads an index rather than re-parsing stored mail ([#98](https://github.com/Krzysztof318/MailFathom/pull/98),
  [#106](https://github.com/Krzysztof318/MailFathom/pull/106)).
- Message text is indexed for full-text search as mail arrives, and anything already stored before that indexing
  existed is caught up in the background rather than left unsearchable
  ([#110](https://github.com/Krzysztof318/MailFathom/pull/110)).
- A folder renamed or re-created on the mail server is detected rather than silently followed
  ([#94](https://github.com/Krzysztof318/MailFathom/pull/94)).
- Everything MailFathom calls out to runs under a configurable timeout, a bounded retry with jittered backoff, and a
  circuit breaker, set per class of dependency ([#83](https://github.com/Krzysztof318/MailFathom/pull/83)), and a
  dropped IMAP session is recovered under that same budget
  ([#92](https://github.com/Krzysztof318/MailFathom/pull/92)).

**The MCP tool contract.** Served over the Streamable HTTP transport
([#135](https://github.com/Krzysztof318/MailFathom/pull/135)). Every call reads the local copy only, so no tool
request can wait on IMAP or change anything remotely, and every tool bounds how much mail one call can draw out.

- `list_emails` returns a bounded keyset page of message summaries — at most 100, with no body text — filtered by
  account, folder, and date ([#136](https://github.com/Krzysztof318/MailFathom/pull/136)).
- `get_email_content` returns bounded bodies for at most 10 named emails under a shared character budget, and names
  attachments only when asked ([#137](https://github.com/Krzysztof318/MailFathom/pull/137),
  [#153](https://github.com/Krzysztof318/MailFathom/pull/153),
  [#232](https://github.com/Krzysztof318/MailFathom/pull/232)).
- `search_emails` returns a bounded ranked window of at most 50 lexical matches, each with bounded extracts
  ([#138](https://github.com/Krzysztof318/MailFathom/pull/138),
  [#163](https://github.com/Krzysztof318/MailFathom/pull/163)).
- Every descriptor declares `readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint`, so a client can
  judge a tool before calling it. No error and no log line carries a filter value, a mailbox address, a subject, body
  text, raw MIME, or an internal identifier; every published failure carries a five-digit error code instead
  ([#111](https://github.com/Krzysztof318/MailFathom/pull/111)).

**What protects that endpoint.** It is disabled by default, and enabling it requires stating what a client presents.

- Named, expiring API keys, and `Origin` validation for browser callers through configurable CORS
  ([#169](https://github.com/Krzysztof318/MailFathom/pull/169)).
- OAuth 2.1 access tokens from configured authorization servers, judged against the issuer, this resource, the
  required scopes, and an explicit list of authorized subjects — so signing in to the authorization server does not by
  itself grant a user this mailbox ([#183](https://github.com/Krzysztof318/MailFathom/pull/183)).
- HTTPS on operator-provided domains and certificates, with the material proven to load, to cover the stated domain,
  and not to have expired before any listener opens ([#175](https://github.com/Krzysztof318/MailFathom/pull/175)).
- Mutual TLS through named client-certificate profiles, proven against a real TLS handshake
  ([#177](https://github.com/Krzysztof318/MailFathom/pull/177),
  [#196](https://github.com/Krzysztof318/MailFathom/pull/196)).
- Per-client token-bucket and process-wide concurrency rate limits, enabled by default, so an endpoint is bounded
  whether or not anyone wrote a number ([#176](https://github.com/Krzysztof318/MailFathom/pull/176)).
- A per-account mail transport security policy decides what TLS an account's connections require
  ([#58](https://github.com/Krzysztof318/MailFathom/pull/58)), and a host whose platform TLS policy refuses a mail
  server can be configured to reach it anyway, and says so when it does
  ([#226](https://github.com/Krzysztof318/MailFathom/pull/226)).

**The configuration schema.** Every MailFathom section is bound strictly: a key the section does not define fails
startup naming it, so a typo cannot silently leave a default in force, and a violated constraint fails startup with
the configuration path in the message.
[The configuration reference](https://github.com/Krzysztof318/MailFathom/blob/main/docs/operations/configuration-reference.md)
is the whole surface, key by key, including which keys reload and which need a restart.

- Secrets are supplied as references rather than inline values by default, so a plain-text credential where a
  reference belongs fails startup instead of authenticating
  ([#64](https://github.com/Krzysztof318/MailFathom/pull/64)).
- Certificate material and secrets are re-read behind unchanged references, so a renewal reaches the process without a
  restart ([#73](https://github.com/Krzysztof318/MailFathom/pull/73)).
- A mounted directory or file of JSON — a Kubernetes ConfigMap, a systemd drop-in — is a first-class configuration
  source ([#168](https://github.com/Krzysztof318/MailFathom/pull/168)).
- The deployment-wide privacy bounds on what a search result may quote, and on how much body text one read may return,
  are configuration rather than constants a caller could raise.

**The deployment contract.**

- A multi-architecture container image for `linux/amd64` and `linux/arm64`, published to `ghcr.io` **and** `docker.io`
  as one manifest list under one digest, under its immutable version tag with `latest` moved onto that same digest.
  The registry to pull from is whichever your environment already reaches
  ([#240](https://github.com/Krzysztof318/MailFathom/pull/240),
  [#256](https://github.com/Krzysztof318/MailFathom/pull/256),
  [#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- The Helm chart is published with the image, in the same run and at the same version, as an OCI artifact at
  `oci://ghcr.io/krzysztof318/charts/mailfathom`. Its `appVersion` is that release, so a chart states which application
  version it deploys without being unpacked, and it is listed on Artifact Hub
  ([#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- Every published artifact, image and chart alike, carries a signed provenance statement that
  `gh attestation verify` checks against this repository ([#281](https://github.com/Krzysztof318/MailFathom/pull/281)).
- Three supported installation shapes: Docker Compose, which provisions PostgreSQL for you; the Helm chart, which
  deliberately installs neither a database nor a Secret; and a native systemd process taking its secrets as systemd
  credentials ([#180](https://github.com/Krzysztof318/MailFathom/pull/180)). Linux is the only platform this project
  supports.
- Startup, readiness, and liveness probes on a listener of their own, with a configurable transport, which a
  deployment can turn off entirely ([#198](https://github.com/Krzysztof318/MailFathom/pull/198),
  [#264](https://github.com/Krzysztof318/MailFathom/pull/264)).
- Each release publishes an idempotent `mailfathom-schema-<version>.sql` artifact naming the migrations it carries and
  the checksum that identifies it ([#258](https://github.com/Krzysztof318/MailFathom/pull/258)).
- One version identifies a deployment wherever you look for it: the assemblies, the image's tags and labels, the
  packaged chart's `appVersion`, the line the host writes at startup, and the server's MCP `initialize` response all
  report the same number ([#208](https://github.com/Krzysztof318/MailFathom/pull/208)).
- OpenTelemetry logs, metrics, and traces export when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and host start, startup
  failure, and shutdown are reported from a bootstrap logger that exists before configuration does
  ([#89](https://github.com/Krzysztof318/MailFathom/pull/89)).
- Every published artifact carries `LICENSE` and `NOTICE`
  ([#172](https://github.com/Krzysztof318/MailFathom/pull/172)). MailFathom is licensed under Apache-2.0, and
  [`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) registers
  every dependency it ships beside ([#173](https://github.com/Krzysztof318/MailFathom/pull/173)).

[0.7.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/Krzysztof318/MailFathom/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Krzysztof318/MailFathom/releases/tag/v0.1.0
