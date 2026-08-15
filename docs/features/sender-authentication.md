# Sender authentication

<!-- describes: src/Domain/Emails/Authentication/**, src/Application/Mail/ITrustedAuthenticationAuthorityReader.cs, src/Infrastructure/Mail/Mime/AuthenticationResultsHeaderReader.cs, src/Infrastructure/Mail/Mime/MimeKitEmailMimeReader.cs -->

Everything about deciding whether a message is from who it says rests on one question: which domain actually sent it?
The obvious answer is wrong. `From` is a header the sender writes, it is what a mail client displays, and it is what
anybody impersonating somebody else controls completely — so a check derived from it would be accurate about honest
mail and silent about the mail it exists for.

The answer that means something comes from the receiving mail server, because it is the only party in the chain that
observed the connection. It saw the envelope sender and could check SPF against the connecting address, and it could
verify a DKIM signature against a key the signing domain publishes. RFC 8601's `Authentication-Results` header is where
it writes both down. MailFathom reads that header back and records what it said; it verifies nothing itself.

This page describes what is recorded and how the header is chosen. Deciding whether an authenticated domain counts as
*trusted* is a separate step and is not implemented yet.

## The header is only believed from one server

`Authentication-Results` is an ordinary header, so anything upstream of the receiving server can write one — and a
message arriving with a fabricated header claiming that everything passed is exactly what an attacker sends. RFC 8601
anticipates that: every server that produces the header stamps its own identifier into it, and a consumer reads only
the headers bearing the identifier it trusts.

So an account states that identifier, as `TrustedAuthenticationServiceIdentifier` in its block of `MailSynchronization`,
and the reading follows two rules and no others:

- **Only headers carrying that identifier are read.** Every other `Authentication-Results` header is ignored whatever it
  says, including one that claims a passing result for the same domain.
- **Of those, the topmost is taken.** A receiving server adds its own header above whatever it found and may leave a
  forged one below, so the first is the one it wrote.

The identifier is compared without regard to case, since RFC 8601 writes it as a domain-shaped token and a server may
change its casing between messages. Which identifier is right is a property of who receives that account's mail rather
than of MailFathom, so there is nothing to default it to; [the configuration
reference](../operations/configuration-reference.md) states where the setting lives. What to write in it is read off
the mail the account already holds: open a message that arrived recently, read its topmost `Authentication-Results`
header, and take the token before the first semicolon, which is the identifier that server stamps on everything it
delivers to this mailbox.

**An account naming no identifier believes no header at all**, and every message it holds carries the not-established
verdict below. That is an ordinary state rather than a misconfiguration: it is also what a deployment whose provider
publishes no results sees on every message. A value that is *present* and unusable — blank, longer than a domain name,
or carrying whitespace — fails startup instead, because the two are indistinguishable afterwards.

The `ARC-Authentication-Results` header of RFC 8617 is deliberately not read here. It preserves an upstream hop's
findings across forwarding, which is a claim a relay signed rather than something this mailbox's own server observed.
[Spam classification](spam-classification.md) reads the ARC chain separately, for a purpose that weighs claims instead
of trusting one.

## What the verdict records

One verdict per message, derived from the stored raw MIME by the same extraction that reads the subject, the
participants, and the body text — so it costs no extra parse, no IMAP round trip, and cannot reach the remote `\Seen`
flag. It is stored on the message; [the stored email schema](../architecture/stored-email-schema.md#the-sender-authentication-verdict)
holds the columns.

| What it holds | Why |
| --- | --- |
| The outcome — not established, failed, or authenticated | Not established is a verdict of its own: a check that was never made is a different fact from one that did not hold |
| The domain that authenticated, and which method reached it | The identity is the point; the method says how much it is worth |
| The DKIM signing domain and the SPF envelope domain, separately | The two disagreeing is itself a fact about the message |
| The DMARC result the server reported | It is where an authenticated domain meets the displayed one, under the sender's own published policy |
| Whether the authenticated domain is the `From` domain | A message authenticated as one domain while claiming another is visible as exactly that |

**DKIM is the authoritative identity wherever both checks produced one.** It is cryptographic — a key the signing
domain publishes signed these bytes — while SPF says only that a particular address was permitted to connect on behalf
of the envelope sender, which a forwarding hop legitimately breaks and a shared relay legitimately satisfies for
everybody using it. Both are kept, so a reader that cares about the difference still has it.

**`From` is never the source of the verdict.** It is recorded beside it, and the alignment value is the comparison of
the two. That comparison is exact: `mail.example.test` and `example.test` are two names, and treating them as one here
would assert an alignment the receiving server never claimed. Where a sender's published policy does permit the relaxed
form, the server's own DMARC result says so and is recorded separately.

### Not established is an answer

A message carries the not-established verdict wherever nothing trusted could be read:

- the account names no trusted server;
- no header carries that server's identifier;
- the trusted header names no method this reading uses, or names one whose result was `none`;
- the trusted header is malformed, or longer than the bound below;
- a check passed but named no usable domain, so there is no identity to record.

It is deliberately distinct from *failed*, which is the receiving server having attempted an identity and found it did
not hold. Any result other than `pass` and `none` for DKIM or SPF is that failure — `fail`, `softfail`, `neutral`,
`policy`, and both error results — because none of them establishes anything and the exact wording stays in the raw
MIME the verdict is re-derivable from.

### The header is untrusted input, and bounded like it

The header is what an attacker writes to defeat the check, so the reading treats it as hostile by construction: at most
16 headers per message, 32 method outcomes per header, 16 properties per outcome, and a header value of at most 4096
characters. A header past the length bound is passed over unread rather than truncated, and one no parser accepts
contributes nothing rather than failing the extraction. Either way the message is still extracted and simply has one
header fewer to read — which, where that was the only trusted header, is the not-established verdict.

## What MailFathom does not do

- It resolves no DNS, verifies no DKIM signature, and evaluates no SPF or DMARC policy. Everything recorded was read
  back out of one header.
- It does not reason from the `Received` chain beyond identifying the trusted header.
- It never acts on the verdict. Nothing here files, flags, or hides a message, and no rule reads it yet.

## Re-deriving what is already stored

The verdict is derived from the raw MIME the deployment stored, so it is re-derivable from it. Configuring a trusted
identifier for an account that previously had none changes what a later extraction records and leaves mail already
stored on the verdict it was given; [the extraction
backfill](imap-synchronization.md#backfilling-messages-stored-earlier) is what re-reads that mail. The
migration that adds the columns fills every stored message in with the not-established verdict, which is what was true
of them.

## What is not recorded anywhere else

Every domain the verdict names is personal data, so no log line, metric, or exception message carries one. What those
may report is the occurrence identity and the outcome. The startup refusal for an unusable configured identifier names
the account and not the value, for the same reason the failure rules refuse a host name in any message a boundary
publishes.
