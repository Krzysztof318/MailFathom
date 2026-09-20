# Tasks read out of mail

<!-- describes: backend/src/AI/DayLayout/**, backend/src/Application/Tasks/MailDerivedTaskProposals.cs, backend/src/Application/Tasks/TodayLayout.cs, backend/src/Application/Tasks/IDayLayoutPlanner.cs, backend/src/Application/Tasks/DayLayout*.cs, backend/src/Application/Emails/Enrichment/EmailTaskProposal.cs -->

A message that asks somebody to do something and a list of what they have to do are the same fact written twice, and
the second copy is the one they make by hand. This page describes the two things MailFathom does about that: it
**offers** a task when a message asks for one, and it **arranges** the tasks due today around what the day already
holds when somebody asks it to.

Neither of them decides anything. A proposal waits on the half of the list nobody has agreed to, and an arrangement is
drawn on a screen and forgotten unless the person acts on it — so mail can put something in front of a reader and can
never put it in their day. What the list itself is, and what accepting or completing a task does, is
[the record](../architecture/stored-email-schema.md#what-a-person-owes) and
[the routes that serve it](../operations/client-endpoint.md#the-task-routes).

## A message proposes, and never commits

Reading a message for what it asks of its reader happens inside
[message enrichment](message-enrichment.md#the-same-reading-offers-a-task), in the call that derives the marks and in
no call of its own. What that reading produces is at most three lines per message — a title, and the day it is due on
where the message named one — and each of them is written onto the list of every person the mailbox is assigned to,
under the origin that says nobody has agreed to it.

**What separates a proposal from a task is who put it there**, which is why it is a column on the row rather than a
second table: accepting one is a single state change on a row that already exists, so the identity a client is holding
stays the identity it was holding, and nothing about the task has to be carried across from one place to another.

**Turning enrichment on is the whole of what turns this on.** There is no key of its own, no second call, and no
ceiling beside the one a derivation is already admitted against —
[`Chat:Enrichment`](../operations/configuration-ai.md#message-enrichment--chatenrichment) is the switch, and a
deployment that derives nothing proposes nothing.

**The title is the reader's to fix.** It is written as the model phrased it, collapsed to single spaces and cut to what
the list holds, in the language the mailbox is read in — the same language the marks are written in, and for the same
reason. A proposal somebody accepts is a line they have read; one they dismiss costs them a glance.

## Arranging a day is asked for, once, and applies nothing

The other half runs when a person presses the control that asks for it, and at no other time. Nothing schedules it,
no pass reaches it, and opening a screen does not run it.

What it is decided from is four things and nothing else: the window the client states, the tasks that person owes by
the end of it, what they are already committed to during it, and how long a placement may be. The tasks are read
through the same reading their own list is drawn with, and the commitments through the calendar's own reading, so a
day is arranged out of exactly what their screens would have shown — and there is no way here to name somebody else's
list or somebody else's calendar.

| What is read | Which of it |
|---|---|
| Tasks | The person's own, on the half of the list they committed to, not yet completed, due on or before that day, soonest due first, at most 20 |
| Commitments | The events on their own calendar during the window — not the dates their mail proposed — at most 50 |
| The day | The two instants the client stated, at most 48 hours apart, because this deployment keeps no timezone for a person |

**What comes back is an offer.** Each placement names a task by identity, when it is suggested to begin, and how long
it is suggested to take; beside them stands the list of tasks that do not realistically fit the day, so somebody is
shown what was left out rather than told about it afterwards. A placement falls inside the window that was asked about
and lasts between 5 minutes and 8 hours, a task stands in at most one of the two lists, and an arrangement naming a
task nobody asked about is dropped rather than drawn.

**Nothing is written by asking.** No task is rescheduled, no due day is rewritten, no proposal is accepted, and no
event reaches a calendar — each of those is an act of the person's own through the route that owns it, and laying a
day out twice leaves the same two lists it started with.

**A day with nothing owed is answered without a call at all**, because the only arrangement of nothing is two empty
lists and asking a provider for it would spend a person's allowance on a question with one answer.

## Why a model arranges it

Fitting work into gaps is arithmetic, and how long something takes is not. Nothing here records a duration, an effort,
or a priority, so *what realistically fits before the 3 pm meeting* is a judgement read out of the lines somebody wrote
— which is what the agent is for, and what a sort by due date could not produce. The bound it is given is the one the
code can state: a placement's window, the day's edges, and which tasks exist.

The agent is composed the way every agent in this product is — one instruction carried as its own, an empty tool set,
one name, and the registered instruction envelope around it — and it opens no chat call of its own. The tool set is
empty for the reason enrichment's is: the day is put to it in the turn, so an agent that could look something up would
be an agent able to reach a list it was not asked about.

**What leaves the deployment is the day.** The window, the lines of the tasks owed by the end of it, and the titles and
times of what is already committed during it. No message, no address, no identity of anything, and nothing about a task
beyond the line it is drawn with — the answer names tasks by identity because the identities were never sent, they were
numbered. Every line is scanned first by whatever
[sensitive-content scanning](sensitive-content-scanning.md) the deployment switched on, under the posture of the person
whose day it is.

**It is admitted against and charged to the same period ceilings a question is**, and it runs behind the same provider
bulkhead every other outbound call does. [`Chat:DayLayout`](../operations/configuration-ai.md#arranging-a-day--chatdaylayout)
is what routes it to a model of its own; a deployment that declares no chat endpoint arranges no day, and the client is
told so before it offers the control.

## When no arrangement is offered

| Withheld because | What it means |
|---|---|
| **Not activated** | The deployment declared no model to arrange a day with. Nothing is read to say so — not the list, not the calendar. |
| **Allowance exhausted** | The answering period has no admission left for another call. |
| **Provider unavailable** | The model failed, timed out, was unreachable, or its credential would not resolve — and where a fallback was declared behind it, that model could not answer either. |

A provider that *answered* with something unreadable is not a withholding: the call was made and paid for, and asking
again buys the same answer, so the day is answered with an arrangement of nothing rather than with a failure nobody can
act on. A day the arrangement genuinely had nothing to place is that same answer, which is why a client draws both the
same way. What the two routes make of each of these is
[the day-layout routes](../operations/client-endpoint.md#the-day-layout-routes).

## Privacy

- **A proposal is derived personal data and inherits the message's classification whole.** What is stored is the title,
  the day, and the identity of the message it was read out of — never a subject, a body, or an address.
- **A task outlives the mail it cites**, deliberately: the citation is a plain value rather than an association, so
  erasing the message leaves the task standing and its citation resolving to nothing. Somebody still owes the thing.
  [The record](../architecture/stored-email-schema.md#what-a-person-owes) holds that
  reasoning, the absence of a retention bound, and what a data-subject erasure reaches instead.
- **Nothing about a day reaches a log or a span.** What an arrangement reports about itself is the endpoint that
  answered, how many tasks it was given, how many it placed, and the reason one was withheld. Never a title, never a
  time, never whose day it was.
- **An arrangement is never stored.** It exists for the length of the response, which is also why nothing has to be
  erased when somebody changes their mind.

## What is not here

- **Accepting anything automatically.** No pass, no schedule, and no arrangement moves a task onto the committed half
  of a list. A person does that, or it does not happen.
- **Reminding, notifying, or chasing.** A due day is a column; nothing watches it.
- **Writing an arrangement onto a calendar.** A placement is a suggestion about a day, not an event — putting one on a
  calendar is [a calendar act](calendar-events.md) somebody takes.
- **Any external task protocol.** The list is native to this deployment in the strong sense: nothing synchronizes, and
  nothing is exported to a task manager.
- **Re-reading a message for tasks.** A settled derivation is settled, including after the instruction or the model
  behind it changes.
