# Remote Expunge and Flag Reconciliation

**Roadmap group:** C — continuous synchronization
**Draft delivery stage:** 3
**Depends on:** 07, 09
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Detect messages that disappeared or changed flags on the server and reflect that locally, and implement the retention grace period and bounded garbage collection that draft section 10 requires for expunged mail.

## Current state

Synchronization only moves forward: it fetches messages after the checkpoint UID and stores them. A message deleted on the server stays in local storage forever, and a message whose `\Seen` flag changed after it was first stored keeps a stale snapshot.

## Approved scope

Reconciliation adds a bounded backward pass alongside the existing forward pass. For a bounded window of already-stored occurrences, the session port reports which UIDs still exist and their current flags. Missing UIDs are marked remotely deleted with a deletion observation timestamp; present UIDs have their flag snapshot refreshed. The window is bounded per run and advances across runs so a large mailbox is reconciled over time without an unbounded scan.

Marking deleted is a local state change only. The raw MIME of a remotely deleted message is retained for a configurable grace period, after which a bounded garbage collection pass removes the content row while the metadata row remains as a tombstone. Metadata tombstone retention is itself a separate configurable period, because the two answer different questions: how long the local mail copy survives remote deletion, and how long the record that it existed survives.

Garbage collection deletes derived data together with the content it derives from — extracted text and the `tsvector` from specification 08, and every later derived artifact — so the deletion path required by draft section 16.3 exists from the moment derived data exists.

## Safety and privacy

Every operation here is read-only against the server. The session port gains no flag-writing and no expunge operation, and the specification requires a test proving the reconciliation pass uses only read-only folder access and cannot mark a message as read while inspecting it. This is the highest-risk path in the roadmap for the `\Seen` invariant, because inspecting existing messages is exactly what a careless implementation does with a fetch that sets the flag.

Retention defaults are conservative in the privacy direction: content is removed after the grace period by default rather than retained indefinitely, satisfying the storage-limitation requirement in draft section 16.2. Deletion and garbage-collection actions emit redacted audit events carrying account, folder alias, counts, and outcome.

## Testing

Unit tests cover: a missing UID being marked deleted, a flag change refreshing the snapshot, window bounding and advance across runs, grace-period expiry driving content removal with `FakeTimeProvider`, derived data being removed with its content, tombstone retention outliving content retention, idempotent re-runs, and the read-only invariant on the reconciliation fetch. A UIDVALIDITY change during reconciliation must fall back to the existing invalidation rule rather than mass-deleting local messages, and that case is tested explicitly.

## Out of scope

Restoring a message that reappears on the server after tombstoning, backup-side deletion replay, and legal holds, all of which draft section 16.3 defers.

## Definition of done

- A message deleted on the server is marked deleted locally within one reconciliation cycle of its window.
- Content is removed after the grace period together with all derived data; the tombstone survives per its own setting.
- A UIDVALIDITY change never causes mass local deletion.
- `docs/features/imap-synchronization.md` documents reconciliation windows, retention settings, and the audit events.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
