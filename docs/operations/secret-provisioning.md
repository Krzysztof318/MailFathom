# Secret provisioning

Every secret-bearing setting holds a *reference* to material the deployment provisions, and the host resolves those references before any worker starts. Under the default `ReferenceOnly` mode with an externally provisioned scheme, a configuration file leaked from a backup or a repository therefore yields credential names and paths, not credentials.

That guarantee is a property of how a deployment is configured, not of MailMcp. Three shapes break it deliberately, and each is a visible choice rather than an accident: `plaintext:` puts the value in the file by definition, the `ReferenceOrInline` and `InlineOnly` modes accept a raw secret in `SecretReference`, and a password written into the connection string never passes through a secret block at all. Each is logged at startup by setting name. When judging what a leaked configuration file exposes, read the deployment's mode and schemes rather than this paragraph.

## The secret block

Every secret-bearing setting is a JSON object whose `SecretReference` property carries the reference:

```json
{
  "MailSynchronization": {
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
          "CertificateTrust": "AdditionalTrustedAuthority",
          "TrustedCertificateAuthority": { "SecretReference": "file:/run/secrets/private-ca.pem" }
        },
        "Folders": [ { "Alias": "inbox", "SpecialUse": "Inbox" } ]
      }
    ]
  },
  "Persistence": {
    "Password": { "SecretReference": "file:/run/secrets/postgres-password" }
  }
}
```

The object rather than a bare string is the unit so that a sibling property can be added later — a bundle password, a format hint, a managed-store version pin — without changing the JSON type of a setting an operator already configured. One sibling exists today: an optional nested `Password` block, itself a secret block, for material that is protected by its own password. A password-protected PKCS#12 trust anchor is the case that uses it:

```json
{
  "TrustedCertificateAuthority": {
    "SecretReference": "systemd-credential:private-ca-bundle",
    "Password": { "SecretReference": "systemd-credential:private-ca-bundle-password" }
  }
}
```

A setting is secret-bearing because it binds to this block type, not because it was annotated. Startup discovers every block by walking the bound configuration, so the rules below apply to settings added in future releases without anyone registering them. The same walk rejects a plain `string` setting whose name contains `Password`, `Secret`, `Credential`, `PrivateKey`, or `Token`, because such a setting would bypass validation, resolution, and erasure alike.

`UserName` is deliberately not a secret block. A mailbox user name is an identifier the operator already writes next to the host; it is excluded from logs as personal data, but turning it into a reference would double the provisioning burden for no confidentiality gain.

## The reference grammar

A reference is `<scheme>:<target>`, split on the **first** colon only, so a Windows path or a URL in the target survives untouched. The scheme is matched ignoring case and surrounding whitespace; the target is taken byte for byte, because a leading or trailing space is a valid password character.

| Scheme | Target | Reads |
| --- | --- | --- |
| `systemd-credential` | credential name | The credentials directory systemd exposes to the unit through `$CREDENTIALS_DIRECTORY` |
| `file` | absolute path | A deployment-provisioned protected file |
| `env` | variable name | The process environment block |
| `plaintext` | the value itself | Nothing; the target *is* the material |

An unknown scheme is a startup failure naming the setting, which is also how an operator learns that a provider adapter was not compiled in or not enabled.

`plaintext:` is the unambiguous spelling for a literal that would otherwise look like a reference — a password whose value genuinely begins with `file:`. It retrieves nothing, so it is reported as inline material and earns the same startup warning as any other value written into configuration.

A credential name for `systemd-credential:` may not contain `/`, `\`, or `..`, so a reference cannot escape the directory the unit was granted.

Material is bounded at 1 MiB, whether it was retrieved or supplied inline. A mistaken reference to a log or a device-backed pseudo-file fails as an oversized secret rather than exhausting memory, and so does a whole document pasted where a credential belongs.

## The PostgreSQL connection string

Three shapes are supported, because provisioning systems differ and none of them is wrong.

| Setting | When to use it |
| --- | --- |
| `ConnectionStrings:mailmcp` plus `Persistence:Password` | The connection string names host, database, and user in ordinary configuration while only the credential is provisioned. |
| `Persistence:ConnectionString` | A secret store holds the whole connection string. It is more than a password, so keeping it whole means one artifact to rotate instead of a credential split across two systems. |
| `ConnectionStrings:mailmcp` alone | An orchestrator or a pre-resolving configuration provider injects a complete connection string. Aspire does this locally. |

```json
{
  "Persistence": {
    "ConnectionString": { "SecretReference": "systemd-credential:mailmcp-connection-string" }
  }
}
```

`Persistence:ConnectionString` replaces `ConnectionStrings:mailmcp` rather than adding to it. Configuring a password in both the connection string and `Persistence:Password` is a startup failure, because two sources for one credential leave the effective one decided by implementation order — and an operator rotating the one that loses would see neither an effect nor an error.

A password written into the connection string with no secret block is **not** rejected. The same shape is both a mistake and a legitimate deployment: an orchestrator-injected connection string never touched a file anyone could commit. Under `ReferenceOnly` it is logged as a warning naming the setting, because that mode is the deployment stating that every secret arrives by reference.

## Deployment shapes

MailMcp runs both as a native systemd service and as a container, and neither is the fallback. No container-specific or Kubernetes-specific scheme exists or is needed, because a container secret *is* a file.

### Native systemd service

Provision the credential with `LoadCredential=` for a file the service user may read, or `LoadCredentialEncrypted=` together with `systemd-creds encrypt` for material encrypted at rest. systemd derives the directory from `$CREDENTIALS_DIRECTORY` and restricts access to the service's own user.

```ini
[Service]
LoadCredentialEncrypted=imap-primary-password:/etc/mailmcp/imap-primary-password.cred
LoadCredential=postgres-password:/etc/mailmcp/postgres-password

# Bound the in-memory exposure that no code-level measure can address.
LimitCORE=0
```

Reference them as `systemd-credential:imap-primary-password` and `systemd-credential:postgres-password`.

### Docker or Podman Compose

Compose mounts a secret at `/run/secrets/<name>`, so the reference is `file:/run/secrets/imap-primary-password`.

```yaml
services:
  mailmcp:
    secrets: [imap-primary-password, postgres-password]
secrets:
  imap-primary-password:
    file: ./secrets/imap-primary-password
```

### Kubernetes

A Secret mounted as a read-only tmpfs volume becomes one file per key at the path the operator chose, so the reference is `file:/etc/mailmcp-secrets/imap-primary-password`. A Secret projected into the environment block is `env:` instead, subject to the caveat below.

### Non-production automation

`env:MAILMCP_IMAP_PRIMARY_PASSWORD` reads a CI or orchestrator environment variable. It is not recommended in production, for the memory reason stated below as well as for the usual visibility of an environment block to anything that can read `/proc`.

## Trailing newlines

`LoadCredential=`, Compose secrets, and Kubernetes Secret files routinely end with a newline, and an untrimmed byte presents as a wrong password. MailMcp therefore strips **one** trailing newline when it decodes material as text. Binary material is never modified: a PKCS#12 bundle or a DER-encoded certificate survives resolution byte for byte.

## Certificate material

A trust anchor is provisioned like any other secret, but the bytes behind it are loaded as a certificate rather than used as a credential. PEM, DER, and PKCS#12 all load, recognized from the material itself so a mistyped encoding hint cannot exist. Only PEM can be supplied inline, because the other two are binary; an inline block carrying them fails startup naming the encoding. A bundle's password, when it has one, goes in the nested `Password` block.

An anchor that carries a private key is rejected. Provision the public certificate — `openssl x509 -in ca.pem -out ca-public.pem` if the file you have holds more than that — because a trust anchor needs nothing else, and a private key MailMcp holds is an authority MailMcp could impersonate.

[IMAP synchronization](../features/imap-synchronization.md) describes how the anchor is used, including the revocation trade-off a private authority implies.

## Interpretation modes

A secret-bearing setting does not always carry a reference. How MailMcp reads one is an explicit deployment choice, configured once at the root:

```json
{ "Secrets": { "Interpretation": "ReferenceOnly" } }
```

| Mode | Behavior |
| --- | --- |
| `ReferenceOnly` | The value must be `<scheme>:<target>`. Anything else fails startup. **This is the default.** |
| `ReferenceOrInline` | A registered scheme resolves through its adapter; any other value is taken as the secret itself. |
| `InlineOnly` | Nothing is parsed. Every value is already the secret. |

`ReferenceOnly` is what keeps a mistyped `fil:/run/secrets/imap` a startup failure instead of a password, and it is what makes a plain-text password pasted where a reference belongs fail loudly rather than authenticate successfully.

`InlineOnly` exists for a configuration provider that resolved the secret *before* MailMcp bound it. Azure App Configuration with Key Vault references is the concrete case: the provider substitutes the vault value, so the bound setting is the raw secret with no prefix MailMcp could recognize. That integration needs no MailMcp adapter and no code change — only this mode.

The active mode is logged at startup. Every setting that resolved to an inline value is logged **by name**, never by value, so an unintended inline secret is discoverable rather than silent. That includes `plaintext:` under any mode, because the value sits in configuration either way.

An undefined mode is a startup failure. A numeric value such as `99` binds without complaint, and treating it as the strictest mode would be safe by accident while reporting a mode nobody selected.

### Addressing the block from a flattening provider

The block is a nested object in JSON but requires no JSON provider. Every hierarchical provider addresses the same setting by its colon-separated path:

| Provider | Key |
| --- | --- |
| Azure App Configuration | `MailSynchronization:Accounts:0:Secrets:Password:SecretReference` |
| Environment block | `MailSynchronization__Accounts__0__Secrets__Password__SecretReference` |

Combined with `InlineOnly`, that is the complete Azure App Configuration path: the store holds the key, Key Vault holds the secret, the provider maps one to the other, and MailMcp binds an already-resolved value and uses it as material.

## Startup behavior

Secret resolution runs before any hosted service starts, so no synchronization run ever starts against an unresolvable secret. Every failure is reported together, each naming its configuration path and a stable failure identity:

```
MailSynchronization:Accounts:0:Secrets:Password — the secret reference could not be resolved [MaterialNotFound].
MailSynchronization:Accounts:1:Secrets:Password — the secret reference could not be resolved [SchemeMissing].
```

The path and the identity are the whole vocabulary. No message, log line, or exception carries the reference target, the environment variable's value, or any part of the material.

Startup resolves and immediately erases. Each actual use resolves again, so nothing long-lived is cached and material rotated behind an unchanged reference is picked up by the next operation without a restart. That includes the database credential, which is retrieved when a physical connection opens rather than baked into the pool's connection string. [Secret rotation](secret-rotation.md) is the operator procedure and states the one shape that still needs a restart.

## Secret material in process memory

Material MailMcp allocates is held in a pinned byte buffer, never in a `string`, never in a pooled buffer, and never in a `SecureString`. The buffer is erased with `CryptographicOperations.ZeroMemory` when the operation that owns it ends. `SecureString` is deliberately unused: Microsoft recommends against it for new development and it does not encrypt its storage on non-Windows platforms, which is every environment MailMcp targets.

Four residual exposures are real and are not papered over:

- **`env:` material cannot be erased.** The platform returns it as a `string`, which is immutable, unpinned, and copied again whenever the collector compacts memory.
- **Inline values cannot be erased either**, for the same reason. That is a genuine cost of `ReferenceOrInline` and `InlineOnly`, and another reason `ReferenceOnly` is the default.
- **Two framework contracts take a `string`** — the IMAP client's authentication call and the PostgreSQL connection-string password. A short-lived copy is unavoidable at exactly those call sites; it is created there, as late as possible, and never stored, logged, or passed on.
- **Managed memory remains readable through a process dump, a debugger, or swap.** No code-level measure changes that.

The last one is an operational control:

- **systemd:** set `LimitCORE=0` on the unit, and set `Storage=none` and `ProcessSizeMax=0` in `/etc/systemd/coredump.conf` (or a drop-in) so `systemd-coredump` writes nothing.
- **Containers:** run with `--ulimit core=0`, and keep the host's `kernel.core_pattern` from piping dumps to a collector that retains them.
- **Both shapes:** keep the service's memory out of swap, either by running the host without swap or by confining the service with a memory limit it does not exceed.

Locking pages with `mlock` is deliberately not attempted. It would need P/Invoke plus `CAP_IPC_LOCK` and a raised `RLIMIT_MEMLOCK` in every deployment shape, against the repository rule that restricts unsafe and platform-invoke code to measured need, and it addresses neither dumps nor debuggers.

## Adding a managed secret store

This section describes the extension contract, not shipped behavior. No managed-store adapter exists today.

Two integration shapes exist and must not be conflated. A provider that **pre-resolves** — Azure App Configuration with Key Vault references — does its mapping below MailMcp in the configuration pipeline and needs no adapter at all, only `InlineOnly`. A store MailMcp **queries itself** — direct Key Vault, HashiCorp Vault, AWS Secrets Manager — earns a scheme, because its retrieval behavior genuinely differs.

Adding one is a registration rather than a refactor:

- one `ISecretSchemeResolver` declaring its own scheme, which the composite dispatch picks up automatically;
- one registration extension called beside `AddSecretResolution`;
- its own timeouts, retry policy, endpoint configuration, and caching, which stay inside the adapter;
- authentication through platform-issued identity — an Azure managed identity, a Kubernetes ServiceAccount token, a Vault role — never through a MailMcp-held credential, which would be circular;
- a `LICENSES.md` entry in the same change set, plus review of the SDK license, service terms, telemetry behavior, and data-processing implications.
