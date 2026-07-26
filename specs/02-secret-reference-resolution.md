# Secret Reference Resolution

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 2
**Depends on:** 01
**Estimated change size:** ~1600 lines including tests and documentation — above the ~1000-line ceiling `specs/README.md` sets per specification, so this specification is a candidate for splitting; see "Delivery size" below

## Goal

Implement the secret handling model from draft section 7.3 so that no mailbox password, database password, or certificate password is ever written into `appsettings.json`, and so that an unresolved secret reference fails startup instead of producing a confusing authentication error later.

## Current state

`MailSynchronizationAccountOptions` holds a plain `Password` string bound directly from configuration. The default `appsettings.json` ships an empty account list, so nothing is leaked today, but the shape invites operators to commit credentials.

## Approved scope

Configuration values that carry secrets become secret references rather than secret values. A reference is a string with an explicit scheme, `<scheme>:<target>`, split on the first colon so a path or URL in the target survives intact. This release implements four schemes:

- `systemd-credential:<name>` reads from the runtime credentials directory that systemd exposes to the service.
- `file:<path>` reads a deployment-provisioned protected file.
- `env:<variable>` reads an environment variable, permitted for non-production automation.
- `plaintext:<value>` names a literal value inline. It is not restricted to Development; see "Inline values and pre-resolving configuration providers" for the mode that governs it.

The scheme set is open by design; see "Extensibility to external secret providers" below. The grammar is fixed, the set of schemes that satisfy it is not.

Configuration carries the reference and nothing else. Every secret-bearing setting is a `*Reference` property whose value the host resolves at startup, so the shape an operator commits to Git is:

```json
{
  "MailSynchronization": {
    "Accounts": [
      {
        "AccountId": "primary",
        "Host": "imap.example.test",
        "UserName": "mailmcp@example.test",
        "Secrets": { "PasswordReference": "systemd-credential:imap-primary-password" },
        "TransportSecurity": {
          "CertificateTrust": "AdditionalTrustedAuthority",
          "TrustedCertificateAuthorityReference": "file:/run/secrets/private-ca.pem"
        }
      }
    ]
  },
  "Persistence": { "PasswordReference": "file:/run/secrets/postgres-password" }
}
```

A setting that names a reference is inert on its own: nothing in the file discloses a secret, and a file leaked from a backup or a repository yields only credential names and paths.

## Inline values and pre-resolving configuration providers

A secret-bearing setting does not always carry a reference. Two legitimate cases require the value itself to be accepted, and neither is a mistake to be prevented:

- **The operator chooses to write the secret into configuration.** It is less safe and the documentation says so, but it is the operator's deployment and their call. Refusing it outright pushes people toward worse workarounds.
- **A configuration provider already resolved the secret before binding.** Azure App Configuration with Key Vault references is the concrete example: App Configuration stores a URI rather than a value, but "because the client provider recognizes the key as a Key Vault reference, it uses Key Vault to retrieve its value", and application code then "access[es] the values of Key Vault references the same way [it] access[es] the values of regular App Configuration keys". By the time MailMcp binds the setting, the value *is* the secret, and it carries no scheme prefix that MailMcp could recognize. A resolver that insists on a scheme would break this integration outright.

Interpretation of a secret-bearing setting is therefore an explicit deployment choice, not an inference:

- `ReferenceOnly` — the value must be `<scheme>:<target>`; anything else fails startup. This is the default, and it is what keeps a mistyped `fil:/run/secrets/imap` a startup failure instead of a password.
- `ReferenceOrInline` — a recognized scheme resolves through its adapter; any other value is taken as the secret itself. For an operator who wants some secrets inline, and for mixed deployments.
- `InlineOnly` — every value is already the secret and no scheme is parsed at all. This is the mode for Azure App Configuration with Key Vault references, and for any future provider that pre-resolves. It removes the ambiguity entirely rather than guessing.

`plaintext:<value>` remains available in the two reference-accepting modes as the unambiguous spelling for a literal that would otherwise look like a scheme — a password whose value genuinely begins with `file:`. In `InlineOnly` no such escape hatch is needed, because nothing is parsed.

Modes make the earlier `plaintext:`-outside-Development rule unnecessary as a hard gate. `plaintext:` and inline values are permitted in any environment when the mode allows them; what protects a production deployment is that `ReferenceOnly` is the default and any other mode is a deliberate, visible setting. Startup logs which mode is active and, when it is not `ReferenceOnly`, which settings resolved to an inline value — naming the settings, never the values — so an unintended inline secret is discoverable rather than silent.

One consequence must be stated rather than hidden: an inline value arrives from the configuration system as a `string`, and a `string` cannot be erased from memory (see "Secret material in memory"). The inline modes therefore forfeit part of the in-memory protection that reference resolution provides. That is a real cost of the convenience, it is documented for the operator, and it is another reason `ReferenceOnly` is the default.

Note that this also settles where a pre-resolving provider belongs architecturally. Azure App Configuration is **not** a scheme adapter: its Key Vault mapping happens below MailMcp in the configuration pipeline, so MailMcp needs no code for it beyond accepting the bound value. A provider MailMcp queries itself — direct Key Vault access, HashiCorp Vault — is a scheme adapter. The two integration shapes are different and should not be conflated.

## Secret material kinds

A secret is not necessarily a password. This specification must serve at least three kinds, and the contract is shaped so a fourth does not force a redesign:

- **Text secrets** — mailbox passwords, database passwords, and API keys. Resolved material is decoded as UTF-8 and stripped of a single trailing newline, because `LoadCredential=`, Compose secrets, and Kubernetes Secret files routinely end with one and an untrailed byte would present as a wrong password.
- **Certificates** — trust anchors today, and any certificate MailMcp must present later. PEM and DER both occur in deployment; PEM is what an authority hands an operator, DER is what some tooling emits.
- **Private keys and key pairs** — a PKCS#12 / PFX bundle is binary and may itself be protected by a password, which is a second reference. PEM certificate-plus-key pairs occur equally often.

Resolution therefore yields opaque bytes, not a string. A text accessor performs the UTF-8 decode and newline trim for the first kind; a certificate loader parses the second and third. Returning a string from the resolver would make a PKCS#12 bundle unrepresentable and would corrupt DER material through encoding round-trips, so the byte form is the primitive and text is the view over it. Trimming applies only to the text view: binary material is never modified.

Loading typed material is the responsibility of the consumer that needs the type, above the resolver. The resolver knows about bytes and schemes and nothing about X.509.

The resolver is not an application-facing capability. ADR 0002 permits the configuration layer to reference secret identifiers or consume already-bound secret values at the host boundary, and explicitly forbids normalizing broad secret access into application code — an `ISecretResolver` visible to `Application` would give every use case the ability to ask for any secret by name, which is exactly that. The resolver contract and its per-scheme adapters therefore live in `Infrastructure`, and `Host` invokes them once during startup, before any hosted service begins work. Application and domain code receive only the resolved, narrowly scoped settings each operation needs, and cannot ask for anything else.

Resolution returns a result rather than throwing, because an unresolved reference is an expected configuration failure. `Host` fails fast on the first unresolved reference and lists which account and which logical secret could not be resolved.

Resolution is asynchronous and accepts a cancellation token even though every scheme implemented here reads a local file or an environment variable synchronously. A provider that reaches a network service is the expected next step, and a synchronous contract would force it to block a thread or force a breaking change through every consumer at the moment it is added. Startup validation therefore runs before hosted services start rather than inside synchronous options validation.

Resolved secret material is held in memory only for as long as the owning options instance lives, and the resolver never caches values across configuration reloads.

## Dynamic reload

Secrets reload without a process restart, on the same terms as the configuration that references them. An operator who rotates a mailbox password, replaces an expiring trust anchor, or re-issues a database credential must not have to restart MailMcp and interrupt synchronization to do it.

Two independent things can change, and both are covered:

- **The reference changes.** A configuration reload delivers a new `*Reference` value. The candidate snapshot is validated by resolving every reference in it before it is published; a snapshot containing an unresolvable reference is rejected and the last known good snapshot stays active, exactly as ADR 0002 requires for reloadable groups. A rejected reload is logged with the account, the setting, and the failure identity, never with material.
- **The material behind an unchanged reference changes.** Rotating the credential file, the systemd credential, or the vault entry leaves configuration untouched, so no configuration reload fires. Resolution therefore happens per use rather than once at startup, and the next operation that needs the secret observes the rotated value. A network-backed provider that cannot afford per-use retrieval caches inside its own adapter with its own expiry, which is why caching policy is an adapter concern rather than a contract concern.

Material is applied at operation boundaries, not mid-operation: a synchronization run that has authenticated continues with the credential it authenticated with, and the next run picks up the rotation. This is ADR 0002's "reloadable for new operations" classification, chosen over "reloadable during running operations" because swapping a credential or a trust anchor underneath an open IMAP session has no coherent meaning.

This departs from ADR 0002's current guidance, which classifies credentials and certificate trust anchors as restart-required. That guidance was written before a secret-reference indirection existed; with it, rotation no longer means mutating a bound secret value in place, but re-resolving a reference whose validity is proven before it is published. **The ADR needs an owner-approved amendment recording this, and this specification must not be implemented against an unamended ADR.** No ADR is modified as part of this specification.

## Extensibility to external secret providers

MailMcp will need more secret sources than the four schemes above — Kubernetes, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager, and comparable managed stores are all plausible. None of them is in scope here, but the shape delivered now decides whether adding one later is an adapter or a refactor. It must be an adapter.

Three consequences bind this specification:

- **A scheme is a value that an adapter declares, not a closed set the core owns.** Each adapter names the scheme it serves, and dispatch is a lookup over the registered adapters. Adding a provider registers one more adapter and changes no existing type, no consumer, and no configuration consumer. An unregistered scheme is a resolution failure, which is also how an operator learns that a provider was not compiled in or not enabled.
- **The contract is asynchronous and cancellable from the start**, for the reason given above.
- **Provider-specific concerns stay inside the provider's adapter.** Timeouts, retries, connection pooling, regional endpoints, SDK types, and any caching policy belong to the adapter that needs them. The resolver contract exposes none of it, so a provider that must cache aggressively and a provider that must never cache can coexist without the contract taking a position.

Two things are deliberately *not* built now. A managed-store adapter needs its own authentication — an Azure managed identity, a Kubernetes ServiceAccount token, a Vault role — and that platform-issued identity must come from the platform rather than from a MailMcp secret, or the design becomes circular. And every such SDK is a new dependency subject to the root `AGENTS.md` licensing, service-terms, telemetry, and data-processing review, with a `LICENSES.md` entry in the same change set. Neither is prejudged here.

Note that container and Kubernetes deployments need no new scheme at all. Docker and Podman Compose mount secrets as files under `/run/secrets/<name>`, and Kubernetes mounts a Secret as a read-only tmpfs directory of one file per key at an operator-chosen path; both are addressed by `file:`, and a Kubernetes Secret exposed as an environment variable is addressed by `env:`. A `docker-secret:` or `kubernetes-secret:` scheme would perform exactly the file read that `file:` already performs, so neither is added. Only a provider with genuinely different retrieval behavior earns a scheme.

## Trusted certificate authority material

Specification 01 requires that certificate validation is never disabled and that private or self-signed servers are supported by configuring additional trusted certificate authorities. Nothing else in the roadmap owns loading that material, and it arrives through exactly the mechanism this specification builds — a deployment-provisioned file or credential — so it is assigned here.

A trusted certificate authority is configured as a reference in the same form as any other deployment-provisioned material. `Infrastructure` loads it, validates that it parses as a certificate and is usable as a trust anchor, and supplies it to the MailKit adapter's certificate validation path so a private server chains to it while ordinary validation stays enabled for everything else. A malformed or unreadable trust anchor fails startup, since silently continuing would either reject a working server or, worse, invite an operator to disable validation to work around it.

## Secret material in memory

Resolved material must live as briefly as possible and be erased when it stops being needed. Four rules follow from current .NET guidance, and one common approach is explicitly rejected.

**`SecureString` is not used.** Microsoft's own documentation says "We recommend that you don't use the `SecureString` class for new development on .NET (Core)", and states that because of platform dependencies "`SecureString` does not encrypt the internal storage on non-Windows platform" — which is every environment MailMcp targets. The same documentation names the recommended alternative: "use an opaque handle to credentials that are stored outside of the process." That is precisely what a secret reference already is, so this specification's core design *is* the sanctioned approach and `SecureString` would add ceremony without protection.

**Secret material is never held in a `string`.** A `string` is immutable, so it cannot be overwritten; it cannot be scheduled for deletion; and because its memory is not pinned, the garbage collector makes additional copies when it moves and compacts memory, each of which outlives any attempt to erase the original. Material is therefore held in a byte buffer, which is the other reason resolution is byte-oriented.

**The buffer is pinned and zeroed.** Material is allocated with `GC.AllocateArray<byte>(length, pinned: true)` so the collector cannot relocate it and leave an un-erased copy behind, and erased with `CryptographicOperations.ZeroMemory`, which exists — in its own documented words — "to future-proof against potential optimizations in the .NET runtime that could eliminate memory writes that aren't followed by memory reads." A plain loop assigning zeroes carries no such guarantee. Pooled buffers are not used for secret material at all, because a returned buffer that was not cleared hands the material to the next unrelated caller.

**Resolved material is owned and disposed.** A resolved secret is disposable, is owned by the operation that resolved it, and is erased when that operation ends. This composes with per-use resolution: material exists for the length of one synchronization run or one connection attempt rather than for the process lifetime, so the window in which a dump could contain it is bounded by an operation rather than by uptime. Because each operation owns its own instance, a configuration reload never erases material an in-flight operation is still using.

Two exposures are accepted and must be documented rather than hidden. Some framework contracts take a `string` — the IMAP client's authentication call and the database connection string among them — so a short-lived `string` copy is unavoidable at exactly those call sites; it is created as late as possible, at the boundary itself, and never stored, logged, or passed on. And managed memory remains readable through a process dump, a debugger, or swap. Those are operational controls rather than code: the deployment must disable core dumps for the service and keep its memory out of swap, and the operations documentation must say so. Locking pages with `mlock` is deliberately not attempted — it would require P/Invoke plus elevated capability in every deployment shape, against a repository rule that restricts unsafe and platform-invoke code to measured need, and it does not address dumps or debuggers anyway.

## Safety and privacy

A resolution failure message names the account identifier, the logical secret name, and the scheme, and never the reference target path, the environment variable value, or any part of the resolved secret. Resolved secrets are excluded from structured logging by construction: the options type exposes them through a dedicated accessor rather than an ordinary public property, so a future serializer or diagnostic dump cannot pick them up incidentally. An inline or `plaintext:` value outside the default `ReferenceOnly` mode is logged at startup by setting name so it cannot pass unnoticed.

## Testing

`Infrastructure.UnitTests` cover each scheme adapter against an in-memory abstraction over the credential directory and file system, since unit tests must not touch the real file system. Tests assert the unknown-scheme failure, the missing-reference failure, each of the three interpretation modes including a bare value failing under `ReferenceOnly` and resolving under `ReferenceOrInline`, an unparsed value under `InlineOnly`, the composite dispatch, and that failure results carry no secret material. A test registers a scheme adapter that exists only in the test project and asserts it resolves through the same dispatch, which is what proves a future provider is an adapter rather than a refactor. Material-kind tests cover the UTF-8 text view with its newline trim, binary material surviving resolution byte-for-byte, and a PEM and a DER certificate both loading. Memory-hygiene tests cover a disposed secret no longer yielding its material, disposal being idempotent, and no accessor returning a `string` except the documented framework-boundary one. Reload tests cover a rotated reference being adopted, a candidate snapshot with an unresolvable reference being rejected while the previous secrets stay active, and rotated material behind an unchanged reference being observed by the next operation but not mid-operation. Trust-anchor tests cover a valid certificate being installed into the validation path, a malformed one failing startup, and the absence of any configuration path that disables validation. An architecture test asserts that no secret-resolution type is reachable from `Application` or `Domain`.

## Delivery size

`specs/README.md` scopes each specification to roughly 1000 changed lines. Adding certificate and private-key material kinds and dynamic reload pushes this past that ceiling, so the specification is a candidate for splitting into:

- **02a — reference resolution and text secrets:** the grammar, the four schemes, the byte-oriented contract, mailbox and database passwords, fail-fast startup validation.
- **02b — certificate material and dynamic reload:** trust anchor loading into the MailKit validation path, PKCS#12 and PEM key material, and the reload behavior with last-known-good rejection.

The split is clean because 02b consumes 02a's contract without changing it, and because 02a alone already satisfies the original goal of keeping passwords out of `appsettings.json`. It is not applied here: renumbering interacts with the roadmap board and the dependency chain of specifications 03 onward, and that is the owner's call. Implementation should not begin until it is made.

## Out of scope

Data Protection key-ring provisioning and encrypted secret storage in PostgreSQL. Client certificates presented by MCP clients are stage 9 work and unrelated to mail transport trust anchors; this specification delivers the byte-oriented resolution and certificate loading they will reuse, but presents no client certificate itself.

Secret rotation without restart is explicitly *in* scope — see "Dynamic reload" above. It was listed as out of scope in an earlier revision of this specification.

Adapters for external managed secret stores — Kubernetes, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager, and comparable services — are out of scope as implementations, but their eventual addition is a design constraint on this specification rather than a later concern. "Extensibility to external secret providers" above states what that constrains; the adapters themselves, their SDK dependencies, and their platform-identity requirements are separate work under their own licensing and service-terms review.

## Definition of done

- No options type in the repository exposes a raw password bound directly from configuration.
- A missing or malformed reference fails startup with a message that identifies the account without disclosing the secret.
- No secret-resolution contract is reachable from `Application` or `Domain`.
- Under the default `ReferenceOnly` mode every secret-bearing setting is a `*Reference` string and a bare value fails startup; the inline modes are reachable only by an explicit, logged configuration choice.
- A configuration provider that pre-resolves secrets, such as Azure App Configuration with Key Vault references, works without a scheme adapter and without code changes.
- Resolution yields bytes, so a PKCS#12 bundle or DER certificate is representable without encoding damage, and text secrets are decoded and newline-trimmed only in the text view.
- A new scheme can be added by registering one adapter, without editing the dispatch, an existing adapter, or any consumer.
- The resolution contract is asynchronous and cancellable, so a network-backed provider needs no breaking change.
- Rotating the material behind an unchanged reference is observed by the next operation without a process restart.
- A configuration reload carrying an unresolvable reference is rejected and leaves the previous secrets active.
- ADR 0002 has been amended, with owner approval, to classify referenced secrets as reloadable for new operations.
- No secret material is held in a `string`, a pooled buffer, or a `SecureString`; buffers are pinned and zeroed with `CryptographicOperations.ZeroMemory` when their owning operation ends.
- `docs/operations/` documents disabling core dumps and swap exposure for the service, in both the systemd and container deployment shapes.
- A private server with a configured trust anchor connects with certificate validation fully enabled.
- `docs/operations/local-development.md` documents the Development workflow and `docs/operations/` gains a page describing the systemd credential deployment path alongside the container path.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
