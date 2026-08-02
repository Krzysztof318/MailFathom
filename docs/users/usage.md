# Using the tools

<!-- describes: src/Mcp/Tools/** -->

MailFathom publishes three MCP tools, and together they are the whole surface: an agent can list mail, read one
message, and search — nothing else. This page is the user's view of that surface: what each tool answers, what every
result carries, what the deliberate limits are, and how to read a failure. The full contracts — every argument, every
field, every bound — live in [MCP tools](../features/mcp-tools.md) and the feature pages it links, and this page does
not restate them.

## The model behind every call

A tool call reads the **local copy** that synchronization maintains. Nothing in a request reaches a mail server, so a
call is fast, works while the server is unreachable, and cannot mark anything as read — locally or remotely. The price
of that model is freshness, which is why every listing and every search carries `folderFreshness`: one entry per
folder in scope, stating when synchronization last committed progress there, or that it never has. An agent that reads
mail without reading that field will eventually present an empty folder as an empty mailbox.

Results are bounded by design. A listing serves at most 100 summaries per page, a search at most 50 ranked matches, a
body at most the configured character bound, a search extract at most a few dozen words. The bounds are the
deployment's privacy control on how much mail one call can draw out, so they are refused rather than stretched when a
caller asks for more, and none of them is a client argument to widen.

## `list_emails` — the timeline

Returns a page of summaries, newest received first by default, filtered by any combination of account, folder, sender,
recipient, subject fragment, received range, seen state, and attachment presence. Every argument is optional; a bare
call reads every folder of every served account.

A summary is enough to recognize a message — subject, sender, recipients, timestamps, size, attachment counts, remote
flags — and carries `storedEmailId`, the identifier a content read uses. Two fields prevent common misreadings:

- `attachments` counts real attachments separately from inline images, so a message whose only payload is a logo in
  its signature does not read as one carrying a document.
- `contentAvailability` says whether the raw content is stored locally, and why not when it is not — a message that
  exceeded the configured size limit reports so here, instead of failing a later read unexplained.

Paging is a cursor: pass `nextCursor` back unchanged, with the same filters and direction. A cursor is bound to the
filters that issued it, so changing a filter mid-walk is refused (`52002`) rather than answered with a page from a
different question.

## `search_emails` — ranked text search

Takes `queryText` — the words to find — plus the same structured filters a listing has, and returns one window of
matches ranked by relevance, each carrying the summary a listing would show and short extracts of the body around what
matched, the matched runs wrapped in `**`.

What it searches is the **lexical index**: subject, normalized participant addresses, and the extracted body text. The
match is by words, not meaning — a query term that appears nowhere in a message will not find it — and text inside
attachment payloads is not indexed at all. Encrypted mail has no indexed body either. `subjectFragment` and
`queryText` are different arguments doing different work: the fragment narrows which emails are eligible, the query
text is what the eligible ones are ranked against.

There is deliberately no search cursor. Relevance order moves as mail keeps arriving and indexing catches up, so a
second page could silently skip or repeat matches; ask a narrower question instead of a longer window.

## `get_email_content` — up to ten messages

Takes the `storedEmailId` values a listing or search returned — at most ten, each named once — and returns, for each
one, normalized headers, the plain-text body, optionally a sanitized HTML body, and how many attachments it carries.
Set `includeAttachmentDetails` to also receive each attachment's name, type, and size — **never bytes**. Attachment
download is out of scope for the first release.

Reading the top few results of a search is therefore one call rather than one per message. The entries come back in the
order you named them, and each carries either `content` or a `failure` saying why there is none, so one message this
deployment cannot serve does not discard the others.

Five parts of the result exist so that an agent does not misreport a message:

- **Truncation is explicit, and says which limit cut.** Each body representation carries `truncatedBy` and the original
  character count, so a cut message is never summarized as a whole one. `bodyCharacterLimit` means the message is longer
  than any single call returns; `readCharacterBudget` means the messages named before it used up the call's shared
  budget, and naming fewer at once returns more of this one.
- **An absent body has a reason.** `availability` distinguishes a message that displayed nothing from one MailFathom
  cannot decrypt and from one whose content the size limit deliberately kept out of storage.
- **Attachments are counted always, named on request.** `attachmentCounts` says how many there are whatever you asked
  for; `attachments` is `null` when you did not ask and `[]` when the message carries none. A file name is text the
  sender chose, so an ordinary read of a body does not publish it.
- **File names are sanitized.** An attachment name is untrusted text from the message; what is published is a bare,
  normalized name, with a flag saying whether it had to be rewritten.
- **A too-long or repetitive list is refused, not trimmed.** More than ten identifiers, none at all, or the same one
  twice ends the call with a code rather than returning part of what you asked for.

The HTML body, when requested, is aggressively sanitized — no scripts, no styles, no remote loads — and
[email content](../features/email-content.md) records exactly what survives.

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
| `52001` / `52002` | A cursor this system did not issue, or one reused after the filters changed | Restart the walk from the first page |
| `53001` | The named account is not served here | Check `AccountId` spelling against the deployment's configuration |
| `53002` | No such email in the local copy | The identifier is stale, or the mail was removed; list again |
| `55001` | The email exists but its stored content is currently unreadable; a repair has been queued | Retry later — this is a local-consistency state, not a mail-server problem |
| `54001` | Something failed for a reason the boundary deliberately does not describe | The server log has the detail, correlated by the request's trace |

`53002` and `55001` also reach you inside a *successful* `get_email_content` result, as the `failure` on the entry for
the message they concern, because a content read answers for each message it was given rather than failing whole.

Refusals are deliberately uninformative in one direction: an account that does not exist and an account that is not
yours are the same answer, so the tool surface cannot be used to discover what a deployment serves.

## What the deployment sees, and what it does not

Every call is logged with the tool name, the outcome, and the duration — never with a filter value, a query text, a
subject, or any part of a result. What you search your own mail for stays out of the operator's log by contract, and
[what the endpoint records](../operations/mcp-endpoint.md#what-the-endpoint-records) is the precise statement. The
flip side is worth stating plainly: snippets and bodies returned to an agent are mail content and travel to wherever
that agent's model runs. Which model sees your mail is decided by the client you connect, not by MailFathom.

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
