# Mail answering

<!-- describes: src/AI/Orchestration/**, src/AI/Retrieval/**, src/AI/ProviderAdapters/ResilientChatClient.cs, src/Application/Retrieval/** -->

A question about the mailbox, answered from the mail the model looks up while answering. This page describes the
composition that does it: what the model may reach, when it reaches it, how much of it leaves the process, and what an
answer carries back.

**No MCP tool reaches this yet.** The composition is in place and is exercised by its own tests; the tool that would
expose it, the formatter that separates retrieved mail from instructions, and the ceilings an operator configures are
each their own change. What is described below is what the code does today.

## The model asks for mail; nothing is pushed at it

Retrieval runs **on demand**. The agent is composed with one tool, `search_mail`, and the model calls it when it decides
it needs context. Nothing is retrieved before the first call, and a question that needs no mail — "what can you do" —
costs one provider call and reads no mailbox at all.

The alternative would be to search before every call and inject the results. That answers a greeting by dragging a
mailbox through a provider, and it makes every question cost the same as the most expensive one.

## The scope is bound before the model sees anything

A question carries the accounts and folders it may be answered from. That scope is bound into the run when it is
composed, and the tool the model is offered takes **a query and nothing else** — there is no argument, no instruction,
and no retrieved message that can widen it.

This is the property worth stating plainly, because it is what makes the boundary structural rather than a request in a
prompt: a model that writes `everything in the secondary account` as its query gets the caller's scope searched for
those words. The scope is not part of the conversation, so nothing in the conversation can move it.

Two further narrowings apply underneath, and neither can widen anything:

- Retrieval goes through the same mailbox search a caller reaches with `search_emails`, which restricts every query to
  the accounts the deployment actually serves. Answering a question therefore cannot see mail that searching for it
  would not. [Email search](email-search.md) describes that ranking and its filters.
- An account or folder named in the scope that the deployment does not serve simply matches nothing.

## What one retrieval may hand over

Two bounds, applied where the passages are built rather than where they are sent:

| Bound | Default | What it controls |
| --- | --- | --- |
| Passages per retrieval | 8 | How many messages one lookup can draw on |
| Characters per passage | 1200 | How much of any single message it can draw out |

They are two numbers rather than one total on purpose. A single total would let one enormous extract satisfy the same
ceiling as a spread across several messages, and the two say different things about a mailbox: the count is how far a
question reaches, the size is how deeply.

The passage count is capped by what one search can rank, so a bound beyond that is refused rather than accepted and
never met. Neither is configurable today; both are fixed in code.

A query with no usable text finds nothing rather than failing. The text is written by a model rather than by a caller
who could be told to correct it, and a run whose lookup found nothing still has an answer to give.

## What a passage carries

Each passage is a bounded extract plus the identity an answer is traced through:

- the stable local message identifier, which is the same one every other read names an email by;
- the account and the folder alias it was read from;
- the subject and the received time, where the message carried them;
- the extract itself, already cut to the bound above.

Nothing else travels. The participants, the size, the flags, and the attachment summary that a listing publishes are
dropped before a provider is reached, because an answer does not need them.

A message whose body yielded no text — encrypted mail, or mail whose content lives entirely in an attachment — can match
on its subject or its participants and still produce no extract. Such a match is dropped rather than sent as an
identifier with nothing beside it.

## What an answer carries back

The answer text, and the passages the run retrieved. The passages travel with it because they are what make it
checkable: each names a message that can then be fetched whole.

They are what the run *retrieved*, not what the model demonstrably used. Nothing outside the model knows which of them
it drew on, so claiming the narrower set would state something this system cannot observe.

An answer with no text at all is a failure rather than an empty answer, classified the same way any other empty
generation is. [Chat generation § What a failing call is classified
as](chat-generation.md#what-a-failing-call-is-classified-as) holds the table.

## A run is several calls, and each carries the bounds of one

A run is a conversation with the provider: the model asks for mail, the tool answers, the model writes the answer. Every
one of those calls carries the deployment's declared generation parameters, its request deadline, its resilience budget,
and its health reporting — the same bounds a single chat request carries, applied per call rather than per run.

The one thing this shape gives up is stated rather than hidden: the transport underneath is opened once for the run
instead of once per attempt, so a retried call reuses the handler chain the run began with. A run is short, and an
endpoint that moves mid-run is reached at its new address by the next run.

The question itself is bounded before anything is sent, by the same conversation bound a single call carries: a question
larger than one call may send is refused rather than truncated.

## The run cannot change anything

The agent is composed with one tool and that tool searches. There is nothing that sends, deletes, moves, or marks mail
as read, so a question is never a mutating act — a property of what the agent is made of rather than a rule written
beside it. Retrieval reaches no mail server either: it answers from what synchronization has already stored.

## What never reaches a log

No question, no answer, no query the model wrote, and no retrieved passage. A record of a run carries the endpoint
alias, how many passages were retrieved, and how many messages they came from — counts and a name the operator chose.

The orchestration framework's own switch for logging queries and retrieved text is set off explicitly rather than left
at its default, because what it would emit is somebody's question and extracts of their mail.

Retrieved mail is untrusted input, and so is what the model writes from it. [Chat generation § What never reaches a
log](chat-generation.md#what-never-reaches-a-log) states the same rule for the transport underneath.
