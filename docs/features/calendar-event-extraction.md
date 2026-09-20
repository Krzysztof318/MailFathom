# Reading a calendar event out of text

<!-- describes: backend/src/AI/CalendarEvents/**, backend/src/Application/Calendar/Extraction/**, backend/src/Host/Api/ClientCalendarEventDraftEndpoint.cs, backend/src/Host/Configuration/Chat/CalendarEventExtractionOptions.cs -->

Two places in MailFathom turn words into a date somebody can act on: a message that arrives naming a meeting, and a
sentence a person types into the dialog that creates an event. Both are the same judgement about the same kind of
words, so both are read by one agent, under one instruction, with one switch in front of them. This page describes
what that reading produces, what it refuses to produce, and what it costs.

What the events themselves are — the record, the difference between one somebody put on their calendar and one that
was merely offered, and what accepting or dismissing a proposal does — is [Calendar events](calendar-events.md).

## The two halves

| Half | What it reads | What it produces | What happens to it |
| --- | --- | --- | --- |
| Mail | One arriving message, as the [enrichment pass](message-enrichment.md) already holds it | Up to four events | Stored as proposals on the calendar of every user the account is assigned to |
| A typed sentence | Up to 500 characters somebody typed, with the instant they typed it at | One event | Answered to the client, stored nowhere |

Neither half puts anything on anybody's calendar. A proposal is a row a person still has to accept, and a drafted
event is fields a dialog fills in that the person then submits themselves — so the worst a wrong reading costs is
something to dismiss, never an appointment somebody finds they are committed to.

**The agent holds no tools.** The text arrives in the turn and the answer is a structure, so there is nothing for it
to look up and nothing for it to write. That is the capability rather than a description of one: an agent able to
search would be able to reach mail beyond the message it was asked about, and an agent able to write would be able to
put an appointment on a calendar.

## What counts as an event

**Only text that fixes an occasion to a day.** Everything else is left alone, and an empty answer is the ordinary
case rather than a failure — an invented event is worse than none, because somebody has to read it in order to throw
it away.

**A deadline is not an event.** A day something has to be *finished* by is a commitment, and putting one in a day's
column tells somebody they are busy when they are not. *Pay the invoice by the tenth*, *send the agenda by Friday*,
and *reply by Monday afternoon* are all commitments, and the [enrichment pass](message-enrichment.md) is what records
them; only *we meet on Tuesday at half past ten* is an occasion.

**A day with no hour is shown as the day.** The event starts at midnight and carries no end, so the person is shown
the day and sets the hour themselves rather than being offered one nobody wrote down.

**An end only where the text says how long it lasts.** No length is invented, and an end that is not after its start
is dropped while the event is kept — what the text fixed was when the thing begins, so the length is the part to
discard rather than the occasion.

## Which clock an hour is in

The model writes a local wall clock and never a zone, and MailFathom supplies the offset. That division is deliberate:
the model is the part that knows *next Thursday* is the twenty-fourth, and the deployment is the part that knows which
offset the text was written in, so neither is asked to guess the other's half.

| Half | The instant every relative day is resolved against, and whose offset an hour is read in |
| --- | --- |
| Mail | When the message arrived, from its own metadata |
| A typed sentence | The instant the client sends beside the sentence, carrying the offset the person is standing in |

Reading the message's own arrival rather than the current time is what makes the same message, read again next month,
resolve *Friday* to the same day it did the first time. A message that carries no arrival instant is read into nothing
at all, because every relative day in it would otherwise be resolved against whenever the pass happened to run.

An answer that carries an offset anyway — a `Z`, a `+02:00`, a day with no time — is refused rather than honoured,
because a model that supplied one guessed it. Refusal here means the event is dropped and whatever else the answer
held is kept.

## Reading mail

The mail half runs inside the [enrichment pass](message-enrichment.md), on the message that pass has just derived, so
it needs no second selection, no second queue, and no second sweep of the mailbox. What it reads is what enrichment
reads: the subject and the message's leading passages, already guarded for whatever the deployment withholds from a
provider.

**It stages its proposals in the same commit as the enrichment record.** A message is therefore never listed as
derived while the events it named were lost, and never proposes events twice because the derivation was written and
the pass then restarted.

**Every user the account is assigned to is proposed to.** A calendar belongs to a person rather than to a mailbox, so
a message reaching an account two people share is offered to both — each on their own calendar, each dismissible
without touching the other's.

**A reading the provider could not answer ends the pass for that run.** The remaining messages stay outstanding and
the next run takes them, rather than being marked derived with no events read out of them. The cost of that choice is
one enrichment call already paid for on the message the pass stopped at.

## Reading a typed sentence

The client asks once whether this deployment reads a description at all, and offers the field only where it does. A
deployment that reads none answers the same way an unreachable provider does, so a dialog is never left with a field
that has quietly stopped working and no way to tell.

What comes back is one event or nothing, and nothing is an ordinary answer: a sentence naming no occasion leaves the
dialog's own empty fields rather than telling the person they made a mistake. The two routes, what they refuse, and
what a refusal carries are in [The client endpoint](../operations/client-endpoint.md).

A spent allowance is the one condition that travels as a refusal rather than as an empty answer, because a field that
has stopped working for the rest of the period is something the person needs to be told about instead of typing into.

## What it costs

**It competes with questions for one allowance.** Every reading is admitted against and charged to the same
`MailAnswering` period ceilings a question is, so a deployment running this beside enrichment and the other
derivations raises those ceilings or accepts that they share them. A refused admission withholds the reading rather
than failing what it was part of.

**Off by default, and off is a supported deployment.** The mail half doubles what an arriving message costs — a second
provider call per message, on top of the enrichment call it runs beside — so it is a spend decision an operator takes
deliberately rather than one a deployment inherits. With it off, nothing proposes an event from mail and the dialog
offers no description field. `Chat:CalendarEventExtraction` in
[Configuring the AI features](../operations/configuration-ai.md#reading-a-calendar-event-out-of-text--chatcalendareventextraction)
holds the keys.

## Privacy

**What reaches the provider is the text and nothing else.** The subject and the leading passages for a message, the
sentence for a description, and the instant each is anchored to. No address, no correspondent's name, no identifier of
the message, the account, or the person, and no part of the calendar they already hold. Everything leaving for a
provider is scanned first by whatever [sensitive-content scanning](sensitive-content-scanning.md) the deployment
switched on, exactly as every other outbound text is.

**Nothing an event says is logged.** A title says who somebody is meeting and a time says when they are not somewhere
else, so neither reaches a log line, a metric dimension, or a failure message. What is recorded is how many events a
reading produced and, where one was withheld, why.

**The text is data rather than an instruction**, and the instruction says so. A message body is the one input anybody
on the internet can write, and a calendar is a place somebody would like to put an appointment nobody agreed to — so
a message asking the agent to ignore what it was told, to change what it is doing, or to reveal its instruction is
read as the text it would be without that. The
[agent evaluation suite](../operations/local-development.md#agent-evaluations) measures that on mail written to do
exactly this, beside the cases that measure what the agent is for.
