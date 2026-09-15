# Carrying a mailbox out of this deployment

<!-- describes: backend/src/Application/Mail/Export/**, backend/src/Domain/Exports/**, backend/src/Host/Api/MailboxExport*.cs, backend/src/Host/Configuration/Persistence/MailboxExportOptions.cs, backend/src/Host/Hosting/Workers/MailboxExportExpiryWorker.cs, backend/src/Infrastructure/ObjectStorage/S3MailboxExportArchive*.cs, backend/src/Infrastructure/ObjectStorage/ObjectBodyReadStream.cs, backend/src/Infrastructure/Persistence/Exports/**, backend/src/Cli/Commands/Exports/**, backend/src/Cli/Administration/Exports/** -->

An export is **a copy of what MailFathom stores for one mail account, in a form somebody else's software reads**: a
Maildir tree in a zip archive, one Maildir per folder, each message a file holding the stored bytes exactly, the
standard flags in the file names, and the keywords beside them in a document of their own. It is available for every
account, because what it carries is what this deployment stored rather than what a server still holds.

It matters most for an account whose mailbox MailFathom is the only holder of. A drained account's source is empty by
design — [ADR 0034](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md)
is that decision — so there is no server to fall back on, no second copy to reconcile against, and no way to get the mail
out of this installation except the one on this page. That is the whole reason the export exists, and it is why the
obligations under [What an operator owes a mailbox nothing else holds](#what-an-operator-owes-a-mailbox-nothing-else-holds)
are stated here rather than left to be worked out after something has gone wrong.

## What it needs before it will run at all

**An object backend.** An archive is a second full copy of the mailbox for as long as it is kept, and a database row is
the wrong place for one — so a deployment holding its mail content in PostgreSQL refuses the export outright and names
`ContentStorage:ObjectStorage` in the refusal. [Moving stored content into the bucket](moving-stored-content.md) is how
a deployment that has been running on the database backend gets one; selecting the backend is enough for the export
itself, because the archive is written into the bucket rather than composed from what is already there.

**Room for it.** The measurement is checked against `MailboxExport:MaximumArchiveByteLength` and against the
stored-content ceiling the deployment configured, both before any work is queued. A mailbox past either is refused with
the figure and the bound, and exporting one folder at a time is the answer that needs no configuration change.

The size limit is asked again while the archive is written, before each message goes into it, because a mailbox grows
between the measurement and the job. An export that would cross the bound stops at the message that would cross it and
fails with the figure it would have reached, so nothing past the limit is ever sent to the bucket.

**The grant.** Every route and every command here is published under `mailfathom.admin.export`, which no reading grant
confers. [Permissions](permissions.md) says why it is a name of its own.

## The steps

```bash
mfctl export measure  --account work                          # what it would carry, per folder and in total
mfctl export start    --account work                          # records the export and queues the work
mfctl export status   --account work                          # every export this account has, newest first
mfctl export download --account work --export <id> --output mailbox.zip
mfctl export delete   --account work --export <id>            # once the archive is safely somewhere else
```

`--folder <alias>` narrows the measurement and the export to one folder, which is the answer to a mailbox past the
configured size limit. `--yes` agrees to `start` without being asked, which is what a scripted run needs.

**Measuring reads no mail.** The figures are summed from the lengths the database already records, so the answer comes
back in seconds even for a mailbox of years, and starting an export shows them again before it asks.

**Starting one queues a job and returns.** Nothing is held open for the length of a mailbox: the response is a record to
follow. `mfctl export start` measures first and asks before it commits, which `--yes` skips.

**An account writes one archive at a time.** Asking again for the same scope answers with the export already running
rather than starting a second; asking for a different scope while one is running is refused.

**Following it reports progress.** The message and byte counts advance as the archive is written, about once every
hundred messages, so a long export is visibly moving rather than merely not finished.

**Cancelling — `mfctl export cancel --account work --export <id>` — stops it at the next checkpoint**, so within a
hundred messages rather than at the end of the mailbox, and deletes whatever had been written. The archive never existed
as far as anything outside the job is concerned: an abandoned write leaves nothing under its key.

**Downloading streams.** The archive is served straight from the object store as `application/zip`, and `mfctl export
download` writes it to disk as it arrives, so neither the deployment nor the terminal holds a multi-gigabyte file in
memory. A download already begun is not cut off by the retention period ending.

**The file it writes must not already exist**, and a file that does is left exactly as it was — very likely an earlier
export of the same mailbox, which for a drained account is the only copy of it there is. A download that fails part way
removes what it had written, because a partial zip still opens and lists entries.

**Deleting is the operator's, and repeating it is safe.** Deleting one already gone succeeds, because the caller asked
for a state the deployment is already in — which is what makes *download, verify, delete* a sequence a script can run.

## What is inside the archive

| Path | What it is |
| --- | --- |
| `cur/`, `new/`, `tmp/` | The inbox, which is the root Maildir of the tree |
| `.Projects.MailFathom/cur/…` | Every other folder, as one Maildir++ directory at the root named `.` followed by its path segments joined by `.` |
| `…/cur/1700000000.7.mailfathom,S=4096:2,FS` | One message: the stored bytes exactly, under a name carrying its arrival, its length, and its flags |
| `keywords.json` | The keywords Maildir file names have no form for, each named against the path of the message carrying it |

The four flag letters are the standard ones — `D` draft, `F` flagged, `R` answered, `S` seen — written in ascending
order so one message's name does not depend on how the flags were read.

**A folder name never reaches a path as it was written.** It was typed by a person or chosen by a remote server, so each
segment is written as its UTF-8 bytes with every byte outside ASCII letters, digits, `-`, and `_` percent-encoded. A
segment of `.` or `..` becomes `%2E` or `%2E%2E`, a `/` becomes `%2F`, and no folder directory can be named `cur`, `new`,
or `tmp`, because every one of them begins with `.`. Every entry is a relative path under the archive root; none is
absolute and none is a link. A server importing the tree reads the encoded name as written, which is the price of an
archive no extractor can be led outside of.

**The file names carry nothing about this deployment.** The host field of a Maildir name is the constant `mailfathom`
rather than the machine's own name, and the archive is offered for download as
`mailfathom-export-<export id>.zip` — no account identifier and no folder path, because a file name travels into a
downloads directory, a shell history, and a backup index.

## What it costs while it runs

Reading every stored payload of a mailbox once is the most expensive thing this deployment does to itself. The job holds
one message at a time — the walk hands out identities, the content store answers one payload, the archive writer writes
it through and lets it go — so the cost is in reads and bandwidth rather than in memory, and nothing else stops while it
runs.

The archive is uploaded in parts as it is produced, so the deployment never holds a whole one either. Compression is the
cheapest level on purpose: mail is mostly text and compresses well at it, while an attachment is usually already
compressed and gains nothing at any level.

An attempt that stops — a shutdown, a lost lease — abandons the write and returns the export to the queue, and the next
attempt writes it from the beginning. A zip has one directory at its end, so there is no half-written archive to resume
into, and nothing is ever served from one.

## How long an archive is kept

`MailboxExport:Retention` — 48 hours by default — is how long a finished archive stays downloadable. One replica sweeps
for archives that have come due every `MailboxExport:ExpirySweepInterval`, records the expiry, and then deletes the
object; a deployment that was down for a day deletes on its next pass everything that came due while it was.

**The retention period is a storage decision.** For as long as an archive is kept, the bucket holds that mailbox twice.
[Storage, keys, jobs, and logging](configuration-runtime.md#mailboxexport) holds the three keys and their bounds.

## What an operator owes a mailbox nothing else holds

A drained account's mail exists in this deployment and nowhere else. Four obligations follow, and none of them is
implied by anything MailFathom does on its own:

- **The backup is the database and the content store together**, snapshotted in that order — the database first, the
  bucket after it. [ADR 0017](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md)
  writes an object before the row pointing at it, so a database older than the bucket leaves orphans the reclamation
  removes, while a bucket older than the database leaves rows pointing at mail that is gone. The order alone does not
  cover a deletion committed between the two snapshots, so the bucket keeps a deleted object for at least as long as a
  backup takes and the database snapshot is kept — object versioning retaining the noncurrent version, with no lifecycle
  rule expiring one sooner — or erasure and reclamation are held off until the bucket snapshot completes.
- **A restore is verified before the account is put back into service**, by reading every payload of the account back
  against its recorded length and digest. A restored account with an unreadable payload is a lost message, and finding
  that out when somebody opens it is too late to do anything about.
- **An export is not a backup.** It is a copy of the mail in a portable form, taken at one instant, kept for a retention
  period measured in hours, and holding none of the derived state — no rules, no evaluation history, no embeddings, no
  contact book, no configuration. It is what somebody leaves with; the backup is what the deployment is restored from.
- **Erasure is final.** On a mirrored account the source still holds what MailFathom erased. On a drained one nothing
  does. Take the export, download it, verify it opens, and only then erase — and note that the export temporarily
  doubles this deployment's stored-content usage for the account, which is what the size limit and the ceiling are
  checked against before a job exists.

## What the client offers

Nothing. [ADR 0028](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)
keeps mail off the device, and a whole mailbox written to a person's disk by the client is exactly that. The export is
an administrative act, reached through `mfctl` and the routes
[the administrative endpoint](admin-endpoint.md#what-the-endpoint-serves) serves.
