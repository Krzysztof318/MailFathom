# Getting started

<!-- describes: src/Mcp/Tools/**, src/Host/Configuration/** -->

This page walks from an installed MailFathom to a first successful tool call: provision the credentials, configure a
mailbox, start the service, verify it, connect an MCP client, and read a result correctly. It assumes an installation
from [installing MailFathom](installation.md); a developer evaluating from the checkout can run the
[Aspire orchestration](../operations/local-development.md#running-locally-with-aspire) instead, which provisions
PostgreSQL and applies the schema on its own.

The examples write configuration as JSON. Every setting can arrive as an environment variable instead — `:` becomes
`__`, so `MailSynchronization:Enabled` is `MailSynchronization__Enabled` — and the
[configuration reference](../operations/configuration-reference.md) lists every key used below.

## 1. Provision the secrets

MailFathom's configuration never carries a credential. A secret-bearing setting holds a *reference* — `file:/…`,
`systemd-credential:…`, `env:…` — and the material lives wherever the deployment provisions it. A configuration file
is therefore safe to review and back up: leaking it leaks paths, not passwords.

You need two pieces of material before anything is configured:

- **The mailbox password** or app password of the IMAP account to synchronize. A mailbox whose provider no longer
  accepts one — a Google Workspace or Exchange Online account — is authenticated with an OAuth refresh token instead;
  obtain it first with [mailbox OAuth](../operations/mailbox-oauth.md) and substitute that block for the password
  below. That account needs a third piece of material as well, the **data-encryption key** the rotated refresh token is
  sealed under: `openssl rand -base64 32`, generated once and never regenerated, provisioned as a reference like
  everything else here. [The data-encryption key](../operations/secret-provisioning.md#the-data-encryption-key) is the
  whole of it.
- **An MCP API key** for the client that will connect. Generate it rather than inventing it:

```bash
openssl rand -base64 33 | tr -d '\n' > mcp-workstation-key
```

Where the files go is the deployment's convention: `secrets/mailfathom/` for
[Compose](../operations/deployment-compose.md#credentials), a Kubernetes `Secret` mounted at
`/etc/mailfathom/secrets` for [the chart](../operations/deployment-kubernetes.md#what-you-supply), `LoadCredential=`
for [systemd](../operations/secret-provisioning.md#native-systemd-service), and an encrypted credential under
`~/.config/credstore.encrypted/` for [the Quadlet](../operations/deployment-quadlet.md#the-credentials). The references
below assume the mounted directory the Compose and Helm shapes share.

## 2. Configure the mailbox

Synchronization is off until configuration turns it on, and an enabled synchronization requires at least one account:

```json
{
  "MailSynchronization": {
    "Enabled": true,
    "Accounts": [
      {
        "AccountId": "primary",
        "DisplayName": "Personal mail",
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

**What goes in `Host`, `Port`, and `Secrets` depends on where the mailbox lives**, and so does whether IMAP has to be
switched on first and whether a password is accepted at all. [Configuring a mailbox at your
provider](mailbox-providers.md) has the address and the credential kind for the popular mail services, and what each
one does differently once synchronization is running.

Points worth knowing before you adapt it:

- **`AccountId` and `Alias` are your names**, not the server's. They are what every tool argument, log line, and error
  message uses, so pick names you are happy to see in a diagnostic.
- **`DisplayName` is the name an assistant reads back to you.** It is required and has no default: the identifier above
  is a key, and a person hearing "the email came from `acct-2`" learns nothing. It travels beside the identifier in
  every tool result, and either spelling narrows a listing, a search, or a question to that mailbox. No two accounts may
  share one, and none may take another account's identifier, so a name always names one mailbox.
- **Folders are best named by role.** `SpecialUse` lets discovery find the folder whatever the server calls it —
  a German server's `Gesendet` is still `Sent` — and configuring no folder at all synchronizes the inbox. Naming an
  exact server path is the alternative for folders with no role.
- **A mapped folder is mirrored, embedded, and readable by tools** unless you say otherwise. `Synchronize`,
  `GenerateEmbeddings`, and `VisibleToTools` each default to `true` on a folder entry, and switching one off is how a
  folder stays nameable while its mail stays out of the local copy, out of an embedding provider, or out of everything
  an assistant can read.
  [What a mapping decides beyond where the folder is](../features/imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
  states what each one costs, including what happens to mail already stored when you turn mirroring off.
- **The transport is TLS by default.** Port 993 with TLS-on-connect is the default posture, and every weakening —
  an unencrypted connection, clear-text authentication over one — must be stated explicitly and fails startup
  otherwise. A server with a private certificate authority is supported by trusting that authority, never by turning
  validation off; [transport security](../features/imap-synchronization.md#transport-security) records the rules.
- **An older server may be refused before any of this applies.** If synchronization reports an authentication failure
  wrapping `SSL Handshake failed with OpenSSL error`, the password is not the problem: the platform's own TLS policy
  ended the handshake before a credential was sent. [The platform TLS policy](../operations/platform-tls-policy.md)
  covers how to confirm that and the one supported way to relax it.
- **How far back to synchronize** is per account: `EarliestEmailReceivedDate` bounds the first synchronization of a
  large mailbox, and omitting it copies everything the server still holds.
- **New mail arrives on the next run unless you ask for push.** The default reconciles each account every five minutes.
  Setting an account's `"Mode": "Push"` makes MailFathom hold an IMAP connection open and synchronize the moment the
  server reports a change. What that costs depends on the server: one connection for the whole account where it supports
  the `NOTIFY` extension, one per watched folder where it supports only `IDLE`, and nothing at all where it supports
  neither — which is polled instead and says so in the log. Push **adds** to the schedule rather than replacing it — the
  account still runs on its `Interval`, and a notification only starts the next run sooner.
  [Push synchronization](../features/imap-synchronization.md#push-synchronization) records the whole model.

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
step you take, with a backup first once there is data to lose.

What you apply is one SQL file. A release attaches it; from a checkout, generate it:

```bash
scripts/build-schema-artifact.sh      # artifacts/schema/mailfathom-schema-<version>.sql
```

Read it, apply it with any PostgreSQL client, and start again — the refusal is gone. The role that applies it needs
more privilege than the one MailFathom connects as, which
[applying the database schema](../operations/database-schema.md) explains along with the grants that leaves behind.

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
    "Authentication": [
      {
        "ApiKey": {
          "Name": "workstation",
          "SecretReference": "file:/etc/mailfathom/secrets/mcp-workstation-key"
        }
      }
    ]
  }
}
```

An empty `Authentication` list is legal — the reverse-proxy-and-loopback deployment is an ordinary one — but it is
announced with a startup warning, because an unauthenticated endpoint serves your mailbox to whoever can reach its port. Read
[the MCP endpoint](../operations/mcp-endpoint.md) before widening anything: it records the OAuth alternative, browser
origins, serving your own domain over TLS, client certificates, and the rate limits that apply out of the box.

MailFathom itself serves plain HTTP unless its own TLS termination is configured, so keep the application port on
loopback or behind a TLS-terminating proxy — the Compose deployment's default — and give the proxy the certificate. If
you put a proxy in front, name it in `ReverseProxy:TrustedProxies` as well. The public scheme and host survive the hop
either way; naming the proxy is what stops anything else that can reach the port from claiming them:
[behind a TLS-terminating reverse proxy](../operations/mcp-endpoint.md#behind-a-tls-terminating-reverse-proxy).

## 7. Connect an MCP client

The endpoint speaks the MCP **Streamable HTTP** transport at `/mcp` — the path is fixed — and the key travels as a
bearer credential. Any client that supports Streamable HTTP connects with two facts:

| The client asks for | The value |
| --- | --- |
| Server URL | `http://127.0.0.1:8080/mcp` under the Compose defaults; your proxy's HTTPS address otherwise |
| Header | `Authorization: Bearer <the key material>` |

**Where a client asks for those two values, and whether it will accept the second at all, differs per client.** Two of
the popular chat clients offer no field for a static header, which decides the deployment's authentication rather than
only the setup steps;
[connecting the chat client you already use](mcp-clients.md) has the steps, the address kind, and the authentication
shapes for each one by name.

A connected client's tool listing should show at least four tools — `list_accounts`, `list_emails`,
`get_email_content`, `search_emails` — each advertising itself as read-only, non-destructive, and idempotent. A fifth,
`ask_mail`, appears only once you have configured a chat model and an embedding model and both are working; until then
its absence is the deployment telling you it cannot answer questions yet rather than a fault.
[Verifying an enabled endpoint](../operations/mcp-endpoint.md#verifying-an-enabled-endpoint) is the checklist form of
this, including what the refusals look like when the key or the origin is wrong.

**Signing a person in through your own identity provider instead of sharing a key** is the other way to connect, and it is
the only way for a client whose dialog takes no header. Most of that work is in the provider rather than here:
[MCP client OAuth](../operations/mcp-client-oauth.md) walks it end to end from a deployment in exactly this state.

## 8. Make the first call, and read it correctly

Ask the connected agent to list recent mail, or have the client call `list_emails` with a small page. Two parts of the
result matter more than the emails on the first day:

- **`folderFreshness`** carries one entry per folder in scope, stating when synchronization last committed progress
  there. An empty page whose entries report `wasSynchronized: false` means the folder has not synchronized yet — not
  that the mailbox is empty. This is the field to check before trusting any early result.
- **`nextCursor`** is how the rest of the timeline is read: pass it back unchanged with the same filters. Results are
  deliberately bounded — at most 100 summaries per page — so an agent reads pages, not mailboxes.

From here, [using the tools](usage.md) describes the day-to-day surface: what each tool answers, what it bounds, and
what its errors mean, and [administering your deployment](administering.md) covers reaching the running service from
your own machine with the `mfctl` command.
