# Configuration reference

<!-- describes: src/**/*Options.cs, src/Host/Configuration/** -->

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
| `MailSynchronization:MaxReconciledEmailsPerRun` | int | `500` | 1 – 10000 | reload |
| `MailSynchronization:MaxMimePartCount` | int | `1000` | 1 – 100000 | reload |
| `MailSynchronization:MaxMimeNestingDepth` | int | `30` | 1 – 1000 | reload |
| `MailSynchronization:MaxExtractedTextCharacters` | int | `100000` | 1000 – 200000; the ceiling keeps the search vector inside PostgreSQL's limit | reload |
| `MailSynchronization:PushRenewalInterval` | TimeSpan | `00:20:00` | 1 min – 29 min; the lifetime of one `IDLE` command, **not** a polling cycle — the ceiling is what RFC 2177 mandates | reload |
| `MailSynchronization:MaxConsecutivePushFailures` | int | `3` | 1 – 100 | reload |
| `MailSynchronization:PushDegradationPeriod` | TimeSpan | `00:15:00` | 10 s – 1 day | reload |
| `MailSynchronization:MaxSubscribedFolders` | int | `20` | 1 – 100; how many folders one push subscription may name on a server supporting `NOTIFY`, the rest synchronizing on the account's interval | reload |

### One account — `MailSynchronization:Accounts:<n>`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:AccountId` | string | — | Required; unique across accounts after normalization | reload |
| `…:Host` | string | — | Required when synchronization is enabled | reload |
| `…:Port` | int | `993` | 1 – 65535 | reload |
| `…:UserName` | string | — | Required when synchronization is enabled; an identifier, not a secret | reload |
| `…:Secrets:Password` | secret block | unset | Required when the permitted mechanisms include any password mechanism; must resolve at startup | reload; material per connection |
| `…:Mode` | enum | `Polling` | `Polling`, `Push`; push holds one connection open per account on a server supporting `NOTIFY`, and one per folder on a server offering only `IDLE` | reload; the next run adopts it |
| `…:EarliestEmailReceivedDate` | date | unset (everything) | Not in the future (compared in UTC) | reload |
| `…:RemotelyDeletedEmailDisposition` | enum | `RetainTombstone` | `RetainTombstone`, `EraseLocalCopy` | reload; governs disappearances observed from then on |
| `…:AuthoredDeleteEmailDisposition` | enum | `RetainLocalCopy` | `RetainLocalCopy`, `RetainTombstone`, `EraseLocalCopy`; what becomes of the local copy of mail MailFathom itself deleted, and it takes precedence over the key above for those | reload; governs deletes authored from then on |
| `…:AuditTrail:Enabled` | bool | `false` | Whether a finished change to this account's mailbox leaves a durable audit entry | reload; governs changes authored from then on |
| `…:AuditTrail:Retention` | TimeSpan | `90.00:00:00` | 1 day – 3650 days; how long this account's audit entries are kept | reload; the next account run erases against the new window |
| `…:Folders` | list | inbox by role | Aliases unique; each entry below | reload |

`AuditTrail` is off by default because the record it keeps is derived personal data: it says where a person's mail has
been, when, and at whose instruction. Turning it on commits the deployment to holding that history, describing it, and
erasing it — which is why the retention is configured beside the switch rather than left unbounded, and why turning the
switch back off stops new entries while leaving the existing ones to age out under the window they were written under.
[An account can keep a record of what was done to it](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)
states what an entry holds and what it deliberately does not.

A folder entry names `Alias` (required — your stable name for the folder) and **exactly one** of `RemotePath` (the
server's own path) or `SpecialUse` (a role discovery resolves: `Inbox`, `Archive`, `Drafts`, `Sent`, `Junk`, `Trash`,
`All`, `Flagged`, `Important`). Configuring no folder synchronizes the inbox by role.

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

## `MailboxSearch`

The deployment-wide privacy bound on what a search result may quote, whether the result was ranked lexically or
hybridly. [Email search](../features/email-search.md) records how snippets are cut.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailboxSearch:SnippetsPerEmail` | int | `3` | 1 – 10 | restart |
| `MailboxSearch:WordsPerSnippet` | int | `24` | 4 – 100 | restart |

## `EmailContent`

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `EmailContent:MaxBodyCharacters` | int | `100000` | 1000 – 1000000; each body representation is truncated to it, explicitly | restart |
| `EmailContent:MaxCharactersPerRead` | int | `200000` | 2000 – 2000000, and at least twice `MaxBodyCharacters`; the body characters one call returns across every email it names | restart |

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
| `…:Address` | string | *(empty)* | absolute HTTPS; empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | restart |
| `…:SupportsRequestedDimension` | bool | `true` | whether the endpoint honours a requested width, so the narrower space is asked for rather than cut out of a wider answer | restart |
| `…:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this and `EntraCredential` is declared | restart, value read per request |

### Microsoft Entra credential — `Embeddings:Endpoints:<n>:EntraCredential`

For an endpoint where no key exists to provision. All four shapes are non-interactive by construction: MailFathom is a
background service with nobody at a keyboard, and `DefaultAzureCredential` is deliberately not used because its chain
reaches an interactive browser credential and the developer-tool credentials of whoever is signed in on the host.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Kind` | enum | `ManagedIdentity` | `ManagedIdentity`, `WorkloadIdentity`, `ClientSecret`, `ClientCertificate`. `ApiKey` is refused here; a key is declared as one | restart |
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

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Chat:Alias` | string | *(empty)* | writing one is what configures a chat provider at all; unique across every AI endpoint the deployment declares, embedding endpoints included. A section carrying other settings without it is refused rather than ignored | restart |
| `Chat:Model` | string | — | required once an alias is written; what a request is routed to, which for a cloud deployment is the deployment's own name rather than the vendor's model identifier | restart |
| `Chat:Address` | string | *(empty)* | absolute HTTPS; empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | restart |
| `Chat:MaxOutputTokens` | int | `1024` | 1 – 200000; what one answer may occupy. Reaching it is not a failure — the answer arrives marked as cut short | restart |
| `Chat:Temperature` | float | *(unset)* | 0 – 2; left unset sends nothing, which is required by the models that reject the parameter outright | restart |
| `Chat:TopP` | float | *(unset)* | 0 – 1; unset the same way, and for the same reason | restart |
| `Chat:MaxMessagesPerRequest` | int | `64` | 1 – 512; the turns one request carries, refused rather than truncated | restart |
| `Chat:MaxRequestCharacters` | int | `120000` | 1 – 4000000; what those turns may add up to. Stated in characters rather than tokens because counting tokens would mean carrying the model's own tokenizer; set it below what the model's context window allows | restart |
| `Chat:RequestTimeout` | TimeSpan | `00:02:00` | positive; one request. Longer than an embedding request's by default, because generating an answer takes as long as the answer is | restart |
| `Chat:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this and `EntraCredential` is declared | restart, value read per request |

### Microsoft Entra credential — `Chat:EntraCredential`

The same block, with the same keys, defaults, and rules as
[`Embeddings:Endpoints:<n>:EntraCredential`](#microsoft-entra-credential--embeddingsendpointsnentracredential) above.
One credential source resolves both sections, which is why the alias uniqueness rule spans them.

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
| `Chat:RelevanceFilter:MaxCandidates` | int | `8` | 1 – 8, which is everything one retrieval hands over — a higher value would name candidates that never exist and is refused rather than accepted and never met. The ceiling on what one lookup spends and how long it takes. Set below the default it buys a weaker filter rather than a shorter result: a passage nobody judged keeps its place | restart |
| `Chat:RelevanceFilter:MinimumRelevance` | int | `50` | 1 – 100, on the scale the model answers a judgement on. A threshold of 0 is refused: it would pay for a judgement that can drop nothing | restart |

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
several blocks; the only entry that fails startup is one carrying none, named by its position. Both endpoints take the
same entries; the administrative one adds a single rule, stated with it below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:<n>:ApiKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`; a second key is a second entry | restart; material per request |
| `…:<n>:PublicKey` | secret block | — | One [named secret](secret-provisioning.md#the-secret-block) with its own `Lifetime`, resolving to one client's PEM public key. Startup refuses material that is not one, an RSA key below 2048 bits, and — explicitly — material carrying a private key | restart; material per request |
| `…:<n>:OAuth:Resource` | string | — | Required; the canonical `https` URL clients reach this endpoint at — behind a proxy, the proxy's public URL. Every OAuth entry names the same one, because the endpoint publishes one metadata document at an address derived from it | restart |
| `…:<n>:OAuth:RequiredScopes` | string list | empty | Scopes a token from *this entry's* servers must carry; empty accepts any token they issued for this resource | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Name` | string | — | Required; the identity diagnostics use, and unique across every entry because it composes the scheme its validator registers under | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:Issuer` | string | — | Required; a well-formed `https` issuer, compared against `iss` exactly, and unique across every entry | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:MetadataAddress` | string | unset | An absolute `https` URL on the issuer's own host; overrides issuer-derived discovery | restart |
| `…:<n>:OAuth:AuthorizationServers:<m>:AuthorizedSubjects` | string list | — | At least one; a token whose `sub` is not listed is refused, so every user the server can sign in does not automatically read this mailbox | restart |

MailFathom is a protected resource only; an external authorization server signs users in.
[`OAuth`](mcp-endpoint.md#oauth) records what a token must prove,
[API keys](mcp-endpoint.md#api-keys) what a key is compared against, and
[Key pairs](mcp-endpoint.md#key-pairs) what a client signs and what the deployment verifies — including the audience,
expiry, and replay identifier an assertion carries, none of which is a setting.

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

The one endpoint subsection where every value has a product default, so an enabled endpoint is bounded whether or not
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
| `AdminEndpoint:Authentication` | list of credentials | empty | Same shape and rules as [`McpEndpoint:Authentication:<n>`](#the-accepted-credentials--mcpendpointauthenticationn), with two additions: every `OAuth` block's `Resource` must end in `/api/admin`, because that is where these routes answer and what `mfctl` appends to find the metadata document; and a client assertion presented here names the audience `urn:mailfathom:admin` rather than `urn:mailfathom:mcp` | restart; material per request |
| `AdminEndpoint:Https:Endpoints:<n>` | list of profiles | empty | Same shape and rules as `McpEndpoint:Https:Endpoints:<n>`, read under the two `Transport` modes that terminate TLS | restart; material per handshake |
| `AdminEndpoint:Https:Redirect` | block | on | Same shape and rules as `McpEndpoint:Https:Redirect`; its socket is this surface's own `BindAddress` and `Port`, so terminating TLS on both surfaces opens two clear-text ports that do not collide | restart |
| `AdminEndpoint:RateLimiting` | block | bounded | Same shape, defaults, and rules as `McpEndpoint:RateLimiting` above; applied whether or not it is written | restart |

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
