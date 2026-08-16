# Mail delivery

<!-- describes: src/Application/Mail/Delivery/**, src/Infrastructure/Mail/MailKit/Delivery/**, src/Infrastructure/Mail/MailAccountDeliveryOptions.cs, src/Infrastructure/Mail/SmtpAccountSettings.cs, src/Host/Configuration/Mail/ConfiguredSmtpAccountSettingsProvider.cs -->

Reading a mailbox and submitting to one are two capabilities against two servers, and MailFathom holds them apart.
What exists today is the submission half's foundation: an account may declare where its mail would be submitted, and a
**delivery session** can be opened against that server — connected, encrypted, authenticated, and asked what it will
accept. Nothing composes a message and nothing sends one, so a deployment that configures a submission endpoint gains
a validated endpoint and an openable session and no outbound mail.

That is deliberate rather than partial. The session is the piece every later step rests on, it is the piece with a
protocol, a credential, and a channel to get wrong, and it is provable on its own — against a real server, over each
mode that server speaks, and against the reply codes it answers with.

## The session, and who may open one

`IMailDeliverySession` lives in `Application` and is opened by `IMailDeliverySessionFactory` for one account. It
carries what the server said it will accept, and nothing else: opening it is establishing a submission channel, not
using one.

**Reading code cannot reach it.** The session type, its factory, and the MailKit adapter behind them are referenced by
the delivery path alone — no mailbox query, no synchronization run, no MCP tool, and no other adapter names them. That
is asserted rather than agreed to: `Boundaries.UnitTests` reads the compiled intermediate language of every MailFathom
assembly and fails a reference to either type from anywhere but `Application` and `Infrastructure`, which catches a
dependency that exists only inside a method body and that assembly metadata never shows.

Disposing the session quits the SMTP conversation and releases the connection. It is `IAsyncDisposable` for that
reason, and the release happens whether the session was used or not.

## What the server says it will accept

A session reports three facts, read from the greeting the server answered `EHLO` with and typed rather than left as
capability flags:

| Fact | Read from | Absent means |
| --- | --- | --- |
| Maximum message bytes | `SIZE` | The server declared no bound, and the session reports none. |
| Eight-bit content accepted | `8BITMIME` | Content has to be transfer-encoded to seven bits. |
| Internationalized addresses accepted | `SMTPUTF8` | Addresses have to be all-ASCII. |

`PermitsMessageOfSize` answers the size question in one place, so no caller re-derives what a missing `SIZE` means: a
declared bound is inclusive, and an undeclared one permits any size. A server advertising `SIZE 0` declares no usable
bound and is read as unbounded, which is what RFC 1870 leaves it meaning.

## Where a submission endpoint is configured

An account's `Delivery` block names it. The block is optional and an account that omits it configures no submission
endpoint at all — an ordinary shape, and the one every account has until somebody writes the block.

```json
{
  "Delivery": {
    "Host": "smtp.example.test",
    "Port": 587,
    "ConnectionSecurity": "StartTlsRequired"
  }
}
```

`Host` is what decides whether the endpoint exists: every other setting has a usable default or an inherited value, so
presence of a host is the whole switch. [The configuration reference](../operations/configuration-reference.md) holds
each key of the block, its default, and its constraint.

**The endpoint's own choice is where it is and how it is encrypted; everything else stays the account's.** A provider
serving implicit TLS for reading and STARTTLS for submission is the ordinary case, which is why `ConnectionSecurity`
is per endpoint — while the permitted mechanisms, the two weakening opt-ins, and the certificate authority are one
decision an account makes about itself and are read from its `TransportSecurity` section for both endpoints.

The credential is the account's too, unless the block names another. `UserName` and a `Secrets` block exist for the
deployment where submission goes through a relay in front of the provider and authenticates as somebody else; a
`Secrets` block present but naming no password reference reads as absent, so `"Secrets": {}` falls back to the
account's credential rather than resolving nothing.

## Transport security is judged by the account's rules

The submission endpoint takes the same five connection-security modes with the same `TlsOnConnect` default, and the
rules in [transport security](imap-synchronization.md#transport-security) apply to it unchanged: only the two
guaranteed-TLS modes need no opt-in, anything that can leave the channel unencrypted needs the account's
`AllowInsecureConnection`, and a clear-text mechanism over such a channel needs the second opt-in beside it.

Startup refuses an endpoint those rules reject, naming the account, the `Delivery.ConnectionSecurity` member, and the
violation the domain reported — for example `Account 'primary' submission endpoint: An unencrypted connection requires
AllowInsecureConnection. [UnencryptedConnectionRequiresExplicitOptIn]`. A mode bound from a number that names no member
is refused the same way rather than being assumed safe. The port is checked at startup as well, and a credential
configured under a block that names no host is refused rather than left silently unused.

## Authentication, and the fallback SMTP does not have

Mechanism selection is the account's allow-list, applied exactly as it is for reading: the adapter narrows the set the
server advertised to the permitted names and lets the mail library pick the strongest survivor, and it never restores a
removed mechanism after a failed attempt. `XOAUTH2` and `OAUTHBEARER` work here as they do there — an account permitting
only those authenticates with an access token from its configured `OAuth` block, and the token-bearing path is not a
reason to relax the channel rules.

What differs is the empty case. RFC 3501 leaves IMAP a clear-text `LOGIN` command as a last resort, and the reading
adapter permits it precisely when the allow-list already permits a clear-text mechanism. RFC 4954 gives SMTP no such
command: `AUTH` is the only way to present a credential, so a server advertising no mechanism the allow-list permits
leaves nothing to fall back to. The attempt ends there with `MailAuthenticationMechanismUnavailableException`, before
any credential is presented.

## Four stages, four budgets

Reaching a submission server is four things that fail differently, so each is bounded on its own and reported as
itself:

| Stage | Default | What it bounds |
| --- | --- | --- |
| Connection | 15 s | Opening the transport to the endpoint. |
| Greeting | 15 s | Encryption, the greeting, and the capability exchange. |
| Authentication | 20 s | The server answering the account's credential. |
| Command | 30 s | Any one command over the established session. |

A stage that runs out of budget raises a `TimeoutException` naming that stage and the account. That is what keeps it
distinguishable from the two other reasons the same call would stop: **caller cancellation** and **host shutdown** both
arrive as the caller's token being cancelled and are propagated as cancellation, never rewritten into a timeout. A hung
server can therefore never be read as a process shutting down, and a shutdown never as a server that stopped answering.

The first three bound the stages of establishing the session and sit inside the attempt budget of the `EmailDelivery`
resilience class, which is what a deployment configures. Their defaults total 50 s against that class's 60 s default
attempt timeout, so a stage can expire on its own before the enclosing budget takes the attempt away from it. The
command budget is not one of them and is not part of that total: it is set on the client itself and bounds a command
over the session once it is established, which is outside the establishment attempt.
[Outbound resilience](../architecture/outbound-resilience.md) holds that class, why delivery has the smallest shipped
budget of the six, and the single-layer rule the adapter follows.

## What a refusal means

A server refuses a command with a three-digit reply code and, usually, an RFC 3463 enhanced status code at the front of
the text. One classifier reads both, beside the session rather than with the general failure classification, and the
resilience pipeline reaches its own answer through that same classifier — so the two readings of one reply cannot
disagree.

- **The reply code decides.** A `4yz` reply is the server saying it did not take the command and that returning is
  welcome, so it is transient. Everything else is permanent, including a reply this system does not recognize:
  repeating a submission nobody understood is what puts a second copy in somebody's mailbox.
- **The enhanced code refines that in one direction only.** Where its class says permanent over a `4yz` reply, the
  permanent reading wins. An enhanced class that agrees, that contradicts in the safe direction, or that reports success
  inside a refusal changes nothing. Being wrong about a permanent failure costs a delivery that had already failed;
  being wrong about a transient one costs a message somebody receives twice.

A session that cannot be established — because the dependency's circuit is open, or because transient refusals used up
the attempts — fails with `MailDeliveryUnavailableException`, error code `27001`, naming the account.

## What never leaves the process

The rules that govern mail content govern this path too, and three things in particular stay inside it:

- **The credential and the access token.** Neither appears in a log line, a message, or an exception, and the resolved
  material is erased as soon as the attempt that asked for it ends.
- **The mechanisms the server advertised.** They describe a server's configuration and are held out of every failure
  message, including the one raised when nothing permitted remains.
- **Every address.** A refusal is logged as the account, the reply code, the enhanced status code, and whether it was
  transient or permanent — never the recipient, the sender, or the text the server wrote beside its numbers.

## What the tests establish, and where

Mechanism narrowing, capability reporting, each reply-code family, the refusal of an unsafe transport choice, the per
stage timeouts, and cancellation are unit tests against a substituted client, where every mode and every reply can be
scripted.

The integration suite opens a real session against the orchestrated GreenMail server and records what it advertises:
`AUTH PLAIN LOGIN XOAUTH2` and `SMTPUTF8`, with neither `SIZE` nor `8BITMIME`. That is the limit of what is provable
there and it is stated rather than assumed — the server speaks plain SMTP on a container port with no `STARTTLS` to
negotiate and no certificate to validate, so implicit TLS and STARTTLS are exercised in the unit suite alone, and so
are a declared size bound and eight-bit content. What the real server does settle is the pair no substitute can: an
account it can satisfy authenticates from the mechanisms actually on offer, and an account restricted to
`OAUTHBEARER` — which this server does not advertise, though it advertises `XOAUTH2` — is refused before a credential
is presented.
