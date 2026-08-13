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
    "Authentication": [
      { "ApiKey": { "Name": "workstation", "SecretReference": "systemd-credential:admin-workstation-key" } }
    ]
  }
}
```

Five things about that block are worth understanding before you copy it:

- **It binds a socket of its own.** `127.0.0.1` above is the safe starting point — reachable from the machine the
  service runs on and nowhere else, which is what an SSH tunnel is for. Publishing it more widely is a decision, not a
  default. The keys are in the [configuration reference](../operations/configuration-reference.md#adminendpoint), and
  how it relates to the port your MCP clients use is
  [where each surface is served](../operations/configuration-reference.md#where-each-surface-is-served).
- **`SecretReference` is a pointer, not a secret.** Where the material actually lives, and how it gets there, is
  [secret provisioning](../operations/secret-provisioning.md). Never write a key into a configuration file.
- **A clear-text endpoint is warned about at startup, not refused.** It is the right posture behind a TLS-terminating
  proxy or on a loopback bind, and the wrong one anywhere else; only you know which you have. Configure
  `AdminEndpoint:Https:Endpoints` to have MailFathom terminate TLS itself.
- **It is rate limited without your writing a number.** An endpoint that answers a network has to bound how fast a
  caller may present wrong credentials, so the limits apply the moment you enable it and the applied numbers are stated
  at startup. [Rate limiting](../operations/admin-endpoint.md#rate-limiting) is where the settings and the one way this
  endpoint's limit differs from the MCP endpoint's are recorded.
- **A request that runs too long is abandoned, also without your writing a number.** The ceiling defaults to ten
  minutes because the MCP endpoint shares the setting and an AI-backed answer can legitimately take minutes; no
  administrative route reaches a provider, so this is the endpoint worth narrowing.
  [Request timeouts](../operations/admin-endpoint.md#request-timeouts) is where that is recorded.

## Getting the command

Every release attaches a self-contained binary per platform, with nothing to install beside it — the .NET runtime is
inside the file. The asset names and the checksum that tells a genuine download from a tampered one are on
[getting the command](../operations/admin-endpoint.md#getting-the-command), and on Linux
[the install script](../operations/admin-endpoint.md#on-linux-with-the-install-script) does the whole of it in one
line.

Download the one for the machine you administer *from*. The command talks to a deployment over HTTP, so it does not
have to run where the service runs — that is the whole point of it being a client.

**Take it from the release your deployment is running.** The command and the deployment have to agree on `major.minor`,
because a minor release is allowed to change what they say to each other; a pair that does not agree is refused rather
than attempted, and a pair that differs only in patch or nightly warns and carries on.
[Take the command from the deployment's own release line](../operations/admin-endpoint.md#take-the-command-from-the-deployments-own-release-line)
is the rule in full.

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

For a scheduled job there is a third way, and it is the one to prefer there. Generate a key pair, give the deployment
the public half only, and sign in with the private one:

```console
$ mfctl login --endpoint https://mail.example.test:8443 --mode keypair --private-key ~/.config/MailFathom/production.key
```

Nothing reusable is stored and nothing reusable reaches the deployment: the command signs a fresh credential per
request, each good for about a minute, and the only thing the service holds is a public key.
[Signing in with a key pair](../operations/admin-endpoint.md#with-a-key-pair) has the `openssl` commands and the entry
to add.

If your deployment serves a certificate your workstation does not trust — self-signed, or issued by an authority only
your organization carries — the sign-in shows you that certificate and asks once whether to trust it, the way an SSH
client asks about a host key:

```console
$ mfctl login --endpoint https://mail.internal.example:8443 --name internal
…
  Fingerprint: 3B:9A:1C:…:7F
Trust this certificate for this profile? [y/N]:
```

Compare the fingerprint against the deployment's own before you answer; nothing has been sent yet. Saying yes stores
that fingerprint on the profile, which makes the profile **stricter** rather than looser: from then on it accepts that
one certificate and refuses every other, so a renewal or a substitution stops the profile rather than passing
unnoticed. You accept a renewed certificate by signing in again. An `http://` address gets a question of its own,
because the credential and every later request would cross the network in clear text and a redirect to `https://`
arrives too late to change that.
[When the connection is weaker than the default](../operations/admin-endpoint.md#when-the-connection-is-weaker-than-the-default)
has both questions in full, and the two switches a scripted sign-in states the answers with.

What is stored afterwards is one small file per user, with the tokens encrypted and the key beside it — and for a
key-pair profile, no credential at all;
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

## Turning semantic search on

Which model your deployment embeds with is a configuration value, and editing it starts nothing. Turning semantic
search on is a separate, deliberate act, because it is the first thing MailFathom does that costs money per unit of
mail — a full mailbox goes to your provider, and you should see the size of that before agreeing to it:

```console
$ mfctl embedding activate
```

The command reads what the deployment would spend, prints it as passages, characters, and approximate tokens, and asks
before it starts. `--yes` agrees up front, for a scripted run.

| What you want | Command |
| --- | --- |
| Find out why semantic search is quiet | `mfctl embedding status` |
| Take up the model your configuration declares | `mfctl embedding activate` |
| Stop a re-embed you have changed your mind about | `mfctl embedding cancel-reindex` |

`mfctl embedding status` is the one to reach for first. It answers, in one output, whether a model is active, whether
that model is still the one your configuration declares, whether your provider is answering, how much of the mailbox
is embedded, what the current budget period has spent, and when the walk that embeds your existing mail next runs. That
last line is the one to read in the minutes after an activation: until the first passages have gone out, a deployment
that is simply between passes looks exactly like one that is broken.

[Administering the embedding profile](../operations/admin-endpoint.md#administering-the-embedding-profile) is the
operator's reference for all three, and [changing the embedding model](../operations/embedding-profiles.md) is what a
switch and a rollback cost.

## Applying your rules, and seeing what they did

[Mail rules](../features/mail-rules.md) select mail as it arrives, so a rule you write today does nothing about the mail
already in the mailbox until you ask — or until a [schedule](../features/mail-rules.md#running-a-rule-on-a-schedule) you
gave the rule asks on your behalf. That asking is a command, and so is finding out what the rules have been doing:

| What you want | Command |
| --- | --- |
| See which rules your deployment is running, in order | `mfctl rules list` |
| Read one of them in full | `mfctl rules show file-invoices` |
| Apply them to mail that arrived before them | `mfctl rules run --account work` |
| Watch that run | `mfctl rules run-status --account work` |
| Find out what a rule did, or why a message is where it is | `mfctl rules history --account work` |

`mfctl rules list` is the one to run after editing a rule file. A deployment refuses a reload whose rules do not
validate and goes on running the previous set, which it reports to its log and nowhere else — so this is where you find
out whether your edit took effect, rather than from mail that kept being filed the old way. Each rule it prints says
what runs it, on a `Runs on:` line, which is how a rule naming [no
trigger](../features/mail-rules.md#which-triggers-run-a-rule) is told apart from one that simply never matched: nothing
fires such a rule by itself, and `mfctl rules run` is how it is run. A rule with a schedule says so on that same line,
with the occasions it declares beside the trigger. It is also where a rule you meant to run over arriving mail shows
that it never says `Arrival`.

None of these writes a rule, and none ever will: rules are configuration, so you change one by editing the file your
deployment reads. [Reading the rules, running them, and finding out what they
did](../operations/admin-endpoint.md#reading-the-rules-running-them-and-finding-out-what-they-did) is the operator's
reference for all five, including what the history records and what it deliberately does not.

## Classifying the mail you already have

[Spam classification](../features/spam-classification.md) reaches arriving mail on its own: a message is classified
because it arrived, and you ask for nothing. What it does not reach is the mail that was already there — everything
stored before you switched it on, and everything stored while it was off — so switching it on, switching filing on, or
moving a threshold does nothing about the existing mailbox until you ask:

| What you want | Command |
| --- | --- |
| Find out what classification would do to the mail you have | `mfctl spam run --account work` |
| Carry that out | `mfctl spam run --account work --apply` |
| Watch the run | `mfctl spam run-status --account work` |
| Find out why a message was filed as junk | `mfctl spam classifications --account work --email <id>` |

**The run is a dry run unless you add `--apply`**, and that is the order to do it in: run it, read what it found with
`run-status`, and only then run it again with `--apply`. With filing switched on, a run over an inbox is the largest
single thing MailFathom does to your mail, and the dry run tells you how much of it would move before any of it does.
The verdicts are recorded either way — what `--apply` adds is the mail server being written to.

A second run while one is going does not start a second walk; you are told the first is still under way, on the terms
it was started with. None of these writes a setting, and none ever will: whether mail is classified, what a scanner is
judged by, and what happens to junk are configuration, so you change them by editing the file your deployment reads.
[Classifying the mail you already have, and reading what was
concluded](../operations/admin-endpoint.md#classifying-the-mail-you-already-have-and-reading-what-was-concluded) is the
operator's reference for all three.

## Getting a folder's storage back

MailFathom never takes local mail away because a file changed. Switching a folder's `Synchronize` off stops mirroring it
and keeps what it already stored; removing its mapping leaves those rows where they are as well. Both are the right
default — an edit to a configuration file should not dispose of mail — and both leave you with storage you may actually
want back:

```console
$ mfctl folder erase --account work --folder archive
1043 stored emails erased from ARCHIVE under work. The folder holds none, and its checkpoint went with them, so
mirroring it again starts from the beginning rather than resuming.
```

This is the only command that erases mail, and it acts on one folder of one account. It refuses a folder your
deployment still mirrors, because the next synchronization run would simply fetch it all again — switch that folder off,
or take its mapping out, and ask again. A folder whose mapping you already removed is accepted, which is the case this
exists for.

Erasing a large folder takes a while and the command prints how far it has got. Stopping it is safe: what it reported
erasing is gone, the rest is untouched, and running the same command again continues from there. [Erasing a folder you
have stopped mirroring](../operations/admin-endpoint.md#erasing-a-folder-you-have-stopped-mirroring) is the operator's
reference, including what goes with the mail and what survives it.

## Background work that stopped

MailFathom does most of what it does in the background: classifying a message, embedding a passage, carrying out what a
rule asked for. A piece of that work that keeps failing is eventually given up on rather than retried forever, and what
is left behind is called a *dead letter* — a record of work nobody will attempt again. Nothing waits on one, so nothing
tells you it is there:

| What you want | Command |
| --- | --- |
| See what has stopped | `mfctl jobs dead-letters` |
| See what has stopped for one account | `mfctl jobs dead-letters --account work` |
| Run one again, after fixing what broke it | `mfctl jobs retry --job <id>` |
| Decide one will never run | `mfctl jobs drop --job <id>` |

```console
$ mfctl jobs dead-letters
2026-08-13 09:30:00Z  classify-email-spam 0199c3d0-0000-7000-8000-000000000002
  Failed:  Permanent PayloadUnreadable after 5 attempt(s)
  Work:    account:work|email:0199c3d0-0000-7000-8000-000000000001 for work
  Queued:  2026-08-13 09:00:00Z

Run one again with 'mfctl jobs retry --job <id>', or write it off with 'mfctl jobs drop --job <id>'.
```

**`Failed:` is what decides which of the two commands is right.** `Permanent` names something that will fail the same
way every time — a credential, a setting, a message the deployment cannot read — so retrying before you have changed
something achieves nothing. `Transient` names a dependency that stayed broken for longer than the queue was willing to
wait, which is the case `retry` exists for: fix it, or wait for it to come back, and ask again.

`retry` runs the same piece of work rather than enqueuing a second one, so it is safe on work that reaches your mailbox.
`drop` deletes nothing — the record stays, keeping what stopped it, and it goes on being the reason the same work is not
enqueued again. Neither command waits for anything: the deployment writes the decision down and the next worker to come
along carries it out, so closing the terminal changes nothing.

An empty reading is the ordinary state of a healthy deployment, and it says so rather than printing nothing. [Reading the
background work that stopped, and deciding what becomes of
it](../operations/admin-endpoint.md#reading-the-background-work-that-stopped-and-deciding-what-becomes-of-it) is the
operator's reference, and [durable background work](../operations/telemetry.md#durable-background-work) is what your
monitoring can watch instead of you reading this by hand.

## Where to go next

- [Administering a deployment](../operations/admin-endpoint.md) — the operator's reference for everything above
- [Mail rules](../features/mail-rules.md) — every fact, operator, and action a rule can use
- [Mailbox OAuth](../operations/mailbox-oauth.md) — registering the application, and every mode of the sign-in above
- [Changing the embedding model](../operations/embedding-profiles.md) — what activating, switching, and rolling back cost
- [Configuration reference](../operations/configuration-reference.md#adminendpoint) — every `AdminEndpoint` key
- [Secret provisioning](../operations/secret-provisioning.md) and [rotation](../operations/secret-rotation.md) — how
  the key on the server is supplied and replaced
