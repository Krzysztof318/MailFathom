# Mail answering

<!-- describes: backend/src/AI/Orchestration/**, backend/src/AI/Retrieval/**, backend/src/AI/ProviderAdapters/ResilientChatClient.cs, backend/src/Application/Retrieval/**, backend/src/Domain/Answering/**, backend/src/Infrastructure/Persistence/Answering/**, backend/src/Host/Configuration/Mail/MailAnsweringAuditTrailOptions.cs, backend/src/Host/Configuration/Access/TransportRequestTimeoutOptions.cs -->

A question about the mailbox, answered from the mail the model looks up while answering. This page describes the
composition that does it: what the model may reach, when it reaches it, how much of it leaves the process, and what an
answer carries back.

The `ask_mail` MCP tool is how a caller reaches it, and [MCP tools § `ask_mail`](mcp-tools.md#ask_mail) documents that
surface — the arguments, the citations, and when the tool is advertised at all. What one question may spend, and how
much of a mailbox may leave the process to answer it, is [§ What one question may spend](#what-one-question-may-spend)
below; [`MailAnswering`](../operations/configuration-ai.md#mailanswering) holds the keys.

## Every AI operation is one agent, composed one way

`ask_mail` is a Microsoft Agent Framework agent built over a provider-neutral chat client, and every other thing this
product does with a model is built the same way, by the same composition. An operation supplies three things and no
others: the name a run reports itself as, its own instruction, and its tool set. Everything else — where the instruction
is carried, the generation parameters each turn runs with, the instruction envelope described below — is decided once,
for all of them.

One composition rather than a chat call written beside each feature, because what makes an operation safe is a property
of that shape rather than of its prose. An instruction cannot be reached from a tool result because of where each is
placed; the tool set **is** the capability, so an operation that only reads composes no mutating tool; every call goes
through the client the run wrapped, which is where its spend is counted; and what a run reports about cost,
cancellation, and the model is what [ADR 0022](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md)
says. A feature composing its own call would re-decide all four in silence, once per feature.

### The instruction envelope, empty in this build

What a composed agent sends as its instruction is three parts: a preamble, the operation's own instruction, and a
postamble. Both outer parts come from one implementation resolved through dependency injection and asked once per
composition, so an answer that varies per person or per request changes what a run sends without any operation changing.

**This build ships the seam and an implementation that returns nothing**, so the composed instruction is the operation's
own text byte for byte and no run pays for a wrapper nobody has written yet. What it buys is that adding the language a
person reads, a deployment's own wording, or anything else true of every operation is one implementation of one
interface rather than an edit to every instruction in the tree.

There is no template language, no substitution syntax, no prompt store, and no configuration key. Whatever the
implementation returns is text, and the composition puts one part before the operation's instruction and the other
after it, with no separator of its own.

**Neither half may carry mail content, an address, or anything else personal.** The instruction envelope is composed
into the instruction, which is the one position [§ Mail is read as evidence, never as an instruction](#mail-is-read-as-evidence-never-as-an-instruction)
keeps mail out of, so an implementation filling it from a mailbox would undo that separation. What belongs there is what
the deployment or the person chose to say about how they are addressed. Whatever it adds rides inside the same
instruction every turn carries, so it is sent through the same client the run's spend is counted on and is inside what a
run reports as spent rather than outside it — and it reaches no log and no telemetry event, for the reason
[§ What never reaches a log](#what-never-reaches-a-log) gives for everything else on this path.

## The model asks for mail; nothing is pushed at it

Retrieval runs **on demand**. The agent is composed with one tool, `search_mail`, and the model calls it when it decides
it needs context. Nothing is retrieved before the first call, and a question that needs no mail — "what can you do" —
costs one provider call and reads no mailbox at all.

The alternative would be to search before every call and inject the results. That answers a greeting by dragging a
mailbox through a provider, and it makes every question cost the same as the most expensive one.

**This is what the declared model has to be able to do.** A model that cannot be given function tools cannot answer a
question here, whatever else is configured, and a reasoning model that refuses tools beside a stated reasoning effort
has to be reached through the other of the provider's two APIs. Both are settings on the endpoint: [Chat generation §
Two APIs, and the deployment says which](chat-generation.md#two-apis-and-the-deployment-says-which) holds the choice and
what a wrong one is reported as.

## What one lookup may ask for

The tool publishes the query and the narrowing `search_emails` publishes to its own callers, and nothing else:

| Argument | What it does |
| --- | --- |
| `queryText` | The text the eligible mail is ranked against. Required |
| `senderAddress` | Only mail sent from this address, matched whole and without regard to case |
| `recipientAddress` | Only mail whose `To` or `Cc` header names this address |
| `subjectFragment` | Only mail whose subject contains this text, which narrows before anything is ranked |
| `receivedOnOrAfter`, `receivedBefore` | Only mail received in the range, the start inclusive and the end exclusive |
| `isRemotelySeen` | Only mail the server last reported as read, or as unread |
| `isRemotelyFlagged` | Only mail the server last reported as flagged, or as unflagged, which is the star a mail client shows |
| `keyword` | Only mail carrying this keyword, matched whole and without regard to case |
| `hasAttachments` | Only mail that carries attachments, or that carries none |

The filters are the greater part of what makes a question reach the mail a search would. A question that is naturally a
narrowing — one person's mail, one week, mail carrying an attachment — is the one shape lexical and vector similarity
are both weakest at, so expressing it as words in a query means competing with every other word in that query. Expressed
as a filter it selects the mail exactly, and the ranking is then left to do the part it is good at.

The arguments a caller of `search_emails` holds and a model does not are deliberately withheld, each for its own reason:

- **The accounts and folders**, because they are the caller's authorization rather than a search preference. A model
  writes queries and never its own boundary.
- **Whether junk mail is read**, because that is settled when the run resolves the scope above, and a run resolves it
  excluded. Answering is the path the exclusion exists for: mail written to manipulate whoever reads it now has a model
  reading it, and a caller hunting a wrongly filed message reaches for the listing or the search that can ask for it.
- **The result count**, because it is the deployment's bound on how much mail one lookup draws out. The one party with
  an incentive to ask for more mail must not be able to ask for more mail.

A filter this system would refuse from a caller is refused from a model, in the same words and under the same error
code, because both reach the same use case. What differs is what happens next: the lookup comes back as a
`<search-refused>` element naming the argument, and the model corrects it and calls again. Nothing was searched, which
is exactly what an empty envelope would have failed to say.

## A question in one language, mail in another

A mailbox holding mail in several languages is the ordinary case, and both halves of a question meet it. Both are
settled by the instruction the run carries, because both are properties of how a run is worded rather than of the
retrieval underneath it, and neither is anything an operator configures.

**A lookup is worded in the language the mail is likely to carry**, which need not be the language the question was
asked in, and one that returns nothing useful is tried again in another language the mailbox plausibly holds before the
run concludes that the mailbox does not answer. The extra lookups are ordinary lookups, bounded by
[§ What one question may spend](#what-one-question-may-spend) like every other.

It cannot be left to retrieval, because the lexical half of retrieval matches a word against a word. The index is built
with one PostgreSQL text search configuration for the whole deployment —
[`Persistence:TextSearchConfiguration`](../operations/configuration-runtime.md#persistence-and-the-connection-string),
`simple` by default, which neither stems a word nor drops a stop word — so it stems for one language at most, and a
lookup worded in the language of the question reaches mail written in that language and in no other. Whether the vector
half bridges the gap depends on the declared embedding model, which a deployment chose for other reasons. A run that
worded every lookup in the question's language would therefore report a mailbox that plainly holds the answer as one
that does not. [Email search](email-search.md) documents the ranking itself.

**An answer is written in the language the question was asked in**, whatever language the mail it rests on was written
in. What the answer quotes from mail — a subject, a name, the phrase a claim rests on — keeps its own wording, with a
rendering into the question's language beside it where the claim turns on what those words mean. That is what keeps a
citation checkable: the quoted words have to be the words the cited message carried for anybody to be able to look them
up in it.

The second retrieval pass, where a deployment turned it on, is told the same thing from the other side: an extract in a
language other than the lookup's is not less relevant for being in it, so the filter does not drop what the lookup was
worded to find.

## The scope is bound before the model sees anything

A question carries the accounts and folders it may be answered from. That scope is bound into the run when it is
composed, and the tool the model is offered **takes no account and no folder argument at all** — there is no argument,
no instruction, and no retrieved message that can widen it.

This is the property worth stating plainly, because it is what makes the boundary structural rather than a request in a
prompt: a model that writes `everything in the secondary account` as its query gets the caller's scope searched for
those words. The scope is not part of the conversation, so nothing in the conversation can move it.

Two further narrowings apply underneath, and neither can widen anything:

- Retrieval goes through the same mailbox search a caller reaches with `search_emails`, which restricts every query to
  the accounts the deployment actually serves. Answering a question therefore cannot see mail that searching for it
  would not. [Email search](email-search.md) describes that ranking and its filters.
- A folder alias named in the scope that this deployment holds no mail under simply matches nothing. An account it does
  not serve is refused instead, and so is a folder named by a [role](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found)
  no account in scope maps: both are names the caller could not have meant, while an alias that matches nothing is a
  folder that is simply empty here.
- The account's junk folder is outside retrieval, and unlike `list_emails` and `search_emails` there is **no override**.
  An answer here is composed by a model from the mail it retrieved, so content written to deceive a reader would arrive
  as ordinary correspondence with nothing left to notice it. Naming `role:Junk` in the scope does not reach it either.
  [Spam classification](spam-classification.md#the-junk-folder-is-left-out-of-listing-and-search) records where that
  folder comes from.

## What one retrieval may hand over

Two bounds, applied where the passages are built rather than where they are sent:

| Bound | Key | Default | What it controls |
| --- | --- | --- | --- |
| Passages per retrieval | `MailAnswering:MaxPassagesPerRetrieval` | 20 | How many messages one lookup can draw on |
| Characters per passage | `MailAnswering:MaxCharactersPerPassage` | 1200 | How much of any single message it can draw out |

They are two numbers rather than one total on purpose. A single total would let one enormous extract satisfy the same
ceiling as a spread across several messages, and the two say different things about a mailbox: the count is how far a
question reaches, the size is how deeply.

The count is the window `search_emails` itself returns, and matching it is deliberate: a run that reached fewer messages
per lookup than one search window holds answers worse than the search it was meant to spare the caller, and it does so
on exactly the questions a search already handles. The count is capped by what one search can rank, so a bound beyond
that is refused rather than accepted and never met.

Neither of them bounds a *run*, and that is the point of the section below: a model decides how many lookups to make, so
two bounds on one lookup say nothing about how much of a mailbox one question can reach. The run's own ceiling is what
does, and it is the pair of them together that decides how many lookups a run fits — a lookup whose every passage
reached the per-passage ceiling would exhaust a run on its own. That is the ceiling working rather than a contradiction:
the passages are admitted in relevance order and the run is told there is no more. The per-passage figure is a ceiling
rather than a size, and a passage is a few bounded extracts of one message.

A query with no usable text, and a filter this deployment would refuse from any caller, both come back as a refusal
naming the argument rather than as an empty result. The caller here is a tool loop, so a model that wrote an unusable
value can be told which one and write another; absorbing the refusal into an empty envelope would tell it the mailbox
holds nothing when what it holds is a bad filter. A lookup that ran and matched nothing is an ordinary empty result, and
a run whose lookups found nothing still has an answer to give.

## What one question may spend

A run is a conversation rather than a call, and its length is the model's decision: it asks for mail, reads what comes
back, and may ask again. Everything above bounds one lookup and the chat declaration bounds one request, so neither of
them says anything about a run that makes twenty lookups. Three ceilings do, and each is checked **before** the next
provider call rather than reported after the run.

| Ceiling | Key | Default | What happens when it is reached |
| --- | --- | --- | --- |
| Retrieved characters per run | `MailAnswering:MaxRetrievedCharactersPerRun` | 20 000 | The lookup is cut and the run continues, told there is no more mail |
| Provider calls per run | `MailAnswering:MaxProviderCallsPerRun` | 8 | The run stops with `57001` |
| Tokens per run | `MailAnswering:MaxTokensPerRun` | 80 000 | The run stops with `57001` |

Three numbers because they fail in three different ways. The retrieved-character ceiling is the privacy one — the total
amount of somebody's mail that may leave the process to answer one question, whatever the model asks for. The token
ceiling is the cost one, and the only one stated in the unit a provider bills by. The call ceiling is the one that
always works: a token count is what the provider *reported*, and an endpoint that reports none would leave the cost
ceiling unreachable while a tool loop went round.

A token ceiling can only be checked against what earlier calls reported, so the call that crosses it is paid for. That
is inherent rather than an oversight — what a call will cost is not knowable until the provider has answered, and the
alternative is refusing calls on an estimate.

**A fourth bound sits outside all three, and it is not a spend ceiling.** The MCP request carrying the question runs
under [`McpEndpoint:RequestTimeout`](../operations/mcp-endpoint.md#request-timeouts), ten minutes by default, and it is
wall clock rather than budget: it is not checked before the next provider call, it abandons the run wherever it has got
to, and the caller receives a `504` instead of `57001`. A run that spent its whole call budget on slow answers can reach
it — eight invocations at the `AiProviderInvocation` class's five-minute total timeout is forty minutes — so the
transport ceiling, not `MaxProviderCallsPerRun`, is what a long question actually stops on first.

That is the deliberate trade rather than an oversight, because a ceiling sized for forty minutes would let one stalled
run hold an MCP concurrency permit for that long. What follows from it is one rule worth carrying: **raising
`MaxProviderCallsPerRun` to let long questions finish means raising `McpEndpoint:RequestTimeout:Duration` with it**, or
the extra calls are bought and then abandoned unanswered.

### The retrieval ceiling cuts; the other two stop the run

A run that may retrieve no more still has an answerable question: it has mail already, and the model is told there is
none left. Each lookup is trimmed to what the run may still send — **whole passages**, in the order retrieval ranked
them, because an extract cut again to fill the remaining allowance exactly would end mid-word for the sake of a few
hundred characters, and skipping ahead to a shorter passage would silently prefer short messages to relevant ones.

Once nothing may be sent, the envelope says so rather than arriving as a mailbox that suddenly holds nothing:

```xml
<retrieved-mail retrieval-mode="hybrid" retrieval-limit-reached="true" />
```

That attribute is what separates a mailbox with no answer in it from a run with no allowance left to read one — the two
produce the same short envelope, and only the second means asking again buys nothing. The instruction tells the model
what it means: answer from what you have, and say the mailbox was not read in full.

The response says the same thing to the caller, as `retrievalTruncated`. A cut nobody reports is one the reader cannot
allow for: an answer written after the run stopped being given mail is a real answer to a *narrower* reading of the
mailbox, and only saying so keeps the two distinguishable.

A run with no allowance for another call is different, and is a failure rather than a cut: the model was stopped before
it wrote anything, so there is no answer to trim down to. Nothing is published, and `57001` says the run reached what
one question may spend.

### And a ceiling over every run of a period

A per-run ceiling makes one question cost a knowable amount, and nothing about the MCP surface stops a client from
asking a hundred of them in a minute — so without an aggregate, an instance's provider bill is a function of how
enthusiastic its client is rather than of what its operator agreed to.

| Ceiling | Key | Default |
| --- | --- | --- |
| Runs per period | `MailAnswering:MaxRunsPerPeriod` | 30 |
| Tokens per period | `MailAnswering:MaxTokensPerPeriod` | 300 000 |
| The period itself | `MailAnswering:AggregatePeriod` | 1 hour |

Two ceilings for the reason a run has three: the run count always works, and the token count is stated in what a
provider bills. The allowance is taken when a question is **admitted** rather than when it finishes, so a run still in
flight already occupies its place — counting them afterwards would admit every concurrent question, which is precisely
the burst the ceiling exists to bound. It is taken after the capability is read and before the run begins, which is the
last point at which nothing has been spent: a question a deployment was never going to answer is not charged against
what it spends.

A question over the ceiling is refused with `57001` and never held until the period turns over, because holding it would
convert a spend ceiling into a queue of requests occupying the endpoint that serves the rest of the surface. The two
refusals carry one code and differ in the message, because only one of them becomes answerable by waiting: a spent
period turns over, while a run that grew past what one question may cost reaches the same ceiling by the same route if
it is asked again.

The window is **fixed and anchored at the Unix epoch**, which is where
[embedding spend](embedding-generation.md#what-an-instance-is-willing-to-spend) places its own period too: an hour-long
period begins on the hour, so a refused caller
has a roll-over instant to come back at and every restart of the process agrees on where a period begins without
anything being stored to say so. An instance that answered nothing all day is not owed the windows it skipped. What a
fixed window costs is stated rather than hidden — a client that spends the whole allowance at the end of one window and
again at the start of the next has spent twice the ceiling across an interval of the same length.

The ledger is **process-local and not durable**, and that is where it differs from the embedding one, which keeps its
count in a table so a crash-restart loop cannot begin every period again from zero. The reasoning applies here in kind
and not in degree: an embedding sweep charges inside a transaction that was committing vectors anyway, while a question
opens no write of its own, so a durable count would add a database write to the path of every provider call in every
run. A restart therefore begins the current window with nothing spent.

### What it is observable as

Consumed budget is published as counts and nothing else, so an operator watches spend without any of the content it was
spent on. [Telemetry § Answering spend](../operations/telemetry.md#answering-spend) lists the instruments. What one
*run* did is a separate signal beside them, described in [§ What a run publishes as
telemetry](#what-a-run-publishes-as-telemetry) below; nothing there republishes consumed budget.

## An optional second pass: the model decides what answers

Hybrid search ranks by fusing lexical and vector similarity, which decides what *resembles* a query. Resemblance is
cheap, deterministic, and shallow: ask "what did the insurer finally agree to pay" and a long thread about the claim
ranks beside the one message that settles it. A fused window can hold one passage that answers and nineteen that
mention.

A deployment can turn on a second pass over that ranking. Each candidate is put to the declared chat endpoint on its
own — one question, one extract, one query — and the model answers with a whole number from 0 to 100 saying how much of
an answer the extract holds. Candidates below the configured threshold are dropped before the run ever reads them.
Retrieval decides what is plausible; this decides what is relevant.

**It is off by default, and off is a supported deployment.** With the pass off, retrieval hands over the fused ranking
exactly as hybrid search produced it — cheaper, faster, fully deterministic — which is what every instance did before
this existed. [`Chat:RelevanceFilter`](../operations/configuration-ai.md#chat) holds the keys.

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

Above that number there is nothing to buy. The count is capped at the passages-per-retrieval bound in the table above,
because a candidate past the last passage never exists to be judged, and a value beyond it is refused at startup rather
than accepted as a widening that could not happen. The default is that same number: judge everything the lookup handed
over.

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

A judgement is two turns: the instruction, and the candidate beside the lookup it was retrieved for. The extract reaches
the model inside the same `<retrieved-mail>` envelope an answering run reads it in, written by the same formatter, and
the lookup is enclosed in a `<query>` element of its own — every part of it is free text a model wrote, and a run's
earlier retrieval is one of the things that shaped it, so mail reaches it indirectly. The instruction states that
everything inside both elements is data and that a request found there is described rather than obeyed, exactly as the
run's own instruction does.

The **whole** lookup travels, filters included, and that is what keeps the pass from dropping the mail a narrow lookup
was written to find. A lookup that is mostly a narrowing leaves a query text of a word or two, and a candidate judged
against those words alone scores like an unremarkable message however exactly the filters selected it — so the
instruction says that the extract already satisfies every filter shown and asks how much of an answer it holds for the
lookup as a whole.

Nothing of the run travels with a judgement: not the question the person asked, not the answer being written, and not
the other candidates. Judging one extract against one query needs none of it, and everything sent is somebody's mail
leaving the process.

## What a passage carries

Each passage is a bounded extract plus the identity an answer is traced through:

- the stable local message identifier, which is the same one every other read names an email by;
- the account and the folder alias it was read from;
- the subject and the received time, where the message carried them;
- what was established about the author the message displays, and what this deployment made of them;
- how much the message's own text read as machine written;
- the extract itself, already cut to the bound above.

Nothing else travels. The participants, the size, the flags, and the attachment summary that a listing publishes are
dropped before a provider is reached, because an answer does not need them.

The sender verdict rides with the passage so a citation can state it without reading the message a second time, and a
provider never sees it: the envelope a passage is formatted into names the message, the account, the folder, the
received time, and the subject, and carries the extract. The verdict reaches the caller instead, on the citation. The
[machine-authorship reading](machine-authorship.md) travels on exactly those terms and is put to the same use — nothing
in the retrieval path selects, ranks, or drops a passage by it, and no model is shown it.

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
<retrieved-mail retrieval-mode="hybrid">
  <message id="019fe12f-a4a1-7759-8d95-21d0bb6eec90" account="work" folder="ARCHIVE" received="2026-08-01T09:14:22.0000000+02:00">
    <subject>Invoice 41</subject>
    <extract>the invoice is attached</extract>
  </message>
</retrieved-mail>
```

The `retrieval-mode` attribute says which ranking answered *this* lookup — `lexical` or `hybrid` — because that is a
fact about the instance at the moment of the call rather than about the build: an instance whose embedding provider is
refusing ranks lexically until it is not. It is what tells the model how a further query is worth wording, and it is
written here for the same reason `search_emails` publishes the mode in every response rather than in its description.

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

### What is actually tried, and what it settles

Everything above is a design until something attacks it, so a maintained set of adversarial messages does. It holds mail
written to give the model orders, mail written to close the envelope's own elements and open a forged instruction inside
it, mail written to talk the second pass into scoring it top, mail written to widen the accounts and folders a run may
read, and mail written to make an answer cite a message nobody retrieved. Each of them is put to the formatter, to a
whole run, to the relevance filter, and to the `ask_mail` request path. Every one of those runs is conducted against a
substituted chat model rather than a provider, so the set costs nothing to run and reaches no network — and the
substitute is scripted as a model that **did** what the message asked, because what is being tested is the system around
the model rather than the model's judgement.

Four things are settled that way, and they are properties of what a run can do:

- **Scope.** A question, a query the model wrote, and a retrieved message are all incapable of widening the accounts and
  folders an answer may be drawn from. That is checked in two independent places — where the request resolves the
  caller's filters, and where the run binds them — because either one alone would be a single point of failure for the
  only escalation here that reaches somebody else's mail.
- **Position.** What one request carries is this build's instruction, the question as it was asked, the model's own
  turns, and the envelope of what was retrieved. There is no fifth thing, so there is no position left for a message to
  have reached other than the one it is quoted in — which is asserted as an equality against those four rather than as
  the absence of the message's words, because the envelope escapes what a message wrote and searching for the raw text
  would report a message that arrived intact as one that never arrived at all.
- **Ranking.** A passage cannot promote itself. The second pass decides whether a candidate survives and never where it
  sits, so a message that begs to be scored top still holds the place the fused ranking gave it, and one judged below the
  threshold is dropped however loudly it objects.
- **Citations.** The messages a response names are read from what the run retrieved, never from what the model wrote, so
  an answer that names some other message names it in prose and in no citation.

What none of it settles is worth stating just as plainly: **a model can still be talked into saying something wrong.**
Nothing here inspects an answer's truth, and a sufficiently well-written message may well persuade a model to repeat its
claims, adopt its framing, or describe the mailbox inaccurately. The guarantee is about capability rather than about
eloquence — an answer that has been talked into a falsehood is a falsehood arriving as one message's content, attributed
to the message that carried it, citing only mail that exists, drawn from no wider a mailbox than the caller allowed. That
is what makes it checkable, which is the property this system can offer and truthfulness is not.

The set is evidence rather than proof, and it is evidence about the attacks that were understood when it was written. It
grows when a new one is: adding a message to it puts that message through every property above without a test being
edited.

## What an answer carries back

The answer text, and the passages the run retrieved. The passages travel with it because they are what make it
checkable: each names a message that can then be fetched whole.

They are what the run *retrieved*, not what the model demonstrably used. Nothing outside the model knows which of them
it drew on, so claiming the narrower set would state something this system cannot observe.

An answer with no text at all is a failure rather than an empty answer, classified the same way any other empty
generation is. [Chat generation § What a failing call is classified
as](chat-generation.md#what-a-failing-call-is-classified-as) holds the table.

Where a sensitive-content scanner is switched on, the answer and each citation's subject pass it before they are
published, and the extracts pass it on their way to the model — two egress points rather than one, because text sent to
a provider and text returned to a caller leave this deployment in different directions. The answer is guarded before
what one response carries is cut, so it is bounded after every placeholder is in it, and a scanner that cannot answer
refuses the question rather than serving a response nothing scanned.
[Sensitive-content scanning § the guarded egress
points](sensitive-content-scanning.md#the-guarded-egress-points) holds the contract, and it applies to nothing on a
deployment with both switches off, which is the default.

## What a caller may ask, and what one response publishes

The use case between a caller and the run owns three things the composition does not: what a question may be, whether
this deployment can answer one at all, and how much of what a run produced a single response carries.

A question is bounded before anything is sent — at most 1000 characters, not blank, and carrying no control character —
and it is refused rather than truncated, because a cut question is a different question and the answer to it would come
back looking like the answer to the one that was asked. That bound is the caller's; the deployment's declared ceiling on
what one conversation may carry is a separate and much larger number, and a question that fits the first cannot be what
exceeds the second.

What a response publishes is bounded too, and here the bounds cut rather than refuse:

| Bound | Key | Default | What it controls |
| --- | --- | --- | --- |
| Answer characters | `MailAnswering:MaxAnswerCharacters` | 20 000 | How much of the model's answer one response carries |
| Cited emails | `MailAnswering:MaxCitations` | 20 | How many messages one response names |

Cutting is right where refusing was wrong, and for one reason: a request larger than a limit is the caller's to correct,
while an answer larger than a limit has already been generated and paid for, and refusing it would discard a real answer
over its length. What makes cutting safe is that it is reported — a response says which of the three cuts was made, so a
shortened answer is never indistinguishable from a complete one, a claim traced to a message the response no longer
names is never presented as checkable, and a run that was stopped from reading further never reads as one that read
everything.

The citations are one per email rather than one per passage. A run makes several lookups and one message can answer more
than one of them; a reader given a list of sources wants the messages, not the number of times each was found. Neither
one carries an extract: the passage has already reached a provider, and returning it to the caller as well would publish
mail content from a call whose result is an answer.

Each citation does carry the sender verdict, in the shape a listing publishes it in and without the evidence behind it.
An answer is worth what the mail behind it is worth, so a reader deciding whether to act on a claim is told whether the
message it came from had an author anybody established and whether this deployment recognizes that author. What the
verdict is reached from stays with the single-email read the citation resolves through.

It carries the [machine-authorship reading](machine-authorship.md) beside it, in the shape a listing publishes that in,
and for a narrower reason: it is one more thing a reader may want to know about a message an answer rests on, and it
says nothing about whether the message is safe or true. The signals behind the number stay with the single-email read
as well.

## When a deployment answers questions at all

Answering needs both halves of the AI configuration at once — an embedding profile a question can be retrieved against,
and a chat endpoint the run is conducted through. Either one absent makes answering something this deployment does not
do; either one failing makes it something it currently cannot do. The reading is made in one place, so the surface that
advertises the tool and the one that runs a question cannot disagree about it.

- **Inactive** — no chat endpoint was declared, or this instance embeds no mail. Nothing is wrong and nothing changes on
  its own.
- **Available** — both halves are configured and neither is currently refusing.
- **Degraded** — both are configured and one of them cannot serve: a refused credential, an unreachable endpoint, or an
  active embedding profile whose space nothing can place a query in.

It is a decision about now rather than a report of the last call, which is the difference from the capability
[Email search](email-search.md#what-the-three-capability-states-mean) publishes. A recorded chat failure withholds
answering for a minute and then stops withholding it, so one question is let through to find out whether the credential
has been rotated. Nothing else in the process calls the chat endpoint once the second retrieval pass is off, so a reading
that stayed degraded for as long as the last failure was on record would leave a repaired deployment permanently unable
to demonstrate it. The embedding half needs no such window: synchronization and the search path call that provider as a
by-product of work they were doing anyway, so its record renews itself.

Reading the capability costs one committed read of local state and one read of process-local health. It calls no
provider, deliberately: a capability that spent a paid call to be reported would put an operator's money behind every
listing of the tools a server offers.

## A run is several calls, and each carries the bounds of one

A run is a conversation with the provider: the model asks for mail, the tool answers, the model writes the answer. Every
one of those calls carries the deployment's declared generation parameters, its request deadline, its resilience budget,
and its health reporting — the same bounds a single chat request carries, applied per call rather than per run. What the
*run* may spend across all of them is [§ What one question may spend](#what-one-question-may-spend) above, and that
check sits outside the resilience one deliberately: a call this deployment's own ceiling refused never reached the
endpoint, so recording it against the provider's circuit or its health would report an outage that is not happening.

The one thing this shape gives up is stated rather than hidden: the transport underneath is opened once for the run
instead of once per attempt, so a retried call reuses the handler chain the run began with. A run is short, and an
endpoint that moves mid-run is reached at its new address by the next run.

The question itself is bounded before anything is sent, by the same conversation bound a single call carries: a question
larger than one call may send is refused rather than truncated.

## The run cannot change anything

The agent is composed with one tool and that tool searches. There is nothing that sends, deletes, moves, or marks mail
as read, so a question is never a mutating act — a property of what the agent is made of rather than a rule written
beside it. Retrieval reaches no mail server either: it answers from what synchronization has already stored.

## An account can keep a record of what a question read, and none does by default

An answer produced by a model is not reproducible. Asked twice, the same question over the same mailbox can produce two
different answers, so the only way to explain one afterwards is to have recorded what produced it. An account can
therefore keep a durable record of the runs answered from its mailbox, and it is **off by default**:

| Key | Type | Default | What it does |
| --- | --- | --- | --- |
| `MailSynchronization:Accounts:<n>:AnsweringAuditTrail:Enabled` | bool | `false` | Whether a finished run leaves an entry for this account |
| `MailSynchronization:Accounts:<n>:AnsweringAuditTrail:Retention` | TimeSpan | `30.00:00:00` | 1 day – 3650 days; how long an entry is kept |

It is a separate decision from the [mutation trail](imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)
beside it and deliberately not the same switch. One record says where a person's mail has been; this says what it was
read for, and an operator may want either without the other. Both are off by default for the same reason: each is
derived personal data an operator undertakes to hold, describe, and erase rather than something MailFathom accumulates
for everyone.

### What an entry holds, and what it deliberately does not

One entry per account in the run's scope, so a question asked across two mailboxes leaves one entry each, sharing a run
identifier and naming only its own account's mail. An entry carries:

- the emails of that account the run retrieved, in the order it first reached each, and which of them the answer cited;
- the chat endpoint alias the run was conducted through, and the version of the instruction it was conducted under;
- when the run began and when it ended;
- how it ended, and how it degraded.

**No retrieved passage, prompt, completion, or fragment of mail content is stored.** Identifiers, MailFathom's own
configured names, and bounded outcomes are the whole of it. A record that stored the retrieved passages would create a
second copy of the mailbox with its own retention, access, export, and erasure obligations — for the sake of a debugging
convenience. What the identifier buys instead is that the message can be fetched and read whole, by somebody entitled to
it, through the reads that already serve it.

An account in scope that a run drew nothing from still gets an entry naming no mail. That is a recorded fact rather than
a missing one: the question was asked of that mailbox and took nothing out of it, and a record that appeared only when
mail was found could not answer whether a mailbox had been queried at all.

A run that ended without an answer is recorded too, with what it had already read. A run that failed on its third
provider call has read somebody's mail, and a record built from the answer alone would say it read nothing.

### It is written after the answer, and never at the answer's expense

The entry is appended once the run has ended and the answer has already been produced, so there is nothing left for it
to roll back and nothing it may fail. An append that does not happen is warned about naming the run and counted, which
is the only thing that makes swallowing the failure defensible — [Telemetry](../operations/telemetry.md#what-a-run-records)
names the counter.

A question refused before a run began — this deployment answers none, or the period has spent its allowance — is not a
run and leaves no entry. Recording a refusal as a run that read no mail would put it among the answers.

### It is erased twice over

Retention erases whole entries past each account's configured window, in bounded batches, on the same pass the account's
own synchronization run already makes for the mutation trail. A failure there never fails the run and never puts the
account into backoff; the next run erases what this one did not.

Beside that, **an entry follows the mail it names**: erasing a message erases it from the runs that read it, through the
same cascade every other derived row rides. That is the difference from the mutation trail, which deliberately survives
the mail it describes because the act it records may have *been* the deletion. Nothing of the sort applies to reading a
message, so a message nobody may hold any more is not one this record goes on naming.

It is read through the administrative endpoint, beside the mutation trail and under the same credential.
[The administrative endpoint § Reading what a question read](../operations/admin-endpoint.md#reading-what-a-question-read)
holds the route, its filters, and the erasure statement a data-subject request is answered with.

## What a run publishes as telemetry

Every run opens a span on the `MailFathom` activity source, named `answer_mail_question`, inside the MCP tool call it
happened in — so the provider calls the run made sit beneath it and a slow or degraded run is attributable without
opening a database. [Telemetry § What a run records](../operations/telemetry.md#what-a-run-records) lists its tags.

The split between the two records is the point rather than an accident. Which messages a run read cannot go on a span:
a tag per message opens a time series per person, a span store is not MailFathom's to carry an obligation in, exports
are off by default, [a deployment may sample spans away](../operations/telemetry.md#how-much-of-a-trace-is-recorded),
and an erasure request cannot reach somebody else's trace backend. How long a run
took and how much it considered cannot usefully go in a table either, because that is what a dashboard already answers.
Between them the three questions an operator actually has are answered, each in one place: why did it answer *that* —
the record; why is it slow — the span; why did it degrade — the span.

## What never reaches a log

No question, no answer, no query the model wrote, no retrieved passage, and no relevance judgement. A record of a run
carries the endpoint alias, how many passages were retrieved, and how many messages they came from — counts and a name
the operator chose. A record of the second pass carries how many candidates were judged, how many were dropped, and
which classification a failed judgement had. A record of a spend ceiling carries the counts the period reached and the
length of the period, and a run that reached its retrieval ceiling is recorded as a passage count.

The same rule reaches the meter: what a period consumed is published as runs and tokens, which describe the size of what
left without describing any of it.

None of MailFathom's own components on this path logs a lookup or an extract, which is a property of what they write
rather than of a level somebody set. What the orchestration framework underneath would emit at `Trace` — the arguments a
tool was called with and the result it returned — is the one place a query or an extract could reach a log, so that
level is not one to turn on for this category on a deployment holding real mail.

Retrieved mail is untrusted input, and so is what the model writes from it. [Chat generation § What never reaches a
log](chat-generation.md#what-never-reaches-a-log) states the same rule for the transport underneath.
