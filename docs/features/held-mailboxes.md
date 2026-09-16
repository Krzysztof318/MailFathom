# Holding a mailbox and emptying its source

<!-- describes: backend/src/Domain/Accounts/MailAccountCustody.cs, backend/src/Domain/Accounts/MailAccountCustodyState.cs, backend/src/Domain/Accounts/MailAccountCustodySwitchRefusal.cs, backend/src/Application/Accounts/Custody/**, backend/src/Application/Synchronization/Drain/**, backend/src/Host/Api/MailAccountCustodyEndpoints.cs, backend/src/Cli/Commands/Accounts/MailAccountCustodies.cs, backend/src/Cli/Commands/Accounts/ShowMailAccountCustodyCommand.cs, backend/src/Cli/Commands/Accounts/SwitchMailAccountCustodyCommand.cs -->

Every mail account this deployment reads has a **custody**, and it says which copy of the mailbox is the truth.
`MirrorSource` is what every account has until somebody changes it: the source IMAP server holds the mailbox, MailFathom
keeps a copy of it, and a deletion on the server is a deletion here. `HoldMailbox` is the other one. MailFathom holds
the mailbox, and the source server is emptied, message by message, of everything MailFathom has stored and verified.

It exists for two reasons and no others. **No mail is stored twice** — when the IMAP server runs on the same host as
MailFathom, every message today occupies that storage once in the server's mailbox and again in MailFathom's content
store. And **no act waits on IMAP** — a delete, a move, or a flag on a held account takes effect when MailFathom's own
state commits, and whatever the source still has to do happens in the background.

The whole design, including every option that was refused, is
[ADR 0034](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md).

## What holding a mailbox obliges you to

Read this part before switching anything. A drained message is gone from the source, and nothing puts that copy back.

- **You are now the only backup.** The source server used to be a second copy of the mailbox and stops being one. Back
  up the database and the content store together, and test restoring them, before the first account is switched.
- **There is a way out, and it is not the source server.** [The mailbox export](../operations/mailbox-export.md) writes
  a Maildir tree in a zip archive, which mail servers read directly and people open with no tooling. It is what a user
  leaving with their mail uses and what an operator putting a mailbox back onto an IMAP server uses.
- **Switching back is a new copy rather than an undo, and the copy is not written yet.** Asking for `MirrorSource` again
  checks every folder mapping, stops the drain, and moves the account to `Restoring`. Appending the mailbox back to the
  source is the half of that phase this release does not implement, so an account switched off stays in `Restoring` and
  MailFathom goes on being the truth about it. Either way the messages the drain removed are not recovered from the
  source, and what takes a mailbox off this deployment today is the export above.

## Switching one account

The switch is an administrative command rather than a configuration key, because emptying a source is a deliberate act
against one mailbox with an author and a moment rather than a value that arrives with a file. It is published under
[`mailfathom.admin.custody.write`](../operations/permissions.md), a grant of its own that neither the configuration
writer nor the erasure grant confers, and every switch — accepted or refused — is written to the service log as an
audit record naming who asked, the account, the value it moved from and to, and every reason a refusal was refused.

```console
$ mfctl account custody switch --account personal --to HoldMailbox
Hold the mailbox of 'personal'? Its source server will be emptied of every message MailFathom has stored and
verified, and nothing puts those copies back. [y/N]
```

```console
$ mfctl account custody show --account personal
Account:   personal
Requested: HoldMailbox
Phase:     Held
Awaiting drain:          4812
Held back, above limit:  3
Held back, no headroom:  0
Awaiting source removal: 0
```

`Requested` is what was asked for and `Phase` is how far the work has got. They differ while a switch is under way,
which is a period rather than an instant: the account's own runs carry it.

### What a switch is refused for

A refusal changes nothing and names every reason at once, so one round of fixing is enough.

- **A replica is running a build that does not know the mode.** During a rolling upgrade two builds run side by side,
  and a build that does not know about custody would read a held account as mirrored — on meeting its source emptied it
  would apply the account's `RemotelyDeletedEmailDisposition` to every drained message. The check reads the work-lease
  rows: a replica that knows the mode stamps its build into the hold it takes, so a live lease carrying no build is a
  replica to upgrade. Finish the rolling upgrade and ask again. The reading is bounded, so a deployment holding more
  live leases than it covers is refused for the same reason rather than accepted on a partial reading — ask again once
  fewer scopes are held. A replica holding no lease at all is invisible to the check, which is the honest limit of it:
  the upgrade order still matters.
- **The account synchronizes a folder playing a virtual role** — `All`, `Flagged`, or `Important`. Such a folder
  presents messages that are occurrences of other folders, so holding it would store each of them twice and draining it
  would remove mail from folders nobody asked about. Providers whose folders are labels over one store are not what
  this mode is for.
- **Switching off, a folder mapping names nothing the source could hold.** Before the mailbox is appended back, every
  mapping's remote path has to be valid and either name a folder the source advertises or be one the mapping's
  `CreateIfMissing` permits. While any fails, nothing starts.

## What the drain does, and what it refuses to do

The drain runs at the end of each synchronization run of a held account, under the lease that run already holds, over
the account's one write connection. It never runs on a client, MCP, or rule request, so how fast the source server is
decides how soon it is emptied and never how long anybody waits.

**Only the folders the account currently synchronizes are ever named.** A command goes out against a folder the
account's configuration maps, mirrors, and gives none of the three virtual roles, and against no other. A folder whose
`Synchronize` was switched off, and a folder no mapping names any more, are answered the same way a virtual one is: what
they stored is kept rather than erased, nothing on a stored row says which of the three it was, and their mail stays on
the source and goes on being counted under *Awaiting drain* — erasing the local copy of that folder is what ends that,
and such an erasure asks the source for nothing.

**A message leaves its source only once all of this is true:**

1. its row is committed, carrying the occurrence about to be expunged;
2. the row says its payload is stored;
3. **the stored bytes have been read back and matched against the length and SHA-256 digest recorded for them**;
4. the folder still reports the `UIDVALIDITY` the occurrence names.

Points 1, 2, and 4 are re-established immediately before the command goes out, from a fresh read of the row and the
folder the command is about to name. Point 3 is established once per message and recorded: a payload that has been read
back and matched is a fact about those bytes, so the drain records the match rather than reading the whole payload
again for every batch a message survives to.

A message that fails any of them stays on the source and is counted under the reason it failed. Two of those reasons
are ordinary and not faults: a message whose payload exceeded `MaxRawMimeBytes` was never stored at all, and one
waiting for storage headroom is stored once the ceiling has room. Both stay on the source indefinitely, which is the
right answer — the alternative is destroying the only copy of a message this deployment does not hold.

**The command is always `UID STORE +FLAGS (\Deleted)` followed by `UID EXPUNGE` naming exactly the UIDs the gate
passed.** A bare `EXPUNGE` is never issued, so a message somebody else flagged `\Deleted` in another client is not
swept up by MailFathom's own removal. A source that does not advertise RFC 4315 `UIDPLUS` cannot serve `UID EXPUNGE`
and is not drained.

Both commands are idempotent against the UIDs they name, and the occurrence on the row is cleared only after the source
has answered. A process that dies in between leaves the occurrence standing, the next pass issues the commands again,
and synchronization meeting that UID in the meantime recognizes the row it already has rather than storing the message
twice. The same property is what makes two replicas overlapping across a lease handover safe.

### Mail erased before the drain reached it

Erasing a message on a held account deletes the local row, which is the only record of where that message was on the
source. So the erasing transaction writes a **source removal record** — a folder, a `UIDVALIDITY`, and a UID, and
nothing from the message — and the drain expunges from it afterwards. The request that asked for the erasure never
waits on a mail server and never fails because one is unreachable.

A record is written for a folder the source keeps messages in, and for no other. Erasing what is stored of a folder
playing a virtual role writes none, because the UIDs such a folder presents are occurrences of messages the source
keeps in other folders and expunging one would take mail out of a folder nobody asked about; an alias no mapping names
any more is answered the same way, since nothing is left to say which of the two it was. What those erasures leave is
mail standing on the source rather than a command against mail nobody asked about.

### Mail that was already stored

An account switched on after years of mirroring has a mailbox already stored. It is drained under exactly the same gate
as mail that arrives afterwards: the pass takes the oldest messages first, so a long mailbox empties from the end
nobody is reading, and each one is read back and matched before its source copy goes.

## What it costs, and how to watch it

One run takes at most [`MailSynchronization:MaxDrainedEmailsPerRun`](../operations/configuration-mail.md) messages off
the source, in commands naming at most `MailSynchronization:MaxDrainedEmailsPerCommand` UIDs each. The two halves of a
pass share that one budget rather than each getting it: the source copies of mail an erasure already took locally are
spent first, because that is an erasure finishing rather than a mailbox emptying, and the stored mailbox is drained
with what is left. That is what keeps emptying a mailbox of years from crowding out the synchronization it sits behind,
and it is why a large mailbox takes days rather than minutes. Raising the first empties a source sooner and lengthens
that account's runs.

`mfctl account custody show` is the operator's view of progress, and it is the only view there is: a drained message
looks exactly like a message that was never on the source. Beside it, these instruments are published per account, and
carry no subject, address, folder path, or UID:

| Instrument | What it counts |
| --- | --- |
| `mailfathom.mailbox.drain.drained` | Messages whose source copy MailFathom removed once it verifiably held them |
| `mailfathom.mailbox.drain.removed_erased` | Messages erased locally whose source copy the drain removed afterwards |
| `mailfathom.mailbox.drain.held_back` | Messages the gate left on the source, broken down by the reason it refused them |
| `mailfathom.mailbox.drain.failed_batches` | Batches whose commands the source did not serve, broken down by what refused them, each attempted again by the next run |
| `mailfathom.mailbox.drain.abandoned_batches` | Batches abandoned before a command went out because the folder reported another `UIDVALIDITY` |

A drain that fails never puts the account into backoff and never fails its run. What it could not take off the source
this time it takes next time, and answering an unreachable source by reading the account's mail less often would be the
wrong trade for everybody using it.

## What changes about the rest of the product

- **The account's `RemotelyDeletedEmailDisposition` is not consulted while the mailbox is held.** A message gone from
  the source is the drain's own work completing rather than somebody deleting mail, so the phase answers what a
  disappearance means. The configured value goes on saying what it says, and applies again once the account is mirrored.
  This is why the switch does not refuse an account configured to erase the local copy.
- **Every synchronized folder is drained, including sent, drafts, junk, and trash.** A folder whose mapping does not
  synchronize is never held and never drained.
- **A `UIDVALIDITY` change on a held account's source duplicates rather than loses.** Rows still carrying an occurrence
  under the old value lose it without an expunge, and the messages the folder now reports are discovered as arrivals.
  Mail the drain had not yet reached is therefore stored twice; recognizing it by content instead would be a guess, and
  a duplicate somebody deletes is recoverable where a loss is not.
