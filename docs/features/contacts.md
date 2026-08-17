# Contacts

<!-- describes: src/Domain/Contacts/**, src/Application/Contacts/**, src/Infrastructure/Persistence/Contacts/**, src/Infrastructure/Persistence/Entities/ContactEntity.cs, src/Infrastructure/Persistence/Entities/ContactAddressEntity.cs, src/Host/Api/Contact*.cs, src/Cli/Commands/Contacts/**, src/Cli/Administration/Contacts/** -->

MailFathom holds a contact book of its own: people, the addresses they use, and what an owner recorded about them, in
the same PostgreSQL database the mail is in. This page describes the record and the rules every writer of it obeys —
what identifies a person, when two addresses are the same address, who may change what, and what erasing somebody
removes.

**`mfctl contact` is where the book is maintained**, over the deployment's administrative endpoint; [administering a
deployment](../operations/admin-endpoint.md#administering-the-contact-book) holds the command group in full. The MCP
tools over the book and collection from arriving mail are separate changes, so nothing writes to the book on its own and
an instance nobody has written to holds no contacts at all.

## A contact is a person, not an address

One person uses a work address, a personal one, and an old one they still receive on. A book keyed on the address could
not say those were the same person, which is the whole reason to have a book rather than a list — so the record is the
person and the addresses hang off them.

| Part | What it holds |
| --- | --- |
| Identity | MailFathom's own UUID, minted when the contact is recorded and never derived from an address |
| Name | What the owner wrote, kept in their casing, at most 256 characters and carrying no character that renders as nothing |
| Addresses | At least one and at most 32, each at most 320 characters |
| Preferred address | Which of them to use when something addresses the person without naming which |
| Note | Optional free text, at most 4000 characters, in which line breaks and tabs are kept and every other character that renders as nothing is refused |
| Origin | How the contact came to be in the book — see [§ Two origins](#two-origins-and-who-may-write-which) |
| Recorded, amended | When the contact entered the book, and when it was last changed |

The identity is the book's own and is never an address, because an address is a thing a person has rather than a thing
they are: they give one up, gain another, and stay the same person. It is also the only part of a contact that may
appear in a log line, a metric, or a failure message, beside the origin, which is MailFathom's own classification of how
a record arrived rather than anything the person supplied. Everything else is personal data about a third party.

Which address is preferred is the owner's choice rather than an ordering accident. Nothing picks one, and a record
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

**One address belongs to one contact, across the whole book.** Adding an address a different contact already holds is
refused with an answer naming which contact holds it, so the caller can look at that person rather than guess. The rule
is a unique index in PostgreSQL rather than a check before the write: two callers claiming one address at once both read
that nobody holds it, and only the database closes that window. Losing that race is a conflict the write retries from a
fresh read, and the second caller is then told who holds the address.

Inside one record, two spellings of one address are merged rather than refused. They name the same mailbox of the same
person, and refusing would ask an owner to resolve a difference nothing else makes.

## Two origins, and who may write which

A contact is either **asserted** — somebody wrote this person down — or **collected**, an address that appeared in mail
that arrived. Both live in one book, because searching for somebody should not require knowing which half they are in.
What the difference decides is who may change the record without anybody asking:

- A writer amends the contacts of its own origin and no others. Collection never touches what an owner wrote down, and
  an owner does not amend a collected record in place either.
- **Promotion** is the one crossing, and it runs one way: a collected contact becomes asserted, which is the act of the
  owner taking responsibility for it. It names its writer for the same reason an amendment does, so collection is refused
  the promotion of the record it just created rather than being able to award itself the authority that comes with it.
  Nothing turns an asserted contact back into a collected one, because nothing can unsay that somebody wrote a person
  down. Promoting a contact that is already asserted is answered as such rather than written again.
- Origin is recorded when the contact is created and is never changed by an amendment.

Erasure is deliberately outside that rule. It is the data-subject path, and somebody asking to be removed from a contact
book is not answered with which half of the book they happen to be in.

## Amending a contact states the whole record

An amendment carries the record the owner wants the contact to have — the name, every address, which one is preferred,
and the note — rather than the difference from the one held. Adding an address, dropping one, choosing a different
default, and correcting a name are then one operation whose result is checked against the invariants above, instead of
four that could each leave a contact without an address or with a default it does not hold.

An address the amended record no longer names is removed, which is also what frees it for another contact to claim.

Two amendments of one contact are last-writer-wins, which is what stating the whole record means: the later one is the
record, exactly as it would be a second later. What is not left to that is an amendment racing an **erasure** — the row
carries PostgreSQL's own version token, as
[ADR 0001](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)
requires of every record written in place, so an amendment of a contact somebody erased meanwhile writes nothing and is
answered as a contact the book does not hold rather than putting the person back.

## Reading the book

Two lookups and one listing:

- **By identity**, which is what every other part of the system will name a person by.
- **By address**, which answers "who is this from" and is served from the unique index rather than from a scan. At most
  one contact can answer, which is the uniqueness rule above rather than a property of the lookup.
- **A page of the book**, bounded and continued by a keyset cursor. The order is the name's comparison form and then the
  identity, which makes it total: two people with one name are still served in a fixed order, so a walk of the book
  serves every contact exactly once. A page holds 50 contacts unless the caller asks for fewer, and never more than 200.
  A listing may be narrowed to one origin, which is the question "what did my instance pick up" and its inverse.

The order is taken on a comparison form stored beside the name rather than on the name itself, and the column holding it
is pinned to PostgreSQL's `C` collation, so the order is the ordinal one MailFathom derived the form to produce rather
than whichever collation the database was created with.

## Erasing and exporting a person

A contact book is the most concentrated personal data this system holds: not mail that arrived about somebody, but an
assembled record about identified third parties. Both data-subject paths therefore exist from the first commit rather
than as a later addition, and both are proven by test rather than described.

**Erasing a contact removes them and everything derived from them.** The schema's own foreign key is what guarantees no
address outlives its person, rather than a second statement somebody remembers to write; the erasure takes those rows
first inside the same transaction so it can answer with what it removed — the contact and how many addresses went with
it — and an owner asking for one gets an answer rather than a call that returned without complaint. Erasing somebody the book does not hold is a completed erasure, not
a failure: the state the owner asked for is the state the book is in.

**Exporting a contact produces everything held about them** as of the instant it was taken: the name, every address,
which is preferred, the note, the origin, and both timestamps. What an owner reads is left to the surface that asks for
it, so no surface can choose which parts of a person to hand back.

Both are commands rather than seams something else is expected to reach: `mfctl contact delete` erases and
`mfctl contact export` writes the document, because a data-subject path nothing invokes is one that will not work on the
day somebody asks for it. The erasure asks before it runs and answers with what went — the identity and how many
addresses — and never with the person.

## What the book is held under

Nothing about a contact is held under weaker terms than a message is. It is the same database, the same access, and the
same storage protection as the mail beside it, with no field-level encryption on either — what protects both is the
deployment's own protection of its database. There is no retention window: a contact is held until somebody erases it,
which is what distinguishes an assembled record from mail that arrived and ages.

Nothing here is logged. No log line, metric dimension, trace attribute, or failure message carries a name, an address,
or a note; the contact's identifier is what a failure names, and it is the one part of the record that is not personal
data.
