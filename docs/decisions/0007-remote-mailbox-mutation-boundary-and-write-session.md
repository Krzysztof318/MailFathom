---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-06
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Write to the remote mailbox through a session type no read path can obtain, and scope the never-marks-read guarantee to retrieval

<!-- describes: src/Application/Mail/Mutations/**, src/Application/Mail/Delivery/Filing/**, src/Domain/Mutations/**, src/Domain/Delivery/Filing/**, src/Infrastructure/Mail/MailKit/Writes/**, src/Infrastructure/Observability/** -->

## Context and Problem Statement

MailFathom has never written to a mailbox. Folders are opened with `FolderAccess.ReadOnly`, bodies are fetched with PEEK semantics, and no code path calls a flag-writing method — and until now the guarantee that mail is never marked read rested on there being no writing path at all. That was the first-release posture rather than the product's destination: draft section 21.5 already sorts remote mailbox mutation into action tiers, and issue 251 asks for rules whose value is mostly on the other side of the line, because a rule that can match a message but not file it is a notification rather than automation.

Issue 446 asked which mutations MailFathom may perform, in what order they become available, and which are refused by construction rather than by configuration. It was answered and closed; this record is that answer, together with the two decisions the answer forced and that only the first implementation could settle — how the read path is kept structurally unable to write, and what the separation costs in connections to a mail server that counts them.

Recorded on issue 447, which is the gate for feature 452. No numbered specification under `specs/` backs it; draft section 11.1 states the invariant this narrows and is amended in the same change set.

**Issue 713 reopened this record**, which is what its refusal of folder management said a request for a refused tier would do. Issue 709 makes a folder mapping the only way MailFathom knows a folder exists at all, and the case it is written for is a rule filing mail into a folder the operator has decided on rather than one the server already has — an archive folder for one sender's mail, a junk folder on a server that ships none. Under the refusal as first written, such a mapping resolves to nothing and the mutation is refused, correctly and by design, which leaves the operator creating the folder by hand in a mail client before MailFathom can file anything into it. Axes E and F below are that reopening. The record's status is still `proposed`, so it is amended here rather than superseded, and the four axes it already decided are untouched by it.

**Issue 917 reopened it a second time**, for the flag tier this record refused whole. A rule that decides a message matters can move it, delete it, or mark it read, and none of those says *this one, look at it later* — the mailbox owner's own gesture for that is the star their mail client draws from `\Flagged`, and every client they read mail in already shows it. Keywords are the same request one step further: a rule that sorts mail into categories the operator named — `$Todo`, `$Invoice`, `$Waiting` — has nowhere to put the category under this refusal except a folder, which moves the message out of the inbox to say something about it. Both are what the tier's own driver calls an act the mailbox owner authored, carried by MailFathom on their behalf, and both are undone in the client with the gesture that would have made them. Axis G below is that reopening and axis H is the one thing carrying it turns out to require; the six axes above are untouched, and the status is still `proposed`, so this is an amendment rather than a supersession.

**Issue 864 reopened it a third time**, for who may author a change rather than for which change may be authored. Axes G and H settled that `\Flagged` and keywords are writable and left the requester where the record had always assumed it: a rule, or the spam verdict that behaves like one. What issue 862 then made visible is that all three values are readable, publishable, and filterable over the protocol surface, so a caller can find the starred mail and star nothing — and the act the tier's own driver describes, *something the mailbox owner authored, carried by MailFathom on their behalf*, is exactly what an agent triaging mail for the owner is doing. Axis I below is that reopening. The eight axes above are untouched by it, no mutation is added or removed, and the status is still `proposed`, so this is an amendment rather than a supersession.

**Issue 739 reopened it a fourth time**, for the one message this record's closed set has no way to put anywhere: the one MailFathom composed itself. Issue 738 delivers a message through a submission server, and SMTP says nothing about the sender's own mailbox — a delivered message leaves no trace in the folder its owner reads their sent mail in unless something appends one there. Every mutation this record permits acts on a message the server already holds, so under the set as written a deployment sends mail and the owner's mail client shows they never did. The same gap covers the two other stages an outgoing message has: a draft the owner is composing, and a message held until an instant still ahead, neither of which exists anywhere the owner can see. Axis J below is that reopening and axis K is the one thing carrying it turns out to require; the nine axes above are untouched, and the status is still `proposed`, so this is an amendment rather than a supersession.

## Decision Drivers

- **The guarantee has to survive every later change, not just this one.** A rule saying reconciliation must not write flags is a rule somebody has to notice during review. A refactor cannot accidentally give reconciliation the ability to write if reconciliation never holds a type that has it.
- **The current invariant conflates a path with an act.** A read — synchronization, content retrieval, any MCP tool — must remain incapable of writing. An act the mailbox owner authored is a different thing, and `\Seen` is where the distinction bites: today it is a snapshot of remote state with exactly one writer, and a rule that marks mail read would make it a value with two.
- **A mail tool may not have side effects nobody asked for.** RFC 3501's bare `EXPUNGE` removes every message in the folder that anyone has flagged `\Deleted`, including messages another client flagged and MailFathom has never seen.
- **Which server an operator uses must not be a feature difference.** RFC 6851 `MOVE` is an extension, and the servers without it are ordinary rather than exotic.
- **A connection is a resource the mail server counts, not a free abstraction.** An account already holds one long-lived connection for `NOTIFY` and IDLE plus `MaxConcurrentFoldersPerAccount` synchronization connections. A provider limit or Dovecot's `mail_max_userip_connections` is reached as a refused login, which surfaces as synchronization failing rather than as the write that caused it.
- **An operator reads an operation, not a command sequence.** What a support question costs is set by whether the record names what was asked for.
- **Irreversible acts driven by attacker-influenced input are the thing to refuse.** Mail content, tool arguments, and model output are untrusted, and a mutation surface wide enough to send is a surface wide enough to be steered into sending.
- **A mapping that resolves to nothing is how a mistyped path reports itself.** `RemotePath` is free text an operator writes, and the unresolved alias is the only thing that ever tells them they wrote it wrong. Whatever MailFathom becomes able to create, that report has to survive: a typo turning into a folder on somebody's mail server named after the mistake is a defect they cannot read.
- **A write that says one thing may not quietly say another.** IMAP's `STORE FLAGS` replaces a message's entire flag set, so the command that most obviously writes a message's keywords also clears `\Seen`, `\Flagged`, `\Answered`, and `\Draft` — values another client set and MailFathom may never have observed. A rule that labels a message and marks it unread as a side effect is a defect nobody would report as one.
- **Who asked is a separate question from what was asked, and only the first is about authorization.** The mutations are a closed set decided on their own merits, and widening who may reach one does not widen the set. What it does change is the input class the authorization review is written against, so a new kind of requester owes a review of its own even where it asks for nothing new.
- **An append is the one write here that cannot be corrected by repeating it.** Every other mutation names a message the server already holds, so issuing it twice reaches the same message and leaves the same state. An `APPEND` creates a message, so a second one is a second copy in somebody's folder — and nothing the folder shows afterwards tells the two apart, because both are the same bytes with the same identity.
- **What a server keeps is a capability rather than an assumption.** RFC 9051 has a server declare through `PERMANENTFLAGS` which flags survive a session, and one that keeps no arbitrary keyword accepts the `STORE` and forgets it. A write nobody refused, whose effect is gone by the next run, reads to an operator as a rule that never fired.

## Considered Options

The decision has eleven axes. The first four are independent — an option on one constrains no option on another, which is why they are listed apart rather than as bundled proposals. E and F arrived with the reopening on issue 713 and are ordered against each other: F is read at all only where E permits a creation to exist. G and H arrived with the reopening on issue 917 and are ordered the same way, H being a question only the widest option on G raises. I arrived with the reopening on issue 864 and is independent of all eight: it decides who may ask for a mutation the set already holds. J and K arrived with the reopening on issue 739 and are ordered against each other like the two pairs before them: K is a question only an append that exists can raise.

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

**G — which flags beyond `\Seen` a rule may write:**

1. None; the flag tier stays refused whole.
2. `\Flagged` in both directions, and keywords.
3. Every system flag IMAP defines, and keywords.

**H — how a rule replaces a message's keywords:**

1. `STORE FLAGS` with the keywords it names, which is the command the operation reads as.
2. Read what the message carries, then `STORE -FLAGS` the surplus and `STORE +FLAGS` the named ones.
3. No replacement exists; a rule adds and removes keywords and nothing else.

**I — who may author a flag or keyword change:**

1. A rule and a spam verdict only; the protocol surface reads the three values and asks for none of them.
2. A caller too, through one MCP tool that opens the same durable record a rule opens and holds no session of either kind, behind a permission of its own that reading mail does not confer.
3. A caller too, through an MCP tool that obtains the write session and issues the `STORE` while the call is open.

**J — whether MailFathom may put a message it composed into a folder:**

1. It may not; `APPEND` stays outside the closed set, and an outgoing message exists only in MailFathom's own record.
2. It may, into any folder a caller names.
3. It may, only into the folder playing the role the outgoing record's own stage calls for, and only for a message MailFathom composed and stored itself.

**K — which flags such an append carries:**

1. None; the copy arrives with no flags, as a plain message in the folder.
2. The flags the destination's role means: `\Draft` where the message has not left, `\Seen` where it has.
3. Whatever the caller names, from the flags IMAP defines.

## Decision Outcome

Chosen options: **A3**, **B1**, **C1**, **D1**, **E3**, **F3**, **G2**, **H2**, **I2**, **J3**, and **K2**.

### MailFathom writes, and the mutations that exist are a closed set

Permitted: **relocate** a message between folders, **delete** one on the server, **set `\Seen`** in both directions, and **copy** one into a second folder. All four are in scope for feature 452; the copy carries a schema question of its own — whether two live occurrences of one message are one local row or two — which is why the action that decides to copy is a separate issue from the session that can.

Issue 917 added four more, all of them under axis G: **set `\Flagged`** in both directions, **add** keywords, **remove** keywords, and **replace** the whole keyword set with the ones named. They are one tier rather than four decisions — a keyword and `\Flagged` are the same act as far as IMAP is concerned, both written with `STORE`, both read back by synchronization the way `\Seen` already is, and both meaning whatever the mailbox owner's client shows them as.

Refused, and not by configuration:

- **Sending anything.** Send, reply, forward, or any other message the owner did not write themselves. The SMTP outbox owns that surface and its own authorization review, and nothing under feature 452 acquires it.
- **`\Answered` and `\Draft`**, and every flag IMAP reserves for a client's own bookkeeping. Each states something about an act rather than about the message — that a reply was sent, that this is a message being composed — and MailFathom writing one asserts an act it did not perform. The `\Deleted` that is part of a delete or a fallback move stays what it was: a step of another mutation rather than a flag a rule may write.
- **Folder management, other than creating a folder**: renaming one, deleting one, unsubscribing from one, and subscribing to one MailFathom did not itself create.

Naming the refused tiers is part of the decision rather than a note beside it. A later request for one of them reopens this record; it does not read a gap as permission. That has now happened twice. Creating a folder was refused above as well until issue 713 reopened the record, and [a folder the operator configured may be created](#a-folder-the-operator-configured-may-be-created-and-only-that-one) is what replaced that refusal; `\Flagged` and keywords were refused with the whole flag tier until issue 917 reopened it, and [what a rule may write on a message](#what-a-rule-may-write-on-a-message-and-what-it-may-not) is what replaced that. Both were reviewed against the driver that produced the refusal, both were narrowed to the case the request was actually about, and both left the rest of their tier refused in the bullets above. Creation remains the one permitted act here that configuration decides, which is the exception the heading above needs stated: every mutation carries out a change the mailbox owner already authored, while a creation is an act MailFathom takes on its own initiative and therefore has to be authorized before it can be reached at all.

### The read path is incapable of writing, as a property of the types

`IMailboxWriteSession` is a different type from `IMailboxSession`, reached through a different factory, and it exposes exactly the mutations above and nothing else — no method that sends, none that manages a folder, and none that writes `\Answered` or `\Draft`. Synchronization, reconciliation, and content retrieval depend on the read factory, so none of them can obtain a session that writes whatever a later change does inside them.

The MCP surface holds neither factory, which is the stronger position and the one axis I keeps it in. A tool that reads depends on the read factory's consumers rather than on the factory, and the one tool that writes — `set_mail_flags`, on issue 864 — opens a durable mutation record and returns. Nothing on that surface can obtain a session of either kind, so the guarantee over it is that no protocol call reaches a mail server at all, rather than that it reaches one incapable of writing.

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

**Creating a folder is not a mutation.** It is kept off `IMailboxWriteSession` entirely, because a folder is not a message and a component that files mail into one has no business making one. Creation arrives as a port of its own — `IRemoteFolderCreator`, reached through its own factory and issuing over the account's single write connection — and the separation buys what option B1 bought: a component that can file a message into a folder cannot create one, and a component that can create one cannot relocate, delete, flag, or copy a message. The name states the port's whole surface, so a rename or a delete would need a different type rather than a further method on this one. No second connection is opened, and the read paths reach neither port; an account still holds at most one connection able to change its mailbox.

**Creation is decided here and built by issue 714.** Until that change lands, a mapping whose folder the server does not advertise is refused exactly as issue 709 states.

### The authorization review folder creation required

A reopened tier owes an authorization review against the driver that refused it, and the driver here is that irreversible acts driven by attacker-influenced input are the thing to refuse. Creating a folder is neither half of that.

**The input class is the operator's own configuration file.** A path reaches a creation from `RemotePath` on a mapping the operator wrote, and from nowhere else. Mail content names no folder. A rule names a destination *alias*, and an alias resolves only to a mapping in that same file, so a message crafted to be matched by a rule can direct mail into a folder the operator already configured and can conjure no other. An MCP tool argument and a model output reach no part of this path at all: no tool creates a folder, and nothing derives a mapping from anything a model produced. Somebody able to write that file already holds the mail credentials it carries, so creation widens nothing they could not already do.

**The act destroys nothing and is bounded by the file.** A `CREATE` adds a name to the operator's own mailbox: it removes no mail, moves none, and changes no flag, and the operator undoes it in their own client with the same gesture they would have used to create the folder by hand. How many folders can be created is bounded by the mappings written down and the segments of their paths — a bounded list read from configuration — rather than by anything that happens per message, so no volume of mail and no rate of rule firings produces a second folder.

**That is what separates it from the tiers that stay refused.** Renaming and deleting a folder displace or destroy mail the operator did not name in the act, and a rename additionally breaks every binding pointing at the old path. Sending is a surface an attacker steers by supplying content, which is the driver in its plainest form. Not one of those steps reads across to creating a folder from a path in a file, which is why this tier reopened and they did not.

### What a rule may write on a message, and what it may not

A rule may set or clear **`\Flagged`**, and it may **add**, **remove**, or **replace** a message's keywords. Every one of those is the same shape as the `\Seen` this record already permitted: a `STORE` against one message, in a direction the operator wrote down, whose effect their own mail client renders and their own gesture undoes. Nothing here changes who writes the *stored* value — synchronization observes what the server reports back, exactly as it does for `\Seen`, so a flag and a keyword each still have one local writer.

The tier stops there rather than at every flag IMAP defines. `\Answered` and `\Draft` state that an act was performed — a reply was sent, a message is being composed — and MailFathom setting one asserts something it did not do, to a client that will render it as fact. That is a different question from whether a message is starred, and it is not the one issue 917 asked.

**A replacement is carried as a difference rather than as `STORE FLAGS`.** The obvious command is the wrong one: `STORE FLAGS` replaces a message's *entire* flag set, so writing a keyword with it also clears `\Seen`, `\Flagged`, `\Answered`, and `\Draft` — including values another client set that MailFathom has never seen. So a replacement reads what the message carries, removes the keywords it carries that the rule did not name, and adds the ones it did, with `STORE -FLAGS` and `STORE +FLAGS`. The read is a `UID FETCH (FLAGS)`, which sets no flag of its own; that is what keeps the retrieval invariant true of a write path as well. Where the difference is empty in one direction no command is issued for it, because `STORE +FLAGS ()` is not a command RFC 9051 has.

**A server that would not keep the keyword refuses instead of accepting it.** RFC 9051 has a selected folder advertise `PERMANENTFLAGS`, and `\*` in that list means arbitrary keywords persist. Where the folder advertises neither `\*` nor the specific keywords a rule names, an addition and a replacement report `MailboxMutationUnsupported` (25001) naming the account, the folder alias, and the capability — the same refusal a missing `UID EXPUNGE` produces, for the same reason: a command the server accepts and forgets is worse than one it declines, because the rule then reads as never having fired. A **removal** is never refused on that ground. Taking a keyword off a message that has one is meaningful whatever the folder will keep in future, and refusing it would leave a label nothing is allowed to clear.

**A keyword is refused when it is written rather than dropped when it is read.** A keyword is an IMAP atom, so a space, a control character, or one of `( ) { % * " \ ]` cannot be sent at all. Reading already handles such a value by dropping it, because a server may report anything and MailFathom is not the party that wrote it; authoring is the opposite case, and an operator who wrote an unusable keyword needs to be told so against the key they wrote it in rather than to have it silently disappear from what their rule does. The operator's own spelling is what goes on the wire, since IMAP compares keywords case-insensitively but a client displays the text the server holds.

**What an account permits is two switches rather than four.** `MarkAsFlagged` and `WriteKeywords` sit beside the existing `Move`, `Copy`, `Delete`, and `MarkAsRead`, and both default to `true` for the reason the reversible ones do: they are undone from any mail client, and `Delete` stays the one action every account opts into. `WriteKeywords` covers all three keyword actions together, because permitting an addition while refusing a removal would leave mail accumulating labels nothing may take off again — which is not a posture anybody would choose deliberately.

### The authorization review the flag tier required

A reopened tier owes an authorization review against the driver that refused it. That driver is again that irreversible acts driven by attacker-influenced input are the thing to refuse, and neither half of it holds here.

**Nothing is destroyed and nothing is displaced.** A flag and a keyword are annotations on a message that stays where it is, in the folder it was in, with its content untouched. Every one of them is reversible from the operator's own client with one gesture, and reversible by MailFathom itself, since each of these mutations exists in both directions. That is the whole distance between this tier and the delete this record permits only behind an explicit opt-in.

**The input class is the operator's own configuration file, with one step to check.** A rule's action block is written by whoever writes the file, so which keyword text is sent is theirs and never the sender's. Mail content reaches this path only as what a *condition* matched — a message crafted to satisfy a rule can cause that rule's own action to fire, and can cause no other action and no other keyword. What that buys is bounded by the file: the worst an attacker who fully controls a message achieves is the labelling the operator already declared for mail of that kind. A caller authoring a change is a second requester with a wider input class, admitted by issue 864 and reviewed in [the authorization review the caller-authored tier required](#the-authorization-review-the-caller-authored-tier-required); this paragraph describes the rule requester alone.

**The keyword text itself is not a second injection surface.** It is validated as an IMAP atom before anything is sent, which rules out the quoting and the line structure a protocol injection would need, and it is refused rather than escaped. It is also the value a rule set's own revision digest is computed over, and every separator that digest uses is a control character the same validation refuses — so an edited keyword moves the revision, as an edited rule must, and no keyword can be written that forges one.

### A caller may author a flag change, and asks for it the way a rule does

The tool records; it does not store. `set_mail_flags` writes one `MailboxMutationRecord` per value the call asked for, in one commit, and answers with those records and their lifecycle. The account's own convergence pass then carries each of them to a completed or a dead-lettered ending, exactly as it carries a change a rule authored. Option I3 was refused for what it would have cost on three counts at once: a protocol request would wait on IMAP, it would open a connection against the account's own bounded write slot on a caller's schedule, and a crash between the command and the answer would leave a mailbox that had changed and a caller that was told nothing. Recording instead makes a retry the same request rather than a second one, which is what the idempotency identity already existed for.

**The requester is the invocation, not the caller and not the change.** A record's identity is the occurrence, the mutation, and who asked; for a tool call the third is the `requestId` the caller sent, or one MailFathom generates when it sent none. That is the honest reading of both cases: a client repeating a call it is unsure about carries the same identity and asks once, and a client that declined to say is asking again — a caller that starred a message, unstarred it, and starred it again has made three requests and means all three. Keying it to the *change* would have made the third of those unsayable. What that costs is one check the other requesters never needed: a rule carries its revision in its own identity and a classification its corpus and threshold, so their terms cannot differ under one identity, while a `requestId` is text a caller picked and can. The use case therefore compares the terms of the record it was answered with against the ones this call asked for and refuses a mismatch, rather than reporting the earlier record as this call's — a change published as written down while the mailbox never moves is the one outcome a caller could not see from the answer, since the result names the record and never the terms.

**Reaching a mailbox at all is its own grant.** `mailfathom.mail.flags.write` is a name of its own under [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) and does not follow from `mailfathom.mail.read`, because reading somebody's mail and changing it are different acts with different consequences and only the second is visible to the owner in the client they open. It is asked for in the listing, in the call, and again in the use case, so an entrypoint added later reaches the same refusal without passing the transport.

**What may be written is what may be read.** The same resolver answers both, so an account this deployment no longer serves and a folder an operator withheld from tools are mail no tool may change either. The three refusals — no such row, an unserved account, a withheld folder — are one answer, because telling them apart would let a caller learn which identifiers exist by asking about them. A write surface that could reach what the read surface withholds would be the way round the withholding rather than a second capability.

**What an account permits stays a statement about rules.** `RuleActions` is where an operator says which changes a *rule* may ask for on an account, validated at configuration time against the rules that were declared, and a tool call is not a rule. So this tool is not gated on those switches; the two gates it does have are the deployment's own read-only posture, where one exists, and the caller's grant. An operator who wants to withhold the capability withholds the permission, or does not enable the surface.

### The authorization review the caller-authored tier required

A new kind of requester owes a review against the driver that refuses irreversible acts driven by attacker-influenced input. The first half is unchanged from the flag tier's own review and is what carries this: nothing is destroyed, nothing is displaced, and every value is reversible with the gesture that would have made it, from MailFathom or from any client the owner opens.

**The input class is what changes, and it is the widest one this record has admitted.** A tool argument is written by whatever is driving the client, which may be a model that has just read the mail it is triaging — so a message crafted to be read can, in principle, influence which message an agent then marks or labels. What that buys an attacker is bounded by the set rather than by the file: the worst outcome is a message marked read, starred, or labelled wrongly, each of them reversible and each of them visible to the owner in their own client. It is not a foothold for anything else, because the tool reaches exactly these three values and the surface below it has no other mutation to offer.

**Keyword text authored by a model is validated exactly as an operator's is.** It is refused as an IMAP atom before anything is sent, which rules out the quoting and the line structure a protocol injection would need, and it is refused rather than escaped. The count and length bounds a read applies are applied here too, so a caller cannot attach what a read would have discarded. The refusal names the rule and never the keyword, because the text is the owner's or their client's and a failure message is not a place to repeat it.

**The sentence issue 917 wrote here has been overtaken and is corrected rather than left standing.** That review said *no MCP tool writes a flag or a keyword, and no model output reaches this path at all*. Both halves stopped being true with this amendment, which is why the paragraph above replaces them rather than sitting beside them: the review that admits a requester is the place the requester has to be described.

### A message MailFathom composed is filed where its own stage says, and nowhere else

MailFathom may **append** a message to a folder, and the message it may append is one it composed and stored itself — never one the mailbox holds, and never bytes a caller supplies. Which folder is not the caller's either: the outgoing record's stage decides it, through the role the destination folder plays. A message waiting for an instant still ahead goes to the folder playing the **outbox** role, and a message a submission server has accepted to the one playing the **sent** role; the **drafts** role is decided by the same tier and filed by nothing today, because composing a draft is not something this system does yet. There is one filing mechanism for all of them, because they are one act with a place per state rather than a feature per place — a further state is a member of the closed set the stage travels on rather than a second filer.

**The outgoing record stays the truth about what will be sent, and the copy is a view of it.** Deleting the copy in a mail client cancels nothing — cancelling is its own command against the record — and nothing reads the folder to decide what to send. That is what makes the copy safe to write and safe to leave out: a deployment that appends nothing loses visibility and loses no mail.

**No server advertises an outbox, so nothing discovers one.** RFC 6154 defines the special-use attributes a server may publish and there is no `\Outbox` among them, because the outbox a mail client shows is that client's own local queue of what it has not managed to send. MailFathom's outbox is the durable outgoing record. So the outbox role is MailFathom's own, it is never read off a server, and a provider folder merely *named* like one is left untouched — a folder plays this role only where an operator mapped a path to it by hand, which is also why the mirror is off unless somebody asks for it. A mapping that names the role with no path is refused where configuration is read, because discovery has nothing to look for.

**A copy is appended once, and an append whose answer never came back is never repeated.** The record of the filing is written before the command goes out, so a process that died between the command and the answer leaves a row saying the copy may be there — and nothing appends again on the strength of it. That is the one outcome in this record that is deliberately left unsettled rather than retried, for the reason the driver states: a second `APPEND` is a second message in somebody's folder, and nothing distinguishes them afterwards. A failure *before* the command is an ordinary failure and nothing forbids attempting it again, although nothing sweeps for one today: a send that has settled is claimed by nothing, so a copy that failed to be filed is reported rather than re-attempted.

**The copy comes back through synchronization, and is recognized rather than guessed at.** Where the server advertises RFC 4315 `UIDPLUS`, its `APPENDUID` response names the occurrence exactly and that is the join. Where it does not, the `Message-ID` MailFathom minted for the message and read back off the appended bytes is what recognizes it — an identity comparison rather than a search for something that looks like the message. Either way the stored email is marked as this deployment's own, which is what keeps a rule conditioned on arriving mail from firing on the owner's own outgoing message and keeps spam classification from scoring one. Nothing else about the copy is treated differently: it is stored, searchable, cut, and embedded as any other message in that folder.

**Filing is never part of delivering.** A message is delivered or it is not, and where its copies are is a second account of the same message: an append that failed after a successful delivery leaves the record saying delivered and not filed, with the reason beside it, and nothing offers the message to anybody again. Whether the sent copy is appended at all is a per-account setting defaulting to on, configured rather than detected, because a provider that files the copy itself does so asynchronously and looking in the folder afterwards cannot tell *will appear shortly* from *will never appear*.

### The flags such a copy carries, and the withdrawal that follows the outbox mirror

The flags are the destination's meaning rather than a caller's choice: **`\Draft`** where the message has not left — a draft, and a message waiting in the outbox — and **`\Seen`** where it has, because a copy of what the owner just sent is not unread mail for them to read. They travel with the role as one value, so a caller cannot file a draft as read or a sent copy as unread.

**This is the one place `\Draft` is written, and it is not the assertion axis G refused.** That refusal was that `\Answered` and `\Draft` state acts MailFathom did not perform, told to a client that renders them as fact. Here MailFathom composed the message and it has not been sent, so the flag states exactly what happened; and unlike the flag tier, this is a flag on a message MailFathom itself created rather than on somebody's mail. The refusal stands unchanged for every message the mailbox already holds — no rule, no tool, and no reconciliation writes `\Draft` on one.

**The outbox mirror is withdrawn when the message leaves, and the withdrawal reaches only that copy.** It is `STORE +FLAGS (\Deleted)` followed by RFC 4315 `UID EXPUNGE` against the UID the append reported, which is axis C's mechanism applied to a message MailFathom itself put there; a bare `EXPUNGE` is no more available here than anywhere else in this record, so a server without `UIDPLUS` leaves the copy standing rather than expunging the folder. A copy the server never named cannot be reached at all — searching the folder for something that looks like the message is a guess about identity — so such a row is marked withdrawn regardless, which leaves one copy of the owner's own message in a folder they mapped, deletable with the gesture they would have used anyway. The one row the withdrawal does **not** resolve is a mirror whose own append was never answered: marking that withdrawn would be MailFathom stating the copy is not in the folder when nobody knows whether it ever arrived, so it stays where the issued write left it and reports its outcome as unknown — the same answer filing gives about the same row, and for the same reason. A sent copy is withdrawn by nothing: it is what the owner keeps.

### The authorization review the filing tier required

A reopened tier owes an authorization review against the driver that refused it, which is again that irreversible acts driven by attacker-influenced input are the thing to refuse.

**Nothing is destroyed, and the one thing that is removed is a message MailFathom put there.** An append adds a message to the mailbox and displaces none; the only expunge in the tier names, by UID, a copy this deployment appended minutes earlier and recorded. A copy the owner does not want is deleted from any mail client with one gesture, which is the same reversibility the flag tier was granted on.

**The content is MailFathom's own, and it is the bytes a submission already carried.** The append reuses the stored MIME rather than recomposing it, so what lands in the folder is what the recipients received, down to the `Message-ID` — a recomposition would thread as a second message in every client. Nothing a sender wrote reaches this path: an arriving message can no more cause an append than it can cause a send, because what is appended is an outgoing record that already exists and whose recipients and content were settled when it was authored. Model output does not reach it either; the authoring boundary that composes an outgoing message is where that question is answered, and this tier only files what that boundary produced.

**The destination is not addressable.** A caller names a stage, never a folder, and the stage resolves through a role an operator mapped. So the widest thing an attacker who fully controls a message could reach — supposing they could reach this path at all — is the folder the operator already nominated for that stage, and a folder nobody nominated is a filing that reports itself as having nowhere to go rather than one that picks somewhere.

### Consequences

- Good, because the never-marks-mail-read guarantee stops depending on a reviewer noticing and starts depending on which type a component holds.
- Good, because a server without RFC 6851 behaves identically to one with it from every layer above the session, and an operator's choice of provider is not a feature difference.
- Good, because the mutations that exist are a closed set with the refused tiers named, so widening it is a decision somebody takes rather than a gap somebody fills.
- Good, because a rule can file mail into a folder the operator decided on, and setting that up is one configuration file rather than a file plus a detour through a mail client.
- Good, because a rule can now say something about a message without moving it. Marking mail for attention and sorting it into named categories both stop requiring a folder, which is the shape those rules were being written in only because it was the shape available.
- Neutral, because a replacement of a message's keywords costs a `FETCH` before its `STORE`s. That is the price of not clearing flags the rule never mentioned, and it is paid only by the replacement — an addition and a removal issue one command each.
- Neutral, because an account that is being written to holds one more connection than before, bounded and given back a configured period after the last change.
- Neutral, because MailFathom now issues a command that changes a mailbox's structure rather than only its messages. It is bounded to the folders a mapping asked for, recorded through the auditor a binding already uses, and reached through a port no path that moves mail can obtain.
- Neutral, because a relocation and a delete are not atomic on a server without `MOVE`, and nothing here makes them so. A caller records its intent durably before it calls; that mechanism is issue 448 and this decision deliberately does not anticipate it.
- Good, because an agent triaging mail for the owner can say so in the mailbox the owner actually opens, rather than only inside MailFathom, and does it through the same durable record everything else here uses.
- Neutral, because the protocol surface now has a tool that is not read-only, so `readOnlyHint` and `openWorldHint` stop being constants of that surface and become facts about a tool.
- Neutral, because a caller's change is not visible in a listing until the next run has both issued it and read the folder back. The stored value keeps its single writer, which is the property that made the lag acceptable everywhere else it appears.
- Bad, because a server advertising neither `MOVE` nor `UIDPLUS` cannot relocate or delete at all under MailFathom, where another mail client would expunge the folder and mostly get away with it.
- Bad, because model output now reaches an authored mutation, which is the input class this record refuses irreversible acts on. What makes it acceptable is that the tier it reaches is reversible in both directions and reaches nothing else, and that is a judgement this record makes explicitly rather than a consequence of the tool existing.
- Bad, because `\Seen` now has an authored writer as well as an observed one, so the simplest reading of the never-marks-mail-read guarantee — *nothing here ever sets that flag* — is no longer the accurate one and has to be stated as the scoped version instead.
- Bad, because `CreateIfMissing` is a key an operator has to know exists. A mapping that meant to have its folder created and did not say so reports the same unresolved alias a typo reports, and telling the two apart is reading the file. That is the price of the typo staying readable, and it is the right way round to pay it.
- Bad, because a rule can now be refused by a folder's `PERMANENTFLAGS` rather than by anything the operator wrote, and that refusal arrives when the rule fires rather than when the configuration is read. Nothing at startup can know it: the capability belongs to a selected folder on a server, so the earliest honest moment to report it is the write itself.
- Bad, because keywords are text an operator invents, and the constraint on that text is IMAP's rather than MailFathom's. A keyword refused for carrying a character an atom cannot hold is a refusal that reads as arbitrary until somebody knows why, which is why the message names what the constraint is instead of only that the value failed.
- Good, because a deployment that sends mail leaves a record of it where its owner actually looks — their own mail client — instead of only in MailFathom's database, which no mail client reads.
- Good, because the copy that comes back is recognized as this deployment's own, so automation conditioned on arriving mail cannot react to what the owner just sent. That is a defect nothing would have reported: the rule would simply have fired.
- Neutral, because MailFathom now creates a message on somebody's mail server rather than only annotating or moving one that is there. It is bounded to messages MailFathom composed, to folders an operator mapped a role to, and to one copy per stage per message.
- Bad, because an append whose answer never arrived leaves a state nobody can settle: the copy may be in the folder or may not, and the record says exactly that rather than resolving it. Resolving it either way would mean a second copy or a permanent gap, and this is the honest one of the three.
- Bad, because a copy that failed to be filed stays unfiled. The failure is recorded against the send, logged, and counted, but a settled send is claimed by nothing, so nothing comes back for the append the way a delivery pass comes back for a deferred send.
- Bad, because whether a provider files the sent copy itself is a setting an operator has to get right. Leaving it on where the provider also files produces two copies of every sent message, and turning it off where the provider does not produces none — and no observation MailFathom can make distinguishes the two providers in time to decide for them.

## Validation

- The type separation is verified by the compiler: a component holding `IMailboxSessionFactory` has no method that writes. A unit test additionally proves that a connection created for reading refuses a mutation without reaching the server, and that the read paths still select `ReadOnly` on every establishment and reconnection.
- The reporting rule is asserted directly rather than described: a unit test relocates on a server advertising `MOVE` and on one without it, and requires the records at `Information` and above to be identical while the fallback's three commands appear in the `Debug` record.
- A regression test requires the remote `\Seen` flag to be untouched by a relocation, a delete, and a copy, which is the write-side counterpart of the read-side invariant test.
- The integration suite proves both protocol paths against the orchestrated GreenMail server, which advertises `MOVE` and `UIDPLUS`: the native path end to end, the fallback with the capability masked, and a message-scoped expunge that spares a neighbour another client flagged `\Deleted`.
- The connection bound is verified through the scripted connection sequence, which fails on an establishment a test did not intend, so a second login is a failure rather than a number that grew.
- The flag tier is proven by unit tests that read the commands a session issued rather than its return value: a flagged write in each direction; an addition and a removal as `+FLAGS` and `-FLAGS` carrying the keywords as written; a replacement issuing its `FETCH` first and then only the difference in each direction, with no `STORE FLAGS` anywhere in the sequence; a folder advertising neither `\*` nor the named keyword refusing an addition and a replacement while still performing a removal; and a keyword an atom cannot hold refused where the configuration is read, naming the key.
- The `\Seen` regression test extends to the new mutations, and it carries the control the absence rule requires: the class issues a real `\Seen` change afterwards and observes it, so an observation channel that silently reports nothing fails rather than passing every absence.
- The integration suite writes `\Flagged` and a keyword against the orchestrated server and reads both back, which is where a real `PERMANENTFLAGS` and the write connection meet.
- The caller-authored path is proven at its own boundary rather than through the session: unit tests require the tool to refuse an identifier that names no email before anything is looked up, to refuse a call that asks for nothing and one that states half a keyword change, to refuse a keyword list longer than a message may carry before anything normalizes it, to name each call its own request where the caller sent no `requestId` and the caller's own where it did, to refuse a `requestId` reused for a different value rather than answering with the earlier record, and to open one record per value in one commit even when the first commit conflicts; the use case is required to ask for `mailfathom.mail.flags.write` before it reads anything, and to answer a withheld folder and an absent row identically. The descriptor test requires the advertised annotations to say not read-only and open-world, which is the one place a client learns it.
- Reconciliation is required to attribute each of the three values independently, so a run withholds the value a record accounts for while raising the one beside it that nobody asked for.
- The filing tier is proven by unit tests that read what the session was asked for rather than what it returned: an account that files no sent copy appends nothing at all; a delivered message appends the stored bytes with `\Seen` at the injected clock's instant; a mirrored message appends with `\Draft` and never with `\Seen`; an append the server never answered is not appended a second time however often the pass runs; and a failed append after a successful delivery leaves the record at `Sent` with a filing failure beside it and its attempt count unmoved.
- The join is proven in both directions a server can answer: a discovery at the occurrence an `APPENDUID` named, and a discovery on a server that named none, matched by the identity in the appended bytes. Both assert that the stored email is recorded as filed from the outgoing record and that the filing is stamped as met — with the control the absence rule requires, since the same run over a message no filing accounts for stores ordinary arriving mail and writes neither.
- The withdrawal is asserted as its commands: `STORE +FLAGS (\Deleted)` and `UID EXPUNGE` naming that UID alone, with no bare `EXPUNGE` anywhere in the sequence, a folder whose UIDVALIDITY moved since the append refusing to remove anything, and a server without `UIDPLUS` refused rather than expunging the folder.
- Configuration is asserted where it binds: an outbox role written without a path fails validation naming the key, the same role beside a path resolves to the folder the operator named, a folder merely named like an outbox plays no role, and an account that says nothing about the sent copy files one.
- The integration suite appends a sent copy against the orchestrated server, reads it back through an ordinary synchronization run, and requires exactly one stored occurrence joined to the outgoing record and absent from the arrival queue a rule pass reads.
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

### G1 — the flag tier stays refused whole

- Good, because it keeps the refusal in the form that needs no reading: `\Seen` is the only flag MailFathom writes.
- Bad, because it refuses the ordinary case for the tier's own reason. Marking mail for attention is a gesture every mail client already has, and the operator asking a rule to make it is authoring exactly the kind of change this record permits.
- Bad, because the workaround it leaves is worse than the act it refuses. A rule that has to say something about a message and may only move it says it by moving it, so mail leaves the inbox to acquire a label.

### G3 — every system flag IMAP defines

- Good, because it needs no line drawn and no later reopening for a flag nobody has asked about yet.
- Bad, because `\Answered` and `\Draft` are assertions about acts rather than annotations on a message, and MailFathom writing one tells the owner's client that something happened which did not.
- Bad, because it widens the surface past the request, which is what this record's refusals exist to stop. Nothing asked for either flag, and a tier granted on the grounds that a narrower one was granted is the gap read as permission.

### H1 — replace with `STORE FLAGS`

- Good, because it is one command and it says exactly what the operation says: these are the message's keywords now.
- Bad, because that command replaces the whole flag set rather than the keywords in it. A rule labelling a message would clear `\Seen`, `\Flagged`, `\Answered`, and `\Draft` as a side effect — values another client set, which MailFathom may never have observed and therefore could not restore even if it tried.

### H3 — no replacement at all

- Good, because add and remove are the two commands IMAP has, and neither needs a read first.
- Bad, because clearing every keyword is then unsayable: a rule would have to name each keyword a message might carry, which is a list nobody has.
- Bad, because it pushes the difference into the operator's file. Saying *these and only these* is the intent, and expressing it as a removal list that has to be kept in step with the addition list is the same computation done by hand and left to rot.

### J1 — `APPEND` stays outside the closed set

- Good, because it keeps the set at mutations of mail the server already holds, which is the smallest surface and the easiest one to reason about.
- Bad, because it leaves a deployment that sends mail with no record of it anywhere its owner reads mail. SMTP files nothing, so the sent folder every mail client shows would stay empty however much MailFathom sent.
- Bad, because the workaround is worse than the act. An operator who wants their sent mail visible would have to send through a client instead, which is the feature being built read backwards.

### J2 — append into any folder a caller names

- Good, because it needs no role mapping and no stage, and one method serves whatever a later feature wants to put in a mailbox.
- Bad, because it makes the destination attacker-reachable in principle. A folder is then a value flowing from a caller into a message-creating command, and the whole of this record's refusal posture is that such surfaces are the ones to refuse.
- Bad, because it turns one act into a general capability nobody asked for. Nothing here needs to append arbitrary bytes anywhere, and a capability that exists is a capability a later change will reach for.

### K1 — the copy arrives with no flags

- Good, because it writes the fewest flags, and it never asserts anything about a message.
- Bad, because it is wrong in the owner's own client for both cases that have one. A sent copy with no `\Seen` shows their sent folder carrying unread mail they wrote themselves, and a mirrored message with no `\Draft` reads as an ordinary message in a folder nothing sends from.

### K3 — whatever the caller names

- Good, because it needs no decision here and leaves the caller free.
- Bad, because it separates two halves of one answer. What a folder means and what a copy in it looks like are the same fact, and splitting them is what lets a draft be filed as read.
- Bad, because it re-opens the flag question this record closed. Every flag IMAP defines would be writable through the append path, including the `\Answered` axis G refused, and by a route the flag tier's own review never covered.

## More Information

- Issue 446 asked the question and carries the decision comment this record is written from; issue 447 is the change that implements it; issue 452 is the feature all of it belongs to.
- Issue 713 reopened the record for folder creation and carries that request; issue 714 is the change that implements what axes E and F decided; issue 709 is the mapping contract whose refusal of a destination that resolves to nothing the creation lifts, and only where a mapping asked.
- Issue 917 reopened it for the flag tier and is the change that implements what axes G and H decided.
- Issue 864 reopened it for the caller-authored requester and is the change that implements what axis I decided; issue 862 is the read half that made the asymmetry visible, and [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) governs the permission the tool is reached under.
- Issue 739 reopened it for filing an outgoing message and is the change that implements what axes J and K decided; issue 738 is the delivery whose successful outcome the sent copy follows, and issue 714 is the folder creation a destination that does not exist falls back on.
- [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) governs `MailboxMutationUnsupported` (25001), which is category 2 subcategory 5 — a subcategory of its own because it says the opposite of an unavailable mailbox about repeating the work.
- RFC 9051 § 2.3.2 defines the flags and keywords a message carries, § 6.4.6 defines what `STORE FLAGS` replaces, and § 7.1 defines the `PERMANENTFLAGS` response and the `\*` that says arbitrary keywords persist.
- RFC 4315 defines `UIDPLUS`, the `APPENDUID` response that names where an append landed, and the `UID EXPUNGE` the mirror is withdrawn with. RFC 6154 defines the special-use attributes a server may advertise, and carries no outbox among them.
- Draft section 11.1 carries the amended invariant, and draft section 21.5 carries the action tiers this narrows.
- Revisit when a request arrives for one of the tiers that remain refused — sending, `\Answered`, `\Draft` on a message the mailbox already holds, or renaming, deleting, or unsubscribing a folder. Each reopens this record with its own authorization review, as folder creation did on issue 713, the flag tier on issue 917, and filing an outgoing message on issue 739, rather than being read out of a gap. A request to let a caller author one of the mutations axis I did not reach — a relocation, a copy, a delete — reopens it the same way, because axis I decided who may ask for the flag tier and nothing wider.
