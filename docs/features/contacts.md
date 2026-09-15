# Contacts

<!-- describes: backend/src/Domain/Contacts/**, backend/src/Application/Contacts/**, backend/src/Infrastructure/Persistence/Contacts/**, backend/src/Infrastructure/Persistence/Entities/ContactEntity.cs, backend/src/Infrastructure/Persistence/Entities/ContactAddressEntity.cs, backend/src/Host/Api/Contact*.cs, backend/src/Cli/Commands/Contacts/**, backend/src/Cli/Administration/Contacts/**, backend/src/Mcp/Tools/Contacts/**, backend/src/Host/Configuration/Mail/ContactCollection*.cs, backend/src/Host/Configuration/Mail/Readers/ConfiguredContactCollectionSettingsReader.cs, backend/src/Infrastructure/Mail/Mime/MailAutomationReading.cs -->

MailFathom holds a contact book of its own: people, the addresses they use, and what a user recorded about them, in
the same PostgreSQL database the mail is in. This page describes the record and the rules every writer of it obeys —
what identifies a person, when two addresses are the same address, who may change what, and what erasing somebody
removes.

**Two surfaces reach the book.** `mfctl contact` maintains it over the deployment's administrative endpoint;
[administering a deployment](../operations/admin-endpoint.md#administering-the-contact-book) holds the command group in
full. An agent reads and writes it over the MCP endpoint, under two grants of its own; [MCP tools §
`list_contacts`](mcp-tools.md#list_contacts) holds those six tools and what each of them answers. A third writer is the
deployment itself: [§ Collecting contacts from arriving mail](#collecting-contacts-from-arriving-mail) describes what an
account records on its own, and it is off until a user switches it on, so an instance nobody has written to and nobody
switched collection on for holds no contacts at all.

**One reader is not a surface at all.** A message may be addressed by naming somebody in the book rather than by writing
an address, and the send resolves that name against the book on its way to the outgoing record; [mail delivery §
Addressing a message by naming a contact](mail-delivery.md#addressing-a-message-by-naming-a-contact) holds how a name
becomes an address, which name refuses to, and what the record then keeps. Nothing about that writes the book: addressing
somebody is not a fact about them, so no contact is created, amended, or promoted by being written to.

## Two books, and a read of both

**What a person wrote down belongs to them; what a mailbox picked up belongs to the mailbox.** Those are two books,
with two holders:

| Book | Holder | What is in it |
| --- | --- | --- |
| A user's own | The user | Everything anybody wrote down for them — every asserted contact |
| A mail account's collected | The mail account | Everything collection picked up out of the mail that arrived on that account — every collected contact |

A contact record names exactly one of the two, the record's origin agrees with which it named, and the database refuses
a row where the three disagree rather than leaving every reader to reason about it. So a collected record filed under a
user, or an asserted one filed under a mailbox, is not a state this system has.

The split is what a shared mailbox costs and what it buys. Two people assigned one account are two users and one
mailbox: the account collects a correspondent **once**, both of them read that one record, and neither of them holds a
copy the other's edit would have to chase. And when one of them leaves, what goes with them is the book they wrote —
the mailbox's stays for whoever is still assigned it. [Single-tenant multi-user
ownership](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
is the decision the second half of that follows from.

**A user reads both, as one book.** Every read a user makes — a listing, a lookup by address, a lookup by name, an MCP
tool, addressing a message by naming a contact — covers their own book and the collected book of every mail account
they are assigned. That set is resolved from who the caller was admitted to act for, exactly as the mail beside it is,
and no request of theirs can name another user's.

**The order between those books is fixed and stated, because two of them may hold one address.** It is the user's own
book first, then the accounts by the ordinal order of their identifiers, and **a record is hidden when a book earlier in
that order holds one of its addresses**. Two things follow. A contact the user wrote down hides a collected one carrying
the same address — which is the point of writing somebody down: their name and their note are what the user sees, and it
hides it for that user alone, because the other users of the mailbox never saw their record. And an address two of their
accounts both collected is answered once, from the first of the two in that order — the same answer on every read, and
the same answer for two users of one mailbox, because the order is the accounts' own rather than the order either user's
record happened to declare them in. The database applies it in the query rather than a reader applying it afterwards, so
a page never serves one person twice, and it is applied as the page is read rather than to a page already served — so a
page still holds the size asked for while the walk has more to serve, and the cursor rather than the count is what says
whether it does.

**Which book an act writes is never the same question as which books it reads.** A caller's writes go into their own
book: recording a person, amending one, promoting one. Collection writes into the book of the account it is
synchronizing and into no other. Erasure is the exception and deliberately so — see [§ Erasing and exporting a
person](#erasing-and-exporting-a-person).

Each book keys onto its holder and is erased with it, by the schema rather than by a statement somebody remembers to
write. Erasing a user takes their own book, and erasing a mail account takes the book it collected, beside that
account's folders and mail; `contact_addresses` keys onto `contacts` through `(ContactId, BookHolderId)` either way, so
the cascade reaches an address through the person holding it. A collected book therefore goes when its account does,
which is when the last user assigned it is erased.

## A contact is a person, not an address

One person uses a work address, a personal one, and an old one they still receive on. A book keyed on the address could
not say those were the same person, which is the whole reason to have a book rather than a list — so the record is the
person and the addresses hang off them.

| Part | What it holds |
| --- | --- |
| Identity | MailFathom's own UUID, minted when the contact is recorded and never derived from an address |
| Name | What the user wrote, kept in their casing, at most 256 characters and carrying no character that renders as nothing |
| Addresses | At least one and at most 32, each at most 320 characters |
| Preferred address | Which of them to use when something addresses the person without naming which |
| Note | Optional free text, at most 4000 characters, in which line breaks and tabs are kept and every other character that renders as nothing is refused |
| Origin | How the contact came to be in the book — see [§ Two origins](#two-origins-and-who-may-write-which) |
| Recorded, amended | When the contact entered the book, and when it was last changed |

The identity is the book's own and is never an address, because an address is a thing a person has rather than a thing
they are: they give one up, gain another, and stay the same person. It is also the only part of a contact that may
appear in a log line, a metric, or a failure message, beside the origin, which is MailFathom's own classification of how
a record arrived rather than anything the person supplied. Everything else is personal data about a third party.

Which address is preferred is the user's choice rather than an ordering accident. Nothing picks one, and a record
naming a preferred address the contact does not hold is refused rather than repaired.

A name and a note are published into listings of the other contacts, into an answer an agent reads, and into an export,
so both refuse the characters that carry no glyph of their own: the control characters, the line and paragraph
separators, and the formatting characters, among which the bidirectional overrides are what would let one name render as
text it does not contain. A note keeps line breaks and tabs, because a note is written to be read as somebody wrote it.
The zero-width joiner and non-joiner are admitted in both, because Persian, Arabic, and the Indic scripts write them
inside ordinary words and refusing them would refuse names people actually have. Text that is not well-formed at all —
a surrogate with no partner, which is no character and has no category — is refused for a different reason: the first
thing to reject it otherwise would be the encoder writing the row, so a value somebody typed would come back as an
encoding failure rather than as a value that was not accepted.

## When two addresses are the same address

Two addresses name the same mailbox when their comparison forms are equal, and the comparison form is the whole address
upper-cased. `Anna@Example.test`, `anna@example.test`, and `ANNA@EXAMPLE.TEST` are one address everywhere in the book:
in a lookup, in the uniqueness rule below, and in what a record is stored as.

RFC 5321 makes the local part case-sensitive and almost no mail provider honours that. A rule that split those three
would store one person as two records for a distinction their own mail server does not make, and every surface over the
book would then disagree with itself about who a message is from. The cost is stated rather than hidden: a server that
genuinely does distinguish them is served one contact where it has two mailboxes.

The written form is kept beside the comparison form, so what a reader is shown is the address as somebody wrote it. Only
the comparison form is matched, grouped, or indexed on. It is the same rule and the same value the mail beside it is
matched by, defined once in the domain rather than per writer.

**One address belongs to one contact, within one book.** Adding an address a different contact in the same book
already holds is refused with an answer naming which contact holds it, so the caller can look at that person rather than
guess. The rule is a unique index in PostgreSQL rather than a check before the write: two callers claiming one address at
once both read that nobody holds it, and only the database closes that window. Losing that race is a conflict the write
retries from a fresh read, and the second caller is then told who holds the address.

It stops at the book's edge deliberately, and there are two edges it stops at. Two users who correspond with the same
person each hold their own record of them, with their own name for them and their own note, because a rule across the
table would make what one user may write down depend on what another already had — and would let one of them find out
that the other corresponds with somebody by being refused. And a user's own record and a record one of their mailboxes
collected may both hold one address: they are two books, the one the user wrote is the one their reads answer with, and
refusing the second would mean a person could not write somebody down because a mailbox they share had already met
them.

Inside one record, two spellings of one address are merged rather than refused. They name the same mailbox of the same
person, and refusing would ask a user to resolve a difference nothing else makes.

## Two origins, and who may write which

A contact is either **asserted** — somebody wrote this person down — or **collected**, an address that appeared in mail
that arrived. The origin is not a flag beside the record: it says which of the two books holds it, and the database
refuses a row whose origin and holder disagree. A user reads both at once, so searching for somebody never requires
knowing which half they are in. What the difference decides is who may change the record without anybody asking:

- A writer amends the contacts of its own origin and no others, and an amendment writes into the writer's own book
  alone. Collection never touches what a user wrote down, and a user amending a record their mailbox collected is
  refused by that record's origin rather than having a mailbox's record rewritten under everybody else assigned it. The
  refusal names the origin rather than reporting a contact nobody holds, because what the caller has to be told is that
  promotion is the act that makes such a record theirs.
- **Promotion** is the one crossing between the books, and it **copies**: it writes the caller's own record of the
  collected person, under an identity of its own, and leaves the mailbox's where it is for whoever else is assigned that
  account. What the caller gains is a record they may then amend like any other, and which their own reads answer with
  in place of the collected one. It names its writer for the same reason an amendment does, so collection is refused the
  promotion of the record it just created rather than being able to award itself the authority that comes with it.
  Nothing turns an asserted contact back into a collected one, because nothing can unsay that somebody wrote a person
  down. Promoting a contact the caller has already asserted is answered as such rather than written again.
- Origin is recorded when the contact is created and is never changed by an amendment.

Both surfaces a caller reaches the book through write as **asserted**, into the caller's own book, because both are
somebody writing a person down: `mfctl` is the user at a terminal, and an agent over MCP is acting for them. What
follows is that neither amends a collected record — an agent's call is refused by that record's origin — and what
either does instead is promote it. Both reach that act: `mfctl contact promote` and the
`promote_contact` tool, under the same writing grant each surface already holds. A promotion reachable from only one of
the two would leave an amendment permanently refused on the other for every record collection produced.

Erasure is deliberately outside that rule. It is the data-subject path, and somebody asking to be removed from a contact
book is not answered with which half of the book they happen to be in.

## Collecting contacts from arriving mail

An account can record the people it corresponds with as its mail is synchronized. It is **off unless a user switched
it on, and switched on per account**, because what it produces is derived personal data about people who never dealt
with MailFathom: an instance nobody asked never accumulates one, and a deployment reading a work mailbox and a personal
one decides separately for each. [Configuration §
`ContactCollection`](../operations/configuration-mail.md#contact-collection), which is a block of the account's own
declaration in its user's record, holds the keys and their bounds.

Collection runs inside the synchronization pass that stored the message, after the transaction that stored it committed.
**What it writes goes into the collected book of the account it is synchronizing, and into no other** — it never asks
who that account's users are, and it never reads a user's own book. That is what makes one mailbox's collection one
mailbox's: a correspondent is recorded once however many users are assigned the account, every one of them reads that
one record, and nothing a user wrote down can stop the mailbox from recording somebody it corresponds with.
It owns no worker, no timer, and no queue, and it reaches the mail server for nothing at all: the headers it reads were
already read to store the message, so what one message costs is a bounded number of indexed reads and, rarely, one
insert.

**The folder decides which header is read, and nothing else is ever read.**

| Folder | What it contributes |
| --- | --- |
| The folder mapped as `Sent` | The primary recipients — the `To` header. The user writing to somebody. |
| `Drafts`, `Junk`, `Trash` | Nothing. A draft is unsent, and the other two say the opposite of what a book is for. |
| Every other folder | The author — the `From` header. Somebody writing to the user. |

`Cc` and `Bcc` are the copied recipients of somebody else's thread; `Sender` and `Reply-To` name where a message was
submitted from and where a reply is to go rather than who the correspondent is. None of the four is read.

**The two directions are held to different evidence.** An address that wrote to the user is recorded once it has
written `MinimumMessagesFromSender` messages to that account — two by default, because one message from a stranger is
not correspondence. An address the user wrote to is recorded on first sight, because the user having addressed
somebody is exactly the evidence a count of their messages stands in for. The count is answered from the mail the
account has already stored, on the same indexed sender column a mailbox query uses, and it stops counting at the
threshold rather than counting the whole mailbox. Nothing is written down to answer it, so collection derives no record
of its own beside the contacts it produces.

**A message the user sent to more than 16 primary recipients contributes none of them.** A letter is addressed to the
few people it concerns and an announcement to everybody, and the count tells them apart without reading a word of
either. Past the bound the message contributes nothing rather than its first few recipients, because a truncation would
record whoever the sender happened to write first.

Four things are never collected, and none of them can be switched off:

- **A message a machine sent.** A mailing list stamps `List-Id`, `List-Post`, or `List-Unsubscribe` on what it
  distributes (RFC 2919 and RFC 2369); an automatic responder states `Auto-Submitted` with any keyword but `no`
  (RFC 3834); and `Precedence: bulk`, `list`, or `junk` is the oldest way of saying a message went to many rather than
  to one. Each is a claim the sender made in a header defined for that purpose rather than something inferred, which
  matters most for a mailing list: a list posting carries the real address of the person who wrote it, and no rule about
  mailbox names could tell one from ordinary correspondence.
- **A role mailbox**, by the names RFC 2142 reserves — `postmaster`, `abuse`, `info`, `support`, `sales`, and the rest —
  together with the `no-reply` family and the `-request`, `-bounces`, `-user`, `-admin`, `-subscribe`, and
  `-unsubscribe` list-administration suffixes.
- **The account's own mailboxes**, derived from the user name of every account declared beside it, so a user writing
  from one of their mailboxes to another is not recorded as a correspondent of themselves. The set stops there:
  somebody this deployment also serves, on an account this one is not declared beside, is an ordinary correspondent of
  the account they wrote to.
- **An address the account's own collected book already holds.** That is where a second sighting of a correspondent
  stops, and the record itself is never read: what a collected record says about somebody is what the message that first
  produced it said, and rewriting it on every later message would make the book a log of the last envelope. What a user
  wrote down is in another book and is neither read nor consulted — a mailbox two people share cannot stop collecting
  because one of them happened to write the sender down, and the precedence a read applies is what hides the collected
  record from that one person and from nobody else.

On top of those a user writes their own exclusions per account, each naming either a domain — optionally reaching the
names beneath it — or a pattern over the whole address, where `*` stands for any run of characters and `?` for exactly
one. An entry that names both, or neither, or a pattern whose only characters are the two wildcards and the at-sign —
which takes every address or none of them, `*@*` being the shape that reaches a user — is refused at startup rather than
silently excluding nobody.

**One folder run records at most `MaxContactsPerRun` contacts**, 50 by default. The bound is per folder rather than per
account, exactly as the run's content budget is, so an account whose configuration maps several folders may reach it
once for each of them in a single synchronization cycle. What that paces is the first synchronization of a mailbox
holding years of mail, where every
message is new and the book would otherwise gain thousands of people in one pass before anybody had seen one of them. A
run that reaches the bound stops recording and leaves the rest for the next run, which reads the same senders again and
finds the evidence they need still standing. Zero records nobody while leaving collection on, which is a way to see what
a policy would do without writing anything.

**A collected record is named by what the message offered** — the display name the header wrote, or else the address
itself where the header wrote nothing usable. A sender's spelling of somebody's name is exactly what a collected claim
is: weaker than a name the user wrote down, and replaced by one the moment they promote the record.

What collection reports is one measurement per address considered, tagged with which of six conclusions was reached, so
a book filling too fast, a policy excluding everything, and a run repeatedly stopping at its bound are readable apart
from each other. No address, name, folder, or message identity reaches an instrument; [telemetry §
`mailfathom.contacts.collection.decisions`](../operations/telemetry.md#contact-collection) holds the counter.

**A user who changes their mind takes the whole of it back, one account at a time.** A collected book is one mail
account's, so `mfctl contact delete-collected --account <id>` empties that account's and leaves both what another
account collected and every record anybody entered. It is an act over a mailbox rather than over one person's records:
the book it empties is read by every user assigned that account. It cannot be undone — switching collection on again
rebuilds the book from the mail that arrives afterwards rather than restoring what went. Switching it off is a separate
act in configuration, and one worth making, per account like the switch itself: with it still on, the book fills again
from the next message.

## Amending a contact states the whole record

An amendment carries the record the user wants the contact to have — the name, every address, which one is preferred,
and the note — rather than the difference from the one held. Adding an address, dropping one, choosing a different
default, and correcting a name are then one operation whose result is checked against the invariants above, instead of
four that could each leave a contact without an address or with a default it does not hold.

An address the amended record no longer names is removed, which is also what frees it for another contact in the same
book to claim.

An amendment writes into the caller's own book alone. A record one of their mailboxes collected is therefore refused by
its origin, naming the record as collected, and what the caller does instead is promote it — which leaves them a record
of their own to amend and leaves the mailbox's for everybody else assigned the account. A contact outside the books the
caller reads at all is a contact they do not hold, and is answered as such.

Two amendments of one contact are last-writer-wins, which is what stating the whole record means: the later one is the
record, exactly as it would be a second later. What is not left to that is an amendment racing an **erasure** — the row
carries PostgreSQL's own version token, as
[ADR 0001](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)
requires of every record written in place, so an amendment of a contact somebody erased meanwhile writes nothing and is
answered as a contact the book does not hold rather than putting the person back.

## Reading the book

Three lookups and one listing:

- **By identity**, which is what every other part of the system names a person by.
- **By address**, which answers "who is this from" and is served from the unique index rather than from a scan. The index
  leads with the book, so the lookup seeks into each of the books the reader holds rather than scanning the table. Each
  of those books can answer at most once, which is the uniqueness rule above, and the order between them settles which
  of two answers the reader is given.
- **By the whole name**, which answers "who did they mean" and is what addressing a message to somebody reads. It matches
  the name's comparison form exactly rather than looking for text inside it, so it is served from the listing index the
  book and the name's key lead, and it answers with the one contact the reader's books carry the name in or with how
  many carry it. A namesake in a book they do not read is not one of them, and a collected record a contact of their own
  hides is not one either. More than one is not
  a result to choose from: nothing here ranks people, and [mail delivery §
  Addressing a message by naming a contact](mail-delivery.md#addressing-a-message-by-naming-a-contact) is where refusing
  that is argued. The count is exact and comes from the database, and the addresses of the people a shared name matched
  are never read, so a name a hundred collected contacts happen to share costs one number rather than a hundred records.
  A name resolving to one person answers with that person and with the count that decided it read together, so the answer
  can never be one of two people a namesake written down meanwhile made ambiguous.
- **A page of the books**, bounded and continued by a keyset cursor. The order is the name's comparison form and then
  the identity, across the books the page is of, which makes it total: two people with one name are still served in a
  fixed order, so a walk serves every contact exactly once. A record a book earlier in the reader's order hides is not
  served at all, and the hiding is applied as the page is read, so a page holds the size asked for until the walk runs out. A page carries 50 contacts unless the caller asks for fewer, and never more than 200.
  A listing may be narrowed to one origin, which is the question "what did my instance pick up" and its inverse, and to
  a **search**.

The name lookup and the identity lookup each also answer a **set**, up to a page of the book's worth in one read, because
one message may name many people and a query per person would let its recipients decide what addressing it costs. What
comes back is a match for every name asked about — reporting nobody where nobody carries it — and a contact for each of
the identities the book holds, with nothing standing in for the ones it does not. Both are the same answers the
single-value lookups give, which is why nothing above changes for a caller with one name in hand.

A search is text a contact has to carry somewhere in its name or in one of its addresses, matched on the same comparison
forms everything else here is matched on — so the search text is upper-cased once and compared against the stored name
key and the stored address key, and casing is not a thing a caller has to get right. It is a containment match rather
than a pattern: a wildcard character a caller wrote is text to look for, and there is no syntax to learn. The text is
bounded at 320 characters, the longest address the book can hold, and is refused for the characters a name is refused
for, because a value that renders as nothing would select on something nobody can see.

It is the one read here no index answers, and that is deliberate rather than pending. A book is a few hundred people at
the scale this system is written for, the pages are bounded whatever narrows them, and an index over "text anywhere in a
value" is a different structure from the ones that make the lookups above exact. Where a caller has an address, the
address lookup is the answer and is served from the unique index; a search is for the case where they have a fragment of
a name.

A search never widens what a walk serves: the cursor is the position in the book's own order and is not bound to the
filters, so continuing a walk with a different search or a different origin is defined rather than refused.

The order is taken on a comparison form stored beside the name rather than on the name itself, and the column holding it
is pinned to PostgreSQL's `C` collation, so the order is the ordinal one MailFathom derived the form to produce rather
than whichever collation the database was created with. The index it is served from leads with the book and ends with
the identity, which is what lets each of a reader's books be reached by a seek rather than the table scanned, and
what makes the walk over them terminate.

## Erasing and exporting a person

A contact book is the most concentrated personal data this system holds: not mail that arrived about somebody, but an
assembled record about identified third parties. Both data-subject paths therefore exist from the first commit rather
than as a later addition, and both are proven by test rather than described.

**Erasing a contact removes them and everything derived from them.** The schema's own foreign key is what guarantees no
address outlives its person, rather than a second statement somebody remembers to write; the erasure takes those rows
first inside the same transaction so it can answer with what it removed — the contact and how many addresses went with
it — and a user asking for one gets an answer rather than a call that returned without complaint. Erasing somebody the books do not hold is a completed erasure, not
a failure: the state the user asked for is the state the books are in.

**It reaches every book the caller reads, not only their own.** That is the one act over the books that does, and it is
what a person asking to be taken out of a contact book is owed: no origin gates it, and somebody making that request is
not answered with which half of which book they happen to be in. So **erasing a collected contact takes it out for every
user assigned that mailbox**, because it was one record rather than a copy each — which is the honest reading of the
request and the cost of the mailbox holding one record for everybody.

**Erasing a user erases their own book.** It is the cascade rather than an erasure of its own, and it runs in two
hops: the asserted half of `contacts` keys onto the user record and `contact_addresses` keys onto `contacts` through
`(ContactId, BookHolderId)`. So a data-subject request against the user discharges the people they wrote down and every
address beneath them along with the mail. What it leaves is what their mailboxes collected, which belongs to those
mailboxes and to whoever is still assigned them; a mailbox nobody is left assigned is erased with them, and its
collected book goes with it.

**Exporting a contact produces everything held about them** as of the instant it was taken: the name, every address,
which is preferred, the note, the origin, and both timestamps. What a user reads is left to the surface that asks for
it, so no surface can choose which parts of a person to hand back.

Both are commands rather than seams something else is expected to reach: `mfctl contact delete` erases and
`mfctl contact export` writes the document, because a data-subject path nothing invokes is one that will not work on the
day somebody asks for it. The erasure asks before it runs and answers with what went — the identity and how many
addresses — and never with the person.

Erasure is also a tool, `delete_contact`, and it answers the same counts. Export is not: the document is the answer to a
request a person made of whoever holds the book, and the surface that produces it is the one that names the user it is
reaching the book of.
An agent that needs what an export holds reads the contact, which is the same record without the framing of a
data-subject reply.

## What the book is held under

Nothing about a contact is held under weaker terms than a message is. It is the same database, the same access, and the
same storage protection as the mail beside it, with no field-level encryption on either — what protects both is the
deployment's own protection of its database. There is no retention window: a contact is held until somebody erases it,
which is what distinguishes an assembled record from mail that arrived and ages.

Nothing here is logged. No log line, metric dimension, trace attribute, or failure message carries a name, an address,
or a note; the contact's identifier is what a failure names, and it is the one part of the record that is not personal
data.
