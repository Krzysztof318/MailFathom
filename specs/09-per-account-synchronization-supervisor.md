# Per-Account Synchronization Supervisor

**Roadmap group:** C — continuous synchronization
**Draft delivery stage:** 3
**Depends on:** 03, 04, 05
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Give each account its own supervised synchronization lifecycle, as draft section 11.3 requires, so a failing account cannot delay or stop synchronization of any other account, and so specifications 11 and 12 have a place to host long-lived IDLE and NOTIFY connections.

## Current state

`MailSynchronizationWorker` is a single hosted service that iterates every configured account and folder on one timer, sequentially, catching failures per folder. One slow server therefore delays every other account until its folders finish, and there is nowhere for a long-lived push connection to live.

## Approved scope

The single worker becomes a coordinator that starts one supervisor per configured account and supervises those supervisors. Each account supervisor owns its own schedule, its own failure state, and its own scope per work unit, honoring the existing rule that a background service creates an explicit scope per independent work unit and never captures scoped services.

Concurrency is bounded in two dimensions and both bounds are configurable and validated: how many accounts synchronize at once, and how many folders within one account synchronize at once. The default for folders within an account is one, because a single IMAP connection per account is the conservative and server-friendly choice and nothing in the current design needs more.

Between runs, a supervisor applies the account-level backoff pipeline from specification 03 when the previous run failed, and returns to the configured interval after a successful run. Run-level backoff is distinct from the operation-level retry inside the adapter, and the single-layer rule from specification 03 applies: the supervisor does not retry an operation the adapter already retried, it decides only when to attempt the next whole run.

Host shutdown cancels every supervisor cooperatively and waits, bounded, for in-flight work units to finish so a run cannot be torn down between persisting content and advancing the checkpoint.

## Safety and privacy

Per-account isolation is a privacy property as well as an availability one: a failure in one account's supervisor must not surface another account's identifiers in its logs or telemetry. Supervisor logs carry the account identifier and folder alias only. Metrics record run duration, stored and skipped counts, consecutive failure count, and current backoff, with no message-level data.

## Testing

`Application.UnitTests` and host-level unit tests with `FakeTimeProvider` cover: a failing account not delaying a healthy one, both concurrency bounds being respected, backoff applying after failure and resetting after success, cancellation completing an in-flight work unit's commit before shutdown, and a configuration with zero accounts starting cleanly when synchronization is disabled.

## Out of scope

Push-style synchronization, which specifications 11 and 12 own. Durable work leases across process restarts, which the draft defers to later operational hardening.

## Definition of done

- Each configured account runs on its own supervised schedule with isolated failure state.
- Both concurrency bounds are configurable, validated at startup, and enforced.
- Shutdown does not interrupt a work unit between content persistence and checkpoint advance.
- `docs/features/imap-synchronization.md` documents the supervisor model, bounds, and backoff layering.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
