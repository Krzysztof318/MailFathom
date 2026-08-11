# Embedding generation

<!-- describes: src/AI/Embeddings/**, src/AI/Providers/**, src/AI/ProviderAdapters/**, src/Application/Emails/Embeddings/**, src/Host/Configuration/Embeddings/**, src/Host/Configuration/Providers/**, src/Host/Hosting/Warnings/AiProviderTransportEncryptionWarning.cs -->

A chunk is a passage of text. A vector is where that passage lands in a space a model defines, and two vectors of one
space can be compared, which is what makes semantic search possible at all. This page describes how MailFathom turns
the first into the second: what it declares, what it calls, and what it does when the call fails.

Nothing here decides *when* to embed, and nothing here stores a vector. Those belong to the worker and to the schema —
[automatic embedding](automatic-embedding.md) is what decides that a newly synchronized message should be embedded.
What this owns is the one boundary that talks to a provider.

## An instance that embeds nothing is a working instance

Writing no `Embeddings` section is a supported deployment rather than an omission. No vectors are produced, no
provider is called, no credential is needed, and lexical search answers exactly as it did before. That is the state an
operator who has not chosen a provider should be left in, rather than being made to choose one to start the service.

Chunking happens either way. Chunks reach no network and cost nothing an operator has to consent to, and they are what
a later activation embeds — so an instance that turns embedding on later has the passages already.

## Declaring is free; activating is what spends

Which model an instance embeds with is a configuration value, so a reviewer, a chart, and a `git diff` all see it.
Editing that value starts nothing: it says what this deployment intends, and an explicit activation is what computes
the profile identity from it, states the estimate, takes the confirmation, and begins producing vectors.

That split is deliberate and is the whole of [ADR
0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)'s
answer to a hazard worth naming: embedding is the first thing MailFathom does that costs money per unit of mail. A
configuration edit that re-embedded a mailbox at the next restart would be an invoice arriving a month later for a
decision nobody was shown.

**An operator who edits the model and expects the change to take effect has to know that it did not.** `mfctl embedding
status` is where they find out — it says outright that a declaration is waiting for an activation nobody has performed
— and `mfctl embedding activate` is what performs one. Neither is automatic, and
[administering the embedding profile](../operations/admin-endpoint.md#administering-the-embedding-profile) is where
both are documented, with the estimate the second one states before it spends.

What an activation then does — build a new generation beside the one still answering searches, switch to it once, and
remove what it replaced — is [changing the embedding model](../operations/embedding-profiles.md). It also starts:
the walk over the mail an instance already had takes its next pass as soon as the activation commits rather than
whenever an interval chosen while there was nothing to embed happens to expire, which
[embedding backfill](embedding-backfill.md#an-operators-act-does-not-wait-for-the-pause-to-expire) records.

## What a declaration says

Each entry of `Embeddings:Endpoints` declares a whole geometry — provider, model, model version, dimension, distance
metric, and how a passage is prepared — beside the endpoint address and the credential. [Configuration
reference](../operations/configuration-reference.md#embeddings) is the inventory.

The geometry is repeated per endpoint rather than stated once for the chain, and that is the point rather than
duplication to tidy away. It is what makes a disagreement expressible, and therefore refusable.

### The chain is one vector space reached several ways

`Embeddings:Endpoints` is ordered, and a failing endpoint falls through to the next. What every entry must share is
the geometry: the same model, the same width, the same metric, the same preparation. One model offered both by a
vendor's own API and by a cloud deployment of it is the case this exists for — the endpoint fails, the vector space
does not.

A chain whose entries disagree is refused at startup, naming both aliases and the property they differ on. The refusal
is not a restriction to work around. A fallback on a different model does not produce a degraded vector; it produces a
point in a different space, and a distance computed against it is a number with no meaning. Written under the active
profile, those vectors would make retrieval slightly worse rather than fail — which is the hardest possible failure to
attribute.

An operator who genuinely wants a different model when the first is unavailable is asking for a second profile and a
switch between them. That is a deliberate operation, not a fallback.

Falling through is logged and changes nothing about what is written: the vectors a fallback returns belong to the same
profile, because its identity is the same.

### The endpoint's name is what everything else calls it

Everything else about an endpoint is either an address or a credential, and neither may be written down: an address
identifies a tenant and a resource, so a failure message that named one would put it in every log line. `Alias` is the
name the operator chose, and it is what a log record, a metric tag, a resilience circuit, and a failure message use.

### What each setting decides

The [configuration reference](../operations/configuration-reference.md#embeddings) is the inventory — every key, its
type, its default, and its bound. What follows is the part a table cannot carry: which question each value answers.

**Identity and routing are separate settings.** `Provider`, `Model`, and `ModelVersion` name the vendor and the model
that define the space, and what they exist for is the profile: they are recorded on it, so a stored vector says what
produced it. `RoutedModelName` is the string a request is routed on, and it is the only one of the four that exists to
be sent. Leaving it empty means *route on `Model`*, which is right wherever an endpoint knows the model by the vendor's
own identifier; the two diverge exactly where the endpoint knows it by a name the operator invented — a cloud
deployment's own name — which is what lets one model reached through a vendor's API and through a deployment of it be
one vector space instead of two. `Provider` is stored verbatim and matched against nothing: which vendors exist is not
MailFathom's set to close, so what a vendor calls itself is what you write.

**Five settings decide what a vector means.** `Dimension` is the width the stored vectors have, `DistanceMetric` is how
two of them are compared, and `InputCharacterLimit`, `PassageInstruction`, and `NormalizeVectors` are the preparation —
how much of a passage the model was shown, what instruction it was shown with, and whether the answer is kept at unit
length. Those five and the three identity settings are the whole of a profile and nothing else in the declaration is
part of one, which is why moving any of them declares a *different* space rather than editing the existing one, and why
the edit alone starts nothing until an activation moves the deployment onto it.

**`SupportsRequestedDimension` is a declaration about the endpoint, not an inference from the model.** With it on, the
declared width is asked for in the request, and a model trained to answer at it returns a vector already normalized for
that width; with it off, the endpoint's own width comes back and is cut down only where the deployment allows trimming.
It is declared for the same reason the model is never a compile-time constant here: a table of which models accept the
parameter would be a list of model names in code, and it would be wrong the week after it was written.

**`Address` and the credential are where the endpoint is and what it presents.** Neither is part of a profile — moving
either changes where a vector is bought, never what it means. Both carry a rule, stated in full in [what an address has
to be](#what-an-address-has-to-be) and [authentication has three shapes](#authentication-has-three-shapes) below.

## Vector width is a database decision

pgvector stores far more than it indexes: a `vector` column holds up to 16000 dimensions and an HNSW index covers
2000. So a model is not merely large or small — it is indexable, or it is stored and searched exactly, which is
correct but linear in the number of vectors.

The index that covers it belongs to one profile rather than to the table, which is why a width is a property of a
generation instead of of the schema. [Stored email
schema](../architecture/stored-email-schema.md#the-index-no-migration-creates) describes its shape and what maintaining
one costs.

`AllowTrimVectors` decides which, and it is off by default. With it off, a declared dimension above what an index
covers is refused at startup, naming the dimension and the ceiling, rather than quietly producing an instance whose
semantic search never becomes fast. With it on, the declared width is what the profile records and what the stored
vectors have — because a trimmed vector occupies a different space than the full one, and a profile claiming the
model's nominal width would be describing vectors that do not exist.

Where the endpoint can produce the narrower vector itself, that is used in preference to cutting one down:
`SupportsRequestedDimension` asks for the declared width, and a model trained to answer at it returns a vector already
normalized for it. Where the adapter must shorten a wider answer, it renormalizes — dropping the tail of a unit vector
leaves one shorter than unit length, and a cosine distance between vectors of differing lengths is a number rather
than an error.

## An endpoint is any service that speaks the OpenAI wire protocol

One client construction reaches all of them. The address, the credential, the transport, the retry opt-out, and every
bound below are the same whoever serves the request, and nothing in the declaration is a compile-time constant: the
vendor, the model, the routed name, the width, the metric, and the preparation are strings and numbers read from
configuration. Pointing a deployment at a different service is therefore a configuration entry rather than a feature
request, and a model published after this version is declared without a rebuild.

**OpenAI and Azure OpenAI are what this project declares, which is a different statement from what the mechanism
reaches.** Azure is already the second case rather than a special one: an Azure resource's v1 data plane is
OpenAI-compatible, so a cloud deployment is that same client pointed at the resource's own `/openai/v1/` address with
the deployment's name as the routed model. A third-party service that speaks the same protocol is declared the same way
— an address, a routed name, and a credential, beside the geometry every entry declares — and reaches the same code.

The choice reaches beyond embeddings — a declared chat endpoint is served by this same wiring rather than by a second
one, as [chat generation](chat-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol)
describes. A chat endpoint may state which of the provider's two request APIs its calls go to, which is a path under the
declared address rather than a second wiring: the address, the credential, the transport, and the retry opt-out are the
ones described here either way. An embedding endpoint has no such choice, because embeddings are served on one API only.

### A worked example: an endpoint that is neither

The names below are placeholders, and deliberately so — nothing in a declaration is matched against a list of vendors,
models, or hosts:

```json
{
  "Embeddings": {
    "Endpoints": [
      {
        "Alias": "house-embeddings",
        "Provider": "example-ai",
        "Model": "example-embed-2",
        "Dimension": 1024,
        "DistanceMetric": "Cosine",
        "NormalizeVectors": true,
        "SupportsRequestedDimension": false,
        "Address": "https://embeddings.example.test/v1",
        "ApiKey": {
          "Name": "house-embedding-key",
          "SecretReference": "file:/etc/mailfathom/secrets/house-embedding-key"
        }
      }
    ]
  }
}
```

Read across it: `Provider` and `Model` are what the profile records and what a stored vector is attributed to, and
`Provider` never leaves the deployment; `Address` is where the request goes, and it carries the whole base path the
service documents for its OpenAI-compatible surface, including any version segment; no `RoutedModelName` is written, so
`Model` is also what the request routes on, which is right wherever the service knows the model by the vendor's own
identifier; `Dimension` and `DistanceMetric` are what the vectors are and how they are compared, so both must match
what the service actually returns rather than what a similarly named model returns elsewhere; and
`SupportsRequestedDimension` is `false` because a service that ignores a requested width would otherwise be sent one
and answer at its own.

The chat half of the same service is a separate declaration under `Chat`, with its own alias and its own credential —
[chat generation](chat-generation.md#a-worked-example-an-endpoint-that-is-neither) carries it.

### Compatible is not verified

"Speaks the OpenAI wire protocol" is a claim a service makes about itself, and MailFathom does not check it: it sends
what this page describes and reports what comes back. The set this project has actually exercised is the two named
above, and nothing else. A service absent from that set is not refused — the mechanism will reach it — but no claim is
made here that it works, and the differences that decide it are ordinary rather than exotic: an OpenAI-compatible chat
surface commonly has no embeddings route at all, a service may reject the requested-width parameter, and a model whose
name matches one elsewhere may answer at a different width or with vectors that are not of unit length. Each of those
surfaces as a *request refused* or an *unexpected vector shape* on the first call rather than at startup, because
nothing here can ask a service what it serves without paying for a request.

## What an address has to be

**Absolute, and HTTP or HTTPS.** Startup refuses anything else, naming the endpoint alias and the rule.

**A credential never travels in the clear.** A plain `http` address is refused wherever the endpoint declares a provider
key or a Microsoft Entra credential, because the request would publish that credential to everything on the path between
this deployment and the service, and an endpoint declared once wrongly would go on doing it on every call. That is the
rule the whole scheme check was ever about: what makes a plain address dangerous is the secret travelling on it, so an
endpoint holding no secret is a different situation rather than an exception to the same one, and it is the one shape a
plain address is accepted for.

**Empty means the provider library's own default**, which is the first-party OpenAI API. That is a convenience for the
one case it fits and a trap for every other, so any endpoint that is not first-party OpenAI writes the address out —
including a cloud deployment, whose resource has an address of its own ending in `/openai/v1/`.

## A model server you run yourself

Every local inference server presents one shape: the OpenAI wire protocol, on a private or loopback address, over plain
HTTP, with no credential in front of it. Declaring one is an ordinary endpoint entry that writes the plain address and
`"Unauthenticated": true`, and it reaches the same client construction, the same resilience pipeline, and the same
ceilings as a vendor endpoint.

```json
{
  "Embeddings": {
    "Endpoints": [
      {
        "Alias": "house-embeddings",
        "Provider": "example-ai",
        "Model": "example-embed-2",
        "Dimension": 1024,
        "SupportsRequestedDimension": false,
        "Address": "http://model-server:8000/v1",
        "Unauthenticated": true
      }
    ]
  }
}
```

**What it gains** is that no mail content leaves the machines the operator runs. The passages that are embedded, and on
the chat side the questions and the answers, reach a service the operator started rather than an account with a vendor;
there is no third-party processor, no per-token invoice, and no key to provision or rotate.

**What it gives up** is the confidentiality of that hop, and MailFathom cannot judge how much that costs. A container
network name and a public host name are the same string in a configuration file, so no startup rule can tell a server
beside this process from one across the internet — which is why nothing here reads the address to decide whether the
plain scheme is allowed. What runs instead is a report: an instance holding an endpoint on a plain address writes one
warning per endpoint at startup, naming the alias and what crosses the hop readable, and leaving the judgement about the
network with the person who built it. The address itself is never written to a log, for the reason no address or
credential is.

**MailFathom reaches such a server; it does not run one.** No model, no weights, and no inference runtime are loaded
into this process or shipped in its image — that would make MailFathom responsible for model distribution, licensing,
hardware sizing, and a second failure domain inside its own process, for a capability an operator gets by starting one
container beside it. Which servers the project has actually exercised is stated under [compatible is not
verified](#compatible-is-not-verified), and that set is unchanged by this: the mechanism reaches a service, and reaching
it is not a claim that it was tested.

## Authentication has three shapes

An endpoint declares **exactly one** of a provider key, a Microsoft Entra credential, and `Unauthenticated`, and startup
refuses any other combination, naming the alias. Declaring none is the shape a forgotten reference takes — an operator
who wrote the address and the model and expects the endpoint to work learns it here rather than from a rejected call
they paid for — which is why an endpoint that genuinely needs no credential says so rather than leaving the blocks out;
and declaring two does not say which one a request should present, which is a question no default here should answer on
an operator's behalf. The rules below govern a declared chat endpoint identically, and
one credential source resolves both — which is why an alias names one endpoint across the whole deployment rather than
within one section.

A key is a secret reference like every other credential this deployment holds, resolved per request — so rotating it
behind an unchanged reference takes effect on the next call, with no cache to invalidate and no restart.

`Unauthenticated` resolves nothing and presents nothing: the request carries no authorization header at all, rather than
a placeholder one. It is the shape [a model server you run yourself](#a-model-server-you-run-yourself) needs, and it is
what makes a plain address permitted.

A Microsoft Entra credential exists for the deployment where there is no secret to provision at all. Four shapes are
supported and they are the whole of the set: managed identity, workload identity, client secret, and client
certificate. Every one of them is non-interactive by construction, because MailFathom is a background service with
nobody at a keyboard: a credential that opens a browser or prints a device code would surface as a request that never
returns.

**`DefaultAzureCredential` is deliberately not used.** Its chain reaches both of those interactive shapes and the
developer-tool credentials of whoever last signed in on the host — which would let a deployed service authenticate as
an operator's own account because a stale sign-in happened to be there. MailFathom composes its four explicitly and
reaches nothing else.

The token a Microsoft Entra credential fetches is cached by the credential, which is what it exists to do, so the
credential is built once per endpoint. One consequence is worth stating: rotating the client secret of a registered
application takes effect at the next restart, while rotating a provider key takes effect on the next call.

## What a failing call is classified as

A provider that fails says six different things, and collapsing them into "the call failed" gets the next two
decisions wrong. A rate limit answered with an immediate retry is how an account gets throttled harder; a refused
credential repeated is how the same refusal is bought again while the account carries the requests.

| Classification | What it means | Repeated? |
| --- | --- | --- |
| Credential rejected | The endpoint refused the credential presented | No — rotate or correct it |
| Rate limited | The deployment is over its allowed rate | Yes, after a backoff |
| Request timed out | The endpoint did not answer within the configured time | Yes |
| Transport faulted | The request never reached an answer | Yes |
| Request refused | The endpoint rejected the request itself — a model it does not serve, an input beyond what it accepts | No — correct the declaration |
| Vector shape unexpected | The answer is not in the declared space: a width nothing declared, a count that does not match the passages, a component that is not a finite number | No |

A caller's own cancellation and a host shutdown are absent from that table on purpose. Both are this system's own
decision rather than a remote party's answer, so reporting one as a provider failure would let it open a circuit
against a healthy endpoint.

Falling through the chain follows the same reasoning but answers a different question. An unreachable, throttled,
slow, or credential-refusing endpoint says nothing about the next one, which is a different address with a different
credential, so the chain continues. So does an endpoint the resilience pipeline declined to call at all — a circuit it
opened after repeated failures, or a concurrency budget already spent — because an unavailable first endpoint is
precisely the condition a fallback exists for, and ending the request there would make the whole chain unusable for as
long as its first entry stayed broken. An answer of the wrong shape is not: every endpoint declares the same geometry,
so a width nothing declared means the declaration is wrong and asking the next endpoint would buy a second paid call
to learn the same thing.

A vector whose length does not match the profile's dimension is therefore a failure at the adapter, named as such,
rather than a row the database rejects later with no provider in sight.

## Bounds every call carries

- **A batch bound.** `MaxPassagesPerRequest` is applied before the provider sees a request, and a caller reads the
  same number from the port so it can cut its own work to it. A batch beyond it is refused rather than split, because
  splitting would spend the caller's budget on a number of requests it never chose.
- **A per-passage bound.** `InputCharacterLimit` is what a passage is cut to. It is deliberately not a second setting
  beside the profile's: what the model sees decides what a vector means, so a rule able to cut a passage differently
  from the one the profile records would produce vectors in a space nothing declared.
- **An explicit timeout.** `RequestTimeout` bounds one request to one endpoint, applied by MailFathom rather than left
  to whatever the provider library defaults to. A deadline that expires is reported as a timeout rather than as a
  cancellation, because a cancellation would tell the pipeline that this system stopped the work.
- **One retry layer.** The call runs under the `AiProviderInvocation` resilience pipeline, and both the provider
  library's own retry policy and the standard HTTP resilience handler are switched off for it. [Outbound
  resilience](../architecture/outbound-resilience.md#the-single-layer-rule) holds the rule and why two layers multiply
  rather than add.
- **A bounded response.** The transport refuses a body larger than the declared geometry could fill, and refuses
  redirects — a moved endpoint answering with one would carry the key or the bearer token to whatever host it named.

## What an instance is willing to spend

The bounds above are about one call. Three more are about the deployment, and they exist because embedding is the first
thing MailFathom does that costs money per unit of mail: a runaway loop somewhere else costs CPU, and a runaway
embedding loop is an invoice that arrives a month later. Each is configuration, each is validated at startup, and none
of them is part of an embedding profile — they decide how many vectors exist, never what any vector means, so moving
one leaves every stored vector exactly as comparable as it was.

- **What one message may cost.** `MaxCharactersPerEmail` is how much of a message's extracted text is cut into
  passages. Raw MIME is bounded in megabytes, so one message can carry more text than an ordinary mailbox does in a
  month; beyond this ceiling the message is bounded rather than refused — its opening is embedded and retrievable, and
  [message chunks](message-chunks.md#the-per-message-ceiling) records what the cut left out, on the message.
- **How fast requests may go out.** `MaxRequestsPerMinute` spaces requests so a deployment never sends faster than it
  declared. It paces nothing by default, and it is for a provider whose quota is stated per minute: being refused for
  exceeding one costs an attempt, a retry, and a place in a circuit-breaker window other work is measured in. A caller
  takes the next free slot and waits for it; nothing polls and nothing spins.
- **What one period may cost.** `MaxInputCharactersPerPeriod` and `SpendPeriod` are the aggregate ceiling, counted in
  the characters actually sent to a provider — the one quantity a price is approximately proportional to that this
  deployment can count exactly without carrying a model's own tokenizer. The period is a fixed window anchored at the
  Unix epoch, so every restart agrees on where one begins without anything being stored to say so, and what each period
  has spent is kept in the database rather than in memory: a process crashing and restarting in a loop would otherwise
  begin every period again from zero.

Reaching the aggregate ceiling pauses embedding until the period rolls over, and nothing is lost by the pause: a
passage with no vector is exactly the condition the [backfill](embedding-backfill.md) selects on. The wait is the
roll-over rather than an interval, so work resumes without anybody acting. [Automatic
embedding](automatic-embedding.md#what-a-reached-ceiling-does) describes what the pause looks like from the worker.

The ceiling binds to within one batch. A batch is admitted whenever anything at all is left in the period and is then
paid for whole, because weighing a batch against what remains would stall a deployment whose ceiling is smaller than one
batch for ever — it would refuse the same request at every roll-over. The overshoot is therefore at most one batch per
call in flight.

**Concurrency is not one of these keys.** How many provider calls may be in flight at once is
`Resilience:AiProviderInvocation:ConcurrencyLimit`, which is the one mechanism that owns that question; [outbound
resilience](../architecture/outbound-resilience.md) holds it. A second limiter counting in-flight calls here would make
two settings answer for one behaviour.

## What never reaches a log

No passage, no vector, no credential, and no provider response body. A provider's own error text quotes the request
that produced it, and the request is mail text; the classification, the endpoint alias, and the counts are what a log
record carries instead. Vectors inherit the classification of the mail they derive from and are not treated as
anonymous.

## Proving it without spending

Almost everything downstream of this boundary is provable at zero provider cost, and a deterministic in-repository
generator is what makes that true. It derives a vector from the text alone — reproducible, of the declared width, and
of unit length — so the schema, the worker, the backfill, and the generation switch are all testable against a real
database and no provider at all. Its profile names a provider of its own, so a deployment that activated it by
accident is visible in the profile row rather than in the quality of its search results.

What only a real provider can establish is much smaller: that the adapter speaks the protocol, authenticates,
classifies a real refusal, and returns the width the profile claims. Those tests exist, and they are skipped unless
somebody asks — the `Integration tests` workflow turns them on through an input that defaults to off. Asking for them
without a credential configured fails the run rather than skipping, because a run somebody requested and which then
quietly proved nothing is worse than one that never started.
