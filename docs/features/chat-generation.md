# Chat generation

<!-- describes: src/AI/Chat/**, src/AI/Providers/**, src/AI/ProviderAdapters/Chat*.cs, src/AI/ProviderAdapters/ProviderChatModelClient.cs, src/AI/ProviderAdapters/ProviderCallFailure*.cs, src/Application/Chat/**, src/Application/AiProviders/**, src/Host/Configuration/Chat/**, src/Host/Configuration/Providers/**, src/Host/Hosting/AiProviderHealthCheck.cs, src/Infrastructure/Observability/AiProviderHealthTracker.cs -->

Text in, generated text out. This page describes the second kind of outbound AI call MailFathom makes: what a
deployment declares to enable it, what one call is allowed to spend, what a failing call is classified as, and how an
operator sees whether the provider is working.

It is the transport and nothing above it. Nothing here composes a prompt, retrieves anything, offers the model tools, or
keeps a conversation — what to say is decided by whoever calls, and this is what says it. What calls it is the answering
run behind the `ask_mail` tool, described in [Mail answering](mail-answering.md), and the optional second retrieval pass
beside it.

## A chat provider and an embedding provider are separate choices

`Chat` is a configuration root of its own beside `Embeddings`, and the two are declared, credentialed, called, and
reported on independently. That is deliberate, because the states they produce are different:

- **No embedding provider.** Semantic search is off and lexical search continues. [Embedding
  generation](embedding-generation.md) describes that half.
- **No chat provider.** Search is unaffected, and only the answering capability stops being offered. [Mail
  answering](mail-answering.md) describes what that capability is composed of.

An instance may reasonably have one and not the other, so a single "AI is configured" flag would be wrong in both
directions. Writing neither section is a supported deployment: nothing is generated, no provider is called, no
credential is needed, and every read path serves as it always did.

A section carrying a model and a key but no `Alias` is the one shape startup refuses rather than passes over. It reads
to an operator as a configured provider while nothing would ever call it.

One declared endpoint serves more than one capability. Beside answering a question, it is what judges retrieved
candidates for relevance where a deployment turns that pass on — a block inside this section, off by default, described
in [Mail answering § An optional second
pass](mail-answering.md#an-optional-second-pass-the-model-decides-what-answers). Each capability is a separate decision
over one endpoint, and every call any of them makes carries the parameters, the deadline, and the budget declared here.

## One endpoint, not a chain

The embedding declaration is an ordered chain because a fallback embedding endpoint is another route to *one vector
space*: startup proves every endpoint of the chain declares the same geometry, so falling through cannot change what a
vector means.

Nothing proves that of two chat models. Falling through would silently answer a person in a different model's voice,
with different capabilities and different refusals, and nothing above this boundary could tell it had happened. So the
chat declaration names one endpoint. An operator who wants failover puts a gateway in front of it, where the
substitution is theirs and is visible to them.

## The endpoint's name is what everything else calls it

`Alias` is the deployment's own name for the endpoint. Everything else in the declaration is an address or a
credential, and neither may be written down — an address identifies a tenant and a resource — so the alias is what a log
line, a metric tag, a resilience circuit, and a failure message carry instead.

**An alias names one endpoint across the whole deployment.** A chat endpoint reusing an embedding endpoint's alias is
refused at startup, because the alias is what a credential is resolved by, what a resilience circuit is keyed by, and
what every log line naming an endpoint carries. Two endpoints answering to one name would share all three, so a chat
outage would open the circuit the embeddings were being served through.

## Two providers, one client

OpenAI and Azure OpenAI are the two this release reaches, through the same client construction the embedding adapter
uses: an Azure resource's v1 data plane is OpenAI-compatible, so a cloud deployment is the same client pointed at the
resource's own `/openai/v1/` address with the deployment's name as the routed model.

There is no vendor and no model identity in the declaration beside the routed name, and that is the difference from an
embedding endpoint rather than an omission. A vector is stored and later compared against other vectors, so which model
produced it has to be recorded and proved; an answer is produced, presented, and gone.

## Two APIs, and the deployment says which

`Api` names which of the provider's two request APIs a call is conducted through: `ChatCompletions`, which is what a
deployment stating nothing runs on, or `Responses`. Both are reached over one endpoint with one credential on one
transport under one resilience budget, so what differs is the path a request goes to — `/chat/completions` or
`/responses` under the declared address — and what the provider will accept there.

**It is declared rather than derived, and that is the decision rather than a shortcut.** Deriving it would mean reading
the routed model name, and that name is not a model identity: for a cloud deployment it is whatever the operator called
the deployment, so a derivation would be guessing from a string the operator invented, and a wrong guess is one nothing
in the deployment could correct. Declaring it costs an operator one line about their own provider, which is a thing they
already know; deriving it would cost every operator whose provider does not match the guess a capability they cannot
turn back on. Azure's OpenAI-compatible surface and any self-hosted OpenAI-compatible server are exactly why: neither
necessarily offers both paths, and nothing about a model name says which.

**When it has to be `Responses`.** A current reasoning model refuses function tools beside a stated reasoning effort on
the chat completions API, and names the responses API as the way to have both. The answering run behind `ask_mail` is a
tool loop by construction — the model asks for mail, retrieval answers, the model writes — so removing the tools would
remove the capability rather than work around the refusal. A deployment that wants such a model states `Responses` here.
Choosing it against a server that does not serve that path is a *request refused*: the endpoint rejected the request
itself, so it is not repeated and the provider is reported misconfigured until the declaration is corrected.

**One consequence is worth knowing.** The responses API reports an outcome rather than a finish reason, so an answer
that simply finished names nothing and arrives as `Unreported` in the table below rather than as `Completed`. A
truncation and a content filter still arrive named, which is what keeps either from being repeated as though it were a
transport fault. Nothing else about the two paths is visible above this boundary.

## Authentication has two shapes

The same two, under the same rules, as an embedding endpoint's: **either** a provider key **or** one of four
non-interactive Microsoft Entra credentials, never both and never neither. [Embedding generation § Authentication has
two shapes](embedding-generation.md#authentication-has-two-shapes) states them in full, including why
`DefaultAzureCredential` is deliberately not used and what rotating each shape costs. One credential source resolves
both sections, keyed by the alias, which is what the deployment-wide uniqueness rule above exists to make safe.

## The model and its parameters come from configuration

None of them is a compile-time constant, so changing model is an edit rather than a rebuild and a model released after
this version can be declared without one.

- `Model` is what a request is routed to. For a cloud deployment that is the name the operator gave the deployment
  rather than the vendor's model identifier, because that is the string the endpoint recognizes.
- `MaxOutputTokens` bounds what one answer may occupy. It is the one generation parameter with no useful provider
  default: left unset, a model is free to generate until it stops and a deployment cannot bound what a single call
  costs.
- `Temperature` and `TopP` are left unset unless written. Several current models reject the parameters outright, so
  sending a value one of them refuses would turn every call the deployment makes into a rejected request — which is why
  writing nothing has to mean sending nothing.
- `ReasoningEffort` states how much reasoning a model is asked to spend before it answers, and follows the same rule for
  the same reason: a model that does not reason rejects the parameter, so a section that writes none sends none and the
  request is exactly what it was. `None`, `Low`, `Medium`, `High`, and `ExtraHigh` are what may be written, and
  `ExtraHigh` reaches the provider as its `xhigh`. **`None` is not the same as writing nothing** — it states an effort of
  none and sends it, which is precisely what a provider refusing function tools beside an *unstated* effort asks for.
  Not every reasoning model accepts every level, and one that does not refuses the request rather than falling back.

**What a model must support for `ask_mail` to work at all.** The answering run offers the model function tools and
requires it to call them, so a model that cannot be given tools cannot answer a question here whatever else is declared.
Where a reasoning model refuses tools beside a stated effort, `Api` is the setting that resolves it, and the two are
therefore chosen together rather than independently.

## Bounds every call carries

- **A conversation bound.** `MaxMessagesPerRequest` and `MaxRequestCharacters` are checked before anything is sent. Both
  are refusals rather than truncations: cutting a conversation down to fit would send the model a different question
  from the one it was given and return an answer to that, which no caller could detect. The character ceiling is stated
  in characters rather than tokens because counting tokens would mean carrying the model's own tokenizer; set it below
  what the model's context window allows.
- **An explicit timeout.** `RequestTimeout` bounds one request, applied by MailFathom rather than left to whatever the
  provider library defaults to. A deadline that expires is reported as a timeout rather than as a cancellation, because
  a cancellation would tell the pipeline that this system stopped the work. Its default is longer than an embedding
  request's, because generating an answer takes as long as the answer is.
- **One retry layer.** The call runs under the `AiProviderInvocation` resilience pipeline, and both the provider
  library's own retry policy and the standard HTTP resilience handler are switched off for it. [Outbound
  resilience](../architecture/outbound-resilience.md#the-single-layer-rule) holds the rule and why two layers multiply
  rather than add.
- **A bounded response.** The transport refuses a body larger than the configured output budget could fill, and refuses
  redirects — a moved endpoint answering with one would carry the key or the bearer token to whatever host it named. It
  is a registration of its own rather than a second consumer of the embedding client's, because an answer's size follows
  the output budget while an embedding response's is fixed by the declared geometry, and one client would have to take
  the larger ceiling and would then bound neither.

## An answer that was cut short is still an answer

A call that produced text returns it, together with why the model stopped:

| Stop | What it means | What a caller does |
| --- | --- | --- |
| Completed | The model finished what it had to say | Present it |
| Output limit reached | The output budget cut the generation off | Present it with the truncation stated, or ask again with a larger budget |
| Content filtered | The provider's content filter stopped the generation | Present it as a refusal — what survives is a fragment |
| Unreported | The provider named no reason | Present it, without claiming the model finished |

Reporting these on the answer rather than as failures is what guarantees neither is ever repeated as though it were a
transport fault: nothing repeats a call that returned something. It is also the honest reading — the text before the
stop is real, and the call has already been paid for.

## What a failing call is classified as

A call that produced no text at all fails, and says which kind of failure ended it. Collapsing these into "the call
failed" gets the next two decisions wrong: a rate limit answered with an immediate retry is how an account gets
throttled harder, and a refused credential repeated is how the same refusal is bought again while the account carries
the requests.

| Classification | What it means | Repeated? |
| --- | --- | --- |
| Credential rejected | The endpoint refused the credential presented | No — rotate or correct it |
| Rate limited | The deployment is over its allowed rate | Yes, after a backoff |
| Request timed out | The endpoint did not answer within the configured time | Yes |
| Transport faulted | The request never reached an answer | Yes |
| Request refused | The endpoint rejected the request itself — a model it does not serve, a conversation beyond its context window, a parameter it does not accept | No — correct the declaration |
| Answer empty | The endpoint ended the call without producing any text | No |

A caller's own cancellation and a host shutdown are absent from that table on purpose. Both are this system's own
decision rather than a remote party's answer, so reporting one as a provider failure would let it open a circuit
against a healthy endpoint.

**A prompt the provider's own safety system refused before generating anything arrives as "request refused."** Telling
it apart from any other rejected request would mean reading the provider's error body, and that body quotes the
request. The request is somebody's question and, once retrieval exists above this boundary, passages of their mail.

An endpoint the resilience pipeline declined to call at all — a circuit it opened after repeated failures, or a
concurrency budget already spent — arrives as a transport fault, which is what a caller waits out.

## Provider health is tracked per provider

Each provider records what its last call established, and the two states are kept apart:

| State | What the last call established | What it asks of an operator |
| --- | --- | --- |
| Unobserved | Nothing has been called yet | Nothing. It is the state of a freshly started instance |
| Serving | The last call reached the model and came back | Nothing |
| Unavailable | The last call failed for a reason a later attempt may not meet | Wait, or look at the provider's own status |
| Misconfigured | The last call failed for a reason no later attempt changes | Rotate a credential, or correct the declaration |

The split between the last two is the same property the resilience pipeline reads, so the health state and the retry
decision can never disagree about whether waiting is the answer.

**Serving is about the provider, not about the answer.** An endpoint that took the request, authenticated it, ran the
model, and came back with no text is a working endpoint — the credential, the address, and the routed model were all
right — so an *answer empty* failure records `Serving` rather than moving the state. The consequence is worth stating,
because it is the one case where a failing capability leaves a healthy-looking provider: a deployment whose model
answers with nothing every time reports `Serving` indefinitely, and what shows the problem is the failures themselves
in the log, not this state. Every other classification in the table above moves the state.

**Nothing probes a provider to find out.** A paid call made to answer a health check would spend an operator's money on
every scrape, and the answer would be about a request nobody asked for. What is reported is the outcome of the last real
call.

One consequence is worth knowing before reading a `Degraded` probe: **the probe reports the state without its age.** The
moment the last call ended is recorded, and the health check does not read it, so a provider that failed once during a
deployment and has not been called since probes exactly as one that failed a moment ago does. On an instance that embeds
continuously the state is as current as the work; on one whose chat provider nothing calls, a stale failure can sit there
indefinitely. Read the log records for when it happened.

What does read the age is whatever has to decide whether calling again now would buy anything. Semantic search lets one
query through after a minute without a fresh observation, and the answering capability behind `ask_mail` does the same
for the chat endpoint, so a repaired credential is discovered without a restart even though the probe would still be
reporting the old state until something calls.

Three things make the states readable:

- **A health check per declared provider**, named `ai-chat-provider` and `ai-embedding-provider`. Both reach the
  readiness probe alone and **neither ever reports worse than degraded**. Neither provider serves a request path — an
  instance with a failing embedding provider still answers every search lexically, and one with a failing chat provider
  still answers every search at all — so a failing provider must not take the instance out of traffic, and must never
  reach the liveness probe where it would restart a process that is working. A deployment that declared only one
  provider registers only that one.
- **A gauge**, `mailfathom.ai.provider.health`, carrying one measurement per role under the tag
  `mailfathom.ai.provider.role`. It publishes the state's own value rather than its name, because an instrument's value
  has to be a number; the values are allocated once and never reordered. A role nothing has called publishes no
  measurement, so a flat line always means a provider that is being watched.
- **A log record for each transition**, and for nothing else. Losing a capability is written at `Warning` naming the
  role, the state it left, and the state it reached; regaining one is written at `Information`. Only a change is
  recorded, because every provider call records a state and a line per call would put the log's volume on the size of
  the mailbox rather than on anything an operator would act on — and a first call that succeeded is not one of those
  changes, because it restored nothing. A first call that *failed* is. This is what answers *when* a state changed,
  which the state itself deliberately does not carry.

## What never reaches a log

No prompt, no answer, no credential, and no provider response body. A prompt is somebody's question and the passages of
their mail; an answer is written from both; a provider's own error text quotes the request that produced it. The
classification, the endpoint alias, the stop reason, and the token counts are what a log record carries instead.

Token counts are the one part of a call that is safe to keep: a count says how much was sent without saying any of it,
and it is what makes a chat provider's cost visible while it is being spent rather than at the end of a billing period.

Model output is treated as untrusted input, because it is written from untrusted input. It is encoded for whatever
destination presents it and never interpreted as markup, a command, or a path on the way there.
