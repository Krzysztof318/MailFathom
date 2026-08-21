---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-12
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Reach a cloud platform over its own OpenAI-compatible surface with a bearer credential the deployment is handed, and write neither a second wire protocol nor a token-minting credential shape

<!-- describes: backend/src/AI/ProviderAdapters/**, backend/src/AI/Providers/** -->

## Context and Problem Statement

Everything MailFathom reaches, it reaches through one client construction: the OpenAI wire protocol, a base address, and a credential that is either a provider key or a Microsoft Entra token. Issue 601 asks whether that boundary is ever crossed for a platform that does not fit, and names two candidates on what it takes to be opposite sides of one line — AWS Bedrock a protocol away, Google Vertex AI a credential away.

Both platforms moved after that question was written, and the answer has to be given against what they serve now. Checked on 2026-08-12 against each vendor's own current documentation:

| | An OpenAI-compatible chat route | An OpenAI-compatible embeddings route | What the surface accepts as the credential |
| --- | --- | --- | --- |
| **AWS Bedrock** | Yes — `ChatCompletions` on `bedrock-runtime` and `bedrock-mantle`, `Responses` on `bedrock-mantle` | **No** — every embedding model in the compatibility matrix answers through the invocation APIs alone | A Bedrock API key as `Authorization: Bearer`, or SigV4 |
| **Google Vertex AI** | Yes — `chat/completions` under `endpoints/openapi` | **Unestablished** — an `openapi.embeddings` method is published, documented around a self-deployed model rather than a managed one | A Google Cloud OAuth2 access token as the bearer, service-account tokens lasting one hour by default |

So neither candidate is where the issue placed it. Bedrock's protocol gap has closed for chat and remains open for embeddings; Vertex's credential gap is not a shape at all but a *lifetime*. The question this record has to answer is therefore narrower and more concrete than the one that was asked: what does MailFathom write, and what does an operator arrange, for each of the gaps that are actually left.

## Decision Drivers

- **One client construction is the whole of the reach, and that is what makes a provider a configuration entry rather than a feature request.** `OpenAiCompatibleClientFactory` builds every request of both roles and both chat APIs. A platform reached any other way is a second construction, a second failure classification, and a second thing to keep working.
- **What a key declaration presents is a bearer token.** The pinned client library sends a declared key as `Authorization: Bearer`, and carries no vendor-specific header scheme beside it, so `ApiKey` is not an OpenAI-specific shape that happens to be called a key. Anything a platform accepts in that header is presentable today, whoever issued it and whatever it is named.
- **A key is resolved per request from a secret reference, with no cache to invalidate.** A value rewritten behind an unchanged reference takes effect on the next call and needs no restart. That turns a short-lived platform token from a code shape into an operational arrangement.
- **The embedding endpoint and the chat endpoint are separate declarations with separate aliases and separate credentials.** A platform that serves one role and not the other costs a deployment nothing structurally, because the other role is already free to come from somewhere else.
- **A dependency is inherited by everyone who runs MailFathom.** The product is Apache-2.0, self-hosted, and redistributed as an image operators copy into their own registries, so an SDK taken here is an SDK taken on their behalf.
- **Every provider call sends mail content out of the deployment.** Where the endpoint is, is a privacy decision belonging to the operator, and the mechanism must not narrow it to the platforms this project chose to write code for.
- **The credential set is append-only and cheap to extend later.** A `ProviderEndpointCredentialKind` member is appended with the next value and never renumbered, so refusing a shape now costs nothing that accepting it later would have saved.

## Considered Options

1. **A first-party Bedrock adapter** — the AWS SDK, SigV4 signing inside the credential path, and `InvokeModel` request and response bodies that differ per embedding model family.
2. **A first-party Vertex credential kind** — a Google authentication library minting and refreshing OAuth2 access tokens behind a new member of the credential set.
3. **Reach each platform over its own OpenAI-compatible surface with the bearer credential the operator supplies and refreshes**, and treat what those surfaces do not serve as out of reach.
4. **Refuse both platforms outright**, and name a protocol-translating gateway as the only route to either.

## Decision Outcome

Chosen option: **3**, because after this year's changes the only thing standing between MailFathom and either platform is a value in a header, and MailFathom already resolves that value from a secret reference on every call. Options 1 and 2 would each write code to produce a string that the deployment can be handed instead, and option 4 would tell an operator to run a translating proxy in front of a surface that already speaks the protocol.

### Bedrock chat needs nothing written, and saying so is the deliverable

An operator with a Bedrock account declares a chat endpoint with the region's address, `ApiKey`, and the model identifier as the routed name, and it reaches the same client, the same resilience pipeline, and the same ceilings as any other entry. There is no adapter to accept or refuse here; there was a gap in what this project had checked and written down, and [provider endpoints](../operations/provider-endpoints.md) is where that is repaired.

The credential is the part that needs stating rather than the address. Bedrock issues two kinds, and AWS's own guidance inverts what a static configuration would prefer: a short-term key lasts at most twelve hours and is what AWS recommends for production, while a long-term key lasts until a configured expiry and is documented for exploration only. MailFathom accepts either, because both are strings in a header — but the recommended one expires, which makes this the same arrangement Vertex needs and is why the two end up with one answer instead of two.

### Bedrock embeddings is the one genuine protocol gap, and it is refused

Titan Text Embeddings, Titan G1, Cohere Embed, Nova Multimodal Embeddings, and Marengo each answer through Bedrock's own invocation APIs and through neither OpenAI-compatible route. Reaching them means the AWS SDK, a signing implementation inside a credential path that currently resolves a string, and a per-model request and response body — Titan's and Cohere's are not the same document — behind a second `IEmbeddingGenerator` implementation that shares nothing with the one that exists.

That is a second wire protocol, which the repository already treats as an architectural decision rather than an addition to a table, and it buys access to embedding models whose peers are reachable today over the protocol MailFathom does speak. Cohere's own compatibility API is already a checked entry, and so is most of the register beside it. The deployment that wants its chat from Bedrock keeps it and declares its embedding chain elsewhere, because those were never one declaration.

An operator who specifically needs a Bedrock-hosted embedding model — a data-residency obligation that reaches the vector as well as the answer is the case that actually forces this — runs a gateway that speaks the OpenAI protocol outward and `InvokeModel` inward, and MailFathom reaches the gateway with nothing new in it. That cost is real and it is named rather than waved at: it is a component the operator runs, secures, and debugs, and it sees every passage of mail text on its way past.

### Vertex is a credential lifetime, not a credential shape

Vertex serves chat over the protocol, at one base address per project and location, and the credential is a bearer token — which is what the key declaration already presents. What it is not is durable: a service-account access token lasts an hour, and Google's own pattern for the OpenAI libraries is a refresher that re-reads credentials before each call. Whether a managed embedding model answers on the same surface is left `unestablished` on the register rather than claimed either way, because the method Google publishes there is documented around a model the operator deployed themselves; that is a question one call settles, and it does not change what this record decides.

MailFathom does not need to be that refresher. The value lives behind a secret reference, the reference is resolved per request, and a process beside the deployment that mints a token and rewrites the file is picked up on the next call with no restart and no reload. A `systemd` timer or a `CronJob` writing the same file every forty minutes is the whole of it.

What that arrangement costs is stated plainly, because it is the sharp edge of this decision. A window in which the file holds an expired token is *credential rejected*, which is deliberately not retried — so the refresh interval needs real margin against the hour, and a refresher that dies silently takes the AI features down until somebody notices. Mail synchronization is unaffected, which bounds the blast radius but does not remove it.

Option 2 would have removed exactly that edge, and it is the closest call in this record. What it would have cost is not the code — MailFathom already has a token-fetching credential shape, and a Google one would compose the same way — but the commitment: a second identity provider permanently in the credential path, its library in the register and in the supply chain, and its four-or-more authentication shapes to keep working against a platform this project has no account with. One vendor's token lifetime is not enough to buy that. Two would be a different question, which is what the revisit criteria below say.

### What this record does not say

It does not say Bedrock or Vertex is supported, tested, or recommended. Both entries rest on documentation read on one date, exactly as most of the register does, and the register's own two claims hold here unchanged: presence is a check at a point in time, and absence is not a refusal. It also says nothing about either platform's terms — MailFathom calls neither of them itself, so neither joins the hosted-services review in `THIRD_PARTY_LICENSES.md`, and an operator pointing a deployment at one is making that assessment for themselves.

### Consequences

- Good, because two major clouds become reachable for chat with no code, no dependency, no licence entry, and no new supply-chain surface — the whole change is a decision and the documentation that carries it.
- Good, because the reason it works is a property the mechanism already had rather than one arranged for these two vendors: any platform whose OpenAI-compatible surface takes a bearer token is reachable by the same route, including ones that do not exist yet.
- Good, because refusing now is cheap to reverse. The credential set is append-only, and the client construction is one type, so accepting either adapter later starts from where this record leaves things rather than from something that has to be undone.
- Neutral, because a gateway remains the answer for Bedrock-hosted embeddings, which is what the issue proposed for both platforms and what this record narrows to the one case that still needs it.
- Neutral, because both register entries are **Documented** rather than **Called**. Turning either into the stronger claim needs an account and a paid request, which is the same condition every hosted entry on that page is under.
- Bad, because a deployment on Vertex, or on Bedrock with the recommended short-term key, depends on a refresher this project does not ship, does not supervise, and cannot report the health of. Its failure surfaces as a refused credential on the next AI call and nowhere earlier.
- Bad, because that refresher is one more place a live credential is written to disk, on the operator's own schedule, with whatever permissions their timer runs under.
- Bad, because a Bedrock operator who needs a Bedrock-hosted embedding model gets a component to build. The answer is honest and it is still work this project handed to somebody else.

## Validation

- The client construction is the check: nothing in `backend/src/AI/ProviderAdapters/` is reached with an address or a credential this record does not describe, and a second wire protocol would appear there as a second `IEmbeddingGenerator` or `IChatClient` implementation rather than quietly.
- `ProviderEndpointCredentialKind` naming a member that mints its own tokens for a platform other than Microsoft Entra is the concrete signal this record has been superseded; the enum's own contiguity rule makes such a member visible in a diff.
- The startup rules that already exist carry both entries without amendment: exactly one credential shape per endpoint, an absolute address, and a key refused over plain `http`. Neither platform is a special case in configuration validation, and a change that made one would be a change against this record.
- [Provider endpoints](../operations/provider-endpoints.md) holds the two entries with their evidence and their dates, and an entry that stops being true is a defect against that page rather than against this one.
- No test is added for this record, because it decides what is not written. The provider-contract tests remain the instrument for anything that is, and either entry can be exercised through them against a real account by whoever holds one.

## Pros and Cons of the Options

### A first-party Bedrock adapter

- Good, because it would reach Titan, Cohere, and Nova embeddings directly, which is the one capability this record leaves out of reach.
- Good, because SigV4 would let a deployment authenticate with an instance role and hold no provider secret at all, which is the property that makes the Microsoft Entra shape worth having.
- Neutral, because the AWS SDK is Apache-2.0, so the licence is not what refuses it.
- Bad, because it is a second wire protocol inside a project whose entire provider reach is one client construction, and the request and response bodies differ per model family rather than per platform.
- Bad, because it would put a request signer in a credential path that resolves a string, and the resulting shape belongs to one vendor with no second use in sight.
- Bad, because it buys a class of model that is already reachable over the supported protocol from several checked services.

### A first-party Vertex credential kind

- Good, because it removes the sharpest edge of the chosen option — a token that expires hourly behind a refresher this project neither ships nor supervises.
- Good, because it would let a deployment on Google Cloud authenticate with the workload identity it already has, holding no secret on disk.
- Neutral, because the shape composes with what exists: a token-fetching credential built once per endpoint and cached is exactly how the Microsoft Entra shape already works.
- Bad, because it puts a second identity provider permanently in the credential path, with its own library, its own register entry, and its own set of non-interactive shapes to keep correct against a platform nobody here has an account with.
- Bad, because the benefit is operational hygiene for one vendor rather than reach. Vertex is reachable either way, and Google's models are reachable today with a static key over the Gemini compatibility layer.

### Refusing both platforms outright

- Good, because it is the smallest possible surface and the easiest sentence to write.
- Bad, because it is false as of this year. Telling an operator to run a translating proxy in front of a surface that already speaks the protocol is advice that costs them a component for nothing.
- Bad, because it would leave the register silent about two of the largest platforms an operator might arrive from, and silence there reads as unchecked rather than as refused.

## More Information

- Issue 601 asks the question and sets the condition this record answers it under: the two gaps are answered separately, the rejected direction is recorded beside the chosen one, and no adapter is written here.
- Issue 600 established the register this record writes into, including what **Called** and **Documented** each claim and why absence from the page is not a refusal.
- [ADR 0006](0006-embedding-profile-identity-lifecycle-and-activation-cost.md) is why one client construction serves both providers and both roles, and [ADR 0004](0004-versioning-and-release-policy.md) is why the configuration schema may still be broken deliberately below `1.0.0` should a credential shape need one.
- [Embedding generation § authentication has three shapes](../features/embedding-generation.md#authentication-has-three-shapes) holds the rule that a key is resolved per request, which is the mechanism this record rests the whole arrangement on.
- Revisit when AWS publishes an OpenAI-compatible embeddings route, which would close the remaining gap with no code at all; when Google accepts a durable credential on the `endpoints/openapi` surface, which would remove the refresher; when a *second* platform needs a token minted rather than supplied, at which point the shape is general rather than a favour to one vendor; or when operators report that the external refresher is what actually breaks their deployments, which is evidence this record cannot produce for itself.
