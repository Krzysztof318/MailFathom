# Administering a deployment

<!-- describes: src/Host/Configuration/AdminEndpointOptions.cs, src/Host/Api/**, src/Host/Hosting/AdminEndpointIsolation.cs, src/Host/Hosting/AdminTransportSecurityWarning.cs, src/Host/Security/Admin*, src/Cli/** -->

How the `mailfathom` command reaches a running deployment, and what that deployment has to have enabled before it will
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
    "Authentication": "ApiKey",
    "ApiKeys": [
      { "Name": "workstation", "SecretReference": "systemd-credential:admin-workstation-key" }
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

`Authentication` takes the same values `McpEndpoint:Authentication` takes — `ApiKey`, `OAuth`, both separated by a
comma, or `None` — and the section that configures them is separate all the way down. A misspelled key fails startup
rather than binding a default.

> **Every authenticated caller may perform every administrative operation.** There is no permission model yet. The
> credential is what bounds access, so provision one per client and rotate it like any other secret.

## Two postures the endpoint warns about

Neither is refused, because both are legitimate somewhere and only you know which you have.

| Startup warning | What it means |
| --- | --- |
| No authentication method turned on | Anything that can reach the address can administer the service. Right only for a loopback bind or a network you control. |
| Served in clear text | Any credential a client presents is readable on the path. Right only behind a TLS-terminating reverse proxy, or on a loopback bind. |

Configure `AdminEndpoint:Https:Endpoints` to have Kestrel terminate TLS itself. It takes the same profile shape the MCP
endpoint's does, including `HttpProtocols`, which defaults to HTTP/1.1 and HTTP/2. Naming any profile binds those
listeners and no clear-text one stays open behind them.

## Getting the command

Each release attaches a self-contained binary per platform, plus one checksum file covering all of them. Download the
one for the machine you administer *from* — the command talks to a deployment over HTTP, so it does not have to run
where the service runs.

| Platform | Asset |
| --- | --- |
| Linux, x86-64 | `mailfathom-<version>-linux-x64` |
| Linux, ARM64 | `mailfathom-<version>-linux-arm64` |
| Windows, x86-64 | `mailfathom-<version>-win-x64.exe` |
| Windows, ARM64 | `mailfathom-<version>-win-arm64.exe` |

Nothing needs installing beside it: the .NET runtime is inside the file.

## Signing in

```console
$ mailfathom login --endpoint https://mail.example.test:8443
Administrative credential (an API key, or an access token from the configured authorization server):
Signed in to https://mail.example.test:8443 as 'workstation' (MailFathom 0.2.0).
```

The credential is read from standard input rather than taken as an argument, because an argument reaches the shell
history, the process list, and any log of either. A script pipes it in instead:

```console
$ printf '%s' "$MAILFATHOM_KEY" | mailfathom login --endpoint https://mail.example.test:8443
```

**It is verified before it is stored.** A deployment that refuses the credential, an address serving no administrative
endpoint, and a host that answers with something that is not MailFathom all fail here rather than at some later command.

`mailfathom logout --endpoint …` forgets the local copy. It does not revoke anything: the credential stays valid until
the deployment stops accepting it.

## Where the credential is kept

| Platform | Path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/MailFathom/credentials.json`, or `~/.config/MailFathom/credentials.json` |
| Windows | `%APPDATA%\MailFathom\credentials.json` |

One entry per endpoint, so a workstation administering staging and production holds both. On Linux the file and its
directory are created owner-only, and created that way rather than tightened afterwards — a file created readable and
corrected later is readable for the moment in between.

`MAILFATHOM_ENDPOINT` supplies the address so a shell can state it once for a session; `--endpoint` overrides it.

## Troubleshooting

| What you see | What it means |
| --- | --- |
| `The deployment refused the credential.` | The key is not one this endpoint is configured with, or its lifetime has ended. Note that an MCP API key is not one of them. |
| `serves no administrative endpoint at /api/admin/session` | The address answered, but on a listener that serves something else. Check the port, and check that `AdminEndpoint:Enabled` is true. |
| `did not identify itself as MailFathom` | Something else is answering on that port — a proxy, or another service. |
| `could not be reached` | Nothing is listening, or a firewall is in the way. The endpoint binds only what `BindAddress` names; `127.0.0.1` is unreachable from another machine by design. |
| `No deployment was named.` | Pass `--endpoint`, or set `MAILFATHOM_ENDPOINT`. |

## Related

- [MCP endpoint](mcp-endpoint.md) — the other protected surface, and the one this is deliberately separate from
- [Secret provisioning](secret-provisioning.md) — how an API key reference is backed by material
- [Configuration reference](configuration-reference.md) — every key in the `AdminEndpoint` block
