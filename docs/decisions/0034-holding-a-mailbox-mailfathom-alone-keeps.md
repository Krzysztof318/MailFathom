---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-13
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Let an account hold its mailbox in MailFathom and empty its source, identify a stored message by its own row rather than by an occurrence, make every act on a held mailbox a local commit, and move every IMAP command the mode needs into bounded background work that switching the mode off reverses

<!-- describes: backend/src/Domain/Emails/EmailOccurrenceId.cs, backend/src/Domain/Emails/RemotelyDeletedEmailDisposition.cs, backend/src/Domain/Folders/**, backend/src/Application/Synchronization/MailboxSynchronizer.cs, backend/src/Application/Mail/Mutations/**, backend/src/Application/Mail/Delivery/Filing/**, backend/src/Application/Mail/Delivery/Drafts/** -->

## Context and Problem Statement

MailFathom is a copy of a mailbox that lives somewhere else. It synchronizes from an IMAP server, keeps the raw message and everything derived from it, and treats the server as the truth about what exists. A stored row **is** one remote occurrence, keyed by folder, `UIDVALIDITY`, and `UID` ([ADR 0008](0008-copied-message-local-identity.md)). Every change a person, a rule, a spam verdict, or a tool asks for is carried to the server through the write session and reaches the local row only when synchronization observes it ([ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md)). A folder exists only as a mapping onto a folder the server advertises, and a draft or a sent copy reaches the user's mailbox by being appended to it.

Issue 1947 asks for the opposite arrangement as an option per mail account: MailFathom downloads each message, keeps it, and removes it from the source server, which becomes an inbound relay rather than a mailbox. It has exactly two purposes. **No mail is stored twice**: when the IMAP server runs on the same machine or the same hosting as MailFathom, every message today occupies that storage once in the server's mailbox and again in MailFathom's content store. **No act waits on IMAP**: a delete, a move, or a flag takes effect when MailFathom's own state commits, and whatever the source still has to do happens in the background, so a slow server delays the cleanup and never a person.

Reading is not the hard part, because every read path already answers from the stored copy. What has no answer yet is what a message, a folder, and a mutation *are* once no server holds them, and every other child of issue 1947 — the identity change on issue 1940, local mutations on 1941, local folders on 1942, export on 1943, filing on 1944, the client surface on 1945, and the switch on 1946 — is written against that answer. Issue 1939 is the gate that settles it, and this record is that answer. It produces no code.

The owner stated one more constraint while this record was being written, and it shapes the whole of it: **the mode is off by default for every account, switching it on empties the source server, and switching it off again fills the IMAP mailbox back up.** It is a reversible mode rather than a migration, and both directions are periods of background work rather than instants.

## Decision Drivers

- **The only copy of somebody's mail is never at the mercy of a crash, a size limit, or a misread configuration.** Every other failure this design can produce is recoverable. A message expunged from its source before MailFathom durably holds it is not, and nothing else here is allowed to trade against that.
- **Nothing on a request path waits on IMAP for a held account.** A design that keeps one IMAP round trip on a client, MCP, or rule request for such an account has defeated the second purpose, however rarely the round trip happens.
- **An account left in the existing mode behaves exactly as before.** The mode is off by default, and a mirrored account's synchronization, reconciliation, remote mutations, and filing are not rewritten to make room for it.
- **One identity for a stored message, whichever mode its account runs in.** Local mutations, local folders, filing, and export written against a second identity beside the occurrence would each exist twice, and the two would drift.
- **Guessing identity stays refused.** ADR 0007 and ADR 0008 refuse to join a message by `Message-ID` or by a digest where the server named no placement, because both are wrong in both directions. Nothing here reopens that, and the one identity comparison this record permits is the one ADR 0007 already accepts: a `Message-ID` this deployment minted itself.
- **A single writer per value.** ADR 0007 made synchronization the one writer of the stored flag snapshot. A held account has no server to observe, so the value needs exactly one writer again rather than two that take turns.
- **Irreversible acts driven by attacker-influenced input are the thing to refuse.** Emptying a source is irreversible and is driven by what arrives from the network, which is why issue 1946 carries the `security` label and why this record owes an authorization review for every command it adds.
- **Several replicas run this code, and during a rolling upgrade two builds run side by side** ([ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)). A build that does not know the mode will meet an account that is in it.
- **Holding the only copy moves obligations onto the operator** that the source server used to discharge silently: the backup, the way out, and the erasure. They are stated rather than implied.

## Considered Options

The decision has nine axes, one per question issue 1939 asks. A to C are ordered against each other — B is a gate over the identity A picks, and C decides which messages B is ever asked about — and D to I are each written against A's answer and are otherwise independent.

**A — what identifies a stored message:**

1. The occurrence stays the identity, and a local move on a held account rewrites the folder and mints a local UID.
2. The row's existing local identifier is the identity for every account, and the occurrence becomes an optional attribute of the row.
3. A new message table above the occurrence rows, as ADR 0008's rejected option 2 described.

**B — what a message must reach before it may be expunged from its source:**

1. A committed row whose content availability says the payload is stored.
2. That, and the payload read back from the backend it is stored in and matched against the recorded length and SHA-256 digest.
3. That, and every derivation — extraction, classification, embedding — finished.

**C — what the mode reaches, and how it is switched:**

1. Every synchronized folder of the account is drained; the mode is switched by configuration, on and off, and each direction is a background period of its own.
2. Only the inbox is drained, and other folders stay mirrored.
3. The mode can be switched on and never off.

**D — how an act on a held message is carried out:**

1. The act commits to stored state, together with its audit entry, in one transaction; no mutation record and no converger are involved.
2. The act is written as a mutation record and applied locally by the account's convergence pass.
3. The act is carried to the source first, as on a mirrored account, and the drain follows.

**E — which folder operations a held account has:**

1. None beyond a mirrored account's; the folder tree stays the source's.
2. Create, rename, move within the hierarchy, and delete, with a fixed set of roles every held account has and nobody may remove.
3. Everything in 2, with every role optional.

**F — where source-side work runs:**

1. On the account's supervision, under its existing lease, over its one write connection, in bounded batches recorded before they are issued.
2. On the request that caused it, bounded by a timeout.
3. On a deployment-wide worker of its own, independent of the account's supervision.

**G — where a held account files a draft and a sent copy:**

1. As a stored message in the account's local drafts or sent folder, written in the transaction that settles the draft revision or the delivery.
2. Appended to the source as today, and drained back like any other arrival.
3. Not filed at all; the draft record and the outgoing record are the only account of either.

**H — what a held account exports as:**

1. A Maildir tree carried in a tar stream.
2. One mbox file per folder.
3. A tree of `.eml` files with a sidecar document for flags.

**I — how the mode meets several replicas and a rolling upgrade:**

1. The mode's work runs under the account's existing lease scope, every command it issues is idempotent against the UIDs it names, and an operator switches the mode on only once every replica runs a build that knows it — with a configuration rule that leaves an older build meeting a held account nothing irreversible to do.
2. A scope of its own for the drain, with its own lease.
3. A build-version handshake in the database that a replica must pass before it touches any account.

## Decision Outcome

Chosen options: **A2**, **B2**, **C1**, **D1**, **E2**, **F1**, **G1**, **H1**, and **I1**.

### The account's custody is a setting, and the phase it is in is a fact the database records

A mail account's synchronization entry gains `Custody`, with two values. `MirrorSource` is the default and is exactly today's behaviour. `HoldMailbox` asks MailFathom to hold the mailbox and empty the source. The setting is what an operator *asks for*; it is read-only configuration like every other account setting, and it is validated at startup with the rest of the account options.

What the account *is* at any moment is a phase stored with the account in the database, because a switch is a period of work and a configuration value cannot say how far that work has got:

| Phase | What is true | What moves it |
|---|---|---|
| `Mirrored` | The source is the truth; everything is as it is today | `Custody` reading `HoldMailbox`, once the account has no remote mutation outstanding |
| `Held` | MailFathom is the truth; the source is drained of what MailFathom holds and delivers new mail to it | `Custody` reading `MirrorSource` again |
| `Restoring` | MailFathom is still the truth; the mailbox is being appended back to the source | Every held message holding a source occurrence again, and every folder existing on the source |

**Switching the mode on empties the source, and it is a period rather than an instant.** The phase becomes `Held` as soon as the account has finished what it already asked the server for: a remote mutation outstanding at the moment of the switch is carried to its ending by the converger first, because a relocation half-issued on the source and then declared local would leave the message in two folders nobody can reconcile. Until then the switch is reported as pending and the account stays `Mirrored`. From the moment it is `Held`, every act on the account is local, and the drain works through what the source still holds under the gate below. An account whose source held nothing has nothing to drain; an account with years of mail on its source spends as long as the drain's batches take, and nobody waits for that but the source's disk.

**Switching it off fills the source back up, and it is a period too.** The phase becomes `Restoring`, MailFathom stays the truth throughout, and the restore appends every held message that no longer has a source occurrence into the source folder its local folder corresponds to, carrying its flags and keywords. A message the drain had not reached yet still has its occurrence and is not appended again. When nothing remains to append, the phase becomes `Mirrored` and the source is the truth once more. A switch back on during `Restoring` returns the account to `Held` and the drain takes back what the restore had appended; neither direction has to finish before the other may be asked for.

**The mode requires RFC 4315 `UIDPLUS`.** The drain removes a message with `UID EXPUNGE` naming its UID, and the restore recognizes what it appended through `APPENDUID`. A source that advertises neither is refused the mode: the account stays `Mirrored`, the refusal names the account and the capability, and nothing is drained. A bare `EXPUNGE` is no more available here than anywhere ADR 0007 governs.

### A stored message is its own row, and the occurrence is where the source still holds it

The row's `id` — the UUIDv7 every other table already references — is the identity of a stored message, **for every account in every mode**. The occurrence (the folder binding, `UIDVALIDITY`, and `UID`) becomes an optional attribute of that row: present while a source holds the message at that place, and absent where none does. A stored message also carries the local folder it is in, which on a mirrored account is the folder its occurrence is in and on a held account is MailFathom's own.

This replaces the second clause of ADR 0008's decision — *leave the occurrence the only identity a stored row carries* — and nothing else of it. Option A1 was refused because a local UID is an identity MailFathom would invent in the protocol's own vocabulary, where it would be mistaken for one a server issued, and because every move would rewrite the key every derived row hangs from. Option A3 was refused for the reason ADR 0008 gave and which still holds: it buys *one message in two places* only for copies MailFathom itself made, at the price of revisiting every read that assumes one row per message. The row identity already exists, every foreign key already points at it, and making it the identity is a change to what the occurrence columns mean rather than to what anything references.

What the occurrence does on a held account is narrow and stated:

- **It is how a message not yet drained is found on the source**, so the drain names the UID it expunges and the refill of deferred content fetches from the right place.
- **It is cleared when the drain's expunge is observed complete**, not when it is issued. A process that died between the two leaves the occurrence standing, and synchronization meeting that UID again recognizes the row it already has rather than storing a second message. Clearing it on observation is also the data-minimization answer: a remote path and UID that name nothing any more are not kept.
- **It is written again by the restore** from the `APPENDUID` answer, which is what makes a restored mailbox a mirrored one without rediscovering anything.

Issue 1940 carries the change, as an additive migration. The client HTTP API and the MCP tools identify a stored message by this identity, so that a message the source has forgotten stays addressable; where a surface identifies one by an occurrence today, that is a break under [ADR 0004](0004-versioning-and-release-policy.md), named in the pull request that makes it with the caller's action.

ADR 0008's decision that **a copy is a second stored message** is untouched and extends to a held account: a local copy is a second row with its own payload, its own derived data, and its own place in the erasure cascade. [ADR 0017](0017-object-storage-content-backend-consistency-and-object-identity.md)'s refusal to share one payload between rows applies unchanged.

### The drain gate: nothing leaves the source before its bytes have been read back

A message on a `Held` account may be expunged from its source only when **all** of these hold:

1. Its row is committed, in the local folder its source folder corresponds to, with its occurrence naming the UID about to be expunged.
2. Its content availability says the payload is stored — neither `ExceededSizeLimit`, nor `AwaitingStorageHeadroom`, nor any later value that means the bytes are absent.
3. **The payload has been read back from the backend that holds it and matches the length and SHA-256 digest recorded on the row**, and the row records when that was established. Under ADR 0017 a committed row already points at a readable object, and that holds as a design property; for the only copy of a message it is verified rather than trusted, once per message, before the one act that cannot be taken back. Option B1 was refused for exactly that gap, and option B3 because extraction, classification, and embedding read the stored copy and never the source, so waiting for them would hold mail on the source for as long as a model is unreachable without making anything safer.

Each of the three states the gate turns on has its own answer:

- **`ExceededSizeLimit` is never drained.** The message is above the configured per-message limit and will be on every run, so it stays on the source with its envelope stored locally, and the account reports it as held back for that reason. An operator who wants it drained raises the limit, which is what fetches it. A held account whose source keeps such a message is not fully emptied, and saying so is the honest result.
- **`AwaitingStorageHeadroom` is drained once the refill pass has stored it**, and not before. The source keeps it for as long as the ceiling does, which is what the ceiling is for.
- **A message outside `EarliestEmailReceivedDate` is never drained**, because MailFathom never discovers it and therefore never holds it. It stays on the source untouched and is not reported as held back, since nothing here knows it exists. The combination is permitted rather than refused: an operator bounding a held account is choosing to leave old mail where it is, and the documentation of the setting says so.

The gate is re-evaluated immediately before a batch is issued, not only when a message is selected for one, so a row erased or moved between selection and issue is checked against what is true when the command goes out.

### What the mode reaches

**Every folder the account synchronizes is drained into a local folder, including the source's sent, drafts, junk, and trash.** A folder whose mapping does not synchronize is never held and never drained. Option C2 was refused because a mailbox half held and half mirrored has two truths, and every act would need to know which half a message is in.

**A virtual folder is refused on a held account.** A mapping that synchronizes a folder playing the `All`, `Flagged`, or `Important` role fails validation where the configuration binds, naming the alias: such a folder presents messages that are occurrences of other folders, so holding it stores each of them twice and draining it removes mail from folders nobody asked about. Providers whose folders are labels over one store are not what this mode is for — its purpose is a server on the operator's own host — and the refusal is what keeps one from being drained by accident.

**Each source folder corresponds to one local folder.** The source inbox, sent, drafts, junk, and trash correspond to the local folders playing those roles; any other synchronized folder corresponds to a local folder created with its name the first time a message arrives from it. The correspondence is to the local folder's identity, so renaming or moving that folder locally changes nothing about where arrivals go. Where the local folder has since been deleted, arrivals go to the inbox.

**What changes on the source after the switch is not imported, other than new mail.** A `Held` account's truth is local: another client flagging, moving, or deleting a message on the source changes nothing locally, and `RemotelyDeletedEmailDisposition` and `AuthoredDeleteEmailDisposition` are not consulted for a held account at all. A message another client removes from the source before MailFathom stored it was never held. The flag state a message carried when its account became `Held` — the last reconciled snapshot, or what the server reported when a new arrival was discovered — is its initial local state.

**A `UIDVALIDITY` change on a held account's source duplicates rather than loses.** Every row in that folder still carrying an occurrence under the old value loses it without an expunge, and the messages the folder now reports are discovered as arrivals and drained. Mail the drain had not yet reached is therefore stored twice; recognizing it by content instead would be the guess this record refuses, and a duplicate the user deletes is recoverable where a loss is not.

**Switching back to `MirrorSource` restores the whole mailbox.** Nothing is left on MailFathom alone when the source is the truth again, because a mirrored account's truth is by definition what the source holds: a message not appended back would be a message the mirror then treats as gone.

### An act on a held message is a local commit

Every act a mirrored account has exists on a held account: **set or clear `\Seen` and `\Flagged`**, **add, remove, or replace keywords**, **move**, **copy**, and **delete**. Each is requested exactly as it is today — by a person through the client endpoint, by a tool, by a rule, or by a spam verdict — under the same permissions (`mailfathom.mail.flags.write`, `mailfathom.mail.move`, `mailfathom.mail.delete`) and the same use cases, and the choice of executor is made once, in the mutation layer, from the account's phase. No caller branches on it.

**On a `Held` account the act commits to stored state, together with its audit entry, in one transaction** (option D1). There is no mutation record and no converger, because there is no sequence of remote commands for a crash to interrupt: the transaction either happened or did not. The request returns when it commits, and the signals announcing a changed message or folder are published as they are for a synchronized change. Option D2 was refused because a durable intent applied by a later pass reintroduces the lag the mode exists to remove, for no failure it guards against; option D3 is the mirrored path itself.

**The stored flags and keywords keep one writer per phase.** On a `Mirrored` account that writer is reconciliation, as ADR 0007 made it; on a `Held` account it is the local mutation path. The phase change is the handover, so the two never write the same value in the same period.

**Delete means the trash, and deleting from the trash means erasure.** A delete on a held account moves the message into the local trash, which any act can move it back out of. A delete of a message already in the trash erases it: the row, its payload — an object after the transaction commits, under ADR 0017 — and everything derived from it through the cascade. Erasure is the one irreversible act a held account has, so it keeps the grace window a person's own client asks for before it commits, as a delete asked for through the client endpoint keeps one today; everything else commits at once.

**A message not yet drained is acted on locally all the same.** The act commits to stored state and the source is not touched on the request. A message moved or flagged is still drained later from wherever its occurrence names. A message erased before its content was stored is removed from the source by the drain without being stored, and a message erased after it was stored but before it was drained is removed from the source the same way — so an erasure always reaches the source, and never on the request.

**During `Restoring` the act is still local**, and for a message whose occurrence the restore has already written back, the same transaction writes an ordinary mutation record against that occurrence, which the converger carries to the source under ADR 0007 as it would on any mirrored account. That is what keeps a message moved after it was appended from arriving back in `Mirrored` in the folder it was appended to.

**The audit entry is the same entry.** A local act writes to the audit trail under the account's audit setting exactly as a remote one does, naming the local folder paths and carrying no occurrence. The record of who changed a mailbox has no hole in it because a change stopped reaching a server.

### A held account has folders of its own, and five of them it always has

A held account may **create**, **rename**, **move within the hierarchy**, and **delete** its folders (option E2), under `mailfathom.mail.folders.write`, a permission of its own under [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) that reading mail does not confer, audited like any other act. Every one of them is refused on a `Mirrored` account, where folder management stays exactly what ADR 0007 permits.

ADR 0007's refusal of renaming and deleting a folder rested on the driver that they displace or destroy mail the operator did not name in the act, on somebody's mail server, and break every binding pointing at the old path. None of that holds for a folder only MailFathom has: the person renaming it is the person the mailbox belongs to, the act reaches no server, and a binding is to the folder's identity rather than to its path. That is why the refusal is left standing for a remote mailbox and does not extend to a local one.

**Every held account has an inbox, a drafts folder, a sent folder, a junk folder, and a trash folder**, created when the account becomes `Held` wherever the corresponding source folder has not already supplied one. Those five cannot be deleted, renamed, or moved: composing, sending, classifying, and deleting each need somewhere to go that no earlier act can have taken away. An archive is an ordinary folder a person may create. A held account has **no outbox mirror**: the outgoing record is the outbox, and the client's outbox routes are where a person reads it.

**Deleting a folder moves it, with everything beneath it, into the trash**, as one hierarchy move rather than as one move per message, so it commits in the time one row update takes whatever the folder holds. Deleting a folder already in the trash erases it and everything beneath it: the folder disappears from every listing when that commits, and its messages are erased in bounded passes afterwards under the same cascade a single erasure uses.

**A folder name is validated where it is written**: it is non-empty, carries no control character and no hierarchy delimiter, is bounded in length, differs without regard to case from every sibling, and is not `INBOX` at the top level. The local hierarchy delimiter is `/`, fixed, because it is MailFathom's own; the restore splits and rejoins paths with the delimiter the source reports.

### Every IMAP command the mode needs runs in the background

Three kinds of source work exist, and none of them runs on a client, MCP, or rule request:

- **The drain expunge** of a message that passed the gate.
- **The removal of a message that was erased before it was drained**, whether or not its content was stored.
- **The restore append** of a held message, and the folder creations it needs, while the account is `Restoring`.

All three run on the account's supervision, under the account's existing lease scope (option F1), as a pass at the end of each synchronization run — after the forward pass and the refill of deferred content, so what a run just stored is drained by the same run. They use the account's single write connection, so the connection budget ADR 0007 bounds is unchanged. Option F2 is the waiting the mode exists to remove, and option F3 would put a second writer against one account's source beside the supervisor that already holds it.

**Each pass is bounded.** It takes at most a configured number of messages, oldest first, and ends; the next run takes the next batch. A drain batch is `UID STORE +FLAGS (\Deleted)` followed by `UID EXPUNGE` naming exactly the UIDs of that batch — never a bare `EXPUNGE`, and never a UID this deployment did not select under the gate. A source that fails a batch is retried on the account's existing backoff, and an unreachable source delays only the drain and the restore: reading and acting on a held account never needed it.

**Each command is written down before it is issued**, in the same durable mutation records the mirrored path uses, so a crash between the command and its answer resumes rather than guesses. Both drain commands are idempotent against the UIDs they name, so a resumed batch issues them again. The restore append is not idempotent — a second `APPEND` is a second message — so ADR 0007's rule for filing holds: an append whose answer never arrived is not repeated, the record says its outcome is unknown, and **an account cannot leave `Restoring` while such a record stands**. The account reports it by name and count, and settling it is an operator's act, which issue 1946 gives a command.

**What a client sees while source work is pending is nothing about the message.** A held message is complete locally the moment it is stored, and its presence on the source is not a property a person acts on. What the account reports is the work: messages drained, held back by the gate and for which reason, awaiting removal, awaiting restore, and failed — as metrics and on the administrative surface, carrying no subject, address, or content.

### Drafts and sent copies are stored messages in the account's own folders

On a held account a **draft** is filed as a stored message in the local drafts folder, written in the same transaction as the draft revision it shows (option G1). A new revision replaces the stored message rather than appending beside it and withdrawing, because a local replacement has no window in which two copies exist. The draft record stays the truth about the draft exactly as it is today, and giving a draft up erases its stored message in the same transaction.

A **sent copy** is filed, where `Delivery:FileSentCopy` holds, as a stored message in the local sent folder, written in the transaction that settles the delivery and keyed to the outgoing record so the same delivery cannot file twice. No IMAP command is issued for either.

**A sent copy the provider files itself is recognized, and drained without being stored.** Such a copy arrives in the source's sent folder and would be drained into the local one as a second message. It is recognized by the `Message-ID` this deployment minted for the outgoing record, on that account, in the source folder playing the sent role — the one identity comparison ADR 0007 already accepts, because the identity is MailFathom's own rather than one it is guessing at — and it is expunged from the source without a second row being written. Option G2 was refused because an append to a source that is then drained is a round trip that stores nothing and makes filing wait on IMAP; option G3 leaves a person's sent folder empty however much they send.

### The export is a Maildir in a tar stream

A mailbox, or one folder of it, is exported as **a Maildir tree carried in a tar stream** (option H1): one Maildir per folder in the hierarchy, each message a file holding the stored bytes exactly, and the standard Maildir flags in each file name — `S` for `\Seen`, `F` for `\Flagged`, `R` for `\Answered`, `D` for `\Draft`. Maildir is read by mail servers directly, which makes the export the operator's way back onto an IMAP server as well as a user's way out, and tar streams without holding the mailbox in memory or a directory of contents at its end. Keywords have no standard Maildir form and are written in a document of their own at the root of the archive rather than in any server's private format. Option H2 was refused because mbox rewrites the stored bytes — a body line beginning `From ` has to be quoted — and has no standard place for flags; option H3 because a sidecar for every flag reinvents what Maildir file names already carry.

The export streams, is bounded and cancellable, and is audited with who asked, which account and folder, and how many messages and bytes — never content. It is available for **every** account, since it exports what MailFathom stores, and is required for a held one. It is reached through `mfctl` and the endpoint `mfctl` calls, under `mailfathom.mail.export`, a permission of its own that `mailfathom.mail.read` does not confer: reading a message and carrying a whole mailbox away are different acts with different consequences. The client does not offer it. [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md) keeps mail off the device, and a whole mailbox written to a person's disk by the client is exactly that, so a request for it reopens this record rather than being read out of the gap.

### What an operator owes a mailbox MailFathom alone holds

- **The backup is the database and the content store together.** Under ADR 0017 an object is written before the row that points at it, so a restored database older than the restored bucket leaves orphans the reclamation removes, while a bucket older than the database leaves rows pointing at mail that is gone. A backup therefore snapshots the database first and the bucket after it, and object-store versioning or lifecycle rules never remove an object newer than the database snapshot being kept.
- **A restore is verified before the account is put back into service**, by reading every payload of the account back against its recorded length and digest. A restored held account with an unreadable payload is a lost message, and finding that out when somebody opens it is too late to do anything about.
- **Nothing is retained or erased automatically.** The trash is emptied by the person whose mailbox it is, and no retention period deletes held mail. Removing a held account from configuration erases nothing: its mail stays until an operator erases it through the administrative erasure under `mailfathom.admin.erase`, or a data-subject erasure of its user reaches it, because configuration going missing is not a statement that the only copy of somebody's mail should go with it.
- **Erasure is final.** On a mirrored account the source still holds what MailFathom erased; on a held one nothing does, and the operator documentation says so beside the export that is the only way to keep a copy first.

Issue 1943 writes those obligations into the operator documentation, and issue 1946 names them where the setting is described.

### Several replicas, and two builds at once

**The mode's work runs under the account's existing lease scope** (option I1), the same one that already gives an account one supervisor across replicas under ADR 0031. The drain, the removal, and the restore are one writer's work, and ADR 0031 promises one writer rather than one runner: two replicas can overlap across a lease handover. What makes the overlap safe is that the gate is re-read before a batch is issued, both drain commands are idempotent against the UIDs they name, and the restore's append is written down before it goes out and never repeated. A local act is a database transaction, and two acts on one message resolve by the row's concurrency token as any other write does. Option I2 was refused because a second scope against the same source is two writers against one write connection; option I3 because a handshake nothing else in the deployment needs is machinery for one feature's upgrade.

**An operator switches the mode on only once every replica runs a build that knows it.** A build that does not know `Custody` reads a held account as mirrored, and on meeting its source emptied it would apply `RemotelyDeletedEmailDisposition` to every drained message. So `HoldMailbox` is refused where configuration binds unless the account's `RemotelyDeletedEmailDisposition` is `RetainTombstone`: the worst an older build can then do is tombstone rows, which hides them and destroys nothing, and a build that knows the phase clears such a tombstone on a held account on its next run. `EraseLocalCopy` beside `HoldMailbox` is refused naming both keys. The occurrence columns becoming nullable is additive, and a null occurrence is written only for a held account, which an older build is exactly the build that must not be holding; the release that ships the mode states the upgrade order in its changelog entry.

### What ADR 0007 and ADR 0008 keep, and what this record changes

Both records are still `proposed`, so each carries a pointer to this one rather than being superseded, and neither text is rewritten.

**ADR 0007 governs a mirrored account unchanged**, and every one of its eleven axes still holds there. It governs the source of a held account as well, and this record adds three acts to what MailFathom may issue against a source — each reached only through a mode an operator configured, and each reviewed below:

- **The drain expunge** — `STORE +FLAGS (\Deleted)` and `UID EXPUNGE` of a message MailFathom stored and nobody asked to delete. It amends axis A's closed set, which held only acts the mailbox user authored; this one the operator authored by choosing the mode.
- **The restore append** of a message MailFathom holds but did not compose. It amends axis J, which admits an append only for a message MailFathom composed itself; the flags it carries are the message's own stored state rather than a role's, which amends axis K for this one act.
- **The restore's folder creation**, for a local folder with no source counterpart. It amends axis E, which admits creation only for a folder a mapping named with `CreateIfMissing`; the path here is the held account's own folder, which the person the mailbox belongs to created.

**ADR 0008's first clause holds everywhere**: a copy is a second stored message. Its second clause, that the occurrence is the only identity a stored row carries, is replaced for every account by [a stored message is its own row](#a-stored-message-is-its-own-row-and-the-occurrence-is-where-the-source-still-holds-it). Its search, retention, erasure, and export consequences are unchanged by that, since each was already stated per row.

### The authorization review the mode required

A new irreversible act owes a review against the driver that refuses irreversible acts driven by attacker-influenced input, and the drain is the plainest instance this system has had: it destroys the source's copy of every message that arrives.

**The input class that decides to drain is the operator's configuration, and nothing else.** `Custody` is written by whoever writes the account's configuration, who already holds the credential that could empty the mailbox directly. No tool argument, client request, rule, model output, or message content selects a mode, a folder, or a message for the drain: the drain takes every message that passed the gate, and the gate is a fact about MailFathom's own storage.

**What an attacker who controls arriving mail can do is bounded by the gate, and none of it loses mail.** A message crafted to be too large is `ExceededSizeLimit` and stays on the source. A flood that fills the storage ceiling leaves every further message `AwaitingStorageHeadroom`, on the source. A message whose bytes fail to store never reaches the digest check. A message that makes derivation fail is still drained, because derivation reads the stored copy and its failure destroys nothing. The act the attacker can cause is the one the operator chose — mail that MailFathom verifiably holds leaves the source — and they can cause it only for their own message.

**The command reaches only what it names.** Every expunge names UIDs the gate selected, on one account's source, and a bare `EXPUNGE` is never issued, so a message another client flagged `\Deleted` and MailFathom never stored is not swept with the batch.

**The restore appends only what MailFathom holds for that account, into that account's source.** Its content is stored bytes a source already delivered once, its destination is the folder the message is in, and nothing a caller supplies reaches it. Its folder creations are paths the mailbox's own user wrote.

**The local acts widen nothing.** They are the same requests, from the same requesters, under the same permissions, as ADR 0007's reviews already admitted. The two permissions this record adds — `mailfathom.mail.folders.write` and `mailfathom.mail.export` — each gate an act reading mail does not imply, and the one irreversible local act, erasure, keeps its grace window.

### Consequences

- Good, because a host that runs the IMAP server beside MailFathom stores each held message once, which is the first purpose of the mode.
- Good, because no act on a held account waits on IMAP: every one of them is a database transaction, and every source command runs in a background pass.
- Good, because a message never leaves its source before its stored bytes were read back and matched, so a storage defect is found while the source still has the mail.
- Good, because one identity serves both modes, so mutations, folders, filing, and export are each written once, and a message the source forgot stays addressable.
- Good, because the mode is reversible in both directions, so choosing it is not a one-way door and leaving it costs time rather than mail.
- Good, because a mirrored account is untouched: its synchronization, reconciliation, remote mutations, and filing behave exactly as before.
- Good, because the export is a format mail servers read, which makes it both the user's way out and the operator's way back onto a server.
- Neutral, because the gate reads every payload back once before its message is drained. That is one read per message, paid in the background, and it is the price of treating the only copy as verified rather than trusted.
- Neutral, because a held account's sent copy exists twice locally — the outgoing record's content and the stored message in the sent folder — which is a local duplicate rather than the source duplicate the mode removes.
- Neutral, because the administrative surface now reports a held account's drain and restore work, and those counts are the only way an operator learns how far a switch has got.
- Bad, because MailFathom now issues an expunge nobody asked for message by message. It is bounded by the operator's configuration and by a gate, and it is still the most destructive command this system issues.
- Bad, because a held account's backup is the operator's alone, and a deployment that loses its database and its bucket together loses that mail with no server behind it.
- Bad, because a `UIDVALIDITY` change on a source that is still being drained stores the undrained mail twice. The duplicate is visible and deletable; recognizing it would have needed a guess.
- Bad, because a message above the size limit keeps a held account's source from ever being empty, and an operator has to read the held-back count to know why.
- Bad, because an append the source never answered holds an account in `Restoring` until an operator settles it, and the only tool for that is theirs to run.
- Bad, because a rolling upgrade to the release that ships the mode has an order: the mode is switched on only after every replica runs that release. The `RetainTombstone` rule makes a mistake reversible rather than impossible.
- Bad, because folders whose provider implements them as labels over one store cannot be held, which leaves one family of providers outside the mode entirely.

## Validation

- Issue 1940 proves the identity: a stored message's identifier is assigned when it is first stored and never changes; a message whose occurrence is cleared is still returned by every read path; erasure through the identity reaches every derived row; and a mirrored account's synchronization is unchanged, including the rediscovery that recognizes an occurrence it already stores.
- Issue 1946 proves the gate for every content availability state — `ExceededSizeLimit` and `AwaitingStorageHeadroom` are never drained, and a payload whose read-back does not match its digest is never drained — and requires the gate to be re-read before a batch is issued, with the control the absence rule requires: the same pass over a message that passes the gate issues its expunge.
- Issue 1946 proves the commands: a drain batch is `UID STORE +FLAGS (\Deleted)` and `UID EXPUNGE` naming exactly the selected UIDs, with no bare `EXPUNGE` anywhere in the sequence; a source without `UIDPLUS` refuses the mode; an interrupted batch resumes without storing any message twice; and a message rediscovered at an occurrence the row still carries is recognized rather than stored again.
- Issue 1946 proves the phases: a switch waits for outstanding remote mutations; `Held` and `Restoring` move in both directions; a restore appends only messages without an occurrence, is not repeated after an unanswered append, and cannot end while one stands; and `HoldMailbox` beside `EraseLocalCopy` or a virtual folder fails configuration binding, naming the keys.
- Issue 1941 proves the local acts: each mutation kind commits to stored state with its audit entry and no mutation record; the executor is chosen from the phase in the mutation layer; delete moves to trash and delete in trash erases; and an act during `Restoring` on a restored message writes the remote record beside the local change.
- Issue 1942 proves the folders: each operation on a held account, refusal on a mirrored one, the five protected roles, folder deletion as one move into the trash, and name validation at the boundary.
- Issue 1943 proves the export byte for byte against stored content, with the folder structure and the flags in each file name, and refuses a caller without `mailfathom.mail.export`.
- Issue 1944 proves filing: a draft revision replaces its stored message in one transaction, a sent copy is filed once per delivery, and a provider's own sent copy is recognized by the minted `Message-ID` and drained without a second row.
- The integration suite drains a folder against the orchestrated server and reads the server back empty, restores it and reads the messages back with their flags, and proves that a message another client flagged `\Deleted` and MailFathom never stored survives a drain batch.

## Pros and Cons of the Options

### A1 — the occurrence stays the identity, with a local UID on a held account

- Good, because nothing above the occurrence changes and every key stays four columns.
- Bad, because a local UID is an identity in the protocol's own vocabulary that no server issued, and anything reading one would take it for a server's.
- Bad, because every local move rewrites the key every derived row hangs from, which turns the cheapest act a person takes into the most expensive write the schema has.

### A3 — a message table above the occurrence rows

- Good, because it models one message in several places, which is what a person might expect of a copy.
- Bad, because it delivers that only for copies MailFathom made, for the reason ADR 0008 gave, and revisits every read that assumes one row per message to get it.
- Bad, because the row identity it would introduce already exists: every foreign key already points at the row's `id`.

### B1 — a committed row with stored content is enough

- Good, because it costs no read and relies on ADR 0017's invariant as every read path already does.
- Bad, because it trusts, for the only copy of a message, a property that is otherwise tested only when somebody opens the message — which on a drained account is too late to recover from.

### B3 — wait for every derivation

- Good, because a message is then complete in every sense before its source copy goes.
- Bad, because derivation reads the stored copy and never the source, so it makes nothing safer while holding mail on the source for as long as a model provider is unreachable or a budget is spent.

### C2 — drain the inbox only

- Good, because it removes most of the duplicated storage with the smallest change.
- Bad, because a mailbox half held and half mirrored has two truths, and every act, every folder, and every export would have to know which half a message is in.

### C3 — a one-way switch

- Good, because nothing has to be appended back to a source, and no restore phase exists.
- Bad, because it turns a storage choice into a door that locks behind the operator, which the owner has refused: switching the mode off fills the source back up.

### D2 — a local mutation record applied by the convergence pass

- Good, because every mutation then has the same durable shape whichever mode it runs in.
- Bad, because the act finishes on the next pass rather than on the request, which is the waiting the mode exists to remove, and there is no multi-command sequence for the record to make resumable.

### D3 — carry the act to the source first

- Good, because it is the existing path and needs no second executor.
- Bad, because it puts an IMAP round trip on every act on a held account, which defeats the second purpose outright.

### E1 — no local folder management

- Good, because it changes nothing ADR 0007 decided about folders.
- Bad, because a held account's folder tree would be frozen at whatever the source had when the mode was switched on, with no server left on which anyone could change it.

### E3 — every role optional

- Good, because a person has full control of their own hierarchy.
- Bad, because deleting the sent or the trash folder leaves sending and deleting nowhere to put a message, and every one of those paths would need a branch for a folder that might be gone.

### F2 — source work on the request

- Good, because the source is emptied as soon as possible.
- Bad, because it is exactly the waiting the mode exists to remove.

### F3 — a deployment-wide worker

- Good, because drain throughput is then independent of how long an account's synchronization run takes.
- Bad, because it is a second writer against a source the account's supervisor already holds, and a second consumer of the one write connection ADR 0007 bounds per account.

### G2 — append to the source and drain it back

- Good, because filing stays one mechanism for both modes.
- Bad, because it makes filing wait on IMAP, spends an append and an expunge to store nothing, and fails whenever the source is unreachable.

### G3 — no filing

- Good, because the draft and outgoing records already hold everything.
- Bad, because a person's sent and drafts folders stay empty however much they write, which is what filing was admitted to fix.

### H2 — mbox

- Good, because it is one file per folder and every mail client imports it.
- Bad, because it rewrites the stored bytes, and it has no standard place for flags.

### H3 — `.eml` files with a flag sidecar

- Good, because each message is a file any client opens.
- Bad, because the sidecar is a private format for what Maildir file names already carry, and no server reads the tree directly.

### I2 — a lease scope of its own for the drain

- Good, because drain work and synchronization then fail and recover independently.
- Bad, because two holders act on one source over one write connection, and the exclusion ADR 0031 gives an account is split in two.

### I3 — a build-version handshake

- Good, because an older build is then excluded by the system rather than by an operator following the upgrade order.
- Bad, because it is coordination machinery nothing else in the deployment needs, built for one feature's upgrade, while a configuration rule already makes the older build's worst act reversible.

## More Information

- Issue 1939 asks the questions this record answers; issue 1947 is the feature it belongs to, and issues 1940 to 1946 are the children that implement it, with 1946 — the switch — landing last.
- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) holds the remote mutation boundary this record leaves unchanged for a mirrored account and amends for a held account's source; [ADR 0008](0008-copied-message-local-identity.md) holds the copy decision this record keeps and the identity clause it replaces.
- [ADR 0017](0017-object-storage-content-backend-consistency-and-object-identity.md) holds the write order the backup obligation rests on and the erasure of objects after commit; [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) holds the lease the drain runs under; [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) governs the two permissions this record names; [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md) is why the client offers no export.
- RFC 4315 defines `UIDPLUS`, `UID EXPUNGE`, and `APPENDUID`; RFC 6154 defines the `\All` and `\Flagged` special uses a held account refuses to synchronize. The Maildir flag letters are the ones the format's own specification defines.
- Revisit when a provider whose folders are labels is asked for, when the read-back in the gate becomes a measured cost rather than an accepted one, when a client export is asked for, or when importing an archive into a held account is — that is outside issue 1947 and would reach the identity decided here.
