# Secret Reference Resolution

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 2
**Depends on:** 01
**Estimated change size:** ~950 lines including tests and documentation

## Goal

Implement the secret handling model from draft section 7.3 so that no mailbox password, database password, or certificate password is ever written into `appsettings.json`, and so that an unresolved secret reference fails startup instead of producing a confusing authentication error later.

This specification builds the resolution mechanism and applies it to text secrets. Specification 02b consumes the same contract for certificate material and adds rotation without restart.

## Current state

`MailSynchronizationAccountOptions` holds a plain `Password` string bound directly from configuration. The default `appsettings.json` ships an empty account list, so nothing is leaked today, but the shape invites operators to commit credentials.

Specification 01 shipped `MailAccountTransportSecurityOptions.TrustedCertificateAuthorityReference` as a nullable string, already documented as carrying a reference rather than certificate material. This specification renames it to `TrustedCertificateAuthority` and changes its shape to the secret block defined below. The `Reference` suffix existed because the value *was* the reference string; once the value is a block whose `SecretReference` property holds it, the suffix says the same word twice and the setting reads as the thing it configures. Loading the material behind it belongs to specification 02b; this specification only fixes its configuration shape, so that the block convention holds everywhere from the moment it exists rather than for new settings only.

This is a deliberate break taken now, while the setting has one consumer and no shipped release depends on it. Deferring it would mean either two spellings of a secret setting in the same file or the same break later against real deployments. `MailTransportSecurityPolicy` in `Domain` continues to receive a nullable string and is unaffected — the block is a configuration-adapter shape and does not cross the boundary.

## Approved scope

Configuration values that carry secrets become secret references rather than secret values. A reference is a string with an explicit scheme, `<scheme>:<target>`, split on the first colon so a path or URL in the target survives intact. This release implements four schemes:

- `systemd-credential:<name>` reads from the runtime credentials directory that systemd exposes to the service.
- `file:<path>` reads a deployment-provisioned protected file.
- `env:<variable>` reads an environment variable, permitted for non-production automation.
- `plaintext:<value>` names a literal value inline. It is not restricted to Development; see "Inline values and pre-resolving configuration providers" for the mode that governs it.

The scheme set is open by design; see "Extensibility to external secret providers" below. The grammar is fixed, the set of schemes that satisfy it is not.

Configuration carries the reference and nothing else. Every secret-bearing setting is a JSON *object* whose `SecretReference` property carries the reference the host resolves, never a bare string:

```json
{
  "MailSynchronization": {
    "Accounts": [
      {
        "AccountId": "primary",
        "Host": "imap.example.test",
        "UserName": "mailmcp@example.test",
        "Secrets": {
          "Password": { "SecretReference": "systemd-credential:imap-primary-password" }
        },
        "TransportSecurity": {
          "CertificateTrust": "AdditionalTrustedAuthority",
          "TrustedCertificateAuthority": { "SecretReference": "file:/run/secrets/private-ca.pem" }
        }
      }
    ]
  },
  "Persistence": {
    "Password": { "SecretReference": "file:/run/secrets/postgres-password" }
  }
}
```

A setting that names a reference is inert on its own: nothing in the file discloses a secret, and a file leaked from a backup or a repository yields only credential names and paths.

## The secret block

Every secret-bearing setting uses the same object shape, whether the secret is a password, a trust anchor, or a key. Two reasons make the object the right unit rather than a bare string.

**It has room to grow without a breaking configuration change.** A secret is not always one opaque value. A PKCS#12 bundle carries its own password, which is a second reference; a certificate may need an explicit format hint when the extension lies about its encoding; a future managed-store reference may need a version pin. Each of those is a sibling property inside a block that already exists:

```json
"ClientCertificate": {
  "SecretReference": "file:/run/secrets/client.pfx",
  "Password": { "SecretReference": "systemd-credential:client-certificate-password" }
}
```

Had the setting shipped as a bare string, adding the bundle password would change the setting's JSON type from string to object, which breaks every deployment that already configured it. Only the first such setting is free; the block makes all of them free.

One sibling is defined now: an optional nested `Password` block, itself a secret block. Specification 02b needs it for a password-protected PKCS#12 bundle, and a sibling *string* would not do — the discovery walk finds settings by the block type, so a string would be invisible to binding, validation, resolution, and erasure alike. Defining it here keeps the recursion honest and keeps 02b from having to change a contract this specification already shipped.

**The block's type is what marks a setting as secret-bearing.** A secret block binds to a dedicated options type rather than to `string`, so a setting is secret-bearing because of what it *is*, not because someone remembered to annotate it. A marker attribute can be omitted on a newly added property and nothing would notice; a type cannot, because the property would not bind. Startup validation therefore discovers secret-bearing settings by walking the bound options graph for that type, and every rule below applies to every such setting automatically, including settings added after this specification ships.

This is what makes the `ReferenceOnly` guarantee complete rather than per-setting. Under the default mode a secret block whose `SecretReference` is not a well-formed `<scheme>:<target>` fails startup, naming the configuration path and the failure identity. A value that is merely text — a password pasted where a reference belongs, or a mistyped `fil:/run/secrets/imap` — is a startup failure, never a secret that resolved by accident. There is no path by which an unmarked setting quietly skips the check, because there is no unmarked secret-bearing setting.

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

### The block must survive a flattening provider

The secret block is a nested object in JSON, but nothing about it requires a JSON provider. Every hierarchical configuration provider addresses the same setting by its colon-separated path, so the block adds one path segment and nothing else. This specification is not complete until an Azure App Configuration store bound with Key Vault references works against it, which means the following must hold:

- The App Config key is the full path, `MailSynchronization:Accounts:0:Secrets:Password:SecretReference`, and its value is the Key Vault reference the provider resolves before MailMcp binds it.
- Environment variables address the same setting as `MailSynchronization__Accounts__0__Secrets__Password__SecretReference`, so a container platform that injects configuration through the environment reaches the block without a JSON file.
- Binding is verified against an in-memory provider populated with flat colon-separated keys, not only against a JSON document, so a regression that makes the block depend on JSON document structure fails a test rather than a deployment.

Combined with `InlineOnly`, this is the complete Azure App Configuration path: the store holds the key, Key Vault holds the secret, the provider maps one to the other, and MailMcp binds an already-resolved value into `SecretReference` and uses it as material.

## Secret material kinds

A secret is not necessarily a password. The contract is shaped so that certificates and key bundles — specification 02b's subject — need no change to it:

- **Text secrets** — mailbox passwords, database passwords, and API keys. Resolved material is decoded as UTF-8 and stripped of a single trailing newline, because `LoadCredential=`, Compose secrets, and Kubernetes Secret files routinely end with one and an untrimmed byte would present as a wrong password.
- **Certificates and key bundles** — a PKCS#12 bundle is binary, and DER-encoded material is not text at all. Specification 02b loads them; this specification guarantees they arrive intact.

Reading material is bounded. A `file:` target can name a log, a database file, or a device-backed pseudo-file by mistake, and reading it whole would exhaust memory or stall the host before validation completes — at startup and again per use. Every read therefore enforces an explicit maximum size while reading, before an owned buffer is allocated, and an oversized target is a named resolution failure rather than an allocation.

A successful resolution records whether its material came from a scheme adapter or was accepted inline. Two things depend on that distinction and neither can be derived from the interpretation mode: naming the settings that resolved inline in the startup log, and specification 02b accepting a binary certificate read through `file:` while rejecting the same bytes supplied inline.

Resolution therefore yields opaque bytes, not a string. A text accessor performs the UTF-8 decode and newline trim for the first kind. Returning a string from the resolver would make a PKCS#12 bundle unrepresentable and would corrupt DER material through encoding round-trips, so the byte form is the primitive and text is the view over it. Trimming applies only to the text view: binary material is never modified.

Loading typed material is the responsibility of the consumer that needs the type, above the resolver. The resolver knows about bytes and schemes and nothing about X.509.

The resolver is not an application-facing capability. ADR 0002 permits the configuration layer to reference secret identifiers or consume already-bound secret values at the host boundary, and explicitly forbids normalizing broad secret access into application code — an `ISecretResolver` visible to `Application` would give every use case the ability to ask for any secret by name, which is exactly that. The resolver contract and its per-scheme adapters therefore live in `Infrastructure`, and `Host` invokes them once during startup, before any hosted service begins work. Application and domain code receive only the resolved, narrowly scoped settings each operation needs, and cannot ask for anything else.

Resolution returns a result rather than throwing, because an unresolved reference is an expected configuration failure. `Host` fails fast and reports every unresolved reference at once, each named by its configuration path.

Resolution is asynchronous and accepts a cancellation token even though every scheme implemented here reads a local file or an environment variable synchronously. A provider that reaches a network service is the expected next step, and a synchronous contract would force it to block a thread or force a breaking change through every consumer at the moment it is added. Startup validation therefore runs before hosted services start rather than inside synchronous options validation.

## Extensibility to external secret providers

MailMcp will need more secret sources than the four schemes above — Kubernetes, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager, and comparable managed stores are all plausible. None of them is in scope here, but the shape delivered now decides whether adding one later is an adapter or a refactor. It must be an adapter.

Three consequences bind this specification:

- **A scheme is a value that an adapter declares, not a closed set the core owns.** Each adapter names the scheme it serves, and dispatch is a lookup over the registered adapters. Adding a provider registers one more adapter and changes no existing type, no consumer, and no configuration consumer. An unregistered scheme is a resolution failure, which is also how an operator learns that a provider was not compiled in or not enabled.
- **The contract is asynchronous and cancellable from the start**, for the reason given above.
- **Provider-specific concerns stay inside the provider's adapter.** Timeouts, retries, connection pooling, regional endpoints, SDK types, and any caching policy belong to the adapter that needs them. The resolver contract exposes none of it, so a provider that must cache aggressively and a provider that must never cache can coexist without the contract taking a position.

Two things are deliberately *not* built now. A managed-store adapter needs its own authentication — an Azure managed identity, a Kubernetes ServiceAccount token, a Vault role — and that platform-issued identity must come from the platform rather than from a MailMcp secret, or the design becomes circular. And every such SDK is a new dependency subject to the root `AGENTS.md` licensing, service-terms, telemetry, and data-processing review, with a `LICENSES.md` entry in the same change set. Neither is prejudged here.

Note that container and Kubernetes deployments need no new scheme at all. Docker and Podman Compose mount secrets as files under `/run/secrets/<name>`, and Kubernetes mounts a Secret as a read-only tmpfs directory of one file per key at an operator-chosen path; both are addressed by `file:`, and a Kubernetes Secret exposed as an environment variable is addressed by `env:`. A `docker-secret:` or `kubernetes-secret:` scheme would perform exactly the file read that `file:` already performs, so neither is added. Only a provider with genuinely different retrieval behavior earns a scheme.

## Secret material in memory

Resolved material must live as briefly as possible and be erased when it stops being needed. Four rules follow from current .NET guidance, and one common approach is explicitly rejected.

**`SecureString` is not used.** Microsoft's own documentation says "We recommend that you don't use the `SecureString` class for new development on .NET (Core)", and states that because of platform dependencies "`SecureString` does not encrypt the internal storage on non-Windows platform" — which is every environment MailMcp targets. The same documentation names the recommended alternative: "use an opaque handle to credentials that are stored outside of the process." That is precisely what a secret reference already is, so this specification's core design *is* the sanctioned approach and `SecureString` would add ceremony without protection.

**Secret material MailMcp allocates is never held in a `string`.** A `string` is immutable, so it cannot be overwritten; it cannot be scheduled for deletion; and because its memory is not pinned, the garbage collector makes additional copies when it moves and compacts memory, each of which outlives any attempt to erase the original. Material is therefore held in a byte buffer, which is the other reason resolution is byte-oriented.

**The buffer is pinned and zeroed.** Material is allocated with `GC.AllocateArray<byte>(length, pinned: true)` so the collector cannot relocate it and leave an un-erased copy behind, and erased with `CryptographicOperations.ZeroMemory`, which exists — in its own documented words — "to future-proof against potential optimizations in the .NET runtime that could eliminate memory writes that aren't followed by memory reads." A plain loop assigning zeroes carries no such guarantee. Pooled buffers are not used for secret material at all, because a returned buffer that was not cleared hands the material to the next unrelated caller.

**Resolved material is owned and disposed.** A resolved secret is disposable, is owned by the operation that resolved it, and is erased when that operation ends. Material exists for the length of one synchronization run or one connection attempt rather than for the process lifetime, so the window in which a dump could contain it is bounded by an operation rather than by uptime. Because each operation owns its own instance, the reload behavior specification 02b adds can publish new material without erasing what an in-flight operation is still using.

Three exposures are accepted and must be documented rather than hidden. `env:` is the first: `Environment.GetEnvironmentVariable` returns a `string`, so an environment-sourced secret arrives already un-erasable and no amount of care downstream changes that. It is a further reason the documentation recommends against `env:` outside non-production automation, and it is why the guarantee above is scoped to material MailMcp allocates rather than material the platform hands it. An inline value under the two inline modes has the same property, for the same reason.

Second, some framework contracts take a `string` — the IMAP client's authentication call and the database connection string among them — so a short-lived `string` copy is unavoidable at exactly those call sites; it is created as late as possible, at the boundary itself, and never stored, logged, or passed on. Third, managed memory remains readable through a process dump, a debugger, or swap. Those are operational controls rather than code: the deployment must disable core dumps for the service and keep its memory out of swap, and the operations documentation must say so. Locking pages with `mlock` is deliberately not attempted — it would require P/Invoke plus elevated capability in every deployment shape, against a repository rule that restricts unsafe and platform-invoke code to measured need, and it does not address dumps or debuggers anyway.

## Safety and privacy

A resolution failure message names the configuration path, the logical secret name, and the scheme, and never the reference target path, the environment variable value, or any part of the resolved secret. Resolved secrets are excluded from structured logging by construction: the resolved type exposes material through a dedicated accessor rather than an ordinary public property, so a future serializer or diagnostic dump cannot pick it up incidentally. An inline or `plaintext:` value outside the default `ReferenceOnly` mode is logged at startup by setting name so it cannot pass unnoticed.

## Testing

`Infrastructure.UnitTests` cover each scheme adapter against an in-memory abstraction over the credential directory and file system, since unit tests must not touch the real file system. Tests assert the unknown-scheme failure, the missing-reference failure, each of the three interpretation modes including a bare value failing under `ReferenceOnly` and resolving under `ReferenceOrInline`, an unparsed value under `InlineOnly`, the composite dispatch, and that failure results carry no secret material. A test registers a scheme adapter that exists only in the test project and asserts it resolves through the same dispatch, which is what proves a future provider is an adapter rather than a refactor.

Secret-block tests bind the shipped options types from an in-memory configuration provider populated with flat colon-separated keys, and assert that a plain-text value in a block fails startup under `ReferenceOnly` while naming its configuration path, that an empty or absent `SecretReference` is reported as a missing reference rather than as an empty secret, that a block nested in a list reports its index in the path, and that a sibling property added to a block leaves already-configured settings binding unchanged. Binding from flat keys rather than only from a JSON document is what keeps the Azure App Configuration and environment-variable paths verified.

Material tests cover the UTF-8 text view with its newline trim and binary material surviving resolution byte-for-byte. Memory-hygiene tests cover a disposed secret no longer yielding its material, disposal being idempotent, and no accessor returning a `string` except the documented framework-boundary one.

An architecture test asserts that no secret-resolution type is reachable from `Application` or `Domain`, and a second one enumerates the bound options graph and fails if any property outside the secret block type carries a name suggesting it holds a secret, so a future raw `Password` string cannot be added silently.

## Out of scope

Certificate and private-key material loading, and secret rotation without a process restart, are specification 02b. This specification delivers the byte-oriented contract and the configuration shape both depend on, and renames `TrustedCertificateAuthority` to its block form, but loads nothing from it.

Data Protection key-ring provisioning and encrypted secret storage in PostgreSQL. Client certificates presented by MCP clients are stage 9 work.

Adapters for external managed secret stores — Kubernetes, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager, and comparable services — are out of scope as implementations, but their eventual addition is a design constraint on this specification rather than a later concern. "Extensibility to external secret providers" above states what that constrains; the adapters themselves, their SDK dependencies, and their platform-identity requirements are separate work under their own licensing and service-terms review.

## Definition of done

- No options type in the repository exposes a raw password bound directly from configuration.
- Every secret-bearing setting binds to the secret block type, so no configuration path carries a secret as a bare string and no setting can be secret-bearing without being subject to the rules below.
- Under the default `ReferenceOnly` mode a secret block whose `SecretReference` is not a well-formed `<scheme>:<target>` fails startup naming the configuration path; a plain-text value is never accepted as the secret. The inline modes are reachable only by an explicit, logged configuration choice.
- Every unresolved reference is reported at once, each named by its configuration path, without disclosing the target or the material.
- Adding a second property to a secret block — a bundle password, a format hint — requires no change to the JSON type of any already-configured setting.
- A secret block binds correctly from flat colon-separated keys, so a pre-resolving provider such as Azure App Configuration with Key Vault references works without a scheme adapter and without code changes.
- No secret-resolution contract is reachable from `Application` or `Domain`.
- Resolution yields bytes, so a PKCS#12 bundle or DER certificate is representable without encoding damage, and text secrets are decoded and newline-trimmed only in the text view.
- A new scheme can be added by registering one adapter, without editing the dispatch, an existing adapter, or any consumer.
- The resolution contract is asynchronous and cancellable, so a network-backed provider needs no breaking change.
- No secret material that MailMcp allocates is held in a `string`, a pooled buffer, or a `SecureString`; buffers are pinned and zeroed with `CryptographicOperations.ZeroMemory` when their owning operation ends, and no intermediate read buffer survives un-erased.
- The residual exposures — `env:`, inline values, the two framework `string` boundaries, and process memory itself — are documented for the operator rather than implied away.
- Reading material enforces an explicit maximum size, so a mistaken reference to a large file is a named failure rather than an allocation.
- A successful resolution records whether its material came from an adapter or was accepted inline.
- Reading a secret is asynchronous and honours the caller's cancellation token end to end, including at the file-system boundary.
- `docs/operations/` documents disabling core dumps and swap exposure for the service, in both the systemd and container deployment shapes.
- `docs/operations/local-development.md` documents the Development workflow and `docs/operations/` gains a page describing the systemd credential deployment path alongside the container path.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
