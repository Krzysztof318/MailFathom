---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-02
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Let a person opening a message in the client mark it read on their own mail server, as a mutation their act authors rather than an effect of the read that served it

<!-- describes: frontend/src/Client.App/src/readingPane/**, frontend/src/Client.App/src/messageBody/**, frontend/src/Client.App/src/thread/**, frontend/src/Client.App/src/messageRows/**, backend/src/Application/Mail/Mutations/**, backend/src/Infrastructure/Persistence/Owners/** -->

## Context and Problem Statement

`backend/AGENTS.md` states the strongest guarantee this repository makes about somebody else's mailbox: *synchronization and content retrieval must never set the remote IMAP `\Seen` flag*. It was written about a service that reads a mailbox on nobody's behalf, and [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) has already scoped it to exactly that — retrieval — while permitting `\Seen` to be written in both directions by a rule, by the spam verdict, and by an MCP caller, each through a write session no read path can obtain.

A mail client is a different act from any of those three. A person opens a message, and every other mail client they have ever used marks it read on the server, so the next client they open agrees with this one. Nobody asked for that behaviour and nobody would describe it as a mutation; it is what opening mail *is*. But it is a `STORE` against somebody's mailbox all the same, issued because a pane rendered — which puts a write on the path of an ordinary read and makes opening a message the one change nothing confirms.

Both answers cost something real, and neither cost is hypothetical. Refusing the write means MailFathom's read state is local to this deployment for ever: mail the owner has read here still shows unread on their phone, which reads as a defect to the person and cannot be explained away by a design principle. Permitting it means the list of writers people have been told about — [the user guide](../users/README.md) names three, and each of them is a separate act somebody configured or a grant somebody made — gains a fourth, and the first that is a consequence of reading rather than an act beside it.

**This question was settled once and its record is gone.** ADR 0020 answered it on [#1139](https://github.com/Krzysztof318/MailFathom/issues/1139), and [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392) withdrew that record together with the Uno Platform client it was written about. What the withdrawal removed was a record whose mechanisms named a WebAssembly bundle's components; the question it answered survived it, and so did the two things it rested on, which were ADR 0007's write session and the client surface neither of them was Uno's. So this record retakes the answer for the client that actually ships rather than restoring a text, and it answers one question the old record never faced, because the screen that raises it did not exist then.

Nothing downstream can be built without the answer. [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475) renders read state on a row and performs whatever this settles on opening, and says so rather than choosing. [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) serves flag mutations to the client and says *nothing here sets `\Seen` implicitly, pending its own decision*. [#1207](https://github.com/Krzysztof318/MailFathom/issues/1207) carries both and names this record as the decision they wait on from outside the stage. Each of those clauses is a placeholder this record replaces.

The decision question is five questions an implementation would otherwise answer by accident: whether opening a message writes the flag at all, what the client shows as read state where it does not, what authors the write and when it reaches the mailbox, which messages a conversation that opens several at once marks read, and where the person's choice about it lives.

Recorded on issue [#1474](https://github.com/Krzysztof318/MailFathom/issues/1474). It implements nothing.

## Decision Drivers

- **A read must stay structurally incapable of writing.** ADR 0007 bought that as a property of the types rather than of anybody's care, and a client feature that reintroduces the write on the read path spends it.
- **A mail client that leaves read state behind is a mail client people keep another mail client beside.** Divergent unread counts are not a small blemish: they are the reason somebody stops using the application, which carries every guarantee in it out of the door with them.
- **The mailbox owner's own act is the authorship this write needs.** ADR 0007's whole driver for the permitted set is *a change the mailbox owner authored, carried by MailFathom on their behalf, undone in their own client with the gesture that would have made it*. A person opening their own mail is the plainest instance of that the record has met.
- **The implicit read and the deliberate flag must not become one mechanism with two entry points.** `mailSpace/MailToolbar.tsx` already draws *mark unread* beside seven other acts, and [#1207](https://github.com/Krzysztof318/MailFathom/issues/1207)'s autonomy scale confirms every one of them. Reading is the act that is not a triage decision, and what separates it has to be stated rather than left to which component fired.
- **A guarantee written down for users may not quietly acquire an exception.** The one this changes is published prose in the user guide, so the change is a change to what somebody was promised and is stated as one.
- **`\Seen` has one local writer and must keep having one.** [Stored email schema](../architecture/stored-email-schema.md#what-a-row-records) rests on the stored flag being an observation of what the server reports, never an instruction. A second, MailFathom-only read state would make the value a merge of two opinions.
- **Nothing about this may be decided by which head is running.** `frontend/src/AGENTS.md` § *The two heads* forbids a screen that has to know, and read state is exactly the kind of thing a per-device answer would fragment.
- **Mail content and who has read what are personal data.** What is stored about a person's reading, and for how long, is a design question rather than an implementation detail.

## Considered Options

**A — whether opening a message in the client sets the remote `\Seen` flag:**

- **A1** Never. The client writes no flag on open, and read state stays whatever the server reports.
- **A2** Always, for every owner and every account, with no way to turn it off.
- **A3** By default, and the owner may turn it off.
- **A4** Only where the owner has turned it on, off by default.

**B — what the client shows as read state where the flag is not written:**

- **B1** The remote flag and nothing else. MailFathom keeps no read state of its own.
- **B2** A local read state per owner, stored beside the remote flag and rendered in preference to it.

**C — what authors the write, and when it reaches the mailbox:**

- **C1** The content route sets the flag as it serves the body.
- **C2** The client submits an ordinary flag mutation, over the mutation surface, once the body it asked for has been drawn.
- **C3** As C2, but only after the message has been on screen for a period the client counts.
- **C4** As C2, but confirmed: the person is asked, per message, before the flag moves.

**D — which messages a conversation that opens several at once marks read:**

- **D1** Every message whose body the conversation drew, including the ones it expanded itself.
- **D2** Only the message the reader named or expanded with their own gesture.

**E — where the owner's choice lives:**

- **E1** A configuration key per mail account, written by the operator.
- **E2** One owner-level setting in the owner document, covering every account that owner reads.
- **E3** A client-local setting, held on each device the owner signs in from.

## Decision Outcome

Chosen options: **A3**, **B1**, **C2**, **D1**, and **E2**.

**Opening a message in the client marks it read on the owner's own mail server, by default, and the owner may turn that off.** The write is an ordinary flag mutation the person's act authors, submitted by the client over the mutation surface and carried to IMAP by the write session ADR 0007 already defines. The route that served the body sets nothing, and where the write does not happen the client shows what the server last reported and remembers no reading of its own.

### The act is the authorship, so nothing is confirmed

Opening a message is not a mutation somebody stumbles into. It is a deliberate gesture, aimed at one message the person picked, whose entire purpose is to read it — and marking read is what every mail client they have does with that gesture. ADR 0007 admits a change *the mailbox owner authored*, and every requester it has admitted so far acts for the owner in their absence — a rule they configured once, a classification they enabled, an agent holding a grant. This is the first where the owner is themselves present, acting on their own mail, in a surface only they sign into. There is nothing left to confirm, and a prompt asking whether opening a message counts as having opened it would be the clearest possible statement that the application does not understand what it is.

**That is what distinguishes this from the control drawn beside it.** `mailSpace/MailToolbar.tsx` draws *mark unread*, and [#1476](https://github.com/Krzysztof318/MailFathom/issues/1476) will make it act; [#1207](https://github.com/Krzysztof318/MailFathom/issues/1207)'s autonomy scale puts every reversible mailbox mutation behind a confirmation, and that control is one. The two are not one mechanism with two entry points, and the line between them is **who chose the value**. Pressing *mark unread* is a triage decision: the person is saying something about a message that is untrue of what they just did with it, and the confirmation states which message and what will change. Opening a message decides nothing — the value follows from the act rather than being chosen beside it, and there is no second thing the person could have meant. They share the mutation, the record, the route, and the convergence, because they are the same change to the same flag; what they do not share is that one of them is an assertion and the other is a consequence.

That is also why this is not the exception to [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md)'s confirmation discipline that it might look like. That record governs mail *leaving* — an act that reaches a stranger and cannot be recalled. A `\Seen` change reaches nobody but the owner, is visible to them in every client they own, and is undone by the same gesture in any of them.

### The write is a mutation, and never an effect of the read

**The content route stays incapable of writing.** The message body is served by the client API's own read path, which holds no write factory and reaches no mail server at all — `GET /api/client/messages/{id}` and `GET /api/client/messages/{id}/body` are built that way and stay built that way. What marks the message read is a separate request the client makes, over the flag mutation surface [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) serves, carrying the same desired `\Seen` state a rule or an MCP caller carries. Nothing new is built to reach the mailbox: the mutation record, its convergence, its idempotence on the wire, and its audit entry are all ADR 0007's, and this record adds a requester rather than a mechanism.

So the contract sentence stays literally true of every path it names. What it needed was not a correction but the missing half — that the author may also be a person opening their own mail — and [the sentence is amended to carry it](#the-contract-sentence-names-the-fourth-author) so that the next agent building a reading pane does not read the guarantee as a refusal of this decision.

**The trigger is the body having been drawn, not the selection having moved.** A client whose reading pane follows list selection would otherwise mark fifty messages read for one press-and-hold of the arrow key, which is the failure every mail client with a preview pane has and answers with a dwell timer. This one answers it without a timer: `messageBody/Message.tsx` requests the body, a selection that moves on discards that read rather than drawing it, and a message whose body was never drawn was never opened. The round trip is the dwell, and unlike a threshold it is not a number somebody has to defend. **C3 is refused for that reason** — *read after two seconds* is a contract nobody can explain to a person and no test can assert without asserting the clock.

**Submission is immediate and delivery is not, which is the shape the mutation surface already has.** The client submits as soon as the body is drawn; the record is durable before it is acknowledged; the row is drawn as read at once from the pending mutation rather than from a second stored value; and convergence happens when the account's write connection next runs. A person reading twenty messages on a slow account produces twenty records that converge in their own time, and an account that is unreachable produces pending mutations, which [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) already treats as a normal condition rather than a failure.

**What may be marked is what may be read**, which is ADR 0007's rule rather than a second one: the same scope answers both, so an account this deployment no longer serves and a folder withheld from the signed-in owner are mail the client can no more mark than open. A write surface reaching past what the read surface withholds would be the way round the withholding rather than a capability of its own.

### A conversation marks read what it drew, which is at most three messages

This is the question ADR 0020 never had to answer, and the screen that raises it is `thread/Thread.tsx`. A conversation does not draw one body: `thread/threadOpening.ts` opens it at the message somebody arrived at where they named one, else at the most recent of what they have not read, and it opens at most three. So a reader who opens a conversation from a search result or from a restored workspace, having named no message, gets three bodies on the screen that they expanded with no gesture of their own.

**Every body the conversation drew is marked read (D1)**, and the reason is that the trigger stays one rule. What marks a message read is that its body was drawn, wherever it was drawn — the reading pane and a thread row mount the same `messageBody/Message.tsx` and put the same words in front of the same person. D2 would make the trigger depend on *why* a component mounted rather than on what the reader is looking at, which is the distinction that produces two mechanisms where the section above has just argued for one, and it is not one a person could state: they would be told that the three messages they just read are read, unread, and unread, according to which of them the screen chose to open.

It is also bounded rather than open. `mostOpenedAtOnce` is three, and it is three because each open message is a body request — so the worst this produces is three records for one conversation opened, and the case where all three were expanded by the screen is the case where all three were unread and catching up on them is what opening the conversation was. Whether a conversation opens at three messages or at one is that screen's decision and is revisited there; what this record fixes is that the flag follows what was drawn.

**The markings a conversation produces travel as one batch**, which is the shape [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208)'s route already has for the reason a person flagging four messages at once has it. That is a transport property rather than a second decision: each message still gets its own durable record and converges on its own, and a body a reader expands later is its own submission because it happened later. Nothing waits for a body that has not been drawn in order to send with one that has.

The consequence is stated rather than hidden: a reader who opens such a conversation, reads the last message, and leaves has marked three messages read. Two of them were on their screen and they scrolled past them, which is what reading a conversation is; the recovery is *mark unread* on the row, in this client or in any other they own.

### Where the flag is not written, there is no second read state

**MailFathom stores no reading of its own.** An owner who turns the setting off gets a client that shows what their mail server last reported — the same `unread` the message row already draws, whose meaning is *whether the mail server last reported the message without `\Seen`* — and nothing else. That is the honest reading of the setting: off means MailFathom does not track that you read it, anywhere, rather than that it tracks it somewhere only this application can see.

**B2 is refused because it breaks the one property the flag snapshot rests on.** [Stored email schema](../architecture/stored-email-schema.md#what-a-row-records) says each remote flag column has exactly one writer, which is synchronization observing what the server was seen to hold; a local read state beside it would make every list render a merge of two opinions, and the merge would have to be resolved somewhere for the *only unread* filter, for the folder counts, and for search. It would also produce precisely the divergence the person turned the setting off to avoid, except invisible: a message that shows read here and unread everywhere else, with nothing on the server to explain it.

**And it would be personal data with no purpose left to justify it.** A per-owner record of which message was read at which moment is a behavioural profile of somebody's correspondence, retained for as long as the mail is. Where the flag *is* written, the mailbox already holds that fact and MailFathom stores none of it; where it is not, storing it would be collecting something the owner has just declined.

### The choice is the owner's, and it is one value

**It is one owner-level setting, held as typed content of the owner settings document.** [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) settled that an owner's settings are typed content of that document rather than a configuration layer, and this is an owner's setting in the plainest sense: it describes how *this person* reads mail, in a surface only they sign into, across every account they own. It is not an operator's key (E1), because an operator deciding whether somebody's mail client marks mail read is deciding something about that person's reading rather than about the deployment. It is not a device setting (E3), because read state is exactly what must not fragment per head: a phone that marks read and a desktop that does not is the divergence problem reintroduced inside one product, and `frontend/src/AGENTS.md` forbids the branch that would express it anyway.

**It covers every account that owner reads, deliberately.** A per-account switch would be right if the accounts differed in kind, and the case where they do is a shared mailbox whose unread state is how a team tracks work. That case is thin here — [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) hangs ownership on the mail account and a shared mailbox is one owner's account like any other — and it is the stated criterion for revisiting: an operator serving a mailbox where unread is somebody else's state is the report that moves this value onto the account.

**The setting governs opening, and nothing else.** An owner who turns it off keeps every deliberate marking the client offers — marking a message read without opening it, and marking one unread again — because what they declined is reading being a write, not the ability to say a message is read. Reading it as a switch over the whole capability would leave somebody unable to clear an unread count they can clear from any other client they own.

**The default is on, for the reason ADR 0007 gave the reversible actions.** That record defaults `MarkAsFlagged` and `WriteKeywords` to `true` because they are undone from any mail client, and keeps `Delete` the one action every account opts into. Marking read is in the first group by every measure: it destroys nothing, displaces nothing, and is undone with one gesture in any client the owner has. A default of off (**A4**) would additionally make the application wrong out of the box in the way people notice fastest, which is the opposite of the easy first run this project asks of its defaults.

### What an operator can still withhold, and what they cannot

**The lever is the permission, not a new key.** `mailfathom.mail.flags.write` is the grant under [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) that reaches the owner's mail server, and [it does not follow from `mailfathom.mail.read`](../operations/permissions.md#the-published-set). A client credential granted reading and not that name signs in, reads mail, and marks nothing: the client discovers this from the `permissions` list [the sign-in route already returns](../operations/client-endpoint.md), offers no marking, and shows the remote flag alone — which is the same behaviour as the owner having turned the setting off, reached from the other side. So a deployment that wants a client incapable of touching a mailbox has that today and needs nothing added.

**`Deployment:ReadOnly` is not that lever, and this record does not make it one.** [What it reaches is sending](../operations/configuration-runtime.md#deployment) — mail leaving this installation for somebody else's mailbox — and changes to a mailbox this deployment reads are governed by the account's own rule action permissions and by the grant a caller holds. Saying so is part of the deliverable: an operator reading the name would reasonably assume otherwise, and would be wrong.

**`RuleActions:MarkAsRead` does not gate it either**, for the reason ADR 0007 gives about the MCP tool: `RuleActions` is where an operator says which changes a *rule* may ask for on an account, validated against the rules that were declared, and a person opening their own mail is not a rule.

### The contract sentence names the fourth author

`backend/AGENTS.md` keeps its guarantee and gains the author it was missing. *Synchronization and content retrieval must never set the remote IMAP `\Seen` flag* is exactly as true after this decision as before it — no read path acquires a write, and the route that serves a body still reaches no mail server. What the sentence listed was three authors, all of them acting for the owner in their absence; the fourth is the owner themselves, present, opening their own mail in the client. The bullet now names it and points here.

The user-facing pages are a separate matter and are **not** changed here. [The user guide](../users/README.md) and [what each mailbox provider needs](../users/mailbox-providers.md) describe verified behaviour, and the behaviour they describe is still what a deployment does today: nothing marks mail read on open until [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475) builds it. They are rewritten by the change that makes them wrong, which is that issue, and rewriting them now would be documenting intent.

### Consequences

- Good, because the client agrees with every other mail client the owner uses, which is the one thing about read state a person notices and the one thing they cannot be argued out of.
- Good, because the write reuses ADR 0007's mutation record whole — durability, convergence, idempotence, audit, and the per-account write connection — so this adds a requester and no mechanism.
- Good, because the read path keeps the property it was given: the content route holds no write factory, and a defect in the reading pane cannot become a defect that writes to somebody's mailbox.
- Good, because the implicit read and the *mark unread* control beside it are separated by a stated rule rather than by which component fired, so the confirmation [#1207](https://github.com/Krzysztof318/MailFathom/issues/1207) requires of a triage decision is not argued again for every act that shares the mutation.
- Good, because turning the setting off produces a client with no read state of its own rather than one with a private read state, so `\Seen` keeps exactly one local writer and the *only unread* filter, the counts, and search all keep answering from one value.
- Neutral, because a person is now writing to their mailbox as a consequence of reading it, which is a real widening of what the client does and is stated as one rather than folded into a feature.
- Neutral, because the owner's choice is one value across their accounts, which is right until somebody serves a shared mailbox in this client and is the stated trigger for moving it.
- Bad, because the list of writers the user guide names gains a fourth. *Fetching mail never sets the remote `\Seen` flag* stays true; *MailFathom never marks your mail read* stops being a fair summary of the product, and the changelog entry for the release that ships [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475) has to say so against the deployment contract.
- Bad, because a mistake in the pane is now a mistake in somebody's mailbox. Opening the wrong message marks the wrong message read, on the server, for every client they own — reversible, and still an effect that used to be impossible.
- Bad, because a conversation opened at no particular message marks up to three read for one gesture, two of which the reader may not have looked at. It is bounded and reversible, and it is the price of the trigger being one rule rather than two.
- Bad, because a list filtered to unread mail loses the row being read the moment the mutation is submitted, so the reading pane and the list disagree about what is in the list. That is a screen problem [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475) has to answer rather than a reason to refuse the write, and it is named here so it is answered deliberately.
- Bad, because a marking submitted against an unreachable account is a pending mutation the person did not know they authored, and may see reported as pending work. The alternative is not submitting it, which is A1.

## Validation

- Review of [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475) and [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) against this record, since both currently carry a clause deferring to it and both must lose that clause rather than keep it beside a contradicting implementation.
- The client API's read routes are proven not to reach a write session by the type separation ADR 0007 established, which is a compile-time property rather than a test: the content route's dependencies contain no write factory.
- The existing `\Seen` regression test in the integration suite — the one requiring the flag to be untouched by a relocation, a delete, and a copy, with a real `\Seen` change afterwards as its control — is what proves the read paths still set nothing once the client can.
- A unit test over the reading pane asserts that a message whose body read was discarded submits no mutation, and that a body drawn submits exactly one, keyed to the message shown.
- A unit test over the conversation asserts that a conversation drawn at three expanded messages submits three markings and no more, and that a message expanded later submits its own.
- A unit test over the owner setting asserts that the default is on for an owner who has written nothing, and that turning it off leaves the client rendering the remote flag with no second state consulted.
- `scripts/review-obligations.sh` and `Fathom review` read the `describes:` marker above, so a change under the paths it names is told this record covers it.

## Pros and Cons of the Options

### A1 — never; the client writes no flag on open

- Good, because the guarantee stays the one sentence anybody can check, with no requester to reason about and no setting to get wrong.
- Good, because nothing a person does in the client can affect what another client shows, which is the strongest form of *reading changes nothing*.
- Bad, because MailFathom's read state then differs from every other client's for ever, and the difference grows with use. A person who reads mail here and triages it on their phone sees a mailbox that never gets read.
- Bad, because it is not neutral about what the person does next: unable to mark read here, they mark read somewhere else, and the reading they wanted to do in this application happens in one that offers fewer guarantees about it.

### A2 — always, with no way to turn it off

- Good, because there is no setting, no owner document field, and no branch in the reading pane.
- Bad, because the one deployment class that most obviously wants MailFathom — an archival or compliance reader over a mailbox that must be left exactly as found — is told the client is not for them, over a behaviour that is one boolean.
- Bad, because it removes the owner from a decision about their own mailbox, which is the opposite of what every other permitted mutation in ADR 0007 does.

### A4 — off by default, the owner may turn it on

- Good, because no existing deployment's behaviour changes on upgrade without somebody choosing it.
- Bad, because it makes the application wrong in the way people notice within one session, and the fix is a setting they have to know exists. A default nobody would choose is a default that mostly measures who found the switch.
- Bad, because it is inconsistent with ADR 0007's own rule for reversible actions, which defaults them on and reserves the opt-in for `Delete`.

### B2 — a local read state stored beside the remote flag

- Good, because the owner who turned the write off still gets a client that remembers what they have read, which is what they would probably say they wanted.
- Neutral, because the storage itself is small — a row per message per owner, or a bit on the row.
- Bad, because it gives `\Seen` a second opinion, and every reader of the flag — the list, the *only unread* filter, the folder counts, search, rules — then has to be told which opinion it is reading, forever.
- Bad, because it produces exactly the divergence the setting was turned off to avoid, in the least explicable form: read here, unread everywhere, and nothing on the server that accounts for it.
- Bad, because it stores a behavioural record of somebody's reading that the mailbox is not holding, retained as long as the mail — collected at the moment the owner declined to have it written down anywhere.

### C1 — the content route sets the flag as it serves the body

- Good, because it is one request instead of two, and the flag can never be out of step with what was shown.
- Bad, because it puts a write on the read path, which is the property ADR 0007 spent a session type to buy. Every read route would then be a route that must be checked for whether it writes.
- Bad, because the write becomes unavoidable rather than a choice: an owner who turned marking off would need the read route to branch on their setting, so the read path would carry the setting as well as the write.
- Bad, because it makes the MCP surface's `get_email_content` and the client's body route two different acts under one use case, or forces them apart.

### C3 — a dwell period the client counts

- Good, because it is what most mail clients with a preview pane do, and it is what people expect from those clients.
- Neutral, because it would sit on top of C2 rather than replacing it.
- Bad, because the contract becomes *read after N seconds*, which is a number with no principled value, differs per client, and cannot be asserted in a test without asserting a clock.
- Bad, because it is a timer bought to fix a problem the body request already fixes: a selection that moves on discards the read, so a message whose body was never drawn was never opened.

### C4 — confirmed, per message

- Good, because no write ever happens without the person having said so in that instant.
- Bad, because it asks somebody whether opening a message counts as opening it, once per message, forever. Nobody would keep the client.
- Bad, because it misapplies ADR 0013's confirmation discipline, which exists for acts that reach a stranger and cannot be recalled. This one reaches the owner alone and is undone with one gesture.

### D2 — only the message the reader expanded themselves

- Good, because no flag moves on a message the reader did not point at, which is the narrowest possible reading of *their act authored it*.
- Neutral, because it changes nothing for the common case: a conversation opened from a message row opens at that message alone, and the two options agree there.
- Bad, because the trigger stops being *the body was drawn* and becomes *the body was drawn for a reason of this kind*, so one screen has two markings rules and a reader is told that three messages they just read are read, unread, and unread.
- Bad, because a conversation somebody read to the bottom stays unread, which is A1's defect reproduced inside the one screen where catching up is the whole purpose.

### E1 — a configuration key per mail account

- Good, because it sits beside `RuleActions`, which is where an operator already says what may be written on an account.
- Bad, because `RuleActions` is a statement about *rules*, and ADR 0007 was explicit that a requester which is not a rule is not gated by it. Adding a person to that block would make the block mean two things.
- Bad, because it puts an operator between a person and how their own mail client behaves, over a setting with no deployment-wide consequence. The operator's genuine lever — withholding the permission — already exists.

### E3 — a client-local setting per device

- Good, because it needs no schema, no owner document field, and no round trip.
- Bad, because read state is the one thing that must not differ per device, and this guarantees that it can. A phone that marks read and a desktop that does not is the divergence problem rebuilt inside one product.
- Bad, because it is lost with the device and invisible to the owner from anywhere else, so *why is my mail being marked read* has an answer only on the machine causing it.

## More Information

- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) is the record this one adds a requester to: the write session, the closed set of mutations, the durable mutation record, and the rule that `\Seen` may move in both directions all come from it, and none of them is reopened here. This record adds no mutation and removes none.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) is why `mailfathom.mail.flags.write` is the operator's lever and why it does not follow from reading mail.
- [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) is why an owner's setting is typed content of the owner settings document rather than a configuration layer.
- [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) settles what the reading pane renders; this record settles what opening it does. The two meet in `messageBody/Message.tsx`, which performs the body read this record's trigger reads, and are otherwise independent.
- ADR 0020 held this answer before [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392) withdrew it with the Uno Platform client. Its number is not reused and its text is not restored: what stands is this record.
- The issues that wait on this one are [#1475](https://github.com/Krzysztof318/MailFathom/issues/1475), which renders read state and performs the marking, and [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208), whose `\Seen` clause is written against it. [#1207](https://github.com/Krzysztof318/MailFathom/issues/1207) is the stage both belong to, and [#1476](https://github.com/Krzysztof318/MailFathom/issues/1476) owns the *mark unread* control this record separates the implicit read from.
- Flags other than `\Seen` are out of scope and stay where ADR 0007 put them: `\Flagged` and keywords are deliberate, visible acts the person performs, and this record does not make any of them a consequence of reading.
- Revisit this record if a deployment serves a mailbox in the client whose unread state belongs to somebody other than the owner, which is the case that moves the setting from the owner onto the account.
