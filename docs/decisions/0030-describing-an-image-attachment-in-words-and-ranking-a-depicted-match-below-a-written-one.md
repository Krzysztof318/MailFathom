---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-05
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Describe an image attachment in words the profile already embeds, open no second vector space, and let a depicted match join a result only under everything somebody wrote

<!-- describes: backend/src/AI/Chunking/**, backend/src/AI/Embeddings/**, backend/src/AI/Retrieval/**, backend/src/Application/Emails/Chunking/**, backend/src/Application/Emails/Search/**, backend/src/Application/Emails/SearchEmails/**, backend/src/Application/Emails/BrowseSearch/**, backend/src/Host/Configuration/Embeddings/** -->

## Context and Problem Statement

Nothing between MIME parsing and the embedding pipeline looks past an attachment's headers today, and issue 1562 is the family that changes it. [ADR 0029](0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md), the family's other Wave 1 decision, has since settled what an embedding is derived from: message text always, attachment *text* where a deployment turns it on, both one kind of passage in one vector space, distinguished by which text the passage is a span of. An image has no text to join by that route. A photograph, a screenshot, or a scanned page with no text layer yields nothing to extract, and reaches the pipeline only if something first turns what it shows into something a vector can represent — which is the fork this record takes.

Two shapes are available. A vision-capable chat provider can write down what an image shows, and that text then flows through the extraction-to-chunk-to-embedding pipeline [ADR 0006](0006-embedding-profile-identity-lifecycle-and-activation-cost.md), issue 425, and ADR 0029 already built. Or a dedicated multimodal embedding model can place the image directly into a vector space of its own, with its own profile, its own dimension, and its own provider requirement.

The other half of the question is a product requirement issue 1562 states outright rather than a preference discovered while building: a match derived from a picture must never be shown ahead of a match found in something somebody actually wrote. A mailbox owner searching for words expects word matches first, and a picture that happens to score well against a query vector is corroborating evidence rather than the headline. Whichever mechanism produces the vector, the ranking has to bound it — and the bound's shape depends on the mechanism, because a description embedded under the ordinary profile competes inside the one ranked list every other passage competes in, while a separate multimodal profile would be a separate list a fusion step would have to combine on its own terms.

Recorded on issue 1549, under the parent issue 1562. Issue 1551 is the description step written against whichever mechanism this record names, issue 1555 chunks and embeds what it produces, issues 1557 and 1559 are the retrieval surfaces that implement the ranking rule, and issue 1561 bounds and reports what any of it spends.

## Decision Drivers

- **The requirement is a floor, not a preference.** "Never shown ahead of" is a statement about order that has to hold on every ranked surface a mailbox is searched through, whatever the raw numbers say on any particular query.
- **Comparability is a property of the vector space.** ADR 0006 already settled that a stored vector means something only inside the profile that produced it, and that retrieval reads exactly one profile at a time. A second geometry is a second everything: a second index, a second activation, a second ceiling, and a fusion step between two lists that share no scale.
- **One concept, one mechanism.** ADR 0006 refused a generation counter beside a profile identifier on exactly this ground, and the same argument reaches a second vector-space kind introduced for one attachment format when a cheaper route reaches the same shelf.
- **A second wire protocol is an architectural decision this project has already taken, and refused.** [ADR 0011](0011-reaching-a-provider-outside-the-openai-wire-protocol.md) names Nova Multimodal Embeddings and Marengo among the models answering through neither OpenAI-compatible route, and refuses the AWS SDK, the signing path, and the second `IEmbeddingGenerator` that reaching them would need. Nothing about an image attachment changes that reasoning.
- **`Microsoft.Extensions.AI`'s embedding surface, as this project reaches it, is text in.** `Microsoft.Extensions.AI.OpenAI` supplies `IEmbeddingGenerator<string, Embedding<float>>` over an embeddings endpoint, and it is the one thing `ProviderTextEmbeddingGenerator` speaks to. Its chat surface, by contrast, already carries bytes: an image is `DataContent` in a `ChatMessage`, over the same client, the same resilience pipeline, and the same ceilings every other chat call runs under.
- **A ranking constant that a change of model invalidates is not a mechanism.** `ReciprocalRankFusion` was chosen because a full-text rank and a vector distance are not on one scale and never will be, and its own reasoning refuses weighing them by any number. A penalty applied to a distance is that refused number wearing a different name.
- **Turning a capability on must not re-embed a mailbox.** ADR 0006 made activation the act that spends, and made a chunk-boundary change cost local computation rather than a provider bill. Whichever route is chosen has to keep an operator turning image description on from paying for every message they already embedded.
- **A description can be read; a vector cannot.** Whatever explains why a picture is in a result has to be showable to the person reading it, and has to be quotable by a model composing an answer.
- **An image is bytes a hostile sender fully controls**, and so is anything derived from them. Whatever route is taken inherits the posture issue 1562 puts under the whole family: bounded, loud on failure, and never trusted because it came back from this deployment's own provider.

## Considered Options

Two independent axes, listed apart because an option on one constrains no option on the other.

**A — what produces the vector:**

1. A vision-capable chat provider writes a description; that text is chunked and embedded under the one profile ADR 0029 keeps, exactly like any other attachment-derived text.
2. A dedicated multimodal embedding model embeds the image directly, in a vector space of its own with its own profile, dimension, and ceiling.
3. Both: a description for search relevance now, and a multimodal vector for a future reverse-image or similarity feature.

**B — how a depicted match is kept from outranking a written one:**

1. A fixed rank floor: every result a written passage placed sorts ahead of every result only a picture placed, whatever the raw score says.
2. A score penalty large enough in practice to have the same effect, applied to image-derived chunks before the existing ranking runs.
3. Image-derived chunks are excluded from the default ranked list entirely and surface only through a filter a caller sets explicitly.

## Decision Outcome

Chosen options: **A1** and **B1**.

### A vision-capable chat provider writes the words, and the profile that already exists embeds them

An image attachment becomes searchable by being described in ordinary text — a caption, a transcription of a scanned page, an account of what a chart shows — by a chat provider reached exactly the way every other chat call in this system is reached: the declared endpoint chain, `ProviderChatModelClient`, the resilience pipeline, and the ceilings issue 1561 bounds. What the description costs is one chat call per image, once, and what it produces is text. From that point nothing in the pipeline knows an image was involved except the one column the ranking rule below needs: the description is chunked by the same chunker under the same boundary rules, embedded under the same profile, stored in the same table, and compared with the same distance operator against the same query vector.

That is what makes the marginal engineering cost of reaching an image approximately zero. Every mechanism ADR 0006 built — the immutable identity, the fingerprint, the two-axis attribution, the per-profile index, the two coexisting generations, the bounded removal of a superseded one — applies to a description chunk without a line written for it.

Its own switch, and the same vector space. Whether a deployment describes images at all is a setting of its own, beside the attachment-extraction switch ADR 0029 put under `Embeddings` and off wherever that one is off — a description is an attachment-derived passage, so nothing describes an image on an instance that derives nothing from attachments at all. It is separate from that switch rather than folded into it for the reason ADR 0029 separated extraction from embedding in the first place: the costs are different quantities. Parsing is CPU over bytes already stored; describing is a chat call per image to a provider, which is a spend issue 1561 reports on its own terms and a disclosure an operator may refuse while accepting the other.

What that switch does **not** do is change the geometry. Turning image description on adds passages to the profile that is already active and embeds only those, exactly as ADR 0029's own switch does for extracted text. It is a backfill, not a re-embed and not a new generation, and no vector already stored becomes less comparable because of it. Under a second vector space the same act would be a second profile, a second full build, and a second thing retrieval has to be reading at the moment a person searches.

### No second vector space, and what would make one worth revisiting

Option A2 is refused on three grounds that each hold on their own.

It is a second geometry beside the one ADR 0006 committed to, and ADR 0006 built retrieval to read exactly one profile at a time on purpose. Serving both would mean two ranked lists over the same mailbox with no shared scale, fused by a step that exists nowhere today — and the reason it exists nowhere is `ReciprocalRankFusion`'s: there is no honest constant relating a text-model distance to an image-model distance.

It is a second wire protocol in practice. No provider this project reaches serves image embeddings over the OpenAI-compatible embeddings route it speaks; the multimodal embedding models that exist answer through their own invocation APIs, which is precisely the gap ADR 0011 examined and refused to close in code. Choosing A2 would reopen that decision for a narrower reason than the one that was already found insufficient.

And it explains nothing. A multimodal vector is a point; it cannot be shown to the person asking why a photograph is in their results, and it cannot be quoted by a model composing an answer. A description can be both, which is worth more here than the fidelity the direct route would buy.

Option A3 is refused because the half of it that is not A1 is scaffolding for a feature nobody has asked for. Reverse-image search over a mailbox is not on this project's roadmap, and building a second vector space now against the possibility of one is exactly the shape this repository refuses elsewhere.

What would make A2 worth revisiting is recorded rather than left to be rediscovered, in the same way ADR 0006 recorded `halfvec`: a multimodal embedding model reachable over the OpenAI-compatible embeddings route with no second wire protocol, *and* a feature that needs image-to-image similarity rather than image-to-query relevance. Either alone is not enough. The first without the second is a cheaper way to do something already done adequately; the second without the first is ADR 0011's refusal again.

### A description is never lexically indexed, and that outlives ADR 0029's asymmetry

An image description reaches the vector index and nothing else. It is never added to the `tsvector` a lexical search ranks.

ADR 0029 already leaves the lexical index untouched by attachment text of any kind, and names that asymmetry as the first thing to revisit once retrieval over both exists. So this section is not what keeps a description out today — it is what keeps it out on the day that revision happens. When document text joins the lexical index, a description does not join with it.

Two reasons, and the first is structural. A lexical hit and a written semantic hit are indistinguishable by the time fusion sees them — both are just a rank in a list — so a description admitted to the lexical index would carry a message into the fused result with no property left to tell it apart, and the floor below could not be restored by anything downstream. The rule has to hold at the point content enters an index rather than at the point results leave one.

The second is that a description is nobody's words. A word search that hit a machine's vocabulary would report the mailbox containing a term the mailbox does not contain, and would do it under exactly the affordance — an exact-phrase match — that a person trusts most.

### The floor is a partition of the result, not a weight inside it

Ranking here is per message: `IEmailVectorSearchIndexReader` returns one candidate per message scored by its nearest passage, and `ReciprocalRankFusion` combines that with the lexical ranking by rank alone. The floor is expressed in the same terms, as a partition applied around fusion rather than as a number inside it.

The vector reader produces two rankings out of one eligible set instead of one:

- the **written ranking** — messages ordered by their nearest passage derived from something a person wrote: message text, and a document attachment's own text where ADR 0029's switch admits it;
- the **depicted ranking** — messages ordered by their nearest passage derived from an image description.

The written ranking is what enters reciprocal rank fusion with the lexical ranking, exactly as the single semantic ranking does today, so nothing about fusion changes and no constant is introduced. The depicted ranking is then reduced to the messages the fused result does not already carry, and appended after it whole. The requested limit is applied to that concatenation.

The guarantee that falls out is stronger than "never outranks" and simpler to test: **a picture never improves a message's place, and only ever adds a message that would not have been in the result at all.** A message with both a written passage and a described image appears once, at the place its written passage earned it; its image contributes nothing to that place. A message whose only near passage is a description appears below every message any written passage or any query word reached.

Both rankings come from two bounded scans of the same table under the same filters, which is what the per-profile index already serves; the set difference is done where fusion already lives.

The same ordering governs every surface, and today that is not one place. `ReciprocalRankFusion.Fuse` has two callers: `MailboxSearchReader`, which `search_emails` and `ask_mail`'s retrieval both reach through `MailboxKnowledgeSearch`, and `MailSearchBrowser`, which `/api/client`'s search route reaches directly and which reads both rankings and fuses them itself. So a floor written where fusion happens would be written twice.

It is written once. The partition — producing the two rankings, fusing the written one, and appending what the depicted one adds — is one step both use cases call, rather than a rule each remembers. That is issue 1562's own requirement that `/api/client` reuse the ranking rather than carry a second implementation of it, and issue 1559's that it reuse issue 1557's; this record is naming which step that is rather than adding an obligation. A guarantee that has to be remembered in two places is one that holds in one of them after the next change to the other.

### A message's place is decided by its nearest written passage

Stated separately because it is the part an implementation gets wrong by accident. The written ranking is ordered by the nearest *written* passage, not by the nearest passage of any kind. Ordering by the nearest passage overall and labelling the result afterwards would let a description decide where a message sits while the result claims a body placed it — the floor would appear to hold in the labels and fail in the order.

### A result says the match came from a picture, and a model is told the same

A depicted result is shown as one. `SearchMatchOrigin` already exists because extracts alone cannot explain why a message is in a list, and the same argument applies with more force here: a message whose only claim on the query is a photograph must not appear as an unexplained row. What carries that — a value on that enumeration or a property beside it — belongs to issue 1557 with the rest of the result shape; what this record fixes is that the surface says it.

The description itself is what the result shows, and that is a deliberate departure from a rule already recorded next to it. `IEmailSearchIndexReader` cuts snippets around the query's own words, and a semantically ranked message carrying none of them carries no extract, because returning its opening words would present the start of a body as though it were a match. A description is the opposite case: it is not a body being sampled but the whole of the derived text, and it is the only readable account of why the picture matched. Serving it entire rather than cutting it is also what issue 1555 already establishes for an attachment chunk generally — content a caller may legitimately receive directly, unlike a message chunk — so this is that rule reaching a description rather than a second one invented for it.

A passage handed to a model as context is attributed the same way. `RetrievedMailContextFormatter` already frames every extract structurally as quoted evidence written by a stranger, which is the right frame for mail and the wrong one for a description: nobody wrote it. A model reading it unattributed would report that somebody wrote "a whiteboard showing a roof plan" when nobody did, and would cite a message for it. So a description is presented as a machine's account of an attached picture, distinctly from an extract of what the message says. It is untrusted on top of that — a hostile sender can compose an image whose description is an instruction, and text arriving back from this deployment's own provider is no more trustworthy than the bytes it was derived from.

### What a passage has to record, on top of what ADR 0029 already gives it

ADR 0029 gave a passage the one thing it needed to be cited: which text it is a span of, recorded as the attachment's position in the message's own walk order and absent for a passage cut from the body. That says *which part* the text came out of. It does not say *how the text was obtained*, and the partition above turns on exactly that — a document attachment's text is written by a person and a description of a picture is not, and both hang off an attachment position.

So a passage records one thing more: whether its text was extracted from the part or described from it. It is a column rather than a media type consulted at query time, for the reason the section above gives about the lexical index — a rule that has to survive a scan is not one to re-derive per query from a value that was only ever a parsing hint.

Three details follow, and each of them is ADR 0029's own reasoning applied one step further.

A description hashes under the attachment domain ADR 0029 opened, with the new discriminator among what that domain covers. A body passage's digest is untouched, as it was there; an extraction and a description of the same part can never collide; and a deployment that later changes how it describes re-embeds descriptions alone.

A description is not a span of anything the sender sent. ADR 0029's offset contract — reading the source text from the offset returns the passage character for character — holds because there is a source text to index. An image has none, so the description *is* the text the passage indexes, stored as such. That costs nothing new: issue 1555 already establishes that an attachment passage's text is content a caller may receive directly, which a message passage's is not.

And a description is bounded on ADR 0029's extraction side of the budget, not its embedding side. What embedding is billed for is the characters a description contains, which the existing ceiling already counts correctly. What is new is a chat call per image, which no character count predicts — the same argument ADR 0029 made for parsing, and issue 1561 is where it lands.

### A scanned page that arrives as an image is transcribed, and that is not the recognition ADR 0029 refused

This has to be said outright rather than left to be inferred from the word "transcription" above, because ADR 0029 refused optical character recognition by name and a reader is owed the boundary.

A scanned page that arrives *as an image attachment* is described like any other image, and what a description of it says is largely what it says in words. So this record does close part of the gap ADR 0029 named as its own worst consequence — the scanned letter, which is exactly the document a person most expects to be found. What stays refused is what ADR 0029 actually refused: an optical-character-recognition dependency, with its own licence review and its own price per page, and the parsing of a scanned page found *inside* a document attachment, which stays the case ADR 0029 records as yielding no text.

Two of ADR 0029's three grounds simply do not reach this case. There is no new dependency: the chat provider is one this deployment already declares and already pays for, reached over the client and the pipeline every other chat call uses. There is no unpredictable price per page either — the cost is the one chat call per image the switch and the budget above already account for, whether the picture is a whiteboard or a page of text.

The third ground does reach it, and this record answers it rather than passing over it. ADR 0029 refused recognition partly on "an accuracy floor that would put invented words into a vector space presented as the mailbox's meaning", and a chat model transcribing a page can misread it exactly as a recognition engine can. Three things already decided above are what contain it, and containment rather than accuracy is the honest claim. The transcription is stored as readable text, so a wrong word is visible to the person reading the result rather than buried in a vector nobody can inspect. It is attributed as a machine's account of a picture wherever a person or a model reads it, so nothing presents an invented word as something the sender wrote. And it sits under the floor: a passage the model invented can never lift a message above one somebody actually wrote, and can only add a message that would not have been in the result at all. That is a bound ADR 0029 did not have available when it refused — its extracted attachment text ranks beside body text with nothing holding it down — and it is the reason the same accuracy risk is acceptable here and was not there.

### What sending an image discloses, and what cannot scan it

Describing an image sends the attachment's bytes to a provider, which is a disclosure of mail content as real as sending message text for embedding, and frequently a larger one — a photograph of a document discloses the document.

`SensitiveContentEgressGuard` is the one thing every egress point calls before handing content to somebody else, and it guards *text*: it detects regions in a string and replaces them. It cannot scan an image, and nothing here pretends otherwise by passing bytes through a guard that would return them unchanged. So the outbound leg of describing an image is the one step on this path with no content guard on it, and what governs it is the switch above — an explicit setting an operator sets, never a default that starts sending pictures because a feature shipped. The documentation says that in those words rather than implying a protection that does not exist.

What comes back is text, and from there it inherits ADR 0029's guard chain whole by being an ordinary passage: redacted before it is written, and redacted again in flight where a passage is sent to a hosted embedding endpoint. That inheritance is the argument for A1 restated on the privacy side — a second vector space would have carried image bytes down a path of its own with none of it, and the failure mode of forgetting one is the one ADR 0029 already named.

Everything derived from an image inherits the classification of the mail that carried it, as ADR 0006 already established for vectors and chunks: the description is personal data, it is stored under the same retention rules, and deleting the message, the attachment, or the owner's mail removes it with them. An attachment on a withheld message is never described at all, following the rule issue 1260 already set rather than a second evaluation.

### Consequences

- Good, because an image reaches semantic search through mechanisms that already exist and are already tested — one chunk model, one profile, one index, one distance, one activation lifecycle — so the marginal architecture this feature adds is a column and an ordering rule.
- Good, because turning image description on is a backfill of new chunks rather than a re-embed, and no stored vector loses comparability when an operator changes their mind about pictures.
- Good, because what made a picture match is a sentence a person can read and a model can quote, rather than a distance in a space nobody can inspect.
- Good, because the floor introduces no constant, so a change of embedding model cannot silently invalidate it, and the property it guarantees is directly testable: a described image never moves a message up.
- Good, because ADR 0011's refusal of a second wire protocol is left standing rather than reopened for a narrower case than the one that was already found insufficient.
- Neutral, because a description is a lossy account of a picture. Two visually similar images with different subjects describe differently and a direct multimodal vector would place them closer; for finding mail by what a picture shows that is the correct behavior rather than a shortfall, and it is a real limit for anything image-to-image, which is why the revisit criteria above name that feature specifically.
- Neutral, because a deployment with no chat provider gets no image search. Description is a chat call, so an instance that declared only an embedding chain reaches documents and message text and stops there, which is a configuration to state in the documentation rather than a failure to report at runtime.
- Bad, because a depicted result is only visible when the written results do not fill the requested window. That is what a strict floor means, and it is the price of the guarantee rather than an oversight: a caller-set filter that raises depicted results explicitly is the first thing to revisit if it proves too blunt in use, and it is deliberately not built now.
- Bad, because an image leaving the deployment cannot be scanned the way text leaving it is, so the operator's activation is the whole of the control. Making that the documented, explicit position is the honest answer available; claiming a scan would be the dishonest one.
- Bad, because a transcription of a scanned page can be wrong in a way an extraction cannot, and this record contains that rather than preventing it: the text is readable, it is attributed to a machine, and the floor keeps it from displacing anything written. A misread word still enters the vector space, and somebody reading a depicted result has to check it against the picture.
- Bad, because a description is model output about attacker-controlled bytes, which is a prompt-injection surface that message text does not have in the same shape. It is bounded by treating the text as untrusted everywhere downstream, which is the same posture extracted document text already carries, and by attributing it as a machine's description wherever a model reads it.
- Bad, because two rankings are read where one was read before, so a semantic search over a deployment that describes images pays a second bounded scan. Both are served by the index that already exists, and the alternative — one scan and a label — is the failure mode named above.

## Validation

- Unit tests prove that a message carrying both a written passage and a nearer image-derived passage is ranked at the place its written passage earns, and that removing the image-derived passage does not move it.
- Unit tests prove that a message placed only by an image-derived passage appears after every fused result, and that it is absent when the fused result fills the requested limit.
- Unit tests prove that a message reached by both rankings appears once.
- A test proves that an image description does not reach the `tsvector` a lexical search ranks, so a word occurring only in a description returns no lexical match.
- A unit test proves that adding the extracted-or-described discriminator leaves a body passage's digest byte for byte what it was, and that a described passage and an extracted passage carrying identical text over the same attachment part produce different digests — the same two properties ADR 0029 asserts for its own encoding.
- A test proves that no image is described on a deployment whose attachment extraction is off, whatever the description switch says.
- Unit tests prove that a depicted result carries its description as the extract shown, while a message ranked semantically on written text and carrying none of the query's words still carries none.
- Unit tests over the retrieval context formatter prove that a description is attributed as a machine's account of an attached picture rather than as an extract of what the message says.
- Tests prove the ordering against the shared partition step, and a test per retrieval surface — `search_emails`, `ask_mail`, and `/api/client`'s search route — proves that surface reaches it, which is what `MailSearchBrowser` fusing separately from `MailboxSearchReader` today makes worth asserting rather than assuming.
- A test proves that an attachment on a withheld message produces no description and no chunk.
- The documentation states, in the operator-facing pages that describe provider endpoints and the AI features, that describing images sends attachment bytes to a chat provider, that it is a separate activation, and that no content scan applies to the bytes.
- Issue 1551's own acceptance carries the bounded-decoding and format-allowlist obligations; this record does not restate them, and a review of that work reads them there.

## Pros and Cons of the Options

### A1. A vision-capable chat provider writes a description, embedded under the existing profile

- Good, because it reuses the entire chunk, embedding, profile, and index model at no marginal architectural cost.
- Good, because the artifact is readable, so it can be shown, quoted, guarded, and audited.
- Good, because enabling it is a backfill rather than a re-embed.
- Neutral, because it costs one chat call per image, which is a real spend and is why it is its own activation and its own reported budget.
- Bad, because a description is lossy, and anything wanting image-to-image similarity is not served by it.
- Bad, because it introduces model output derived from attacker-controlled bytes into the index.

### A2. A dedicated multimodal embedding model, in a vector space of its own

- Good, because it is the higher-fidelity representation of what an image actually is, and the only route to image-to-image similarity.
- Good, because it needs no chat provider.
- Bad, because it is a second geometry beside the one ADR 0006 committed to, with a second profile, a second index, a second activation, and a second ceiling.
- Bad, because retrieval would have to fuse two ranked lists with no shared scale, and `ReciprocalRankFusion` exists precisely because no honest constant relates them.
- Bad, because no provider this project reaches serves image embeddings over the OpenAI-compatible embeddings route, so it is the second wire protocol ADR 0011 refused.
- Bad, because a vector explains nothing to a person or to a model, so a result derived from one could not say why it is there.

### A3. Both, with the multimodal profile deferred

- Good, because it names the future feature rather than leaving it unconsidered.
- Neutral, because in what it builds today it is A1 exactly.
- Bad, because the deferred half is either scaffolding for a feature nobody has asked for or a decision taken with none of the evidence a real requirement would supply. Recording the revisit criteria under A1 gets the same value with nothing built.

### B1. A fixed rank floor

- Good, because it states the product requirement directly rather than approximating it, and it holds on every query.
- Good, because it introduces no constant, so no change of embedding model can weaken it.
- Good, because it is a partition around fusion rather than a change inside it, so the fusion reasoning stays untouched.
- Neutral, because it costs a second bounded scan of the vector index.
- Bad, because a depicted result is invisible whenever the written results fill the window.

### B2. A score penalty on image-derived chunks

- Good, because it is one number and needs no second ranking.
- Bad, because it is exactly the constant `ReciprocalRankFusion` was chosen to avoid: a value relating two incomparable scales, silently invalidated by a change of embedding model.
- Bad, because fusion reads rank rather than score, so a penalty on a distance has no predictable effect on the final order at all — it shifts a position within one ranking, and what that does downstream depends on how densely the other candidates sit.
- Bad, because "large enough in practice" is not a guarantee, and the requirement is a guarantee.

### B3. Excluded from the default list, surfaced by an explicit filter

- Good, because a depicted match can then never displace anything, under any window size.
- Good, because it is the least code.
- Bad, because it defeats the requirement it was meant to serve: a mailbox owner wanting a photograph findable "so I don't have to remember it was a picture" would have to remember it was a picture in order to set the filter.
- Bad, because it makes a capability the deployment paid to build reachable only by a caller who already knows it exists.

## More Information

- Issue 1549 is where this decision was recorded, under the parent issue 1562.
- ADR 0006 settles what a profile is, what a vector is attributable to, and why activation is the act that spends. This record adds no axis to it.
- [ADR 0029](0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md) settles that attachment-derived text is one kind of passage in one vector space under a switch of its own, and this record extends that model rather than sitting beside it: one further discriminator on the passage, one further switch beside its extraction switch, one further budget beside its extraction budget, and the ranking rule it does not carry.
- ADR 0011 settles that reaching a provider outside the OpenAI wire protocol is refused in code, which is the ground A2 is refused on.
- **What this does and does not admit of ADR 0029's optical-character-recognition refusal** is decided above, under *A scanned page that arrives as an image is transcribed*: a scan arriving as an image attachment is transcribed and part of ADR 0029's stated gap closes; an OCR dependency and a scan inside a document attachment stay refused. That the two would plausibly reuse one mechanism is noted on issue 1551, and is a reason to keep them one when somebody takes the remaining half of that decision.
- Revisit A2 when both hold at once: a multimodal embedding model reachable over the OpenAI-compatible embeddings route with no second wire protocol, and a feature that needs image-to-image similarity rather than image-to-query relevance.
- Revisit the strictness of the floor if depicted results prove too rarely visible in use. The change is a caller-set filter that raises them explicitly, added beside the floor rather than in place of it.
