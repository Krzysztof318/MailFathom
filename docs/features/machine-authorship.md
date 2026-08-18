# Machine authorship

<!-- describes: src/Domain/Emails/Authorship/**, src/Application/Emails/Extraction/MachineAuthorshipEvaluatingEmailMimeReader.cs, src/Mcp/Tools/Authorship/** -->

MailFathom records what the receiving server established about *who* sent a message, and what this deployment makes of
that author. Neither says anything about *how* the message was written. A fluent, confident, well-structured message
reads as trustworthy to an agent whoever produced it, and the case where that matters most is the one where the text
also carries characters no mail client renders — the channel by which instructions meant for a reading agent are hidden
from the person the mailbox belongs to.

This page describes a third answer, recorded in the same shape as the other two: derived at extraction from the raw MIME
already stored, kept on the message, re-derivable, and published on every read tool's result.

**It is informational and it is not a safety signal.** A high likelihood says the text reads as machine written. It does
not say the message is unwanted, dishonest, or dangerous, and a great deal of ordinary correspondence is drafted with a
text generator by people who mean every word of it. Nothing here files, flags, hides, or refuses a message and no other
part of the system consults the value — publishing it is the whole of what this feature does with it, and what to make
of it is the reader's. The one thing that can act on it is a rule the owner wrote, which reads the band rather than the
number. Whether a message is *wanted* is [spam classification](spam-classification.md)'s
question and is reached by other means entirely.

**It is a heuristic estimate and not a measured probability.** Nothing here proves who or what wrote a message. No model
is asked, no service is consulted, and no corpus is compared against: the whole answer comes from characters in text
this deployment already holds. The number invites being read as a probability, which is exactly why every surface that
publishes it says it is not one.

## What is recorded

One reading per message, produced by the same extraction that reads the subject, the participants, and the body text —
so it costs no extra parse, no IMAP round trip, no network call of any kind, and it cannot reach the remote `\Seen`
flag. It is stored on the message; [the stored email schema](../architecture/stored-email-schema.md#the-machine-authorship-reading)
holds the columns.

| What it holds | Why |
| --- | --- |
| A band — not assessed, unlikely, possible, or likely | It is the value a reader branches on, and it stays legible without knowing where the thresholds sit |
| A likelihood from zero to one | It is what makes two messages in one mailbox comparable, which a four-valued band cannot do |
| The signals the text carried | A number whose derivation is gone cannot be judged; the signals are what make the reading a reason rather than a verdict |
| The profile the reading was reached under | Weights are tuned and thresholds move, so a stored number means nothing without the weighting that produced it |

**Not assessed is an answer, and it is not the same as unlikely.** It covers three ordinary states and is deliberately
not split into three: the deployment does not assess authorship, the message's body yielded no words to read, and the
message was stored before this deployment assessed anything. All three are the absence of a reading. *Unlikely* is a
reading that ran and found nothing, which is a statement about a message that had words.

## What the reading is made of

The signals fall into two groups that are weighed an order of magnitude apart, and the split is the substance of the
design rather than a presentation of it.

### Concealment — facts about the bytes

A person typing into a mail client does not produce characters that render as nothing. A program assembling text does,
and a payload hidden in a message is made of them. These are close to unambiguous, and each is narrowed before it is
reported so that a legitimate use of the same character is not read as one.

| Signal | What the text carried | What is deliberately not counted |
| --- | --- | --- |
| `tagCharacters` | Characters from `U+E0000`–`U+E007F`, which mirror printable ASCII one for one and render as nothing at all, so a run of them is a complete hidden message | A run belonging to a subdivision flag emoji: it spells its region in the same block and closes with the cancel character within the eight characters a region code needs. Nothing is forgiven until a run closes that way, and the forgiveness is spent from one message-wide budget of twenty-four characters — about four flags, far past what correspondence carries — so a payload wearing flag bases is read as what it is however finely it is chopped |
| `variationSelectorRun` | Eight or more variation selectors in one run, which selects nothing — no base character has that many renderings — and carries data instead; or eight across the message that each stand on top of another selector, which is the same payload cut into shorter runs | One selector after a base character, which is what the sequence is for, at any number of them: a message of nothing but emoji stacks no selector on another and adds nothing to that total |
| `hiddenCharacters` | Four or more of the zero-width space, word joiner, zero-width no-break space, soft hyphen, invisible mathematical operators, and Mongolian vowel separator | Fewer than four, since a single soft hyphen from a word processor reaches ordinary mail; and the zero-width joiner and non-joiner at any count, because both are how Indic and Arabic script and emoji sequences are written |
| `bidirectionalOverrides` | A direction override or isolate from `U+202A`–`U+202E` or `U+2066`–`U+2069`, which reorders what a reader sees away from what the bytes say | The same characters in a message that contains any right-to-left writing, where they are what makes it render correctly |

The bound on each of them is what keeps the signal about construction rather than about the writer's language or their
editor. A concealed instruction is a payload rather than a stray character, so it clears these comfortably.

### Prose — observations about style

Each of these is something a careful writer also produces, and **none of them means anything on its own**. Every prose
weight sits below the lowest band boundary, so no single one moves a message out of the unlikely band at all; it takes
several of them together to reach the middle one. They are read only where the text is at least 400 characters long,
because a two-line reply has no room for a habit and reading one would report the size of the sample rather than the
shape of the writing.

| Signal | What the text carried |
| --- | --- |
| `formulaicFraming` | Two or more distinct phrases from a small closed set a text generator opens, closes, and hedges with. Whole phrases rather than a vocabulary: single words drift into ordinary use as people read more generated text, so matching them would report the writer's register |
| `unspacedEmDashes` | Two or more em dashes closed up against a word on both sides, which is the most-cited typographic mark of generated prose and one of the weakest alone |
| `listScaffolding` | Three or more bullets each opening with a short bolded or colon-terminated label, which is the shape a generated summary takes |
| `uniformTypography` | At least three typographic quotation marks and no straight ones anywhere. Typing into a mail client mixes the two, because substitution reaches some keystrokes and not pasted text, quoted code, or a URL; text assembled in one pass is uniform |

### Which text each group is asked of

Two texts rather than one, because the two groups ask different questions of a message.

- **Concealment is read from the body as it was delivered**, quoted history included. A payload hidden inside a quoted
  block is still hidden inside this message and is still what a reading agent would be handed.
- **Prose is read from the trimmed text alone.** Quoted history and a signature block are somebody else's writing, and
  reading them would report their habits as this sender's.

The reading also happens *below* [sensitive-content redaction](sensitive-content-scanning.md), so it judges the words the
message carried rather than the words a scanner rewrote in it. That is safe because nothing about the text leaves the
step: what comes out is a set of signal names, a number, and a band, none of which can carry a fragment of the message.

## How the signals become one number

Each weight is read as the chance that its signal alone accounts for the text, and the reading is the chance that at
least one of them does. Two properties follow, and both are why the combination is not a sum with a cap: signals
reinforce each other without any of them pushing the result past the scale, and **adding a signal to a message can only
raise its likelihood**. A sum would let two moderate prose habits outweigh one conclusive concealed payload, and
everything above the cap would read identically.

The bands are read off the number at 0.30 and 0.65. So a single concealment signal reaches at least the middle band and
the two strongest reach the top one by themselves, while the prose signals reach the middle band only in combination.

**The weights are the project's and there is deliberately no configuration for them.** A number an operator can move is
a number every stored answer has to be read against, and the value of the reading is that two messages in one mailbox
are comparable. What an operator decides is whether the reading runs at all.

## The reading outlives the weighting

The answer is stored on the message rather than computed when the message is read, so a reader is not shown a number
that quietly changed under them when a weight was tuned. What makes that legible is the **profile revision** recorded
beside it: a digest of the weights and the thresholds, so two readings carrying different revisions were reached under
different weightings and one carrying none was reached by nothing at all.

It is a digest rather than a version number somebody raises by hand, for the reason [the sender-trust policy
revision](sender-authentication.md#the-verdict-outlives-the-list) is one: a number maintained by hand is wrong the first
time a weight moves without it, and the whole value of the column is that it cannot be.

What re-judges mail already stored is [`mfctl mailbox rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release),
the same deliberate act that re-reads mail after an account gains a trusted authority. The migration that adds the
columns fills every stored message in with the not-assessed state, which is what was true of it.

## Turning it off

`MailSynchronization:AssessMachineAuthorship` is the whole of the configuration, and it defaults to on. The assessment
costs one pass over text the extraction has already produced — no network, no model, no DNS, and nothing an operator has
to configure for it to mean something — and the strongest thing it reports is a message carrying characters no mail
client renders, which is worth knowing on a first run rather than after somebody has thought to look for it. [The
mail configuration](../operations/configuration-mail.md#mailsynchronization) states where the setting lives.

A deployment that turns it off records the not-assessed state, which is exactly what a message with no readable body
carries and what mail stored before this release carries. That is deliberate: **nothing about a stored row says which of
those reasons produced it**, so the column describes the mail rather than the operator.

## What the read tools publish

Every read tool publishes the band and the likelihood, and only the single-email read publishes what they were reached
from — the same split the sender verdicts already use, and for the same reason. A listing exists to let a reader
recognize a message, and the signals are how a reader judges a number on a message they have already found.

| Tool | What it publishes |
| --- | --- |
| `list_emails` | `machineAuthorship`, on each listed email's summary |
| `search_emails` | The same, by republishing that summary rather than reshaping it |
| `get_email_content` | The same, and beside it `authorshipEvidence`: the signals the text carried and the profile revision |
| `ask_mail` | The same, on each citation, without the evidence |

The published descriptions are the advertised output schema and are therefore the whole of what a model reading a result
is told, so they carry the two things easiest to get wrong about this value: that it is a heuristic estimate rather than
a measured probability, and that a high reading is an observation about the text rather than a finding against the
sender. [MCP tools](mcp-tools.md#list_emails) holds the published shape of each result.

**The signal list names which signals fired and nothing else.** It carries no position, no count, and no matched text,
so no part of the message reaches a caller through it that the caller could not already read.

## What MailFathom does not do

- It asks no model, consults no service, and compares against no corpus. Every signal is a property of characters in
  text this deployment already stored.
- It acts on the reading nowhere by itself. Nothing files, flags, hides, or refuses a message because of it, and it
  takes no part in spam classification, ranking, retrieval, or answering. What can act on it is a rule the owner wrote:
  `machineAuthorship` is a [fact a condition can read](mail-rules.md#the-facts-a-condition-can-read), carrying the band
  and not the likelihood, because a number comparable only within one profile is not something to write a threshold
  against.
- It does not claim to distinguish an AI text generator from any other program that assembles text. What the signals
  establish is that the text was constructed rather than typed, which is why the value is named for machine authorship
  and not for a particular tool.
- It does not let a caller filter or sort a listing or a search by the reading. That is a question about what may be
  asked for rather than about what a result carries, and it is a decision of its own.

## What is not recorded anywhere else

The text a reading is taken from is mail content and personal data. No log line, metric, exception message, or audit
record carries any part of it, and none carries the signal set either — what those may report is the occurrence identity.
The signals and the number travel with the message's own read results and nowhere else.
