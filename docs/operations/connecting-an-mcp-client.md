# Connecting an MCP client through your identity provider

<!-- describes: src/Host/Configuration/Access/**, src/Host/Security/Mcp/OAuthTokenValidation.cs, src/Host/Security/Mcp/OAuthAuthorizationServerMetadataRetriever.cs, src/Host/Security/Mcp/InsufficientScopeResultHandler.cs, src/Host/Security/Transport/ProtectedResourceMetadataAddress.cs, src/Common/OAuth/OAuthMetadataAddresses.cs -->

You have MailFathom running and you want an MCP client signed in through the identity provider you already operate.
This page is the order those steps happen in. It is the only page written from that side: the settings themselves belong
to [the MCP endpoint](mcp-endpoint.md#oauth), which states what each one means and what a token has to prove, and this
page links into it rather than restating any of it.

Almost all of the work is outside MailFathom. Configuring MailFathom is one JSON block. What a first connection actually
costs is a resource identifier chosen once, an application registered per client, a callback URL copied out of the
client rather than invented, a token-endpoint authentication method that has to agree on both sides, one switch in the
provider that makes the token's audience come out right, and knowing where to find a subject identifier. None of that is
MailFathom's to own, and all of it decides whether MailFathom answers.

> This page names other companies' products in order to describe interoperating with them. Those names belong to their
> owners, MailFathom is affiliated with none of them, and nothing here is an endorsement in either direction. A
> provider's or a client's own documentation is authoritative about its console; a dialog described here may have been
> rearranged since.

## What MailFathom is not

MailFathom is an [OAuth 2.1 protected resource](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
and never an authorization server. If you arrive from a product that does both, four things you will look for are not
here and never will be:

| Not here | Where it is instead |
| --- | --- |
| A login page, a consent screen, or a password prompt | Your authorization server's, reached by the client's browser |
| Token issuing, code redemption, refresh tokens | Your authorization server's; none of them ever reaches MailFathom |
| A user store, user records, group membership | Your authorization server's; MailFathom keeps an issuer, a subject, and the scopes, and drops every other claim |
| Client registration, client secrets | An arrangement between the client and your authorization server |

So there is nothing in MailFathom to create a user in, and no place to type a password. What MailFathom holds is a list
of the issuers it trusts and, per issuer, the subjects it serves.

## Who chooses how the client registers

Three registration shapes are in use, and **which are available is the client's decision rather than MailFathom's** —
the client's own dialog is where it is made. MailFathom neither advertises nor constrains the choice, because the
registration is between the client and your authorization server and MailFathom sees only the token that comes out.

| Shape | What the client does | What your authorization server needs |
| --- | --- | --- |
| **Static credentials** | You paste a client ID, and a secret where the server requires one, into the client's dialog | An application you registered by hand, with the client's callback URL allowed |
| **Dynamic Client Registration** ([RFC 7591](https://datatracker.ietf.org/doc/html/rfc7591)) | Registers itself the first time it connects, and reuses what came back | A `registration_endpoint` in its metadata, reachable from the client's network, and a registration policy that permits it |
| **Client ID Metadata Document** | Sends a URL it hosts as the `client_id`; the server fetches it to learn the client's redirect URIs and authentication method | `client_id_metadata_document_supported` in its metadata, and outbound access to fetch that URL |

Static credentials are the shape to reach for when the other two are unavailable, or when you want one application per
organization that you can see and revoke in your own console. Dynamic registration asks the least of an operator, at the
cost of a fresh client registration per connection accumulating in the provider. A metadata document asks about as
little and registers nothing, which is why a client that supports both usually prefers it.

**A client may offer no alternative to OAuth.** Some connector dialogs take exactly two answers — no authentication, or
OAuth — so where such a client is the one connecting, an [API key or a key pair](mcp-endpoint.md#api-keys) is not a
fallback you can retreat to. Decide that before you start, because it changes whether this page is optional.

## Which steps repeat and which do not

The second client is minutes of work, and this is why. Everything in the left column is done once for the deployment,
whatever connects to it afterwards:

| Once per deployment | Once per client | Once per person |
| --- | --- | --- |
| Choose the resource identifier | Register an application in the provider | Find the subject identifier |
| Make the token's audience match it | Allow that client's callback URL | Add it to `AuthorizedSubjects` |
| Define the scope, if you require one | Set the token-endpoint authentication method | |
| Write the `OAuth` entry in MailFathom | Paste the credentials into the client, where it takes them | |
| Verify the endpoint answers | Point the client at the server URL | |

Adding a second person to an existing client is the right-hand column alone: one `sub` in `AuthorizedSubjects` and a
restart. Adding a second client is the middle column. Nothing in the left column is touched again unless the deployment
moves to a new public address.

## The sequence

The worked example throughout is Keycloak, chosen because it is open source and self-hostable so you can follow it
without an account anywhere. Field names in **bold** are its console's. Every host, realm, identifier, and subject below
is a placeholder — substitute your own. [Another provider](#another-provider) is the same sequence with different names.

### 1. Fix the resource identifier

Everything else is derived from this one value, so choose it first and do not change it afterwards. It is the canonical
public URL clients reach the endpoint at, including the fixed `/mcp` path:

```text
https://mail.example.com/mcp
```

Three things read it, which is why it has to be exactly right before anything is configured: it becomes
`McpEndpoint:Authentication[].OAuth.Resource`, it is the audience every token must carry, and it decides where the
metadata document is published — for the value above, at
`https://mail.example.com/.well-known/oauth-protected-resource/mcp`.

It has to be HTTPS, and it is the address your clients use rather than an internal one. Behind a reverse proxy that is
the proxy's public URL; [the reference](mcp-endpoint.md#oauth) says why it is never derived from a request header.

**A client will compare it, so take the value from the metadata document rather than from your configuration file.** At
least one widely used client requires the `resource` field of that document to equal the server URL the operator typed
into it, character for character and path included — and MailFathom publishes the identifier in
[a canonical form](mcp-endpoint.md#oauth) that need not be the characters you wrote. `/mcp` and `/mcp/` are two different
identifiers, so a trailing slash here is a failed connection rather than a cosmetic difference.

### 2. Register MailFathom as a resource in the provider

The provider needs something to represent the thing being protected, because that is what a token's audience names.

**In Keycloak** there is no separate resource-server object: an audience is a string a mapper writes, so this step and
the next are the same step. Create a **client scope** under **Client scopes** → **Create client scope**, and give it the
name you intend to require as a scope — `mailfathom.read` in the examples below. Leave **Protocol** as
`openid-connect` and leave **Include in token scope** on, so the name reaches the token's `scope` claim. **Type** here
decides only whether clients created later receive this scope automatically; which clients actually get it is the
assignment in [step 4](#4-register-an-application-for-the-client), so `None` is the safe answer and nothing depends on
it.

One client scope then carries both halves of what a token must prove: the audience, from the mapper added next, and the
scope, from its own name.

If you require no scope, create the client scope anyway and call it something descriptive. It exists to carry the
mapper.

### 3. Make the token's audience agree

**This is the step that is not guessable, and the one most first connections fail on.** An MCP client asks for a token
with the [RFC 8707](https://www.rfc-editor.org/rfc/rfc8707.html) `resource` parameter. Several widely deployed
authorization servers do not act on that parameter at all — they decide the audience from their own configuration — so
the client asks correctly, the server issues a token for something else, and MailFathom refuses it with a `401` that by
design says nothing about why. There is no setting in MailFathom that relaxes the audience check, deliberately.

**In Keycloak**, open the client scope from step 2, go to its **Mappers** tab, choose **Configure a new mapper** and
select **Audience**. Then:

- leave **Included Client Audience** empty — it offers the realm's own clients, and the audience here is a URL rather
  than a client;
- write the resource identifier from step 1 into **Included Custom Audience**;
- leave **Add to access token** on.

That is the whole of it. A resource-indicator implementation is
[open work upstream](https://github.com/keycloak/keycloak/issues/47117), so treat the mapper as the arrangement rather
than as a workaround for something about to change.

### 4. Register an application for the client

One application per MCP client, so that revoking one client does not revoke the others.

**In Keycloak**: **Clients** → **Create client**, **Client type** `OpenID Connect`, and a **Client ID** naming the
client rather than MailFathom. On the next step leave **Standard flow** on — the authorization-code flow is what an MCP
client performs — and turn **Direct access grants** off, since an MCP client never uses it and
[step 8](#8-verify-before-you-touch-the-client) uses a client of its own for the one place it is useful.

**Client authentication** is the switch that has to agree with the client:

| The client is | **Client authentication** | What the client sends at the token endpoint |
| --- | --- | --- |
| A public client — no secret to keep | Off | Nothing but PKCE; `token_endpoint_auth_method` is `none` |
| A confidential client — an operator pasted a secret | On, and the **Credentials** tab is where the secret is | The client ID and secret |

A mismatch here is the failure that says least: the sign-in fails without either side reporting which of them expected
what. Choose from what the client actually does, not from which sounds safer. A client registering dynamically or
through a metadata document is a public client — leave it off.

Finally, assign the client scope: the client's **Client scopes** tab → **Add client scope** → the scope from step 2, as
**Default**. Without this the tokens this client receives carry neither the audience nor the scope, and every one of
them is refused.

### 5. Take the callback URL from the client

**A callback URL belongs to the client and is generated by it — never invented and never MailFathom's.** Some clients
publish one fixed address; others mint one per connector when the connector is created, which means the value has to be
copied again if the connector is deleted and recreated. Read it out of the client's own dialog, usually under an
advanced or developer section, and paste it into the provider.

**In Keycloak** that goes into the client's **Valid redirect URIs**, which takes several entries so the surfaces of one
client can share an application.

Two shapes are worth expecting, because they are configured differently:

- **A hosted client** redirects to one HTTPS address on the vendor's own domain, and it has to match exactly.
- **A client running on the operator's machine** uses an
  [RFC 8252](https://datatracker.ietf.org/doc/html/rfc8252#section-7.3) loopback redirect on a port that changes every
  session, and declares `http://localhost/callback` and `http://127.0.0.1/callback`. Your provider has to match those
  ignoring the port, or the sign-in fails on whichever session picked a new one. Do not respond by listing ports.

Neither address is one MailFathom ever sees. An authorization code is redeemed between the client and the
authorization server, and MailFathom is handed the finished token.

A client that registers dynamically or through a metadata document supplies its own redirect URIs as part of that
registration, and this step disappears — which is most of why those shapes are less work.

### 6. Find the subject identifier

`AuthorizedSubjects` is required and at least one entry is needed, because MailFathom serves one owner's mail to
everyone it admits: without the list, every colleague who can obtain a token for this resource reads that mailbox. What
goes in it is the `sub` the authorization server issues, and **an email address is not it** — a subject is what a server
promises never to reuse, and an address is reassigned to whoever holds the mailbox next.

**In Keycloak** it is the **ID** shown on the user's own page under **Users**, a UUID.

The reliable way is to ask the server, because it answers with the value the token will actually carry rather than with
whatever the console labels an identifier. With an access token in hand — see the next step for one way to get one —
the standard endpoint answers it:

```console
$ curl -sS -H "Authorization: Bearer $ACCESS_TOKEN" \
    https://sso.example.test/realms/mailfathom/protocol/openid-connect/userinfo | jq -r .sub
9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04
```

Do this once per person who will use the deployment.

### 7. Write the MailFathom entry

Now the part that is MailFathom's, and it is one block:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": [
      {
        "OAuth": {
          "Resource": "https://mail.example.com/mcp",
          "RequiredScopes": [ "mailfathom.read" ],
          "AuthorizationServers": [
            {
              "Name": "workforce",
              "Issuer": "https://sso.example.test/realms/mailfathom",
              "AuthorizedSubjects": [ "9f2c7c1e-8a4d-4c62-9f0b-3d2a1b5e7c04" ]
            }
          ]
        }
      }
    ]
  }
}
```

`Resource` came from step 1, `RequiredScopes` from step 2, and `AuthorizedSubjects` from step 6; `Name` is a label of
your own that diagnostics report. `Issuer` is the one new value and the one to be careful with: **copy it verbatim from
the provider, trailing slash included**, because it is compared to a token's `iss` by exact string equality and is the
one identifier MailFathom deliberately never rewrites. Several widely deployed servers publish an issuer whose entire
path is one trailing slash, and tidying it by hand produces a deployment that starts cleanly and then refuses every token
that server issues. In Keycloak, **Realm settings** → **General** → **Endpoints** → **OpenID Endpoint Configuration**
opens the discovery document, and its `issuer` field is the value to copy.

Nothing else about the server is configured here: MailFathom finds the discovery document itself, at addresses it derives
from the issuer, and takes the key set address out of it.

The section is read once while the host is composed, so this takes effect on restart.

[The reference](mcp-endpoint.md#oauth) is where the rules on these four live: what happens with several entries, why
every entry must agree on `Resource`, why `Name` and `Issuer` are unique across the whole list, and what leaving
`RequiredScopes` empty means. Two more things it records are worth knowing before the next step. A deployment behind a
TLS-terminating proxy must name that proxy in
[`ReverseProxy:TrustedProxies`](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy) — an access token is refused
outright when the request did not arrive over TLS, and with no proxy named that refusal stops working rather than
becoming stricter. And an `OAuth` entry may sit beside an `ApiKey` or `PublicKey` entry: a request is served when it
satisfies any one of them, which is how a scheduled job keeps its own credential while people sign in.

### 8. Verify before you touch the client

Three commands, in this order. Each isolates one thing, so a failure at step *n* means everything before it is right —
which is worth much more than discovering the same failure through a client dialog that reports "couldn't connect".

**The metadata document answers, and describes this deployment:**

```console
$ curl -sS https://mail.example.com/.well-known/oauth-protected-resource/mcp | jq
{
  "resource": "https://mail.example.com/mcp",
  "authorization_servers": [ "https://sso.example.test/realms/mailfathom" ],
  "scopes_supported": [ "mailfathom.read" ],
  "bearer_methods_supported": [ "header" ],
  "resource_name": "MailFathom"
}
```

Check `resource` against what you will type into the client, and `authorization_servers` against your issuer. A
deployment configuring several authorization servers publishes them all here, and a client may use only the first —
order them accordingly.

**An unauthenticated request is refused, and says where to authorize:**

```console
$ curl -sS -i -X POST https://mail.example.com/mcp \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer resource_metadata="https://mail.example.com/.well-known/oauth-protected-resource/mcp"
```

That header is what starts the whole flow: the client reads the pointer, fetches the document, discovers the
authorization server, and signs in. A `401` carrying no `resource_metadata` parameter, or a `404` on the address above,
means the client has nothing to follow.

**A token is accepted.** Getting one by hand needs a grant that runs with nobody at a browser, and the direct grant is
the short way where your provider permits it. Use a client of its own for this rather than the one from step 4 — in
Keycloak a public client with **Direct access grants** on and **Standard flow** off — so the registration your MCP client
depends on is never modified for a probe, and delete it when you are done:

```console
$ ACCESS_TOKEN=$(curl -sS -X POST \
    https://sso.example.test/realms/mailfathom/protocol/openid-connect/token \
    -d grant_type=password \
    -d client_id=mailfathom-connection-check \
    -d 'scope=openid mailfathom.read' \
    -d username=operator -d password="$PROVIDER_PASSWORD" | jq -r .access_token)
```

Assign the client scope from step 2 to that probe client as well, or the token comes back without the audience and the
check below fails for a reason that has nothing to do with MailFathom. The password reaches `curl` as a variable
reference rather than as a literal on purpose: an argument list is readable by every process on the machine. Then:

```console
$ curl -sS -o /dev/null -w '%{http_code}\n' -X POST https://mail.example.com/mcp \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
200
```

`200` means the token satisfied the issuer, the audience, the subject, and the scopes, and the transport answered. Drop
`-o /dev/null -w` to read the tool listing itself; the transport may frame the JSON-RPC message on a `data:` line of an
event stream rather than returning it as the body, which is its choice and not a fault.
[Verifying an enabled endpoint](mcp-endpoint.md#verifying-an-enabled-endpoint) says which tools should be in that
listing and why one of them may legitimately be absent.

A `401` or `403` here is a configuration answer, not a mystery — [the table below](#what-a-failure-looks-like) maps each
to the setting behind it.

### 9. Connect the client

The client asks for one fact, and it is the value from step 1:

```text
https://mail.example.com/mcp
```

Everything else it discovers. Where the client also asks for a client ID, and a secret if you configured a confidential
client, those are the ones from step 4. Then the client opens a browser, the person signs in against your provider, and
the tool listing appears.

## What a failure looks like

Every refusal is deliberately uninformative to the caller — an expired token, a wrong audience, an unknown issuer, and
an invalid signature are one answer, because a protected resource that explained itself would be explaining itself to
whoever was probing it. **The server log is where they differ**, and it is the first place to look. This table is the
second:

| What you see | What it usually is |
| --- | --- |
| `401` on every call, sign-in itself succeeded | The audience. The token was issued for something other than `Resource` — [step 3](#3-make-the-tokens-audience-agree) |
| `401`, and the log names an unknown issuer | `Issuer` does not match the token's `iss` exactly. Check the trailing slash — [step 7](#7-write-the-mailfathom-entry) |
| `401` after a successful sign-in, on a deployment behind a proxy | The token was refused because the request did not arrive over TLS. The scheme is read after forwarded headers are applied, so either the proxy sends no `X-Forwarded-Proto` or its address falls outside [`ReverseProxy:TrustedProxies`](mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy). Do not answer it by emptying that list — that trusts every peer and turns the refusal off rather than fixing the hop |
| `403` naming no scope | The subject is not in `AuthorizedSubjects` for that issuer. Signing in again cannot fix it, which is why it is not a `401` — [step 6](#6-find-the-subject-identifier) |
| `403` naming a scope in `WWW-Authenticate` | The token is missing a required scope. The client scope is not assigned, or not `Default`, or **Include in token scope** is off — [step 4](#4-register-an-application-for-the-client) |
| `404` on the metadata address, everything else working | The request did not arrive under the scheme and host `Resource` names. Behind a proxy, the forwarded scheme and host are missing or the peer is outside `TrustedProxies` — [discovery a client uses](mcp-endpoint.md#discovery-a-client-uses) |
| The client reports it cannot reach the server, and the provider logs no traffic at all | Discovery never completed, so the client never learned where to authorize. Run the first two commands of [step 8](#8-verify-before-you-touch-the-client) |
| The sign-in page never appears, or the redirect is rejected | The callback URL is not allowed, or not matched ignoring the port for a loopback client — [step 5](#5-take-the-callback-url-from-the-client) |
| The sign-in fails at the token exchange | The token-endpoint authentication method disagrees. A public client sending nothing to a confidential registration, or the reverse — [step 4](#4-register-an-application-for-the-client) |
| `429` where you expected `401` | The [rate limiter](mcp-endpoint.md#rate-limiting) ran out of capacity before authorization was reached. A flood of bad credentials is meant to cost the sender something |
| A startup failure naming `McpEndpoint:Authentication:0` | The entry itself. The [reference](mcp-endpoint.md#authentication) lists what is refused before anything binds |

## Another provider

The sequence is the same and the names differ. Microsoft Entra ID, for example, has an app registration in place of a
client and an **Application ID URI** in place of the audience mapper — and it is that URI, rather than a mapper, that
has to be the resource identifier from step 1, or the token request is refused by the provider before MailFathom sees
anything. Auth0 issues a `sub` of the form `auth0|…` for step 6.

This page walks one provider on purpose. A per-vendor matrix would be a page that ages every time a console is
rearranged, and the part worth writing down is the order — which is the same everywhere, because it comes from the
protocol rather than from the vendor. Take the sequence, and read your provider's own documentation for the names.

No screenshots either, for the same reason and one more: a picture of somebody else's console goes stale silently, while
a named field and a path can be checked against their documentation.

## Related

- [The MCP endpoint](mcp-endpoint.md#oauth) — every setting named here, what a token must prove, and what the endpoint
  publishes. The reference for this page's whole subject.
- [Signing in with OAuth](admin-endpoint.md#with-oauth) — the administrative endpoint takes the same entries and its own
  audience rule, which is a `Resource` ending in `/api/admin`. `mfctl` performs that sign-in for you.
- [Mailbox OAuth](mailbox-oauth.md) — the other direction entirely: MailFathom authenticating *to* a mail provider. A
  different key ring, a different set of registrations, and nothing on this page applies to it.
- [Getting started](../users/getting-started.md) — the guided path from an empty deployment to a first tool call.
