# Secret provisioning

<!-- describes: src/Infrastructure/Secrets/** -->

Every secret-bearing setting holds a *reference* to material the deployment provisions, and the host resolves those references before any worker starts. Under the default `ReferenceOnly` mode with an externally provisioned scheme, a configuration file leaked from a backup or a repository therefore yields credential names and paths, not credentials.

That guarantee is a property of how a deployment is configured, not of MailFathom. Three shapes break it deliberately, and each is a visible choice rather than an accident: `plaintext:` puts the value in the file by definition, the `ReferenceOrInline` and `InlineOnly` modes accept a raw secret in `SecretReference`, and a password written into the connection string never passes through a secret block at all. Each is logged at startup by setting name. When judging what a leaked configuration file exposes, read the deployment's mode and schemes rather than this paragraph.

## The secret block

Every secret-bearing setting is a JSON object carrying a `Name`, a `SecretReference`, and a `Lifetime`:

```json
{
  "MailSynchronization": {
    "Accounts": [
      {
        "AccountId": "primary",
        "DisplayName": "Personal mail",
        "Host": "imap.example.test",
        "Port": 993,
        "UserName": "mailfathom@example.test",
        "Secrets": {
          "Password": {
            "Name": "imap-primary-password",
            "SecretReference": "systemd-credential:imap-primary-password"
          }
        },
        "TransportSecurity": {
          "ConnectionSecurity": "TlsOnConnect",
          "CertificateTrust": "AdditionalTrustedAuthority",
          "TrustedCertificateAuthority": {
            "Name": "primary-private-ca",
            "SecretReference": "file:/run/secrets/private-ca.pem"
          }
        },
        "Folders": [ { "Alias": "inbox", "SpecialUse": "Inbox" } ]
      }
    ]
  },
  "Persistence": {
    "Password": {
      "Name": "postgres-password",
      "SecretReference": "file:/run/secrets/postgres-password"
    }
  }
}
```

| Property | Required | Meaning |
| --- | --- | --- |
| `Name` | yes | The identity every diagnostic, rotation instruction, and audit record names this secret by |
| `SecretReference` | yes | The `<scheme>:<target>` reference, or the material itself under an inline interpretation mode |
| `Lifetime` | no, defaults to `NoLimit` | `NoLimit`, or the instant the secret stops being usable |
| `Password` | no | A nested secret block holding the password of material that is itself protected |

The object rather than a bare string is the unit so that a sibling property can be added later without changing the JSON type of a setting an operator already configured. The nested `Password` is one such sibling, for material protected by its own password. A password-protected PKCS#12 trust anchor is the case that uses it, and the nested block is a secret in its own right, so it carries its own name:

```json
{
  "TrustedCertificateAuthority": {
    "Name": "primary-private-ca",
    "SecretReference": "systemd-credential:private-ca-bundle",
    "Password": {
      "Name": "primary-private-ca-bundle-password",
      "SecretReference": "systemd-credential:private-ca-bundle-password"
    }
  }
}
```

A setting is secret-bearing because it binds to this block type, not because it was annotated. Startup discovers every block by walking the bound configuration, so the rules below apply to settings added in future releases without anyone registering them. The same walk rejects a plain `string` setting whose name contains `Password`, `Secret`, `Credential`, `PrivateKey`, `Token`, or `ApiKey`, because such a setting would bypass validation, resolution, and erasure alike.

### Names

A name is required because the alternatives are worse. An array position renumbers the moment an entry is inserted, so a log line naming position 2 describes a different credential after the next edit; and naming a secret by its value is what the rest of this machinery exists to prevent. The name is what a rotation instruction, an expiry warning, and an audit record can all agree on.

It may carry up to 64 letters, digits, dots, dashes, and underscores, and must begin with a letter or a digit. The set is narrow on purpose: the name is written into logs, metric labels, and audit records without escaping, so a name that could carry a newline or a quotation mark would let a configuration file decide how a log line parses.

Names must be unique within one bound configuration root — within `MailSynchronization`, within `Persistence`, within `McpEndpoint`. Uniqueness stops at the section boundary so that adding a section to a working deployment cannot collide with a name it cannot see. A duplicate, a missing name, and an unacceptable one all fail startup naming the exact setting.

### Lifetimes

Every secret states how long it stays usable. The default is the literal `NoLimit`, written out rather than left absent, so "this credential never expires" is something the configuration says rather than something its silence implies:

```json
{
  "Name": "workstation",
  "SecretReference": "systemd-credential:mailfathom-mcp-workstation-key",
  "Lifetime": "2027-01-31T00:00:00Z"
}
```

A bounded lifetime is an **absolute instant carrying an explicit offset**, never a duration. A duration would restart at every process start and every configuration reload, so a credential retired for a week would come back with the next deployment. An instant without an offset is refused rather than read in the host's local time, because the same configuration would then expire at a different moment on every machine that runs it. `2027-01-31T00:00:00Z` and `2027-01-31T01:00:00+01:00` are the same instant and both are accepted; `2027-01-31T00:00:00` and `2027-01-31` are not.

**A lifetime is enforced where the consumer can act on one.** Today that is the MCP API keys: an expired key authenticates nothing, which is what makes two overlapping keys a rotation rather than an outage. Everywhere else — a mailbox password, the database credential, a trust anchor — the lifetime is recorded and reported, and nothing stops using the credential when it passes. It is a statement of intent that shows up in the log, not a kill switch:

```
warn: Configuration setting MailSynchronization:Accounts:0:Secrets:Password carries the secret
      imap-primary-password, whose configured lifetime ended at 2026-07-30T00:00:00Z.
```

An expired secret never fails startup, in any section. An expired entry left beside its replacement is exactly what a completed rotation looks like, and refusing to start over one would make rotating a credential harder than never rotating it.

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

Material is bounded at 1 MiB, whether it was retrieved or supplied inline. A mistaken reference to a log fails as an oversized secret rather than exhausting memory, and so does a whole document pasted where a credential belongs.

### What a `file:` or `systemd-credential:` target must be

**A regular file, and nothing else.** A path can name a FIFO, a socket, a terminal, or a device just as easily as a file, and none of them holds a credential: a FIFO yields nothing until a writer appears, and `/dev/zero` or `/dev/urandom` yields bytes without end. Any of them fails as `TargetNotRegularFile`, naming the setting as every other resolution failure does.

That is decided from the opened target rather than from a file type, because .NET publishes none portably — permission bits are all `File.GetUnixFileMode` returns, and a FIFO reports the same attributes a file does. What an opened handle exposes is enough: a regular file is seekable and yields exactly the byte count it reports, while a pipe is not seekable at all and a device reports no length while yielding bytes anyway. One consequence is worth knowing: `/dev/null` and an empty file are indistinguishable by that test, and both fail as `MaterialEmpty`.

**And it must answer within five seconds.** Opening a file is the one step no cancellation reaches — the kernel returns from it when it is ready to, which for a FIFO nobody is writing to is never and for a mount that has stopped responding is whenever the storage recovers. Left alone that is a host which neither starts nor explains itself, because startup resolves every reference before any worker begins. A retrieval that has not finished within five seconds is therefore abandoned and reported as `RetrievalTimedOut`, which is a different operator problem from `ProviderUnavailable`: the provider refused in one case and never answered in the other.

What abandoning costs is stated rather than hidden. **The thread already inside the kernel call stays there** until the storage answers or the process ends; nothing can interrupt it. At most four retrievals may be in flight at once, and a stalled one keeps its place, so once four are stuck every further retrieval reports `RetrievalTimedOut` without entering the platform at all. That is the ceiling on the damage a dead mount does: four threads, not one per configured secret. What it does not bound is how long startup takes. Every reference is given its own five seconds, whether it spends them inside the open or waiting for a permit the stuck retrievals never give back, and references are resolved one after another — so a dead mount still costs five seconds for each reference that names it.

## The PostgreSQL connection string

Three shapes are supported, because provisioning systems differ and none of them is wrong.

| Setting | When to use it |
| --- | --- |
| `ConnectionStrings:mailfathom` plus `Persistence:Password` | The connection string names host, database, and user in ordinary configuration while only the credential is provisioned. |
| `Persistence:ConnectionString` | A secret store holds the whole connection string. It is more than a password, so keeping it whole means one artifact to rotate instead of a credential split across two systems. |
| `ConnectionStrings:mailfathom` alone | An orchestrator or a pre-resolving configuration provider injects a complete connection string. Aspire does this locally. |

```json
{
  "Persistence": {
    "ConnectionString": {
      "Name": "mailfathom-connection-string",
      "SecretReference": "systemd-credential:mailfathom-connection-string"
    }
  }
}
```

`Persistence:ConnectionString` replaces `ConnectionStrings:mailfathom` rather than adding to it. Configuring a password in both the connection string and `Persistence:Password` is a startup failure, because two sources for one credential leave the effective one decided by implementation order — and an operator rotating the one that loses would see neither an effect nor an error.

A password written into the connection string with no secret block is **not** rejected. The same shape is both a mistake and a legitimate deployment: an orchestrator-injected connection string never touched a file anyone could commit. Under `ReferenceOnly` it is logged as a warning naming the setting, because that mode is the deployment stating that every secret arrives by reference.

## Deployment shapes

MailFathom runs both as a native systemd service and as a container, and neither is the fallback. No container-specific or Kubernetes-specific scheme exists or is needed, because a container secret *is* a file.

### Native systemd service

Provision the credential with `LoadCredential=` for a file the service user may read, or `LoadCredentialEncrypted=` together with `systemd-creds encrypt` for material encrypted at rest. systemd derives the directory from `$CREDENTIALS_DIRECTORY` and restricts access to the service's own user.

```ini
[Service]
LoadCredentialEncrypted=imap-primary-password:/etc/mailfathom/imap-primary-password.cred
LoadCredential=postgres-password:/etc/mailfathom/postgres-password

# Bound the in-memory exposure that no code-level measure can address.
LimitCORE=0
```

Reference them as `systemd-credential:imap-primary-password` and `systemd-credential:postgres-password`.

#### What an encrypted credential is bound to

`systemd-creds encrypt` defaults to `--with-key=auto`, and that default is not a neutral one. It derives the key from the machine's TPM2 chip when one is found and the command is not running in a container, **and** from the host key in `/var/lib/systemd/credential.secret` when `/var/lib/systemd/` is on persistent media. On an ordinary machine both hold, so decrypting the credential again needs both the original chip and that OS installation. The TPM2 half is never written to the file system at all, and the host half is a root-only file systemd generates the first time it needs one.

MailFathom implements none of this and has no setting for it. systemd decrypts the credential as it starts the unit and places the plaintext in `$CREDENTIALS_DIRECTORY`, which is the only thing a `systemd-credential:` reference ever reads. The binding therefore happens entirely below the reference, and it is chosen by the flags on the `systemd-creds` command line and nowhere else.

**A `.cred` file is consequently not a copy of the material it holds.** It opens on the machine that produced it and on no other, and it stops opening on that machine once the board is replaced or `/var/lib/systemd/` is lost to a reinstall. For a mailbox password that is an inconvenience, because the provider issues another one. For the data-encryption key it is exactly the loss that key must never suffer, which [the data-encryption key](#the-data-encryption-key) states in full.

Two further properties run against the intuition and are worth stating:

- **`auto` expresses a preference, not a requirement.** With no TPM2 present, or in a container, it encrypts against the host key alone and reports success, so an operator who wanted the chip cannot tell from the result what they got. `--with-key=tpm2` is what makes it a requirement, because it fails instead of falling back. `auto` itself fails outright only when neither a TPM2 nor a persistent `/var/lib/systemd/` is available.
- **No PCRs are bound unless you ask for them.** `--tpm2-pcrs=` is opt-in and binds none by default, so a firmware or bootloader update does *not* invalidate the credential. Binding PCRs buys measured-boot assurance at the cost of recoverability, which makes it a deliberate choice rather than a hardening step to apply by reflex.

`--with-key=null` provides neither confidentiality nor authenticity, because the key is a fixed zero-length one, so the encryption is a format rather than a protection and the resulting file opens anywhere. **A machine with no TPM2 does not need it** — `auto` still encrypts against the host key there, and reaches for `null` never. What it covers is material produced where neither a TPM2 nor a persistent `/var/lib/systemd/` is available and `auto` would therefore fail outright. systemd refuses to decrypt such a credential on a machine that has a TPM2 with UEFI SecureBoot enabled, which is a refusal that cannot fire on the chip-less machine where somebody would most likely have reached for it.

### Docker or Podman Compose

Compose mounts a secret at `/run/secrets/<name>`, so the reference is `file:/run/secrets/imap-primary-password`.

```yaml
services:
  mailfathom:
    secrets: [imap-primary-password, postgres-password]
secrets:
  imap-primary-password:
    file: ./secrets/imap-primary-password
```

**`LoadCredentialEncrypted=` does not reach this shape under either engine**, and what decides that is whether a systemd service manager starts the container rather than whether a container is involved at all. Compose starts no per-service unit under dockerd or under `podman compose` alike, so `$CREDENTIALS_DIRECTORY` never exists for the container and a `systemd-credential:` reference resolves to nothing. A Podman Quadlet sits on the other side of that line, because a `.container` file *is* a systemd unit source: a container started from one takes credentials exactly as the native service does, with the caveat that reaching the decrypted material there needs `SecurityLabelDisable=true` and so trades SELinux label separation for it. MailFathom ships no Quadlet. Here a secret is a file, protected by the host it sits on and by the permissions on the directory holding it rather than by anything the format carries.

### Kubernetes

A Secret mounted as a read-only tmpfs volume becomes one file per key at the path the operator chose, so the reference is `file:/etc/mailfathom-secrets/imap-primary-password`. A Secret projected into the environment block is `env:` instead, subject to the caveat below.

Mounting is the shape to prefer, for a reason beyond memory hygiene: because material is resolved per use rather than cached, a Secret the cluster rotates behind an unchanged mount path reaches the next connection without a restart and without a configuration reload. A Secret projected into the environment block is fixed for the life of the pod, so rotating it means replacing the pod.

A Secrets Store CSI driver — Vault, Azure Key Vault, AWS Secrets Manager — needs no MailFathom adapter for the same reason no Kubernetes scheme exists: it mounts files, so the reference is `file:` and the store's own authentication stays the driver's concern.

**`LoadCredentialEncrypted=` does not reach a pod either**, and the encryption behind it would work against this shape even if it did. Nothing schedules a systemd unit here, so `$CREDENTIALS_DIRECTORY` never exists; and per-machine sealing is the opposite of what a replica set needs, because [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) requires every replica to open what any other replica sealed, which no TPM-bound copy satisfies unless the same material is sealed once per node. What protects a Secret at rest is the cluster's own mechanism, which is configured on the API server rather than here and is absent until the cluster enables it: without an `EncryptionConfiguration`, upstream Kubernetes stores a Secret's values unencrypted in etcd, so a deployment that wants encryption at rest here arranges it rather than inheriting it.

[Configuration sources](configuration-sources.md#kubernetes) states the whole mapping, including how the non-secret half of a deployment reaches MailFathom through a mounted ConfigMap.

### Non-production automation

`env:MAILFATHOM_IMAP_PRIMARY_PASSWORD` reads a CI or orchestrator environment variable. It is not recommended in production, for the memory reason stated below as well as for the usual visibility of an environment block to anything that can read `/proc`.

## Trailing newlines

`LoadCredential=`, Compose secrets, and Kubernetes Secret files routinely end with a newline, and an untrimmed byte presents as a wrong password. MailFathom therefore strips **one** trailing newline when it decodes material as text. Binary material is never modified: a PKCS#12 bundle or a DER-encoded certificate survives resolution byte for byte.

## The data-encryption key

MailFathom seals values it stores under a key the deployment provisions, and the key arrives as an ordinary secret reference like every other credential. Two things depend on it today. One value is sealed under it — the OAuth refresh token an account's authorization server rotates, which [mailbox OAuth](mailbox-oauth.md#rotation) describes. The other is not sealed at all: the key that signs an [attachment download link](../features/email-content.md#what-a-download-link-is-and-what-bounds-it) is derived from the ring per operation, under an identity of its own so it is never the material that seals a stored value. A deployment whose mailboxes all authenticate with a password and which hands out no attachment links needs no key at all and starts without one; one that wants links and configures no ring serves every other part of a read and issues none. What differs is the material behind it: it is **base64 that decodes to exactly 32 bytes**, and startup refuses anything else naming the setting rather than accepting a weaker key.

Generate one with:

```console
openssl rand -base64 32
```

**This is not the command beside it.** The two database passwords in a Compose deployment are generated with `openssl rand -base64 33`, which is right for a password and wrong for a key, so copying the neighbouring line produces material startup rejects. Thirty-two is what AES-256 takes.

The key is generated once and never regenerated. Losing it makes every value sealed under it unopenable, and the failure appears at the next read rather than at the moment of loss, so **back it up with the database rather than beside it** — a database restored without its key restores nothing that was sealed. Nothing in MailFathom generates the key for you, in any deployment channel, for the same reason: a mechanism that can create a key can create a second one. [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) records that decision and what each alternative costs.

**What is backed up is the base64 the generating command printed, and a sealed `.cred` file does not stand in for it.** That is the substitution most likely to look like a backup and be none: a credential produced by `systemd-creds encrypt` opens on the machine that produced it and nowhere else, as [what an encrypted credential is bound to](#what-an-encrypted-credential-is-bound-to) describes, so a database restored beside a `.cred` file carried off a machine that no longer exists restores nothing either. Keep the base64 wherever the deployment keeps material it cannot re-obtain — the mail provider can issue another mailbox password, and nothing can issue this key again — and treat the `.cred` file as an artifact of one host, produced from that material again on whatever host runs next.

`DataEncryption:ActiveKeyId` selects which configured key new values are sealed under, and the ring keeps every key a stored value may still name. Rotating is therefore two steps and no downtime: add the new key, move `ActiveKeyId` to it, and leave the previous key configured until nothing references it. [Configuration reference](configuration-reference.md#dataencryption) states every key of the section.

## Certificate material

A trust anchor is provisioned like any other secret, but the bytes behind it are loaded as a certificate rather than used as a credential. PEM, DER, and PKCS#12 all load, recognized from the material itself so a mistyped encoding hint cannot exist. Only PEM can be supplied inline, because the other two are binary; an inline block carrying them fails startup naming the encoding. A bundle's password, when it has one, goes in the nested `Password` block.

An anchor that carries a private key is rejected. Provision the public certificate — `openssl x509 -in ca.pem -out ca-public.pem` if the file you have holds more than that — because a trust anchor needs nothing else, and a private key MailFathom holds is an authority MailFathom could impersonate.

Two things are provisioned this way and they are judged differently. A mail account's anchor decides whether MailFathom trusts the *server* it connects to; an [MCP client certificate profile](mcp-endpoint.md#client-certificates)'s anchors decide whether MailFathom trusts a *client* connecting to it, and a profile names several so an authority can rotate by overlap.

[IMAP synchronization](../features/imap-synchronization.md) describes how the server-side anchor is used, including the revocation trade-off a private authority implies.

## Interpretation modes

A secret-bearing setting does not always carry a reference. How MailFathom reads one is an explicit deployment choice, configured once at the root:

```json
{ "Secrets": { "Interpretation": "ReferenceOnly" } }
```

| Mode | Behavior |
| --- | --- |
| `ReferenceOnly` | The value must be `<scheme>:<target>`. Anything else fails startup. **This is the default.** |
| `ReferenceOrInline` | A registered scheme resolves through its adapter; any other value is taken as the secret itself. |
| `InlineOnly` | Nothing is parsed. Every value is already the secret. |

`ReferenceOnly` is what keeps a mistyped `fil:/run/secrets/imap` a startup failure instead of a password, and it is what makes a plain-text password pasted where a reference belongs fail loudly rather than authenticate successfully.

`InlineOnly` exists for a configuration provider that resolved the secret *before* MailFathom bound it. Azure App Configuration with Key Vault references is the concrete case: the provider substitutes the vault value, so the bound setting is the raw secret with no prefix MailFathom could recognize. That integration needs no MailFathom adapter and no code change — only this mode.

The active mode is logged at startup. Every setting that resolved to an inline value is logged **by name**, never by value, so an unintended inline secret is discoverable rather than silent. That includes `plaintext:` under any mode, because the value sits in configuration either way.

An undefined mode is a startup failure. A numeric value such as `99` binds without complaint, and treating it as the strictest mode would be safe by accident while reporting a mode nobody selected.

### Addressing the block from a flattening provider

The block is a nested object in JSON but requires no JSON provider. Every hierarchical provider addresses the same setting by its colon-separated path:

| Provider | Key |
| --- | --- |
| Azure App Configuration | `MailSynchronization:Accounts:0:Secrets:Password:SecretReference` |
| Environment block | `MailSynchronization__Accounts__0__Secrets__Password__SecretReference` |

Combined with `InlineOnly`, that is the complete Azure App Configuration path: the store holds the key, Key Vault holds the secret, the provider maps one to the other, and MailFathom binds an already-resolved value and uses it as material.

## Startup behavior

Secret resolution runs before any hosted service starts, so no synchronization run ever starts against an unresolvable secret. Every failure is reported together, each naming its configuration path and a stable failure identity:

```
MailSynchronization:Accounts:0:Secrets:Password — the secret reference could not be resolved [MaterialNotFound].
MailSynchronization:Accounts:1:Secrets:Password — the secret reference could not be resolved [SchemeMissing].
MailSynchronization:Accounts:2:Secrets:Password — the secret reference could not be resolved [RetrievalTimedOut].
MailSynchronization:Accounts:3:Secrets:Password:Name — every secret needs a name, which is the identity a rotation, an expiry, and an audit record name it by.
```

A target that never answers is one line of that report rather than the end of it, which is what the deadline above buys: the reference after an unreachable mount is still resolved and still reported.

The path and the identity are the whole vocabulary. No message, log line, or exception carries the reference target, the environment variable's value, or any part of the material.

Startup resolves and immediately erases. Each actual use resolves again, so nothing long-lived is cached and material rotated behind an unchanged reference is picked up by the next operation without a restart. That includes the database credential, which is retrieved when a physical connection opens rather than baked into the pool's connection string. [Secret rotation](secret-rotation.md) is the operator procedure and states the one shape that still needs a restart.

## Secret material in process memory

Material MailFathom allocates is held in a pinned byte buffer, never in a `string`, never in a pooled buffer, and never in a `SecureString`. The buffer is erased with `CryptographicOperations.ZeroMemory` when the operation that owns it ends. `SecureString` is deliberately unused: Microsoft recommends against it for new development and it does not encrypt its storage on non-Windows platforms, which is every environment MailFathom targets.

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

Two integration shapes exist and must not be conflated. A provider that **pre-resolves** — Azure App Configuration with Key Vault references — does its mapping below MailFathom in the configuration pipeline and needs no adapter at all, only `InlineOnly`. A store MailFathom **queries itself** — direct Key Vault, HashiCorp Vault, AWS Secrets Manager — earns a scheme, because its retrieval behavior genuinely differs.

Adding one is a registration rather than a refactor:

- one `ISecretSchemeResolver` declaring its own scheme, which the composite dispatch picks up automatically;
- one registration extension called beside `AddSecretResolution`;
- its own timeouts, retry policy, endpoint configuration, and caching, which stay inside the adapter;
- authentication through platform-issued identity — an Azure managed identity, a Kubernetes ServiceAccount token, a Vault role — never through a MailFathom-held credential, which would be circular;
- a `THIRD_PARTY_LICENSES.md` entry in the same change set, plus review of the SDK license, service terms, telemetry behavior, and data-processing implications.
