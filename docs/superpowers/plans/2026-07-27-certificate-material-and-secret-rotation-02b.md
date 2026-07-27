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
- **29. The bundle password is a second secret block, resolved through the same contract.** A PKCS#12 bundle protected by a password needs two references, which is the concrete case 02a decision 25 shaped the block for. The password is a sibling `PasswordSecretReference` inside the same block, so it is discovered, validated, and erased by exactly the machinery 02a already built. Nothing about PKCS#12 gets its own resolution path.

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
}

internal static class X509CertificateMaterialLoader
{
    internal static bool TryLoad(
        ReadOnlySpan<byte> material,
        ResolvedSecret? bundlePassword,
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
[Fact] TryLoad_Pkcs12BundleWithoutAPassword_ReportsBundlePasswordMissing
[Fact] TryLoad_Pkcs12BundleWithTheWrongPassword_ReportsBundlePasswordIncorrect
[Fact] TryLoad_ArbitraryBytes_ReportsNotACertificate
[Fact] TryLoad_EveryFailure_ProducesNoExceptionAndNoMaterialInTheResult
```

- [ ] **Step 2: Run the tests and confirm they fail**

Expected: FAIL — the loader does not exist.

- [ ] **Step 3: Implement**

`X509Certificate2.CreateFromPem` and the PKCS#12 constructor both throw `CryptographicException` on bad material, so each is wrapped in a `try`/`catch (CryptographicException)` that becomes a named failure. A catch this broad is justified only because the alternative is an unhandled exception during startup validation with a message that may quote material; the caught exception is not rethrown and its message is not propagated.

Encoding is detected by inspecting the material rather than by trusting a file extension, because `file:/run/secrets/ca.pem` may hold DER and the operator will not find that out from the name.

- [ ] **Step 4: Resolve the anchor in the account options**

`MailAccountSecretOptions.FindConfigurationErrorsAsync` and `ResolveAsync` regain the `ConfiguredSecret? trustedCertificateAuthority` parameter that 02a deliberately left out, resolve it when present, and load it through `TryLoad`. `MailAccountSecrets` becomes `(ResolvedSecret Password, X509Certificate2? TrustedCertificateAuthority)` and disposes both — `X509Certificate2` is itself disposable, and 02a decision 9's ownership rule already says the operation that resolved it disposes it.

Inline material reaches `TryLoad` as bytes like any other, so decision 28's binary-inline rejection is a check on the interpretation mode plus the detected encoding, not a separate code path.

```csharp
[Fact] FindConfigurationErrorsAsync_UnresolvableTrustAnchorReference_ReportsTheFailureAgainstTheTrustAnchorSetting
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
[Fact] ResolveAsync_MaterialRotatedBehindAnUnchangedReference_ReturnsTheRotatedValue
[Fact] ResolveAsync_MaterialRotatedMidOperation_DoesNotAffectTheOperationInFlight
```

The last two are the pair that distinguishes the two reload halves, and the last one is what keeps decision 17's "not during running operations" honest.

- [ ] **Step 2: Bind for reload**

`Host` switches the mail options consumer from `IOptions<MailSynchronizationOptions>` to `IOptionsMonitor<MailSynchronizationOptions>`. Note that `IOptions<T>` is a singleton captured once, so leaving it in place would silently defeat the reference half of this task.

- [ ] **Step 3: Validate the candidate before publishing it**

`MailSecretSnapshotPublisher` subscribes to `OnChange`, resolves every reference in the candidate snapshot, and publishes an immutable snapshot only when all of them resolve. On failure it retains the previous snapshot and logs the configuration path and the `SecretResolutionFailure` — never a path, a variable name, or material. This is the last-known-good behavior ADR 0002 requires of any reloadable group.

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
| A PKCS#12 password comes from a second secret block | decision 29, Task 1 |
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
- Decision 28 rejects inline binary certificate material rather than accepting base64. Base64 would work and would let a DER anchor be configured inline, but it introduces a second encoding an operator has to know about and get right, for a case PEM already covers. If you would rather support it, it is a small addition to the loader and a line in the documentation.
- `RevocationMode = NoCheck` on the custom-anchor path is a real trade-off, not an oversight: a private authority typically publishes no reachable CRL or OCSP responder, and a revocation lookup that cannot complete would reject a correctly provisioned server. It is confined to the custom-anchor path and the system trust path keeps the platform default. If revocation checking on private anchors matters more than that failure mode, this is the decision to revisit.
