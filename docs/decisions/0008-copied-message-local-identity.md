---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-08
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Store a copied message as a second local email, and leave the occurrence the only identity a stored row carries

<!-- describes: backend/src/Application/Synchronization/MailboxSynchronizer.cs, backend/src/Application/Mail/Mutations/IMailboxMutationReconciliationStore.cs, backend/src/Domain/Mutations/MailboxMutationRequest.cs -->

## Context and Problem Statement

[ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) permits MailFathom to copy a message into a second folder, and deliberately left what that means locally to the action rather than to the session that can issue the command. A relocation ends with one live occurrence, so *which local email is this* has one honest answer. A copy ends with two, in two folders, both real and both synchronized — and a stored email row **is** an occurrence, keyed by account, folder, `UIDVALIDITY`, and `UID` under a unique index, with no email identity above it that two occurrences could share.

Issue 476 asks the question the action cannot be written without: when MailFathom copies a message into a second folder, is the result one local email with two remote occurrences, or two local emails that happen to share their content? Provenance is not what is undecided. The durable record from issue 448 already tells a copy MailFathom performed from one the owner made by hand, and issue 449 already joins a discovered occurrence to the record that placed it. What is undecided is what a row means once two of them describe the same words.

## Decision Drivers

- **The protocol offers no identity above the occurrence.** `UID COPY` puts a new message in the destination folder with its own UID, its own flags, and, on some providers, rewritten headers. One mail living in two folders is a claim MailFathom would be making, not one the server makes.
- **Whatever is decided has to hold for the copies MailFathom did not make.** A mailbox owner copying a message in their own client is the ordinary case and leaves no record behind. A model that is true only of MailFathom's own copies answers *is this the same mail* sometimes, which is a worse contract than answering it never.
- **Guessing that identity is already refused.** Joining a discovery by `Message-ID` or by a content digest is wrong in both directions — a message legitimately appears twice under one `Message-ID`, and a provider may rewrite headers on copy — which is why a placement the server did not name stays visibly unjoined instead of being matched to something that looks right.
- **Derived data is not free, and part of it is paid for.** A stored email carries raw MIME, extracted text, a search document, passages, and one vector per passage per active profile. A second row derives every one of them again, and the vectors cost money per unit of mail.
- **A link nothing reads is personal data kept for no purpose.** Both rows describe a person's correspondence, so a stored relation between them needs a purpose before it needs a column.
- **Erasure must reach every copy and destroy no more than was asked.** The cascade from a stored email is what makes erasure structural rather than a rule somebody has to remember, and any identity chosen here has to keep that true without making the removal of one folder's copy remove a message the mailbox still holds elsewhere.

## Considered Options

1. **Two independent local emails.** The copy is discovered by synchronization and stored like any other message; the mutation record settles only whose act the arrival was.
2. **One local email with two remote occurrences.** An email identity above the occurrence, which today does not exist: a new table, a migration, and every query, projection, and deletion path that assumes one row per email revisited.
3. **Two local emails joined by a recorded copy relation.** The occurrences stay independent rows, and the record that already says one came from the other is projected onto them.

## Decision Outcome

Chosen option: **two independent local emails**, because it is the only one of the three that describes the owner's copies and MailFathom's own identically, and because the two that model a shared identity buy that identity only for the copies MailFathom itself performed.

### A copy is discovered, never carried

The destination folder's own forward pass meets the copied message and stores it as it stores any discovery: it fetches the content without setting `\Seen`, reads the MIME, writes its own row, and derives its own extracted text, search document, passages, and vectors from it. Nothing is carried across. The source row stays exactly where it was, keeps its occurrence, its flags, and everything derived from it, and the copy's own record settles nothing about it — which is the opposite of a relocation, where carrying the local row onto the new occurrence is what takes the email out of the source folder locally.

What the record settles for a copy is one thing: whose act the arrival was, so that a rule does not react to a message MailFathom has just filed.

### The truthful-looking model is only truthful about MailFathom's own copies

Option 2 reads as the honest one — one message, one search hit, one set of derived data — and it can deliver exactly that for a copy joined to a record, which means a copy MailFathom performed against a server that answered with `COPYUID`. For the copy the owner made by hand in their own client, the discovered message is joined to nothing, so under option 2 it becomes a second local email precisely as it does here, unless MailFathom guesses the identity from a header or a digest, which the driver above refuses and the synchronization behavior already refuses in the one place it would matter.

So option 2 pays for an email identity above the occurrence, a migration, and a revisit of every read, projection, and deletion path keyed on one row per email, and still answers *is this the same mail* only about its own work. That is the objection the issue raises against option 3, at a considerably larger price. Option 3 is the same partial answer without the price, and is refused for the simpler reason: nothing would read the relation, and a relation nothing reads is a column of personal data kept for a purpose that has not arrived.

### What this means for search, retention, erasure, and export

- **Search.** A message copied into a second folder is returned twice, once per folder, and each hit names the folder it was found in. Lexical and semantic ranking treat the two as separate documents, because that is what they are locally; a caller that scopes its query to one folder sees one. An answer that cites either is citing a message the mailbox really holds at the place it says.
- **Retention and storage.** Everything is stored twice — the raw MIME, the extracted text, the search document, the passages, and one vector per passage per active profile. Copying is therefore a storage decision as much as a filing one. The content store deliberately does not deduplicate identical bytes across rows: sharing one payload between two rows would couple their lifetimes and turn erasing one into a reference count, which is a larger change than the duplication it saves.
- **Erasure and restriction of processing.** A data-subject request reaches rows, and both copies are ordinary rows that any selection over sender, recipient, subject, or content reaches; neither hides behind the other's identity, and the cascade from each stored email reaches its own chunks and vectors. Removing one copy leaves the other, which is correct rather than incomplete: the mailbox still holds it, and MailFathom mirrors the mailbox. Erasing the message everywhere is the same act performed against each occurrence, exactly as it is for a message that arrived twice on its own.
- **Access and export.** An export lists both copies, each with the folder it was found in. That is what the mailbox contains, and collapsing them would export something the server does not hold.

### The record keeps its one job, and the gap in it is named rather than discovered

The mutation record stays what issues 448 to 451 made it — the idempotency key that stops a copy being issued twice, and the provenance that withholds MailFathom's own arrival from rule evaluation. It gains nothing here: no relation written onto the new row, no identity spanning the two, because nothing would read one.

One consequence of that is worth stating plainly. The suppression depends on the placement the server named. Where the destination folder answers with `COPYUID`, the arrival is joined to the record and withheld. Where the server advertises no `UIDPLUS`, the copy still happens and the record still stops it being repeated, but the arrival is a discovery like any other and reaches rule evaluation as one. ADR 0007 chose to report an absent `COPYUID` as itself rather than to search the destination folder for something that looks right, and this is what that choice costs. Nothing loops over it today, because no rule engine can ask for a copy yet; a rule that copies, on a server without `UIDPLUS`, is what turns this into a decision to revisit.

### Consequences

- Good, because a stored row keeps exactly one meaning — one occurrence the server holds — and no read has to ask which of several rows is the canonical one.
- Good, because a copy the owner made by hand and a copy MailFathom performed are the same thing locally, so no behavior depends on who filed the message.
- Good, because no query, projection, index, or deletion path changes and no migration is needed: the schema already says what a copy is, and the code already stores one that way.
- Good, because erasure stays structural. Two rows are two cascades, and neither has to be reasoned about in terms of the other.
- Neutral, because provenance is untouched: the record still tells the two apart, which is what rule evaluation needs and all it needs.
- Bad, because a copied message is two search results, and a caller that searches across folders sees the same words twice.
- Bad, because everything derived is derived twice, including the vectors somebody pays for.
- Bad, because *show me this message wherever it is* has no answer, and giving it one later means an email identity above the occurrence, a migration, and revisiting every read that assumes one row per email. What such a decision would be built from is already stored — `internet_message_id` on each row, and the copy's own durable record — for the copies MailFathom performed; for the owner's own copies it would still need an identity nobody can derive without guessing.

## Validation

- Unit tests over the synchronizer require a discovery a copy placed to be stored as a second email with nothing carried across and the source row untouched, and require the arrival to be withheld from rule evaluation.
- A unit test requires a copy whose placement the server never named to be stored as an ordinary discovery whose arrival is raised, which is the behavior the paragraph above describes rather than an accident of the join failing.
- The integration suite proves that a copy interrupted after the command went out converges to exactly one message in the destination folder, against a server advertising `UIDPLUS`, so the `COPYUID` path is the one exercised.
- [IMAP synchronization](../features/imap-synchronization.md) carries what an operator reads about it, and [the stored email schema](../architecture/stored-email-schema.md) carries what the duplication costs at rest.

## Pros and Cons of the Options

### Two independent local emails

- Good, because it is what the schema and the synchronization path already say, so it needs no migration and no revisit of the read model.
- Good, because it treats every copy alike, whoever made it.
- Neutral, because the duplication it accepts is the duplication the mail server itself performed; the second message is genuinely a second message on the server.
- Bad, because search returns the message twice and everything derived from it is derived and paid for twice.
- Bad, because two rows describing one conversation have no recorded relation, so deleting one says nothing about the other.

### One local email with two remote occurrences

- Good, because it is the model that would make a copy behave like the same mail in two places: one search hit, one set of derived data, one thing to erase.
- Neutral, because the schema change is additive and permitted below `1.0.0`; what makes it expensive is the reads, not the migration.
- Bad, because it delivers that only for copies joined to a record, and the owner's own copies stay two emails unless identity is guessed from a header or a digest — which is refused for being wrong in both directions.
- Bad, because every query, projection, index, retention path, and deletion path is written against one row per email, and each would have to be revisited to decide which of them means occurrence and which means message.
- Bad, because it invents an identity the protocol does not supply, so a disagreement between MailFathom's model and the server's is possible in a way it is not today.

### Two local emails joined by a recorded copy relation

- Good, because it is cheap: the occurrences stay independent rows, and the record already says one came from the other.
- Neutral, because it forecloses nothing — the same relation could be derived later from the records already stored.
- Bad, because nothing reads it, so it is personal data written for a purpose that has not arrived.
- Bad, because the relation is only as complete as the copies MailFathom itself performed, so *is this the same mail* is answered sometimes, which is the worst of the three contracts.

## More Information

- Issue 476 asks the question and carries the options this record decides between; issues 447 to 451 built the session, the record, the join, the suppression, and the convergence it depends on, and issue 452 is the feature all of it belongs to.
- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) permits the copy, refuses searching the destination folder for an unnamed placement, and states why a copy is never reissued.
- Revisit when a rule engine can ask for a copy — above all against a server without `UIDPLUS`, where the arrival is not withheld — when *show me this message wherever it is* is actually asked for, or when the duplicated derived data, and the paid vectors in particular, becomes a measured cost rather than an accepted one.
