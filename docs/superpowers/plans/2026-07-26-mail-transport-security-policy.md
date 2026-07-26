# Mail Transport Security Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Governing issue:** [#35 — Spec 01 — Mail transport security policy](https://github.com/Krzysztof318/MailMcp/issues/35)
**Governing specification:** [`specs/01-mail-transport-security-policy.md`](../../../specs/01-mail-transport-security-policy.md)
**Architectural context:** `specs/2026-07-22-mail-mcp-architecture-draft.md` sections 7.1–7.3, ADR `docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md`

**Goal:** Replace the boolean `UseSslOnConnect` switch with a provider-neutral connection-security and SASL authentication policy that makes an unencrypted channel and clear-text authentication explicit, opt-in, and fail-fast at startup.

**Architecture:** `Domain` owns the policy value objects and the pure rules that reject unsafe combinations. `Application` extends the mailbox session port so the resolved policy is an explicit input the adapter must obey rather than something the adapter chooses. `Infrastructure` maps the domain policy onto MailKit's `SecureSocketOptions` and narrows the client's advertised SASL mechanism set before authenticating. `Host` binds the new configuration shape, re-checks the domain rules during options validation, and fails startup naming the offending account.

**Tech Stack:** .NET 10, C# preview, MailKit 4.17.0, xUnit.net v3 on Microsoft Testing Platform v2, NSubstitute 6.0.0.

## Global Constraints

- No new third-party packages. Every version stays pinned in `Directory.Packages.props`; `LICENSES.md` therefore needs no dependency entry for this change.
- `Domain` stays free of MailKit, `SecureSocketOptions`, `Microsoft.Extensions.*`, and configuration types. The mapping to MailKit lives only in `src/Infrastructure/Mail/MailKit`.
- Certificate validation stays enabled unconditionally. No type introduced here exposes `ServerCertificateValidationCallback`, a "trust any certificate" flag, or an `SslProtocols` override.
- Validation diagnostics name the account identifier and the violated rule only. They never contain the user name, password, secret reference value, host credentials, or provider responses.
- The adapter must not widen the permitted mechanism set after an authentication failure: no `catch` around authentication that retries with more mechanisms.
- Every enum member gets an explicit contiguous value starting at `0`; members are appended, never reordered or renumbered.
- Synchronization stays read-only: nothing in this change may set the remote `\Seen` flag.
- Unit tests only. No integration test project, Testcontainers, real IMAP server, network, filesystem, or database.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` must pass the 85% whole-scope gate.

## Design decisions locked before implementation

1. **The permitted mechanism set is a closed domain enum, not free text.** Config supplies SASL names as strings; `Domain` owns the canonical name table because SASL mechanism names are RFC vocabulary, not MailKit vocabulary. This gives one parse/normalize implementation shared by `Host` (configuration) and `Infrastructure` (filtering MailKit's advertised set), and makes the clear-text classification an exhaustive switch instead of a string comparison.
2. **XOAUTH2 and OAUTHBEARER are not in the enum yet.** Spec 01 puts OAuth-based mailbox authentication out of scope, and an allow-list entry with no token source would be dead configuration. Enum values are append-only, so spec-level OAuth work adds them later without renumbering.
3. **`Auto` and `StartTlsWhenAvailable` require `AllowInsecureConnection`,** because draft 7.1 makes "opportunistic downgrade behavior" require the same opt-in as `None`. Both modes can complete a connection with no encryption at all.
4. **The policy travels as an input to the application port.** `IMailboxSessionFactory.OpenReadOnlyAsync` gains the policy parameter and `MailboxSynchronizer` resolves it through a focused application-owned reader (ADR 0002 contract style). Host/port/credentials keep coming from the existing infrastructure-owned `IMailKitImapAccountSettingsProvider`, because secret resolution belongs to specification 02. The security policy is deliberately the half that the adapter is handed rather than the half it looks up.
5. **The trust anchor is configuration shape only.** `MailServerCertificateTrust.AdditionalTrustedAuthority` requires a non-blank reference and `SystemTrustStore` forbids one, but nothing loads the material — specification 02 owns loading and installing it. Documented under pending work so the gap is visible rather than silent.
6. **Options-validation rules are tested through their `Domain` entry point.** The repository has no `Host.UnitTests` project, `Host` is excluded from coverage, and `tests/AGENTS.md` names the permitted test projects. `MailTransportSecurityPolicy.FindViolations` is the exact API host validation calls, so `Domain.UnitTests` covers every unsafe configuration shape and the `Host` side stays a thin violation-to-`ValidationResult` translation.
7. **An empty permitted-mechanism list is a configuration error when synchronization is enabled.** "Explicit allow-list" means the operator states the set; there is no implicit default that lets MailKit choose freely.

## File structure

**Create**

- `src/Domain/Transport/MailConnectionSecurity.cs` — the five connection-security modes.
- `src/Domain/Transport/MailAuthenticationMechanism.cs` — closed set of permitted SASL mechanisms plus their canonical names, parsing, and clear-text classification.
- `src/Domain/Transport/MailAuthenticationPolicy.cs` — ordered normalized allow-list plus the two opt-in flags.
- `src/Domain/Transport/MailServerCertificateTrust.cs` — system trust store versus an additional trusted authority.
- `src/Domain/Transport/MailTransportSecurityViolation.cs` — machine-readable rule identities.
- `src/Domain/Transport/MailTransportSecurityPolicy.cs` — the aggregate value object and the pure rule evaluation.
- `src/Domain/Transport/MailTransportSecurityPolicyViolationException.cs` — thrown when an unsafe policy is constructed directly.
- `src/Application/Mail/IMailTransportSecurityPolicyReader.cs` — resolves the policy for one account.
- `src/Infrastructure/Mail/MailKit/MailKitTransportSecurityMapping.cs` — domain policy to `SecureSocketOptions` and to MailKit's advertised mechanism set.
- `src/Infrastructure/Mail/MailKit/MailAuthenticationMechanismUnavailableException.cs` — no permitted mechanism is advertised by the server.
- `tests/Domain.UnitTests/MailTransportSecurityPolicyTests.cs`
- `tests/Infrastructure.UnitTests/MailKitTransportSecurityMappingTests.cs`

**Modify**

- `src/Application/Synchronization/IMailboxSessionFactory.cs` — the port gains the policy input.
- `src/Application/Synchronization/MailboxSynchronizer.cs` — resolve the policy and pass it in.
- `src/Infrastructure/Mail/MailKit/MailKitImapAccountSettings.cs` — drop `UseSslOnConnect`.
- `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs` — the client port exposes the advertised mechanism set; the factory maps the policy, restricts mechanisms, then authenticates.
- `src/Host/Configuration/MailSynchronizationOptions.cs` — new account settings, policy mapping, fail-fast validation.
- `src/Host/Program.cs` — register the policy reader.
- `tests/Application.UnitTests/MailboxSynchronizerTests.cs` — supply the policy reader.
- `tests/Infrastructure.UnitTests/MailKitImapMailboxSessionTests.cs` — replace the boolean TLS theory, add mechanism-restriction coverage.
- `docs/features/imap-synchronization.md` — document the modes, allow-list, opt-ins, and pending trust-anchor loading.

---

### Task 1: Domain connection-security and authentication policy

**Files:**
- Create: `src/Domain/Transport/MailConnectionSecurity.cs`, `MailAuthenticationMechanism.cs`, `MailAuthenticationPolicy.cs`, `MailServerCertificateTrust.cs`, `MailTransportSecurityViolation.cs`, `MailTransportSecurityPolicy.cs`, `MailTransportSecurityPolicyViolationException.cs`
- Test: `tests/Domain.UnitTests/MailTransportSecurityPolicyTests.cs`

**Interfaces produced:**

```csharp
public enum MailConnectionSecurity { Auto = 0, TlsOnConnect = 1, StartTlsRequired = 2, StartTlsWhenAvailable = 3, None = 4 }

public enum MailAuthenticationMechanism
{
    Plain = 0, Login = 1, CramMd5 = 2, DigestMd5 = 3,
    ScramSha1 = 4, ScramSha1Plus = 5, ScramSha256 = 6, ScramSha256Plus = 7,
    ScramSha512 = 8, ScramSha512Plus = 9, Ntlm = 10,
}

public static class MailAuthenticationMechanismNames
{
    public static string ToSaslName(this MailAuthenticationMechanism mechanism);
    public static bool TryParseSaslName(string? saslName, out MailAuthenticationMechanism mechanism);
    public static bool TransmitsCredentialsInClearText(this MailAuthenticationMechanism mechanism);
}

public sealed record MailAuthenticationPolicy
{
    public IReadOnlyList<MailAuthenticationMechanism> PermittedMechanisms { get; }
    public bool AllowInsecureConnection { get; }
    public bool AllowClearTextAuthenticationOverUnencryptedConnection { get; }
    public static MailAuthenticationPolicy Create(
        IEnumerable<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection);
}

public enum MailServerCertificateTrust { SystemTrustStore = 0, AdditionalTrustedAuthority = 1 }

public enum MailTransportSecurityViolation
{
    PermittedAuthenticationMechanismRequired = 0,
    UnencryptedConnectionRequiresExplicitOptIn = 1,
    OpportunisticEncryptionRequiresExplicitOptIn = 2,
    ClearTextAuthenticationRequiresEncryptedConnection = 3,
    TrustedCertificateAuthorityReferenceRequired = 4,
    TrustedCertificateAuthorityReferenceNotApplicable = 5,
}

public sealed record MailTransportSecurityPolicy
{
    public MailConnectionSecurity ConnectionSecurity { get; }
    public MailAuthenticationPolicy Authentication { get; }
    public MailServerCertificateTrust CertificateTrust { get; }
    public string? TrustedCertificateAuthorityReference { get; }
    public bool GuaranteesEncryptedChannel { get; }

    public static MailTransportSecurityPolicy Create(
        MailConnectionSecurity connectionSecurity,
        MailAuthenticationPolicy authentication,
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference);

    public static IReadOnlyList<MailTransportSecurityViolation> FindViolations(
        MailConnectionSecurity connectionSecurity,
        IReadOnlyList<MailAuthenticationMechanism> permittedMechanisms,
        bool allowInsecureConnection,
        bool allowClearTextAuthenticationOverUnencryptedConnection,
        MailServerCertificateTrust certificateTrust,
        string? trustedCertificateAuthorityReference);
}
```

- [ ] **Step 1: Write the failing tests**

`tests/Domain.UnitTests/MailTransportSecurityPolicyTests.cs` covers, one behavior per test:

```csharp
[Theory]
[InlineData(MailConnectionSecurity.TlsOnConnect, true)]
[InlineData(MailConnectionSecurity.StartTlsRequired, true)]
[InlineData(MailConnectionSecurity.StartTlsWhenAvailable, false)]
[InlineData(MailConnectionSecurity.Auto, false)]
[InlineData(MailConnectionSecurity.None, false)]
public void GuaranteesEncryptedChannel_ConnectionSecurityMode_ReportsWhetherEncryptionIsMandatory(
    MailConnectionSecurity connectionSecurity,
    bool expectedGuarantee)
```

```csharp
[Fact] FindViolations_UnencryptedConnectionWithoutOptIn_RequiresExplicitOptIn
[Theory(Auto, StartTlsWhenAvailable)] FindViolations_OpportunisticEncryptionWithoutOptIn_RequiresExplicitOptIn
[Theory(Plain, Login)] FindViolations_ClearTextMechanismOnUnencryptedChannelWithOnlyInsecureOptIn_RequiresClearTextOptIn
[Fact] FindViolations_ClearTextMechanismOnUnencryptedChannelWithBothOptIns_ReportsNoViolation
[Theory(CramMd5, DigestMd5, ScramSha256, Ntlm)] FindViolations_ChallengeResponseMechanismOnUnencryptedChannel_ReportsOnlyTransportViolation
[Fact] FindViolations_ClearTextMechanismOnEncryptedChannel_ReportsNoViolation
[Fact] FindViolations_EmptyMechanismList_RequiresPermittedMechanism
[Fact] FindViolations_AdditionalTrustedAuthorityWithoutReference_RequiresReference
[Fact] FindViolations_SystemTrustStoreWithReference_ReportsReferenceNotApplicable
[Fact] Create_UnsafePolicy_ThrowsViolationException
[Fact] Create_SafePolicy_TrimsTrustedCertificateAuthorityReference
[Fact] MailAuthenticationPolicyCreate_DuplicateMechanisms_KeepsFirstOccurrenceOrder
[Fact] MailAuthenticationPolicyCreate_EmptyMechanisms_Throws
[Theory] TryParseSaslName_MixedCaseAndSurroundingWhitespace_ParsesMechanism   // " scram-sha-256 " => ScramSha256
[Fact] TryParseSaslName_UnknownName_ReturnsFalse                             // "GSSAPI" => false
[Theory] ToSaslName_EveryMechanism_ReturnsCanonicalWireName                   // ScramSha1Plus => "SCRAM-SHA-1-PLUS"
```

The exception test asserts the exception carries the violations:

```csharp
// Act
var exception = Assert.Throws<MailTransportSecurityPolicyViolationException>(
    () => MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.None,
        MailAuthenticationPolicy.Create([MailAuthenticationMechanism.Plain], allowInsecureConnection: false, allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null));

// Assert
Assert.Contains(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn, exception.Violations);
```

- [ ] **Step 2: Run the tests and confirm they fail to compile**

Run: `dotnet test --project tests/Domain.UnitTests/Domain.UnitTests.csproj`
Expected: FAIL — `MailTransportSecurityPolicy` does not exist.

- [ ] **Step 3: Implement the domain types**

Rule evaluation, kept in one readable sequence:

```csharp
public static IReadOnlyList<MailTransportSecurityViolation> FindViolations(...)
{
    var violations = new List<MailTransportSecurityViolation>();

    if (permittedMechanisms is not { Count: > 0 })
    {
        violations.Add(MailTransportSecurityViolation.PermittedAuthenticationMechanismRequired);
    }

    if (connectionSecurity == MailConnectionSecurity.None && !allowInsecureConnection)
    {
        violations.Add(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn);
    }

    if (connectionSecurity is MailConnectionSecurity.Auto or MailConnectionSecurity.StartTlsWhenAvailable && !allowInsecureConnection)
    {
        violations.Add(MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn);
    }

    var channelMayStayUnencrypted = !GuaranteesEncryption(connectionSecurity);
    var permitsClearTextCredentials = permittedMechanisms?.Any(mechanism => mechanism.TransmitsCredentialsInClearText()) == true;
    if (channelMayStayUnencrypted && permitsClearTextCredentials &&
        !(allowInsecureConnection && allowClearTextAuthenticationOverUnencryptedConnection))
    {
        violations.Add(MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection);
    }

    violations.AddRange(FindCertificateTrustViolations(certificateTrust, trustedCertificateAuthorityReference));

    return violations;
}
```

`Create` calls `FindViolations` and throws `MailTransportSecurityPolicyViolationException` when the list is non-empty, so the invariant cannot be bypassed by constructing the record directly (the constructor is private and the record is not `struct`). `TransmitsCredentialsInClearText` is an exhaustive switch expression returning `true` only for `Plain` and `Login`. `MailAuthenticationPolicy.Create` deduplicates while preserving first-occurrence order and rejects an empty result with `ArgumentException`.

XML documentation covers every public type and member, states that the two opt-ins are security-relevant, and notes that `PermittedMechanisms` order expresses operator preference.

- [ ] **Step 4: Run the tests**

Run: `dotnet test --project tests/Domain.UnitTests/Domain.UnitTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Domain/Transport tests/Domain.UnitTests/MailTransportSecurityPolicyTests.cs
git commit -m "Add domain mail transport security policy"
```

---

### Task 2: Application port carries the resolved policy

**Files:**
- Create: `src/Application/Mail/IMailTransportSecurityPolicyReader.cs`
- Modify: `src/Application/Synchronization/IMailboxSessionFactory.cs`, `src/Application/Synchronization/MailboxSynchronizer.cs`
- Test: `tests/Application.UnitTests/MailboxSynchronizerTests.cs`

**Interfaces:**
- Consumes: `MailTransportSecurityPolicy` from Task 1.
- Produces:

```csharp
public interface IMailTransportSecurityPolicyReader
{
    MailTransportSecurityPolicy GetPolicy(MailAccountId accountId);
}

Task<IMailboxSession> OpenReadOnlyAsync(
    MailAccountId accountId,
    MailFolderName folderName,
    MailTransportSecurityPolicy transportSecurityPolicy,
    CancellationToken cancellationToken);
```

`MailboxSynchronizer`'s constructor gains `IMailTransportSecurityPolicyReader transportSecurityPolicyReader` immediately after `IMailboxSessionFactory`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task SynchronizeAsync_ConfiguredAccount_OpensSessionWithTheAccountTransportSecurityPolicy()
{
    // Arrange
    var policy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.StartTlsRequired,
        MailAuthenticationPolicy.Create([MailAuthenticationMechanism.ScramSha256], allowInsecureConnection: false, allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);
    // ... existing fakes from this file, plus:
    policyReader.GetPolicy(accountId).Returns(policy);

    // Act
    await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

    // Assert
    await sessionFactory.Received(1).OpenReadOnlyAsync(accountId, folderName, policy, CancellationToken.None);
}
```

Existing tests in this file are updated to construct `MailboxSynchronizer` with the new dependency and to stub `OpenReadOnlyAsync` with the extra argument.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/Application.UnitTests/Application.UnitTests.csproj`
Expected: FAIL — the port has three parameters.

- [ ] **Step 3: Implement**

Add the reader interface with XML documentation stating it returns an immutable snapshot resolved per synchronization run and that the returned policy is authoritative for the adapter. Extend the port and have `SynchronizeAsync` resolve the policy before opening the session:

```csharp
var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(accountId);

await using var mailboxSession = await this.mailboxSessionFactory.OpenReadOnlyAsync(
    accountId,
    folderName,
    transportSecurityPolicy,
    cancellationToken);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test --project tests/Application.UnitTests/Application.UnitTests.csproj`
Expected: PASS (Infrastructure and Host still fail to build until Tasks 3 and 4 — build the two test projects individually in this step).

- [ ] **Step 5: Commit**

```bash
git add src/Application tests/Application.UnitTests
git commit -m "Pass the resolved transport security policy through the mailbox session port"
```

---

### Task 3: MailKit mapping and mechanism restriction

**Files:**
- Create: `src/Infrastructure/Mail/MailKit/MailKitTransportSecurityMapping.cs`, `src/Infrastructure/Mail/MailKit/MailAuthenticationMechanismUnavailableException.cs`
- Modify: `src/Infrastructure/Mail/MailKit/MailKitImapAccountSettings.cs`, `src/Infrastructure/Mail/MailKit/MailKitImapMailboxSession.cs`
- Test: `tests/Infrastructure.UnitTests/MailKitTransportSecurityMappingTests.cs`, `tests/Infrastructure.UnitTests/MailKitImapMailboxSessionTests.cs`

**Interfaces:**
- Consumes: `MailTransportSecurityPolicy`, `MailAuthenticationMechanism`, the extended `IMailboxSessionFactory`.
- Produces:

```csharp
internal static class MailKitTransportSecurityMapping
{
    internal static SecureSocketOptions ToSecureSocketOptions(this MailConnectionSecurity connectionSecurity);
    internal static void RestrictAdvertisedMechanisms(ISet<string> advertisedMechanisms, MailAuthenticationPolicy authentication, string accountId);
}

internal interface IMailKitImapClient : IAsyncDisposable
{
    bool IsConnected { get; }
    ISet<string> AuthenticationMechanisms { get; }   // added
    // existing members unchanged
}

public sealed record MailKitImapAccountSettings(string AccountId, string Host, int Port, string UserName, string Password);
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData(MailConnectionSecurity.Auto, SecureSocketOptions.Auto)]
[InlineData(MailConnectionSecurity.TlsOnConnect, SecureSocketOptions.SslOnConnect)]
[InlineData(MailConnectionSecurity.StartTlsRequired, SecureSocketOptions.StartTls)]
[InlineData(MailConnectionSecurity.StartTlsWhenAvailable, SecureSocketOptions.StartTlsWhenAvailable)]
[InlineData(MailConnectionSecurity.None, SecureSocketOptions.None)]
public void ToSecureSocketOptions_ConnectionSecurityMode_MapsToMailKitSocketOptions(
    MailConnectionSecurity connectionSecurity,
    SecureSocketOptions expected)
```

In `MailKitImapMailboxSessionTests`, replacing the `UseSslOnConnect` theory:

```csharp
[Fact]
public async Task OpenReadOnlyAsync_ServerAdvertisesMoreMechanismsThanPermitted_RemovesThemBeforeAuthenticating()
{
    // Arrange
    await using var client = new FakeImapClient();
    client.AuthenticationMechanisms.Add("PLAIN");
    client.AuthenticationMechanisms.Add("LOGIN");
    client.AuthenticationMechanisms.Add("SCRAM-SHA-256");
    // policy permits SCRAM-SHA-256 only

    // Act
    await using var session = await factory.OpenReadOnlyAsync(accountId, folderName, policy, CancellationToken.None);

    // Assert
    Assert.Equal(["SCRAM-SHA-256"], client.MechanismsWhenAuthenticated);
}

[Fact]
public async Task OpenReadOnlyAsync_NoPermittedMechanismAdvertised_FailsWithoutAuthenticatingOrWideningTheSet()
{
    // Assert
    var exception = await Assert.ThrowsAsync<MailAuthenticationMechanismUnavailableException>(...);
    Assert.Equal("primary", exception.AccountId);
    Assert.False(client.AuthenticateCalled);
    Assert.Equal(["PLAIN"], client.AuthenticationMechanisms);   // untouched advertised set is not widened
}

[Fact]
public async Task OpenReadOnlyAsync_ConnectionSecurityMode_ConnectsWithTheMappedSocketOptions()
```

`FakeImapClient` (already in this test file's fixtures) gains `public ISet<string> AuthenticationMechanisms { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`, records `MechanismsWhenAuthenticated` as a snapshot taken inside `AuthenticateAsync`, and records `AuthenticateCalled`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj`
Expected: FAIL — mapping type missing, port has no mechanism set.

- [ ] **Step 3: Implement**

`MailKitImapAccountSettings` drops `UseSslOnConnect`. `IMailKitImapClient` exposes `ISet<string> AuthenticationMechanisms`, and `MailKitImapClientAdapter` forwards `client.AuthenticationMechanisms` (MailKit exposes the server-advertised set as a mutable `HashSet<string>` populated during `Connect`; removing entries is the documented way to prevent a mechanism from being negotiated).

The factory becomes an explicit sequence:

```csharp
var settings = settingsProvider.GetSettings(accountId.Value);
var client = clientFactory();
try
{
    await client.ConnectAsync(settings.Host, settings.Port, transportSecurityPolicy.ConnectionSecurity.ToSecureSocketOptions(), cancellationToken);

    // The advertised set is narrowed before authenticating so MailKit cannot negotiate a mechanism the policy does not
    // permit. A failure here is final: widening the set after an authentication failure would defeat the allow-list.
    MailKitTransportSecurityMapping.RestrictAdvertisedMechanisms(client.AuthenticationMechanisms, transportSecurityPolicy.Authentication, settings.AccountId);

    await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
    ...
}
```

`RestrictAdvertisedMechanisms` computes the permitted canonical names, removes every advertised name outside that set (ordinal-ignore-case comparison), and throws `MailAuthenticationMechanismUnavailableException(accountId, permittedMechanismNames)` when nothing remains. The exception message names the account and the permitted mechanism names only — never the user name or password.

- [ ] **Step 4: Run the tests**

Run: `dotnet test --project tests/Infrastructure.UnitTests/Infrastructure.UnitTests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure tests/Infrastructure.UnitTests
git commit -m "Map the transport security policy onto MailKit and restrict SASL mechanisms"
```

---

### Task 4: Host configuration binding and fail-fast validation

**Files:**
- Modify: `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Host/Program.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `MailSynchronizationOptions` additionally implements `IMailTransportSecurityPolicyReader`.

New account configuration shape:

```jsonc
{
  "AccountId": "primary",
  "Host": "imap.example.test",
  "Port": 993,
  "ConnectionSecurity": "TlsOnConnect",
  "PermittedAuthenticationMechanisms": [ "SCRAM-SHA-256", "PLAIN" ],
  "AllowInsecureConnection": false,
  "AllowClearTextAuthenticationOverUnencryptedConnection": false,
  "CertificateTrust": "SystemTrustStore",
  "TrustedCertificateAuthorityReference": null
}
```

- [ ] **Step 1: Remove `UseSslOnConnect` and add the new properties**

`MailSynchronizationAccountOptions` gains `MailConnectionSecurity ConnectionSecurity { get; set; } = MailConnectionSecurity.TlsOnConnect;`, `List<string> PermittedAuthenticationMechanisms { get; set; } = [];`, the two `bool` opt-ins defaulting to `false`, `MailServerCertificateTrust CertificateTrust { get; set; } = MailServerCertificateTrust.SystemTrustStore;`, and `string? TrustedCertificateAuthorityReference { get; set; }`. XML documentation on each property states the security meaning and that the two opt-ins weaken transport protection.

- [ ] **Step 2: Add mechanism parsing and policy mapping**

```csharp
internal bool TryCreateTransportSecurityPolicy(
    out MailTransportSecurityPolicy? policy,
    out IReadOnlyList<string> unknownMechanismNames,
    out IReadOnlyList<MailTransportSecurityViolation> violations)
```

parses each configured name with `MailAuthenticationMechanismNames.TryParseSaslName`, collects unknown names, calls `MailTransportSecurityPolicy.FindViolations` with the parsed mechanisms, and only builds the policy when both lists are empty.

- [ ] **Step 3: Re-check the domain rules during options validation**

`ValidateForSynchronization(bool synchronizationEnabled)` adds, when enabled:

```csharp
if (!this.TryCreateTransportSecurityPolicy(out _, out var unknownMechanismNames, out var violations))
{
    foreach (var unknownMechanismName in unknownMechanismNames)
    {
        yield return new ValidationResult(
            $"Account '{this.AccountId}' lists unsupported SASL mechanism '{unknownMechanismName}'.",
            [nameof(this.PermittedAuthenticationMechanisms)]);
    }

    foreach (var violation in violations)
    {
        yield return new ValidationResult(
            $"Account '{this.AccountId}' violates transport security rule '{violation}': {DescribeViolation(violation)}",
            [nameof(this.ConnectionSecurity)]);
    }
}
```

`DescribeViolation` is a private static switch expression over `MailTransportSecurityViolation` returning one safe sentence per rule. No message interpolates `UserName`, `Password`, or `TrustedCertificateAuthorityReference`.

- [ ] **Step 4: Implement the reader and drop the removed setting from `GetSettings`**

`MailSynchronizationOptions` implements `IMailTransportSecurityPolicyReader.GetPolicy(MailAccountId)` by locating the account the same way `GetSettings` does and calling `MailTransportSecurityPolicy.Create(...)`, so an unsafe policy that somehow reached runtime still throws instead of connecting. `GetSettings` no longer passes `UseSslOnConnect`.

`Program.cs` registers the reader next to the existing settings provider:

```csharp
builder.Services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<IOptions<MailSynchronizationOptions>>().Value);
```

- [ ] **Step 5: Verify the whole solution builds and the removal is complete**

```bash
dotnet build MailMcp.slnx
grep -ri "UseSslOnConnect" --exclude-dir=artifacts --exclude-dir=.git .
```

Expected: build succeeds; `grep` returns nothing (the specification's first definition-of-done item).

- [ ] **Step 6: Commit**

```bash
git add src/Host
git commit -m "Bind and validate per-account mail transport security configuration"
```

---

### Task 5: Documentation and full verification

**Files:**
- Modify: `docs/features/imap-synchronization.md`

- [ ] **Step 1: Document the implemented behavior**

Replace the `UseSslOnConnect` sentence in the Configuration section with: the five connection-security modes and what each guarantees; the explicit SASL allow-list and that it is required when synchronization is enabled; that `Auto`, `StartTlsWhenAvailable`, and `None` require `AllowInsecureConnection`; that a clear-text mechanism on a channel that may stay unencrypted additionally requires `AllowClearTextAuthenticationOverUnencryptedConnection`; that certificate validation is always on with no opt-out; and that the adapter removes non-permitted mechanisms before authenticating and never retries with a wider set. Update the JSON example to the new account shape. Add to Pending work: trust anchor material is configuration shape only until specification 02 loads it, and OAuth mechanisms are not in the allow-list yet.

- [ ] **Step 2: Run the documentation and licensing gate**

Run `$check-docs-licenses`. No dependency changed, so the licensing verdict is `n/a`; the documentation verdict must be satisfied by Step 1.

- [ ] **Step 3: Run the full verification gate**

```bash
git add -A
scripts/verify-full.sh
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

Expected: build, format, workflow contract, complete unit-test suite, and the 85% whole-scope coverage gate all pass.

- [ ] **Step 4: Review the change**

Run `$review-change` over the working-tree diff. Check specifically: no `Domain` reference to MailKit or configuration types, no credential in any message, no certificate-validation opt-out, no widening retry around authentication.

- [ ] **Step 5: Finish**

Run `$finish-change`: commit, push `agent/mail-transport-security-policy`, and open a draft pull request whose body contains `Closes #35`. Patch the body through `gh api repos/Krzysztof318/MailMcp/pulls/<number> -X PATCH -f body="$(cat body.md)"` because `gh pr edit` fails against this repository.

---

## Self-review

**Spec coverage**

| Specification requirement | Task |
| --- | --- |
| `MailConnectionSecurity` with the five draft modes in `Domain` | 1 |
| `MailAuthenticationPolicy` with ordered allow-list and the two opt-ins | 1 |
| Clear-text-over-unencrypted rule owned by `Domain` | 1 |
| Trust anchor reference carried and validated as present when required | 1, 4 |
| `Application` port inputs extended with the resolved policy | 2 |
| `Infrastructure` maps modes to `SecureSocketOptions` | 3 |
| Adapter removes non-permitted mechanisms before authenticating, never widens | 3 |
| Certificate validation unconditionally enabled | 1–3 (no opt-out introduced anywhere), 5 (documented) |
| `Host` binds, validates with `ValidateOnStart`, fails naming the account | 4 |
| Rule re-checked at options validation | 4 |
| `UseSslOnConnect` removed repository-wide | 3, 4 (verified by `grep` in Task 4 Step 5) |
| Domain tests: each mode, rejection, both opt-in combinations, normalization | 1 |
| Infrastructure tests: every mode's mapping, mechanism restriction via the narrow port | 3 |
| Fail-fast tests for each unsafe shape | 1 (through `FindViolations`, per locked decision 6) |
| `docs/features/imap-synchronization.md` documents modes, allow-list, opt-ins | 5 |
| 85% coverage gate | 5 |

**Out of scope, deliberately untouched:** secret reference resolution and trust anchor loading (specification 02), SMTP transport policy, OAuth mailbox authentication, GSSAPI.
