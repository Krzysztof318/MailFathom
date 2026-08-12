# Sensitive-content scanning

<!-- describes: src/Application/SensitiveContent/**, src/Host/Configuration/SensitiveContent/**, src/Infrastructure/SensitiveContent/** -->

Mail carries credentials. A deployment key pasted into a thread, a connection string in a stack trace, an API token a
colleague sent because it was quicker than a vault — all of it arrives in a mailbox and, from there, would otherwise
reach a chat provider's context window, a hosted embedding request, a retrieval snippet, and a log line. Sensitive-content
scanning is the stage that finds such material and replaces it before the text is copied anywhere or handed out.

Two scanners can be switched on, independently and both off by default. This page records the contract they share: what
a detection is, what replaces it, what one scan may spend, and what happens when a scanner cannot answer.

## What is scanned, and what is never touched

The boundary is **egress and derived data**, never ingestion. The table names the paths this contract is written for;
the note below states which of them redact through it today.

| Kind of text | What scanning does to it |
| --- | --- |
| Extracted text, chunks, and the embeddings built from them | Redacted before they are written, so the placeholder is what is stored and later retrieved |
| Text handed to a model, to a hosted embedding endpoint, or back through an MCP tool | Redacted in flight, on every call |
| The stored RFC 822 bytes | **Nothing.** They are never rewritten |

Raw MIME stays byte-exact because it is the fetched artifact and the local source of truth: redacting it would break
signature verification, make re-derivation impossible, and destroy content permanently with no way back from a false
positive. What protects it is access control, encryption at rest, and retention — not redaction.

## The two scanners

They are separate switches because they are not alike in precision, not because one is more optional than the other.

**`Secrets`** looks for material that identifies its own format: a provider-prefixed token, a PEM block, a JWT, a
connection string. Detection is close to exact and costs one pass over the text.

**`Pii`** looks for personal data beyond the fixed-format identifiers. A mailbox is made of names, addresses, and
signature blocks, so this pass applied the same way redacts a great deal of an ordinary corpus. An operator handling
regulated correspondence wants it; the ordinary one wants secrets alone and would switch a single combined switch off
entirely rather than accept the second.

All four combinations are supported configurations. With both off nothing is scanned, nothing is constructed, and no
cost lands on any path.

> `Secrets` has a detector behind it and is described below. **`Pii` has none**, so switching that one on fails startup
> naming the scanner that has nothing behind it — the same refusal that protects a deployment whose analyzer went
> missing. **No consumer redacts through the contract yet either**: none of the rows in the table above redacts today,
> so each states the path the contract is written for rather than one that is covered. The analyzer and the consumers
> arrive with their own changes, and this note narrows as they do.

## A finding names a position, never a value

A detection is a category, a region of the analyzed text, a confidence from 0 to 1, the detector that produced it, and
the revision of the rule corpus or analyzer profile it ran under, stamped with when it was evaluated.

It never carries the detected value. Recording one would recreate the leak inside the object written to prevent it, and
every consumer that logs, stores, or audits a finding would carry the credential with it.

The detector and its revision travel with the finding rather than being read from the deployment, because redaction is
only reproducible against a stated corpus: the same text under a newer rule set is a different result, and something
that stored one has to be able to say which one it stored.

## One placeholder, everywhere

A redacted region is replaced by `[redacted:<category>]` — the category name and nothing else. Not a length, not a
preserved prefix, not a masked remainder: a length narrows a credential's search space and a prefix names the service it
belongs to, and neither is worth the readability it buys.

One scheme serves every consumer, produced by one implementation. That is what lets a citation drawn from a redacted
chunk land on the same redacted text when the reader opens the message; two implementations would drift the moment
either gained a rule about ordering or overlap.

Redaction is reproducible. For a given text, a given set of switched-on categories, and a given set of detector
revisions, the redacted text is identical on repeat: scanners run in a fixed order, findings are sorted before they are
applied rather than applied as they arrive, and two overlapping detections merge into one region covering both — so no
character any detector covered survives into the text that is handed on.

## Categories, and the one rule below them

A **category** is the unit an operator configures. A rule corpus carries hundreds of entries, and choosing among them
individually is maintaining a fork of that corpus.

Each scanner ships a default set of categories, which is the product's opinion rather than a starting point somebody
assembles. Naming categories in configuration **replaces** that set outright: naming three yields exactly those three,
naming none yields the defaults. A list that added to a set an operator could not see would leave them unable to say
what is being scanned for by reading their own file.

A **suppression** silences one rule inside a category that stays switched on — the remedy for a single corpus entry that
misfires on one deployment's mail. It can never switch a category on: a suppression naming a category this deployment
does not look for resolves to nothing and changes nothing. Suppressing every rule of a category is not how a category is
switched off, either; that is what the category list is for.

## What one scan may spend

Three bounds, because a scan goes wrong in three different ways.

- **Analyzed length.** Text beyond the ceiling is not analyzed — and therefore not handed on. What a redaction returns
  stops at the ceiling and reports how many characters it dropped. Emitting the remainder would make the one input
  nobody scanned the one input that leaves.
- **Per-call timeout.** How long one call to one scanner may take before the operation it guards is refused.
- **Concurrency.** How many scans run at once across the process, which matters most for the scanner that reaches a
  container over the network and would otherwise open a connection per caller.

The defaults are in the [configuration reference](../operations/configuration-reference.md#sensitivecontent).

## Failing closed

A switch decides whether a scanner runs at all. It never decides what happens to a finding.

With a scanner on, a detector that is unreachable, slower than its budget, broken, or reporting a region outside the
text it was handed **refuses the operation it guards**. It blocks the egress, blocks the derived write, and fails the
read rather than serving unfiltered content. An opt-in that degraded to "send it through" under load would be worse than
no switch at all, because the operator would believe it was in force.

Every one of those failures reports error code `81001` and names the scanner. None of them names the text, the finding,
or the endpoint: the content the scan was about is exactly what must not appear in a failure written to a log.

## The secret scanner

`Secrets` runs **in this process**. Secret detection is pattern matching over text, so a container of its own would buy
nothing and would put a network hop and a failure mode on a read path that already has to fail closed.

### Where its rules come from

Three places, and the reason is a measured gap.

- **The detection engine's own corpus**, `Microsoft.Security.Utilities.Core`. Roughly ninety high-confidence patterns,
  almost all of them Microsoft credential formats, against two third-party ones. They become `ProviderToken` rules, and
  the handful of shape-recognising entries in its unclassified list become `JsonWebToken`, `PrivateKey`, and
  `CredentialUrl` rules instead.
- **The gitleaks rule data**, at the release recorded in `THIRD_PARTY_LICENSES.md`. A mailbox receives forge tokens,
  cloud access keys, payment keys, and model-provider keys, and the engine alone catches almost none of them. Only
  entries that recognise a credential **by its own shape** are taken: gitleaks also ships rules that recognise a secret
  by its proximity to a keyword, and a mailbox is prose, so those would turn ordinary sentences into findings.
- **MailFathom's own**, for the two shapes both corpora miss because both are written for source control: a connection
  string pasted into a thread so somebody can reproduce a failure, and a link whose query string is the credential.

Whichever of the three matched, a finding reports **one detector identity and one corpus revision**, and that revision
moves when any of the three does. An operator diagnosing a false positive should not have to learn which corpus a rule
came from before they can suppress it.

### What is redacted, and what is left readable

A finding covers the credential rather than the line it sits on. Where the surrounding text carries no secret and is
worth keeping, only the credential inside it is replaced: a connection string still says which database it reached, and
a link still says what it linked to.

### The entropy heuristic

`HighEntropyString` is the recall layer for a credential with no format to recognise, and it is off by default. It is
equally what turns a base64 attachment fragment, a message identifier, and a tracking parameter into findings — a trade
an operator should be choosing rather than discovering.

It is not shape alone. A candidate is measured for randomness, in bits per character, and reported only above a floor
that separates a credential drawn from a random alphabet from an encoded run of ordinary text. Its confidence is that
measurement rather than a fixed value, which is what makes it the one category whose findings are scored rather than
certain.

### Bounding an untrusted match

Mail is untrusted input, so **no expression runs without a ceiling on how long it may take**. The scan budget an
operator configures bounds a whole scan; a separate per-expression ceiling bounds one pattern within it, and exceeding
either refuses the operation rather than returning text nobody finished scanning.

The budget bounds a scan already running, not only one about to start, which is why the scanner walks its own corpus
instead of handing the text to the engine's masker: that one runs every expression before it returns anything, so a
budget expiring midway would be read only once the work it was meant to stop had finished. Ending the pass between one
expression and the next is what makes the budget and a shutting-down host mean the same thing here as everywhere else.

Which matcher runs an expression was decided by measurement rather than by preference. MailFathom's own corpus is
compiled by the `[GeneratedRegex]` source generator, because it is derived from RE2 expressions that carry no
backreference and no nested quantifier for a backtracking matcher to degrade on, and because the generated matcher runs
several times faster than the linear-time alternative over exactly the text a mailbox produces the worst case with — a
base64 attachment fragment or a long run of hexadecimal. The engine's own patterns stay on the linear-time matcher it
selects for them, because their expressions are the package's to reason about rather than this repository's.

## What a detector is not

A detector is never treated as complete. Its role is to reduce exposure and to fail closed, and it does not license
sending mail content to a provider that would otherwise be prohibited.
