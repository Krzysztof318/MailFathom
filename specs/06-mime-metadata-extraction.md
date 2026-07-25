# MIME Metadata Extraction

**Roadmap group:** B — message data enrichment
**Draft delivery stage:** 3
**Depends on:** 04
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Extract the normalized message metadata that draft section 9.1 requires — participants, dates, thread headers, and an attachment summary — from the raw MIME that synchronization already stores, so the read side of specification 13 has something to filter and sort on.

## Current state

`RemoteMessageMetadata` carries only occurrence identity, internet message identifier, subject, sent timestamp, and size. `StoredEmailEntity` mirrors that. Nothing knows who sent a message, who received it, whether it has attachments, or which thread it belongs to.

## Approved scope

`Domain` gains value objects for the concepts that carry invariants: an email address with its display name and a normalized comparison form, an address role distinguishing sender, from, reply-to, to, cc, and bcc, and a thread reference set built from the message identifier, `In-Reply-To`, and `References` headers. Normalization is a domain rule because search, filtering, and future deduplication all depend on two addresses that differ only in case or folding comparing equal.

`Application` gains an `IMessageMimeReader` port that turns raw RFC 822 content into a normalized metadata record: participants by role, the sent and received timestamps, subject, thread references, and an attachment summary of count, total declared byte size, and per-attachment file name, media type, and declared size. The port returns a result rather than throwing on malformed input, because badly formed mail is expected rather than exceptional; draft section 17 requires such messages to be quarantined after bounded parsing attempts without blocking the folder checkpoint, and this port is where that decision is made visible.

`Infrastructure` implements the port with MimeKit, which is already available through the pinned MailKit package. The synchronizer calls the reader on content it has already fetched, so extraction adds no additional IMAP traffic and cannot affect the remote `\Seen` flag.

## Safety and privacy

Attachment bytes are never materialized during extraction; only declared metadata is read. Extraction is streaming where MimeKit permits it, bounded by the existing `MaxRawMimeBytes` limit. Logs record counts and the stable occurrence identity, never addresses, subjects, file names, or body text. Extracted participant data is classified as personal data by default per draft section 16.1, which constrains how specification 07 persists and indexes it.

## Testing

`Domain.UnitTests` cover address normalization, malformed address rejection, role assignment, and thread reference set construction including a message with no thread headers. `Infrastructure.UnitTests` parse in-memory MIME fixtures covering a plain message, a multipart message with attachments, a message with an encoded non-ASCII subject and display name, a message with a missing `Date` header, a nested multipart, and a truncated body, asserting the malformed case produces a failure result rather than an exception. All fixtures are in-memory byte content; no test touches the file system.

## Out of scope

Persisting the extracted values, which specification 07 owns. Body text extraction and HTML handling, which specification 08 owns. Re-extraction of already stored messages, which specification 08 also addresses as a backfill concern.

## Definition of done

- The synchronizer produces enriched metadata for every stored message without an additional IMAP round trip.
- A malformed message produces an explicit parse failure that is recorded and does not stop the batch or the checkpoint.
- No attachment content is read into memory during extraction.
- `docs/features/imap-synchronization.md` documents the extracted fields and the malformed-message behavior.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
