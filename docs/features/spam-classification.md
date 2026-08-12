# Spam classification

<!-- describes: src/Domain/Spam/**, src/Application/Spam/**, src/Application/Folders/IJunkMailFolderCatalog.cs, src/Infrastructure/Persistence/Spam/**, src/Infrastructure/Spam/**, src/Infrastructure/Mail/Mime/MimeKitEmailSpamHeaderReader.cs, src/Host/Configuration/Spam/** -->

A mailbox that an assistant reads is a mailbox somebody else can write to. Mail written to deceive a reader is
indistinguishable from correspondence once it is a row in a timeline, and the receiving mail server has usually already
decided what it thought of it — in an `Authentication-Results` header, in a provider's `X-Spam-*` header, or by filing
the message in the junk folder. Spam classification is what keeps that decision rather than discarding it, and records
it as derived data beside the message.

Three things ship here and are independent of each other: the classification record with the stage that fills it, the
two switches that let a verdict reach the mail server, and the junk folder becoming a fact that mailbox reads act on.
The last is true of a mailbox with no classification at all.

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
which flags it carries. Where a message lives is the server's, which is why acting on a verdict is a separate decision
behind switches of its own — described in [what an operator can let a verdict do](#what-an-operator-can-let-a-verdict-do).

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

One implementation ships: an Apache SpamAssassin daemon deployed beside this service, described in the section below.
The port is the boundary — nothing above it knows which scanner answered — so a deployment that switches the scanner on
without one registered still classifies, through the deterministic stage alone.

Where a scanner does answer with a score, it decides the verdict. Where the deterministic stage already reached spam,
that verdict stands whatever the scanner says: it rests on the provider's own decision or on where the mailbox filed
the message, both taken with network context that nothing after delivery has. A scanner that could not be reached
leaves the deterministic verdict exactly as it was, including undetermined.

An operator who does not administer the scanner can re-judge its score with a threshold of their own. It replaces the
scanner's rather than being compared beside it, so the record states one pair of numbers in one scale. It reaches no
other stage: a provider header carries a threshold in a scale this one knows nothing about.

## The Apache SpamAssassin scanner

The scanner is a **sidecar**: a container running Apache SpamAssassin's own daemon beside this service, reached over a
TCP line protocol on its port. It is deployed only where the switch is on — the Helm chart renders no scanner objects
without `spamScanning.enabled`, the Compose deployment starts nothing outside the `spam-scanning` profile, and the
Quadlet deployment has no unit to start because an operator who wants none of it never installs the file. Nothing
constructs a conversation with a daemon that was not asked for.

Where it *was* asked for and no daemon answers, **the host refuses to start**. That is the opposite of what one message
gets: a scan that fails leaves that message with the verdict its headers reached and the run carries on, which is right
for one message and wrong for a deployment. An instance whose sidecar never came up would classify everything from
headers alone, look entirely healthy doing it, and leave an operator reading a switched-on scanner as a second opinion
that was being taken. The startup check asks the daemon to score a synthetic message of MailFathom's own, so what it
proves is that something scans rather than that a port accepts connections; a daemon that answers but is not a spam
daemon fails startup the same way nothing at all does. It is one attempt rather than a wait loop: a sidecar still
fetching its rules is reached by the orchestrator restarting this process.

**The whole message is sent, unredacted.** A corpus scores what it reads, so a scanner shown a redacted message scores
the redactions — the markers become the text, the rules that read addresses and URIs find placeholders, and the number
that comes back describes a message nobody was sent. That is why the daemon belongs inside the deployment's own trust
boundary. Nothing refuses an address elsewhere, because a deployment may legitimately run one daemon for several
services on its own network and no rule about addresses tells the two apart; what an address outside that boundary
gives up is the owner's mail, in full, to somebody else.

Three bounds are the adapter's own, because each is a property of the daemon rather than of classification, and each is
configurable with the default stated in the
[configuration reference](../operations/configuration-reference.md#spamclassification):

| Bound | Default | Why that number |
| --- | --- | --- |
| The largest message sent at all | 512 000 bytes | The size SpamAssassin's own client truncates at, which is the scale its corpus was tuned against. A message past it keeps the verdict its headers reached, which is a fact about the message rather than about the deployment: retrying produces the same answer |
| How long one scan may take | 30 seconds | Long enough for a cold daemon to run a full corpus over a large message, short enough that a wedged one is noticed within one message rather than one run |
| How many scans run at once | 5 | The number of children the daemon spawns by default. Sending more does not scan more — it queues them inside the daemon, where this deployment's own timeout cannot see the wait — so the two numbers are one decision |

**A verdict is whole or absent.** Every failure — an unreachable daemon, a scan past its budget, an answer this adapter
could not read, a message past the size limit — leaves the classification with the deterministic stage's verdict and no
scanner stage and no scanner signals in the record. An answer that parsed but stated no score is treated the same way
rather than as a score of zero, because a zero would be recorded as a message a corpus read and found clean, which is a
stronger claim than *nothing scored it* and the one a reader would act on.

**Every scan records the corpus it ran under.** The protocol carries no rule-corpus identity, so the adapter reads the
release the daemon states about itself in the header it writes onto a scored message, and records it as
`spamassassin.<release>+<build>` beside every signal. A daemon whose own configuration removed that header is recorded
by the protocol version it spoke instead, which is deliberately weaker and deliberately differently shaped: a reader
comparing two classifications can see at a glance that one of them was reached against a daemon that would not name its
release.

**Rule updates and the DNS posture are the deployment's decision, and both defaults are stated.** The daemon fetches its
rule corpus on start and daily afterwards, which needs egress; the Compose deployment gives it that egress and the
Quadlet deployment does not, each saying so in the file. A frozen corpus scores today's mail worse than a fresh one, and
that is the trade an operator makes rather than one made for them. Separately, the daemon's blocklist rules would send
the sending addresses and the URI host names out of the owner's mail to third-party lists — **that is off in every
deployment asset here**, because sending what is being scanned to somebody else is what scanning inside the trust
boundary exists to avoid. Off, the daemon runs local rules only, and a deployment that wants those checks turns them on
knowing what leaves.

The image is pinned to an exact digest in all four places that name it, and moving it changes what a scan concludes —
which is what the recorded corpus revision exists to make visible. `THIRD_PARTY_LICENSES.md` records the image, its
licences, that whole messages are sent to it, and that it bundles no plugin reporting anything outside the deployment.

## What an operator can let a verdict do

Two switches, independent of each other and **both off by default**. With neither on, a verdict is recorded and no
mailbox is written to, which is what makes classification safe to watch for a while before it is allowed to act.

**File junk in the junk folder.** The message is moved on the mail server, through the same durable mutation record and
the same write session that carries a change somebody made by hand — described in
[IMAP synchronization](imap-synchronization.md#every-change-is-written-down-before-it-is-issued). The local row changes
afterwards, because synchronization observed the move; nothing writes a folder locally ahead of the server. What an
operator agrees to by switching this on is that spam moves in every client they open, and that correcting a false
positive means dragging the message back in any one of them — there is no MailFathom-specific undo to learn, and the
next section says what happens when they do.

**Mark it read.** The remote `\Seen` flag is set. This is the one authored act that sets it: synchronization and content
retrieval still never do, so reading mail on the owner's behalf goes on leaving the flag alone. A message the server
already reports read is not written to at all.

With both on, **the flag is set before the move**. On a server without RFC 6851 `MOVE` a relocation gives the message a
new UID, so a flag stored afterwards would be aimed at an occurrence the source folder no longer holds.

Nothing else is ever done. No delete, no flag other than `\Seen`, no folder created, nothing sent —
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
refuses the rest and this feature does not reopen that record.

### Where junk is filed, and why it need not be mirrored

The destination is whichever folder the account maps to the `Junk` role, or a folder named explicitly in the
configuration. **It does not have to be a folder MailFathom mirrors, and for most deployments it should not be**: the
point of filing spam is to be rid of it rather than to move it around inside the instance. Filing into an unmirrored
folder takes the local copy with it, under the account's own answer about mail it deletes; filing into a mirrored one
files identically and leaves the message stored, excluded from listing and search by the rule at the top of this page.
[IMAP synchronization](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is) holds what a folder's
participation decides.

The folder is found the same way a rule's destination is, through the one resolution every author of a filing shares:
a mirrored folder is read from the binding its own run recorded, and an unmirrored one is resolved against what the
server advertises the first time a filing needs it. **Classification asks for no folder to be created.** Whether a
mapping creates its folder when the server advertises none is that mapping's own declaration in the synchronization
section, decided once for every author rather than by this feature.

An account that maps no destination at all fails startup naming that account, rather than leaving its spam unfiled with
nothing said about why.

### A message the owner moved back is never filed again

Two rules keep filing from becoming an argument with the person whose mailbox it is.

A message already in the destination is not moved into it. And a message this feature has already asked to have filed,
which is not in the destination, is **left alone entirely** — not filed again, and not marked read again. Either
somebody moved it back out, which is exactly the correction a false positive is supposed to have, or the change is
written down and has not been carried out yet; asking again would argue with the first or duplicate the second. That is
read from the durable mutation record rather than from the message, because the record is what survives the message
moving.

The two readings are deliberately not told apart. Doing so would mean treating a change that ran out of attempts as
licence to file afresh — and a message that sat there failing to move is precisely the one somebody is most likely to
have moved by hand.

The protection reaches as far as the mirror does. A junk folder MailFathom does not mirror is one it stops seeing into,
so a message moved back out of *that* folder arrives as new mail and is classified afresh — which is the trade an
unmirrored destination makes, and the reason a deployment that expects to correct verdicts often may prefer a mirrored
one.

### The score an operator is willing to act at

A threshold of its own decides whether to act, separate from the one the verdict was reached under, so an operator can
label at five and move mail only from eight. It judges what a **scanner** scored, in the scanner's own scale, for the
reason the classification threshold reaches no other stage: a provider's header carries a threshold in a scale this one
knows nothing about, and a verdict resting on where the receiving server filed the message carries no score at all. Both
of those are the receiving server's own decision, taken with network context nothing after delivery has, and both are
acted on.

Raising the threshold is deliberately not the same edit as switching classification off. The verdicts go on being
recorded and only the acting stops.

### Asking twice is one change

A change is written down under an identity made of the occurrence, the mutation, and what asked for it — and what asked
here is the **profile the verdict was decided under**: the rule corpus the deciding stage ran against, together with the
score the operator acts at. Rescanning the same message against the same corpus therefore asks for nothing new, while a
corpus update or a threshold somebody moved asks afresh. A rule and a classification are told apart on the record by
their origin, so an operator reading a stuck change knows whether to look at a rule they wrote or at this section.

Where a filing has nowhere to go — a mapping withdrawn while mail was being classified, a mirrored folder no run has
bound yet, a folder the server advertises none of, or a role two folders carry — **nothing is written down at all**,
including a `\Seen` change asked for beside it. The same holds for an account a reload stopped declaring, whose answer
about mail it files away is then nobody's to guess. Marking spam read while leaving it in the inbox takes the unread
marker off mail that is still there, which is worse than waiting; a later attempt performs the pair whole.

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

The action switches are the same shape. With both off, deciding what a verdict causes is one property read and nothing
else: no mailbox is looked at, **no write session is ever obtained**, and no account holds the write connection that
would carry a change.

## Configuration

The `SpamClassification` section, in full, is in the
[configuration reference](../operations/configuration-reference.md#spamclassification). What it decides:

- whether classification runs at all;
- whether a configured scanner is consulted after the deterministic stage;
- which folder aliases are classified, defaulting to whichever alias each account maps to its inbox;
- the threshold a scanner's score is judged against, defaulting to the scanner's own.

Two blocks sit below it. `SpamClassification:Scanner` holds the daemon's address and bounds and is read only where the
scanner is switched on. `SpamClassification:Actions` holds the two switches, the folder junk is filed into, and the score
an operator is willing to act at; it is read per verdict, so switching filing on reaches the next one without a restart.

An operator who switched the scanner on and left classification off is told at startup rather than given the quiet
answer, and so is one who switched it on and named no address for it, one who asked for junk to be acted on with
classification off, and one whose accounts do not all map the folder a filing would go to. An unusable folder alias, an
out-of-range threshold, and a bound outside its range each fail startup naming themselves.

## What is not here

No on-demand run over a mailbox that is already stored, and nothing yet schedules classification as mail arrives,
because the durable job model it is to run as an execution in is not built. Until something calls it, the switches above
describe what a verdict causes rather than something happening on a schedule.

## Privacy

A classification is derived from mail content and inherits its classification, retention, and deletion constraints: the
record hangs off the message occurrence and is removed with it, so whatever erasure and retention already reach the
message reach the record too. A change a verdict asked for is the same kind of thing and follows the same rule: the
mutation record says where a person's mail was moved and holds a folder path, a UID, and a decision profile — never
anything from the message — and it is removed with the email it describes.

Nothing about a message's content reaches a log line or a telemetry attribute from any path here — not a header value,
not an authentication detail, not a subject. What is safe to report is the occurrence identifier, the folder alias, the
outcome, and the verdict. A fired rule name is safe as well: it is the corpus's own identifier and carries nothing from
the message that fired it.

The scanner path holds to the same rule from inside a failure. What the adapter reports when a scan does not produce a
verdict is the reason and, for an oversized message, the two sizes — never the message, never any part of it, and never
the daemon's address. The one startup failure this feature raises is `81003`, when the scanner is switched on and the
configured daemon cannot be reached, does not answer inside its bound, or answers as something that is not a spam
daemon; its message names the configuration key to repair rather than the address it tried.
