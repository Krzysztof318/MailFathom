# The client endpoint

<!-- describes: backend/src/Host/Configuration/Endpoints/ClientEndpointOptions.cs, backend/src/Host/Configuration/Endpoints/ClientApplicationOptions.cs, backend/src/Host/Api/ClientApiEndpoints.cs, backend/src/Host/Api/ClientMailAccountsEndpoint.cs, backend/src/Host/Api/ClientMailFoldersEndpoint.cs, backend/src/Host/Api/ClientMailTimelineEndpoint.cs, backend/src/Host/Api/ProtectedResourceMetadataEndpoint.cs, backend/src/Host/Security/Endpoints/ClientTransportSecurityExtensions.cs, backend/src/Host/Hosting/ClientApplicationFiles.cs, backend/src/Host/Hosting/Warnings/ClientTransportSecurityWarning.cs -->

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
        "OAuth": {
          "Resource": "https://mail.example.test/api/client",
          "AuthorizationServers": [
            {
              "Name": "workforce",
              "Issuer": "https://sso.example.test/realms/mailfathom",
              "AuthorizedSubjects": [ "11111111-2222-3333-4444-555555555555" ]
            }
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

**A local `aspire run` configures everything but this key**: a loopback socket of its own and the browser head's own
origin as the one origin it answers, because that run starts a head that has to reach something. It does not turn the
surface on — that stays a developer's act here as much as anywhere else.
[The client resource](local-development.md#the-client-resource) is what to state locally, and what is already stated
for you.

## What it serves

| Route | Grant it needs |
| --- | --- |
| `GET /api/client/session` | none |
| `GET /api/client/accounts` | `mailfathom.mail.read` |
| `GET /api/client/folders` | `mailfathom.mail.read` |
| `GET /api/client/emails` | `mailfathom.mail.read` |

There is no version segment in either path: the major version is `0` and
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
permits breaking the contract outright, so `/v1` would be scaffolding for a promise this project has not made.

### The session route

It answers with three fields: `service`, which is always `MailFathom`; `version`, the running release; and
`permissions`, the published names the credential just presented carries, in the order this project publishes them.

That is what a client needs before it has drawn a single message: that this is MailFathom rather than something else
answering the port, which contract it speaks, and what the rest of the surface will serve it. It is also what lets
sign-in be built and proven end to end before a screen exists — a client that reached here with a token it had just been
issued knows the token works.

**It names no credential**, which is the one way it differs from what
[`GET /api/admin/session`](admin-endpoint.md) answers. That surface's reader is `mfctl` in an operator's own hands, and
the deployment's configured name for the credential that authenticated is what tells them which of their own entries let
them in. This surface's reader is a page holding a token, which brought no name and has nothing to do with one; a
response echoing a deployment's configured identity for a credential would be a way to read configuration back out of
the service from a browser.

A caller granted nothing reads an empty `permissions` list rather than a refusal, because "nothing" is the accurate
answer to what such a caller may do, and because a credential retired by narrowing its entry to nothing should be
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
a mail screen. A sender, a subject fragment, or a phrase from the body is search, which ranks rather than orders, and
is not this route.

The filters are part of what a cursor was issued for, so changing any of them — or the `order` — makes a cursor taken
before the change belong to a different list. Changing `pageSize` alone does not: page size moves no boundary.

**The page is served from the local copy.** Nothing here contacts a mail server, so no screen waits on IMAP, and
reading a folder cannot set the remote `\Seen` flag. How current that copy is, per folder, is what
[the folders route](#the-folders-route) answers; it is not repeated on every page.

**An owner with no mail account reads an empty `emails` list**, and a credential whose grant does not carry
`mailfathom.mail.read` is answered `403`.

## Credentials do not cross surfaces

A key configured under `McpEndpoint` or `AdminEndpoint` authenticates nothing here, and one configured here
authenticates nothing there. The separation is mechanical rather than conventional: each surface registers its own
authentication schemes and its own authorization policy, and a policy consults only its own schemes.

`Authentication` takes the same entries the other two sections take — one entry per credential, each carrying an
`ApiKey` block, a `PublicKey` block, an `OAuth` block, or any combination of them — and every one of them is this
endpoint's own. Each method is documented once, under [the MCP endpoint](mcp-endpoint.md#authentication). What differs
here is the audience a signed assertion names, `urn:mailfathom:client`, which is what keeps a credential minted to read
a mailbox as an agent from signing in as somebody's mail client even where one client is registered on both.

A grant written on an entry draws from the mailbox half of the published set, so a name or a pattern reaching only the
administrative half fails startup naming the entry's index. [Writing a grant](permissions.md#writing-a-grant) is the
whole of that rule; nothing about it is particular to this surface.

## Signing a person in

The credential this surface is built around is an access token a public client obtained under the authorization code
flow with PKCE. MailFathom is a protected resource only — it signs nobody in, holds no user, and issues no token — so
what a deployment configures here is which authorization server's tokens are believed and which subjects they may be
issued to.

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

This is the one control the administrative endpoint has no use for. Its clients are command-line tools with no origin to
be told anything, while a WebAssembly head calls this surface from a page — and a preflight this endpoint cannot answer
is a client that never starts.

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

The policy allows `GET`, the `Authorization`, `Content-Type`, and `Accept` request headers, and exposes
`WWW-Authenticate` so a page can read the challenge that tells it where to authorize. **Credentials are never allowed
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
- an endpoint serving [the page](#serving-the-client-from-the-deployment) over clear text an operator explicitly
  permitted reports that separately, at every startup, naming the permission rather than assuming it is still true.

Both are warnings rather than refusals, because a loopback bind, a private network, and a proxy that terminates TLS are
each a deployment where one of them is the right answer, and only an operator knows which they have. Serving the page
is the one part that is refused rather than warned about, for the reason the next section gives.

## Serving the client from the deployment

The MailFathom container image carries the client's browser head, and one setting serves it from this endpoint's own
listeners:

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
than served — the desktop head — reaches the same routes from its own origin and does need one.

**Clear text is refused rather than warned about**, and that is the one difference from every other posture on this
page. A page is what a person types their credential into, so a deployment that serves it over a socket this process
opened in the clear fails at startup naming the two ways out: terminate TLS here, with `ClientEndpoint:Transport` set
to `HttpsOnly` and a `ClientEndpoint:Https:Endpoints` profile; or state that something in front of this process already
did, by writing `ClientEndpoint:Application:AllowClearText: true`. Nothing here can tell an ingress terminating TLS
from a socket published to a network, which is why the second one is a declaration an operator makes rather than
something MailFathom infers. A socket that only redirects to HTTPS serves nothing and needs no permission.

Two more refusals belong to the same setting. Writing `Application:Enabled` while `ClientEndpoint:Enabled` is off fails
at startup rather than being ignored, and so does enabling it on a host that carries no bundle — the files travel
inside the container image, so a service run straight from the sources serves the API surfaces alone and says so
instead of answering a page of 404s.

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
