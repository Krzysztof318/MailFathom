# Configuration reference

<!-- describes: src/**/*Options.cs, src/Host/Configuration/**, src/Domain/Access/MailFathomPermission.cs -->

Every user-settable option, in one place, checked against the options classes that bind it. Each section's table
states the key, its type, the value a deployment gets by writing nothing, the constraint startup enforces, and what a
change needs to take effect. The prose around a setting group — what it means, why it is shaped that way, how to
choose a value — lives on the page each section links; this page is the inventory.

## How to read the tables

**Keys.** Written in configuration-section form. As an environment variable, `:` becomes `__` and a list index is a
numbered segment: `MailSynchronization:Accounts:0:Host` is `MailSynchronization__Accounts__0__Host`. Where the
configuration comes from, and which source wins, is [configuration sources](configuration-sources.md).

**Types.** A `TimeSpan` binds from `hh:mm:ss` (`"00:05:00"` is five minutes; a leading `d.` adds days). A date binds
as `yyyy-MM-dd`, an instant as ISO 8601 with an explicit offset. An enum binds by member name, and a **secret block**
is the three-field shape [secret provisioning](secret-provisioning.md#the-secret-block) defines:

```json
{ "Name": "imap-primary-password", "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password", "Lifetime": "NoLimit" }
```

`Name` is the identity diagnostics use, `SecretReference` is `<scheme>:<target>` with the schemes
`systemd-credential:`, `file:`, `env:`, and `plaintext:`, and `Lifetime` is `NoLimit` (the default) or the ISO 8601
instant the material stops being accepted. Trust-anchor and certificate blocks nest a fourth field, `Password`, itself
a secret block, for protected PKCS#12 bundles.

**Change.** What ADR 0002 classifies for the group:

- *restart* — the section is read while the host composes itself; edit it, then restart.
- *reload* — a changed value is validated and, if sound, adopted by the next operation without a restart; a rejected
  candidate leaves the running configuration in force. Reload of a file-shaped source has caveats of its own under
  Kubernetes — see [configuration sources](configuration-sources.md#reload).

Whatever the classification, the **material behind a secret reference is read per use**: rotating a password, key, or
certificate behind an unchanged reference needs no restart and no reload. [Secret rotation](secret-rotation.md) walks
each case.

**Validation.** Every MailFathom section below is bound strictly: a key the section does not define fails startup
naming it, so a typo cannot silently leave a default in force. Values are validated on start, and a violated
constraint fails startup with the configuration path in the message. The two exceptions are the framework-shaped
entries — `Logging` and `ConnectionStrings` — and the single-key `Secrets:Interpretation`, which is read with a
default rather than bound as a section.

## `ConfigurationSources`

Names JSON configuration provisioned outside the application — a mounted ConfigMap, a systemd drop-in.
[Configuration sources](configuration-sources.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConfigurationSources:Directory` | string | unset | Must exist when named | restart |
| `ConfigurationSources:File` | string | unset | Must exist when named | restart |

The *content* of files that existed at startup reloads; adding or removing a file is a restart.

## `Secrets`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Secrets:Interpretation` | enum | `ReferenceOnly` | `ReferenceOnly`, `ReferenceOrInline`, `InlineOnly` | restart |

Under the default, a plain-text value where a reference belongs fails startup instead of authenticating.
[Interpretation modes](secret-provisioning.md#interpretation-modes) records when the other two are appropriate;
development keeps `ReferenceOrInline` so `plaintext:` references stay convenient.

## `MailSynchronization`

Whether and how mailboxes are synchronized. [IMAP synchronization](../features/imap-synchronization.md#configuration)
explains the model. The section reloads **per operation** — a run takes one validated snapshot when it begins, so a
changed account list, bound, or policy is adopted at the next run rather than mid-run — except the four values that
shape the coordinator loop itself, which are read once at start and marked *restart* below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailSynchronization:Enabled` | bool | `false` | Enabled requires at least one account | restart |
| `MailSynchronization:Interval` | TimeSpan | `00:05:00` | 10 s – 1 day; measured end-of-run to start-of-run | restart |
| `MailSynchronization:MaxFailureBackoff` | TimeSpan | `00:30:00` | 10 s – 1 day, and never below `Interval` | reload |
| `MailSynchronization:MaxConcurrentAccounts` | int | `4` | 1 – 100 | restart |
| `MailSynchronization:MaxConcurrentFoldersPerAccount` | int | `1` | 1 – 20 | reload |
| `MailSynchronization:WriteConnectionIdlePeriod` | TimeSpan | `00:02:00` | 5 s – 30 min; how long an account's single write connection keeps its slot after the last change it carried | restart |
| `MailSynchronization:MaxMutationAttempts` | int | `5` | 1 – 100; how many attempts one recorded change to a mailbox may spend before it is given up on and left visible as stuck | restart |
| `MailSynchronization:MaxMutationsPerConvergencePass` | int | `50` | 1 – 1000; how many unfinished changes one account run takes in hand before the rest wait for the next run | reload |
| `MailSynchronization:UnknownMutationOutcomeGrace` | TimeSpan | `06:00:00` | 1 min – 7 days; how long a change whose placement was never acknowledged waits to be settled by observation before it is given up on | reload |
| `MailSynchronization:ShutdownDrainTimeout` | TimeSpan | `00:00:10` | 0 – 2 min | restart |
| `MailSynchronization:MaxMetadataBatchSize` | int | `100` | 1 – 1000 | reload |
| `MailSynchronization:MaxRawMimeBytes` | long | `26214400` (25 MiB) | 1024 – 104857600; larger messages are stored without content | reload |
| `MailSynchronization:MaxMetadataBatchesPerRun` | int | `10` | 1 – 1000 | reload |
| `MailSynchronization:MaxContentBytesPerRun` | long | `1073741824` (1 GiB) | 1024 – 1099511627776; how much raw MIME one folder run may fetch before it ends at its checkpoint. Must be at least `MaxRawMimeBytes` | reload |
| `MailSynchronization:MaxStoredContentBytes` | long | *(none)* | 1024 – `9223372036854775807`; how much storage stored content may occupy before ingestion degrades to metadata only. Unset means no ceiling. Must be at least `MaxRawMimeBytes` | restart |
| `MailSynchronization:MaxInFlightRawMimeBytes` | long | `134217728` (128 MiB) | 1024 – 4294967296; how much raw MIME every folder work unit together may hold in memory. Must be at least `MaxRawMimeBytes` | restart |
| `MailSynchronization:MaxReconciledEmailsPerRun` | int | `500` | 1 – 10000 | reload |
| `MailSynchronization:MaxMimePartCount` | int | `1000` | 1 – 100000 | reload |
| `MailSynchronization:MaxMimeNestingDepth` | int | `30` | 1 – 1000 | reload |
| `MailSynchronization:MaxExtractedTextCharacters` | int | `100000` | 1000 – 200000; the ceiling keeps the search vector inside PostgreSQL's limit | reload |
| `MailSynchronization:PushRenewalInterval` | TimeSpan | `00:20:00` | 1 min – 29 min; the lifetime of one `IDLE` command, **not** a polling cycle — the ceiling is what RFC 2177 mandates | reload |
| `MailSynchronization:MaxConsecutivePushFailures` | int | `3` | 1 – 100 | reload |
| `MailSynchronization:PushDegradationPeriod` | TimeSpan | `00:15:00` | 10 s – 1 day | reload |
| `MailSynchronization:MaxSubscribedFolders` | int | `20` | 1 – 100; how many folders one push subscription may name on a server supporting `NOTIFY`, the rest synchronizing on the account's interval | reload |
| `MailSynchronization:TrustOwnAccountDomains` | bool | `true` | Whether an author writing from a domain one of the configured accounts uses counts as trusted on every account. The set is read from each account's `UserName` where that is an address, so it needs no list of its own | reload; the next extraction judges against it |
| `MailSynchronization:AssessMachineAuthorship` | bool | `true` | Whether extraction reads how much each message's own text reads as machine written. What the reading weighs is the project's and is not configurable; this decides only whether it runs | reload; the next extraction reads against it |

### One account — `MailSynchronization:Accounts:<n>`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AccountId` | string | — | Required; unique across accounts after normalization | reload |
| `…:DisplayName` | string | — | Required, with no default; at most 128 characters, no control characters, and it may not be another account's identifier or display name compared without regard to case | reload |
| `…:Host` | string | — | Required when synchronization is enabled | reload |
| `…:Port` | int | `993` | 1 – 65535 | reload |
| `…:UserName` | string | — | Required when synchronization is enabled; an identifier, not a secret | reload |
| `…:Secrets:Password` | secret block | unset | Required when the permitted mechanisms include any password mechanism; must resolve at startup | reload; material per connection |
| `…:Mode` | enum | `Polling` | `Polling`, `Push`; push holds one connection open per account on a server supporting `NOTIFY`, and one per folder on a server offering only `IDLE` | reload; the next run adopts it |
| `…:EarliestEmailReceivedDate` | date | unset (everything) | Not in the future (compared in UTC) | reload |
| `…:TrustedAuthenticationServiceIdentifier` | string | unset (believe no header) | The authserv-id of the server that receives this account's mail; at most 253 characters and no whitespace, compared without regard to case. Present and unusable fails startup; omitted is an ordinary choice | reload; the next extraction reads against it |
| `…:RemotelyDeletedEmailDisposition` | enum | `RetainTombstone` | `RetainTombstone`, `EraseLocalCopy` | reload; governs disappearances observed from then on |
| `…:AuthoredDeleteEmailDisposition` | enum | `RetainLocalCopy` | `RetainLocalCopy`, `RetainTombstone`, `EraseLocalCopy`; what becomes of the local copy of mail MailFathom itself deleted, and it takes precedence over the key above for those | reload; governs deletes authored from then on |
| `…:RuleActions:Move` | bool | `true` | Whether a rule may file this account's mail into another of its folders | reload; the next rule pass writes down no change this refuses |
| `…:RuleActions:Copy` | bool | `true` | Whether a rule may place a copy of this account's mail in another of its folders | reload; the same |
| `…:RuleActions:Delete` | bool | `false` | Whether a rule may remove this account's mail; the one action that is opt-in | reload; the same |
| `…:RuleActions:MarkAsRead` | bool | `true` | Whether a rule may set or clear this account's remote `\Seen` flag | reload; the same |
| `…:RuleActions:MarkAsFlagged` | bool | `true` | Whether a rule may set or clear this account's remote `\Flagged` flag | reload; the same |
| `…:RuleActions:WriteKeywords` | bool | `true` | Whether a rule may add, remove, or replace this account's keywords; one switch for all three, since permitting an addition while refusing a removal would leave labels nothing may take off | reload; the same |
| `…:AuditTrail:Enabled` | bool | `false` | Whether a finished change to this account's mailbox leaves a durable audit entry | reload; governs changes authored from then on |
| `…:AuditTrail:Retention` | TimeSpan | `90.00:00:00` | 1 day – 3650 days; how long this account's audit entries are kept | reload; the next account run erases against the new window |
| `…:AnsweringAuditTrail:Enabled` | bool | `false` | Whether a finished `ask_mail` run leaves a durable entry naming the mail it read from this account | reload; governs runs from then on |
| `…:AnsweringAuditTrail:Retention` | TimeSpan | `30.00:00:00` | 1 day – 3650 days; how long this account's answering entries are kept | reload; the next account run erases against the new window |
| `…:TrustedSenders` | list | empty | The authors this account recognizes on top of the deployment's own domains; each entry below | reload; the next extraction judges against it |
| `…:TrustedSenders:<n>:Domain` | string | unset | A domain this account recognizes. Exactly one of `Domain` and `Address` is written, and an entry writing neither or both fails startup naming the account and the entry's position | reload |
| `…:TrustedSenders:<n>:Address` | string | unset | A single mailbox this account recognizes. It matches when the established author's domain is that address's own **and** the message's `From` displays exactly that address | reload |
| `…:TrustedSenders:<n>:IncludeSubdomains` | bool | `false` | Whether a domain entry also reaches the names beneath that domain. Refused on an address entry, where it could mean nothing | reload |
| `…:ContactCollection:Enabled` | bool | `false` | Whether this account records the people it corresponds with as its mail is synchronized | reload; the next folder run collects under it |
| `…:ContactCollection:MinimumMessagesFromSender` | int | `2` | 1 – 100; how many messages an address must have written to this account before the person behind it is recorded. One records every admitted sender on first sight. It bounds only that direction — an address the owner wrote to is recorded at once | reload; the next folder run |
| `…:ContactCollection:MaxContactsPerRun` | int | `50` | 0 – 1000; how many contacts one folder run may record, bounded per folder exactly as `MaxContentBytesPerRun` is, so an account synchronizing several folders may reach it once for each. Zero records nobody while leaving collection on | reload; the run after the one in flight |
| `…:ContactCollection:Exclusions` | list | empty | The addresses and domains this account never records a contact from; each entry below | reload; the next folder run |
| `…:ContactCollection:Exclusions:<n>:Domain` | string | unset | A domain this account collects nobody at. Exactly one of `Domain` and `AddressPattern` is written, and an entry writing neither or both fails startup naming the account and the entry's position | reload |
| `…:ContactCollection:Exclusions:<n>:AddressPattern` | string | unset | A pattern over the whole address, where `*` stands for any run of characters including none and `?` for exactly one; at most 320 characters, and a pattern whose only characters are those two and the at-sign is refused, because `*@*` takes every address and `*@` takes none | reload |
| `…:ContactCollection:Exclusions:<n>:IncludeSubdomains` | bool | `false` | Whether a domain entry also reaches the names beneath that domain. Refused on a pattern entry, which writes its own | reload |
| `…:Folders` | list | inbox by role | Aliases unique; each entry below | reload |

`TrustedAuthenticationServiceIdentifier` names the one server whose `Authentication-Results` headers this account
believes, which is what stops the check from being defeated by a header an attacker wrote upstream. There is nothing to
default it to, because the right value is a property of who receives this account's mail; an account that omits it
believes no header and every message it holds records that nothing was established.
[Sender authentication](../features/sender-authentication.md) states how the header is chosen and what the verdict
holds.

`TrustedSenders` and `TrustOwnAccountDomains` are the second half of that: they decide whether a message's author is
somebody this deployment recognizes, which is a separate question from what the receiving server established. Both
lists are held against an **authenticated author** — the domain the receiving server's DMARC result or a matching
DKIM or SPF identity established for the `From` header — and never against the raw header, so naming a correspondent
here cannot be exploited by writing their address into a message. Most legitimate mail stays unknown and that is the
intended outcome: the claim is that this deployment does not know the author, not that the message is suspicious.
Turning `TrustOwnAccountDomains` off is the right move for a deployment whose accounts sit on a large shared provider,
since every user of that provider writes from the same domain; the same page states what an address entry rests on and
what it deliberately does not establish.

`AssessMachineAuthorship` answers a different question again — how a message's text was *written* rather than who sent
it — and it defaults to on because the reading costs one pass over text extraction has already produced and needs
nothing configured to mean something. What it reports is **informational and not a safety signal**: a high reading is an
observation about the text, not a finding against the message or its sender, and nothing in MailFathom acts on it. The
case for turning it off is a deployment that would rather its readers were not handed the observation at all; a
deployment that does records the same not-assessed state a message with no readable body carries, so no stored row says
which of the two reasons produced it. [Machine authorship](../features/machine-authorship.md) states what the reading is
made of and what it deliberately does not claim.

`RuleActions` is what a rule is judged against rather than what it is filtered by: a rule declaring an action this
account does not permit **fails startup** naming the rule, the action, and the account, rather than running with that
action quietly dropped. An unscoped rule reaches every declared account, so permitting an action on one account and not
on another means either permitting it on both or narrowing the rule's own `Accounts` filter. Narrowing this block while
a rule set that declares the action is in force is the one case the two do not resolve together, since the sections
reload apart: the permission is read again as each change is written down, so the withdrawal takes effect at the next
pass and the rule is refused the next time the rule section is read.
[What an account permits a rule to do](../features/mail-rules.md#what-an-account-permits-a-rule-to-do) states why
deletion is the one opt-in of the four.

`AuditTrail` is off by default because the record it keeps is derived personal data: it says where a person's mail has
been, when, and at whose instruction. Turning it on commits the deployment to holding that history, describing it, and
erasing it — which is why the retention is configured beside the switch rather than left unbounded, and why turning the
switch back off stops new entries while leaving the existing ones to age out under the window they were written under.
[An account can keep a record of what was done to it](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)
states what an entry holds and what it deliberately does not.

`AnsweringAuditTrail` is the same shape for the other record and the same default, and it is a **separate decision**
rather than the same switch: one says where a person's mail has been, the other says what it was read for, and an
operator may want either without the other. Its default window is shorter because an entry names every message one
question reached, so the record grows with how much an instance is asked rather than with how much it is told to change.
[An account can keep a record of what a question read](../features/mail-answering.md#an-account-can-keep-a-record-of-what-a-question-read-and-none-does-by-default)
states what an entry holds, what it deliberately does not, and the one way it differs from the trail above — an erased
message is erased from the runs that read it.

`AccountId` and `DisplayName` are both names for the account and they answer different questions. The identifier is the
stable key everything else is expressed in — every stored row, every continuation cursor, every log line — and it is
what you keep unchanged. The display name is what a caller reads: it appears beside the identifier in every MCP result
that names an account, and either spelling may be used to narrow a listing, a search, or a question to that mailbox.
There is deliberately no default, because a name MailFathom invented would be published to callers as though you had
chosen it. The two share one naming space so that a name can never select two mailboxes, which is why startup refuses a
display name that another account's identifier or display name already carries; a display name equal to the account's
*own* identifier is fine, since both spellings then reach the same mailbox.

A folder entry names `Alias` (required — your stable name for the folder) and **at least one** of `RemotePath` (the
server's own path) or `SpecialUse` (`Inbox`, `Archive`, `Drafts`, `Sent`, `Junk`, `Trash`, `All`, `Flagged`,
`Important`). Configuring no folder synchronizes the inbox by role.

**This list is what the deployment has.** A folder no entry names does not exist for any reader: nothing lists, searches,
reads, or answers from it, no rule is evaluated against its mail, nothing cuts it into passages or embeds it, and no
alias of it resolves as a rule's destination. That holds for mail an earlier configuration had already stored — removing
an entry makes its rows unreachable and leaves them in the database, since removing a mapping is an edit rather than an
act against stored mail, exactly as `Synchronize: false` is. Naming the folder again is what makes its mail readable
again, and the folder resumes from the checkpoint it kept rather than mirroring afresh.
[What a mapping decides beyond where the folder is](../features/imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states it beside the three switches.

`SpecialUse` says what the folder is *for*, and where the folder is found is a separate question. Named alone it
answers both: discovery resolves the folder the server advertises with that role. Named beside a `RemotePath`, the path
is what finds the folder and the role is what the folder plays, which is how a server that advertises nothing still
gets a junk folder anything can name by its role. A role belongs to **at most one folder per account**: startup refuses
a configuration giving one role to two folders of an account, naming both aliases and the role. Roles are optional and
most folders carry none.

Wherever a folder is named — a rule's destination, an MCP tool's `folders` argument — the role is written as
`role:<role>`, for example `role:Junk`, and anything without that prefix is an alias. A role no folder of the account
carries is refused, naming the role, rather than answered with an empty result.

The same entry decides what MailFathom does with the folder, through three switches that each default to `true`, and
whether the folder may be created at all, through a fourth that defaults to `false`:

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Folders:<n>:Synchronize` | bool | `true` | With `false`, no run schedules the folder: no connection is opened for it and nothing further of it is stored | reload; the next run stops scheduling it and keeps everything already stored for it |
| `…:Folders:<n>:GenerateEmbeddings` | bool | `true` | With `false`, stored mail of the folder is never cut into passages and never reaches an embedding provider; refused alongside `Synchronize: false` | reload; governs what is stored from then on, and passages already produced stay |
| `…:Folders:<n>:VisibleToTools` | bool | `true` | With `false`, no MCP tool lists, searches, reads, or answers from the folder; refused alongside `Synchronize: false` | reload; the next request reads the new value |
| `…:Folders:<n>:CreateIfMissing` | bool | `false` | With `true`, the folder is created on the mail server when the server advertises none at `RemotePath`; refused on a mapping that names no `RemotePath` | reload; the next resolution of the alias creates it |

Startup refuses a folder that asks for embedding or tool visibility while `Synchronize` is `false`, naming the alias,
because a folder that stores nothing has nothing to embed and nothing a tool could read. Leaving a switch out is not
asking for it, so `Synchronize: false` on its own binds. Mirrored, embedded, and withheld from tools binds as well and
costs what it says: the vectors are produced and paid for while no reader reaches them, since the tools are the only
readers there are.

`CreateIfMissing` is the one switch here that authorizes an act against your mail server rather than withdrawing an
existing folder from something MailFathom does locally, which is why it defaults to `false` while the other three
default to `true`: a mapping that says nothing keeps a mistyped `RemotePath` reporting itself as an alias that resolves
to nothing, instead of turning the mistake into a folder named after it. Startup refuses it on a mapping that names no
`RemotePath`, naming the alias, because a folder that does not exist advertises no role and only an explicit path says
what to create. A mapping naming both a path and a role may ask for the creation: the path is what is created, and the
role is what the created folder plays. It is issued where the alias is
resolved — before the run of a folder the account mirrors, and at the moment a change first files into one it does not,
so the switch reaches a `Synchronize: false` mapping the first time something files mail into it. Renaming, deleting, and
unsubscribing from a folder stay refused outright, and no folder MailFathom did not create is ever subscribed to.
[A folder the mapping asked for is created](../features/imap-synchronization.md#a-folder-the-mapping-asked-for-is-created)
states when the creation happens, what it does with a folder that already exists and with a hierarchical path, and what
a server's refusal reports.

Switching `Synchronize` off for a folder that was mirrored **keeps what is stored for it**. Nothing is removed, and the
folder's checkpoint stays where the last run left it, so switching the folder back on resumes: the next run fetches
what arrived while it was off and reconciles the retained mail rather than mirroring the folder again. What is kept is
inert — no tool lists, searches, reads, or answers from it, nothing of it is embedded, and no rule evaluates it — so
the only thing an operator gives up by leaving the switch off is the storage it occupies. No configuration value erases
stored mail: taking a folder's local copy away is an act somebody performs, never something a switch performs on their
behalf. The mapping stays too, so the alias goes on resolving, any role the mapping names goes on being answered, and
the folder stays a destination a rule may file mail into — resolved the first time a change names it, since no run
schedules it.
[What a mapping decides beyond where the folder is](../features/imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states all three switches together, what an unmapped folder is instead, and what becomes of the local copy of a message
relocated into a folder nothing mirrors.

### Contact collection

`…:ContactCollection` is per account and off unless an owner switches it on, because what it produces is derived
personal data about people who never dealt with MailFathom: an instance nobody asked never accumulates a contact book,
and a deployment reading a work mailbox and a personal one decides separately for each. Switched on, the account records
the author of mail arriving in its ordinary folders and the primary recipients of mail in the folder mapped as `Sent`,
as those messages are synchronized.

The two numbers bound who is written down and how fast. `MinimumMessagesFromSender` is the evidence an address that
wrote to the owner needs — two by default, because one message from a stranger is not correspondence — and it says
nothing about an address the owner wrote to, which is recorded on first sight. `MaxContactsPerRun` paces the first
synchronization of a mailbox holding years of mail; a run that reaches it leaves the rest for the next run rather than
losing them.

`Exclusions` is the owner's own list. The structural half needs no entry and cannot be switched off: a message a mailing
list or an automatic responder stamped as its own, a role mailbox, a `no-reply` name, a list-administration address, and
every mailbox a configured account's own user name names. An entry naming both a domain and a pattern, or neither, or
asking to include subdomains on a pattern, or writing a pattern that selects on nothing but the at-sign every address
carries, **fails startup** naming the
account and the entry's position and never the value it holds — because a domain and a pattern over an address are both
personal data, and a validation failure is written to a log.

[Contacts § Collecting contacts from arriving mail](../features/contacts.md#collecting-contacts-from-arriving-mail)
states which header each folder contributes, what is never collected, and how an owner takes back everything a
deployment collected.

### OAuth — `…:OAuth`

Read only when the account's permitted mechanisms include `XOAUTH2` or `OAUTHBEARER`. An account that authenticates
with a password leaves the whole block unset, and configuring it anyway fails startup rather than provisioning
credentials nothing can use. [Mailbox OAuth](mailbox-oauth.md) covers where each value comes from.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:OAuth:Grant` | string | — | `refresh_token` or `client_credentials` | reload |
| `…:OAuth:TokenEndpoint` | string | — | Absolute HTTPS address; no opt-in exists for `http` | reload |
| `…:OAuth:ClientId` | string | — | Required; an identifier, not a secret | reload |
| `…:OAuth:Scope` | string | — | Space-delimited, as RFC 6749 defines it | reload |
| `…:OAuth:PublicClient` | bool | `false` | Set when the application is registered as a public client, which holds no secret | reload |
| `…:OAuth:ClientSecret` | secret block | unset | Required unless `PublicClient` is `true`, and refused alongside it; must resolve at startup | reload; material per token request |
| `…:OAuth:RefreshToken` | secret block | unset | Required by `refresh_token`; absent for `client_credentials` | reload; material per token request |

### Transport security — `…:TransportSecurity`

[The rules](../features/imap-synchronization.md#transport-security) are the domain's; every weakening is an explicit
opt-in, and unsafe combinations fail startup.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:ConnectionSecurity` | enum | `TlsOnConnect` | `Auto`, `TlsOnConnect`, `StartTlsRequired`, `StartTlsWhenAvailable`, `None`; anything but the two guaranteed-TLS modes requires `AllowInsecureConnection` | reload |
| `…:PermittedAuthenticationMechanisms` | string list | `PLAIN`, `LOGIN` | Supported SASL names, including `XOAUTH2` and `OAUTHBEARER`; an unordered allow-list, the client picks the strongest that survives | reload |
| `…:AllowInsecureConnection` | bool | `false` | Opt-in for modes that can leave the channel unencrypted | reload |
| `…:AllowClearTextAuthenticationOverUnencryptedConnection` | bool | `false` | Opt-in on top of the above | reload |
| `…:CertificateTrust` | enum | `SystemTrustStore` | `SystemTrustStore`, `AdditionalTrustedAuthority` | reload |
| `…:TrustedCertificateAuthority` | secret block | unset | Required by, and only valid with, `AdditionalTrustedAuthority` | reload; material per connection |

Certificate validation itself cannot be disabled; a private server is supported by trusting its authority.

### Submission endpoint — `…:Delivery`

Where this account's mail would be submitted, which is a second server and never the one above. The whole block is
optional: an account that names no `Host` configures no submission endpoint, and no delivery session can be opened for
it. Naming one has the block validated at startup, so an unsafe or incomplete endpoint is refused there rather than at
the moment something tries to send. [Mail delivery](../features/mail-delivery.md) states what a session establishes and
what it does not.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Delivery:Host` | string | unset | Its presence is what configures the endpoint; every other key here has a default or an inherited value | reload |
| `…:Delivery:Port` | int | `465` | 1 – 65535; the default is the implicit-TLS submission port, agreeing with the default connection security | reload |
| `…:Delivery:ConnectionSecurity` | enum | `TlsOnConnect` | The same five modes judged by the same rules as the reading endpoint's, against the account's own opt-ins | reload |
| `…:Delivery:UserName` | string | unset (the account's) | An identifier, not a secret; for a relay that authenticates a different login than the mailbox does | reload |
| `…:Delivery:Secrets:Password` | secret block | unset (the account's) | A block naming no reference reads as absent and falls back to the account's credential | reload; material per connection |
| `…:Delivery:FromAddress` | string | unset (the account's `UserName` when it is a mailbox address) | A mailbox address; startup refuses an endpoint that resolves to none | reload |
| `…:Delivery:FromDisplayName` | string | unset (the address alone) | The name recipients see this mailbox sign itself with; deliberately not the account's `DisplayName` | reload |

The permitted mechanisms, both weakenings, and the certificate authority are **not** repeated here: they are one
decision the account makes about itself in `TransportSecurity` above, and both endpoints are reached under it. What
differs between the two servers is where they are and how the channel to them is encrypted, which is exactly what this
block carries.

The two sending keys are the account's answer to *who this mailbox writes as*, and they are configuration precisely so
that nothing else can be. No request, rule, or tool argument names a sender, so the only way to send as somebody else is
to send through an account an operator configured that way. Most deployments write neither key: a provider that
authenticates the mailbox by its address has already stated it as `UserName`, and the address alone is what recipients
see.

## `MailDelivery`

How large a message this deployment is willing to compose, and how it delivers one. The submission endpoints above are
per account because they are different servers; these are one answer for the whole installation, because what a mailbox
may send is a policy an operator holds once and a provider that is briefly unreachable is answered the same way
whichever mailbox was waiting on it. `MaxMessageBytes` is the only key here the submission server has an answer to as
well, through the size it advertises on connection, and a composed message is measured against both with the smaller
deciding. The other composition bounds are this deployment's alone: no server advertises how many people a message may
be addressed to or how many files it may carry, so a small server bound is no protection against either.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailDelivery:MaxRecipientCount` | int | `50` | 1 – 256, the ceiling being what one outgoing record holds | restart |
| `MailDelivery:MaxBodyCharacters` | int | `100000` | 1 – 10000000; applies to the plain text and to the HTML alternative separately | restart |
| `MailDelivery:MaxAttachmentCount` | int | `10` | 0 – 100; `0` attaches nothing | restart |
| `MailDelivery:MaxAttachmentBytes` | long | `10485760` | 1 – 104857600, and never above `MaxMessageBytes` while files may be attached at all | restart |
| `MailDelivery:MaxMessageBytes` | long | `26214400` | 1 – 209715200; measured on the composed bytes rather than summed from the parts | restart |

The whole-message bound is measured on what the message became rather than on what an author supplied, because transfer
encoding decides the difference: base64 costs roughly a third more than the octets it carries, and headers, boundaries,
and folding are the rest of it.

The remaining keys govern the delivery of what has been written down: how much of one account's outbox a pass takes,
how long it holds it, and how patient this deployment is with a submission server that is not answering.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailDelivery:MaxDeliveriesPerPass` | int | `10` | 1 – 1000 | restart |
| `MailDelivery:LeaseDuration` | TimeSpan | `00:10:00` | 30 s – 1 h | restart |
| `MailDelivery:AttemptTimeout` | TimeSpan | `00:07:00` | 10 s – 59 min, and always shorter than `LeaseDuration` | restart |
| `MailDelivery:MaxAttempts` | int | `5` | 1 – 100; `1` leaves no retry at all | restart |
| `MailDelivery:RetryBaseDelay` | TimeSpan | `00:01:00` | 1 s – 1 h | restart |
| `MailDelivery:RetryMaxDelay` | TimeSpan | `01:00:00` | 1 s – 24 h, and never below `RetryBaseDelay` | restart |
| `MailDelivery:SignalQueueCapacity` | int | `64` | 1 – 1000 | restart |

**`AttemptTimeout` below `LeaseDuration` is the one ordering startup refuses rather than warns about.** The lease is
what lets a crashed process's send be attempted again without anything being told the process died, and an attempt
still transmitting when its own lease expires is a second attempt taking a message the first may already have sent. So
the attempt is cancelled first, by a margin the operator chooses, and a configuration stating otherwise fails startup
naming `AttemptTimeout`.

A send that spends `MaxAttempts` stops being attempted and stands in the outbox where an operator can see it, rather
than being retried forever; a permanent refusal is terminal at the first answer and never spends the remaining
attempts. Between attempts the delay doubles from `RetryBaseDelay`, drawn with jitter so a provider that refused every
account at once is not offered all of them back together, and is capped at `RetryMaxDelay`.

`SignalQueueCapacity` bounds only how promptly a send leaves, never whether it leaves. The queue holds accounts rather
than messages and an account already waiting is not queued twice, so it cannot grow past the number of configured
accounts however much is enqueued: raising it past that buys nothing, and a value below it means a signal is
occasionally refused and those sends wait for the account's own synchronization run instead.

## `Persistence` and the connection string

Where the local copy lives. The connection settings travel through the validated snapshot, so repointing them reaches
the next physical connection without a restart; the remaining settings are read while the host composes itself.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConnectionStrings:mailfathom` | string | `Host=localhost;Database=mailfathom;Username=mailfathom` | Carries no password | reload (new connections) |
| `Persistence:ConnectionString` | secret block | unset | Replaces `ConnectionStrings:mailfathom` entirely when set | reload (new connections) |
| `Persistence:Password` | secret block | unset | A present block must carry a reference | reload (new connections); material per connection |
| `Persistence:MaximumConcurrencyCommitAttempts` | int | `2` | 1 – 10; counts the first attempt | restart |
| `Persistence:CommandTimeoutSeconds` | int | `30` | 1 – 600; bounds one command, not one unit of work | restart |
| `Persistence:TextSearchConfiguration` | string | `simple` | A stock PostgreSQL text search configuration (`simple`, `english`, `german`, …) | restart — **and it is part of the schema**: the value is compiled into the index, startup fails with `32003` on a mismatch, and changing it means regenerating the migration and rebuilding the search documents |

Repointing a reference or editing the connection string reloads; changing *which* setting supplies the credential —
moving a password out of the connection string into `Persistence:Password`, or back — is refused on reload and needs a
restart, because the connection pool attaches its password provider once.

## `DataEncryption`

The key ring every value MailFathom seals at rest is sealed under. A configuration root of its own rather than a
section of `Persistence`, because the database is the first thing sealed under it and there is no reason it is the
last. [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) records the whole decision, and
[secret provisioning](secret-provisioning.md) states how the material is generated and referenced.

An absent section is a valid deployment that seals nothing. Configuring the section makes every rule below apply.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `DataEncryption:ActiveKeyId` | string | unset | Must name one of `Keys`; required once any key is configured, and refused when none is | reload |
| `DataEncryption:Keys:<n>:KeyId` | string | — | Up to 64 letters, digits, dots, dashes, and underscores, beginning with a letter or a digit; unique within the ring | reload |
| `DataEncryption:Keys:<n>:Material` | secret block | — | Base64 decoding to exactly 32 bytes, generated with `openssl rand -base64 32` | reload; material per operation |

`KeyId` is stored beside every value the key seals, so it is chosen once and never edited — renaming it orphans every
value already carrying the previous spelling. The operator's own label for a key is its material's `Name`, which every
secret block requires; there is no second name on the entry.

The ring holds several keys so that rotation needs no downtime: move `ActiveKeyId` to the new key, leave the previous
key configured, and every value still carrying it keeps opening under it. Removing a key the database still references
makes those values unopenable, and the failure appears at the next read rather than at the edit.

## `Deployment`

What this installation is, rather than what any one surface it serves does. One key today, and a root of its own for
that reason: the address clients reach this deployment at is not a property of the feature that first needed it, so an
operator answers it once and whatever else has to hand back an absolute address later reads the same key.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Deployment:PublicBaseAddress` | url | — | Absolute, `https` unless the host is loopback, no path, no query, no fragment | restart |

**It has no default on purpose.** Only an operator knows which name a client reaches this process by, and a guess would
produce addresses that resolve to nothing or, worse, to somebody else. Nothing derives it from a request either: an
address composed from a `Host` header would let whoever called a tool decide where the URL it receives points.

It carries no path because this process serves its routes at its root, and clear text is refused off this machine
because what is composed beneath it may be a capability — a secret in transit. Today the one consumer is the
[attachment download link](../features/email-content.md#what-a-download-link-is-and-what-bounds-it); a deployment that
declares no address issues none, which is a supported posture rather than a misconfiguration.

## `SensitiveContent`

What this deployment scans mail for before that mail is copied into a derived store or handed out. A configuration root
of its own, because it is a property of the deployment rather than of its database, its accounts, or its providers, and
because the switches it holds reach several of those at once. [Sensitive-content
scanning](../features/sensitive-content-scanning.md) records what a finding is, what replaces it, and why a scanner that
cannot answer refuses the operation it guards.

Both scanners are off by default, and an absent section is that default rather than a startup failure. `Secrets` runs in
this process. `Pii` reaches an analyzer deployed beside it, configured in the block below, and switching it on with
nowhere to ask **fails startup** rather than running unprotected.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `SensitiveContent:Secrets:Enabled` | bool | `false` | A scanner switched on with no detector registered fails startup | restart |
| `SensitiveContent:Secrets:Categories:<n>` | string | unset | Must name a category the scanner detects; the list replaces the scanner's defaults, and an absent list yields them | restart |
| `SensitiveContent:Secrets:Suppressions:<n>:Category` | string | — | Must name a category the scanner detects; naming one never switches it on | restart |
| `SensitiveContent:Secrets:Suppressions:<n>:Rule` | string | — | Must name a rule that category holds | restart |
| `SensitiveContent:Pii:Enabled` | bool | `false` | As above, for the personal-data scanner | restart |
| `SensitiveContent:Pii:Categories:<n>` | string | unset | As above | restart |
| `SensitiveContent:Pii:Suppressions:<n>:Category` | string | — | As above | restart |
| `SensitiveContent:Pii:Suppressions:<n>:Rule` | string | — | As above | restart |
| `SensitiveContent:PersonalDataAnalyzer:Endpoint` | string | unset | Required once `Pii` is on, and an absolute `http` or `https` address; read by nothing while that switch is off | restart |
| `SensitiveContent:PersonalDataAnalyzer:Languages:<n>` | string | unset | Two lowercase letters each, naming a language the analyzer loads a model for and registers recognizers in; an absent list yields `en`. At most eight, since one scan asks once per language inside a single `ScanTimeout`. The order is not read — the set is deduplicated and ordered before use — and the set is part of the derivation stamp | restart |
| `SensitiveContent:PersonalDataAnalyzer:MinimumConfidence` | double | `0.4` | 0 – 1 inclusive, compared inclusively by the analyzer. It decides which regions are replaced, so it is part of the derivation stamp and changing it marks earlier-derived rows stale | restart |
| `SensitiveContent:MaximumAnalyzedCharacters` | int | `200000` | 1 – 10000000; text beyond it is dropped from the result rather than handed on unscanned. On the derived path that is what is *stored*, so lowering it truncates every message indexed afterwards and the value is part of the derivation stamp | restart |
| `SensitiveContent:ScanTimeout` | TimeSpan | `00:00:15` | One second to two minutes, per call to one scanner — which for the personal-data scanner covers every configured language together rather than each. A scan that misses it is refused rather than served unscanned, and on the derivation path that refusal ends the synchronization run carrying it, so a budget below what the analyzer spends on a large body leaves a folder repeating the same batch. It also bounds one personal-data readiness scrape whole, so naming more languages costs more analyzer requests and never a longer scrape | restart |
| `SensitiveContent:MaximumConcurrentScans` | int | `4` | 1 – 256, across the process | restart |
| `SensitiveContent:RebuildStaleDerivedData` | bool | `false` | Read only while a scanner is on; re-derives every message whose derived text predates the current configuration | restart |

**The rebuild switch spends a whole mailbox.** Switching a scanner on, or widening what it looks for, protects what is
derived from that moment onward and reaches nothing already extracted, chunked, or embedded — the host reports how many
messages that leaves behind every time it starts. This key is what re-derives them, and it costs one full re-indexing of
the affected messages: each is read, extracted, scanned, re-chunked, and re-embedded, so a deployment with a hosted
embedding endpoint pays that provider again for every one. It rides the extraction backfill rather than a worker of its
own, so `MailExtractionBackfill:Enabled` has to be on for it to perform anything, and
[`MailExtractionBackfill`](#mailextractionbackfill)'s interval and batch size are what pace the spend. Switch it back off
once the count reaches zero. [Derived data](../features/sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped)
records what is stamped, what makes a row stale, and why nothing rewrites stored text in place.

A category name is matched against what the scanner declares, ignoring capitalization, and the declared spelling is what
survives the match — so a placeholder in redacted text does not depend on how the name was written here. A name that
matches nothing **fails startup and quotes both the value and the categories the scanner does detect**, rather than
being dropped by the binder and leaving the section reading as protection that is on. So does a suppression naming a
rule that does not exist. A suppression inside a category this deployment does not look for is accepted and inert.

The `Secrets` scanner declares these seven categories. Six are on when `Categories` names none; the seventh is not, and
listing categories **replaces** the default set, so switching the entropy heuristic on means naming every category
wanted alongside it.

| Category | What it finds | On by default |
| --- | --- | --- |
| `ProviderToken` | An API token, key, or session credential a named service issued and prefixes as its own | yes |
| `CloudAccessKey` | An access key or client secret for a cloud platform's own control plane | yes |
| `PrivateKey` | A private key or certificate bundle, armoured as PEM or encoded whole | yes |
| `JsonWebToken` | A JSON Web Token, in its ordinary form or encoded a second time | yes |
| `ConnectionString` | A connection string carrying the credential it connects with | yes |
| `CredentialUrl` | A URL carrying a credential in its user information, its path, or its query | yes |
| `HighEntropyString` | A string dense enough to be a credential, recognised by its randomness rather than its shape | **no** |

A rule name inside them is the corpus entry's own name — `github-pat` and `aws-access-token` from the gitleaks rule
data, `AzureCosmosDBIdentifiableKey` and `UrlCredentials` from the detection engine's own corpus, and
`database-connection-uri-credential` from MailFathom's. [Sensitive-content
scanning](../features/sensitive-content-scanning.md#the-secret-scanner) records where each corpus comes from and what
the entropy heuristic costs.

The `Pii` scanner declares these eleven categories. The first five are on when `Categories` names none, and listing
categories **replaces** that set, so adding a personal name means naming the five wanted alongside it.

| Category | What it finds | On by default |
| --- | --- | --- |
| `PaymentCard` | A payment card number | yes |
| `BankAccount` | An IBAN or another bank account number | yes |
| `NationalIdentifier` | A national identification, social-security, or tax number | yes |
| `IdentityDocument` | A passport, identity-card, or driving-licence number | yes |
| `HealthIdentifier` | A number that names a person inside a health system | yes |
| `PersonName` | A personal name | **no** |
| `EmailAddress` | An email address | **no** |
| `PostalAddress` | A postal address, or a place named precisely enough to be one | **no** |
| `PhoneNumber` | A telephone number | **no** |
| `Date` | A date or a time, absolute or relative | **no** |
| `NetworkAddress` | An address that identifies a machine, whether the network assigned it or the hardware carries it | **no** |

A rule name inside them is the **analyzer's** own entity name, spelled as the analyzer spells it — `CREDIT_CARD`,
`IBAN_CODE`, `US_SSN`, `UK_NHS` — which is what lets a suppression silence one recognizer inside a category that stays
on. An operator never names one in `Categories`: those are the units this product publishes, and the mapping between the
two is MailFathom's. [The personal-data
scanner](../features/sensitive-content-scanning.md#the-personal-data-scanner) records what each of the six optional
categories costs retrieval, why the endpoint belongs inside the deployment, and what the confidence floor is protecting
against.

The analyzer block is read only while `Pii` is on. An address left behind under a scanner nobody runs is accepted and
inert, for the reason a category list under one is: it describes no protection, so refusing to start over it would be
refusing over a comment. The reverse — the scanner on with no address, a relative or non-HTTP address, a language that is
not two lowercase letters, more than eight of them, or a floor outside 0 to 1 — fails startup naming the key.

**Every code in `Languages` is one the analyzer has to have been built for**, and setting the key is only the last of the
steps that make a language work: the model, the image, the analyzer's recognizer registry, its engine, and then this key.
A code the analyzer registers nothing for leaves the deployment unready **naming that code**, rather than falling back to
English or quietly contributing nothing. Which codes are named decides which of the eleven categories above can find
anything — under `pl` alone the shipped registry knows one national identifier and no identity document at all, which is
the reason to name the languages a mixed mailbox actually carries rather than choosing between them. [The analyzer's
languages](personal-data-analyzer-languages.md) records what each language reaches and what adding one takes.

**Each language is a request, and they share one budget.** A scan states one language per call, so a deployment naming
two asks the analyzer twice over the same text, one after the other, and merges what came back; `ScanTimeout` bounds that
whole scan rather than each call, and `MaximumConcurrentScans` still counts scans rather than requests. Two languages
reporting the same value over the same span are one finding carrying the stronger score, and overlapping regions merge
into one placeholder as they already did.

**The readiness probe judges a category across the set.** A category is reachable when at least one configured language
recognises an entity of it, so adding a language never turns a ready deployment unready — while a category no configured
language reaches still refuses the start, naming the category as it does today.

The analyzed ceiling defaults to the same number as `EmailContent:MaxCharactersPerRead`, so an ordinary content read is
analyzed whole and only something pathological reaches it.

## `SpamClassification`

Whether mail is classified as spam, and where. A root of its own for the same reason `SensitiveContent` is one: it is a
property of the deployment rather than of one account, and what it switches on reaches the mailbox reads as well as the
classification. [Spam classification](../features/spam-classification.md) records what a classification holds, which
facts the deterministic stage reads, and why a scanner never overturns a provider's own verdict.

Every switch is off by default, and an absent section is that default rather than a startup failure. The deterministic
stage works alone and is the whole of the feature without a sidecar; `UseScanner` adds the Apache SpamAssassin daemon
described below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `SpamClassification:Enabled` | bool | `false` | | reload |
| `SpamClassification:UseScanner` | bool | `false` | Asking for a scanner while `Enabled` is false fails startup, because a scanner is only consulted where classification runs | restart |
| `SpamClassification:ScannedFolders:<n>` | string | unset | A usable folder alias; an absent list is every account's inbox mapping, and an explicitly empty list is no folder at all | reload |
| `SpamClassification:ScannerThreshold` | double | unset | 0.1 – 1000; unset keeps the threshold the scanner itself answered with | reload |
| `SpamClassification:ClassificationWait` | TimeSpan | `00:15:00` | 1 s – 7 days; how long a stored message may wait for a verdict before it is derived from anyway | reload |
| `SpamClassification:RunBatchSize` | int | `50` | 1 – 10 000 | reload |
| `SpamClassification:MaxRunBatchesPerPass` | int | `4` | 1 – 1 000 | reload |
| `SpamClassification:Scanner:Host` | string | unset | Required once `UseScanner` is on, and a host name or IP address rather than a URL or an address with the port on it | restart |
| `SpamClassification:Scanner:Port` | int | `783` | 1 – 65535 | restart |
| `SpamClassification:Scanner:ScanTimeoutSeconds` | int | `30` | 1 – 120 | restart |
| `SpamClassification:Scanner:MaximumMessageBytes` | int | `512000` | 32 000 – 33 554 432 | restart |
| `SpamClassification:Scanner:MaximumConcurrentScans` | int | `5` | 1 – 64 | restart |
| `SpamClassification:Actions:FileInJunkFolder` | bool | `false` | Asking for it while `Enabled` is false fails startup, and so does an account that maps no destination to file into | reload |
| `SpamClassification:Actions:MarkAsRead` | bool | `false` | Asking for it while `Enabled` is false fails startup | reload |
| `SpamClassification:Actions:JunkFolder` | string | `role:Junk` | A folder alias, or a role written as `role:<name>`; every configured account has to map it once filing is on | reload |
| `SpamClassification:Actions:Threshold` | double | unset | 0.1 – 1000; unset acts on every spam verdict, and a value judges what a scanner scored | reload |

`UseScanner` and the `Scanner` block are read once, at startup: whether a scanner exists at all decides what is
constructed and whether the host refuses to start without a daemon, which a reload cannot revisit. Everything else in
this section is read per classification.

`ClassificationWait` bounds the ordering rather than a scan. Wherever classification is on, a message it covers is not
chunked, embedded, or offered to the rule set until a verdict exists — and this is how long that may hold before the
message is derived from regardless, which is what stops a classifier nobody noticed was wedged from silently stopping
the index. Lengthening it delays mail of a classified folder by that much longer in the worst case; shortening it
narrows the window in which a verdict can arrive first. Zero is refused, because a wait of none releases every message
before anything could have scored it.
[Junk is kept out of what a deployment derives from mail](../features/spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
records what each answer means and what is counted.

**A scanner switched on with no daemon answering fails startup**, with error code `81003` naming the key to repair
rather than the address it tried. That is deliberate asymmetry with what one message gets, where a failed scan leaves
the deterministic verdict standing: an instance whose sidecar never came up would classify everything from headers alone
and look healthy doing it. The bounds are validated whether or not the scanner is switched on, so a value written wrong
is reported before the run that first switches scanning on rather than during it.

The daemon receives whole messages, so it belongs inside the deployment's own trust boundary; the feature page states
what an address outside it gives up, and what the rule-update and DNS postures cost. The deployment assets carry the
sidecar itself — [Kubernetes](deployment-kubernetes.md#spam-scanning),
[Compose](deployment-compose.md#spam-scanning), and [Quadlet](deployment-quadlet.md#spam-scanning).

The default scope follows the folder **role** rather than the text `INBOX`: it is whichever alias each account maps to
`Inbox` in [`MailSynchronization`](#one-account--mailsynchronizationaccountsn), so a server presenting the inbox under
another name is classified without the scope being restated here. The two shapes of an unset list are deliberately
distinguishable — writing no key asks for that default, and writing an empty list asks for no folder, which switches the
work off without switching the section off.

A folder alias that this system could never have issued **fails startup and names itself**, rather than being dropped by
the binder and leaving the section reading as a scope that is covered. So does a threshold outside the range above: one
at or below zero files every message whatever a scanner answered, and one beyond the ceiling can never be reached, so
both are a typed digit rather than an intent.

The section is read per classification rather than captured, so a reload takes effect on the next one. What a reload
never does is revisit a message already classified: replacing a verdict is an explicit operation.

Which folder is left out of `list_emails` and `search_emails` is not configured here. It is the folder mapped to the
`Junk` special use in [`MailSynchronization`](#one-account--mailsynchronizationaccountsn), and it is withheld whether or
not this section switches anything on.

The `Actions` block is the only part of this section that writes to a mailbox, and both of its switches are off. Each
works alone: filing moves the message on the server, marking read sets its `\Seen` flag, and turning both on sets the
flag first, because a relocation can renumber the message. Nothing else is ever done — no delete, no other flag, no
folder created, nothing sent.

`JunkFolder` **does not have to be a folder MailFathom mirrors, and for most deployments it should not be**: mapping it
with `synchronize: false` files spam out of the instance entirely, under the account's own
`AuthoredDeleteEmailDisposition`. What it does have to be is a folder every configured account maps, because
classification asks for none to be created — an account that maps no destination **fails startup naming that account**,
rather than leaving its spam unfiled with nothing said about why. A folder the account only maps is resolved against the
server the first time a filing needs it, exactly as a rule's destination is.

`RunBatchSize` and `MaxRunBatchesPerPass` bound one pass of the [classification
run](../features/spam-classification.md#classifying-the-mail-you-already-have) an operator asks for, and neither is a
schedule: a pass is a step of the account's synchronization run, so how often one happens is that run's interval. What
these decide is how much of a mailbox one pass takes in hand — raising them walks a mailbox nobody has scored in fewer
account runs, and lowering them shortens the stretch an interrupted pass has to cover again and leaves more of each run
for the folders it exists to fetch. Both defaults are smaller than the rule pass's, because a classification reads the
stored message and, with a scanner configured, sends the whole of it across a socket and waits for a score.

`Threshold` judges what a scanner scored, in the scanner's own scale, so an operator can label at `ScannerThreshold` and
move mail only from a higher score. It reaches no other stage, exactly as `ScannerThreshold` does not: a verdict resting
on a provider's header or on where the receiving server filed the message carries no score in this scale, and is acted
on. Raising it is deliberately not the same edit as switching classification off — the verdicts go on being recorded.

## `MailboxSearch`

The deployment-wide privacy bound on what a search result may quote, whether the result was ranked lexically or
hybridly. [Email search](../features/email-search.md) records how snippets are cut.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailboxSearch:SnippetsPerEmail` | int | `3` | 1 – 10 | restart |
| `MailboxSearch:WordsPerSnippet` | int | `24` | 4 – 100 | restart |

## `EmailContent`

What one `get_email_content` call may hand back, and where the files it describes are fetched from. Only text is
bounded here: no response carries an attachment's bytes, so a file costs a response the length of a URL whatever it
weighs. [Email content](../features/email-content.md) records how each bound is applied and reported.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `EmailContent:MaxBodyCharacters` | int | `100000` | 1000 – 1000000; each body representation is truncated to it, explicitly | restart |
| `EmailContent:MaxCharactersPerRead` | int | `200000` | 2000 – 2000000, and at least twice `MaxBodyCharacters`; the body characters one call returns across every email it names | restart |
| `EmailContent:AttachmentDownloads:LinkLifetime` | duration | `00:10:00` | 1 to 30 minutes; how long a minted link stays redeemable, refused outside that range rather than clamped | restart |

Two things decide whether any link is issued at all, and neither is here. [`Deployment`](#deployment) has to declare the
address a link is composed from, and [`DataEncryption`](#dataencryption) has to configure a ring, because the signing
key is derived from it rather than provisioned separately. A deployment missing either serves every other part of a
read and answers each attachment with `downloadState: unavailable`.

## `Embeddings`

What this deployment intends to embed with. Writing nothing is a supported deployment: no vectors are produced,
semantic search is unavailable, and lexical search serves exactly as before. Declaring a chain does not start
spending — an activation does. [Embedding generation](../features/embedding-generation.md) records what a declaration
means and what it costs.

Nothing here is a switch for semantic search, and none of these keys turns it on. What a search reports as its semantic
capability follows from three facts this section does not hold: whether a profile has been activated, whether the
declaration below still names that profile's identity, and whether the last call to the endpoint chain was answered.
A search never fails because one of them is not true — it answers lexically and says which of the three states it is in.
[Email search](../features/email-search.md#what-the-three-capability-states-mean) states what each means for a caller
and what an operator does about it. Editing a key in this section and restarting therefore changes what is embedded
next, never what a search is currently able to do; only an activation does that.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Embeddings:AllowTrimVectors` | bool | `false` | with it off, a declared dimension above 2000 is refused at startup; with it on, a wider answer is cut to the declared width and renormalized | restart |
| `Embeddings:MaxPassagesPerRequest` | int | `64` | 1 – 2048; the batch bound, applied before the provider sees a request | restart |
| `Embeddings:RequestTimeout` | TimeSpan | `00:01:00` | positive; one request to one endpoint | restart |
| `Embeddings:MaxQueuedEmails` | int | `1024` | 1 – 1000000; newly synchronized messages that may wait to be embedded at once, beyond which synchronization stops offering and the backfill reaches the rest | restart |

### What an instance is willing to spend

The four keys below bound cost rather than correctness, and they are validated whether or not a chain is declared:
passages are cut for every synchronized message on an instance that has chosen no provider, so a ceiling left
unvalidated would be one already applying. None of them is part of an embedding profile — they decide how many vectors
exist and never what one means, so moving any of them leaves every stored vector as comparable as it was. [Embedding
generation](../features/embedding-generation.md#what-an-instance-is-willing-to-spend) records what each bounds and why.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Embeddings:MaxCharactersPerEmail` | int | `200000` | 1000 – 10000000; how much of one message's extracted text is cut into passages. A message beyond it is bounded rather than refused — its opening is embedded and the length its text had is recorded on the message | restart |
| `Embeddings:MaxRequestsPerMinute` | int | `0` | 0 – 100000; `0` paces nothing, which is the default. For a provider whose quota is stated per minute; a caller takes the next free slot and waits for it | restart |
| `Embeddings:MaxInputCharactersPerPeriod` | long | `50000000` | zero or positive; the characters one period may send a provider, counted as sent rather than as stored. `0` declares no ceiling at all, which is supported and means an enabled feature can produce a bill nobody agreed to | restart |
| `Embeddings:SpendPeriod` | TimeSpan | `1.00:00:00` | 1 min – 31 days; the fixed window the ceiling is counted over, anchored at the Unix epoch so every restart places it identically | restart |

Reaching `MaxInputCharactersPerPeriod` pauses embedding until the period rolls over, and resumes without anybody
acting; nothing is dropped, because a passage with no vector is what the backfill selects on. The ceiling binds to
within one batch: a batch is admitted whenever anything at all is left and is then paid for whole, because weighing it
against what remains would stall a deployment whose ceiling is smaller than one batch for ever.

The default is chosen to bind. Fifty million characters a day is roughly twelve million tokens and embeds something
like sixteen thousand ordinary messages, so an instance keeping up with arriving mail never meets it and one working
through a decade of archive is paced rather than surprised — raise it deliberately for an initial backfill, having seen
the number.

**Concurrency is not here.** How many provider calls may be in flight at once is
`Resilience:AiProviderInvocation:ConcurrencyLimit`, which is the one setting that owns that question; [outbound
resilience](../architecture/outbound-resilience.md) holds it, and a second limiter beside it would make two keys answer
for one behaviour.

### One endpoint — `Embeddings:Endpoints:<n>`

An ordered chain. Every entry declares the same geometry and reaches the same vector space, so a failing endpoint
falls through to the next without changing what any stored vector means; startup refuses a chain whose entries
disagree, naming both aliases and the property.

An entry declares any service reachable over the OpenAI wire protocol, not one of a fixed set: `Provider`, `Model`, and
`ModelVersion` are what the profile records, while `RoutedModelName` — or `Model` where that is empty — is the string a
request is routed on. Two rules bind the address and the credential, and one implementation applies them to this section
and to `Chat` alike: an address is absolute HTTP or HTTPS, with a plain `http` one refused wherever the endpoint holds a
credential, because the request would publish it to everything on the path; and exactly one of `ApiKey`,
`EntraCredential`, and `Unauthenticated` is declared, because none of them is what a forgotten reference looks like and
two leaves unsaid which one a request presents. [Embedding
generation § An endpoint is any
service that speaks the OpenAI wire
protocol](../features/embedding-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol) holds
both rules with their reasons, what each setting decides, and a worked example of an endpoint that is neither OpenAI
nor Azure. [Embedding generation § A model server you run
yourself](../features/embedding-generation.md#a-model-server-you-run-yourself) covers the plain-address case, what it
gains, what it gives up, and the startup warning an instance holding such an endpoint writes. [Provider
endpoints](provider-endpoints.md) is the register of services somebody checked — what each one's `Address` and
credential are, whether it serves an embeddings route at all, and whether `SupportsRequestedDimension` may stay at its
default.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Alias` | string | — | required, unique within the chain; what a log line, a metric tag, a resilience circuit, and a failure message call this endpoint | restart |
| `…:Provider` | string | — | required, at most 64 characters; the vendor whose model defines the space, not the endpoint it is reached at | restart |
| `…:Model` | string | — | required, at most 128 characters; the vendor's published model identifier | restart |
| `…:ModelVersion` | string | *(empty)* | at most 64 characters; empty is a vendor that versions nothing, which is the ordinary case | restart |
| `…:RoutedModelName` | string | *(empty)* | at most 128 characters; what is sent as the model of a request where that differs — a cloud deployment's own name. Empty means it equals `Model` | restart |
| `…:Dimension` | int | — | 1 – 16000, and 1 – 2000 unless `AllowTrimVectors` is on; the width the stored vectors have and the profile records | restart |
| `…:DistanceMetric` | enum | `Cosine` | `Cosine`, `InnerProduct`, `EuclideanDistance` | restart |
| `…:InputCharacterLimit` | int | `8000` | positive; what a passage is cut to before it is sent, which is what the model saw and therefore part of what a vector means | restart |
| `…:PassageInstruction` | string | *(empty)* | at most 512 characters; empty for a model that requires none. Whitespace is refused, because it would register a second profile for a space identical to one already registered | restart |
| `…:NormalizeVectors` | bool | `true` | whether the space's vectors are of unit length | restart |
| `…:Address` | string | *(empty)* | absolute HTTP or HTTPS; a plain `http` one only for an endpoint declaring `Unauthenticated`. Empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | restart |
| `…:SupportsRequestedDimension` | bool | `true` | whether the endpoint honours a requested width, so the narrower space is asked for rather than cut out of a wider answer | restart |
| `…:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this, `EntraCredential`, and `Unauthenticated` is declared | restart, value read per request |
| `…:Unauthenticated` | bool | `false` | that this endpoint asks for no credential, so a request presents none — the shape of a model server you run yourself. Written rather than inferred from the other two being absent, because that is what a forgotten key reference looks like | restart |

### Microsoft Entra credential — `Embeddings:Endpoints:<n>:EntraCredential`

For an endpoint where no key exists to provision. All four shapes are non-interactive by construction: MailFathom is a
background service with nobody at a keyboard, and `DefaultAzureCredential` is deliberately not used because its chain
reaches an interactive browser credential and the developer-tool credentials of whoever is signed in on the host.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Kind` | enum | `ManagedIdentity` | `ManagedIdentity`, `WorkloadIdentity`, `ClientSecret`, `ClientCertificate`. `ApiKey` and `Unauthenticated` are refused here; a key is declared as one, and an endpoint needing no credential says so on the endpoint | restart |
| `…:TokenScope` | string | `https://ai.azure.com/.default` | required; the audience an access token is minted for. Declared rather than derived from the address, so a renamed service does not silently mint tokens for the wrong audience | restart |
| `…:TenantId` | string | *(empty)* | required for `ClientSecret` and `ClientCertificate` | restart |
| `…:ClientId` | string | *(empty)* | required for `ClientSecret` and `ClientCertificate`; optional for `ManagedIdentity`, where it selects a user-assigned identity | restart |
| `…:ClientSecret` | secret block | *(absent)* | required for `ClientSecret` | restart |
| `…:CertificatePath` | string | *(empty)* | required for `ClientCertificate`; a PKCS#12 file the process account can read | restart |
| `…:CertificatePassword` | secret block | *(absent)* | where the certificate file has one | restart |

## `Chat`

What this deployment generates text with. A root of its own beside `Embeddings` rather than a block inside it, because
the two are separate choices with separate consequences: without an embedding provider semantic search is off and
lexical search continues, while without a chat provider search is unaffected and only the answering capability stops
being offered. Writing nothing is a supported deployment, exactly as writing no `Embeddings` section is. [Chat
generation](../features/chat-generation.md) records what a declaration means and what one call may spend.

One endpoint rather than an ordered chain. A fallback embedding endpoint is another route to one vector space and
startup proves it; nothing proves that of two chat models, so falling through would answer a person in a different
model's voice with nothing above able to tell. An operator who wants failover puts a gateway in front of the declared
endpoint.

The endpoint is any service reachable over the OpenAI wire protocol, under the same two rules the embedding chain
follows and through the same implementation of them: an absolute HTTP or HTTPS address with a plain `http` one refused
wherever a credential is held, and exactly one of `ApiKey`, `EntraCredential`, and `Unauthenticated`. [Chat generation §
An endpoint is any service that speaks the OpenAI wire
protocol](../features/chat-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol) carries a
worked example of one that is neither OpenAI nor Azure, and `Chat:Api` is the key most often decided by which of the
two paths such a service serves. [Provider endpoints](provider-endpoints.md) records which paths each checked service
was found to serve.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Chat:Alias` | string | *(empty)* | writing one is what configures a chat provider at all; unique across every AI endpoint the deployment declares, embedding endpoints included. A section carrying other settings without it is refused rather than ignored | reload to rename, restart to declare or remove |
| `Chat:Model` | string | — | required once an alias is written; what a request is routed to, which for a cloud deployment is the deployment's own name rather than the vendor's model identifier | reload |
| `Chat:Address` | string | *(empty)* | absolute HTTP or HTTPS; a plain `http` one only for an endpoint declaring `Chat:Unauthenticated`. Empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | reload |
| `Chat:Api` | enum | `ChatCompletions` | `ChatCompletions` or `Responses`; which of the provider's two request APIs a call goes to under the declared address. Declared rather than derived, because the routed model name is the operator's own deployment name and nothing about it says which paths the server serves. State `Responses` for a reasoning model that refuses function tools beside a stated effort; a server that does not serve that path answers *request refused* | reload |
| `Chat:MaxOutputTokens` | int | `1024` | 1 – 200000; what one answer may occupy. Reaching it is not a failure — the answer arrives marked as cut short | reload |
| `Chat:Temperature` | float | *(unset)* | 0 – 2; left unset sends nothing, which is required by the models that reject the parameter outright | reload |
| `Chat:TopP` | float | *(unset)* | 0 – 1; unset the same way, and for the same reason | reload |
| `Chat:ReasoningEffort` | string | *(unset)* | the level the model documents, written as the provider spells it — `none`, `minimal`, `low`, `medium`, `high`, `xhigh`, or whatever a later model adds. Unset sends no reasoning parameter at all, which a model that does not reason requires. `none` is not the same as unset — it states an effort of none and sends it, which is what a provider refusing tools beside an unstated effort asks for. Startup checks the shape and not the vocabulary, because which levels exist belongs to the model; a level this deployment's model does not accept refuses the request rather than falling back | reload |
| `Chat:MaxMessagesPerRequest` | int | `64` | 1 – 512; the turns one request carries, refused rather than truncated | reload |
| `Chat:MaxRequestCharacters` | int | `120000` | 1 – 4000000; what those turns may add up to. Stated in characters rather than tokens because counting tokens would mean carrying the model's own tokenizer; set it below what the model's context window allows | reload |
| `Chat:RequestTimeout` | TimeSpan | `00:02:00` | positive; one request. Longer than an embedding request's by default, because generating an answer takes as long as the answer is | reload |
| `Chat:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this, `EntraCredential`, and `Unauthenticated` is declared | reload, value read per request |
| `Chat:Unauthenticated` | bool | `false` | that this endpoint asks for no credential, so a request presents none — the shape of a model server you run yourself. Written rather than inferred from the other two being absent, because that is what a forgotten key reference looks like | reload |

**What a reload changes here, and what it does not.** Everything the declared endpoint says is read again per
question, so correcting a model the provider refused — the ordinary case, because a wrong model is only discovered from
a refusal — costs an edit rather than a restart of a process that is synchronizing mailboxes and holding an IMAP IDLE
connection. A run already in flight keeps the declaration it began with, so a reload landing mid-question changes the
next question and not that one. A candidate that breaks any rule in the table is refused whole, logged with the key to
fix, and leaves the previous declaration answering; the process stays up either way. What stays a restart is the pair
that decides which services this deployment registered at all: whether `Chat:Alias` names an endpoint, and whether
`Chat:RelevanceFilter:Enabled` turns the second pass on. Renaming a declared alias reloads, because the credential and
the resilience circuit are both looked up by whatever the declaration in force calls it; going from no chat section to
one, or the reverse, does not, and is refused with that message rather than silently ignored.

**What the declared model has to be able to do.** `ask_mail` answers by offering the model a retrieval tool and reading mail when the model calls it, so a model that cannot be given function tools cannot answer a question here whatever else is written above. That is what the two settings in the middle of the table exist for: a current reasoning model refuses function tools beside an *unstated* reasoning effort and names the responses API as the way to have both, so such a model needs `Chat:Api` set to `Responses` and `Chat:ReasoningEffort` written — including written as `none`, which states an effort rather than omitting the parameter. A model this deployment cannot use is not detected at startup, because nothing here can ask a provider what a routed name supports without paying for a call; it surfaces as *request refused* on the first question. [Chat generation](../features/chat-generation.md#two-apis-and-the-deployment-says-which) holds the whole reasoning, and [Mail answering](../features/mail-answering.md) describes the run that imposes the requirement.

### Microsoft Entra credential — `Chat:EntraCredential`

The same block, with the same keys, defaults, and rules as
[`Embeddings:Endpoints:<n>:EntraCredential`](#microsoft-entra-credential--embeddingsendpointsnentracredential) above.
One credential source resolves both sections, which is why the alias uniqueness rule spans them. Its keys reload here
and take a restart there, for the reason the table above gives: this section is read again per question and the
embedding chain is read once while the host composes itself.

### Relevance filter — `Chat:RelevanceFilter`

The optional second pass over a retrieval: each candidate the fused ranking produced is put to the declared chat
endpoint on its own, and the ones the model scores below the threshold are dropped before an answer is written. A block
inside `Chat` rather than a root of its own, because the pass judges with that endpoint and has nowhere to send a
question without one — removing the chat section removes this with it.

Off by default, and off is a supported deployment: retrieval then hands over the fused ranking exactly as hybrid search
produced it. Turning it on is a spend decision as much as a quality one — it costs one provider call per candidate on
every lookup a question makes. [Mail answering § An optional second pass](../features/mail-answering.md#an-optional-second-pass-the-model-decides-what-answers)
describes what it drops, what it keeps, and what it does when the provider cannot tell it.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Chat:RelevanceFilter:Enabled` | bool | `false` | turning it on requires a declared `Chat:Alias`, and a `Chat:MaxMessagesPerRequest` of at least 2, because a judgement is an instruction and a candidate | restart |
| `Chat:RelevanceFilter:MaxCandidates` | int | *(unset)* | 1 – [`MailAnswering:MaxPassagesPerRetrieval`](#mailanswering), which is everything one retrieval hands over; a higher value would name candidates that never exist and is refused at startup rather than accepted and never met. Unset judges every passage the retrieval hands over, which is why there is no literal default here: one would go on saying a number of its own after the retrieval it follows was narrowed or widened. The ceiling on what one lookup spends and how long it takes; set below what retrieval returns it buys a weaker filter rather than a shorter result, because a passage nobody judged keeps its place | reload |
| `Chat:RelevanceFilter:MinimumRelevance` | int | `50` | 1 – 100, on the scale the model answers a judgement on. A threshold of 0 is refused: it would pay for a judgement that can drop nothing | reload |

## `MailAnswering`

What answering one question is allowed to cost, and how much of a mailbox may leave the process to do it. A root of its
own beside `Chat` rather than a block inside it, because the two answer different questions: `Chat` says which endpoint
generates text and what one *call* to it may carry, while this bounds a *run* — the conversation in which a model looks
mail up, reads it, and writes an answer — and the aggregate over every run of a period.

Unlike the provider sections, an absent section is not an absent capability. Every deployment has these ceilings and
writing nothing takes the conservative defaults below, because an absent provider is a capability nobody asked for while
an absent ceiling would be a bill nobody asked for. [Mail answering § What one question may
spend](../features/mail-answering.md#what-one-question-may-spend) describes what each one does when it is reached, and
what a caller is told.

The period is a **fixed window anchored at the Unix epoch**, placed exactly as
[`Embeddings:SpendPeriod`](#embeddings) places the other spend ceiling: an `AggregatePeriod` of one hour begins on the
hour, so a refused caller has a roll-over instant to come back at. A client that spends the whole allowance at the end
of one window and again at the start of the next has therefore spent twice the ceiling across an interval of the same
length.

Unlike the embedding ceiling, this ledger is **process-local and not durable**: a restart begins the current window with
nothing spent. The difference is deliberate and is stated rather than implied — an embedding sweep charges inside a
transaction that was committing vectors anyway, while answering opens no write of its own, so a durable count here would
put a database write on the path of every provider call in every run.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailAnswering:MaxPassagesPerRetrieval` | int | `20` | 1 – 50; how many messages one lookup may draw on. Capped by what one search can rank, because a retrieval is answered from a search window, and defaulted to the window `search_emails` itself returns so that asking a question reaches as many messages per lookup as searching for the same thing would | restart |
| `MailAnswering:MaxCharactersPerPassage` | int | `1200` | 1 – 100000; how much of any single message a lookup may draw out. Separate from the count above, because one enormous extract and a spread across several messages say different things about a mailbox | restart |
| `MailAnswering:MaxRetrievedCharactersPerRun` | int | `20000` | 1 – 10000000, and at least `MaxCharactersPerPassage` or no lookup could hand over even one passage. The ceiling on how much retrieved mail leaves the process to answer one question, whatever the model asks for, and the one that decides how many lookups a run fits: a lookup whose every passage reached the per-passage ceiling would exhaust a run on its own. Reaching it cuts rather than refuses: the run answers from what it has and the response says the mailbox was not read in full | restart |
| `MailAnswering:MaxProviderCallsPerRun` | int | `8` | 1 – 1000; the ceiling that holds whatever the provider reports, because a run is a tool loop whose length is the model's decision. Reaching it stops the run with `57001`. The run is also bounded by wall clock: raising this means raising [`McpEndpoint:RequestTimeout:Duration`](#request-timeout--mcpendpointrequesttimeout-and-adminendpointrequesttimeout) with it, or the extra calls are bought and then abandoned with a `504` | restart |
| `MailAnswering:MaxTokensPerRun` | long | `80000` | 1 – 100000000; the cost ceiling, stated in the unit a provider bills by. Checked before each call against what the calls before it reported, so the call that crosses it is paid for — what a call will cost is not knowable until it is answered. Reaching it stops the run with `57001` | restart |
| `MailAnswering:MaxAnswerCharacters` | int | `20000` | 1 – 1000000; how much of the model's answer one response carries. Cut rather than refused, and the response says it was cut | restart |
| `MailAnswering:MaxCitations` | int | `20` | 1 – 1000; how many messages one response names. Cut the same way and reported the same way | restart |
| `MailAnswering:AggregatePeriod` | TimeSpan | `01:00:00` | positive; how long one period lasts before what was spent in it is forgotten. An hour rather than a day, because a ceiling an operator only meets once a day is one they meet after the spend has happened | restart |
| `MailAnswering:MaxRunsPerPeriod` | int | `30` | 1 – 1000000; the ceiling on how enthusiastic a client may be. Nothing about the MCP surface stops one from asking a hundred questions in a minute, and without this a per-run ceiling bounds each of those hundred and none of the total. A question over it is refused with `57001` | restart |
| `MailAnswering:MaxTokensPerPeriod` | long | `300000` | 1 – 10000000000; the same ceiling in what a provider bills. Checked before a run begins, against what the runs of this period have consumed so far | restart |

## `EmbeddingBackfill`

The sweep that gives mail stored before the active profile its passages and its vectors. A root of its own rather than
a block inside `Embeddings`, because what an instance embeds with is a commitment and how fast it works through the
mail it already had is a rate an operator changes while watching a bill. Every key here is a pacing control:
`BatchSize` × `MaxBatchesPerRun` is the most one run may spend, and `Interval` is how often that is paid.
[Embedding backfill](../features/embedding-backfill.md) describes what it reaches and why it repeats.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `EmbeddingBackfill:Enabled` | bool | `true` | turning it off stops the spending within one interval and loses nothing already embedded | restart |
| `EmbeddingBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 24 h; the pause between runs while messages still await embedding | restart |
| `EmbeddingBackfill:IdleSweepInterval` | TimeSpan | `00:15:00` | 1 s – 24 h; the pause before a sweep starts again after one reached the end | restart |
| `EmbeddingBackfill:BatchSize` | int | `20` | 1 – 500 | restart |
| `EmbeddingBackfill:MaxBatchesPerRun` | int | `5` | 1 – 1000 | restart |

## `MailExtractionBackfill`

The worker that extracts text for messages stored before extraction existed or before a limit was raised.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailExtractionBackfill:Enabled` | bool | `true` | — | restart |
| `MailExtractionBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 1 h | restart |
| `MailExtractionBackfill:BatchSize` | int | `50` | 1 – 500 | restart |
| `MailExtractionBackfill:MaxBatchesPerRun` | int | `10` | 1 – 1000 | restart |

## `Jobs`

The queue of durable background work, and the worker that runs it. A root of its own rather than a block inside any
feature, because the queue is a mechanism every consumer shares: what a job does belongs to the feature that enqueues
it, and how much of the instance the queue may take belongs here. Nothing here names a job type, and an instance whose
build registers no handler runs no pass at all — the worker says so once at startup and stops, which is what leaves work
an older replica cannot run for a newer one.

`MaxConcurrentJobs` decides how much of the instance background work may take, and it is stated here rather than left to
emerge from the database connection pool. A limit nobody wrote down moves whenever anything else in the process opens a
connection, and it arrives as a query waiting on a pool rather than as a job waiting for its turn. `BatchSize` is a
different number — what one claim takes — so a claimed job waits for a slot like any other, and raising the batch buys
fewer round trips rather than more work in flight.

`MaxConcurrentJobsPerType` bounds one kind of work on its own, and startup refuses a value above `MaxConcurrentJobs`,
which already caps it. A job waiting on the per-type ceiling holds none of the instance-wide one, so a bulk
re-evaluation of one kind of work is never the reason another kind never runs.

`MaxQueueDepthPerType` bounds what may be waiting rather than what is running. An enqueue against a queue already
holding that many jobs of a type is refused and says so, and the caller slows down, asks again later, or stops
producing — the work is neither queued nor lost, and a request whose work is already queued is answered with that job
rather than turned away. It is the one setting here that still applies with `Enabled` switched off, because it bounds
enqueuing rather than running. Two callers meeting the bound together can both pass it, so a queue may overshoot by as
many enqueuers as raced; this is backpressure rather than an invariant, and what it exists to stop is a backlog growing
without limit.

`ExecutionTimeout` must be shorter than `LeaseDuration`, and startup refuses a pair that inverts them. That ordering is
what keeps two workers off one job: an attempt is cancelled before its lease can expire underneath it. The lease is
renewed at half its duration while a handler works, so a job that legitimately takes longer than one lease is not
reclaimed while it runs.

A failed attempt is classified before the attempt budget is consulted, and only a failure that could clear on its own is
attempted again. A permanent one — a credential the dependency refused, a request it rejected, anything whose meaning is
unknown — ends the job on its first attempt rather than spending `MaxAttempts` to reach an answer it already had. What
runs out of attempts and what could never succeed both become dead letters: terminal rows nothing claims again, which
hold up no other job and keep the classification and the reason they ended on. A shutdown is neither, and spends no
attempt: the job goes straight back to the queue with the attempt it was claimed for given back.

No key decides what becomes of a dead letter, because an operator does. [`mfctl
jobs`](../users/administering.md#background-work-that-stopped) reads what has stopped and either returns one to the
queue or writes it off, and [durable background work](telemetry.md#durable-background-work) is what says one is there.

`RetryMaxDelay` must be at least `RetryBaseDelay`, and startup refuses a pair that inverts them. A retry delay doubles
per attempt from `RetryBaseDelay`, is capped at `RetryMaxDelay`, and is drawn from a range rather than computed exactly
— jobs that failed together failed on the same dependency, and an exact delay would return all of them to it in the same
instant.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Jobs:Enabled` | bool | `true` | turning it off leaves enqueued work where it is, for a replica that runs it | restart |
| `Jobs:BatchSize` | int | `5` | 1 – 100; how many jobs one pass claims. Each of them waits for a concurrency slot, so this bounds what one claim takes rather than what runs at once | restart |
| `Jobs:MaxConcurrentJobs` | int | `4` | 1 – 32; how many jobs this instance runs at once, across every type together. Kept well below the connection pool a stock connection string provides, so the pool is never what expresses the limit | restart |
| `Jobs:MaxConcurrentJobsPerType` | int | `2` | 1 – 32, and at most `Jobs:MaxConcurrentJobs`; how many jobs of one type run at once. A job waiting on this holds none of the instance-wide ceiling | restart |
| `Jobs:MaxQueueDepthPerType` | int | `10000` | 1 – 1000000; how many jobs of one type may be waiting before enqueuing is refused as backpressure. Applies whether or not `Jobs:Enabled` is on | restart |
| `Jobs:LeaseDuration` | TimeSpan | `00:05:00` | 2 s – 1 h; how long work stays held after the process running it stops existing, which is the delay before a crash is recovered from | restart |
| `Jobs:ExecutionTimeout` | TimeSpan | `00:02:00` | 1 s – 1 h, and strictly shorter than `Jobs:LeaseDuration`; exceeding it cancels the job, which counts as a transient failure and is attempted again. Raise it where this kind of work legitimately takes longer | restart |
| `Jobs:MaxAttempts` | int | `5` | 1 – 20; how many attempts one job may be handed out for before a transient failure dead-letters it. `1` leaves no retry at all. A permanent failure ends the job whatever this says | restart |
| `Jobs:RetryBaseDelay` | TimeSpan | `00:00:30` | 1 s – 1 h; the delay the first retry is drawn around, doubling per attempt | restart |
| `Jobs:RetryMaxDelay` | TimeSpan | `00:30:00` | 1 s – 24 h, and at least `Jobs:RetryBaseDelay`; the ceiling a grown retry delay never exceeds | restart |
| `Jobs:PollInterval` | TimeSpan | `00:00:10` | 1 s – 10 min; how long an idle worker waits before looking again, and how often at most it measures the queue depth it publishes and asks whether a rule's schedule has come due. A schedule is therefore noticed within one interval of its occasion rather than at it. A pass that filled its batch looks again at once | restart |

## `MailRules`

The rules that select mail, written here rather than held in a table, because a rule is a statement about how an
instance is configured. [Mail rules](../features/mail-rules.md) documents the whole authoring surface a condition may
use — every fact, every function, every operator — and this section documents the shape the rules are declared in.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailRules:MaxConditionLength` | int | `1000` | 1 – 10000 characters; a condition over it is refused naming the rule | reload |
| `MailRules:MaxConditionNestingDepth` | int | `16` | 1 – 64 levels of the parsed condition. A length limit alone would admit a short expression nested past reading | reload |
| `MailRules:ConditionEvaluationTimeout` | TimeSpan | `00:00:01` | Greater than zero and at most `00:00:30`; bounds one condition against one email, including resolving the facts it names | reload |
| `MailRules:EvaluationBatchSize` | int | `200` | 1 – 10000 messages read, evaluated, and committed together; the unit of progress an interrupted pass gives back | reload |
| `MailRules:MaxEvaluationBatchesPerPass` | int | `5` | 1 – 1000 batches per walk per account run; what a pass leaves behind is the next run's, so a long queue drains over several runs instead of holding one up | reload |
| `MailRules:HistoryRetention` | TimeSpan | `30.00:00:00` | At most 3650 days; how long a recorded rule execution is kept. Zero or less keeps one for exactly as long as the message it names. A pass records one execution per rule it reached per message, so this is the bound on a record that would otherwise grow with the mailbox | reload; the next account run erases against the new window |
| `MailRules:Rules` | list | empty | At most 200 rules, evaluated in the order they are written | reload |
| `MailRules:Rules:0:Name` | string | required | 1 – 64 characters of letters, digits, spaces, and `.`, `_`, `-`; unique across the section, ignoring case | reload |
| `MailRules:Rules:0:Accounts` | list | empty | The accounts the rule applies to, each naming a declared `MailSynchronization:Accounts:<n>:AccountId` exactly; empty applies the rule to every account | reload |
| `MailRules:Rules:0:Condition` | string | required | One expression producing a boolean, within the two limits above | reload |
| `MailRules:Rules:0:StopWhenMatched` | bool | `false` | A match ends the pass and the rules below it are not reached | reload |
| `MailRules:Rules:0:Enabled` | bool | `true` | A rule switched off is left out of the set entirely | reload |
| `MailRules:Rules:0:Triggers` | list | `[]` | The automatic occasions that run the rule; `Arrival` and `Schedule` are the declared names, an unknown or repeated name is refused, and naming none is a rule nothing fires by itself that a whole-mailbox run applies | reload |
| `MailRules:Rules:0:Schedule` | string | unset | When a rule declaring the `Schedule` trigger runs: `Every <hh:mm:ss>` or `Every <d.hh:mm:ss>`, from one minute to 365 days, or `Daily at <HH:mm>` optionally followed by a time-zone identifier and read in UTC without one. Required by that trigger and refused without it | reload |
| `MailRules:Rules:0:Actions:MoveTo` | string | unset | The alias of the folder a match is filed into; the account must mirror it | reload |
| `MailRules:Rules:0:Actions:CopyTo` | string | unset | The alias of the folder a copy of a match is placed in; the account must mirror it | reload |
| `MailRules:Rules:0:Actions:Delete` | bool | unset | `true` removes a match from the folder it matched in; the account must permit deletion | reload |
| `MailRules:Rules:0:Actions:MarkAsRead` | bool | unset | `true` sets the remote `\Seen` flag and `false` clears it; leaving the key out leaves the flag alone | reload |
| `MailRules:Rules:0:Actions:MarkAsFlagged` | bool | unset | `true` sets the remote `\Flagged` flag and `false` clears it; leaving the key out leaves the flag alone | reload |
| `MailRules:Rules:0:Actions:AddKeywords` | list | unset | The keywords put on a match beside the ones it carries; each an IMAP atom of at most 64 characters, at most 64 of them, and an empty list is refused | reload |
| `MailRules:Rules:0:Actions:RemoveKeywords` | list | unset | The keywords taken off a match, under the same limits; an empty list is refused | reload |
| `MailRules:Rules:0:Actions:SetKeywords` | list | unset | The keywords a match ends up carrying in place of whatever it carried; `[]` is what clears them all, and is the one empty keyword list that is accepted | reload |

An absent action key is a change the rule does not ask for, which is why `MarkAsRead` carries a value rather than being
a switch. At most one of `MoveTo`, `CopyTo`, and `Delete` may be declared by one rule, `Delete` admits nothing beside
it, `SetKeywords` admits neither `AddKeywords` nor `RemoveKeywords` beside it, and every permitted combination is
applied in MailFathom's own order — the flags and keywords first and the relocation or the deletion last. A keyword
carrying a space, a control character, or one of `( ) { % * " \ ]` cannot be sent as an IMAP atom and is refused at
startup naming the rule and the key. A rule declaring nothing here selects mail and changes nothing.
[What a matching rule does](../features/mail-rules.md#what-a-matching-rule-does) states the whole table, the order, and
how a change reaches the mail server.

Every condition is read while the host composes itself, and a defect in one — an unparseable expression, a name that is
not a fact, a call that is not an available function, a comparison between shapes that could never match, a result that
is not a boolean — fails startup naming the rule and what was wrong. So does a scope naming an account the deployment
does not declare, which would otherwise leave the rule reaching no mail in silence. Every defect in every rule is
reported together.

An edit that does not validate is **refused and logged, and the previously valid rule set stays in effect**. That is
deliberately stronger than the framework's own reload behaviour, which would drop the candidate without saying so.

## Where each surface is served

Every socket this process opens is named by the section of the surface that owns it, and by nothing else. `McpEndpoint`
and `AdminEndpoint` each state a `BindAddress`, a `Port`, and a `Transport`; `HealthEndpoints` states the same three
under a smaller set of TLS settings. Read those three sections and you have read every listener the process binds.

**The host's own ways of naming a listener are refused at startup.** `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`,
`ASPNETCORE_HTTPS_PORTS`, `--urls`, and any endpoint under `Kestrel:Endpoints` each fail the process with a message
naming the setting that replaces them. They are refused rather than ignored because ignoring them is silent: Kestrel
drops the URL-shaped addresses as soon as a listener is bound in code — which every surface here does — and binds a
configured endpoint beside them on a socket no section describes, no credential guards, and no isolation middleware was
composed for. An operator who states a port deserves to be told it moved, not to find the surface answering somewhere
else.

A deployment that enables no surface at all is refused for the same reason. Kestrel answers zero listeners by binding
its own default address, and a development certificate on the machine would add a TLS one beside it, so the process
would hold a socket nothing configured.

### `Transport`

`McpEndpoint:Transport` and `AdminEndpoint:Transport` decide what the surface's clear-text socket does. The HTTPS half
is the profiles under `Https:Endpoints`, each with its own domain, certificate, TLS floor, and HTTP versions.

| Value | The socket at `BindAddress`:`Port` | `Https:Endpoints` |
| --- | --- | --- |
| `Http` | Serves the routes | Must be empty |
| `HttpAndHttps` | Redirects to the profiles, or serves the routes when `Https:Redirect:Enabled` is `false` | Bind |
| `HttpsOnly` | Not opened at all | Bind |

`Http` is the default, so adopting a release costs no certificate work, and it is the right posture behind a
TLS-terminating reverse proxy and wrong anywhere else — startup warns about it either way, because only an operator
knows which they have.

`HttpsOnly` is the posture that leaves nothing reachable in clear text. `HttpAndHttps` keeps the clear-text socket, and
the redirect is on unless a deployment turns it off, so enabling TLS does not read as an outage to a client nobody has
repointed yet; turning the redirect off is what makes that socket serve the routes, which is the deliberate
both-schemes posture rather than the migration one. `HealthEndpoints:Transport` takes the same three values, and
because the probes carry one certificate rather than profiles, its `HttpAndHttps` needs a second port of its own in
`HealthEndpoints:HttpsPort`.

### Sharing a socket

Two surfaces, or all three, may name one port. That is the posture a single-node deployment behind one ingress wants —
one socket to publish and one backend to route — and it is why both request-serving surfaces default to `8080` for clear
text and `8443` for a profile. The port is bound once and serves each surface's own paths; which paths a request may ask
for is decided from the port it arrived on, so a surface that is not on that port is still refused there with a `404`.

**What sharing costs is exposure.** The probes answer without a credential and the administrative surface is a different
authority from the mailbox, so putting either on the endpoint's port publishes it wherever that port is published. Keep
them apart when that matters; the ports exist so the decision is yours.

#### Which settings a shared socket couples

Sharing is per socket, not per surface. Each surface declares its clear-text socket (`BindAddress` + `Port`) and one
socket per HTTPS profile (`Https:Endpoints:<n>:BindAddress` + `:Port`) separately, so two surfaces may share the
clear-text one and keep TLS sockets of their own. Every rule below applies to one socket at a time, and each failure
names both sections.

| Setting | Coupled on | Why it cannot differ |
| --- | --- | --- |
| `Transport` — whether *this* socket carries TLS | Every shared socket | One socket serves one scheme |
| `Https:Redirect:Enabled` | A shared clear-text socket | The socket either redirects or serves the routes; it cannot do both |
| The domain a redirect resolves | A shared **redirecting** socket | The client sent one host name, so two answers to it would be settled by composition order |
| Profiles by server name vs one certificate | A shared TLS socket | A socket answers a handshake one way; the probes present one certificate and the endpoints select by name |
| `ClientCertificateProfiles` — configured or not | A shared TLS socket | Whether a certificate is asked for is settled while the connection is established |
| `Https:Endpoints:<n>:Domain` — uniqueness | A shared TLS socket | One name served by two surfaces would leave composition order deciding which the client reached |
| `Https:Endpoints:<n>:HttpProtocols` | A shared TLS socket | ALPN offers what the listener was bound with, which is before any server name has been read |
| `BindAddress` — a wildcard beside a specific address | The same port | The operating system grants only one of those two sockets |

**What stays each surface's own, on a shared socket as much as on a separate one:**

- The **HTTPS ports.** Two surfaces sharing a clear-text socket may redirect to profiles on ports of their own, because a
  redirect resolves the name the client asked for: `mail.example.test` goes to the MCP endpoint's port and
  `admin.example.test` to the administrative one, from the same `8080`. Only publishing *one* name at two ports is
  refused.
- `MinimumTlsVersion`, per profile. The TLS floor is settled per connection, after the server name is known, so profiles
  on one socket may each keep their own.
- Different **domains** on one TLS socket, which is what sharing one is for.
- Everything decided per request rather than per socket: `Authentication` and every method it carries, `Cors`,
  `RateLimiting`, origin validation, and the route prefix. An API key provisioned for an agent still authenticates
  nothing under `/api/admin`, whichever port both are reached on.

Two specific addresses on one port are two sockets and are accepted; none of the rules above applies between them.

## `ReverseProxy`

Which peers this process accepts a public scheme and host from, when something in front of it terminates TLS. One
section for the whole process rather than one per surface: it runs at the front of the one request pipeline every
listener shares, so a proxy named here is trusted on each of them.
[Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) is the page.

`X-Forwarded-Proto` and `X-Forwarded-Host` are always read; there is no key that switches that off. What the section
carries is who they are believed from, and **an unconfigured section believes every peer.**

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ReverseProxy:TrustedProxies` | string list | empty, which trusts `0.0.0.0/0` and `::/0` | Each entry an IP address or a CIDR network whose host bits are clear — not a DNS name. What is named replaces the default rather than adding to it, and the framework's loopback default is cleared rather than inherited. Left empty, or written as `0.0.0.0/0` and `::/0`, it trusts every peer and so disables the refusal of an OAuth token that arrived without TLS — see [what the default costs](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) | restart |
| `ReverseProxy:MaximumForwardedHops` | int | `1` | At least 1; how far right-to-left through each header a value is believed | restart |

`X-Forwarded-For` is never read, so the peer MailFathom observes stays the one that opened the connection, and the
configured OAuth `Resource` stays a value you wrote rather than anything derived from a header.

## `ConnectionLimits`

How many connections this process accepts at once, across every listener it opens. The other section that belongs to
the whole process rather than to a surface, and for a stronger reason than `ReverseProxy`: a connection is accepted
before any routing has decided which endpoint it was for, so there is no per-surface form of this question to ask.

**Read it as the process's ceiling, never as the sum of what the endpoints permit.** `McpEndpoint:RateLimiting:MaxConcurrentRequests`
bounds what one surface serves at once; this bounds what the machine accepts at all, the probe listener included. The
two numbers are deliberately far apart, because a connection is not a request — a client holds one open across several,
and an idle one survives until the keep-alive timeout — so a ceiling near the request limit would refuse ordinary
clients long before it refused a flood.

It exists because every other limit is reached too late to see this. The rate limiter partitions a request that already
has an `HttpContext`, and what a connection flood spends before that point — the accept, the TLS handshake, and on the
MCP surface the client certificate's chain building — is the most expensive per-connection work the process does.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `ConnectionLimits:Enabled` | bool | `true` | Turning it off restores the framework's own default, which accepts connections until the operating system stops supplying them; it costs a startup warning | restart |
| `ConnectionLimits:MaxConcurrentConnections` | int | `1000` | 1 – 100000; process-wide, across every listener | restart |

Like every limit here it is counted in this process alone, so a deployment running several instances enforces it once
per instance rather than once in total, and none of it is protection against a distributed flood.

## `McpEndpoint`

Whether the protocol surface is served and what a client must present. The whole section is **restart** — it decides
routing and listeners — while key and certificate material is read per request or per handshake. Where it is served is
[its own `BindAddress`, `Port`, and `Transport`](#where-each-surface-is-served).
[The MCP endpoint](mcp-endpoint.md) is the page, section by section.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `McpEndpoint:Enabled` | bool | `false` | — | restart |
| `McpEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; binds the clear-text socket, which `HttpsOnly` does not open | restart |
| `McpEndpoint:Port` | int | `8080` | 1–65535. The administrative endpoint's default as well — see [sharing a socket](#sharing-a-socket) | restart |
| `McpEndpoint:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` — see [`Transport`](#transport) | restart |
| `McpEndpoint:Authentication` | list of credentials | empty | One entry per accepted credential; empty warns at startup. A value written here rather than a list fails startup | restart |

### The accepted credentials — `McpEndpoint:Authentication:<n>`

Each entry carries the block of whichever method judges it, and the block's presence is what selects that method — there
is no separate setting naming it. As many entries may state any method as a deployment needs, and one entry may carry
several blocks; an entry carrying none fails startup, named by its position. A grant written on an entry adds five more
refusals and makes one combination of blocks impossible —
[what a credential may do](#what-a-credential-may-do--permissions) is where they are stated. Both endpoints take the
same entries; the administrative one adds a single rule, stated with it below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:<n>:ApiKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`; a second key is a second entry | restart; material per request |
| `…:<n>:PublicKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`, resolving to one client's PEM public key. Startup refuses material that is not one, an RSA key below 2048 bits, and — explicitly — material carrying a private key | restart; material per request |
| `…:<n>:Permissions` | string list | absent = everything this surface publishes | The [permissions](#what-a-credential-may-do--permissions) every credential this entry admits may hold; an empty list grants nothing. A name nothing publishes, a name belonging to the other surface, and a repeated name each fail startup naming the entry's index | restart |
| `…:<n>:PermissionsFromTokenScopes` | bool | `false` | Narrows the list above by each token's own scopes instead of granting all of it. Refused on an entry that also carries `ApiKey` or `PublicKey`, neither of which can carry a scope | restart |
| `…:<n>:OAuth:Resource` | string | — | Required; the canonical `https` URL clients reach this endpoint at — behind a proxy, the proxy's public URL. Every OAuth entry names the same one, because the endpoint publishes one metadata document at an address derived from it | restart |
| `…:<n>:OAuth:RequiredScopes` | string list | empty | Scopes a token from *this entry's* servers must carry; empty accepts any token they issued for this resource. A permission name is refused here, because requiring one would close the door on a caller the deployment meant to serve less | restart |
| `…:<n>:OAuth:AdvertisedScopes` | string list | empty | Scopes published in `scopes_supported` for a client to ask for and checked on no token — `offline_access` is what a client needs to be issued a refresh token. Every required scope is published regardless, so a value repeating one is refused, as is one that is not a scope token, as is a permission name — the grant that reads one advertises it already | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Name` | string | — | Required; the identity diagnostics use, and unique across every entry because it composes the scheme its validator registers under | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Issuer` | string | — | Required; a well-formed `https` issuer, compared against `iss` exactly, and unique across every entry | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:MetadataAddress` | string | unset | An absolute `https` URL on the issuer's own host; overrides issuer-derived discovery | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:AuthorizedSubjects` | string list | — | At least one; a token whose `sub` is not listed is refused, so every user the server can sign in does not automatically read this mailbox | restart |

MailFathom is a protected resource only; an external authorization server signs users in.
[`OAuth`](mcp-endpoint.md#oauth) records what a token must prove and
[scopes you advertise but do not require](mcp-endpoint.md#scopes-you-advertise-but-do-not-require) why the published list
is longer than the checked one,
[API keys](mcp-endpoint.md#api-keys) what a key is compared against, and
[Key pairs](mcp-endpoint.md#key-pairs) what a client signs and what the deployment verifies — including the audience,
expiry, and replay identifier an assertion carries, none of which is a setting.

### What a credential may do — `Permissions`

A permission is a named capability MailFathom publishes. The set is closed, so every name a grant can carry has a check
behind it and a misspelling fails startup instead of reading as a narrower grant than it is. The two endpoints draw from
disjoint halves, and the name says which half it belongs to.

| Permission | Surface | What it covers |
| --- | --- | --- |
| `mailfathom.mail.read` | MCP | The tools that read the local mailbox copy: `list_accounts`, `list_emails`, `get_email_content`, `search_emails`. Where semantic retrieval is configured, searching places the caller's own query text with the embedding provider, so this is not an egress-free grant |
| `mailfathom.mail.ask` | MCP | `ask_mail`, which answers from mail content by sending it to a model provider. It does not imply `mailfathom.mail.read`, and granting it is granting access to mail |
| `mailfathom.mail.contacts.read` | MCP | `list_contacts` and `get_contact`, which read the deployment's own contact book: names, addresses, and the notes an owner wrote about identified third parties |
| `mailfathom.mail.contacts.write` | MCP | `create_contact`, `update_contact`, `delete_contact`, and `promote_contact`, which record, amend, erase, and take on a person in that book. The erasure is here rather than apart, because a grant that cannot edit the book cannot be trusted to take somebody out of it |
| `mailfathom.admin.read` | administrative | The reads reporting the deployment's own state and no mail: what synchronization is doing, embedding status and the activation preview, the loaded rules, a run's progress, what a rewind would cost, the stopped-job list |
| `mailfathom.admin.audit.read` | administrative | Everything derived from somebody's mail: the mailbox-mutation audit, the answering audit, the rules history, the spam classifications, and reading the contact book |
| `mailfathom.admin.operate` | administrative | Asking the deployment to do work it can already do: running rules, classifying an account, retrying or dropping a stopped job, cancelling a reindex, rewinding synchronization, re-deriving stored mail, writing to the contact book |
| `mailfathom.admin.credentials.write` | administrative | Storing a mailbox refresh token |
| `mailfathom.admin.spend` | administrative | Activating the declared embedding model, which is the one operation that starts a provider bill |
| `mailfathom.admin.erase` | administrative | Disposing of what the deployment holds: the mail stored for a folder an account no longer mirrors, and one person and everything the contact book derived from them |

No permission implies another, so a credential that needs two is granted two.

**The grant belongs to the entry, not to the block.** An entry may carry an `ApiKey`, a `PublicKey`, and an `OAuth`
block at once, and `Permissions` on it applies to every credential it admits. Two credentials to be granted differently
are therefore two entries — which is what turns grouping, until now only a matter of tidiness, into a decision.

**Both surfaces enforce a grant, and they refuse differently.** On the MCP endpoint a caller is listed only the tools
its grant permits and a call naming any other is answered as a call naming a tool that does not exist, with nothing said
about the permission that was missing — [MCP tools](../features/mcp-tools.md#what-a-caller-is-offered) has the
tool-to-permission mapping. On the administrative endpoint a caller the grant does not admit is answered `403` naming
the one permission that would have sufficed and nothing else, because the caller there is an operator at their own
terminal — [what the endpoint serves](admin-endpoint.md#what-the-endpoint-serves) names the permission every route is
published under.

**An absent `Permissions` key and an empty list are opposites.** Writing no key at all leaves the entry holding
everything its surface publishes, which is what makes a first deployment work before it is governed. The key's absence
means *this surface* rather than the names published the day the file was written, so a permission added in a later
release reaches an unrestricted entry on its own — the contact tools are the worked example, since an entry that wrote
no key gains `mailfathom.mail.contacts.read` and `mailfathom.mail.contacts.write` on upgrade alone, and with the second
of those a credential that can record, amend, and irreversibly erase what this deployment holds about identified third
parties. Writing `Permissions: []` grants nothing, which is how a credential is retired without deleting its entry: it
still authenticates, and on the administrative surface it still reads `GET /api/admin/session`, which needs no
permission because it reports only what the caller already presented — and which is where an operator reads that the
credential now holds nothing. Everything else is refused: an emptied grant is served an empty tool list on the MCP
endpoint and reaches no administrative route at all.

**A surface with no `Authentication` entry at all grants that surface's whole half**, because there is no entry for a
grant to be written on. That is the unauthenticated posture the startup warning already reports.

**`PermissionsFromTokenScopes` makes the list a ceiling rather than a grant.** With it, a token holds the published
names its scopes carry *and* the entry lists, so the authorization server decides per subject within a bound the
deployment fixed; a scope naming anything else — `openid`, `offline_access`, another resource's scope — is ignored, and
a scope naming a permission the entry never listed grants nothing. It is available only where the entry's sole block is
`OAuth`: neither a key nor a public key can carry a scope, so startup refuses the combination rather than asking a
credential a question it cannot answer. Every such entry's ceiling is published in `scopes_supported`, which is what an
operator creates in their authorization server; an entry granting from configuration publishes none of its permissions,
because a client cannot ask for one.

Startup records what every entry resolved to, one line per entry, so the posture is read on the first run rather than
inferred later. [The MCP endpoint](mcp-endpoint.md#what-a-credential-may-do) and
[the administrative endpoint](admin-endpoint.md#what-a-credential-may-do) each carry the lines their surface produces.

### Browser origins — `McpEndpoint:Cors`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AllowedOrigins` | string list | absent = every origin | `*` for every origin, a list for exactly those, an empty list for none | restart |

The default is deliberately the permissive one — an `Origin` header only exists in browsers, and a native client is
unaffected — but a deployment reachable from a browser should narrow it.
[CORS and the `Origin` header](mcp-endpoint.md#cors-and-the-origin-header) explains what the check does and does not
protect.

### TLS termination — `McpEndpoint:Https:Endpoints:<n>`

Read under the two `Transport` modes that terminate TLS and refused under the one that does not. Configuring any
profile **takes over the host's listeners**: only the profiles' sockets are opened.
[HTTPS and your own domain](mcp-endpoint.md#https-and-your-own-domain) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Name` | string | — | Required; unique | restart |
| `…:Domain` | string | — | Required; the DNS name the certificate is proven to cover | restart |
| `…:BindAddress` | string | `0.0.0.0` | An IP address | restart |
| `…:Port` | int | `8443` | 1 – 65535 | restart |
| `…:MinimumTlsVersion` | enum | `Tls12` | `Tls12`, `Tls13` | restart |
| `…:HttpProtocols` | enum list | `Http1`, `Http2` | `Http1`, `Http2`, `Http3`; selecting `Http3` where the platform provides no QUIC fails startup rather than falling back | restart |
| `…:ServerCertificate` | certificate block | — | Required; see below | restart; renewal behind unchanged references — see [secret rotation](secret-rotation.md#renewing-an-mcp-server-certificate) |

A certificate block names either `Bundle` (one PKCS#12 secret block, optionally with a nested `Password`) or the pair
`CertificateChain` and `PrivateKey` (PEM, as two secret blocks). Startup proves the material loads, covers the stated
domain, and is not expired — before any listener opens.

### Clear-text redirect — `McpEndpoint:Https:Redirect`

What the surface's clear-text socket does while the profiles above are served. On, it answers every request with a `308`
to the address those profiles are at, so enabling TLS does not read as an outage to a client nobody repointed yet; it
then maps no route and runs no credential check. Off, the same socket serves the routes in clear text.

The socket is `McpEndpoint:BindAddress` and `McpEndpoint:Port` — there is no address here to state again. The section is
meaningful under `Transport: HttpAndHttps` alone, which is the one mode with both a clear-text socket and somewhere to
send what arrives on it; writing it under either other mode fails startup.
[Redirecting a client still pointed at `http://`](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http) records what
a redirect does and does not protect.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Refused unless `Transport` is `HttpAndHttps` | restart |

### Client certificates — `McpEndpoint:ClientCertificateProfiles:<n>`

Mutual TLS, judged per configured client application. A certificate exists only on a TLS connection this process
terminates — over the HTTPS profiles above, or over a listener the deployment configured with TLS otherwise — so a
plain-HTTP deployment presents none, which a `Required` profile refuses.
[Client certificates](mcp-endpoint.md#client-certificates) records how a presented certificate is judged.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Name` | string | — | Required; unique | restart |
| `…:Requirement` | enum | — | `Optional`, `Required`; required to be stated | restart |
| `…:TrustAnchors` | list of secret blocks | — | At least one; the authorities the client's chain must anchor in | restart; material per handshake |
| `…:SubjectAlternativeNames` | string list | — | At least one; a DNS name the certificate must carry | restart |

### Rate limiting — `McpEndpoint:RateLimiting` and `AdminEndpoint:RateLimiting`

One of the two endpoint subsections where every value has a product default — [request timeout](#request-timeout--mcpendpointrequesttimeout-and-adminendpointrequesttimeout)
is the other — so an enabled endpoint is bounded whether or not
anyone wrote a number. Both endpoints carry it, with the same keys, defaults, and validation, and configure it
independently: neither one's traffic reaches the other's limits. [Rate limiting](mcp-endpoint.md#rate-limiting) records
whose capacity a request spends, and [administering a deployment](admin-endpoint.md#rate-limiting) records the one
behavioural difference on the administrative endpoint — its burst is the endpoint's rather than one caller's, because
that surface judges a credential behind the limiter.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Turning it off costs a startup warning | restart |
| `…:MaxConcurrentRequests` | int | `20` | 1 – 1000; process-wide, per endpoint | restart |
| `…:ConcurrencyQueueLimit` | int | `0` | 0 – 1000; `0` refuses instead of queueing | restart |
| `…:TokenCapacity` | int | `60` | 1 – 1000000; the largest burst one caller may spend | restart |
| `…:TokensPerReplenishmentPeriod` | int | `60` | 1 – 1000000, and not above `TokenCapacity` | restart |
| `…:ReplenishmentPeriod` | TimeSpan | `00:01:00` | 1 s – 1 h | restart |
| `…:RequestQueueLimit` | int | `0` | 0 – 1000, and below `MaxConcurrentRequests` | restart |

### Request timeout — `McpEndpoint:RequestTimeout` and `AdminEndpoint:RequestTimeout`

How long one request may run before the endpoint abandons it, answering `504` and releasing the concurrency permit it
held. Defaulted throughout like the rate limits, carried by both endpoints with the same keys, and configured
independently of them — because how much traffic is admitted and how long an admitted request may hold what it was
admitted with are different questions, and a deployment may already have one answered in front of the process without
the other.

Without it, `MaxConcurrentRequests` bounds how many requests run at once and nothing bounds how long any of them lasts,
so twenty slow requests take a surface out of service without exceeding any rate.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Enabled` | bool | `true` | Turning it off costs a startup warning | restart |
| `…:Duration` | TimeSpan | `00:10:00` | 1 s – 1 h | restart |

**The default is a bound on a hang rather than a promise that no legitimate request is abandoned.** An `ask_mail` run is
a conversation whose length the model decides, bounded by `MailAnswering:MaxProviderCallsPerRun` at eight calls, each an
`AiProviderInvocation` whose own `TotalTimeout` defaults to five minutes — so a ceiling enclosing the maximum would sit
at forty minutes, which is not a request ceiling and would let one stalled run hold a concurrency permit
that long. Ten minutes clears an ordinary answering run by a wide margin and abandons one that walks its whole provider
budget, which is the trade taken. Raise it alongside `MailAnswering:MaxProviderCallsPerRun` if you raise that. A
deployment serving no AI-backed tool narrows it instead: every other MCP tool answers from the local mailbox copy with a
bounded query, so a minute is generous there. `AdminEndpoint` reaches no provider at all, which makes it the endpoint to
narrow without having to ask what a tool call needs.

The ceiling is applied ahead of the rate limiter, so time a request spends waiting for a limiter lease is inside it.
That wait is nothing under the default queue limits of `0`, and is the whole point of the ordering once a queue is
configured: a request queued for its caller's tokens already holds a concurrency permit.

## `AdminEndpoint`

Whether the administrative surface the `mfctl` command reaches is served, and what a client must present. Its own
listener, its own credentials, and its own authorization servers: a key configured under `McpEndpoint` authenticates
nothing here, and the reverse holds. The whole section is **restart**, while key and certificate material is read per
request or per handshake. [Administering a deployment](admin-endpoint.md) is the page.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `AdminEndpoint:Enabled` | bool | `false` | — | restart |
| `AdminEndpoint:BindAddress` | string | `0.0.0.0` | An IP address; binds the clear-text socket, which `HttpsOnly` does not open | restart |
| `AdminEndpoint:Port` | int | `8080` | 1–65535. The MCP endpoint's default as well, so enabling both without stating a port publishes one shared socket — see [sharing a socket](#sharing-a-socket) | restart |
| `AdminEndpoint:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` — the same setting the MCP endpoint carries, read the same way | restart |
| `AdminEndpoint:Authentication` | list of credentials | empty | Same shape and rules as [`McpEndpoint:Authentication:<n>`](#the-accepted-credentials--mcpendpointauthenticationn), with three additions: every `OAuth` block's `Resource` must end in `/api/admin`, because that is where these routes answer and what `mfctl` appends to find the metadata document; a client assertion presented here names the audience `urn:mailfathom:admin` rather than `urn:mailfathom:mcp`; and `Permissions` draws from the `mailfathom.admin.*` half of [the published set](#what-a-credential-may-do--permissions), so a `mailfathom.mail.*` name written here fails startup | restart; material per request |
| `AdminEndpoint:Https:Endpoints:<n>` | list of profiles | empty | Same shape and rules as `McpEndpoint:Https:Endpoints:<n>`, read under the two `Transport` modes that terminate TLS | restart; material per handshake |
| `AdminEndpoint:Https:Redirect` | block | on | Same shape and rules as `McpEndpoint:Https:Redirect`; its socket is this surface's own `BindAddress` and `Port`, so terminating TLS on both surfaces opens two clear-text ports that do not collide | restart |
| `AdminEndpoint:RateLimiting` | block | bounded | Same shape, defaults, and rules as `McpEndpoint:RateLimiting` above; applied whether or not it is written | restart |
| `AdminEndpoint:RequestTimeout` | block | bounded | Same shape, defaults, and rules as [`McpEndpoint:RequestTimeout`](#request-timeout--mcpendpointrequesttimeout-and-adminendpointrequesttimeout) above; applied whether or not it is written. This surface reaches no AI provider, so it is the one the default can be narrowed on freely | restart |

The routes are served beneath `/api/admin`, which is a constant rather than a setting: a client is configured with a
host and a port and appends the rest.

## `HealthEndpoints`

The startup, readiness, and liveness probes and the dedicated listener they answer on.
[Health endpoints](health-endpoints.md) records why the surface carries no credential and how each transport behaves.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `HealthEndpoints:Enabled` | bool | `true` | Off maps no probe route and opens no listener | restart |
| `HealthEndpoints:BindAddress` | string | `0.0.0.0` | An IP address; `127.0.0.1` restricts to the machine | restart |
| `HealthEndpoints:Port` | int | `8081` | 1 – 65535. A port another surface binds is permitted and shares that socket — see [sharing a socket](#sharing-a-socket) | restart |
| `HealthEndpoints:HttpsPort` | int | unset | Required by, and only valid with, `HttpAndHttps` | restart |
| `HealthEndpoints:Transport` | enum | `Http` | `Http`, `HttpAndHttps`, `HttpsOnly` | restart |
| `HealthEndpoints:Domain` | string | — | Required by the TLS transports; the name the certificate is proven against | restart |
| `HealthEndpoints:ServerCertificate` | certificate block | unset | Required by the TLS transports; refused otherwise | restart |

## `Resilience`

Retry, timeout, circuit-breaker, and concurrency budgets for the non-HTTP outbound dependencies, one subsection per
dependency class: `MailboxSessionEstablishment`, `MailboxDataRetrieval`, `MailAuthorizationServerInvocation`,
`EmailDelivery`, `DatabaseCommandExecution`, `AiProviderInvocation`. A subsection naming no class fails startup. Every setting is **restart** by construction, and
[outbound resilience](../architecture/outbound-resilience.md#configuration) explains each strategy and the
per-class reasoning.

Settings, per class:

| Key | Type | Constraint |
| --- | --- | --- |
| `Resilience:<Class>:MaxAttempts` | int | 1 – 10; counts the first call, so `1` disables retry |
| `Resilience:<Class>:BaseDelay` / `MaxDelay` | TimeSpan | Jittered exponential backoff between attempts |
| `Resilience:<Class>:AttemptTimeout` / `TotalTimeout` | TimeSpan | One attempt / the whole operation |
| `Resilience:<Class>:CircuitBreakerFailureRatio` | double | 0.01 – 1.0 |
| `Resilience:<Class>:CircuitBreakerMinimumThroughput` | int | 2 – 1000 |
| `Resilience:<Class>:CircuitBreakerSamplingDuration` / `CircuitBreakerBreakDuration` | TimeSpan | — |
| `Resilience:<Class>:ConcurrencyLimit` | int | 1 – 1000 |

Defaults, per class:

| Class | Attempts | Base/max delay | Attempt/total timeout | Breaker ratio · min · sampling · break | Concurrency |
| --- | --- | --- | --- | --- | --- |
| `MailboxSessionEstablishment` | 3 | 2 s / 30 s | 30 s / 2 min | 0.5 · 5 · 60 s · 30 s | 4 |
| `MailboxDataRetrieval` | 3 | 1 s / 15 s | 60 s / 3 min | 0.5 · 10 · 30 s · 15 s | 8 |
| `MailAuthorizationServerInvocation` | 3 | 500 ms / 5 s | 10 s / 30 s | 0.5 · 10 · 60 s · 30 s | 8 |
| `EmailDelivery` | 2 | 5 s / 60 s | 60 s / 3 min | 0.5 · 5 · 60 s · 60 s | 4 |
| `DatabaseCommandExecution` | 3 | 200 ms / 2 s | 15 s / 30 s | 0.5 · 20 · 30 s · 5 s | 32 |
| `AiProviderInvocation` | 3 | 2 s / 30 s | 120 s / 5 min | 0.5 · 5 · 60 s · 30 s | 4 |

## `Logging`

The standard .NET `Logging` section applies unchanged, and the host clears no provider: `Console`, `Debug`, and
`EventSource` stay attached beside the OpenTelemetry provider that the service defaults add. `Debug` writes only under
an attached debugger, and `EventSource` writes to the `Microsoft-Extensions-Logging` event source, which produces
nothing until something collects it — a `dotnet-trace` session, typically. So on a deployment the console is the
provider that produces output, and until `OTEL_EXPORTER_OTLP_ENDPOINT` names a collector it is where logs go at all,
which [telemetry](telemetry.md) records is the shipped default for both the Compose deployment and the chart. Log
lines are structured and never carry credentials, message content, or raw MIME, whatever the level or the format.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Logging:LogLevel:<category>` | enum | `Information`, and `Warning` for `Microsoft.AspNetCore` | A `LogLevel` name. `Default` is the catch-all; any other segment is a log-category prefix | reload |
| `Logging:Console:LogLevel:<category>` | enum | the `Logging:LogLevel` value | Filters the console alone, leaving what the OTLP exporter sends untouched | reload |
| `Logging:Console:FormatterName` | string | `simple` | `simple`, `systemd`, or `json` | reload |
| `Logging:Console:FormatterOptions:<name>` | mixed | — | `IncludeScopes`, `TimestampFormat`, and `UseUtcTimestamp` under any formatter; `SingleLine` and `ColorBehavior` under `simple` alone; `JsonWriterOptions` under `json` alone | reload |

An option the selected formatter does not define is accepted and does nothing — `SingleLine` under `json` is the one
worth naming, because it reads like it would fold a record onto one line and the JSON formatter already writes one
line per record without it.

`reload` here is the logging framework's own rather than a classification ADR 0002 made: a changed value is observed
by the next record written, without a restart and without reloading anything else. It is also why this section is
among the framework-shaped entries exempt from the strict binding above — a key this table does not name is the
framework's to accept or to ignore, so a misspelling here leaves a default in force instead of failing startup with
the path.

**Executed SQL is a `Debug` record.** EF Core reports every command it runs through
`Microsoft.EntityFrameworkCore.Database.Command`, at `Information` in the library's own configuration. MailFathom logs
that one event at `Debug` instead, because a synchronization run, a backfill sweep, and every MCP read reach the
database repeatedly, and one record per round trip would leave the stream mostly SQL. What is lowered is the level of
the event rather than a filter over the category, so the records come back by asking for them — set
`Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Debug` for the commands alone, or `Default`
where a whole run is being read. A command that *fails* is untouched and stays in the default stream: only the
executed-command event is lowered, and every other EF Core event keeps the level the library gives it.

Select `json` where something parses the stream rather than reads it, and `systemd` where `journalctl` should read
the level rather than print it as text. Both are worth setting deliberately: `simple` is the default because it is
what a person reading `docker compose logs` wants, and it is the wrong shape for everything downstream of that.

**The startup records ignore every key above.** The host writes those four through a pipeline composed before
configuration exists, which attaches a console of its own at a fixed `Information` level, so a deployment that selects
`json` gets a stream whose `MailFathom.Host.Startup` records are still `simple` text — the `Critical` one explaining a
failed start included. Give a log shipper a path for those lines rather than assuming the stream is uniform;
[host startup telemetry](host-startup-telemetry.md) records why that pipeline cannot read this section.

## Environment-only settings

A few settings are read from the environment alone, because they configure the process before configuration exists or
belong to the platform rather than to MailFathom:

| Variable | What it does |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Attaches the OTLP exporter for logs, metrics, and traces — startup records included. Unset exports nothing. [Telemetry](telemetry.md) is the page, including the sibling `OTEL_*` variables the exporter reads itself |
| `OTEL_SERVICE_NAME` | The service identity the startup records and every exported record carry. Unset reports the host assembly's own name |
| `OTEL_TRACES_SAMPLER` / `OTEL_TRACES_SAMPLER_ARG` | How much of a trace is recorded. Unset records every trace this process starts and honors the decision on one it did not; [telemetry](telemetry.md#how-much-of-a-trace-is-recorded) holds the values and why that is the default |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_HTTPS_PORTS` | Nothing — [each surface states where it is served](#where-each-surface-is-served), and setting one of these fails startup with a message naming the key that replaces it |
| `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` | The environment name; `Development` is what admits user secrets and `appsettings.Development.json` |
| `DOTNET_USE_POLLING_FILE_WATCHER` | Set to `1` where reload must observe a mounted volume's atomic update — Kubernetes ConfigMaps in particular |
| `OPENSSL_CONF` | The OpenSSL configuration file every TLS connection in the process is handshaked under. Unset is the platform's own policy; setting it is how a mail server the platform refuses is reached at all, and the host warns at startup that it is in force. [The platform TLS policy](platform-tls-policy.md) is the page |

Each of these has a reader that runs before MailFathom's configuration exists, or that never consults it: the bootstrap
logging pipeline is composed before the configuration providers are, because a malformed `appsettings.json` is one of
the failures it exists to report; the OpenTelemetry exporter reads its own `OTEL_*` variables directly; the .NET host
settles the environment name before the application's configuration is composed; and OpenSSL reads `OPENSSL_CONF` while
it initializes, which is the one entry here that could not be a MailFathom setting even in principle.

### Writing one anywhere else fails startup

A value for any of them that did not come from the process environment is refused, naming every such variable at once:

```
Settings only the process environment can deliver carry a value that did not come from it: OPENSSL_CONF,
OTEL_SERVICE_NAME. Each is read before MailFathom's configuration exists, or by a library that never consults it, so a
value written into an appsettings file, a provisioned configuration file, or a command-line argument reaches nobody. Set
each as an environment variable on the host process, or remove it.
```

That failure carries error code `12002` and ends the process through the same bootstrap pipeline every other startup
failure does. It exists because the mistake is otherwise invisible: the configuration pipeline accepts
`"OTEL_SERVICE_NAME"` in a mounted ConfigMap and reads it back happily, while the exporter — which took its value from
the environment long before that file was layered in — keeps reporting under the assembly name. Nothing in the file, in
the logs, or in the process would say which of the two an operator was looking at.

The check compares against the environment rather than merely looking for the name, because the environment provider
puts these names into configuration too, and a value that arrived that way is exactly what a correct deployment looks
like. What it catches beyond an absent variable is an override: a command-line argument outranks the environment
provider, so `--OTEL_SERVICE_NAME=…` leaves configuration reporting one identity while the exporter keeps using another.

Whole families are covered rather than the names in the table alone. Every `OTEL_*`, `ASPNETCORE_*`, and `DOTNET_*`
variable belongs to a reader that takes it from the environment, so naming only the handful MailFathom itself reads
would leave the rest — `OTEL_EXPORTER_OTLP_HEADERS` above all, which carries a collector's credential — silently
ignorable. A blank value counts as unset on both sides, because templating a manifest routinely emits an empty string
for a setting nobody chose.

The three URL-shaped listener addresses are the exception, and they are stricter rather than looser: they are refused
from *every* source, the environment included, because no MailFathom surface is served from one at all.
