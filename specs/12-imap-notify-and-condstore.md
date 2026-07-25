# IMAP NOTIFY and CONDSTORE

**Roadmap group:** C — continuous synchronization
**Draft delivery stage:** 3
**Depends on:** 10, 11
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Extend push-style synchronization beyond the inbox using NOTIFY where the server supports it, and use CONDSTORE and QRESYNC modification sequences to make reconciliation cheaper, as draft section 11.3 specifies.

## Current state

After specification 11, push covers the inbox through one IDLE session per account. Other folders still poll on a schedule, and every reconciliation pass re-inspects its window without knowing whether anything changed.

## Approved scope

When the server advertises NOTIFY, one session per account subscribes to change events for the account's configured folders and dispatches a synchronization pass for the folder that changed. Where NOTIFY is unavailable, non-inbox folders keep the scheduled reconciliation from specification 09, per account policy. The number of folders in one subscription is bounded, and folders beyond the bound fall back to scheduling rather than being dropped.

Where the server advertises CONDSTORE, the synchronization checkpoint gains the folder's highest modification sequence, and the backward reconciliation pass from specification 10 uses it to ask only for occurrences changed since that value instead of re-inspecting the whole window. Where QRESYNC is advertised, the vanished-message report replaces the window scan for detecting expunged messages. Both are optimizations layered on top of specification 10's semantics: when neither is available, behavior is unchanged, and the specification requires tests proving the two paths reach the same end state.

Extending the checkpoint changes persisted synchronization state, so the checkpoint must tolerate reading rows written before this change without a migration, since migrations are deferred to specification 19.

## Safety and privacy

NOTIFY subscription and CONDSTORE fetches are read-only, and the specification carries forward the requirement that no path introduced here can set a message flag. The vanished-message handling feeds specification 10's deletion path, which already emits redacted audit events, so no new event shape is introduced. Notification payloads are treated as untrusted server input and are validated before dispatching work.

## Testing

`Infrastructure.UnitTests` model: a server advertising NOTIFY for multiple folders, a server advertising neither NOTIFY nor CONDSTORE, subscription bounds with overflow folders falling back to scheduling, a modification-sequence-limited reconciliation matching the full-window result, a QRESYNC vanished report producing the same deletions as the window scan, a UIDVALIDITY change invalidating the stored modification sequence, and a checkpoint row written before this change being read without error.

## Out of scope

Server-side search, mailbox quota reporting, and any IMAP extension not needed for read-only synchronization.

## Definition of done

- Non-inbox folders receive push notifications where the server supports NOTIFY, and fall back to scheduling otherwise.
- CONDSTORE and QRESYNC reduce reconciliation work while producing the same end state as the fallback path.
- A checkpoint written before this change is readable without migration.
- `docs/features/imap-synchronization.md` documents capability negotiation and the fallback matrix.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
