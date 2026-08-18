# Using the tools

<!-- describes: src/Mcp/Tools/** -->

MailFathom publishes eleven MCP tools, and together they are the whole surface: an agent can see which mailboxes exist,
list mail, read one message, search, ask a question, and keep the deployment's own contact book — nothing else. This
page is the user's view of that surface: what each tool answers, what every result carries, what the deliberate limits
are, and how to read a failure. The full contracts — every argument, every field, every bound — live in
[MCP tools](../features/mcp-tools.md) and the feature pages it links, and this page does not restate them.

Four of the five mailbox tools are always within the deployment's reach. `ask_mail` needs a chat model and an embedding
model configured and working, so a deployment that has neither does not offer it at all; its absence from a tool listing
is that deployment saying it cannot answer questions rather than something being broken.

Which of the ten *you* are offered is a second question, and its answer is the grant on the credential you connected
with. A tool that grant does not permit is absent from the listing, and calling it anyway is answered as though no such
tool existed — nothing names the permission that was missing, so a shorter tool list than this page describes is a
question for whoever configured the deployment:
[what a credential may do](../operations/mcp-endpoint.md#what-a-credential-may-do). A deployment that wrote no grant,
which is the default, offers everything it has, and the six contact tools are part of that everything.

## The model behind every call

A mailbox tool call reads the **local copy** that synchronization maintains. Nothing in a request reaches a mail server,
so a call is fast, works while the server is unreachable, and cannot mark anything as read — locally or remotely. That
holds for the contact tools too, including the three that write: what they change is a table in MailFathom's own
database, and no mail and no mail server is touched by any of them. The price
of that model is freshness, which is why every listing and every search carries `folderFreshness`: one entry per
folder in scope, stating when synchronization last committed progress there, or that it never has. An agent that reads
mail without reading that field will eventually present an empty folder as an empty mailbox.

Results are bounded by design. A listing serves at most 100 summaries per page, a search at most 50 ranked matches, a
body at most the configured character bound, a search extract at most a few dozen words. The bounds are the
deployment's privacy control on how much mail one call can draw out, so they are refused rather than stretched when a
caller asks for more, and none of them is a client argument to widen.

## `list_accounts` — which mailboxes exist

Takes no arguments and returns the mailboxes this deployment serves. Call it first: every other tool narrows by account,
and this is where the names to narrow with come from. Each entry carries two of them — the `accountId` an operator
configured and the `displayName` they gave it — and **either one names the account** in a later call, the display name
matched without regard to case. Quote the display name to a person; the identifier is what other results report and what
stays stable if the readable name is changed.

Each account also lists its folders with the same freshness statement a listing carries, and the result says whether
synchronization is running at all. An account with no folders listed has never been synchronized, which is a different
thing from a mailbox that holds nothing.

Nothing about how MailFathom reaches a mailbox is returned: no server, no port, no user name, no credential.

## `list_emails` — the timeline

Returns a page of summaries, newest received first by default, filtered by any combination of account, folder, sender,
recipient, subject fragment, received range, seen state, flagged state, a keyword, and attachment presence. Every
argument is optional; a bare
call reads every folder every served account maps and lets tools read — configuration is what says which folders those
are, and a folder it does not name is one this deployment does not have.

A summary is enough to recognize a message — subject, sender, recipients, timestamps, size, attachment counts, remote
flags — and carries `storedEmailId`, the identifier a content read uses. It names its account both ways, as `accountId`
and `accountDisplayName`, so telling a person which mailbox a message came from needs no second call. Five fields prevent
common misreadings:

- `senderVerification` is what somebody else established, next to the `senderAddress` the message wrote about itself.
  `authorAuthentication` is what your mail server concluded about the sender shown in `From` — `authenticated`,
  `failed`, or `notEstablished` — and `deploymentTrust` is whether your own trusted-sender configuration names that
  sender — `trusted` or `unknown`. **Read the two together.** `authenticated` beside `unknown` is ordinary mail from
  somebody you have never listed and says nothing against it; `unknown` on its own says only that this deployment does
  not recognize the sender, and it is what an email whose sender failed authentication carries too.
- `machineAuthorship` is about the text rather than the sender: `state` says how much the message's own wording reads as
  machine written — `likely`, `possible`, `unlikely`, or `notAssessed` — and `likelihood` is the number behind it.
  **It is a hint, not a verdict.** It is a heuristic estimate rather than a measured probability, and `likely` is not a
  finding against the message or the person who sent it — plenty of honest mail is drafted with an AI these days.
  MailFathom never acts on it. What makes it worth having is the strongest thing it notices: a message carrying
  characters no mail client displays, which is how instructions aimed at *your* agent get hidden from you.
  `notAssessed` means nothing read the text — an empty body, the reading turned off, or mail stored before this release.
- `attachments` counts real attachments separately from inline images, so a message whose only payload is a logo in
  its signature does not read as one carrying a document.
- `remoteFlags` reports what the mail server last said about the message, including `flagged` — the star a mail client
  shows, which `isRemotelyFlagged` also filters on — and `keywords`, the labels a client or a server set beside the five
  standard flags. `keyword` narrows a listing to one of them, matched without regard to case. Read `wasObserved` before
  trusting any of it: an email nothing has looked at yet carries every flag unset and no keyword, which is not the same
  as a server reporting none.
- `contentAvailability` says whether the raw content is stored locally, and why not when it is not, instead of failing a
  later read unexplained. A message larger than the configured size limit reports so here and will report the same on
  every later read; one that arrived while local storage stood at its ceiling reports that it is waiting for room, and a
  later synchronization run fetches it.

Paging is a cursor: pass `nextCursor` back unchanged, with the same filters and direction. A cursor is bound to the
filters that issued it, so changing a filter mid-walk is refused (`52002`) rather than answered with a page from a
different question.

**Junk is left out unless you ask for it.** Mail your provider or your own filter already set aside is not what a
timeline is for, and an agent reading it cannot tell mail written to deceive it from correspondence. Set
`includeJunkMail: true` when you are looking for a message a filter took; the result says which of the two listings you
got, in `includedJunkMail`. The same argument and the same field are on `search_emails`.

## `search_emails` — ranked text search

Takes `queryText` — the words to find — plus the same structured filters a listing has, and returns one window of
matches ranked by relevance, each carrying the summary a listing would show and short extracts of the body around what
matched, the matched runs wrapped in `**`.

What it searches is the **lexical index**: subject, normalized participant addresses, and the extracted body text. Text
inside attachment payloads is not indexed at all, and encrypted mail has no indexed body either. `subjectFragment` and
`queryText` are different arguments doing different work: the fragment narrows which emails are eligible, the query
text is what the eligible ones are ranked against.

**Word the query in the language the mail was written in.** The index matches words rather than translating them, so a
query written in the language you asked your question in reaches mail written in that language and in no other. A
mailbox holding several languages is searched once per language a question could plausibly be answered in.

**Read `retrievalMode` on the result to know how the match was made.** `lexical` means by words rather than by meaning,
so a query term that appears nowhere in a message will not find it. `hybrid` means the same ranking was combined with a
search by embedding similarity, so mail whose meaning is close is found too — a search for a roof leak reaching the
message that said water damage. Which one you get depends on whether the server has an embedding provider configured and
reachable, and it can differ between two calls, which is why the field is on every response rather than something to
look up once.

**`semanticSearch` beside it says why a `lexical` answer was lexical.** `inactive` means the server does not embed mail
at all, so lexical is what it is meant to do and nothing is wrong. `available` means it does and its provider is
answering. `degraded` means it does but currently cannot reach the provider or its configuration is wrong, so these
results are narrower than the server intends — retrying will not help, and it is the server's operator who has a
credential or a declaration to fix. Recovery is automatic once they do.

There is deliberately no search cursor. Relevance order moves as mail keeps arriving and indexing catches up, so a
second page could silently skip or repeat matches; ask a narrower question instead of a longer window.

## `get_email_content` — up to ten messages

Takes the `storedEmailId` values a listing or search returned — at most ten, each named once — and returns, for each
one, normalized headers, the plain-text body, optionally a sanitized HTML body, and every attachment it carries by name,
type, and size. Set `includeAttachmentDownloadLinks` to also receive, for each file, **a short-lived URL that fetches
it** over ordinary HTTP. No response ever carries a file's bytes, so a message with a video in it costs the same as one
with a note.

Reading the top few results of a search is therefore one call rather than one per message. The entries come back in the
order you named them, and each carries either `content` or a `failure` saying why there is none, so one message this
deployment cannot serve does not discard the others.

**Or name a whole conversation instead.** Pass `threadId` — the value a listing, a search, or an earlier read returned —
and leave `storedEmailIds` out entirely: the conversation's messages come back in its own order, still at most ten, and
`unreadThreadMessages` names the ones that did not fit so a second call asks for them directly. Give exactly one of the
two; a call carrying both, or neither, is refused rather than guessed at.

Ten parts of the result exist so that an agent does not misreport a message:

- **The sender verdict comes with the evidence behind it.** `senderVerification` is the same pair a listing carries, and
  `headers.senderAuthentication` adds what it was reached from: the domain that actually authenticated, the domain the
  `From` header displayed, which check established the first (`dkim`, `spf`, or `none`), and the DMARC result your
  server reported. The two domains differing is ordinary rather than a warning: the authenticated one is whichever
  identity authenticated the transport, so mail sent through a provider that signs as itself differs there and is
  authenticated exactly as it appears. `authorAuthentication` is the answer to whether the displayed author was
  established. Either domain can be `null`, which means nothing authenticated or the message wrote no usable `From` — an
  outcome rather than missing data.
- **The authorship reading comes with what it was read from.** `machineAuthorship` is the same band and number a
  listing carries, and `authorshipEvidence` adds `signals` — what the text actually carried — and `profileRevision`,
  which says which weighting produced the number, so two messages are only comparable when it matches. The signals are
  worth reading in two halves. `tagCharacters`, `variationSelectorRun`, `hiddenCharacters`, and `bidirectionalOverrides`
  mean the message contains characters your mail client never shows you, which is worth a look on its own;
  `formulaicFraming`, `unspacedEmDashes`, `listScaffolding`, and `uniformTypography` are style habits any careful writer
  also has, and none of them means anything by itself. Both lists are empty on a message nothing assessed.
- **Truncation is explicit, and says which limit cut.** Each body representation carries `truncatedBy` and the original
  character count, so a cut message is never summarized as a whole one. `bodyCharacterLimit` means the message is longer
  than any single call returns; `readCharacterBudget` means the messages named before it used up the call's shared
  budget, and naming fewer at once returns more of this one; `sensitiveContentScanCeiling` means the deployment scans
  mail for sensitive content and analyzed as much of this body as it may, so the rest is withheld from every call rather
  than served unscanned.
- **An absent body has a reason.** `availability` distinguishes a message that displayed nothing from one MailFathom
  cannot decrypt, from one whose content the size limit deliberately kept out of storage, and from one that is simply
  waiting for storage room — which is the one case where asking again later returns the body.
- **Attachments are described always, fetched through a link.** Every read tells you what each file is called, what it
  is, and how large it is, because that is what you decide against when choosing whether to fetch one; `attachments` is
  `[]` only when the message carries none. `attachmentCounts` says how many either way.
- **A link is a secret with a deadline.** `downloadState` is `notRequested` when you did not set
  `includeAttachmentDownloadLinks`, `issued` when `downloadUrl` fetches the whole file until `downloadExpiresAt`, and
  `unavailable` when this server issues no attachment links at all — which only its operator can change. Anyone holding
  the URL can fetch that file without a credential, so fetch it once and do not log or store it; ten minutes is the
  usual window and half an hour the most any server may allow. When it expires, call `get_email_content` again for a
  new one.
- **File names are sanitized.** An attachment name is untrusted text from the message; what is published is a bare,
  normalized name, with a flag saying whether it had to be rewritten.
- **A too-long or repetitive list is refused, not trimmed.** More than ten identifiers, none at all, or the same one
  twice ends the call with a code rather than returning part of what you asked for — and so does a call that names both
  `storedEmailIds` and `threadId`, or neither.
- **`thread` says what else is in the exchange, without returning it.** Every message comes back with the conversation
  it belongs to: its identifier, where this message sits in it, which message it answers, how many messages of it you
  may see, and the others named by subject, sender, and sent time. Nothing of their bodies travels with it, so opening
  one is still a call. Conversations are assembled from the message identifiers mail carries and never from subjects, so
  a reply that renamed the subject stays in the exchange and two unrelated messages sharing one never join. `thread` is
  absent for a message this server has not assembled a conversation for yet.
- **A `[redacted:…]` marker is not message text.** On a server whose operator switched sensitive-content scanning on,
  the body, the subject, and the display names come back with each detected credential or piece of personal data
  replaced by `[redacted:<category>]`. Report it as material of that kind withheld rather than quoting it as words the
  sender wrote, and expect the same marker every time. Only the first 40 named participants of a message have their
  display name scanned; every one after that is published as an address with no name, so on such a server a missing
  `displayName` does not prove the sender wrote none. [Sensitive-content
  scanning](../features/sensitive-content-scanning.md#reading-a-message-is-scanned-in-flight) records what is scanned.

The HTML body, when requested, is aggressively sanitized — no scripts, no styles, no remote loads — and
[email content](../features/email-content.md) records exactly what survives.

## `ask_mail` — a question, answered with its sources

Takes a question in ordinary words, optionally narrowed to accounts and folders, and returns prose plus the emails the
answer was drawn from. A chat model conducts the run and looks up mail as it decides it needs context, so this is the one
tool that costs a provider call and takes seconds rather than milliseconds. Ask it when the answer spans several
messages; search when the messages themselves are what you want.

The question is yours to write, not a query to construct: its words are not matched against the mail, and the lookups are
the model's own. That is also why there is no sender or date filter here — one supplied would narrow every lookup without
the model knowing why its searches came back empty. The model narrows its own lookups instead, by the same sender,
recipient, subject, date, seen state, and attachment filters `search_emails` publishes, so a question about one person's
mail or one week reaches that mail rather than competing for it in a ranking. What it can and cannot ask for is
[Mail answering § What one lookup may ask for](../features/mail-answering.md#what-one-lookup-may-ask-for).

**Ask in whatever language you like.** The answer comes back in the language of the question, whatever language the mail
behind it was written in, and what it quotes from a message — a subject, a name, the phrase a claim rests on — stays as
that message wrote it so the citation can still be checked. The lookups are worded the other way round, in the languages
the mailbox plausibly holds, which is why a Polish question reaches English mail here and a search worded the same way
would not. [Mail answering § A question in one language, mail in
another](../features/mail-answering.md#a-question-in-one-language-mail-in-another) records both.

Junk is outside every lookup a run makes, and here there is **no** way to ask for it. The answer is written by a model
from the mail it retrieved, so a message written to deceive a reader would arrive as ordinary correspondence with
nothing left to notice it. Use `search_emails` with `includeJunkMail` when junk is what you are looking for.

### What leaves your instance when you ask

Asking is the one thing MailFathom does that sends your mail somewhere else on demand, so what leaves is worth knowing
before the tool is enabled at all. Exactly two things reach the chat endpoint the operator declared:

- **your question**, as you wrote it, and
- **the extracts the run retrieved** — for each message the model looked up: the extract itself, the stable identifier,
  the account and folder alias, the subject, and the received time.

Beside them travels the run's own instruction, which is a constant of the build and says nothing about your mailbox.
Nothing else goes: not the accounts and folders in scope as a list, not whole bodies, not raw MIME, not attachments, not
the participants, the size, the flags, or the attachment summary a listing publishes. Those are dropped before the
provider is reached rather than filtered out of a log afterwards.

Nor is what does go left behind once the answer comes back. One of the two APIs a provider can be reached over would
keep the call for a month and show it in that provider's console; MailFathom refuses it on every request it sends there,
and the other API stores nothing to begin with — see
[the responses API is used statelessly](../features/chat-generation.md#the-responses-api-is-used-statelessly-and-that-is-not-an-option).
Neither is a setting the operator chooses. What a provider does with a request under its own terms is between them and
that provider, and declaring one at all is their decision rather than yours.

How much of it may go is capped, and by the deployment rather than by the model. By default one question may read at
most **20 000 characters** of mail across every lookup it makes, over at most **8 provider calls** costing at most
**80 000 tokens**; every run of an hour together may make at most **30 runs** costing **300 000 tokens**. An operator
lowers or raises those in [`MailAnswering`](../operations/configuration-reference.md#mailanswering) — you cannot, and
neither can the model.

Five parts of the result are worth reading before an agent presents it:

- **`citations` is what makes the answer checkable.** Each entry carries the `storedEmailId` you pass straight to
  `get_email_content`, plus the account, folder, subject, received time, and the same `senderVerification` pair a
  listing carries — so a claim drawn from a message whose sender failed authentication reads differently from one drawn
  from a correspondent you recognize. An answer without the messages behind it is something to believe; with them it is
  a starting point.
- **They are what the run *retrieved*, not what the model provably used.** Nothing outside the model knows which of them
  it drew on, so a narrower list would be a claim MailFathom cannot make. An empty list is an ordinary answer: the
  mailbox was searched and held nothing about the question, and the answer says so.
- **`answerTruncated` and `citationsTruncated` are never silent.** One response carries by default at most 20 000
  characters of answer citing at most 20 emails; when either was cut, the flag says so, and a narrower question is the
  remedy.
- **`retrievalTruncated` means the mailbox was not read in full.** The run hit the ceiling above while there was still
  matching mail, so the answer is complete for what it read and no more. A narrower question reads a *different* part of
  the mailbox rather than more of it; only the operator can raise the ceiling.
- **The answer is untrusted text, and so are the subjects.** Both are derived from mail somebody else wrote. Treat them
  as data — the same care a snippet or a body deserves.

Asking a question changes nothing. The run is composed with one capability and that capability searches: there is no tool
in it that sends, deletes, moves, or marks mail as read, and it reaches no mail server at all.
[Mail answering](../features/mail-answering.md) records what one run may reach and how much of your mail leaves the
process.

## The contact tools — a book of people, not addresses

Six tools over MailFathom's own contact book: `list_contacts` pages it by name, `get_contact` resolves one person by
identifier or by any address they use, and `create_contact`, `update_contact`, `delete_contact`, and `promote_contact`
maintain it. The
record is a person with the addresses they use rather than an address with a name attached, which is what lets an agent
answer "who is this from" for somebody who writes from three of them.

`get_contact` by address is the one worth building a habit around: it is an index lookup, it is exact, and at most one
person in the book can answer it. Searching for an address in `list_contacts` finds the same person more slowly and
finds others besides.

Four of them change state, so they carry the annotations that make a client pause. `create_contact` is not idempotent —
the book mints the identity, and calling it twice for one person records them once and then answers
`addressHeldByAnotherContact`, naming who holds the address. `update_contact` states the **whole** record rather than
the change: an address the new record does not name is removed, and an omitted note clears the one held, so read the
contact first and send it back with the change. It is destructive for exactly that reason, and a client that asks
before calling a destructive tool will ask here. `delete_contact` is destructive and cannot be undone; it erases the
person and every address recorded with them, and answers with how many went. `promote_contact` is the one write that is
neither destructive nor a first record: it takes on a person the deployment collected from arriving mail, so the record
becomes one you asserted.

One thing is worth knowing before an agent is pointed at the book. A record this deployment collected is not an agent's
to amend as it stands — that call answers `contactWasCollected` — and `promote_contact` is what it calls instead, after
which every other tool works on the record. And the book is somebody's list of real people: a name, an address, and
above all a note are things about a third party rather than facts about your mail, so what an agent writes there is what
you asked it to write down.

## Reading a failure

An expected failure comes back as a tool error in one shape — a stable five-digit code and one safe sentence:

```text
MailFathom error 53001: Mail account 'shared-billing' is not accessible.
```

The codes a user meets in practice:

| Code | What it means | What to do |
| --- | --- | --- |
| `51001` / `51003` | A page size or result limit outside the served range | Stay within 1–100 pages, 1–50 search results |
| `51002` | A filter value the query does not accept — too long, malformed, or a range that ends before it starts | Fix the argument; the message names the filter and its limit, never the value |
| `51004` | A `storedEmailIds` entry is no identifier this system issues — blank, truncated, or invented | Pass the identifiers a listing or search actually returned; never construct or guess one |
| `51005` | A content read named no messages, or more than the ten one call serves | Split the list into calls of at most ten |
| `51006` | A content read named the same message twice | Remove the repeat; results are not served twice |
| `51007` | A content read named both `storedEmailIds` and `threadId`, or neither | Name exactly one of them; which you meant is not something the server guesses |
| `51008` | A `threadId` is no identifier this system issues — blank, truncated, or invented | Pass the `threadId` a listing, a search, or a read actually returned |
| `51009` | A contact listing's page size, origin, or search text is not one the book serves | Stay within 1–200 contacts, name `asserted` or `collected`, keep the search to at most 320 characters |
| `51010` | A contact was named with text that is no identifier and no usable address | Pass a `contactId` a listing or a write returned, or an address on its own — and exactly one of the two |
| `51011` | A contact record breaks a rule the book holds | The message names the rule: a missing name, no address, a preferred address the record does not name, a value over its limit |
| `52001` / `52002` | A cursor this system did not issue, or one reused after the filters changed | Restart the walk from the first page |
| `52003` | A contact listing's cursor is not one this system issued | Restart the walk; changing the search or the origin mid-walk is allowed and is not what caused it |
| `53001` | The named account is not served here | Call `list_accounts` and use an `accountId` or `displayName` it returns |
| `53002` | No such email in the local copy | The identifier is stale, or the mail was removed; list again |
| `53003` | A folder was named by a role no folder in scope carries | Name the folder's alias instead, or ask the operator to map the role on that account |
| `55001` | The email exists but its stored content is currently unreadable; a repair has been queued | Retry later — this is a local-consistency state, not a mail-server problem |
| `56001` | This deployment cannot answer questions about mail, either at all or for now | Nothing about the question caused it; the message says which, and only the operator can change it |
| `57001` | Answering would cost more than this deployment allows | The message says which ceiling: a spent period is worth asking again once it turns over, while a question that reached what one question may cost needs to be narrower |
| `54001` | Something failed for a reason the boundary deliberately does not describe | The server log has the detail, correlated by the request's trace |

`53002` and `55001` also reach you inside a *successful* `get_email_content` result, as the `failure` on the entry for
the message they concern, because a content read answers for each message it was given rather than failing whole.

Refusals are deliberately uninformative in one direction: an account that does not exist and an account that is not
yours are the same answer, so the tool surface cannot be used to discover what a deployment serves. A contact tool your
credential was not granted follows the same rule from the other end — it is missing from the tool listing, and calling
it anyway is answered as an unknown tool rather than as a permission you lack.

## What the deployment sees, and what it does not

Every call is logged with the tool name, the outcome, and the duration — never with a filter value, a query text, a
subject, or any part of a result. What you search your own mail for stays out of the operator's log by contract, and
[what the endpoint records](../operations/mcp-endpoint.md#what-the-endpoint-records) is the precise statement. The
flip side is worth stating plainly: snippets and bodies returned to an agent are mail content and travel to wherever
that agent's model runs. Which model sees your mail is decided by the client you connect, not by MailFathom.

`ask_mail` adds a second destination, and it is one the deployment chose rather than the client: extracts of the mail a
run retrieves go to the chat endpoint its operator declared, bounded as [§ What leaves your instance when you
ask](#what-leaves-your-instance-when-you-ask) states. Nothing of the question, the answer, the query the model wrote, or
a retrieved passage reaches a log on the way — what the operator can read is how many runs and tokens a period spent,
which are counts and nothing about what they were spent on.

## Expectations worth setting

- **Freshness is the synchronization interval** — five minutes by default, per account, plus the length of the run
  itself. Mail sent a moment ago is not yet listable.
- **The remote `\Seen` flag is an observation, not an effect.** Results report the flags the last synchronization run
  saw, with `wasObserved` saying whether any run has looked; reading through MailFathom never changes them.
- **Removing an account from configuration makes its stored mail unreachable** through the tools, though the rows
  remain until removed. Disabling synchronization does not — the copy already stored stays readable.
- **What happens to locally stored mail the server deleted is per account**: the default keeps a hidden tombstone,
  and a deployment can choose erasure instead. [IMAP synchronization](../features/imap-synchronization.md) records
  both dispositions.
