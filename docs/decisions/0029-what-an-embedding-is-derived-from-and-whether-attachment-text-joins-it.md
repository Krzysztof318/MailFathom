---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-05
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Derive an embedding from message text always and from attachment text only where a deployment turns it on, keep both one kind of passage in one vector space distinguished by the text it is a span of, and bound extraction beside the embedding budget rather than inside it

<!-- describes: backend/src/AI/Chunking/**, backend/src/Application/Emails/Chunking/**, backend/src/Application/Emails/Extraction/**, backend/src/Host/Configuration/Embeddings/** -->

## Context and Problem Statement

[ADR 0006](0006-embedding-profile-identity-lifecycle-and-activation-cost.md) settled what an embedding profile is, what a stored vector is attributable to, and what an operator pays at the moment they activate one. It settled nothing about what goes *into* a vector, and left that question open by name.

Today the answer is implied rather than decided. Extraction gives every synchronized message plain text — a genuine `text/plain` part where one exists, a lossy derivation from HTML where it does not — and `ExtractedEmailAttachment` describes each attachment by file name, media type, and decoded size while opening none of them. `DeterministicEmailTextChunker` cuts exactly that text, `EmailChunkEntity` hangs each passage off the message with an offset into it, and [Message chunks](../features/message-chunks.md) closes on the sentence that states the implication: chunking attachment payloads is out of scope, *because extraction never opens them in the first place*.

That implication is load-bearing and is worth deciding rather than inheriting, because an attachment is frequently where the substance of a message is. A contract, an invoice, a scanned letter, an exported table: a mailbox owner asking what was agreed about the roof is usually asking about a PDF, and a semantic search answering only from covering notes reads as broken rather than as bounded.

It is a decision rather than a backlog item because it reaches three things that are already built. It decides whether an attachment-derived passage is the same kind of passage under the same profile or a second kind with its own identity and lifecycle — and `EmailChunkContentHash` is already computed and already has vectors hanging off it, so the shape of that answer decides whether taking it costs a re-embed of every mailbox. It decides whether the cost of parsing, and of optical character recognition where a scan carries no text layer, sits inside the embedding budget [#434](https://github.com/Krzysztof318/MailFathom/issues/434) built or beside it. And it decides how much further the most sensitive part of a mailbox travels to a provider.

## Decision Drivers

- **A profile is a geometry, and attachment text is text.** ADR 0006 made a profile the identity of a vector space — provider, model, dimension, metric — precisely so that every vector attributed to one is comparable with every other. Text lifted out of a PDF is not a different geometry from text lifted out of an HTML body; it is the same words in the same space. A second profile for attachments would create two incomparable spaces and hand retrieval a fusion problem that has no principled answer, in exchange for a separation that is really about consent and money rather than about vectors.
- **The chunk's offsets are already meaningless without knowing which text they index.** `EmailChunkEntity.StartOffset` names a span in the message's extracted text, and the contract on the feature page is that reading that text from the offset returns the passage character for character. An attachment-derived passage is a span in a *different* text. So the row has to say which text it is a span of whatever else is decided — that is not machinery a second kind of chunk buys, it is a fact about a passage that citation needs anyway.
- **The redaction chain already covers a passage, and covers it twice.** [Sensitive-content scanning](../features/sensitive-content-scanning.md) redacts extracted text, chunks, and embeddings before they are written, and redacts every passage sent to a hosted embedding endpoint in flight at the `hosted_embedding_input` site. Attachment text that becomes an ordinary passage inherits both guards with no rule anybody has to remember. Attachment text that travels its own path inherits neither until somebody wires it, and the failure mode of forgetting is a bank statement in a provider's request body.
- **The cost that is genuinely different is extraction, not embedding.** Embedding is priced per character sent, and a character out of a PDF costs exactly what a character out of a body costs — `MaxInputCharactersPerPeriod` already bounds it correctly and would go on doing so. Parsing is the new cost, and it is unlike anything the pipeline does today: a per-attachment CPU and memory cost over a byte stream a stranger composed, in formats with their own parser vulnerabilities, and — if optical character recognition were ever admitted — a per-page cost that no character count predicts. A budget belongs where the cost is.
- **This deployment currently undertakes to parse no attachment type at all, and says so in writing.** The sensitive-content page refuses to screen attachments on exactly that ground: an attachment is a byte stream a caller supplied whose type this deployment does not undertake to parse, and reporting it as covered would be worse than saying it is not. Any answer that opens attachments narrows that undertaking, so the answer has to name what it undertakes rather than opening whatever arrives.
- **The most sensitive content in a mailbox is disproportionately in the attachments**, and sending them to a third party is a larger disclosure than sending a covering note. Root `AGENTS.md` asks for data minimization and purpose limitation to be visible in the architecture. A default that starts shipping payslips to a provider because a feature shipped is not that.
- **Existing hashes are money.** Every stored vector hangs on an `EmailChunkContentHash`, and that digest already covers the boundary rules. A change that alters the digest of an *unchanged body passage* re-cuts every mailbox and re-bills every vector at a provider's rate. Whatever is added must leave the body encoding byte for byte as it is.
- **Nothing extracts attachment text today, so this decision buys a shape rather than a feature.** The tables exist, the migrations are append-only, and the ceilings are configured. What is not yet written is the bounded-cost extraction design, and its scope is precisely this question.

## Considered Options

- **A. Message text only, permanently.** Attachments are described and downloadable and never derived from.
- **B. Message text now, attachment text later as the same kind of passage under one profile**, joining the existing chunk table and the existing embedding budget.
- **C. Attachment text as its own kind**, with its own profile, its own vector space, its own lifecycle, and its own ceiling.

Two axes are decided separately below and are the reason none of the three is taken whole: whether attachment text shares a *vector space* is a different question from whether it shares a *switch and a budget*, and options B and C each bundle both answers together.

## Decision Outcome

Chosen option: **B, with the separability C was really asking for moved off the profile and onto extraction.** Concretely, four decisions:

**1. An embedding is derived from message text always, and from attachment text where a deployment turns it on.** The default is message text alone, so an instance that upgrades into this behaves exactly as it did. Attachment extraction is a configuration switch under `Embeddings`, beside the ceilings that already live there, because ADR 0006 keeps the profile row to identity and lifecycle and puts every limit and every declaration in configuration where a reviewer, a chart, and a `git diff` can see it. The existing per-folder `GenerateEmbeddings: false` continues to exclude a folder's messages whole — attachments included — with no second switch.

**2. An attachment-derived passage is the same kind of passage, in the same table, under one profile and one vector space.** It carries what a passage has always carried plus the one thing it now needs: which text it is a span of. That is the attachment's position in the message's own walk order — the coordinate the download route and the attachment summary already name a part by — absent for a passage cut from the message body. There is no second profile, no second vector table, no second ceiling on what may be sent, and no fusion across spaces at retrieval: an attachment passage and a body passage are ranked against each other by the same distance in the same geometry, which is the only reason a semantic search over both means anything.

**3. The body encoding of `EmailChunkContentHash` does not change, and an attachment passage hashes under a domain of its own.** The digest already opens with `mailfathom.email-chunk.v1`, a version string put there so a later encoding cannot collide with this one. An attachment passage opens with its own domain and appends the attachment's position and the declared media type its text was parsed from; a body passage appends nothing new and keeps the digest it has, byte for byte. So taking this decision re-cuts nothing, orphans no vector, and re-bills no mailbox, and the two kinds of passage still cannot collide. The column that records the source is nullable and additive, which is the only migration shape the schema permits.

**4. Extraction is bounded by its own ceilings and undertakes to parse a declared set of media types, and it performs no optical character recognition.** The ceilings — what may be parsed per attachment, per message, and per account run — sit beside the embedding ceilings in configuration and are counted in what extraction reads rather than in what embedding sends, because those are different quantities and only the second is priced. The undertaking is an allow-list of text-bearing types a deployment names rather than whatever arrives, which narrows the current refusal to parse anything rather than reversing it. An attachment outside the list, one past a ceiling, or one that yields no text is recorded as having yielded none and is not retried into a loop; a scan with no text layer is exactly that case. Optical character recognition is refused here rather than deferred silently: it is a new dependency with its own licence review, a cost per page that no character count predicts, and an accuracy floor that would put invented words into a vector space presented as the mailbox's meaning. Admitting it is a decision of its own, and this record is not it.

### Consequences

- Good, because the substance of a mailbox becomes findable. The invoice, the contract, and the exported table stop being invisible to the one search that was supposed to answer questions about them.
- Good, because attachment text inherits the whole guard chain unchanged — redaction before a write, redaction in flight at `hosted_embedding_input`, the cascade that deletes a passage with its message, the folder switch, and the ban on content in a log, a metric, or a trace. A parallel path would have inherited none of them by construction.
- Good, because nothing an instance already paid for is paid for again. The body digest is untouched, so no existing vector moves.
- Good, because an operator who does not want attachments leaving keeps a default that never sends one, and an operator who does gets a switch that says so in the configuration a chart and a review can read.
- Neutral, because the chunk row grows one nullable column and the hash grows one domain, which is the smallest shape that lets a citation name a span inside a named attachment rather than a message alone.
- Neutral, because a deployment that turns extraction on and embedding off gets attachment passages that are cut and stored and not sent anywhere, which is the same state body passages are already in on an instance with no provider.
- Bad, because this deployment now undertakes to parse formats a stranger composed. That is a real attack surface, and the allow-list, the ceilings, and the refusal to add optical character recognition are what bound it rather than eliminate it; a parser is chosen against its own security record and reviewed as a dependency like any other.
- Bad, because the cost of a message stops being predictable from its text length. A ten-line covering note with a two-hundred-page report attached is now an expensive message, and the per-attachment ceiling is what an operator has to reason about rather than a single number per message.
- Bad, because a passage now has two possible sources and every later feature that reports on passages — the backfill's progress, a retrieval citation, an erasure record — has to say which, or be quietly wrong about one of them.
- Bad, because refusing optical character recognition means the scanned letter, which is exactly the document a person most expects to be found, stays unfindable. That is a stated gap rather than an oversight, and the record above says what would have to be decided to close it.

## Validation

- The decision is validated by review of the extraction work it unblocks, which cannot merge without the switch, the allow-list, and the ceilings this record names, each documented in the configuration reference with its default and what happens when it binds.
- A unit test asserts that a body passage's digest is unchanged by the presence of the attachment encoding — the same rules and the same text produce the digest they produce today — and that a body passage and an attachment passage carrying identical text produce different digests.
- A unit test asserts that an attachment of a type outside the allow-list, one past a ceiling, and one that yields no text each produce no passage and a recorded reason rather than a retry.
- The privacy claims are validated the way every other one on this path is: the tests that assert no passage, digest, or snippet reaches a log, a metric, a trace, or an error message cover attachment passages by covering passages, and the cascade test that deletes a message's passages covers them for the same reason.
- Nothing here is validated by a script. No gate can tell whether a media type was one the deployment meant to undertake.

## Pros and Cons of the Options

### A. Message text only, permanently

- Good, because one kind of passage, one extraction path, and a spend that is predictable from the text a mailbox holds.
- Good, because the deployment goes on parsing nothing a stranger composed, which is the strongest possible position on that attack surface.
- Bad, because semantic search answers from covering notes while the substance sits in a file nobody can find, which reads to a mailbox owner as the feature not working.
- Bad, because it forecloses any later claim that the mailbox is searchable by meaning, and the claim is the reason the vector column exists.

### B. Message text now, attachment text later as the same kind of passage

- Good, because the reader's mental model is right: a message is its text and what came with it, and retrieval fuses them without a second path because there is no second space to fuse across.
- Good, because every guard already written for a passage applies to an attachment passage with no new rule.
- Neutral, because the passage has to record its source either way, which is one column rather than a second model.
- Bad, because extraction becomes a cost per attachment rather than per message, and the embedding budget alone does not model it — which is why this record puts a second budget beside it rather than inside it.
- Bad, on the option as stated in [#478](https://github.com/Krzysztof318/MailFathom/issues/478), because sharing the profile was taken to imply sharing the budget and the switch. It does not, and separating them is what this decision adds to the option.

### C. Attachment text as its own kind, with its own profile and ceiling

- Good, because attachment embedding can be enabled, budgeted, and disabled on its own, and the most sensitive content stays a separate decision from the mail body. This is the half of the option that is right, and decisions 1 and 4 above take it.
- Bad, because a second *profile* buys none of that. A profile is a vector space, and giving attachments their own means body vectors and attachment vectors are no longer comparable, so a single search over a message and its attachment has to fuse two rankings with no shared metric — a hard problem taken on for a separation that configuration already provides.
- Bad, because two chunk kinds with their own lifecycles means two backfills, two re-embed paths, two activation estimates, and two of every retention and erasure guarantee, each of which can be forgotten independently.
- Bad, because it is the most machinery of the three for the least additional guarantee, and the guarantee it does add — that an operator can turn attachments off — is a boolean.

## More Information

- Issue [#478](https://github.com/Krzysztof318/MailFathom/issues/478) records the decision. It is one of the two ADR 0006 left open by name; the other is [#479](https://github.com/Krzysztof318/MailFathom/issues/479), on provider support beyond the two the first release takes, and this record does not touch it.
- [ADR 0006](0006-embedding-profile-identity-lifecycle-and-activation-cost.md) is what this rests on: the profile is a geometry, the boundary rules live in the chunk's own identity rather than in the profile, and the limits live in configuration. All three are upheld here rather than amended — a profile stays a geometry, the source of a passage joins the chunk's identity where the boundary rules already are, and the extraction ceilings join the ones already under `Embeddings`.
- [#425](https://github.com/Krzysztof318/MailFathom/issues/425) built the chunk model and excluded attachment payloads pending exactly this answer; [#434](https://github.com/Krzysztof318/MailFathom/issues/434) built the ceilings this adds a second family beside. Both are delivered, which is why decision 3 exists: the answer had to be one that costs a delivered mailbox nothing.
- [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md) governs what a caller must do before mail leaves, and [ADR 0017](0017-object-storage-content-backend-consistency-and-object-identity.md) the store the raw MIME an attachment is re-derived from is read through. Extraction reads that stored MIME rather than reaching the mail server again, so no part of this reopens a mailbox session.
- **The lexical index is deliberately untouched.** `search_emails` queries a `tsvector` built from one message's extracted text, one document per message, and extending it to attachment text is a separate decision with its own storage cost and its own effect on ranking. An instance that turns attachment extraction on therefore gains attachment text in semantic retrieval and not in lexical search, and that asymmetry is a stated consequence rather than an oversight. It is the first thing to revisit once retrieval over both exists.
- Revisit this decision if optical character recognition becomes worth its cost and its accuracy floor, which is the largest gap it leaves; if a retrieval evaluation shows attachment passages crowding out body passages badly enough that ranking needs to know the difference beyond citing it; or if the lexical asymmetry above turns out to be what people actually notice.
