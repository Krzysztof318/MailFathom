# Sender authentication

<!-- describes: src/Domain/Emails/Authentication/**, src/Application/Mail/ITrustedAuthenticationAuthorityReader.cs, src/Application/Mail/ISenderTrustPolicyReader.cs, src/Application/Emails/Extraction/SenderTrustEvaluatingEmailMimeReader.cs, src/Infrastructure/Mail/Mime/AuthenticationResultsHeaderReader.cs, src/Infrastructure/Mail/Mime/MimeKitEmailMimeReader.cs -->

Everything about deciding whether a message is from who it says rests on one question: which domain actually sent it?
The obvious answer is wrong. `From` is a header the sender writes, it is what a mail client displays, and it is what
anybody impersonating somebody else controls completely — so a check derived from it would be accurate about honest
mail and silent about the mail it exists for.

The answer that means something comes from the receiving mail server, because it is the only party in the chain that
observed the connection. It saw the envelope sender and could check SPF against the connecting address, and it could
verify a DKIM signature against a key the signing domain publishes. RFC 8601's `Authentication-Results` header is where
it writes both down. MailFathom reads that header back and records what it said; it verifies nothing itself.

Knowing what the receiving server established is half the question. The other half is whether the message's author is
somebody this deployment recognizes, which is a decision about a list rather than a fact about the message, and the two
are recorded separately for that reason. This page describes both: what is recorded, how the header is chosen, and what
makes an author *trusted*.

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

## Whether the author is one this deployment recognizes

A domain that authenticated is still a stranger until something says otherwise, so a second verdict is recorded beside
the first: **trusted** or **unknown**. The rule is deliberately narrow. An author is trusted when their domain belongs
to an account this deployment synchronizes, or when an entry on the receiving account's trusted-sender list names them.
Everything else is unknown — including most legitimate mail, and that is the intended outcome. The claim being made is
*this deployment does not know who wrote this*, never *this is suspicious*; whether a message is wanted is [spam
classification](spam-classification.md)'s question and is reached by other means entirely.

The two verdicts are independent axes and are read together. **Authenticated and unknown** is the ordinary state of
almost every message in a mailbox and says nothing against it, while **failed and unknown** is a message whose author
was checked and did not hold. Which of them applies stays where it was recorded, on the authentication verdict above;
nothing is folded into the trust value, which has exactly two values and answers only about this deployment's list.

### What the list is held against

**The subject of the decision is the message's authenticated author, never the raw `From` header and never whichever
identity happened to authenticate.** Both of the alternatives are forgeries waiting to happen:

- `From` is written by whoever sent the message, so holding it against a list would let anybody be recognized by
  claiming to be somebody who is on one.
- The identity a receiving server authenticates belongs to whoever handed the message over. A relay, a mailing list, and
  a delivery provider all authenticate as themselves while carrying somebody else's `From`, so recognizing one of them
  would recognize every message it ever relays, whoever it says wrote them.

Two things establish an author, and neither believes the header on its own. A trusted `dmarc=pass` is the receiving
server's own statement that the displayed domain passed under its published policy, so the displayed domain is the
answer. Failing that, an authenticated DKIM or SPF identity whose domain is exactly the displayed one is the same claim
reached without DMARC. Everything else — nothing established, an authentication that failed, `dmarc=fail`, an identity
belonging to somebody other than the displayed author — establishes no author, and the list is not consulted at all.

Only the one DKIM identity the authentication verdict names is considered, so a message carrying a second signature over
the displayed domain beside an unrelated first one reads as establishing no author. That withholds an author rather than
inventing one, which is the direction a mistake here has to fall in.

### The deployment's own domains

Mail whose author writes from a domain one of this deployment's own accounts uses is trusted, on every account rather
than on the account that owns the domain. An instance synchronizing a work mailbox and a personal one is synchronizing
one person's correspondence, and mail that person sends from the first to the second is the least suspicious mail in the
mailbox; recognizing it only against the receiving account's own domain would leave the owner's own mail unknown.

The set is derived from the configured accounts rather than restated in configuration, so adding an account extends it
and removing one narrows it without a second edit. It is read from each account's IMAP user name, which is the only
mailbox identity an IMAP account states — a server is reached at a host that is rarely the mail domain, and the account
identifier is a key an operator invented. **An account whose user name is a bare login therefore contributes nothing**,
and a deployment in that position names its domains on the accounts' own lists instead. Only the domain itself counts
and never the names beneath it.

`MailSynchronization:TrustOwnAccountDomains` is what turns the whole set off, and it defaults to on because that mail is
either the owner's own or somebody who has taken their mailbox, and the first is far more common. **The case for turning
it off is an account on a large shared provider**: every user of that provider writes from the same domain, so the set
would recognize all of them. A deployment that turns it off names the domains it does mean on the per-account list
below.

### The trusted-sender list

Each account has one, and it is per account because the accounts an instance synchronizes are different correspondence:
a work account's counterparties have nothing to do with a personal one's, and a single list would either recognize too
much on one account or make an owner maintain the union of both.

An entry names a domain or a single address, never both, and the two are different claims.

- **A domain entry** recognizes an author established as writing from that domain. Reaching under it is opt-in per entry
  — `IncludeSubdomains` — rather than a mode the list runs in, so an organization signing everything as one name can be
  given its subdomains while a single host recognized inside a domain full of unrecognized ones does not drag the rest
  in.
- **An address entry** narrows that to one mailbox, and it is worth knowing what it rests on. What is ever established
  is a domain and never a mailbox — DKIM signs as `d=`, SPF answers for the envelope sender's domain, and DMARC states
  that one of those held for the displayed domain — so an address entry matches when the author's domain is the entry's
  own **and** the message's `From` header displays exactly the entry's address. The claim is therefore: this domain
  wrote the message, and it presents it as coming from this mailbox of its own. That is worth something, because a
  domain answerable for its own `From` is answerable for the whole address in it; it is worth less than a domain entry,
  because a domain that can authenticate can display any local part it likes. A message whose `From` is missing or
  unusable is unknown to an address entry.

Comparison is case-insensitive, and an internationalized domain is held in one encoding rather than compared in two: a
name is put into its ASCII A-labels wherever it comes from, so an entry written `bücher.example` recognizes a message
that carried `xn--bcher-kva.example`. A name no encoder accepts is refused rather than compared as it arrived, because
a value nothing else can produce would match nothing and look like an author who is simply not on a list.

**The list has two halves and one matcher reads both.** Configuration holds what an operator declared when they set the
deployment up; a store holds what somebody added while it was running, because the useful act when a reader meets a
warning on mail from a correspondent they trust is to trust them, and editing a file and restarting is not that act. An
entry in either half recognizes, and neither can undo the other — configuration is not editable at runtime and the
store is not editable by a configuration reload. Where both name one author the configured half is reported, so a
deployment's declared trust is never described as something added later. The stored half and the surfaces that edit it
are [issue #760](https://github.com/Krzysztof318/MailFathom/issues/760); until it exists the effective list is the
configured one.

An entry that names neither a domain nor an address, names both, writes one nothing can compare, or asks for subdomains
on an address **fails startup**, naming the account and the entry's position and never the value it holds. The
alternative is indistinguishable afterwards: a list nobody wrote and a list whose entries match nothing both leave
every author unknown, and an operator would meet the difference as mail that never stops carrying a warning.

### The verdict outlives the list

The answer is stored on the message rather than decided when the message is read, so a reader — and later a rule — can
ask whether an author is trusted without re-evaluating a policy that may since have changed. What makes that legible is
the **policy revision** recorded beside it: a digest of the effective list, so two verdicts carrying different revisions
were reached under different lists, and one carrying none was never put to a policy at all. Reordering a list is not a
change to it and produces the same revision; adding to either half produces a different one.

Adding a domain therefore does not silently rewrite what a reader was already shown. What re-judges mail already stored
is [the extraction backfill](imap-synchronization.md#backfilling-messages-stored-earlier), the same deliberate act that
re-reads mail after an account gains a trusted authority.

## What MailFathom does not do

- It resolves no DNS, verifies no DKIM signature, and evaluates no SPF or DMARC policy. Everything recorded was read
  back out of one header.
- It does not reason from the `Received` chain beyond identifying the trusted header.
- It never acts on either verdict. Nothing here files, flags, or hides a message, and no rule reads them yet.

## Re-deriving what is already stored

The authentication verdict is derived from the raw MIME the deployment stored, so it is re-derivable from it, and the
trust verdict is re-derived in the same pass against whatever list is in force then. Configuring a trusted identifier
for an account that previously had none changes what a later extraction records and leaves mail already stored on the
verdict it was given; [the extraction backfill](imap-synchronization.md#backfilling-messages-stored-earlier) is what
re-reads that mail. The migration that adds each group of columns fills every stored message in with what was true of it
— the not-established verdict, and the unknown answer under no policy at all.

## What is not recorded anywhere else

Every domain either verdict names is personal data, and so is every entry of a trusted-sender list, which says who
somebody corresponds with. No log line, metric, or exception message carries one. What those may report is the
occurrence identity and the outcome. The startup refusal for an unusable configured identifier names the account and
not the value, and the refusal for an unusable trusted-sender entry names the account and the entry's position, for the
same reason the failure rules refuse a host name in any message a boundary publishes.
