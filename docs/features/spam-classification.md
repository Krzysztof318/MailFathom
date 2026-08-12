# Spam classification

<!-- describes: src/Domain/Spam/**, src/Application/Spam/**, src/Application/Folders/IJunkMailFolderCatalog.cs, src/Infrastructure/Persistence/Spam/**, src/Infrastructure/Mail/Mime/MimeKitEmailSpamHeaderReader.cs, src/Host/Configuration/Spam/** -->

A mailbox that an assistant reads is a mailbox somebody else can write to. Mail written to deceive a reader is
indistinguishable from correspondence once it is a row in a timeline, and the receiving mail server has usually already
decided what it thought of it — in an `Authentication-Results` header, in a provider's `X-Spam-*` header, or by filing
the message in the junk folder. Spam classification is what keeps that decision rather than discarding it, and records
it as derived data beside the message.

Two things ship here and are independent of each other: the classification record with the stage that fills it, and the
junk folder becoming a fact that mailbox reads act on. The second is true of a mailbox with no classification at all.

## The junk folder is left out of listing and search

`list_emails` and `search_emails` leave the account's junk folder out by default and report which of the two answers
they gave, in `includedJunkMail`. A caller that is looking for a message a filter took asks for it explicitly with
`includeJunkMail: true`. Nothing else changes: the folder is still synchronized, still stored, and still reachable by
identifier through `get_email_content`.

Which folder that is comes from configuration rather than from what a server advertised — the `Junk` special use in the
account's folder mapping, described in [IMAP synchronization](imap-synchronization.md). A deployment that maps no junk
folder withholds nothing, and both tools behave exactly as they did before this existed.

The override can never reveal a folder an operator withheld. A folder taken out of tool visibility with
`visibleToTools: false` stays out whatever a caller asks for, because the two decisions are held apart and a query is
handed their union. [Mailbox queries](mailbox-queries.md) holds the rest of what narrows a read.

`ask_mail` excludes junk and offers no override. Its answer is composed by a model from the mail it retrieved, so
content written to deceive a reader would reach the model as ordinary correspondence with nothing left to notice it.

## What a classification records

One classification per occurrence, replacing whatever was recorded for it. It is derived data of the same kind as an
embedding: computed locally, never mirrored to the mail server, and never a statement about where the message lives or
which flags it carries. Filing a message somewhere is a different feature and is not part of this one.

| What it holds | Why |
| --- | --- |
| The verdict — undetermined, not spam, or spam | Undetermined is an ordinary answer: it says nothing was found either way |
| The stage that decided it | A verdict from a header and a verdict from a scanner are different claims |
| The score and the threshold it was judged against, when a stage produced numbers | A score without its threshold cannot be read: the same number is spam under one configuration and ordinary mail under another |
| The rule corpus the deciding stage ran under, when it has one | What a reclassification is worth comparing against |
| The signals the verdict rests on, in the order the stages produced them | An operator diagnosing a wrong verdict asks *which* authentication method failed and *what* the provider header said |
| When it was evaluated | |

A signal is one fact: its kind, its name, what was observed, and where it came from — a header field, a folder
placement, or a scanner's rule corpus. The signals stay separable rather than being merged into one opaque number,
which is what makes the record answerable rather than merely present.

The record is bounded: at most 64 signals, and an observation longer than 512 characters is shortened. The
deterministic stage's facts are produced first, so a record truncated at the bound keeps the ones the verdict rests on.

## The deterministic stage

It works alone, with no scanner configured and no sidecar deployed, and it is the whole of the working feature without
one. It reads the stored message's header block — never its body, never its MIME tree, never an attachment — and
records three kinds of fact.

**Sender authentication.** Every outcome each `Authentication-Results` header states, once per authenticating hop, with
the properties the hop wrote beside it. `ARC-Authentication-Results` is read too and recorded as its own kind: an ARC
outcome is a relay's signed claim about what *it* saw, which is a weaker statement than what this mailbox's own server
saw. A failure is recorded and decides nothing on its own — a message the receiving server chose to deliver despite a
DMARC failure is a message about which something is known, not a message this system overrules the server about.

**Provider spam headers.** `X-Spam-Flag`, `X-Spam-Status`, `X-Spam-Score`, and `X-Spam-Level`. The flag is read before
the status, so a message that disagrees with itself is answered by the one field with two accepted values. A score
becomes an assessment only where the same header carries the threshold it was judged against; a bare
`X-Spam-Score: 15.2` is recorded as a signal and produces no numbers, because nothing says what scale it is in.

**Junk folder placement.** A message in the account's junk folder is spam, and the placement outranks a header saying
otherwise: somebody's filter already acted on this message, and that action is more recent than the header.

A message whose headers cannot be parsed is classified from its folder alone, which is a weaker classification and an
honest one. One unreadable message does not stop a run.

## The scanner stage

An `Application`-owned port takes the stored RFC 822 bytes and answers with a score, a threshold, the names of the
rules that fired, and the identity of the corpus it ran under — or with the reason it could not answer. No protocol
type, socket type, or scanner vocabulary crosses it.

**No implementation ships here.** A deployment that switches the scanner on without one registered still classifies,
through the deterministic stage alone, because the switch is an operator's intent and the registration is whether an
implementation exists — two separate facts.

Where a scanner does answer with a score, it decides the verdict. Where the deterministic stage already reached spam,
that verdict stands whatever the scanner says: it rests on the provider's own decision or on where the mailbox filed
the message, both taken with network context that nothing after delivery has. A scanner that could not be reached
leaves the deterministic verdict exactly as it was, including undetermined.

An operator who does not administer the scanner can re-judge its score with a threshold of their own. It replaces the
scanner's rather than being compared beside it, so the record states one pair of numbers in one scale. It reaches no
other stage: a provider header carries a threshold in a scale this one knows nothing about.

## Classifying is idempotent, and reclassifying is explicit

Classification is keyed to the occurrence, so repeating it either leaves the existing record alone or replaces it with
what the same inputs produce. Two callers asking together resolve to one record rather than to a history: a concurrent
write conflicts on the record's own optimistic-concurrency token and is retried from a fresh read.

Replacing an existing verdict is an explicit operation. Nothing sets it off — not a reload, not a configuration change,
and not a message being read again.

Content comes from the local content store, which already holds it, so no classification path opens an IMAP session and
none can affect a remote `\Seen` flag. A message stored without content — one that exceeded the size limit — is
reported as unclassifiable rather than fetched.

## What it costs when it is off

Nothing. Every switch is off by default, and the checks run in the order of what they cost: whether classification is
on at all is free, the scope and any existing record are one lookup each, and only then is a message's content read. An
occurrence outside the configured scope therefore costs no read of its mail.

## Configuration

The `SpamClassification` section, in full, is in the
[configuration reference](../operations/configuration-reference.md#spamclassification). What it decides:

- whether classification runs at all;
- whether a configured scanner is consulted after the deterministic stage;
- which folder aliases are classified, defaulting to whichever alias each account maps to its inbox;
- the threshold a scanner's score is judged against, defaulting to the scanner's own.

An operator who switched the scanner on and left classification off is told at startup rather than given the quiet
answer, and an unusable folder alias or an out-of-range threshold fails startup naming itself.

## What is not here

No scanner implementation, no mutation of the remote mailbox, and no on-demand run over a mailbox that is already
stored. The classification is recorded and read back; nothing yet schedules it as mail arrives, because the durable
job model it is to run as an execution in is not built.

## Privacy

A classification is derived from mail content and inherits its classification, retention, and deletion constraints: the
record hangs off the message occurrence and is removed with it, so whatever erasure and retention already reach the
message reach the record too.

Nothing about a message's content reaches a log line or a telemetry attribute from any path here — not a header value,
not an authentication detail, not a subject. What is safe to report is the occurrence identifier, the folder alias, the
outcome, and the verdict.
