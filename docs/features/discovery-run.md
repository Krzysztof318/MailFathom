# The Discover run

<!-- describes: backend/src/AI/Discovery/**, backend/src/Application/Discovery/Planning/**, backend/src/Application/Discovery/Runs/** -->

A question about a mailbox arrives as words and a scope: *which supplier quoted least for the racking*, asked about one
conversation, about four selected messages, or about every folder of every account. Before anything is read, that has to
become two decisions — what to retrieve, and what an answer to it will look like. This page describes how those two are
made, what bounds each of them, and what a deployment does when it cannot make them at all.

What the decisions produce is a [presentation plan](presentation-plan.md): the typed contract an answer is delivered in.
Filling that plan with facts and citations is separate work again.

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

## What is deliberately not here

- **Filling the plan.** Which facts, columns, dates, and citations the composed blocks end up carrying is separate work.
- **What a run spent.** The single derivation call is not metered against a run ledger, because one turn with no tools
  cannot iterate; a Discover run's own budget is separate work, and the derivation joins it when it exists.
- **Rendering.** No client draws a presentation plan yet.
