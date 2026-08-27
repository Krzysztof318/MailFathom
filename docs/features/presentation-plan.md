# The presentation plan

<!-- describes: backend/src/Application/Discovery/** -->

An answer about a mailbox is rarely a paragraph. A comparison wants a table, a course of events wants dates in order,
a question about people wants people. The presentation plan is the contract that lets a run say which of those an
answer is, in a form a client draws with ordinary typed UI and never evaluates.

This page describes the contract as it stands: what a plan holds, what its parts mean, how a client that is behind the
service reads one, and what is deliberately not in it. Producing a plan and rendering one are not described here,
because neither exists yet.

## Two properties the contract is built around

**It is closed.** A plan holds blocks drawn from a catalogue of nine types and nothing else. No part of it is markup, a
template, an expression, or a reference to code, and adding a tenth block type is a change to
`PresentationBlockType` that a reviewer reads rather than a string a model chose. That is what makes generative
presentation safe here: the flexibility is in which blocks a run composes and what it puts in them, and the drawing is
typed UI a client already has.

**It is versioned apart from the application.** A deployment and a client are updated separately — a browser bundle is
served by the deployment, but a desktop head is not — so a client will meet a plan written by a service ahead of it.
The plan carries a schema version, and every block carries the version of its own type, so such a client can draw the
blocks it recognizes, say what it cannot, and keep the rest of the run. Without both, the only safe answer to one
unfamiliar block would be to discard the whole thing.

Neither number is the application's version. The plan's schema version moves when the shape of the plan itself moves;
a block type's version moves when that block's own shape does, and for no other reason. A release that changes neither
leaves both where they are.

A block's version is written from the catalogue rather than supplied by whatever composed the plan, so nothing can
stamp a revision it did not write, and it is checked again on the way in: reading a plan whose block claims a revision
this build does not implement is refused rather than read as though it were the revision this build knows. Degrading
such a block instead of refusing the plan is a decision for a reader that has chosen to make it, and a reader making it
takes the number out of the JSON rather than binding it to a catalogue of its own.

## What a plan holds

| Part | What it is |
|---|---|
| `schemaVersion` | Which revision of this contract the plan was written against. |
| `blocks` | The blocks, in the order they are read. At least one, and at most twenty. |
| `citations` | Every source the blocks rest on, declared once each and named by an identifier local to the plan. |
| `limitations` | What the run knows about its own reach, as values from a closed set. Empty where it reached everything. |

A plan whose blocks name a citation it does not declare is refused when it is composed and again when it is read, which
is the one way a citation contract fails without anybody noticing.

## The nine block types

Each block carries the evidence behind it — see [§ What the correspondence does for a block](#what-the-correspondence-does-for-a-block) —
beside the data below.

| Block | Used when | What it carries |
|---|---|---|
| `answer` | one synthesized answer | the text, and how far it is worth trusting beyond its sources |
| `evidenceList` | the messages themselves are the point | per entry: one source, the part worth reading, a relevance, and how current the copy was |
| `timeline` | the question is about change over time | per entry: when, what happened, what it happened to, and its sources |
| `factTable` | amounts, terms, or versions are compared | columns from a closed catalogue, and one cell per column per row, each with its sources |
| `people` | the question is about people or organizations | per entry: a name, an address where there is one, where they stand, when they were last in contact, and sources |
| `threadState` | where a conversation stands is wanted | participants, plus what was agreed, what is open, and what somebody undertook |
| `attachmentGallery` | files are being looked for | per entry: the citation, the name, the declared media type, the size, and whether it can be opened |
| `draft` | the result is text to be sent | recipients, subject, body, and what has become of it locally |
| `suggestedAction` | there is a sensible next step | which step, why, what it would change, and whether it must be confirmed |

Three of them are worth a note.

**A fact table's columns come from a catalogue** rather than from the producer, and a column carries no heading. A
heading is words in somebody's language and the client is localized, so a producer that shipped the word "Amount"
would have shipped an English screen to a Polish reader. What a column does carry is the kind of value its cells hold,
which is how a client decides alignment. A cell the correspondence says nothing about carries no value at all, which is
a different thing from a cell somebody left blank.

**A draft is a proposal and never an act.** Its local status can say that it was composed, saved into the owner's
drafts, or queued in the outbox, and the set deliberately holds no member meaning sent: sending is something a person
does afterwards, through the surface that governs sending.

**A suggested action names a step from a closed set**, so a plan cannot propose something nobody wrote a control for.
It says what taking the step would change, and an action that sends mail is refused unless it also asks to be
confirmed — nothing here can recall a message that has left the deployment.

## What the correspondence does for a block

Every block carries three things about its own footing, and the combination is checked rather than trusted.

- **Support** is `Supported`, `Unsupported`, or `Conflicting`. A supported block names at least one citation, a
  conflicting one names at least two — a disagreement needs two sides — and an unsupported one names none, because a
  source that backed it would make it supported.
- **Citations** are the identifiers the plan declares, in the order they are worth reading.
- **Freshness** says whether the local copy behind the block was current, was known to be behind the mail server, or
  was never established, and carries when that was established for the first two.

Those three are what let a plan be honest about the two states a run reaches routinely over years of mail: a fact
nothing backs, and two sources that disagree. Both are worse as prose inside an answer than as values a client can draw
differently.

## What a citation resolves to

A citation resolves to one of exactly three things, and each of them is somewhere a reader can be taken:

- an **email**, for a fact that rests on the message as such;
- a **fragment**, one persisted passage of a message, for a fact taken from a particular part of a long one;
- an **attachment**, named by the message and the attachment's position within it, which is the same pair the download
  route is addressed with.

Each names the email by its local identity rather than by its remote occurrence, because a citation is followed inside
this deployment and an occurrence moves when a folder is renamed or a mailbox is rebuilt.

## How a plan cannot become code

The contract holds this by construction rather than by review.

- The block hierarchy is closed by a private protected constructor, so nothing outside the assembly that declares it can
  bring a block into being from data. What C# leaves reachable is the copy constructor every non-sealed record has,
  which the language requires to be protected; a type derived through it can only copy a block the contract already
  composed, carries that block's catalogue type, and is refused by the serializer as a type the contract never
  declared.
- Every free text in the contract is a `PresentationText`, which refuses a value that opens and closes as a tag — the
  shape a model returning XAML, HTML, or SVG produces — refuses control characters, and is bounded in length. It
  deliberately does not sanitize prose that merely mentions an angle bracket: mail quoted from a developer's inbox
  contains one, and the client draws the value into a typed text element rather than into a parser.
- Everything else is a number, a timestamp, an identity, or a value from a closed set. There is no member anywhere in
  the contract whose meaning is "render this". An address is the one of those whose validity is a domain rule rather
  than this contract's, and that rule is about the shape of an address rather than its length, so the length is bounded
  here — like every other text arriving from outside, and before anything expands it.

Those rules hold for a plan read off the wire exactly as for one composed in process, because deserialization goes
through the same constructors.

## The form on the wire

A plan is serialized by its own source-generated serializer, `PresentationPlanJsonContext`. Property names are camel
case, values from a closed set are written as their names rather than as ordinals, and each block carries `type` — the
catalogue identity — and `version`. An identity is what a client keys its renderers by, so it survives a rename of the
C# member; an ordinal would silently change meaning the first time a set were reordered.

Identities that this deployment already publishes elsewhere are written the way it publishes them: an email as the UUID
the client API names one by, an address as the message wrote it, without the comparison form used internally for
grouping.

The whole contract — both version numbers, every block identity, every column, every bound, and the JSON schema each
part serializes as — is recorded in `backend/tests/PublicSurfaces.UnitTests/presentation-plan-contract.json`, which a
test holds each build against. A diff there is a change to a published contract and belongs in the release's changelog.
It is not yet part of the generated OpenAPI document, because that document is derived from the endpoints the host maps
and no route serves a plan yet; the route that streams a run is what will put it there.

## A plan is mail

Everything in a plan comes from somebody's correspondence: its texts are quoted or summarized mail, its people are real
people, and its citations name messages in a mailbox. It is sensitive throughout and is classified that way by default
— it belongs in a response to whoever asked the question and nowhere else, never in a log line, a span attribute, or an
exception message. The bounds above are part of that too: they cap how much of a mailbox one answer can carry out of
the deployment at all.

## What is deliberately not here

- **Producing a plan.** What a run retrieves and which blocks it composes is separate work.
- **Rendering one.** The client's canvas and its block renderers are separate work again.
- **What a run spent.** Cost, cancellation, and the events a run streams are properties of the run rather than of the
  plan it produced.
