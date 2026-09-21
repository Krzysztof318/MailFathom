# Reminders

<!-- describes: backend/src/Domain/Reminders/**, backend/src/Application/Reminders/**, backend/src/Infrastructure/Persistence/Calendar/CalendarReminderSchedule.cs, backend/src/Infrastructure/Persistence/Tasks/PersonalTaskReminderSchedule.cs, backend/src/Infrastructure/Persistence/Entities/CalendarEventReminderEntity.cs, backend/src/Infrastructure/Persistence/Entities/PersonalTaskReminderEntity.cs, backend/src/Host/Hosting/Workers/ReminderWorker.cs -->

Two records in MailFathom announce themselves: a [calendar event](calendar-events.md), and the due date on a task in
somebody's own list. This page describes what a reminder is, what it raises and how it comes to be raised exactly
once, and the two things that differ between the two kinds — what a lead is measured back from, and what silences one.

## A reminder is a lead rather than an instant

What somebody chose is *a quarter of an hour beforehand*, and the instant that falls at is derived from the record
every time it is asked for. That is the whole reason moving a record carries its reminders with it: nothing rewrites
what the person asked for, and the new instants follow from the new date.

| Rule | What it is |
| --- | --- |
| The unit | Whole minutes, which is the coarsest unit the client offers and the finest anything acts on — a run looking for what has come due cannot be more precise than the interval it runs on |
| No lead at all | Zero is a lead like any other and means the anchor itself. It is not the absence of a reminder: a record nobody wants to be told about carries none |
| The longest lead | Four weeks, which covers the far end of what somebody sets by hand on a quarterly commitment and bounds how far ahead of now a run has to look |
| How many | Sixteen on one record, above every preset the client offers together, because each one is a notification somebody may be sent |
| Each lead once | A set stating one lead twice is refused rather than folded, that being a caller stating one reminder twice |

**A record carrying no reminder announces nothing, and that is a statement rather than an omission.** Turning the last
one off is an amendment stating the record with none, exactly as amending anything else states the whole record.

## What a lead is measured back from

The anchor is the one thing the two kinds decide differently, and each of them decides it the same way: an hour
somebody named, or nine in the morning on a day they named.

| Record | Anchor |
| --- | --- |
| An event that names a clock time | The instant it begins |
| An event stated as a day | Nine in the morning on that day, read in the offset the event itself carries |
| A task's due date | Nine in the morning on the due day, read in the whole-minute UTC offset the client stated with the leads |

**A day names no hour, so this deployment supplies one.** A person who wrote down a day never chose midnight, and a
reminder an hour before one would arrive in the night before anybody is awake to be told about it. Nine in the morning
is that hour, it is MailFathom's own rule rather than a client's, and it is stated once so that two clients cannot
come to disagree about when the same reminder falls.

**Which offset that hour runs in is the client's to state**, because a calendar event carries its own offset with the
instant it begins while a task carries a day and nothing else — so a client writing a reminder onto one sends the
whole-minute UTC offset that day runs in beside the leads. A request stating no lead states no offset either, and a
task nobody has dated carries neither: a lead has nothing to be measured back from, so stating one against an undated
task is refused rather than stored.

**The zone that offset is read in is the reader's own record**, which is what
[the user record](../operations/client-endpoint.md#the-time-zone-a-persons-days-are-read-in) states and what every date a client draws is
placed in. It is the client that resolves it, because the offset a zone runs at depends on the day, and it is read at
the anchor rather than at the moment of writing — a due day three weeks out may fall on the other side of a
daylight-saving change from today, and today's offset would put every reminder on that task an hour out. A record
stating no zone leaves the machine's own in force, which is the answer a deployment that has never been told anything
about a reader gives.

## What silences one

| Record | What stops it announcing |
| --- | --- |
| Either | Turning its reminders off, deleting the record, or the record ageing past what is still worth saying |
| A task | Completing it — a task somebody finished early is one nobody needs to be told about |

**A completed task is read as completed at the moment the pass runs rather than copied onto its reminders.** So
completing a task and then reopening it needs no write to any reminder, and a task completed after a reminder has
already been announced keeps that announcement — what was said was true when it was said.

## What a reminder raises, and exactly once

A reminder comes due whether or not anybody has a client open, so what raises it is a pass over the deployment rather
than anything a screen does: every minute, one replica takes the lease on the pass and writes a
[notification](../operations/client-endpoint.md#the-notification-routes) for each reminder that has fallen. Every
client then learns about it the way it learns about everything else — the one that was open, the one opened an hour
later, and the second machine.

**One producer rather than one per kind.** The ordering that makes it exactly once, the bound on a pass, the window
that keeps a long outage quiet, and the lease are one decision each, so each kind of record contributes a schedule
that knows its own table and the pass reads every one of them. What a schedule answers with is the same shape for
both: who is to be told, what the record is called, the lead, and the instant it fell at.

**Exactly once comes from two rules rather than one.** The notification is written first and the claim recorded after
it, so a pass that ends between them says nothing twice — the notification's own deduplication names the kind of
record, the record, the lead, and the instant it falls at, so the repeat folds into the statement already standing
unread — and loses nothing either, the reminder still being unclaimed when the next pass reaches it. **The instant is
half of that name rather than a detail of it**: a reminder that has become due at a new time is a different thing to be
told, so it is said even where the statement about the old time has not been read yet, and a deduplication naming only
the record and the lead would swallow it and then claim the reminder anyway. What the claim is recorded against is that
same instant, which is what makes a record moved forward due again at its new time and one moved back onto an announced
time stay quiet. **The kind is part of that name too**, because an identifier is unique within its kind rather than
across the deployment: without it, a task and an event that happened to share one would collapse into a single
statement.

**Nothing long overdue is announced.** A deployment that was off for a day comes back to reminders nobody could have
acted on, about things that have already happened, and delivering them would be a burst of statements in place of the
one thing somebody wanted to be told. An hour is how late is still worth saying; anything older is left where it is and
announced by nothing.

**One pass announces at most two hundred reminders of each kind**, so a deployment whose calendars all name the same
hour does not spend a pass on every one of them; what it does not reach comes due on the next. The bound is per kind
rather than across them, so a backlog of one cannot starve the other.

**What the notification carries is the record's own name and the lead**, and nothing else about it. The name is what
somebody being reminded needs to read first and there is nothing else a reminder is about; the lead travels as a
number, so the client says *fifteen minutes left* in whatever language its reader has rather than reading a sentence
the deployment composed. Following the notification leads to the record — the event, or the task.

## Privacy

A reminder is a number of minutes and an instant derived from it, and holds no text at all. What a notification
carries beside it is the record's own name, which is what the person wrote about their own affairs and is personal
data on the same terms as the record; nothing about a message the record cites reaches one. The pass itself writes
neither a name nor an identifier to a log — it reports how many reminders it announced and nothing more.
