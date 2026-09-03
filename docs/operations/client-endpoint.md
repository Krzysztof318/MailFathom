# The client endpoint

<!-- describes: backend/src/AppHost/Program.cs, backend/src/AppHost/OrchestrationContract.cs, backend/src/Host/Configuration/Endpoints/ClientEndpointOptions.cs, backend/src/Host/Configuration/Endpoints/ClientApplicationOptions.cs, backend/src/Host/Api/ClientApiEndpoints.cs, backend/src/Host/Api/ClientMailAccountsEndpoint.cs, backend/src/Host/Api/ClientMailFoldersEndpoint.cs, backend/src/Host/Api/ClientMailTimelineEndpoint.cs, backend/src/Host/Api/ClientMailThreadEndpoint.cs, backend/src/Host/Api/ClientMailMessageEndpoint.cs, backend/src/Host/Api/ClientMailBodyEndpoint.cs, backend/src/Host/Api/ClientMailAttachmentEndpoint.cs, backend/src/Host/Api/AttachmentContentResponse.cs, backend/src/Host/Api/ProtectedResourceMetadataEndpoint.cs, backend/src/Host/Security/Endpoints/ClientTransportSecurityExtensions.cs, backend/src/Host/Hosting/ClientApplicationFiles.cs, backend/src/Host/Hosting/Warnings/ClientTransportSecurityWarning.cs, backend/src/Host/Hosting/Warnings/PasswordClearTextTransportWarning.cs, backend/src/Host/Api/ClientOwnerRecordEndpoint.cs, backend/src/Host/Api/ClientPortraitEndpoint.cs, backend/src/Host/Api/ClientDisplayNameEndpoint.cs, backend/src/Host/Configuration/OwnerSettings/Administration/OwnDisplayName.cs, backend/src/Host/Api/ClientMailMutationsEndpoint.cs, backend/src/Host/Api/ClientDraftEndpoints.cs, backend/src/Host/Api/ClientDraftResponses.cs, backend/src/Host/Api/ClientOutboxEndpoints.cs, backend/src/Host/Api/ClientTelemetryEndpoint.cs, backend/src/Host/Observability/ClientTelemetry/** -->

Where the MailFathom client reaches the service, what a deployment has to enable before it answers, and what a person's
mail client presents to get in.

It is a third transport surface beside the two that already exist, and it is deliberately not either of them with more
routes on it. An agent's key, an operator's key, and the credential somebody signs their own mail client in with are
three things to provision and three things to revoke; keeping them on separate listeners with separate credentials is
what makes that a fact rather than a convention.

What it draws on is the mailbox rather than a vocabulary of its own. A grant written here comes from the same half of
[the published set](permissions.md#the-published-set) the MCP endpoint's grants come from, because the client reads the
mail an agent reads. Only the transport is new.

## The endpoint is off unless you turn it on

A deployment that configures nothing serves no client surface, so adopting a release opens no new network door onto a
mailbox. Enabling it opens a listener of its own:

```jsonc
{
  "ClientEndpoint": {
    "Enabled": true,
    "Port": 8080,
    "Cors": { "AllowedOrigins": [ "https://mail.example.test" ] },
    "Authentication": [
      {
        "Method": "oauth-subject",
        "OAuth": {
          "Resource": "https://mail.example.test/api/client",
          "AuthorizationServers": [
            { "Name": "workforce", "Issuer": "https://sso.example.test/realms/mailfathom" }
          ]
        }
      }
    ]
  }
}
```

**The listener is its own, and that is the point.** Client routes answer on the client listener and nowhere else, and a
request for `/api/client` arriving on the MCP or the administrative port is refused before it reaches any credential
check — answered `404`, because the honest answer is that nothing is served there. The default port is the other
surfaces' own, so enabling several without stating a port publishes one socket serving each of them at its own prefix
rather than three sockets; [sharing a socket](configuration-endpoints.md#sharing-a-socket) states what that couples and
what stays each surface's own.

Every key this section takes is in
[endpoint configuration](configuration-endpoints.md#clientendpoint), with its default and its constraint.

**A normal local `aspire run` is the development exception to that default.** It enables the surface on a loopback
socket of its own and admits the password method, so whatever a developer points at it has something to reach. The
AppHost provisions its synthetic credential after the service reports ready;
[the client surface](local-development.md#the-client-surface) records the complete local topology and credential.

## What it serves

| Route | Grant it needs |
| --- | --- |
| `GET /api/client/session` | none |
| `GET /api/client/accounts` | `mailfathom.mail.read` |
| `GET /api/client/folders` | `mailfathom.mail.read` |
| `GET /api/client/emails` | `mailfathom.mail.read` |
| `GET /api/client/emails/search` | `mailfathom.mail.read` |
| `GET /api/client/threads/{threadId}` | `mailfathom.mail.read` |
| `GET /api/client/messages/{storedEmailId}` | `mailfathom.mail.read` |
| `GET /api/client/messages/{storedEmailId}/body` | `mailfathom.mail.read` |
| `GET /api/client/messages/{storedEmailId}/attachments/{position}` | `mailfathom.mail.read` |
| `GET /api/client/mutations` | `mailfathom.mail.read` |
| `POST /api/client/mutations/flags` | `mailfathom.mail.flags.write` |
| `POST /api/client/mutations/flags/withdrawals` | `mailfathom.mail.flags.write` |
| `POST /api/client/mutations/moves` | `mailfathom.mail.move` |
| `POST /api/client/mutations/moves/withdrawals` | `mailfathom.mail.move` |
| `GET /api/client/record` | `mailfathom.mail.read` |
| `POST /api/client/record` | `mailfathom.mail.accounts.write` |
| `POST /api/client/record/mail-accounts` | `mailfathom.mail.accounts.write` |
| `POST /api/client/record/mail-accounts/removal` | `mailfathom.mail.accounts.write` |
| `GET /api/client/display-name` | `mailfathom.mail.read` |
| `POST /api/client/display-name` | `mailfathom.mail.accounts.write` |
| `GET /api/client/drafts` | `mailfathom.mail.drafts.write` |
| `POST /api/client/drafts` | `mailfathom.mail.drafts.write` |
| `GET /api/client/drafts/{draftId}` | `mailfathom.mail.drafts.write` |
| `PUT /api/client/drafts/{draftId}` | `mailfathom.mail.drafts.write` |
| `DELETE /api/client/drafts/{draftId}` | `mailfathom.mail.drafts.write` |
| `POST /api/client/drafts/{draftId}/attachments` | `mailfathom.mail.drafts.write` |
| `DELETE /api/client/drafts/{draftId}/attachments/{attachmentId}` | `mailfathom.mail.drafts.write` |
| `POST /api/client/drafts/{draftId}/send` | `mailfathom.mail.send` |
| `GET /api/client/outbox` | `mailfathom.mail.send` |
| `GET /api/client/outbox/{outgoingEmailId}` | `mailfathom.mail.send` |
| `POST /api/client/outbox/cancellation` | `mailfathom.mail.send` |
| `POST /api/client/outbox/requeue` | `mailfathom.mail.send` |
| `GET /api/client/preferences` | `mailfathom.mail.read` |
| `POST /api/client/preferences` | `mailfathom.mail.read` |
| `GET /api/client/portrait` | `mailfathom.mail.read` |
| `POST /api/client/portrait` | `mailfathom.mail.read` |
| `DELETE /api/client/portrait` | `mailfathom.mail.read` |
| `POST /api/client/telemetry/v1/traces` | none |
| `POST /api/client/telemetry/v1/metrics` | none |
| `POST /api/client/telemetry/v1/logs` | none |

No path here carries a version segment MailFathom chose: the major version is `0` and
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
permits breaking the contract outright, so `/v1` would be scaffolding for a promise this project has not made. The
`/v1` in the three [telemetry routes](#the-telemetry-routes) is not one: those paths are fixed by the OTLP
specification, so a client points its exporter at the prefix above them and appends nothing itself.

### The session route

It answers with four fields: `service`, which is always `MailFathom`; `version`, the running release; `permissions`, the
published names the credential just presented carries, in the order this project publishes them; and `telemetry`,
whether this deployment forwards a client's own telemetry.

That is what a client needs before it has drawn a single message: that this is MailFathom rather than something else
answering the port, which contract it speaks, and what the rest of the surface will serve it. It is also what lets
sign-in be built and proven end to end before a screen exists — a client that reached here with a token it had just been
issued knows the token works.

**`telemetry` is not part of the grant and never varies by credential.** What decides it is whether the deployment named
a collector, which is the same condition that decides whether
[the telemetry routes](#the-telemetry-routes) are served at all — so the two cannot disagree. A client reads it to
decide whether to record anything and whether its own switch is worth offering; the only other way to find out is to
export a batch and read the `404`, which is finding out by doing the thing. A client older than this field, or one
reading an answer that omits it, treats it as `false` and sends nothing.

It has a second reader, holding nothing at all. A person naming their deployment in the client types a host, and the
client asks here before it sends anything to that address, so a mistyped one is reported as an address that answers as
nothing rather than as a sign-in that failed. Wherever the endpoint requires a credential — which is every deployment
that configured one — such a caller is refused, and the refusal is itself the answer: every MailFathom surface
challenges under the `MailFathom` protection space, and this endpoint's CORS policy names `WWW-Authenticate` among the
headers a browser may read. A deployment that requires none answers the body instead, and `service` is what the client
reads out of it.

**It names no credential**, which is the one way it differs from what
[`GET /api/admin/session`](admin-endpoint.md) answers. That surface's reader is `mfctl` in an operator's own hands, and
the deployment's configured name for the credential that authenticated is what tells them which of their own entries let
them in. This surface's reader is a page holding a token, which brought no name and has nothing to do with one; a
response echoing a deployment's configured identity for a credential would be a way to read configuration back out of
the service from a browser.

A caller granted nothing reads an empty `permissions` list rather than a refusal, because "nothing" is the accurate
answer to what such a caller may do, and because a credential retired by narrowing its grant to nothing should be
distinguishable from one that no longer works.

It is the one route on this surface published under no permission, for the reason the administrative session route is:
it reports the credential the caller already presented and the version this deployment already publishes, so putting it
behind a permission would make that permission a component of every client grant.

### The accounts route

```http
GET /api/client/accounts
```

It answers with the mail accounts the signed-in owner owns, and how current the local copy of each one is:

```jsonc
{
  "synchronizationEnabled": true,
  "accounts": [
    {
      "id": "work",
      "displayName": "Work mail",
      "synchronizationState": "Synchronized",
      "lastSynchronizedAt": "2026-08-15T10:00:00+00:00",
      "behind": false
    },
    {
      "id": "private",
      "displayName": "Private mail",
      "synchronizationState": "Failing",
      "lastSynchronizedAt": "2026-08-14T21:12:00+00:00",
      "behind": true
    }
  ]
}
```

`id` is the identifier the account was declared under and `displayName` is the name it is published under; each is
unique within the owner rather than across the deployment, and both are MailFathom's own names for the mailbox. The mail
server, the port, the user name, and every credential are deliberately absent, and so is everything of the mailbox
itself — no message, no subject, no correspondent, no folder listing.

**`synchronizationState`, `lastSynchronizedAt` and `behind` answer different parts of one question**, which is why they
are three fields rather than one:

| `synchronizationState` | What it says |
| --- | --- |
| `Synchronized` | Progress has been committed and this deployment's most recent finished attempt did not fail |
| `Failing` | This deployment's most recent finished attempt did not complete, whether or not it has ever synchronized |
| `Unreachable` | The mail server did not serve this deployment within its resilience budget, so nothing is refreshing the copy and nothing here is wrong with it |
| `NeverSynchronized` | No run has ever durably committed progress for the account |

`Unreachable` is separate from `Failing` because the two ask different things of whoever reads them: an unreachable
mailbox is waited out or looked at on the server, and a failing one is a mapping, a credential, or a defect. An account
reaches it only when unreachability is the whole of what went wrong — a run that also failed some other way reached the
server, so it reads `Failing` instead.

`lastSynchronizedAt` is when any of the account's folders last durably took something in, and is `null` where none ever
has. How *old* is too old is the reader's judgement rather than this deployment's, so nothing here calls an account
stale — an account that has been failing since yesterday and one nobody has written to since yesterday carry the same
instant and are not the same situation.

`behind` is `true` when any of the account's folders ended its last attempt with mail it had not yet taken in. It is not
a state, because a folder can be behind under any of them: an attempt that succeeded within its batch budget leaves mail
for the next one, and a failing folder is usually behind as well. An account that is merely catching up is therefore
`Synchronized` and `behind`, which is a different situation from an unreachable one and reads as one.

The state describes what the running process has observed. A process that has just started reports an account it has not
run yet by what its stored progress says rather than by how its runs were going before the restart, because the backoff
that was failing is not one this process is applying. A host shutting down mid-attempt is not a failure either: the
supervisor counts it under none, so an account backed off for every restart would be one approached less often for being
stopped.

**`synchronizationEnabled` is the deployment's switch rather than the owner's**, and it is reported beside the accounts
because no per-account value carries it: a copy that last moved a week ago means one thing where the deployment is still
trying every few minutes and another where an operator switched synchronization off.

**An owner with no mail account reads an empty `accounts` list**, which is a state to render rather than an error. A
credential whose grant does not carry `mailfathom.mail.read` is answered `403` instead, naming the permission it lacks,
so the two are never confused; naming it discloses nothing, since the session route already tells that same caller its
whole grant. **An account another owner holds is absent exactly as an account this deployment does not serve is
absent** — nothing in the response, its timing, or its failure modes separates the two.

Nothing here contacts a mail server. The answer is composed from local state, so it is the same whether or not a mailbox
is reachable at the moment it is asked, and asking cannot set the remote `\Seen` flag. It is bounded by how many
accounts the owner has and by nothing else — not by their folders, their messages, or how many times synchronization has
run.

### The folders route

```http
GET /api/client/folders
```

It answers with the owner's mailboxes and every folder in them, which is the one tree a mail screen is drawn from:

```jsonc
{
  "synchronizationEnabled": true,
  "accounts": [
    {
      "account": {
        "id": "work",
        "displayName": "Work mail",
        "synchronizationState": "Synchronized",
        "lastSynchronizedAt": "2026-08-15T10:00:00+00:00",
        "behind": false
      },
      "folders": [
        {
          "alias": "INBOX",
          "role": "Inbox",
          "path": [ "INBOX" ],
          "storedEmailCount": 4213,
          "unreadEmailCount": 12,
          "synchronizationState": "Synchronized",
          "lastSynchronizedAt": "2026-08-15T10:00:00+00:00",
          "behind": false
        },
        {
          "alias": "ARCHIVE-2024",
          "role": null,
          "path": [ "Archiwum", "2024" ],
          "storedEmailCount": 980,
          "unreadEmailCount": 0,
          "synchronizationState": "Synchronized",
          "lastSynchronizedAt": "2026-08-15T09:41:00+00:00",
          "behind": true
        }
      ]
    }
  ]
}
```

**`account` is the accounts route's own answer, field for field**, so a client parses one account shape across this
surface and the two routes cannot come to disagree about what a mailbox is. That is why it is nested rather than
flattened into the folders beside it.

**The tree arrives in one request** because it is one thing on screen. A client that read the folders here and the
mailbox names from [the accounts route](#the-accounts-route) would be composing one picture out of two answers, the
second already stale relative to the first. The accounts route stays the cheaper answer for a client that only wants the
mailbox list: counting a folder's mail is work proportional to the mail, so a client polling for whether a mailbox is
reachable asks there and a client drawing a tree asks here.

**`role` is the answer a client cannot work out for itself.** Special-use folders are advertised by server attribute
rather than by name, and the names differ per provider and per language, so a screen that guessed which folder is the
sent one from its name would guess wrong on somebody's Polish provider. The value is one of `Inbox`, `Archive`,
`Drafts`, `Sent`, `Junk`, `Trash`, `All`, `Flagged`, `Important`, or `Outbox` — the role
[configuration labelled the folder with](configuration-mail.md), and `null` where it labelled it with none. Nothing is
guessed from a folder's name to fill it in.

**`path` is the folder's place on its mail server, outermost level first.** It is split into levels rather than
published as a path and a delimiter, so a client builds a tree without knowing that mail servers have hierarchy
delimiters or which character this one chose. The last level is what a person recognizes as the folder's name; `alias`
above it is MailFathom's own name for the folder — one upper-cased configured word, unique within its account — and is
what everything else on this surface names the folder by. A server that reports no delimiter has a flat mailbox and the
whole path arrives as one level.

**The counts are of the local copy, not of the mailbox.** `storedEmailCount` is what this deployment holds and would
serve, and `unreadEmailCount` is how many of those the mail server last reported without `\Seen`. A folder still being
backfilled holds fewer than the server does, which is why the three freshness fields travel in the same object: a count
read without them is a figure somebody would take for the mailbox's own. Reading the unread count sets nothing — the
flags are a snapshot reconciliation wrote, and this endpoint speaks no mail protocol at all.

**Each folder's `synchronizationState`, `lastSynchronizedAt` and `behind` mean exactly what they mean on an account**,
one folder at a time; the table above is the whole of it. An account's own reading is the reduction of its folders', so
a tree and the mailbox list beside it never disagree: a folder that failed makes its account `Failing`, and an account
is `Unreachable` only where being unable to reach the server is the whole of what went wrong.

**The folders are the ones this deployment knows of.** An alias
[configuration maps](configuration-mail.md) that nothing has ever bound to a remote folder is absent rather than empty —
there is no folder on the server to draw — and where an operator finds out about such a mapping is
[the administrative status route](admin-endpoint.md). A folder an operator withheld from tools is absent for the same
reason it is absent from every other read: this surface admits what configuration admits. A folder that has been
discovered but never synchronized *is* present, carrying `NeverSynchronized`, no `path` where its binding is not
recorded yet, and both counts at zero, because an empty folder and an unsynchronized one are not the same thing on
screen.

**An owner with no mail account reads an empty `accounts` list**, and a credential whose grant does not carry
`mailfathom.mail.read` is answered `403` — the same two answers the accounts route gives, because naming an owner's
folders is the same disclosure as naming their mailboxes.

The answer is bounded by the folders the owner's accounts have, which configuration bounds, and by nothing the mailbox
can grow. Nothing here contacts a mail server, and asking cannot set the remote `\Seen` flag.

### The mail list route

```http
GET /api/client/emails?account=work&folder=INBOX&pageSize=50
```

It answers with one page of the owner's mail, ordered by when each message was received, and with the cursor that
continues the list at each end:

```jsonc
{
  "emails": [
    {
      "id": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "account": "work",
      "folder": "INBOX",
      "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
      "subject": "Release 0.8.0 is out",
      "receivedAt": "2026-08-15T09:58:00+00:00",
      "sentAt": "2026-08-15T09:57:12+00:00",
      "senderAddress": "releases@example.test",
      "senderDisplayName": "Example Releases",
      "toAddresses": [ "somebody@example.test" ],
      "unread": true,
      "flagged": false,
      "answered": false,
      "hasAttachments": true,
      "attachmentCount": 2,
      "sizeOctets": 48213,
      "preview": "The release went out this morning and the notes are attached."
    }
  ],
  "nextCursor": "AbCd...",
  "previousCursor": null,
  "pageSize": 50
}
```

**A row is everything a list draws and nothing else.** Who wrote it, what it is about, when it arrived, whether it has
been read, flagged or answered, whether anything is attached, and the opening of the message's own text. There is no
body and no raw MIME on this route at all: a page of fifty rows each carrying a body is a megabyte to draw a list, and
reading a message is a different request.

**`preview` is at most 200 characters of the message's own text**, with runs of whitespace collapsed to single spaces,
cut by PostgreSQL rather than by the service — so a body never crosses out of the database to produce one. It is the
text as extraction trimmed it, without quoted history or a signature block, which is what keeps a reply's preview from
being the message it was answering. It is `null` for mail this deployment has stored but not yet extracted, which is
not the same as a message whose text is empty. The bound is fixed: no request may raise it and no deployment may
change it.

**The cursor is opaque and yours to hold.** It names a row of the page together with the list it was read under, and
nothing on the server remembers it, so a client may keep one while the screen is closed, or across a restart of the
deployment, and continue from it afterwards. `nextCursor` reads the page after this one and `previousCursor` the page
before it; either being `null` means that end of the list has been reached. A page that came back empty carries
neither, and a client that asked to scroll back from the first row it holds keeps the cursor it already had.

**Scrolling back is the same list read the other way, never a re-sort.** `direction=backward` returns the page
*before* the cursor, still in the order the list is sorted in, so a client prepends what it receives. It continues from
a cursor and from nothing else: `direction=backward` with no `cursor` is refused, because there is no page before the
leading end and answering with the leading page would read as having scrolled to the top.

| Parameter | Accepts | Default |
| --- | --- | --- |
| `account` | An account identifier or display name | every account the owner owns |
| `folder` | A folder alias, or a role as `role:Inbox` | every folder of those accounts |
| `includeJunk` | `true`, `false` | `false` |
| `unread` | `true`, `false` | both |
| `flagged` | `true`, `false` | both |
| `hasAttachments` | `true`, `false` | both |
| `receivedOnOrAfter` | A timestamp, inclusive | no start |
| `receivedBefore` | A timestamp, exclusive | no end |
| `sort` | `receivedAt` | `receivedAt` |
| `order` | `newestFirst`, `oldestFirst` | `newestFirst` |
| `direction` | `forward`, `backward` | `forward` |
| `pageSize` | 1 to 100 | 25 |
| `cursor` | A cursor a previous page returned | the leading end of the list |

**A value this deployment cannot honour is refused with `400`, never ignored.** `sort=subject` is the case worth
naming: the list is ordered by the column the timeline indexes are ordered by, and a screen that asked for something
else and was handed the default order would have no way to tell. The same holds for an unknown `order` or `direction`,
a `folder` naming neither an alias nor a role, a `pageSize` outside the range, and a cursor — a cursor this deployment
never issued and a cursor issued for a different list are refused separately, because they are two mistakes with two
repairs. What is never returned instead is the first page.

**A parameter sent empty is a parameter that was not sent**, and `sort`, `order` and `direction` are matched without
regard to case. A query string is composed by a page rather than typed, so a field the screen has nothing to put in yet
arrives as `?folder=`; making that mean something other than "every folder" would be a refusal no client could act on.

**Filtering is what a list offers, not what a search does.** The parameters above are the controls a person can see on
a mail screen. A phrase from the body ranks rather than orders, and is [the search route](#the-mail-search-route)
instead.

The filters are part of what a cursor was issued for, so changing any of them — or the `order` — makes a cursor taken
before the change belong to a different list. Changing `pageSize` alone does not: page size moves no boundary.

**The page is served from the local copy.** Nothing here contacts a mail server, so no screen waits on IMAP, and
reading a folder cannot set the remote `\Seen` flag. How current that copy is, per folder, is what
[the folders route](#the-folders-route) answers; it is not repeated on every page.

**An owner with no mail account reads an empty `emails` list**, and a credential whose grant does not carry
`mailfathom.mail.read` is answered `403`.

### The mail search route

```http
GET /api/client/emails/search?query=invoice%20from%20accounting&folder=INBOX&pageSize=20
```

It answers with one page of the owner's mail ranked against what they are looking for:

```jsonc
{
  "results": [
    {
      "id": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "account": "work",
      "folder": "INBOX",
      "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
      "subject": "Invoice 4471",
      "receivedAt": "2026-08-15T09:58:00+00:00",
      "sentAt": "2026-08-15T09:57:12+00:00",
      "senderAddress": "accounts@example.test",
      "senderDisplayName": "Example Accounting",
      "toAddresses": [ "somebody@example.test" ],
      "unread": false,
      "flagged": false,
      "answered": false,
      "hasAttachments": true,
      "attachmentCount": 1,
      "sizeOctets": 48213,
      "preview": "The invoice for August is attached and due at the end of the month.",
      "snippets": [ "The **invoice** for August is attached" ],
      "matchedBy": "BothRankings"
    }
  ],
  "nextCursor": "AbCd...",
  "pageSize": 20,
  "retrievalMode": "Hybrid",
  "semanticSearch": "Available",
  "includedJunkMail": false
}
```

**One route searches by words and by meaning at once.** A person looking for a message does not know whether the words
they remember are the words the message used, so there is no parameter that chooses between the two: the deployment
ranks both ways wherever it can, and the answer says which happened.
[Search](../features/email-search.md) is where the two rankings and their fusion are described.

**A result is a list row with two fields added**, so one layout draws both and a result can be opened, filtered, and
acted on without a second request. `snippets` are the extracts around what matched, each marking the matched words with
`**` — text cut from untrusted mail rather than markup to render — and `preview` is the same bounded opening the list
route publishes.

**`matchedBy` is why the row is there**: `LexicalRanking` for a message carrying the query's words, `SemanticRanking`
for one matching by meaning, and `BothRankings` for one both rankings found. It is the field a screen needs most for
the second of those: a semantically ranked message carries no `snippets`, because there is no extract of it that shows
the query's words, and a row with nothing under it would otherwise read as unexplained. On a lexically ranked page
every result is `LexicalRanking` by construction.

**`retrievalMode` and `semanticSearch` are how a narrower answer says it is narrower.** `Lexical` means this page was
ranked by words alone; `Hybrid` means both rankings took part. What separates a deployment that deliberately does not
embed from one whose provider is refusing is the field beside it: `Inactive` is the first, `Degraded` the second, and
`Available` is a deployment ranking by meaning. A search is never refused because an embedding provider was
unreachable — it is answered lexically and says so, which is what makes the degradation something an operator can act
on rather than results that quietly got worse.

| Parameter | Accepts | Default |
| --- | --- | --- |
| `query` | The text to search for, up to 512 characters | required |
| `account` | An account identifier or display name | every account the owner owns |
| `folder` | A folder alias, or a role as `role:Inbox` | every folder of those accounts |
| `includeJunk` | `true`, `false` | `false` |
| `sender` | An address the sender must carry | any sender |
| `recipient` | An address a `To` or `Cc` recipient must carry | any recipient |
| `unread` | `true`, `false` | both |
| `flagged` | `true`, `false` | both |
| `hasAttachments` | `true`, `false` | both |
| `receivedOnOrAfter` | A timestamp, inclusive | no start |
| `receivedBefore` | A timestamp, exclusive | no end |
| `pageSize` | 1 to 50 | 20 |
| `cursor` | A cursor a previous page returned | the best-ranked results |

**Every parameter beside `query` constrains rather than ranks.** A person who narrowed to one sender and to last year
has said which mail may come back, so those values decide what is eligible before anything is ranked and the query
decides only the order of what remains. They are applied inside the ranking query rather than to what it returned,
which is what keeps a page from being filled by mail the filters exclude and then emptied.

**A query that matched nothing is answered with nothing.** `results` comes back empty and `nextCursor` is `null`,
rather than the page being filled with the nearest mail — so a person told the search found nothing knows to write a
different one.

**Paging walks forward through a ranked list of at most 200 results.** `nextCursor` continues it and `null` means the
list ends there; a client keeps the pages it has already drawn, because a relevance order is recomputed per query and
there is nothing to scroll back to. What a ranked cursor cannot promise is what the list route's promises: a message
indexed between two pages can move across a boundary a client is holding and be seen twice or not at all. Somebody who
has read two hundred results without finding what they wanted narrows the filters, which is what would have found it
sooner.

**A value this deployment cannot honour is refused with `400`, never ignored.** A blank or over-long `query`, an
address filter that is not an address, a `folder` naming neither an alias nor a role, a `pageSize` outside the range,
and a cursor — a cursor this deployment never issued and a cursor issued for a different search are refused separately,
because they are two mistakes with two repairs. What is never returned instead is the best-ranked page of a search
nobody asked for. A refusal states what to change and never echoes what was sent: a query is the most revealing value
this surface carries.

**The page is served from the local copy**, and only mail that has been extracted is searchable at all — a message
this deployment has stored but not yet indexed matches nothing here. Nothing on this route contacts a mail server, so
no search waits on IMAP and none can set the remote `\Seen` flag.

**An owner with no mail account reads an empty `results` list**, and a credential whose grant does not carry
`mailfathom.mail.read` is answered `403`.
### The conversation route

```http
GET /api/client/threads/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91?pageSize=50
```

It answers with one conversation as a single document — the messages in it, who wrote in it, and how big it is:

```jsonc
{
  "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
  "messages": [
    {
      "position": 0,
      "answeredId": null,
      "email": {
        "id": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
        "account": "work",
        "folder": "INBOX",
        "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
        "subject": "Release 0.8.0 is out",
        "receivedAt": "2026-08-15T09:58:00+00:00",
        "sentAt": "2026-08-15T09:57:12+00:00",
        "senderAddress": "releases@example.test",
        "senderDisplayName": "Example Releases",
        "toAddresses": [ "somebody@example.test" ],
        "unread": true,
        "flagged": false,
        "answered": false,
        "hasAttachments": true,
        "attachmentCount": 2,
        "sizeOctets": 48213,
        "preview": "The release went out this morning and the notes are attached."
      }
    },
    {
      "position": 1,
      "answeredId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "email": {
        "id": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92",
        "account": "work",
        "folder": "SENT",
        "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
        "subject": "Re: Release 0.8.0 is out",
        "receivedAt": "2026-08-15T10:04:00+00:00",
        "sentAt": "2026-08-15T10:03:41+00:00",
        "senderAddress": "somebody@example.test",
        "senderDisplayName": "Somebody",
        "toAddresses": [ "releases@example.test" ],
        "unread": false,
        "flagged": false,
        "answered": false,
        "hasAttachments": false,
        "attachmentCount": 0,
        "sizeOctets": 3120,
        "preview": "Thanks — I will read the notes this afternoon."
      }
    }
  ],
  "participants": [
    { "address": "releases@example.test", "displayName": "Example Releases", "messageCount": 1 },
    { "address": "somebody@example.test", "displayName": "Somebody", "messageCount": 1 }
  ],
  "messageCount": 2,
  "moreMessagesNotAssembled": false,
  "moreParticipantsNotNamed": false,
  "nextCursor": null,
  "pageSize": 50
}
```

**A conversation is not scoped to a folder, and the route takes no account or folder to scope it by.** The question is
in the inbox, the answer is in `SENT`, and a forwarded copy is in a project folder — so it is read across every folder
of every account the signed-in owner owns, and the junk folder takes part too, because a reply that landed in junk is
still part of the exchange somebody is reading. A folder an operator withheld from tools is the one exception, and it is
absent for the reason it is absent everywhere: the message is in no conversation this surface publishes, in no count of
one, and in no participant list.

**What a conversation covers today is one account's mail.** MailFathom assembles a thread from the mail of the account
that holds it, so the same exchange in two of your mailboxes is two conversations with two identifiers, each complete
within its own account. Nothing in this route narrows by account — it never asks which mailbox you were looking at — so
what bounds a thread here is how threading is assembled rather than how it is served.

**`email` is [the mail list route's](#the-mail-list-route) own row, field for field.** A client parses one message shape
across this surface, and its `preview` is what that message added with the quoted history and the signature block
trimmed off — which is what keeps the eighth reply from redrawing the seven above it. There is no body here either: the
whole of a message is a request of its own, named by the `id` that row already carries.

**`position` and `answeredId` are where the message sits.** `position` is its zero-based place in the conversation's
own order and continues across pages, so a client that has paged twice still knows what it is holding. `answeredId`
names the message it answers *among the ones you are shown* — a message whose parent sits in a withheld folder is
published as a root naming nothing, so the withheld message is not disclosed by the gap it would otherwise leave.

**The order is the conversation's own, not a re-sort.** The reply relation decides it wherever it is known, because it
is the only statement about sequence a sender did not make about themselves; the sent time settles messages answering
the same parent, and the local identity settles the rest so the order is total. For an ordinary exchange, where each
message answers the one before it, that is chronological order — and a reply somebody's clock dated a year early still
sits under what it answers rather than at the top of the screen. It is the same order the MCP surface publishes for the
same conversation, so two clients of one deployment never see one exchange in two orders.

**`participants` is who wrote, drawn from the whole conversation rather than the page.** That is the point of it: a
client draws the header from the first page without walking the messages. Each entry carries the address, the display
name their most recent message wrote, and how many of the conversation's messages are theirs; a message with no usable
sender names no participant. At most 50 authors are named, and `moreParticipantsNotNamed` says when a list expansion
went past that. Addressees are not participants here — who a message went to is on the message.

**`messageCount` is the whole conversation, and the page is bounded separately.** A conversation is assembled to at most
500 messages the caller may see; `moreMessagesNotAssembled` is `true` when it runs past that, which is a mailing list's
archive rather than correspondence somebody is following. Within it, `pageSize` messages are returned at a time and
`nextCursor` continues from the last one; `null` means the conversation ends there. Paging runs forward from the
beginning of the conversation, because that is how a thread is read.

**The cursor names a message, not a position.** The order is derived on every read, so a reply arriving in the middle
moves positions and moves no message — and the next page is whatever follows the message the last page ended on. Three
things are refused with `400`, each saying which it is: a cursor this deployment never issued, a cursor issued for a
different conversation, and a cursor whose message this conversation no longer shows, which is a message deleted or
moved into a folder you may no longer read. The repair for the last one is to read the conversation from its beginning,
which is why it is not answered with the first page — a thread that silently jumped back to the top would read as a
defect in the client.

| Parameter | Accepts | Default |
| --- | --- | --- |
| `threadId` | The conversation's identifier, as a message row published it | required, in the path |
| `pageSize` | 1 to 100 | 25 |
| `cursor` | A cursor a previous page returned | the beginning of the conversation |

**A conversation this owner does not hold is answered `404`**, and so is one no deployment ever held: nothing in the
answer, its timing, or its failure separates somebody else's exchange from one that never existed. Text that is not a
UUID matches no route and is the same `404`. A credential whose grant does not carry `mailfathom.mail.read` is answered
`403`, as everywhere else on this surface.

**The conversation is served from the local copy.** Nothing here contacts a mail server, so no screen waits on IMAP, and
opening a thread cannot set the remote `\Seen` flag.

### The message route

```http
GET /api/client/messages/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90
```

It answers with everything a reading pane draws around a message and none of what it draws inside one — the headers the
message displays, what this deployment established about the author it displays, the files it carries, and which forms
of its body exist to be asked for:

```jsonc
{
  "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
  "account": "personal",
  "folder": "INBOX",
  "threadId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
  "sizeOctets": 2416381,
  "headers": {
    "subject": "Release 0.8.0",
    "sentAt": "2026-08-27T09:14:00+00:00",
    "receivedAt": "2026-08-27T09:14:06+00:00",
    "participants": [
      { "role": "From", "address": "release@example.test", "displayName": "Release notices" },
      { "role": "To", "address": "reader@example.test", "displayName": null }
    ],
    "messageId": "release-0-8-0@example.test",
    "inReplyTo": null,
    "references": []
  },
  "body": { "availability": "Readable", "plainText": true, "html": true },
  "sender": {
    "authorAuthentication": "Authenticated",
    "deploymentTrust": "Trusted",
    "authenticatedDomain": "example.test"
  },
  "attachments": [
    {
      "position": 0,
      "fileName": "release-notes.pdf",
      "wasFileNameNormalized": false,
      "mediaType": "application/pdf",
      "sizeOctets": 2401337
    }
  ],
  "carried": {
    "attachmentCount": 1,
    "totalSizeOctets": 2401337,
    "inlineResourceCount": 2,
    "encrypted": false,
    "unverifiedSignature": false,
    "unexpandedTnefPart": false
  },
  "unread": true,
  "flagged": false,
  "answered": false
}
```

**It is the other half of the pane, and the body is the first.** The two are separate requests because they are
separately expensive: a header block is drawn as soon as this answers, whatever the body costs, and a client that had
asked for both at once would wait for the slower of them to draw either.

**The headers are parsed from the stored message rather than read off a list row.** That is why a display name, a `Bcc`
the message carried for its own recipient, and the three threading identifiers are here and not on a row: the columns a
list is served from keep the comparison forms a filter needs, and a reader shown those would be shown a narrower message
than the one that arrived. A message whose content the size limit kept out of storage has the narrower set a row can
answer for, which is what its `availability` says.

**`body` says what the sender wrote, not what a request would return.** The body route answers with words for every
readable message, deriving them from the markup where the sender wrote no text part, so a returned representation says
nothing about what arrived — and whether there is a richer rendering to draw is exactly what a pane is asking. `html`
is `true` where the message carried an HTML part, which is what the body route's document is reduced from; both are
`false` for a body nothing could read, and `availability` is the same set that route reports.

**`sender` is two states and is never one.** `authorAuthentication` is what the receiving mail server established about
the author the message displays, and `deploymentTrust` is whether this deployment recognizes that author — a fact about
the message beside a classification of a list. They are published side by side because collapsing them into one badge
would mean inventing the rule that combines them: an authenticated author nobody has named is the ordinary state of
legitimate mail and carries the same trust value as one whose authentication failed outright. Both are read back as they
were stored; nothing on this path re-reads a header, resolves DNS, or evaluates a policy, so what a reader is shown is
what was concluded about the authenticated author rather than a reading of the `From` header.

**`authenticatedDomain` is who the authentication was established for**, which is the fact a reader needs when the
name a message displays is not it. It is the domain the receiving mail server authenticated and nothing wider: it is
`null` for a message nothing authenticated a sender for, and it is published beside the two states rather than
compared against the `From` address, because judging whether the two agree is the policy this path deliberately does
not evaluate.

**No octet of a file is here, at any size and in any encoding.** Each entry says what the file is called, what it
declares itself to be, and how large it decodes to — which is what a reader decides against — and `position` is what its
own [attachment route](#the-attachment-route) is asked with. The position is the identity because it is the only stable
one a message's parts have: MIME gives an attachment no identifier, a `Content-ID` is optional and sender-chosen, and a
file name is neither unique nor required. A file name is text a sender chose and arrives normalized to a bare name —
never a path, never a traversal segment, never a control character — with `wasFileNameNormalized` saying whether that
rewrote anything.

**The pictures a message displays inside its own body are not in this list and are not a route.** An inline part is
resolved against the message's own parts while the [body](#the-message-body-route) is reduced, so a sender's own images
arrive drawn rather than as references a pane would have to fetch, and `inlineResourceCount` is where they are counted
instead. That is also what keeps them from being confused with the remote ones, whose addresses are removed there rather
than turned into something this surface would resolve.

**`carried` is `null` rather than zero for a message nothing has ever parsed** — the case of content the size limit kept
out of storage. Zero would claim the message carries no files, which no local copy exists to support; `attachments` is
empty beside it for the same reason.

| Parameter | Accepts | Default |
| --- | --- | --- |
| `storedEmailId` | The message's identifier, as a list row or a conversation published it | required, in the path |

**A message this owner does not hold is answered `404`**, and so is one no deployment ever held, and so is one whose
stored content is missing or damaged. Text that is not a UUID matches no route and is the same `404`. A credential whose
grant does not carry `mailfathom.mail.read` is answered `403`, as everywhere else on this surface.

**It is served from the local copy.** Nothing here contacts a mail server, so no screen waits on IMAP, and opening a
message cannot set the remote `\Seen` flag.

### The message body route

```http
GET /api/client/messages/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/body
```

It answers with one message's body in both the renderings a reading pane draws from — the words, and the message
reduced to a closed document tree — and, where the read asked for it, in a third the surface that opens the sender's own
layout reads:

```jsonc
{
  "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
  "availability": "Readable",
  "plainText": {
    "text": "The release went out this morning and the notes are attached.",
    "originalCharacterCount": 61,
    "truncation": "None"
  },
  "document": {
    "schemaVersion": 1,
    "refusal": "None",
    "removedRemoteReferenceCount": 2,
    "retainedRemoteImageCount": 0,
    "inlineImageCount": 1,
    "undrawnInlineImageCount": 0,
    "truncated": false,
    "blocks": [
      {
        "type": "heading",
        "version": 1,
        "level": 2,
        "alignment": "Inherited",
        "content": [ { "text": "Release 0.8.0", "emphasis": "None", "foreground": null, "link": null } ]
      },
      {
        "type": "paragraph",
        "version": 1,
        "alignment": "Inherited",
        "content": [
          { "text": "The notes are ", "emphasis": "None", "foreground": null, "link": null },
          {
            "text": "on the site",
            "emphasis": "Underline",
            "foreground": "#0b57d0",
            "link": {
              "target": "https://example.test/notes",
              "host": "example.test",
              "asciiHost": null,
              "deception": "None"
            }
          }
        ]
      }
    ]
  },
  "selfContainedHtml": null,
  "remoteImagesRequested": false
}
```

**Both renderings travel together**, because a pane needs both: `document` says whether it was refused and `plainText`
is what it falls back to. A client that had to ask twice would draw an empty pane in between. `selfContainedHtml` is
present on every response and is `null` on a read that did not ask for it, which is what the example above shows.

**The document is a closed tree, and that is the whole of what makes it safe to draw.** It is not sanitized markup: it
is a list of typed blocks, and every value in one is text, a number, a colour in `#rrggbb`, or a member of a fixed set.
There is nowhere in it to put a script, an event handler, an embedded object, a form, a style sheet, or an element — so
a construct nobody thought of cannot survive by being unfamiliar, and a renderer drawing it with its own typed controls
cannot be steered by what a stranger wrote. `type` names the block and `version` names the revision of that block's own
shape, so a client keys its renderers by the pair and shows a placeholder for a pair it does not implement rather than
failing the message. The eight identities are `paragraph`, `heading`, `list`, `table`, `quote`, `image`, `separator`,
and `preformatted`, each at revision `1`, and `schemaVersion` names the revision of the document itself.

**Nothing in a body reaches somebody else's server unless the reader asked.** Every reference to a remote address is
removed while the tree is built rather than left for a renderer to decline to follow, so a tracking pixel is defeated by
the document not carrying it. `removedRemoteReferenceCount` is what survives instead — a count a pane can put in front
of the reader — and it counts a remote `img`, a `url(` in a style declaration, and a `background` attribute alike. A
picture the message carries itself is different and is resolved in the deployment: an inline part reached by
`Content-Id` or `Content-Location` becomes a `data:` URI, bounded by count, by the size of each, and by how much they
come to together, with `inlineImageCount` and `undrawnInlineImageCount` saying how many were drawn and how many were
beyond a bound. That last bound is what makes this route's answer a size a client can plan for: a body is the one
document this surface serves that carries a message's own content rather than a description of something, so a pane
reads it against a ceiling of its own rather than against the megabyte every other route here is held to.

**Asking for remote pictures is a second request, and nothing on either side remembers it:**

```http
GET /api/client/messages/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/body?remoteImages=true
```

The answer then carries the absolute addresses of the pictures the message asked for, counted in
`retainedRemoteImageCount`, and echoes the request in `remoteImagesRequested`. Nothing is written down: there is no
per-sender allowance, no per-message allowance, and no setting, so opening the message again asks again. That is
deliberate rather than unfinished — a remembered allowance is a standing consent that outlives the reason it was given,
and the reader's own act is what should decide, message by message, whether a sender's server is told they opened it.
Only pictures are ever retained; a style declaration's reference and a `background` attribute are dropped under both
readings, because neither has anywhere in the contract to go.

**Asking for the sender's own markup is a second request too:**

```http
GET /api/client/messages/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/body?fullHtml=true
```

The answer then carries `selfContainedHtml` beside the two renderings above — `text`, `originalCharacterCount`, and
`truncation`, exactly as `plainText` does — and `null` on every read that did not ask, so the default read is unchanged
and pays for none of it. What it holds is the message's own layout with the message's own pictures inlined, with
nothing in it that a renderer would run and nothing in it that resolves against another host. `truncation` reads
`InlineImageOctetLimit` where the message carried more of its own pictures than one representation inlines, which is
the one bound that leaves the words whole and the pictures short.

Both queries compose. `remoteImages=true` widens this representation further than it widens the tree — a background and
a web font are restored beside the pictures, because a layout served without them is the reduction the surface was
opened to escape — while an `@import` and the candidates of a `srcset` stay refused, because neither reaches the pass
that decides a URL, and nothing executable is restored by either query.

**A link carries where it actually goes, and what the deployment made of how it was written.** `target` is the resolved
absolute address, carrying only `http`, `https`, or `mailto` — a `javascript:`, `data:`, `vbscript:`, or `file:` target
is dropped and the words it was written on are kept. `host` is the host as a reader recognizes it and `asciiHost` is
the same host in its ASCII form, **present only where the two differ**, which is what a homograph looks like.
`deception` is `DisplayedHostDiffers` where the link's own text names one host and the link goes to another,
`NotApplicable` where the text is not a place, and `None` where the two agree. The determination is the deployment's
rather than each client's, so two clients reading the same message cannot disagree about how loudly to warn.

**A body the reduction refuses is answered as its plain text with the reason beside it**, in `refusal`: `NoHtmlPart`
where the message carried no markup at all, `ReductionFailed` where the markup could not be read, `NothingRenderable`
where it reduced to no content, and `None` where the document is the message. `truncated` says a bound stopped the
reduction before the end of the body, and `truncation` on `plainText` says the same about the words.

**`availability` is the same set the tool surface reports** — `Readable`, `EncryptedNotReadableLocally`,
`NotStoredExceededSizeLimit`, and `NotStoredAwaitingStorageHeadroom` — and `document` is `null` for every value but the
first, because there was no body to reduce.

| Parameter | Accepts | Default |
| --- | --- | --- |
| `storedEmailId` | The message's identifier, as a list row or a conversation published it | required, in the path |
| `remoteImages` | `true` to fetch what the message asks for from other servers on this read alone | `false` |
| `fullHtml` | `true` to also answer with the sender's own markup, self-contained | `false` |

**A message this owner does not hold is answered `404`**, and so is one no deployment ever held, and so is one whose
stored content is missing or damaged: nothing in the answer separates somebody else's mail from mail that never existed
or from this deployment's own defect. Text that is not a UUID matches no route and is the same `404`. A credential
whose grant does not carry `mailfathom.mail.read` is answered `403`, as everywhere else on this surface.

**The body is served from the local copy.** Nothing here contacts a mail server, so no screen waits on IMAP, and opening
a message cannot set the remote `\Seen` flag. The words and the document both pass the same sensitive-content egress
guard the tool surface's readings pass, so a redaction rule configured for this deployment applies to what a pane draws
exactly as it applies to what a model reads.

[The mail document](../features/email-content.md) holds the reasoning for every paragraph above, and states what a
client is left to render.

### The attachment route

```http
GET /api/client/messages/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/attachments/0
```

It answers with one file's octets and nothing else:

```http
HTTP/1.1 200 OK
Content-Type: application/pdf
Content-Length: 2401337
Content-Disposition: attachment; filename=release-notes.pdf
X-Content-Type-Options: nosniff
Cache-Control: no-store
```

**`position` is the place the file holds in the message route's `attachments` list**, which is the order the message's
structure is walked. A position that list does not have is answered `404`, as is a negative one, and neither reads the
mailbox to find out.

**The three headers are the sender's own values, encoded for where they are being written.** The media type is parsed
before it is echoed, so a header value the sender wrote cannot introduce a parameter or a second header, and a value
that is not a media type at all is served as `application/octet-stream` rather than repaired into something plausible.
The file name is written through RFC 5987 encoding rather than concatenated into a header. `Content-Length` is the size
the parse measured — the same number the message route published — so a client knows what to expect and a transfer cut
short is visible as one rather than as a shorter file.

**The disposition is always `attachment` and `nosniff` is always set.** These are bytes a sender chose, served from the
origin the client itself is served from: rendered in place, a message carrying HTML would be a scripted page on the
address the operator publishes MailFathom at. `no-store` is there because this is an ordinary cacheable `GET` whose
response is mail content, and the deployments this surface is documented for put a reverse proxy in front of it.

**The octets are streamed rather than buffered**, decoded from the stored copy straight into the response, so a large
attachment costs the copy buffer rather than its own size on either side.

**It is the client's own route rather than the signed link the tool surface mints**, and the difference is who is being
served. [A download link](mcp-endpoint.md#the-one-route-on-this-surface-that-admits-no-credential) exists to be handed
to something holding no credential, which is why it is a bearer capability that expires within minutes; a reader here
has already authenticated and holds
`mailfathom.mail.read`, so the credential they presented is the access control and nothing is minted. That is also what
keeps this working on a deployment serving no MCP endpoint, which serves no link route either.

**A file of a message this owner does not hold is answered `404`**, and so is one of a message no deployment ever held,
and so is one whose stored content is missing or damaged: nothing in the answer separates somebody else's mail from mail
that never existed or from this deployment's own defect. A credential whose grant does not carry `mailfathom.mail.read`
is answered `403`, as everywhere else on this surface.

**It is served from the local copy.** Nothing here contacts a mail server, so downloading a file cannot fetch a message
and cannot set the remote `\Seen` flag.

### The mutation routes

These five are how a person changes the mailbox itself: marking mail read or unread, starring it, relabelling it, and
filing it into another folder — and asking where each of those changes has got to, or taking one back.

| Route | What it does |
| --- | --- |
| `POST /api/client/mutations/flags` | Writes down one batch of flag and tag changes, one result per message |
| `POST /api/client/mutations/moves` | Writes down one batch of folder moves, one result per message |
| `GET /api/client/mutations?record=…&record=…` | Reports where each of the caller's own changes stands |
| `POST /api/client/mutations/flags/withdrawals` | Takes back flag and tag changes nothing has been asked of a server for |
| `POST /api/client/mutations/moves/withdrawals` | Takes back moves nothing has been asked of a server for |

**Nothing here reaches a mail server, and that is the design rather than a limitation.** A submission writes a durable
record and answers; the account's own reconciliation pass is what issues the IMAP command, which is
[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)'s
write session and the same path a rule, a spam verdict, and an MCP tool go down. So a screen never waits on somebody
else's mail server to redraw, a change asked for while the account is unreachable is kept rather than lost, and a
process that dies between a copy and a delete leaves a record saying what was half done.

**A submission is a batch, and the answer is per message.** At most 200 messages per request — and at most 100 records
per read, because a read names them in the request line. The route enforces that hundred itself, answering a longer one
with an ordinary `400`; it is set well under the request line Kestrel will actually accept, so a longer path or a proxy
that rewrites the prefix never turns a documented read into a raw `414` nobody can act on. A refusal about one of the messages is that message's own result rather than the request's — so a batch composed from a list that has moved on since
the screen drew it reports exactly which entries did not apply and writes the rest down. `requestId` is the caller's own
identity for the request: repeating a request under the same one is the same request and produces the same records
rather than a second set, and omitting it has one generated, which makes every submission distinct.

```http
POST /api/client/mutations/flags
Content-Type: application/json

{
  "requestId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
  "changes": [
    {
      "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "flags": { "seen": true, "flagged": null },
      "tags": { "change": "add", "keywords": ["$Todo"] }
    }
  ]
}
```

```json
{
  "results": [
    {
      "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "outcome": "recorded",
      "detail": null,
      "changes": [
        { "mutation": "set-seen", "recordId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91", "state": "pending" },
        { "mutation": "add-keywords", "recordId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92", "state": "pending" }
      ]
    }
  ]
}
```

**A record per value rather than per message**, because a record is the unit convergence resumes, abandons, and
attributes an observation back to. A message whose `\Seen` change completes while its keywords are still converging
reports exactly that.

**`flags` and `tags` are two different things and the response keeps them apart.** `flags` are the IMAP system flags
`\Seen` and `\Flagged`; `tags` are IMAP keywords, which is what a mail server calls a label. Both reach the mailbox —
MailFathom holds no tag of its own, so there is no local label that could leak to a server by being confused with a
keyword, and equally none to keep private. A tag change names both halves together, `change` being one of `add`,
`remove`, or `replace` and `keywords` the list it applies to: a list without a direction and a direction without a list
are each half a change, and guessing which was meant would make clearing every tag unreachable and an accidental empty
list destructive.

**Nothing on this surface sets `\Seen` implicitly.** Reading a message does not mark it read anywhere — not the message
route, not the body route, not the attachment route. Marking a message read is a flag change submitted here and nothing
else. What MailFathom's own client does with that is
[ADR 0026](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md)'s
answer rather than this route's: a person opening a message has the client submit one change here, unless they turned
[`markReadOnOpen`](#the-preferences-routes) off or the credential was never granted `mailfathom.mail.flags.write`.

**A move names the folder by its alias**, exactly as the folders route publishes it, rather than by its place on the
server: an alias keeps its meaning when the server renames or recreates the folder behind it.

```http
POST /api/client/mutations/moves
Content-Type: application/json

{ "moves": [{ "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90", "destinationFolder": "ARCHIVE" }] }
```

Each result carries one of `recorded`, `message-not-found`, `destination-not-found`, `already-in-destination`, or
`account-no-longer-configured`, and a flag change may also answer `change-not-usable` when the values asked for are not
values one request can name. A folder withheld from this caller is reported as a destination that is not there, on the
same rule that makes mail in a withheld folder a message that is not there: filing mail into a folder the caller cannot
read would move it out of sight rather than be a capability of its own.

**Moving mail is its own grant.** `mailfathom.mail.flags.write` does not reach it and `mailfathom.mail.move` does. A
flag misdescribes mail the owner can still find; a move puts the mail somewhere else, and on a server without `MOVE` it
is a copy followed by a delete — which is why the two are granted separately and why the record exists at all.

**A move either completes or leaves the message where it was, and a half-finished one is reported rather than
guessed at.** The read route's `outcomeUnknown` is that report: a placement command went out and its answer never came
back, so the message may be in the destination, in the source, or in both, and MailFathom will not decide which. The
account's next pass re-establishes it; until then the field is the one thing on this response a person acts on rather
than waits through.

```http
GET /api/client/mutations?record=0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91
```

```json
{
  "changes": [
    {
      "recordId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91",
      "storedEmailId": "0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90",
      "mutation": "relocate",
      "state": "converging",
      "outcomeUnknown": false,
      "attemptCount": 2,
      "lastFailure": 22001,
      "recordedAt": "2026-08-12T09:00:00+00:00",
      "stateChangedAt": "2026-08-12T09:04:00+00:00"
    }
  ]
}
```

**`state` is `pending`, `converging`, `completed`, `dead-lettered`, or `cancelled`**, and `attemptCount` with
`lastFailure` are what say a change against an unreachable account is being retried rather than forgotten. The retries
are bounded and backed off by the account's own pass; a change that exhausts them is `dead-lettered`, which is a person's
to look at rather than something the deployment keeps trying forever.

**A change that has not reached the server is withdrawable, and one that has is not.** A withdrawal moves a record to
`cancelled`, and the account's pass never sees it. A record past the point a command went out is reported where it
stands instead of being refused, which is also what makes the call safe to repeat: a `STORE` already issued cannot be
recalled, and a placement whose answer never came back has to be re-established rather than declared void. The grant
that authored a change is the grant that withdraws it, which is why there are two withdrawal routes rather than one.

**A record belonging to somebody else is absent from every answer here rather than refused**, and so is one recorded in
a folder this caller may no longer read — the same answer a read of that folder's mail gives. Nothing on this surface
says whether an identity somebody else holds exists.

**No mutation record carries mail content.** A record names the message, the change, the folder, and where it stands; no
subject, no correspondent, no body, and no keyword reaches a log line or a telemetry dimension from it.

### The record routes

These four are how a person maintains what this deployment reads for them: which mailboxes it synchronizes, what each
one is reached with, and the settings that are theirs rather than the deployment's. Reading is `mailfathom.mail.read`
like the rest of this surface; the three writes are `mailfathom.mail.accounts.write`, separately granted for the reason
every write here is separately granted — reading somebody's mail is not deciding which mailboxes are read for them.

| Route | What it does |
| --- | --- |
| `GET /api/client/record` | Hands over the signed-in owner's record as redacted JSON, with the version it was read at |
| `POST /api/client/record` | Commits that record back edited, as one change against the version it was opened over |
| `POST /api/client/record/mail-accounts` | Declares one more mailbox in it |
| `POST /api/client/record/mail-accounts/removal` | Stops it declaring one mailbox, named by the identifier it was declared under |

**No route here names an owner, and that is the whole of the isolation.** The record acted on is the one belonging to
the credential that authenticated, resolved from the request rather than read out of the body or the path, so there is
no identifier a client could put in a request to reach somebody else's record. It follows that a request naming another
owner cannot be composed at all — there is nowhere to put the name — and a deployment serving several people publishes
no route through which any of them learns that the others exist. [The administrative
surface](admin-endpoint.md#owners-and-their-records) is where a roster is read, and it is not reachable with a
credential issued for this one.

**A withdrawal withdraws no mail.** Stopping the record declaring a mailbox stops this deployment synchronizing it;
every message, folder, and attachment already stored for that account stays exactly where it is and stays readable
through the routes above. Disposing of stored mail is administrative and irreversible, and it is deliberately not
something a client can reach.

**Nothing here reports a secret, and nothing here can overwrite one blindly.** A record is handed over with every
password, token, and client secret replaced by the redaction marker; a save is read as the difference from what the row
holds, so a marker saved back leaves the credential beneath it as it was, and a marker this deployment cannot place is
refused rather than committed over somebody's password. Material supplied here is sealed under the deployment's
[data-encryption key](secret-provisioning.md) like every other MailFathom secret, and what the record keeps is the
reference to it.

**A candidate is validated before it is committed, and committed whole or not at all.** It is bound strictly against
the same rules a configuration file is, checked for two mail accounts declared under one identifier, checked that every
account in it belongs to this owner, and put through the same mail-synchronization validators a start applies —
including the walk that resolves every credential the record names, so a reference that reaches nothing is refused here
rather than committed and then refusing the whole deployment's next start. What the record asks about [scanning this
owner's mail](configuration-sources.md#what-an-owner-may-say-about-scanning-their-own-mail) is judged here too: an owner
may switch a scanner on for their own mail and never off, and asking for the personal-data scanner where the deployment
stood no analyzer up is refused at the write rather than left to fail closed on the next message. A refusal names what
to correct — for a scanning one, the deployment setting behind it — and carries nothing that was supplied as a secret. A record another writer moved on in the meantime — the owner from a second
device, or an administrator — is refused as superseded rather than overwritten, so the client re-reads and composes the
change again.

**A mailbox declared here names a credential this deployment provisioned for this owner, and nothing else.** A record
never carries a password: what it carries is a [reference](secret-provisioning.md) to material the deployment can
resolve, and that material is reached by whatever the reference names — a mounted file, a systemd credential, an
environment variable. The mail server the account names is the owner's own, so a reference written here decides what
this deployment hands to a machine that person controls; unbounded, `mailfathom.mail.accounts.write` would reach the
database password and every other secret this deployment can resolve.

Two references are therefore admissible and no others. One the record already carries stays admissible whoever put it
there, so a change that was never about the credential is never refused over it. And one whose material was provisioned
for this owner is admissible, which is read from the name the operator gave it: **the last segment of the reference's
target begins with `owner-<owner identifier>-`**. So `file:/run/secrets/owner-3f1d…-work-password` and
`systemd-credential:owner-3f1d…-work-password` are the owner's to name, and `file:/run/secrets/database-password` is
not — whatever path is written in front of it, because the bound is the name of the material rather than the way to it.
An operator provisioning a mailbox credential for somebody to declare themselves names it that way; one who would
rather not writes the mail account through [`mfctl owner account
add`](admin-endpoint.md#owners-and-their-records) instead, which is bounded by nothing here. A refusal names both
routes out.

**An owner whose mail accounts are still read from this deployment's configuration cannot write here.** The write is
refused, naming the administrative `mfctl owner adopt` that moves them into the record first, because committing it
would leave two answers to which mailboxes this deployment reads and the files would win at the next restart. Nothing on
this surface can perform that move: which decisions leave a deployment's own files is the operator's, not the owner's.

**An accepted record change is published to the running process.** A mailbox declared here is stored and scheduled
without a restart. The coordinator drains work already in flight against the immutable document version it began with,
then starts the replacement supervisor, so one run never reads two answers.

### The name routes

These two are how a client draws the person signed in. Until they existed it held a finished credential and nothing
the person is called, so the account menu and the Settings screen had an anonymous icon where a name belongs.

| Route | What it does |
| --- | --- |
| `GET /api/client/display-name` | Hands over the name this deployment records the signed-in person under, and says whether it would accept a change of it |
| `POST /api/client/display-name` | Records them under the name they corrected theirs to |

```json
{ "displayName": "Ada Lovelace", "changeable": true }
```

**The name is the envelope rather than the record.** It is the same label `mfctl owner list` shows and
[the record routes](#the-record-routes) do not serve: a column beside the document, unique across the deployment, that
nothing resolves anybody by. A client reading the record alone would still have nothing to draw a person with, which is
why this is a route of its own rather than a key in that document.

**The read says whether the write would be accepted, so the client never has to find out by trying.** `changeable` is
false for somebody whose credential was not granted `mailfathom.mail.accounts.write`, and false for somebody whose mail
accounts a configuration source still declares. It does not say which of the two: a client draws the same read-only
field either way, and naming the grant a credential lacks would report a deployment's own entries back to a page
holding a token.

**A person a configuration source declares is refused, and told what to correct.** A start writes every declared
owner's name back from the declaration, so a change made here would stand until the next restart and then revert. What
comes back names the entry to change instead, or `mfctl owner adopt` to move the accounts into the person's own record.

**The name is bound exactly as the envelope binds it.** Blank is refused, so is anything past 128 characters, and so is
a name somebody else on this deployment already carries — each naming what to correct. Surrounding white space is
trimmed, and the answer carries the name as it was stored rather than as it was sent. The body is bounded like every
other on this surface, and one past the bound is answered `413`.

**It carries no version.** The version the record's writes state guards the document a change was composed over, and
the name is not part of that document, so nothing here is refused as superseded.

**Neither route names an owner**, exactly as no record route does: the name read and written is that of the credential
that authenticated, resolved from the request rather than out of the body or the path. Neither adds a name to
[the published permission set](permissions.md).

**Reading is `mailfathom.mail.read` and writing is `mailfathom.mail.accounts.write`.** The write is the record's own
grant because it writes the same row an administrator maintains, and the read is the grant every signed-in person
already holds — somebody whose mailboxes an administrator maintains still sees their own name.

### The preferences routes

These two hold what a person set about their own client, so that it follows them rather than the machine they set it
on. Somebody who turns telemetry off on their laptop has not agreed to anything on the next machine they sign in from,
and somebody who set the client up the way they work should not have to set it up again per browser profile.

| Route | What it does |
| --- | --- |
| `GET /api/client/preferences` | Hands over the signed-in person's preferences, with every one they never set answered by its default |
| `POST /api/client/preferences` | Replaces all of them, as one document |

```json
{ "telemetryEnabled": true, "theme": "system", "openMailInTabs": false, "markReadOnOpen": true }
```

**It holds four preferences and nothing else.** Whether this deployment may be told what the person's client is doing;
what the client is painted in, which is `system`, `light`, or `dark`; whether opening a message opens a tab rather
than replacing what is on the screen; and whether opening a message marks it read on the person's own mail server. Each
of them says how somebody wants to work, which is why it belongs to the
person. The language does not, and stays on the device: it is resolved for somebody who has not signed in and may never
get a session. Neither does the width a person drags the message list to, which describes the screen in front of them.

**Unset reads as telemetry on, the theme following the machine, tabs off, and marking read on.** A person who has set
nothing is
answered a document rather than a refusal, so a first run draws a screen. The theme is still resolved on the device
before sign-in — the client cannot wait on the network to paint itself, and there is no session to read this over above
the sign-in screen — and what this answers replaces that device value once a session exists.

**`markReadOnOpen` covers every account that person reads, and turning it off stores no read state instead.** It is one
value rather than one per mailbox because read state is what must not differ between the machines somebody reads on, and
a client whose owner has turned it off shows what their mail server last reported and remembers no reading of its own.
It governs opening a message and nothing else: marking a message read or unread deliberately, through the flag
mutations above, is unaffected by it.
[ADR 0026](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md)
holds the reasoning, including why an operator's lever here is
[`mailfathom.mail.flags.write`](permissions.md#the-published-set) rather than a key of their own.

**A write states the whole document.** It is a closed set rather than a patch: a key nothing binds is refused rather
than stored, a theme this deployment does not publish is refused naming the three that are, and a preference the body
omits is committed as its own default rather than left at whatever was there. The body is bounded like every other on
this surface, and one past the bound is answered `413`.

**It carries no version, and the last write wins.** The only writers are one person's own devices, so there is nobody
whose change could be lost — and a superseded refusal here would be a conflict screen over a checkbox. That is the one
way these routes differ from [the record routes](#the-record-routes), which are written by an administrator too.

**No route here names an owner**, exactly as no record route does: the preferences acted on are those of the credential
that authenticated, resolved from the request rather than read out of the body or the path.

**Reading and writing are both `mailfathom.mail.read`.** The write is deliberately not
`mailfathom.mail.accounts.write`, which every record write is: that grant decides which mailboxes this deployment
connects to and under whose credentials, somebody whose mail accounts an administrator maintains does not hold it, and
what may be said about a person must not be decided by a grant over their mail configuration. Neither route adds a name
to [the published permission set](permissions.md).

**Nothing here reports when anything was set, or from where.** This deployment keeps no record of the machines somebody
signed in from, and a response saying when a switch last moved would be the beginning of one. Erasing an owner erases
their preferences with everything else derived from them.

**A deployment that proxies no telemetry still serves these routes** and stores the switch unchanged, so a person's
answer survives a deployment that starts forwarding later. What such a deployment tells a client is
[`"telemetry": false` on the session route](#the-session-route), and what MailFathom's own client draws for that is
a sentence saying so in place of the switch — a control over a client that is already sending nothing decides nothing.

### The portrait routes

These three hold the picture a person is drawn by, which the account menu and the settings screen put beside their
name. It is stored on the owner axis beside the preferences document rather than inside one: a megabyte of image
octets is not a small closed document, and reading a switch should not carry a photograph.

| Route | What it does |
| --- | --- |
| `GET /api/client/portrait` | Serves the signed-in person's picture under the kind it is |
| `POST /api/client/portrait` | Replaces it with the octets the request body carries |
| `DELETE /api/client/portrait` | Removes it, leaving everything else about the person as it was |

**An upload is at most 1 MB, and is a JPEG or a PNG.** The bound is applied before a handler is entered, and an upload
over it is answered `413` naming the megabyte. The kind is read from the signature the file opens with rather than
from the `Content-Type` the request declared — a declared type is a string an uploader wrote, so trusting it would let
anything at all be stored under an image's name and served back to a browser as one. Octets that are neither kind are
answered `415` naming both. A request carrying no body at all is answered `400`.

**Nothing is done to the picture beyond that.** It is not resized, cropped, re-encoded, or stripped of its metadata,
and it is served back exactly as it was supplied — so the bound and the kind check are the whole of what stands
between an upload and the database. A person keeps one picture and no history of previous ones.

**Having no picture is answered `204`, not `404`.** The client draws the initials it already has from the person's
name, so an absent portrait is an ordinary state of the screen rather than an error on it; `404` on this surface says
a route or a record is not there, which is a different thing. A person this deployment no longer holds a record for is
answered `404` on the upload, and `204` on the removal like anybody else.

**The served picture is named, and the client revalidates against that name.** The response carries an entity tag over
the octets and `Cache-Control: private, no-cache`, so the second screen that draws the same portrait is answered `304`
rather than the picture again, while a replaced one reaches the next screen rather than the next expiry. It is
deliberately not cached without revalidation: a portrait is personal data.

**No route here names an owner**, exactly as no record route does: the picture acted on is that of the credential that
authenticated, resolved from the request rather than read out of the body or the path.

**All three are `mailfathom.mail.read`**, and none adds a name to [the published permission set](permissions.md). The
two writes are deliberately not `mailfathom.mail.accounts.write`, for the reason [the preferences
write](#the-preferences-routes) is not: that grant decides which mailboxes this deployment connects to, and what a
person is drawn by must not be decided by a grant over their mail configuration.

**The octets hang off the owner row.** Erasing an owner erases their portrait with everything else derived from them,
without an erasure naming that table.

### The drafts routes

These are what a person writes mail through, and the first thing to know about them is that **a draft here is the one
in the owner's own drafts folder**. Composing one writes a row and a stored message in this deployment and files a copy
on their mail server in the same act, so there is no second kind held only here for somebody to go looking for, and no
route to promote one into the other.

| Route | What it does |
| --- | --- |
| `GET /api/client/drafts` | Lists what the signed-in owner is writing, newest first, optionally narrowed to one account |
| `POST /api/client/drafts` | Writes one new draft, as a message of its own or as an answer to stored mail |
| `GET /api/client/drafts/{draftId}` | Opens one draft with the words its stored message carries |
| `PUT /api/client/drafts/{draftId}` | Replaces one draft with what the author has written since |
| `DELETE /api/client/drafts/{draftId}` | Gives one draft up, and takes its copies back out of the folder |
| `POST /api/client/drafts/{draftId}/attachments` | Stages one file against a draft, the octets being the request body |
| `DELETE /api/client/drafts/{draftId}/attachments/{attachmentId}` | Takes one staged file back off |
| `POST /api/client/drafts/{draftId}/send` | Queues the message the draft holds, which is the one act here that reaches anybody else |

**Every route is scoped to the caller's own owner, and a draft another owner holds answers exactly as one nobody
holds.** A save names the account it belongs to and that name is resolved against the accounts the caller's owner owns;
every other act names a draft, and the identifier becomes a draft this owner holds before anything acts on it. So a
`404` here says nothing about whether such a draft exists somewhere in the deployment.

**A draft survives a restart and is reachable from another head**, because it is a row, a stored message, and a copy in
the owner's own drafts folder rather than anything the client holds: the desktop shell and the browser sign in to the
same deployment and list the same drafts, and the owner's phone shows them from their mail server.

**The grants are two rather than one, because writing a draft and sending it are different powers.** Writing, listing,
opening, revising, giving up, and attaching are `mailfathom.mail.drafts.write`, whose effect reaches the owner's own
mailbox and nobody else's; sending is `mailfathom.mail.send`, which puts a message in somebody else's mailbox and is
the one act here that cannot be taken back. A client granted the first alone composes freely and sends nothing. They
are the same two names [the drafting tools](../features/mcp-tools.md#the-drafting-surface) are published under, because
a draft written here and a draft written by an agent are the same thing in the same folder.

**Every save is an `APPEND` and a removal against the owner's mail server**, since IMAP has no command that changes a
stored message. So a revision is what an author asks for rather than what a keystroke produces: the client saves when
somebody says to save, and holds what they are typing until then.

**An answer is written by naming what it answers rather than by quoting it.** A save carries either `account` with
`subject`, which is a message of its own, or `answeredEmailId` with `answers` — `senderOnly`, `everyone`, or `forward`
— which reads the account, the subject, and the threading identifiers from the message being answered, so an edited
reply stays a reply. One of that pair without the other names nothing and is refused. Answering stored mail needs
`mailfathom.mail.read` beneath the drafting grant, because an answer is derived from the message it answers.

**An upload is the request body and nothing else.** There is no form to parse and no boundary to trust: the octets are
the body, the file's name is the `fileName` query value, and what it declares itself to be is the request's own
`Content-Type` without its parameters — a request declaring none is read as `application/octet-stream` rather than
having its content examined. The bound is the deployment's own configured attachment size, applied by the routing
pipeline as well as by the use case, so a body past it is refused before it is buffered; how many files a draft may
carry is the deployment's configured attachment count. Cancelling an upload leaves nothing behind, and a file already
taken in is removed by naming it.

**A refused send says which rule refused it and never what triggered it.** Screening, the recipient policy, and the
spending ceilings each answer with their own error code beside a sentence written for an operator to read, and the
sentence carries no recipient, no subject, and no fragment of the message. `409` is a rule of this deployment about
what the author already wrote, so no rewriting of the request would help; `503` is the one temporary refusal here and
the only one worth retrying, and means the message was neither refused nor sent because what would have judged it could
not be reached. Nothing on any of these routes reaches a log, a span attribute, or a telemetry event.

**Nothing has been transmitted when a send answers.** The message is queued, and the outbox routes below are where a
client watches what becomes of it.

### The outbox routes

These are what a client draws after a message has been sent: the send the owner just asked for has not left yet, and
this is where the screen watches it go, takes one back while it is still queued, or offers a failed one another chance.

| Route | What it does |
| --- | --- |
| `GET /api/client/outbox` | Reads one page of the named account's outbox, optionally narrowed to one stage |
| `GET /api/client/outbox/{outgoingEmailId}` | Reads one send, with what the server told this deployment about each recipient |
| `POST /api/client/outbox/cancellation` | Withdraws one send that has not begun transmitting |
| `POST /api/client/outbox/requeue` | Offers one send again |

**A listing names an account and never the deployment.** The narrowing is required rather than optional, so there is no
unnarrowed reading here at all: one that fell back to every account would page through every owner's outgoing mail,
which is the deployment-wide catalog an owner-facing surface must never compose. An account another owner owns is
refused exactly as one nobody configured, and a send another owner made answers exactly as one nobody made.

**What the answers carry is what [the administrative outbox](admin-endpoint.md) already settled.** A page names no
recipient and no subject, because a page of an outbox would otherwise be an export of who this owner writes to, a page
at a time; one send read by identity names its recipients and what the server said about each, because that is the
question it was asked. Neither reads the message, at any size.

**A decision reports what became of the send it named rather than refusing.** A send that has gone past the point of
recall, and one this owner does not hold, are outcomes in the answer — which is exactly what somebody acting on a
screen a moment old needs. Only a request naming no send at all is refused. Each decision names one send and never a
set, because a message whose outcome nobody knows may already be in somebody's mailbox, so a filtered re-queue would be
an unknown number of duplicates asked for in one request.

**Every route here is `mailfathom.mail.send`, the two readings included.** What an outbox says is what this owner is
sending, so a credential granted to read a mailbox learns nothing here, and withdrawing a send is part of sending
rather than a power beside it.

### The telemetry routes

These three are an OTLP receiver, served for the client's own instrumentation and for nothing else. A client exports to
`/api/client/telemetry` the way it would export to a collector — the paths beneath it are the OTLP specification's own,
so the exporter is pointed at the prefix and appends nothing — and what it sends is forwarded to wherever this
deployment already exports [its own telemetry](telemetry.md#the-one-switch-otel_exporter_otlp_endpoint).

| Route | What it takes |
| --- | --- |
| `POST /api/client/telemetry/v1/traces` | One `ExportTraceServiceRequest` as `application/x-protobuf` |
| `POST /api/client/telemetry/v1/metrics` | One `ExportMetricsServiceRequest`, same encoding |
| `POST /api/client/telemetry/v1/logs` | One `ExportLogsServiceRequest`, same encoding |

**A deployment that named no collector does not serve them at all.** The routes are mapped only where
`OTEL_EXPORTER_OTLP_ENDPOINT` is set, so a client exporting to a deployment that exports nothing is answered `404` and
learns from the answer rather than from a setting it would have to be told about. That is the same shape the rest of
this surface uses for an endpoint that is off, and it is what keeps the default a privacy default: a deployment that
decided its own signals stay in the process does not become a relay for somebody else's.

**They authenticate exactly as every other route here does**, on this surface's own credentials, and they add no name to
[the published permission set](permissions.md) — like the session route, and for a narrower reason: a permission would
have to be granted to every client that reports anything, which makes it a component of every grant rather than a
decision anybody takes. What the credential does decide is whose telemetry this is. The resource attributes naming the
owner are written here from the authenticated credential and **replace** whatever the client put in their place, so a
page cannot report as somebody else however its bundle was modified. Nothing else in the payload is transformed: the
batch is forwarded as it arrived, so a signal a client's own instrumentation names arrives at the collector under that
name.

**Read attribution off the resource, which is the level this holds at.** A client is free to write anything into a
span, a log record, or a metric data point, a key spelled like the owner one included, exactly as it is free to write
anything into a span's name — those are its own words about its own work, they are forwarded untouched like every other
field it sends, and nothing here reads them. Reaching them would mean decoding all three signals against their schemas,
which is what would make this a processor rather than a proxy. So a dashboard, a query, or a retention rule that has to
know whose telemetry it is reads the **resource** attribute, and that one is the deployment's own and cannot be
influenced from a browser.

**What it forwards to is the deployment's, never the client's.** There is no second destination setting, and the
collector's address and its credential never leave the process — a client holding either would be publishing them to
whoever opened the developer tools. The endpoint, the protocol, the headers, and the per-export timeout are the ones
the deployment's own exporter reads.

**Everything is bounded**, per credential, and a refusal names the bound rather than truncating the batch:

| Bound | What it is | What a client is answered |
| --- | --- | --- |
| Request body | 4 MiB | `413`, carrying the specification's own status message |
| Records in one batch | 10 000 spans, metric points, or log records | `413`, with nothing forwarded |
| Export rate | 120 requests a minute for one signed-in person, and the same again for the next | `429` with `Retry-After: 60` |

A batch the endpoint cannot read as a protocol-buffers export request is answered `400` and forwarded nowhere: a
deployment's collector is not the place to find out whether a client's encoder works. A collector that answered a
partial success is relayed verbatim rather than reported as a success, because a client that was told everything
arrived stops holding what did not. A collector that refused the batch outright is answered in this endpoint's own
words instead: what it said is about this deployment's own export — a version it does not speak, a message it could not
read — and is not a document to hand to a browser. Anything transient — a collector that is down, unreachable, slower
than the configured timeout, or refusing this deployment's own collector credential — is answered `503`, which is what
leaves the batch where the client can still send it again. That last one is a deployment fault rather than a client
one, so it is reported to the operator as [its own
condition](telemetry.md#what-the-client-telemetry-proxy-emits) rather than folded into the others.

**Accepting and forwarding writes no log record at any level.** A signed-in client exports every few seconds for as
long as it is open, so a line per batch would make a deployment's own logs unreadable within a day. A forwarding
failure does write one, aggregated per condition rather than per batch and repeated at most every five minutes, naming
the condition and how many batches it has cost since it was last reported; it carries no part of a payload. What the
proxy accepted, refused, forwarded, and failed to forward is [reported as
metrics](telemetry.md#what-the-client-telemetry-proxy-emits) instead, which is where a rate belongs.

**A browser reaches them under the origins this surface already declares.** The preflight is answered from
[the same list](#browser-origins) every other route here is served to, and no origin is admitted here that the rest of
the surface refuses.

## Credentials do not cross surfaces

A key admitted under `McpEndpoint` or `AdminEndpoint` authenticates nothing here, and one admitted here authenticates
nothing there. The separation is mechanical rather than conventional: each surface registers its own authentication
schemes and its own authorization policy, and a policy consults only its own schemes.

`Authentication` takes the same entries the MCP section takes — one entry per accepted method, each naming a `Method` of
`password`, `api-key`, `public-key`, or `oauth-subject` — and every method is documented once, under
[the MCP endpoint](mcp-endpoint.md#authentication). The credentials themselves are rows beside the owner rather than
settings here, and one credential is presented on whichever surface accepts its method; what keeps the two apart for a
signed assertion is the audience it names, `urn:mailfathom:client`, so an assertion minted to read a mailbox as an agent
cannot sign in as somebody's mail client.

A grant is recorded on the credential rather than on the entry, and it draws from the mailbox half of the published set
— a name reaching only the administrative half is refused where the credential is provisioned.
[Writing a grant](permissions.md#writing-a-grant) is the whole of that rule; nothing about it is particular to this
surface.

## Signing a person in

Two of the four methods sign a person in rather than a client, and which of them a deployment offers is what it
configures.

**A username and password** is what the client's own sign-in screen asks for. A deployment that runs no authorization
server reaches for it; the entry names the method and the credentials are provisioned over
[the administrative endpoint](admin-endpoint.md#owner-credentials):

```json
{
  "ClientEndpoint": { "Enabled": true, "Authentication": [ { "Method": "password" } ] }
}
```

The method is documented once, under [the MCP endpoint](mcp-endpoint.md#passwords) — including the bound on guessing,
the indistinguishable refusal, and what this surface's transport does to it: **a password crossing a clear-text hop is
reported at every startup and never refused**, naming this surface and its port, because this process can read the
scheme of its own socket and nothing beyond it. A loopback deployment behind nothing, and a public socket nobody meant
to expose, are one reading from here.

A caller admitted this way acts for **the owner the credential belongs to**, and so does every other method here: each
of the four resolves a record beside one owner. That is what lets one deployment serve more than one person's mail over
one address.

**An access token** is the other, and it is what a deployment that already runs an authorization server uses.
MailFathom is a protected resource only — it signs nobody in, holds no user, and issues no token — so what a deployment
configures here is which authorization server's tokens are believed, and each person's subject is mapped onto their
owner record with `mfctl credential create --method oauth-subject`.

**Every `OAuth` entry must name a `Resource` ending in `/api/client`.** Startup refuses anything else, naming the
setting. The reason is discovery rather than OAuth: the client is configured with an address and finds the
protected-resource metadata document by appending the prefix it is about to call, which reaches the document's RFC 9728
location exactly when the resource names the same prefix. It matters more here than on the administrative surface,
because the reader is given an origin and nothing else — the client refuses an address carrying anything beneath one —
so every other address it uses is one it derived rather than one anybody could correct.

The document is published at the root, beneath `/.well-known/oauth-protected-resource`, and it follows its surface: it
answers on the client listener and is `404` on any other. It is served to a caller holding nothing, which is the whole
point of publishing one — its reader has not authorized yet and is trying to find out where to.

```http
GET /.well-known/oauth-protected-resource/api/client
```

Behind a reverse proxy, write the public URL and keep the path: `https://mail.example.test/api/client`. What a token
must prove, and why the advertised scope list is longer than the checked one, is
[under the MCP endpoint](mcp-endpoint.md#oauth); none of it differs here.

## Browser origins

A WebAssembly head calls this surface from a page, and a preflight this endpoint cannot answer is a client that never
starts. The same setting exists on the MCP and administrative endpoints, configured separately.

| `ClientEndpoint:Cors:AllowedOrigins` | What the endpoint answers |
| --- | --- |
| absent | Every browser origin |
| `["*"]` | Every browser origin, stated |
| `["https://mail.example.test"]` | Exactly the origins listed |
| `[]` | No browser origin, which still serves every client that sends no `Origin` |

The permissive default is deliberate, for the reason [the MCP endpoint's is](mcp-endpoint.md#cors-and-the-origin-header):
a surface is protected by the credential a caller presents rather than by which page it was called from, and a first run
that failed a preflight would look like a broken deployment. Narrow it to the origin your own head is served from once
you know what that is. The empty list is the third posture rather than an oversight — it advertises nothing to a
browser, which is what a deployment whose client is a desktop or mobile head wants, since neither is subject to CORS at
all.

The policy allows `GET` and `POST`, the `Authorization`, `Content-Type`, `Accept`, and `traceparent` request headers,
and exposes `WWW-Authenticate` so a page can read the challenge that tells it where to authorize and `Retry-After` so a
client told to hold can read for how long. `traceparent` is the W3C trace context the client sends on every request, and
it is the only propagation header this surface admits — [what a client-originated trace
contains](telemetry.md#what-a-client-originated-trace-contains) is what it carries and why nothing else is allowed
beside it. **Credentials are never allowed
under any of the postures above**: a browser that could attach an ambient cookie would let a page act as whoever is
signed in somewhere else, and this surface's credential is a bearer token the client sets deliberately.

## What bounds the traffic

`ClientEndpoint:RateLimiting` and `ClientEndpoint:RequestTimeout` carry the same keys, defaults, and validation the
other two surfaces carry, and are configured independently of them: neither one's traffic reaches another's limits. Both
apply whether or not anyone wrote a number, which is what stops a surface reachable from a page from serving unbounded
key guessing.

**Both are attached to this surface's routes rather than applied as the process's default policy**, which is what keeps
the health probes answering while the client endpoint is refusing. A default limiter would count a readiness probe
against the same capacity a browser is spending, and a deployment under load would start failing the probe that decides
whether it is taken out of service.

## Transport security

The surface takes the same `Transport` setting the other two do, with the same three modes and the same clear-text
redirect — see [`Transport`](configuration-endpoints.md#transport). It is `Http` by default, which is right behind a
TLS-terminating reverse proxy and wrong anywhere else, so startup says so:

- an enabled endpoint with no `Authentication` entry warns that anything reaching the address is served the mailbox, and
  names the section that fixes it;
- an enabled endpoint terminating no TLS warns that any credential a client presents is readable on the path, and names
  its clear-text port;
- an endpoint that accepts [a password](#signing-a-person-in) on that port warns again, separately, because a password
  is the one credential here a person typed and may have typed elsewhere — it names this surface, the port, and, where
  `ReverseProxy:TrustedProxies` names one, that the hop is the one between the proxy and this process;
- an endpoint serving [the page](#serving-the-client-from-the-deployment) over clear text an operator explicitly
  permitted reports that separately, at every startup, naming the permission rather than assuming it is still true.

All three are warnings rather than refusals, because a loopback bind, a private network, and a proxy that terminates
TLS are each a deployment where one of them is the right answer, and only an operator knows which they have. Serving
the page is the one part that is refused rather than warned about, for the reason the next section gives — and that
refusal is about publishing a page rather than about a credential, which is why a deployment can accept a password on
this port while still having to declare that the page may be served over it.

## Serving the client from the deployment

**Every published image carries the page**, built into it from the client workspace under `frontend/` and copied
beneath the image's web root. Serving it pulls nothing, starts no second process, and costs an image that never serves
it about 230 kB. What it is today is an early client: it draws its own sample data rather than this deployment's mail,
so a person can open it and nothing signs in to it yet.

One setting serves a bundle from this endpoint's own listeners, where a host was given one:

```jsonc
{
  "ClientEndpoint": {
    "Enabled": true,
    "Application": { "Enabled": true }
  }
}
```

It is off in every default — in the configuration, in the chart's `values.yaml`, in the Compose file, and in the Quadlet
unit — so adopting a release publishes no page. Each deployment asset turns it on with one decision of its own:
`client.enabled` in the chart and `MAILFATHOM_CLIENT` in Compose each write both keys below, while the Quadlet unit
carries them as two adjacent commented `Environment=` lines, because a `.container` file has no variable to write two
keys through. [Kubernetes](deployment-kubernetes.md), [Compose](deployment-compose.md), and
[Quadlet](deployment-quadlet.md) each state theirs.

**What it adds is static files and nothing else.** The bundle answers the root of the listeners this endpoint is served
on, and the routes beneath `/api/client` are unchanged: same credentials, same grants, same limits. The page itself
carries no credential and needs none — a browser has to load the application before it can obtain one — and what that
application then calls is authorized exactly as any other caller is. Turning this on grants nobody anything; it puts
the client in front of the sign-in the endpoint already required.

Serving it from the same origin as the surface it calls is the point of serving it here at all: the page then needs no
cross-origin permission and `Cors:AllowedOrigins` has nothing to say about it. A client downloaded and installed rather
than served reaches the same routes from an origin of its own and does need one.

**Clear text is refused rather than warned about**, and that is the one difference from every other posture on this
page. A page is what a person types their credential into, so a deployment that serves it over a socket this process
opened in the clear fails at startup naming the two ways out: terminate TLS here, with `ClientEndpoint:Transport` set
to `HttpsOnly` and a `ClientEndpoint:Https:Endpoints` profile; or state that something in front of this process already
did, by writing `ClientEndpoint:Application:AllowClearText: true`. Nothing here can tell an ingress terminating TLS
from a socket published to a network, which is why the second one is a declaration an operator makes rather than
something MailFathom infers. A socket that only redirects to HTTPS serves nothing and needs no permission.

Two more refusals belong to the same setting. Writing `Application:Enabled` while `ClientEndpoint:Enabled` is off fails
at startup rather than being ignored, and so does enabling it on a host that carries no bundle — which is every host
today, and was already the answer for a service run straight from the sources. It says so instead of answering a page
of 404s.

There is no client-certificate profile here. The trust question a certificate answers is a second one this endpoint does
not yet ask; where it is served is stated in exactly the settings the existing endpoints use, so the day it does ask,
the answer arrives as a profile on a listener already shaped to carry one.

## Publishing it

Behind an ingress, publish `/api/client` as its own path to the same backend the other surfaces are on and keep the
prefix — the client appends the rest to the address it was configured with, and the `Resource` written above has to be
the public URL a browser actually reaches. The chart's `ingress.hosts[].paths` is where that goes;
[deploying on Kubernetes](deployment-kubernetes.md) is the page.

An ingress answers no preflight on the application's behalf, so `Cors:AllowedOrigins` still has to name the origin the
page is served from even where the ingress and the page share a host.

Serving the page needs one more path published: `/`, to the same backend, so the bundle and the routes it calls arrive
on one origin. Publish that only where the page is actually enabled — a path routed to a deployment that serves no
client is a `404` with an ingress in front of it.
