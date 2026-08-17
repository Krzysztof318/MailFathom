# Mail delivery

<!-- describes: src/Application/Mail/Delivery/**, src/Domain/Delivery/**, src/Infrastructure/Mail/MailKit/Delivery/**, src/Infrastructure/Mail/Mime/Composition/**, src/Infrastructure/Persistence/Delivery/**, src/Infrastructure/Mail/MailAccountDeliveryOptions.cs, src/Infrastructure/Mail/SmtpAccountSettings.cs, src/Host/Configuration/Mail/ConfiguredSmtpAccountSettingsProvider.cs, src/Host/Configuration/Mail/MailDeliveryOptions.cs, src/Host/Configuration/Mail/MailSynchronizationOptions.cs -->

Reading a mailbox and submitting to one are two capabilities against two servers, and MailFathom holds them apart.
What exists today is the submission half up to the point of transmission: an account may declare where its mail would be
submitted, a **delivery session** can be opened against that server — connected, encrypted, authenticated, and asked what
it will accept — an authored message can be **composed into MIME**, and the send can be written down durably before
anything acts on it. Nothing transmits a message, so a deployment that configures a submission endpoint gains a validated
endpoint, an openable session, a composer, an outbox holding a message ready to go, and no outbound mail.

That is deliberate rather than partial. Each of those is a piece every later step rests on: the session is the piece with
a protocol, a credential, and a channel to get wrong; the composer is the piece that decides who a message says it is
from and what an authored field may not smuggle into a header; and the record is the piece that decides whether a crash
mid-send can deliver a message twice. Each is provable on its own — the session against a real server, over each mode
that server speaks and against the reply codes it answers with; the composer against the bytes it produces; and the
record against a real database, where a constraint rather than any code decides a race.

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

**The block also states who this account writes as**, through `FromAddress` and `FromDisplayName`. Most deployments
write neither: a provider that authenticates the mailbox by its address has already stated it as the account's
`UserName`, which is what the sending address falls back to, and writing the address alone is the honest default for the
name. They exist for the account whose login is a bare name rather than an address, and for the mailbox that sends under
an address it is not reached at. An endpoint that resolves to no address at all is refused at startup, because it would
compose nothing — and the display name is deliberately not the account's own `DisplayName`, which is the alias an
operator invented for their tooling rather than a name to sign somebody's mail with.

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

## Composing the message, and the headers this system owns

Between what somebody authored — these recipients, this subject, this text, these files — and the bytes a server accepts
sit a set of decisions that are made once, in one place. `IAuthoredEmailComposer` is that place, and the MimeKit adapter
behind it is the only code in MailFathom that assembles a message rather than parsing one. Callers hand over an
`AuthoredEmail` and receive either the bytes and the record to write, or a refusal naming the field that stopped it. No
MIME type crosses back.

**Some of those decisions are ownership.** The `From` address is the account's own and is never an argument: the
authored contract carries no sender at all, which is a stronger guarantee than validating one would be, and the address
comes from the account's `Delivery` block instead. The `Message-ID` is minted here from a cryptographically secure
random half and the account's own domain, so nothing outside this deployment can predict the identity of a message it
has not seen and forge a reply into its thread. The `Date` comes from the injected clock, as every timestamp in this
system does.

**Some are correctness at the protocol edge.** Every author-supplied value that becomes a header — the subject, the name
written beside each address, the name and declared media type of each file — is refused for carrying a line break rather
than sanitized: stripping it would compose a message whose subject is not what the author wrote and not what they would
be told about. An address outside ASCII is refused unless the submission server advertised **both** `SMTPUTF8` and
`8BITMIME`, before anything is transmitted — such an address is written as raw UTF-8 in the header block or not at all,
so a server offering only the first has not said it will accept one, and RFC 6531 requires it to advertise both anyway.
The refusal happens here rather than after the whole body has crossed the network. A subject outside ASCII is not
subject to that and never was: a header is encoded the way every mail transport has always carried one, and only an
address has no such encoding.

**Some are what the message is made of.** A plain-text body is required and an HTML alternative is optional; where both
exist the message is a proper `multipart/alternative`, and the plain text is the author's own rather than a reading of
the markup — a body produced by stripping tags is text nobody wrote, and every recipient whose client prefers plain text
would read that instead of the message. Required means written rather than merely supplied, so a blank plain text is
refused instead of composed — sending nothing to those same recipients is the worse half of what the rule exists to
prevent — and an alternative present but blank is refused for the same reason read from the other side. Each attachment
carries the media type its author declared it as, because deriving one from the octets would be this system asserting
what somebody else's file is.

**And some are bounds.** A recipient count, a body length, an attachment count, a per-file size, and a whole-message
size are all the deployment's numbers rather than whatever a caller passed, and each is checked before a connection is
worth opening. `MailDelivery` in [the configuration reference](../operations/configuration-reference.md#maildelivery)
holds each of them. The whole-message bound is the one a server has an answer to as well: the `SIZE` it advertised is
checked beside that number rather than in place of it, so whichever is smaller decides, while the other four are the
deployment's alone because nothing on the far side advertises them. It is measured on the composed bytes, because
transfer encoding, headers, and boundaries are the difference between what an author supplied and what a server is
offered — and files whose octets already exceed it together are refused before the assembly that would expand them,
since encoding only ever makes them more numerous.

**One mailbox is offered once.** Somebody an author named in two headers is placed in the more visible one — `To`, then
`Cc`, then `Bcc` — and the later mention is dropped, because a person meant to be seen must not be hidden from the other
recipients by an accident of ordering. A blind recipient is offered to the server exactly as any other is; what makes
them blind is that the transmitted headers do not name them. The count that is bounded is therefore the resolved one,
since a repeated mention is not a further person — but the authored list is measured against three times that bound
before any of it is read, because three headers is the whole of what a repetition can mean.

**A refusal names the field and the limit, and never the value.** An address, a subject, and a body are personal data of
the people a message is between, so nothing that reaches a log line, a metric, or an exception carries them. The codes
are `28001` for an account configuring no address to send from, `28002` for an injected header, `28003` for a field no
message can be composed from, `28004` for an internationalized address the server cannot carry, and `28005` for any
bound.

## The record a send is written down as, before anything is sent

Nothing transmits a message yet. What exists beside the session and the composer is the durable state a transmission
would be carried out against: an **outgoing record**, written before any SMTP command could be issued, and the message
it points at.

The reason is that a send is not one act. The MIME is built, an intent is recorded, a connection is opened, each
recipient is offered and accepted or refused, the body is transmitted, and the server answers — and a process can die
between any two of those. One of those windows cannot be decided from outside afterwards: a crash immediately after the
body went out and immediately before the acknowledgement was recorded leaves an outbox that cannot say whether the
message was delivered. Retrying sends it twice, and not retrying loses it. A duplicated delivery, unlike a duplicated
local copy, cannot be withdrawn from the mailbox it reached.

The record is the answer, in the same shape [remote mailbox mutations](imap-synchronization.md) use for IMAP: write the
intent down before acting on it, and advance it as the attempt proceeds. The window is narrowed rather than closed — the
record moves to *the transmission has begun and its outcome is unknown* **before** the transmission starts, so a row
found in that stage on restart is recognizable and is never blindly re-sent. What is then done with such a row is not
part of this, and neither is any retry policy.

`MailOutbox.EnqueueAsync` is the one way in. It takes what was asked for and the composed message, and writes both in one
transaction: a record whose message was never stored has nothing to transmit, and a message stored under no record is
bytes nothing will ever read.

**The same authored request arriving twice delivers once.** The identity is the sending account together with the act
that asked — a rule with its name, its revision, and the email it acted on; or a caller with a key of their own — and it
is enforced by a unique index rather than by any check. Two callers asking together both reach the database, the second
insert is refused there, and the loser's retry reads back the winner's record. A caller that generates a fresh key per
attempt has asked twice, and nothing can tell that apart from two genuine sends; what the record guarantees is that one
key sends once.

**The message is written once and read back, never recomposed.** A message rebuilt between attempts carries a different
`Message-ID`, which threads one send as two in every recipient's client, so a second enqueue of the same identity leaves
the stored payload exactly as it is. Raw MIME is reached through `IEmailContentStore` here as it is everywhere else, and
nothing above that port handles it as a byte array.

**A refusal is one recipient's, not the message's.** Each recipient carries its own outcome, so a mistyped address among
five does not stop the other four, and the four the message reached are not offered it again when the fifth is retried.
A recipient a server temporarily rejected stays outstanding with the reply that deferred them recorded beside it.

[The stored email schema](../architecture/stored-email-schema.md#the-outgoing-messages-waiting-to-be-sent) holds the
columns, the stages, and which of them each terminal stage may follow.

## What never leaves the process

The rules that govern mail content govern this path too, and three things in particular stay inside it:

- **The credential and the access token.** Neither appears in a log line, a message, or an exception, and the resolved
  material is erased as soon as the attempt that asked for it ends.
- **The mechanisms the server advertised.** They describe a server's configuration and are held out of every failure
  message, including the one raised when nothing permitted remains.
- **Every address.** A refusal is logged as the account, the reply code, the enhanced status code, and whether it was
  transient or permanent — never the recipient, the sender, or the text the server wrote beside its numbers. The
  outgoing record holds its recipients because a send cannot be resumed without them, and a failure about one never
  names the address: it names the record, and the position in the recipient list where a position exists — reading a
  stored row back names one, while an answer about somebody the record does not name has no position to give.

## What the tests establish, and where

Mechanism narrowing, capability reporting, each reply-code family, the refusal of an unsafe transport choice, the per
stage timeouts, and cancellation are unit tests against a substituted client, where every mode and every reply can be
scripted.

Composition is settled entirely in the unit suite, because it reaches nothing: the tests compose a message and read the
bytes back with the same parser this system reads arriving mail with. What they establish is each refusal against the
field it names, the sending address and the minted identity against an account that supplied neither, one mailbox named
in two headers becoming one offer in the more visible one, a blind recipient appearing in the envelope and in none of
the transmitted bytes, an eight-bit body transfer-encoded when the server takes none, and the line ending a stored
message carries — which matters precisely because the bytes are transmitted verbatim rather than re-serialized.

The integration suite opens a real session against the orchestrated GreenMail server and records what it advertises:
`AUTH PLAIN LOGIN XOAUTH2` and `SMTPUTF8`, with neither `SIZE` nor `8BITMIME`. That is the limit of what is provable
there and it is stated rather than assumed — the server speaks plain SMTP on a container port with no `STARTTLS` to
negotiate and no certificate to validate, so implicit TLS and STARTTLS are exercised in the unit suite alone, and so
are a declared size bound and eight-bit content. What the real server does settle is the pair no substitute can: an
account it can satisfy authenticates from the mechanisms actually on offer, and an account restricted to
`OAUTHBEARER` — which this server does not advertise, though it advertises `XOAUTH2` — is refused before a credential
is presented.

The outgoing record is split the same way. What the shape of the state guarantees — which stages are undecidable, which
recipients a later attempt still owes, and which terminal stage may follow which — is a unit test over the domain
record. What only PostgreSQL can settle is in the integration suite: the index refusing an insert two transactions each
reached without seeing the other, the second enqueue leaving the first message in place, a record left mid-transmission
being found and read as such by a later scope, and the cascade erasing the message and the recipients with the record.
