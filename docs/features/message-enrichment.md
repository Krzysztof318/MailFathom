# Message enrichment

<!-- describes: backend/src/Application/Emails/Enrichment/**, backend/src/AI/Enrichment/**, backend/src/Infrastructure/Persistence/Enrichment/**, backend/src/Infrastructure/Persistence/Entities/EmailEnrichmentEntity.cs, backend/src/Infrastructure/Persistence/Entities/EmailEnrichmentMarkEntity.cs, backend/src/Host/Configuration/Chat/EmailEnrichmentOptions.cs, backend/src/Host/Api/ClientMailEnrichmentResponses.cs -->

A subject line says what somebody chose to call a message, which is not the same as what the message is about. So a
mail list drawn from subjects makes a reader open a message to find out whether it needed opening. **Enrichment derives
three short readings of a message once, when it arrives, and stores them** — what it is about, why it may matter, and
any commitment it contains — so a list row carries them without a model call while the row is being drawn.

Deriving them per row would be a model call per message per scroll, on somebody's mail, at somebody's expense. Deriving
them once and storing them is what makes the readings affordable at all, and it is also what makes them checkable: a
mark that was written down can be shown its evidence, and a mark computed for a screen and discarded cannot.

## What a mark is

A mark is never a sentence on its own. Each one carries:

| Part | What it holds |
|---|---|
| **Aspect** | Which of the three readings it is: the *sense*, the *significance*, or a *commitment*. |
| **Text** | The reading, as one plain sentence, at most 240 characters. |
| **Reason** | Why the producer says it, which is what somebody checks the reading against. |
| **Evidence** | The passages of the message the reading rests on, at most four, best first. |
| **Provenance** | What produced it — a deterministic rule or a model — and what within that source it was. |
| **Due date** | When the commitment falls due. Only a commitment may carry one, and only where the message named one. |

A message carries at most one mark of each aspect, so a row drawing the sense has one candidate rather than a choice
between two answers to one question. A reading with no evidence is refused rather than stored: it would read on a
screen exactly like one a passage supports, and no reader could tell them apart.

The evidence names passages in the same terms the Discover run's citations name them, so the same reader resolves both
— following a mark's evidence is reading what [chunking](message-chunks.md) already derived rather than fetching the
message again. A passage the store no longer holds resolves to nothing, which is a mark whose message has been re-cut
rather than a mark that never had any.

**Every mark is written in the language its reader reads, whatever language the message was in.** That language is
[the one their own record names](../operations/configuration-sources.md#the-language-a-user-reads) — `English` or
`Polish` — so a Polish reader's German mail is marked in Polish, and the two readers of one thread read it each in
their own. A name, a subject, or a phrase quoted out of the message stays as it was written, because a quotation that
was translated is no longer evidence of anything.

## A rule's verdict and a model's are different in the data

The provenance is two columns rather than a convention, and that is the point of it. `Source` says whether a
deterministic rule or a model produced the mark, and `Origin` says which — a rule identity, or the name the agent was
composed under. Somebody deciding whether to act on a mark asks what said so before they ask what it says: a rule
re-run over the same message says the same thing, and a model promises no such thing.

No deterministic rule writes a mark today, and the record can express one anyway. A shape that could only ever hold one
of the two would make the source column a constant, and the first deterministic producer would then arrive as a schema
change rather than as a caller.

The column carries the two kinds of producer that exist and no reserved member beside them, because a member nothing
ever writes is a state every reader has to rule out on every row.

## When it runs

Enrichment is the last stage of the [arrival pipeline](../architecture/arrival-pipeline.md), behind the cut — a message
derived before it was cut would have nothing to rest its evidence on. Everything the earlier stages settle is settled
by the time it runs: classification has admitted the message, the user's rules have finished with it, and its passages
exist.

**All of its passages**, which is stricter than having some. A derivation is taken once and never revisited, so a
message reached while its body was still uncut, or while an attachment it names was still unread, would be recorded for
good as a reading of the half that happened to exist. Such a message waits here instead, and the next run takes it once
the stage in front has finished with it. A deployment that reads no attachments waits for none.

It is a step of the account's synchronization run rather than a schedule of its own, for the reason every other pass is
one: that run already has per-account isolation, a slot count, a jittered backoff, and a failure path that defers the
account rather than the process.

One pass derives from at most **eight messages**, reading at most **six leading passages** of each. Both bounds are
constants rather than settings, because they bound the pass's latency rather than describing a deployment — a
derivation is a provider call, and every other account is waiting behind the one holding the slot. The opening of a
message is what says what it is about; a long thread's later passages are quoted history the cut kept because somebody
wrote around it.

*Leading* is read body first. An attachment's passages are numbered from zero alongside the body's rather than after
them, so the number alone puts no order between a body passage and an attachment passage carrying the same one. The six
are therefore taken from the message's own text, and an attachment's passages join only where the body has fewer — on a
message with several attachments the other ordering could have handed a derivation no body text at all, which is the
opposite of the opening it reads a message for.

**No cursor exists and none is needed.** A message leaves the selection by being derived from, so an interrupted pass
repeats nothing and skips nothing. That is also the whole of how an existing mailbox is backfilled: a deployment that
turns enrichment on drains its stored mail over successive runs, eight messages at a time, and each run reports how far
it got and whether more remains. There is no sweep of its own, because there is no mail the account run does not reach.

## What is written down and what is not

Two outcomes are possible for one message, and everything downstream turns on telling them apart.

A **settled** derivation is written down and takes the message out of the queue permanently — including a settled
answer of *no marks at all*, which is the honest answer for a message there is nothing to say about. A **withheld** one
is not written down, and it ends the pass rather than moving to the next message, because every reason a derivation is
withheld outlives one message:

| Withheld because | What it means |
|---|---|
| **Not activated** | The deployment has not turned enrichment on. The pass answers before it looks for work, so nothing is queried, nothing is read, and nothing is sent — and it says nothing each run, being the state every default deployment is in rather than something that happened. |
| **Allowance exhausted** | The answering period has no admission left for another call. |
| **Provider unavailable** | The model failed, timed out, was unreachable, or its credential would not resolve — and where the deployment declared a fallback behind it, that model could not answer either. |

The distinction is what keeps a transient condition from permanently removing a message from the queue. A spent period
today is an ordinary period tomorrow, and the message is still waiting.

A provider that *answered* with something unreadable is the one failure that settles rather than withholds. The call
was made and paid for, and asking again buys the same answer, so the message is recorded as having nothing to say
rather than being offered to the endpoint forever.

## The agent, and what leaves the deployment

The derivation runs as an agent composed the way every agent in this product is: one instruction carried as the agent's
own, one declared tool set, one name, and the registered instruction envelope wrapped around it. It opens no chat call
of its own.

**The tool set is empty, and that is the capability rather than a description of one.** The message is put to the agent
in the turn, so an agent that could look something up would be an agent able to reach mail beyond the message it was
asked about.

What leaves the deployment is the subject, the arrival instant, and the selected passages — nothing else, and every one
of them scanned first by whatever [sensitive-content scanning](sensitive-content-scanning.md) the deployment switched
on. The passages are **numbered in the turn and cited by number**, and the model is shown no identifier at all, so a
citation is a position in a list this deployment composed: an answer naming a passage it was not given names nothing,
and its mark is dropped.

The arrival instant is in the turn because a message saying "by Friday" is resolved against when it arrived rather than
against now, which is what makes the same message derived again next month resolve to the same day.

The message is data rather than an instruction, and the instruction says so. Mail is the most adversarial text this
system reads: a message asking to be marked urgent is a sender writing on a row somebody else's triage depends on.

## What it costs, and turning it on

Enrichment is off unless a deployment turns it on:

```json
{
  "Chat": {
    "Enrichment": {
      "Enabled": true
    }
  }
}
```

It needs a declared chat endpoint, exactly as [mail answering](mail-answering.md) does, and it is admitted against and
charged to **the same period ceilings a question is**. That is what makes it bounded rather than a second, unmetered
way of spending a deployment's allowance — and it is also the trade an operator makes by turning it on, because a
derivation and a question compete for one allowance. A refused admission withholds the derivation rather than failing
it.

Every call runs behind the same provider bulkhead every other outbound AI call does: the endpoint's own circuit,
concurrency limiter, timeout, and health record. Each derivation opens its own credential, transport, chat client, and
agent, and releases all four with it, so a rotated key is picked up by the next message rather than at the next
restart.

## Privacy

A mark is a sentence derived from somebody's mail and inherits its classification whole.

- **Nothing about a mark reaches a log or a span.** What a derivation reports about itself is the endpoint that
  answered, a count of marks, a count of passages, and the reason a derivation was withheld. Never the text, never the
  reason, never the subject.
- **The text and the reason are scanned on the way out**, at the client listing point as well as at the prompt point,
  so a mark quoting a credential a message carried is redacted before it reaches a screen exactly as the preview beside
  it is.
- **Deleting a message deletes its marks.** The record hangs off the stored message with a cascading delete, so an
  erasure removes the readings with the mail rather than leaving them behind.
- **The evidence is published as passage identifiers rather than as text**, because following one is a request of its
  own — a list page carrying the passages themselves would be publishing a body it had no reason to.

## What a client reads

A message's enrichment reaches the client timeline beside its preview, as the derivation instant and the list of marks.
The field is always on the row: it is **`null`** for a message no derivation has reached, and an **object carrying an
empty list** for one a derivation settled with nothing to say. That is how a client tells *not derived yet* from
*nothing to say* without a third field saying which, and both are renderable states rather than failures — a deployment
with enrichment off draws every row exactly as it did before.

It reaches a conversation's rows the same way, because a message is one shape across that surface and a list row and a
message inside a conversation are drawn from the same fields. A conversation answering `null` for a message the list
beside it shows marks for would be stating that no derivation has reached it, which is the one thing that field is for.

## What is not here

- **Rendering it.** What a row draws, how a mark expands into its evidence, and how a commitment appears on a screen
  belong to the client and are not decided here.
- **Acting on a commitment.** Nothing reminds, schedules, files, or replies. A commitment is a reading of the message,
  not a task this deployment took on.
- **A deterministic rule that writes a mark.** The record expresses one; nothing produces one yet.
- **Re-deriving a message.** A settled message is settled. Nothing re-runs enrichment over mail already derived from,
  including after the instruction or the model behind it changes.
