# Mail rules

<!-- describes: src/Application/Rules/**, src/Infrastructure/Rules/**, src/Host/Configuration/Rules/** -->

A mail rule selects mail. It is a name, a condition, the accounts it applies to, and whether a match ends the pass, and
an owner writes it in the configuration their deployment already carries. This page documents the whole of what a
condition may say: every fact it can read, every function and operator available to it, the limits it is read and run
under, and the order a set of rules is evaluated in.

What a match leads to is not a property of the rule set and is not on this page. A rule states which mail it selects,
and nothing more.

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

## The facts a condition can read

A condition reaches these twenty-one names and nothing else. Each carries one shape of value, which is what the
comparison, operator, and function checks below are made against.

| Fact | Type | What it holds |
| --- | --- | --- |
| `account` | text | The configured alias of the account the email belongs to |
| `folder` | text | The configured alias of the folder the email occurrence is in |
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
| `bodyText` | text | The text extracted from the email's body; absent while no extraction has run for it |

Names are case-sensitive. `senderDomain` is a fact and `SenderDomain` is not, so the surface documented here and the
surface accepted are the same one.

**Every fact resolves only if the condition names it, and once per email however many rules name it.** Twenty of the
twenty-one come from metadata a pass already holds, so they cost nothing to read. `bodyText` is the exception: it reads
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
| `ConditionEvaluationTimeout` | 1 second | How long one condition may take, including resolving the facts it names |
| `Rules` | 200 rules | How many rules one set may declare |

The first two are checked when the configuration is read, so a condition over either is refused with the rule named.
The third is checked while a condition runs, because nothing readable in the text says how long a read of stored content
will take.

A length limit and a depth limit are both needed: length alone would admit a short expression nested past what anyone
can follow, and depth alone would admit a flat expression of ten thousand terms.

## What a rule set is checked for

Every condition is read while the host composes itself, before any mail is seen. Reading one checks four things, and
each is refused with a message naming the rule, what was wrong, and where:

- **Syntax.** The condition parses, and a failure reports the position it failed at.
- **The names.** Every identifier is one of the twenty-one facts and every call is one of the seven functions.
- **The types.** Every comparison, operator, and argument holds between shapes that could match. `subject == 1`,
  `sizeInBytes > 'large'`, `recipientDomains == 'example.test'`, and `contains(subject, 3)` are each refused here.
- **The result.** The condition produces a boolean. A condition producing text or a number is refused rather than read
  as truthy, so what a rule means never depends on a coercion nobody wrote down.

The rule's `Accounts` filter is checked beside those four: every identifier names a declared account, none is blank, and
none is repeated.

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

Rules are evaluated in the order they are written, skipping the ones scoped to other accounts. That order is the whole
of the contract — nothing sorts, groups, or reorders a set — so two rules that both match one email produce the same
outcome on every run and on every instance.

`StopWhenMatched` on a rule that matches ends the pass, and the rules below it are not reached. It defaults to `false`.
A rule that does not match never stops a pass whatever it declares.

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

- Changing a rule's name, its condition, its `StopWhenMatched`, or the accounts it applies to moves it.
- Adding, removing, or switching off a rule moves it.
- Reordering the rules moves it, because declared order is part of what a rule set means.
- Reformatting the file, reordering the keys within one rule, and changing an unrelated configuration section all leave
  it alone.

The identity carries none of the authored text and no ordering. A record naming a revision therefore holds nothing
personal that a condition contributed, and a record that has to say which of two revisions came first carries its own
timestamp rather than inferring one.

## Worked conditions

A single sender:

```text
senderAddress == 'billing@supplier.test'
```

One account's mail only, which is the `Accounts` filter beside the condition rather than anything in it:

```json
{ "Name": "work-invoices", "Accounts": [ "work" ], "Condition": "contains(subject, 'invoice')" }
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
