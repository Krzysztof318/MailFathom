# The MCP endpoint and its interim security posture

The MCP endpoint is how an agent reaches MailMcp. This page records what enabling it means operationally, what protects it
today, and what has to change before that posture is acceptable outside development. The tools it serves are described in
`docs/features/mcp-tools.md`.

## The endpoint is off by default

A deployment that configures nothing serves no MCP endpoint. That is a security default rather than a convenience: the
endpoint exposes synchronized mailboxes, and until transport authentication exists, anything that can reach its address can
read them.

```json
{
  "McpEndpoint": {
    "Enabled": false
  }
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Whether the Streamable HTTP endpoint is mapped at all |

Whether is the only question this section answers, deliberately.

The endpoint always answers on **`/mcp`**, which is a constant rather than a setting. An MCP client is configured with a
server URL, so a deployment could only move the path in step with every client pointed at it — the configurability would
buy nothing and add one more way for the surface to end up reachable somewhere nobody is looking. Put it behind a reverse
proxy if it has to appear elsewhere.

The transport is always **stateless**, for the same kind of reason. Every MailMcp tool answers one request from the local
mailbox copy and sends nothing back on its own, so a session would carry no state and only give a client something to lose
across a restart. Stateless is also what MCP deployments assume today. Should a tool that pushes notifications arrive, that
is a change to this surface rather than a switch an operator was expected to have found.

The section is bound strictly, so an unrecognized key fails startup instead of being ignored: a misspelled `Enabeld` would
otherwise leave the endpoint off while an operator believed they had switched it on. The value is read once while the host
is being composed, because whether an endpoint exists is part of the application's routing. Changing it takes effect on
restart; it does not participate in configuration reload.

## What protects it today: nothing at the transport

**Until the OAuth 2.1 work of draft stage 9 lands, an enabled MCP endpoint has no transport authentication.** There is no
bearer token, no resource-server metadata, no mutual TLS, and no client identity of any kind. Anything that can open a
connection to the address can call every tool and read every synchronized mailbox.

The owner has accepted this for the current roadmap segment so that real MCP clients can be exercised during development,
and no address restriction is imposed in code as a result. The consequence is operational and is stated here rather than
enforced:

- **Point an enabled endpoint at development mailboxes only.** Do not enable it against a mailbox whose contents would
  matter if read by someone else.
- **Restrict who can reach the address at the network layer.** A loopback bind, a firewall rule, a private network, or an
  authenticating reverse proxy in front of the host are all outside MailMcp and all appropriate. MailMcp does not do this
  for you.
- **Treat the whole surface as read-only but not harmless.** The tools cannot send, delete, move, or mark mail as read, so
  the exposure is disclosure rather than modification. Disclosure of a mailbox is enough.

Startup makes this unmissable. Whenever the endpoint is enabled, the host logs one warning naming the controls that are
absent and where they arrive:

```text
warn: MailMcp.Host.Hosting.McpTransportAuthenticationWarning
      The MCP endpoint is enabled on /mcp with no transport authentication: neither OAuth 2.1 resource-server
      authentication nor mutual TLS is in place, so anything that can reach this address can read the synchronized
      mailboxes. This is the interim posture until the OAuth 2.1 work of draft stage 9 lands. Point it at development
      mailboxes only, and restrict who can reach the address at the network layer.
```

The warning is unconditional on the endpoint being enabled because there is nothing to detect yet: no scheme, no
certificate requirement, nothing a check could observe. When authentication exists, the warning becomes a real condition
rather than a second mechanism beside it.

### When this posture expires

At draft stage 9, which adds OAuth 2.1 resource-server authentication and mTLS. That work should remove this section, not
soften it. Authorization itself is already in place and is not what stage 9 introduces: every tool call resolves the
accounts the owner controls and refuses anything outside them, so stage 9 replaces where the owner's identity comes from —
configuration today, an authenticated token then — rather than adding the first authorization check.

## What the endpoint records

Every tool call is logged once with the tool name, whether it ended in an error, and how long it took. Nothing else:
no filter values, no mailbox addresses, no subjects, and no part of a result, because a filter argument names a person as
surely as a result does.

An undiagnosed failure is logged in full, with its exception, at error level, correlated by the trace the request already
carries — and answered with the single generic error code `54001`, which tells the caller that the call failed and nothing
about why. When a client reports `54001`, the server log is where the reason is.

A refused call is logged with the five-digit code it was refused with, so an operator can correlate a client's complaint
against a server record without learning what was searched for.

## Verifying an enabled endpoint

With the endpoint enabled, the Streamable HTTP transport answers on `/mcp` and advertises three tools. Any MCP
client that speaks Streamable HTTP can list them; `tools/list` should report `list_emails`, `get_email_content`, and
`search_emails`, each with `readOnlyHint` true, `destructiveHint` false, `idempotentHint` true, and `openWorldHint`
false.

A call answers from the local mailbox copy, so what it returns depends on what synchronization has stored rather than on
whether a mail server is reachable. A deployment whose folders have never synchronized answers an empty page whose
`folderFreshness` entries report `wasSynchronized` as false, which is the state to check before treating an empty result as
a statement about the mailbox.

`get_email_content` reads that same local copy: it takes a `storedEmailId` a listing returned and never fetches, so an
email whose content is missing or damaged locally is answered with `55001` and a durable repair request rather than with
a download. An operator reading `55001` in the log is reading a local-consistency problem, not a mail-server one.

`search_emails` reads the lexical index built over that copy, so a folder that has synchronized but whose text
extraction has not run yet answers an empty window rather than a failure — the same state `folderFreshness` is there to
distinguish. Its `retrievalMode` reports `lexical`, and a request that asks for more than 50 ranked results is refused
with `51003` rather than served a smaller window.
