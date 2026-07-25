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

The resolver is not an application-facing capability. ADR 0002 permits the configuration layer to reference secret identifiers or consume already-bound secret values at the host boundary, and explicitly forbids normalizing broad secret access into application code — an `ISecretResolver` visible to `Application` would give every use case the ability to ask for any secret by name, which is exactly that. The resolver contract and its per-scheme adapters therefore live in `Infrastructure`, and `Host` invokes them once during startup validation. Application and domain code receive only the resolved, narrowly scoped settings each operation needs, and cannot ask for anything else.

Resolution returns a result rather than throwing, because an unresolved reference is an expected configuration failure. `Host` fails fast on the first unresolved reference and lists which account and which logical secret could not be resolved.

Resolved secret material is held in memory only for as long as the owning options instance lives, and the resolver never caches values across configuration reloads. ADR 0002 governs the configuration reading and reload boundary; this specification stays inside it and does not introduce a new reload mechanism.

## Trusted certificate authority material

Specification 01 requires that certificate validation is never disabled and that private or self-signed servers are supported by configuring additional trusted certificate authorities. Nothing else in the roadmap owns loading that material, and it arrives through exactly the mechanism this specification builds — a deployment-provisioned file or credential — so it is assigned here.

A trusted certificate authority is configured as a reference in the same form as any other deployment-provisioned material. `Infrastructure` loads it, validates that it parses as a certificate and is usable as a trust anchor, and supplies it to the MailKit adapter's certificate validation path so a private server chains to it while ordinary validation stays enabled for everything else. A malformed or unreadable trust anchor fails startup, since silently continuing would either reject a working server or, worse, invite an operator to disable validation to work around it.

## Safety and privacy

A resolution failure message names the account identifier, the logical secret name, and the scheme, and never the reference target path, the environment variable value, or any part of the resolved secret. Resolved secrets are excluded from structured logging by construction: the options type exposes them through a dedicated accessor rather than an ordinary public property, so a future serializer or diagnostic dump cannot pick them up incidentally. `plaintext:` outside Development is a startup failure, not a warning.

## Testing

`Infrastructure.UnitTests` cover each scheme adapter against an in-memory abstraction over the credential directory and file system, since unit tests must not touch the real file system. Tests assert the unknown-scheme failure, the missing-reference failure, the Development-only `plaintext:` rule, the composite dispatch, and that failure results carry no secret material. Trust-anchor tests cover a valid certificate being installed into the validation path, a malformed one failing startup, and the absence of any configuration path that disables validation. An architecture test asserts that no secret-resolution type is reachable from `Application` or `Domain`.

## Out of scope

Data Protection key-ring provisioning, encrypted secret storage in PostgreSQL, external secret-provider integrations such as a cloud vault, and secret rotation without restart. Client certificates presented by MCP clients are stage 9 work and unrelated to mail transport trust anchors.

## Definition of done

- No options type in the repository exposes a raw password bound directly from configuration.
- A missing or malformed reference fails startup with a message that identifies the account without disclosing the secret.
- No secret-resolution contract is reachable from `Application` or `Domain`.
- A private server with a configured trust anchor connects with certificate validation fully enabled.
- `docs/operations/local-development.md` documents the Development workflow and `docs/operations/` gains a page describing the systemd credential deployment path.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
