# The Discover run

<!-- describes: backend/src/AI/Discovery/**, backend/src/Application/Discovery/Planning/**, backend/src/Application/Discovery/Runs/**, backend/src/Application/Discovery/Streaming/**, backend/src/Host/Api/ClientDiscoveryRunEndpoints.cs, backend/src/Host/Api/DiscoveryRunLauncher.cs -->

A question about a mailbox arrives as words and a scope: *which supplier quoted least for the racking*, asked about one
conversation, about four selected messages, or about every folder of every account. Before anything is read, that has to
become two decisions — what to retrieve, and what an answer to it will look like. This page describes how those two are
made, what bounds each of them, and what a deployment does when it cannot make them at all.

What the decisions produce is a [presentation plan](presentation-plan.md): the typed contract an answer is delivered in.
The plan does not cross the wire whole — a run publishes its parts as they become ready, which is
[what a client watches](#a-run-is-watched-rather-than-waited-for) — and filling a derived block with facts is separate
work again.

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
| `findDocuments` — which documents exist and where | An attachment gallery |
| `unclassified` — none of the above, or nothing readable came back | An answer paragraph |

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
read: the owner, the accounts, the folders a mapping admits, and the junk decision. Discover adds one narrowing to it — the conversation or
the messages the question was actually asked about — and that narrowing travels the same path as every other part of the
scope, into the query itself.

That placement is the whole point. A question about four selected messages issues a search bounded to those four rows,
rather than ranking the mailbox and discarding what was not selected afterwards — which would read mail the question
never reached for, and would return nothing at all whenever the four fell outside the ranked window. Because the
narrowing is part of the scope, it reaches the lexical and the vector index equally, and it can only ever remove rows:
a conversation reaching into a folder this caller may not read still yields nothing from that folder, and an identifier
naming another owner's mail matches nothing rather than being refused.

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

The fallback plan is the one a deployment with no model at all would run: a single lookup for the question's own words,
classified as `unclassified`, which opens with an answer and its evidence. It is a worse plan than a derived one and it
is a plan, which is the difference between a question answered less well and a question refused. A derivation that fell
back is logged as unreadable, with no question text in the record.

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

Two routes rather than one because **a run outlives the connection that asked for it**. A phone that changes network
loses its reading connection and nothing else: the run goes on executing, and the client comes back to the second route
and is given what it missed. A single route that streamed the answer over the connection that asked would lose the whole
run instead, which is exactly the case this surface exists for.

### What a client is told, and in what order

Every event names its run and carries a sequence that starts at `1` and never skips, so a client renders in arrival
order and never has to sort. Six kinds are published, and the run ends on exactly one of the last two:

| Event | What it carries |
|---|---|
| `started` | The revision of the [presentation contract](presentation-plan.md), which is what a client keys its renderers by |
| `retrieval` | How far retrieval has got: lookups run, lookups refused, lookups planned, passages found — counts, and no mail |
| `citation` | One source the run declares, ready to be named by a block |
| `block` | One composed block, ready to be drawn |
| `completed` | The run finished, with what made the answer narrower than the question |
| `failed` | The run stopped, as one of `Unavailable`, `TemporarilyUnavailable`, `RetrievalRefused`, `TimedOut`, `Stopped`, or `Failed` |

Those six cross the wire as written here. This surface applies no naming policy to an enum, so the value is the member's
own name — unlike the same kind of value on the MCP surface, where the tool contract's serializer lower-cases the first
letter, and a client matching the wrong spelling falls through every branch it has.

`TimedOut` and `Stopped` are the pair worth telling apart, because the same cancellation produces both: the first says
the run spent the longest a run may take, the second says the deployment shut down while it was executing. One is about
the question having been more than a run could answer and the other says nothing about the question at all, so a client
offering to retry has a reason to offer it differently. `Failed` is the value that carries nothing: the reason is in the
deployment's own logs, which is where it is written when a run ends on something it has no name for.

Two orderings hold within that. **A source is always declared before the block naming it**, so a block can be drawn the
moment it arrives instead of being held until the run closes. And **an ending is the last thing a run publishes** —
nothing is appended after it, so a client that has seen one has seen the whole run.

Nothing crosses the wire as a whole presentation plan. What a client assembles is the plan; what the run publishes is
its parts. That is what makes *a failure keeps what came before* fall out rather than being arranged: the blocks that
arrived stay exactly where they were, and the failure is one more event behind them.

A run belongs to the owner who asked for it. Reading somebody else's is answered as **no such run** rather than as a
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

Four bounds, each with a stated behaviour when it is reached. All four are constants of this build rather than settings,
because none of them is a deployment decision an operator has any basis to take differently — and a Discover run's own
metered budget is separate work.

| Bound | What it is | What happens when it is reached |
|---|---|---|
| The longest one run may take | Five minutes | The run is stopped and ends as `failed` with `TimedOut` |
| Events one run may publish | Fifty-two — one opening, six lookups, twenty-four sources, twenty blocks, one ending | Nothing further is composed, and the run still ends: it completes stating `BlocksOmitted` |
| Runs this process holds at once | Eight | The asking route answers `429` rather than opening a ninth |
| How long a finished run is held | Five minutes after it was last read | The run is forgotten, and reading it reports no such run |

Nothing is held past the first two of those together, ended or not: a run older than that is one whose execution never
reported at all, and holding it would spend one of the eight slots until the process was restarted.

The buffer is bounded and **never evicts**, which is what makes resumption exact rather than best-effort: there is no
state in which a client asks for what it missed and is told the run has moved on. The last slot is reserved for the
ending, so a run that filled its buffer still says so instead of leaving a reader waiting. And a run that is still
executing is never forgotten however long it has been held — only a run that has ended starts its retention window, so
a client reconnecting to a slow run is never told it never existed.

### What a run composes today

Out of the correspondence alone, a run composes the one block whose contract is satisfied by the mail itself: an
evidence list, quoting the extracts it answered from, in the order retrieval handed them over. One citation per distinct
message, so two facts drawn from one message are visibly the same source, labelled by the message's subject where it
carried one a plan may carry and by the account and folder it was read from where it did not. The relevance beside each
entry is the place retrieval gave it expressed as a fraction, because retrieval attaches no score and the ordering is
the whole of what is known.

The blocks that state something *about* the correspondence rather than showing it — an answer, a timeline, a fact table
— need a derivation, and a composition that filled one from passage order would be inventing the facts a citation exists
to make checkable. Those are filled by separate work.

### None of it reaches a log

Everything a run publishes about the mail — a source, a quoted fragment, a subject — reaches the caller over these two
routes and nowhere else. The events that describe how a run is *going* carry counts and closed values alone, which is
what makes a run observable without any of the mail: a failure names one of six words, and retrieval progress names
four numbers.

The one thing that does reach a log is the failure a run has no word for. `failed` promises an operator can read what
happened, so the run itself publishes only the endings it can name and lets the rest travel out to the composition root,
which is where a logger exists — the application layer has none, deliberately. What is written there is the fault and
nothing about the question or the mail it read.

## What is deliberately not here

- **Filling the plan.** Which facts, columns, dates, and citations a derived block ends up carrying is separate work;
  what a run composes without one is the evidence list above.
- **What a run spent.** The single derivation call is not metered against a run ledger, because one turn with no tools
  cannot iterate; a Discover run's own budget is separate work, and the derivation and the bounds above join it when it
  exists.
- **Rendering.** No client draws a presentation plan yet.
