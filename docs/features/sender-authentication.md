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

Knowing that *an* identity authenticated is not yet knowing that the author a reader is shown did, and knowing that is
still not knowing whether this deployment recognizes them. Those are three answers rather than one, and they are
recorded separately for that reason. This page describes all three: what is recorded, how the header is chosen, what
makes the displayed author *authenticated*, and what makes them *trusted*.

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
than of MailFathom, so there is nothing to default it to; [the mail
configuration](../operations/configuration-mail.md#one-account--mailsynchronizationaccountsn) states where the setting
lives. What to write in it is read off
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
| Whether the displayed author authenticated, and their domain where they did | The identity a reader is shown is the one an impersonation gets wrong, and it is a different fact from the identity that handed the message over |
| The domain the `From` header displayed, whether or not anything held | It is the other half of the comparison, and the half a reader needs most on the messages where nothing established an author. It is read from `From` alone, never from the `Sender` fallback a timeline names a message's sender by, so it cannot be derived from the stored address |

**DKIM is the authoritative identity wherever both checks produced one.** It is cryptographic — a key the signing
domain publishes signed these bytes — while SPF says only that a particular address was permitted to connect on behalf
of the envelope sender, which a forwarding hop legitimately breaks and a shared relay legitimately satisfies for
everybody using it. Both are kept, so a reader that cares about the difference still has it.

**`From` is never the source of the verdict.** It is attacker-controlled message content, so nothing is believed for
appearing in it and no list is ever held against it. It is recorded beside the verdict, and what it takes part in is
the second conclusion below rather than the identity above.

### Whether the displayed author authenticated

The identity a receiving server authenticates belongs to whoever handed the message over. A relay, a mailing list, and
a delivery provider all authenticate as themselves while carrying somebody else's `From`, so a message can authenticate
perfectly well while the author a reader is shown authenticated nothing at all — which is what every impersonation
looks like from here. That is recorded as a conclusion of its own, with the same three values and the same meanings as
the verdict above, beside the identity it was reached from.

Two things establish the author, and neither believes the header on its own.

- **A trusted `dmarc=pass`** is the receiving server's own statement that the displayed domain passed under the
  sender's published policy. That policy may permit a signing subdomain, which is exactly why the result is read back
  rather than reconstructed: nothing here resolves DNS, computes an organizational domain, or consults a public suffix
  list, so this answer is not one MailFathom could reach for itself.
- **An authenticated identity whose domain is exactly the displayed one**, where no usable DMARC result was reported.
  Exactly, because `mail.example.test` and `example.test` are two names and only the sender's own policy says whether
  the first may speak for the second. Every identity the trusted header reported as passing is compared, DKIM and SPF
  alike, so a delivery provider's signature listed first cannot hide the author's own listed after it.

**`dmarc=fail` is the only route to a failure**, and it ends the question rather than falling through to the second
route: the receiving server reached it with the displayed domain's own policy in hand, which is more than a comparison
made here without one. Everything else that establishes nothing is *not established* instead — a signing subdomain with
no DMARC result to interpret it, an identity belonging to somebody else, a message displaying no usable domain, a
header nothing trusted could be read from. A DKIM signature that did not verify is among them: it says nothing about
the author, because the signature may never have been theirs.

So a message may hold a passing DKIM identity, a passing SPF identity, and a failed author all at once, and it may hold
`dmarc=pass` while the domain that signed it is not the domain it displays. Both are ordinary readings rather than
contradictions, and both stay visible because the identity and the author are recorded separately.

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

An author who authenticated is still a stranger until something says otherwise, so a third answer is recorded beside
the two above: **trusted** or **unknown**. The rule is deliberately narrow. An author is trusted when their domain belongs
to an account this deployment synchronizes, or when an entry on the receiving account's trusted-sender list names them.
Everything else is unknown — including most legitimate mail, and that is the intended outcome. The claim being made is
*this deployment does not know who wrote this*, never *this is suspicious*; whether a message is wanted is [spam
classification](spam-classification.md)'s question and is reached by other means entirely.

This and the author conclusion are independent axes and are read together. **Author authenticated and unknown** is the
ordinary state of almost every message in a mailbox and says nothing against it, while **author failed and unknown** is
a message whose displayed author was checked and did not hold. Which of them applies stays where it was recorded, on
the authentication verdict above; nothing is folded into the trust value, which has exactly two values and answers only
about this deployment's list.

### What the list is held against

**The subject of the decision is the message's authenticated author, never the raw `From` header and never whichever
identity happened to authenticate.** Both of the alternatives are forgeries waiting to happen:

- `From` is written by whoever sent the message, so holding it against a list would let anybody be recognized by
  claiming to be somebody who is on one.
- The identity a receiving server authenticates belongs to whoever handed the message over. A relay, a mailing list, and
  a delivery provider all authenticate as themselves while carrying somebody else's `From`, so recognizing one of them
  would recognize every message it ever relays, whoever it says wrote them.

What establishes an author is [the conclusion above](#whether-the-displayed-author-authenticated), and the list is held
against its answer and nothing else. A message whose author was not established, and one whose author authentication
failed, both reach *unknown* without the list being consulted at all; which of the two it was stays where it was
recorded, on the authentication verdict.

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

**The list is what configuration declares, and nothing else adds to it.** An operator writes it when they set the
deployment up, and no surface edits it while the deployment runs — not `mfctl`, not a tool on the MCP surface, and
nothing a model can reach. So recognizing a new correspondent is a configuration change like any other: it is picked up
by a reload, and the next extraction judges against it — mail already stored keeps the verdict the list it was judged
under produced, which is what the revision below is for.

An entry that names neither a domain nor an address, names both, writes one nothing can compare, or asks for subdomains
on an address **fails startup**, naming the account and the entry's position and never the value it holds. The
alternative is indistinguishable afterwards: a list nobody wrote and a list whose entries match nothing both leave
every author unknown, and an operator would meet the difference as mail whose authors never stop reading as unknown.

### The verdict outlives the list

The answer is stored on the message rather than decided when the message is read, so a reader and a rule can ask
whether an author is trusted without re-evaluating a policy that may since have changed. What makes that legible is
the **policy revision** recorded beside it: a digest of the effective list, so two verdicts carrying different revisions
were reached under different lists, and one carrying none was never put to a policy at all. Reordering a list is not a
change to it and produces the same revision; adding an entry to it produces a different one.

Adding a domain therefore does not silently rewrite what a reader was already shown. What re-judges mail already stored
is [`mfctl mailbox rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release), the same deliberate
act that re-reads mail after an account gains a trusted authority.

## What the read tools publish

Every read tool publishes the verdict, because a caller that cannot see it cannot weigh a message — and the caller these
tools were built for is an agent, which reads a listing row as fact. What reaches a caller is **two published values and
never one**: `authorAuthentication`, which is [the conclusion about the displayed
author](#whether-the-displayed-author-authenticated) with its three values intact, and `deploymentTrust`, which is
[whether this deployment recognizes them](#whether-the-author-is-one-this-deployment-recognizes) with its two. Neither
is derived from the other and no published field merges them, because a single value would lose the distinction the two
conclusions exist to make. A third value travels beside them and belongs to neither: `machineAuthorship` is about how a
message's text was *written* rather than about who sent it, and [machine authorship](machine-authorship.md) is where it
is described.

| Tool | What it publishes |
| --- | --- |
| `list_emails` | The pair, on each listed email's summary |
| `search_emails` | The same pair, by republishing that summary rather than reshaping it |
| `get_email_content` | The pair, and beside it the evidence: the domain that authenticated, the domain the `From` header displayed, which check established the first, and the DMARC result |
| `ask_mail` | The pair, on each citation, without the evidence |

**The listing carries the verdict and the single-email read carries the evidence.** A listing exists to let a reader
recognize a message and already narrows `Cc` and `Reply-To` away, and the two outcomes are what a caller branches on;
the domains, the method, and the DMARC result are how a reader judges the verdict rather than acts on it, so they sit
with the rest of the headers, on the read of a message somebody has already found. Both domains are published in the
comparison form the columns hold — upper-cased, and an internationalized name in its ASCII form. A `null` domain is an
ordinary outcome rather than missing data: nothing authenticated, or the message wrote no usable `From` mailbox.

**The two domains differing is not by itself a spoofed author.** The authenticated one is whichever identity
authenticated the transport, and where both checks produced an identity that is the DKIM domain — while the author is
established by *any* authenticated identity matching the displayed domain, the SPF one included. A message relayed by a
provider that signs as itself, whose envelope sender passes for the author's own domain, therefore publishes two
different domains and is authenticated exactly as it appears. That is why nothing here restates the comparison as a flag
of its own: `authorAuthentication` is the conclusion, reached against every identity that authenticated rather than
against the one kept as evidence, and the domains say what stood behind the message rather than answering the question
themselves.

**Every published value was stored when the message was extracted.** A read evaluates nothing, resolves no DNS, re-reads
no header, and triggers no IMAP fetch. Mail stored before the columns existed therefore reads as *not established* and
*unknown*, with every domain absent, which is what its row holds rather than a state invented for it — and an absent
domain there is indistinguishable from a message that displayed none. [`mfctl mailbox
rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) is what fills it in, by re-reading the raw
MIME the deployment already stored back through the same extraction that wrote the columns in the first place. It is
that pass rather than [the extraction backfill](imap-synchronization.md#backfilling-messages-stored-earlier), which
selects only messages carrying no search document and therefore steps over every message already extracted — which is
all of them, on a deployment where the gap is a column a later release added rather than an extraction that never ran.

The published descriptions are the advertised output schema and are therefore the whole of what a model is told about
these values, so they say what the values mean rather than only what they are called: that deployment trust is this
deployment's own classification and not an authentication result, that *unknown* is the ordinary state of legitimate
mail from a new correspondent, and that the sender address beside them is a claim the message wrote about itself. None
of them characterizes the message or the sender's intent. A failed authentication is stated as a failed authentication.
[MCP tools](mcp-tools.md#list_emails) holds the published shape of each result.

## What MailFathom does not do

- It resolves no DNS, verifies no DKIM signature, and evaluates no SPF or DMARC policy. It computes no organizational
  domain and consults no public suffix list, so it never reconstructs DMARC's relaxed alignment for itself. Everything
  recorded was read back out of one header.
- It does not reason from the `Received` chain beyond identifying the trusted header.
- It acts on neither verdict by itself. Nothing here files, flags, or hides a message, and publishing the pair through
  the read tools is not acting on it: what a caller is handed is the stored conclusion, and what to make of it is the
  caller's. What can act on it is a rule the owner wrote — `authorAuthentication` and `senderTrust` are
  [facts a condition can read](mail-rules.md#the-facts-a-condition-can-read), so filing mail on a verdict is something
  an owner declares rather than something this feature does.
- It does not let a caller filter or sort a listing or a search by either verdict. That is a question about what may be
  asked for rather than about what a result carries, and it is a decision of its own.

## Re-deriving what is already stored

The authentication verdict is derived from the raw MIME the deployment stored, so it is re-derivable from it, and the
trust verdict is re-derived in the same pass against whatever list is in force then. Configuring a trusted identifier
for an account that previously had none changes what a later extraction records and leaves mail already stored on the
verdict it was given; [`mfctl mailbox rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) is
what re-reads that mail, writing the whole group of columns back through the extraction that first wrote them. The migration that adds each group of columns fills every stored message in with what was true of it
— the not-established verdict, and the unknown answer under no policy at all.

## What is not recorded anywhere else

Every domain either verdict names is personal data, and so is every entry of a trusted-sender list, which says who
somebody corresponds with. No log line, metric, or exception message carries one. What those may report is the
occurrence identity and the outcome. The startup refusal for an unusable configured identifier names the account and
not the value, and the refusal for an unusable trusted-sender entry names the account and the entry's position, for the
same reason the failure rules refuse a host name in any message a boundary publishes.
