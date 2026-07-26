# Secret Reference Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Governing issue:** [#36 — Spec 02 — Secret reference resolution](https://github.com/Krzysztof318/MailMcp/issues/36)
**Governing specification:** [`specs/02-secret-reference-resolution.md`](../../../specs/02-secret-reference-resolution.md)
**Architectural context:** `specs/2026-07-22-mail-mcp-architecture-draft.md` sections 7.3 and 19, ADR `docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md`
**Depends on:** [#35 — Spec 01](https://github.com/Krzysztof318/MailMcp/issues/35), merged as `a732e9a`

**Goal:** Replace every raw secret bound from configuration with a scheme-qualified reference that `Infrastructure` resolves and `Host` fails fast on, and load the trust anchor material that specification 01 deliberately left as configuration shape only.

**Architecture:** `Infrastructure` owns the reference grammar, the per-scheme adapters, the composite dispatch, and the certificate-validation path that consumes a resolved trust anchor. `Host` binds reference strings, invokes resolution once before hosted services start, and turns failures into startup errors. `Application` and `Domain` gain nothing: they never see a resolver, a reference, or a scheme, which is the boundary ADR 0002 draws when it forbids normalizing broad secret access into application code.

The shape is chosen so that a future Kubernetes, Azure Key Vault, HashiCorp Vault, or AWS Secrets Manager adapter is a registration rather than a refactor: a scheme is declared by the adapter that serves it, dispatch is a lookup, and the contract is asynchronous and cancellable from the first commit. None of those providers is implemented here.

**Tech Stack:** .NET 10, C# preview, MailKit 4.17.0, Npgsql 10.0.3 (transitive through `Npgsql.EntityFrameworkCore.PostgreSQL`), xUnit.net v3 on Microsoft Testing Platform v2, NSubstitute 6.0.0.

## Global Constraints

- No new third-party packages. `LICENSES.md` therefore needs no dependency entry; `$check-docs-licenses` returns `n/a` for licensing and must still be run.
- `Domain` and `Application` stay free of every type introduced here. The architecture test in Task 7 is the enforcement, not a review habit.
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
9. **A resolved secret is owned by the operation that resolved it and disposed when that operation ends.** Combined with per-use resolution this bounds the exposure window to one synchronization run or one connection attempt rather than to process uptime. It also removes a lifetime hazard from dynamic reload: because every operation owns its own instance, publishing a new configuration snapshot never disposes material that an in-flight operation is still reading. `MailAccountSecrets` therefore implements `IDisposable` and disposes the secrets it owns, and `X509Certificate2` — itself disposable — is disposed on the same path.
10. **The residual exposures are documented, not papered over.** Managed memory is still readable through a process dump, a debugger, or swap, and no code-level measure changes that. The mitigations are operational and belong in the operations documentation: `LimitCORE=0` on the systemd unit and `Storage=none` / `ProcessSizeMax=0` for `systemd-coredump`, plus keeping the service's memory out of swap in both the systemd and container shapes. `mlock` is deliberately rejected — it needs P/Invoke plus `CAP_IPC_LOCK` and a raised `RLIMIT_MEMLOCK` in every deployment shape, against the repository rule restricting unsafe and platform-invoke code to measured need, and it does not address dumps or debuggers anyway.
11. **Typed material is loaded above the resolver, not inside it.** `X509CertificateMaterialLoader` in `Infrastructure` turns resolved bytes into an `X509Certificate2`, accepting PEM and DER and, for key material, PKCS#12 with an optional separately-referenced password. The resolver stays ignorant of X.509 so that adding a future material kind — an SSH key, a JWT signing key — adds a loader rather than touching every scheme adapter.
12. **`ResolvedSecret` hides its material behind a method, not a property.** `System.Text.Json`, `record` synthesized `ToString()`, and most diagnostic dumps enumerate properties, so a property named `Value` would eventually be serialized by something. `ImapAccountSettings` is a `record`, which makes this concrete: its synthesized `ToString()` prints every member today. `RevealBytes()` and `RevealAsString()` are methods for that reason, and `ToString()` is overridden to return `***`.
13. **Interpretation is an explicit three-valued mode, not an environment gate.** `SecretValueInterpretation` is `ReferenceOnly = 0` (default), `ReferenceOrInline = 1`, `InlineOnly = 2`. This replaces the earlier "`plaintext:` only in Development" rule, which turned out to be both too strict and too weak: too strict because an operator may knowingly put a secret in JSON and that is their call, and too weak because it does not address the real driver — a configuration provider that has already resolved the secret before MailMcp binds it. Azure App Configuration with Key Vault references is exactly that: the provider substitutes the vault value, so the bound setting is the raw secret with no prefix MailMcp could recognize. `InlineOnly` serves it by parsing nothing at all, which removes the ambiguity rather than guessing at it. The mode is passed to the composite resolver at registration, so `Infrastructure` still needs no reference to `Microsoft.Extensions.Hosting.Abstractions` and the rule stays trivially testable in all three directions.
14. **Inline modes are logged, and their memory cost is stated.** Startup logs the active mode and, when it is not `ReferenceOnly`, the *names* of settings that resolved to an inline value — never the values — so an unintended inline secret is discoverable instead of silent. And an inline value arrives from `IConfiguration` as a `string`, which decision 7 establishes cannot be erased; the inline modes therefore forfeit part of the in-memory protection, which the operations documentation must say plainly. Both facts are why `ReferenceOnly` stays the default rather than the friendliest option winning.
15. **`env:` resolves in every environment.** The specification gates exactly one scheme on the environment, and the definition of done names only the `plaintext:` rule. `env:` stays permitted with a documented recommendation against production use, rather than inventing a second environment gate the specification did not ask for. *Flagged for the owner: if `env:` should also fail outside Development, it is a one-line change to the same flag.*
16. **Resolution runs at startup validation and again per use, which is what makes rotation work.** Startup validation resolves every reference and discards the material, so an unresolvable reference fails the host. Each actual use resolves again, so no long-lived copy exists and material rotated behind an unchanged reference — a replaced credential file, a re-issued vault entry — is observed by the next operation with no cache to invalidate and no restart. The material is a few hundred bytes from a tmpfs or an environment block; per-run re-reading is not a measured cost. A network-backed adapter for which per-use retrieval is too expensive caches inside itself under decision 19, without changing this.
17. **Secrets are reloadable for new operations, not during running operations.** Both halves reload: a changed *reference* arrives through `IOptionsMonitor` and is validated by resolving the whole candidate snapshot before publishing it, with the last known good snapshot retained on failure; changed *material* is picked up by decision 10. Neither is applied mid-operation — a synchronization run that has authenticated finishes with the credential it authenticated with, because swapping a credential or trust anchor underneath an open IMAP session has no coherent meaning. This is ADR 0002's middle classification, chosen deliberately over the strictest one.
18. **ADR 0002 must be amended before this is implemented.** The ADR currently classifies credentials and certificate trust anchors as restart-required, and decision 17 departs from that. The departure is defensible — the reference indirection means rotation re-resolves a validated reference rather than mutating a bound secret in place — but `docs/AGENTS.md` forbids modifying an ADR without explicit owner approval, so no ADR is touched here. **This is a blocking prerequisite, not a follow-up.**
19. **Provider-specific concerns stay inside the provider's adapter.** Timeouts, retry, backoff, endpoint and region configuration, SDK client lifetime, platform identity, and caching policy belong to the adapter that needs them, never to `ISecretReferenceResolver`. The contract carries a reference and a cancellation token and returns a result; that is the whole surface. This is what lets a store that must cache aggressively and a local file that must never cache coexist without the contract taking a position, and it keeps SDK types out of everything above the adapter exactly as the root dependency rules require.
20. **A managed-store adapter authenticates through platform identity, not through a MailMcp secret.** An Azure managed identity, a Kubernetes ServiceAccount token, or a Vault role is issued by the platform the process already runs on. Requiring MailMcp to hold a credential in order to fetch its credentials would be circular and would put the most sensitive value back into the configuration this specification removes it from. Recorded now so a future adapter does not quietly reintroduce the problem; nothing implements it here.
21. **A custom trust anchor is validated by rebuilding the chain, never by accepting errors.** `RemoteCertificateNotAvailable` and `RemoteCertificateNameMismatch` are rejected outright. Only `RemoteCertificateChainErrors` is re-examined, by building an `X509Chain` with `TrustMode = X509ChainTrustMode.CustomRootTrust` and the anchor in `CustomTrustStore` — which Microsoft documents as respected only under that trust mode — and requiring a clean rebuild. MailKit's own `SslCertificateValidation.cs` example stops at describing errors and does not implement custom-CA trust, so this logic is ours to write and ours to test.
22. **The database password becomes a reference too.** The specification's goal names it explicitly. `ConnectionStrings:mailmcp` keeps host, database, and user name; an optional `Persistence:PasswordReference` is resolved at startup and applied through `NpgsqlConnectionStringBuilder.Password`, so the connection string in `appsettings.json` never carries the password and the composed string never reaches a log.
23. **Two resolver names, two scopes.** `ISecretReferenceResolver` resolves one reference. `MailAccountSecretOptions` resolves one account's set of secrets and reports per-account configuration errors. Naming them both "secret resolver" would make the call sites ambiguous, which the root naming rules forbid.
24. **`UserName` stays a plain configuration value.** The definition of done names raw *passwords*. A mailbox user name is an identifier the operator already writes next to the host, and turning it into a reference would double the provisioning burden for no confidentiality gain. It remains excluded from logs as personal data.

## File structure

**Create**

- `src/Infrastructure/Secrets/SecretReferenceScheme.cs` — the open scheme value and the four well-known members.
- `src/Infrastructure/Secrets/SecretReference.cs` — parsed scheme and target, with the parse rules of decision 1.
- `src/Infrastructure/Secrets/SecretResolutionFailure.cs` — machine-readable failure identities.
- `src/Infrastructure/Secrets/SecretResolutionResult.cs` — success carrying `ResolvedSecret`, or a failure identity.
- `src/Infrastructure/Secrets/ResolvedSecret.cs` — pinned buffer, `RevealBytes()`/`RevealAsString()`, zeroing `Dispose`, redacted `ToString()`.
- `src/Infrastructure/Secrets/ISecretReferenceResolver.cs`, `ISecretSchemeResolver.cs` — the one-reference contract and the public per-scheme extension point.
- `src/Infrastructure/Secrets/ISecretFileReader.cs`, `IEnvironmentVariableReader.cs` — the two in-memory-testable ports.
- `src/Infrastructure/Secrets/FileSystemSecretFileReader.cs`, `ProcessEnvironmentVariableReader.cs` — the real adapters.
- `src/Infrastructure/Secrets/X509CertificateMaterialLoader.cs` — PEM, DER, and PKCS#12 loading over resolved bytes.
- `src/Infrastructure/Secrets/SystemdCredentialSecretReferenceResolver.cs`, `FileSecretReferenceResolver.cs`, `EnvironmentVariableSecretReferenceResolver.cs`, `PlaintextSecretReferenceResolver.cs`, `CompositeSecretReferenceResolver.cs`
- `src/Infrastructure/Mail/MailAccountSecretOptions.cs` — the account's reference strings and their resolution into `MailAccountSecrets`.
- `src/Infrastructure/Mail/MailAccountSecrets.cs` — resolved password plus optional trust anchor.
- `src/Infrastructure/Mail/MailServerCertificateValidator.cs` — the custom-root-trust validation callback.
- `tests/Infrastructure.UnitTests/SecretReferenceTests.cs`
- `tests/Infrastructure.UnitTests/SecretReferenceResolverTests.cs`
- `tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs`
- `tests/Infrastructure.UnitTests/MailServerCertificateValidatorTests.cs`
- `tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs`
- `src/Host/Hosting/MailSecretReferenceStartupValidator.cs` — `IHostedLifecycleService.StartingAsync` fail-fast resolution.
- `docs/operations/secret-provisioning.md`

**Modify**

- `src/Infrastructure/Mail/ImapAccountSettings.cs` — `Password` becomes `ResolvedSecret`; the record gains the trust anchor; `IImapAccountSettingsProvider.GetSettings` becomes `GetSettingsAsync`.
- `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs` — the client port carries the validation callback; the factory installs it before connecting and reveals the password at the single `AuthenticateAsync` call.
- `src/Infrastructure/ServiceCollectionExtensions.cs` — `AddMailMcpSecretResolution`, and the resolved database password applied to the connection string.
- `src/Host/Configuration/MailSynchronizationOptions.cs` — `Password` becomes a nested `Secrets` section; `GetSettings` becomes `GetSettingsAsync` and resolves.
- `src/Host/Configuration/PersistenceOptions.cs` — optional `PasswordReference`.
- `src/Host/Program.cs` — register secret resolution with the configured interpretation mode, register the startup validator ahead of the worker, compose the connection string.
- `src/Host/appsettings.json`, `src/Host/appsettings.Development.json` — reference-shaped examples.
- `docs/features/imap-synchronization.md` — configuration, the two resolved pending items, transport-security trust anchor behavior.
- `docs/operations/local-development.md` — the Development secret workflow.

---

### Task 1: The reference grammar and the resolution result

**Files:**
- Create: `src/Infrastructure/Secrets/SecretReferenceScheme.cs`, `SecretReference.cs`, `SecretResolutionFailure.cs`, `SecretResolutionResult.cs`, `ResolvedSecret.cs`
- Test: `tests/Infrastructure.UnitTests/SecretReferenceTests.cs`

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
}

public sealed record SecretReference
{
    public SecretReferenceScheme Scheme { get; }
    public string Target { get; }

    public static bool TryParse(string? reference, out SecretReference? parsed, out SecretResolutionFailure failure);
}
```

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

public sealed record SecretResolutionResult
{
    public bool Succeeded { get; }
    public ResolvedSecret? Secret { get; }
    public SecretResolutionFailure? Failure { get; }

    public static SecretResolutionResult Resolved(ResolvedSecret secret);
    public static SecretResolutionResult Failed(SecretResolutionFailure failure);
}
```

- [ ] **Step 1: Write the failing tests**

`tests/Infrastructure.UnitTests/SecretReferenceTests.cs`, one behavior per test:

```csharp
[Theory]  // "systemd-credential:imap" => SystemdCredential, "file:/run/secrets/imap" => File, ...
TryParse_SupportedScheme_ParsesSchemeAndTarget
[Fact]    TryParse_TargetContainsColon_KeepsEverythingAfterTheFirstColon        // "file:C:\\secrets\\imap" => "C:\\secrets\\imap"
[Fact]    TryParse_MixedCaseScheme_ParsesScheme                                 // "File:/run/secrets/imap"
[Fact]    TryParse_SurroundingWhitespace_TrimsBeforeParsing
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
```

The last two matter more than they look: they are the regression guard for decision 4.

- [ ] **Step 2: Run the tests and confirm they fail to compile**

Run: `dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj`
Expected: FAIL — `SecretReference` does not exist.

- [ ] **Step 3: Implement**

`TryParse` trims, rejects blank, splits on `IndexOf(':')`, normalizes the scheme name to lower case with `ToLowerInvariant`, and rejects an empty remainder. It does not check the name against a list: an unknown scheme is a well-formed reference to a provider that is not registered, and decision 3 puts that answer in the dispatch. `SecretReferenceScheme` compares by normalized name with `StringComparer.Ordinal`, and its well-known members are the four wire names. The member name `EnvironmentVariable` and the wire name `env` deliberately differ, because the configuration prefix should stay short while the member name stays explicit.

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

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Commit**

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
    bool TryReadAllBytes(string path, out ReadOnlyMemory<byte> material);
}

internal interface IEnvironmentVariableReader
{
    string? GetValue(string name);
}
```

`ISecretSchemeResolver` is `public` — unusually for this repository, which defaults to `internal` — because it is the extension point decision 2 promises. A provider adapter in another folder, another project, or a later change set implements it, and an `internal` contract would make that impossible without editing this file. The four adapters implementing it here stay `internal sealed`. The two reader ports stay `internal`: they exist for testability, not for extension, and `InternalsVisibleTo MailMcp.Infrastructure.UnitTests` already covers them.

- [ ] **Step 1: Write the failing tests**

`SecretReferenceResolverTests` uses hand-written in-memory fakes — a dictionary-backed `ISecretFileReader` and `IEnvironmentVariableReader` — never the real file system or environment block:

```csharp
[Fact] ResolveAsync_SystemdCredential_ReadsTheNameFromTheCredentialsDirectory
[Fact] ResolveAsync_SystemdCredentialWithoutCredentialsDirectory_FailsWithCredentialsDirectoryUnavailable
[Fact] ResolveAsync_SystemdCredentialNameContainingPathSeparator_FailsWithTargetMissing   // no escaping the directory
[Fact] ResolveAsync_SystemdCredentialMaterialEndsWithNewline_TrimsTheTrailingNewline
[Fact] ResolveAsync_File_ReadsTheProvisionedFile
[Fact] ResolveAsync_FileMissing_FailsWithMaterialNotFound
[Fact] ResolveAsync_FileEmpty_FailsWithMaterialEmpty
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
[Theory] ResolveAsync_EveryFailure_ReturnsNoSecretMaterial
```

`ResolveAsync_SchemeAdapterRegisteredOnlyByTheTest_ResolvesThroughTheSameDispatch` is the extensibility proof the specification's definition of done requires: the test project declares an `ISecretSchemeResolver` for a scheme the production code has never heard of, registers it, and resolves a reference through it. If that test ever needs a production edit to pass, the extension point has regressed.

The path-separator test encodes a real hazard: `systemd-credential:../../etc/shadow` must not become a traversal. The trailing-newline test encodes the other: `LoadCredential=` and Compose secrets both commonly carry a file that ends with a newline, and an untrimmed `\n` produces an authentication failure that looks like a wrong password.

- [ ] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — the resolvers do not exist.

- [ ] **Step 3: Implement**

`SystemdCredentialSecretReferenceResolver` reads `CREDENTIALS_DIRECTORY` through `IEnvironmentVariableReader` — systemd documents that "the path to access them is derived from the environment variable `$CREDENTIALS_DIRECTORY`" and that access is restricted to the service's user — rejects a target containing `/`, `\`, or `..`, joins, and reads. `FileSecretReferenceResolver` reads the target directly, which is also the container path: Compose mounts secrets at `/run/secrets/<secret_name>`. Both read bytes and neither modifies them; the newline trim lives in `ResolvedSecret.RevealAsString()` so binary material is never touched, and both report `MaterialEmpty` when the material has zero length.

`PlaintextSecretReferenceResolver` returns the target verbatim; it carries no environment gate of its own, because decision 13 moved that responsibility to the interpretation mode.

`CompositeSecretReferenceResolver` takes the `SecretValueInterpretation` mode and branches before parsing. Under `InlineOnly` it never parses and wraps the whole value as material — the Azure App Configuration case, where `file:/x` may legitimately *be* the password. Under `ReferenceOnly` an unparseable value fails. Under `ReferenceOrInline` a recognized scheme dispatches and anything else becomes material. The `InlineOnly` branch must come first: parsing and then ignoring the result would leave a code path where a scheme-shaped password is silently treated as a reference.

`CompositeSecretReferenceResolver` parses once and dispatches on the scheme through a dictionary built from the injected `ISecretSchemeResolver` set, so a scheme with no registered adapter fails as `SchemeNotSupported` rather than throwing. Its XML documentation states that no result and no diagnostic derived from it may carry the material.

`FileSystemSecretFileReader` catches only `IOException` and `UnauthorizedAccessException` and maps them to `false`, so a permission error becomes `MaterialNotFound` rather than a startup crash with a path in the stack trace.

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/Secrets tests/Infrastructure.UnitTests/SecretReferenceResolverTests.cs
git commit -m "Resolve secret references through per-scheme adapters"
```

---

### Task 3: Account secrets replace the raw password

**Files:**
- Create: `src/Infrastructure/Mail/MailAccountSecretOptions.cs`, `src/Infrastructure/Mail/MailAccountSecrets.cs`
- Modify: `src/Infrastructure/Mail/ImapAccountSettings.cs`
- Test: `tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs`

**Interfaces produced:**

```csharp
public sealed record MailAccountSecretConfigurationError(string PropertyName, SecretResolutionFailure Failure);

public sealed class MailAccountSecretOptions
{
    public string PasswordReference { get; set; } = string.Empty;

    public Task<IReadOnlyList<MailAccountSecretConfigurationError>> FindConfigurationErrorsAsync(
        ISecretReferenceResolver resolver,
        string? trustedCertificateAuthorityReference,
        CancellationToken cancellationToken);

    public Task<MailAccountSecrets> ResolveAsync(
        ISecretReferenceResolver resolver,
        string? trustedCertificateAuthorityReference,
        CancellationToken cancellationToken);
}

public sealed record MailAccountSecrets(ResolvedSecret Password, X509Certificate2? TrustedCertificateAuthority) : IDisposable;

public sealed record ImapAccountSettings(
    string AccountId,
    string Host,
    int Port,
    string UserName,
    ResolvedSecret Password,
    X509Certificate2? TrustedCertificateAuthority);
```

The trust anchor reference is passed in rather than duplicated onto this type, because specification 01 already placed it on `MailAccountTransportSecurityOptions` and moving it now would churn a shipped configuration section for no gain.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] FindConfigurationErrorsAsync_ResolvableReferences_ReportsNoError
[Fact] FindConfigurationErrorsAsync_UnresolvablePasswordReference_ReportsTheFailureAgainstPasswordReference
[Fact] FindConfigurationErrorsAsync_MissingPasswordReference_ReportsReferenceMissing
[Fact] FindConfigurationErrorsAsync_UnresolvableTrustAnchorReference_ReportsTheFailureAgainstTheTrustAnchorSetting
[Fact] FindConfigurationErrorsAsync_TrustAnchorMaterialIsNotACertificate_ReportsMaterialNotFound
[Fact] FindConfigurationErrorsAsync_NoTrustAnchorConfigured_DoesNotConsultTheResolver
[Fact] FindConfigurationErrorsAsync_EveryError_CarriesNoSecretMaterial
[Fact] ResolveAsync_ResolvableReferences_ReturnsThePasswordAndTheParsedTrustAnchor
[Fact] ResolveAsync_PemTrustAnchor_LoadsTheCertificateSubject
```

The PEM test builds its certificate in memory with `CertificateRequest` and `CreateSelfSigned`, then exports it with `ExportCertificatePem()` — no file system, no network.

- [ ] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — `MailAccountSecretOptions` does not exist and `ImapAccountSettings.Password` is still a `string`.

- [ ] **Step 3: Implement**

`FindConfigurationErrorsAsync` resolves the password reference and, when a trust anchor reference is present, resolves and parses it, then discards both — decision 16's startup half. Trust anchor parsing uses `X509Certificate2.CreateFromPem` inside a `try`/`catch (CryptographicException)` that becomes a configuration error, so malformed material fails startup instead of failing the first TLS handshake hours later.

`Resolve` performs the same work and returns the values, and throws `InvalidOperationException` if a reference that validated at startup no longer resolves — a fail-closed path, not an ordinary branch.

`ImapAccountSettings` carries `ResolvedSecret` and the optional anchor. Its XML documentation gains a remark that the record's synthesized `ToString()` is safe only because `ResolvedSecret` redacts itself.

`MailAccountSecrets` implements `IDisposable` and disposes both the password and the `X509Certificate2`, which is itself disposable. Per decision 9 the instance is owned by the operation that resolved it: `MailKitImapMailboxSessionFactory.OpenReadOnlyAsync` disposes it once the client is authenticated and the folder is open, and the startup validator disposes everything it resolved before returning. `ImapAccountSettings` does *not* own the secrets — it is a carrier — so the ownership rule is stated in its XML documentation to stop a future caller from disposing it twice or not at all.

- [ ] **Step 4: Run the tests**

Expected: PASS for `Infrastructure.UnitTests`; the solution does not build until Task 6 updates `Host`.

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/Mail tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs
git commit -m "Bind account secrets as references and resolve them in Infrastructure"
```

---

### Task 4: Custom trust anchor in the certificate validation path

**Files:**
- Create: `src/Infrastructure/Mail/MailServerCertificateValidator.cs`
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs`
- Test: `tests/Infrastructure.UnitTests/MailServerCertificateValidatorTests.cs`

**Interfaces produced:**

```csharp
internal static class MailServerCertificateValidator
{
    internal static RemoteCertificateValidationCallback? CreateCallback(X509Certificate2? trustedCertificateAuthority);

    internal static bool IsTrusted(
        X509Certificate2? trustedCertificateAuthority,
        X509Certificate? serverCertificate,
        SslPolicyErrors sslPolicyErrors);
}
```

`IMailKitImapClient` gains `RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }`, forwarded by `MailKitImapClientAdapter` to `ImapClient.ServerCertificateValidationCallback`.

- [ ] **Step 1: Write the failing tests**

The fixture builds an in-memory CA and a leaf signed by it with `CertificateRequest`, so no file system, network, or real trust store is involved:

```csharp
[Fact] IsTrusted_NoPolicyErrors_TrustsWithoutConsultingTheAnchor
[Fact] IsTrusted_ChainErrorsAndLeafChainsToTheConfiguredAnchor_Trusts
[Fact] IsTrusted_ChainErrorsAndLeafChainsToADifferentAuthority_Rejects
[Fact] IsTrusted_ChainErrorsWithoutAConfiguredAnchor_Rejects
[Fact] IsTrusted_NameMismatch_RejectsEvenWhenTheAnchorMatches
[Fact] IsTrusted_CertificateNotAvailable_Rejects
[Fact] CreateCallback_NoAnchorConfigured_ReturnsNullSoTheDefaultValidationApplies
```

`IsTrusted_NameMismatch_RejectsEvenWhenTheAnchorMatches` is the test that stops this task from becoming a certificate-validation bypass.

- [ ] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — the validator does not exist.

- [ ] **Step 3: Implement**

```csharp
internal static bool IsTrusted(
    X509Certificate2? trustedCertificateAuthority,
    X509Certificate? serverCertificate,
    SslPolicyErrors sslPolicyErrors)
{
    if (sslPolicyErrors == SslPolicyErrors.None)
    {
        return true;
    }

    // Only an untrusted chain is reconsidered. A missing certificate proves nothing, and a name mismatch means the
    // certificate belongs to a different host, which a private trust anchor does not and must not excuse.
    if (trustedCertificateAuthority is null || sslPolicyErrors != SslPolicyErrors.RemoteCertificateChainErrors)
    {
        return false;
    }

    if (serverCertificate is not X509Certificate2 presentedCertificate)
    {
        return false;
    }

    using var chain = new X509Chain();
    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    chain.ChainPolicy.CustomTrustStore.Add(trustedCertificateAuthority);
    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

    return chain.Build(presentedCertificate);
}
```

`CustomTrustStore` is documented as respected only when `TrustMode` is `CustomRootTrust`, so both must be set together; setting the collection alone would silently keep the system trust store in effect. `VerificationFlags` is pinned to `NoFlag` so no future edit can relax expiry or basic-constraint checking. `RevocationMode` is `NoCheck` because a private authority typically publishes no reachable CRL or OCSP responder, and a revocation lookup that cannot complete would otherwise reject a correctly provisioned server — this is a deliberate, documented trade-off confined to the custom-anchor path, and the system trust path keeps whatever the platform default applies.

The session factory installs the callback before connecting and reveals the password once:

```csharp
client.ServerCertificateValidationCallback =
    MailServerCertificateValidator.CreateCallback(settings.TrustedCertificateAuthority);

await client.ConnectAsync(...);
...
await client.AuthenticateAsync(settings.UserName, settings.Password.RevealAsString(), cancellationToken);
```

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/Mail tests/Infrastructure.UnitTests/MailServerCertificateValidatorTests.cs
git commit -m "Validate a private mail server against its configured trust anchor"
```

---

### Task 5: Database password reference

**Files:**
- Modify: `src/Host/Configuration/PersistenceOptions.cs`, `src/Infrastructure/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the optional reference**

`PersistenceOptions` gains `string? PasswordReference`. When it is null the connection string is used unchanged, which keeps trust-authentication and Aspire-provided connection strings working untouched.

- [ ] **Step 2: Apply it when composing the connection string**

`AddMailMcpInfrastructure` gains an optional resolved password parameter and applies it through `NpgsqlConnectionStringBuilder`:

```csharp
var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
if (databasePassword is { } password)
{
    connectionStringBuilder.Password = password.RevealAsString();
}
```

The composed string is never logged and never returned. An unresolvable reference fails startup through the same validator as the mail secrets.

- [ ] **Step 3: Commit**

```bash
git add src/Host/Configuration/PersistenceOptions.cs src/Infrastructure/ServiceCollectionExtensions.cs
git commit -m "Resolve the database password from a secret reference"
```

---

### Task 6: Host binding, wiring, and fail-fast validation

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs`, `src/Host/appsettings.json`, `src/Host/appsettings.Development.json`

New account configuration shape:

```jsonc
{
  "AccountId": "primary",
  "Host": "imap.example.test",
  "Port": 993,
  "UserName": "mailmcp@example.test",
  "Secrets": { "PasswordReference": "systemd-credential:imap-primary-password" },
  "TransportSecurity": { "ConnectionSecurity": "TlsOnConnect" },
  "Folders": [ "INBOX" ]
}
```

- [ ] **Step 1: Replace the raw password**

`MailSynchronizationAccountOptions` drops `Password` and gains `MailAccountSecretOptions Secrets { get; set; } = new();`, mirroring the `TransportSecurity` nesting specification 01 established. The `Password is required` validation rule becomes a reference rule reported by `MailAccountSecretOptions`.

- [ ] **Step 2: Resolve at the settings boundary**

`IImapAccountSettingsProvider.GetSettings` becomes `GetSettingsAsync(string accountId, CancellationToken cancellationToken)` per decision 4, and `MailSynchronizationOptions` takes `ISecretReferenceResolver` to satisfy it. `MailKitImapMailboxSessionFactory.OpenReadOnlyAsync` is already asynchronous and already holds the token, so the call site changes by one `await`.

- [ ] **Step 3: Fail fast on an unresolvable reference**

Add `MailSecretReferenceStartupValidator : IHostedLifecycleService` in `src/Host/Hosting/`, injected with the options and the resolver. `StartingAsync` calls `MailAccountSecretOptions.FindConfigurationErrorsAsync` for every configured account plus the database reference, and throws `OptionsValidationException` listing every failure at once:

```
Account 'primary': the PasswordReference could not be resolved [MaterialNotFound].
```

The account, the setting, and the failure identity — no path, no variable name, no material. Reporting every failure together matters when an operator provisions five accounts and mistypes two names; one-at-a-time discovery costs five restarts.

`StartingAsync` is documented to run before any hosted service's `StartAsync`, so the synchronization worker never starts against an unresolvable secret. The remaining four lifecycle members are empty. The type stays a thin translation because `Host` is excluded from coverage; every rule it reports comes from Task 3.

- [ ] **Step 4: Wire the resolver**

```csharp
builder.Services.AddMailMcpSecretResolution(builder.Configuration.GetValue("Secrets:Interpretation", SecretValueInterpretation.ReferenceOnly));
builder.Services.AddHostedService<MailSecretReferenceStartupValidator>();
```

`AddMailMcpSecretResolution` is a focused extension owned by `Infrastructure`, per the `src/AGENTS.md` rule that registration lives with the implementation. It registers the two reader ports, the four scheme adapters, and the composite with its interpretation mode. The default is `ReferenceOnly`, so a deployment that says nothing gets the safe behavior. A future provider adapter adds its own `AddMailMcpAzureKeyVaultSecrets(...)` extension next to this call and needs no edit here — the composite resolves whatever `ISecretSchemeResolver` registrations it is handed, which is decision 2 made concrete in the container.

The validator is registered before the synchronization worker so hosted-service ordering reinforces `StartingAsync` ordering rather than depending on it alone.

- [ ] **Step 5: Update the shipped configuration examples**

`appsettings.json` keeps an empty account list, documents the reference shape by example only, and leaves `Secrets:Interpretation` unset so the default `ReferenceOnly` applies. `appsettings.Development.json` sets `ReferenceOrInline` and shows a `plaintext:` example, which is what makes local development convenient without weakening the shipped default. No real credential appears in either file.

- [ ] **Step 6: Verify the removal is complete**

```bash
dotnet build MailMcp.slnx
grep -rn "\"Password\"" src/ --include=*.cs --include=*.json
```

Expected: build succeeds; no options type binds a raw password — the specification's first definition-of-done item.

- [ ] **Step 7: Commit**

```bash
git add src/Host
git commit -m "Bind mail and database secrets as references and fail startup when one is unresolvable"
```

---

### Task 7: Architecture test for the boundary

**Files:**
- Create: `tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs`

- [ ] **Step 1: Write the test**

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

No architecture-test package is added: introducing one would require a license review and owner approval for a rule that two lines of reflection already prove. The test lives in `Infrastructure.UnitTests` because that is the only unit-test project referencing all three assemblies.

- [ ] **Step 2: Run and commit**

```bash
dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
git add tests/Infrastructure.UnitTests/SecretBoundaryArchitectureTests.cs
git commit -m "Assert secret resolution stays out of Domain and Application"
```

---

### Task 8: Dynamic reload of references and material

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs`, `src/Infrastructure/Mail/MailAccountSecretOptions.cs`
- Create: `src/Host/Configuration/MailSecretSnapshotPublisher.cs`
- Test: `tests/Infrastructure.UnitTests/MailAccountSecretReloadTests.cs`

Decision 18 makes this a blocking prerequisite: **do not start this task until ADR 0002 has been amended with owner approval.** The ADR classifies credentials and trust anchors as restart-required, and this task deliberately reclassifies them as reloadable for new operations.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] ReloadAsync_ReferenceChangedToAResolvableTarget_PublishesTheNewSecrets
[Fact] ReloadAsync_CandidateSnapshotHasAnUnresolvableReference_KeepsThePreviousSecrets
[Fact] ReloadAsync_RejectedSnapshot_LogsTheAccountAndFailureWithoutMaterial
[Fact] ResolveAsync_MaterialRotatedBehindAnUnchangedReference_ReturnsTheRotatedValue
[Fact] ResolveAsync_MaterialRotatedMidOperation_DoesNotAffectTheOperationInFlight
```

The last two are the pair that distinguishes the two reload halves, and the last one is what keeps decision 17's "not during running operations" honest.

- [ ] **Step 2: Bind for reload**

`Host` switches the mail options consumer from `IOptions<MailSynchronizationOptions>` to `IOptionsMonitor<MailSynchronizationOptions>`. Note that `IOptions<T>` is a singleton captured once, so leaving it in place would silently defeat the reference half of this task.

- [ ] **Step 3: Validate the candidate before publishing it**

`MailSecretSnapshotPublisher` subscribes to `OnChange`, resolves every reference in the candidate snapshot, and publishes an immutable snapshot only when all of them resolve. On failure it retains the previous snapshot and logs the account, the setting, and the `SecretResolutionFailure` — never a path, a variable name, or material. This is the last-known-good behavior ADR 0002 requires of any reloadable group.

Reload validation must not crash the process: ADR 0002 is explicit that a rejected reload leaves the previous valid settings active, unlike a startup failure which is fatal.

- [ ] **Step 4: Resolve material per operation**

No change is needed for the material half — decision 16 already resolves per use, which is what makes rotation behind an unchanged reference work. The test in Step 1 is what proves it, and it exists to catch a future "optimization" that caches the resolved value on the options instance.

- [ ] **Step 5: Run the tests and commit**

```bash
dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
git add src/Host src/Infrastructure tests/Infrastructure.UnitTests/MailAccountSecretReloadTests.cs
git commit -m "Reload secret references and material without restarting the host"
```

---

### Task 9: Documentation and full verification

**Files:**
- Create: `docs/operations/secret-provisioning.md`
- Modify: `docs/features/imap-synchronization.md`, `docs/operations/local-development.md`

- [ ] **Step 1: Write the provisioning page**

`docs/operations/secret-provisioning.md` documents the reference grammar, the four schemes, and — because MailMcp is deployed several ways — a section per deployment shape: the native systemd path with `LoadCredential=`, `LoadCredentialEncrypted=`, `systemd-creds`, and `$CREDENTIALS_DIRECTORY`; the container path with Compose `secrets:` mounted at `/run/secrets/<name>`; and the Kubernetes path with a Secret mounted as a read-only tmpfs volume — the last two both addressed as `file:`, with an explicit note that no container- or Kubernetes-specific scheme exists or is needed. It states the trailing-newline trimming and that it applies to text secrets only, the accepted certificate encodings, the operational hardening that bounds in-memory exposure — `LimitCORE=0` on the systemd unit, `Storage=none` and `ProcessSizeMax=0` for `systemd-coredump`, and keeping the service out of swap in both the systemd and container shapes, with the honest statement that a dump or debugger can still read managed memory and that no code-level measure changes that — the three interpretation modes with `ReferenceOnly` as the default and `InlineOnly` as the Azure App Configuration path, the fact that inline values cannot be erased from memory, the recommendation against `env:` in production, and the ADR 0002 classification from decision 17.

A closing section states what a future managed-store provider would add — one `ISecretSchemeResolver`, one registration extension, its own timeouts and caching, platform identity rather than a MailMcp-held credential, and a `LICENSES.md` entry — so an operator reading the page can tell which schemes exist today from which are anticipated. It documents anticipated extension, not unimplemented behavior, and says so plainly; `docs/AGENTS.md` requires documentation to describe verified implemented behavior, and this section is explicitly labelled as the extension contract rather than as a feature.

- [ ] **Step 2: Update the feature and development pages**

`docs/features/imap-synchronization.md` gets the new account JSON, the resolution and fail-fast behavior, the trust-anchor behavior from Task 4 including the revocation trade-off, and loses the two now-resolved pending items — deployment-specific secret binding, and trust anchor loading. `docs/operations/local-development.md` gains the Development workflow: `plaintext:` in `appsettings.Development.json` or user secrets, and why neither is a production secret store.

- [ ] **Step 3: Run the documentation and licensing gate**

Run `$check-docs-licenses`. No dependency changed, so the licensing verdict is `n/a`; the documentation verdict must be satisfied by Steps 1 and 2.

- [ ] **Step 4: Run the full verification gate**

```bash
git add -A
bash scripts/verify-full.sh
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

Expected: build, format, workflow contract, complete unit-test suite, and the 85% whole-scope coverage gate all pass.

- [ ] **Step 5: Review the change**

Run `$review-change`. Check specifically: no secret material in any message, log, or exception; no `Application` or `Domain` reference to a resolver, reference, or scheme; no certificate-validation opt-out and no callback returning `true` on an unexamined chain; no cached secret material outliving its use.

- [ ] **Step 6: Finish**

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
| No options type binds a raw password | 3, 5, 6 (verified by `grep` in Task 6 Step 6) |
| Failure message names account, logical secret, and scheme only | 3, 6 |
| Secrets exposed through an accessor, not an ordinary property | 1, 3 |
| Rotated material observed without restart | decision 16, Tasks 3 and 8 |
| Every secret-bearing setting is a `*Reference` string in JSON | 3, 5, 6 |
| Bytes-first resolution; PKCS#12 and DER representable | 1, 2 |
| Three interpretation modes; `ReferenceOnly` the default | decisions 13 and 14, Tasks 1 and 6 |
| Pre-resolving provider works with no adapter and no code | decision 13 (`InlineOnly`) |
| No `string`, pooled buffer, or `SecureString` holds material | decisions 7 and 8, Task 1 |
| Pinned buffer zeroed with `CryptographicOperations.ZeroMemory` | 1 |
| Material owned by its operation and disposed at its end | decision 9, Tasks 3 and 6 |
| Core-dump and swap exposure documented for both shapes | 9 |
| Text view decodes UTF-8 and trims one trailing newline | 1 |
| Rotated material observed by the next operation, no restart | 8 |
| Reload with an unresolvable reference keeps previous secrets | 8 |
| Trust anchor loaded, validated as usable, and installed in the validation path | 3, 4 |
| Malformed trust anchor fails startup | 3 |
| Private server connects with validation fully enabled | 4 |
| No secret-resolution contract reachable from `Application` or `Domain` | 7 |
| Scheme adapters tested against in-memory file/credential abstractions | 2 |
| Failure results carry no secret material | 1, 2, 3 |
| `docs/operations/local-development.md` Development workflow | 8 |
| `docs/operations/` page for the systemd credential deployment path | 8 |
| 85% coverage gate | 8 |

**Deliberately out of scope:** Data Protection key-ring provisioning, encrypted secret storage in PostgreSQL, secret rotation without restart, and MCP client certificates (stage 9).

Managed secret stores — Kubernetes-native APIs, Azure Key Vault, HashiCorp Vault, AWS Secrets Manager — are out of scope as implementations but not as constraints. Decisions 2, 4, 19, and 20 exist for them, and the Task 2 extensibility test is what keeps the promise honest. Note that Kubernetes and container deployments need nothing added at all: their secrets are files, so `file:` already serves them today.

**Flagged for the owner**

- **Blocking:** ADR 0002 must be amended before Task 8 begins (decision 18). The dynamic-reload requirement contradicts the ADR's current restart-required classification for credentials and trust anchors, and `docs/AGENTS.md` forbids amending an ADR without explicit owner approval. Tasks 1–7 and 9 are unaffected.
- **Blocking:** the specification is now estimated at ~1300 lines against `specs/README.md`'s ~1000-line ceiling, and `specs/02` records a proposed 02a/02b split. Whether to split — and therefore whether this plan stays one plan — is the owner's call, because renumbering touches the roadmap board and the dependency chain from specification 03 onward.
- The specification originally estimated ~500 lines. Loading and validating the trust anchor (Task 4), the database password reference (Task 5), and the asynchronous contract that ripples through `IImapAccountSettingsProvider` and startup validation (decisions 4 and 5) realistically bring this to roughly 850 lines including tests and documentation. Nothing is proposed for deferral; the estimate is what is optimistic. Issue #36's `Size` line should be updated when this plan is accepted.
- Decision 15 reads "permitted for non-production automation" as guidance rather than an enforced environment gate for `env:`. If it should be enforced, say so and it moves onto the same Development flag as `plaintext:`.
- Decision 4 makes the contract asynchronous before any asynchronous provider exists. The cost is real and visible: `GetSettings` becomes `GetSettingsAsync`, and startup validation moves out of `IValidateOptions` into `IHostedLifecycleService`. It is proposed because retrofitting it alongside a first Key Vault adapter would touch the same files while also introducing SDK, identity, and licensing questions, and doing one of those at a time is cheaper. If managed stores are further off than this assumes, the synchronous contract is defensible and this is the decision to revisit.
