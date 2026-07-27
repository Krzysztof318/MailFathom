# Certificate Material and Secret Rotation

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 2
**Depends on:** 01, 02a
**Estimated change size:** ~750 lines including tests and documentation

## Goal

Load deployment-provisioned certificate material through the resolution contract specification 02a builds, install a trusted certificate authority into the mail transport's certificate validation path so a private server is supported with validation fully enabled, and let a rotated secret take effect without restarting the process.

## Current state

Specification 02a delivers the reference grammar, the four scheme adapters, the secret block, the byte-oriented resolution contract, memory hygiene, and fail-fast startup validation. It resolves mailbox and database passwords and it fixes the configuration shape of `TrustedCertificateAuthority`, but nothing reads that block's material.

Specification 01 requires that certificate validation is never disabled and that private or self-signed servers are supported by configuring additional trusted certificate authorities. It defines the policy and validates that a trust anchor is configured when `CertificateTrust` is `AdditionalTrustedAuthority`. It deliberately stops there: `MailAccountTransportSecurityOptions` carries the setting, and no code loads it.

Resolution in 02a happens once, at startup validation. A credential rotated afterwards is not observed until the process restarts.

## Approved scope

### Typed material above the resolver

The resolver yields opaque bytes. A certificate loader in `Infrastructure` turns those bytes into an X.509 certificate and is the only place that knows about X.509 at all, so a future material kind adds a loader rather than touching a scheme adapter.

Three encodings must load, because all three occur in deployment:

- **PEM** — what a certificate authority hands an operator, and the only encoding that also survives being pasted into configuration.
- **DER** — what some tooling emits. Binary, so it is reachable through `file:` and `systemd-credential:` but not through an inline value.
- **PKCS#12 / PFX** — binary, and *may* be protected by a password. When it is, that password is the nested secret block 02a defines. When it is not, the bundle is still valid and still loads; requiring a password block unconditionally would reject files an operator is entitled to use. This specification loads a bundle but presents no client certificate; MCP client certificates are stage 9 work.

A trust anchor must not carry a private key. Bundles commonly do, and this feature needs only a public anchor, so a key would sit outside the buffer whose lifetime this design controls — and with default key-storage flags the import can persist it to a key store on disk. Material is therefore imported with ephemeral key storage, and a trust anchor whose certificate carries a private key is rejected rather than silently accepted.

A trust anchor that does not parse, or parses but is unusable as a trust anchor, fails startup. Silently continuing would either reject a working server or invite an operator to disable validation to work around it.

### Inline certificate material

A trust anchor is a public certificate, not a private key, so writing one into configuration leaks nothing. Under `ReferenceOrInline` or `InlineOnly` the block therefore accepts the PEM text directly, which is what makes an Azure App Configuration deployment work end to end: the store holds the certificate, the provider binds it, and MailMcp parses what it was given.

Two limits are stated rather than discovered:

- Only PEM works inline. DER and PKCS#12 are binary and have no faithful representation in a configuration value, so an inline block carrying them is a startup failure that says so, rather than a parse error further down.
- PEM is multi-line, so a JSON document must escape the newlines. That is ugly but valid, and it is the operator's choice; a store-backed provider has no such problem because the value is transported, not authored in JSON.

### Installing the trust anchor

The loaded anchor is supplied to the mail transport's certificate validation path so a private server chains to it while ordinary validation stays enabled for everything else.

Trust is decided by rebuilding the chain against the configured anchor, never by accepting the error the platform reported. A name mismatch and an unavailable certificate are rejected outright; only a chain-trust failure is re-examined, and only by building a chain that trusts the configured anchor as a root and requiring a clean rebuild.

The rebuild reuses the intermediate certificates the server supplied. A private server whose certificate is signed by an intermediate rather than directly by the configured root is an ordinary deployment, and its intermediate is often available only from the handshake — not from the machine store and not from an AIA-reachable location. Discarding it would reject a correctly provisioned server. The supplied intermediates are candidates for path building only; trust still comes solely from the configured anchor. No configuration path exists that disables validation, and the definition of done asserts its absence.

### Rotation without restart

Secrets reload on the same terms as the configuration that references them. An operator who rotates a mailbox password, replaces an expiring trust anchor, or re-issues a database credential must not have to restart MailMcp and interrupt synchronization to do it.

Two independent things can change, and both are covered:

- **The reference changes.** A configuration reload delivers a new `SecretReference` inside one of the secret blocks. The candidate snapshot is validated by resolving every reference in it before it is published; a snapshot containing an unresolvable reference is rejected and the last known good snapshot stays active, exactly as ADR 0002 requires for reloadable groups. A rejected reload is logged with the configuration path and the failure identity, never with material.
- **The material behind an unchanged reference changes.** Rotating the credential file, the systemd credential, or the vault entry leaves configuration untouched, so no configuration reload fires. Resolution therefore moves from once-at-startup to per use, and the next operation that needs the secret observes the rotated value. A network-backed provider that cannot afford per-use retrieval caches inside its own adapter with its own expiry, which is why caching policy is an adapter concern rather than a contract concern.

The database credential needs its own treatment, because it is composed into a connection source once rather than read per operation. Without explicit handling, neither a changed reference nor rotated material would reach a connection opened afterwards, and revoking the old credential would take MailMcp offline until restart — the outcome rotation exists to prevent. The connection source is therefore rebuilt when the resolved credential changes, and superseded sources are disposed only after their in-flight connections drain.

Material is applied at operation boundaries, not mid-operation: a synchronization run that has authenticated continues with the credential it authenticated with, and the next run picks up the rotation. This is ADR 0002's "reloadable for new operations" classification, chosen over "reloadable during running operations" because swapping a credential or a trust anchor underneath an open IMAP session has no coherent meaning.

"The next operation" needs a definition that survives specification 11. Once a folder holds one authenticated IDLE connection across many synchronization runs, there is no next connect to pick up a rotation, and an old credential and an old trust anchor could stay in use for the process lifetime unless an unrelated disconnect happened to occur. The operation boundary for a long-lived session is therefore the *connection*, and a session whose secrets have rotated is deliberately recycled at the next safe point rather than left running. Specification 11 must honour this; it is recorded here because this specification is what makes the promise.

Per-use resolution composes with 02a's ownership rule rather than fighting it. Each operation owns the material it resolved and erases it when it ends, so publishing a new snapshot never erases material an in-flight operation is still reading.

### The ADR this changes

This departs from ADR 0002's current guidance, which classifies credentials and certificate trust anchors as restart-required. That guidance was written before a secret-reference indirection existed; with it, rotation no longer means mutating a bound secret value in place, but re-resolving a reference whose validity is proven before it is published.

**The ADR needs an owner-approved amendment recording this, and this specification must not be implemented against an unamended ADR.** `docs/AGENTS.md` forbids modifying an ADR without explicit owner approval, so no ADR is modified as part of writing this specification.

## Safety and privacy

A trust anchor is public material, so it may be logged by subject and thumbprint. Nothing else changes: a bundle password, like any other secret, is never logged, and a reload failure names the configuration path and the failure identity only.

Rotation shortens the window in which any single credential is valid, which is a privacy and security improvement rather than a cost. The one new exposure is that per-use resolution reads the credential source more often; it stays bounded because material is erased at the end of each operation instead of being cached.

## Testing

Certificate-loading tests cover PEM and DER both loading from bytes, a PKCS#12 bundle loading with a password drawn from a second block, malformed material failing with a named identity rather than an exception escaping, and inline DER being rejected with the encoding-specific failure rather than a generic parse error.

Trust-anchor tests cover a server certificate chaining to the configured anchor being accepted, a name mismatch being rejected even when the anchor would otherwise validate, an unavailable certificate being rejected, an untrusted chain without a configured anchor being rejected, and the absence of any configuration path that disables validation.

Reload tests cover a rotated reference being adopted, a candidate snapshot with an unresolvable reference being rejected while the previous secrets stay active, a rejected reload logging the path and failure without material, rotated material behind an unchanged reference being observed by the next operation, and the same rotation not affecting an operation already in flight.

## Out of scope

Presenting a client certificate. This specification loads PKCS#12 material and proves the bundle-password path works, but MailMcp presents no certificate of its own; MCP client certificates and the ChatGPT mTLS profile are stage 9 work.

Certificate revocation policy beyond what the chain rebuild requires, certificate expiry monitoring and alerting, and automatic renewal.

Managed secret store adapters, which specification 02a's extensibility section constrains and neither specification implements.

## Definition of done

- A private server with a configured trust anchor connects with certificate validation fully enabled.
- No configuration path exists that disables certificate validation, and a test asserts its absence.
- Trust is decided by rebuilding the chain against the configured anchor; a name mismatch or an unavailable certificate is rejected regardless of the anchor.
- PEM, DER, and PKCS#12 material all load from resolved bytes; a protected bundle takes its password from the nested secret block, and an unprotected bundle loads without one.
- A trust anchor carrying a private key is rejected, and material is imported with ephemeral key storage so nothing is written to a key store.
- A server certificate signed by an intermediate the server supplies chains successfully to the configured anchor, and that intermediate gains no trust of its own.
- A rotated database credential reaches connections opened afterwards without a process restart, and connections already open finish with the credential they authenticated with.
- A reload never blocks the configuration callback thread, never terminates the process on a resolution failure, and never lets an older candidate publish after a newer one.
- A trust anchor may be supplied inline as PEM under the inline interpretation modes; inline DER or PKCS#12 fails startup with a message that names the encoding as the reason.
- Malformed or unusable trust anchor material fails startup with a message that discloses no secret.
- Rotating the material behind an unchanged reference is observed by the next operation without a process restart.
- A configuration reload carrying an unresolvable reference is rejected and leaves the previous secrets active.
- A rotation is never applied to an operation already in flight.
- ADR 0002 has been amended, with owner approval, to classify referenced secrets as reloadable for new operations.
- `docs/features/imap-synchronization.md` documents the trust anchor behavior including the revocation trade-off, and `docs/operations/` documents the rotation procedure for both deployment shapes.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
