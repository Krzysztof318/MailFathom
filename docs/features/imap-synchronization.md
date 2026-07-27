# IMAP synchronization

MailMcp now includes the first vertical slice for read-only IMAP synchronization. The implemented slice is intentionally limited to periodic reconciliation so the persistence model, authenticated IMAP adapter seam, application ports, and safety invariants can be reviewed before adding long-lived IDLE or NOTIFY workers.

## Implemented behavior

- `Domain` models stable IMAP email occurrence identity as `EmailOccurrenceId`, keyed by `(account, folder, UIDVALIDITY, UID)`. `Email` is the repository-wide term for the mail artifact; `Message` is reserved so it stays unambiguous once AI conversation types exist.
- `Application` owns IMAP, metadata repository, content store, and checkpoint ports, plus the `IPersistenceSession` write-transaction port in `MailMcp.Application.Persistence`. The persistence session is named separately from `IMailboxSession` because both would otherwise be "the session" at a call site.
- `MailboxSynchronizer` opens folders through a read-only session port and requests bounded metadata batches. It retains at most one fetched MIME payload at a time: each seen-preserving remote fetch finishes before a short local session atomically upserts that occurrence's metadata, uses the returned local stored-email identifier for its content, and commits and disposes before the next remote fetch starts. After the inspected batch finishes, a separate short session advances the checkpoint only when the mailbox adapter reports a non-speculative UID cursor known safe from the opened folder state.
- Batches are bounded by email count, not by UID-space width. The adapter searches the whole remaining assigned UID range — a UID SEARCH returns identifiers only — and then fetches envelopes for at most `MaxMetadataBatchSize` emails. A folder whose UIDs are sparse after deletions therefore still advances a full batch per iteration instead of crawling the UID space, which keeps an initial backfill practical.
- An email that exceeds `MaxRawMimeBytes` is never silently dropped. Its occurrence metadata is committed with `ContentAvailability = ExceededSizeLimit` before the checkpoint moves past it, so the gap stays queryable and auditable instead of existing only as a counter in a log line. The same applies when the advertised size understated the payload and the bounded stream read rejects it mid-fetch.
- Committing occurrences before the window checkpoint means a process failure may cause a later run to fetch an already stored occurrence again. Content and metadata writes use the stable remote occurrence identity and are idempotent, so this retry does not create duplicate stored emails.
- `Infrastructure` maps the pre-migration PostgreSQL model to `mailbox_accounts`, `mail_folders`, `stored_emails`, `email_message_contents`, and separate `synchronization_checkpoints`. Each stored email has a local UUIDv7; its raw MIME row uses the same UUID as both primary key and foreign key and records byte length, SHA-256, and storage time. Each stored email also records a `ContentAvailability` value as text so a metadata-only occurrence is distinguishable from one whose raw MIME is present. Persistence sessions clear tracked state after cleanup so one scoped context does not retain MIME arrays between per-email transactions, and re-synchronizing an occurrence that is already stored overwrites its payload with a set-based update rather than reading the existing `bytea` back into the change tracker.
- A write repository takes its EF Core context from the `IPersistenceSession` it is handed, and injects none of its own. The write is therefore always issued on that session's own context, whichever scope the session came from, so "this write joined the caller's transaction" is structurally true instead of being an effect of both objects happening to resolve from the same DI scope. A session backed by a different persistence provider cannot supply a context at all and is rejected outright. Read methods take no session and use the scoped context, because a read joins no transaction.
- Lookups that must see an insert still pending in the open session use the change tracker before the database, since EF Core never flushes pending changes before a query. Primary-key lookups rely on `FindAsync`, which already does this; alternate-key lookups go through one shared two-pass helper driven by a single predicate expression. The one hand-written exception is the raw MIME row, where materializing the existing `bytea` is precisely the cost being avoided.
- Mutable tracked email metadata and synchronization checkpoints carry an infrastructure-only `ConcurrencyVersion`. It is a `uint` row version, which is how Npgsql maps a property onto the PostgreSQL `xmin` system column, so the token is server-generated and no concurrency column exists in either table. A stale tracked update is translated from `DbUpdateConcurrencyException` into an application-owned commit result at the session boundary, which is the only place a conflict is an ordinary branch: its consumer is the retry policy's loop. Synchronization retries a complete idempotent metadata/content write in a fresh persistence session, never repeats the preceding IMAP fetch, and uses cancellation-aware exponential backoff with jitter between bounded attempts. Checkpoint writes are attempted once and only when their durable UIDVALIDITY and last-seen UID still equal the progress read at the start of the run; timestamp precision differences are ignored, while the later synchronization timestamp is retained. `xmin` detects a later race before commit, and a concurrent first-checkpoint primary-key collision is treated narrowly as the same conflict.
- Once bounded attempts are spent, or a checkpoint moved under the run, the conflict leaves `SynchronizeAsync` as `PersistenceConcurrencyConflictException` instead of being restated as a result value by each layer it passes. Progress the run already committed stays durable. The worker catches it per folder, logs a deferral with the reason, and continues with the remaining folders; the next interval rereads the last committed checkpoint. The attempt bound is one deployment-wide setting, not a synchronization option, because writers compete for shared rows rather than for anything a single service owns.
- The MailKit adapter resolves folders asynchronously, caps UID progress with the opened folder UIDNEXT value, normalizes email sent dates to UTC before persistence, and rejects occurrence identities that do not belong to the open account, folder, and UIDVALIDITY scope.
- Failed MailKit session setup attempts both disconnect and disposal without replacing the primary setup failure. Normal session disposal also attempts both operations and reports the first cleanup failure.
- `Domain` owns the mail transport security policy: the five connection-security modes, the ordered SASL allow-list, the two opt-ins that permit weakening transport protection, and the trust-anchor selection. The rules that reject an unsafe combination live in `MailTransportSecurityPolicy` rather than in a configuration validator, so a future command-line or MCP entry point cannot reach a transport adapter with a policy that host startup would have refused.
- A permitted mechanism is a domain value object rather than an enum, so its registered SASL name, its clear-text classification, and its JSON form travel with the value instead of living in a separate mapping table that could drift. It serializes as that SASL name, which is also the name configuration accepts and the name matched against a server's advertised set.
- The policy is an input to `IMailboxSessionFactory.OpenReadOnlyAsync`, not something the adapter resolves. `MailboxSynchronizer` reads it per run through `IMailTransportSecurityPolicyReader`, which is why an adapter can only narrow what it is handed.
- `Infrastructure` owns secret reference resolution. Every secret-bearing setting binds to a block carrying a `<scheme>:<target>` reference, four scheme adapters resolve it into pinned byte material that is erased when the operation that owns it ends, and `Host` fails startup when any reference is unresolvable. `Application` and `Domain` see none of it. [Secret provisioning](../operations/secret-provisioning.md) documents the grammar, the deployment shapes, the interpretation modes, and the residual in-memory exposures.
- `Infrastructure` also owns the one place that knows about X.509. `TrustAnchorLoader` turns the bytes a resolver produced into a certificate, so a future material kind arrives as another loader rather than as a change to how a secret is retrieved. It recognizes PEM, DER, and PKCS#12 from the material itself, imports every bundle with ephemeral key storage, and rejects an anchor that carries a private key.
- `MailServerCertificateValidator` decides trust for an account that names an additional authority, by rebuilding the chain against the configured anchor rather than by forgiving what the platform reported. Nothing anywhere can switch validation off.
- Secrets are re-resolved per operation rather than cached, and the configuration snapshot that names them is republished only after every reference in it has resolved. A rotated credential, trust anchor, or database password therefore reaches the next operation without a restart, and a reload that cannot resolve leaves the previous snapshot active. [Secret rotation](../operations/secret-rotation.md) is the operator procedure.
- `Host` provides typed `MailSynchronization` options, startup validation for enabled account connection settings and their transport security policy, secret resolution and trust anchor loading before any hosted service starts, a validated snapshot every consumer reads instead of the raw bound one, and a periodic scoped background worker that isolates failures per account/folder work unit.

## Configuration

Synchronization is disabled by default:

```json
{
  "MailSynchronization": {
    "Enabled": false,
    "Interval": "00:05:00",
    "MaxMetadataBatchSize": 100,
    "MaxRawMimeBytes": 26214400,
    "MaxMetadataBatchesPerRun": 10,
    "Accounts": [
      {
        "AccountId": "primary",
        "Host": "imap.example.test",
        "Port": 993,
        "UserName": "mailmcp@example.test",
        "Secrets": {
          "Password": { "SecretReference": "systemd-credential:imap-primary-password" }
        },
        "TransportSecurity": {
          "ConnectionSecurity": "TlsOnConnect",
          "PermittedAuthenticationMechanisms": [ "SCRAM-SHA-256", "PLAIN" ],
          "AllowInsecureConnection": false,
          "AllowClearTextAuthenticationOverUnencryptedConnection": false,
          "CertificateTrust": "SystemTrustStore"
        },
        "Folders": [ "INBOX" ]
      }
    ]
  }
}
```

Optimistic concurrency is configured once for the whole deployment, outside the synchronization section, because it bounds every local writer rather than this feature. The PostgreSQL password sits in the same section as an optional secret reference, so the connection string in configuration keeps host, database, and user name and never carries the credential:

```json
{
  "Persistence": {
    "MaximumConcurrencyCommitAttempts": 2,
    "Password": { "SecretReference": "file:/run/secrets/postgres-password" }
  }
}
```

When enabled, at least one account with a non-blank `AccountId`, host, and user name must be configured. The account password is not a configuration value at all: `Secrets.Password` carries a reference, and startup fails when it cannot be resolved. If an account omits `Folders`, the worker applies the post-binding default `INBOX`; explicit folder lists replace that default.

### Secrets

Every secret-bearing setting — the account password, the trust anchor, the database password — binds to a block whose `SecretReference` property holds a `<scheme>:<target>` reference rather than the credential. The host resolves every reference before any hosted service starts and reports all failures at once, each naming its configuration path and a stable failure identity and nothing else. Each actual connection attempt resolves again and erases the material when it finishes, so no long-lived copy exists and a rotated credential is observed without a restart.

Secret resolution is not gated on `Enabled`, unlike the transport security rules. Every configured account's password reference is resolved at startup even when synchronization is disabled, because a reference an operator wrote is a reference they intend to work, and discovering it broken at the moment synchronization is switched on is worse than discovering it now. An account that is configured but has no reachable password therefore fails startup; remove the account rather than disabling synchronization around it.

[Secret provisioning](../operations/secret-provisioning.md) is the operator reference: the four schemes, the systemd, Compose, and Kubernetes provisioning paths, the three interpretation modes and why `ReferenceOnly` is the default, and the in-memory exposures that need operational rather than code-level mitigation.

Account identifiers and folder names must be unique after domain normalization, IMAP ports must be between 1 and 65535, and `MaximumConcurrencyCommitAttempts` must be between 1 and 10. The default of two attempts covers the single lost race that a rare conflict represents; a folder deferred after that is retried by the next interval anyway.

### Transport security

Every setting below lives in the account's `TransportSecurity` section, which `MailAccountTransportSecurityOptions` in `Infrastructure` binds and validates. `ConnectionSecurity` selects one of five modes and defaults to `TlsOnConnect`:

| Mode | Behavior |
| --- | --- |
| `TlsOnConnect` | Encrypts immediately with implicit TLS. |
| `StartTlsRequired` | Requires STARTTLS and fails when the server does not advertise it. |
| `StartTlsWhenAvailable` | Uses STARTTLS when advertised and otherwise continues unencrypted. |
| `Auto` | Lets the client negotiate and continues unencrypted when the server offers no encryption. |
| `None` | Uses no encryption. |

Only the first two guarantee that nothing travels unencrypted. The other three require `AllowInsecureConnection: true`, including `Auto` and `StartTlsWhenAvailable`: an opportunistic mode completes the connection in clear text whenever the server declines encryption, which is the same exposure as `None`.

`PermittedAuthenticationMechanisms` is an **unordered** allow-list that defaults to `[ "PLAIN", "LOGIN" ]` when omitted, which is safe under the default `TlsOnConnect` and trips the clear-text rule on any mode that can stay unencrypted. The default is applied after binding rather than as a property initializer, because the configuration binder appends bound entries to an existing list and would otherwise keep `PLAIN` and `LOGIN` permitted alongside whatever the operator configured. Supported names are `PLAIN`, `LOGIN`, `CRAM-MD5`, `DIGEST-MD5`, `SCRAM-SHA-1`, `SCRAM-SHA-1-PLUS`, `SCRAM-SHA-256`, `SCRAM-SHA-256-PLUS`, `SCRAM-SHA-512`, `SCRAM-SHA-512-PLUS`, and `NTLM`; names are matched ignoring case and duplicates collapse while keeping the configured order. That order is presentation only: the adapter narrows the server's advertised set to the permitted names and lets MailKit pick the strongest survivor, deliberately rather than obeying the configured sequence, so a list that happens to put `PLAIN` before `SCRAM-SHA-256` still authenticates with SCRAM when the server offers it. Permitting `PLAIN` or `LOGIN` on a mode that can stay unencrypted additionally requires `AllowClearTextAuthenticationOverUnencryptedConnection: true` on top of `AllowInsecureConnection: true`, because those two mechanisms hand over the reusable password itself.

The MailKit adapter removes every non-permitted mechanism from the set the server advertised before it authenticates, and fails with `MailAuthenticationMechanismUnavailableException` when nothing permitted remains. It never restores a removed mechanism after a failed authentication, so a server cannot negotiate its way to a mechanism the operator refused. A server that advertises no SASL mechanism at all is treated the same way and the account fails rather than falling back to the clear-text IMAP `LOGIN` command, which the allow-list cannot describe.

Certificate validation is always enabled and no configuration path disables it. A private or self-signed server is supported by setting `CertificateTrust` to `AdditionalTrustedAuthority` and naming the deployment-provisioned material in the `TrustedCertificateAuthority` secret block. `SystemTrustStore` rejects a configured anchor, and `AdditionalTrustedAuthority` requires one. A block present with a blank `SecretReference` reads as no anchor at all, so `"TrustedCertificateAuthority": {}` fails the rule that requires one rather than passing it and failing later with a confusing missing-material error.

#### Trust anchor material

The block resolves like any other secret, and the bytes behind it are loaded as a certificate. Three encodings occur in deployment and all three load, recognized from the material rather than declared in configuration:

| Encoding | Where it comes from | Inline |
| --- | --- | --- |
| PEM | What a certificate authority hands an operator. | Yes |
| DER | What some tooling emits. Binary. | No |
| PKCS#12 / PFX | A bundle, optionally protected by a password. Binary. | No |

A protected bundle takes its password from the nested `Password` block, which is itself an ordinary secret block, so a bundle password is validated, resolved, and erased by exactly the machinery every other secret uses. An unprotected bundle is still a valid file an operator is entitled to use and loads without one.

```json
{
  "TrustedCertificateAuthority": {
    "SecretReference": "systemd-credential:private-ca-bundle",
    "Password": { "SecretReference": "systemd-credential:private-ca-bundle-password" }
  }
}
```

The configured value decides only whether an anchor is *present*; whether it is usable is the loader's question. A non-blank value is an anchor the operator supplied, whether or not it is a `<scheme>:<target>` reference, so the inline shape below passes the presence rule and then gets a named load failure if the material is wrong — rather than being reported as a missing anchor, which it is not. What crosses into the domain policy is never the raw value: a parsed reference crosses masked, and anything else as a fixed `***`.

Under `ReferenceOrInline` or `InlineOnly` the block may carry the PEM text directly, which is what makes an Azure App Configuration deployment work end to end: the store holds the certificate, the provider binds it, and MailMcp parses what it was given. A trust anchor is a public certificate, so writing one into configuration leaks nothing. Only PEM works that way — DER and PKCS#12 are binary and have no faithful representation in a configuration value, so an inline block carrying them fails startup with `InlineEncodingNotSupported`, naming the encoding rather than surfacing a parse error further down. PEM is multi-line, so a JSON document has to escape the newlines; a store-backed provider has no such problem, because the value is transported rather than authored in JSON.

Material is imported with ephemeral key storage, and an anchor that carries a private key is **rejected**. A trust anchor needs only a public certificate; a private key would sit outside the buffer whose lifetime the secret machinery controls, and with default key-storage flags the import could persist it to a key store on disk. Material that does not parse, or parses but is unusable, fails startup with a named failure and never with the material itself:

| Failure | Meaning |
| --- | --- |
| `MaterialMissing` | The block carries no reference at all. |
| `SecretNotResolvable` | The reference, or the bundle password's reference, produced no material. |
| `EncodingNotRecognized` | The material is neither PEM nor an ASN.1 certificate or bundle. |
| `InlineEncodingNotSupported` | Binary material was supplied as the configuration value itself. |
| `MaterialNotReadable` | The encoding is supported but the material does not parse. |
| `BundlePasswordMissing` | The bundle is protected and no nested `Password` block was configured. |
| `BundlePasswordIncorrect` | The bundle did not open with the configured password. |
| `BundleCarriesNoCertificate` | The bundle parsed but holds no certificate. |
| `TrustAnchorCarriesPrivateKey` | The certificate carries a private key. |

The platform reports a wrong bundle password, a missing one, and corrupt bundle contents identically, so the last two are told apart by what was configured rather than by what the platform said. It is a diagnostic refinement that points at the part an operator controls, not a claim about the material. A loaded anchor is logged by subject and thumbprint, which is public information and the detail that confirms MailMcp trusts the authority the operator provisioned.

#### How the anchor is used

Trust is decided by rebuilding the chain against the configured anchor, never by accepting the error the platform reported:

- A name mismatch and an unavailable certificate are rejected outright. Neither has anything to do with which authority signed the certificate, and forgiving them would turn the private-authority path into the validation bypass this design exists to avoid.
- Only a chain-trust failure is re-examined, by building a chain that trusts the configured anchor as its sole root and requiring a clean rebuild. The rebuild re-applies the requirement that the certificate be usable for TLS server authentication, because a chain error also covers a usage rejection and the same private authority commonly issues client certificates too. It also refuses a chain the platform reported as revoked or explicitly distrusted, since neither verdict is about which authority signed the certificate and the rebuild checks no revocation of its own.
- Certificate downloads are disabled for the rebuild. The handshake already supplied every intermediate it is meant to use, and leaving them enabled would let an incomplete, server-chosen chain send a synchronous validation callback to a URL of the server's choosing with no caller cancellation reaching it.
- The certificates the server sent are reused as path-building candidates. A private server whose certificate is signed by an intermediate rather than directly by the configured root is an ordinary deployment, and that intermediate is often reachable only from the handshake. It completes a path; it gains no trust of its own.

**Revocation trade-off.** The rebuild does not check revocation. A private authority typically publishes neither a CRL distribution point nor an OCSP responder, so an online check would fail every connection to the server this feature exists to support, and a status-unknown result would have to be either ignored — which is what skipping the check states plainly — or treated as fatal. Compromise of a deployment-provisioned anchor is therefore handled by replacing the provisioned material, which rotation now makes possible without a restart. An account left on `SystemTrustStore` is unaffected: it keeps the mail client's own validation, revocation checking included.

The whole `MailSynchronization` section binds strictly (`ErrorOnUnknownConfiguration`), so a misspelled key fails startup instead of being ignored. Without that, a singular `PermittedAuthenticationMechanism` would be dropped silently and the default allow-list would take its place, quietly permitting mechanisms the operator meant to exclude.

Every rule above is enforced twice: in the domain policy object and again during `ValidateOnStart` options validation. A connection-security mode or certificate-trust source bound from a raw number that names no member is reported as a violation rather than slipping past the rules it cannot be evaluated against. `Host` binds the section and turns each reported configuration error into a startup failure that names the account and the violated rule and never includes the user name, password, or the trust anchor reference.

Each reported error carries the domain's `MailTransportSecurityViolation` alongside its operator sentence, and the startup message appends that identity in brackets — for example `Account 'primary': An unencrypted connection requires AllowInsecureConnection. [UnencryptedConnectionRequiresExplicitOptIn]`. The bracketed name is the stable half: an operator or log query can match on it while the surrounding prose stays free to change. An unsupported SASL mechanism name carries no violation and is reported without brackets, because it is a parse failure rather than a violated rule.

Secret resolution is the one rule that cannot join `ValidateOnStart`, because options validation is synchronous while resolution is not — the contract is asynchronous so a network-backed secret store needs no breaking change later. It runs instead in the host's starting phase, which completes before any hosted service starts, so the worker never runs against an unresolvable secret.

## Safety assumptions

The application layer exposes only `FetchEmailContentWithoutSettingSeenAsync` for content retrieval during synchronization. This name is part of the contract: implementations must use IMAP read-only selection and BODY.PEEK-equivalent behavior so remote `\Seen` flags are not changed. The MailKit adapter satisfies both halves — it selects the folder with `FolderAccess.ReadOnly` and retrieves content through `GetStreamAsync(uid)`, which issues `UID FETCH <uid> (BODY.PEEK[])`. A regression test exercises a successful fetch and asserts that neither `StoreAsync`, the only `IMailFolder` member able to change flags, nor a read-write reselection was requested. Metadata requests are bounded by `MaxMetadataBatchSize`, each run is bounded by `MaxMetadataBatchesPerRun`, empty unassigned UID ranges are not checkpointed speculatively, and raw MIME above `MaxRawMimeBytes` is recorded as metadata-only. Logs record counts and account/folder identifiers only; raw MIME, email bodies, attachments, credentials, and tokens remain sensitive and must not be logged.

### Reloading a rotated reference

Every consumer reads a published snapshot rather than the raw bound options. A configuration reload produces a candidate, and the candidate becomes the published snapshot only after every secret reference in it resolves and every configured trust anchor loads. A candidate that fails is discarded with a log line naming the configuration path and the failure identity, and the previous snapshot stays active — a mistyped credential name does not take a running deployment offline.

Validation never runs on the thread that reported the reload. It is handed to a single background reader through a channel that keeps only the newest candidate, so a burst of reloads costs one validation rather than a queue of stale ones, and an older candidate can never overwrite a newer one that already published. A reload that fails unexpectedly is logged and dropped; it never terminates the process.

Snapshots are read once per operation, and one operation means one snapshot end to end. The worker takes accounts and folders when a run begins and hands that same snapshot down to each folder's scope, so a folder scheduled from one account list can never connect with another's endpoint, policy, and credentials. Each work unit's scope therefore holds one snapshot — the transport security policy it validates against and the material it connects with therefore always come from the same reload, which two independent reads of the published snapshot could not guarantee. Whether synchronization runs at all and how often are read once at start, because both shape the worker loop itself.

The database secrets reload on the same terms. `Persistence:Password` and `Persistence:ConnectionString` are read from their own published snapshot each time a physical connection needs a credential, so repointing a reference takes effect without a restart, and a reload whose reference does not resolve is rejected with the previous one left active. Two further checks run before that snapshot publishes, because resolving a reference proves less for a connection string than for a password: the material must parse as a PostgreSQL connection string and, when it is what supplies the credential, still carry one. Changing *which* setting supplies the credential is refused outright — the pool attaches its password provider once, so that change is restart-required and is reported as such instead of being adopted with no effect.

A rejected reload is logged with the configuration path and the failure identity. When a credential provider fails in a way no failure identity covers, only the exception's type is logged and its message and stack trace are deliberately withheld, because a provider exception routinely carries the target path, request URI, or credential identifier that the reload contract keeps out of diagnostics.

## Pending work

- Adapters for external managed secret stores. Kubernetes and container deployments need none, because their secrets are files.
- OAuth mailbox authentication. `XOAUTH2` and `OAUTHBEARER` are deliberately absent from the allow-list because no token source exists yet.
- IMAP IDLE and NOTIFY support.
- Explicit EF Core migrations after schema review.
- Integration tests with PostgreSQL and a real IMAP server in the later integration-test phase, including EF mapping, `xmin` conflict detection across transactions, same-transaction token semantics, PK/FK, integrity-metadata, and uniqueness-constraint verification required by ADR 001. Temporary provider-bound coverage exclusions carry adjacent TODOs for removal at that point.
- MCP read tools, RAG indexing, and SMTP outbox integration.
