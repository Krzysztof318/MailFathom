# IMAP Session Resilience

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 3
**Depends on:** 01, 03
**Estimated change size:** ~500 lines including tests and documentation

## Goal

Apply the resilience pipelines from specification 03 to the MailKit IMAP adapter so a flapping mail server produces bounded, observable retry behavior instead of an unhandled exception that aborts a synchronization run.

## Current state

`MailKitImapMailboxSession` and its factory connect, authenticate, select a folder, and fetch without any timeout or retry. `MailSynchronizationWorker` catches per-folder failures and moves on, which isolates damage but discards a run entirely on a single transient disconnect.

## Approved scope

Session establishment — connect, TLS negotiation, and authenticate — executes under the mailbox-session pipeline. Data retrieval — UID enumeration, metadata batches, and content fetch — executes under the mailbox-retrieval pipeline. The circuit breaker is keyed per account so one unreachable server cannot open the breaker for a healthy account.

Failure classification is explicit at this boundary. Authentication rejection, unsupported capability, and policy violation from specification 01 are terminal and surface immediately. Socket failures, server-initiated disconnects, and protocol timeouts are transient. Caller cancellation is distinguished from a pipeline timeout so the worker can tell shutdown apart from a slow server, and the two produce different application failures.

A retried operation re-establishes the session when the previous attempt lost the connection, and the folder is always reopened read-only, so a retry can never reach the server through a read-write path.

## Safety and privacy

The read-only invariant survives retry. Because a retried attempt reopens the folder, the specification requires a test proving that every reopen after a failure uses read-only access and that the retried content fetch still uses the seen-preserving fetch operation. This is the case draft section 11.1 makes non-negotiable, and it is exactly the path most likely to regress when retry is introduced. Retry logs record the account identifier, folder name, dependency class, and attempt number, never credentials or message content.

## Testing

`Infrastructure.UnitTests` drive the narrow IMAP client port with NSubstitute to model a disconnect mid-batch, a transient connect failure followed by success, an authentication rejection, and a server that never responds. `FakeTimeProvider` keeps the tests instant. Assertions cover attempt counts, terminal-versus-transient classification, per-account breaker isolation, cancellation versus timeout distinction, and the read-only reopen invariant.

## Out of scope

The supervisor-level scheduling and backoff between whole synchronization runs, which specification 09 owns. IDLE reconnection, which specification 11 owns.

## Definition of done

- No hand-written retry loop remains in the MailKit adapter.
- A transient disconnect during a metadata batch is retried and the run completes; an authentication failure is not retried.
- A test proves a retried content fetch preserves the remote `\Seen` flag path.
- `docs/features/imap-synchronization.md` documents the applied pipelines and the failure classification.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
