# Provider endpoints, and what each entry rests on

<!-- describes: src/AI/ProviderAdapters/**, src/AI/Embeddings/**, src/AI/Chat/**, tests/IntegrationTests/ProviderAdapters/** -->

MailFathom reaches a model through one client construction that speaks the OpenAI wire protocol, so pointing a
deployment at a service is a configuration entry rather than a feature request. [Embedding generation § an endpoint is
any service that speaks the OpenAI wire
protocol](../features/embedding-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol) holds
the mechanism. What the mechanism cannot supply is the thing an operator actually needs before they write the entry:
whether *this* service, today, serves the route the role needs, at which address, against which credential, and whether
it honours the vector width the declaration asks for.

"It speaks the OpenAI wire protocol" does not answer any of those. A service can serve chat completions and no
embeddings route at all; can serve both and reject the `dimensions` parameter an embedding declaration may send; can
name that parameter something else; can serve the responses API on one model family and not another. This page is the
register of what was checked, entry by entry, with what each check rests on.

## Two claims this page does not make

**Presence is a check at a point in time, not a supported-provider list.** Every entry below is what a third party's own
current documentation said on the date the entry carries, or what a call through this project's own adapter
established. None of these services is under this project's control, and any of them may change the answer next week
without anybody here touching anything. An entry that stops being true is a defect in this page rather than in the
deployment that trusted it.

**Absence is not a refusal.** A service missing from this page is not blocked, unsupported, or known to fail — it is
unchecked. The mechanism reaches any service that speaks the protocol, and an operator who points a deployment at one
is doing an ordinary thing. What they do not get is this page's word for it.

Neither claim is a review of the service's terms, and this page is not the place one is recorded. MailFathom itself
calls two of these — the two its own deployment may be pointed at without an operator choosing anything further — and
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) reviews those
two under *Hosted services*, with what each one retains and what the operator has to hold to send mail text to it. Every
other entry below is named rather than adopted, which is exactly why it has no row there.

**Declaring any of them sends mail content out of the deployment.** An embedding endpoint receives the prepared passage
text of the mail this instance holds; a chat endpoint receives the question a caller asked together with the passages
retrieved to answer it. That is personal data of the operator's own correspondents, and choosing where it goes is the
decision this page exists to inform rather than one it makes. [What leaves your instance when you
ask](../users/usage.md#what-leaves-your-instance-when-you-ask) states it from the reader's side, and a server the
operator runs themselves is the entry that answers it differently.

## What "checked" means here

Each entry says which of two kinds of evidence it rests on, because they are not the same claim:

- **Called** — a request went through MailFathom's own adapter to the real service and the answer was what the port
  publishes. The provider-contract tests are the instrument, and [how to exercise an entry
  yourself](#how-to-exercise-an-entry-yourself) below is how one is run against any address. It carries no date,
  because the tests are run on request rather than on a schedule and the last run is what a pipeline record says rather
  than what this page could.
- **Documented** — the service's own current documentation was read on the date the entry carries, and nothing was
  called. It establishes that the route exists and what it accepts; it does not establish that MailFathom's request is
  the shape that route accepts.

Nothing here is inferred from a model's name, from another service serving the same open-weights model, or from a
compatibility claim on a landing page.

## What an entry has to establish

Five things, because those are what a declaration writes and what a first call fails on:

| What | Why it decides the entry |
| --- | --- |
| Which roles it serves | Embeddings and chat are separate declarations reaching separate routes, and a compatibility layer commonly serves one and not the other |
| The address | `Address` carries the whole base path the service documents, version segment included; empty means the provider library's own default, which is first-party OpenAI |
| The credential shape | Exactly one of `ApiKey`, `EntraCredential`, and `Unauthenticated` is declared, and a credential over a plain `http` address is refused at startup |
| Whether a requested width is honoured | `SupportsRequestedDimension` defaults to `true`, so an endpoint that ignores or rejects `dimensions` needs it written `false` |
| Which chat API it serves | `Chat:Api` names `ChatCompletions` or `Responses`, and a service serving only one refuses the other as *request refused* |

[Configuration reference § `Embeddings`](configuration-reference.md#embeddings) and [§ `Chat`](configuration-reference.md#chat)
are the inventory of every key named here.

## Vendor APIs with a compatibility layer

| Service | Roles | `Address` | Credential | `SupportsRequestedDimension` | Evidence |
| --- | --- | --- | --- | --- | --- |
| [OpenAI](https://platform.openai.com/docs/api-reference/introduction) | embeddings, chat | *(empty — the library's default)* | `ApiKey` | `true` | Called, through the provider-contract tests |
| [Azure OpenAI](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) | embeddings, chat | the resource's own address ending `/openai/v1/` | `ApiKey` or `EntraCredential` | `true` | Called, through the provider-contract tests |
| [Google Gemini](https://ai.google.dev/gemini-api/docs/openai) | embeddings, chat | `https://generativelanguage.googleapis.com/v1beta/openai/` | `ApiKey` | **`false`** | Documented, 2026-08-12 |
| [Mistral](https://docs.mistral.ai/api/) | embeddings, chat | `https://api.mistral.ai/v1` | `ApiKey` | **`false`** | Documented, 2026-08-12 |
| [Cohere](https://docs.cohere.com/docs/compatibility-api) | embeddings, chat | `https://api.cohere.ai/compatibility/v1` | `ApiKey` | **`false`** | Documented, 2026-08-12 |
| [xAI](https://docs.x.ai/docs/api-reference) | chat only | `https://api.x.ai/v1` | `ApiKey` | n/a | Documented, 2026-08-12 |

Azure is not a special case in the code and is not one here: its v1 data plane is OpenAI-compatible, so a deployment is
the same client pointed at the resource with the deployment's own name as `RoutedModelName`. It is the one entry on the
page that takes a Microsoft Entra credential, and [embedding generation § authentication has three
shapes](../features/embedding-generation.md#authentication-has-three-shapes) holds the four non-interactive shapes that
covers.

Cohere is the only vendor entry that refuses the width parameter in writing: its compatibility API lists `dimensions`
among the parameters it does not support. Gemini's compatibility page documents an embeddings request of `model` and
`input` and nothing else. Mistral does accept a requested width, under the name `output_dimension` — which is not the
member MailFathom sends, so the effect is the same and the reason is worth knowing, because a reader comparing the
vendor's page against this one will otherwise think this entry is wrong.

## Aggregators and hosted open models

| Service | Roles | `Address` | Credential | `SupportsRequestedDimension` | Evidence |
| --- | --- | --- | --- | --- | --- |
| [Groq](https://console.groq.com/docs/openai) | chat only | `https://api.groq.com/openai/v1` | `ApiKey` | n/a | Documented, 2026-08-12 |
| [Together AI](https://docs.together.ai/docs/openai-api-compatibility) | embeddings, chat | `https://api.together.ai/v1` | `ApiKey` | **`false`** | Documented, 2026-08-12 |
| [Fireworks AI](https://docs.fireworks.ai/tools-sdks/openai-compatibility) | embeddings, chat | `https://api.fireworks.ai/inference/v1` | `ApiKey` | per model | Documented, 2026-08-12 |
| [DeepInfra](https://docs.deepinfra.com/chat/overview) | embeddings, chat | `https://api.deepinfra.com/v1/openai` | `ApiKey` | unestablished | Documented, 2026-08-12 |
| [Nebius Token Factory](https://docs.tokenfactory.nebius.com/) | embeddings, chat | `https://api.tokenfactory.nebius.com/v1/` | `ApiKey` | unestablished | Documented, 2026-08-12 |
| [OpenRouter](https://openrouter.ai/docs/api_reference/embeddings) | embeddings, chat | `https://openrouter.ai/api/v1` | `ApiKey` | unestablished | Documented, 2026-08-12 |
| [Hugging Face Inference Providers](https://huggingface.co/docs/inference-providers/index) | chat only | `https://router.huggingface.co/v1` | `ApiKey` | n/a | Documented, 2026-08-12 |

The Hugging Face router is the entry most likely to be assumed wrong, so it is worth stating outright: the router serves
embedding models, and its **OpenAI-compatible** surface does not. Its own documentation says the compatible endpoint is
"currently available for chat completion tasks only" and directs every other task, embeddings included, at the Hugging
Face inference clients — which are a different protocol and out of this mechanism's reach.

Fireworks documents `dimensions` as accepted but honoured only by particular models, so the value is a property of the
model an entry names rather than of the service. It also documents `normalize` as defaulting to false, which is a
declaration to check against `NormalizeVectors` rather than to assume.

"Unestablished" is not "no". It means the service's request schema, as published, does not name the parameter and no
call was made to find out. `false` is the safe declaration until a call says otherwise, because an endpoint sent a
parameter it does not accept refuses the request rather than ignoring it.

## A model server you run yourself

These are the entries an operator can exercise without an account or a per-token invoice, and they share one shape: the
OpenAI wire protocol on a private or loopback address over plain HTTP with nothing in front of it. That shape is
declared with the plain address and `"Unauthenticated": true`, and [embedding generation § a model server you run
yourself](../features/embedding-generation.md#a-model-server-you-run-yourself) holds what it gains, what it gives up,
and the startup warning an instance writes about the hop.

| Server | Roles | `Address` shape | Credential | `SupportsRequestedDimension` | Evidence |
| --- | --- | --- | --- | --- | --- |
| [Ollama](https://docs.ollama.com/openai) | embeddings, chat | `http://<host>:11434/v1` | `Unauthenticated` | `true` | Documented, 2026-08-12 |
| [llama.cpp](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md) (`llama-server`) | embeddings, chat | `http://<host>:8080/v1` | `Unauthenticated` | unestablished | Documented, 2026-08-12 |
| [vLLM](https://docs.vllm.ai/en/stable/serving/online_serving/) | embeddings, chat | `http://<host>:8000/v1` | `Unauthenticated` | per model | Documented, 2026-08-12 |
| [LM Studio](https://lmstudio.ai/docs/app/api/endpoints/openai) | embeddings, chat | `http://<host>:1234/v1` | `Unauthenticated` | unestablished | Documented, 2026-08-12 |
| [LocalAI](https://localai.io/docs/features/embeddings/) | embeddings, chat | `http://<host>:8080/v1` | `Unauthenticated` | `true` | Documented, 2026-08-12 |
| [Text Embeddings Inference](https://github.com/huggingface/text-embeddings-inference) | embeddings only | `http://<host>:8080/v1` | `Unauthenticated` | **`false`** | Documented, 2026-08-12 |

Each port above is what the server's own documentation shows and is the part most likely to be wrong in a given
deployment, because every one of these is routinely published on another one. Text Embeddings Inference is the entry
where that gap is already visible: its container listens on `80` and the `docker run` its quick tour shows maps that to
`8080`, so the number in the table is a mapping rather than the server's own default. The address is whatever the
operator's network says it is; the port is recorded so a reader recognises the shape rather than copies it.

Several of these accept an optional key — llama.cpp, vLLM, LM Studio, and Text Embeddings Inference each document a
flag that turns authentication on — and an entry that uses one declares `ApiKey` and an `https` address instead,
because a credential over a plain address is refused at startup. Declaring `Unauthenticated` against a server that does
require a key is a *credential rejected*, which is not repeated. Ollama is the case in between: its compatibility layer
documents a key as required by the client and ignored by the server, so `Unauthenticated` is the correct declaration
and the request carrying no authorization header at all is what the server expects.

vLLM is the one entry where the width parameter fails loudly rather than quietly: it accepts `dimensions` and returns
an error naming the model for one that was not trained for Matryoshka representation, so `SupportsRequestedDimension`
must follow the model an entry names, not the server. Text Embeddings Inference serves the OpenAI-compatible embeddings
route and publishes no width parameter on it at all.

llama.cpp serves the embeddings route only for a model loaded with a pooling type, and a deployment that wants both
roles from it runs two servers with two declarations rather than one — which the chain and the separate `Chat` section
already express.

## A cloud platform whose own API is something else

AWS Bedrock and Google Vertex AI are the two entries an operator most often expects to be absent, because neither
platform's native API is this protocol: Bedrock's own is `InvokeModel` and `Converse` under SigV4, and Vertex's is
`generateContent`. Both nonetheless publish an OpenAI-compatible surface beside it, and on both of them the credential
that surface accepts is a bearer token — which is exactly what an `ApiKey` declaration presents. So each is an ordinary
entry with one operational condition attached rather than a platform out of reach.

| Service | Roles | `Address` | Credential | `SupportsRequestedDimension` | Evidence |
| --- | --- | --- | --- | --- | --- |
| [AWS Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/inference-chat-completions-mantle.html) | **chat only** | `https://bedrock-mantle.<region>.api.aws/v1` | `ApiKey` | n/a | Documented, 2026-08-12 |
| [Google Vertex AI](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/migrate/openai/overview) | chat; embeddings unestablished | `https://<location>-aiplatform.googleapis.com/v1/projects/<project>/locations/<location>/endpoints/openapi` | `ApiKey` | unestablished | Documented, 2026-08-12 |

**The credential on both of these expires, and that is the condition.** Bedrock issues a short-term key lasting at most
twelve hours, which is the kind AWS recommends for production, and a long-term key lasting until a configured expiry,
which its own documentation marks for exploration only. A Vertex bearer is a Google Cloud access token, which for a
service account lasts an hour by default. MailFathom holds either as an ordinary secret reference and resolves it per
request, so a process beside the deployment that mints a fresh token and rewrites the file is picked up on the next
call — no restart, no configuration reload, nothing to invalidate. A `systemd` timer or a Kubernetes `CronJob` writing
the same path is the whole arrangement.

What it costs is worth reading before either entry is declared. A window in which the file holds an expired token is a
*credential rejected*, which is deliberately not retried, and a refresher that dies silently takes the AI features down
until somebody notices — mail synchronization is unaffected, which bounds it but does not remove it. Give the interval
real margin against the lifetime rather than matching it, and treat the refresher as a component of the deployment.

Bedrock's address is the part most likely to be written wrong, because AWS documents two endpoints and its own pages
show more than one base path. `bedrock-mantle` is the one AWS recommends and the only one serving the responses API;
`bedrock-runtime` serves chat completions as well, at a base path its current examples write as `/v1` and its guardrails
example and legacy reference write as `/openai/v1`. Which models each endpoint carries differs too, so the region, the
endpoint, and the routed model identifier are one choice rather than three — take the address from the endpoint the
model is actually on rather than from this row.

**Neither platform is reachable for what its compatible surface does not serve**, and for Bedrock that is embeddings
entirely. Titan Text Embeddings, Titan G1, Cohere Embed, Nova Multimodal Embeddings, and Marengo each answer through
Bedrock's own invocation APIs and through neither OpenAI-compatible route, so a deployment taking chat from Bedrock
declares its embedding chain against one of the entries above — which costs nothing, because the two were always
separate declarations. An operator who specifically needs a Bedrock-hosted embedding model runs a gateway that speaks
this protocol outward and `InvokeModel` inward, and MailFathom reaches the gateway as an ordinary entry. That gateway
sees every passage of mail text on its way past, which makes it a component to secure rather than a translation layer
to forget about.

Vertex's embeddings answer is genuinely open rather than negative, and the distinction is the reason this row says
`unestablished` instead of `chat only`. Google's REST reference does publish an `openapi.embeddings` method beside
`chat.completions`, but it documents that method around a model the operator deployed to an endpoint of their own with
`invokeRoutePrefix` set, while every page written about reaching Google's *managed* models through the OpenAI libraries
is written about chat completions. Whether a managed embedding model answers at `.../endpoints/openapi/embeddings` is
therefore something a call would settle and reading has not.

[ADR 0011](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0011-reaching-a-provider-outside-the-openai-wire-protocol.md)
records why MailFathom writes neither a Bedrock adapter nor a credential shape that mints Google tokens for itself, and
what would change either answer.

## What has no embeddings route at all

Groq, xAI, the Hugging Face router, and AWS Bedrock each serve chat and no OpenAI-compatible embeddings route. That is a
fact about the service rather than a failure, and it costs nothing structurally: the chat endpoint and the embedding
chain are separate declarations with separate aliases and separate credentials, so a deployment may take chat from one
of these and embeddings from an entry above. What it must not do is declare one of them under `Embeddings:Endpoints` and
wait for the first call to say so.

## Which chat API an entry serves

`Chat:Api` defaults to `ChatCompletions`, which every chat-serving entry on this page documents. A `Responses` path is
documented by OpenAI, Azure OpenAI, xAI, Groq (in beta), Ollama, llama.cpp, LM Studio, and vLLM. The Gemini, Mistral,
Cohere, and Vertex AI compatibility layers document none, and a deployment stating `Responses` against one of those is
answered *request refused*. Every other entry above was checked for the embeddings route rather than for this path, so
its answer here is unestablished in the same sense the width column uses.

Bedrock is the entry where the two APIs are two addresses. `bedrock-mantle` documents both paths and `bedrock-runtime`
documents only chat completions, so `Chat:Api` and `Address` are one decision there rather than two independent keys.

Serving the path is not the whole of it. MailFathom conducts every responses call statelessly and asks for the
reasoning content it will hand back on the next turn, for the reasons [chat generation § the responses API is used
statelessly](../features/chat-generation.md#the-responses-api-is-used-statelessly-and-that-is-not-an-option) gives, and
a server implementing part of that surface may accept the path and refuse the request. Ollama, for one, documents its
responses route as serving no stateful request at all — which is what MailFathom asks for anyway, and is why the
distinction is worth reading before an entry is trusted.

The other half is capability rather than protocol. The `ask_mail` run offers the model function tools and the model
calls them when it decides it needs mail, so a model that cannot be given tools cannot answer here whichever API it is
reached over. That surfaces as *request refused* on the first question.

## How to exercise an entry yourself

The provider-contract tests in `tests/IntegrationTests/ProviderAdapters/` are what turn a **Documented** entry into a
**Called** one. They run MailFathom's own adapter against whatever address they are given and assert the four things
only a real service can establish: that the adapter speaks the protocol, authenticates, classifies a real refusal, and
returns an answer or a vector in the shape the port publishes.

They are skipped unless asked for, and asking for one without what it needs fails the run rather than skipping it.
`MAILFATHOM_AI_CONTRACT_TESTS` turns them on; the embedding half then reads `MAILFATHOM_EMBEDDING_ADDRESS`,
`MAILFATHOM_EMBEDDING_MODEL`, `MAILFATHOM_EMBEDDING_DIMENSION`, and `MAILFATHOM_EMBEDDING_API_KEY`, with
`MAILFATHOM_EMBEDDING_ROUTED_MODEL` where the routed name differs, and the chat half reads the corresponding
`MAILFATHOM_CHAT_*` variables. The address is the same string an `Address` key would carry, so an entry is checked
exactly as it would be deployed. [Local development](local-development.md) holds how the suite is run at all.

**One limit of the instrument is worth knowing before an entry is trusted.** A contract run reads the key variable as
required and presents what it reads, so it exercises the `ApiKey` shape and never the `Unauthenticated` one. Against a
server that ignores an authorization header it did not ask for — which is what a self-hosted server started without its
own key flag does — the run still establishes the protocol, the route, and the vector shape, which is what the entry
claims. What it does not establish for that entry is the credential column, and that is why every row in [a model
server you run yourself](#a-model-server-you-run-yourself) reads **Documented**.

A run against a hosted service costs whatever that service charges for the handful of calls the tests make. It is the
only evidence this page treats as stronger than reading, which is why the distinction is on every row rather than in a
footnote.

## What this page deliberately does not carry

**Continuous verification.** Nothing here runs on a schedule or in a pull-request check. A suite that re-checked every
entry would need an account and a budget with each of these services, which is a different decision from writing the
register, and the dates on the rows exist precisely because nothing renews them automatically.

**Anything that does not speak this protocol.** A service reachable only through its own SDK or its own wire format is
out of the mechanism's reach entirely, and pointing a deployment at one is not a configuration entry. Adding a second
protocol is an architectural decision rather than an addition to this table, and
[ADR 0011](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0011-reaching-a-provider-outside-the-openai-wire-protocol.md)
is where that decision was taken and what would reopen it.

**A verdict on quality.** Which model retrieves or answers better is not what any of this establishes. Every entry says
only that the protocol, the route, the credential, and the width behaved as recorded.
