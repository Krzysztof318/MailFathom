# Mail Transport Security Policy

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 2
**Depends on:** nothing
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Replace the current boolean `UseSslOnConnect` switch with the connection-security and authentication policy described in draft sections 7.1 and 7.2, so an operator cannot silently configure a mailbox that sends credentials in clear text over an unencrypted channel.

## Current state

`MailKitImapAccountSettings` carries a single `UseSslOnConnect` flag, and `MailSynchronizationAccountOptions` exposes host, port, user name, and password. There is no TLS mode selection, no explicit opt-in for insecure transport, and no SASL mechanism allow-list. MailKit therefore picks authentication mechanisms on its own.

## Approved scope

`Domain` gains a provider-neutral `MailConnectionSecurity` value with the five draft modes (`Auto`, `TlsOnConnect`, `StartTlsRequired`, `StartTlsWhenAvailable`, `None`) and a `MailAuthenticationPolicy` value carrying an ordered allow-list of permitted SASL mechanism names plus the two explicit opt-in flags for insecure transport and for clear-text authentication over an unencrypted channel. The domain owns the rule that rejects a clear-text mechanism on an unencrypted channel unless both opt-ins are present; that rule is pure policy with no I/O, so it belongs in `Domain` rather than in options validation.

`Application` extends the mailbox session port inputs with the resolved connection policy. `Infrastructure` maps the domain policy onto MailKit's `SecureSocketOptions` and its authentication mechanism set, and removes mechanisms the policy does not permit from the client's advertised mechanism collection before authenticating. Certificate validation stays enabled unconditionally; there is no configuration path that disables it.

Private servers are supported through explicit trusted certificate authority configuration. This specification defines the configuration shape — the policy carries a reference to trust anchor material — and validates that the reference is present when required. Loading that material and installing it into the certificate validation path is assigned to specification 02b, which builds on the reference-resolution mechanism specification 02a delivers. Specification 02a also renames this setting from `TrustedCertificateAuthorityReference` to `TrustedCertificateAuthority` and changes it from a bare string to a secret block.

`Host` binds the new options, validates them with `ValidateOnStart`, and fails startup with a specific message naming the offending account when a policy is unsafe.

## Safety and privacy

The rejection rule is enforced in the domain object that owns it and again at options validation, so a future entry point such as the planned `mcpmail` CLI cannot bypass it. Validation failures name the account identifier and the violated rule; they never include the user name, password, or secret reference value. The MailKit adapter must not fall back to a broader mechanism set when the permitted set fails to authenticate.

## Testing

`Domain.UnitTests` cover each connection-security mode, the clear-text-over-unencrypted rejection, both opt-in combinations, and mechanism-list normalization. `Infrastructure.UnitTests` verify the mapping from every domain mode to the expected `SecureSocketOptions` value and prove that the adapter restricts the mechanism set before calling authenticate, using the existing narrow IMAP client port rather than a substituted concrete MailKit client. Options validation tests assert fail-fast behavior for each unsafe configuration shape.

## Out of scope

Secret reference resolution, which specification 02a owns, and trusted certificate authority loading, which specification 02b owns; SMTP transport policy; and OAuth-based mailbox authentication mechanisms. GSSAPI/Kerberos remains unsupported per draft section 7.2.

## Definition of done

- `UseSslOnConnect` no longer exists anywhere in the repository.
- An account configured with `None` and no `AllowInsecureConnection` fails startup.
- An account configured for a clear-text mechanism on an unencrypted channel fails startup unless both opt-ins are set.
- `docs/features/imap-synchronization.md` documents the modes, the allow-list, and the opt-in semantics.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
