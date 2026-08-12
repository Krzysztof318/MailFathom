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

**Issue 713 reopened this record**, which is what its refusal of folder management said a request for a refused tier would do. Issue 709 makes a folder mapping the only way MailFathom knows a folder exists at all, and the case it is written for is a rule filing mail into a folder the operator has decided on rather than one the server already has — an archive folder for one sender's mail, a junk folder on a server that ships none. Under the refusal as first written, such a mapping resolves to nothing and the mutation is refused, correctly and by design, which leaves the operator creating the folder by hand in a mail client before MailFathom can file anything into it. Axes E and F below are that reopening. The record's status is still `proposed`, so it is amended here rather than superseded, and the four axes it already decided are untouched by it.

## Decision Drivers

- **The guarantee has to survive every later change, not just this one.** A rule saying reconciliation must not write flags is a rule somebody has to notice during review. A refactor cannot accidentally give reconciliation the ability to write if reconciliation never holds a type that has it.
- **The current invariant conflates a path with an act.** A read — synchronization, content retrieval, any MCP tool — must remain incapable of writing. An act the mailbox owner authored is a different thing, and `\Seen` is where the distinction bites: today it is a snapshot of remote state with exactly one writer, and a rule that marks mail read would make it a value with two.
- **A mail tool may not have side effects nobody asked for.** RFC 3501's bare `EXPUNGE` removes every message in the folder that anyone has flagged `\Deleted`, including messages another client flagged and MailFathom has never seen.
- **Which server an operator uses must not be a feature difference.** RFC 6851 `MOVE` is an extension, and the servers without it are ordinary rather than exotic.
- **A connection is a resource the mail server counts, not a free abstraction.** An account already holds one long-lived connection for `NOTIFY` and IDLE plus `MaxConcurrentFoldersPerAccount` synchronization connections. A provider limit or Dovecot's `mail_max_userip_connections` is reached as a refused login, which surfaces as synchronization failing rather than as the write that caused it.
- **An operator reads an operation, not a command sequence.** What a support question costs is set by whether the record names what was asked for.
- **Irreversible acts driven by attacker-influenced input are the thing to refuse.** Mail content, tool arguments, and model output are untrusted, and a mutation surface wide enough to send is a surface wide enough to be steered into sending.
- **A mapping that resolves to nothing is how a mistyped path reports itself.** `RemotePath` is free text an operator writes, and the unresolved alias is the only thing that ever tells them they wrote it wrong. Whatever MailFathom becomes able to create, that report has to survive: a typo turning into a folder on somebody's mail server named after the mistake is a defect they cannot read.

## Considered Options

The decision has six axes. The first four are independent — an option on one constrains no option on another, which is why they are listed apart rather than as bundled proposals. The last two arrived with the reopening on issue 713, and only they are ordered: F is read at all only where E permits a creation to exist.

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

**E — whether MailFathom may create a folder it is configured to use:**

1. It may not; folder management stays refused whole.
2. It may, for any mapping, whenever the alias resolves to no advertised folder.
3. It may, and only where the mapping asks for it through a switch of its own.

**F — when a permitted creation is issued:**

1. When configuration binds, so every mapping is made real as it is read.
2. On the first use of the alias as the destination of a mutation.
3. Wherever the alias is resolved, which is before a run for a mirrored folder and on demand for one that is only a destination.

## Decision Outcome

Chosen options: **A3**, **B1**, **C1**, **D1**, **E3**, and **F3**.

### MailFathom writes, and exactly four mutations exist

Permitted: **relocate** a message between folders, **delete** one on the server, **set `\Seen`** in both directions, and **copy** one into a second folder. All four are in scope for feature 452; the copy carries a schema question of its own — whether two live occurrences of one message are one local row or two — which is why the action that decides to copy is a separate issue from the session that can.

Refused, and not by configuration:

- **Sending anything.** Send, reply, forward, or any other message the owner did not write themselves. The SMTP outbox owns that surface and its own authorization review, and nothing under feature 452 acquires it.
- **Every flag other than `\Seen`** and the `\Deleted` that is part of a delete or a fallback move. `\Flagged`, `\Answered`, `\Draft`, and keywords stay unwritten.
- **Folder management, other than creating a folder**: renaming one, deleting one, unsubscribing from one, and subscribing to one MailFathom did not itself create.

Naming the refused tiers is part of the decision rather than a note beside it. A later request for one of them reopens this record; it does not read a gap as permission. That has now happened once. Creating a folder was refused above as well until issue 713 reopened the record, and [a folder the operator configured may be created](#a-folder-the-operator-configured-may-be-created-and-only-that-one) is what replaced that refusal: reviewed against the driver that produced it, narrowed to the case the request was actually about, and leaving the rest of the tier refused in the bullet above. It is also the one permitted act here that configuration decides, which is the exception the heading above needs stated: the four mutations carry out a change the mailbox owner already authored, while a creation is an act MailFathom takes on its own initiative and therefore has to be authorized before it can be reached at all.

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

### A folder the operator configured may be created, and only that one

MailFathom creates a folder a mapping names when the account's server advertises none at that path, and only where the mapping asked for it. `CreateIfMissing` is the switch, it sits on the folder entry beside the three that decide what the folder takes part in, and it defaults to `false`. The asymmetry with those three, which default to `true`, is the decision rather than an oversight: they withdraw a folder that already exists from something MailFathom does locally, while this one authorizes an act against somebody's mail server. So a mapping that says nothing behaves exactly as it did before this reopening, and a mistyped `RemotePath` stays the unresolved alias issue 709 refuses rather than becoming a folder named after the mistake.

A creation is issued **where the alias is resolved**, which is one rule covering both cases rather than two triggers to keep in step. A mirrored folder resolves before its run, so its folder is created on the first run after the mapping appears; a folder that is only a destination is resolved on demand when a mutation names it — issue 716 — so its folder is created then. Nothing is created by reading configuration, and nothing is created for a mapping nothing ever resolves. After the first creation the server advertises the folder, so resolution finds it and no further `CREATE` is issued: the write happens once, at the point the folder is genuinely missing, rather than on every process start.

**Only an explicit `RemotePath` may be created.** `CreateIfMissing: true` beside a `SpecialUse` mapping is refused when the configuration binds, naming the alias, exactly as an explicit `GenerateEmbeddings: true` beside `Synchronize: false` already is. A role is how a folder the server has already marked is *found*, and a folder that does not exist advertises no role, so creating one from a role means either RFC 6154's `CREATE ... (USE (\Junk))`, whose support is uneven and which a server unable to set the attribute is required to refuse, or MailFathom inventing a name in somebody's own mailbox and leaving it there for good. Neither is a choice to make on the operator's behalf when writing the path they wanted is one line of configuration.

**Subscription follows a creation and nothing else.** A folder MailFathom created is subscribed to as part of creating it, so it appears in a mail client that lists `LSUB` and the operator can find the mail a rule filed there — a folder they cannot see is most of the way back to the problem creation exists to solve. That is the whole of the subscription tier that reopens: no folder MailFathom did not create is ever subscribed to, and nothing is ever unsubscribed. A server that refuses the `SUBSCRIBE` does not fail the creation, because the folder exists and that is what was asked for; the refusal is logged as a warning naming the alias.

**What IMAP makes awkward is settled here rather than left to the implementation**, because each of these is a defect if it is decided by whichever server was tested against:

- A `CREATE` the server refuses is followed by one re-listing of the account's folders. A folder now advertised at the path means another client, or another MailFathom process, created it between the listing and the attempt, and the creation reads as success; anything else is a failure. That is a listing rather than the destination search this record refuses for a relocation, and the difference is what each one asks: the listing answers exactly the question that was put — does this folder exist — while the search would substitute a guess for an identity the server should have given.
- A hierarchical path is split with the delimiter the server reports through `LIST` or `NAMESPACE`, never an assumed `/`. The configured text is the server's own path and is never rewritten, so the delimiter is read to find the path's segments and used for nothing else.
- The ancestors the configured path names are created first, in order, each one skipped where it is already advertised. Every one of them is a name the operator wrote, so none of them is a folder nobody named; RFC 3501 leaves implicit parent creation to the server's discretion, and walking the path is one behaviour rather than a branch on which discretion a given server exercises.
- A name the server holds as a `\NoSelect` or `\NonExistent` node is a refusal rather than a creation. Discovery leaves both out of the catalog, so the alias resolves to nothing while the name is already taken, and what the operator needs is a different path rather than an act MailFathom can take for them.

**A created folder is bound exactly as a discovered one.** Creation happens before resolution completes, so what follows is an ordinary first binding under a new generation and the mapping-change auditor records it as it records any other. The creation itself is recorded through that same auditor, which is what keeps this record's sentence about remote paths true: a folder path is written outside the database in exactly one place. A refused creation fails as itself, under an error code of its own in category 2 so it is distinguishable from a destination that resolves to nothing, and its message names the alias alone — a remote folder path is not something an exception message may carry, and the path belongs to the audit record and the debug detail.

**Creating a folder is not a fifth mutation.** `IMailboxWriteSession` stays closed at exactly four methods, because appending one is extending the session, which is what this record said a reopening does not do. Creation arrives as a port of its own — `IRemoteFolderCreator`, reached through its own factory and issuing over the account's single write connection — and the separation buys what option B1 bought: a component that can file a message into a folder cannot create one, and a component that can create one cannot relocate, delete, flag, or copy a message. The name states the port's whole surface, so a rename or a delete would need a different type rather than a further method on this one. No second connection is opened, and the read paths reach neither port; an account still holds at most one connection able to change its mailbox.

**Creation is decided here and built by issue 714.** Until that change lands, a mapping whose folder the server does not advertise is refused exactly as issue 709 states.

### The authorization review folder creation required

A reopened tier owes an authorization review against the driver that refused it, and the driver here is that irreversible acts driven by attacker-influenced input are the thing to refuse. Creating a folder is neither half of that.

**The input class is the operator's own configuration file.** A path reaches a creation from `RemotePath` on a mapping the operator wrote, and from nowhere else. Mail content names no folder. A rule names a destination *alias*, and an alias resolves only to a mapping in that same file, so a message crafted to be matched by a rule can direct mail into a folder the operator already configured and can conjure no other. An MCP tool argument and a model output reach no part of this path at all: no tool creates a folder, and nothing derives a mapping from anything a model produced. Somebody able to write that file already holds the mail credentials it carries, so creation widens nothing they could not already do.

**The act destroys nothing and is bounded by the file.** A `CREATE` adds a name to the operator's own mailbox: it removes no mail, moves none, and changes no flag, and the operator undoes it in their own client with the same gesture they would have used to create the folder by hand. How many folders can be created is bounded by the mappings written down and the segments of their paths — a bounded list read from configuration — rather than by anything that happens per message, so no volume of mail and no rate of rule firings produces a second folder.

**That is what separates it from the tiers that stay refused.** Renaming and deleting a folder displace or destroy mail the operator did not name in the act, and a rename additionally breaks every binding pointing at the old path. Sending is a surface an attacker steers by supplying content, which is the driver in its plainest form. Not one of those steps reads across to creating a folder from a path in a file, which is why this tier reopened and they did not.

### Consequences

- Good, because the never-marks-mail-read guarantee stops depending on a reviewer noticing and starts depending on which type a component holds.
- Good, because a server without RFC 6851 behaves identically to one with it from every layer above the session, and an operator's choice of provider is not a feature difference.
- Good, because the mutations that exist are a closed set with the refused tiers named, so widening it is a decision somebody takes rather than a gap somebody fills.
- Good, because a rule can file mail into a folder the operator decided on, and setting that up is one configuration file rather than a file plus a detour through a mail client.
- Neutral, because an account that is being written to holds one more connection than before, bounded and given back a configured period after the last change.
- Neutral, because MailFathom now issues a command that changes a mailbox's structure rather than only its messages. It is bounded to the folders a mapping asked for, recorded through the auditor a binding already uses, and reached through a port no path that moves mail can obtain.
- Neutral, because a relocation and a delete are not atomic on a server without `MOVE`, and nothing here makes them so. A caller records its intent durably before it calls; that mechanism is issue 448 and this decision deliberately does not anticipate it.
- Bad, because a server advertising neither `MOVE` nor `UIDPLUS` cannot relocate or delete at all under MailFathom, where another mail client would expunge the folder and mostly get away with it.
- Bad, because `\Seen` now has an authored writer as well as an observed one, so the simplest reading of the never-marks-mail-read guarantee — *nothing here ever sets that flag* — is no longer the accurate one and has to be stated as the scoped version instead.
- Bad, because `CreateIfMissing` is a key an operator has to know exists. A mapping that meant to have its folder created and did not say so reports the same unresolved alias a typo reports, and telling the two apart is reading the file. That is the price of the typo staying readable, and it is the right way round to pay it.

## Validation

- The type separation is verified by the compiler: a component holding `IMailboxSessionFactory` has no method that writes. A unit test additionally proves that a connection created for reading refuses a mutation without reaching the server, and that the read paths still select `ReadOnly` on every establishment and reconnection.
- The reporting rule is asserted directly rather than described: a unit test relocates on a server advertising `MOVE` and on one without it, and requires the records at `Information` and above to be identical while the fallback's three commands appear in the `Debug` record.
- A regression test requires the remote `\Seen` flag to be untouched by a relocation, a delete, and a copy, which is the write-side counterpart of the read-side invariant test.
- The integration suite proves both protocol paths against the orchestrated GreenMail server, which advertises `MOVE` and `UIDPLUS`: the native path end to end, the fallback with the capability masked, and a message-scoped expunge that spares a neighbour another client flagged `\Deleted`.
- The connection bound is verified through the scripted connection sequence, which fails on an establishment a test did not intend, so a second login is a failure rather than a number that grew.
- Folder creation is built by issue 714, and what it has to establish is stated here rather than there: a mapping without `CreateIfMissing` still refuses, tested beside one that creates so the refusal cannot quietly become a creation; `CreateIfMissing` on a `SpecialUse` mapping fails configuration binding; a `CREATE` against a folder that already exists reads as success, including where another client won the race between the listing and the attempt; a server reporting a delimiter other than `/` builds the same hierarchy from the same configured text; a refused `SUBSCRIBE` leaves the creation successful; and the integration suite creates a folder and files a message into it against a live server, which is where a real `CREATE` and the write connection meet.

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

### E1 — folder management stays refused whole

- Good, because it changes nothing and the refusal keeps its simplest form: MailFathom writes messages and never touches the shape of a mailbox.
- Bad, because the case it refuses is the ordinary one. A rule filing mail into a folder the operator decided on cannot be set up from configuration at all, and the remedy left to them — opening a mail client and making the folder by hand — is a step MailFathom could take from a path it has already been given.

### E2 — create a folder for any mapping that resolves to nothing

- Good, because it needs no configuration key and every mapping simply works.
- Bad, because it turns a typo into a folder. A mapping resolving to nothing is the only report a mistyped `RemotePath` ever produces, and creating the folder makes the mistake succeed, leaves a folder named after it on somebody's mail server, and files mail into it.

### F1 — create when configuration binds

- Good, because a mapping is made real at a predictable moment and a server that refuses the creation says so at startup rather than during a rule.
- Bad, because reading configuration would then write to a mail server, including for a mapping nothing ever uses.
- Bad, because a folder that is only a destination is resolved on demand rather than before a run, so this would have to be two triggers with two chances to disagree about which mappings each one covers.

### F2 — create on the first use as a destination

- Good, because nothing is created until a message actually goes somewhere, which is the narrowest trigger available.
- Bad, because a mirrored folder whose mapping asked to have it created would never be created at all, since such a folder is never a destination. The alias would go on reporting itself unresolved on every run, with the operator having already asked for the folder.

## More Information

- Issue 446 asked the question and carries the decision comment this record is written from; issue 447 is the change that implements it; issue 452 is the feature all of it belongs to.
- Issue 713 reopened the record for folder creation and carries that request; issue 714 is the change that implements what axes E and F decided; issue 709 is the mapping contract whose refusal of a destination that resolves to nothing the creation lifts, and only where a mapping asked.
- [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) governs `MailboxMutationUnsupported` (25001), which is category 2 subcategory 5 — a subcategory of its own because it says the opposite of an unavailable mailbox about repeating the work.
- Draft section 11.1 carries the amended invariant, and draft section 21.5 carries the action tiers this narrows.
- Revisit when a request arrives for one of the tiers that remain refused — sending, a flag other than `\Seen`, or renaming, deleting, or unsubscribing a folder. Each reopens this record with its own authorization review, as folder creation did on issue 713, rather than extending the session.
