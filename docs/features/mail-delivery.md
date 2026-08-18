# Mail delivery

<!-- describes: src/Application/Mail/Delivery/**, src/Domain/Delivery/**, src/Infrastructure/Mail/MailKit/Delivery/**, src/Infrastructure/Mail/Mime/Composition/**, src/Infrastructure/Persistence/Delivery/**, src/Infrastructure/Mail/MailAccountDeliveryOptions.cs, src/Infrastructure/Mail/SmtpAccountSettings.cs, src/Host/Configuration/Mail/ConfiguredSmtpAccountSettingsProvider.cs, src/Host/Configuration/Mail/MailDeliveryOptions.cs, src/Host/Configuration/Mail/MailSynchronizationOptions.cs, src/Host/Hosting/Workers/OutboxDeliveryWorker.cs -->

Reading a mailbox and submitting to one are two capabilities against two servers, and MailFathom holds them apart. The
submission half is whole: an account declares where its mail is submitted, a **delivery session** is opened against that
server — connected, encrypted, authenticated, and asked what it will accept — an authored message is **composed into
MIME**, a **reply or a forward is authored** from mail this deployment already holds, the send is written down durably
before anything acts on it, and it is then **claimed, transmitted, and settled** against the record it was written as. A
deployment that configures a submission endpoint sends mail.

Each of those is a piece the ones after it rest on, and each is provable on its own: the session is the piece with a
protocol, a credential, and a channel to get wrong; the composer is the piece that decides who a message says it is from
and what an authored field may not smuggle into a header; the record is the piece that decides whether a crash mid-send
can deliver a message twice; and the delivery is the piece that has to hold that guarantee while an attempt fails,
retries, is stopped by a shutdown, or loses the message to a second attempt. The session is proven against a real
server, over each mode it speaks and against the reply codes it answers with; the composer against the bytes it
produces; the record against a real database, where a constraint rather than any code decides a race; and the delivery
against both, plus a scripted server that can be made to answer in ways no real one can be asked to.

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

## Five stages, five budgets

Reaching a submission server and using one are five things that fail differently, so each is bounded on its own and
reported as itself:

| Stage | Default | What it bounds |
| --- | --- | --- |
| Connection | 15 s | Opening the transport to the endpoint. |
| Greeting | 15 s | Encryption, the greeting, and the capability exchange. |
| Authentication | 20 s | The server answering the account's credential. |
| Command | 30 s | Any one command over the established session. |
| Transmission | 5 min | Offering the envelope and transmitting the whole message, as one. |

The transmission budget is generous beside the others deliberately: it covers a message of up to the whole size bound
crossing a link this deployment does not choose, and cutting one short is what leaves a record nobody can settle. It is
bounded again from outside by the attempt timeout, which is what stops a submission outliving the lease that holds it.

A stage that runs out of budget raises a `TimeoutException` naming that stage and the account. That is what keeps it
distinguishable from the two other reasons the same call would stop: **caller cancellation** and **host shutdown** both
arrive as the caller's token being cancelled and are propagated as cancellation, never rewritten into a timeout. A hung
server can therefore never be read as a process shutting down, and a shutdown never as a server that stopped answering.

The first three bound the stages of establishing the session and sit inside the attempt budget of the `EmailDelivery`
resilience class, which is what a deployment configures. Their defaults total 50 s against that class's 60 s default
attempt timeout, so a stage can expire on its own before the enclosing budget takes the attempt away from it. The other
two are not part of that total: both bound work over a session that is already established, which is outside the
establishment attempt. The command budget is set on the client itself; the transmission budget is applied around the
submission and is enclosed instead by `MailDelivery:AttemptTimeout`, which is the outbox's own bound rather than the
resilience class's.
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

**The threading headers are owned here too.** `In-Reply-To` and `References` are what every mail client threads by and are
the whole of what it threads by, so a message answering another carries both or neither. Nothing above the composer
writes either one: an authored message states which conversation it answers and the composer writes the headers, which is
what keeps a second path from appending its own answer to a question this one settles. The identifiers themselves are
never a caller's — the section below is where they come from.

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

## Replying and forwarding from mail this deployment already holds

A reply and a forward are the two sends that begin from a message rather than from a blank one, and everything that makes
them correct is read out of the stored copy. `StoredEmailResponseAuthoring` is where that reading happens. What it takes
is the stable identity of the email being answered, which of the three acts it is, and what the author wrote; what it
produces is an ordinary `AuthoredEmail` that the composer above turns into MIME under the same bounds as any other. There
is no reply-shaped path below it and no second way into the composition.

**The identity of the answered message is the only thing a caller states.** Every value that decides whether the answer
is correct — the threading identifiers, the addresses, the subject, the quoted text, the files — comes out of the stored
copy that identity resolves to, so none of them can be supplied and none of them can be supplied wrongly.

**Threading is the first of those and has no partial credit.** `In-Reply-To` is the answered message's own `Message-ID`
and `References` is the path it carried with that identifier appended last, which is where a client looks for the
immediate parent. A message that carried no identity of its own can be answered and cannot be pointed at, so the answer
inherits its path and names no parent — naming an ancestor instead would attach the reply to the wrong message in the
same conversation. The path is bounded at 32 identifiers, far below the 256 a parse keeps, and gives up its middle: the
root names the conversation and the recent end is what a client walks. That cost is paid twice, because
[the conversation a message belongs to](../architecture/stored-email-schema.md#the-conversation-a-message-belongs-to) is
assembled from those same three identifiers — a reply whose headers are wrong comes
back from `Sent` as a conversation of one in this deployment as well as in every recipient's mailbox.

**Recipients are a decision, not a copy.** A reply goes to `Reply-To` where the sender wrote one and to `From` otherwise.
A reply to all adds the original's `To` and `Cc` in the headers they were written in, less the address the account sends
from — a deployment that answers a message it was copied on and mails itself has written a loop, and will then run its
arrival rules over its own answer. That address is the whole of what configuration states an account owns, so a mailbox
reached under a second address the `Delivery` block never names is not recognized as the account's own. The exclusion
reaches what a reply to all *adds* and nothing else: whoever asked for answers is who an answer goes to even when that
is this account's own address, which is what a message somebody sent themselves and a shared mailbox two colleagues both
send as both look like, and leaving it out would resolve the reply to nobody and refuse it. One mailbox is offered once and in the more visible header, as it is for any
authored list. A forward addresses nobody of its own: the people it goes to are people the original never named, so its
author names all of them. Which act it is, is explicit; there is no default that quietly becomes the other.

**The subject takes the conventional prefix only where there is not one already.** `Re:` and `Fwd:` are what this system
writes, and the comparison that decides whether to write one recognizes the prefixes actually in use — `Aw`, `Sv`,
`Vs`, `Odp`, `Res`, `Rif` and `Ynt` for a reply, `Wg`, `Tr`, `Doorst` and `Vl` for a forward, in the numbered `Re[2]:`
form as well. Recognizing only the English one produces the `Re: Re: Re:` a thread becomes unreadable as against every
correspondent whose client is not in English. The two sets are read apart rather than merged, because a language's two
markers mean opposite things: Finnish writes `Vs` for a reply and `Vl` for a forward, so a subject already carrying `Vl`
is a forward somebody is now replying to and takes `Re:` like any other. A prefix already there is left exactly as it was
written rather than incremented.

**A forward carries the original's own files, out of the content store.** That is the whole reason it is worth beginning
from a local copy: the alternative is a second fetch from the mail server, which a send has no business performing and
which would set the remote `\Seen` flag on somebody's mail, or rebuilding files from what was recorded about them, which
cannot be done. The files are held to the same `MailDelivery` bounds any attachment set is, measured from what the
message's own parse reported and checked before an octet of one is read, and a forward past one of them is refused naming
the limit. A part the sender left unnamed is named after its position in the composed message, because refusing to
forward a message over somebody else's omission is the worse answer of the two.

**The quotation is produced from that same reading.** An attribution line naming who wrote the message and when sits
above the quoted text in the plain-text body, and in the HTML alternative where the author wrote one — a message that
carried no markup of its own is quoted as encoded text there rather than inserted. The author's own words are never cut:
where the two together exceed what this deployment composes, the quotation is what gives way, and an author who writes
past the bound on their own is refused by the composition rather than silently trimmed.

Everything quoted from the answered message is bounded by what it costs in the body it is written into, which for the
HTML alternative is what the encoding produces rather than what the sender wrote. An ampersand becomes five characters
there and a quotation mark six, so a sender's display name and a message written out of those characters are both
several times their own length once they are markup — and a bound measured before that expansion would let one
correspondent's name decide whether somebody else's answer can be composed at all. The attribution line is bounded the
same way in both bodies, so the sender's name is what gives way rather than the sentence saying whose message this is.

**The stored email is a permission boundary as well as a source.** An email nothing may read is an email nothing may
forward, so an account this deployment no longer serves and a folder mapped `VisibleToTools: false` are both refused with
`28006` — the same not-found answer a read of that email gives, because telling them apart would let a caller learn which
mail exists by trying to reply to it. An email whose content this deployment cannot read is refused with `28007` instead:
content synchronization deliberately left unstored, a local copy that has gone missing or is damaged, bytes that no
longer parse, and a body inside a cryptographic envelope all arrive there, because an answer quoting nothing reads to its
recipient as an answer to an empty message. A damaged copy records a repair request on the way out, exactly as reading
the message's content does.

Nothing is written down and nothing is sent by any of this, and no log line, metric, or refusal carries an address, a
subject, or a line of quoted text.

## The record a send is written down as, before anything is sent

A transmission is carried out against durable state rather than against a caller's intent: an **outgoing record**,
written before any SMTP command is issued, and the message it points at.

The reason is that a send is not one act. The MIME is built, an intent is recorded, a connection is opened, each
recipient is offered and accepted or refused, the body is transmitted, and the server answers — and a process can die
between any two of those. One of those windows cannot be decided from outside afterwards: a crash immediately after the
body went out and immediately before the acknowledgement was recorded leaves an outbox that cannot say whether the
message was delivered. Retrying sends it twice, and not retrying loses it. A duplicated delivery, unlike a duplicated
local copy, cannot be withdrawn from the mailbox it reached.

The record is the answer, in the same shape [remote mailbox mutations](imap-synchronization.md) use for IMAP: write the
intent down before acting on it, and advance it as the attempt proceeds. The window is narrowed rather than closed — the
record moves to *the transmission has begun and its outcome is unknown* **before** the transmission starts, so a row
found in that stage on restart is recognizable and is never blindly re-sent. What is then done with such a row is
[below](#the-window-that-cannot-be-decided).

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

## How a written-down send reaches a server

Two things start a delivery pass over one account's outbox, and they answer different questions.

**The account's own synchronization run drains it**, as the last thing it does after its folders. That is the guarantee:
whatever is outstanding is claimed again on every run, so a signal that was never delivered, a process that was stopped
mid-backlog, and a send whose backoff elapsed while nothing was watching all resolve without anything having to
remember them. The drain never fails the run — SMTP is a different server from IMAP, and an outbound provider that is
down must not back an account's mailbox synchronization off — so a pass that ends unexpectedly is logged and the run
carries on.

**A signal makes it prompt.** Writing a record signals its account through a bounded in-process queue, and a worker
waiting on that queue takes a pass immediately. A message somebody authored, or a tool call that answered with a queued
identifier, must not wait behind a mailbox scan. The queue holds accounts rather than messages and an account already
waiting is not queued twice, so a hundred messages written at once produce one pass rather than a hundred and the depth
cannot grow past the number of configured accounts. **The backpressure is explicit**: a signal that finds the queue full
is refused and the caller is told so, which is a delay rather than a loss, because the run above is what picks the work
up. A pass that filled its batch signals its own account again, so a backlog drains rather than trickling one signal at
a time.

One account at a time, in one loop. A pass already attempts its sends one after another because they share a submission
server, and a second loop beside it would be an unstated second bound on how many connections this deployment opens to
the providers it sends through. A pass that throws is confined to its account: the loop logs it and serves the next
account, because a database briefly unavailable for one account says nothing about whether another has mail to send.

**A pass claims before it attempts.** One statement takes a batch of the account's due sends, oldest first, and stamps
each with an owner, an expiry, and the attempt it is about to be given — so no instant exists in which a send is chosen
but unheld, and two passes over one account take disjoint sets rather than queueing behind each other. Every write about
that send afterwards carries the lease it was claimed under and is refused if the row no longer names that owner, which
is what stops an attempt whose lease ran out from recording an outcome over the attempt that has since taken the
message. The attempt itself is bounded below the lease, so that case stays rare rather than routine, and startup
refuses a configuration stating otherwise.
[The claim a delivery attempt holds](../architecture/stored-email-schema.md#the-claim-a-delivery-attempt-holds) has the
predicate and the columns.

## The window that cannot be decided

A crash immediately after the body went out and immediately before the acknowledgement was read leaves a record nobody
can settle: re-sending puts a second copy in somebody's mailbox and not re-sending loses the message. MailFathom does
neither and says so instead.

**The stage is written before the transmission and never rewound past what was transmitted.** An attempt that failed
before any recipient had been accepted has provably offered no body — a server is only ever handed one after at least
one `RCPT TO` was accepted — so its record goes back to *recorded* and is attempted again. An attempt that failed after
an acceptance may have transmitted, so its record stays at *the transmission has begun*, is stamped with error code
`28011`, and is claimed by nothing: the claim takes recorded rows alone, so no lease expiry and no restart reaches it.
Which of the two applies is read from the replies the server actually gave during that attempt rather than from the
exception that ended it.

Such a send is **visible rather than silent**: it stays in the outbox an operator reads, it says *unknown* rather than
*stuck*, the worker logs it at error level, and the delivery counter measures it under `outcome-unknown`. It moves only
when somebody decides what happened; nothing automatic re-queues it.

A pass stamps whatever it finds in that stage before it claims anything, so a send stranded by a process that stopped is
marked on the next pass rather than at some later restart.

## Failing, retrying, and the single layer of it

A server that answered settles the outcome by itself, and only a non-answer consults what the attempt observed:

- **A permanent refusal is terminal at the first answer.** No attempt is spent on a reply that will not change, and the
  record carries the code the server answered with. A message every one of whose recipients was refused is terminal
  even where the server did not refuse the message.
- **A transient refusal defers.** The record goes back to being claimable at an instant in the future, and the delay
  doubles per attempt from `RetryBaseDelay` up to `RetryMaxDelay`, drawn with jitter so a provider that refused every
  account at once is not offered all of them back together. The same backoff the durable job queue uses, rather than a
  second implementation of one.
- **A send that spends `MaxAttempts` stops being attempted** and stands in the outbox with error code `28010`, rather
  than being retried forever.
- **Host shutdown is neither.** A send the host stopped before it transmitted anything gives its lease back together
  with the attempt it had counted, so a restart is not a spent attempt; a send it stopped after an acceptance is the
  undecidable case above.

**There is exactly one retry layer, and this is it.** The submission is deliberately not run inside the retrying
`EmailDelivery` resilience pipeline that establishes a session, because a retry there would re-transmit a body a server
may already have taken. What that pipeline still covers is reaching the server at all; what happens to a message once
it is being offered is the outbox's, once.
[Outbound resilience](../architecture/outbound-resilience.md) holds the single-layer rule.

The code the record carries says which of these ended it, and it is what an operator looks the send up by:

| Code | What ended the send |
| --- | --- |
| `28001` | The account configures no address to send from, so nothing could be offered |
| `28005` | The composed message exceeds what the submission server said it accepts |
| `28009` | A server permanently refused the message, or refused every one of its recipients |
| `28010` | Every attempt was spent, the last of them on a failure that could still have cleared |
| `28011` | A transmission was begun and the server never answered it |
| `28012` | The attempt ended in something none of the above describes |

`28008` is the one code that is not a send's ending. It records an attempt that finished after its lease had already
passed to another attempt, so nothing it observed was written down; what the send ends as is whatever the attempt
holding it concludes.

Caller cancellation, host shutdown, an expired stage budget, an authentication failure, and a transport failure remain
distinguishable in what is recorded and what is logged, exactly as they are while a session is being established: a
shutdown is never written down as a timeout, and a hung server is never written down as a process stopping.

## What one recipient's refusal costs the others

A message is offered address by address and answered address by address, and the delivery keeps that resolution rather
than collapsing it into one verdict:

- A recipient the server **accepted** is recorded as accepted, and a later attempt of the same message offers only the
  recipients still outstanding. Nobody is sent the same message twice because somebody else's address was wrong.
- A recipient the server **permanently refused** is recorded as refused and is never offered again. The message is still
  delivered to everybody else, and the send ends as sent.
- A recipient the server **temporarily refused** stays outstanding with the reply that deferred them recorded beside
  them, and the send is deferred as a whole so those addresses are offered again.
- A message **no recipient accepted** is terminal, because there is nobody left to offer it to.

The per-address replies are read off the submission conversation as the server gives them, and matched back to the
address this deployment offered rather than to whatever form the server echoed — a server that answers about an address
in a different case is answering about the same person, and one that answers about an address nobody offered is
answering about nothing and is dropped.

## What an operator sees while mail is leaving

Each attempt opens the `submit_outgoing_email` span over the exchange with the server, its duration is recorded, and
its outcome counts under `mailfathom.mail.delivery.attempts` by account. `outcome-unknown` is the value worth alerting
on at any rate above zero, because each measurement is a message nothing will attempt again until a person decides.
[Telemetry § What delivering the outbox emits](../operations/telemetry.md#what-delivering-the-outbox-emits) holds the
instruments, the tags, and what none of them carries.

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
the transmitted bytes, an eight-bit body transfer-encoded when the server takes none, the two threading headers written
together or not at all, and the line ending a stored message carries — which matters precisely because the bytes are
transmitted verbatim rather than re-serialized.

Authoring a reply or a forward is settled in the unit suite for the same reason, since it reaches no server either. What
the tests establish there is the threading of an answer to a message with and without a `References` header and to one
carrying no identity of its own, a reply to all against a message naming the answering account in both `To` and `Cc`, the
subject-prefix comparison across the spellings clients write, the forward of a message whose files exceed each of the
three bounds in turn, the refusal of an email withheld from tools and of one whose content this deployment cannot read,
and the repair request a damaged copy leaves behind on the way out.

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
being found and read as such by a later scope, a claim holding a record against the next claim, and the cascade erasing
the message and the recipients with the record.

Delivery is where the two halves meet, so it is proven from both ends.

The unit suite carries the cases a real server cannot be asked for. A crash at each stage of an attempt, over the
rewind and the refusal to rewind on either side of the first acceptance; a lease reassigned while an attempt was
transmitting, which then writes nothing at all; a partial acceptance and the retry that offers only the addresses still
outstanding; a permanent per-recipient refusal that still delivers the message; every recipient refused; the attempt
bound being spent; host shutdown before and after an acceptance; and each of the settings validations, including the
attempt timeout that reaches its lease. Beside them the loop's own claims — that a signal is what wakes it, that a full
batch asks for its account again, and that neither a failed pass nor an account with nowhere to submit stops the account
behind it — and the signal's own: that one account signalled a hundred times is one pass, that a full queue refuses and
says so, and that a queue refilled as fast as it drains still ends when the host stops.

The transmission itself is a unit test against a scripted submission client, because every reply class can be stated
there and none can be provoked from a real server on demand: a message accepted, a message refused permanently and
temporarily, no address accepted, a server that stops answering mid-transmission with the addresses it had already
accepted kept, and the bytes a transmission offers — blind recipients absent from the transmitted headers, and the line
endings a submission requires.

The integration suite settles what only a real server and a real database can. A queued send is delivered, recorded as
sent, and then found in the mailbox it was addressed to — read back over a connection nothing under test owns, which is
what makes the arrival an observation rather than the outbox agreeing with itself. The same authored send queued twice
produces one message. A send left mid-transmission is marked and transmitted no further, and the mailbox holds no copy
of it. What the orchestrated server cannot settle is a refusal: GreenMail accepts every recipient it is offered and
creates the mailbox behind it, so both refusal shapes stay in the unit suite — the same division that already puts the
size and eight-bit capabilities there.
