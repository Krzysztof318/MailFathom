# The Discover run

<!-- describes: backend/src/AI/Discovery/**, backend/src/Application/Discovery/Planning/**, backend/src/Application/Discovery/Runs/**, backend/src/Application/Discovery/Streaming/**, backend/src/Host/Api/ClientDiscoveryRunEndpoints.cs, backend/src/Host/Api/DiscoveryRunLauncher.cs -->

A question about a mailbox arrives as words and a scope: *which supplier quoted least for the racking*, asked about one
conversation, about four selected messages, or about every folder of every account. Before anything is read, that has to
become two decisions — what to retrieve, and what an answer to it will look like. The mail is then read, and what it
says is composed into the answer. This page describes how those decisions are made, what bounds each of them, what the
composition may and may not say, and what a deployment does when it cannot do any of it.

What a run produces is a [presentation plan](presentation-plan.md): the typed contract an answer is delivered in. The
plan does not cross the wire whole — a run publishes its parts as they become ready, which is
[what a client watches](#a-run-is-watched-rather-than-waited-for).

---

## Two decisions, and only one of them is a model's

A run derives its plan from one call to the chat endpoint, and that call decides less than it appears to. The model is
asked for exactly two things: **what kind of question this is**, and **which searches would find the answer**. It is
never asked which blocks the answer should hold — the instruction it is given does not contain the word.

The composition is derived in code, from the kind of question, through a mapping this build fixes:

| The kind of question | What the answer opens with |
|---|---|
| `findFact` — one fact somebody stated | An answer paragraph |
| `trackChange` — how something moved over time | A timeline |
| `compareTerms` — several offers, terms, or positions held against each other | A fact table |
| `findDocuments` — which documents exist and where | An answer paragraph |
| `unclassified` — none of the above, or nothing readable came back | An answer paragraph |

A question about documents opens with an answer paragraph rather than with the attachment gallery the catalogue holds,
because a gallery entry names an attachment inside a message and retrieval returns a passage that carries no attachment
coordinate. The gallery waits on a passage that carries one; until then, saying which documents exist in prose over the
messages that hold them is what a run can honestly compose.

Every composition then ends with an evidence list, because a Discover answer is only worth as much as the mail behind
it, and the list is where a reader goes from a claim to the message that made it.

Deriving it here rather than asking for it is what makes a run reproducible in the part that matters. Two people asking
the same question of the same mailbox get the same composition, because nothing about the composition is generated; a
model that answers `compareTerms` twice cannot produce a table once and a paragraph the next time. It is also what keeps
the block catalogue closed — a model cannot name a block a client has no renderer for, because it is never naming blocks.

## What retrieval is allowed to do

The searches the model proposes are a plan, not commands. Each becomes an ordinary knowledge lookup, run in order
against the same retrieval the rest of the system uses, and the plan is bounded before it runs:

- **At most six lookups.** More than that is a model misjudging a question rather than a question that needs them, so
  the surplus is dropped and the first six run.
- **A lookup whose words could not be searched for is dropped**, and the rest of the plan still runs. Blank query text
  and text longer than a search query may be are both this case.
- **`sufficientPassages` says when to stop**, and is clamped into what one retrieval may return — never below one, never
  above the deployment's own passage ceiling. A run stops issuing lookups as soon as it holds that many distinct
  passages, so a question the first search answers costs one search.
- **A passage is counted once.** Two lookups finding the same extract of the same message contribute one passage, which
  is what stops a model from filling the budget by asking the same thing in four wordings.

What a run reports back is the passages, which ranking answered them, how many lookups ran, and how many were refused.
A lookup a filter refuses is counted and skipped; only a plan whose every lookup was refused fails, and it fails with
that refusal rather than with an empty answer.

## The scope is what a question was asked about, and it is applied in the database

A question carries a [mailbox scope](mailbox-queries.md#what-each-filter-accepts-and-what-it-refuses) like any other
read: the user, the accounts, the folders a mapping admits, and the junk decision. Discover adds one narrowing to it — the conversation or
the messages the question was actually asked about — and that narrowing travels the same path as every other part of the
scope, into the query itself.

That placement is the whole point. A question about four selected messages issues a search bounded to those four rows,
rather than ranking the mailbox and discarding what was not selected afterwards — which would read mail the question
never reached for, and would return nothing at all whenever the four fell outside the ranked window. Because the
narrowing is part of the scope, it reaches the lexical and the vector index equally, and it can only ever remove rows:
a conversation reaching into a folder this caller may not read still yields nothing from that folder, and an identifier
naming another user's mail matches nothing rather than being refused.

## What the model is shown

One call leaves the deployment per derivation, and it carries the question and a description of its scope. It carries no
mail: no message, no subject, no address, no attachment, and no extract — deciding what to search for is a reading of the
question, and mail has not been retrieved yet when it is made.

The description of the scope is a shape and a count, never an identifier. *Four individually selected messages, and
nothing else*, *one conversation, and nothing else*, *three folders of this mailbox*, *every folder of two mail accounts,
which may hold years of mail* — enough for a model to tell a question about a thread from one about a decade of mail,
and not enough to name an account, a folder, or a message. The question itself passes the deployment's
[sensitive-content egress guard](sensitive-content-scanning.md) first, exactly as any other prompt does, so a credential
somebody pasted into a question is withheld here too.

The call is made with no tools at all. A derivation that cannot call anything cannot iterate, cannot retrieve, and
cannot spend more than the single turn it was given.

## When a model does not answer, a run still runs

Nothing about a derivation is allowed to end a question. An answer that is not JSON, is JSON of the wrong shape, is
fenced in a code block, is wrapped in a sentence, names a kind of question this build does not know, or never arrives
because the provider refused the call — every one of those is read as far as it can be and then falls back rather than
failing. So does a call the deployment never managed to make: an endpoint the configuration in force no longer declares,
or a key behind a reference that did not resolve, ends the derivation exactly as a refusal does rather than ending the
run around it.

**A spend ceiling is the one exception**, in the derivation and in the composition alike. Every other failure leaves a
worse plan and the run goes on, because a question answered less well is better than a question refused. A ceiling is
not that: it is the reason the run is being stopped, so falling back would spend the next call against a budget that
has already run out and would tell somebody their answer was thin when what happened was that the deployment stopped
paying.

The fallback plan is the one a deployment with no model at all would run: a single lookup for the question's own words,
classified as `unclassified`, which opens with an answer and its evidence. It is a worse plan than a derived one and it
is a plan, which is the difference between a question answered less well and a question refused. A derivation that fell
back is logged as unreadable, with no question text in the record.

## Composing the answer, and what it may not say

Once the plan has run, a second call puts the question and the extracts to a composing agent — its own instruction, its
own name, no tools at all, and the same instruction envelope every agent here carries. It is shown a numbered set of
extracts and nothing else about the mailbox.

**The citations are minted before the model sees anything, and they are the only ones a claim may rest on.** The run
declares one source per distinct message it retrieved, names it `s1`, `s2`, and so on, and shows the model those names.
A name the model invents resolves to nothing, so the claim resting on it is read as resting on nothing — which is what
makes a composed answer checkable at all. Nothing a model writes ever becomes a reference to mail.

**At most twenty-four distinct messages become sources of one run**, taken in the order retrieval ranked them. A message
past that is neither shown to the model nor listed in the evidence, so a run over a wide question answers from the
mail it names rather than from mail a reader has no way to reach.

**Four judgements are the composition's own and are not negotiable by what the model wrote.**

- **The support** follows from whether the cited names resolve, whether the model reported a disagreement between them,
  and how current the accounts they were read from are — supported, unsupported, conflicting, or stale, in the sense
  [the presentation plan](presentation-plan.md#what-the-correspondence-does-for-a-block) fixes. An answer resting on
  nothing carries a sentence about the run rather than the model's own prose, because an unsupported block's prose
  would be exactly the sentence nobody wrote.
- **The disagreement is kept as a disagreement.** Where the model reports two or more sides, each naming sources it was
  actually offered, the block carries every side with its own citations rather than one figure the run chose.
- **The confidence is capped by the support**, on the definition that page states, so a model cannot report a settled
  answer over a contradiction, over sources that are all behind, or over no source at all.
- **The freshness of a block is the freshness of the accounts its own sources came from**, reduced to the worst of them:
  a block resting on one current message and one from a mailbox that is behind is a block a reader should treat as
  behind.

**The evidence list is built from what retrieval returned rather than from what the model cited**, so a reader checking
a thin answer sees the mail the run actually read. And the material the model is asked for follows the intent: events
for a question about change, columns and rows for a comparison, and the answer alone otherwise. What it gives back is
held to the contract — a column the catalogue does not hold is dropped, a row whose cells do not match the columns is
dropped rather than padded, and a shape that ends up empty falls back to the answer itself rather than to an invented
one.

A question looking for documents is answered this way today as well. A passage carries the message it was cut from and
not the attachment within it, so a gallery entry would name a file this run cannot resolve; an attachment nothing could
read is a state the plan expresses and nothing yet produces.

**What the run says about its own reading** is composed here too: one coverage entry per account the scope reached, and
the limitations the run observed rather than was told — the local copy behind where an account is known to be, sources
unavailable where a lookup was refused, semantic ranking unavailable where the deployment fell back to words alone, and
retrieval truncated where the run found more distinct messages than the twenty-four it may declare as sources. A plan
carrying no limitation states that the run reached everything it was asked about, so a run composed over a cut set says
so rather than staying silent.

### What leaves the deployment, and what does not

The extracts are mail, and they pass the [sensitive-content egress guard](sensitive-content-scanning.md) on the way to
the provider exactly as the question does. What the **plan** quotes is not guarded, deliberately: that is the user's
own mail going back to the user, and redacting it there would hide from somebody what they already have. So two copies
of each extract exist for the length of one call — the guarded one the provider is shown, and the user's own one the
evidence list quotes.

One call leaves per composition, with no tools, so a question costs two turns however much mail it read. Both of them
are charged to the run's own ledger and to the deployment's period ledger before they are sent, which is what makes the
ceilings in [what bounds a run](#what-bounds-a-run) real rather than nominal.

### When the composing model does not answer

A provider that failed, timed out, or answered with something unreadable does not end the run. The composition falls
back to a result saying the sources do not answer the question, over the same citations, the same evidence list, and
the same account coverage — which is the honest answer for a run whose model was unavailable, and the one a person is
owed rather than an error page: the mail was read, and what could not be done was the reading of it.

## When a deployment refuses the question outright

Two refusals come before any of the above, and neither is a fallback.

- **The caller must hold the grant that lets mail reach a chat provider.** A Discover run is subject to it exactly as
  asking a question is, and a caller without it is refused before the capability is even read.
- **The deployment must currently answer questions at all.** The reading is the same one
  [mail answering](mail-answering.md#when-a-deployment-answers-questions-at-all) publishes, and the two states refuse
  differently on purpose: *inactive* is a deployment that does not do this — no chat endpoint, or no embedded mail —
  and *degraded* is one that does and cannot right now, which is worth retrying. A build with no planner configured
  reports the first of those, because a deployment that cannot derive a plan does not run Discover.

## A run is watched rather than waited for

A run takes as long as a model and a mailbox take, and what it produces becomes readable in pieces — so it is delivered
as it happens rather than as one answer at the end. Two routes do that, both under the
[client endpoint](../operations/client-endpoint.md) and both published under the same grant that governs asking a
question anywhere else:

| Route | What it does |
|---|---|
| `POST /api/client/discovery/runs` | Asks the question. Answers `202` with the run's identifier and the address its events are read at, as soon as the question and its scope are known to be answerable. |
| `GET /api/client/discovery/runs/{runId}/events` | Reads that run, from its beginning or from wherever a dropped connection left off. |
| `DELETE /api/client/discovery/runs/{runId}` | Stops that run. Answers `204` once the run has been told to stop, and `404` for a run this user did not start or this process no longer holds. |

Several routes rather than one because **a run outlives the connection that asked for it**. A phone that changes network
loses its reading connection and nothing else: the run goes on executing, and the client comes back to the reading route
and is given what it missed. A single route that streamed the answer over the connection that asked would lose the whole
run instead, which is exactly the case this surface exists for.

That is also why stopping is a route rather than a closed connection. A client that stops reading has said nothing about
the run, which goes on calling the provider and drawing mail out of the mailbox for nobody — so **closing the stream is
looking away and the `DELETE` is stopping**, and only the second one stops the spending. A run that finished a moment
before the request arrives answers `204` as well, because whoever asked could not have known it ended and reporting that
as a failure would make a control that worked look broken.

### What a client is told, and in what order

Every event names its run and carries a sequence that starts at `1` and never skips, so a client renders in arrival
order and never has to sort. Six kinds are published, and the run ends on exactly one of the last two:

| Event | What it carries |
|---|---|
| `started` | The revision of the [presentation contract](presentation-plan.md), which is what a client keys its renderers by; the ceilings that will stop this run; and the endpoint alias and published model name that will answer it |
| `retrieval` | How far retrieval has got: lookups run, lookups refused, lookups planned, passages found — counts, and no mail — beside what the run has spent so far |
| `citation` | One source the run declares, ready to be named by a block |
| `block` | One composed block, ready to be drawn |
| `completed` | The run finished, with what made the answer narrower than the question, what it read of each account, and what it spent |
| `failed` | The run stopped, as one of `Unavailable`, `TemporarilyUnavailable`, `RetrievalRefused`, `TimedOut`, `Stopped`, `Failed`, `Cancelled`, `PeriodSpent`, or `RunSpent`, with what the run had spent when it stopped and, for `PeriodSpent`, when asking again would be admitted |

Those six cross the wire as written here. This surface applies no naming policy to an enum, so the value is the member's
own name — unlike the same kind of value on the MCP surface, where the tool contract's serializer lower-cases the first
letter, and a client matching the wrong spelling falls through every branch it has.

`TimedOut`, `Stopped`, and `Cancelled` are the three worth telling apart, because the same cancellation mechanism
produces all of them: the first says the run spent the longest a run may take, the second says the deployment shut down
while it was executing, and the third says somebody asked for it to stop. One is about the question having been more
than a run could answer, one says nothing about the question at all, and one is the answer to a control the person
themselves used — so a client offering to retry has a reason to offer it differently, or not to offer it at all.
`Failed` is the value that carries nothing: the reason is in the deployment's own logs, which is where it is written
when a run ends on something it has no name for.

**A spend ceiling is a state rather than an error**, which is what the last two are for. `PeriodSpent` says the
deployment has answered as many questions this period as it may and carries the instant the period rolls over, so a
client can say *available again at* rather than *something went wrong*; `RunSpent` says this one question reached what a
single question may cost and carries no instant, because waiting is not what would let it through. Neither of them names
an amount anybody else spent: what a refusal carries is this run's own consumption and nothing about the period's total,
so what one person is told about a shared ceiling is never a reading of what another person did with it.

### Which model answered

A person comparing two answers reads a fast cheap one differently from a careful one, so the run says which model
produced it — on the `started` event, before anything has been composed.

Two names go out, and they answer different questions. **The endpoint alias is always published**: it is the operator's
own name for a configured endpoint, it names nothing outside this deployment, and it is what an operator matches an
answer against their own configuration by. **The published model name is whatever the operator chose to publish**, and
it is a setting of its own — `Chat:PublishedModel`, described under
[the AI configuration](../operations/configuration-ai.md) — rather than the routed model name the deployment sends to
the provider. Those are separate because a routed name can carry a deployment identifier, a tenant, or an internal
routing label that is nobody's business outside the deployment, and publishing it by default would leak the
configuration to every client. An operator that sets nothing publishes nothing: the field arrives empty, and a client
shows the alias alone.

Two orderings hold within that. **A source is always declared before the block naming it**, so a block can be drawn the
moment it arrives instead of being held until the run closes. And **an ending is the last thing a run publishes** —
nothing is appended after it, so a client that has seen one has seen the whole run.

Nothing crosses the wire as a whole presentation plan. What a client assembles is the plan; what the run publishes is
its parts. That is what makes *a failure keeps what came before* fall out rather than being arranged: the blocks that
arrived stay exactly where they were, and the failure is one more event behind them.

A run belongs to the user who asked for it. Reading somebody else's is answered as **no such run** rather than as a
refusal, so an identifier says nothing about whether it exists.

### Resuming a dropped connection

Resumption is the protocol's own: each event goes out under its sequence as the event id, and a client reattaching
sends the last one it holds back in `Last-Event-ID`. A browser's `EventSource` does that by itself, so a client that
never wrote the reconnection gets the resumption too.

A header that is absent, blank, or names a place this run never reached reads as the beginning. That is the safe
direction: no client can hold such a value honestly — a sequence is only ever learned by being sent it — so what it
means is a client whose state belongs to some other run, and replaying costs it a few events it already has rather than
an answer it never receives.

### Why Server-Sent Events, and why not SignalR

A run is one-directional, its events are small JSON documents, and the resumption above is already in the protocol. So
what a plain chunked HTTP stream would need built by hand — framing, event names, an identifier per event, a
reconnection that says where it left off — is what this gets from the browser's own `EventSource` and from
`TypedResults.ServerSentEvents` on the server.

SignalR would carry it too, and is deliberately not introduced. What it adds over this is multi-directional messaging
and a connection lifecycle of its own, and a run has no use for either: nothing is sent back up the stream, and the run
is addressed by an identifier rather than by a connection. Nothing about the events depends on the choice, which is the
point — the contract is the sequence, so a deployment that ever needed a different transport would serve the same events
over it.

### What bounds a run

Seven bounds, each with a stated behaviour when it is reached. The first four are constants of this build rather than
settings, because none of them is a deployment decision an operator has any basis to take differently. The last three
are the deployment's own answering ceilings, configured where
[mail answering](mail-answering.md#what-one-question-may-spend) describes them, and Discover is bound by exactly the
ceilings every other question is — a second set of Discover-specific budgets would be a second thing to keep in step
with the first.

| Bound | What it is | What happens when it is reached |
|---|---|---|
| The longest one run may take | Five minutes | The run is stopped and ends as `failed` with `TimedOut` |
| Events one run may publish | Two hundred and twenty-eight — one opening, six lookups, two hundred sources, twenty blocks, one ending | Nothing further is published, and the run still ends: it completes stating `BlocksOmitted` |
| Runs this process holds at once | Eight | The asking route answers `429` rather than opening a ninth |
| How long a finished run is held | Five minutes after it was last read | The run is forgotten, and reading it reports no such run |
| What one run may draw out of the mailbox | The deployment's own per-run character ceiling | Retrieval is trimmed to whole passages that fit and the run answers from them, stating `RetrievalTruncated` |
| What one run may call and consume | The deployment's own per-run call and token ceilings | The next call is refused before it is sent, and the run ends as `failed` with `RunSpent` |
| What every run of one period may add up to | The deployment's own per-period run and token ceilings | The question is refused before anything is read or derived, and the run ends as `failed` with `PeriodSpent` |

The retrieval ceiling is the one that trims rather than refuses, because a question with some mail already retrieved is
answerable and the model is told there is no more. Everything a spend ceiling stops is stopped before it is spent, with
the one exception the ledger states: a token ceiling can only be checked against what earlier calls reported, so the call
that crosses it is paid for.

Nothing is held past the first and the last of those together — ten minutes — whether it ended or not. A run that old
is one whose execution never reported at all: a task that never ran, or a fault between the run being opened and being
started. Without that ceiling such a run would spend one of the eight slots until the process was restarted, and eight
of them would leave a deployment answering `429` to every question.

The buffer is bounded and **never evicts**, which is what makes resumption exact rather than best-effort: there is no
state in which a client asks for what it missed and is told the run has moved on. The last slot is reserved for the
ending, so a run that filled its buffer still says so instead of leaving a reader waiting.

The two windows are not the same window, and which one a run is held on is decided by whether it has ended. **A run
that has ended is held for the retention window**, measured from the last time anything read or wrote it, so a client
that is reading is never cut off mid-stream and one that never came back is dropped rather than kept for the life of
the process. **A run that is still executing is held on the wider ceiling above instead**, so a slow run is never
forgotten out from under a client reconnecting to it while its provider call is still outstanding. A healthy run never
meets that ceiling: it is stopped at the five-minute mark and ends there, which starts its own retention window well
before the wider one could elapse.

### What the stream publishes is the composition, not a second one

The plan a run publishes is the one [the composition produced](#composing-the-answer-and-what-it-may-not-say), part by
part. Nothing about it is decided by the stream: the citations are the plan's citations, the blocks are the plan's
blocks, and the limitations and the coverage on the ending are the plan's own. What the stream decides is the order they
go out in, which is what lets a client draw a block the moment it arrives instead of holding everything until the run
closes.

That also fixes what a client holds at the end. A client that read the whole stream holds every part of the plan — its
blocks, its sources, what made the answer narrower than the question, and what the run read of each account — so the
stream is a delivery of the plan rather than a summary of one.

### None of it reaches a log

Everything a run publishes about the mail — a source, a quoted fragment, a subject — reaches the caller over these
routes and nowhere else. The events that describe how a run is *going* carry counts and closed values alone, which is
what makes a run observable without any of the mail: a failure names one of nine words, retrieval progress names four
numbers, and what a run spent is four more. **No cost record carries mail content, a query, or an address** — a spend is
a count of characters and messages rather than of which ones, so nothing about what a run cost says what it read.

The one thing that does reach a log is the failure a run has no word for. `failed` promises an operator can read what
happened, so the run itself publishes only the endings it can name and lets the rest travel out to the composition root,
which is where a logger exists — the application layer has none, deliberately. What is written there is the fault and
nothing about the question or the mail it read.

## What is deliberately not here

- **The blocks a composition does not fill.** People, thread state, attachment galleries, drafts, and suggested actions
  are part of the contract and are composed by nothing here: the intent decides between an answer, a timeline, and a
  fact table, and the rest wait for the surfaces that produce them.
- **A price.** What a run reports is its own consumption against the ceilings that will stop it — calls, tokens,
  characters, and messages — and never a currency amount. What those cost is a matter between an operator and a provider
  whose prices MailFathom does not know and would be wrong about the moment they changed.
- **A live Case's own updates.** Nothing here updates an answer on its own yet. When something does, it runs through the
  same ledgers as a question somebody asked, so unattended spend is bounded by what is written above rather than by a
  second mechanism.
- **Rendering.** No client draws a presentation plan yet.
