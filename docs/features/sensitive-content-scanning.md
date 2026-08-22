# Sensitive-content scanning

<!-- describes: backend/src/Application/SensitiveContent/**, backend/src/Host/Configuration/SensitiveContent/**, backend/src/Infrastructure/SensitiveContent/**, backend/src/Application/Emails/Extraction/RedactingEmailMimeReader.cs, backend/src/Host/Hosting/Warnings/StaleDerivedDataStartupReport.cs -->

Mail carries credentials. A deployment key pasted into a thread, a connection string in a stack trace, an API token a
colleague sent because it was quicker than a vault — all of it arrives in a mailbox and, from there, would otherwise
reach a chat provider's context window, a hosted embedding request, a retrieval snippet, and a log line. Sensitive-content
scanning is the stage that finds such material and replaces it before the text is copied anywhere or handed out.

Two scanners can be switched on, independently and both off by default. This page records the contract they share: what
a detection is, what replaces it, what one scan may spend, and what happens when a scanner cannot answer.

## What is scanned, and what is never touched

The boundary is **egress and derived data**, never ingestion. The table names the paths this contract is written for.

| Kind of text | What scanning does to it |
| --- | --- |
| Extracted text, chunks, and the embeddings built from them | Redacted before they are written, so the placeholder is what is stored and later retrieved |
| Text handed to a model, to a hosted embedding endpoint, or back through an MCP tool | Redacted in flight, on every call |
| The message a caller asks this deployment to send or to hold as a draft | **Nothing is rewritten.** What is found cancels the act instead |
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

The first row is what a message stores rather than what leaves, so what it covers, what a derived row records about the
configuration behind it, and what a late switch does to text already written are in [derived data](#derived-data-is-written-redacted-and-stamped)
below.

## The guarded egress points

Every place text leaves this deployment goes through one guard, and the guard is told which place it is. There are
five, and the register is closed: a sixth is a code change rather than a configuration one, which is what makes the
list below answerable by reading it.

| Egress point | What crosses it |
| --- | --- |
| `chat_prompt` | Everything a model is sent: the question a client asked, and the subjects and passages of the mail retrieved to answer it |
| `hosted_embedding_input` | Every passage sent to a hosted embedding endpoint |
| `mcp_snippet` | The mail text an MCP tool answers with: the subjects and sender display names of a listing, the same plus the extracts of a search, and an answer with its citations |
| `mcp_email_content` | The message `get_email_content` returns: both body representations, the subject, and every participant's display name |
| `outgoing_mail` | The message a caller asks to send or to hold as a draft: its subject and both body representations, read back out of the MIME it would be transmitted as |

**A value is guarded, never a composed document.** A snippet, a subject, and a question are each scanned on their own
and the result is composed afterwards. Scanning the composed thing instead would let one detection cover the end of one
field and the beginning of the next, and replacing that region would take the boundary between them with it — an XML
envelope handed to a model would stop being one.

**The single-request chat port is the one exception, and it is one on purpose.** A conversation arrives there already
built, and nothing at that boundary can tell which turn a mailbox reached, so every turn is scanned whole — including
one MailFathom composed itself, such as the judgement a model-judged retrieval sends per candidate. The cost is the one
the rule names: a detection covering the tail of an extract and the element closing it is replaced by a single
placeholder, and the envelope in that turn stops closing. That is a degraded prompt rather than a leak, and it is the
price of a port that guarantees nothing leaves it unscanned whatever its caller composed. Where MailFathom builds a
document it also owns the values, it guards them first and composes afterwards, which is why the retrieval envelope an
answering run sends is assembled from guarded extracts.

**One text is scanned once, at the point it actually crosses.** An answering run looks mail up through the same ranked
window `search_emails` publishes, and that window is guarded where it is published rather than where it is searched — so
a lookup made on behalf of a model is scanned once, at `chat_prompt`, instead of twice under two different tags. The
same reasoning bounds what is scanned at all: a citation the answer's own ceiling drops is never scanned, because it is
never published.

**The guard sits above the retry boundary**, so one call costs one scan whatever the provider does, and a scanner that
cannot answer surfaces as its own refusal rather than being reclassified as a provider fault and retried. What it never
sits above is the identifiers beside the text: an account, a folder alias, an address, a stored identity, and a cursor
are what a caller acts on rather than text to read, and redacting the address a reply has to go to would remove a
result's whole use while protecting nothing the message body did not already carry. Those are protected by who may
reach this deployment. The line falls between a routing identity and free text rather than between fields that look
structured and fields that do not, which is why a sender's display name is guarded while the address it sits in front of
is not.

**Three paths carry no guard because they carry no mail.** A log line and an audit record hold identifiers, aliases,
counts, and outcomes and never message text, which is the rule the [telemetry](../operations/telemetry.md) and audit
contracts already hold rather than something redaction would enforce. MailFathom sends no webhook at all. The
deterministic in-process embedding generator is exempt for a different reason: nothing leaves the process, so there is
nothing for a guard to sit in front of.

**The fifth is the one a redaction never reaches**, because rewriting what an author wrote is not a disposition
MailFathom may take: a placeholder in a message somebody signed would be sent in their name. It refuses the act instead,
and [outgoing mail is screened rather than redacted](#outgoing-mail-is-screened-rather-than-redacted) is where that
difference is stated in full.

A refusal at any of the five fails the operation it guards, as [failing closed](#failing-closed) describes — the
question is not answered, the passages are not embedded, the listing is not served, the message is not returned, the
send is not queued. What
each guarded call found, refused, and cost is published; [telemetry § what guarding an egress point
publishes](../operations/telemetry.md#what-guarding-an-egress-point-publishes) names the instruments.

## Outgoing mail is screened rather than redacted

A message this deployment is about to send is the one guarded text MailFathom did not derive and does not own. Somebody
wrote it, their name is on it, and a placeholder dropped into it would be transmitted in that name — so at
`outgoing_mail` the finding cancels the act instead of rewriting it. Nothing is redacted, nothing is edited, and
nothing partial is stored.

**Every route into a send and every route into a draft is covered, because the screen sits where they converge.** There
is one way a message enters the outbox and one way a draft is written, and the screen is inside each of them rather than
at the tools in front of them. So the act is judged identically whether it was a `send_email` call, a send an operator
asked for at a later moment, an occurrence of a recurring send, a `save_draft`, a revision through `update_draft`, or a
held draft promoted by `send_draft` — including the promotion of a draft written before the deployment screened
anything, which is judged when it is sent rather than grandfathered in.

**What is screened is the composed message**, read back out of the MIME that would actually be transmitted: the subject,
the plain-text body, and the HTML alternative exactly as it will leave, markup and all. Reading the composed message
rather than the arguments a caller sent is what makes the routes above one contract — a promotion and a recurring
occasion carry bytes and no authored fields at all.

**Attachments are not screened.** A scan is over text, and an attachment is a byte stream a caller supplied whose type
this deployment does not undertake to parse. An operator who needs an attachment examined needs a different control
than this one, and reporting it as covered here would be worse than saying it is not.

**Nothing is written down when the screen stops the act.** No outbox row, no draft, no revision, no content row. The
draft being revised keeps the text it already had, and the message is refused before the transaction the act would have
committed is opened.

**A caller reads why.** The refusal carries `59001` naming the category that stopped it — never the rule, the position,
the confidence, or one character of what was found — and `59002` when the text was longer than
`SensitiveContent:MaximumAnalyzedCharacters`, which stops the act because nothing read the remainder and a message
whose tail nobody analyzed is exactly the message that must not leave. Both belong to the MCP boundary's own category,
so an MCP client is told what happened rather than that the tool failed; the same
[error reporting](mcp-tools.md#error-reporting) rule governs every other code.

### What it screens for, and why the default is secrets alone

Screening reuses the deployment's scanners, its category lists, its suppressions, and its bounds. What it does not reuse
is which findings matter, because a redaction and a refusal are not the same act at the same cost:
`SensitiveContent:ScreenOutgoingMailFor` names the scanners whose findings stop a send.

- **The default is `Secrets`.** A credential in an outgoing message is what this exists for, and no ordinary
  correspondence carries one, so the default protects a deployment that switched a scanner on without reading this page
  and refuses almost nothing it did not mean to.
- **`Pii` is named or it is absent.** A mailbox is made of names, addresses, and signature blocks, so a deployment that
  screened outgoing mail for personal data would refuse nearly every message somebody tried to send. That is a
  defensible configuration for regulated correspondence and a broken one everywhere else, which is why it is written
  down rather than inherited. The consequence to read twice: a deployment running **only** the personal-data scanner
  screens no outgoing mail at all until it names `Pii` here, because the default names a scanner that deployment has
  switched off.
- **`[]` switches screening off** while the scanners keep redacting everywhere else, and is how an operator says that
  what leaves through a model may be redacted but what a person sends is theirs.
- **A scanner named here but switched off screens nothing**, because it detects nothing to screen with. Naming one is
  not how it is switched on.

With no scanner switched on, nothing is screened, no message is parsed, and no detector is constructed — the opt-in
nobody took costs an enqueue and a draft save nothing at all.

Each stopped act is counted, by egress point, scanner, and category, without the message or the finding;
[telemetry § what guarding an egress point publishes](../operations/telemetry.md#what-guarding-an-egress-point-publishes)
names the instrument.

## Reading a message is scanned in flight

`get_email_content` is the one egress point that publishes a whole body rather than an extract of one, and it scans on
every call rather than serving something scanned earlier.

**Nothing stored is rewritten by it.** Not the raw MIME, which is byte-exact everywhere in this feature, and not the
extracted text either — a read redacts a copy on its way out and leaves the store as it was. Scanning per call is also
what keeps the store free of a map naming where each message's credentials sit: a persisted span list would be a new
artifact pointing straight at the sensitive material, which a cheaper read does not justify. No offset, no finding
location, and no span list for a stored message is written anywhere.

**One message reads the same through both paths.** The redaction here is the same implementation the derived path runs,
so for a given rule corpus, analyzer profile, and category set, the text `ask_mail` cited out of a redacted chunk is the
text `get_email_content` returns for the same message. That agreement is the point: a citation that landed on different
words would read as invented.

**What is scanned is what the message's author wrote** — both body representations, the subject, and the display names
of the first 40 named participants, past which the address is published with no display name rather than one nothing
scanned. That count is bounded here rather than by the message because a parse publishes up to 256 addresses per header
role and a scan is a round trip on a deployment running the analyzer in a container. The addresses, the account, the
folder alias, the stored identity, the sizes, the flags, and every attachment's file name are left as they are, on the
same line the guarded points above draw: a routing identity is what a caller acts on, and redacting the address a reply
goes to would remove the read's whole use while protecting nothing the body did not already carry. Each value is scanned
on its own and the message is composed afterwards.

**The analyzed ceiling is stated rather than hidden.** A body longer than `SensitiveContent:MaximumAnalyzedCharacters`
comes back cut at that ceiling, with `truncatedBy` reading `sensitiveContentScanCeiling` — the third bound a
representation can name, beside the per-body limit and the read's budget, and the only one a caller cannot act on:
naming fewer emails in a call returns no more of that message, and only raising the ceiling does. The default ceiling
matches what one whole read may return, so an ordinary message never reaches it.

**A detector that cannot answer fails the read.** It is not degraded to serving the stored text: the caller keeps
nothing, and the same read succeeds once the detector answers again. What each side sees follows the rule
[MCP tools § error reporting](mcp-tools.md#error-reporting) already states — the server log records `81001` naming the
scanner, and the client receives `54001`, because only the MCP boundary's own category is described to a caller. An
operator diagnosing a refused read therefore reads the log rather than the client's message.

With both switches off the read is exactly the read it was before this feature existed — no detector is constructed, no
text is scanned, and no representation names a ceiling.

## Derived data is written redacted and stamped

Everything MailFathom derives from a message's body — the extracted text, the passages cut from it, and the vectors
built from those passages — is derived from redacted text while a scanner is on. The redaction happens once, where the
body is read out of the stored MIME, so a placeholder is what is stored, what is embedded, and what a search or an
answer later returns; nothing downstream scans a second time and nothing downstream sees the original.
[The arrival pipeline](../architecture/arrival-pipeline.md) draws where that read sits among the stages around it, and
why the spam scanner beside it is deliberately shown the message unredacted.

**Only the body goes through it.** A subject, a display name, an address, a folder alias, and a thread identity are
routing identity rather than free text, exactly as the egress rule above draws the line, and they are guarded where they
leave rather than where they are stored — otherwise a listing would name messages nobody could recognise and a reply
would have nowhere to go. A subject an operator wants hidden from a model or an MCP client is hidden by the egress
guard, which already covers it.

**A refused scan writes nothing.** The derived path fails closed like every guarded one: a detector that is unreachable,
over its budget, or answering nonsense refuses the extraction, and nothing downstream of it runs. On the synchronization
path that message is not stored at all — its folder run stops on it and the checkpoint stays where it was, so the whole
message is fetched again on the next run rather than landing half-derived. On the extraction backfill the message keeps
whatever it already held, uncommitted work is discarded, and the walk resumes from its last committed position. Both
retry on the next interval, and neither leaves text in the store that no scanner saw.

### The stamp a derived row carries

Each message's derived row records the sensitive-content configuration its text was written under, as a 64-character
digest of: which scanners ran and in what order, each one's detector name and corpus or profile revision, the categories
switched on for it, and the rules suppressed inside them. That is the whole of what decides the redacted result, which is
what makes two rows with the same stamp comparable and two with different stamps not.

**The analyzed ceiling is part of it**, because on this path it is not a cost control. A redaction returns the text cut
at the ceiling, and here what is returned is what is stored — so a deployment that lowers
`SensitiveContent:MaximumAnalyzedCharacters` indexes every message derived afterwards with its body cut at that length,
and raising it back has to leave those rows stale or the missing text is never restored by anything.

**The personal-data confidence floor is part of it too**, through the analyzer profile's revision, which names the
mapping, the set of languages, and the floor together. The languages are in it because they decide what could be found
at all, so adding one marks earlier-derived rows stale exactly as changing the only one did. The floor is sent to the analyzer rather than applied to its answer, so a
finding below it never crosses the process boundary: two deployments differing only there did not ask the same question,
and a mailbox indexed under a higher floor holds personal data the lower one replaces.

What the stamp does leave out is the per-call timeout and the concurrency limit. Neither changes one character of what a
scan that finished produced, so folding them in would mark a whole mailbox stale for tuning a deployment does against
its own load.

**An absent stamp means the text predates any scanner.** It is a different value from every stamp, not a missing one,
so a mailbox derived before the feature was switched on is counted and rebuilt exactly like one derived under an older
configuration. A message indexed on its envelope alone — one whose stored MIME no reader could parse — is the one
exception, and it is neither counted nor re-read: it holds no derived body text to correct, and re-reading it would
produce nothing to write on every pass forever.

### What a late switch does, and what it costs to fix

Switching a scanner on, widening a category list, lifting a suppression, or moving to a build with a newer rule corpus
changes the stamp. It does not change one byte of what is already stored: **stored raw MIME is never rewritten and
stored derived text is never edited in place.** The way back is a rebuild, and the reason is the same one that keeps
raw MIME byte-exact — an in-place edit of derived text would leave a chunk whose vector was built from something else,
with nothing recording which half was which.

So the deployment says so instead. At startup, a deployment with a scanner on counts the messages whose derived text was
written under a different configuration and reports that count on its own log:

- **A warning** when the count is above zero and no rebuild was asked for, naming `SensitiveContent:RebuildStaleDerivedData`
  as what re-derives them. It is a warning rather than a refusal, because derived text written before a switch is a
  state to act on rather than a misconfiguration, and refusing to start over it would take the deployment down for
  something switching the scanner on had already improved.
- **An informational line** when the rebuild is already switched on, saying the extraction backfill will re-derive them
  and that it performs none while `MailExtractionBackfill:Enabled` is off.
- **An informational line** when nothing is stale, because silence would otherwise read as a figure nobody looked up.

The count is a count and nothing else — no subject, no address, no identity — like every other line this deployment
writes. A database that cannot answer it is reported as unavailable and the host starts anyway: the report decides
nothing, and a failed count is a worse reason to refuse a start than the stale rows it was counting.

The rebuild is opt-in, off by default, and asked for with
[`SensitiveContent:RebuildStaleDerivedData`](../operations/configuration-ai.md#sensitivecontent). Switched on,
the extraction backfill stops selecting only messages that never had text and selects every message whose stamp is not
the current one, walking them at its configured batch size and interval; its cursor is scoped to the stamp, so a switch
flipped after a walk finished restarts that walk instead of resuming past the rows it must revisit.

**What it costs is one full re-derivation of the mailbox.** Every selected message is read out of content storage,
extracted again, scanned, re-chunked, and re-embedded — so it is the same spend as first indexing that mailbox: the
stored text, the passages, and the vectors are all replaced, and on a deployment with a hosted embedding endpoint the
embeddings are **billed again**, per message, at that provider's rate. It is off by default for exactly that reason.
Nothing triggers it automatically: switching a scanner on protects what is derived from that moment onward, and spending
a mailbox's worth of embedding credit is the operator's decision rather than a side effect of a protection switch.

With both scanners off nothing here runs at all: no detector is constructed, no text is scanned on the way to storage,
and no stamp is written — a derived row on such a deployment is byte-identical to one written before this existed.

## A finding names a position, never a value

A detection is the corpus entry that matched, the category that entry belongs to, a region of the analyzed text, a
confidence from 0 to 1, the detector that produced it, and the revision of the rule corpus or analyzer profile it ran
under, stamped with when it was evaluated.

It never carries the detected value. Recording one would recreate the leak inside the object written to prevent it, and
every consumer that logs, stores, or audits a finding would carry the credential with it.

The rule travels with the finding because it is the one thing a suppression is written from. A category says what kind
of material was found and is what the placeholder names; the rule says which entry decided that, which is what an
operator needs when one entry misfires and the other several hundred are doing their job. Reporting only the category
would leave them switching off every rule in it.

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

The defaults are in the [AI configuration](../operations/configuration-ai.md#sensitivecontent).

## Failing closed

A switch decides whether a scanner runs at all. It never decides what happens to a finding.

With a scanner on, a detector that is unreachable, slower than its budget, broken, or reporting a region outside the
text it was handed **refuses the operation it guards**. It blocks the egress, blocks the derived write, and fails the
read rather than serving unfiltered content. An opt-in that degraded to "send it through" under load would be worse than
no switch at all, because the operator would believe it was in force.

Every one of those failures reports error code `81001` and names the scanner. None of them names the text or the
finding: the content the scan was about is exactly what must not appear in a failure written to a log.

One failure in this feature is an availability failure rather than a scan's: `81002`, raised when the personal-data
analyzer cannot be reached, answers the availability probe with a refusal, or recognises nothing the configured
categories need. It is what the readiness probe reports on rather than what a guarded operation raises. It
names `SensitiveContent:PersonalDataAnalyzer:Endpoint` — the key an operator edits — rather than the address that key
resolved to, because a message reaches a log and no message in this feature carries a host name. Neither does it carry
the analyzer's own words: a refusal is reported as its status number and .NET's name for that status, because a proxy or
a wrong service at the configured address writes the body and the reason phrase alike. The resolved address is on the
failure itself for a caller that has somewhere safe to put it.

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
  by its proximity to a keyword, and a mailbox is prose, so those would turn ordinary sentences into findings. The
  expressions that are taken are adapted rather than copied, and one of those adaptations is about prose too. gitleaks
  establishes where a credential ends by requiring a quotation mark, whitespace, a semicolon, or the end of the text
  after it, which is where one ends in a file and not where one ends in a message; MailFathom requires instead that the
  credential's own alphabet has stopped, so a token closing a sentence, standing in a bracket, or sitting in a table
  cell is found exactly as one followed by a space is.
- **MailFathom's own**, for the two shapes both corpora miss because both are written for source control: a connection
  string pasted into a thread so somebody can reproduce a failure, and a link whose query string is the credential.

Whichever of the three matched, a finding reports **one detector identity and one corpus revision**, and that revision
moves when any of the three does. It also moves when the adaptation above changes what a rule matches, which is a
fourth thing it names: the gitleaks half is identified by the release it came from and by the revision of what was done
to it, because either one moving is a different corpus and a text redacted under the earlier one is a different result.
An operator diagnosing a false positive should not have to learn which corpus a rule came from before they can suppress
it.

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

## The personal-data scanner

`Pii` reaches **a container beside the service**. Finding a personal name, a postal address, or a national
identification number in prose needs a language model, and MailFathom loads none into its own process — so this scanner
sends the text to a [Presidio](https://github.com/data-privacy-stack/presidio) analyzer over HTTP and maps the offsets
it answers with back onto the text.

Every category goes through the analyzer, the fixed-format ones included. A payment card number could be matched here
with a checksum and no model at all, but splitting the categories across two implementations would leave two things
deciding what a personal-data finding is: they would disagree about the same message, each would need a false-positive
corpus of its own, and the deployment rule below would become a rule per category.

### The analyzer is deployed only when the switch is on

With `Pii` off — the default — no analyzer exists anywhere. The Helm chart renders no workload and no service for one,
the Compose deployment leaves its analyzer service behind a profile that is not active, and the Quadlet deployment's
analyzer unit is a file an operator never copies. An opt-in nobody took pulls no image, holds no memory, and adds no
listener.

Switching it on **without naming an address at all** fails startup and names
`SensitiveContent:PersonalDataAnalyzer:Endpoint`, the key to correct. That is a configuration error, and configuration is
validated before anything is dialled.

An address that names an analyzer which does not answer is a different thing, and it does **not** fail startup. The host
comes up and reports itself **unready** — `/health` answers `Unhealthy`, so an orchestrator takes the instance out of
traffic without restarting it. So does an analyzer that recognises nothing at all in one of the configured languages, and
one that answers but recognises nothing for a switched-on category in any of them — a narrower registry than the shipped
image, or a language it has no model for. All of them are refusals to serve
rather than warnings, because the alternative is a deployment whose configuration reads as protection in force while
every scan finds nothing, and nothing finding anything is indistinguishable from a clean message.

**The probe runs on every readiness scrape**, not once at startup, because the analyzer is a container with a lifetime of
its own: one that becomes ready a minute after MailFathom and one that stops answering hours later are the same question,
and an answer taken while the host came up settles neither. A transition into unavailability is written to the log at
`Error` and the recovery at `Information` — the probe response is one word by design, so the log is the only place the
reason is readable. Beneath the probe, the fail-closed contract above is what covers each individual operation: a scan
that cannot reach the analyzer refuses the operation it guards.

See [the health endpoints](../operations/health-endpoints.md#the-three-probes) for what each probe consults and what a
failure of it costs.

**What it costs to run.** The analyzer loads a language model into memory before it answers anything and holds it for the
life of the container, so the deployment assets give it roughly a gigabyte to request and two as a ceiling; below about a
gigabyte it is killed while loading, which reaches MailFathom as an analyzer that never became ready. That load is also
why the first start after switching the feature on is slow — tens of seconds — and why MailFathom reports itself unready
for that interval rather than refusing to come up in it. Whether it needs a CPU of its own depends
entirely on how much mail flows through the guarded paths; the concurrency bound above is what keeps a burst from becoming
a queue at the analyzer. The per-shape figures are on the deployment pages:
[Compose](../operations/deployment-compose.md), [Kubernetes](../operations/deployment-kubernetes.md), and
[Quadlet](../operations/deployment-quadlet.md).

### Keep the endpoint inside the deployment

The whole point of scanning is that content is inspected **before it leaves the trust boundary**. An analyzer on the
public internet inverts that: the mail is handed to a third party in order to establish whether it may be handed to one.

Nothing in the configuration refuses it, because one analyzer serving several services inside a private network is a
legitimate arrangement and no rule about addresses can tell the two cases apart. What an operator gives up by pointing it
outside is stated here rather than enforced: every message that would have been redacted is sent, in full and in the
clear, to whatever is at that address.

### The categories, and which are on by default

An operator configures MailFathom's categories. They never configure the analyzer's own entity names: those are a third
party's identifiers, they change between analyzer releases, and a deployment named against them would be configured
against a service rather than against this product. Each category is declared here with the analyzer entities it covers,
and one entity is one rule inside it — which is what makes a suppression able to silence a single misfiring recognizer
inside a category that stays on.

**On by default** — the identifiers that are high harm and low ambiguity. Nothing about the surrounding message makes one
of them safe:

| Category | What it covers |
| --- | --- |
| `PaymentCard` | Payment card numbers |
| `BankAccount` | IBANs and other bank account numbers |
| `NationalIdentifier` | National identification, social-security, and tax numbers |
| `IdentityDocument` | Passport, identity-card, and driving-licence numbers |
| `HealthIdentifier` | Numbers that name a person inside a health system |

**Off unless configured on** — everything a mailbox is made of. Hiding these is a legitimate choice under a strict
regime, and it is also what empties a chunk store of the terms a search runs on, so it is the operator's decision rather
than the product's default:

| Category | What it covers | What switching it on costs retrieval |
| --- | --- | --- |
| `PersonName` | Personal names | Every question of the form "what did *she* say about the invoice" stops matching, because the name is gone from the chunk the answer is in and from the query's own match |
| `EmailAddress` | Email addresses | An address is how a thread's participants are found in text as well as in headers; searching for one returns nothing |
| `PostalAddress` | Postal addresses, and places named precisely enough to be one | The analyzer reports a place name as this category, so a city or a country in ordinary prose is redacted along with a street |
| `PhoneNumber` | Telephone numbers | Small, and the one on this list with the least retrieval cost |
| `Date` | Dates and times, absolute and relative | The heaviest of the six. A mailbox is full of dates in prose, and a redacted one takes the sentence's meaning with it |
| `NetworkAddress` | IP and MAC addresses | An operational mailbox — alerts, incident threads, log excerpts — loses the addresses the thread is about |

The health, clinical, and demographic entities the analyzer can also report are deliberately unmapped. A disease, a
medication, or a procedure is health *narrative* rather than a health identifier, and hiding it turns a message about a
patient into a message about nothing; nationality, religion, and political affiliation is exactly the special category
that deserves the strongest treatment, and the analyzer's answer for it is a named-entity guess whose false-positive rate
would make the category unusable rather than protective. Company and vehicle registrations are out for a third reason:
one names a legal entity rather than a person, and the other matches ordinary prose often enough to empty a chunk store
on its own.

**A category is only as good as the analyzer behind it.** The shipped image registers nineteen entities for English, so
most of the entries in each category above — the national identifiers of two dozen countries, the passport formats of
several — are recognised only by an analyzer configured with the recognizers for them. The readiness probe checks that
every switched-on category has **at least one** entity the analyzer knows in at least one configured language, which is
what catches a category that would be scanned for and never found; it deliberately does not require all of them, because
a narrower registry costs recall inside a category that still works.

That rule is right and it has a consequence worth stating plainly: a category can be half unreachable while the
deployment reads healthy. `NationalIdentifier` covers 27 analyzer entities, of which the shipped image knows `US_SSN`
and `US_ITIN`; the category passes the probe on those two while `PL_PESEL` — also one of its entities — is not looked
for at all under English. Nothing is logged, because nothing went wrong: the analyzer was asked what it knows and
answered. What closes that particular gap is naming `pl` beside `en`, which the next section is about.

### The languages, chosen once for the deployment

`SensitiveContent:PersonalDataAnalyzer:Languages` is a set of two-letter codes, defaulting to `en` alone. There is no
per-account, per-folder, or per-message language and no detection — the set belongs to the deployment — and it is part of
the detector revision a finding carries, so the same text asked under two different sets is two results and widening the
set marks derived text stale.

**A recognizer exists only for the languages it is registered for.** This is the mechanism behind the paragraph above:
the analyzer's registry declares each recognizer against a language, and one not declared for a configured language is
absent rather than weaker. The shipped image's registry names a recognizer for eleven languages and ships most of the
locale-specific ones switched off, so what a category can find is decided by the languages before it is decided by
anything else MailFathom configures. `en` and `it` are the two languages under which all five default categories have
something behind them on their own; `es` and `pl` leave `IdentityDocument` with nothing, and the remaining seven leave
`PaymentCard`, `NationalIdentifier`, and `IdentityDocument` empty — which is why naming several is how a mixed mailbox
covers what any one of them misses.

**A category has to be reachable in one configured language, not in every one.** The probe unions what each language
answers and then asks its per-category question over that union, so `en` beside `pl` reaches `IdentityDocument` through
English while `PL_PESEL` closes the Polish half of `NationalIdentifier` — and adding a language never turns a ready
deployment unready. What each configured language must do is answer at all: one the analyzer was never built for is
refused by name, because a language contributing nothing is protection an operator asked for and did not get.

**One scan is one request per language.** A call states one language, so the scan asks once for each of them over the
same text, one after another, and merges the answers. `SensitiveContent:ScanTimeout` bounds the whole scan rather than
each call and `SensitiveContent:MaximumConcurrentScans` still counts scans rather than requests, which is why the set is
capped at eight. The merge rests on rules the redactor already had: two languages reporting the same value over the same
span are one finding carrying the stronger score, overlapping regions become one placeholder, and the findings are
ordered before they are applied, so the redacted text does not depend on which language answered first.

**Polish is the case worth naming**, because it is where these rules meet. Its only entry in the registry is `PL_PESEL`;
there is no Polish passport recognizer and no Polish identity-card one, while several other locales carry both. So a
deployment naming `pl` beside `en` finds PESEL numbers and keeps `IdentityDocument` reachable through the English
entities, and the two identifiers Polish correspondence carries most are still the two with nothing to switch on — which
needs a recognizer added to the analyzer rather than another language. What it takes to make a language work at all —
the model, the image, the registry, and the MailFathom-side entry a new entity needs — is [the analyzer's
languages](../operations/personal-data-analyzer-languages.md).

### The confidence floor

The analyzer scores every finding, and redaction acts on a finding without weighing it. The floor is therefore the only
thing between a deployment and the analyzer's weakest guesses — and those are weak. Measured against the pinned image with
no floor at all, an eight-digit build number is reported as a bank account number at 0.05 and as a driving licence at
0.01, a contract reference of one letter and seven digits as a driving licence at 0.3, and a nine-digit passport number as
a national identifier at 0.3 on top of being a passport number.

The default is `0.4`, and it is the only value that drops all of those while leaving every category detectable. Both of
its bounds are the analyzer's rather than a preference: everything above is measured noise, and *at* 0.4 sit a passport
number and a bank routing number, so raising the floor at all stops two of the five default categories from being found.
The floor is sent to the analyzer rather than applied to its answer, so the weakest guesses never cross the process
boundary at all, and it is compared inclusively — a finding scored exactly 0.4 survives a floor of 0.4.

It is part of the detector revision a finding carries, beside the mapping this build ships and the model the analyzer
loaded, because the analyzer applies it rather than this process: a finding below the floor is never reported, so it is
part of the question asked rather than a filter over the answer. The category list is not, because that is which of the
results a deployment wanted. What the revision promises is that two deployments carrying it asked the same detector the
same question — which is also what makes it safe for a derived row's stamp to be computed from it.

### Two things the offsets have to survive

The analyzer indexes a Python string and MailFathom indexes a .NET one, so the offsets it answers with count Unicode
**code points** where a `string` counts UTF-16 code units. Every text made only of basic-plane characters gives the same
two numbers, and a message with an emoji, an ideograph beyond the basic plane, or a flag in front of the finding does not:
the region would be shifted, leaving part of the value in the redacted text and destroying part of what surrounded it.
The translation is part of the adapter, and the integration suite proves it against the real analyzer rather than against
a payload somebody hand-wrote.

An entity the mapping does not know is **ignored** rather than refused. An analyzer may run recognizers of its own, and
one reporting something no category covers is answering a question nobody asked; a deployment that refused the whole scan
over it would fail closed on every message.

## What a detector is not

A detector is never treated as complete. Its role is to reduce exposure and to fail closed, and it does not license
sending mail content to a provider that would otherwise be prohibited.
