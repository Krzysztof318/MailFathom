# Administering a deployment

<!-- describes: src/Host/Configuration/Endpoints/AdminEndpointOptions.cs, src/Host/Api/**, src/Host/Hosting/Startup/SurfaceIsolation.cs, src/Host/Hosting/Warnings/AdminTransportSecurityWarning.cs, src/Host/Security/Endpoints/TransportListenerBinder.cs, src/Host/Security/Transport/TransportRateLimiting.cs, src/Cli/** -->

How the `mfctl` command reaches a running deployment, and what that deployment has to have enabled before it will
answer.

MailFathom is administered over HTTP. The command never reads the service's configuration, never opens its database, and
never touches its secret store — every operation it performs is a request to the administrative endpoint. That is what
lets it run on your own machine, on Linux or Windows, against a deployment running somewhere else entirely.

## The endpoint is off unless you turn it on

A deployment that configures nothing serves no administrative surface. Enabling it opens a listener of its own:

```jsonc
{
  "AdminEndpoint": {
    "Enabled": true,
    "BindAddress": "127.0.0.1",
    "Port": 8090,
    "Authentication": [
      { "ApiKey": { "Name": "workstation", "SecretReference": "systemd-credential:admin-workstation-key" } }
    ]
  }
}
```

**The listener is its own, and that is the point.** Administrative routes answer on the administrative listener and
nowhere else, and nothing else answers on it — a request for `/mcp` that arrives on the administrative port is refused
before it reaches the protocol surface, and a request for `/api/admin` that arrives on the MCP port is refused before it
reaches any credential check. Both are answered `404`, because the honest answer is that nothing is served there.

A port another listener in this process already binds fails startup naming the section, rather than failing later with
an address-in-use error that names a socket.

## Credentials do not cross surfaces

An API key configured under `McpEndpoint` authenticates nothing here, and one configured here authenticates nothing
there. Reading a mailbox and administering the service that reads it are different authorities, and the separation is
mechanical rather than conventional: each endpoint registers its own authentication schemes and its own authorization
policy, and a policy consults only its own schemes.

`Authentication` takes the same entries `McpEndpoint:Authentication` takes — one entry per credential, each carrying an
`ApiKey` block, a `PublicKey` block, an `OAuth` block, or any combination of them — and every one of them is this
endpoint's own. A misspelled key fails startup rather than binding a default. Each method is documented once, under
[the MCP endpoint](mcp-endpoint.md#authentication): what a key is, what a
[key pair](mcp-endpoint.md#key-pairs) is and what a client signs to present one, and what a token must prove. The
difference here is the audience an assertion names — `urn:mailfathom:admin` rather than `urn:mailfathom:mcp` — which is
what keeps a credential minted to read a mailbox from administering the service even where one client is registered on
both.

**With an `OAuth` entry configured, every one of them must name a `Resource` ending in `/api/admin`** — the path these routes answer
beneath. Startup refuses anything else, naming the setting. The reason is discovery rather than OAuth: `mfctl` is handed
a host and a port and finds the metadata document by appending that prefix, which reaches the document's RFC 9728
location exactly when the resource names the same one. A deployment whose resource said something else would publish a
document nothing could find, and OAuth sign-in would be unreachable for a reason no refusal would explain. Behind a
reverse proxy, write the public URL and keep the path: `https://mail.example.test/api/admin`.

> **Every authenticated caller may perform every administrative operation.** There is no permission model. The
> credential is what bounds access, so provision one per client and rotate it like any other secret.
>
> Weigh that against what the operations are. The endpoint serves one read — who a credential makes the caller — and
> one write, which stores a mailbox refresh token. Any credential that can do the first can therefore do the second, so
> an administrative key is as sensitive as the mailbox credentials it can place.

## What the endpoint serves

| Route | What it does |
| --- | --- |
| `GET /api/admin/session` | Reports the credential that authenticated and the running version. This is what `login` and `status` ask. |
| `POST /api/admin/mailbox/refresh-token` | Stores a mailbox refresh token for one configured account, sealed under the deployment's data-encryption key. This is what [`mfctl mailbox authorize --account`](mailbox-oauth.md#sending-the-token-to-the-deployment) sends. |
| `GET /api/admin/mailbox/mutations/audit` | Reads one account's record of the changes MailFathom made to its mailbox, where that account [keeps one](../features/imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default). |

The write route's body carries a long-lived credential for a named mailbox owner, which is what makes the clear-text
warning below matter more here than it does for a session probe. It refuses, with `400` and a sentence naming what was
wrong, an account this deployment does not configure and a body missing either field; a second grant for the same
account replaces the first rather than adding to it. It reads at most 16 KB, which is far more than any authorization
server's refresh token and far less than the server's own default. It answers with no body at all, so nothing it stores
can be read back out through it.

Storing seals the token under the deployment's [data-encryption key](secret-provisioning.md). A deployment that
configures no key ring cannot store one, and the route answers `500` rather than a refusal it can explain, because
nothing about the request was wrong.

### Reading what MailFathom changed

The audit route serves one bounded, keyset-paginated page of one account's finished changes, newest first. The account
is required rather than optional, and that is deliberate: the answer says where a person's mail has been and at whose
instruction, so a caller names whose history they are reading rather than asking for a deployment-wide list.

| Query parameter | What it does |
| --- | --- |
| `account` | Required. The configured identifier of the account whose trail is read. |
| `mutation` | Narrows to one change: `relocate`, `delete`, `set-seen`, or `copy`. |
| `from`, `before` | Narrows to entries that ended within a range; `from` is inclusive and `before` is exclusive. |
| `pageSize` | Between 1 and 200; 50 when omitted. |
| `cursor` | The `nextCursor` the previous page returned. |

```console
$ curl -sS -H "X-API-Key: $MAILFATHOM_ADMIN_KEY" \
    "http://127.0.0.1:8081/api/admin/mailbox/mutations/audit?account=work&mutation=delete&pageSize=2"
```

The response carries the entries and, while more remain, the cursor the next page is asked with. **A walk ends when no
cursor comes back**, never by comparing a short page against the size you asked for. A cursor names a boundary within
the filters it was issued for, so presenting one alongside different filters is refused with `400`; changing only the
page size is not, because pacing is not a filter. Every other refusal is `400` too, with a sentence naming what to
change: an account this deployment does not configure, a mutation name that is not one of the four, a page size outside
the range, a range that ends where it begins, and a cursor this deployment did not issue.

Nothing in the answer is mail. Folder paths, UIDs, the local email identifier, the requester, the two timestamps, and
the outcome are what an entry holds, which is what makes the route readable without exposing the message it is about.

**Erasing entries for a data-subject request.** Retention erases what has outlived each account's configured window, and
that is the ordinary path. A request that reaches further — erase everything held about one person's mail now — is
answered against the table directly, because the trail deliberately survives the deletion of the mail it describes and
therefore has no cascade to ride:

```sql
DELETE FROM mailbox_mutation_audit_entries
WHERE "MailboxAccountId" = 'work'
  AND "StoredEmailId" = ANY($1);
```

The identifiers are the local email identifiers the entries name, which the same account's mailbox queries return for
the messages in scope; erasing the whole of one account's trail is the same statement without the second predicate.
Take it as a deliberate administrative act on a database you have a backup of: nothing here replays an erasure, and the
entries it removes are the accountability evidence for the changes they recorded.

## Rate limiting

An enabled endpoint is bounded, whether or not anyone wrote a number. That is what stops an administrative surface
reachable from a network from serving unbounded API-key guessing, which is the attack it is most exposed to and the one
where a successful guess is worth the most.

`AdminEndpoint:RateLimiting` is the same section `McpEndpoint:RateLimiting` is, with the same keys, the same product
defaults, and the same validation. [Rate limiting](mcp-endpoint.md#rate-limiting) is where the settings, the ranges, the
reasoning, and what a refused request receives are recorded in full;
[configuration reference](configuration-reference.md#rate-limiting--mcpendpointratelimiting-and-adminendpointratelimiting)
is the key table.

Two things differ here, and both follow from where the credential is judged:

- **The burst is the endpoint's, not one caller's.** These routes carry no authentication middleware of their own — the
  credential is judged by the authorization middleware, which runs *behind* the limiter so that a request about to be
  refused for a wrong key has still spent capacity. There is therefore no identity to partition on when the limiter
  counts, and every administrative caller shares one bucket. Size `TokenCapacity` as what the whole endpoint may burst
  to rather than what one operator may.
- **Neither endpoint's traffic reaches the other's limits.** The partitions are keyed per surface, so a key spelled the
  same way under both sections is two independent buckets, and an agent that exhausted the MCP endpoint's capacity has
  taken nothing from the surface you would use to stop it.

The two endpoints' concurrency limits are separate for the same reason: a runaway agent saturating `/mcp` must not lock
you out of `/api/admin`.

Turning the limits off is an explicit value and costs one startup warning, as it does on the MCP endpoint.

## Three postures the endpoint warns about

None is refused, because each is legitimate somewhere and only you know which you have.

| Startup warning | What it means |
| --- | --- |
| No authentication method turned on | Anything that can reach the address can administer the service. Right only for a loopback bind or a network you control. |
| Served in clear text | Any credential a client presents is readable on the path. Right only behind a TLS-terminating reverse proxy, or on a loopback bind. |
| `AdminEndpoint:RateLimiting:Enabled` set to `false` | Nothing bounds how fast a caller may present wrong credentials. Right only where something in front of the process already bounds the traffic reaching it. |

Configure `AdminEndpoint:Https:Endpoints` to have Kestrel terminate TLS itself. It takes the same profile shape the MCP
endpoint's does, including `HttpProtocols`, which defaults to HTTP/1.1 and HTTP/2. Naming any profile binds those
listeners and no clear-text one stays open behind them serving these routes.

### Redirecting `mfctl` after you configure TLS

A profile also binds one clear-text listener whose only answer is a `308` to the address the profiles are served at, on
**port 8091** unless you state another. It is what keeps an `mfctl` profile that still holds an `http://` endpoint from
failing as though the deployment were down:

```console
$ curl -i http://admin.example.com:8091/api/admin/session
HTTP/1.1 308 Permanent Redirect
Location: https://admin.example.com:8543/api/admin/session
```

**Repoint the profile rather than relying on it.** An administrative API key sent in clear text was on the wire before
anything answered, and this route stores mailbox credentials — a redirect protects the next request and never the one that
arrived. `mfctl login --endpoint https://admin.example.com:8543` writes the corrected address; see
[working with more than one deployment](#working-with-more-than-one-deployment).

That listener maps no route. No administrative operation, no session probe, and no protected-resource metadata document is
reachable over it, and no credential check runs for a request that arrived on it — every path gets the same redirect, and a
`Host` header naming no configured domain gets `400`. The port is checked against every other listener in the process, so a
port the MCP surface or the probes also bind is shared rather than refused. What the two surfaces must then agree about is the socket itself — the scheme, the redirect, the client-certificate question — while their credentials, limits, and HTTPS ports stay their own; [which settings a shared socket couples](configuration-reference.md#which-settings-a-shared-socket-couples) is the table.

Turn it off with `AdminEndpoint:Https:Redirect:Enabled` set to `false`, which is what a deployment behind a proxy that
already answers the clear-text port wants. The setting shape and every refusal are the MCP endpoint's, documented once in
[redirecting a client still pointed at `http://`](mcp-endpoint.md#redirecting-a-client-still-pointed-at-http); only the
default port differs, so enabling TLS on both surfaces opens two clear-text ports that do not collide.

## Behind a TLS-terminating reverse proxy

If a proxy holds your certificate, the request states the public name it arrived under, which is what lets
the endpoint's OAuth discovery complete over a proxied address. `ReverseProxy:TrustedProxies` is what limits who may
state it; left empty it is anybody.
[Behind a TLS-terminating reverse proxy](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) documents that in
full, including what the unnamed default gives up; three things are worth stating from this endpoint's side.

- **It is one process-wide setting, not one per endpoint.** This surface is a separate listener over the same request
  pipeline, so naming your proxy once covers it along with the MCP and probe listeners. There is no
  `AdminEndpoint:ReverseProxy`, deliberately.
- **The OAuth entry's `Resource` is unaffected.** It stays the value you wrote, still ends in `/api/admin`, and is
  still what a token's audience is compared against. The mode never derives it from a header.
- **A proxy that authenticates its own callers is not this endpoint's authentication.** `AdminEndpoint:Authentication`
  still decides who may administer the service, and the clear-text warning above still fires, because the hop between
  the proxy and this process is still clear text.

Whether the proxy publishes this listener at all is your decision: the administrative port is separate from the
application port, so a deployment can proxy the MCP surface publicly and keep this one on a network you control.

## Getting the command

Each release attaches a self-contained binary per platform, plus one checksum file covering all of them.

Download the one for the machine you administer *from* — the command talks to a deployment over HTTP, so it does not
have to run where the service runs.

| Platform | Asset |
| --- | --- |
| Linux, x86-64 | `mfctl-<version>-linux-x64` |
| Linux, ARM64 | `mfctl-<version>-linux-arm64` |
| Windows, x86-64 | `mfctl-<version>-win-x64.exe` |
| Windows, ARM64 | `mfctl-<version>-win-arm64.exe` |

Nothing needs installing beside it: the .NET runtime is inside the file.

**No binary is signed**, on any platform, so Windows warns about an unknown publisher when you run one and the checksum
file is the only thing that distinguishes a genuine download from a tampered one. Check it in the directory you
downloaded into, before running anything:

```bash
sha256sum --check --ignore-missing 'mfctl-<version>.sha256'
```

`--ignore-missing` is what lets one file cover four binaries: it checks the ones present and says nothing about the
three platforms you did not download. **`<version>` is the release you downloaded** — substitute it, and note that the
name is quoted so a line pasted without that substitution fails with a missing file rather than with a redirection.

The command binaries carry no build provenance attestation either. That is the other question worth asking about a
download — the checksum says the bytes are the ones published, and an attestation would say which workflow and commit
produced them — and the image and the chart are where this repository answers it.
[The container image](container-image.md#published-images) records how.

### On Windows, through winget

The two Windows binaries are offered through the Windows Package Manager as well, so a Windows machine has a packaged
path beside the download:

```console
> winget install MailFathom.mfctl
```

It is a portable package: winget places the binary, puts `mfctl` on your `PATH`, and `winget upgrade` carries you to
the next release. The manifest names the same release asset the table above does and carries the same hash the checksum
file does, so both paths install the same bytes and check them the same way. That is the whole of what it checks: what
the two paragraphs above say about signatures and provenance is unchanged, and a package the Windows Package Manager
offers is not thereby a package Windows knows a publisher for.

A release submits its own manifest and the community repository's review is what accepts it, so a version is offered
through winget a little after it is attached here. **No version has been accepted yet**, which is the thing to know
before trying it: until the first one is, [the releases page](https://github.com/Krzysztof318/MailFathom/releases) is
where the command comes from on every platform.

## Signing in

`--mode` chooses how the credential is produced, and it is stated rather than guessed — guessing would put a machine
with no browser on a redirect that can never arrive.

| Mode | What it does | When |
| --- | --- | --- |
| `key` (default) | Reads one credential from standard input | An API key, or an access token you obtained elsewhere |
| `keypair` | Signs each request with a private key on this machine | A scheduled job, or anywhere a stored credential is one too many |
| `interactive` | Opens a browser here and catches the redirect | You are at the machine you are administering from |
| `device` | Prints a code to enter on another device | A jump host, or anything without a browser |

### With an API key

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production
Administrative credential (an API key, or an access token from the configured authorization server):
Signed in to https://mail.example.test:8443 as 'workstation' (MailFathom 0.2.0), saved as profile 'production' and selected.
```

The credential is read from standard input rather than taken as an argument, because an argument reaches the shell
history, the process list, and any log of either. A script pipes it in instead:

```console
$ printf '%s' "$MAILFATHOM_KEY" | mfctl login --endpoint https://mail.example.test:8443
```

### With a key pair

Generate a pair on the machine that will run the command, and give the deployment the public half only:

```console
$ openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out ~/.config/MailFathom/production.key
$ chmod 600 ~/.config/MailFathom/production.key
$ openssl pkey -in ~/.config/MailFathom/production.key -pubout
```

Register that public key under `AdminEndpoint:Authentication` as a `PublicKey` entry — see
[Key pairs](mcp-endpoint.md#key-pairs) for the block and what it accepts — then sign in:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production --mode keypair \
    --private-key ~/.config/MailFathom/production.key
Signed in to https://mail.example.test:8443 as 'reporting-job' (MailFathom 0.4.0), saved as profile 'production' and selected.
No credential was stored. Every command signs a short-lived assertion with the key at
/home/you/.config/MailFathom/production.key, so keep that file readable by this account alone and the sign-in lasts as
long as the deployment accepts its public half.
```

**Nothing presentable is written down.** The profile records where the key lives and no credential at all, and every
later command reads that key and signs a fresh assertion that expires within the minute. A credentials file that leaves
this machine — in a backup, a synced folder, a support bundle — therefore carries nothing anyone could present, which is
the difference from every other mode. The key itself is never copied into the store: it stays where you generated it,
under the permissions you gave it.

The path is made absolute when it is stored, because a scheduled job rarely runs from the directory you signed in from.
Move the key and sign in again; there is nothing to revoke in between, because nothing was issued.

This mode needs no browser, no authorization server, and no interactive step, so it is the one to reach for in a cron
entry or a systemd timer. Signing in is still verified against the deployment, which is what proves it holds the matching
public half before the first real command runs.

### With OAuth

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production --mode interactive --client-id mfctl

A browser has been opened for you. If it did not appear, open this address yourself:

  https://sso.example.test/realms/mailfathom/protocol/openid-connect/auth?client_id=mfctl&response_type=code&...

Waiting for the sign-in to come back to http://127.0.0.1:8765/...
Signed in to https://mail.example.test:8443 as 'kasia' (MailFathom 0.2.0), saved as profile 'production' and selected.
The access token is renewed for you until the refresh token expires or is revoked, and the sign-in ends when it does.
```

**Only `--client-id` is configured.** Which authorization server to use, the resource the token must be issued for, and
the scopes to ask for all come from the deployment: it publishes an [RFC 9728](https://www.rfc-editor.org/rfc/rfc9728)
metadata document at `/.well-known/oauth-protected-resource/api/admin`, and the server it names publishes where to
authorize. Nothing is transcribed, so nothing is transcribed wrongly.

Register the command as a **public client** with an authorization-code grant, PKCE required, and the redirect address
`http://127.0.0.1:8765/`. It ships as a binary anyone can download, so it holds no client secret and presents none.
Pass `--redirect-uri` if you registered a different loopback port, and `--issuer` if the deployment accepts tokens from
more than one authorization server — with several configured, the command asks rather than picking one, because they
are separate populations of people.

`--mode device` needs none of that redirect machinery:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --mode device --client-id mfctl

Open this address on any device with a browser:
  https://sso.example.test/device

and enter the code: WDJB-MJHT
The code expires at 2026-08-03 12:10:00Z. Waiting for the sign-in to complete...
```

It requires the authorization server to publish a device authorization endpoint; one that does not is reported as that
rather than left polling.

### What happens either way

**The credential is verified before it is stored.** A deployment that refuses it, an address serving no administrative
endpoint, and a host that answers with something that is not MailFathom all fail here rather than at some later command.

`--name` is what the deployment is remembered as; without it the profile takes the host name. Signing in also selects
the profile, because it is the deployment you just chose to work with.

When a deployment issues a new credential, sign in again by profile name rather than by address — `mfctl login
--endpoint production` — and the address it already holds is reused.

## When the connection is weaker than the default

A deployment on an internal host commonly serves a certificate no workstation trusts — self-signed, or issued by an
authority only your organization carries — and some are reached over `http://` at all. Neither is refused outright and
neither is waved through: `login` asks about it once, records the answer on the profile, and no later command asks
again. Both questions default to no, and refusing either stores nothing and signs in to nothing.

### A certificate this machine does not trust

Nothing happens for a deployment whose certificate validates on its own; the question exists only where it does not.

```console
$ mfctl login --endpoint https://mail.internal.example:8443 --name internal

https://mail.internal.example:8443 presented a certificate this machine does not trust:

  Subject:     CN=mail.internal.example
  Issuer:      CN=Example internal authority
  Fingerprint: 3B:9A:1C:…:7F
  Valid:       2026-01-04 09:12:00Z to 2027-01-04 09:12:00Z
  Not trusted: this machine does not trust the chain it was presented with (UntrustedRoot)

Accepting it stores this fingerprint on the profile. Every later command then accepts this certificate and refuses any other,
so a deployment that renews or replaces its certificate is signed in to again rather than trusted silently.

Trust this certificate for this profile? [y/N]: y
Signed in to https://mail.internal.example:8443 as 'workstation' (MailFathom 0.5.0), saved as profile 'internal' and selected. The connection is protected by a pinned certificate rather than by a chain this machine trusts; the profile now accepts 3B:9A:1C:…:7F and refuses any other.
```

Read the fingerprint against the deployment's own before answering — `openssl x509 -in server.crt -noout -fingerprint
-sha256` prints it in the same form. Nothing is sent until you answer: the handshake was refused, so the credential was
never on the wire.

**A pin is stricter than what it replaces, not weaker.** Ordinary chain validation accepts any certificate a trusted
authority signed; a pinned profile accepts one certificate and refuses every other, including one your machine would
have trusted on its own. That is what makes accepting a self-signed certificate once safe to live with — a later
substitution fails as loudly as an untrusted certificate does today, naming both fingerprints.

The consequence is that a **renewed certificate ends the profile's connection until you accept the new one**. Run
`mfctl login --endpoint internal` again: the sign-in starts from ordinary validation, presents whatever the deployment
now serves, and asks again. `mfctl logout` removes the pin with the profile.

The pin covers the deployment and nothing else. An OAuth sign-in reaches an authorization server as well, and every
request to it goes out under ordinary chain validation, because a fingerprint taken at your deployment says nothing
about the machine your identity platform runs on.

### An endpoint reached over `http://`

An address is taken as written and no scheme is guessed onto a bare host, so `http://` is a decision — one that is easy
to make out of habit:

```console
$ mfctl login --endpoint http://mail.internal.example:8090 --name internal

http://mail.internal.example:8090 is an HTTP address, so nothing protects this connection.
The credential you are about to present, and every later request from this profile, cross the network in clear text.
A redirect the deployment might send to an https:// address would not change that: the credential is already on the wire by then.

Sign in over an unprotected connection anyway? [y/N]:
```

The redirect sentence is the part worth taking seriously. `mfctl` never follows a redirect — that is what stops a
request carrying a bearer credential from being moved to an address you did not name — and
[the redirect this endpoint serves](#redirecting-mfctl-after-you-configure-tls) protects the *next* request rather than
the one that arrived. So the question is asked from the address alone, before anything is sent, and a deployment that
would have answered `308` never gets to answer it. Sign in to the `https://` address instead wherever there is one.

Accepting is recorded on the profile and widens nothing else: a clear-text profile that later answers over HTTPS with
an untrusted certificate is still refused.

### Signing in with nobody at the terminal

`--mode key` reads the credential from standard input, so a piped sign-in has no terminal to read an answer from. Both
questions are therefore stated up front instead, and a sign-in that needed one and did not get it fails naming the
switch rather than prompting into the pipe:

```console
$ printf '%s' "$MAILFATHOM_KEY" | mfctl login \
    --endpoint https://mail.internal.example:8443 --trust-untrusted-certificate
```

| Switch | What it accepts |
| --- | --- |
| `--trust-untrusted-certificate` | Whatever certificate the deployment presents at this sign-in. It is pinned to the profile exactly as an interactively accepted one is, so the switch weakens the one sign-in rather than the profile it produces. |
| `--allow-clear-text` | That an `http://` endpoint carries the credential and every later request unprotected. |

There is deliberately no fingerprint to pass: somebody who had to obtain the fingerprint first could have installed the
certificate instead. Neither switch has any effect on a deployment whose transport is already protected.

Nothing on the service side changes for any of this, and no configuration key turns certificate validation or
clear-text protection off globally. These are the client's decisions about one deployment.

## How long an OAuth sign-in lasts

An access token is typically minted for an hour, and you should never notice. Every command checks the stored token
before it sends anything and exchanges the refresh token for a new one when it is within a minute of expiring, which is
what keeps that hour from being an hourly interruption.

**The refresh token itself is never renewed, and a rotated one is not adopted.** When the authorization server answers a
renewal with a new refresh token, the command keeps the one issued at sign-in and discards the new one. That is
deliberate: adopting it would make your session last as long as you kept using it, and revoking your access at the
authorization server would then take effect only whenever you happened to stop.

The service does the opposite with *its* OAuth credentials, and the difference is the point rather than an
inconsistency. A synchronizing account is a headless process that must keep reading a mailbox indefinitely with nobody
there to sign it in, so it [follows a rotated refresh token](mailbox-oauth.md) and stores it. A `mfctl` session belongs
to a person who is present, can sign in again in seconds, and whose access someone may need to revoke — so it ends.

The cost is worth stating plainly, because it depends on a setting that is not MailFathom's. **On an authorization
server that invalidates the old refresh token when it rotates one — Keycloak and Entra ID do this by default — the
session ends at the second renewal rather than at the refresh token's own expiry.** It ends cleanly, naming what
happened:

```console
$ mfctl status
The sign-in has ended: the authorization server no longer accepts the stored refresh token ('invalid_grant').
Run 'mfctl login --endpoint <address>' to sign in again.
```

If that is too short for how you work, turn refresh-token rotation off for this client at the authorization server. The
session then runs to the refresh token's configured lifetime, which is the length your identity platform already
governs — and which is the only place that decision belongs, since MailFathom issues no tokens at all.

## Working with more than one deployment

Every profile is a deployment you are signed in to, and one of them is the one commands act on.

```console
$ mfctl profiles
* production  https://mail.example.test:8443  workstation
  staging     https://staging.example.test:8443  workstation

$ mfctl switch staging
Now acting on 'staging' (https://staging.example.test:8443) as 'workstation'.
```

`--endpoint` overrides the selection for one invocation without changing it, and takes either a profile name or an
address:

```console
$ mfctl status --endpoint production
'production' (https://mail.example.test:8443) accepts the stored credential as 'workstation' (MailFathom 0.2.0).
```

The order is the option, then `MAILFATHOM_ENDPOINT`, then the profile last switched to: what you typed beats what your
shell was told, and both beat what you chose last time. `status` is what asks a deployment whether the stored credential
still works, which is how a revoked or expired key is distinguished from an unreachable host.

`mfctl logout` forgets one profile — the selected one, or whichever `--endpoint` names. It does not revoke anything: the
credential stays valid until the deployment stops accepting it. Forgetting the selected profile leaves none selected
rather than promoting a neighbour, so the next command asks which deployment you mean instead of quietly reaching a
different one.

Every command that needs a credential and has none says so, and says what to run:

```console
$ mfctl status
Not signed in. Run 'mfctl login --endpoint https://host:port' first.
```

## Where the credential is kept

| Platform | Path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/MailFathom/credentials.json`, or `~/.config/MailFathom/credentials.json` |
| Windows | `%APPDATA%\MailFathom\credentials.json` |

One entry per profile, keyed by the name rather than by the address, so a deployment that moves port or gains a domain
keeps its profile instead of becoming a second entry. On Linux the file and its directory are created owner-only, and
created that way rather than tightened afterwards — a file created readable and corrected later is readable for the
moment in between.

**Tokens are encrypted in the file** with AES-256-GCM, under a random key generated on first use and kept beside the
store as `credentials.key`; on Windows that key file's contents are additionally wrapped with DPAPI under the current
user. Each token is bound to its own endpoint, so a value moved between entries does not decrypt.

An OAuth profile holds a refresh token as well, sealed the same way and bound to the same endpoint — it is the
longer-lived of the two secrets, so anything weaker would be a regression in the value most worth protecting. Beside it
sit the values a renewal needs and that are not secrets: the token endpoint, the issuer, the client identifier, the
resource, the scopes, and when the access token expires. They are recorded rather than rediscovered because a renewal
happens on a command somebody is waiting on, and re-reading two discovery documents to spend a refresh token would put
two more round trips in front of every expired session. A deployment that moves one of them is answered by signing in
again.

**A key-pair profile stores no credential at all.** It records the absolute path of the private key and nothing else, so
there is no sealed token in the file and nothing an attacker could present even if the key file's protection failed. The
path is not a secret and is stored in clear; what it names is, and it is protected by that file's own permissions rather
than by anything here.

**A profile that accepted something about its transport records that too**, beside the endpoint and in clear: the
pinned certificate's SHA-256 fingerprint, and whether the connection is unprotected. Neither is a secret — a fingerprint
is what the deployment presents to anybody who connects — and what they protect is that the profile keeps talking to the
same deployment. A profile that accepted nothing beyond the default records nothing, so the presence of the entry is
itself the statement that something was accepted, and a file written before the entry existed reads as an ordinary
profile rather than failing.

Be clear about what that buys. A credentials file that leaves the machine — in a backup, a synced folder, a support
bundle, a screenshot of a directory listing — discloses nothing on its own. Someone already able to read your files on
your machine can read the key too, and on Linux nothing prevents that; the file mode is what answers that case, and the
encryption answers the copy. Holding the credential in the platform's own secret service is tracked as
[#318](https://github.com/Krzysztof318/MailFathom/issues/318).

## Troubleshooting

| What you see | What it means |
| --- | --- |
| `Not signed in.` | No profile exists yet. Run `mfctl login --endpoint https://host:port`. |
| `No default profile is set.` | Profiles exist but none is selected, which is what forgetting the selected one leaves behind. Run `mfctl switch <name>`. |
| `There is no profile named …` | A typo, or a profile that was never created. The message lists the ones that exist. |
| `Not signed in to https://…` | `--endpoint` named an address no profile serves. Sign in to it, or name a profile instead. |
| `The deployment refused the credential.` | The key is not one this endpoint is configured with, or its lifetime has ended. Note that an MCP API key is not one of them. |
| `answered 429` | The endpoint refused the request for its rate limit rather than for its credential. `Retry-After` on the response says when capacity returns where the limiter can compute one. The whole endpoint shares one bucket, so another caller's burst — including somebody guessing keys — is enough to cause this. |
| `serves no administrative endpoint at /api/admin/…` | The address answered, but on a listener that serves something else. Check the port, and check that `AdminEndpoint:Enabled` is true. |
| `This deployment configures no mail account named …` | `mailbox authorize --account` named an identifier no `MailSynchronization:Accounts` entry carries, or you are signed in to the wrong deployment. Nothing was stored. |
| `The deployment refused the grant without saying why.` | The request was refused with no reason in the answer, which is what something in front of the endpoint answering `400` looks like. Check that `--endpoint` reaches the deployment itself. |
| `rather than storing the token` | The endpoint answered with neither an acceptance nor an explained refusal. The token was not stored and the account is unchanged. A `500` here is most often a deployment with no `DataEncryption` key ring, which is what a stored token is sealed under; its own log names the cause. |
| `did not identify itself as MailFathom` | Something else is answering on that port — a proxy, or another service. |
| `could not be reached` | Nothing is listening, or a firewall is in the way. The endpoint binds only what `BindAddress` names; `127.0.0.1` is unreachable from another machine by design. |
| `presented a certificate this machine does not trust` | On `login`, the question described in [when the connection is weaker than the default](#when-the-connection-is-weaker-than-the-default). On any other command, a profile that holds no pin met a certificate that stopped validating — sign in again to review it. Nothing was sent either way. |
| `presented a certificate this profile has not pinned` | The deployment's certificate is not the one this profile accepted. Both fingerprints are named. A renewal is the ordinary cause and `mfctl login` is the answer; anything else is worth finding out about before you accept it. |
| `The deployment's certificate was refused` | You answered no. Nothing was signed in to and nothing was stored. |
| `Transport protection was refused` | You answered no to the clear-text question. Sign in to the `https://` address, or accept the unprotected connection. |
| `there is no terminal to ask on` | A piped or non-interactive `login` met one of the two questions. Pass `--trust-untrusted-certificate` or `--allow-clear-text`, whichever the message names. |
| `did not answer in time` | The connection was accepted and no answer arrived within 30 seconds, so the address and the port are right and the deployment is what to look at — an overloaded host, a stalled process, or a firewall that drops rather than refuses. |
| `The stored credential could not be read.` | The credentials file and the key that opens it no longer match, which is what a store copied from another machine or another user looks like. Sign in again to replace it. |
| `No deployment was named.` | `login` needs an address the first time. Pass `--endpoint`, or set `MAILFATHOM_ENDPOINT`. |
| `The sign-in has ended` | The refresh token expired, was revoked, or was invalidated by a server that rotates them. Run `login` again. This names the *stored* token, so it only ever appears on a command that had a session; a `login` that fails names what it presented instead. |
| `did not accept the code the redirect carried` | The authorization code was already redeemed or had expired by the time it was exchanged, which is what a redirect answered twice or approved long after it was opened looks like. Run `login` again. |
| `The device code is no longer valid` | Nobody finished at the verification address before the code expired, or the authorization server withdrew it. Run `login --mode device` again. |
| `not a usable web address` | The authorization server published a `verification_uri` that is not an absolute `http` or `https` address, so there is nothing to put in front of the person signing in. This is a fault at the authorization server rather than in its configuration here. |
| `publishes no OAuth metadata` | The endpoint accepts API keys only. Sign in with one, or ask the operator to add an `OAuth` entry to `AdminEndpoint:Authentication`. |
| `accepts tokens from several authorization servers` | More than one is configured and only you know which population you belong to. Name it with `--issuer`. |
| `issued no refresh token` | The client was not granted offline access, so the session would end within the hour. Grant it at the authorization server. |
| `no device authorization endpoint` | That authorization server offers no device grant. Sign in from a machine with a browser. |

## Related

- [MCP endpoint](mcp-endpoint.md) — the other protected surface, and the one this is deliberately separate from
- [Secret provisioning](secret-provisioning.md) — how an API key reference is backed by material
- [Configuration reference](configuration-reference.md) — every key in the `AdminEndpoint` block
