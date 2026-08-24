---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-03
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Seal data at rest under one deployment-wide symmetric key ring, provisioned as a secret reference the operator creates

<!-- describes: backend/src/Host/Configuration/DataEncryption/**, backend/src/Infrastructure/DataEncryption/**, backend/src/Common/AesGcmEnvelope.cs, backend/src/AppHost/Program.cs, backend/src/AppHost/OrchestrationContract.cs -->

## Context and Problem Statement

MailFathom is about to hold its first credential that the service itself writes. A mailbox refresh token is a long-lived credential acting for a named mailbox owner, and until now it arrived the way every other credential does: as a `SecretReference` the process resolves and never writes back. That read-only path is why a refresh token the authorization server rotates cannot be followed, and `docs/operations/mailbox-oauth.md` documents the consequence as an instruction to the operator — watch for a warning and re-authorize before the configured token stops being accepted. An operator watching a log is a mitigation, not a design.

Giving the service a store it can write moves the credential out of the operator's secret system and into MailFathom's own database, which is the whole reason this decision exists. A credential the service persists is a credential the service is now responsible for protecting, and the database is a place a refresh token has never been before: it is dumped for backups, streamed to replicas, restored onto other machines, and read by anyone who obtains one of those copies.

The decision question has three parts that are usually collapsed into one: **what seals the data**, **where the key comes from**, and **who creates the key in the first place**. The third is the part that decides whether the feature is usable, and it is the one where the obvious courtesy — generate it so nobody has to — is the dangerous answer.

Recorded on issue 329, whose scope this ADR is part of. Issue 330 delivers the provisioning through each deployment channel and issue 331 writes a grant through the administrative endpoint; both depend on what is decided here. No numbered specification backs it, though specification 02a defines the secret-reference grammar this reuses and specification 02b defines the rotation vocabulary.

## Decision Drivers

- The threat is a **copy of the database**, not a compromised host: a backup file, a logical dump, a read replica, a restored volume, a decommissioned disk. A mechanism that only protects the running server answers none of them.
- Every replica of one deployment must open what any other replica sealed. This is stated as an acceptance criterion on issue 329 and it eliminates every per-machine scheme, including the one `mfctl` already uses for its own workstation store.
- A key must never be **regenerated**. Losing it is not a reset an operator notices and repairs; it is data that no longer opens, discovered at the first read rather than at the moment of loss.
- Provisioning must have **one shape across all three deployment channels** — Compose, Kubernetes, and a native systemd unit — because a mechanism per channel is three things to document, three to test, and three to get wrong.
- MailFathom must start with **no network dependency for the key**. A token refresh that first needs a key service turns one outage into two.
- The key ring must be **general from the start**. Refresh tokens are the first sealed column and will not be the last; a second one must configure nothing new.
- Local development must not need the operator's ceremony. A developer running `dotnet run` on the AppHost is not provisioning a deployment.

## Considered Options

The decision has two independent axes, and an option on one does not constrain an option on the other.

**What seals the data:**

1. Application-level AES-256-GCM through the existing `AesGcmEnvelope`, under a key ring the deployment configures.
2. PostgreSQL `pgcrypto`, sealing in the database with `pgp_sym_encrypt`.
3. Volume or full-disk encryption alone, with the column left in the clear.
4. An external key-management service — Vault transit, AWS KMS, Azure Key Vault — invoked per operation.

**Where the key comes from, and who creates it:**

- **A.** The operator generates 32 bytes once at install and provisions them as an ordinary `SecretReference`, identically in every channel.
- **B.** The host generates the key on first start into a durable state directory, and every channel supplies one.
- **C.** Each deployment channel's own tooling generates it: a one-shot init service under Compose, a `pre-install` hook Job in the Helm chart, an `ExecStartPre` in the systemd unit.
- **D.** `mfctl` generates it, either as a local command, as a full installer, or through the administrative endpoint.

## Decision Outcome

Chosen options: **1, application-level AES-256-GCM under a configured key ring**, and **A, an operator-generated key provisioned as a secret reference** — with one deliberate exception for local development, recorded below.

Option 1 wins because it is the only one whose protection survives the copy. The value is already ciphertext before it reaches Npgsql, so it is ciphertext in the write-ahead log, in a `pg_dump`, on a replica, and in a restored volume, and the key is never in the database, never in a SQL statement, and never in a query log.

Option A wins because provisioning a key is a one-line act the operator already performs twice for the two database passwords, and because every alternative buys the removal of that one line by adding a mechanism that can lose the key. That trade is wrong in the direction that cannot be undone.

### What is decided

**The key is a `SecretReference`, like every credential MailFathom already holds.** It adds no provisioning mechanism, no new startup failure mode, and no new supply-chain surface. A `systemd-credential:`, a `file:`, or an `env:` reference resolves it, and a mounted Kubernetes `Secret` gives every replica of a deployment the same bytes.

```jsonc
{
  "DataEncryption": {
    "ActiveKeyId": "2026-08",
    "Keys": [
      {
        "KeyId": "2026-08",
        "Material": { "Name": "mailfathom-data-key", "SecretReference": "systemd-credential:mailfathom-data-key" }
      }
    ]
  }
}
```

**The ring is its own configuration root rather than a section of `Persistence`.** The database is the first thing sealed under it and there is no reason it will be the last — a cached credential, an exported artifact, and a queued outbound payload are all candidates, and none of them is persistence. Nesting the ring under the first consumer would name it after the consumer, and the second one would either move the section or inherit a section whose name says it belongs to something else. `DataEncryption` also becomes its own uniqueness scope for secret names, which is what every configuration root already is, so a key's material cannot collide with a name `Persistence` already uses.

`DataProtection` was the other candidate and is refused: ASP.NET Core publishes a subsystem under exactly that name with a different purpose, key lifetime, and threat model, and a reader who knows it would read this section as that one.

**A key entry is identified twice, and the two identities have different jobs.** `KeyId` is written into every sealed value, so it is an identity the database holds and it can never be changed once a single row references it. `Material.Name` is the operator's own label, required of every secret block by the rules `docs/operations/secret-provisioning.md` already states, and it is what a validation failure, a rotation instruction, and an audit record name this key by — exactly as they do for a mailbox password or a trust anchor.

No third name is added on the key entry itself. A key entry holds exactly one material, so a label there would be a second name for the same object, and the two would disagree the first time somebody edited one of them.

**The material is base64 that decodes to exactly 32 bytes.** Base64 rather than raw bytes because every channel that carries this already carries text: a Compose secret file, a Kubernetes `Secret` value, and a systemd credential are all handled as text by the tools that write them, and a raw 32-byte file acquires a trailing newline the first time anyone edits it. Exactly 32 because that is what AES-256 takes, and the length is validated at startup rather than at the first read — a shorter key is a typo, not a weaker key.

The generating command is therefore `openssl rand -base64 32`, and it is documented as such in one place. The neighbouring database passwords use `-base64 33`, which is correct for them and wrong here, so the two must never be copied from one another.

**Every sealed value is bound to `(subject, purpose, keyId)` as associated data.** Sharing one key ring across future sealed columns is only safe if a value cannot be moved between them, and the binding is what makes it unsafe to try: a sealed refresh token does not open as anything else even under the same key, a row copied between accounts fails to open rather than opening as the wrong owner's credential, and a row from another deployment fails to open at all. `purpose` is what lets the second sealed column arrive without a second key.

**Each sealed value stores the identifier of the key that sealed it.** Without it, replacing a key is a flag day with the service stopped. With it, two keys coexist in the ring, a value is re-sealed under the active key the next time it is written, and a key is retired once nothing references it. That is the whole rotation model, and it is the reason `ActiveKeyId` names a key rather than the ring holding a single one.

**A deployment that seals nothing needs no ring.** An absent `DataEncryption` section is a valid configuration and not an omission to report, because no stored value carries a key identifier until something seals one. The section becomes required by whatever first seals a value, at the point that value is written, rather than by the ring's own existence — otherwise adding the ring would refuse to start every deployment that has no use for it yet, which is a break bought for nothing. What is refused even in an absent ring is an `ActiveKeyId` naming a key nothing configures, because an operator who wrote one meant to provision it.

**The chart must never generate the key, and neither must a Compose hook.** A Helm-generated value regenerates on every `helm upgrade` unless it is guarded with `lookup`, and `lookup` returns nothing during `helm template`, during a dry run, and under Argo CD. For a password that is a reset an operator notices and repairs. For a data-encryption key it is every sealed row becoming permanently unopenable, discovered at the first read after the upgrade rather than at the upgrade.

**Local development is the one place where the operator's step is the wrong answer, and the answer is a fixed development constant.** A developer running the Aspire app host is not provisioning a deployment, and a key they have to generate by hand before the first `dotnet run` is ceremony bought for nothing: the local database holds synthetic mail and the machine is not a deployment.

What that exception must not be is generation into a store that can outlive, or be outlived by, the data it protects. `backend/src/AppHost/Program.cs` states the PostgreSQL password as the fixed constant `postgres` rather than generating one for exactly that reason — a generated password persisted per run diverges from a data volume that survives it, because PostgreSQL applies a password when it initializes an empty data directory and never again. A key is worse under the same failure: a diverged password reports an authentication error, and a diverged key leaves every locally sealed row unopenable, reported as nothing but a failed authentication tag.

So the app model states the key the way it states the password. `OrchestrationContract.DataEncryptionKeyMaterial` is base64 of the ASCII text `mailfathom-development-only-key!`, handed to the host as a `plaintext:` reference under the key identifier `development`, and it cannot diverge from itself. Resetting the local database is what re-seals it, which is the same act that already recreates the schema. Generation into user secrets was the other candidate and is refused: it is precisely what the password deliberately does not do, and it would reintroduce the divergence this constant exists to remove. A value published in a public repository is not a secret and is not treated as one — it protects one developer's synthetic mail on a container published on the loopback address alone, and a deployment resolves its key from a provisioned reference that this app model builds no part of.

### Consequences

- Good, because a database copy — a dump, a replica, a restored volume, a stolen disk — discloses no refresh token, which is the only threat any of the four options was asked to answer.
- Good, because provisioning is one command in one shape, and an operator learns it once for all three channels rather than once per channel.
- Good, because the key ring is general: a second sealed column configures nothing, migrates nothing, and rotates through the same `ActiveKeyId` move.
- Good, because rotation needs no downtime and no flag day, and a half-rotated deployment is a valid state rather than an outage.
- Neutral, because the key protects the database against a copy and not against the running host. Anyone able to read the process's resolved configuration can read the key, and no scheme that starts unattended can prevent that.
- Neutral, because MailFathom joins the set of systems whose backup is incomplete without a second artifact. That is true of every encrypted store and it is a documentation obligation rather than a design flaw.
- Bad, because losing the key loses the sealed data. Today that means re-authorizing every mailbox; once a second column is sealed it will mean more, and the cost of the mistake grows without the mechanism changing.
- Bad, because the operator has one more step at install. It is one line between two identical lines they already run, and it is the price of the two `Good` entries above.
- Bad, because local development seals under a key this repository publishes, so a developer's local database is protected against nothing. That is the intended trade — it holds synthetic mail on a loopback-published container — and it is a trade only because the constant is unmistakably a development one rather than a weak secret somebody might reuse.

## Validation

- Startup validation refuses, naming the setting: an `ActiveKeyId` that names no configured key, material that does not decode to exactly 32 bytes, a duplicate `KeyId`, and a key identifier the database could not hold. Unit tests cover each refusal.
- An empty ring is not one of those refusals, and cannot be: whether a sealed value exists is a question about the data rather than about the configuration, and startup validation reads no rows. A deployment that stored a token and then lost its ring fails at the next token request, naming the key the stored value asks for — which is the same failure a retired key produces and is reported the same way.
- Unit tests cover the associated-data binding by proving a value sealed for one subject or one purpose does not open as another, and cover active-key selection and re-sealing.
- The integration suite covers the EF mapping, the migration, and the round trip through PostgreSQL.
- `helm lint` and `helm template` against `deploy/helm/mailfathom/ci/*-values.yaml` prove the chart creates no `Secret` of its own; a template that did would be a review failure against this record.
- `docs/operations/secret-provisioning.md` holds the generating command once, and the other pages point at it. A second spelling of the command anywhere is a documentation defect.

## Pros and Cons of the Options

### 1. Application-level AES-256-GCM under a configured key ring

Sealing happens in `Infrastructure` before the value reaches the provider, using `AesGcmEnvelope`, which already exists, is already tested, and already holds no key of its own.

- Good, because the ciphertext is what PostgreSQL ever sees, so every copy of the database is covered by the same guarantee.
- Good, because it reuses a component the repository already ships and trusts, rather than adding a second cryptographic implementation with its own format.
- Good, because associated data gives the binding that makes one key ring safe across several columns.
- Neutral, because a sealed column cannot be searched, ordered, or indexed by value. Nothing needs to search a refresh token, and a future column that does would need its own decision.
- Bad, because MailFathom owns the format. A layout change would have to open values written by an older build, which is why the envelope's layout is fixed rather than configurable.

### 2. PostgreSQL `pgcrypto`

- Good, because it needs no application code and the column is encrypted at rest.
- Bad, because the key travels to the database in the statement text, so it lands in `pg_stat_statements`, in server logs at a sufficient `log_statement`, and in the memory of the process holding the data. The key and the ciphertext end up in the same trust boundary, which is the boundary this decision exists to separate.
- Bad, because key rotation becomes a SQL migration over rows rather than a configuration move.

### 3. Volume or full-disk encryption alone

- Good, because it costs nothing to implement and every deployment target offers it.
- Neutral, because it remains worth doing underneath any of the other options.
- Bad, because it protects a powered-off disk and nothing else. A dump, a replica, a backup file, and a restored volume are all readable, and those are the copies a credential actually leaks through.

### 4. An external key-management service per operation

- Good, because the key never exists in MailFathom's process and rotation is the service's problem.
- Bad, because it puts a network dependency in the path of a mailbox token refresh, so one provider's outage becomes two.
- Bad, because it replaces one credential to provision with another — the KMS credential — and does not remove the first-secret problem, only moves it.
- Neutral, because a future adapter can supply the key ring's material from such a service without changing anything decided here. The ring resolves a `SecretReference`, and a scheme is where that would land.

### A. The operator generates the key at install

- Good, because it is the same act, in the same place, as the two database passwords the operator already generates, and the Compose `.env.example` already establishes the precedent.
- Good, because nothing can regenerate what nothing generates.
- Good, because the operator **knows the key exists**, which is what makes them back it up with the database. No automatic scheme can give this back.
- Bad, because it is a manual step, and an install that fails because it was skipped is a worse first experience than one that starts.
- Neutral, because the failure is immediate, named, and at startup rather than at the first token request, which is what keeps the skipped step cheap.

### B. The host generates on first start into a durable state directory

- Good, because Compose and a systemd unit both have exactly one writer and a place that persists, so it would be safe in those two channels.
- Bad, because Kubernetes has neither, and the answers are a read-write-once volume that forbids a second replica, or an init container writing `Secret`s with a permission the chart has no other reason to hold.
- Bad, because the container is deliberately `read_only` with only a `tmpfs`, so this buys the one line back by adding a writable mount to a hardened deployment.
- Bad, because a key nobody chose is a key nobody backs up. The failure surfaces when a database is restored somewhere else, which is the worst moment to learn the design.

### C. Per-channel install tooling

- Good, because the operator does nothing in any of the three channels.
- Bad, because the MailFathom image is chiseled and has no shell, so a Compose init service needs a second image and the Helm hook needs a third.
- Bad, because Argo CD maps `pre-install` and `pre-upgrade` alike onto `PreSync`, so the hook that was meant to run once runs on every sync and its own idempotence becomes the only thing standing between an upgrade and unopenable data.
- Bad, because it is three mechanisms in YAML and shell where option A is one sentence, and none of them is reachable by the test suite.

### D. `mfctl`

- Good, as a local generator, because it would produce exactly 32 bytes in exactly the right encoding, where `openssl rand -base64 33` copied from the line above it would not.
- Neutral, because that variant is option A with a different command, so it changes the ergonomics and not the decision.
- Bad, as an installer, because preparing a deployment directory is a deployment script, and issue 119 removed those deliberately; it also has nothing to prepare under Kubernetes, where Helm installs.
- Bad, through the administrative endpoint, because the service would need somewhere to persist the key before it has one, and the only place it always has is the database — which would put the key protecting the database inside the database.

## More Information

- Issue 329 implements the ring and the first sealed column; issue 330 provisions the key through each channel; issue 331 writes a grant through the administrative endpoint.
- ADR 0001 governs the port the sealed store is reached through, and ADR 0002 governs where the key ring is bound and mapped.
- `AesGcmEnvelope` in `backend/src/Common/` is shared with `mfctl`'s own credential store. That store keeps a per-machine key beside its file, which is right for one workstation and is exactly the scheme this decision rejects for a deployment, because a per-machine key gives a replica set as many keys as replicas.
- Revisit this decision if a sealed column ever has to be searched or joined by value, if a deployment target appears where an operator cannot provision a secret before the first start, or if MailFathom gains a first-class key-management adapter — the third would extend the ring's material resolution rather than replace anything recorded here.
