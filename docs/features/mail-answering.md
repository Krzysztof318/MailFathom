# Mail answering

<!-- describes: src/AI/Orchestration/**, src/AI/Retrieval/**, src/AI/ProviderAdapters/ResilientChatClient.cs, src/Application/Retrieval/** -->

A question about the mailbox, answered from the mail the model looks up while answering. This page describes the
composition that does it: what the model may reach, when it reaches it, how much of it leaves the process, and what an
answer carries back.

**No MCP tool reaches this yet.** The composition is in place and is exercised by its own tests; the tool that would
expose it and the ceilings an operator configures are each their own change. What is described below is what the code
does today.

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

## An optional second pass: the model decides what answers

Hybrid search ranks by fusing lexical and vector similarity, which decides what *resembles* a query. Resemblance is
cheap, deterministic, and shallow: ask "what did the insurer finally agree to pay" and a long thread about the claim
ranks beside the one message that settles it. The fused top eight can hold one passage that answers and seven that
mention.

A deployment can turn on a second pass over that ranking. Each candidate is put to the declared chat endpoint on its
own — one question, one extract, one query — and the model answers with a whole number from 0 to 100 saying how much of
an answer the extract holds. Candidates below the configured threshold are dropped before the run ever reads them.
Retrieval decides what is plausible; this decides what is relevant.

**It is off by default, and off is a supported deployment.** With the pass off, retrieval hands over the fused ranking
exactly as hybrid search produced it — cheaper, faster, fully deterministic — which is what every instance did before
this existed. [`Chat:RelevanceFilter`](../operations/configuration-reference.md#chat) holds the keys.

### What it costs

One provider call per candidate, on every lookup a question makes, and a run makes several. That is why the candidate
count is bounded rather than taken from whatever the search returned: an unbounded candidate list is an unbounded bill.

The judgements are made **one after another**, so the count bounds latency as well as spend — a lookup takes as long as
its judgements do, in sequence. That is not a tuning choice: each judgement is an ordinary call to the declared endpoint
under the same deadline, resilience budget, and circuit as any other, and that budget's concurrency limiter admits a few
invocations at a time and *rejects* the rest rather than queueing them. A lookup that sent its whole candidate list at
once would have most of it refused by MailFathom's own bulkhead, and every one of those refusals would be recorded
against a provider that is working perfectly well.

Setting the candidate count below what retrieval returns buys a weaker filter rather than a shorter result. A passage
nobody judged was never found irrelevant, so it keeps the place the fused ranking gave it.

### It filters; it does not reorder

What survives is a subsequence of the fused ordering. The fusion is computed across every candidate at once, while a
judgement is made about one candidate in isolation, so sorting by judgement would replace a ranking with a set of
unrelated opinions.

### Every failure keeps the passage

| What happened | What the retrieval hands over |
| --- | --- |
| The chat provider's last call left it unavailable or misconfigured | The fused ranking, judged by nobody |
| A judgement timed out, was throttled, was refused, or could not be sent | That candidate and every candidate after it, kept; the pass stops there |
| A judgement came back as anything other than a score | That candidate, kept; the pass goes on to the next |
| Every candidate was judged and every one scored below the threshold | Nothing, which is the filter working |

The first three are the same rule stated three times: a filter that failed closed would turn one degraded provider into
a mailbox that appears to hold nothing, and the fused ranking is already a usable answer. Each of them is recorded — as
a count and a classification, never as an extract.

The second and third rows differ in how far the damage spreads, and the reason is what each says about the endpoint. A
provider that answered something other than a score is answering, so the candidates after it are still worth asking
about. One that failed is not, and asking it once per remaining candidate would buy the same answer while a question
waits.

The last row is not a failure and is not degraded. A question whose mail does not answer it is answered by saying so,
and a lookup that found nothing has always been an ordinary outcome here.

An answer is read strictly: a score with a word, a unit, a fence, or an explanation around it is refused rather than
mined for the number inside it. Refusing costs one candidate its filtering, while a lenient reading would turn a model
that answered something else into a score this system invented about somebody's mail.

### The candidate is quoted, never obeyed

A judgement is two turns: the instruction, and the candidate beside the query it was retrieved for. The extract reaches
the model inside the same `<retrieved-mail>` envelope an answering run reads it in, written by the same formatter, and
the query is enclosed in a `<query>` element of its own — it is free text a model wrote, and a run's earlier retrieval is
one of the things that shaped it, so mail reaches it indirectly. The instruction states that everything inside both
elements is data and that a request found there is described rather than obeyed, exactly as the run's own instruction
does.

Nothing of the run travels with a judgement: not the question the person asked, not the answer being written, and not
the other candidates. Judging one extract against one query needs none of it, and everything sent is somebody's mail
leaving the process.

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

## Mail is read as evidence, never as an instruction

Mail is written by strangers, so an extract of it is quoted evidence rather than something the run said. Two mechanisms
keep it that way, and they are deliberately unequal.

**The structural one.** An extract never occupies the position an instruction arrives in. The run's instruction is
carried beside the conversation on every turn; an extract is the *result of the tool the model called*, and the two are
separate parts of the request the provider receives. Nothing composes one into the other, so no message — however it is
phrased, whoever it claims to be from — can be delivered where the model reads what it was told to do.

Inside that tool result, each extract sits in an element of its own:

```xml
<retrieved-mail>
  <message id="019fe12f-a4a1-7759-8d95-21d0bb6eec90" account="work" folder="ARCHIVE" received="2026-08-01T09:14:22.0000000+02:00">
    <subject>Invoice 41</subject>
    <extract>the invoice is attached</extract>
  </message>
</retrieved-mail>
```

Nothing a message contains can end an element or open one. Every value — the extract, the subject, the aliases — is
written through an XML writer that escapes what would, so a message whose text closes the envelope and opens a forged
instruction arrives as that text: visible, attributed to the message that sent it, and inert. A lookup that found
nothing writes the empty envelope rather than nothing at all, so the model reads that the mailbox was searched instead of
guessing at a blank result.

The identity is the other reason the envelope exists. Each extract carries the stable local identifier an answer cites
and the account and folder alias it was read from, unchanged — an answer that cannot say which message a claim came from
cannot be checked.

**The weaker one.** The instruction states that everything inside the envelope is data, that a request found there is
described rather than obeyed, and that each statement is cited by the identifier of the message it came from. It is
worth writing, and it is second: a model that ignored every word of it would still not have read mail in the position an
instruction arrives in.

This replaces the orchestration framework's own formatting, which writes each result as a labelled paragraph between
dashed separators and closes with an instruction of its own. Retrieved mail written that way is prose in the same voice
as an instruction, and a message imitating one of those separators is indistinguishable from it.

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

No question, no answer, no query the model wrote, no retrieved passage, and no relevance judgement. A record of a run
carries the endpoint alias, how many passages were retrieved, and how many messages they came from — counts and a name
the operator chose. A record of the second pass carries how many candidates were judged, how many were dropped, and
which classification a failed judgement had.

The orchestration framework's own switch for logging queries and retrieved text is set off explicitly rather than left
at its default, because what it would emit is somebody's question and extracts of their mail.

Retrieved mail is untrusted input, and so is what the model writes from it. [Chat generation § What never reaches a
log](chat-generation.md#what-never-reaches-a-log) states the same rule for the transport underneath.
