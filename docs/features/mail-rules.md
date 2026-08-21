# Mail rules

<!-- describes: backend/src/Application/Rules/**, backend/src/Infrastructure/Rules/**, backend/src/Infrastructure/Persistence/Rules/**, backend/src/Host/Configuration/Rules/** -->

A mail rule selects mail and changes it. It is a name, a condition, the accounts it applies to, the occasions that run
it, what a match leads to, and whether a match ends the pass, and an owner writes it in the configuration their
deployment already carries. This
page documents both halves: every fact a condition can read, every function and operator available to it, the limits it
is read and run under, the order a set of rules is evaluated in, and every change a matching rule can ask for.

Rules live in configuration rather than in a table, and a condition is one expression rather than a nested structure of
predicates. [ADR 0010](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0010-rule-authoring-in-configuration-and-ncalc-conditions.md)
records both decisions and what each one costs. Nothing creates, edits, or deletes a rule at run time: not `mfctl`, not
MCP, not the administrative endpoint. An owner who wants to change what their instance selects edits a file.

## Writing a rule

```json
{
  "MailRules": {
    "Rules": [
      {
        "Name": "supplier-invoices",
        "Accounts": [ "work" ],
        "Condition": "senderDomain == 'supplier.test' and attachmentCount > 0",
        "Triggers": [ "Arrival" ],
        "Actions": { "MoveTo": "invoices", "MarkAsRead": true },
        "StopWhenMatched": true
      },
      {
        "Name": "old-newsletters",
        "Condition": "contains(subject, 'newsletter') and ageInDays > 90",
        "Enabled": false
      }
    ]
  }
}
```

`Name` is what a rule is reported by, and it is restricted to letters, digits, spaces, and `.`, `_`, `-`. That
restriction is deliberate: everything else about a rule may carry an address its author typed, and the name is the one
part that can be promised to carry no such thing when it reaches a log line.

`Enabled` defaults to `true`. A rule switched off is left out of the bound set entirely, so it costs nothing and
changes the set's revision exactly as deleting it would.

`Triggers` is what decides when a rule runs, and it defaults to nothing: a rule that should run over arriving mail says
`[ "Arrival" ]`, and a rule that says nothing is one a whole-mailbox run applies. [The section
below](#which-triggers-run-a-rule) states what each way of writing it means, and why it is a different statement from
`Enabled`.

`Schedule` is when a rule declaring the `Schedule` trigger runs, written as `Every <interval>` or
`Daily at <HH:mm> [<zone>]`. The key and the trigger are one declaration: neither is written without the other, and
[running a rule on a schedule](#running-a-rule-on-a-schedule) states the syntax, what happens to an occasion that
passed while nothing was running, and how such a run differs from one an owner asks for.

## Which accounts a rule applies to

`Accounts` is the filter, and it is the one part of a rule that says which mail reaches it at all rather than which mail
matches it.

- **Naming no account is a rule for every account.** That is what a single-account deployment writes, and what a rule
  about a sender rather than about a mailbox usually wants.
- **Naming one or more accounts narrows the rule to exactly those.** Mail from any other account never reaches the
  condition — the rule is not evaluated and records no outcome, because it is not that account's rule rather than a rule
  that declined to match. It follows that a scoped rule cannot end another account's pass whatever `StopWhenMatched`
  says.

An account is named exactly as `MailSynchronization:Accounts:<n>:AccountId` declares it, and the comparison is
case-sensitive, because two identifiers differing only in case are two accounts there. **An account nobody declared is
refused when the configuration is read**, naming the rule and the identifier: a rule scoped to a mistyped account would
otherwise reach no mail and say nothing about why.

The `account` fact stays available and is a different tool. The filter decides whether a rule runs; the fact lets one
rule that does run say something about which account it is running for — `account == 'work' ? … : …` inside a condition
that applies to several.

[Mail configuration](../operations/configuration-mail.md#mailrules) lists every key of the section with its
type, default, and constraint.

## Which triggers run a rule

`Triggers` is the list of automatic occasions a rule takes part in. It governs automatic firing and nothing else, so it
decides *when* a rule is reached rather than which mail it matches once it is.

| Written | What the rule takes part in |
| --- | --- |
| `[ "Arrival" ]` | Every message the account's synchronization run commits |
| `[ "Schedule" ]` | The occasions the rule's own `Schedule` names, each of them a walk of the whole mailbox |
| `[]` | No automatic occasion at all: nothing fires the rule, and a whole-mailbox run is what applies it |
| the key left out | The same as `[]` — a rule takes part in the occasions it names and in no others |

**`Arrival` is a message the account's synchronization run has just committed.** It is named after the moment rather
than after the transport, because `Push` already names a folder's
[synchronization mode](imap-synchronization.md#choosing-the-mode-per-folder) and a rule is unaffected by which one its
account uses: mail a polled run commits reaches this trigger exactly as mail a watched one commits does.

**A rule that names no trigger runs on no arriving message.** Leaving the key out is a statement rather than an
omission, and it is the same statement as writing `[]`: the rule is bound, validated, and reported, and a whole-mailbox
run is what applies it. So a rule meant to file mail as it arrives writes `Arrival` — there is no occasion a rule joins
without naming it.

**Such a rule is one you run rather than one that runs**, which is what housekeeping wants — file everything
older than a quarter, delete what a mailing list left behind — where firing on each arriving message is either useless
or exactly what the owner is afraid of. [A whole-mailbox
run](#running-the-rules-over-mail-you-already-have) is what applies it, and [a
schedule](#running-a-rule-on-a-schedule) is how the same rule gets applied without anybody asking each time.

**A rule a trigger does not reach is not evaluated and records no outcome**, exactly as a rule scoped to another
account is not: it did not decline to match, it was not one of that pass's rules. It follows that such a rule cannot end
a pass either, whatever `StopWhenMatched` says.

**A run somebody asks for is never a member of the list**, and it applies the whole rule set including the manual-only
rules. Somebody asking for a run is the request itself, so a rule declining to run because it had not agreed to be asked
would be surprising in the one place surprise is least affordable. `Schedule` is a member of the list for the opposite
reason: nobody is asking, so the rule has to have said in advance that it wanted to be run. No run selects rules by name
either: what a run applies is the set the configuration declares, narrowed only by what started the run.

**A name this system does not recognize is refused when the configuration is read**, naming the rule and the value, and
so is the same trigger written twice, because the value is a set. Neither is dropped: a list whose only entry was
mistyped would otherwise arrive as an empty one, which would silently turn an automatic rule into a manual one — and a
rule that never fires is indistinguishable from a rule nothing matched. The name is read the way the binder reads every
other closed vocabulary this configuration declares, so `arrival` and `Arrival` are one trigger.

### Running a rule on a schedule

A rule declaring the `Schedule` trigger names its own occasions in a `Schedule` key, and each occasion is a walk of the
whole mailbox rather than anything about one message:

```json
{
  "Name": "archive-old-newsletters",
  "Condition": "contains(subject, 'newsletter') and ageInDays > 90",
  "Triggers": [ "Schedule" ],
  "Schedule": "Daily at 03:00 Europe/Warsaw",
  "Actions": { "MoveTo": "archive" }
}
```

**The syntax is MailFathom's own, and it accepts exactly two forms.** It is deliberately not cron: an owner writing
when their own mailbox is tidied needs an interval or a time of day, and the four fields cron would add are ones nothing
here would honour to the minute anyway.

| Written | What it means |
| --- | --- |
| `Every <interval>` | An occasion every interval, where the interval is `hh:mm:ss` or `d.hh:mm:ss` — `Every 06:00:00`, `Every 7.00:00:00` |
| `Daily at <HH:mm>` | One occasion a day at that time of day, read in UTC |
| `Daily at <HH:mm> <zone>` | The same, read in a named time zone — `Daily at 03:00 Europe/Warsaw` |

- **The keyword is read case-insensitively and repeated spaces are ignored**, so `daily at 03:00` and `Daily at 03:00`
  are one schedule and the rule set's revision does not move between them.
- **An interval is at least one minute and at most 365 days.** A shorter one would ask for a walk of the whole mailbox
  more often than the queue is polled; a longer one is a date rather than a recurrence.
- **A time of day is written as `HH:mm` on a 24-hour clock**, zero-padded — `03:00`, not `3:00`.
- **The zone is a time-zone identifier the host recognizes**, so a deployment writing one gets that zone's wall clock
  including its daylight-saving changes. Leaving it out means UTC, which is what an owner reads off the declaration
  without knowing where the container runs.
- **Anything else is refused when the configuration is read**, naming the rule and what could not be read — a cron
  expression, an unpadded time, a zone nothing knows. A schedule silently dropped would leave a rule that never runs,
  which is indistinguishable from a rule nothing matched.

**Occasions are anchored rather than counted from the last run.** `Every 06:00:00` means midnight, 06:00, 12:00, and
18:00 UTC whatever time the instance started, so restarting the process does not shift a schedule and two instances
reading one configuration agree on when its occasions are.

**A local time daylight saving skips happens when the gap ends, and one it passes through twice happens once.** A rule
at `Daily at 02:30 Europe/Warsaw` runs at the instant the clock reaches 03:00 on the spring-forward day, and on the
autumn day it runs at the first 02:30 rather than at both.

**An occasion that passed while nothing was running is skipped rather than replayed.** Whatever a process being down,
a queue being full, or a previous run still walking cost, the dispatch takes the most recent occasion and steps over the
ones before it — a mailbox does not want six walks because the host was off for a day, and each of those walks would
apply the same rules to the same mail. What the skip leaves is a count:
`mailfathom.jobs.schedule.skipped_occurrences`, broken down by why they were passed over, beside
`mailfathom.jobs.schedule.dispatches` for the decisions themselves. [Telemetry](../operations/telemetry.md#durable-background-work)
lists both.

**One run per schedule at a time.** An occasion arriving while that schedule's previous run is still in the queue or
still being walked is answered with the run already under way rather than starting a second one, and it counts as
skipped. It follows that a schedule shorter than the walk it asks for does not pile up; it runs as often as the mailbox
allows.

**A scheduled walk reaches the rules that declared a schedule, and no others.** That is the one place it differs from a
whole-mailbox run an owner asks for, which applies the entire rule set including the rules declaring no trigger at all.
Both walk the same mail, are bounded by the same two settings, run as a step of the account's synchronization run, and
are bound to the rule-set revision they started under.

**An owner's request replaces an outstanding scheduled run** rather than being answered with it, because the request
reaches every rule and the scheduled run reaches only some of them. A schedule's occasion arriving while any run is
outstanding stands down, whichever started it.

**A scheduled run is distinguishable from the other two.** The run says what started it — `mfctl rules run-status`
prints `under way, started by ScheduledRun` — and every outcome it records carries the trigger `ScheduledRun`, apart
from `Arrival` for the walk of arriving mail and `RequestedRun` for the one somebody asked for. [Finding out what a rule
did](#finding-out-what-a-rule-did) is where those records are read.

**A rule scoped to accounts has one schedule per account it reaches**, so a mailbox behind on its walk does not delay
another's. Two rules declaring different schedules for one account are two schedules, and either occasion walks the
mailbox for every scheduled rule that account has — the walk is per mailbox rather than per rule, so a rule whose
occasion has not come round yet is still reached by one that has. Where that matters, give the rules the same schedule
or put them on different accounts.

### `Triggers: []` and `Enabled: false` say different things

The two keys sound alike and are not:

| | `Triggers: []` | `Enabled: false` |
| --- | --- | --- |
| In the bound rule set | Yes | No |
| Validated when the configuration is read | Yes | Yes, apart from its condition |
| Run when mail arrives | No | No |
| Run by a whole-mailbox run | Yes | No |
| What it is for | A rule an owner applies deliberately | A rule taken out of service without deleting the condition |

## What a matching rule does

A rule declares what a match leads to in an `Actions` block, one named key per change:

| Key | Value | What a match asks for |
| --- | --- | --- |
| `MoveTo` | a folder alias or role | The message leaves the folder it matched in and is filed into the named one |
| `CopyTo` | a folder alias or role | A second copy of the message is placed in the named folder, and the one that matched stays where it is |
| `Delete` | `true` | The message is removed from the folder it matched in |
| `MarkAsRead` | `true` or `false` | The message's remote `\Seen` flag is set, or cleared |
| `MarkAsFlagged` | `true` or `false` | The message's remote `\Flagged` flag is set, or cleared — the star a mail client draws |
| `AddKeywords` | a list of keywords | Each named keyword is put on the message, beside the ones it already carries |
| `RemoveKeywords` | a list of keywords | Each named keyword is taken off the message, leaving the ones the rule did not name |
| `SetKeywords` | a list of keywords, possibly empty | The message ends up carrying exactly the named keywords and no others |

**An absent key is a change the rule does not ask for**, which is why `MarkAsRead` carries a value rather than being a
switch: `MarkAsRead: false` is a rule asking for mail to be marked *unread*, and leaving the key out is a rule that does
not touch the flag at all. `MarkAsFlagged` reads the same way. `Delete: false` says the same thing as leaving `Delete`
out. The distinction is sharpest on `SetKeywords`: writing `[]` is a rule asking for every keyword to be cleared, which
is the one thing the other two keyword keys cannot say however many keywords they name, while leaving the key out is a
rule that does not touch keywords at all. One key per action rather than a list of action objects, because a rule
declaring the same action twice is then unrepresentable rather than merely refused, and because a binder drops a list
element whose value it cannot read — which would leave a rule quietly doing less than its file says.

**A rule that declares no action is not a defect.** It selects mail and changes nothing, which is what a rule ending the
pass with `StopWhenMatched` does to keep the mail it names away from the rules below it.

A destination is a **folder alias** — one an account declares under `MailSynchronization:Accounts:<n>:Folders` — and
never a path on the server. What that alias is bound to is resolved when the change is written down, so a rule goes on
working across a server that renames the folder underneath it, and a rule may name any folder the account maps —
including one it deliberately does not mirror, which is how mail is filed somewhere MailFathom keeps no copy of.
[Folder aliases and discovery](imap-synchronization.md#folder-aliases-and-discovery) states what a binding is
and when it moves.

A destination may instead name the **role** the folder plays, written `role:<role>` — `role:Junk`, `role:Archive`, and
the rest. That is what lets one rule file mail correctly across accounts whose folders you named differently, since the
role is asked of the account the mail belongs to. Anything without the `role:` prefix is an alias, so an alias spelled
`Junk` still means that alias. Startup refuses a role no folder of a reached account carries, exactly as it refuses an
alias the account maps nothing for, and refuses text that reads as neither an alias nor a role, naming the roles that
exist.
[What a role says, beside how a folder is found](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found)
states what a role is and why an account has at most one folder per role.

### What a keyword may be

A keyword is a label a mail server stores on a message beside its flags, and mail clients show them as tags or labels.
The text is yours to choose, and the convention worth following is the one clients already use — a leading `$`, as in
`$Todo`, `$Invoice`, `$Waiting` — but nothing here requires it.

What IMAP requires is that a keyword be a single unbroken token, so a keyword may not contain a space, a control
character, any of `( ) { % * " \ ]`, or anything above US-ASCII — `café` is refused for that last reason and `cafe` is
not. One that does is refused at startup, naming the rule and the key it was written
in, because a keyword that cannot be sent is a rule that cannot do what it says. A keyword is at most 64 characters and
a rule names at most 64 of them in one key, which are the same bounds MailFathom reads a message's keywords under.
Keywords are compared without regard to case, so naming both `$todo` and `$Todo` is naming one keyword — and the
spelling you wrote is the one sent to the server.

`AddKeywords: []` and `RemoveKeywords: []` are refused at startup, because a list naming nothing asks the server for
nothing and is a mistyped list far more often than an intent. `SetKeywords: []` is the one that means something.

**A condition reads keywords as well as writing them.** [`keywords`](#the-facts-a-condition-can-read) is a fact, so a
rule can select on a label an earlier run put on a message, under the same case-insensitive comparison.

What it reads is what the server last reported, never what a rule asked for. A keyword a rule declares in this pass is
a change written down: the account's next run carries it to the server, and the fact carries it once
[reconciliation](imap-synchronization.md#reconciling-against-the-server) has read the message's flags back. So a rule
below one that adds `$Invoice` does not see `$Invoice` on that message in the same pass, which is deliberate — a fact
that answered from a change nobody had performed yet would report labels the mailbox does not carry.

**A server may refuse to keep a keyword**, and that is the one refusal that cannot be reported at startup. A folder
tells MailFathom which flags it keeps permanently when it is opened, so a server that keeps no arbitrary keyword — some
do not — is discovered as the change is issued, and `AddKeywords` and `SetKeywords` then fail with
`MailboxMutationUnsupported` naming the account and the folder alias rather than being accepted and forgotten.
`RemoveKeywords` is never refused for that reason: taking a keyword off a message that carries one is meaningful
whatever the folder keeps.

### Which combinations a rule may declare

At most one action decides where the matched occurrence ends up — a relocation, a copy, or a deletion — and a deletion
admits nothing beside it. Beyond that, a rule may declare any mixture of flag and keyword changes, with one exception:
`SetKeywords` states the whole set, so nothing else about the same message's keywords may be declared beside it.

| Combination | Verdict | Why |
| --- | --- | --- |
| `MoveTo` alone | permitted | |
| `CopyTo` alone | permitted | |
| `Delete` alone | permitted | |
| `MarkAsRead` alone | permitted | |
| `MarkAsFlagged` alone | permitted | |
| `MarkAsRead` and `MarkAsFlagged` | permitted | Two different flags, so neither decides anything about the other |
| `AddKeywords` and `RemoveKeywords` | permitted | The removal is issued first, so a keyword named by both ends up on the message |
| `SetKeywords` alone, including `[]` | permitted | An empty list is what clears every keyword |
| `MoveTo` and `MarkAsRead` | permitted | The flag is written first, on the occurrence the condition matched |
| `CopyTo` and `MarkAsRead` | permitted | The same: the flag is written before the copy is placed |
| `MoveTo`, `MarkAsFlagged`, and `AddKeywords` | permitted | Every flag and keyword change is written before the message is filed |
| `MoveTo` and `CopyTo` | refused | Two fates for one occurrence; whichever ran second would act on a message no longer where the rule matched it |
| `MoveTo` and `Delete` | refused | The same, and the deletion would undo the filing |
| `CopyTo` and `Delete` | refused | The same |
| `Delete` and `MarkAsRead` | refused | A flag written on a message being removed is a flag nobody will ever read |
| `Delete` and `AddKeywords` | refused | The same |
| `SetKeywords` and `AddKeywords` | refused | Two answers about one set of keywords; which one held would come down to which ran second |
| `SetKeywords` and `RemoveKeywords` | refused | The same |

**A refused combination is refused where it is written.** Startup fails naming the rule, the key, and which action could
not be honored beside which, rather than a run resolving it against a mailbox — no resolution invented at run time would
be the one the operator meant, and a rule that resolved differently depending on what a server answered would not be a
rule.

### The order they are applied in

The actions of one rule are applied in **MailFathom's order rather than the order they were written in**, so that every
permitted combination acts on the occurrence the condition matched, and so that the answer is the same on every run and
on every instance:

1. `MarkAsRead`
2. `MarkAsFlagged`
3. `SetKeywords`
4. `RemoveKeywords`
5. `AddKeywords`
6. `CopyTo`
7. `MoveTo`
8. `Delete`

The same order governs the actions of two rules that both match one email, which no single rule's block could order on
its own.

Only two positions in that list decide anything observable. Every flag and keyword change comes before `CopyTo`,
`MoveTo`, and `Delete`, so all of them act on the occurrence the condition matched rather than on one that has already
been filed elsewhere; and `RemoveKeywords` comes before `AddKeywords`, so a keyword one rule takes off and another puts
on ends up on the message. The rest is fixed for determinism — each of those actions writes a different flag, and the
one pair that would contradict another is refused before it is applied.

### How a change reaches the mail server

**A rule pass issues no IMAP command a rule asked for.** Each action a match asks for is written down as a
[durable mutation record](imap-synchronization.md#every-change-is-written-down-before-it-is-issued) inside the same
transaction that records the evaluation, and the account's own convergence pass — the first thing every account run does
— is what issues the IMAP commands. The one server call a pass makes for itself is
[finding a folder the account maps and does not mirror](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is),
made before the batch's transaction is opened and only where a rule files into such a folder. Three things follow, and
each is the point of the arrangement:

- **A pass that fails costs no mail server work.** Nothing the evaluation itself decided is carried remotely, so a local
  failure never defers the account's fetching.
- **A change survives a restart.** A record written and not yet carried is picked up by the next run, and
  [a change nobody finished finishes by itself](imap-synchronization.md#a-change-nobody-finished-finishes-by-itself)
  states every way one can be left and what becomes of it.
- **Asking twice asks once.** The record's identity is the email occurrence, the mutation, and who asked — and for a
  rule, *who asked* is the rule's name together with the revision of the rule set that matched. So a whole-mailbox run
  over mail a rule has already acted on issues nothing, while an edit to the rule set is a new revision and therefore a
  fresh request. An owner who moved the message back by hand is not overruled by the rule that filed it.

**A change MailFathom made does not come back as something to act on.** A rule filing a message would otherwise meet it
in its new folder, match again, and file it again for as long as the folder is watched;
[a change MailFathom made is not a change to react to](imap-synchronization.md#a-change-mailfathom-made-is-not-a-change-to-react-to)
states how the record answers that exactly, without a cycle counter or a rate limit.

**What a deletion does to the local copy is the account's decision**, taken from `AuthoredDeleteEmailDisposition` at the
moment the request is written, exactly as a deletion somebody authored through a tool is.
[What becomes of a message MailFathom deleted itself](imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself)
states the three answers.

### What an account permits a rule to do

Each account states, under `MailSynchronization:Accounts:<n>:RuleActions`, which changes a rule may make to its mailbox:

| Key | Default | What it permits |
| --- | --- | --- |
| `Move` | `true` | A rule may file this account's mail into another of its folders |
| `Copy` | `true` | A rule may place a copy of this account's mail in another of its folders |
| `Delete` | `false` | A rule may remove this account's mail |
| `MarkAsRead` | `true` | A rule may set or clear this account's `\Seen` flag |
| `MarkAsFlagged` | `true` | A rule may set or clear this account's `\Flagged` flag |
| `WriteKeywords` | `true` | A rule may add, remove, or replace the keywords of this account's mail |

**Deletion is opt-in and everything else is opt-out**, because deletion is the one action whose result cannot be
undone by editing a rule afterwards.

`WriteKeywords` is one switch for all three keyword actions rather than three, because permitting an addition while
refusing a removal would leave mail accumulating labels nothing is allowed to take off again.

**A rule declaring an action an account it reaches does not permit is refused when the configuration is read**, naming
the rule, the action, and the account. It is refused rather than skipped for the reason a mistyped account identifier
is: a rule that silently did less on one account than on another would be indistinguishable from a rule that never
matched there. An unscoped rule reaches every declared account, so the remedy is either to permit the action on that
account or to narrow the rule's `Accounts` filter to the accounts that permit it.

**Withdrawing a permission takes effect at the next pass, not at the next edit of the rules.** The two sections reload
independently, so narrowing what an account permits leaves a rule set nobody edited in force; the permission is
therefore read again when each change is written down, and an action it now refuses is
[a failed action](#when-a-change-cannot-be-made) rather than one more deletion. The rule itself stays as written and is
refused the next time the rule section is read, which is the message that says what to edit.

### When a change cannot be made

Validation settles what a rule may declare, so what is left is what only the mailbox in front of the pass can cause.
Each is recorded against the rule that asked, and the actions beside it are still carried:

| Reason | What happened |
| --- | --- |
| `DestinationFolderUnresolved` | The destination names a folder the account mirrors and no run of that folder has bound it yet |
| `DestinationFolderUnmapped` | No mapping of the account answers to the name — one was withdrawn between the rule set being read and the change being written |
| `DestinationFolderNotAdvertised` | The mapping is there and the server holds no folder for it: the folder was deleted or renamed, the path was never right, or one the mapping asked to have created could not be |
| `DestinationFolderAmbiguous` | The mapping names a role that two advertised folders carry, so which one was meant is yours to state |
| `AccountNoLongerConfigured` | The account was withdrawn from the configuration between the rule set being read and the change being written |
| `ActionNoLongerPermitted` | The account has stopped permitting this action since the rule set that declares it was read |

Nothing is written down in any of these cases: filing into whichever folder looked closest to the name is precisely
what a stale destination must not do. The account run reports how many changes it asked for, how many it withheld because
another matching rule had already settled the same message, and how many named something that no longer resolves,
together with the rules involved. Counts and rule names only — nothing derived from a message reaches a log line, a
metric, or a span.

## The facts a condition can read

A condition reaches these twenty-six names and nothing else. Each carries one shape of value, which is what the
comparison, operator, and function checks below are made against.

| Fact | Type | What it holds |
| --- | --- | --- |
| `account` | text | The configured alias of the account the email belongs to |
| `folder` | text | The configured alias of the folder the email occurrence is in |
| `folderRole` | text | The [special-use role](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found) that folder plays — `Inbox`, `Junk`, `Archive` and the rest; absent when its mapping names none |
| `subject` | text | The subject line; absent when the email carries none |
| `senderAddress` | text | The sender's address in its comparison form; absent when the email names no sender |
| `senderDomain` | text | The part of the sender's address after the at sign; absent when there is no sender |
| `recipientAddresses` | text set | The addresses the email was sent to and copied to, in their comparison form |
| `recipientDomains` | text set | The distinct domains of every recipient address |
| `authorAuthentication` | text | What was established about the author the email displays — `authenticated`, `failed`, or `notEstablished`. Reached by the receiving server, or by local DKIM verification where that server wrote nothing; a rule reads the conclusion and not which of the two produced it |
| `senderTrust` | text | Whether this deployment recognizes that author — `trusted` or `unknown` |
| `receivedAt` | timestamp | When the last receiving hop recorded the email; absent when no hop recorded one |
| `sentAt` | timestamp | When the sender's client stamped the email; absent when it carries no such header |
| `ageInDays` | number | Days since the email was received; absent when nothing recorded that |
| `sizeInBytes` | number | The size of the whole email as the server reported it |
| `attachmentCount` | number | How many attachments the email carries |
| `attachmentTotalBytes` | number | The size of every attachment added together |
| `isEncrypted` | boolean | Whether the email's body is encrypted |
| `carriesUnverifiedSignature` | boolean | Whether a signature part is present; nothing has verified it |
| `isSeen` | boolean | Whether the server reports the email as read |
| `isAnswered` | boolean | Whether the server reports the email as answered |
| `isFlagged` | boolean | Whether the server reports the email as flagged |
| `isDraft` | boolean | Whether the server reports the email as a draft |
| `keywords` | text set | The keywords the server reports the email as carrying; empty when it carries none |
| `hasExtractedContent` | boolean | Whether text has been extracted from the email's body |
| `bodyText` | text | The text extracted from the email's body, after quoted history and signatures were removed; absent while no extraction has run for it |
| `machineAuthorship` | text | How much the email's own text reads as machine written — `likely`, `possible`, `unlikely`, or `notAssessed` |

Names are case-sensitive. `senderDomain` is a fact and `SenderDomain` is not, so the surface documented here and the
surface accepted are the same one.

**Every fact resolves only if the condition names it, and once per email however many rules name it.** Twenty-four of
the twenty-six come from metadata a pass already holds, so they cost nothing to read. `folderRole` is read from
configuration, which costs no read either. `bodyText` is the exception: it reads
stored content, which is why a rule set naming it nowhere pays for no read at all, and one naming it in five conditions
pays for one. The boolean operators short-circuit, so a condition whose first half already decides it never resolves
what its second half would have named.

**An absent fact answers rather than failing.** A message with no subject compares unequal to every subject, matches no
`contains`, and reports `isNull(subject)` as true. Guarding a comparison is a choice about what the rule should mean,
never a requirement.

**A timestamp is always in UTC.** A date literal is written `#yyyy/MM/dd#` and carries no offset of its own, so
`receivedAt >= #2026/01/01#` means the start of that day in UTC.

**Text comparison ignores case and is ordinal.** `senderDomain == 'Supplier.TEST'` matches `supplier.test`, and it does
so identically on every instance whatever locale its host is set to. That is what makes `keywords` symmetrical with the
keyword actions: a rule that put `$Todo` on a message is selected by a later rule naming `contains(keywords, '$todo')`.

**Three of the facts are verdicts this deployment stored when the email was extracted**, and reading one re-evaluates
nothing: no DNS is resolved, no header is re-read, and no text is re-assessed. So a condition names what was concluded
at the time rather than what the same policy would conclude now, and mail stored before a verdict existed reads as the
value that says nothing was established — `notEstablished`, `unknown`, and `notAssessed` — until
[`mfctl mailbox rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) fills it in.
[Sender authentication](sender-authentication.md) and [machine authorship](machine-authorship.md) state what each value
means and what it is worth.

**`senderTrust` says little on its own and is written beside `authorAuthentication`.** `unknown` is the ordinary state
of legitimate mail from a correspondent nobody has named, so a rule acting on it alone acts on most of a mailbox; what
carries a real statement is `authorAuthentication == 'failed'`, which is a receiving server reporting that the
displayed author did not satisfy their own domain's published policy.

**`machineAuthorship` publishes the band and not the number behind it.** The likelihood a reader is shown is a
heuristic comparable only within one weighting, so a rule written against a threshold would change meaning the next
time that weighting moved. `likely` is not an accusation and warrants no action on its own — a rule acting on it is
filing mail, not judging anybody.

## Operators

| Operator | Written | Takes | Produces |
| --- | --- | --- | --- |
| Conjunction | `and`, `&&` | two booleans | boolean |
| Disjunction | `or`, `\|\|` | two booleans | boolean |
| Negation | `not`, `!` | a boolean | boolean |
| Equality | `==`, `!=` | two values of one type, other than a text set | boolean |
| Ordering | `<`, `<=`, `>`, `>=` | two numbers, or two timestamps | boolean |
| Membership | `in`, `not in` | text, a number, or a timestamp, then a parenthesized list of two or more of its type | boolean |
| Arithmetic | `+`, `-`, `*`, `/`, `%` | two numbers | number |
| Sign | `-`, `+` | a number | number |
| Choice | `? :` | a boolean, then two values of one type | that type |

Grouping is by parentheses and precedence is the conventional one, so
`a == 1 or b == 2 and c == 3` reads as `a == 1 or (b == 2 and c == 3)`. Write the parentheses.

`in` needs a list of at least two values — `folder in ('inbox', 'archive')`. A single parenthesized value is not a list,
and `folder in ('inbox')` is refused with a message saying so; write `folder == 'inbox'`.

A text set — `recipientAddresses`, `recipientDomains` — is neither compared nor tested with `in`, because a set has no
single value to compare. `contains` is how a set is asked about a member, and both refusals say so.

**Everything else the expression language offers is refused when the configuration is read**, including the bitwise and
shift operators, `**`, the null-coalescing `??`, the pattern-matching `like`, and the factorial `!` suffix. The last two
are refused for cost rather than tidiness: a factorial turns a short expression into arbitrarily large arithmetic, and a
pattern match takes an authored pattern and runs it against mail content.

## Functions

| Function | Takes | Produces |
| --- | --- | --- |
| `contains(searched, term)` | text or a text set, then text | boolean |
| `startsWith(text, prefix)` | two texts | boolean |
| `endsWith(text, suffix)` | two texts | boolean |
| `isNull(value)` | any value | boolean |
| `isNullOrEmpty(text)` | text | boolean |
| `if(condition, whenTrue, whenFalse)` | a boolean, then two values of one type | that type |
| `in(value, first, second, …)` | text, a number, or a timestamp, then one or more of its type | boolean |

`contains` is the one function that reads two shapes: over text it looks for a substring, and over a text set it looks
for a member equal to the term. Both are literal searches rather than pattern matches, which is what keeps the cost of a
text condition the length of the two strings and nothing else.

**No other function is available**, and naming one is refused when the configuration is read rather than at evaluation.
The expression language ships a mathematical library — `Sqrt`, `Max`, `Round` and the rest — and none of it is part of
this surface.

## Limits

| Limit | Default | What it bounds |
| --- | --- | --- |
| `MaxConditionLength` | 1000 characters | How long one condition may be written |
| `MaxConditionNestingDepth` | 16 levels | How deeply the parsed condition may nest |
| `ConditionEvaluationTimeout` | 1 second | How long one condition may take, including resolving the facts it names; at most 30 seconds |
| `Rules` | 200 rules | How many rules one set may declare |

The first two are checked when the configuration is read, so a condition over either is refused with the rule named. The
depth limit is checked twice there — once over the written text before it reaches a parser, because a parser descends
into a nesting before there is anything to inspect, and again over the parsed condition, which is what catches depth
reached through operators rather than brackets.

The timeout is checked while a condition runs, because nothing readable in the text says how long a read of stored
content will take. It has a ceiling of its own for the reason the two written limits do: a timeout an operator meant as
milliseconds and wrote as minutes would stop bounding anything, per email.

A length limit and a depth limit are both needed: length alone would admit a short expression nested past what anyone
can follow, and depth alone would admit a flat expression of ten thousand terms.

## What a rule set is checked for

Every condition is read while the host composes itself, before any mail is seen. Reading one checks four things, and
each is refused with a message naming the rule, what was wrong, and where:

- **Syntax.** The condition parses, and a failure reports the position it failed at.
- **The names.** Every identifier is one of the twenty-six facts and every call is one of the seven functions.
- **The types.** Every comparison, operator, and argument holds between shapes that could match. `subject == 1`,
  `sizeInBytes > 'large'`, `recipientDomains == 'example.test'`, and `contains(subject, 3)` are each refused here.
- **The result.** The condition produces a boolean. A condition producing text or a number is refused rather than read
  as truthy, so what a rule means never depends on a coercion nobody wrote down.

The rule's `Accounts` filter is checked beside those four: every identifier names a declared account, none is blank, and
none is repeated. Its `Triggers` list is checked the same way: every name is one this system declares, none is blank,
and none is repeated. Its `Schedule` is checked against that list and against itself: a rule declaring the `Schedule`
trigger carries one, a rule carrying one declares that trigger, and the expression is one [this system
runs](#running-a-rule-on-a-schedule) — each refused naming the rule.

So is its `Actions` block, against the rule itself and against every account the rule reaches:

- **The destinations.** Each names something readable as a folder alias or as `role:<role>`, and each names a folder the
  account maps — a rule filing into a folder no mapping declares has nowhere to file. Mirroring is not asked about: a
  mapped folder the account does not mirror is resolved when the first change files into it.
- **The combination.** The actions are ones MailFathom applies together, per [the table above](#which-combinations-a-rule-may-declare).
- **The permissions.** Every action is one the account permits a rule to take.

Every defect in every rule is reported together, so a rule set with three mistakes is fixed once rather than three
restarts running.

**An invalid rule set fails startup**, because there is no previously valid set to fall back to.

## Reload, and what a bad edit does

The section reloads. An edit takes effect for the next pass, and a pass already running finishes against the rule set it
started with — the reload contract [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md)
defines for every group that reaches a running operation.

**An edit that does not validate is refused and logged, and the previous rule set stays in effect.** That is deliberately
not the options framework's own behaviour, which would discard the candidate silently: an owner who mistypes a fact name
would otherwise get an instance still acting under the rules their file no longer states.

## Order, and stopping

Rules are evaluated in the order they are written, skipping the ones scoped to other accounts and the ones the walk's
trigger does not reach. That order is the whole of the contract — nothing sorts, groups, or reorders a set — so two rules that both match one email produce the same
outcome on every run and on every instance.

`StopWhenMatched` on a rule that matches ends the pass, and the rules below it are not reached. It defaults to `false`.
A rule that does not match never stops a pass whatever it declares.

**Declared order also settles what two matching rules do to one email.** Their actions are gathered in that order, and
an action the ones already gathered leave no room for is **withheld** — judged by exactly the table one rule's own
actions are judged by, so two rules naming incompatible fates settle the way one rule declaring both would have been
refused. Two rules filing one email into different folders therefore file it into the folder the rule written first
names; the same two rules the other way round file it the other way; and a rule deleting an email leaves nothing for a
later rule to file. Whatever survives is then applied in the fixed order above, so a flag a later rule asked for is
still written before an earlier rule moves the message.

A withheld action is reported with the name of the rule that asked for it, because a rule that appears not to have fired
is otherwise indistinguishable from one that never matched. `StopWhenMatched` is the way to say which rule owns a
message rather than leaving it to be settled this way.

## When rules run

**Rules are evaluated as a step of the account's synchronization run, and there is no schedule of their own.** That run
already has everything a rule pass needs — one account at a time, a slot count that stops one account starving another,
a jittered backoff when something is wrong, and a shutdown that lets work in flight finish — so evaluation joins it
instead of adding a second thing to configure and watch.

The step comes after every folder has synchronized and committed, after the classification pass, and outside the
synchronization transaction entirely. Only one thing runs after it, and it runs after it because of this pass: the cut
that gives a message its passages, which waits until the rules have finished so that nothing derived describes a folder
a rule was about to move the message out of. Waiting for the rules is not by itself enough for that, because a rule
declares a move rather than performing one and the account's *next* run is what carries it to the mail server — so the
cut also passes over a message whose relocation is still converging, and cuts it once it is in the folder it ended up
in. [The arrival pipeline](../architecture/arrival-pipeline.md) draws the whole order. Two consequences of this step's own position follow that are worth stating rather than inferring. A rule
can only ever see mail the run before it has already stored, so a provider redelivering a message or a synchronization
retry cannot produce a different processing boundary than a clean run. And nothing an MCP tool does waits on a rule:
reads are served from what is already stored, and a pass neither blocks one nor is blocked by one.

**A pass reads nothing out of a folder the account does not mirror.** Such a folder keeps whatever it stored before its
synchronization was switched off, and neither the arrival queue nor a whole-mailbox run walks those rows: their flags
are whatever they were on the day the switch was flipped and nothing will ever correct them, so a rule reading one
would act on a mailbox MailFathom stopped observing. The exclusion is applied where the candidates are read rather than
after they come back, which is what keeps such a message from sitting at the head of the arrival queue forever.

**Both passes reach only the folders the account's configuration mirrors.** A rule is evaluated against mail in a folder
a mapping names and leaves synchronized, and against no other mail. Two kinds of stored mail are outside that, and both
are mail this deployment keeps: a folder whose `Synchronize` was switched off keeps what it had already stored, and a
folder whose mapping was removed keeps it as well. Neither is evaluated again, by an arrival pass or by a whole-mailbox
run, because nothing refreshes either — a rule acting on such a message would flag, file, or delete mail against a
mailbox state nobody here is still reading.
[What a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states what each of the two is and what makes both unreachable everywhere else too.

**A copy of this deployment's own outgoing mail is not arriving mail.** A message MailFathom
[filed](mail-delivery.md#the-copy-in-the-accounts-own-folders) into the account's own sent or outbox folder comes back
through the next synchronization like anything else, and it is recognized as this system's own and joined to the send
it is a copy of. Neither pass ever offers it to a rule: a rule conditioned on mail arriving would otherwise fire on
what the owner just sent, and a rule that files or deletes would act on the copy of a message the record above it still
governs. The exclusion is a column on the row, applied where the candidates are read and repeated in the queue's own
partial index, so such a message leaves the queue rather than sitting at the head of it — and it is never stamped as
evaluated, because it was not.

**Junk is not offered to the rule set at all.** Where spam classification is switched on, a message in the account's
junk folder and a message a verdict called spam are left out of both passes, so a rule set is never fired by mail
somebody else chose to send. A message no verdict has been reached about is left out too and stays a candidate, so the
pass that evaluates it is the one after classification has decided or after the message's wait has run out — which is
the same ordering everything derived from mail follows here, and it is applied where the candidates are read rather than
after they come back.
[Junk is kept out of what a deployment derives from mail](spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
holds the whole of it, including the bound on the wait and what a failed classification does. With classification off,
which is the default, nothing here is narrowed.

**A rule declaring `Arrival` applies to mail that arrives after the rule exists.** Each message is evaluated once, and
the record of that
evaluation is what takes it out of the queue the next pass reads — so editing a rule changes what happens to the mail
that arrives from then on and does nothing to the mail already in the mailbox. Mail an instance stored before it had
rule evaluation at all is recorded as evaluated when the schema is applied, for the same reason: an upgrade must not
hand a first rule set an entire mailbox's history as though all of it had just arrived. Re-running the rules over mail
already stored is the whole-mailbox run below, and it is always something somebody asks for.

**A pass is bounded, and what it leaves behind is the next run's.** It reads, evaluates, and commits `EvaluationBatchSize`
messages at a time and takes at most `MaxEvaluationBatchesPerPass` batches, so an account whose mail has never been
evaluated drains over as many runs as its size needs instead of holding up the run that fetches its mail. Each batch
commits its evaluations together with the position they account for, so a restart resumes at the message nobody read
rather than replaying a batch or stepping over one.

**A message whose body text has not been extracted yet is skipped and stays eligible.** It applies only where the rules
that walk actually runs for the account name `bodyText`, which is decided per walk — so a rule only a whole-mailbox run
applies holds nothing up on arrival, and a rule declaring `Arrival` does: such a message is left in the queue and
evaluated once its text has been
derived, rather than evaluated against a fact that would answer absent and then never reconsidered. Mail whose payload
local storage has not had headroom for is waited on the same way, because a later run fetches it as soon as the ceiling
permits. A message whose content will never yield text is evaluated now with the fact absent, because waiting for text
that is not coming would stall the queue behind it. Two messages are that: one above the size limit, which every later
run refuses for the same reason, and one whose stored payload a reader has already failed to read, which every later
run would fail to read the same way.

**A rule that cannot answer for one message costs that message's rule and nothing else.** It is recorded as a failed
rule with a reason, the rules below it still run, the remaining messages of the batch are still evaluated, and the
message is recorded as evaluated. The account is not put into backoff for it either: nothing a rule decided is carried
remotely, so fetching the account's mail less often would answer a local problem by slowing remote work that had nothing
to do with it. The next section lists the two reasons.

**Nothing scans on a timer of its own.** The account run recurs already, so anything a scan would find on arrival is
found by the `Arrival` trigger; a rule whose condition only becomes true with the passage of time — mail older than some
age — is applied by a whole-mailbox run, which the owner asks for or a rule's own
[schedule](#running-a-rule-on-a-schedule) asks for on its behalf. A schedule adds no loop and no second worker either:
its occasions are dispatched as ordinary jobs by the queue's worker, under the same capacity bounds as every other job,
and the walk still happens as a step of the account's synchronization run.

## Running the rules over mail you already have

Editing a rule is only useful if the rules can be applied to mail that arrived before the edit, and that is what a
**whole-mailbox run** is: a walk over everything stored for the folders one account mirrors, evaluating each message
again under the rules now in force.

- **One per account, and asking twice is asking once.** A request that finds a run already outstanding is answered with
  that run rather than starting a second walk of the same mailbox. The one exception is a run [a schedule
  started](#running-a-rule-on-a-schedule), which a request replaces rather than is answered with, because a request
  reaches rules a scheduled walk does not.
- **It runs where every other pass runs.** The request records that the run is wanted and nothing more; the work happens
  as a step of the account's synchronization run, bounded by the same two settings. Whoever asked is not what keeps it
  alive, so closing a terminal does not cancel a walk of a mailbox.
- **A run you ask for applies the whole rule set.** Every rule the configuration declares for the account is run,
  including the ones that declare no automatic trigger, and nothing selects a subset of them for one run. A run a
  schedule started is the one narrower case: it applies the rules that declared a schedule, which is what they opted
  into.
- **It is bound to one rule set.** The revision in force when the run starts is recorded with it, so a reload cannot
  change what the run is doing halfway through. If the rules do change while a run is outstanding, the run ends as
  **superseded** and says so, because MailFathom keeps only the rule set its configuration currently declares and
  finishing under a different one would apply two rule sets to one mailbox. Asking again re-runs under the rules now in
  force.
- **It is resumable and cancellable.** Its position is committed with each batch, so a restart or a shutdown costs at
  most the batch that was in flight, and the run finishes over as many account runs as it needs.

**`mfctl rules run --account <id>` is what asks for one**, and `mfctl rules run-status --account <id>` is where it is
watched from. Both are documented under [reading the rules, running them, and finding out what they
did](../operations/admin-endpoint.md#reading-the-rules-running-them-and-finding-out-what-they-did). Neither waits for
the walk, because the run is carried by the account's synchronization runs rather than by whoever asked.

**A rule's own [schedule](#running-a-rule-on-a-schedule) asks for one of these walks without anybody typing the
command.** Such a run is the same walk under the same bounds and is watched the same way; it differs in reaching only
the rules that declared a schedule, in saying `ScheduledRun` where a requested run says `RequestedRun`, and in giving
way to a request rather than answering it.

## Finding out what a rule did

A rule that concluded something about a message leaves a record of it: the rule, the revision it ran under, whether the
condition matched, the facts it read, and each change it asked for with what became of that change. `mfctl rules
history --account <id>` reads it, narrowed to a rule with `--rule` or to a message with `--email`. That is the answer
to both "what is this rule doing" and "why is this message here", and it is the only place either is answered — a rule
publishes nothing about itself through MCP, and a run reports counts rather than which message went where.

Three distinctions the record exists to keep:

- **Reached and answered no** is recorded; **never reached** leaves nothing at all. A rule below one that ends the pass
  is the second case, and reading it as the first is what makes a misspelled scope look like a condition that is never
  true.
- **Could not answer** is neither of those. It is recorded as failed with its [reason](#when-a-condition-cannot-answer)
  rather than folded into a non-match.
- **A change that was refused** is distinguishable from one **another rule had already settled**, and from a rule that
  simply asked for nothing. The refusal carries its classification, and the change that gave way names the rule that
  declared it.

A change that named its folder by role is recorded against the **folder it resolved to**, so the history names the alias
mail was actually filed into rather than the word the rule was written with. One that never resolved a destination —
a role this account maps no folder with, an alias nothing has bound — names no folder at all, and its
[refusal](#when-a-change-cannot-be-made) is what says why.

**The facts are recorded by name and never by value.** That a condition read `senderDomain` is kept; what the domain
was is not, and neither is a subject, a matched span, or any other value the message supplied. The revision recorded
beside them is what the expression is retrievable from, so the reasoning is reconstructible without the record becoming
a second copy of the mailbox. What a change did to the mail server is likewise not restated here: the record points at
the mutation it opened, and [what happened to that
mutation](imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default) is the
mutation's own trail to answer.

The record inherits the obligations of the mail it names. Erasing a message erases what every rule concluded about it,
and `MailRules:HistoryRetention` bounds how long the rest is kept — thirty days by default, and a window of zero keeps
it until the mail itself goes.

## When a condition cannot answer

Validation catches an unknown fact and a comparison that could never hold, so neither reaches an evaluation. What is
left is what only the email in front of the condition can cause, and each is recorded as a rule that **failed** with a
reason:

| Reason | What happened |
| --- | --- |
| `EvaluationFaulted` | The expression raised a failure while it was being evaluated for this email |
| `EvaluationTimedOut` | The evaluation, including resolving the facts it named, outlasted `ConditionEvaluationTimeout` |

A failed rule is never read as a match and never as a non-match. It did not match, so it never stops a pass, and the
pass carries on to the rules below it — one unlucky email must not stop a rule set that works from being applied.

## The revision a pass runs under

A rule set is identified by a digest over its rules, taken in declared order, rendered as twelve lowercase hexadecimal
characters. The identity is derived rather than declared, so it cannot be left stale by an edit that forgot to bump it,
and two instances reading the same file name the same revision.

What moves it and what does not:

- Changing a rule's name, its condition, its actions, its `StopWhenMatched`, the accounts it applies to, or the
  triggers it takes part in moves it.
- Adding, removing, or switching off a rule moves it.
- Writing a rule's default triggers out in full leaves it alone, because a rule that says nothing and a rule that says
  `[ "Arrival" ]` are the same rule.
- Reordering the rules moves it, because declared order is part of what a rule set means.
- Reformatting the file, reordering the keys within one rule, and changing an unrelated configuration section all leave
  it alone.

The identity carries none of the authored text and no ordering. A record naming a revision therefore holds nothing
personal that a condition contributed, and a record that has to say which of two revisions came first carries its own
timestamp rather than inferring one.

The revision is also half of what makes a change idempotent, so moving it has a consequence worth stating: a rule whose
actions were edited asks afresh for mail it has already acted on, while an unchanged rule re-evaluated under the same
revision asks for nothing it has already asked for.

## Worked conditions

A single sender:

```text
senderAddress == 'billing@supplier.test'
```

One account's mail only, which is the `Accounts` filter beside the condition rather than anything in it:

```json
{ "Name": "work-invoices", "Accounts": [ "work" ], "Condition": "contains(subject, 'invoice')", "Triggers": [ "Arrival" ] }
```

Anything from a domain, in one of two folders:

```text
senderDomain == 'supplier.test' and folder in ('inbox', 'archive')
```

Large mail with attachments that nobody has read:

```text
attachmentCount > 0 and attachmentTotalBytes > 10000000 and not isSeen
```

A domain, an attachment, and an age together:

```text
senderDomain == 'supplier.test'
  and attachmentCount > 0
  and ageInDays > 30
```

Something in the subject or, failing that, in the extracted body:

```text
contains(subject, 'invoice') or contains(bodyText, 'invoice')
```

Received this year, unless it is a draft:

```text
receivedAt >= #2026/01/01# and not isDraft
```

Mail addressed to a particular domain that carries no readable text yet:

```text
contains(recipientDomains, 'example.test') and not hasExtractedContent
```

Size measured in a unit an owner thinks in:

```text
sizeInBytes / 1048576 > 25
```

Mail a rule of your own already labelled, which is what makes a keyword worth writing:

```text
contains(keywords, '$Invoice') and not isSeen
```

Mail whose displayed author failed their own domain's published policy:

```text
authorAuthentication == 'failed'
```

Mail from nobody this deployment recognizes, whose author nothing established either:

```text
senderTrust == 'unknown' and authorAuthentication != 'authenticated'
```

Bulk-generated mail from outside, filed without asking a model anything:

```text
machineAuthorship == 'likely' and senderTrust == 'unknown'
```

## Worked rules

Filing supplier invoices and marking them read, on one account, so that the rules below never see them:

```json
{
  "Name": "supplier-invoices",
  "Accounts": [ "work" ],
  "Condition": "senderDomain == 'supplier.test' and attachmentCount > 0",
  "Triggers": [ "Arrival" ],
  "Actions": { "MoveTo": "invoices", "MarkAsRead": true },
  "StopWhenMatched": true
}
```

Keeping a copy of everything a regulator sends, without disturbing where the message itself sits or whether it has been
read:

```json
{
  "Name": "regulator-copies",
  "Condition": "senderDomain == 'regulator.example'",
  "Triggers": [ "Arrival" ],
  "Actions": { "CopyTo": "compliance" }
}
```

Putting automated notices back in front of somebody by clearing the flag, and nothing else:

```json
{
  "Name": "unread-alerts",
  "Condition": "contains(subject, 'alert') and isSeen",
  "Triggers": [ "Arrival" ],
  "Actions": { "MarkAsRead": false }
}
```

Marking mail from a handful of people for attention, leaving it exactly where it arrived:

```json
{
  "Name": "star-the-board",
  "Condition": "senderDomain == 'board.example'",
  "Triggers": [ "Arrival" ],
  "Actions": { "MarkAsFlagged": true }
}
```

Labelling mail so it can be found by tag in any mail client, without moving it out of the inbox:

```json
{
  "Name": "label-invoices",
  "Accounts": [ "work" ],
  "Condition": "contains(subject, 'invoice') and attachmentCount > 0",
  "Triggers": [ "Arrival" ],
  "Actions": { "AddKeywords": [ "$Invoice", "$Todo" ] }
}
```

Taking a label off once the thing it stood for is over, and stating the whole set rather than adding to it:

```json
{
  "Name": "settle-invoices",
  "Accounts": [ "work" ],
  "Condition": "contains(subject, 'payment received') and ageInDays > 1",
  "Actions": { "SetKeywords": [ "$Invoice", "$Done" ] },
  "Triggers": []
}
```

Deleting mail nobody needs — which the account has to permit under
`MailSynchronization:Accounts:<n>:RuleActions:Delete`, and which is refused at startup if it does not:

```json
{
  "Name": "drop-build-notifications",
  "Accounts": [ "work" ],
  "Condition": "senderAddress == 'builds@ci.example' and ageInDays > 7",
  "Triggers": [ "Arrival" ],
  "Actions": { "Delete": true }
}
```

Quarterly housekeeping nothing fires by itself, which the owner applies by asking for a whole-mailbox run:

```json
{
  "Name": "retire-old-newsletters",
  "Condition": "contains(subject, 'newsletter') and ageInDays > 90",
  "Actions": { "MoveTo": "archive" },
  "Triggers": []
}
```

The same housekeeping without anybody asking, run once a night in the owner's own time zone:

```json
{
  "Name": "retire-old-newsletters-nightly",
  "Condition": "contains(subject, 'newsletter') and ageInDays > 90",
  "Actions": { "MoveTo": "archive" },
  "Triggers": [ "Schedule" ],
  "Schedule": "Daily at 03:00 Europe/Warsaw"
}
```

Selecting mail and changing nothing, which is what a rule owning a message ahead of the rules below it looks like:

```json
{
  "Name": "leave-family-mail-alone",
  "Condition": "senderDomain == 'family.example'",
  "Triggers": [ "Arrival" ],
  "StopWhenMatched": true
}
```
