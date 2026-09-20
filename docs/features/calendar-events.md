# Calendar events

<!-- describes: backend/src/Domain/Calendar/**, backend/src/Application/Calendar/**, backend/src/Infrastructure/Persistence/Calendar/**, backend/src/Infrastructure/Persistence/Entities/CalendarEventEntity.cs -->

MailFathom holds a calendar of its own: events a person put there, and dates their mail named that nobody has agreed
to yet, in the same PostgreSQL database the mail is in. This page describes what an event is, the difference between
the two halves of the record, and what writing, accepting, and deleting one does.

**Nothing here reaches an external calendar server.** No CalDAV, no Exchange, no subscription to a published
calendar, and no protocol of any kind: a MailFathom event is one this deployment created or was handed, holdable and
deletable exactly like a contact, and nothing more. That is a decision rather than an omission — synchronizing against
a calendar somebody else owns is a second protocol with its own consent, conflict, and retention questions, and this
deployment answers none of them. Reading an `.ics` file a person chooses is neither of those things and is separate
work; a file read once subscribes to nothing and writes nothing back.

**One surface serves this store: the client endpoint.** [Its calendar routes](../operations/client-endpoint.md#the-calendar-routes)
are where the signed-in person reads a window of their own calendar, reads one event, puts one there, amends one,
accepts a date their mail proposed, and removes one. No MCP tool and no command reaches an event.

**What fills it besides a person is a reading of text.** Where an operator turned it on, an arriving message naming a
meeting is read into proposals on the calendar of everyone the account is assigned to, and a sentence somebody types
into the dialog that creates an event is read into the fields it opens with — both by the agent
[Reading a calendar event out of text](calendar-event-extraction.md) describes.

## What an event is

| Part | What it holds |
| --- | --- |
| Identity | MailFathom's own, assigned once and kept through every acceptance and amendment |
| Title | What the event is called, as whoever wrote it down wrote it |
| Start | When it begins |
| End | When it ends, where anything said so — an event that states none simply lasts no stated time |
| Origin | Whether it is on the calendar or merely offered to it |
| Source message | The message a date was found in, where one was |
| Imported identifier | The `UID` the `.ics` entry it came from named itself by, where a file did |

A duration is not stored beside the end, because the two are one fact stated twice: a writer supplies whichever of
them it has, and the record answers the other from it.

**An end is after the start or there is no end.** An entry naming both where the span is empty or reversed is a file
or a reading that went wrong rather than an event to draw, so it is refused when it is composed instead of being
stored and drawn as a line of no length.

**A title carries no character that renders as nothing.** Three writers reach the value and none is trusted — a person
typing, a reading of a message, and a file somebody was handed — and a title is drawn in a list beside the other
events, so a line break would end the row it is on and a bidirectional override would render the rest of it as text
the record does not contain. Such a title is refused rather than silently stripped, so a person who typed one is told
it was not accepted instead of being shown something other than what they wrote.

## The calendar belongs to a person

Every event is one person's, and every read and write names them. A person assigned two mailboxes has one day, so the
record hangs on the user rather than on a mail account — which is the opposite of [the collected half of the contact
book](contacts.md), where the record is the mailbox's precisely because two people assigned one account correspond
with one set of people.

Two things follow. An identifier learned elsewhere reaches nothing: every statement carries the owner beside the
identity it was given, so an event of somebody else's calendar matches no row rather than being read, amended, or
deleted. And erasing a user takes their calendar with them, through the key the row hangs on rather than through a
step in an erasure that has to know this table exists.

## Asserted and proposed

The origin is two different claims rather than two kinds of event.

| Origin | What it means |
| --- | --- |
| `Asserted` | Somebody put this event on their calendar — typed it, imported it from a file they chose, or accepted a proposal |
| `Proposed` | Something read this date out of mail, and nobody has agreed to it |

**A proposal is not a calendar entry.** It is read where proposals are read, by asking for that half of the window on
its own, and by nothing that answers what a day holds.

**Accepting one changes the origin on the same row.** It is not a second record, which is what keeps the message the
proposal cited, and everything later derived from it, pointing at the event the person actually holds. The time of the
acceptance is what the record then reports as its last amendment, and where it came from — the message, above all —
survives it, because where a date was found stays true once somebody agrees to it.

**Accepting an event already on the calendar is refused.** A second acceptance is a caller acting on a proposal
somebody else already took, and answering it as done would move the record of when the event was actually accepted.

**Dismissing a proposal deletes it.** A date nobody wanted is not a fact worth keeping, so it is the same operation
as deleting an event.

## Amending an event

**An amendment states the event as it is to stand rather than the difference from the one held.** Retitling a meeting,
moving it, and dropping its end are therefore one operation instead of three that each pass through a shape the rules
above refuse — and a caller that sends only what changed is sending a record that is missing the rest.

What an amendment may change is the title, the start, and the end. What it always keeps is the identity, the origin,
the message the event cites, and the identifier it was imported under: an amendment is not how a proposal becomes a
calendar entry, not how an event moves to another calendar, and not how the record of where it came from is edited.
The instant it happened is what the event then reports as its last amendment.

Everything a composition is held to, an amendment is held to again — an end that is not after the start is refused
here exactly as it is when the event is first written, rather than being checked once and trusted afterwards.

## Deleting an event

A deletion removes the row. There is no state a deleted event is in, nothing restores one, and a calendar that
appears to hold nothing at a date holds nothing at it.

## Reading a window

Every view over a calendar is a span — a month, a week, a day, an agenda — so every read is a window, and an event is
in one when any part of it falls inside. A meeting that began before the window opened is therefore still what the day
it runs into shows.

The window is half-open: an event beginning exactly as it closes, or ending exactly as it opens, is outside it. That
is what lets two consecutive days, weeks, or months answer for every event exactly once between them rather than
drawing one of them twice.

What bounds a read is how many events it may answer with rather than how long the span may be — a year of an agenda
is an ordinary request, while the number of rows is what decides the cost of answering. A window is answered earliest
first, with the identity settling two events that begin at the same instant so that reading one window twice answers
the same way twice.

## An imported identifier, and why it is carried

Every entry in an iCalendar file carries a `UID` of its own, and an event imported from such a file keeps the one it
arrived under. It exists for a single question, asked by whoever reads such a file: has this entry already been
imported here. One calendar holds an identifier at most once, and the database is what enforces that rather than a
check before the insert — two imports running at once both read nothing, so only the constraint closes that window.

Two calendars holding one identifier is ordinary rather than a conflict: two people handed the same file each keep
their own copy of what it described.

The value is compared exactly as written. RFC 5545 makes it opaque and gives it no property but equality, so two
spellings are two identifiers to everybody producing them, and folding them together would recognize as a repeat an
entry the file says is a different one. An event a person typed and a date read out of mail carry none at all.

## What the record keeps of a message

An event that came out of a message keeps a pointer to it and nothing else. No subject, no participant, no line of a
body is copied here, so the thread can be opened from the event and the message stays the one copy of itself.

When that message goes — deleted in the mailbox like any other, or erased on somebody's request — the event stays and
its citation resolves to nothing. Everything else derived from a message goes with it, deliberately; an event somebody
accepted is their own plan rather than a derivation, and deleting mail is not something anybody expects to change what
their day holds. What a reader loses is the thread behind the event, which is the honest consequence of the mail being
gone.

## Privacy

A title says who somebody is meeting and the times say when they are not somewhere else, so every part of an event but
its identity, its owner, and its origin is personal data. It is held under the same terms as the mail beside it — the
same database, the same access, the same retention, and no field encryption, which the deployment's own storage
protection covers for both. Nothing is logged, recorded as a metric dimension, or written into a failure message; the
event's identifier is what a failure names.

## What this store does not hold

- **Recurrence.** A repeating meeting is entered as however many single events it needs. Nothing here expands a rule
  into occurrences or stores one.
- **Reminders**, attendees, locations, attachments, and free-or-busy status. None of them is refused as an idea; none
  of them is part of the record today.
- **Any synchronization**, as the top of this page states.
