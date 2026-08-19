# Mail configuration

<!-- describes: src/Host/Configuration/Mail/**, src/Host/Configuration/Rules/** -->

Every key deciding which mail is fetched and how, what is sent, what a deployment records about either, what a mailbox
query and a stored message may cost, and which rules run over what arrives. The tables read as
[the configuration reference](configuration-reference.md#how-to-read-the-tables) says they do, and that page is the map
to the rest of the sections.
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
| `MailSynchronization:VerifyDkimLocally` | bool | `true` | Whether extraction verifies a message's own DKIM signatures where no trusted `Authentication-Results` header was found. A fallback and never a supplement: an account whose server writes the header verifies nothing locally. It is the only path that makes an outbound DNS query | reload; the next extraction verifies against it |
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
| `…:Folders` | list | inbox by role | Aliases unique; each entry below | reload |

`TrustedAuthenticationServiceIdentifier` names the one server whose `Authentication-Results` headers this account
believes, which is what stops the check from being defeated by a header an attacker wrote upstream. There is nothing to
default it to, because the right value is a property of who receives this account's mail; an account that omits it
believes no header and every message it holds records that nothing was established.
[Sender authentication](../features/sender-authentication.md) states how the header is chosen and what the verdict
holds.

`TrustedSenders` and `TrustOwnAccountDomains` are the second half of that: they decide whether a message's author is
somebody this deployment recognizes, which is a separate question from what was established about them. Both lists are
held against an **authenticated author** — the domain a trusted DMARC result or a matching DKIM or SPF identity
established for the `From` header, or, where no server wrote a verdict, a locally verified DKIM signature naming that
same domain — and never against the raw header, so naming a correspondent here cannot be exploited by writing their
address into a message. Most legitimate mail stays unknown and that is the
intended outcome: the claim is that this deployment does not know the author, not that the message is suspicious.
Turning `TrustOwnAccountDomains` off is the right move for a deployment whose accounts sit on a large shared provider,
since every user of that provider writes from the same domain; the same page states what an address entry rests on and
what it deliberately does not establish. Both are unreachable where no trusted header is read at all — an account
that names no authority, and equally one whose receiving server records no results — because a message with no
established author reaches unknown without either being consulted. [Whether your server says who sent a
message](../users/mailbox-providers.md#whether-your-server-says-who-sent-a-message) is how that is checked against a
deployment's own delivered mail before entries are written here.

`VerifyDkimLocally` is what a deployment whose receiving server writes no `Authentication-Results` header at all
depends on. Without it every message there records that nothing was established, no author ever authenticates, and
neither list above is ever consulted; with it, a message's own DKIM signatures are verified against the keys their
domains publish and the whole chain has an identity to stand on. It defaults to **on** for that reason — shipping it
off would switch it off for exactly the mailboxes it exists for, while leaving them looking correctly configured — and
it changes nothing for an account whose server does write the header, which goes on believing that server.

**It is the one path in MailFathom that makes an outbound DNS query, and what it sends is
`<selector>._domainkey.<signing-domain>`**: a low-cardinality name the signing domain published in order to be asked
for, shared by every message that domain signs, carrying nothing about the message, the mailbox, or the recipient, and
resolved when a message is stored rather than when one is read. That is a different transaction from the spam scanner's
DNS checks, which every deployment asset here leaves off: those would send the sending address and the URI hosts taken
out of the body. The lookups are bounded by an explicit deadline, after which the verdict is simply not established,
and cached per selector and signing domain for as long as the record's own time-to-live allows. **An operator who wants
no egress from the extraction path at all sets this to `false`**, which gives exactly the behaviour of a deployment
that never had it. [Sender authentication](../features/sender-authentication.md#where-no-server-said-anything-the-signature-still-does)
states what such a verdict reaches, what it deliberately does not, and how a reader tells the two apart.

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
`Important`, `Outbox`). Configuring no folder synchronizes the inbox by role.

`Outbox` is the one role that cannot be written alone: no mail server advertises one, so it names a folder only beside
a `RemotePath` and startup refuses it without one, naming the alias. It is the folder a message waiting for an instant
still ahead is [mirrored into](../features/mail-delivery.md#the-copy-in-the-accounts-own-folders); mapping none is the
default and mirrors nothing.

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

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:ContactCollection:Enabled` | bool | `false` | Whether this account records the people it corresponds with as its mail is synchronized | reload; the next folder run collects under it |
| `…:ContactCollection:MinimumMessagesFromSender` | int | `2` | 1 – 100; how many messages an address must have written to this account before the person behind it is recorded. One records every admitted sender on first sight. It bounds only that direction — an address the owner wrote to is recorded at once | reload; the next folder run |
| `…:ContactCollection:MaxContactsPerRun` | int | `50` | 0 – 1000; how many contacts one folder run may record, bounded per folder exactly as `MaxContentBytesPerRun` is, so an account synchronizing several folders may reach it once for each. Zero records nobody while leaving collection on | reload; the run after the one in flight |
| `…:ContactCollection:Exclusions` | list | empty | The addresses and domains this account never records a contact from; each entry below | reload; the next folder run |
| `…:ContactCollection:Exclusions:<n>:Domain` | string | unset | A domain this account collects nobody at. Exactly one of `Domain` and `AddressPattern` is written, and an entry writing neither or both fails startup naming the account and the entry's position | reload |
| `…:ContactCollection:Exclusions:<n>:AddressPattern` | string | unset | A pattern over the whole address, where `*` stands for any run of characters including none and `?` for exactly one; everything else is the literal text of an address. At most 320 characters, and a pattern whose only characters are those two wildcards and the at-sign is refused, because `*@*` takes every address and `*@` takes none | reload |
| `…:ContactCollection:Exclusions:<n>:IncludeSubdomains` | bool | `false` | Whether a domain entry also reaches the names beneath that domain. Refused on a pattern entry, which writes its own | reload |

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

**Both ranges and every entry are judged whatever `Enabled` holds.** The block is always there — an account naming
none of it is bound to the defaults above — so a number outside its range, or an entry nobody could read, refuses
the start of an account this feature never touches. That is the deliberate half of it: a bound nothing reads today
is the bound switching collection on tomorrow adopts, and startup is the last moment anybody is looking at it.

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
| `…:Delivery:Enabled` | bool | `false` | Startup refuses `true` on an account that names no `Host` | reload |
| `…:Delivery:Host` | string | unset | Its presence is what configures the endpoint; every other key here has a default or an inherited value | reload |
| `…:Delivery:Port` | int | `465` | 1 – 65535; the default is the implicit-TLS submission port, agreeing with the default connection security | reload |
| `…:Delivery:ConnectionSecurity` | enum | `TlsOnConnect` | The same five modes judged by the same rules as the reading endpoint's, against the account's own opt-ins | reload |
| `…:Delivery:UserName` | string | unset (the account's) | An identifier, not a secret; for a relay that authenticates a different login than the mailbox does | reload |
| `…:Delivery:Secrets:Password` | secret block | unset (the account's) | A block naming no reference reads as absent and falls back to the account's credential | reload; material per connection |
| `…:Delivery:FromAddress` | string | unset (the account's `UserName` when it is a mailbox address) | A mailbox address; startup refuses an endpoint that resolves to none | reload |
| `…:Delivery:FromDisplayName` | string | unset (the address alone) | The name recipients see this mailbox sign itself with; deliberately not the account's `DisplayName` | reload |
| `…:Delivery:FileSentCopy` | bool | `true` | Whether a delivered message is appended to the folder this account maps to the `Sent` role | reload |

**`Enabled` is off on every account of every deployment, and turning it on is the act that makes sending possible.** An
installation upgrading into a release that can send therefore does not thereby become able to: the release meets a
configuration that never asked for the capability. It is per account rather than per deployment because an owner may
want one identity able to write and another purely archival, and it is separate from `Host` because the two are
different decisions — an endpoint provisioned before anybody decided to use it is an ordinary shape, while an account
permitted to send with nowhere to submit is a permission that could never be acted on, which startup refuses naming the
account. What an enabled account may then write, and how much of it, is
[`MailDelivery`](#maildelivery) below; whether this installation may send at all is
[`Deployment:ReadOnly`](configuration-runtime.md#deployment).

`FileSentCopy` is on because a submission server files nothing: without it the owner's own mail client shows a Sent
folder that is empty however much this account sends. Turn it off for a provider that files the copy itself, which is
the one case leaving it on produces two copies of every message.
[The copy in the account's own folders](../features/mail-delivery.md#the-copy-in-the-accounts-own-folders) states why
this is configured rather than detected, and what an account that maps no `Sent` folder does instead.

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
| `MailDelivery:AllowedSendLateness` | TimeSpan | `08:00:00` | 1 min – 30 d | restart |
| `MailDelivery:SignalQueueCapacity` | int | `64` | 1 – 1000 | restart |

**`AttemptTimeout` below `LeaseDuration` is one of the two orderings startup refuses rather than warns about**, the
other being `RetryMaxDelay` at or above `RetryBaseDelay`, which the table above states as a constraint of its own. The
first is the one worth the paragraph, because what it protects is a message rather than a schedule. The lease is
what lets a crashed process's send be attempted again without anything being told the process died, and an attempt
still transmitting when its own lease expires is a second attempt taking a message the first may already have sent. So
the attempt is cancelled first, by a margin the operator chooses, and a configuration stating otherwise fails startup
naming `AttemptTimeout`.

A send that spends `MaxAttempts` stops being attempted and stands in the outbox where an operator can see it, rather
than being retried forever; a permanent refusal is terminal at the first answer and never spends the remaining
attempts. Between attempts the delay doubles from `RetryBaseDelay`, drawn with jitter so a provider that refused every
account at once is not offered all of them back together, and is capped at `RetryMaxDelay`.

**`AllowedSendLateness` applies to a message written to leave at a named time and to nothing else.** A send that named
no time is never late, however long a retry or an unreachable provider has held it, so raising or lowering this key
changes nothing about ordinary correspondence. What it decides is the case where the moment came and went with nothing
running — an instance that was down, a queue that was full — where delivering and dropping are both wrong answers. Up to
this much lateness the message is delivered as written; past it the send is refused, stands in the outbox where an
operator sees it, and reports the outcome `missed-due-time` on
[the delivery counter](telemetry.md). Neither outcome is silent. The default is a working day, which is the span over
which a message written for nine in the morning still reads as the message its author meant; what to do about one later
than that is a person's decision rather than a bound's.

`SignalQueueCapacity` bounds only how promptly a send leaves, never whether it leaves. The queue holds accounts rather
than messages and an account already waiting is not queued twice, so it cannot grow past the number of configured
accounts however much is enqueued: raising it past that buys nothing, and a value below it means a signal is
occasionally refused and those sends wait for the account's own synchronization run instead.

The last two groups are not about one message but about what may leave this installation at all. Both are judged where
the outgoing record is written, which is the one place every author passes through, so a rule, a tool call, and a
command meet them identically and nothing is written down for a send either of them refuses.

### Who this deployment may write to — `MailDelivery:RecipientPolicy`

Four lists, all empty by default, which is the deployment that writes to anybody an enabled account is asked to write
to. Naming anybody at all narrows every account of the installation at once, because who an instance may correspond
with is a decision about the instance rather than about a mailbox.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailDelivery:RecipientPolicy:AllowedDomains` | string[] | empty | Each entry is a domain name; startup refuses one that is not | restart |
| `MailDelivery:RecipientPolicy:AllowedAddresses` | string[] | empty | Each entry is a mailbox address; startup refuses one that is not | restart |
| `MailDelivery:RecipientPolicy:DeniedDomains` | string[] | empty | Each entry is a domain name; startup refuses one that is not | restart |
| `MailDelivery:RecipientPolicy:DeniedAddresses` | string[] | empty | Each entry is a mailbox address; startup refuses one that is not | restart |

A domain entry names that domain and every name beneath it, on both sides: `example.test` covers
`anna@team.example.test` as well as `anna@example.test`. **The denied lists are read first and win outright**, so a
recipient an operator wrote on both is refused. An allowed list is a statement about everybody — write one and every
recipient it does not name is refused — while a denied list alone restricts only whom it names.

Every recipient of every message is judged, and a message naming one refused recipient is **refused whole** rather than
delivered to the rest. A message written to four people and sent to three is a message its author never wrote, and
nothing downstream could tell the two apart afterwards. What the caller is told names which half of the policy refused
and never the address, because a refusal reaches a log and a recipient is somebody else's personal data.

An entry that names no domain or mailbox fails startup naming its list and its position, without quoting the entry. A
policy is what stands between a fault above it and somebody's mailbox, so an entry that silently matched nothing would
be a restriction an operator believes they wrote and a permission this deployment actually holds.

### How much may leave in a period — `MailDelivery:SendCeilings`

The bound that turns a fault above it — a rule matching more mail than its author expected, a caller in a loop — into a
refusal rather than into a provider suspending the account. Every ceiling is zero by default, which is no ceiling at
all.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailDelivery:SendCeilings:Period` | TimeSpan | `1.00:00:00` | 1 min – 31 days | restart |
| `MailDelivery:SendCeilings:MaxMessagesPerAccount` | long | `0` | Not negative, and never above `MaxMessagesPerDeployment` where both are declared; `0` is no ceiling | restart |
| `MailDelivery:SendCeilings:MaxRecipientsPerAccount` | long | `0` | Not negative, and never above `MaxRecipientsPerDeployment` where both are declared; `0` is no ceiling | restart |
| `MailDelivery:SendCeilings:MaxMessagesPerDeployment` | long | `0` | Not negative; `0` is no ceiling | restart |
| `MailDelivery:SendCeilings:MaxRecipientsPerDeployment` | long | `0` | Not negative; `0` is no ceiling | restart |
| `MailDelivery:SendCeilings:MaxMessagesPerCaller` | long | `0` | Not negative, and never above `MaxMessagesPerDeployment` where both are declared; `0` is no ceiling | restart |
| `MailDelivery:SendCeilings:MaxRecipientsPerCaller` | long | `0` | Not negative, and never above `MaxRecipientsPerDeployment` where both are declared; `0` is no ceiling | restart |

The period is a **fixed window anchored at the Unix epoch**, the same shape the
[embedding spend ceiling](configuration-ai.md#embeddings) uses: every process of a deployment and every restart of one
agree on where a period begins with nothing stored to say so, and a refused send has a moment to come back after. A
rolling window would have to retain every send for the length of the window and would name no such moment.

What is counted is what was **written down** rather than what was delivered, since a fault above produces records
whether or not a submission server ever accepts them. The message being asked for is weighed by the people it names, so
one message can reach a recipient ceiling on its own, and the message that exactly fills a ceiling is admitted — a
ceiling states what a period may send. A refused send names which of the four ceilings it reached and never the number,
which is the operator's own configuration and nothing a caller could have influenced.

A per-account ceiling above the deployment's own fails startup, because it is a bound this installation could never
apply: the account would meet the deployment's ceiling first, under a refusal naming a number nobody configured for it.
The two per-caller ceilings are refused the same way and for the same reason. Declaring no ceiling at all is a supported
posture rather than an oversight — sending is already off until an account is turned on — and what a deployment with
sending on and no ceiling is exposed to is
[mail delivery](../features/mail-delivery.md#what-a-deployment-must-turn-on-before-it-can-send).

**The two per-caller ceilings count a client rather than a mailbox**, over the same window and against the same
counting rule. They are what a deployment answers an agent in a loop with: the account and deployment ceilings bound
the installation, so one client can spend the whole of them, and a client that keeps asking is then a refusal after a
handful of messages instead of after the provider notices. What a caller is counted as is the principal the credential
it authenticated with resolves to, and a caller reaching one of these is told which ceiling and that the period has to
roll over, exactly as one reaching the deployment's own is.

A send is weighed and counted in one operation, so a client dispatching several at once cannot have them all pass the
same remaining slot. It is counted under its own idempotency identity, which makes a retry one message. What follows
is that a send refused *below* this ceiling — by an account's own switch, by the deployment's ceilings, by the
read-only posture — has still spent the caller's allowance; a client asking repeatedly for a send this deployment
refuses is exactly the loop these two bound.

Two properties follow from the counting being per process and in memory. The count is **not durable**: a restart begins
the period's counting again, which the account and deployment ceilings — counted from the outgoing records themselves —
do not. And one period counts at most a few thousand distinct callers, past which a caller it is not already counting
is refused rather than admitted uncounted. Both are deliberate: a durable count would mean writing the identity a
credential resolves to onto every outgoing record and keeping it as long as the record, which is a great deal of
retained personal data for a bound whose whole purpose is to stop a loop within minutes.

### A recipient nothing here vouches for — `MailDelivery:UnvouchedRecipients`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailDelivery:UnvouchedRecipients` | enum | `Admit` | `Admit` or `Refuse` | restart |

`Refuse` narrows an authored send to people this deployment already holds a record of: an address in the contact book,
or one an account of this installation sends as. An address that is none of those and that the **caller itself named**
refuses the whole message, under `53007`, which names neither the address nor how many were refused.

Only what the caller named is judged. A recipient this deployment derived — whoever a reply answers, whoever a
reply-to-all keeps, an address resolved from a contact the caller named by identity — is this system's own answer rather
than the caller's word.

That does not divide neatly by tool. A plain reply is untouched, since everybody it reaches was read out of the message
being answered; a `cc` the caller adds to that reply is judged; and a **forward is judged in full**, because a forward
addresses nobody of its own and every address on it came from the call. So under `Refuse` a forward to somebody not yet
in the contact book is refused — which is the setting doing its work, and the thing to know before turning it on.

`Admit` is the default because refusing by default would refuse the first message of every installation whose contact
book is still empty. It is not the same as not judging: a send reaching somebody nothing vouches for is recorded as
such either way, which is the line to look for when reading back what a caller has been sending. What this setting is
for, and what it is not a defence against, is
[mail delivery](../features/mail-delivery.md#what-a-caller-may-be-talked-into).


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

Two things decide whether any link is issued at all, and neither is here. [`Deployment`](configuration-runtime.md#deployment) has to declare the
address a link is composed from, and [`DataEncryption`](configuration-runtime.md#dataencryption) has to configure a ring, because the signing
key is derived from it rather than provisioned separately. A deployment missing either serves every other part of a
read and answers each attachment with `downloadState: unavailable`.


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
