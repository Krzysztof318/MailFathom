# Sensitive-content scanning

<!-- describes: src/Application/SensitiveContent/**, src/Host/Configuration/SensitiveContent/** -->

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

> This release ships the contract and nothing on either side of it. **No detector is registered**, so switching either
> scanner on fails startup naming the scanner that has nothing behind it — the same refusal that protects a deployment
> whose analyzer went missing. **No consumer redacts through it either**: none of the rows in the table above redacts
> today, so each states the path the contract is written for rather than one that is covered. The detectors and the
> consumers arrive with their own changes, and this note narrows as they do.

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

## What a detector is not

A detector is never treated as complete. Its role is to reduce exposure and to fail closed, and it does not license
sending mail content to a provider that would otherwise be prohibited.
