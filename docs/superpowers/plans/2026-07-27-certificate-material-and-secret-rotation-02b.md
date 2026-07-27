# Certificate Material and Secret Rotation Implementation Plan (02b)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Governing issue:** [#63 — Spec 02b — Certificate material and secret rotation](https://github.com/Krzysztof318/MailMcp/issues/63)
**Governing specification:** [`specs/02b-certificate-material-and-secret-rotation.md`](../../../specs/02b-certificate-material-and-secret-rotation.md)
**Architectural context:** `specs/2026-07-22-mail-mcp-architecture-draft.md` sections 7.3 and 19, ADR `docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md`
**Depends on:** [#36 — Spec 02a](https://github.com/Krzysztof318/MailMcp/issues/36) and its plan, [`2026-07-27-secret-reference-resolution-02a.md`](2026-07-27-secret-reference-resolution-02a.md) — this plan consumes that contract and changes none of it

**Goal:** Load deployment-provisioned certificate material through the resolution contract 02a delivers, install a trusted certificate authority into the mail transport's validation path so a private server is supported with validation fully enabled, and let a rotated secret take effect without restarting the process.

**Architecture:** `Infrastructure` gains a certificate material loader above the resolver and the custom-root-trust validation callback the MailKit adapter installs. `Host` switches the mail options consumer to `IOptionsMonitor` and publishes a validated snapshot. `Application` and `Domain` gain nothing, exactly as in 02a.

**Tech Stack:** .NET 10, C# preview, MailKit 4.17.0, xUnit.net v3 on Microsoft Testing Platform v2, NSubstitute 6.0.0.

## Global Constraints

- Certificate validation stays enabled unconditionally. Nothing introduced here may expose a "trust any certificate" flag, an `SslProtocols` override, or a callback that returns `true` on an unexamined chain. This is the constraint most at risk in this plan, because the trust anchor path is exactly where a shortcut would look reasonable.
- No secret material reaches a log, an exception message, or a diagnostic dump. A trust anchor is public and may be logged by subject and thumbprint; a bundle password may not.
- `Domain` and `Application` stay free of every type introduced here.
- Nothing in this plan changes a contract 02a shipped. If a change looks necessary, it is a signal that the split was drawn in the wrong place and belongs back to the owner rather than being absorbed quietly.

## Design decisions

This plan inherits the decisions recorded in the 02a plan and does not restate them; they are one design record for one mechanism, and duplicating them would let two copies drift. Four of those decisions are this plan's own subject and keep their original numbers so a cross-reference from either plan resolves to the same thing:

- **11. Typed material is loaded above the resolver, not inside it.** `X509CertificateMaterialLoader` in `Infrastructure` turns resolved bytes into an `X509Certificate2`, accepting PEM and DER and, for key material, PKCS#12 with an optional separately-referenced password. The resolver stays ignorant of X.509 so that adding a future material kind — an SSH key, a JWT signing key — adds a loader rather than touching every scheme adapter.
- **17. Secrets are reloadable for new operations, not during running operations.** Both halves reload: a changed *reference* arrives through `IOptionsMonitor` and is validated by resolving the whole candidate snapshot before publishing it, with the last known good snapshot retained on failure; changed *material* is picked up by 02a decision 16's per-use resolution. Neither is applied mid-operation — a synchronization run that has authenticated finishes with the credential it authenticated with, because swapping a credential or trust anchor underneath an open IMAP session has no coherent meaning. This is ADR 0002's middle classification, chosen deliberately over the strictest one.
- **18. ADR 0002 must be amended before this is implemented.** The ADR currently classifies credentials and certificate trust anchors as restart-required, and decision 17 departs from that. The departure is defensible — the reference indirection means rotation re-resolves a validated reference rather than mutating a bound secret in place — but `docs/AGENTS.md` forbids modifying an ADR without explicit owner approval, so no ADR is touched here. **This is a blocking prerequisite for Task 3, not a follow-up.** Tasks 1 and 2 are unaffected.
- **21. A custom trust anchor is validated by rebuilding the chain, never by accepting errors.** `RemoteCertificateNotAvailable` and `RemoteCertificateNameMismatch` are rejected outright. Only `RemoteCertificateChainErrors` is re-examined, by building an `X509Chain` with `TrustMode = X509ChainTrustMode.CustomRootTrust` and the anchor in `CustomTrustStore` — which Microsoft documents as respected only under that trust mode — and requiring a clean rebuild. MailKit's own `SslCertificateValidation.cs` example stops at describing errors and does not implement custom-CA trust, so this logic is ours to write and ours to test.

Two decisions are new to this plan:

- **28. Inline certificate material is PEM only, and says so.** A trust anchor is a public certificate, so writing one into configuration leaks nothing and the inline interpretation modes must accept it — that is what makes an Azure App Configuration deployment work end to end for the anchor as well as for passwords. But only PEM survives a configuration value: DER and PKCS#12 are binary and have no faithful representation there. An inline block carrying binary material therefore fails startup with a failure identity that names the encoding as the reason, rather than surfacing a generic parse error that sends the operator looking in the wrong place. PEM is multi-line, so a JSON document has to escape the newlines; that is the operator's problem to accept, and a store-backed provider does not have it because the value is transported rather than authored in JSON.
- **29. The bundle password is the nested block 02a already ships.** A PKCS#12 bundle protected by a password needs two references, which is the case 02a decision 25 shaped `ConfiguredSecret` for: the block carries an optional nested `Password` that is itself a `ConfiguredSecret`. An earlier draft made it a sibling *string* named `PasswordSecretReference`, which would have been invisible to the discovery walk — the walk looks for the block type, and a string is not one — so it could not have been bound, validated, resolved, or erased by the machinery this plan claims to reuse. Worse, fixing it here would have changed a contract 02a had already shipped, which this plan is barred from doing. The nested block is therefore defined in 02a, and this plan adds no property to `ConfiguredSecret` at all.

```json
"TrustedCertificateAuthority": {
  "SecretReference": "file:/run/secrets/ca-bundle.pfx",
  "Password": { "SecretReference": "systemd-credential:ca-bundle-password" }
}
```

## File structure

**Create**

- `src/Infrastructure/Secrets/X509CertificateMaterialLoader.cs` — PEM, DER, and PKCS#12 loading over resolved bytes.
- `src/Infrastructure/Mail/MailServerCertificateValidator.cs` — the custom-root-trust validation callback.
- `src/Host/Configuration/MailSecretSnapshotPublisher.cs` — validates a candidate snapshot before publishing it.
- `tests/Infrastructure.UnitTests/X509CertificateMaterialLoaderTests.cs`
- `tests/Infrastructure.UnitTests/MailServerCertificateValidatorTests.cs`
- `tests/Infrastructure.UnitTests/MailAccountSecretReloadTests.cs`

**Modify**

- `src/Infrastructure/Mail/MailAccountSecretOptions.cs` — resolves and parses the trust anchor beside the password.
- `src/Infrastructure/Mail/MailAccountSecrets.cs` — gains the resolved `X509Certificate2`, disposed on the same path as the password.
- `src/Infrastructure/Mail/ImapAccountSettings.cs` — carries the optional trust anchor.
- `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs` — the client port carries the validation callback; the factory installs it before connecting.
- `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs` — `IOptionsMonitor` binding and snapshot publication.
- `docs/features/imap-synchronization.md` — trust anchor behavior and the revocation trade-off; the pending item 02a left open is resolved here.
- `docs/operations/secret-provisioning.md` — certificate encodings, the inline PEM limit, and the rotation procedure.

---

### Task 1: Certificate material loading above the resolver

**Files:**
- Create: `src/Infrastructure/Secrets/X509CertificateMaterialLoader.cs`
- Modify: `src/Infrastructure/Mail/MailAccountSecretOptions.cs`, `src/Infrastructure/Mail/MailAccountSecrets.cs`, `src/Infrastructure/Mail/ImapAccountSettings.cs`
- Test: `tests/Infrastructure.UnitTests/X509CertificateMaterialLoaderTests.cs`, `tests/Infrastructure.UnitTests/MailAccountSecretOptionsTests.cs`

**Interfaces produced:**

```csharp
public enum CertificateMaterialFailure
{
    NotACertificate = 0,
    BinaryEncodingNotPermittedInline = 1,
    BundlePasswordMissing = 2,
    BundlePasswordIncorrect = 3,
    PrivateKeyNotPermittedInTrustAnchor = 4,
}

internal static class X509CertificateMaterialLoader
{
    internal static bool TryLoad(
        ReadOnlySpan<byte> material,
        ResolvedSecret? bundlePassword,
        SecretMaterialSource source,
        out X509Certificate2? certificate,
        out CertificateMaterialFailure failure);
}
```

- [ ] **Step 1: Write the failing tests**

The fixture builds its certificates in memory with `CertificateRequest` and `CreateSelfSigned`, exports PEM with `ExportCertificatePem()` and DER with `Export(X509ContentType.Cert)` — no file system, no network, no real trust store.

```csharp
[Fact] TryLoad_PemCertificate_LoadsTheSubject
[Fact] TryLoad_DerCertificate_LoadsTheSubject
[Fact] TryLoad_Pkcs12BundleWithTheCorrectPassword_LoadsTheCertificate
[Fact] TryLoad_UnprotectedPkcs12Bundle_LoadsWithoutAPasswordBlock
[Fact] TryLoad_ProtectedPkcs12BundleWithNoPasswordBlock_ReportsBundlePasswordMissing
[Fact] TryLoad_Pkcs12BundleWithTheWrongPassword_ReportsBundlePasswordIncorrect
[Fact] TryLoad_ArbitraryBytes_ReportsNotACertificate
[Fact] TryLoad_EveryFailure_ProducesNoExceptionAndNoMaterialInTheResult
```

- [ ] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — the loader does not exist.

- [ ] **Step 3: Implement**

`X509Certificate2.CreateFromPem` and the PKCS#12 import both throw `CryptographicException` on bad material, so each is wrapped in a `try`/`catch (CryptographicException)` that becomes a named failure. A catch this broad is justified only because the alternative is an unhandled exception during startup validation with a message that may quote material; the caught exception is not rethrown and its message is not propagated.

Encoding is detected by inspecting the material rather than by trusting a file extension, because `file:/run/secrets/ca.pem` may hold DER and the operator will not find that out from the name.

**An unprotected PKCS#12 bundle is valid and loads.** The specification says a bundle *may* carry its own password, so a certificate-only or deliberately unprotected PFX is a legitimate form of a required encoding. The loader attempts a null password when no password block is configured, and reserves `BundlePasswordMissing` for a bundle that is actually protected. Requiring a password block unconditionally would reject files an operator is entitled to use.

**A trust anchor must not carry a private key.** PKCS#12 bundles commonly do, and this feature needs only a public anchor — it presents no certificate of its own. An imported private key would sit outside the buffer this plan owns, so disposing the certificate would not satisfy the operation-scoped lifetime guarantee, and with default key-storage flags the import can persist key material to disk. Import therefore uses `X509KeyStorageFlags.EphemeralKeySet` so nothing is written to a key store, and a bundle whose certificate carries a private key is rejected as `PrivateKeyNotPermittedInTrustAnchor` rather than silently accepted. The rule is stated on the loader because a later caller that legitimately needs a key — a client certificate in stage 9 — must opt in explicitly rather than inherit an anchor's permissions.

**Binary encodings are rejected only when the material came inline.** This is where 02a decision 28's `SecretMaterialSource` earns its place. `InlineValue` plus a detected binary encoding is `BinaryEncodingNotPermittedInline`; `SchemeAdapter` plus the same bytes is a perfectly ordinary DER or PFX anchor read from a file. Consulting the global interpretation mode instead would be wrong in both directions under `ReferenceOrInline` — it would reject a valid `file:` DER anchor, or accept an inline one, depending on which way the guess was written.

- [ ] **Step 4: Resolve the anchor in the account options**

`MailAccountSecretOptions.FindConfigurationErrorsAsync` and `ResolveAsync` regain the `ConfiguredSecret? trustedCertificateAuthority` parameter that 02a deliberately left out, resolve it when present, and load it through `TryLoad`. `MailAccountSecrets` becomes `(ResolvedSecret Password, X509Certificate2? TrustedCertificateAuthority)` and disposes both — `X509Certificate2` is itself disposable, and 02a decision 9's ownership rule already says the operation that resolved it disposes it.

Inline material reaches `TryLoad` as bytes like any other, so the binary-inline rejection is a check on 02a decision 28's `SecretMaterialSource` plus the detected encoding, not a separate code path.

A protected bundle takes its password from the nested `Password` block 02a decision 25 defines, resolved through the same resolver as every other secret and disposed with the rest of `MailAccountSecrets`. Nothing here adds a property to `ConfiguredSecret`.

```csharp
[Fact] FindConfigurationErrorsAsync_UnresolvableTrustAnchorReference_ReportsTheFailureAgainstTheTrustAnchorSetting
[Fact] FindConfigurationErrorsAsync_UnresolvableBundlePasswordBlock_ReportsTheFailureAgainstTheNestedPasswordPath
[Fact] ResolveAsync_ProtectedPkcs12AnchorWithANestedPasswordBlock_ResolvesBothAndLoadsTheAnchor
[Fact] FindConfigurationErrorsAsync_TrustAnchorMaterialIsNotACertificate_ReportsNotACertificate
[Fact] FindConfigurationErrorsAsync_InlineDerTrustAnchor_ReportsBinaryEncodingNotPermittedInline
[Fact] FindConfigurationErrorsAsync_NoTrustAnchorConfigured_DoesNotConsultTheResolver
[Fact] ResolveAsync_PemTrustAnchor_ReturnsThePasswordAndTheParsedAnchor
[Fact] Dispose_ResolvedAccountSecrets_ErasesThePasswordAndDisposesTheAnchor
```

- [ ] **Step 5: Run the tests and commit**

```bash
dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
git add src/Infrastructure tests/Infrastructure.UnitTests
git commit -m "Load PEM, DER, and PKCS#12 certificate material from resolved secrets"
```

---

### Task 2: Install the trust anchor in the certificate validation path

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
        X509Chain? serverChain,
        SslPolicyErrors sslPolicyErrors);
}
```

`IMailKitImapClient` gains `RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }`, forwarded by `MailKitImapClientAdapter` to `ImapClient.ServerCertificateValidationCallback`.

- [ ] **Step 1: Write the failing tests**

The fixture builds an in-memory CA and a leaf signed by it with `CertificateRequest`, so no file system, network, or real trust store is involved:

```csharp
[Fact] IsTrusted_NoPolicyErrors_TrustsWithoutConsultingTheAnchor
[Fact] IsTrusted_ChainErrorsAndLeafChainsToTheConfiguredAnchor_Trusts
[Fact] IsTrusted_LeafSignedByAnIntermediateSuppliedByTheServer_Trusts
[Fact] IsTrusted_IntermediateSuppliedByTheServerIsNotItselfTrusted_Rejects
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
    X509Chain? serverChain,
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

    // The server may have supplied intermediates that are in neither the machine store nor an AIA-reachable
    // location. Discarding the callback's chain would reject a correctly provisioned root -> intermediate -> leaf
    // deployment. Only non-leaf elements are carried over, and ExtraStore supplies candidates for path building
    // without granting any of them trust: trust still comes solely from CustomTrustStore.
    if (serverChain is not null)
    {
        foreach (var element in serverChain.ChainElements.Skip(1))
        {
            chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }
    }

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


### Task 3: Dynamic reload of references and material

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs`, `src/Infrastructure/Mail/MailAccountSecretOptions.cs`
- Create: `src/Host/Configuration/MailSecretSnapshotPublisher.cs`
- Test: `tests/Infrastructure.UnitTests/MailAccountSecretReloadTests.cs`

Decision 18 makes this a blocking prerequisite: **do not start this task until ADR 0002 has been amended with owner approval.** The ADR classifies credentials and trust anchors as restart-required, and this task deliberately reclassifies them as reloadable for new operations.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] ReloadAsync_ReferenceChangedToAResolvableTarget_PublishesTheNewSecrets
[Fact] ReloadAsync_CandidateSnapshotHasAnUnresolvableReference_KeepsThePreviousSecrets
[Fact] ReloadAsync_RejectedSnapshot_LogsThePathAndFailureWithoutMaterial
[Fact] ReloadAsync_ResolutionThrows_KeepsThePreviousSnapshotAndDoesNotPropagate
[Fact] ReloadAsync_TwoCandidatesInRapidSuccession_PublishesTheNewerOneLast
[Fact] ReloadAsync_DatabasePasswordReferenceChanged_RebuildsTheDataSource
[Fact] ResolveAsync_MaterialRotatedBehindAnUnchangedReference_ReturnsTheRotatedValue
[Fact] ResolveAsync_MaterialRotatedMidOperation_DoesNotAffectTheOperationInFlight
```

The last two are the pair that distinguishes the two reload halves, and the last one is what keeps decision 17's "not during running operations" honest.

- [ ] **Step 2: Bind for reload**

`Host` switches the mail options consumer from `IOptions<MailSynchronizationOptions>` to `IOptionsMonitor<MailSynchronizationOptions>`. Note that `IOptions<T>` is a singleton captured once, so leaving it in place would silently defeat the reference half of this task.

- [ ] **Step 3: Validate the candidate before publishing it**

`IOptionsMonitor.OnChange` takes a **synchronous** callback, and resolving a candidate snapshot is asynchronous by decision 4 and may later perform network I/O. Implementing this literally leaves only two shapes, and both are defects: blocking inside the callback stalls the configuration-provider thread, and `async void` lets an exception tear down the process and lets two rapid reloads race so that an older candidate publishes after a newer one.

`MailSecretSnapshotPublisher` therefore does almost nothing in the callback. `OnChange` writes the candidate into a bounded channel and returns; a single `BackgroundService` reads it, resolves every reference, and publishes an immutable snapshot only when all of them resolve. One reader means candidates are validated in arrival order and the last one to publish is the last one to arrive. The channel drops the older candidate when a newer arrives while one is queued, since validating a superseded snapshot wastes a credential read for a result nobody will use.

On failure it retains the previous snapshot and logs the configuration path and the `SecretResolutionFailure` — never the target, a variable name, or material. This is the last-known-good behavior ADR 0002 requires of any reloadable group.

Reload validation must not crash the process: ADR 0002 is explicit that a rejected reload leaves the previous valid settings active, unlike a startup failure, which is fatal. The worker therefore catches every exception from resolution, logs it, and continues — the one place in this plan where a blanket catch is correct, because the alternative is a configuration edit killing a running host.

- [ ] **Step 4: Resolve material per operation**

For mail secrets no change is needed: 02a decision 16 already resolves per use, which is what makes rotation behind an unchanged reference work. The test in Step 1 exists to catch a future "optimization" that caches the resolved value on the options instance.

- [ ] **Step 5: Rotate the database credential too**

The database password is the exception, and it would otherwise silently break the promise this specification makes for it. 02a composes it once into a singleton `NpgsqlDataSource`, so neither a changed `Persistence:Password` reference nor rotated material behind it reaches a connection opened afterwards — revoking the old credential would take MailMcp offline until restart, which is exactly the outcome rotation exists to prevent.

`NpgsqlDataSource` is registered behind a provider that rebuilds it when the persistence snapshot changes, disposing the superseded data source only after its in-flight connections drain. Connections already open keep the credential they authenticated with, which is decision 17's operation boundary applied to the database rather than an exception to it.

Rotation of material behind an *unchanged* database reference cannot be observed by a data source that was built once, so the provider re-resolves on a bounded interval and rebuilds when the resolved value differs. That is a deliberate departure from per-use resolution: opening a pooled connection is not the place to read a credential file, and the interval bounds how long a revoked credential stays usable. The interval is configuration, and its default is documented.

- [ ] **Step 6: Run the tests and commit**

```bash
dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj
git add src/Host src/Infrastructure tests/Infrastructure.UnitTests/MailAccountSecretReloadTests.cs
git commit -m "Reload secret references and material without restarting the host"
```

---


### Task 4: Documentation and full verification

**Files:**
- Modify: `docs/features/imap-synchronization.md`, `docs/operations/secret-provisioning.md`

- [ ] **Step 1: Document the implemented behavior**

`docs/features/imap-synchronization.md` gains the trust anchor behavior: how an anchor is configured, that validation is never disabled, that a name mismatch is rejected regardless of the anchor, and the `RevocationMode = NoCheck` trade-off with the reason it is confined to the custom-anchor path. It removes the pending item that 02a left open.

`docs/operations/secret-provisioning.md` gains the accepted certificate encodings, decision 28's inline PEM limit stated as a rule rather than as a discovery, the PKCS#12 bundle-password shape, and the rotation procedure for both deployment shapes — replacing a credential file, replacing a systemd credential, and what an operator should expect to see in the log when a reload is rejected.

- [ ] **Step 2: Run the completion gates**

```bash
bash scripts/verify-full.sh
```

Run `$check-docs-licenses` and `$review-change`. Check specifically: no certificate-validation opt-out and no callback returning `true` on an unexamined chain; no bundle password in any message or log; no secret material outliving the operation that resolved it.

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "Document trust anchor behavior and the secret rotation procedure"
```

## Self-review

| Specification requirement | Covered by |
| --- | --- |
| PEM, DER, and PKCS#12 all load from resolved bytes | Task 1 |
| A PKCS#12 password comes from the nested secret block 02a ships | decision 29, Task 1 |
| An unprotected PKCS#12 bundle loads without a password block | Task 1 |
| A private-key-bearing anchor is rejected; import is ephemeral | Task 1 |
| A server-supplied intermediate completes the chain without gaining trust | Task 2 |
| The database credential rotates too | Task 3 Step 5 |
| Reload never blocks, never crashes, never publishes out of order | Task 3 Step 3 |
| Inline PEM works; inline binary fails naming the encoding | decision 28, Task 1 |
| Malformed anchor material fails startup without disclosing material | Task 1 |
| A private server connects with validation fully enabled | Task 2 |
| A name mismatch is rejected regardless of the anchor | decision 21, Task 2 |
| No configuration path disables validation | Task 2, asserted by test |
| Rotated material behind an unchanged reference is observed | decision 17, Task 3 |
| A reload with an unresolvable reference keeps the previous secrets | Task 3 |
| A rotation never affects an operation in flight | Task 3 |
| ADR 0002 amended before implementation | decision 18, blocking |

**Flagged for the owner**

- **Blocking:** ADR 0002 must be amended before Task 3 begins (decision 18). Tasks 1 and 2 are unaffected and can proceed as soon as 02a merges.
- The Codex review on PR #59 raised the intermediate-certificate, private-key, unprotected-bundle, reload-callback, and database-rotation points now folded into this plan. The one that changed the shape of the split is the bundle password: it had to become a nested block in 02a, because a sibling string would have been invisible to the discovery walk and fixing it here would have modified a contract 02a had already shipped. That is the escalation this plan's global constraint asks for, and it was resolved by moving the property rather than by bending the constraint.
- Decision 28 rejects inline binary certificate material rather than accepting base64. Base64 would work and would let a DER anchor be configured inline, but it introduces a second encoding an operator has to know about and get right, for a case PEM already covers. If you would rather support it, it is a small addition to the loader and a line in the documentation.
- `RevocationMode = NoCheck` on the custom-anchor path is a real trade-off, not an oversight: a private authority typically publishes no reachable CRL or OCSP responder, and a revocation lookup that cannot complete would reject a correctly provisioned server. It is confined to the custom-anchor path and the system trust path keeps the platform default. If revocation checking on private anchors matters more than that failure mode, this is the decision to revisit.
