# Secret Reference Resolution

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 2
**Depends on:** 01
**Estimated change size:** ~500 lines including tests and documentation

## Goal

Implement the secret handling model from draft section 7.3 so that no mailbox password, database password, or certificate password is ever written into `appsettings.json`, and so that an unresolved secret reference fails startup instead of producing a confusing authentication error later.

## Current state

`MailSynchronizationAccountOptions` holds a plain `Password` string bound directly from configuration. The default `appsettings.json` ships an empty account list, so nothing is leaked today, but the shape invites operators to commit credentials.

## Approved scope

Configuration values that carry secrets become secret references rather than secret values. A reference is a string with an explicit scheme:

- `systemd-credential:<name>` reads from the runtime credentials directory that systemd exposes to the service.
- `file:<path>` reads a deployment-provisioned protected file.
- `env:<variable>` reads an environment variable, permitted for non-production automation.
- `plaintext:<value>` exists only for local development and is rejected outside the Development environment.

`Application` owns a narrow `ISecretResolver` port returning a resolved secret or an explicit resolution failure; it returns a result type rather than throwing, because an unresolved reference is an expected configuration failure. `Infrastructure` implements one adapter per scheme behind a composite resolver that dispatches on the scheme. `Host` resolves every configured reference once during startup validation and fails fast, listing which account and which logical secret could not be resolved.

Resolved secret material is held in memory only for as long as the owning options instance lives, and the resolver never caches values across configuration reloads. ADR 0002 governs the configuration reading and reload boundary; this specification stays inside it and does not introduce a new reload mechanism.

## Safety and privacy

A resolution failure message names the account identifier, the logical secret name, and the scheme, and never the reference target path, the environment variable value, or any part of the resolved secret. Resolved secrets are excluded from structured logging by construction: the options type exposes them through a dedicated accessor rather than an ordinary public property, so a future serializer or diagnostic dump cannot pick them up incidentally. `plaintext:` outside Development is a startup failure, not a warning.

## Testing

`Infrastructure.UnitTests` cover each scheme adapter against an in-memory abstraction over the credential directory and file system, since unit tests must not touch the real file system. Tests assert the unknown-scheme failure, the missing-reference failure, the Development-only `plaintext:` rule, and that failure results carry no secret material. `Application.UnitTests` cover the composite dispatch and result mapping.

## Out of scope

Data Protection key-ring provisioning, encrypted secret storage in PostgreSQL, external secret-provider integrations such as a cloud vault, and secret rotation without restart.

## Definition of done

- No options type in the repository exposes a raw password bound directly from configuration.
- A missing or malformed reference fails startup with a message that identifies the account without disclosing the secret.
- `docs/operations/local-development.md` documents the Development workflow and `docs/operations/` gains a page describing the systemd credential deployment path.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
