# Mail rules

<!-- describes: src/Application/Rules/**, src/Infrastructure/Rules/**, src/Infrastructure/Persistence/Rules/**, src/Host/Configuration/Rules/** -->

A mail rule selects mail and changes it. It is a name, a condition, the accounts it applies to, the occasions that run
it, what a match leads to, and whether a match ends the pass, and an owner writes it in the configuration their
deployment already carries. This
page documents both halves: every fact a condition can read, every function and operator available to it, the limits it
is read and run under, the order a set of rules is evaluated in, and the four changes a matching rule can ask for.

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

[Configuration reference](../operations/configuration-reference.md#mailrules) lists every key of the section with its
type, default, and constraint.

## Which triggers run a rule

`Triggers` is the list of automatic occasions a rule takes part in. It governs automatic firing and nothing else, so it
decides *when* a rule is reached rather than which mail it matches once it is.

| Written | What the rule takes part in |
| --- | --- |
| `[ "Arrival" ]` | Every message the account's synchronization run commits |
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

**Such a rule is one you run rather than one that runs**, which is what periodic housekeeping wants — file everything
older than a quarter, delete what a mailing list left behind — where firing on each arriving message is either useless
or exactly what the owner is afraid of. [A whole-mailbox
run](#running-the-rules-over-mail-you-already-have) is what applies it.

**A rule a trigger does not reach is not evaluated and records no outcome**, exactly as a rule scoped to another
account is not: it did not decline to match, it was not one of that pass's rules. It follows that such a rule cannot end
a pass either, whatever `StopWhenMatched` says.

**A whole-mailbox run is never a member of the list**, and it applies the whole rule set including the manual-only
rules. Somebody asking for a run is the request itself, so a rule declining to run because it had not agreed to be asked
would be surprising in the one place surprise is least affordable. No run selects rules by name either: what a run
applies is the set the configuration declares.

**A name this system does not recognize is refused when the configuration is read**, naming the rule and the value, and
so is the same trigger written twice, because the value is a set. Neither is dropped: a list whose only entry was
mistyped would otherwise arrive as an empty one, which would silently turn an automatic rule into a manual one — and a
rule that never fires is indistinguishable from a rule nothing matched. The name is read the way the binder reads every
other closed vocabulary this configuration declares, so `arrival` and `Arrival` are one trigger.

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

**An absent key is a change the rule does not ask for**, which is why `MarkAsRead` carries a value rather than being a
switch: `MarkAsRead: false` is a rule asking for mail to be marked *unread*, and leaving the key out is a rule that does
not touch the flag at all. `Delete: false` says the same thing as leaving `Delete` out. One key per action rather than a
list of action objects, because a rule declaring the same action twice is then unrepresentable rather than merely
refused, and because a binder drops a list element whose value it cannot read — which would leave a rule quietly doing
less than its file says.

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

### Which combinations a rule may declare

At most one action decides where the matched occurrence ends up — a relocation, a copy, or a deletion — and a deletion
admits nothing beside it.

| Combination | Verdict | Why |
| --- | --- | --- |
| `MoveTo` alone | permitted | |
| `CopyTo` alone | permitted | |
| `Delete` alone | permitted | |
| `MarkAsRead` alone | permitted | |
| `MoveTo` and `MarkAsRead` | permitted | The flag is written first, on the occurrence the condition matched |
| `CopyTo` and `MarkAsRead` | permitted | The same: the flag is written before the copy is placed |
| `MoveTo` and `CopyTo` | refused | Two fates for one occurrence; whichever ran second would act on a message no longer where the rule matched it |
| `MoveTo` and `Delete` | refused | The same, and the deletion would undo the filing |
| `CopyTo` and `Delete` | refused | The same |
| `Delete` and `MarkAsRead` | refused | A flag written on a message being removed is a flag nobody will ever read |

**A refused combination is refused where it is written.** Startup fails naming the rule, the key, and which action could
not be honored beside which, rather than a run resolving it against a mailbox — no resolution invented at run time would
be the one the operator meant, and a rule that resolved differently depending on what a server answered would not be a
rule.

### The order they are applied in

The actions of one rule are applied in **MailFathom's order rather than the order they were written in**, so that every
permitted combination acts on the occurrence the condition matched, and so that the answer is the same on every run and
on every instance:

1. `MarkAsRead`
2. `CopyTo`
3. `MoveTo`
4. `Delete`

The same order governs the actions of two rules that both match one email, which no single rule's block could order on
its own.

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

Each account states, under `MailSynchronization:Accounts:<n>:RuleActions`, which of the four changes a rule may make to
its mailbox:

| Key | Default | What it permits |
| --- | --- | --- |
| `Move` | `true` | A rule may file this account's mail into another of its folders |
| `Copy` | `true` | A rule may place a copy of this account's mail in another of its folders |
| `Delete` | `false` | A rule may remove this account's mail |
| `MarkAsRead` | `true` | A rule may set or clear this account's `\Seen` flag |

**Deletion is opt-in and the other three are opt-out**, because deletion is the one action whose result cannot be
undone by editing a rule afterwards.

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

A condition reaches these twenty-two names and nothing else. Each carries one shape of value, which is what the
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
| `hasExtractedContent` | boolean | Whether text has been extracted from the email's body |
| `bodyText` | text | The text extracted from the email's body, after quoted history and signatures were removed; absent while no extraction has run for it |

Names are case-sensitive. `senderDomain` is a fact and `SenderDomain` is not, so the surface documented here and the
surface accepted are the same one.

**Every fact resolves only if the condition names it, and once per email however many rules name it.** Twenty of the
twenty-two come from metadata a pass already holds, so they cost nothing to read. `folderRole` is read from configuration,
which costs no read either. `bodyText` is the exception: it reads
stored content, which is why a rule set naming it nowhere pays for no read at all, and one naming it in five conditions
pays for one. The boolean operators short-circuit, so a condition whose first half already decides it never resolves
what its second half would have named.

**An absent fact answers rather than failing.** A message with no subject compares unequal to every subject, matches no
`contains`, and reports `isNull(subject)` as true. Guarding a comparison is a choice about what the rule should mean,
never a requirement.

**A timestamp is always in UTC.** A date literal is written `#yyyy/MM/dd#` and carries no offset of its own, so
`receivedAt >= #2026/01/01#` means the start of that day in UTC.

**Text comparison ignores case and is ordinal.** `senderDomain == 'Supplier.TEST'` matches `supplier.test`, and it does
so identically on every instance whatever locale its host is set to.

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
- **The names.** Every identifier is one of the twenty-two facts and every call is one of the seven functions.
- **The types.** Every comparison, operator, and argument holds between shapes that could match. `subject == 1`,
  `sizeInBytes > 'large'`, `recipientDomains == 'example.test'`, and `contains(subject, 3)` are each refused here.
- **The result.** The condition produces a boolean. A condition producing text or a number is refused rather than read
  as truthy, so what a rule means never depends on a coercion nobody wrote down.

The rule's `Accounts` filter is checked beside those four: every identifier names a declared account, none is blank, and
none is repeated. Its `Triggers` list is checked the same way: every name is one this system declares, none is blank,
and none is repeated.

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

The step is the last of the run's local work: it comes after every folder has synchronized and committed, and outside
the synchronization transaction entirely. Two consequences follow that are worth stating rather than inferring. A rule
can only ever see mail the run before it has already stored, so a provider redelivering a message or a synchronization
retry cannot produce a different processing boundary than a clean run. And nothing an MCP tool does waits on a rule:
reads are served from what is already stored, and a pass neither blocks one nor is blocked by one.

**A pass reads nothing out of a folder the account does not mirror.** Such a folder keeps whatever it stored before its
synchronization was switched off, and neither the arrival queue nor a whole-mailbox run walks those rows: their flags
are whatever they were on the day the switch was flipped and nothing will ever correct them, so a rule reading one
would act on a mailbox MailFathom stopped observing. The exclusion is applied where the candidates are read rather than
after they come back, which is what keeps such a message from sitting at the head of the arrival queue forever.

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
permits. A message whose content will never yield text — one above the size limit, which every later run refuses for
the same reason — is evaluated now with the fact absent, because waiting for text that is not coming would stall the
queue behind it.

**A rule that cannot answer for one message costs that message's rule and nothing else.** It is recorded as a failed
rule with a reason, the rules below it still run, the remaining messages of the batch are still evaluated, and the
message is recorded as evaluated. The account is not put into backoff for it either: nothing a rule decided is carried
remotely, so fetching the account's mail less often would answer a local problem by slowing remote work that had nothing
to do with it. The next section lists the two reasons.

**Nothing scans on a timer.** The account run recurs already, so anything a scan would find on arrival is found by the
`Arrival` trigger; a rule whose condition only becomes true with the passage of time — mail older than some age — fires
when the owner asks for a whole-mailbox run. Such a rule is what [`Triggers: []`](#which-triggers-run-a-rule) is for:
declaring no automatic trigger keeps it out of every arrival pass without taking it out of the set.

## Running the rules over mail you already have

Editing a rule is only useful if the rules can be applied to mail that arrived before the edit, and that is what a
**whole-mailbox run** is: a walk over everything stored for one account, evaluating each message again under the rules
now in force.

- **One per account, and asking twice is asking once.** A request that finds a run already outstanding is answered with
  that run rather than starting a second walk of the same mailbox.
- **It runs where every other pass runs.** The request records that the run is wanted and nothing more; the work happens
  as a step of the account's synchronization run, bounded by the same two settings. Whoever asked is not what keeps it
  alive, so closing a terminal does not cancel a walk of a mailbox.
- **It applies the whole rule set.** Every rule the configuration declares for the account is run, including the ones
  that declare no automatic trigger, and nothing selects a subset of them for one run.
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

Selecting mail and changing nothing, which is what a rule owning a message ahead of the rules below it looks like:

```json
{
  "Name": "leave-family-mail-alone",
  "Condition": "senderDomain == 'family.example'",
  "Triggers": [ "Arrival" ],
  "StopWhenMatched": true
}
```
