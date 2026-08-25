# Moving the content already in the database into the bucket

<!-- describes: backend/src/Application/EmailContent/Move/**, backend/src/Application/EmailContent/Release/**, backend/src/Host/Api/Content*.cs, backend/src/Host/Configuration/Persistence/ContentMoveOptions.cs, backend/src/Host/Configuration/Persistence/ContentReleaseOptions.cs, backend/src/Host/Hosting/Workers/StoredContentMoveWorker.cs, backend/src/Infrastructure/Persistence/Emails/StoredContentMove*.cs, backend/src/Infrastructure/Persistence/Emails/RetainedContentReleaseStore.cs, backend/src/Cli/Commands/Content/** -->

Selecting `ContentStorage:ObjectStorage` decides where the **next** payload is written and says nothing about the mail
already stored — which for a deployment that has been synchronizing a mailbox for a year is all of it.
[Where a payload is kept](../features/email-content.md#where-a-payload-is-kept) states why: every content row names the
store that holds its own payload, so both backends go on answering and nothing moves on its own.

This page is the operation that moves it. It is an operator's act rather than a consequence of a setting, because it
rewrites where somebody's mail is held, and a deployment must not begin that the first time it is restarted with a new
configuration.

It is **two** acts rather than one, and the difference between them is the whole shape of this page. The copy is
reversible in the only sense that matters: while it runs and after it finishes, both stores hold the mail, so nothing an
endpoint does can cost a deployment a message. The release is what ends that, and it is the one irreversible step here.

## The order of the steps, and what each one cannot be undone from

| Step | What it does | What undoes it |
|---|---|---|
| Select `ContentStorage:Backend` | Decides where the **next** payload is written | Selecting the other backend again |
| `mfctl content move` | Copies every stored payload into the bucket, verifies each, and points its row at the object | Nothing needs to: the database still holds every payload it held |
| `mfctl content release` | Frees the copies the database was holding | **Nothing.** A restore from a backup taken before it is the only way back |

The step in the middle is where a deployment stands for as long as its operator wants it to. There is no deadline, no
interval that ends it on its own, and no background job that reaches the third row of that table. The default is to stay
there until somebody says otherwise.

## What one pass of the copy does

The deployment carries the copy in bounded background passes, one per `ContentStorage:Move:Interval`. A pass walks the
four tables that hold raw MIME in turn — incoming messages, outgoing messages, drafts, and the drafts a repeated send is
composed from — and for each payload it reaches:

1. **Reads the stored bytes** under the same process-wide raw-MIME budget synchronization reads under, so the move waits
   behind ordinary work rather than holding memory beside it.
2. **Checks them against their own row** — the byte length and the SHA-256 digest the row records — *before* anything is
   written. A payload nobody can vouch for never reaches the bucket.
3. **Puts the object**, minting a fresh key exactly as an ordinary write does.
4. **Reads the object back** and checks it against the same length and digest. The endpoint verified the checksum the
   put carried; what the row is about to point at is the store the deployment will read from afterwards, and that is the
   question worth the second request.
5. **Points the row at the object**, records the instant the object was verified, and **leaves the payload column
   exactly as it is**, in one statement. Only an answer that says the row was still database-backed counts as moved.

No database transaction is open across any of that: the endpoint is reached before the row is written, which is
[ADR 0001](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)'s
rule and the same shape an ordinary object-backed write already has.

**A payload that cannot be carried is left in the database, counted, and stepped past.** The position advances past
every payload the pass reached a verdict on, so one message the move cannot vouch for never stands in front of every
message behind it. A payload a restart interrupted mid-copy is the one exception: the position stays on the one before
it, so the next pass carries it from the beginning rather than stepping past a message nobody decided about.

## While both stores hold it

A row the copy has carried names an object and still carries the bytes the database always held. The object is the
authoritative store for it — that is what the row says — and the payload beside it is a retained duplicate, which
[ADR 0017](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md) § 6
admits as the one duplicated state this system has.

**A read resolves the object first and falls back to that copy when the object cannot be vouched for.** Either the
endpoint holds no object under the key the row names, or the object comes back and is not the length and digest the row
records; in both cases the read succeeds from the database and the deployment records that it had to. Refusing over
bytes the deployment still has would be a self-inflicted outage.

Three things happen when it does, and each is worth watching:

- The `mailfathom.mail.content.fallback` counter moves, carrying `object_absent` or `object_mismatch` as its one
  dimension. [Telemetry](telemetry.md#the-move-of-stored-content) lists it.
- A read of an **incoming** message records a durable repair request against that message, marked `ObjectUnreadable`.
  Nothing is wrong with the message; what is wrong is the endpoint, or one object in it.
- The reader gets the message, exactly as if nothing had happened.

**A flat fallback counter is the evidence the release is waiting for.** A deployment whose copy has finished and whose
fallback counter has not moved while its mail was being read is one that has actually been reading from its bucket. One
where the counter moves has a bucket to look at before anything irreversible happens.

**New mail does not wait for any of this.** From the moment `ContentStorage:Backend` selects the object backend, every
payload this deployment stores is written straight to the bucket, whatever the copy is doing and however much of the
mailbox it has reached. Such a payload has no database copy at all and is not part of what a release frees.

## Running it

Five commands, all of them against the [administrative endpoint](admin-endpoint.md).

| Command | What it does |
|---|---|
| `mfctl content move` | Reports the backlog, asks, and writes the copy down |
| `mfctl content move-status` | Reports the backlog, what is still duplicated, where the copy has got to, and what to do next |
| `mfctl content move-pause` | Stops the copy where it is |
| `mfctl content move-resume` | Sets a stopped copy going again from the position it stopped at |
| `mfctl content release` | Frees the database copies, in bounded batches, once nothing is left to carry |

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

## Releasing the copies the database is holding

`mfctl content release` is the last step and the only one that takes anything away. It reports what is still duplicated,
asks, and then sends one request per bounded batch until nothing is left — `--yes` states the agreement up front, which
is what a scripted release needs. Interrupting it stops it between batches: what a batch freed stays freed, the rest is
still there, and running the command again continues.

Three rules govern it, and each of them is a refusal rather than a warning.

**It is refused outright while the database still owns a payload the copy has not carried.** A payload the copy has not
reached is one no object was ever verified for, and freeing the copies of everything else would end the safety of a job
somebody is still in the middle of. The refusal names the backlog; `mfctl content move` is what repairs it, and the four
reasons under [when a payload is left behind](#when-a-payload-is-left-behind) are what to fix first. This is strict on
purpose: a single message the copy refused to carry holds the whole release, which is what makes such a message
something an operator deals with rather than something they scroll past.

**It frees nothing that was verified more recently than `ContentStorage:Release:SafetyInterval`.** The default is
`00:00:00`, which is not the same as freeing anything on its own — the default hold is the operator's own decision, and
nothing frees a copy until they ask. What a positive value adds is a floor beneath that decision: a deployment that
states `7.00:00:00` cannot free anything it has not been reading from the bucket for a week, however emphatically
somebody asks. That is the answer to an operator who discovers a problem after switching, and it is why the interval is
measured from when each object was verified rather than from when the mail was stored. A year is the most a deployment
may state, because a hold wider than that is a mistyped duration rather than a policy; holding a copy indefinitely needs
no setting at all.

**It needs `mailfathom.admin.erase`, not `mailfathom.admin.operate`.** Every other command on this page asks a
deployment to do work; this one asks it to dispose of what it holds, which is the grant [permissions](permissions.md)
allocates to exactly that. A credential that may start the copy cannot end it.

What it does not do is verify anything again. The object was read back and checked against the row's own length and
digest before the row was ever pointed at it, the retention window is where ordinary reads exercise that object for as
long as the operator wants, and the release is the operator's statement that they are satisfied. Its cost is one bounded
read and one narrow update per batch, and it reaches no endpoint at all.

**The recorded length and SHA-256 stay on the row.** A released payload is still checkable against its object, by every
read that resolves it and by anything looking at the schema, which is what keeps the integrity contract the same on both
sides of the release.

## What it costs while it runs

Two ceilings bound one pass of the copy and the interval bounds how often one happens. Between them the deployment
spends most of every interval on synchronization, delivery, and the reads a caller is waiting on.
[Storage, keys, jobs, and logging](configuration-runtime.md#moving-stored-content-into-the-object-backend) holds the
keys and their ranges.

| Key | Default | What it bounds |
|---|---|---|
| `ContentStorage:Move:Interval` | 10 seconds | How long the deployment waits between two passes |
| `ContentStorage:Move:PayloadsPerPass` | 20 | How many payloads one pass reaches |
| `ContentStorage:Move:MaxBytesPerPass` | 64 MiB | How much raw MIME one pass reads, whatever the count says |
| `ContentStorage:Release:SafetyInterval` | `00:00:00` | How long a copy is held after its object was verified, up to a year |
| `ContentStorage:Release:PayloadsPerBatch` | 200 | How many copies one release request frees |

A pass ends on whichever ceiling it reaches first. Raising either, or shortening the interval, moves the mailbox sooner
and leaves less of the deployment for everything else.

What the copy costs the endpoint is two requests per payload — one put and one read-back — and what it costs the
database is one bounded read per payload and one narrow update. What it does **not** cost is a mail server: nothing here
opens an IMAP session, so a move of any length cannot touch a remote `\Seen` flag.

**The database does not shrink when a copy is freed.** Emptying a payload column leaves the space to PostgreSQL's own
reclamation, so what falls immediately is what a new backup has to carry rather than what the volume reports. Reclaiming
the space on the volume is the operator's own maintenance, on their own schedule, and nothing here automates it.

## Reading progress

`mfctl content move-status` answers on any deployment, including one that stores its content in the database and one
that has never been asked for a move — the backlog is exactly the figure an operator weighs before selecting the other
backend.

It reports what the database still owns, what it holds a second copy of, what the copy is doing, when it was asked for,
and what it has carried: payloads moved, the bytes they held, and payloads left behind. Then it says what to do next,
which differs for each answer: no endpoint configured, no move ever asked for, a move stopped, a move that finished with
content still in the database, and a move that finished with copies still to release.

With a metrics backend, the same figures are counters that survive the restarts and pauses a move of a large mailbox
lives through. [Telemetry](telemetry.md#the-move-of-stored-content) lists them and the span one pass publishes, and
[while both stores hold the same payload](telemetry.md#while-both-stores-hold-the-same-payload) lists the fallback
counter that says whether the bucket is answering, beside the released payloads and bytes.

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
everything already object-backed is no longer part of the backlog it walks. Until every one of them is carried, the
release is refused.

## Whether the move can be reversed

**No, and it is not going to be.** There is no command that copies objects back into PostgreSQL, and selecting the
database backend again changes nothing about the rows already pointing at the bucket: each row names its own store, so
an object-backed row goes on being read from the endpoint whatever the setting says. A deployment that goes back to the
database backend writes its *next* payloads there and keeps reading its object-backed ones from the endpoint, which is
what [losing the endpoint is a readiness condition](../features/email-content.md#losing-the-endpoint-is-a-readiness-condition)
is about.

What answers the operator who regrets the switch is the step this page kept apart: **do not release**. Until the release,
every carried payload is in both stores, the deployment reads from the object and falls back to the database, and the
whole change costs a bucket nobody has to trust yet. `ContentStorage:Release:SafetyInterval` is how that hesitation is
written down rather than remembered.

After the release, the objects are the only copy, and a restore of a backup taken before it is the only route back. That
is the trade the third row of the table at the top of this page states, and it is why it is a separate command with a
separate grant and a confirmation of its own.

## What this does not do

- **It does not free anything on its own.** No interval, no finished copy, and no restart releases a retained payload.
  Only `mfctl content release` does, every time.
- **It carries raw content and nothing else.** Metadata, the lexical index, embeddings, and audit records are unaffected
  and stay where they are.
- **It does not shrink the database volume.** PostgreSQL reclaims the space a freed payload leaves on its own schedule,
  and reclaiming it on the file system is the operator's own maintenance.
- **Nothing about the mail reaches a log or a metric.** Every line and every series here carries counts, a state name, a
  refusal reason, and a fallback reason; no subject, address, folder, or fragment of a message reaches any of them, and
  neither the released payloads nor the fallbacks are ever reported as a list of messages.
