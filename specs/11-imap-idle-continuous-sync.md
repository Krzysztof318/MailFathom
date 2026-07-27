# IMAP IDLE Continuous Synchronization

**Roadmap group:** C — continuous synchronization
**Draft delivery stage:** 3
**Depends on:** 04, 09
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Replace polling with push-style synchronization for the inbox where the server supports IDLE, as draft section 11.3 specifies, while keeping bounded polling as the configured fallback and as an explicitly selectable mode.

## Current state

Every folder is reconciled on a fixed interval. There is no capability detection, no long-lived connection, and no way for an operator to choose push over polling.

## Approved scope

Each account supervisor from specification 09 chooses a synchronization mode per folder from account policy and server capability: push where the account requests it and the server advertises IDLE, otherwise bounded polling. The choice is decided once per session and re-decided on reconnect, and the effective mode is observable, because an operator who configured push needs to know when the server silently forced a fallback.

A push-mode folder holds a dedicated session that enters IDLE, renews before the conservative timeout the draft calls for, exits on notification, runs one ordinary synchronization pass through the existing synchronizer, and re-enters IDLE. The synchronization pass is the same code path as polling; IDLE only changes what triggers it, which keeps a single implementation of the correctness-critical logic.

Loss of the IDLE connection is a transient failure handled by the session pipeline from specification 04, and repeated failures degrade the folder to polling for a bounded period before push is retried. Degradation is recorded, not silent.

## Safety and privacy

The IDLE session opens the folder read-only exactly as the polling path does, and the notification handler performs no fetch of its own; it only signals that a synchronization pass should run. This keeps the `\Seen` invariant proven by the existing synchronizer tests rather than duplicated into a second fetch path, and the specification requires a test asserting the push path performs no direct content fetch.

A long-lived connection holds credentials in memory for the process lifetime; the specification requires that the session obtains its secret through the resolver from specification 02a at connect time and does not retain a copy beyond the session object.

## Testing

`Infrastructure.UnitTests` drive the narrow IMAP client port with NSubstitute to model: a server advertising IDLE, a server not advertising it, renewal firing before the timeout under `FakeTimeProvider`, a notification triggering exactly one synchronization pass, a dropped connection reconnecting, repeated failures degrading to polling and later retrying push, and cancellation exiting IDLE promptly during shutdown. Tests assert the effective mode is reported when it differs from the configured mode.

## Out of scope

NOTIFY for multiple folders and CONDSTORE/QRESYNC, which specification 12 owns.

## Definition of done

- An account configured for push uses IDLE on its inbox when the server supports it and polls when it does not, with the effective mode observable.
- A notification triggers one synchronization pass through the existing synchronizer, not a separate fetch path.
- Repeated IDLE failures degrade to bounded polling and later retry push, and the degradation is recorded.
- `docs/features/imap-synchronization.md` documents mode selection, renewal, degradation, and observability.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
