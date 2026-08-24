# Moving the content already in the database into the bucket

<!-- describes: backend/src/Application/EmailContent/Move/**, backend/src/Host/Api/Content*.cs, backend/src/Host/Configuration/Persistence/ContentMoveOptions.cs, backend/src/Host/Hosting/Workers/StoredContentMoveWorker.cs, backend/src/Infrastructure/Persistence/Emails/StoredContentMove*.cs, backend/src/Cli/Commands/Content/** -->

Selecting `ContentStorage:ObjectStorage` decides where the **next** payload is written and says nothing about the mail
already stored — which for a deployment that has been synchronizing a mailbox for a year is all of it.
[Where a payload is kept](../features/email-content.md#where-a-payload-is-kept) states why: every content row names the
store that holds its own payload, so both backends go on answering and nothing moves on its own.

This page is the operation that moves it. It is an operator's act rather than a consequence of a setting, because it
rewrites where somebody's mail is held, and a deployment must not begin that the first time it is restarted with a new
configuration.

## What one pass does

The deployment carries the move in bounded background passes, one per `ContentStorage:Move:Interval`. A pass walks the
four tables that hold raw MIME in turn — incoming messages, outgoing messages, drafts, and the drafts a repeated send is
composed from — and for each payload it reaches:

1. **Reads the stored bytes** under the same process-wide raw-MIME budget synchronization reads under, so the move waits
   behind ordinary work rather than holding memory beside it.
2. **Checks them against their own row** — the byte length and the SHA-256 digest the row records — *before* anything is
   written. A payload nobody can vouch for never reaches the bucket.
3. **Puts the object**, minting a fresh key exactly as an ordinary write does.
4. **Reads the object back** and checks it against the same length and digest. The endpoint verified the checksum the
   put carried; what the row is about to point at is the only copy the deployment will read from afterwards, and that is
   the question worth the second request.
5. **Points the row at the object** and empties its payload column, in one statement. Only an answer that says the row
   was still database-backed counts as moved.

No database transaction is open across any of that: the endpoint is reached before the row is written, which is
[ADR 0001](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)'s
rule and the same shape an ordinary object-backed write already has.

**A payload that cannot be carried is left in the database, counted, and stepped past.** The position advances past
every payload the pass reached a verdict on, so one message the move cannot vouch for never stands in front of every
message behind it. A payload a restart interrupted mid-copy is the one exception: the position stays on the one before
it, so the next pass carries it from the beginning rather than stepping past a message nobody decided about.

## Running it

Four commands, all of them against the [administrative endpoint](admin-endpoint.md).

| Command | What it does |
|---|---|
| `mfctl content move` | Reports the backlog, asks, and writes the move down |
| `mfctl content move-status` | Reports the backlog, where the move has got to, and what to do next |
| `mfctl content move-pause` | Stops the move where it is |
| `mfctl content move-resume` | Sets a stopped move going again from the position it stopped at |

`mfctl content move` returns as soon as the move is recorded. The passes are the deployment's own background work, so
closing the terminal stops nothing and the answer is immediate however much mail there is. It asks first — `--yes`
states the agreement in the command, which is what a scripted move needs — and a deployment naming no object-storage
endpoint is refused outright rather than given a move that would carry nothing.

Asking twice is asking once: a move already running or paused is answered with itself rather than started over, so a
second operator's request never discards the position the first one stopped at.

**Pausing cancels nothing.** The pass that is running reads the decision between payloads, so it finishes the one
payload it holds and ends there, which is why stopping is immediate and costs nothing. Resuming continues from the committed position rather
than from the beginning.

**Progress survives a restart**, because it is a row rather than a process's state: the payload kind the walk is on and
the identity it reached are committed with the counts at the end of every pass. A deployment restarted mid-move resumes
where it was and does not re-copy what it verified.

## What it costs while it runs

Two ceilings bound one pass and the interval bounds how often one happens. Between them the deployment spends most of
every interval on synchronization, delivery, and the reads a caller is waiting on.
[Storage, keys, jobs, and logging](configuration-runtime.md#moving-stored-content-into-the-object-backend) holds the
keys and their ranges.

| Key | Default | What it bounds |
|---|---|---|
| `ContentStorage:Move:Interval` | 10 seconds | How long the deployment waits between two passes |
| `ContentStorage:Move:PayloadsPerPass` | 20 | How many payloads one pass reaches |
| `ContentStorage:Move:MaxBytesPerPass` | 64 MiB | How much raw MIME one pass reads, whatever the count says |

A pass ends on whichever ceiling it reaches first. Raising either, or shortening the interval, moves the mailbox sooner
and leaves less of the deployment for everything else.

What the move costs the endpoint is two requests per payload — one put and one read-back — and what it costs the
database is one bounded read per payload and one narrow update. What it does **not** cost is a mail server: nothing here
opens an IMAP session, so a move of any length cannot touch a remote `\Seen` flag.

**The database does not shrink as the move runs.** Emptying a payload column leaves the space to PostgreSQL's own
reclamation, so what falls immediately is what a new backup has to carry rather than what the volume reports.

## Reading progress

`mfctl content move-status` answers on any deployment, including one that stores its content in the database and one
that has never been asked for a move — the backlog is exactly the figure an operator weighs before selecting the other
backend.

It reports what the database still holds, what the move is doing, when it was asked for, and what it has carried:
payloads moved, the bytes they held, and payloads left behind. Then it says what to do next, which differs for each
answer: no endpoint configured, no move ever asked for, a move stopped, or a move that finished with content still in
the database.

With a metrics backend, the same figures are counters that survive the restarts and pauses a move of a large mailbox
lives through. [Telemetry](telemetry.md#the-move-of-stored-content) lists them, and the span one pass publishes.

## When a payload is left behind

Four reasons, each published as a value of the refusal counter's one dimension, and each asking something different:

| Reason | What it means | What to do |
|---|---|---|
| `source_mismatch` | The stored bytes disagree with the length or digest on their own row | Re-synchronize that mailbox; nothing was written to the bucket |
| `object_mismatch` | The object read back is not the payload the row describes | Look at the endpoint; the row still points at the database |
| `object_absent` | The object could not be read back at all | Look at the endpoint |
| `oversized` | The payload is larger than `MailSynchronization:MaxInFlightRawMimeBytes` | Raise that ceiling and move again |

In every one of them the row stays database-backed and readable. Nothing is lost and nothing is half-written: the row is
repointed only after a copy has been read back and vouched for.

A move that reached the end of the content with payloads left behind reports itself as finished, and `move-status` says
so rather than leaving the two figures to be reconciled. **Asking for another move walks what the last one left**, which
is how those payloads are reached once the reason has been repaired — the walk starts again at the first kind, and
everything already object-backed is no longer part of the backlog it walks.

## What this does not do

- **It does not read from both stores during the move.** It does not have to: a row is repointed only once its object is
  vouched for, and every read resolves the backend from the row it is reading.
- **It does not release the PostgreSQL payloads afterwards** beyond emptying the column the move wrote through.
- **It carries raw content and nothing else.** Metadata, the lexical index, embeddings, and audit records are unaffected
  and stay where they are.
- **It never moves anything the other way.** A deployment that goes back to the database backend keeps reading its
  object-backed rows from the endpoint, which is what
  [losing the endpoint is a readiness condition](../features/email-content.md#losing-the-endpoint-is-a-readiness-condition)
  is about.
- **Nothing about the mail reaches a log or a metric.** Every line and every series here carries counts, a state name,
  and a refusal reason; no subject, address, folder, or fragment of a message reaches any of them.
