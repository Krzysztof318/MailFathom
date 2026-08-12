# Connecting the chat client you already use

<!-- describes: src/Mcp/**, src/Host/Security/**, src/Host/Configuration/Endpoints/McpEndpointOptions.cs -->

[Getting started § connect an MCP client](getting-started.md#7-connect-an-mcp-client) states the two facts every client
needs — the Streamable HTTP endpoint at `/mcp`, and the key as a bearer credential — and that is the complete answer for
a client you configure by writing a file. This page answers what comes next and what no reference page can answer,
because the answer belongs to somebody else: **where the dialog is in the product I actually use, what it accepts as an
address, and what it accepts as a credential.**

The interesting part is not the URL. Three things differ per client, and each decides whether the connection is possible
at all rather than how tidy it is:

- **Where the client runs.** A client on your own machine reaches a loopback address; a client that runs in its vendor's
  cloud reaches only an address published on the public internet.
- **Whether it can send a static `Authorization` header.** Two of the clients below cannot, which means a deployment
  authenticated by an API key cannot be connected from them at all — no address and no header spelling changes that.
- **Which transport it speaks.** The endpoint serves Streamable HTTP and nothing else, so a client limited to the legacy
  HTTP+SSE transport is blocked here.

A reader who does not know the second one reads a `401`, or a connector that lists no tool, as a MailFathom defect.

## Two claims this page does not make

**Presence is a check at a point in time, not a supported-client list.** Every entry below is what that product's own
current documentation said on the date the entry carries. None of these products is under this project's control, and
any of them may change its dialog, its authentication choices, or its transports next week without anybody here touching
anything. An entry that has stopped being true is a defect in this page rather than in the deployment that trusted it.

**Absence is not a refusal.** A client missing from this page is not blocked, unsupported, or known to fail — it is
unchecked. MailFathom serves any client that speaks the MCP Streamable HTTP transport, and
[any other client that speaks Streamable HTTP](#any-other-client-that-speaks-streamable-http) is the section for one.

## What the evidence column means

Each entry says which of two kinds of evidence it rests on, because they are not the same claim:

- **Documented** — the product's own current documentation was read on the date the entry carries. It establishes what
  the vendor publishes about the dialog, the address, and the credential. It does not establish that a connection was
  made.
- **Observed** — a MailFathom deployment was genuinely connected from that client and behaved as the entry says.

**Every entry on this page today is `Documented`.** That is deliberate rather than a gap waiting to be filled: none of
these clients is something this repository can run in a test, and an entry claiming otherwise would be asserting a
verification that nothing here performs.

## What is the same for every client

Four things hold whichever client is being configured, and each is stated in full on the page that owns it rather than
repeated per client below:

- **The address ends in `/mcp`**, which is a constant rather than a setting. What precedes it is where the deployment is
  served: `http://127.0.0.1:8080` under the Compose defaults, or the public HTTPS address your proxy or
  [MailFathom's own TLS](../operations/mcp-endpoint.md#https-and-your-own-domain) serves.
- **An API key travels as `Authorization: Bearer <the key material>`**, and nothing else about it is client-specific.
  [The MCP endpoint § API keys](../operations/mcp-endpoint.md#api-keys) records what a key is, how it rotates, and what
  every refusal looks like.
- **A browser-based client is also subject to the origin policy.** A client that runs as a web page sends an `Origin`
  header, and a deployment that narrowed `McpEndpoint:Cors:AllowedOrigins` has to list that client's origin;
  [CORS and the `Origin` header](../operations/mcp-endpoint.md#cors-and-the-origin-header) holds the rule. A client that
  is a desktop application or a command-line tool sends none and is unaffected.
- **Enabling the endpoint at all, and choosing what protects it, is one decision made before any of this.**
  [Getting started § enable the MCP endpoint](getting-started.md#6-enable-the-mcp-endpoint) is the short form and
  [the MCP endpoint](../operations/mcp-endpoint.md) is the whole contract.

## The clients, in one table

| Client | Where it runs | The address it needs | Authentication it offers | Evidence |
| --- | --- | --- | --- | --- |
| [The ChatGPT web application](#the-chatgpt-web-application-in-developer-mode) | The vendor's cloud | Public HTTPS | OAuth, or none — **no static header** | Documented, 2026-08-12 |
| [The Claude applications](#the-claude-applications-on-the-web-and-on-the-desktop) | The vendor's cloud | Public HTTPS | OAuth, or none — **no static header** | Documented, 2026-08-12 |
| [The Claude Code command-line tool](#the-claude-code-command-line-tool) | Your machine | Loopback or public | Static header, or OAuth | Documented, 2026-08-12 |
| [Visual Studio Code with GitHub Copilot](#visual-studio-code-with-the-github-copilot-chat-agent) | Your machine | Loopback or public | Static header, or OAuth | Documented, 2026-08-12 |
| [The Cursor editor](#the-cursor-editor) | Your machine | Loopback or public | Static header, or OAuth | Documented, 2026-08-12 |

Every client in the table speaks the MCP Streamable HTTP transport, which is the one MailFathom serves, so none of them
is blocked on transport today. That column would only be worth a table of its own for a client that speaks the legacy
HTTP+SSE transport alone; MailFathom serves no such endpoint, and a client limited to it cannot connect at all.

**The two `no static header` rows are the finding this page exists for.** Both products connect a remote MCP server
through a dialog that offers OAuth or nothing, so the deployment shape most readers start from — one API key, one
client — has no way to present its credential there. Neither is a MailFathom limitation and neither has a workaround
worth writing down: the endpoint's answer for those clients is
[an OAuth entry](../operations/mcp-endpoint.md#oauth) beside or instead of the key, and
[MCP client OAuth](../operations/mcp-client-oauth.md) is the sequence that gets one working — the identity provider's
side, which is where nearly all of that work is.

## The ChatGPT web application, in developer mode

OpenAI documents connecting a remote MCP server of your own as a *developer mode* app rather than through the ordinary
connector list, and states the feature is available to Pro, Plus, Business, Enterprise, and Education accounts on the
web.

**Where the dialog is.** Turn the feature on under **Settings → Security and login → Developer mode**, then open
**ChatGPT Plugins**, select the plus button, and create a developer-mode app pointing at your server URL. A created app
appears under **Drafts**, where its tools can be toggled individually and refreshed to pull the current tool list from
the server.

**The address has to be public.** The app is connected by OpenAI's own infrastructure rather than by the browser in
front of you, so a loopback address reaches nothing and a deployment on a private network is unreachable however well it
works from your desk. Serve the endpoint over HTTPS on an address that resolves publicly before starting here.

**An API key cannot be presented.** OpenAI documents three authentication modes for a developer-mode app — OAuth, no
authentication, and a mixed mode combining the two — and no field for a static header or an API key. So a MailFathom
deployment whose `McpEndpoint:Authentication` list holds only `ApiKey` entries cannot be connected from this client, and
the two shapes that can are [an OAuth entry](../operations/mcp-endpoint.md#oauth) and
[an endpoint requiring no credential](../operations/mcp-endpoint.md#requiring-no-credential) — the second of which is
the wrong answer on a public address, and says so in a startup warning.

**Transport.** OpenAI documents SSE and streaming HTTP as the supported MCP protocols, so the Streamable HTTP endpoint
is served as-is.

**This is the one client the endpoint documentation carries a worked certificate profile for.** OpenAI publishes a
managed client certificate for its connector, and a deployment can require it in addition to whatever credential it asks
for;
[the ChatGPT connector profile](../operations/mcp-endpoint.md#the-chatgpt-connector-profile) is the configuration and
the reason it composes with OAuth rather than replacing it.

Sources: [ChatGPT developer mode](https://developers.openai.com/api/docs/guides/developer-mode),
[Building MCP servers for plugins and API integrations](https://developers.openai.com/api/docs/mcp).

## The Claude applications, on the web and on the desktop

Anthropic calls a remote MCP server a *custom connector*, and documents the feature as available on Claude, Cowork, and
Claude Desktop for Free, Pro, Max, Team, and Enterprise plans. A connector belongs to the account rather than to one
application: Anthropic's own documentation for the command-line tool below records that connectors added on the web
load there too, for whoever is signed in with that account.

**Where the dialog is.** On an individual plan, **Customize → Connectors → + → Add custom connector**, then enter the
remote MCP server URL. On a Team or Enterprise plan an owner adds it instead, under **Organization settings →
Connectors**, and members reach it from their own **Customize → Connectors**.

**The address has to be public.** Anthropic documents that the server must be reachable over the public internet from
its own IP ranges, and that a server on a private corporate network, behind a VPN, or blocked by a firewall will not
connect even when it is reachable from your own machine. A loopback address cannot be a custom connector.

**An API key cannot be presented.** The dialog takes the URL, and an **Advanced settings** panel taking an OAuth client
identifier and client secret. There is no field for a static header, so — exactly as with the client above — an
API-key deployment cannot be connected from here, and [an OAuth entry](../operations/mcp-endpoint.md#oauth) is what this
client is served with.

**The desktop application's configuration file is a different thing.** `claude_desktop_config.json` configures local
stdio servers that the application starts as processes on your machine. MailFathom is a service reached over HTTP rather
than a process to start, so it is a connector rather than an entry in that file.

**Transport.** Anthropic documents both Streamable HTTP and SSE as valid transports for a custom connector, so the
endpoint is served as-is.

Sources: [Get started with custom connectors using remote MCP](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp),
[Build custom connectors via remote MCP servers](https://support.claude.com/en/articles/11503834-build-custom-connectors-via-remote-mcp-servers).

## The Claude Code command-line tool

This client runs on your machine, so it reaches a loopback address, and it takes a static header on the command line —
which makes it the shortest path from an API-key deployment to a working tool call.

```bash
claude mcp add --transport http mailfathom http://127.0.0.1:8080/mcp \
  --header "Authorization: Bearer $(cat mcp-workstation-key)"
```

**What each part is.** `--transport http` is the Streamable HTTP transport; `mailfathom` is a name you choose, and it is
what labels the tools in a session; the URL is the endpoint. The server is registered for the current project by
default — add `--scope user` to register it once for every project, or `--scope project` to write it into the
repository's own `.mcp.json`.

**Verify it before opening a session.** `claude mcp list` reports the server as `✔ Connected` once it has answered. Two
other statuses are worth reading rather than retrying: `! Needs authentication` means the endpoint answered `401` or
`403` and no credential was configured for it, and `✘ Failed to connect` covers a rejected header as well as an
unreachable address — Anthropic documents that a server which rejects a configured `headers.Authorization` is a failed
connection rather than falling back to an OAuth flow, so a mistyped key looks like a broken address until you read the
detail on the status.

**OAuth instead of a key.** Add the server without `--header`, then run `/mcp` inside a session and choose
`Authenticate`, or run `claude mcp login mailfathom` from the shell. The endpoint's protected-resource metadata is what
directs the client at your authorization server.

Sources: [Connect to MCP servers](https://code.claude.com/docs/en/mcp-quickstart),
[Connect Claude Code to tools via MCP](https://code.claude.com/docs/en/mcp).

## Visual Studio Code with the GitHub Copilot chat agent

MCP servers are configured for the chat agent in an `mcp.json` file, reachable from the command palette as **MCP: Add
Server**, which asks whether the entry belongs to the workspace or to your user profile. The editor runs on your
machine, so a loopback address works.

Write the key as an input rather than into the file, so the file itself carries no credential:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "mailfathom-key",
      "description": "MailFathom MCP API key",
      "password": true
    }
  ],
  "servers": {
    "mailfathom": {
      "type": "http",
      "url": "http://127.0.0.1:8080/mcp",
      "headers": { "Authorization": "Bearer ${input:mailfathom-key}" }
    }
  }
}
```

`"type": "http"` is the Streamable HTTP transport. Microsoft's own guidance is the reason for the `inputs` block:
hardcoding an API key in the file is what it tells you to avoid, and a workspace `mcp.json` is commonly committed.

**OAuth instead of a key.** Replace the `headers` block with an `oauth` block naming the client identifier your
authorization server issued; Microsoft documents that the editor then runs the flow itself.

**One thing to check in a remote workspace.** A server defined in workspace settings runs where the workspace does, so a
deployment on your laptop is not reached from a workspace opened on a remote machine. Define the entry in the settings
of whichever side can actually reach the address.

Sources: [Use MCP servers in VS Code](https://code.visualstudio.com/docs/copilot/customization/mcp-servers),
[MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration).

## The Cursor editor

Cursor reads `~/.cursor/mcp.json` for every project and `.cursor/mcp.json` for one, and its own documentation lists
stdio, SSE, and Streamable HTTP as the transports it speaks. It runs on your machine, so a loopback address works.

```json
{
  "mcpServers": {
    "mailfathom": {
      "url": "http://127.0.0.1:8080/mcp",
      "headers": { "Authorization": "Bearer ${env:MAILFATHOM_MCP_KEY}" }
    }
  }
}
```

`${env:NAME}` is Cursor's own interpolation, resolved in `url` and `headers` among other fields, which is how the key
stays out of a file that is commonly committed with the project.

**OAuth instead of a key.** Cursor takes an `auth` block carrying a client identifier, a client secret, and the scopes
to request, in place of the `headers` block.

Sources: [Model Context Protocol (MCP)](https://cursor.com/docs/mcp).

## Any other client that speaks Streamable HTTP

There is no entry to look up here, and that is the point: MailFathom serves the MCP Streamable HTTP transport at `/mcp`
and asks for whatever its `Authentication` list configures, so any client implementing that transport connects with the
two facts [getting started](getting-started.md#7-connect-an-mcp-client) already states. Three questions decide whether a
given client is one of them, and each has an answer above rather than a setting to find:

- **Can it send a header you choose?** If yes, an API key is the whole configuration. If no, the deployment needs an
  OAuth entry, whatever the client is called.
- **Where does it run?** A client in somebody else's cloud needs an address published on the public internet, and
  serving a mailbox there is a decision about TLS and authentication rather than about the client.
- **Does it speak Streamable HTTP?** A client limited to the legacy HTTP+SSE transport connects to nothing here.

## What a working connection looks like

Whichever client was configured, a connected one lists at least four tools — `list_accounts`, `list_emails`,
`get_email_content`, and `search_emails` — each advertising itself as read-only, non-destructive, and idempotent. Where
that list is shown is the one client-specific part: `claude mcp list` or the `/mcp` panel for the command-line tool, the
server's entry in the editor's MCP view for Visual Studio Code and for Cursor, the connector's own settings page for the
two cloud clients.

A fifth tool, `ask_mail`, appears only once a chat model and an embedding model are both configured and working. Its
absence is the deployment saying it cannot answer questions yet rather than a connection fault, and no client setting
changes it.

Then ask the assistant to list recent mail, and read `folderFreshness` in the result before reading the emails:
[getting started § make the first call](getting-started.md#8-make-the-first-call-and-read-it-correctly) is what that
field means, and [using the tools](usage.md) is the day-to-day surface.

## When the connection is refused

Every refusal below comes from the endpoint rather than from the client, so it means the same thing in all five
products — what differs is only where each one shows it.
[Verifying an enabled endpoint](../operations/mcp-endpoint.md#verifying-an-enabled-endpoint) gives the same reading for
a request made by hand.

| What the client reports | What it is | Where to look |
| --- | --- | --- |
| `401`, or a prompt to authenticate | No credential, a credential that is not a bearer one, a key matching no entry, or a key whose `Lifetime` has passed | [API keys](../operations/mcp-endpoint.md#api-keys) |
| `403` from a browser-based client | The client's origin is outside `McpEndpoint:Cors:AllowedOrigins` | [CORS and the `Origin` header](../operations/mcp-endpoint.md#cors-and-the-origin-header) |
| `403` with no body, on an HTTPS deployment | A configured client-certificate profile refused the certificate, or none was presented to a `Required` profile | [Client certificates](../operations/mcp-endpoint.md#client-certificates) |
| `429` where `401` was expected | The shared anonymous rate-limit partition is exhausted, so the request never reached the point where a challenge is written | [Rate limiting](../operations/mcp-endpoint.md#rate-limiting) |
| `404`, or nothing at all | The address is wrong, the endpoint is not enabled, or a proxy in front of it does not route `/mcp` | [The endpoint is off by default](../operations/mcp-endpoint.md#the-endpoint-is-off-by-default) |
| A connection that never establishes, from a cloud client | The address is not reachable from the vendor's network — loopback, a private network, or a firewall | The two cloud sections above |

A client refusing to accept the configuration at all — no field for a header, a transport it does not speak — is the
other kind of failure, and it is the per-client section rather than this table that answers it.

## Related

- [Getting started](getting-started.md) — the whole path from an installed instance to a first tool call
- [The MCP endpoint](../operations/mcp-endpoint.md) — every setting named here, with its rules: keys, OAuth, origins,
  TLS, client certificates, rate limits, and timeouts
- [Using the tools](usage.md) — what each tool answers, what it bounds, and what its errors mean
- [Configuring a mailbox at your provider](mailbox-providers.md) — the other half of a working deployment

---

**Trademarks.** The product, service, and company names on this page are their owners' trademarks and are used solely to
identify the client applications a MailFathom deployment can be connected from. Their use implies no affiliation with,
sponsorship by, endorsement by, or certification from those owners, in either direction, and this page reproduces no
third-party logo, icon, wordmark, or screenshot.

OpenAI and ChatGPT are trademarks of OpenAI, Inc. Anthropic, Claude, and Claude Code are trademarks of Anthropic, PBC.
Microsoft, Visual Studio Code, GitHub, and GitHub Copilot are trademarks of the Microsoft group of companies. Cursor is
a trademark of Anysphere, Inc.
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md#trademark-and-brand-use)
records the per-owner review this statement comes out of, and why it sits here rather than in `NOTICE`.
