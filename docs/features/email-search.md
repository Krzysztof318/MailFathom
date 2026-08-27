# Email search

<!-- describes: backend/src/Application/Emails/SearchEmails/**, backend/src/Application/Emails/BrowseSearch/**, backend/src/Application/Emails/Search/**, backend/src/Infrastructure/Persistence/** -->

MailFathom searches its local copy. `SearchEmails` is the second read use case: it takes a free-text query plus
the same structured filters a listing takes, and returns a bounded window of matched emails ordered by relevance, each
carrying the summary a listing would show, a relevance rank, and highlighted extracts of the body around what matched.
It reaches no mail server, so a search behaves the same whether or not IMAP is available — and it never touches the
remote `\Seen` flag, because it speaks no mail protocol at all.

How it ranks depends on what the instance can do at the moment of the call: full-text ranking alone, or that ranking
combined with a search by embedding similarity. Every result says which, and [Hybrid retrieval](#hybrid-retrieval)
records what the combination does and what it does not promise. Everything else on this page holds under both.

This page documents the use case. `MailboxSearchReader` is where every rule below is enforced, so a second entrypoint
cannot reach the query without them; the `search_emails` MCP tool maps protocol arguments onto it and publishes what it
returns, and [MCP tools](mcp-tools.md#search_emails) documents that surface.

**A screen searches the same mail through a second use case.** `MailSearchBrowser` composes the same scope, the same
filters, both rankings, the same fusion and the same extracts, and differs in the two things a screen needs and a tool
does not: the results continue past the first window, and each one says which ranking found it.
[Paging a ranking](#paging-a-ranking) is where the first of those is described and
[What a result carries](#what-a-result-carries) the second; the route is
[the client endpoint's](../operations/client-endpoint.md#the-mail-search-route). Everything else on this page holds for
both.

## What is searchable, and what is not

The lexical index covers the subject, the normalized participant addresses, and the trimmed body text of each stored
message. [Extracted text and the full-text index](imap-synchronization.md) records how that document is derived, and
[Message chunks](message-chunks.md) records the passages the vectors hang on.

**Words that appear only inside an attachment payload are not searchable.** Text extraction never opens an attachment, so
a PDF, a spreadsheet, or a scanned image contributes nothing, and a message whose information lives entirely in an
attachment is findable by its subject and its participants alone. That is a deliberate limit rather than an oversight:
attachment extraction is an unbounded-cost path and document parsers are a far larger hostile-input surface than MIME
parsing.

**Where a sensitive-content scanner is switched on, the indexed body text is the redacted text.** Redaction happens as
the message is extracted, so what `search_vector` is generated from is what a reader of a result would see, and a word
inside a redacted region is not in the lexical index at all: no query matches it and no ranking counts it. That is the
protection working rather than a search fault, and it applies to the passages and the vectors built from the same
extraction. [Sensitive-content scanning § derived data is written redacted and
stamped](sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped) records what a switch decides and
what rebuilding an index costs after one moves.

A message the extraction recorded as encrypted has no readable body and is indexed on its subject and participants for
the same reason. It stays distinguishable from a message whose body was genuinely empty.

## The request contract

`SearchEmailsRequest` carries what a caller asked for, unvalidated. `MailboxSearchReader` turns it into a validated
`MailboxEmailSelection` and `EmailSearchQueryText`, so no adapter can reach a query with either one unvalidated.

| Field | Meaning | Absent means |
|---|---|---|
| `QueryText` | The text to search for | refused — see below |
| `Accounts` | The accounts to search, each named by its identifier or by its display name | every account the caller's owner owns |
| `Folders` | The folders to search, each named by its alias or by the role it plays | every folder of those accounts |
| `SenderAddress` | The address the sender must carry, in any case | any sender |
| `RecipientAddress` | The address a `To` or `Cc` recipient must carry | any recipient |
| `SubjectFragment` | Text the stored subject must contain, compared without regard to case | any subject |
| `ReceivedOnOrAfter` | Inclusive start of the received range | no start |
| `ReceivedBefore` | Exclusive end of the received range | no end |
| `IsRemotelySeen` | The remote `\Seen` state to require | either state |
| `IsRemotelyFlagged` | The remote `\Flagged` state to require | either state |
| `Keyword` | One keyword the email must carry, compared without regard to case | any keyword |
| `HasAttachments` | Whether attachments are required | either |
| `IncludeJunkMail` | Whether the account's junk folder takes part | it does not |
| `ResultLimit` | How many ranked results to return | the default of 20 |

The structured filters are the ones `ListEmails` takes and they mean exactly the same things, because both read models
apply one validated `MailboxEmailSelection` and one SQL predicate.
[Mailbox queries](mailbox-queries.md#what-each-filter-accepts-and-what-it-refuses) documents what each of them accepts
and refuses, including the attachment-presence rule and the account-scope resolution; nothing about them changes here.
A folder mapped with `VisibleToTools: false`, and a folder no mapping names at all, are outside a search for the same
reason each is outside a listing, and by the same single decision —
[folders withheld from tools](mailbox-queries.md#folders-withheld-from-tools).
The account's junk folder is left out by the same one decision and lifted by the same caller override, which the
result reports back — [the junk folder, withheld by default](mailbox-queries.md#the-junk-folder-withheld-by-default-and-reachable-on-request).

`SubjectFragment` and `QueryText` are unrelated. The fragment is a structured filter over the stored subject column that
narrows which emails are eligible; the query text is what the eligible ones are matched and ranked against.

### The query text

- **Blank text is refused** with `51002 MailboxQueryFilterInvalid`. A search with no text is a listing, which
  `ListEmails` already answers in a stable order and with a cursor. Answering it here would return an arbitrary
  relevance-ordered window of the whole mailbox in which every result carried a rank of zero.
- **The text is bounded at 512 characters** and a control character is refused, for the reason a subject fragment's is:
  PostgreSQL text cannot hold a zero byte, so a query carrying one would surface as a provider exception rather than as
  the stable failure this boundary publishes.
- **Nothing else is interpreted.** The text reaches PostgreSQL as one parameter and `websearch_to_tsquery` parses it
  there, so quoted phrases, `OR`, and a leading `-` are operators that function understands and every other
  metacharacter is ordinary text. Nothing at any point concatenates the value into SQL.
- **Query text is never logged**, and no failure message repeats it. What somebody is searching their own mailbox for is
  personal data of a particularly revealing kind.

`websearch_to_tsquery` is chosen over `to_tsquery` deliberately: it accepts whatever a person types instead of failing
on an unbalanced bracket, which is what makes "the query is data" true for every input rather than for well-formed ones.

### The result count

A search returns between 1 and 50 results, defaulting to 20. A count outside that range is refused with
`51003 EmailSearchResultLimitOutOfRange` rather than clamped, because a clamped window looks exactly like the window a
caller asked for, and a client that reasoned about the completeness of what came back would be reasoning about a
fiftieth of it.

The maximum is lower than a listing's page size of 100. A ranked result costs more than a listed one — PostgreSQL builds
a highlighted extract per row on top of matching and ranking it — and the bound is also what limits how much mail
content one query can draw out of a mailbox.

One bound covers both readings, because what it bounds is the same thing in both: a client search cuts a page with it
rather than closing a window, and refuses a page size outside the range the same way.

## What a result carries

`EmailSearchMatch` pairs the `EmailSummary` a listing would show with two values that exist only for the query that
produced them, and `SearchEmailsResult` adds the retrieval mode the whole window was ranked by.

- **The relevance rank** is the score of the ranking that produced the window: PostgreSQL's `ts_rank` under lexical
  retrieval, a fused rank score under hybrid. It is comparable within one result set and means nothing across two, and
  the two scales are unrelated — reading it without reading the mode says nothing.
- **The snippets** are extracts of the body text around the matched words, each matched run wrapped in `**`.
- **The retrieval mode** is `Lexical` or `Hybrid`, and it describes this one call rather than the deployment.
- **The semantic capability** is `Inactive`, `Available`, or `Degraded`, and it describes the instance. It is what
  separates a lexical answer that is exactly what the deployment intends from one that is narrower than intended; the
  section below states what each value means.
- **Whether junk mail took part** says which of the two searches the caller got, so an absent result is never
  ambiguous between missing and withheld.

A client search publishes the same result with two differences, both following from who reads it. It adds **which
ranking found the message** — `LexicalRanking`, `SemanticRanking`, or `BothRankings` — and the bounded **preview** the
mail list route publishes, so a result row is drawn from one request and a semantically ranked message, which carries no
extract because there is no part of it that shows the query's words, still has something to show and a word for why it
is in the list. It publishes **no relevance rank at all**: a number on a row invites a comparison between two searches
that no ranking supports, and the order of the results is what the ranking has to say. Freshness is absent for the same
kind of reason — a screen reads it once from
[the folders route](../operations/client-endpoint.md#the-folders-route) rather than on every page.

The subject, the snippets, and the sender's display name are what a result carries that a message's author wrote, so
where a sensitive-content scanner is switched on all three are redacted before the window is served, each value scanned
on its own rather than as one composed result. The display name is scanned rather than treated as part of the address it
accompanies, because an address is a routing identity a server issued while the name in front of it is free text the
sending side wrote. A client search scans the preview beside those three and reports under a point of its own, because
what crosses there is chosen by the query rather than by where a message sits in a folder. A scanner that cannot answer
refuses the search. Both switches are off by default,
and nothing on this path is scanned then. [Sensitive-content scanning § the guarded egress
points](sensitive-content-scanning.md#the-guarded-egress-points) holds the contract; that redaction leaves the ranking
alone, because it happens after the query has run over the stored index. What the index itself holds is the earlier
question the section above answers.

The search vector carries no lexeme weights, so the lexical rank reflects how often and how densely a message's document
mentions the query's words rather than where in the message they appear. A subject match and a body match count the
same.

A message that exists in two folders — because the mailbox owner copied it, or because MailFathom did — is **two
results**, one per folder, ranked independently and each naming where it was found. Nothing collapses them, because
nothing joins them: a stored row is one occurrence, which is the decision
[ADR 0008](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0008-copied-message-local-identity.md)
records. A query scoped to one folder returns one.

## Hybrid retrieval

An instance that has activated an embedding profile ranks twice and combines the two orderings.
[Embedding generation](embedding-generation.md) records what activating a profile means and
[Automatic embedding](automatic-embedding.md) what fills the vectors.

- **Lexical ranking** is the full-text ranking above, unchanged.
- **Semantic ranking** embeds the query text through the active profile's generator and orders the eligible mail by how
  near its nearest embedded passage sits, under that profile's own distance metric. A message with no vector under the
  active profile is absent from this ranking rather than ranked as distant.
- **Fusion** combines the two by Reciprocal Rank Fusion: a message scores `1 / (60 + rank)` in each ranking that
  returned it, counting from one, and the fused score is the sum. Each ranking is asked for four times the window being
  returned, so agreement between them can be observed at all rather than only inside the window.

The method reads **where** each ranking placed a message and never **what** it scored it. That is the point: a full-text
rank and a vector distance are not on one scale and never will be, so any weighted combination of the two numbers would
be a constant that a change of embedding model silently invalidates. Fusion by rank asks nothing of the numbers, which
is why changing the model changes what is found and never how the two findings are weighed. `60` is the published
constant from the method's own paper and is not configurable, because a deployment-tunable value would be a second way
for two instances to disagree about what "most relevant" means while both reported the same mode.

### What it does and does not promise

- **A message can rank without carrying any of the query's words.** That is the feature: mail about water damage is
  found by a search for a roof leak.
- **A message both rankings found outranks one only one of them did.** An exact phrase that was already ranking first
  keeps ranking first; nothing displaces it in favour of a merely related message.
- **The structured filters apply before either ranking**, so a message outside the caller's scope never takes part in
  the fusion and never influences the order of a message that is inside it.
- **Nothing is re-ranked, rewritten, or expanded.** No chat model is reachable from this path at all: the query is
  embedded and compared, never interpreted.
- **A search says which mode answered it, on every call.** An instance configured for hybrid retrieval answers
  `Lexical` when its embedding provider is unreachable, when it has activated no profile, or when the configured
  generator disagrees with the active profile's identity. None of those is an error: a provider outage costs a search
  its second ranking rather than turning a mailbox read into a failure, and the mode is what makes that visible instead
  of silent.
- **A search also says whether it could have answered hybridly at all**, which the mode alone cannot. The capability
  below is what distinguishes the three cases in that sentence.
- **Semantic recall follows the backfill.** A mailbox that is still being embedded reports `Hybrid` and finds only what
  already carries vectors. [Embedding backfill](embedding-backfill.md) records how that catches up.
- **The vector ranking is exact rather than approximate**, because the caller's filters join it and an approximate index
  scan cannot carry a filter on a joined table. No vector index is built at all, so the fused order is deterministic —
  the property the whole method rests on. [What a semantic search
  costs](../architecture/semantic-ranking-cost.md) states what exactness costs on a mailbox at the scale an owner
  reaches, measured, what a caller's own filters take off that, and what an approximate path was found to cost.
- **The query is prepared exactly as a passage is.** A profile's `PassageInstruction` is applied to the query text too,
  because the preparation is part of the profile's identity and a search that prepared its query differently would be
  measuring against a space the stored vectors do not belong to. A model that asks for a different prefix on a query
  than on a passage therefore gets the passage one; configure such a model's profile with the instruction that suits
  both, or with none.
- **Nothing about a query is recorded.** The query text is not logged, the vector it produces is held for one call and
  published to nobody, and no snippet reaches a log, a metric, or a trace.

### What the three capability states mean

The retrieval mode answers *how was this call ranked*. It cannot answer *should it have been*, and that is the question
an operator whose API key expired is actually asking: their instance still answers every search, just with a worse
ranking, and nothing about a `Lexical` mode says whether that is the deployment working as configured. The capability is
that second answer, and it is part of every result.

| Capability | What is true | What an operator does |
|---|---|---|
| `Inactive` | No embedding profile is active. This instance does not embed mail at all | Nothing. It is a supported deployment. Activating a declared profile is what changes it |
| `Available` | A profile is active, a generator declares the same identity, and the provider answered the last call made to it | Nothing |
| `Degraded` | A profile is active and a query cannot be placed in its space | Restore the credential, the endpoint, or the model declaration. Recovery is automatic afterwards |

An instance reaches `Degraded` in four ways, and every one of them needs a person rather than time alone:

- the last embedding call was refused for a reason no later attempt changes — an expired or revoked credential, a
  rejected request;
- the last embedding call failed for a reason that may pass — the whole endpoint chain unreachable, a rate limit, a
  timeout. [ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
  makes the chain one vector space reached several ways, so falling from one endpoint to the next is the declaration
  working rather than ill health; only the chain being exhausted is;
- no endpoint chain is declared any more while a profile is still active, so vectors exist and nothing can place a query
  beside them;
- the declared model is not the one the active profile records, which is the same disagreement the embedding workers
  report and refuse to write under.

Two properties make the signal cheap enough to consult on every search.

**No query establishes health.** What the capability reads is the outcome of calls the embedding worker and the backfill
already made and paid for. A health check that called the provider would spend an operator's money on every scrape and
would report on a request nobody asked for.

**A failing provider is not asked again by every search.** While the recorded failure is less than a minute old, a query
reports `Degraded` and returns the lexical ranking without opening a connection. That is what keeps an outage from
turning every arriving search into one more call against a provider that is already saying no. A rejected credential in
particular is not a transient failure, so nothing in the resilience budget stops it from being retried — without this
gate, an expired key would buy one refused request per search for as long as it stayed expired.

**Recovery needs no restart and no operator action beyond the fix itself**, and it does not depend on there being mail
left to embed. The workers restore the state as a by-product of work they had to do anyway, which covers an instance
that is still catching up — but an instance whose mail is fully embedded and whose mailbox is quiet calls the provider
for nothing at all, and a gate that trusted the recorded state unconditionally would leave it lexical indefinitely after
the cause was gone. So the gate is a window rather than a latch: once a minute has passed with no fresher observation,
one search is let through to find out. A provider still refusing costs that one call and re-stamps the window; a
repaired one is picked up on it, and every search after it is `Hybrid` again.

That minute is a fixed constant rather than a setting, for the reason the fusion depth is: its whole range sits between
*immediately* and *within a minute*, so there is nothing for an operator to gain by tuning it and one more number to
reason about if they could.

Every transition is logged with the provider role and the classification, at `Warning` when a capability is withdrawn
and at `Information` when it returns. Only transitions are logged — every provider call records a state, so a line per
call would put the log's volume on the size of the mailbox — and a first call that succeeded is not one, because it
restored nothing. `mailfathom.ai.provider.health` publishes the same state as
a gauge per role, and the `ai-embedding-provider` health check reports it on the readiness probe as `Degraded`, never
worse: an instance whose embedding provider is down still answers every search, and taking it out of traffic for that
would be a worse outage than the one being reported. [Health endpoints](../operations/health-endpoints.md) documents the
probe and [Telemetry](../operations/telemetry.md) the instrument.

### Snippets

Snippets are the data-minimization boundary of the whole use case. A result publishes several bounded extracts, never
the body they were cut from, and no result ever carries raw MIME or attachment bytes.

- The extracts are cut by PostgreSQL's `ts_headline`, so **the body text never leaves the database**: the query projects
  the headline, and the column holding the body is not part of any result set that crosses the persistence boundary.
- **They are drawn from the body only.** A message that matched on its subject or a participant address carries no
  snippets, because the summary already publishes the subject and the sender whole. A fragment `ts_headline` returned
  without a highlight marker is discarded for the same reason — it would be the opening words of a message body
  presented as though they were what matched.
- **A message with no indexed body text carries no snippets at all**, which is the encrypted and attachment-only case
  above.
- `**` is the only markup MailFathom adds. A snippet is text cut from untrusted mail, so it is handed back as text rather
  than wrapped in HTML.

Whether a fragment was highlighted is decided before that markup exists. PostgreSQL is asked to mark matches with two
control characters, and the indexed body provably cannot contain either — text extraction drops every control character
except the tab and the newline — so a body of its own writing `**`, which Markdown mail routinely does, can never be
mistaken for a match. The control markers are replaced by `**` only after the fragment has been recognized.

The bounds are deployment configuration under `MailboxSearch`, validated at startup:

| Setting | Meaning | Default | Range |
|---|---|---|---|
| `SnippetsPerEmail` | How many extracts one result may carry | 3 | 1–10 |
| `WordsPerSnippet` | How many words one extract may carry | 24 | 4–100 |

They are configuration rather than request input because a caller who could raise them could lift the control, and the
useful values follow from how a deployment's mail is written rather than from what any single request wants. Both are
applied twice here: once in the option list PostgreSQL cuts the extracts by, and again on what comes back, because the
bound is the privacy control and a result must not depend on the server having honored it. The protocol boundary applies
the count a third time for the same reason one level up, and applies a ceiling derived from the character bound rather
than the bound itself; [MCP tools](mcp-tools.md#what-the-boundary-bounds-and-why-it-bounds-it-again) records why the
exact count cannot be repeated once the markers are `**`.

A third bound is derived rather than configured. `WordsPerSnippet` counts words, and a word is whatever lies between two
spaces, so a message carrying words far longer than prose writes — a URL, a base64 blob, a hash — would satisfy a limit
of a few words while publishing most of its body. Each extract therefore carries at most `WordsPerSnippet × 64`
characters **of the message**, and is marked with `…` when that cut applies. The `**` markers do not count against it:
they are MailFathom's own, and counting them would make the same setting show less of a message the better the query
matched it. The shortest extract `ts_headline` may return is derived the same way, so no pair of configured numbers can
produce an option list PostgreSQL rejects.

**Changing either setting requires a restart.** The bounds are read once at startup and published as a single value for
the process, so editing `MailboxSearch` in a running host reloads the configuration file but leaves every search
applying the bounds the host started with. This is deliberate — the value is a deployment-wide privacy control rather
than a per-request preference — but it means an operator who tightens a bound and does not restart is not protected by
the number they just wrote. Restart the host to adopt it.

## Ordering, and what a cursor into a ranking means

Ranking alone is not a total order: several messages carrying an uncommon word once each score identically, and an
unbroken tie leaves the server free to return either order. The ordering contract therefore appends the timeline key —
received timestamp descending with undated mail last, then the stable local identifier — so two identical requests over
an unchanged index return the same sequence.
[Mailbox queries](mailbox-queries.md#the-order) documents that key, which is the same one a listing pages over.

The same key settles a fused tie, and there ties are not rare: two messages at symmetric places — first lexically and
fifth semantically against fifth and first — score identically by construction. Both rankings the fusion consumes are
ordered by that key too, so one search applies one tiebreaker rather than three that happen to agree.

**A tool search returns a window, not a page, and nothing continues it.** A model asks once and takes the ranked answer,
so the result bound is the whole control and a caller who needs different mail narrows the structured filters or writes a
different query.

### Paging a ranking

A screen is the other case, and it pages. What makes that sound is the bound rather than a promise the ranking cannot
keep: `MailSearchBrowser` ranks **one list of at most 200 results** and every page of a search ranks the same list to
the same depth, so the sequence a client walks is one sequence rather than a series of differently-deep re-rankings.
Both rankings reach that depth, which keeps agreement between them observable as far down as paging can go, and the
cost of a page is the same wherever in the list it is asked for — 200 candidates per side is what the tool search
already pays for its deepest window.

Inside that list the boundary is a keyset one: `RankedSearchCursor` names the score and the timeline position the last
result held, and the next page reads what the order places strictly after it. Nothing on the server remembers a cursor,
and a cursor presented against a different query or different filters is refused rather than followed, because a
fingerprint of both travels in it.

What such a boundary cannot promise is what a timeline's promises. Relevance is recomputed per query, so a message
indexed between two pages can move across a boundary a client is holding and be seen twice or not at all; a timeline
boundary stays exact because the order it names is a property of the data rather than of the query. The order here is
still total, so a continuation always advances, and the depth is where every walk ends — a person who has read two
hundred results without finding what they wanted narrows the filters, which is the thing that would have found it
sooner.

Paging runs forward only. There is no backward cursor, because a client keeps the pages it has already drawn and a
backward one would promise a re-read of a list that no longer exists in the form it was read in.

## Empty results, and what they do not reveal

A query that matches nothing returns an empty window rather than a failure, so a search cannot be used to establish that
an account or a folder holds mail the caller was not already entitled to see. A name that reaches none of the accounts
the caller's owner owns is still refused with `53001 MailAccountNotAccessible` before anything is read, for the reason a
listing refuses one: an empty result would confirm the identifier. It is one answer for three cases — nothing carries
that name, this deployment stopped serving the account, or the account is somebody else's — so a refusal separates none
of them.

Every tool result carries one `MailboxFolderFreshness` entry per folder in scope, exactly as a listing does. Without it a
caller cannot tell a folder that holds nothing matching from one whose synchronization has been failing for a week.

## Where the pieces live

- `MailFathom.Application.Emails.SearchEmails` — the tool use case, its request, and its result.
- `MailFathom.Application.Emails.BrowseSearch` — the client use case: `MailSearchBrowser`, `BrowseSearchRequest`, and
  `BrowsedSearchPage`; `RankedSearchList`, which is the bounded list one search pages through and the fingerprint a
  cursor belongs to; `RankedSearchCursor`, the boundary between two pages; and `BrowsedSearchResult` with
  `SearchMatchOrigin`, which is the result and the word for why it is in the list.
- `MailFathom.Application.Emails.Search` — `EmailSearchQueryText`, `EmailSearchResultLimit`, and
  `EmailSearchSnippetBounds`; `EmailSearchMatch`, `RankedEmailCandidate`, and `EmailSearchRetrievalMode`;
  `ReciprocalRankFusion`, which combines two rankings and reaches nothing at all; `SemanticEmailSearch`, which decides
  whether a search can be ranked semantically and embeds the query when it can, together with the
  `SemanticSearchCapability` it reports and the `SemanticEmailSearchOutcome` the two travel in; and
  `IEmailSearchIndexReader` and `IEmailVectorSearchIndexReader`, the two ports the adapters implement.
- `MailFathom.Application.AiProviders` — `IAiProviderHealthReader` and `AiProviderHealthState`, the recorded outcome of
  the last provider call that the capability is read from. `MailFathom.Infrastructure.Observability` holds
  `AiProviderHealthTracker`, which is what records it, publishes the gauge, and logs a transition.
- `MailFathom.Application.Emails.Mailboxes` — `MailboxEmailSelection`, the validated structural filters both read models
  share, and `MailboxScopeResolver`, which resolves the accounts a read runs against and refuses one this deployment does
  not serve, once, for every read model.
- `MailFathom.Infrastructure.Persistence.Emails` — `StoredEmailSearchIndexReader`, which composes the lexical ranking
  query and the extract query, and `StoredEmailSelectionPredicate`, the filter predicate it shares with the listing read
  model, and `StoredEmailSummaryRow`, the projection and mapping it shares with every other read that publishes a
  summary.
- `MailFathom.Infrastructure.Persistence.Embeddings` — `EmailVectorSearchIndexReader`, which composes the vector ranking
  query over the same filter predicate.
- `MailFathom.Host.Configuration.Mail.MailboxSearchOptions` — the snippet bounds, bound strictly and validated on start.
- `MailFathom.Mcp.Tools` — `SearchEmailsTool`, the protocol adapter, and `MailboxScopeArguments`, the conversion of
  caller-supplied text into account identifiers and folder references that it shares with the listing tool.
- `MailFathom.Mcp.Tools.Results` — `SearchEmailsToolResult`, `SearchedEmailMatch`, `EmailRetrievalMode`, and
  `SemanticSearchAvailability`, the published contract.
- `MailFathom.Host.Api.ClientMailSearchEndpoint` — the client route, its refusals, and the response it publishes.

## How the guarantees are verified

Each claim this feature rests on is checked where it is observable.

- **That the query text and the query vector are parameters**, that each ranking narrows by the caller's filters, and
  that the vector ranking measures with the operator its profile's metric names are asserted against the SQL EF Core
  generates, in `Infrastructure.UnitTests`. An in-memory repository generates no SQL, so a test feeding one
  metacharacters would pass against a vulnerable adapter and prove nothing — and a query that fails to translate at all
  fails there rather than at the first search a deployment runs.
- **That PostgreSQL then treats the text as search terms**, that the composed commands run at all, and that the snippets
  come back bounded and marked are asserted against a real database in the integration suite.
- **Everything the use case decides** — the refusals, the window bound, the tie-breaking, the empty-result case, which
  mode answers a call, and how deep each ranking is asked — is asserted in `Application.UnitTests` against in-memory
  indexes.
- **Each of the three capability states, and both transitions between them**, are asserted there too, against a
  substituted health reader: that a provider already recorded as failing is reported degraded without the generator
  being called at all, and that the same instance ranks semantically again once the recorded state says the provider
  answered. That a transition is logged once, with the role and the classification and nothing else, is asserted in
  `Infrastructure.UnitTests` against the captured records.
- **The fusion itself** is asserted against known rank inputs, with no provider and no database anywhere near it: what
  it promises is a function of where two rankings placed a message and of nothing else, so a test that needed vectors to
  state it would be testing something other than the method.
