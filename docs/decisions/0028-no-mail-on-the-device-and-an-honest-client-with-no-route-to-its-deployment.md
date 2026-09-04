---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-04
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep no copy of mail and no queued write on the device, bound a client store to what the person themselves did rather than to what the deployment answered, and meet a deployment out of reach with a client that says so

<!-- describes: frontend/src/Client.App/src/device/**, frontend/src/Client.App/src/deployment/adoptedDeployment.ts, frontend/src/Client.App/src/workspace/rememberedWorkspace.ts, frontend/src/Client.App/src/messageList/rememberedListings.ts, frontend/src/Client.App/src/composer/keptComposition.ts, frontend/src/Client.App/src/shell/useConnection.ts, frontend/src/Client.App/src/shell/ConnectionSummary.tsx -->

## Context and Problem Statement

`frontend/src/AGENTS.md` § *What the application is, and is not* opens on the sentence this record has to answer: the client **shows what the service already holds**, and it does not talk to a mail server, **hold a second copy of anything**, or decide what MailFathom is allowed to do. The service keeps a durable local copy, which is why the client is fast and why it survives a mailbox being briefly unreachable — but *local* there is local to the deployment. The client itself reaches `/api/client` for every listing, every message, and every attachment, so a client with no route to its deployment shows nothing it has not already got on the screen.

That is tolerable on a desktop beside its deployment and is the open question on a phone. [#1141](https://github.com/Krzysztof318/MailFathom/issues/1141) states the three answers and they are genuinely different products: a read cache, a read cache plus queued local writes, or nothing kept at all and a client that is honest about needing its deployment. Each decides how much state the client holds, which is the one architectural property the current design keeps deliberately at zero — and it is not a property that can be retrofitted, because paging, freshness, and eviction are designed once or designed twice.

The question is asked now rather than when a phone needs it because [#1212](https://github.com/Krzysztof318/MailFathom/issues/1212) waits on it, and because [ADR 0027](0027-an-android-head-built-every-night-and-supported-by-nothing.md) put an Android artifact on a device while promising nothing about it. Both are downstream of an answer nobody has written down.

## Decision Drivers

- **A copy on a device is the third copy of the same mail, and it is the first one outside the boundary every privacy obligation in this repository is written around.** The mail server holds it, the deployment holds it under the retention, access, and erasure rules root `AGENTS.md` § *Enterprise governance, privacy, and GDPR readiness* requires of it, and a device holds it under nothing. What makes the deployment's copy governable is that the deployment can be told to delete it.
- **A cache is not one decision, it is six, and each of them is invisible when it is wrong**: what is cached, the bound, the retention, the encryption at rest, what wipes it and when, and what a data-subject erasure at the deployment does about a copy sitting in somebody's pocket. Refusing the cache answers all six at once; taking it answers none of them and defers each to whoever writes the next screen.
- **A queued write needs a conflict rule that survives days, and the conflict rule for a queue that survives *minutes* is not written yet.** #1212 is the issue writing it, for a queue that lives as long as the client is open. A device queue is that same problem with the window widened to a week and a process kill in the middle of it, and it would be designed against a rule that does not exist.
- **The client is deliberately not a second implementation of the service.** `frontend/src/AGENTS.md` refuses re-deriving in the client what the service decides, on the ground that the second implementation will disagree with the first. Freshness, paging, and eviction over a 214 000-message mailbox are exactly that kind of judgement, and a cache moves them to the far side of a wire where they cannot be corrected by a deployment.
- **Most of what somebody loses with no route is not recoverable by a cache anyway.** A read cache shows the mail already looked at, which is the mail least likely to be wanted; what a person in a lift actually wants is the message that arrived while they were in it, and no client-side store has it.
- **The one thing genuinely lost today is what somebody typed and has not yet filed**, and that already has an answer of its own in `composer/keptComposition.ts`, with the argument for its bound written beside it.
- **The client already draws this as a state rather than as a failure.** `frontend/src/AGENTS.md` § *UX* requires five states of every screen and names `offline` as one, distinguishable from an empty answer; `shell/useConnection.ts` and `shell/ConnectionSummary.tsx` already separate *this machine has no network* from *the deployment is not answering*, keep the last answer on the screen with its age rather than clearing it, and reconnect on a bounded backoff. The honest option is not a thing to build; it is a thing to finish and to stop eroding.
- **Nothing on a phone is promised to survive anything.** ADR 0027 made the Android head a nightly artifact supported by nothing, with no update path and no promise that one night's data is readable by the next. A durable store on that head is retention nobody undertook to honour.

## Considered Options

- **Nothing is kept beyond the process, and the client says so.**
- **A read cache on the device**, with writes still refused while there is no route.
- **A read cache plus queued local writes**, reconciled when the route returns.

## Decision Outcome

Chosen option: **nothing is kept beyond the process, and the client says so** — because a client with no route to its deployment has nothing true to show that it is not already showing, and every other answer buys a narrow convenience by moving mail outside the one boundary where this project's privacy obligations are enforceable.

The rest of this record is what "nothing" means precisely, because it is not nothing, and what the client owes instead.

### The client already keeps three things, and none of them is mail

Stating the decision as *no client-side state* would be false about the tree as it stands and would make the rule unusable at the moment it matters, which is somebody adding a fourth store. What exists today is:

- **What the deployment last answered, in memory, for the life of the process.** `shell/useConnection.ts` holds the session and the accounts and deliberately does not clear them when the network goes: the last answer is still the truest thing anybody has, and saying so beside it is what the offline state is for. It dies with the process, is never written anywhere, and is not a cache — nothing re-reads it, ages it, or evicts from it.
- **Where the person was and what they typed, in the session's store**, which dies with the tab: the workspace in `workspace/rememberedWorkspace.ts`, the reading position per folder in `messageList/rememberedListings.ts`, and what is being written in `composer/keptComposition.ts`. Each of the three already carries the argument for its own bound, and `rememberedListings.ts` already states the rule this record generalizes — *a store is a place a screen's contents must not accumulate in*.
- **What belongs to the machine, in the device's store**, which outlives the tab: the theme, the language, how the Mail space divides its width, whether telemetry may be recorded before the deployment has answered, and the deployment's address. `frontend/src/AGENTS.md` § *State* governs which of the two stores a setting goes in, and none of what it governs is mail.

The credential is the fourth thing either store holds and is [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md)'s rather than this record's; it is named here only so the list above is not read as the whole of what the client writes.

### The two rules this record fixes

**In the device's store — anything that outlives the process — nothing about mail is written, in any form.** Not a message, a body, a subject, a header, a sender, an address, an attachment, a thread, a search result, a folder's contents, an unread count, a notification's text, or a summary, a snippet, an embedding, or any other thing derived from one; and not an identifier naming any of them. The store holds settings and an address, and it holds those because they are what the machine is rather than what the mail is.

**In the session's store — anything that dies with the tab — what may be written is what the person themselves did, never what the deployment answered.** Where they were, what they folded shut, what they were about to ask, the position a page was read at, and the words they have typed and not yet filed. A cursor the deployment issued and an identifier naming a place are the two things allowed to point at mail, and neither carries any of it. That line is where `rememberedListings.ts` already drew it, and this is the record it was drawn against.

The composition is the one thing in either store that *is* mail, and it is the exception the rules are shaped to permit rather than a hole in them. What it holds is what the person is writing and has not yet handed to anybody — not a copy of something the deployment has — and it is in the store that dies with the tab, which is what keeps it off a machine two people share. Saving a draft is a separate act somebody asks for, because every revision of that one reaches their mail server.

**No mutation is queued anywhere that outlives the process.** A flag, a move, a delete, a read mark, a send, and a draft save each reach the deployment or they do not happen. #1212 governs a queue that lives as long as the client is open; it gains nothing durable from this record.

### What the client owes instead, and it is an obligation rather than a consolation

Refusing the cache is only defensible if the client is honest, so the honesty is the decision's other half rather than a note about the current implementation.

- **Every screen has an offline state, and it is distinguishable from an empty answer.** That is already the rule in `frontend/src/AGENTS.md` § *UX*; this record is what makes it load-bearing rather than a quality bar.
- **The two sentences stay two sentences.** *This machine has no network* is the machine's to fix and *your deployment is not answering* is not, and a single spinner over both is the failure this decision would otherwise cause.
- **What was read stays on the screen with its age**, rather than being cleared to a loading state that will not finish. A person is owed what they were looking at and an honest label on how old it is.
- **A control whose act cannot reach the deployment says so before it is used, not after.** An act accepted and silently dropped is worse than a refusal, and it is the specific way a client with no queue misleads somebody.
- **The client reconnects on its own, on a bounded budget, and then hands the retry to a person.** An unbounded retry against a deployment that is down is a battery cost on the head this decision is most about.

### The privacy question, answered by the refusal rather than dodged by it

#1141 asks the privacy question of a cache. There is no cache, so what is owed is why that is itself the privacy answer.

Mail content, metadata, snippets, and anything derived from them are sensitive by default under root `AGENTS.md`, and derived indexes inherit the classification, retention, access-control, deletion, and export constraints of the source. A device store inherits all of that and can honour none of it:

- **There is no erasure path that reaches it.** A deployment can delete what it holds and can be made to prove it. A phone in somebody's pocket is not reachable by the deployment at all, so an erasure honoured at the deployment would leave a copy the project told somebody was gone. That is worse than not having implemented erasure, because it is a false statement rather than a missing feature.
- **There is no retention rule that survives a head nobody supports.** ADR 0027 promises nothing about an Android nightly's data across builds, so a cache there would be personal data with an undefined lifetime by construction.
- **The web head runs in a browser on a machine somebody else may use.** A device store outlives the tab by definition, so it outlives somebody walking away without signing out — which is precisely the case `keptComposition.ts` and `rememberedWorkspace.ts` chose the session's store to avoid.
- **Data minimization and storage limitation are the two obligations a cache is in direct tension with**, and no cache design removes that tension; the most a good one does is bound it. The bound this record takes is the strongest available and costs nothing to enforce, because it is enforced by there being nowhere to write.

### What is required before the mobile heads open

The acceptance asks for this because a supported mobile head is where the answer above stops being adequate. It does not stop being adequate at the Android artifact ADR 0027 already permits: that head is a nightly supported by nothing, promises no data survives it, and would gain from a cache exactly the retention promise it declines to make. **This decision covers the nightly head as written.**

What reopens it is a *supported* mobile head, and the following have to be true first. They are stated as a checklist for the reason 0027 states its own: so that a cache is something somebody decides to build rather than something the first offline defect report causes.

1. **The head is supported** — every item on ADR 0027 § *What would make this a supported head* — because a cache is a retention promise, and a channel that promises nothing cannot make one.
2. **A privacy design covering the third copy**, answering each of the six questions this record refuses to leave open: what is cached, the bound, the retention, the encryption at rest, what wipes it — sign-out, a refused credential, a changed deployment address, and an account removed — and what a data-subject erasure at the deployment does about a device the deployment cannot reach.
3. **The conflict rule #1212 writes, extended to a queue that survives a process kill and several days**, with the replay order, the bounded retries, and the reporting of a mutation that can never be applied that its acceptance already names. A durable queue is that issue's contract with the window widened, not a second design.
4. **One store, declared once.** `frontend/src/AGENTS.md` § *The two heads* refuses a per-head component and a module chosen by target, so a device store with a different mechanism per head is a `shellOperations/` operation the application declares, in the shape `linkOpener.ts` established — never a branch on the platform.
5. **A measurement rather than a guess about what is worth keeping.** The mailbox the client has to render holds 214 000 messages, so what a cached window contains is decided from what a screen actually shows and what a reader actually returns to, and it carries a bound checked on the way in.
6. **A `THIRD_PARTY_LICENSES.md` review against the head's artifact** for whatever store is adopted, under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md), if it is not the platform's own.

Until all six are true, a mobile head is a client that reaches its deployment or says it cannot.

### Consequences

- Good, because mail stays inside the boundary the privacy obligations are enforceable at, and an erasure honoured at the deployment is honoured everywhere rather than everywhere the deployment can see.
- Good, because there is no freshness, paging, or eviction judgement in the client to disagree with the service's, which is the failure `frontend/src/AGENTS.md` refuses re-derivation to prevent.
- Good, because the offline behaviour is one screen state designed once, rather than a cache whose staleness has to be explained on every surface that reads through it.
- Good, because the rule is enforceable by reading a diff: a store gaining a mail-shaped value is visible in review, where a cache's retention being subtly wrong is not.
- Neutral, because nothing in the tree changes to comply — the decision names a boundary the client already holds, which is the evidence for it rather than a reason it was cheap.
- Neutral, because #1212 loses nothing: its queue was always the in-session one, and this record removes an open question from its scope rather than narrowing what it delivers.
- Bad, because a phone with no signal shows what is on the screen and nothing more, and somebody who opened the client in a lift gets a sentence rather than their mail. That is the cost, it is real, and no wording removes it.
- Bad, because a reply written with no route can be typed and cannot be sent — it survives a reload and is lost to a process the operating system kills, which on a phone is common rather than exceptional. The correction below is what stops the client overstating that.
- Bad, because the answer will be argued again the first time somebody wants a supported phone, and the checklist above is a longer road than adding a store would have been.

### The one correction this decision owes now

`compose.offline` reads _"This machine is offline. What you write is kept here, and it can be filed or sent once the network is back."_ On the two supported heads that is true: the store behind it survives a reload, which is the only interruption a browser tab or a desktop window ordinarily takes. On a phone it overstates, because the operating system kills the process and the session's store goes with it, and a person told their words are kept is exactly the person the issue's own user story is about. [#1642](https://github.com/Krzysztof318/MailFathom/issues/1642) is where that sentence is corrected; it is copy rather than a store, and it is the whole of what this decision changes about what the client says.

## Validation

- `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so this record's own creation is gated on the owner authoring the change that carries it.
- The `describes:` marker names both stores, the three session-scoped modules, the connection hook, and the summary that renders its states, which is what tells a later pull request under any of them that it is read against this decision. `scripts/review-obligations.sh` and `Fathom review` both resolve it.
- The rule is enforced by review rather than by a script, which is why it is stated as a closed list of what a store may hold rather than as a principle: a value written to `device/deviceStore.ts` or to either session store is visible in a diff, and the question a reviewer asks is whether it is a setting, a place, or something the deployment answered. `frontend/src/AGENTS.md` § *State* carries the pointer here, so somebody about to add a store meets it at the moment it matters.
- The offline obligations are enforced where they already are: `frontend/src/AGENTS.md` § *UX* requires the five states of every screen, and `shell/ConnectionSummary.test.tsx` and `App.test.tsx` assert the separation between a machine with no network and a deployment that is not answering.
- Nothing here is enforced by the type system, and that is deliberate rather than an omission: a store takes a string, so a type cannot tell a folder name from a theme.

## Pros and Cons of the Options

### Nothing is kept beyond the process, and the client says so

- Good, because mail never leaves the deployment's storage, so retention, access control, erasure, and export stay answerable in one place.
- Good, because it is the only option with no new architectural property: the client's state stays at zero and every screen already designed against that stays correct.
- Good, because it needs no eviction rule, no cache invalidation, and no answer to the question of what a stale row means — three of the most expensive classes of defect in a mail client, avoided by not having the thing that produces them.
- Neutral, because it is what the tree already does, so choosing it is a record rather than a change; the value is that the next store is refused with a reason rather than added because nothing said not to.
- Bad, because a phone with no signal is a client that shows a sentence, and that is the honest but unsatisfying answer to somebody who wanted their mail.
- Bad, because it puts a ceiling on what a mobile head can be without reopening this record, and somebody will reach that ceiling.

### A read cache on the device

- Good, because mail already looked at stays readable with no route, which is most of what a person on a train is doing.
- Good, because it would make the client's first paint faster on every head, which is a benefit unrelated to being offline and is the argument most likely to be made for it later.
- Neutral, because it needs no conflict rule at all — reads do not collide — so it is genuinely the smaller half of the two caching options.
- Bad, because it moves mail content outside the deployment with no erasure path back, which is the objection the decision above rests on and which no cache size makes go away.
- Bad, because it needs the six answers listed above, each of which is a decision somebody has to take correctly and none of which is visible when it is taken wrongly.
- Bad, because a cache that is wrong is worse than no cache: a person shown a message that has since been deleted, moved, or answered elsewhere acts on mail that no longer exists, and the client has no way to know which rows are in that state.
- Bad, because the mail somebody most wants offline is the mail that arrived while they were offline, which a read cache does not have — so it buys less of the actual complaint than its cost suggests.

### A read cache plus queued local writes

- Good, because it is the only option that answers the issue's own story of writing a reply in a lift and having it leave when the signal returns, which is what a mail application on a phone is expected to do.
- Neutral, because the visible queue and the resolution surface it needs are largely what #1212 is already designing for the in-session case, so the screens are not wholly new.
- Bad, because the conflict rule it needs is the in-session one with the window widened to days, and that rule does not exist yet — so this option would be designed against something unwritten.
- Bad, because a queued write is mail leaving the deployment's control in the other direction: a message somebody composed sits on a device, unsent and unrecorded anywhere the deployment can see, which is a copy with no retention rule and no audit trail.
- Bad, because it puts the client in the position of deciding which change wins, which is the second-implementation failure the client's boundary exists to prevent, at the one place where getting it wrong loses somebody's work silently.
- Bad, because every offline write has to be replayable, and some of them are not: a send passes a point of no return under [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md) and is withdrawable only while it is still queued at the deployment, so a send queued on a *device* for two days is a message leaving on a decision its author took against a mailbox that has since changed, with the withdrawal window not yet started.

## More Information

- [#1141](https://github.com/Krzysztof318/MailFathom/issues/1141) is the issue this record answers. [#1212](https://github.com/Krzysztof318/MailFathom/issues/1212) waits on it and gets its answer here: the queue it makes visible is the in-session one, and nothing about it becomes durable. [#1642](https://github.com/Krzysztof318/MailFathom/issues/1642) is the copy correction named above.
- [ADR 0027](0027-an-android-head-built-every-night-and-supported-by-nothing.md) is what makes the mobile half of this answerable today: the Android head is a nightly that promises no data survives it, and its supported-head checklist is the first item of the checklist above. [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) is where the client's two shipped heads and the no-platform-branch rule come from.
- [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) is the same question asked about the credential rather than about mail, and its answer differs for a reason worth naming: a credential is one short value the platform offers protected storage for, and mail is an unbounded collection no platform offers anything for. [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) is where the client's other refusal to let mail leave the deployment's control lives.
- [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md) governs what has to be true before mail is transmitted, and is why a queued send is the hardest of the queued writes rather than one more of them.
- Revisit this decision when a supported mobile head is wanted and every item on the checklist above is true, which is the condition it was written against; or if the deployment gains a way to reach a device it has served — a wipe an erasure could travel over — since the absence of one is the load-bearing half of the privacy argument rather than an incidental fact.
