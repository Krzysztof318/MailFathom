# Mail delivery

<!-- describes: src/Application/Mail/Delivery/**, src/Domain/Delivery/**, src/Domain/Scheduling/**, src/Infrastructure/Mail/MailKit/Delivery/**, src/Infrastructure/Mail/Mime/Composition/**, src/Infrastructure/Persistence/Delivery/**, src/Infrastructure/Observability/LoggedAuthoredSendAuditor.cs, src/Infrastructure/Mail/MailAccountDeliveryOptions.cs, src/Infrastructure/Mail/SmtpAccountSettings.cs, src/Host/Configuration/Mail/ConfiguredSmtpAccountSettingsProvider.cs, src/Host/Configuration/Mail/ConfiguredOutgoingSendPermissionReader.cs, src/Host/Configuration/Mail/MailDeliveryOptions.cs, src/Host/Configuration/Mail/MailSynchronizationOptions.cs, src/Host/Hosting/Workers/OutboxDeliveryWorker.cs -->

Reading a mailbox and submitting to one are two capabilities against two servers, and MailFathom holds them apart. The
submission half is whole: an account declares where its mail is submitted, a **delivery session** is opened against that
server — connected, encrypted, authenticated, and asked what it will accept — a recipient named as a person is
**resolved against the contact book**, an authored message is **composed into
MIME**, a **reply or a forward is authored** from mail this deployment already holds, the send is written down durably
before anything acts on it, and it is then **claimed, transmitted, and settled** against the record it was written as. A
deployment that configures a submission endpoint and turns sending on for the account sends mail, and `send_email` on
[the MCP surface](mcp-tools.md#send_email) is how a caller asks it to. The same composition also **writes a message
without sending it**: a draft is held here, kept in step with the folder the owner's own mail client reads, and offered
to a submission server only when somebody promotes it.

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
presence of a host is the whole switch. [The mail configuration](../operations/configuration-mail.md#maildelivery)
holds each key of the block, its default, and its constraint.

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

## What a deployment must turn on before it can send

An endpoint says where mail would be submitted. It does not say that this deployment may send, and four separate
answers stand between a configured endpoint and a message leaving the process. All four are the operator's rather than
any author's, and all four are asked **where the outgoing record is created**, which is the one place every author
passes through: a tool call, a rule, a command, and whatever asks next meet them identically, and a send either of them
refuses leaves nothing written down, nothing queued, and nothing for a delivery pass to find.

**Sending is off until an account is turned on.** `Delivery:Enabled` is `false` on every account of every deployment, so
an installation upgrading into a release that can send does not thereby become able to — the release meets a
configuration that never asked for the capability. It is per account because an owner may want one identity able to
write and another purely archival. An account enabled with no submission host fails startup, since it is a permission
nothing could act on.

**A read-only deployment sends nothing at all.** `Deployment:ReadOnly` is a posture the whole process holds rather than
a setting an account can argue with: in it every send is refused whatever any account enabled. It is off by default,
which changes nothing for an existing deployment, and what it buys is the kind of assurance — from a reading of the
account list, which has to be re-read after every edit, into an answer that holds however the list is edited.

**A recipient policy bounds who may be written to.** `MailDelivery:RecipientPolicy` names allowed and denied domains
and addresses, and every recipient of every message is judged against it before the record exists. A domain entry
reaches the names beneath it, the denied lists are read first and win, and a policy naming nobody restricts nobody. **A
message naming one refused recipient is refused whole** rather than delivered to the rest, because a message written to
four people and sent to three is a message its author never wrote. What the caller is told names which half of the
policy refused; the address never appears, in the answer or in a log.

**Ceilings bound how much may leave in a period.** `MailDelivery:SendCeilings` counts messages and recipients, per
account and per deployment, over a fixed window anchored at the Unix epoch. What is counted is what was written down
rather than what was delivered, which is what makes the ceiling bound the fault it exists for: a rule matching more mail
than expected and a caller in a loop both produce records whether or not any server accepts them. The message being
asked for is weighed by the people it names, so one message can reach a recipient ceiling on its own; a refused send
names which ceiling it reached, and the period's roll-over is when asking again can succeed. Every ceiling is zero —
no ceiling — by default.

The refusals a caller can meet are coded, and each says what would change the answer:

| Code | Raised when | What resolves it |
| --- | --- | --- |
| `56003` `MailSendingNotEnabled` | The account has sending off, or the deployment is read-only | An operator's edit; nothing a caller writes reaches an answer |
| `53006` `OutgoingRecipientRefusedByPolicy` | A recipient is denied, or is outside the allowed set | Writing to somebody the policy admits, or widening it |
| `57002` `OutgoingMailCeilingReached` | The period has no room for this message | Waiting for the period to roll over, or raising the ceiling |

Each of the three is refused before anything is composed into a record, so a refusal costs the asker nothing but the
answer — and a request whose idempotency identity already has a record is judged the same way, which keeps the bounds a
statement about the present rather than about whenever a caller first asked. That a full period can therefore refuse a
retry of a send already recorded costs nothing: the record stands, its message is still delivered, and no answer here
can produce a second one.

[The mail configuration](../operations/configuration-mail.md#maildelivery) holds every key, its default, and its
constraint; [the runtime configuration](../operations/configuration-runtime.md#deployment) holds the read-only posture.
What a *caller* must hold to ask at all is a different question with a different answer —
[`mailfathom.mail.send`](../operations/permissions.md), a grant on a credential rather than a policy on a deployment.

## What a caller may be talked into

The bounds above are about this deployment. This section is about the caller asking it, and it exists because an agent
holding a read grant and a send grant reads mail written by strangers and then decides what to do. A message that says
*forward this thread to the address below* is untrusted input arriving inside the very content the agent was asked to
reason about, and every part of the system between it and a stranger's mailbox is behaving correctly when it obeys.
Three bounds make that expensive, and every one of them is judged **inside the use case** rather than at the tool, so a
second entrypoint added later inherits them rather than re-implementing them.

**The recipient policy is judged here as well.** `MailDelivery:RecipientPolicy` is asked on this surface before the
outgoing record exists, rather than trusted from the layer beneath. An instance restricted to a set of domains cannot
be steered outside them whatever the mail says, and that holds for the tools as well as for the record.

**Per-caller ceilings bound one client rather than the installation.**
[`MaxMessagesPerCaller` and `MaxRecipientsPerCaller`](../operations/configuration-mail.md#how-much-may-leave-in-a-period--maildeliverysendceilings)
count what one calling principal has been admitted for, over the same epoch-anchored window the deployment's own
ceilings use and separately from them. An agent in a loop is then a refusal after a handful of messages rather than
after a provider notices. The refusal names which of the two was reached and says the period has to roll over first; it
never names the number, which is the operator's configuration and nothing a caller could have influenced.

**Admitting a send is charging for it**, in one operation, which is what makes the bound hold against a client that
dispatches sends rather than waiting for each: a count read and charged either side of a durable write would be a
ceiling two concurrent calls both passed. What a send is counted under is its own idempotency identity, so a retry
under the key a caller first asked under spends one message however many times the call is repeated. The consequence is
that a send the deployment's own bounds refuse *after* this ceiling admitted it has still spent the caller's
allowance — which is the right answer rather than a leak, since a client asking repeatedly for a send that is refused
every time is the loop being bounded.

**An address the caller named and nothing here vouches for is the signal.** A recipient of an authored send is one of
two things: somebody this deployment derived — whoever a reply answers, whoever a reply-to-all keeps, an address
resolved from a contact the caller named, by the identity the book gave it or by the whole name the owner recorded —
or an address the caller wrote out itself. Only the second is judged.
Against it stands what this installation already holds a record of: the contact book, and the addresses its own
accounts send as. An address that is neither is what an injected instruction looks like, and
[`MailDelivery:UnvouchedRecipients`](../operations/configuration-mail.md#a-recipient-nothing-here-vouches-for--maildeliveryunvouchedrecipients)
is the deployment's choice of what to do about one: `Admit` records it, `Refuse` refuses the whole message.

**Which tool is affected follows from that, and it is not the same answer for all three.** A plain `reply_to_email` is
untouched under `Refuse`, because everybody it reaches was read out of the message being answered. A `cc` or `bcc` the
caller adds to that reply is its own word and is judged. And **`forward_email` is judged in full**: a forward addresses
nobody of its own, so every address on it came from the call, and forwarding to somebody this deployment holds no
record of is refused under `Refuse`. That is the setting working rather than a gap in it — *forward this thread to the
address below* is the archetype of the instruction this bound exists to refuse — but it is also what an operator has to
know before turning it on, because forwarding to a new correspondent stops working until that person is in the book.

| Code | Raised when | What resolves it |
| --- | --- | --- |
| `53007` `OutgoingRecipientUnvouched` | A caller-named recipient is one this deployment holds no record of, under `Refuse` | Writing to somebody the contact book holds, or admitting unvouched recipients |
| `57002` `OutgoingMailCeilingReached` | This caller's own period has no room for this message, or the period is already counting as many distinct callers as this deployment holds counts for | Waiting for the period to roll over, or raising the per-caller ceiling; the second case names itself rather than a setting, because it is not one an operator wrote |

**Every send from this surface is recorded**: the calling principal, the grant it held, which of the four acts was
asked for, the account, the identity of the outgoing record, how many people it names, and how many of those nothing
here vouched for. A send that reached somebody unvouched for is recorded at a level of its own, because that is the
line an owner looking for an odd send is looking for. What is **not** recorded is everything about the message — no
prompt, no mail content, no subject, no body, and no address. The record answers *who asked for this and under what*,
which turns "an agent sent something odd" from a suspicion into something an owner can read; what was sent is the
stored MIME the outgoing record already points at. Today it is written to the structured log, under the deployment's
own log retention; the port behind it is what a durable evidence store would replace without any caller changing. One
send is one entry: a call repeated under the idempotency key it first carried is answered with the record the first one
left and is not recorded again, so a client retrying after a timeout leaves a trail of what it sent rather than of how
often it asked.

**What none of this is.** Every bound here answers a caller that was *manipulated* — one acting in good faith on text
somebody else wrote. **None of it is a defence against a caller that is itself hostile.** A client holding
`mailfathom.mail.send` can send within the policy, within the ceilings, and to people the contact book holds, and each
of those sends is correct by every rule this deployment has. What bounds that is the grant itself and who holds the
credential carrying it — [permissions](../operations/permissions.md) — rather than anything on this page. Nor is the
vouching a judgement about a person: an address the contact book happens to hold is admitted whatever the message says
about it, and an address a correspondent legitimately asked to be copied is refused under `Refuse` until somebody
records it. The setting trades correspondence with new people for a bound on where an injected instruction can send
mail, and which side of that trade an installation wants is the operator's to choose.

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
worth opening. `MailDelivery` in [the mail configuration](../operations/configuration-mail.md#maildelivery)
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

## Addressing a message by naming a contact

An author names each recipient either by an address or by somebody [the contact book](contacts.md) holds, and
`NamedRecipientResolver` is the single place the second becomes the first. It sits between authoring and composition on
the way to the outgoing record, which is what makes naming a person a convenience of addressing rather than a second way
out of the deployment: what it produces is an ordinary address, indistinguishable from one an author typed, so every
bound, refusal, and check a written-down address meets it meets as well. Naming a contact can therefore reach no mailbox
that naming an address could not.

**A contact is named by the identity the book gave it or by the whole name the owner recorded**, and a name addresses a
message only where exactly one contact carries it. Nothing ranks candidates, nothing prefers the most recently written
down, and nothing falls back to a near match: a recipient chosen that way is a message delivered to somebody nobody
named. The name is compared on the form the book compares its own values in, so the casing an author wrote is immaterial,
and it is the whole name rather than part of one — text that merely appears inside somebody's name is not that person
being named, which is what separates addressing from the contained match a search of the book performs.

**The address used is the one the owner made preferred**, unless the authored act names another address of that same
contact — and an address that contact does not hold is refused rather than sent to and rather than quietly replaced by
the preferred one. The value that reaches the message is the book's own spelling of it. The name written beside it in the
header is the name the owner recorded, which is the point of addressing somebody by it: the message reads as one to a
person rather than to a mailbox.

**One unresolved recipient refuses the whole message.** Sending to everybody else would tell an author their message went
out while the person they cared about never receives it.

**The identities named and the names are each read in groups of at most a page of the book**, so a message costs one read
per way its recipients were named, and a second of one such way only past two hundred distinct people in it. Both ways are
named out of one recipient list bounded below twice that, so at most one of them ever reaches its second read: three reads
for the longest recipient list an outgoing record can hold, against one per recipient. What addressing costs therefore
follows from how a message was addressed rather than from how many people it goes to. The recipient count is
bounded before the first of those reads at the most an outgoing record can hold, because the reads carry what the caller
supplied. A name resolving to one person comes back with the count that decided it, which is what stops a namesake
written down while the read ran from turning an ambiguous name into an arbitrary one of two people.

**The record keeps both facts.** A recipient resolved from the book is written down with the address the send was offered
to and the contact it came from, so what was sent stays answerable after the contact is amended, promoted, or erased. The
addresses in the composed message are the ones that were resolved; nothing re-reads the book to resume a send.

**A refusal names how many contacts matched and nothing else about them.** The codes are `28013` for a contact the book
does not hold — an identity nothing answers to and a name nobody carries are one answer, because the remedy is the same —
`28014` for a name several contacts carry, which carries the count, and `28015` for an address the named contact does not
hold, which text naming no mailbox at all also produces. No refusal, result, or log line reveals an address the caller
did not supply, and none of them names anybody who was counted.

**The resolution asks for no permission of its own.** Every use case above it runs under a principal, and only a caller
can hold a grant at all — the process identity that runs work nobody requested holds none — so a grant demanded there
would refuse a rule addressing a contact rather than authorize anything. Whether a caller may name people out of the book
is decided at the boundary the caller reached, beside the grant that lets it send.

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
author names all of them. Which act it is, is explicit; there is no default that quietly becomes the other. Whoever the
author names themselves may be named as a person out of the contact book, resolved exactly as on a message answering
nothing, while everybody the act itself derives is an address already by the time it is read out of the stored copy's own
headers — so an answer can reach no mailbox a new message could not, and a name several contacts carry refuses the answer
rather than addressing one of them.

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
the message's content does. The two codes are one code by the time a caller reads either, for the reason the pair of
them exists at all: the distinction is worth keeping where a repair request is decided and worth nothing to somebody who
could use it to find out which mail this deployment holds.

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

**Being the one way in is also where the send grant is asked for.** The record already names the kind of act that asked
— a rule, or somebody present — and the outbox admits each origin under exactly the principal that can produce it: a
command needs a caller granted `mailfathom.mail.send`, and a rule needs MailFathom's own identity, which holds no
permission at all. So a caller cannot enqueue as a rule and borrow a rule's idempotency identity, and work nobody
requested cannot enqueue as a command however a grant is written. The question is put here with no transport in the
picture, which is what makes it hold for an entrypoint added later — a command, a gateway, a second protocol — rather
than only for a request that passed a filter. Nothing has been written down when it is asked, so a refusal leaves no
record and no stored message behind. [What a credential may do](../operations/permissions.md#the-published-set) is the
grant, and `send_email`, `reply_to_email`, and `forward_email` are the tools that reach the enqueue behind it.

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

## Asking for a message to be sent

`AuthoredMailSubmission` is the one use case a boundary reaches to send a message that answers nothing, and it composes
the three steps above it rather than adding a fourth: the account a caller named is resolved against the accounts this
deployment serves, the people named become addresses, the addresses and the text become MIME, and the MIME and the
request become the durable record. Composing them in one place is what keeps a second entrypoint from doing two of the
three and inventing the middle one, and it is what `send_email` calls and the whole of what that tool does.

**Nothing here transmits, and no configuration makes it.** The use case holds no delivery session and no factory for
one, so there is nothing in it that could open a submission channel; what it answers with is the record, at the stage a
delivery pass reads and continues from.
[ADR 0013](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md) is the decision that fixes this, and it fixes
the wording a caller is given with it — queued, never sent.

**The message is composed before any submission server has spoken**, because the record has to exist before a connection
is worth opening. `MailDeliveryCapabilities.BeforeAnyServerHasSpoken` is what the composition is held to in that gap,
and each of its three values is the answer that stays correct whatever the server turns out to say:

| Fact | Value | Why that one |
| --- | --- | --- |
| Maximum message bytes | None declared | The deployment's own bound is applied at composition regardless, and the server's is checked against the stored length before the message is offered, so assuming a number here would refuse a message a server would have taken without protecting one it would not. |
| Eight-bit content accepted | No | Content encoded to seven bits is accepted by a server that would also have taken it unencoded, at a fraction more size. Composing eight-bit and meeting a server that takes none produces a message that can never be delivered. |
| Internationalized addresses accepted | No | This one is a refusal rather than a cost. An address outside ASCII is refused while the caller is still there to be told, instead of being queued to fail against the server hours later with nobody to answer for it. |

**The grant is asked for twice on the way in, and neither ask is the duplicate.** The use case asks first, so a caller
without `mailfathom.mail.send` spends nothing and learns nothing about who the contact book holds; the outbox asks
again with no boundary in the picture, which is the authority and is what an entrypoint added later meets whatever it
did first.

**What a refusal costs is nothing.** A message this deployment will not compose is refused before the outbox is
reached, so no record, no stored MIME, and no signal to a delivery pass is left behind, and the caller is told which
field, bound, or count decided it and never what was in it. The codes are the MCP boundary's rather than this
category's, because that is where they surface;
[MCP tools](mcp-tools.md#error-reporting) holds the table.

`AuthoredResponseSubmission` is the same use case for the answer, and it is a sibling rather than a mode: what it takes
is the identity of the email being answered, which act it is, what the author wrote, and whoever the author names
themselves, and it composes the authoring above with the same composer, the same capabilities, and the same outbox. The
account is not among its arguments and never becomes one — the answered email decides it, which is the whole reason a
reply lands on the mailbox the correspondent has heard from — and neither is anything the authoring derives. `reply_to_email`
and `forward_email` are what call it, and it is the whole of what those tools do.

**A refusal about the answered email is one answer rather than four.** The authoring below distinguishes an email nothing
is held under from one whose content cannot be read, because a damaged copy is worth a repair request and a missing row
is not, and that distinction stays where it is useful. What crosses to a caller is `53005` for both, with the same
sentence, joining the withheld folder and the unserved account the authoring had already collapsed into the first — so
none of the four situations can be told from another by asking. The repair request is still recorded on the way out.

## Holding a send until the time it names

A submission may carry a **due time**: the instant the message is to leave, together with the zone whoever named it was
thinking in. The record is written down at once, exactly as any other send is, and the one thing that changes is when it
may be claimed — the instant a delivery pass compares against is the due time rather than the moment it was recorded.
Nothing else about the send differs, so a message held until Monday rides the same lease, the same retry policy, the
same capacity bounds, and the same restart behaviour as a message sent now.

**The zone is kept beside the instant rather than folded into it.** A person names a time in a place: nine in the
morning in Warsaw is nine in the morning on both sides of a daylight-saving transition, and the two are different
instants. The wall-clock time is resolved once, where it is named, so a zone whose rules change afterwards cannot move a
decision that was already taken — and the two cases a clock creates are answered rather than left to whatever
arithmetic happens next. A time the clock springs over does not occur at all and is taken at the instant the gap ends,
so the occasion happens rather than being lost; a time the clock passes through twice is taken at the earlier of the two
readings, so it happens once. `JobRecurrence` answers both the same way and through the same code, because a schedule
and a held message meet the same two questions.

**Noticing that the moment has arrived is a job on the durable queue.** No timer, no scheduler, and no queue of this
feature's own: the record is already unclaimable until its instant, so what was missing was something to say the instant
had come, and that is what the queue already does for every other kind of deferred work. The outbox enqueues one
`dispatch-held-send` job per record, made available at the due time and keyed by the record, so the same send has one
such job however many identical requests reach the outbox. Running it twice is running it once — it announces an
account, a pass reads the outbox rather than the job, and an account already waiting is not queued twice. A send
withdrawn or already settled during the hold announces nothing.

**A queue that is full costs the send its punctuality rather than the send itself.** The record is durable and its
instant is written on it, so the account's next delivery pass claims it whether or not any job ran. That is the same
thing a refused signal costs an ordinary send, and it is why neither refusal is raised to whoever asked for the message.

**A due time that has already passed is refused where the author is still there to be told.** The two readings of a past
time are opposite — somebody who meant tomorrow and typed yesterday's date wants to fix it, and somebody who meant now
would not have named a time — so a system that guessed would sometimes send a message its author was still writing. The
refusal states the rule and never repeats the instant, the address, or the subject.

**A moment that came and went while nothing was running is the case with no obviously right answer**, and the deployment
decides it. `MailDelivery:AllowedSendLateness` is how much later than its due time a message may still be delivered as
written; the default is a working day. Up to that, the pass sends it. Past it, the send is refused with `28016`, stands
in the outbox where an operator can see it, and counts under the delivery outcome `missed_due_time` — neither outcome is
silent, and nothing decides on the owner's behalf that a message which missed its moment should still go out. The bound
applies to a message written for a named time and to nothing else: a send that named none is never late, however long a
retry or an unreachable provider has held it.

The decision is taken in the delivery pass rather than in the dispatch job, because the pass is what holds the lease the
decision has to be recorded under — and because a record reaches the pass by other routes than that job, including the
account's own synchronization run and a restart that found the outbox unclaimed.

**A held message is withdrawable for the whole of the hold**, because the hold is the `Recorded` stage and that is
exactly the stage [a withdrawal](#what-an-operator-sees-while-mail-is-leaving) applies at. `MailOutbox.CancelAsync` is the same withdrawal
asked for by whoever wrote the message rather than by an operator: it asks for the grant that lets a caller send —
stopping somebody's message is a decision about their correspondents exactly as sending one is — and then reaches the
one conditional statement that writes the transition, so neither caller can withdraw a message the other could not, and
a record an attempt holds a live lease on is refused to both. During the hold the message is also visible as a copy in
[the mapped outbox folder](#the-copy-in-the-accounts-own-folders), and deleting that copy in a mail client cancels
nothing — the record is the truth about what will be sent.

Today the due time is a property of the application-level submission rather than an argument on an MCP tool:
[`send_email`](mcp-tools.md#send_email) names no time, so every message a tool call submits is due at once.

## A message the owner asked to be sent again

A **recurring send** is one message written once and sent again on every occasion a schedule names.
`RecurringMailSubmission` is the use case, and it is the authored submission's counterpart stopping one step earlier:
nothing is queued, because nothing is due. What it leaves is a declaration and the draft its occasions are composed
from, written in one transaction for the reason the record and its message are — a declaration whose draft was never
stored describes occasions that can produce no message.

**The repetition is written in the same syntax a scheduled rule declares one in** — `Every hh:mm:ss`, or
`Daily at HH:mm` with an optional IANA zone, as [mail rules](mail-rules.md) states it. Reusing it is deliberate: a
second syntax for the same idea would be a second set of rules about daylight saving, about how short an interval may
be, and about what an operator has to learn. It is parsed before anything is read or composed, so a repetition nobody
can resolve is refused while the author is present, naming what was wrong with it in the syntax's own words, and nothing
durable ever states an occasion nothing can resolve.

**Each occasion produces an ordinary send.** The dispatch is the same one recurring rules ride: the declarations are
read as schedules beside them, the worker that already polls the job queue decides which occasions have passed, and the
occasion enqueues a `send-recurring-occurrence` job. The handler composes that occasion's message from the stored draft,
stamps it with an identity and a date of its own, and writes it into the outbox as a send held until the occasion
itself — which is what leaves the lateness bound holding for a repetition exactly as it holds for a message somebody
scheduled by hand. The message is re-stamped rather than reused because a repeated `Message-ID` would thread a year of
Mondays as one message in every recipient's client.

**Which occasion a dispatch is running for is read from the schedule rather than carried in the job.** The most recent
occasion at or before now is the one composed, so a run that started late produces the message that was due rather than
one for a moment that has not come — and two runs reaching the same occasion compose the same idempotency identity, so
the outbox answers the second with the record the first one wrote.

**Only one occurrence is in flight at a time, and that is enforced rather than assumed.** The message the last occasion
produced is asked about first, and while it is still queued or transmitting this occasion is answered instead of
started: a weekly message whose provider has been unreachable all week must not put a second week's copy behind the
first, and a message whose outcome nobody knows must not be followed by another until somebody has looked at it. The
declaration still advances past the occasion it passed over, so the next one is not offered it again.

**Stopping a declaration and withdrawing a message are two acts.** `RecurringMailSubmission.CancelAsync` stops every
occasion still to come and touches no message: an occurrence already written down goes out as it was going to, because
it is a message the owner asked for at a moment that has already come, and stopping that one is asked for against its
own record. A stopped declaration is read by nothing that dispatches, from the moment it is stopped, and keeps its row —
what it last did and when it was stopped are the account of a mailbox that used to send something every week, and
deleting the row would make that indistinguishable from a repetition nobody ever declared.

**A stored schedule that no longer parses is left out rather than raised over.** The syntax was read where the
declaration was made, so such a row is a damaged payload rather than a decision to take at dispatch time, and one of
them must not stop every other repetition the deployment holds. `RecurringSendBounds.MaximumActiveDeclarations` bounds
how many declarations dispatch at all, at 500, which is a ceiling rather than a page: a deployment approaching it has
something wrong with it rather than a bound to raise.

The declaration is derived personal data of the same kind an outgoing record is, and inherits the same retention,
deletion, and export obligations —
[the stored email schema](../architecture/stored-email-schema.md#what-an-owner-asked-to-be-sent-again) holds the
columns and says why it says more about a relationship than a single send does.

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

One send is confined the same way inside a pass. An attempt records its own answer, so what is left to fail is the
recording — a store that went away while an outcome was being committed, which the recovery write then meets as well —
and such a send is reported as *not recorded* and the pass goes on to the one behind it. Raising instead would hold
every send left in that batch until its lease expired, over a failure that says nothing about any of them; the record
stands where the failed write left it, and its lease is what makes it claimable again.

**A pass claims before it attempts.** One statement takes a batch of the account's due sends, oldest first, and stamps
each with an owner, an expiry, and the attempt it is about to be given — so no instant exists in which a send is chosen
but unheld, and two passes over one account take disjoint sets rather than queueing behind each other. Every write about
that send afterwards carries the lease it was claimed under and is refused if the row no longer names that owner, which
is what stops an attempt whose lease ran out from recording an outcome over the attempt that has since taken the
message. The attempt itself is bounded below the lease, so that case stays rare rather than routine, and startup
refuses a configuration stating otherwise. That budget opens before the first thing the attempt waits on rather than
before the first thing it sends, so a content read that never answers is cancelled with everything else instead of
holding the whole batch behind it.
[The claim a delivery attempt holds](../architecture/stored-email-schema.md#the-claim-a-delivery-attempt-holds) has the
predicate and the columns.

One claim stamps a whole batch with one expiry while the sends under it are attempted one at a time, so a send far
enough down a slow batch is reached after its own lease has already run out. It is reported as such and nothing is
offered for it: every write it would make is refused anyway — which is what keeps a reclaimed send from being
transmitted twice — so asking first buys the connection, the submission, and the wait that would otherwise be spent on
a record this attempt no longer holds.

## Reading a send back, and withdrawing one that is still waiting

A caller is answered with an identifier and `queued`, which is honest and is only half a contract: a caller holding that
identifier with no way to learn what became of the message does the one thing that cannot be taken back and sends again.
`OutgoingMailReader` is the other half. It reads the durable record — the state, the attempts counted against it, what a
server has said about each recipient, and the code the last failed attempt ended on — and it reaches no submission
server to do it, so the answer is as fresh as the last delivery pass rather than a live check.

`OutgoingMailCancellation` is the one point at which sending is reversible at all. The window is between the record
being written and the first byte of the body going out — ordinarily seconds, longer where an operator configured a hold
— and past it the message is somebody else's, so the call says so rather than reporting a withdrawal it did not perform.

**Withdrawing never races a delivery pass.** The decision and the write are one statement, conditioned on exactly what
the claim above is conditioned on: the record still at *recorded*, and no unexpired lease on it. A send an attempt is
holding is therefore left alone instead of being cancelled out from under a session that may be part-way through an
envelope, and no state exists in which a message was transmitted and the record says it was withdrawn. The stage read
before the write is advisory — a pass may take the record in between — so what happened is the statement's own answer,
read back from the record afterwards. An expired lease counts as free, by the same predicate that lets a pass reclaim
such a row.

It is the statement `mfctl outbox cancel` writes, reached under a second authorization rather than written a second
time. What differs between the two is who may ask and about which records — an operator names any send this deployment
holds, and a caller here reaches only what it queued — and that difference is decided before the statement rather than
inside it. Two accounts of one invariant would drift, and whichever drifted would be found as a message somebody was
told had been withdrawn.

**Withdrawing twice is one withdrawal.** A record already at *cancelled* is answered with itself and nothing is written
a second time, which is what makes the tool over it idempotent rather than idempotent as long as nobody repeats a call.
A withdrawn record is terminal: nothing reschedules it, and queueing the message again is a fresh send with a fresh
idempotency key.

**What either use case may reach is what the caller queued.** A send carries the principal it was queued under —
stamped by the outbox from the admitted identity when the record is written, never stated by a caller — and a record
whose principal is not this caller's answers exactly as a record that does not exist. Two consequences follow. A caller
cannot learn from an identifier alone that this mailbox sent something, which is the enumeration this surface refuses to
offer, reached one guess at a time instead of through a listing. And a send a **rule** queued is reachable by no caller
at all: the origin the record already carries is checked beside the principal, so a send this deployment made for itself
stays out of every caller's reach whatever a credential happens to be named. What reads those is
[the operator's own view](#what-an-operator-sees-while-mail-is-leaving), on the administrative surface rather than
this one.

The stamp is a fixed-width digest of the admitted identity rather than the identity itself, so what is at rest is enough
to compare a later caller against and is not a second copy of whatever a token asserted. A record written before the
column existed carries none and therefore matches nobody, which is the safe direction: it reads as not found rather than
as everybody's. [The outgoing messages waiting to be sent](../architecture/stored-email-schema.md#the-outgoing-messages-waiting-to-be-sent)
holds the column.

[`get_outgoing_email` and `cancel_outgoing_email`](mcp-tools.md#get_outgoing_email) are the tools over these two use
cases, and both take the sending grant rather than the reading one — what a caller may read back and stop is exactly
what it was allowed to start.

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
*stuck*, whichever pass settled it logs it at error level — the signalled worker and the account's own run alike — and
the delivery counter measures it under `outcome_unknown`. It moves only
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

## The copy in the account's own folders

SMTP files nothing. A message a submission server accepted leaves no trace in the mailbox it was sent from, so a
deployment that only submits sends mail its owner's mail client shows they never sent. **Filing** is the answer: one
mechanism appends a copy of an outgoing message to the folder playing the role the message's own state calls for, and
takes it back out when that state changes.

A message's own state is what decides both halves of that, and an outgoing message has two states worth a copy:

| The message is | The copy goes to the folder playing | And carries |
| --- | --- | --- |
| waiting for an instant still ahead | the **outbox** role | `\Draft`, and is withdrawn when it leaves |
| accepted by a submission server | the **sent** role | `\Seen` |

The flags travel with the role rather than being chosen by a caller, so a draft cannot be filed as read and a sent copy
cannot arrive as unread mail somebody has to open. The copy's internal date is the instant the injected clock reports,
which is the same clock everything else in this system is stamped from.

**The outgoing record stays the truth about what will be sent.** A copy in a folder is a view of it: deleting the copy
in a mail client cancels nothing, cancelling is [a command of its own](#holding-a-send-until-the-time-it-names),
and nothing reads a folder to decide what to send. That is what makes the copy safe to write and safe to leave out — a
deployment that appends nothing loses visibility and loses no mail.

**No server has an outbox, so nothing looks for one.** RFC 6154 defines the special-use attributes a server may
advertise and there is no `\Outbox` among them, because the outbox a mail client shows is that client's own local queue
of what it has not managed to send. MailFathom's outbox is the durable record above. So the outbox role is MailFathom's
own: it is never read off a server, a provider folder merely *named* like one plays no role — nothing here reads a
folder's name — and a folder plays it only where an operator mapped a path to it by hand. A mapping naming the role
with no path is refused at startup, naming the alias and the key it wants, because discovery has nothing to look for.
Nothing is mirrored until somebody writes that mapping.

**The sent copy is appended after a delivery the server acknowledged, and only then.** Whether it is appended at all is
`Delivery:FileSentCopy`, a per-account setting that defaults to on and is configured rather than detected: a provider
that files the copy itself does so asynchronously, so looking in the folder immediately after a delivery cannot tell
*will appear shortly* from *will never appear*. Turn it off for an account whose provider files the copy, and leave it
alone otherwise — a duplicate an owner deletes beats a record of what they sent that never existed.

**The bytes are the ones the recipients received.** The append reuses the stored MIME rather than recomposing it, for
the reason a retry does: a recomposed message carries a different `Message-ID` and threads as a second message in every
client, including the owner's own.

**A copy is appended once, and an append whose answer never came back is never repeated.** The filing is written down
before the command goes out, so a process that died in between leaves a row saying the copy may be there — and nothing
appends again on the strength of it. That is deliberate and it is the one outcome here left unsettled: an `APPEND`
issued twice is a second message in somebody's folder rather than a repeat of the first, and nothing the folder shows
afterwards tells the two apart. A failure *before* the command reached the server is ordinary and may be attempted
again.

**The copy comes back through synchronization and is recognized rather than guessed at.** Where the server advertises
RFC 4315 `UIDPLUS`, its `APPENDUID` response names the occurrence exactly and that is the join; where it does not, the
`Message-ID` this system minted, read back off the appended bytes, is what recognizes it. Either way the stored message
is marked as this deployment's own, which is what keeps [a rule](mail-rules.md#when-rules-run) conditioned on arriving
mail from firing on what the owner just sent, and what keeps
[spam classification](spam-classification.md#mail-is-classified-as-it-arrives) from scoring a message this system
composed.

**Nothing else about the copy is treated differently, and that is deliberate.** It is stored, extracted, searchable,
cut into passages, and embedded exactly as any other message in that folder — a question asked of the mailbox is
answered from the mail the owner sent as well as the mail they received. Contacts are collected from it too, because
which header a message contributes is decided by the role of its folder: a copy in the sent folder contributes the
people the owner wrote to, which is the same answer the copy would get had the provider filed it. What the join changes
is the two things that would otherwise be this system reacting to its own act.

**Filing is never part of delivering.** A send is delivered or it is not, and where its copies are is a second account
of the same message. An append that failed after a successful delivery leaves the record saying delivered and not
filed, with the reason beside it, and the delivery is never attempted again because of it. Nothing retries the append on
its own either: a settled send is claimed by nothing, so what an operator gets is the reason on the record, a log line
naming the account and the folder alias, and a measurement under the outcome that failed. A
destination that does not exist is the same answer a mapped folder the server does not advertise gives — the mapping is
what changes it, including
[creating the folder](imap-synchronization.md#a-folder-the-mapping-asked-for-is-created) where the mapping asked for
that — and the message is untouched either way.

The withdrawal of an outbox mirror reaches that copy and nothing else: `\Deleted` followed by `UID EXPUNGE` against the
UID the append reported. A server without `UIDPLUS` leaves the copy standing rather than expunging the folder, and so
does a copy the server never named — one copy of the owner's own message in a folder they mapped, deletable with the
gesture they would have used anyway. A mirror whose append the server never answered is left alone entirely: nobody
knows whether that copy reached the folder, so the record goes on reporting the outcome as unknown rather than claiming
the copy was taken back out. A sent copy is withdrawn by nothing: it is what the owner keeps.

[ADR 0007](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
is where appending became something MailFathom may do at all, and holds the authorization review that admitted it.

## A message that is written and not sent

A **draft** is a message this deployment holds and will offer to nobody until somebody asks it to. It is written down
the same way a send is — an authored message composed into MIME, or a reply or forward authored from mail this
deployment already holds — and then it stops there: no submission endpoint is opened, no recipient is required, and
nothing claims it. A message addressed to nobody at all is an ordinary draft rather than a refused one, which is what
saving something half-written means. It is not what [a repeated send](#a-message-the-owner-asked-to-be-sent-again)
stores under the same word: that draft is a template an occasion is composed from and reaches no folder, while this one
is a message somebody is writing and is the message the owner's own client shows them.

**The record and the message cross one transaction, and the mailbox follows.** A draft whose message was never stored
describes a version nothing can append or send, and a message stored under no draft is bytes nothing will ever read, so
both are committed together and the drafts folder is brought into step afterwards. A crash in between leaves a draft
the next pass finishes; the other order would leave a message in somebody's drafts folder that nothing here can name.

**The copy goes through the same filing mechanism a sent copy does.** The folder is the one playing the **drafts** role
and is found by that role rather than by name, the copy carries `\Draft`, its internal date is the injected clock's
instant, and the append and the withdrawal are the two operations the write session opens for exactly this. What is not
shared is the durable account of it: a filed copy is written once and kept, while a draft's copy is written, replaced,
and eventually taken back out, so the copies hang off the draft with one row per revision. An account that maps no
folder to that role holds its drafts and puts none of them in front of the owner — the destination is reported as
unavailable, the draft is untouched, and mapping the role is the whole of what changes it.

**Editing replaces the copy: append the new version, then remove the one it replaced, in that order.** IMAP has no
command that changes a stored message, and the order is the safety rather than a preference. Removing first and then
failing to append leaves the owner with no draft at all — the version they were working on, gone — while appending
first and then failing to remove leaves them with two, which is untidy and loses nothing. **The revision is durable
before either command goes out**, so a process that dies between them is recognized for what it is and the pass that
follows finishes the pair. That is why an owner who edits a draft is looking at one draft rather than at two.

**The only occurrence anything here ever removes is one an append of its own reported.** There is no path from a
supplied UID, a folder search, or a message identity to a removal, so a draft the owner wrote in their own mail client
is unreachable by construction rather than spared by a check. Where the tracked copy stops being provably this
deployment's — the role now resolves to another folder, the folder was recreated since the append, the server named no
placement, an append was never answered — the message is left exactly where it is and the divergence is written onto
the draft, which is what an operator reads instead of a message that quietly went missing.

**Giving a draft up removes what this system put there and nothing else.** The record is marked before anything is
issued and removed once the copies are settled, so a process that dies in between leaves a draft the pass finishes. A
copy that could not be reached does not make the draft undeletable: it is marked as one nothing will touch again, the
reason is recorded, and the owner is left with one message in a folder they can delete with the gesture they would have
used anyway. **A draft that has been promoted is not given up this way**: its message is a queued send that giving the
draft up would leave untouched, so the answer is `53008` `MailDraftNotFound` — the same one revising it gives — and
what stops the message is cancelling the send rather than deleting the draft it came from.

**Promoting a draft produces an ordinary outgoing record carrying the same MIME.** The bytes are the ones that were
stored rather than a recomposition, for the reason a retry reuses them: a rebuilt message threads as a second message
in every client. From that point the send is an ordinary send — claimed, transmitted, and settled exactly as
[any other](#how-a-written-down-send-reaches-a-server) — and **everything this deployment refuses a send for is asked
at the promotion rather than at the writing**: whether sending is on for the account, whether every recipient is
somebody this deployment may write to, the ceilings, and the size bound. A draft written a month before an operator
tightened one of those is refused by the tightened one. A draft addressed to nobody is refused here too, with
`53010` `MailDraftNotAddressed`, which is the one refusal that is about the draft rather than about the deployment. A
draft this deployment does not hold — never written, given up without ever having been sent, or another account's —
answers `53008` `MailDraftNotFound` whichever of the three it is, so nothing learns which drafts exist by asking about
them.

**Promoting one draft sends one message, however many callers ask.** A draft that already names its record answers with
that record, which is what a caller whose first answer never reached it is told — including while the draft is being
given up, because that is what a delivered send does to the draft it came from and the copy leaves the folder over a
round trip after the mark is written. Two callers arriving together are the
case that read cannot settle — neither of them can see a write that has not happened yet — so the request's identity is
the draft rather than a key whoever asked supplies: their two asks compose one identity, and the outbox answers the
second with the record the first opened. It is the same mechanism [an occasion of a repeated
send](#a-message-the-owner-asked-to-be-sent-again) is keyed by, and it is here for the same reason: a message put in
somebody's mailbox twice is the one duplication nothing downstream can withdraw.

**A promotion that fails leaves the draft exactly as it was.** Nothing about the draft is written until the outgoing
record exists, so a refusal is a message the owner still has. And the draft is given up on **delivery** rather than on
promotion: a send that is refused, deferred, or left with an unknown outcome leaves the draft standing, so an owner
whose message did not leave still has what they wrote. Once the server has accepted it, the draft's copy is taken out
of the drafts folder in the same pass that files the sent copy — which is what leaves the message in one place rather
than in two.

**What is outstanding is finished by the pass that delivers.** Saving, revising, and giving up each act on the mailbox
where they are asked for, so what is left for a pass is the half nobody is standing there for: a process that stopped
between the two commands of a replacement, a mail server that was briefly unreachable, and a promoted draft whose
message has since been delivered. The pass reads the record rather than the folder, and an account whose drafts are all
settled costs one bounded query and reaches no mail server at all. It runs **before** the submission endpoint is asked
for, because a draft is written over IMAP and owes nothing to SMTP: an account that reads mail without configuring a
place to send from keeps drafts like any other, and this sweep is the only thing that ever brings its drafts folder
back into step.

**A draft is derived personal data.** It is a message addressed to people, and one drafted as an answer is composed in
part from mail this deployment holds, so it carries the classification of what it came from and is reached by the same
retention and erasure. [Stored email schema § The drafts nothing will
send](../architecture/stored-email-schema.md#the-drafts-nothing-will-send) holds the four tables and the cascades that
make that structural.

**[The MCP surface](mcp-tools.md#the-drafting-surface) reaches all of this through four tools**, and the split between
them is the point rather than the arithmetic. `save_draft`, `update_draft`, and `delete_draft` write a draft, replace
it, and give it up under `mailfathom.mail.drafts.write`; `send_draft` promotes one under `mailfathom.mail.send`, which
is the grant every act that causes mail to leave is admitted by. So a deployment can hand an agent the whole of the
writing above and none of the promotion, which is what makes a draft the arrangement to reach for where a person
belongs between an agent and a recipient.

## What an operator sees while mail is leaving

Each attempt opens the `submit_outgoing_email` span over the exchange with the server, its duration is recorded, and
its outcome counts under `mailfathom.mail.delivery.attempts` by account. `outcome_unknown` is the value worth alerting
on at any rate above zero, because each measurement is a message nothing will attempt again until a person decides.
Filing is counted beside it, under `mailfathom.mail.filing.attempts` by account, place, and outcome, and each append
opens a span of its own in the mailbox-mutation record. Keeping the drafts folder in step is counted separately again,
under `mailfathom.mail.draft.attempts` by account and outcome, because a draft was offered to no server and summing it
with the deliveries would report an outbox busier than the mail actually leaving it; `diverged` is the value that names
a decision for a person rather than a failure to retry.
The span carries the outbox record it is submitting, which is what joins a slow or failed submission to the row an
operator then reads. `mailfathom.mail.outbox.depth` reports how much stands at each stage a send can still move from,
as the delivery pass last measured it, and `mailfathom.mail.delivery.retries` counts the attempts that were not a
message's first.
[Telemetry § What delivering the outbox emits](../operations/telemetry.md#what-delivering-the-outbox-emits) holds the
instruments, the tags, and what none of them carries. A failure names the account alias and the folder alias and
nothing else: no subject, no address, and no line of a message.

What a dashboard cannot answer is *which* message, and that is what the outbox commands are for.
`mfctl outbox status` counts the stages, `mfctl outbox list` names the sends without naming anybody they are addressed
to, and `mfctl outbox show` answers about one of them with its recipients and what each of their servers said. The two
decisions are the only points at which this path is steered by hand: `mfctl outbox cancel` withdraws a send that has
not begun transmitting, and `mfctl outbox requeue` offers one again — one named message at a time, never a selection,
and never a permanently refused message without the refusal being restated. None of the five is reachable from the MCP
surface: they are administrative, they are bounded by the administrative credential's `mailfathom.admin.read`,
`mailfathom.admin.audit.read` — which is what `outbox show` needs, because it is the one of the five that names people
— and `mailfathom.admin.operate` grants, and putting a message back on its way to somebody's mailbox is not a decision
to leave to a model. [The administrative endpoint § Reading what is in the outbox, and deciding about one
message](../operations/admin-endpoint.md#reading-what-is-in-the-outbox-and-deciding-about-one-message) holds the
routes, the outcomes, and the stage table.

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

Resolving a recipient named as a person is settled in the unit suite too, against an in-memory book rather than a
substitute, because every claim is about what a lookup of the book can answer: a name one person carries whatever casing
it was written in, a name several carry refusing and naming how many, an identity and a name nobody answers to producing
one refusal, an act naming another of the contact's addresses and an act naming one they do not hold, an address the
author wrote reaching the composition unparsed and naming no contact, and one recipient nobody can be found for refusing
the whole message. That a resolved address is then an ordinary address is proven where it would stop being one — in the
composition, where a contact-resolved address refused for internationalization is refused exactly as a written-down one
is, and where a contact and a typed address naming one mailbox become one offer. That the record keeps the contact beside
the address is the integration suite's, since only a real column can round-trip it.

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

Filing is split the same way again. The unit suite carries what a scripted session settles: an account that files no
sent copy appending nothing at all, a delivered message appended from the stored bytes with `\Seen` at the clock's
instant, a mirrored message appended with `\Draft` and never with `\Seen`, an append the server never answered not
being appended a second time however often the pass runs, and a failed append after a successful delivery leaving the
record at `Sent` with a filing failure beside it and its attempt count unmoved. The adapter's own half is asserted as
the commands it issues — the flags, the internal date, the `APPENDUID` read back, and a withdrawal that names one UID
and never issues a bare `EXPUNGE` — including the two refusals, a folder recreated since the append and a server
without `UIDPLUS`. The join is proven in both directions a server can answer, and with the control the absence rule
needs: the same run over a message no filing accounts for stores ordinary arriving mail.

The integration suite runs the whole loop once against the orchestrated server: a delivered message is appended to the
folder mapped as this account's sent folder, exactly one copy of it is there and it is read, asking for the settlement
again reports it as already filed rather than appending a second, an ordinary synchronization then joins the copy to
the send it came from — and the queue a rule pass reads holds a message appended beside it and not the copy.

Drafts are split the same way once more. The unit suite carries what a scripted session settles, and each of the four
is a state a real server cannot be asked to produce on demand: a process resumed between the append and the removal of
a replacement, which withdraws the copy that was replaced and only that one; a tracked copy in a folder the drafts role
no longer names, which is left standing with the divergence written onto the draft; giving up a draft this system never
wrote, which is refused before any folder is opened because nothing holds it under an identifier the call accepts; and
a promotion, including the bound and the recipient policy asked at the promotion rather than at the writing, the second
ask answering with the record the first produced, two callers who both found the draft unpromoted queueing one message
between them, and a delivery that failed leaving the draft where it was. The pass that delivers is covered where it is
assembled: that it settles an outstanding draft before it claims anything, and that a delivered send takes the draft it
came from out of the drafts folder.

The integration suite runs the whole loop once against the orchestrated server, with the owner's own draft appended
beside MailFathom's as the control: a written draft reaches the folder under the UID the record names, an edit leaves
exactly one of this deployment's drafts there, a promotion delivers the message and leaves none — the copy taken out of
the drafts folder in the same pass that files the sent one — and the draft appended by hand is still there under the
UID it arrived with, throughout.

The outgoing record is split the same way. What the shape of the state guarantees — which stages are undecidable, which
recipients a later attempt still owes, and which terminal stage may follow which — is a unit test over the domain
record. What only PostgreSQL can settle is in the integration suite: the index refusing an insert two transactions each
reached without seeing the other, the second enqueue leaving the first message in place, a record left mid-transmission
being found and read as such by a later scope, a claim holding a record against the next claim, and the cascade erasing
the message and the recipients with the record.

The grant the outbox asks for is split the same way again. Which principal each origin admits, and that a refusal
signals no delivery pass, are unit tests over the outbox; that the principal a scope reports actually reaches it through
the composed graph is the integration suite's, where a scope reporting no caller — every worker in the process — is
refused a command and leaves the account's outbox as it found it.

Delivery is where the two halves meet, so it is proven from both ends.

The unit suite carries the cases a real server cannot be asked for. A crash at each stage of an attempt, over the
rewind and the refusal to rewind on either side of the first acceptance; a lease reassigned while an attempt was
transmitting, which then writes nothing at all; a partial acceptance and the retry that offers only the addresses still
outstanding; a permanent per-recipient refusal that still delivers the message; every recipient refused; the attempt
bound being spent; host shutdown before and after an acceptance; a store that will not take one send's outcome, which
ends that send and leaves the one behind it in the batch still delivered; and each of the settings validations,
including the attempt timeout that reaches its lease. Beside them the loop's own claims — that a signal is what wakes it, that a full
batch asks for its account again, and that neither a failed pass nor an account with nowhere to submit stops the account
behind it — and the signal's own: that one account signalled a hundred times is one pass, that a full queue refuses and
says so, and that a queue refilled as fast as it drains still ends when the host stops.

The transmission itself is a unit test against a scripted submission client, because every reply class can be stated
there and none can be provoked from a real server on demand: a message accepted, a message refused permanently and
temporarily, no address accepted, a server that stops answering mid-transmission with the addresses it had already
accepted kept, and the bytes a transmission offers — blind recipients absent from the transmitted headers, and the line
endings a submission requires. The three hooks the submission client overrides are exercised on the real client rather
than through the scripted one, because they are what keeps a refused address from stopping the addresses beside it and a
substitute would prove the substitute instead.

The claim is asserted as text, without a database. It is one statement and each clause in it fails silently if it is
lost, so the stage filter, the locking clause, the bound, the two predicates that make a send due, and the stamp that
counts the attempt are each read off what is composed. So is the shape of the whole: the column names are part of the
text and only the values are parameters, which is the difference between a statement PostgreSQL runs and one that
reaches it asking for a column named after a parameter marker.

The integration suite settles what only a real server and a real database can. A queued send is delivered, recorded as
sent, and then found in the mailbox it was addressed to — read back over a connection nothing under test owns, which is
what makes the arrival an observation rather than the outbox agreeing with itself. The same authored send queued twice
produces one message. A send left mid-transmission is marked and transmitted no further, and the mailbox holds no copy
of it. What the orchestrated server cannot settle is a refusal: GreenMail accepts every recipient it is offered and
creates the mailbox behind it, so both refusal shapes stay in the unit suite — the same division that already puts the
size and eight-bit capabilities there.
