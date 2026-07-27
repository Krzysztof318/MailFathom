# Secret Reference Resolution Implementation Plan (02a)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Governing issue:** [#36 — Spec 02a — Secret reference resolution](https://github.com/Krzysztof318/MailMcp/issues/36)
**Governing specification:** [`specs/02a-secret-reference-resolution.md`](../../../specs/02a-secret-reference-resolution.md)
**Followed by:** [`docs/superpowers/plans/2026-07-27-certificate-material-and-secret-rotation-02b.md`](2026-07-27-certificate-material-and-secret-rotation-02b.md), which consumes this contract for certificate material and rotation
**Architectural context:** `specs/2026-07-22-mail-mcp-architecture-draft.md` sections 7.3 and 19, ADR `docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md`
**Depends on:** [#35 — Spec 01](https://github.com/Krzysztof318/MailMcp/issues/35), merged as `a732e9a`

**Goal:** Replace every raw secret bound from configuration with a scheme-qualified reference that `Infrastructure` resolves and `Host` fails fast on, and give every secret-bearing setting one uniform block shape so the guarantee holds for settings that do not exist yet.

**Architecture:** `Infrastructure` owns the reference grammar, the secret block and its discovery, the per-scheme adapters, and the composite dispatch. `Host` binds secret blocks, invokes resolution once before hosted services start, and turns failures into startup errors. `Application` and `Domain` gain nothing: they never see a resolver, a reference, or a scheme, which is the boundary ADR 0002 draws when it forbids normalizing broad secret access into application code.

The shape is chosen so that a future Kubernetes, Azure Key Vault, HashiCorp Vault, or AWS Secrets Manager adapter is a registration rather than a refactor: a scheme is declared by the adapter that serves it, dispatch is a lookup, and the contract is asynchronous and cancellable from the first commit. None of those providers is implemented here.

**Tech Stack:** .NET 10, C# preview, MailKit 4.17.0, Npgsql 10.0.3 (transitive through `Npgsql.EntityFrameworkCore.PostgreSQL`), xUnit.net v3 on Microsoft Testing Platform v2, NSubstitute 6.0.0.

## Global Constraints

- No new third-party packages. `LICENSES.md` therefore needs no dependency entry; `$check-docs-licenses` returns `n/a` for licensing and must still be run.
- `Domain` and `Application` stay free of every type introduced here. The architecture test in Task 6 is the enforcement, not a review habit.
- Certificate validation stays enabled unconditionally. Nothing introduced here may expose a "trust any certificate" flag, an `SslProtocols` override, or a callback that returns `true` on an unexamined chain.
- No resolution failure message, log line, or exception may contain the resolved material, the file path, the environment variable's value, or the credential's contents. The account identifier, the logical secret name, and the scheme are the permitted vocabulary.
- Unit tests never touch the real file system, environment block, network, or clock. Every scheme adapter reads through an injected in-memory port.
- Every enum member gets an explicit contiguous value starting at `0`; members are appended, never reordered or renumbered.
- Synchronization stays read-only: nothing here may set the remote `\Seen` flag.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` must pass the 85% whole-scope gate. `Host` is excluded from coverage, so logic that needs testing belongs in `Infrastructure` — the same constraint that moved `MailAccountTransportSecurityOptions` there in specification 01.

## Deployment shapes this must serve

MailMcp runs both as a container (Docker Compose or rootless Podman Compose under systemd) and as a native systemd service, and draft section 19 treats neither as the fallback. The scheme set covers both without a container-specific scheme, because a container secret *is* a file:

| Deployment | Provisioning | Reference |
| --- | --- | --- |
| Native systemd service | `LoadCredential=` / `LoadCredentialEncrypted=`, `systemd-creds` | `systemd-credential:imap-primary-password` |
| Docker / Podman Compose | Compose `secrets:`, mounted at `/run/secrets/<name>` | `file:/run/secrets/imap-primary-password` |
| Kubernetes | Secret mounted as a read-only tmpfs volume, one file per key | `file:/etc/mailmcp-secrets/imap-primary-password` |
| Non-production automation | CI or orchestrator environment block | `env:MAILMCP_IMAP_PRIMARY_PASSWORD` |
| Local development | `appsettings.Development.json` or user secrets | `plaintext:dev-password` |

Adding `docker-secret:` or `kubernetes-secret:` schemes was rejected: each would resolve to exactly the file read that `file:` already performs, and a scheme whose only distinction is documentation is configuration surface with no behavior behind it. A Kubernetes Secret projected into the environment block is `env:` for the same reason.

Two integration shapes exist and must not be conflated. A provider that **pre-resolves** — Azure App Configuration with Key Vault references — does its mapping below MailMcp in the configuration pipeline, so the bound value is already the secret and MailMcp needs no adapter at all, only the `InlineOnly` mode of decision 13. A store MailMcp **queries itself** — direct Key Vault, HashiCorp Vault, AWS Secrets Manager — is the case that earns a scheme, because retrieval behavior genuinely differs: authentication, endpoint, timeout, retry, and caching. Nothing here implements one, but decisions 1, 2, and 19 exist so that adding one later registers an adapter and edits nothing else.

## Design decisions locked before implementation

1. **The reference grammar is `<scheme>:<target>`, split on the first colon only.** A Windows-style path or a URL in the target therefore survives untouched — the latter matters for a future `azure-key-vault:https://vault.example/secrets/imap` style target. Under the default `ReferenceOnly` interpretation mode an empty scheme or an empty target is a resolution failure, never a silent fallback to treating the whole string as a literal — that fallback is exactly the leak the specification exists to prevent, and it is reachable only by the explicit opt-in in decision 13. Parsing validates the *grammar* only; whether a scheme is supported is a dispatch question, answered by decision 2.
2. **A scheme is a value an adapter declares, not a closed enum the core owns.** `SecretReferenceScheme` is a `sealed record` wrapping its normalized wire name, with well-known static members for the four schemes shipped here. Each adapter exposes the scheme it serves, and `CompositeSecretReferenceResolver` builds its dispatch from the registered adapter set, so adding Azure Key Vault or Vault later registers one adapter and edits no existing type. This mirrors specification 01's accepted reversal of `MailAuthenticationMechanism` from an enum plus mapping table to a value carrying its own registered name. It is a `record` rather than a `readonly record struct` because `default(T)` would otherwise produce a scheme with a null name, and unlike the SASL mechanism this value is parsed once per resolution and never sits on a hot path.
3. **Resolution returns a result; it does not throw.** An unresolved reference is an expected configuration failure with a named cause, per the specification. The failure enum is justified here because `Host` acts on it by rendering a startup message; nothing above `Host` branches on it. `SchemeNotSupported` is reported by the dispatch, which is also how an operator learns a provider adapter was not registered.
4. **The contract is asynchronous and cancellable from the first commit.** Every scheme implemented here reads a local file or an environment variable and would be happy synchronously. A managed-store adapter is a network call, and the root rules forbid blocking on it; retrofitting `ResolveAsync` later would break `IImapAccountSettingsProvider`, the session factory, and startup validation at once. Paying one `Task` allocation per resolution now is cheaper than that change, and resolutions happen once per synchronization run.
5. **Startup validation runs in `IHostedLifecycleService.StartingAsync`, not in `IValidateOptions`.** Options validation is synchronous, so an async resolver cannot run inside it without blocking — which decision 4 exists to avoid. `StartingAsync` is documented to run before any hosted service's `StartAsync`, so a throw there fails the host before the synchronization worker or any endpoint starts, which is the fail-fast behavior the specification requires. Structural options validation stays in `ValidateOnStart` where it belongs; only secret resolution moves.
6. **The resolver returns bytes; text is a view over them.** A secret is not always a password: a trust anchor may be PEM or DER, and a private key arrives as a PKCS#12 bundle that is binary and may carry its own password reference. A `string`-returning resolver would make PKCS#12 unrepresentable and would corrupt DER through encoding round-trips. `ResolvedSecret` therefore stores a pinned `byte[]` (decision 7) and exposes it as `ReadOnlySpan<byte>`, which is what the root rules prescribe for byte payloads. `RevealBytes()` returns the material untouched; `RevealAsString()` returns the UTF-8 text view with one trailing newline stripped. The trim belongs to the text view only — trimming a PFX would corrupt it. `plaintext:` and `env:` UTF-8-encode on the way in, which costs nothing and keeps one result shape.
7. **Secret material is a pinned byte buffer that is zeroed on dispose; it is never a `string` and never a `SecureString`.** Microsoft's guidance is explicit on all three points. `SecureString` is not recommended for new development on .NET, and it "does not encrypt the internal storage on non-Windows platform" — every MailMcp deployment target. A `string` is immutable, cannot be scheduled for deletion, and because its memory is not pinned "the garbage collector will make additional copies of `String` values when moving and compacting memory", so erasing one is not even well defined. The buffer is therefore allocated with `GC.AllocateArray<byte>(length, pinned: true)`, which the collector cannot relocate, and erased with `CryptographicOperations.ZeroMemory`, documented as existing "to future-proof against potential optimizations in the .NET runtime that could eliminate memory writes that aren't followed by memory reads" — a hand-written zeroing loop carries no such guarantee. `ArrayPool` is not used for secret material at all: a buffer returned uncleared hands the material to the next unrelated caller.
8. **`RevealAsString()` exists, is the exception, and is named to look like one.** MailKit's `AuthenticateAsync(string, string)` and Npgsql's connection-string password are framework contracts that take a `string`, so a copy that cannot be erased is unavoidable at exactly those two call sites. `RevealBytes()` is the primary accessor and everything else uses it. `RevealAsString()` is called at the boundary itself, as late as possible, and its result is never stored, logged, or passed on. Its XML documentation states that the returned string cannot be erased and will persist until the collector reclaims it.
9. **A resolved secret is owned by the operation that resolved it and disposed when that operation ends.** Combined with per-use resolution this bounds the exposure window to one synchronization run or one connection attempt rather than to process uptime. It also removes a lifetime hazard from dynamic reload: because every operation owns its own instance, publishing a new configuration snapshot never disposes material that an in-flight operation is still reading. `MailAccountSecrets` therefore implements `IDisposable` and disposes the secrets it owns; specification 02b adds the resolved trust anchor to the same disposal path, since `X509Certificate2` is itself disposable.
10. **The residual exposures are documented, not papered over.** Managed memory is still readable through a process dump, a debugger, or swap, and no code-level measure changes that. The mitigations are operational and belong in the operations documentation: `LimitCORE=0` on the systemd unit and `Storage=none` / `ProcessSizeMax=0` for `systemd-coredump`, plus keeping the service's memory out of swap in both the systemd and container shapes. `mlock` is deliberately rejected — it needs P/Invoke plus `CAP_IPC_LOCK` and a raised `RLIMIT_MEMLOCK` in every deployment shape, against the repository rule restricting unsafe and platform-invoke code to measured need, and it does not address dumps or debuggers anyway.
11. **Typed material is loaded above the resolver, not inside it.** — *moved to specification 02b.* Recorded there, keeping this number so cross-references from either plan resolve to the same decision.
12. **`ResolvedSecret` hides its material behind a method, not a property.** `System.Text.Json`, `record` synthesized `ToString()`, and most diagnostic dumps enumerate properties, so a property named `Value` would eventually be serialized by something. `ImapAccountSettings` is a `record`, which makes this concrete: its synthesized `ToString()` prints every member today. `RevealBytes()` and `RevealAsString()` are methods for that reason, and `ToString()` is overridden to return `***`.
13. **Interpretation is an explicit three-valued mode, not an environment gate.** `SecretValueInterpretation` is `ReferenceOnly = 0` (default), `ReferenceOrInline = 1`, `InlineOnly = 2`. This replaces the earlier "`plaintext:` only in Development" rule, which turned out to be both too strict and too weak: too strict because an operator may knowingly put a secret in JSON and that is their call, and too weak because it does not address the real driver — a configuration provider that has already resolved the secret before MailMcp binds it. Azure App Configuration with Key Vault references is exactly that: the provider substitutes the vault value, so the bound setting is the raw secret with no prefix MailMcp could recognize. `InlineOnly` serves it by parsing nothing at all, which removes the ambiguity rather than guessing at it. The mode is passed to the composite resolver at registration, so `Infrastructure` still needs no reference to `Microsoft.Extensions.Hosting.Abstractions` and the rule stays trivially testable in all three directions.
14. **Inline modes are logged, and their memory cost is stated.** Startup logs the active mode and, when it is not `ReferenceOnly`, the *names* of settings that resolved to an inline value — never the values — so an unintended inline secret is discoverable instead of silent. And an inline value arrives from `IConfiguration` as a `string`, which decision 7 establishes cannot be erased; the inline modes therefore forfeit part of the in-memory protection, which the operations documentation must say plainly. Both facts are why `ReferenceOnly` stays the default rather than the friendliest option winning.
15. **`env:` resolves in every environment.** The specification gates exactly one scheme on the environment, and the definition of done names only the `plaintext:` rule. `env:` stays permitted with a documented recommendation against production use, rather than inventing a second environment gate the specification did not ask for. *Flagged for the owner: if `env:` should also fail outside Development, it is a one-line change to the same flag.*
16. **Resolution runs at startup validation and again per use, which is what makes rotation work.** Startup validation resolves every reference and discards the material, so an unresolvable reference fails the host. Each actual use resolves again, so no long-lived copy exists and material rotated behind an unchanged reference — a replaced credential file, a re-issued vault entry — is observed by the next operation with no cache to invalidate and no restart. The material is a few hundred bytes from a tmpfs or an environment block; per-run re-reading is not a measured cost. A network-backed adapter for which per-use retrieval is too expensive caches inside itself under decision 19, without changing this.
17. **Secrets are reloadable for new operations, not during running operations.** — *moved to specification 02b.* Recorded there, keeping this number so cross-references from either plan resolve to the same decision.
18. **ADR 0002 must be amended before reload is implemented.** — *moved to specification 02b.* Recorded there, keeping this number so cross-references from either plan resolve to the same decision.
19. **Provider-specific concerns stay inside the provider's adapter.** Timeouts, retry, backoff, endpoint and region configuration, SDK client lifetime, platform identity, and caching policy belong to the adapter that needs them, never to `ISecretReferenceResolver`. The contract carries a reference and a cancellation token and returns a result; that is the whole surface. This is what lets a store that must cache aggressively and a local file that must never cache coexist without the contract taking a position, and it keeps SDK types out of everything above the adapter exactly as the root dependency rules require.
20. **A managed-store adapter authenticates through platform identity, not through a MailMcp secret.** An Azure managed identity, a Kubernetes ServiceAccount token, or a Vault role is issued by the platform the process already runs on. Requiring MailMcp to hold a credential in order to fetch its credentials would be circular and would put the most sensitive value back into the configuration this specification removes it from. Recorded now so a future adapter does not quietly reintroduce the problem; nothing implements it here.
21. **A custom trust anchor is validated by rebuilding the chain, never by accepting errors.** — *moved to specification 02b.* Recorded there, keeping this number so cross-references from either plan resolve to the same decision.
22. **The database password becomes a reference too.** The specification's goal names it explicitly. `ConnectionStrings:mailmcp` keeps host, database, and user name; an optional `Persistence:Password` secret block is resolved at startup and applied through `NpgsqlConnectionStringBuilder.Password`, so the connection string in `appsettings.json` never carries the password and the composed string never reaches a log.
23. **Two resolver names, two scopes.** `ISecretReferenceResolver` resolves one reference. `MailAccountSecretOptions` resolves one account's set of secrets and reports per-account configuration errors. Naming them both "secret resolver" would make the call sites ambiguous, which the root naming rules forbid.
24. **`UserName` stays a plain configuration value.** The definition of done names raw *passwords*. A mailbox user name is an identifier the operator already writes next to the host, and turning it into a reference would double the provisioning burden for no confidentiality gain. It remains excluded from logs as personal data.
25. **Every secret-bearing setting is a JSON object, never a bare string.** The object is `ConfiguredSecret`, and its `SecretReference` property carries the reference. The reason is forward compatibility: a PKCS#12 bundle needs a second reference for its own password, a certificate may need an explicit format hint, and a managed-store reference may need a version pin. Inside a block each of those is a sibling property. Had the setting shipped as a bare string, the first such addition would change the setting's JSON type from string to object and break every deployment that had configured it. Only the first setting to need it would be affected, but the fix would then have to be applied setting by setting against real deployments; the uniform block pays that cost once, now, while `TrustedCertificateAuthorityReference` has one consumer and no shipped release depends on it. One sibling is defined now rather than later: an optional nested `Password` block, itself a `ConfiguredSecret`. Specification 02b needs it for a password-protected PKCS#12 bundle, and discovering that mid-02b would have forced a change to a contract 02a had already shipped — which is the case this plan says must go back to the owner rather than be absorbed. Defining it here costs one nullable property and keeps the recursion honest: the discovery walk of decision 26 descends into it like any other block, so a bundle password is bound, validated, resolved, and erased by exactly the machinery every other secret uses. The cycle guard the walk already needs covers the recursion.
26. **The block's type is the marker, in place of an attribute.** A secret-bearing property is declared by binding to `ConfiguredSecret` rather than to `string`, so `MailSecretReferenceStartupValidator` discovers every one of them by walking the bound options graph for that type. An attribute would work the same way at the point of use, but it can be omitted on a property added six months from now and nothing would notice — the property would bind to `string`, hold a password, and skip every rule in this plan. A type cannot be omitted, because the property would not bind to the block shape at all. This is what makes decision 13's `ReferenceOnly` guarantee total rather than per-setting: under the default mode a block whose `SecretReference` is not a well-formed `<scheme>:<target>` fails startup naming its configuration path, so a plain-text password pasted where a reference belongs is a startup error and never a secret that resolved by accident. The Task 6 architecture test enforces the other direction, failing the build if a secret-looking property is bound as a raw `string`.
27. **The block is verified against flat colon-separated keys, not only against JSON.** The block is one more path segment to any hierarchical configuration provider, so `MailSynchronization:Accounts:0:Secrets:Password:SecretReference` in Azure App Configuration and `MailSynchronization__Accounts__0__Secrets__Password__SecretReference` in an environment block address exactly the same setting. That has to be proved rather than assumed, because the whole Azure App Configuration path depends on it: the store holds that key, Key Vault holds the value, the provider maps one to the other, and `InlineOnly` tells MailMcp the bound value is already the secret. Binding tests therefore populate an in-memory provider with flat keys instead of parsing a JSON document, so a change that made the block depend on JSON document structure fails a test rather than a deployment.
28. **A successful resolution carries where its material came from.** `SecretResolutionResult` exposes `SecretMaterialSource`: `SchemeAdapter` or `InlineValue`. Two commitments already made are unimplementable without it. Decision 14 promises to log *which settings* resolved inline, which cannot be known from the mode alone under `ReferenceOrInline`. And specification 02b must accept a DER or PKCS#12 anchor read through `file:` while rejecting the same bytes supplied inline — with only the global mode to consult it would either reject valid referenced binary certificates or accept forbidden inline ones. Since 02b is barred from changing this contract, the provenance has to exist before it ships. Both paths are tested here, not in 02b.
29. **Material is bounded before it is allocated.** A mistaken `file:` target can name a log, a database file, or a device-backed pseudo-file, and reading it whole and then allocating an equally large pinned copy would exhaust memory or stall the host before validation finishes — during startup and again per operation, since decision 16 resolves per use. The reader therefore takes a maximum byte count and enforces it while reading, before the owned buffer is allocated, failing as `MaterialTooLarge`. The ceiling is generous enough for a certificate bundle and far below anything that threatens the process; the root rules require an explicit limit at every boundary that reads external input, and a secret path is one.
30. **The source buffer is erased, and the read is asynchronous.** An earlier draft had the file port return `ReadOnlyMemory<byte>` from `File.ReadAllBytes`, which `ResolvedSecret.FromBytes` then copied into the pinned buffer. Only the copy would have been zeroed; the original movable array would have kept the credential until collection, defeating decision 7 for `file:` and `systemd-credential:` — the two schemes production actually uses. The port therefore returns the owned `ResolvedSecret` and erases its own intermediate buffer in a `finally`, so ownership transfers rather than duplicating. The same port is asynchronous and takes the caller's token, because a synchronous read blocks a thread on a stalled network-mounted secret path and would make decision 4's end-to-end cancellation claim false one layer below the contract that asserts it.
31. **`env:` is a documented third exposure, not a clean scheme.** `Environment.GetEnvironmentVariable` returns a `string`, and decision 7 establishes that a `string` cannot be erased. Resolving an `env:` reference therefore leaves an un-erasable copy behind exactly as an inline value does, which the definition of done must say rather than imply otherwise. This is a further reason the documentation recommends against `env:` in production, and it is why the memory-hygiene guarantee is stated as covering material MailMcp allocates rather than material the platform hands it as a `string`.
32. **The startup validator, not only the architecture test, enforces the marker.** The Task 6 reflection test scans `Infrastructure` and cannot reach `MailSynchronizationOptions` or `PersistenceOptions`, which live in coverage-excluded `Host` — so a future `Host` property named `ApiKey` or `Token` could bind a secret as a raw `string` while the test still reported green. The graph walk already runs over the real bound roots at startup, so it also fails the host when it finds a `string` property whose name matches the same secret-name rule. The architecture test keeps the failure at build time where it is cheapest; the runtime check is what makes the guarantee actually repository-wide instead of Infrastructure-wide.

## File structure

**Create**

- `src/Infrastructure/Secrets/SecretReferenceScheme.cs` — the open scheme value and the four well-known members.
- `src/Infrastructure/Secrets/SecretReference.cs` — parsed scheme and target, with the parse rules of decision 1.
- `src/Infrastructure/Secrets/ConfiguredSecret.cs` — the bindable secret block, and the type that marks a setting as secret-bearing.
- `src/Infrastructure/Secrets/ConfiguredSecretDiscovery.cs` — walks a bound options object and yields every secret block with the configuration path that reached it.
- `src/Infrastructure/Secrets/SecretResolutionFailure.cs` — machine-readable failure identities.
- `src/Infrastructure/Secrets/SecretResolutionResult.cs` — success carrying `ResolvedSecret`, or a failure identity.
- `src/Infrastructure/Secrets/ResolvedSecret.cs` — pinned buffer, `RevealBytes()`/`RevealAsString()`, zeroing `Dispose`, redacted `ToString()`.
- `src/Infrastructure/Secrets/ISecretReferenceResolver.cs`, `ISecretSchemeResolver.cs` — the one-reference contract and the public per-scheme extension point.
- `src/Infrastructure/Secrets/ISecretFileReader.cs`, `IEnvironmentVariableReader.cs` — the two in-memory-testable ports.
- `src/Infrastructure/Secrets/FileSystemSecretFileReader.cs`, `ProcessEnvironmentVariableReader.cs` — the real adapters.
- `src/Infrastructure/Secrets/SystemdCredentialSecretReferenceResolver.cs`, `FileSecretReferenceResolver.cs`, `EnvironmentVariableSecretReferenceResolver.cs`, `PlaintextSecretReferenceResolver.cs`, `CompositeSecretReferenceResolver.cs`
- `src/Infrastructure/Mail/MailAccountSecretOptions.cs` — the account's secret blocks and their resolution into `MailAccountSecrets`.
- `src/Infrastructure/Mail/MailAccountSecrets.cs` — the account's resolved secrets, owned and disposed by the operation that resolved them.
- `tests/Infrastructure.UnitTests/SecretReferenceTests.cs`
- `tests/Infrastructure.UnitTests/SecretReferenceResolverTests.cs`
- `tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs`
- `tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs`
- `src/Host/Hosting/MailSecretReferenceStartupValidator.cs` — `IHostedLifecycleService.StartingAsync` fail-fast resolution.
- `docs/operations/secret-provisioning.md`

**Modify**

- `src/Infrastructure/Mail/ImapAccountSettings.cs` — `Password` becomes `ResolvedSecret`; `IImapAccountSettingsProvider.GetSettings` becomes `GetSettingsAsync`.
- `src/Infrastructure/Mail/MailAccountTransportSecurityOptions.cs` — `TrustedCertificateAuthorityReference` is renamed to `TrustedCertificateAuthority` and becomes a `ConfiguredSecret?` block; the domain contract is unchanged.
- `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs` — the factory awaits `GetSettingsAsync`, reveals the password at the single `AuthenticateAsync` call, and disposes the resolved secrets once the folder is open.
- `src/Infrastructure/ServiceCollectionExtensions.cs` — `AddMailMcpSecretResolution`, and the resolved database password applied to the connection string.
- `src/Host/Configuration/MailSynchronizationOptions.cs` — `Password` becomes a nested `Secrets` section; `GetSettings` becomes `GetSettingsAsync` and resolves.
- `src/Host/Configuration/PersistenceOptions.cs` — optional `Password` secret block.
- `src/Host/Program.cs` — register secret resolution with the configured interpretation mode, register the startup validator ahead of the worker, compose the connection string.
- `src/Host/appsettings.json`, `src/Host/appsettings.Development.json` — reference-shaped examples.
- `docs/features/imap-synchronization.md` — the new account configuration shape and the resolution and fail-fast behavior.
- `docs/operations/local-development.md` — the Development secret workflow.

---

### Task 1: The reference grammar and the resolution result

**Files:**
- Create: `src/Infrastructure/Secrets/SecretReferenceScheme.cs`, `SecretReference.cs`, `ConfiguredSecret.cs`, `ConfiguredSecretDiscovery.cs`, `SecretResolutionFailure.cs`, `SecretResolutionResult.cs`, `ResolvedSecret.cs`
- Test: `tests/Infrastructure.UnitTests/SecretReferenceTests.cs`, `tests/Infrastructure.UnitTests/ConfiguredSecretDiscoveryTests.cs`

**Interfaces produced:**

```csharp
public sealed record SecretReferenceScheme
{
    public string Name { get; }                       // normalized, lower-case wire name

    public static SecretReferenceScheme SystemdCredential { get; }    // "systemd-credential"
    public static SecretReferenceScheme File { get; }                 // "file"
    public static SecretReferenceScheme EnvironmentVariable { get; }  // "env"
    public static SecretReferenceScheme Plaintext { get; }            // "plaintext"

    public static SecretReferenceScheme Create(string name);          // for adapters declared elsewhere
}

public enum SecretValueInterpretation
{
    ReferenceOnly = 0,      // default: a bare value fails
    ReferenceOrInline = 1,  // recognized scheme resolves; anything else is the secret
    InlineOnly = 2,         // nothing is parsed; every value is already the secret
}

public enum SecretResolutionFailure
{
    ReferenceMissing = 0,
    SchemeMissing = 1,
    SchemeNotSupported = 2,
    TargetMissing = 3,
    InlineValueNotPermittedByInterpretationMode = 4,
    CredentialsDirectoryUnavailable = 5,
    MaterialNotFound = 6,
    MaterialEmpty = 7,
    ProviderUnavailable = 8,
    MaterialTooLarge = 9,
}

public sealed record SecretReference
{
    public SecretReferenceScheme Scheme { get; }
    public string Target { get; }

    public static bool TryParse(string? reference, out SecretReference? parsed, out SecretResolutionFailure failure);

    // Suppresses the synthesized record printing, which would otherwise write Target verbatim.
    public override string ToString() => $"{this.Scheme.Name}:***";
    private bool PrintMembers(StringBuilder builder) => throw new NotSupportedException();
}

/// <summary>The bindable shape of every secret-bearing configuration setting.</summary>
public sealed class ConfiguredSecret
{
    public string SecretReference { get; set; } = string.Empty;

    /// <summary>Password protecting the referenced material, when the material is itself protected.</summary>
    public ConfiguredSecret? Password { get; set; }
}
```

`ConfiguredSecret` is mutable with a settable property because the configuration binder requires it, and it is the only bindable type in `src/Infrastructure/Secrets/`. It carries no resolution logic: it is the JSON shape and the marker of decision 26, nothing more. The nested `Password` is the reason it is a class rather than the bare string it wraps: a PKCS#12 bundle carries its own password, and 02b resolves it through this shape without touching this file.

```csharp
public sealed record DiscoveredSecret(string ConfigurationPath, ConfiguredSecret Secret);

internal static class ConfiguredSecretDiscovery
{
    internal static IReadOnlyList<DiscoveredSecret> FindIn(object boundOptions, string rootSectionName);
}
```

`ConfiguredSecretDiscovery` is what makes the marker useful: it walks public readable properties of the bound options object, descends into nested options objects, into `IEnumerable` elements, and into a block's own nested `Password` block, and yields each `ConfiguredSecret` with the colon-separated path that reached it — `MailSynchronization:Accounts:0:Secrets:Password`. It must guard against a cycle in the object graph, since options types are ordinary classes and nothing forbids a back-reference. It never reads a `ConfiguredSecret`'s value; it returns the block and lets the caller resolve, so nothing in the walk can end up in a diagnostic.

Tests cover a block at the root, a nested one, one inside a list with its index in the path, a block's own nested `Password` block reported at `…:TrustedCertificateAuthority:Password` so 02b's bundle password is discovered by the same walk, a null block being skipped rather than reported as missing, a `string` property named for a secret failing the walk under decision 32, and a cyclic graph terminating. A separate binding test builds the options graph from an in-memory provider populated with flat colon-separated keys and asserts the discovered paths match them exactly — decision 27, and what keeps the Azure App Configuration and environment-variable paths honest.

`SecretReferenceScheme` is open by decision 2: a future Azure Key Vault adapter calls `Create("azure-key-vault")` in its own file and registers itself. `ProviderUnavailable` is appended now rather than later so a network-backed adapter has a failure identity to report that is distinguishable from a missing secret; nothing in this change set produces it. Enum values are append-only, so this costs nothing and avoids renumbering a persisted-looking value later.

```csharp

public sealed class ResolvedSecret : IDisposable
{
    public static ResolvedSecret FromBytes(ReadOnlySpan<byte> material);
    public static ResolvedSecret FromText(string material);   // UTF-8 encodes; for plaintext: and env:

    public ReadOnlySpan<byte> RevealBytes();      // primary accessor: material, untouched
    public string RevealAsString();               // framework-contract escape hatch; see decision 8
    public void Dispose();                        // CryptographicOperations.ZeroMemory over the pinned buffer
    public override string ToString();            // "***"
}

public enum SecretMaterialSource
{
    SchemeAdapter = 0,   // resolved by a registered adapter through a reference
    InlineValue = 1,     // the configured value was itself the material
}

public sealed record SecretResolutionResult
{
    public bool Succeeded { get; }
    public ResolvedSecret? Secret { get; }
    public SecretMaterialSource? Source { get; }
    public SecretResolutionFailure? Failure { get; }

    public static SecretResolutionResult Resolved(ResolvedSecret secret, SecretMaterialSource source);
    public static SecretResolutionResult Failed(SecretResolutionFailure failure);
}
```

- [x] **Step 1: Write the failing tests**

`tests/Infrastructure.UnitTests/SecretReferenceTests.cs`, one behavior per test:

```csharp
[Theory]  // "systemd-credential:imap" => SystemdCredential, "file:/run/secrets/imap" => File, ...
TryParse_SupportedScheme_ParsesSchemeAndTarget
[Fact]    TryParse_TargetContainsColon_KeepsEverythingAfterTheFirstColon        // "file:C:\\secrets\\imap" => "C:\\secrets\\imap"
[Fact]    TryParse_MixedCaseScheme_ParsesScheme                                 // "File:/run/secrets/imap"
[Fact]    TryParse_WhitespaceAroundTheScheme_TrimsTheSchemeOnly                 // " file :/run/secrets/imap"
[Fact]    TryParse_PlaintextTargetWithLeadingAndTrailingSpaces_KeepsEverySpace  // "plaintext: secret " => " secret "
[Fact]    TryParse_NullOrWhitespace_FailsWithReferenceMissing
[Fact]    TryParse_NoColon_FailsWithSchemeMissing                               // under ReferenceOnly a bare password must never parse
[Fact]    TryParse_UnregisteredScheme_ParsesBecauseSupportIsADispatchQuestion   // "azure-key-vault:imap" parses
[Fact]    TryParse_EmptyTarget_FailsWithTargetMissing                           // "file:"
[Fact]    TryParse_UrlTarget_KeepsTheWholeUrl                                   // "azure-key-vault:https://v.example/s/imap"
[Fact]    ToString_ResolvedSecret_DoesNotContainTheMaterial
[Fact]    RevealAsString_ResolvedSecret_ReturnsTheUtf8TextView
[Fact]    RevealAsString_MaterialEndsWithNewline_StripsOneTrailingNewline
[Fact]    RevealBytes_BinaryMaterial_ReturnsEveryByteUnchanged      // a PKCS#12 bundle must survive
[Fact]    RevealBytes_MaterialEndsWithNewline_DoesNotStripIt        // trimming a PFX would corrupt it
[Fact]    Dispose_ResolvedSecret_ZeroesTheMaterial
[Fact]    Dispose_CalledTwice_DoesNotThrow
[Fact]    RevealBytes_AfterDispose_Throws                           // a use-after-erase is a bug, not a silent empty
[Fact]    ToString_SecretResolutionResult_DoesNotContainTheMaterial             // the record's synthesized ToString
[Fact]    ToString_SecretReference_ContainsNeitherTheTargetNorAPlaintextSecret  // the record's synthesized ToString
```

The last three matter more than they look: they are the regression guard for decision 12. `SecretReference` is a record with a public `Target`, so the synthesized printing would otherwise write a file path, a variable name, a vault identifier, or — for `plaintext:` — the complete secret into any log, exception message, or diagnostic dump the parsed object reaches.

- [x] **Step 2: Run the tests and confirm they fail to compile**

Run: `dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj`
Expected: FAIL — `SecretReference` does not exist.

- [x] **Step 3: Implement**

`TryParse` rejects a blank input, splits on `IndexOf(':')`, and normalizes the scheme name to lower case with `ToLowerInvariant`. It trims **only the scheme**, never the target: `plaintext: secret ` must resolve to `" secret "`, because leading and trailing spaces are valid password characters and a parser that trims them silently changes the credential. A target that is empty after the separator is still a failure; a target that is entirely whitespace is not, because that too can be a password. Every byte after the first colon reaches the adapter untouched. It does not check the name against a list: an unknown scheme is a well-formed reference to a provider that is not registered, and decision 3 puts that answer in the dispatch. `SecretReferenceScheme` compares by normalized name with `StringComparer.Ordinal`, and its well-known members are the four wire names. The member name `EnvironmentVariable` and the wire name `env` deliberately differ, because the configuration prefix should stay short while the member name stays explicit.

`ResolvedSecret` allocates its buffer with `GC.AllocateArray<byte>(length, pinned: true)` so the collector cannot relocate it and leave an un-erased copy, copies the material in, and erases it in `Dispose` with `CryptographicOperations.ZeroMemory`. `Dispose` is idempotent, and every accessor throws `ObjectDisposedException` afterwards rather than returning empty — a use-after-erase is a defect and must present as one. `ToString()` returns `"***"`.

XML documentation states that `RevealBytes` is the primary accessor, that `RevealAsString` produces a copy which cannot be erased and exists only for framework contracts that take a `string`, and that the instance is owned by the operation that resolved it.

```csharp
public void Dispose()
{
    if (this.disposed)
    {
        return;
    }

    CryptographicOperations.ZeroMemory(this.material);
    this.disposed = true;
}
```

The zeroing must go through `CryptographicOperations.ZeroMemory` rather than `Array.Clear` or a loop: it is documented as existing "to future-proof against potential optimizations in the .NET runtime that could eliminate memory writes that aren't followed by memory reads", which is exactly this write.

- [x] **Step 4: Run the tests**

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Infrastructure/Secrets tests/Infrastructure.UnitTests/SecretReferenceTests.cs
git commit -m "Add the secret reference grammar and resolution result"
```

---

### Task 2: Per-scheme resolvers and composite dispatch

**Files:**
- Create: `src/Infrastructure/Secrets/ISecretReferenceResolver.cs`, `ISecretFileReader.cs`, `IEnvironmentVariableReader.cs`, `FileSystemSecretFileReader.cs`, `ProcessEnvironmentVariableReader.cs`, `SystemdCredentialSecretReferenceResolver.cs`, `FileSecretReferenceResolver.cs`, `EnvironmentVariableSecretReferenceResolver.cs`, `PlaintextSecretReferenceResolver.cs`, `CompositeSecretReferenceResolver.cs`
- Test: `tests/Infrastructure.UnitTests/SecretReferenceResolverTests.cs`

**Interfaces produced:**

```csharp
public interface ISecretReferenceResolver
{
    Task<SecretResolutionResult> ResolveAsync(string? reference, CancellationToken cancellationToken);
}

public interface ISecretSchemeResolver
{
    SecretReferenceScheme Scheme { get; }
    Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken);
}

internal interface ISecretFileReader
{
    // Hands ownership of the pinned buffer to the caller; the reader retains no copy.
    Task<ResolvedSecret?> ReadAsync(string path, int maxByteCount, CancellationToken cancellationToken);
}

internal interface IEnvironmentVariableReader
{
    string? GetValue(string name);
}
```

The file reader is asynchronous, takes the caller's token, and takes a byte ceiling, all three for reasons decision 30 records: a synchronous port would block a thread on a stalled network-mounted secret path and would make decision 4's "cancellable end to end" claim false one layer below the contract that makes it. It returns the owned `ResolvedSecret` rather than a `ReadOnlyMemory<byte>` so that no un-erased intermediate array survives the read.

`ISecretSchemeResolver` is `public` — unusually for this repository, which defaults to `internal` — because it is the extension point decision 2 promises. A provider adapter in another folder, another project, or a later change set implements it, and an `internal` contract would make that impossible without editing this file. The four adapters implementing it here stay `internal sealed`. The two reader ports stay `internal`: they exist for testability, not for extension, and `InternalsVisibleTo MailMcp.Infrastructure.UnitTests` already covers them.

- [x] **Step 1: Write the failing tests**

`SecretReferenceResolverTests` uses hand-written in-memory fakes — a dictionary-backed `ISecretFileReader` and `IEnvironmentVariableReader` — never the real file system or environment block:

```csharp
[Fact] ResolveAsync_SystemdCredential_ReadsTheNameFromTheCredentialsDirectory
[Fact] ResolveAsync_SystemdCredentialWithoutCredentialsDirectory_FailsWithCredentialsDirectoryUnavailable
[Fact] ResolveAsync_SystemdCredentialNameContainingPathSeparator_FailsWithTargetMissing   // no escaping the directory
[Fact] ResolveAsync_SystemdCredentialMaterialEndsWithNewline_TrimsTheTrailingNewline
[Fact] ResolveAsync_File_ReadsTheProvisionedFile
[Fact] ResolveAsync_FileMissing_FailsWithMaterialNotFound
[Fact] ResolveAsync_FileEmpty_FailsWithMaterialEmpty
[Fact] ResolveAsync_FileLargerThanTheCeiling_FailsWithMaterialTooLargeWithoutAllocatingTheMaterial
[Fact] ResolveAsync_FileMalformedPath_FailsWithMaterialNotFoundInsteadOfThrowing   // a NUL character throws ArgumentException
[Fact] ResolveAsync_EnvironmentVariable_ReadsTheVariable
[Fact] ResolveAsync_EnvironmentVariableUnset_FailsWithMaterialNotFound
[Fact] ResolveAsync_Plaintext_ReturnsTheLiteralInEveryModeThatParses
[Fact] ResolveAsync_BareValueUnderReferenceOnly_FailsWithSchemeMissing
[Fact] ResolveAsync_BareValueUnderReferenceOrInline_ReturnsItAsTheSecret
[Fact] ResolveAsync_SchemeShapedValueUnderInlineOnly_ReturnsItVerbatimWithoutParsing   // "file:/x" is the password
[Fact] ResolveAsync_RecognizedSchemeUnderReferenceOrInline_ResolvesThroughTheAdapter
[Fact] ResolveAsync_UnregisteredScheme_FailsWithSchemeNotSupportedWithoutConsultingAnyReader
[Fact] ResolveAsync_SchemeAdapterRegisteredOnlyByTheTest_ResolvesThroughTheSameDispatch
[Fact] ResolveAsync_Cancelled_PropagatesTheCancellation
[Fact] ResolveAsync_ResolvedThroughAnAdapter_ReportsSchemeAdapterAsTheSource
[Fact] ResolveAsync_ValueAcceptedInline_ReportsInlineValueAsTheSource
[Theory] ResolveAsync_EveryFailure_ReturnsNoSecretMaterial
```

`ResolveAsync_SchemeAdapterRegisteredOnlyByTheTest_ResolvesThroughTheSameDispatch` is the extensibility proof the specification's definition of done requires: the test project declares an `ISecretSchemeResolver` for a scheme the production code has never heard of, registers it, and resolves a reference through it. If that test ever needs a production edit to pass, the extension point has regressed.

The path-separator test encodes a real hazard: `systemd-credential:../../etc/shadow` must not become a traversal. The trailing-newline test encodes the other: `LoadCredential=` and Compose secrets both commonly carry a file that ends with a newline, and an untrimmed `\n` produces an authentication failure that looks like a wrong password.

- [x] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — the resolvers do not exist.

- [x] **Step 3: Implement**

`SystemdCredentialSecretReferenceResolver` reads `CREDENTIALS_DIRECTORY` through `IEnvironmentVariableReader` — systemd documents that "the path to access them is derived from the environment variable `$CREDENTIALS_DIRECTORY`" and that access is restricted to the service's user — rejects a target containing `/`, `\`, or `..`, joins, and reads. `FileSecretReferenceResolver` reads the target directly, which is also the container path: Compose mounts secrets at `/run/secrets/<secret_name>`. Both read bytes and neither modifies them; the newline trim lives in `ResolvedSecret.RevealAsString()` so binary material is never touched, and both report `MaterialEmpty` when the material has zero length.

`PlaintextSecretReferenceResolver` returns the target verbatim; it carries no environment gate of its own, because decision 13 moved that responsibility to the interpretation mode.

`CompositeSecretReferenceResolver` takes the `SecretValueInterpretation` mode and branches before parsing. Under `InlineOnly` it never parses and wraps the whole value as material — the Azure App Configuration case, where `file:/x` may legitimately *be* the password. Under `ReferenceOnly` an unparseable value fails. Under `ReferenceOrInline` a recognized scheme dispatches and anything else becomes material. The `InlineOnly` branch must come first: parsing and then ignoring the result would leave a code path where a scheme-shaped password is silently treated as a reference.

`CompositeSecretReferenceResolver` parses once and dispatches on the scheme through a dictionary built from the injected `ISecretSchemeResolver` set, so a scheme with no registered adapter fails as `SchemeNotSupported` rather than throwing. Its XML documentation states that no result and no diagnostic derived from it may carry the material.

`FileSystemSecretFileReader` validates the path before touching the file system and then catches the complete set of expected failures — `IOException`, `UnauthorizedAccessException`, `ArgumentException`, `NotSupportedException`, and `System.Security.SecurityException` — mapping each to `false`. Catching only the first two would let a malformed target such as a path containing a NUL character throw `ArgumentException` straight out of the resolver, past the result boundary, into an unhandled startup exception whose message quotes the path. That defeats both fail-fast aggregation and the guarantee that no diagnostic carries a target.

- [x] **Step 4: Run the tests**

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add src/Infrastructure/Secrets tests/Infrastructure.UnitTests/SecretReferenceResolverTests.cs
git commit -m "Resolve secret references through per-scheme adapters"
```

---

### Task 3: Account secrets replace the raw password

**Files:**
- Create: `src/Infrastructure/Mail/MailAccountSecretOptions.cs`, `src/Infrastructure/Mail/MailAccountSecrets.cs`
- Modify: `src/Infrastructure/Mail/ImapAccountSettings.cs`, `src/Infrastructure/Mail/MailAccountTransportSecurityOptions.cs`
- Test: `tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs`, `tests/Infrastructure.UnitTests/MailAccountTransportSecurityOptionsTests.cs`

**Interfaces produced:**

```csharp
public sealed record MailAccountSecretConfigurationError(string PropertyName, SecretResolutionFailure Failure);

public sealed class MailAccountSecretOptions
{
    public ConfiguredSecret Password { get; set; } = new();

    public Task<IReadOnlyList<MailAccountSecretConfigurationError>> FindConfigurationErrorsAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken);

    public Task<MailAccountSecrets> ResolveAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken);
}

public sealed record MailAccountSecrets(ResolvedSecret Password) : IDisposable;

public sealed record ImapAccountSettings(
    string AccountId,
    string Host,
    int Port,
    string UserName,
    MailAccountSecrets Secrets) : IDisposable;
```

`MailAccountSecrets` wraps a single secret today and looks like a type that could be collapsed into `ResolvedSecret`. It is not, because specification 02b adds the resolved trust anchor beside the password and the ownership rule below has to cover both; introducing it now keeps that a one-line addition instead of a signature change through the session factory.

**The trust anchor setting is reshaped here but not read here.** `MailAccountTransportSecurityOptions.TrustedCertificateAuthorityReference` is renamed to `TrustedCertificateAuthority` and goes from `string?` to `ConfiguredSecret?`, so decision 25 holds for every secret-bearing setting from the moment the block exists rather than for new settings only. The `Reference` suffix earned its place while the value *was* the reference string; now that a block holds it in `SecretReference`, the suffix repeats the word and the setting is better named after what it configures. Loading the material behind it is specification 02b. Four consequences to handle in this task:

- `MailTransportSecurityPolicy.Create` and `FindViolations` in `Domain` keep taking a nullable `string`, and the options type now passes `this.TrustedCertificateAuthority?.SecretReference`. The block is a configuration-adapter shape and must not cross into `Domain`, which is also why the domain violation members `TrustedCertificateAuthorityReferenceRequired` and `TrustedCertificateAuthorityReferenceNotApplicable` keep their names: they describe a domain rule about a reference being present, not the configuration key that carries it.
- A block that is present but carries an empty `SecretReference` must read as *absent* to those domain rules, not as a configured anchor. Otherwise `"TrustedCertificateAuthority": {}` would satisfy `TrustedCertificateAuthorityReferenceRequired` and then fail later at resolution, reporting a confusing missing-material error instead of the missing-anchor error the operator needs.
- `SettingFor(MailTransportSecurityViolation)` maps those two violations onto a setting name for the operator message. It must report `TrustedCertificateAuthority`, the new key, not the violation member's own spelling — otherwise the startup error names a key that does not exist in the file the operator is editing.
- The existing `MailAccountTransportSecurityOptionsTests` that set the reference as a string are updated to the block. They are assertions about domain rules, not about the JSON shape, so their expectations stay as they are.

- [x] **Step 1: Write the failing tests**

```csharp
[Fact] FindConfigurationErrorsAsync_ResolvableReferences_ReportsNoError
[Fact] FindConfigurationErrorsAsync_UnresolvablePasswordReference_ReportsTheFailureAgainstThePasswordBlock
[Fact] FindConfigurationErrorsAsync_EmptyPasswordSecretReference_ReportsReferenceMissing
[Fact] FindConfigurationErrorsAsync_PlainTextInThePasswordBlockUnderReferenceOnly_ReportsSchemeMissing
[Fact] FindConfigurationErrorsAsync_EveryError_CarriesNoSecretMaterial
[Fact] ResolveAsync_ResolvableReference_ReturnsTheResolvedPassword
[Fact] Dispose_ResolvedAccountSecrets_ErasesThePasswordMaterial
```

`MailAccountTransportSecurityOptionsTests` gains coverage of the reshaped setting, since the domain rules now read through the block:

```csharp
[Fact] FindConfigurationErrors_AdditionalTrustedAuthorityWithABlock_ReportsNoError
[Fact] FindConfigurationErrors_AdditionalTrustedAuthorityWithAnEmptyBlock_ReportsTheAnchorAsMissing
[Fact] FindConfigurationErrors_TrustAnchorBlockWithoutAdditionalTrustedAuthority_ReportsNotApplicable
[Fact] FindConfigurationErrors_TrustAnchorViolation_NamesTheTrustedCertificateAuthoritySetting
```

- [x] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — `MailAccountSecretOptions` does not exist and `ImapAccountSettings.Password` is still a `string`.

- [x] **Step 3: Implement**

`FindConfigurationErrorsAsync` resolves the password reference and discards the material — decision 16's startup half.

`ResolveAsync` performs the same work and returns the value, and throws `InvalidOperationException` if a reference that validated at startup no longer resolves — a fail-closed path, not an ordinary branch.

`ImapAccountSettings` carries `ResolvedSecret`. Its XML documentation gains a remark that the record's synthesized `ToString()` is safe only because `ResolvedSecret` redacts itself.

`MailAccountSecrets` implements `IDisposable` and disposes the password. Per decision 9 the instance is owned by the operation that resolved it: `MailKitImapMailboxSessionFactory.OpenReadOnlyAsync` disposes it once the client is authenticated and the folder is open, and the startup validator disposes everything it resolved before returning. `ImapAccountSettings` does *not* own the secrets — it is a carrier — so the ownership rule is stated in its XML documentation to stop a future caller from disposing it twice or not at all.

- [x] **Step 4: Run the tests**

Expected: PASS for `Infrastructure.UnitTests`; the solution does not build until Task 5 updates `Host`.

- [x] **Step 5: Commit**

```bash
git add src/Infrastructure/Mail tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs tests/Infrastructure.UnitTests/MailAccountTransportSecurityOptionsTests.cs
git commit -m "Bind account secrets as reference blocks and resolve them in Infrastructure"
```

---

### Task 4: Database password reference

**Files:**
- Modify: `src/Host/Configuration/PersistenceOptions.cs`, `src/Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/Infrastructure.UnitTests/DatabasePasswordCompositionTests.cs`

- [x] **Step 1: Add the optional reference**

`PersistenceOptions` gains `ConfiguredSecret? Password`:

```json
"Persistence": { "Password": { "SecretReference": "file:/run/secrets/postgres-password" } }
```

When it is null the connection string is used unchanged, which keeps trust-authentication and Aspire-provided connection strings working untouched. A block present with an empty `SecretReference` is a startup failure rather than a silent fallback to the unchanged connection string, because an operator who wrote the block meant to supply a password.

- [x] **Step 2: Compose the connection string after resolution, not during registration**

The obvious shape — `AddMailMcpInfrastructure` taking an already-resolved password — does not work, and the reason is an ordering trap worth stating so nobody reintroduces it. Service registration runs synchronously during composition, while resolution is asynchronous and decision 5 puts it in `StartingAsync`, which runs *after* every registration. A resolved password therefore does not exist yet at the moment the connection string would be composed. An optional parameter would simply stay omitted: the host would still build, the reference would still validate, and EF Core would quietly keep using the passwordless connection string.

Composition is therefore deferred to first use. `AddMailMcpInfrastructure` registers a singleton `NpgsqlDataSource` built by a factory that resolves the reference when the data source is first requested:

```csharp
services.AddSingleton(provider =>
{
    var persistence = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value;
    var resolver = provider.GetRequiredService<ISecretReferenceResolver>();
    var builder = new NpgsqlDataSourceBuilder(connectionString);

    if (persistence.Password is { } configuredPassword)
    {
        builder.ConnectionStringBuilder.Password = ResolveDatabasePassword(resolver, configuredPassword);
    }

    return builder.Build();
});
```

`ResolveDatabasePassword` is the one place this plan permits blocking on an asynchronous call, because the DI factory contract is synchronous. It is confined to a singleton factory that runs once, after `StartingAsync` has already proved the reference resolves, so it neither races startup validation nor repeats per request. It is called out here rather than left implicit, because the repository forbids blocking on tasks and a reader who finds it without this paragraph will reasonably assume it is a defect. If it turns out that first use can occur before `StartingAsync`, this becomes an `IHostedLifecycleService` that builds the data source eagerly instead.

The composed string is never logged and never returned; `NpgsqlDataSource` owns it and no code path reads it back. The revealed password is the second documented `string` boundary from decision 8.

`DatabasePasswordCompositionTests` asserts the resolved password actually reaches Npgsql — `NpgsqlDataSource.ConnectionString` redacts the password, so the assertion is made on the `NpgsqlConnectionStringBuilder` the factory composed, before the data source is built:

```csharp
[Fact] BuildDataSource_ResolvablePasswordReference_ComposesTheResolvedPasswordIntoTheConnectionString
[Fact] BuildDataSource_NoPasswordBlock_LeavesTheConnectionStringUnchanged
```

Without those the failure mode is silent: everything validates, nothing connects.

Rotation of this reference is specification 02b's concern and is listed in its plan, because a singleton data source composed once cannot observe a rotated credential.

- [x] **Step 3: Commit**

```bash
git add src/Host/Configuration/PersistenceOptions.cs src/Infrastructure/ServiceCollectionExtensions.cs tests/Infrastructure.UnitTests/DatabasePasswordCompositionTests.cs
git commit -m "Resolve the database password from a secret reference"
```

---

### Task 5: Host binding, wiring, and fail-fast validation

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs`, `src/Host/appsettings.json`, `src/Host/appsettings.Development.json`

New account configuration shape:

```jsonc
{
  "AccountId": "primary",
  "Host": "imap.example.test",
  "Port": 993,
  "UserName": "mailmcp@example.test",
  "Secrets": {
    "Password": { "SecretReference": "systemd-credential:imap-primary-password" }
  },
  "TransportSecurity": { "ConnectionSecurity": "TlsOnConnect" },
  "Folders": [ "INBOX" ]
}
```

- [x] **Step 1: Replace the raw password**

`MailSynchronizationAccountOptions` drops `Password` and gains `MailAccountSecretOptions Secrets { get; set; } = new();`, mirroring the `TransportSecurity` nesting specification 01 established. The `Password is required` validation rule becomes a reference rule reported by `MailAccountSecretOptions`.

- [x] **Step 2: Resolve at the settings boundary**

`IImapAccountSettingsProvider.GetSettings` becomes `GetSettingsAsync(string accountId, CancellationToken cancellationToken)` per decision 4, and `MailSynchronizationOptions` takes `ISecretReferenceResolver` to satisfy it. `MailKitImapMailboxSessionFactory.OpenReadOnlyAsync` is already asynchronous and already holds the token, so the call site changes by one `await`.

- [x] **Step 3: Fail fast on an unresolvable reference**

Add `MailSecretReferenceStartupValidator : IHostedLifecycleService` in `src/Host/Hosting/`, injected with the options and the resolver. `StartingAsync` walks the bound options graph, collects every `ConfiguredSecret` it finds with the configuration path that reached it, resolves each one through the resolver, and throws `OptionsValidationException` listing every failure at once:

```
MailSynchronization:Accounts:0:Secrets:Password — the secret reference could not be resolved [MaterialNotFound].
MailSynchronization:Accounts:1:Secrets:Password — the value is not a secret reference and the interpretation mode is ReferenceOnly [SchemeMissing].
```

The configuration path, the failure identity, and nothing else — no target path, no variable name, no material. The path replaces the earlier account-plus-property phrasing because decision 25 nests the setting one level deeper and because an operator fixes the error by editing that exact path; `AccountId` is not usable as the anchor anyway, since a mistyped or duplicated account may not have one.

The same walk applies decision 32's other half: a `string` property whose name contains `Password`, `Secret`, `Credential`, `PrivateKey`, or `Token` is reported as a configuration error against its path, because `MailSynchronizationOptions` and `PersistenceOptions` live in `Host` where Task 6's build-time test cannot reach them.

The second line is decision 26 doing its work: the block was found by type, so a plain-text password sitting where a reference belongs fails here rather than authenticating successfully against the server. Reporting every failure together matters when an operator provisions five accounts and mistypes two names; one-at-a-time discovery costs five restarts.

The graph walk is the one place this plan accepts reflection, and it earns it: enumerating `ConfiguredSecret` properties is what makes decision 26's guarantee automatic for settings that do not exist yet, which an explicit call list cannot be. It runs once, at startup, over a bound options object of a few dozen properties. The walk itself lives in `Infrastructure` as `ConfiguredSecretDiscovery` so it is unit-testable — `Host` is excluded from coverage, and the same rule that put `MailAccountSecretOptions` in `Infrastructure` applies here. `MailAccountSecretOptions.FindConfigurationErrorsAsync` stays as the account-scoped path, which specification 02b's reload reuses to validate one candidate snapshot rather than the whole graph.

`StartingAsync` is documented to run before any hosted service's `StartAsync`, so the synchronization worker never starts against an unresolvable secret. The remaining four lifecycle members are empty. The type stays a thin translation because `Host` is excluded from coverage; every rule it reports comes from Task 3 and from `ConfiguredSecretDiscovery`.

- [x] **Step 4: Wire the resolver**

```csharp
builder.Services.AddMailMcpSecretResolution(builder.Configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly));
builder.Services.AddHostedService<MailSecretReferenceStartupValidator>();
```

`AddMailMcpSecretResolution` is a focused extension owned by `Infrastructure`, per the `src/AGENTS.md` rule that registration lives with the implementation. It registers the two reader ports, the four scheme adapters, and the composite with its interpretation mode. The default is `ReferenceOnly`, so a deployment that says nothing gets the safe behavior. A future provider adapter adds its own `AddMailMcpAzureKeyVaultSecrets(...)` extension next to this call and needs no edit here — the composite resolves whatever `ISecretSchemeResolver` registrations it is handed, which is decision 2 made concrete in the container.

The validator is registered before the synchronization worker so hosted-service ordering reinforces `StartingAsync` ordering rather than depending on it alone.

- [x] **Step 5: Update the shipped configuration examples**

`appsettings.json` keeps an empty account list, documents the block shape by example only, and leaves `Secrets:Interpretation` unset so the default `ReferenceOnly` applies. `appsettings.Development.json` sets `ReferenceOrInline` and shows the block carrying a `plaintext:` reference:

```json
"Secrets": { "Password": { "SecretReference": "plaintext:dev-password" } }
```

That is what makes local development convenient without weakening the shipped default — the shape is identical to production, only the reference differs, so moving a working development configuration to a real deployment is one string edit rather than a restructuring. No real credential appears in either file.

- [x] **Step 6: Verify the removal is complete**

```bash
dotnet build MailMcp.slnx
grep -rnE "string\??\s+(Password|Secret|Credential|PrivateKey|Token)\b" src/ --include=*.cs
```

Expected: build succeeds; the grep matches only `ConfiguredSecret.SecretReference`. Searching for the *type* rather than for the JSON key `"Password"` is the point — after decision 25 the key is legitimate and appears in every secret block, so the old key-based grep would report the correct shape as a finding. This is the specification's first definition-of-done item, and Task 6's assembly-wide test is its permanent form; this grep also reaches `Host`, which that test cannot.

- [x] **Step 7: Commit**

```bash
git add src/Host
git commit -m "Bind mail and database secrets as references and fail startup when one is unresolvable"
```

---

### Task 6: Architecture tests for the boundary and the marker

**Files:**
- Create: `tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs`

- [x] **Step 1: Write the test**

```csharp
[Fact]
public void SecretResolutionTypes_DomainAndApplicationAssemblies_AreNotReachable()
{
    // Arrange
    var secretAssemblyName = typeof(ISecretReferenceResolver).Assembly.GetName().Name;
    var boundaryAssemblies = new[] { typeof(MailAccountId).Assembly, typeof(MailboxSynchronizer).Assembly };

    // Act
    var referencedNames = boundaryAssemblies
        .SelectMany(assembly => assembly.GetReferencedAssemblies())
        .Select(reference => reference.Name);

    // Assert
    Assert.DoesNotContain(secretAssemblyName, referencedNames);
}
```

A second test enforces decision 26 from the other side, so the marker cannot be bypassed by declaring a secret as a plain string:

```csharp
[Fact]
public void InfrastructureOptionsTypes_PropertyNamedForASecret_BindsToConfiguredSecret()
```

It enumerates every type in the `Infrastructure` assembly whose name ends in `Options`, walks its property graph, and fails on any `string` property whose name contains `Password`, `Secret`, `Credential`, `PrivateKey`, or `Token`. Scanning the assembly rather than an `[InlineData]` list matters: an options type added later is covered without anyone remembering to register it, which is the same argument decision 26 makes about the marker itself. `ConfiguredSecret.SecretReference` is excluded, since it is the shape the rule steers toward.

One honest limit remains: the rule is name-based, so it cannot catch a secret called `Value`. It exists to make the *ordinary* mistake — adding `public string Password { get; set; }` to an options type — fail the build rather than ship, and its own documentation says so.

The test reaches `Infrastructure` only, because `MailSynchronizationOptions` and `PersistenceOptions` live in coverage-excluded `Host`. Decision 32 closes that gap at runtime instead of leaving it documented: `ConfiguredSecretDiscovery` already walks the real bound roots during `StartingAsync`, so it applies the same secret-name rule to `string` properties it passes and fails the host on a match. Build-time coverage where it is cheap, startup coverage where reflection can actually see the roots.

No architecture-test package is added for either test: introducing one would require a license review and owner approval for rules that a few lines of reflection already prove. Both live in `Infrastructure.UnitTests` because that is the only unit-test project referencing all three assemblies.

- [x] **Step 2: Run and commit**

```bash
dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
git add tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs
git commit -m "Assert secret resolution stays out of Domain and Application"
```

---

### Task 7: Documentation and full verification

**Files:**
- Create: `docs/operations/secret-provisioning.md`
- Modify: `docs/features/imap-synchronization.md`, `docs/operations/local-development.md`

- [x] **Step 1: Write the provisioning page**

`docs/operations/secret-provisioning.md` documents the reference grammar, the four schemes, and — because MailMcp is deployed several ways — a section per deployment shape: the native systemd path with `LoadCredential=`, `LoadCredentialEncrypted=`, `systemd-creds`, and `$CREDENTIALS_DIRECTORY`; the container path with Compose `secrets:` mounted at `/run/secrets/<name>`; and the Kubernetes path with a Secret mounted as a read-only tmpfs volume — the last two both addressed as `file:`, with an explicit note that no container- or Kubernetes-specific scheme exists or is needed. It states the trailing-newline trimming and that it applies to text secrets only, the operational hardening that bounds in-memory exposure — `LimitCORE=0` on the systemd unit, `Storage=none` and `ProcessSizeMax=0` for `systemd-coredump`, and keeping the service out of swap in both the systemd and container shapes, with the honest statement that a dump or debugger can still read managed memory and that no code-level measure changes that — the three interpretation modes with `ReferenceOnly` as the default and `InlineOnly` as the Azure App Configuration path, the fact that inline values cannot be erased from memory, and the recommendation against `env:` in production. It also states, for each of the three modes, how the same setting is addressed by a flattening provider — `MailSynchronization:Accounts:0:Secrets:Password:SecretReference` in Azure App Configuration and its double-underscore form in an environment block — because that is the path an operator on a managed store actually configures.

A closing section states what a future managed-store provider would add — one `ISecretSchemeResolver`, one registration extension, its own timeouts and caching, platform identity rather than a MailMcp-held credential, and a `LICENSES.md` entry — so an operator reading the page can tell which schemes exist today from which are anticipated. It documents anticipated extension, not unimplemented behavior, and says so plainly; `docs/AGENTS.md` requires documentation to describe verified implemented behavior, and this section is explicitly labelled as the extension contract rather than as a feature.

- [x] **Step 2: Update the feature and development pages**

`docs/features/imap-synchronization.md` gets the new account JSON and the resolution and fail-fast behavior, and loses the now-resolved pending item on deployment-specific secret binding. Trust anchor loading stays listed as pending, because specification 02b delivers it. `docs/operations/local-development.md` gains the Development workflow: `plaintext:` in `appsettings.Development.json` or user secrets, and why neither is a production secret store.

- [x] **Step 3: Run the documentation and licensing gate**

Run `$check-docs-licenses`. No dependency changed, so the licensing verdict is `n/a`; the documentation verdict must be satisfied by Steps 1 and 2.

- [x] **Step 4: Run the full verification gate**

```bash
git add -A
bash scripts/verify-full.sh
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

Expected: build, format, workflow contract, complete unit-test suite, and the 85% whole-scope coverage gate all pass.

- [x] **Step 5: Review the change**

Run `$review-change`. Check specifically: no secret material in any message, log, or exception; no `Application` or `Domain` reference to a resolver, reference, or scheme; no certificate-validation opt-out and no callback returning `true` on an unexamined chain; no cached secret material outliving its use.

- [x] **Step 6: Finish**

Run `$finish-change`: commit, push, and open a draft pull request whose body contains `Closes #36`. Patch the body through `gh api repos/Krzysztof318/MailMcp/pulls/<number> -X PATCH -f body="$(cat body.md)"` because `gh pr edit` fails against this repository with a Projects-classic GraphQL error.

---

## Self-review

**Spec coverage**

| Specification requirement | Task |
| --- | --- |
| `systemd-credential:`, `file:`, `env:`, `plaintext:` schemes | 1, 2 |
| `plaintext:` rejected outside Development | 2 |
| Resolution returns a result rather than throwing | 1, 2 |
| Composite dispatch over per-scheme adapters | 2 |
| Unknown-scheme and missing-reference failures | 1, 2 |
| A new scheme is one adapter registration, no core edit | decisions 2 and 19, proved by the Task 2 extensibility test |
| Contract asynchronous and cancellable for a network provider | decision 4, Tasks 1–3, 6 |
| Provider concerns confined to the provider's adapter | decisions 19 and 20 |
| Resolver contract and adapters live in `Infrastructure` | 1–4 |
| `Host` invokes resolution once before hosted services start | 6 |
| No options type binds a raw password | 3, 4, 5 (verified by `grep` in Task 5 Step 6) |
| Failure message names account, logical secret, and scheme only | 3, 6 |
| Secrets exposed through an accessor, not an ordinary property | 1, 3 |
| Rotated material observed without restart | decision 16, Tasks 3 and 8 |
| Every secret-bearing setting is a `SecretReference` block in JSON | 3, 5, 6 |
| A plain-text value under `ReferenceOnly` fails startup naming its path | 6 |
| No secret-bearing setting can be declared as a raw `string` | 7 |
| Bytes-first resolution; PKCS#12 and DER representable | 1, 2 |
| Three interpretation modes; `ReferenceOnly` the default | decisions 13 and 14, Tasks 1 and 6 |
| Pre-resolving provider works with no adapter and no code | decision 13 (`InlineOnly`) |
| No `string`, pooled buffer, or `SecureString` holds allocated material | decisions 7, 8, 30, Task 1 |
| Residual `string` exposures documented rather than implied away | decision 31 |
| Material is bounded before allocation | decision 29, Task 2 |
| A resolution records adapter or inline provenance | decision 28, Task 1 |
| A parsed reference never prints its target | decision 12 applied to `SecretReference`, Task 1 |
| The resolved database password actually reaches Npgsql | Task 4 Step 2 |
| Pinned buffer zeroed with `CryptographicOperations.ZeroMemory` | 1 |
| Material owned by its operation and disposed at its end | decision 9, Tasks 3 and 6 |
| Core-dump and swap exposure documented for both shapes | 9 |
| Text view decodes UTF-8 and trims one trailing newline | 1 |
| Rotated material observed by the next operation, no restart | 8 |
| Trust anchor setting reshaped to a block without being read | 3 |
| Private server connects with validation fully enabled | 4 |
| No secret-resolution contract reachable from `Application` or `Domain` | 7 |
| Scheme adapters tested against in-memory file/credential abstractions | 2 |
| Failure results carry no secret material | 1, 2, 3 |
| `docs/operations/local-development.md` Development workflow | 8 |
| `docs/operations/` page for the systemd credential deployment path | 8 |
| 85% coverage gate | 8 |

**Deliberately out of scope:** certificate and private-key material loading, installing a trust anchor into the validation path, and secret rotation without restart — all three are specification 02b, which consumes this contract unchanged. Also out of scope: Data Protection key-ring provisioning, encrypted secret storage in PostgreSQL, and MCP client certificates (stage 9).

Managed secret stores — Kubernetes-native APIs, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager — are out of scope as implementations but not as constraints. Decisions 2, 4, 19, and 20 exist for them, and the Task 2 extensibility test is what keeps the promise honest. Note that Kubernetes and container deployments need nothing added at all: their secrets are files, so `file:` already serves them today.

**Flagged for the owner**

- The combined specification reached ~1700 lines against `specs/README.md`'s ~1000-line ceiling and has been split. This plan covers 02a at roughly 950 lines including tests and documentation; certificate material and rotation are ~750 lines in the 02b plan. Nothing is deferred outside the two specifications, and nothing depended on `02`, so the split cost no renumbering of specifications 03 onward. Issue #36's `Size` line should be updated when this plan is accepted.
- Decision 25 renames `MailAccountTransportSecurityOptions.TrustedCertificateAuthorityReference` to `TrustedCertificateAuthority` and changes it from a string to a block. That is a breaking configuration change to a setting specification 01 already shipped, and it is proposed now precisely because it is cheap now — one consumer, no released deployment — and expensive later. An affected operator edits one line.
- Decision 26's discovery walk uses reflection over the bound options graph, which the root rules restrict to measured need. The need argued here is that an explicit call list cannot cover a secret-bearing setting added after this ships, which is the entire value of the marker. It runs once at startup over a small object. If you would rather keep reflection out and accept that each new secret setting must be registered by hand, `ConfiguredSecretDiscovery` collapses into an explicit list and decision 26's guarantee weakens from structural to procedural.
- The Codex review on PR #59 found the database-password ordering trap (registration runs before `StartingAsync`, so an "already-resolved password" parameter would have stayed omitted while everything still validated green), the un-erased intermediate read buffer, the missing provenance value, and `SecretReference.ToString()` printing a `plaintext:` secret verbatim. All four are folded in above. The provenance one is worth the owner's attention because it is a contract change made *for* 02b in 02a — the alternative was 02b modifying a shipped contract, which its plan forbids.
- Decision 15 reads "permitted for non-production automation" as guidance rather than an enforced environment gate for `env:`. If it should be enforced, say so and it moves onto the same Development flag as `plaintext:`.
- Decision 4 makes the contract asynchronous before any asynchronous provider exists. The cost is real and visible: `GetSettings` becomes `GetSettingsAsync`, and startup validation moves out of `IValidateOptions` into `IHostedLifecycleService`. It is proposed because retrofitting it alongside a first Key Vault adapter would touch the same files while also introducing SDK, identity, and licensing questions, and doing one of those at a time is cheaper. If managed stores are further off than this assumes, the synchronous contract is defensible and this is the decision to revisit.
