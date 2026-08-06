---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-06
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Write to the remote mailbox through a session type no read path can obtain, and scope the never-marks-read guarantee to retrieval

<!-- describes: src/Application/Mail/Mutations/**, src/Domain/Mutations/**, src/Infrastructure/Mail/MailKit/Writes/**, src/Infrastructure/Observability/** -->

## Context and Problem Statement

MailFathom has never written to a mailbox. Folders are opened with `FolderAccess.ReadOnly`, bodies are fetched with PEEK semantics, and no code path calls a flag-writing method — and until now the guarantee that mail is never marked read rested on there being no writing path at all. That was the first-release posture rather than the product's destination: draft section 21.5 already sorts remote mailbox mutation into action tiers, and issue 251 asks for rules whose value is mostly on the other side of the line, because a rule that can match a message but not file it is a notification rather than automation.

Issue 446 asked which mutations MailFathom may perform, in what order they become available, and which are refused by construction rather than by configuration. It was answered and closed; this record is that answer, together with the two decisions the answer forced and that only the first implementation could settle — how the read path is kept structurally unable to write, and what the separation costs in connections to a mail server that counts them.

Recorded on issue 447, which is the gate for feature 452. No numbered specification under `specs/` backs it; draft section 11.1 states the invariant this narrows and is amended in the same change set.

## Decision Drivers

- **The guarantee has to survive every later change, not just this one.** A rule saying reconciliation must not write flags is a rule somebody has to notice during review. A refactor cannot accidentally give reconciliation the ability to write if reconciliation never holds a type that has it.
- **The current invariant conflates a path with an act.** A read — synchronization, content retrieval, any MCP tool — must remain incapable of writing. An act the mailbox owner authored is a different thing, and `\Seen` is where the distinction bites: today it is a snapshot of remote state with exactly one writer, and a rule that marks mail read would make it a value with two.
- **A mail tool may not have side effects nobody asked for.** RFC 3501's bare `EXPUNGE` removes every message in the folder that anyone has flagged `\Deleted`, including messages another client flagged and MailFathom has never seen.
- **Which server an operator uses must not be a feature difference.** RFC 6851 `MOVE` is an extension, and the servers without it are ordinary rather than exotic.
- **A connection is a resource the mail server counts, not a free abstraction.** An account already holds one long-lived connection for `NOTIFY` and IDLE plus `MaxConcurrentFoldersPerAccount` synchronization connections. A provider limit or Dovecot's `mail_max_userip_connections` is reached as a refused login, which surfaces as synchronization failing rather than as the write that caused it.
- **An operator reads an operation, not a command sequence.** What a support question costs is set by whether the record names what was asked for.
- **Irreversible acts driven by attacker-influenced input are the thing to refuse.** Mail content, tool arguments, and model output are untrusted, and a mutation surface wide enough to send is a surface wide enough to be steered into sending.

## Considered Options

The decision has four independent axes. An option on one constrains no option on another, which is why they are listed apart rather than as four bundled proposals.

**A — which mutations are permitted:**

1. Stay read-only.
2. Moves and copies only.
3. Moves, copies, and `\Seen`, with delete.
4. Everything, including sending.

**B — how a read path is kept unable to write:**

1. A separate session type, reached through a separate factory, with the folder access fixed when a connection is created.
2. One session type with a write mode, and a review rule that reads do not use it.
3. One session type, with the mail server's read-only selection left to refuse the command.

**C — how a relocation is carried on a server without `MOVE`:**

1. `COPY`, then `STORE +FLAGS \Deleted`, then RFC 4315 `UID EXPUNGE`; refuse where `UID EXPUNGE` is unavailable.
2. The same three commands, falling back to a bare `EXPUNGE` where `UID EXPUNGE` is unavailable.
3. Delegate to MailKit's own `MoveTo`, which implements option C2 internally.

**D — how many connections the separation costs:**

1. One write connection per account, opened on demand and kept for a bounded idle period.
2. One write connection per mutation.
3. No new connection: take the notification session out of IDLE to carry a write.

## Decision Outcome

Chosen options: **A3**, **B1**, **C1**, and **D1**.

### MailFathom writes, and exactly four mutations exist

Permitted: **relocate** a message between folders, **delete** one on the server, **set `\Seen`** in both directions, and **copy** one into a second folder. All four are in scope for feature 452; the copy carries a schema question of its own — whether two live occurrences of one message are one local row or two — which is why the action that decides to copy is a separate issue from the session that can.

Refused, and not by configuration:

- **Sending anything.** Send, reply, forward, or any other message the owner did not write themselves. The SMTP outbox owns that surface and its own authorization review, and nothing under feature 452 acquires it.
- **Every flag other than `\Seen`** and the `\Deleted` that is part of a delete or a fallback move. `\Flagged`, `\Answered`, `\Draft`, and keywords stay unwritten.
- **Folder management**: creating, renaming, deleting, or subscribing to a folder.

Naming the refused tiers is part of the decision rather than a note beside it. A later request for one of them reopens this record; it does not read a gap as permission.

### The read path is incapable of writing, as a property of the types

`IMailboxWriteSession` is a different type from `IMailboxSession`, reached through a different factory, and it exposes exactly the four mutations above — no method that sends, none that manages a folder, and none that writes another flag. Synchronization, reconciliation, content retrieval, and every MCP tool depend on the read factory, so none of them can obtain a session that writes whatever a later change does inside them.

Below the port the same separation is fixed rather than repeated. How a connection selects its folder is decided when the connection is created and can never change afterwards: the read paths create theirs through `MailKitImapConnection.ForReading`, which selects `ReadOnly` on the first establishment and on every reconnection after a dropped socket, and which refuses a mutation outright rather than sending a command the server would reject. A read connection cannot acquire the ability to write by losing its socket.

### What stays true, and what changes

**Root `AGENTS.md` is unchanged, deliberately.** Its sentence — *synchronization and content retrieval must never set the remote `\Seen` flag* — already scopes the guarantee to retrieval, and it is exactly as true after this decision as before it. Saying so here is part of the deliverable, so a later reader does not correct a sentence that is still right.

**Draft section 11.1 changes**, because its bullets state the invariant unscoped. *No code path calls `AddFlags`, `SetFlags`, or equivalent methods* stops being true of the write session. *Stored `\Seen` is a snapshot of remote state only* stays true and becomes load-bearing: an authored `\Seen` change is a request MailFathom makes of the server, and the stored value still has exactly one writer, which is synchronization observing what the server reports back.

### One operation, one name

Whichever path carries a relocation, it is a relocation everywhere an operator reads: the same log message, the same span name, and the same counter dimension. Which path ran, and the individual `UID COPY`, `UID STORE +FLAGS (\Deleted)`, and `UID EXPUNGE` steps of the fallback, are recorded at `Debug` and nowhere else, and no metric dimension distinguishes the two at any level. A failure keeps the same identity — a relocation that failed, with the step it failed at in the debug detail.

The alternative is worse than untidy. A fallback that announces a copy and a delete turns a missing server extension into something a person has to interpret, and the shape that mistake takes in practice is a support question about mail that was copied and deleted instead of moved, asked about an operation that did exactly what was asked of it. The debug record still has to be complete, because a genuinely broken fallback is diagnosed from which of the three commands was reached.

### A bare `EXPUNGE` is never issued

Where RFC 4315 `UID EXPUNGE` is unavailable, a delete and a fallback relocation are **refused** and report `MailboxMutationUnsupported` (25001). The relocation is refused before the copy rather than after it, so a refusal leaves no duplicate behind in the destination folder. `COPYUID` is read where UIDPLUS is available and its absence is reported as itself, because searching the destination folder afterwards would replace a fact the server gave with a guess.

### The write connection is one per account, kept while it is warm

An account holds at most one connection able to change its mailbox. It is opened the first time something asks to write, kept for `MailSynchronization:WriteConnectionIdlePeriod` after the last session using it was disposed, re-established like any other connection, and closed when that period elapses. A second caller for the same account waits for the first, so a burst of changes can never turn into a burst of logins.

It counts against the same per-account budget as the `MaxConcurrentFoldersPerAccount` synchronization connections and the push session, and the total is bounded by configuration and by construction rather than by how many mutations happen to be in flight. No read path can open, borrow, or reach it, and the notification session is never taken out of IDLE to carry a write: leaving IDLE would mean dropping the subscription and resubscribing with a `NOTIFY SET` that replaces the server's default again, which is a worse trade than one more connection.

Capability detection, timeouts, cancellation, and failure classification follow the existing IMAP session conventions and the account's resilience pipelines. The account's existing circuit therefore gates writes as well as reads, and that is deliberate: a mail server that is failing reads is not a server to start changing mail on, and the two share an establishment budget because they share the credential that a repeated rejection would lock out. What writes do **not** share is repetition — a mutation is issued exactly once, because a `COPY` issued twice is a second message rather than a repeat of the first.

### Consequences

- Good, because the never-marks-mail-read guarantee stops depending on a reviewer noticing and starts depending on which type a component holds.
- Good, because a server without RFC 6851 behaves identically to one with it from every layer above the session, and an operator's choice of provider is not a feature difference.
- Good, because the mutations that exist are a closed set with the refused tiers named, so widening it is a decision somebody takes rather than a gap somebody fills.
- Neutral, because an account that is being written to holds one more connection than before, bounded and given back a configured period after the last change.
- Neutral, because a relocation and a delete are not atomic on a server without `MOVE`, and nothing here makes them so. A caller records its intent durably before it calls; that mechanism is issue 448 and this decision deliberately does not anticipate it.
- Bad, because a server advertising neither `MOVE` nor `UIDPLUS` cannot relocate or delete at all under MailFathom, where another mail client would expunge the folder and mostly get away with it.
- Bad, because `\Seen` now has an authored writer as well as an observed one, so the simplest reading of the never-marks-mail-read guarantee — *nothing here ever sets that flag* — is no longer the accurate one and has to be stated as the scoped version instead.

## Validation

- The type separation is verified by the compiler: a component holding `IMailboxSessionFactory` has no method that writes. A unit test additionally proves that a connection created for reading refuses a mutation without reaching the server, and that the read paths still select `ReadOnly` on every establishment and reconnection.
- The reporting rule is asserted directly rather than described: a unit test relocates on a server advertising `MOVE` and on one without it, and requires the records at `Information` and above to be identical while the fallback's three commands appear in the `Debug` record.
- A regression test requires the remote `\Seen` flag to be untouched by a relocation, a delete, and a copy, which is the write-side counterpart of the read-side invariant test.
- The integration suite proves both protocol paths against the orchestrated GreenMail server, which advertises `MOVE` and `UIDPLUS`: the native path end to end, the fallback with the capability masked, and a message-scoped expunge that spares a neighbour another client flagged `\Deleted`.
- The connection bound is verified through the scripted connection sequence, which fails on an establishment a test did not intend, so a second login is a failure rather than a number that grew.

## Pros and Cons of the Options

### B2 — one session type with a write mode

- Good, because it is the smallest change and duplicates no connection machinery.
- Bad, because the guarantee becomes a review rule again. The type a read path holds would have a method that writes, and nothing but attention would stop a later change from calling it.

### B3 — let the server's read-only selection refuse the command

- Good, because it costs nothing to build.
- Bad, because the refusal arrives as an IMAP protocol error at run time, which is a defect discovered in production rather than on the first test that makes the mistake.
- Bad, because it says nothing about a code path that opens its own read-write selection.

### C2 — fall back to a bare `EXPUNGE`

- Good, because relocation and deletion then work on every server.
- Bad, because it removes messages nobody asked about. Unmarking the other flagged messages first, expunging, and restoring the flags afterwards — which is what MailKit does — still destroys another client's pending deletion if the process stops in the middle, and still destroys a message another client flagged between the search and the expunge.

### C3 — delegate to MailKit's `MoveTo`

- Good, because it is one call and MailKit already implements the fallback.
- Neutral, because its native path is exactly what option C1 uses, and that is what MailFathom calls where the server advertises `MOVE`.
- Bad, because its fallback is option C2, and because the individual commands are then invisible: the debug record a broken fallback is diagnosed from would not exist.

### D2 — a write connection per mutation

- Good, because nothing is held between changes.
- Bad, because a run of changes pays for a TCP connection, a TLS handshake, and an authentication each, against a server that counts logins.

### D3 — write over the notification session

- Good, because it costs no additional connection.
- Bad, because a connection in IDLE cannot issue a command until it leaves, so every write would drop the subscription and resubscribe with a `NOTIFY SET` that replaces the server's default again.
- Bad, because it would give a session the read paths hold the ability to write, which is option B2 by another route.

## More Information

- Issue 446 asked the question and carries the decision comment this record is written from; issue 447 is the change that implements it; issue 452 is the feature all of it belongs to.
- [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) governs `MailboxMutationUnsupported` (25001), which is category 2 subcategory 5 — a subcategory of its own because it says the opposite of an unavailable mailbox about repeating the work.
- Draft section 11.1 carries the amended invariant, and draft section 21.5 carries the action tiers this narrows.
- Revisit when a request arrives for one of the refused tiers — sending, a flag other than `\Seen`, or folder management. Each reopens this record with its own authorization review rather than extending the session.
