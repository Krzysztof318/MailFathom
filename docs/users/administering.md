# Administering your deployment

<!-- describes: backend/src/Cli/** -->

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

> **What it does change is the deployment's own state rather than its configuration**: it can place a mailbox
> credential, ask for work to be run, dispose of a folder's stored mail, and maintain the contact book. None of that is
> a setting, and none of it survives as one — [configuration sources](../operations/configuration-sources.md) stays the
> only place a deployment's behaviour is decided.

**What it prints is meant to be read and safe to capture.** A command's result goes to standard output and everything
else to standard error, so redirecting one captures the answer alone, and a redirected run — like any run whose
environment sets `NO_COLOR` — carries no escape sequences at all. How a listing and a single record are laid out, and
which lines colour marks, is stated in [administering a deployment](../operations/admin-endpoint.md) with the rest of
the contract.

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
  default. The keys are in the [endpoint configuration](../operations/configuration-endpoints.md#adminendpoint), and
  how it relates to the port your MCP clients use is
  [where each surface is served](../operations/configuration-endpoints.md#where-each-surface-is-served).
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

What is stored afterwards is one small file per user naming the deployments you signed in to — and the secrets
themselves go to your operating system's own store, the Credential Manager on Windows and your keyring on Linux, so the
file holds no credential at all. On a machine with no such store, a headless jump host being the usual one, the tokens
are sealed into that file instead under a key beside it; `mfctl login` says which of the two you got rather than
leaving you to guess. A key-pair profile keeps no credential in either place.
[Where the credential is kept](../operations/admin-endpoint.md#where-the-credential-is-kept) states the paths, what each
arrangement does protect, and what it does not.

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

`status` also prints what your credential may do, which is what decides whether any other command here will work:

```console
$ mfctl status
'production' (https://mail.example.test:8443) accepts the stored credential as 'workstation' (MailFathom 0.2.0).
It holds mailfathom.admin.read, mailfathom.admin.operate.
Documentation for that version: https://krzysztof318.github.io/MailFathom/v0.2.0/
```

A credential is granted a set of named permissions on the deployment, and each command needs the one its operation is
published under — two, for the twelve commands that read something before they change it. Signing in
needs none, so a key that reads `It holds no administrative permission` still signs in and is refused everywhere else —
which is how a credential is retired without its entry being removed. When a command is refused for want of one, it
names the permission to add and where it is written, so the answer is to widen that credential's grant rather than to
replace the key.
[What a credential may do](../operations/permissions.md) lists the names, what each covers, and which twelve commands need
a second one; [what the endpoint serves](../operations/admin-endpoint.md#what-the-endpoint-serves) names the permission
every route is published under.

When you work against one deployment for a whole session, `MAILFATHOM_ENDPOINT` states it once for the shell.
`--endpoint` beats it, and both beat the profile you last switched to.

Every command exits `0` when it did what you asked and `1` when it did not, having explained itself on standard error
first. [The troubleshooting table](../operations/admin-endpoint.md#troubleshooting) reads each message back to you as
a cause.

Each invocation also leaves a line in `~/.config/MailFathom/mfctl.log` — what ran, against which of your profiles, how
long it took, and how it ended — so a command you ran yesterday is still answerable today once the scrollback is gone:

```console
$ tail -3 ~/.config/MailFathom/mfctl.log | jq -r '"\(.at) \(.command) \(.outcome)"'
```

No credential and no mail goes into it, it is bounded so it cannot fill a disk, and `--no-log` leaves one invocation
out. What it does name is your own deployment — the profile, and the address where a failure message quoted the one you
typed — so read it before you paste it anywhere, the way you would the credentials file beside it. [What the command
records about itself](../operations/admin-endpoint.md#what-the-command-records-about-itself) has the path on each
platform, every field, and the switch that turns it off for a whole session.

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

## Finding out why mail is not arriving

A mailbox that looks empty and a mailbox nothing is fetching look identical from the outside. This is the command that
tells them apart:

```console
$ mfctl mailbox status
```

It reports, for every account your deployment configures and every folder it maps, what the deployment is doing right
now, how its last run ended, how far each folder has actually got, and when that last moved.

The reading worth learning is the pair of columns in each account's folder table. **Progress** is how far the deployment
has durably got and when it last got there; **Last run** is what happened the last time it tried. A folder whose
progress stopped yesterday and whose last run succeeded has nothing left to fetch. A folder whose progress stopped
yesterday and whose runs keep ending has stopped making headway, and the outcome beside it says why — an alias naming no
folder your server advertises, a server that stopped answering, or a failure to look up in the log. Without both columns
the two are indistinguishable, which is exactly the situation this command exists to end.

The account's own readings above that table say whether a run is happening now, queued behind other accounts, or
waiting; and, when runs have been failing, how many in a row — which is what a wait far longer than your configured
interval is explained by. [Administering a deployment](../operations/admin-endpoint.md#reading-what-synchronization-is-doing) reads every
line of the output back to you.

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
out whether your edit took effect, rather than from mail that kept being filed the old way. It prints one row per rule,
and the `Runs on` column says what runs each of them, which is how a rule naming [no
trigger](../features/mail-rules.md#which-triggers-run-a-rule) is told apart from one that simply never matched: nothing
fires such a rule by itself, and `mfctl rules run` is how it is run. A rule with a schedule says so in that same column,
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

## Filling in what a newer version records

An upgrade sometimes teaches MailFathom to record something new about a message — who was established to have sent it,
which keywords its folder carries. New mail gets it because it arrives after the upgrade. **The mail you already have
does not**, and nothing fills it in on its own: synchronization resumes from where it left off in each
folder, so a message it has already stored is never looked at again.

| What you want | Command |
| --- | --- |
| Fill in what is already in the mail you stored | `mfctl mailbox rederive --account work` |
| See how far that has got | `mfctl mailbox rederive-status --account work` |
| Fill in what only your mail server knows | `mfctl mailbox rewind --account work` |
| Any of them, for a single folder | add `--folder archive` |

**Reach for `rederive` first, because it is nearly free.** Anything the message itself carries is in the raw mail
already on your server's disk, so filling in the column means reading that back and parsing it. Nothing is fetched,
your mail server is not contacted at all, and nothing is marked read.

**The command asks for the re-reading and returns; the deployment does it.** Your terminal is not what keeps it going,
so closing it changes nothing and the run survives a restart of the deployment:

```console
$ mfctl mailbox rederive --account work
A re-derivation of every folder under work has been asked for.
Requested:  2026-08-18 12:00:00Z
Progress:   0 re-read, 0 unparseable, 0 no longer stored
The deployment carries the run in the background. Watch it with 'mfctl mailbox rederive-status --account work'.
```

Asking again while one is going is answered with the run already under way rather than starting a second, so a command
you are not sure landed is safe to repeat. How far it has come is a second command:

```console
$ mfctl mailbox rederive-status --account work
Scope:      every folder under work — under way
Requested:  2026-08-18 12:00:00Z
Progress:   1,043 re-read, 0 unparseable, 0 no longer stored
If it stops moving, look for the work that stopped with 'mfctl jobs dead-letters'.
```

**`rewind` is the one to be careful with.** Some things are your mail server's answer rather than the message's — flags,
keywords, the date it received the message — and the only way to learn them is to ask for the mail again. So `rewind`
forgets how far synchronization has got, which makes the next runs fetch the whole mailbox over again. It tells you how
much that is and asks before it does anything:

```console
$ mfctl mailbox rewind --account work
Scope:  every folder under work
Cost:   22,500 stored emails would be fetched from the mail server, re-read, and stored again.
Rewind that scope? [y/N]
```

Say no and nothing changes. Add `--yes` if you are scripting it and there is nobody to answer.

**Neither command deletes anything, and neither creates duplicates.** Your mail, its attachments, and everything built
from it stay where they are; mail fetched again lands on the message that is already there. Neither one re-embeds
anything either, so no refresh can run up a bill with your AI provider.

Both take a while on a large mailbox. `rewind` prints how far it has got as it goes, because the command is what does
it; `rederive` returns at once and `rederive-status` is where its progress is read. Neither loses what it has already
done: a deployment restarted mid-re-derivation picks the run up where it stopped, and mail it had already re-read is not
re-read again. [Bringing stored mail up to a later
release](../operations/admin-endpoint.md#bringing-stored-mail-up-to-a-later-release) is the operator's reference for all
three.

## Moving your stored mail into object storage

If you configure object storage for message content, MailFathom writes the **next** message there and leaves everything
it has already stored in the database. That is deliberate — a change to a configuration file should not start rewriting
where your mail is held — and it means switching does nothing for the mailbox you already have until you ask:

| What you want | Command |
| --- | --- |
| See how much is still in the database | `mfctl content move-status` |
| Start carrying it into the bucket | `mfctl content move` |
| Stop it while the machine is busy | `mfctl content move-pause` |
| Set it going again | `mfctl content move-resume` |
| Free the copies once you trust the bucket | `mfctl content release` |

The first one answers before you have configured anything, which is the point: it tells you how much of your database is
message content, so you know what a bucket would take off it.

```console
$ mfctl content move
To move:  22,500 payloads carrying 1,048,576 bytes
Move that content into the object backend? [y/N] y
Move:      under way
Progress:  0 moved carrying 0 bytes, 0 left in the database
Watch it with 'mfctl content move-status', and stop it with 'mfctl content move-pause'.
```

The command returns straight away — the deployment does the carrying, a little at a time, so it stays responsive while
it works. Closing your terminal stops nothing, and restarting the deployment loses nothing: it picks up where it was and
does not copy anything twice.

**Every message is checked before and after it moves.** MailFathom compares what it read against what your database
recorded for it, writes the object, reads it back, compares again, and only then points the row at the bucket. Anything
it cannot vouch for is left in the database, counted, and reported — nothing is lost and nothing is half-moved.
`mfctl content move-status` says how many, and asking for another move once you have fixed the cause picks them up.

**Pausing is safe and immediate.** It finishes the one message it is holding and stops there; resuming continues from
the same place.

**Moving copies; it does not remove.** When a message has been carried, MailFathom reads it from the bucket and your
database goes on holding it as well — so if the bucket ever fails to answer, the message is still served from the copy
you have. That safety net is the point, and it means a finished move leaves your database no smaller than it was.

Freeing those copies is a separate command you run when you are satisfied the bucket is answering:

```console
$ mfctl content release
Retained:  22,500 payloads carrying 1,048,576 bytes
Free those copies, leaving the object backend the only place that mail is held? [y/N] y
Released:  22,500 payloads carrying 1,048,576 bytes
```

**This one cannot be undone.** What it removes is the last copy of that mail outside the bucket, so nothing does it for
you — no interval, no finished move, no restart. It also refuses while any message is still waiting to be carried, and
`mfctl content move-status` tells you when nothing is. If you would rather MailFathom hold the copies for a fixed
period first, `ContentStorage:Release:SafetyInterval` is where you say how long, and even a release you ask for will not
touch anything younger than that. [Moving stored content into the bucket](../operations/moving-stored-content.md) is the
operator's reference — the order of the steps, what each one costs, and what each refusal is telling you.

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
Stopped               Job                                   Kind                 Failed                                          Work                                                              Queued
2026-08-13 09:30:00Z  0199c3d0-0000-7000-8000-000000000002  classify-email-spam  Permanent PayloadUnreadable after 5 attempt(s)  account:work|email:0199c3d0-0000-7000-8000-000000000001 for work  2026-08-13 09:00:00Z

Run one again with 'mfctl jobs retry --job <id>', or write it off with 'mfctl jobs drop --job <id>'.
```

**`Failed` is what decides which of the two commands is right.** `Permanent` names something that will fail the same
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

## Mail you asked it to send

Sending is the one thing MailFathom does that somebody else notices going wrong before you do. A message that will not
leave waits quietly: nothing retries it forever, nothing pages you, and the first sign is usually a person asking why
they never heard back. The `outbox` commands are how you look:

| What you want | Command |
| --- | --- |
| See whether anything is stuck | `mfctl outbox status` |
| See what is queued, newest first | `mfctl outbox list` |
| See only what is waiting | `mfctl outbox list --stage Recorded` |
| See who one message is for, and what their server said | `mfctl outbox show --message <id>` |
| Take one back before it leaves | `mfctl outbox cancel --message <id>` |
| Offer one again | `mfctl outbox requeue --message <id>` |

```console
$ mfctl outbox status
Stage              Messages
Recorded           2
TransmissionBegun  1
Sent               418
Refused            3
Cancelled          0

3 message(s) are still waiting. See which with 'mfctl outbox list'.
```

**The stage is what tells you which command is right.** `Recorded` is a message waiting for its next attempt — leave it
alone unless it stops moving, and it is the only stage `cancel` applies at. `Sent` and `Cancelled` are finished.
`Refused` is a message a server will not take, so offering it again is a decision to disbelieve that and the command
makes you say so: `mfctl outbox requeue --message <id> --despite-refusal`.

`TransmissionBegun` is the one that needs you. The message began to go out and the server never answered, so **nobody
knows whether it arrived** — and MailFathom will not guess on your behalf, because attempting it again might put a
second copy in somebody's mailbox rather than fix a failure. Read it with `outbox show`, decide, and then either
`requeue` it or leave it where it is.

`cancel` means the message reached nobody: the deployment refuses to withdraw one that has begun transmitting rather
than racing the worker sending it. `requeue` gives the attempts back and offers the message only to the addresses still
outstanding, so nobody a server already accepted it for gets it twice. Neither command waits for anything — the
deployment writes the decision down and the next delivery pass carries it out.

`mfctl outbox list` names no recipient and no subject, deliberately: a listing of who you write to and when is not
something to leave in a terminal. Ask `outbox show` about a message when you need to know who it was for.

[Reading what is in the outbox, and deciding about one
message](../operations/admin-endpoint.md#reading-what-is-in-the-outbox-and-deciding-about-one-message) is the
operator's reference, and [what delivering the outbox
emits](../operations/telemetry.md#what-delivering-the-outbox-emits) is what your monitoring can watch instead of you
reading this by hand.

## Keeping your contact book

MailFathom holds a contact book of its own — people, the addresses each of them uses, and what you wrote about them —
and `mfctl contact` is where you keep it:

```console
$ mfctl contact create --name "Anna Kowalska" --address anna@example.test
$ mfctl contact add-address --id 018f2b1c-9b3a-7c41-8f7d-2c6a5e9d10ab --address a.kowalska@work.example
$ mfctl contact show --address a.kowalska@work.example
```

A contact is a *person* rather than an address, which is why the third command answers with Anna rather than with a
match: one person uses a work address, a personal one, and an old one they still receive on, and the book knows those
are the same person. [Contacts](../features/contacts.md) is what the record holds and every rule it obeys.

Two of the commands are not conveniences. **`mfctl contact delete` erases somebody** — the record and their addresses go
from the database and nothing can put them back, so the command shows you the record and asks first. **`mfctl contact
export` writes everything held about a person** as JSON on standard output, which is what you redirect into a file and
hand to somebody who asked what you have about them. Those are the two things you will need on a day when somebody asks,
and they are commands so that you are not assembling either by hand on that day.

Listing is paged on purpose: your contact book is other people's personal data, so there is no command that prints all
of it in one go. `mfctl contact list` reads a page and prints the cursor for the next one. [Administering the contact
book](../operations/admin-endpoint.md#administering-the-contact-book) is the operator's reference for every command,
option, and refusal.

**The book can also fill itself, and it does not until you say so.** Switching
[contact collection](../features/contacts.md#collecting-contacts-from-arriving-mail) on for an account records the
people that account corresponds with as its mail is synchronized. Those records are the deployment's rather than yours:
`mfctl contact promote` is how you take one on, and every other command works on it afterwards. If you change your mind
about the whole thing, `mfctl contact delete-collected` erases everything it collected and keeps everything you entered
— and switching collection off in configuration is the separate act that stops the book filling again.

## Changing a setting without a restart

Most of what MailFathom reads comes from the files your deployment provisioned, and those stay yours: nothing in the
process edits one. What a deployment persists for itself is a document in the database, layered above those files, and
`mfctl config` is where you read and change it:

```console
$ mfctl config show MailboxSearch
MailboxSearch:
  SnippetsPerEmail = 3 [file (10-deployment.json)]
  WordsPerSnippet = 12 [persisted-layer]
2 settings, over persisted configuration version 7.

$ mfctl config set MailboxSearch:SnippetsPerEmail 5
```

**Every value comes with where it was decided**, and that is half the answer. A deployment reads its settings from
files, from that persisted document, and from an environment variable or a command-line argument somebody put beside
the process — so before changing anything, the reading tells you which of those you would actually have to edit. A
value an environment variable is supplying is refused rather than persisted, naming the source that outranks the
persisted layer, because persisting it would spend a version and change nothing you read.

`mfctl config unset` gives one setting back to the file beneath it. `mfctl config edit` opens the whole persisted
document in your `$EDITOR` and commits what you saved as one change, which is what you want when a change spans half a
section — set it up as `VISUAL="code --wait"` if your editor is a graphical one, since the command reads the file back
when the editor exits. And `mfctl config adopt` copies what your files decide beneath a path into the database, which
is the one thing here that stops a file deciding a value; it shows you exactly what it would take and asks first.

A setting that holds a credential reads back as `(redacted)` everywhere, including in the editor — the document holds a
reference to where the material is kept rather than the material, and neither reaches your screen.

[Configuration sources](../operations/configuration-sources.md#reading-and-changing-settings-from-mfctl) is the whole
of what a write proves before it commits and every refusal it can answer with.

## Where to go next

- [Administering a deployment](../operations/admin-endpoint.md) — the operator's reference for everything above
- [Configuration sources](../operations/configuration-sources.md) — where every setting can come from, and what
  `mfctl config` changes
- [Contacts](../features/contacts.md) — what the contact book holds, and every rule a writer of it obeys
- [Mail rules](../features/mail-rules.md) — every fact, operator, and action a rule can use
- [Mailbox OAuth](../operations/mailbox-oauth.md) — registering the application, and every mode of the sign-in above
- [Changing the embedding model](../operations/embedding-profiles.md) — what activating, switching, and rolling back cost
- [Endpoint configuration](../operations/configuration-endpoints.md#adminendpoint) — every `AdminEndpoint` key
- [Secret provisioning](../operations/secret-provisioning.md) and [rotation](../operations/secret-rotation.md) — how
  the key on the server is supplied and replaced
