---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-28
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Let a person opening a message in the client mark it read on their own mail server, as a mutation their act authors rather than an effect of the read that served it

<!-- describes: backend/src/Application/Mail/Mutations/**, backend/src/Host/Api/Client*.cs, backend/src/Infrastructure/Persistence/Owners/**, frontend/src/Client/Presentation/Messages/**, frontend/src/Client/Presentation/Spaces/Mail/** -->

## Context and Problem Statement

Root `AGENTS.md` states the strongest guarantee this repository makes about somebody else's mailbox: *synchronization and content retrieval must never set the remote IMAP `\Seen` flag*. It was written about a service that reads a mailbox on nobody's behalf, and [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) has already scoped it to exactly that — retrieval — while permitting `\Seen` to be written in both directions by a rule, by the spam verdict, and by an MCP caller, each through a write session no read path can obtain.

A mail client is a different act from any of those three. A person opens a message, and every other mail client they have ever used marks it read on the server, so the next client they open agrees with this one. Nobody asked for that behaviour and nobody would describe it as a mutation; it is what opening mail *is*. But it is a `STORE` against somebody's mailbox all the same, issued because a pane rendered — which puts a write on the path of an ordinary read and makes opening a message the one change nothing confirms.

Both answers cost something real, and neither cost is hypothetical. Refusing the write means MailFathom's read state is local to this deployment for ever: mail the owner has read here still shows unread on their phone, which reads as a defect to the person and cannot be explained away by a design principle. Permitting it means the list of writers people have been told about — [`docs/users/README.md`](../users/README.md) names three, and each of them is a separate act somebody configured or a grant somebody made — gains a fourth, and the first that is a consequence of reading rather than an act beside it.

Nothing downstream can be built without the answer. [#1156](https://github.com/Krzysztof318/MailFathom/issues/1156) serves a message's content and says *nothing here sets `\Seen`*, pending this. [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) composes the reading pane and says *opening a message sets no remote flag, pending the decision that settles whether it ever should*. [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) serves flag mutations to the client and says *nothing here sets `\Seen` implicitly*. Each of those clauses is a placeholder this record replaces.

The decision question is four questions an implementation would otherwise answer by accident: whether opening a message writes the flag at all, what the client shows as read state where it does not, what authors the write and when it reaches the mailbox, and where the person's choice about it lives.

Recorded on issue [#1139](https://github.com/Krzysztof318/MailFathom/issues/1139). It implements nothing: [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) builds the trigger, [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) builds the route it goes over, and [#1156](https://github.com/Krzysztof318/MailFathom/issues/1156) is what keeps the content route out of it.

## Decision Drivers

- **A read must stay structurally incapable of writing.** ADR 0007 bought that as a property of the types rather than of anybody's care, and a client feature that reintroduces the write on the read path spends it.
- **A mail client that leaves read state behind is a mail client people keep another mail client beside.** Divergent unread counts are not a small blemish: they are the reason somebody stops using the application, which carries every guarantee in it out of the door with them.
- **The mailbox owner's own act is the authorship this write needs.** ADR 0007's whole driver for the permitted set is *a change the mailbox owner authored, carried by MailFathom on their behalf, undone in their own client with the gesture that would have made it*. A person opening their own mail is the plainest instance of that the record has met.
- **A guarantee written down for users may not quietly acquire an exception.** The one this changes is published prose in the user guide, so the change is a change to what somebody was promised and is stated as one.
- **`\Seen` has one local writer and must keep having one.** [Stored email schema](../architecture/stored-email-schema.md#what-a-row-records) rests on the stored flag being an observation of what the server reports, never an instruction. A second, MailFathom-only read state would make the value a merge of two opinions.
- **Nothing about this may be decided by which head is running.** `frontend/src/AGENTS.md` forbids assuming the mobile heads never open, and read state is exactly the kind of thing a per-device answer would fragment.
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

**D — where the owner's choice lives:**

- **D1** A configuration key per mail account, written by the operator.
- **D2** One owner-level setting in the owner document, covering every account that owner reads.
- **D3** A client-local setting, held on each device the owner signs in from.

## Decision Outcome

Chosen options: **A3**, **B1**, **C2**, and **D2**.

**Opening a message in the client marks it read on the owner's own mail server, by default, and the owner may turn that off.** The write is an ordinary flag mutation the person's act authors, submitted by the client over the mutation surface and carried to IMAP by the write session ADR 0007 already defines. The route that served the body sets nothing, and where the write does not happen the client shows what the server last reported and remembers no reading of its own.

### The act is the authorship, so nothing is confirmed

Opening a message is not a mutation somebody stumbles into. It is a deliberate gesture, aimed at one message the person picked, whose entire purpose is to read it — and marking read is what every mail client they have does with that gesture. ADR 0007 admits a change *the mailbox owner authored*, and every requester it has admitted so far acts for the owner in their absence — a rule they configured once, a classification they enabled, an agent holding a grant. This is the first where the owner is themselves present, acting on their own mail, in a surface only they sign into. There is nothing left to confirm, and a prompt asking whether opening a message counts as having opened it would be the clearest possible statement that the application does not understand what it is.

That is also why this is not the exception to [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md)'s confirmation discipline that it might look like. That record governs mail *leaving* — an act that reaches a stranger and cannot be recalled. A `\Seen` change reaches nobody but the owner, is visible to them in every client they own, and is undone by the same gesture in any of them.

### The write is a mutation, and never an effect of the read

**The content route stays incapable of writing.** The message body is served by the client API's own read path, which holds no write factory and reaches no mail server at all — [#1156](https://github.com/Krzysztof318/MailFathom/issues/1156) is built that way and stays built that way. What marks the message read is a separate request the client makes, over the flag mutation surface [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) serves, carrying the same `DesiredSeenState` a rule or an MCP caller carries. Nothing new is built to reach the mailbox: the mutation record, its convergence, its idempotence on the wire, and its audit entry are all ADR 0007's, and this record adds a requester rather than a mechanism.

So root `AGENTS.md`'s sentence stays literally true of every path it names. What it needed was not a correction but the missing half — where the write that *is* permitted lives — and [the sentence is amended to carry it](#the-contract-sentence-says-where-the-write-lives) so that the next agent building a reading pane does not read the guarantee as a refusal of this decision.

**The trigger is the body having been shown, not the selection having moved.** A client whose reading pane follows list selection would otherwise mark fifty messages read for one press-and-hold of the arrow key, which is the failure every mail client with a preview pane has and answers with a dwell timer. This one answers it without a timer: the pane requests the body, a selection that moves on cancels that request, and a message whose body was never drawn was never opened. The round trip is the dwell, and unlike a threshold it is not a number somebody has to defend. **C3 is refused for that reason** — *read after two seconds* is a contract nobody can explain to a person and no test can assert without asserting the clock.

**Submission is immediate and delivery is not, which is the shape the mutation surface already has.** The client submits as soon as the body is drawn; the record is durable before it is acknowledged; the row is drawn as read at once from the pending mutation rather than from a second stored value; and convergence happens when the account's write connection next runs. A person reading twenty messages on a slow account produces twenty records that converge in their own time, and an account that is unreachable produces pending mutations, which [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) already treats as a normal condition rather than a failure.

**Marking read is offered in both directions, and so is marking read without opening.** Both are the same mutation with a different `DesiredSeenState`, both already exist, and a decision that made opening write the flag while leaving the person no way to undo it in the application that wrote it would be the defect this record exists to avoid.

**What may be marked is what may be read**, which is ADR 0007's rule rather than a second one: the same scope answers both, so an account this deployment no longer serves and a folder withheld from the signed-in owner are mail the client can no more mark than open. A write surface reaching past what the read surface withholds would be the way round the withholding rather than a capability of its own.

### Where the flag is not written, there is no second read state

**MailFathom stores no reading of its own.** An owner who turns the setting off gets a client that shows what their mail server last reported — the same `IsUnread` [`MessageRow`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/src/Client/Presentation/Messages/MessageRow.cs) already draws, whose documented meaning is *whether the mail server last reported the message without `\Seen`* — and nothing else. That is the honest reading of the setting: off means MailFathom does not track that you read it, anywhere, rather than that it tracks it somewhere only this application can see.

**B2 is refused because it breaks the one property the flag snapshot rests on.** [Stored email schema](../architecture/stored-email-schema.md#what-a-row-records) says each remote flag column has exactly one writer, which is synchronization observing what the server was seen to hold; a local read state beside it would make every list render a merge of two opinions, and the merge would have to be resolved somewhere for filters like *unread only*, for counts, and for search. It would also produce precisely the divergence the person turned the setting off to avoid, except invisible: a message that shows read here and unread everywhere else, with nothing on the server to explain it.

**And it would be personal data with no purpose left to justify it.** A per-owner record of which message was read at which moment is a behavioural profile of somebody's correspondence, retained for as long as the mail is. Where the flag *is* written, the mailbox already holds that fact and MailFathom stores none of it; where it is not, storing it would be collecting something the owner has just declined.

### The choice is the owner's, and it is one value

**It is one owner-level setting, held as typed content of the owner document.** [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) settled that an owner's settings are typed content of that document rather than a configuration layer, and this is an owner's setting in the plainest sense: it describes how *this person* reads mail, in a surface only they sign into, across every account they own. It is not an operator's key (D1), because an operator deciding whether somebody's mail client marks mail read is deciding something about that person's reading rather than about the deployment. It is not a device setting (D3), because read state is exactly what must not fragment per head: a phone that marks read and a desktop that does not is the divergence problem reintroduced inside one product.

**It covers every account that owner reads, deliberately.** A per-account switch would be right if the accounts differed in kind, and the case where they do is a shared mailbox whose unread state is how a team tracks work. That case is thin here — [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) hangs ownership on the mail account and a shared mailbox is one owner's account like any other — and it is the stated criterion for revisiting: an operator serving a mailbox where unread is somebody else's state is the report that moves this value onto the account.

**The setting governs opening, and nothing else.** An owner who turns it off keeps every deliberate marking the client offers — marking a message read without opening it, and marking one unread again — because what they declined is reading being a write, not the ability to say a message is read. Reading it as a switch over the whole capability would leave somebody unable to clear an unread count they can clear from any other client they own.

**The default is on, for the reason ADR 0007 gave the reversible actions.** That record defaults `MarkAsFlagged` and `WriteKeywords` to `true` because they are undone from any mail client, and keeps `Delete` the one action every account opts into. Marking read is in the first group by every measure: it destroys nothing, displaces nothing, and is undone with one gesture in any client the owner has. A default of off (**A4**) would additionally make the application wrong out of the box in the way people notice fastest, which is the opposite of the easy first run this project asks of its defaults.

### What an operator can still withhold, and what they cannot

**The lever is the permission, not a new key.** `mailfathom.mail.flags.write` is the grant under [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) that reaches the owner's mail server, and [it does not follow from `mailfathom.mail.read`](../operations/permissions.md#the-published-set). A client credential granted reading and not that name signs in, reads mail, and marks nothing: the client discovers this from the `permissions` list [the sign-in route already returns](../operations/client-endpoint.md), offers no marking, and shows the remote flag alone — which is the same behaviour as the owner having turned the setting off, reached from the other side. So a deployment that wants a client incapable of touching a mailbox has that today and needs nothing added.

**`Deployment:ReadOnly` is not that lever, and this record does not make it one.** [What it reaches is sending](../operations/configuration-runtime.md#deployment) — mail leaving this installation for somebody else's mailbox — and changes to a mailbox this deployment reads are governed by the account's own rule action permissions and by the grant a caller holds. Saying so is part of the deliverable: an operator reading the name would reasonably assume otherwise, and would be wrong.

**`RuleActions:MarkAsRead` does not gate it either**, for the reason ADR 0007 gives about the MCP tool: `RuleActions` is where an operator says which changes a *rule* may ask for on an account, validated against the rules that were declared, and a person opening their own mail is not a rule.

### The contract sentence says where the write lives

Root `AGENTS.md` keeps its guarantee and gains the half it was missing. *Synchronization and content retrieval must never set the remote IMAP `\Seen` flag* is exactly as true after this decision as before it — no read path acquires a write, and the route that serves a body still reaches no mail server. What it did not say is that a change the owner authored may reach the flag through a session no read path can obtain, which is what ADR 0007 decided and what an agent reading only the contract would take the sentence to refuse. The bullet now names that, and names this record as the case where the author is a person opening their own mail.

The user-facing pages are a separate matter and are **not** changed here. [`docs/users/README.md`](../users/README.md) and [`docs/users/mailbox-providers.md`](../users/mailbox-providers.md) describe verified behaviour, and the behaviour they describe is still what a deployment does today: nothing marks mail read on open until [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) builds it. They are rewritten by the change that makes them wrong, which is that issue, and rewriting them now would be documenting intent.

### Consequences

- Good, because the client agrees with every other mail client the owner uses, which is the one thing about read state a person notices and the one thing they cannot be argued out of.
- Good, because the write reuses ADR 0007's mutation record whole — durability, convergence, idempotence, audit, and the per-account write connection — so this adds a requester and no mechanism.
- Good, because the read path keeps the property it was given: the content route holds no write factory, and a defect in the reading pane cannot become a defect that writes to somebody's mailbox.
- Good, because turning the setting off produces a client with no read state of its own rather than one with a private read state, so `\Seen` keeps exactly one local writer and the *unread only* filter, the counts, and search all keep answering from one value.
- Neutral, because a person is now writing to their mailbox as a consequence of reading it, which is a real widening of what the client does and is stated as one rather than folded into a feature.
- Neutral, because the owner's choice is one value across their accounts, which is right until somebody serves a shared mailbox in this client and is the stated trigger for moving it.
- Bad, because the list of writers the user guide names gains a fourth. *Fetching mail never sets the remote `\Seen` flag* stays true; *MailFathom never marks your mail read* stops being a fair summary of the product, and the changelog entry for the release that ships [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) has to say so against the deployment contract.
- Bad, because a mistake in the pane is now a mistake in somebody's mailbox. Opening the wrong message marks the wrong message read, on the server, for every client they own — reversible, and still an effect that used to be impossible.
- Bad, because a list filtered to unread mail loses the row being read the moment the mutation is submitted, so the reading pane and the list disagree about what is in the list. That is a screen problem [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) has to answer rather than a reason to refuse the write, and it is named here so it is answered deliberately.
- Bad, because a marking submitted against an unreachable account is a pending mutation the person did not know they authored, and may see reported as pending work. The alternative is not submitting it, which is A1.

## Validation

- Review of [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) and [#1208](https://github.com/Krzysztof318/MailFathom/issues/1208) against this record, since both currently carry a clause deferring to it and both must lose that clause rather than keep it beside a contradicting implementation.
- The client API's read routes are proven not to reach a write session by the type separation ADR 0007 established, which is a compile-time property rather than a test: the content route's dependencies contain no write factory.
- The existing `\Seen` regression test in the integration suite — the one requiring the flag to be untouched by a relocation, a delete, and a copy, with a real `\Seen` change afterwards as its control — is what proves the read paths still set nothing once the client can.
- A unit test over the reading pane asserts that a selection whose body request was cancelled submits no mutation, and that a body drawn submits exactly one, keyed to the message shown.
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
- Bad, because it gives `\Seen` a second opinion, and every reader of the flag — the list, the *unread only* filter, counts, search, rules — then has to be told which opinion it is reading, forever.
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
- Bad, because it is a timer bought to fix a problem the body request already fixes: a selection that moves on cancels the request, so a message whose body never arrived was never opened.

### C4 — confirmed, per message

- Good, because no write ever happens without the person having said so in that instant.
- Bad, because it asks somebody whether opening a message counts as opening it, once per message, forever. Nobody would keep the client.
- Bad, because it misapplies ADR 0013's confirmation discipline, which exists for acts that reach a stranger and cannot be recalled. This one reaches the owner alone and is undone with one gesture.

### D1 — a configuration key per mail account

- Good, because it sits beside `RuleActions`, which is where an operator already says what may be written on an account.
- Bad, because `RuleActions` is a statement about *rules*, and ADR 0007 was explicit that a requester which is not a rule is not gated by it. Adding a person to that block would make the block mean two things.
- Bad, because it puts an operator between a person and how their own mail client behaves, over a setting with no deployment-wide consequence. The operator's genuine lever — withholding the permission — already exists.

### D3 — a client-local setting per device

- Good, because it needs no schema, no owner document field, and no round trip.
- Bad, because read state is the one thing that must not differ per device, and this guarantees that it can. A phone that marks read and a desktop that does not is the divergence problem rebuilt inside one product.
- Bad, because it is lost with the device and invisible to the owner from anywhere else, so *why is my mail being marked read* has an answer only on the machine causing it.

## More Information

- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) is the record this one adds a requester to: the write session, the closed set of mutations, the durable mutation record, and the rule that `\Seen` may move in both directions all come from it, and none of them is reopened here. This record adds no mutation and removes none.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) is why `mailfathom.mail.flags.write` is the operator's lever and why it does not follow from reading mail.
- [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) is why an owner's setting is typed content of the owner document rather than a configuration layer, and why issue [#1118](https://github.com/Krzysztof318/MailFathom/issues/1118) was refused.
- [ADR 0019](0019-rendering-mail-html-in-the-client.md) settles what the reading pane renders; this record settles what opening it does. The two meet in [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) and are otherwise independent.
- Flags other than `\Seen` are out of scope and stay where ADR 0007 put them: `\Flagged` and keywords are deliberate, visible acts the person performs, and this record does not make any of them a consequence of reading.
- Revisit this record if a deployment serves a mailbox in the client whose unread state belongs to somebody other than the owner, which is the case that moves the setting from the owner onto the account.
