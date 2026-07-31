# Lexical email search

MailFathom searches its local copy for text. `SearchEmails` is the second read use case: it takes a free-text query plus
the same structured filters a listing takes, and returns a bounded window of matched emails ordered by relevance, each
carrying the summary a listing would show, a relevance rank, and highlighted extracts of the body around what matched.
It reaches no mail server, so a search behaves the same whether or not IMAP is available — and it never touches the
remote `\Seen` flag, because it speaks no mail protocol at all.

This page documents the use case. `MailboxSearchReader` is where every rule below is enforced, so a second entrypoint
cannot reach the query without them; the `search_emails` MCP tool maps protocol arguments onto it and publishes what it
returns, and [MCP tools](mcp-tools.md#search_emails) documents that surface.

## What is searchable, and what is not

The index covers the subject, the normalized participant addresses, and the trimmed body text of each stored message.
[Extracted text and the full-text index](imap-synchronization.md) records how that document is derived.

**Words that appear only inside an attachment payload are not searchable.** Text extraction never opens an attachment, so
a PDF, a spreadsheet, or a scanned image contributes nothing, and a message whose information lives entirely in an
attachment is findable by its subject and its participants alone. That is a deliberate limit rather than an oversight:
attachment extraction is an unbounded-cost path and document parsers are a far larger hostile-input surface than MIME
parsing.

A message the extraction recorded as encrypted has no readable body and is indexed on its subject and participants for
the same reason. It stays distinguishable from a message whose body was genuinely empty.

## The request contract

`SearchEmailsRequest` carries what a caller asked for, unvalidated. `MailboxSearchReader` turns it into a validated
`MailboxEmailSelection` and `EmailSearchQueryText`, so no adapter can reach a query with either one unvalidated.

| Field | Meaning | Absent means |
|---|---|---|
| `QueryText` | The text to search for | refused — see below |
| `AccountIds` | The accounts to search | every account this deployment serves |
| `FolderAliases` | The folder aliases to search | every folder of those accounts |
| `SenderAddress` | The address the sender must carry, in any case | any sender |
| `RecipientAddress` | The address a `To` or `Cc` recipient must carry | any recipient |
| `SubjectFragment` | Text the stored subject must contain, compared without regard to case | any subject |
| `ReceivedOnOrAfter` | Inclusive start of the received range | no start |
| `ReceivedBefore` | Exclusive end of the received range | no end |
| `IsRemotelySeen` | The remote `\Seen` state to require | either state |
| `HasAttachments` | Whether attachments are required | either |
| `ResultLimit` | How many ranked results to return | the default of 20 |

The structured filters are the ones `ListEmails` takes and they mean exactly the same things, because both read models
apply one validated `MailboxEmailSelection` and one SQL predicate.
[Mailbox queries](mailbox-queries.md#what-each-filter-accepts-and-what-it-refuses) documents what each of them accepts
and refuses, including the attachment-presence rule and the account-scope resolution; nothing about them changes here.

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

## What a result carries

`EmailSearchMatch` pairs the `EmailSummary` a listing would show with two values that exist only for the query that
produced them.

- **The relevance rank** is PostgreSQL's `ts_rank` of the message's search vector against the parsed query. It is
  comparable within one result set and means nothing across two.
- **The snippets** are extracts of the body text around the matched words, each matched run wrapped in `**`.

The search vector carries no lexeme weights, so the rank reflects how often and how densely a message's document
mentions the query's words rather than where in the message they appear. A subject match and a body match count the
same.

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

## Ordering, and why there is no cursor

Ranking alone is not a total order: several messages carrying an uncommon word once each score identically, and an
unbroken tie leaves the server free to return either order. The ordering contract therefore appends the timeline key —
received timestamp descending with undated mail last, then the stable local identifier — so two identical requests over
an unchanged index return the same sequence.
[Mailbox queries](mailbox-queries.md#the-order) documents that key, which is the same one a listing pages over.

A search returns a **window**, not a page, and nothing continues it. Relevance order is recomputed per query and moves as
mail is indexed, so a boundary into it would name a position that had stopped meaning what it meant when it was handed
out — unlike a timeline, where a keyset boundary stays valid because the order it names is a property of the data rather
than of the query. The result bound is the control that replaces a cursor, and a caller who needs different mail narrows
the structured filters or writes a different query.

## Empty results, and what they do not reveal

A query that matches nothing returns an empty window rather than a failure, so a search cannot be used to establish that
an account or a folder holds mail the caller was not already entitled to see. An account this deployment does not serve
is still refused with `53001 MailAccountNotAccessible` before anything is read, for the reason a listing refuses one: an
empty result would confirm the identifier.

Every result carries one `MailboxFolderFreshness` entry per folder in scope, exactly as a listing does. Without it a
caller cannot tell a folder that holds nothing matching from one whose synchronization has been failing for a week.

## Where the pieces live

- `MailFathom.Application.Emails.SearchEmails` — the use case, its request, and its result.
- `MailFathom.Application.Emails` — `MailboxEmailSelection`, the validated structural filters both read models share;
  `EmailSearchQueryText`, `EmailSearchResultLimit`, and `EmailSearchSnippetBounds`; `EmailSearchMatch`; and
  `IEmailSearchIndexReader`, the port the adapter implements.
- `MailFathom.Application.Emails.MailboxScopeResolver` — resolves the accounts a read runs against and refuses one this
  deployment does not serve, once, for every read model.
- `MailFathom.Infrastructure.Persistence` — `StoredEmailSearchIndexReader`, which composes the ranking query, and
  `StoredEmailSelectionPredicate`, the filter predicate it shares with the listing read model, and
  `StoredEmailSummaryRow`, the projection and mapping it shares with every other read that publishes a summary.
- `MailFathom.Host.Configuration.MailboxSearchOptions` — the snippet bounds, bound strictly and validated on start.
- `MailFathom.Mcp.Tools` — `SearchEmailsTool`, the protocol adapter, with `SearchEmailsToolResult`, `SearchedEmailMatch`,
  and `EmailRetrievalMode`, the published contract; and `MailboxScopeArguments`, the conversion of caller-supplied text
  into account identifiers and folder aliases that it shares with the listing tool.

## How the guarantees are verified

The three claims this feature rests on are each checked where they are observable.

- **That the query text is a parameter** is asserted against the SQL EF Core generates, in `Infrastructure.UnitTests`. An
  in-memory repository generates no SQL, so a test feeding one metacharacters would pass against a vulnerable adapter and
  prove nothing.
- **That PostgreSQL then treats it as search terms**, that the composed command runs at all, and that the snippets come
  back bounded and marked are asserted against a real database in the integration suite.
- **Everything the use case decides** — the refusals, the window bound, the tie-breaking, the empty-result case — is
  asserted in `Application.UnitTests` against an in-memory index.
