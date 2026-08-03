# Administering your deployment

<!-- describes: src/Cli/** -->

MailFathom ships a command, `mfctl`, that talks to a running deployment from your own machine. This page is the
user's view of it: what it is for, the path from a running service to a verified sign-in, and what it deliberately
cannot do yet. The full contract — every asset name, every stored path, every message the command can print — is
[administering a deployment](../operations/admin-endpoint.md), and this page links there rather than repeating it.

## What the command is, and is not

`mfctl` is a client. Every operation it performs is an HTTP request to the deployment's administrative endpoint, so it
runs on the machine you administer *from* rather than the one the service runs on — your laptop against a container on
a server, or against a pod in a cluster.

It is **not** how MailFathom is configured. Configuration is files and environment variables read at startup, described
in [configuration sources](../operations/configuration-sources.md); the command never reads them, never opens the
database, and never touches the secret store. Nothing you do with it changes what the service will do on its next
restart.

> **Today it verifies who you are and nothing more.** The administrative surface publishes one route, and the commands
> below are the whole of what exists: sign in, keep several deployments straight, and ask one whether your credential
> still works. Operational commands — inspecting synchronization, triggering work, reading accounts — are not there
> yet. If you are looking for something to *do* to a running deployment, this is not yet the page that has it.

## Before it can answer

The administrative endpoint is off unless a deployment turns it on, and it has credentials of its own. An API key that
works against the MCP endpoint authenticates nothing here, deliberately: reading a mailbox and administering the
service that reads it are different authorities.

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

Three things about that block are worth understanding before you copy it:

- **It binds a socket of its own.** `127.0.0.1` above is the safe starting point — reachable from the machine the
  service runs on and nowhere else, which is what an SSH tunnel is for. Publishing it more widely is a decision, not a
  default. The keys are in the [configuration reference](../operations/configuration-reference.md#adminendpoint), and
  how it relates to the port your MCP clients use is
  [the application listener](../operations/configuration-reference.md#the-application-listener).
- **`SecretReference` is a pointer, not a secret.** Where the material actually lives, and how it gets there, is
  [secret provisioning](../operations/secret-provisioning.md). Never write a key into a configuration file.
- **A clear-text endpoint is warned about at startup, not refused.** It is the right posture behind a TLS-terminating
  proxy or on a loopback bind, and the wrong one anywhere else; only you know which you have. Configure
  `AdminEndpoint:Https:Endpoints` to have MailFathom terminate TLS itself.

## Getting the command

`mfctl` arrives with the **0.2.0** release. The `0.1.0` release predates the administrative endpoint and attaches only
the schema artifact, so until then the command comes from a checkout:

```bash
dotnet publish src/Cli/Cli.csproj --configuration Release --output ./mfctl-build
```

From 0.2.0 each release attaches a self-contained binary per platform, with nothing to install beside it — the .NET
runtime is inside the file. [The asset names](../operations/admin-endpoint.md#getting-the-command) are on the
operations page.

## Signing in

```console
$ mfctl login --endpoint https://mail.example.test:8443 --name production
Administrative credential (an API key, or an access token from the configured authorization server):
Signed in to https://mail.example.test:8443 as 'workstation' (MailFathom 0.2.0), saved as profile 'production' and selected.
```

The credential is typed at the prompt or piped in, never passed as an argument — an argument reaches your shell
history and the process list. It is checked against the deployment before anything is written, so a wrong key, a wrong
port, and a host that is not MailFathom all fail here rather than at some later command that leaves you guessing which
of the three it was.

What is stored afterwards is one small file per user, with the token encrypted and the key beside it;
[where the credential is kept](../operations/admin-endpoint.md#where-the-credential-is-kept) states the paths, what
the encryption does protect, and what it does not.

## Everyday use

| What you want | Command |
| --- | --- |
| See which deployments you are signed in to | `mfctl profiles` |
| Work against a different one from now on | `mfctl switch staging` |
| Work against one just this once | any command with `--endpoint staging` |
| Check whether your credential still works | `mfctl status` |
| Forget a deployment on this machine | `mfctl logout` |

Two of those repay a second look. `status` asks the *deployment*, which is what tells a revoked or expired key apart
from a host that is simply down — the stored profile can only say what was true when you signed in. And `logout`
forgets a local profile without revoking anything: the credential keeps working until the deployment stops accepting
it, so a lost laptop is a reason to rotate the key on the server rather than to sign out.

When you work against one deployment for a whole session, `MAILFATHOM_ENDPOINT` states it once for the shell.
`--endpoint` beats it, and both beat the profile you last switched to.

Every command exits `0` when it did what you asked and `1` when it did not, having explained itself on standard error
first. [The troubleshooting table](../operations/admin-endpoint.md#troubleshooting) reads each message back to you as
a cause.

## Where to go next

- [Administering a deployment](../operations/admin-endpoint.md) — the operator's reference for everything above
- [Configuration reference](../operations/configuration-reference.md#adminendpoint) — every `AdminEndpoint` key
- [Secret provisioning](../operations/secret-provisioning.md) and [rotation](../operations/secret-rotation.md) — how
  the key on the server is supplied and replaced
