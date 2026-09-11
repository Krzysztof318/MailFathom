# A conversation's state

<!-- describes: backend/src/Application/Emails/ThreadStates/**, backend/src/AI/ThreadStates/**, backend/src/Infrastructure/Persistence/ThreadStates/**, backend/src/Infrastructure/Persistence/Entities/EmailThreadStateEntity.cs, backend/src/Infrastructure/Persistence/Entities/EmailThreadStateEntryEntity.cs, backend/src/Host/Configuration/Chat/ThreadStateOptions.cs, backend/src/Host/Api/ClientMailThreadStateEndpoint.cs -->

A long correspondence is read by scrolling it. Somebody coming back to a negotiation after a week has to reconstruct
what was agreed, what is still open, and what they themselves promised, out of eight messages that each quote the one
before. **A conversation's state is that reconstruction, derived once and written down** — what its people settled,
what they raised and left open, what anybody undertook, and how a document they exchanged changed between two versions
of it — so the block stands beside the correspondence instead of being asked for again.

Deriving it while a screen waits would be a model call per view, on somebody's mail, at somebody's expense — and a
summary produced for a screen and discarded is a message with better placement. A state that is derived, kept, updated
when the conversation changes, and cited is an object a screen can be built on and a later stage can read.

## What a statement is

A statement is never a sentence on its own. Each one carries:

| Part | What it holds |
|---|---|
| **Aspect** | Which of the four it is: an *agreement*, an *open question*, a *commitment*, or a *version difference*. |
| **Text** | What it says, as one plain sentence, at most 240 characters. |
| **Owed by** | Who undertook a commitment, at most 120 characters. Only a commitment may carry one, and only where the conversation named somebody. |
| **Due date** | When a commitment falls due. Only a commitment may carry one, and only where the conversation named a date. |
| **Sources** | The messages the statement rests on, at most four, best first. |

The four aspects are apart from one another because a reader acts on each differently — an agreement is relied on, an
open question is answered, a commitment is kept, and a difference between two versions of a document is checked — and
somebody scanning for what they still owe should not have to find it among what was settled.

**A statement with no source is refused rather than stored.** It would read on a screen exactly like one a message
supports, and no reader could tell them apart. The sources name messages in the same terms the Discover run's citations
name them, so the same reader resolves both: following one is opening a message the mailbox already holds.

**One statement is kept per aspect**: the first, because the derivation is asked to put the most important first. The
block is a row of cards a reader glances at, one line under each label, and a second agreement beside the first is a
paragraph nobody reads in the place a glance was promised. A state recorded while more were kept is read as the first
statement of each aspect rather than derived again, which is what it would have led with anyway.

## Nothing to say, and too much to read

A record with **no statements at all** is a real outcome rather than a failure. It says a derivation ran and found
nothing to put beside the correspondence — two messages arranging a time, a receipt and its acknowledgement — and
storing it is what stops the same nothing being paid for again.

A conversation longer than one derivation may take in is the other case, and it is stored as a coverage of its own
rather than as an absence:

| Coverage | What it means |
|---|---|
| **`WholeThread`** | The derivation was shown every message of the conversation this caller may see. |
| **`ThreadTooLarge`** | The conversation is longer than one derivation may take in, so nothing was derived from any part of it. |

Summarizing the part that fits and saying nothing about the rest is the third option, and it is refused: a statement
drawn from a third of an exchange reads exactly like one drawn from all of it. `ThreadTooLarge` is a settled record
carrying no statements, so the conversation leaves the queue — nothing about its length stops being true on the next
run, and withholding it would put the same conversation at the front of every pass forever.

## When it runs, and what puts a conversation back

The derivation is a step of the account's synchronization run, behind the arrival pipeline, for the reason every other
pass is: that run already has per-account isolation, a slot count, a jittered backoff, and a failure path that defers
the account rather than the process.

One pass derives for at most **four conversations**, reading at most **forty messages** of each and at most **4 000
characters** of every message. All three are constants rather than settings, because they bound the pass's latency
rather than describing a deployment — a derivation is a provider call, and every other account is waiting behind the
one holding the slot.

**A state does not go stale silently.** Every record stores the shape of the conversation it was derived from: how many
messages the derivation was shown, and when the most recent of them arrived. A conversation whose shape no longer
matches is owed a derivation again and re-enters the queue; writing the new state takes it out. Both halves are needed —
a count alone misses a message deleted and another received, and an instant alone misses a message received out of
order. One membership change escapes the pair, a message removed and another carrying exactly the same arrival instant
added between two runs, and that is accepted rather than solved: closing it would cost a digest over every message of
every conversation on every pass.

That is also the whole of how an existing mailbox is filled in. A deployment that turns the derivation on drains its
stored correspondence over successive runs, four conversations at a time, and each run reports how far it got and
whether more remains. There is no sweep of its own, because there is no mail the account run does not reach.

## What is written down and what is not

A **settled** derivation is written down and takes the conversation out of the queue until its shape changes — including
a settled answer of no statements, and including `ThreadTooLarge`. A **withheld** one is not written down, and it ends
the pass rather than moving to the next conversation, because every reason a derivation is withheld outlives one
conversation:

| Withheld because | What it means |
|---|---|
| **Not activated** | The deployment has not turned the derivation on. The pass answers before it looks for work, so nothing is queried, nothing is read, and nothing is sent. |
| **Allowance exhausted** | The answering period has no admission left for another call. |
| **Provider unavailable** | The endpoint failed, timed out, was unreachable, or its credential would not resolve. |

A spent period today is an ordinary period tomorrow, and the conversation is still waiting. A provider that *answered*
with something unreadable settles rather than withholds: the call was made and paid for, and asking again buys the same
answer.

## The agent, and what leaves the deployment

The derivation runs as an agent composed the way every agent in this product is: one instruction carried as the agent's
own, one declared tool set, one name, and the registered instruction envelope wrapped around it. It opens no chat call
of its own.

**The tool set is empty, and that is the capability rather than a description of one.** The conversation is put to the
agent in the turn, so an agent that could look something up would be an agent able to reach mail beyond the
correspondence it was asked about.

What leaves the deployment is the subject, each message's author, arrival instant and text — nothing else, and every
one of them scanned first by whatever [sensitive-content scanning](sensitive-content-scanning.md) the deployment
switched on. The messages are **numbered in the turn and cited by number**, and the model is shown no identifier at
all, so a citation is a position in a list this deployment composed: an answer naming a message it was not given names
nothing, and its statement is dropped.

The correspondence is data rather than an instruction, and the instruction says so. Mail is the most adversarial text
this system reads: a message that asks to be recorded as an agreement is a sender writing on a block somebody else's
decisions depend on. The instruction describes no action either — replying, filing and reminding are acts this
deployment takes elsewhere, so a model cannot propose one.

## What it costs, and turning it on

The derivation is off unless a deployment turns it on:

```json
{
  "Chat": {
    "ThreadState": {
      "Enabled": true
    }
  }
}
```

It needs a declared chat endpoint, exactly as [mail answering](mail-answering.md) and
[message enrichment](message-enrichment.md) do, and it is admitted against and charged to **the same period ceilings a
question is**. That is what makes it bounded rather than a third, unmetered way of spending a deployment's allowance —
and it is the trade an operator makes by turning it on, because a derivation, an enrichment and a question compete for
one allowance. A refused admission withholds the derivation rather than failing it.

Every call runs behind the same provider bulkhead every other outbound AI call does: the endpoint's own circuit,
concurrency limiter, timeout, and health record. Each derivation opens its own credential, transport, chat client, and
agent, and releases all four with it, so a rotated key is picked up by the next conversation rather than at the next
restart.

## Privacy

A statement is a sentence derived from somebody's mail and inherits its classification whole.

- **Nothing about a statement reaches a log or a span.** What a derivation reports about itself is the endpoint that
  answered, a count of conversations, a count of statements, and the reason a derivation was withheld. Never the text,
  never who owes a commitment, never the subject.
- **The text and the owner are scanned on the way out**, at the client point as well as at the prompt point, so a
  statement quoting a credential a message carried is redacted before it reaches a screen.
- **Deleting the conversation's mail deletes its state.** The record hangs off the surviving thread with a cascading
  delete, so an erasure removes the block with the correspondence rather than leaving it behind.
- **A source is published as a message identifier rather than as text**, because following one is a request of its own.
- **It publishes no participants.** Who is taking part is something the correspondence states exactly — the authors of
  its messages — and the conversation route already publishes it from the mail itself. Asking a model for it would
  store a second, worse copy of a fact the mailbox holds.

## What a client reads

`GET /api/client/threads/{threadId}/state` answers with the block: the conversation, the coverage, when it was derived,
whether it is current, and the statements with their sources.

**A stale state is never published as current.** Between a reply arriving and the next pass reaching the conversation
— which is indefinite while the answering allowance is spent — the stored record describes a conversation that no
longer exists, and its newest message may have withdrawn exactly what the block says. So `current` is `false` whenever
the conversation's shape no longer matches the one the state was derived from, and it is decided by the same selection
and comparison the pass uses to find what is owed rather than by a second one: a read calls a state current exactly
when the next pass would leave the conversation alone. A client draws no statement of a state that is not current, in
any composition, and says in the block that the conversation has changed since.

**Absence is a `404`, and it is a state rather than a failure.** A deployment that never turned the derivation on, one
that has not reached this conversation yet, and a conversation this caller does not hold all answer the same way, and a
client draws a conversation nothing has been derived about rather than an error nobody can act on. Telling those apart
is not something a client is owed: each of them is *there is no block here*, and the deployment's own operator knows
which.

**A machine with no network says so in the block itself.** The read is not attempted while the client is offline, and
the conversation it sits above may already be on the screen — so the block draws the offline sentence rather than
looking like a read still in flight. It reads once the network comes back.

## What is not here

- **Acting on a commitment.** Nothing reminds, schedules, files, or replies. A commitment is a reading of the
  conversation, not a task this deployment took on.
- **Drafting a reply.** The block says where a correspondence stands and stops there.
- **A deterministic producer.** Every statement today comes from the agent.
- **Re-deriving on a changed instruction.** A conversation is re-derived when its shape changes and at no other time,
  including after the instruction or the model behind it moves.
