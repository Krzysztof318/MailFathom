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

> **One thing it does changes a running deployment: it can place a mailbox credential.** Everything else below verifies
> who you are and keeps several deployments straight. Operational commands — inspecting synchronization, triggering
> work, reading accounts — are not there yet.
>
> The exception is [authorizing a mailbox](#authorizing-a-mailbox), which signs you in to a *mail provider* and can then
> hand the resulting credential to your deployment to keep.

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

A release attaches a self-contained binary per platform, with nothing to install beside it — the .NET runtime is inside
the file. Which release starts attaching them, the asset names, and how to build the command from a checkout until then
are all on [getting the command](../operations/admin-endpoint.md#getting-the-command).

Download the one for the machine you administer *from*. The command talks to a deployment over HTTP, so it does not
have to run where the service runs — that is the whole point of it being a client.

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

If the deployment is configured for OAuth, sign in with a browser instead and let it do the authenticating:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --mode interactive --client-id mfctl
```

`--mode device` is the same thing on a machine with no browser: it prints a short code to enter on your phone. Either
way the only thing you supply is the client identifier — the command asks the deployment where to authorize. Your access
token is then renewed for you until the sign-in genuinely ends, and how long that is depends on your identity platform;
[how long an OAuth sign-in lasts](../operations/admin-endpoint.md#how-long-an-oauth-sign-in-lasts) states the rule and
the one setting that shortens it.

What is stored afterwards is one small file per user, with the tokens encrypted and the key beside it;
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

## Authorizing a mailbox

A mailbox at a provider that no longer accepts a password — a Google Workspace account, anything on Exchange Online —
needs a person to sign in once before MailFathom can read it. A headless service cannot arrange that, so the command
does it:

```console
$ mfctl mailbox authorize --provider google --client-id <client-id>
```

This is the one command that talks to something other than your deployment, and it is why running `mfctl` on your own
computer matters. It listens on a loopback address, opens your browser, and catches the redirect the provider sends
back — so there is nothing to copy, and the authorization code never crosses a network. On a machine with no browser,
forward the port over SSH, or use `--mode device` for Microsoft and `--mode manual` for Google.

What it produces is a refresh token, and `--account` decides where that token goes:

```console
$ mfctl mailbox authorize --provider google --client-id <client-id> --account workspace
…
Stored the refresh token for account 'workspace' on 'production'. It was not printed.
```

**Named, the token goes straight to your deployment**, which encrypts it and keeps it. It is never printed, so it never
reaches your scrollback or a session log, and there is nothing for you to place by hand. The account has to be one your
deployment configures; a name it does not know is refused and nothing is stored. Re-running it replaces what was
stored, which is what re-authorizing after a revocation is.

**Omitted, the token is printed on standard output and you provision it on the server yourself**, as a secret reference
like every other credential. That is what a deployment with no administrative endpoint needs, and it stays the right
choice if you would rather place credentials yourself.

[Mailbox OAuth](../operations/mailbox-oauth.md) is the whole procedure, including what each provider requires of the
application you register and how a stored token relates to the reference in your configuration.

## Where to go next

- [Administering a deployment](../operations/admin-endpoint.md) — the operator's reference for everything above
- [Mailbox OAuth](../operations/mailbox-oauth.md) — registering the application, and every mode of the sign-in above
- [Configuration reference](../operations/configuration-reference.md#adminendpoint) — every `AdminEndpoint` key
- [Secret provisioning](../operations/secret-provisioning.md) and [rotation](../operations/secret-rotation.md) — how
  the key on the server is supplied and replaced
