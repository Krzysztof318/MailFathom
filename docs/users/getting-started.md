# Getting started

This page walks from an installed MailFathom to a first successful tool call: provision the credentials, configure a
mailbox, start the service, verify it, connect an MCP client, and read a result correctly. It assumes an installation
from [installing MailFathom](installation.md); a developer evaluating from the checkout can run the
[Aspire orchestration](../operations/local-development.md) instead, which provisions PostgreSQL and applies the schema
on its own.

The examples write configuration as JSON. Every setting can arrive as an environment variable instead — `:` becomes
`__`, so `MailSynchronization:Enabled` is `MailSynchronization__Enabled` — and the
[configuration reference](../operations/configuration-reference.md) lists every key used below.

## 1. Provision the secrets

MailFathom's configuration never carries a credential. A secret-bearing setting holds a *reference* — `file:/…`,
`systemd-credential:…`, `env:…` — and the material lives wherever the deployment provisions it. A configuration file
is therefore safe to review and back up: leaking it leaks paths, not passwords.

You need two pieces of material before anything is configured:

- **The mailbox password** or app password of the IMAP account to synchronize.
- **An MCP API key** for the client that will connect. Generate it rather than inventing it:

```bash
openssl rand -base64 33 | tr -d '\n' > mcp-workstation-key
```

Where the files go is the deployment's convention: `secrets/mailfathom/` for
[Compose](../operations/deployment-compose.md#credentials), a Kubernetes `Secret` mounted at
`/etc/mailfathom/secrets` for [the chart](../operations/deployment-kubernetes.md#what-you-supply), `LoadCredential=`
for [systemd](../operations/secret-provisioning.md#native-systemd-service). The references below assume the mounted
directory the Compose and Helm shapes share.

## 2. Configure the mailbox

Synchronization is off until configuration turns it on, and an enabled synchronization requires at least one account:

```json
{
  "MailSynchronization": {
    "Enabled": true,
    "Accounts": [
      {
        "AccountId": "primary",
        "Host": "imap.example.test",
        "Port": 993,
        "UserName": "you@example.test",
        "Secrets": {
          "Password": {
            "Name": "imap-primary-password",
            "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password"
          }
        },
        "Folders": [
          { "Alias": "inbox", "SpecialUse": "Inbox" },
          { "Alias": "sent", "SpecialUse": "Sent" }
        ]
      }
    ]
  }
}
```

Points worth knowing before you adapt it:

- **`AccountId` and `Alias` are your names**, not the server's. They are what every tool argument, log line, and error
  message uses, so pick names you are happy to see in a diagnostic.
- **Folders are best named by role.** `SpecialUse` lets discovery find the folder whatever the server calls it —
  a German server's `Gesendet` is still `Sent` — and configuring no folder at all synchronizes the inbox. Naming an
  exact server path is the alternative for folders with no role.
- **The transport is TLS by default.** Port 993 with TLS-on-connect is the default posture, and every weakening —
  an unencrypted connection, clear-text authentication over one — must be stated explicitly and fails startup
  otherwise. A server with a private certificate authority is supported by trusting that authority, never by turning
  validation off; [transport security](../features/imap-synchronization.md#transport-security) records the rules.
- **How far back to synchronize** is per account: `EarliestEmailReceivedDate` bounds the first synchronization of a
  large mailbox, and omitting it copies everything the server still holds.

The Compose deployment reads this from `config/10-mailfathom.json`; Kubernetes mounts it as a ConfigMap key; a native
process names the file through [`ConfigurationSources`](../operations/configuration-sources.md).

## 3. Point it at the database

The Compose deployment wires the connection and its password for you — skip this step there. Elsewhere, the connection
string carries everything but the password, and the password joins it through a reference:

```json
{
  "ConnectionStrings": { "mailfathom": "Host=db.example.test;Database=mailfathom;Username=mailfathom" },
  "Persistence": {
    "Password": {
      "Name": "mailfathom-database-password",
      "SecretReference": "file:/etc/mailfathom/secrets/mailfathom-database-password"
    }
  }
}
```

## 4. Start it, and apply the schema

Start the service the way the installation shape does. The first start against an empty database **fails on purpose**,
naming the migration it expects:

```text
The database has not applied 1 migration(s) this build defines: 20260731132336_Initial.
```

That is the explicit schema step described in [installing MailFathom](installation.md#what-every-shape-needs):
MailFathom verifies the schema and refuses to serve against one it does not recognize, and applying migrations is a
step you take, with a backup first once there is data to lose. Apply the schema, start again, and the refusal is gone.

## 5. Verify it is healthy

```bash
curl -fsS http://127.0.0.1:8081/started
curl -fsS http://127.0.0.1:8081/health
```

`/started` confirms the startup gates passed — every secret reference resolved, the schema matched. `/health` is
readiness and includes the database. Both answer on the probe listener, `8081` by default, never on the application
port; [health endpoints](../operations/health-endpoints.md) explains that separation.

Then let the first synchronization run. Its progress is visible in the log — each folder run reports what it stored —
and, once you can call a tool, in the `folderFreshness` every result carries. A large mailbox takes a while on the
first pass; later runs move only what changed, every five minutes by default.

## 6. Enable the MCP endpoint

The endpoint is off by default, and enabling it means stating how it is authenticated:

```json
{
  "McpEndpoint": {
    "Enabled": true,
    "Authentication": "ApiKey",
    "ApiKeys": [
      {
        "Name": "workstation",
        "SecretReference": "file:/etc/mailfathom/secrets/mcp-workstation-key"
      }
    ]
  }
}
```

`Authentication: "None"` is legal — the reverse-proxy-and-loopback deployment is an ordinary one — but it is announced
with a startup warning, because an unauthenticated endpoint serves your mailbox to whoever can reach its port. Read
[the MCP endpoint](../operations/mcp-endpoint.md) before widening anything: it records the OAuth alternative, browser
origins, serving your own domain over TLS, client certificates, and the rate limits that apply out of the box.

MailFathom itself serves plain HTTP unless its own TLS termination is configured, so keep the application port on
loopback or behind a TLS-terminating proxy — the Compose deployment's default — and give the proxy the certificate.

## 7. Connect an MCP client

The endpoint speaks the MCP **Streamable HTTP** transport at `/mcp` — the path is fixed — and the key travels as a
bearer credential. Any client that supports Streamable HTTP connects with two facts:

| The client asks for | The value |
| --- | --- |
| Server URL | `http://127.0.0.1:8080/mcp` under the Compose defaults; your proxy's HTTPS address otherwise |
| Header | `Authorization: Bearer <the key material>` |

A connected client's tool listing should show exactly three tools — `list_emails`, `get_email_content`,
`search_emails` — each advertising itself as read-only, non-destructive, and idempotent.
[Verifying an enabled endpoint](../operations/mcp-endpoint.md#verifying-an-enabled-endpoint) is the checklist form of
this, including what the refusals look like when the key or the origin is wrong.

## 8. Make the first call, and read it correctly

Ask the connected agent to list recent mail, or have the client call `list_emails` with a small page. Two parts of the
result matter more than the emails on the first day:

- **`folderFreshness`** carries one entry per folder in scope, stating when synchronization last committed progress
  there. An empty page whose entries report `wasSynchronized: false` means the folder has not synchronized yet — not
  that the mailbox is empty. This is the field to check before trusting any early result.
- **`nextCursor`** is how the rest of the timeline is read: pass it back unchanged with the same filters. Results are
  deliberately bounded — at most 100 summaries per page — so an agent reads pages, not mailboxes.

From here, [using the tools](usage.md) describes the day-to-day surface: what each tool answers, what it bounds, and
what its errors mean.
