# MIME Metadata Extraction

**Roadmap group:** B — message data enrichment
**Draft delivery stage:** 3
**Depends on:** 04
**Estimated change size:** ~900 lines including tests and documentation

## Goal

Extract the normalized message metadata that draft section 9.1 requires — participants, dates, thread headers, and an attachment summary — from the raw MIME that synchronization already stores, so the read side of specification 13 has something to filter and sort on.

## Current state

`RemoteMessageMetadata` carries only occurrence identity, internet message identifier, subject, sent timestamp, and size. `StoredEmailEntity` mirrors that. Nothing knows who sent a message, who received it, whether it has attachments, or which thread it belongs to.

## Approved scope

`Domain` gains value objects for the concepts that carry invariants: an email address with its display name and a normalized comparison form, an address role distinguishing sender, from, reply-to, to, cc, and bcc, and a thread reference set built from the message identifier, `In-Reply-To`, and `References` headers. Normalization is a domain rule because search, filtering, and future deduplication all depend on two addresses that differ only in case or folding comparing equal.

`Application` gains an `IMessageMimeReader` port that turns raw RFC 822 content into a normalized metadata record: participants by role, the sent and received timestamps, subject, thread references, and the attachment summary defined below. The port returns a result rather than throwing on malformed input, because badly formed mail is expected rather than exceptional; draft section 17 requires such messages to be quarantined after bounded parsing attempts without blocking the folder checkpoint, and this port is where that decision is made visible.

`Infrastructure` implements the port with MimeKit, which is already available through the pinned MailKit package. The synchronizer calls the reader on content it has already fetched, so extraction adds no additional IMAP traffic and cannot affect the remote `\Seen` flag.

## Attachment classification

What counts as an attachment is a stated rule here, not a library default. MimeKit's `MimeMessage.Attachments` keys off `Content-Disposition`, and inheriting that would make an S/MIME-signed message report an attachment named `smime.p7s` and would leave a signature-block logo counting or not counting depending on how the sender wrote one header. Specifications 13 and 15 expose attachment presence as a filter and specifications 14 and 17 show the count to a caller, so the rule has to mean what a mailbox owner means by it.

A leaf part is classified as exactly one of three things. It is an **attachment** when it is a payload a person would recognize as a separate file: an explicit `attachment` disposition, or a leaf part that is neither a body representation nor an inline resource nor a cryptographic part. It is an **inline resource** when it carries an `inline` disposition together with a `Content-ID` that an HTML part of the same message references, which is how signature images and embedded screenshots arrive. It is a **cryptographic part** when its media type is `application/pkcs7-signature`, `application/pgp-signature`, `application/pkcs7-mime`, or `application/pgp-encrypted`. Parts that represent the message body itself — the members of a `multipart/alternative` and the `text/plain` or `text/html` root — are never attachments.

Three cases need naming because they are common and are read wrongly by default. A nested `message/rfc822` is one attachment and is not recursed into, so a forwarded thread does not report the attachment count of every message inside it. A TNEF `winmail.dat` part is recorded as one attachment and marked as unexpanded, because expanding it is a separate decision with its own parsing surface. A `text/calendar` part depends on where it sits: as a member of a `multipart/alternative` it is an alternative rendering of the body and is not an attachment, which is how Outlook sends a meeting invitation, while the same content sent as a separate `.ics` part is an attachment, which is how several other clients send one. The body-representation rule wins wherever the two readings meet, so classification never depends on the media type alone.

The record therefore carries the attachment count and total size, the inline-resource count, and markers stating whether the message is signed, encrypted, or contains an unexpanded TNEF part, alongside the per-attachment file name, media type, and size. Attachment presence means the attachment count is greater than zero; an inline-only or signature-only message does not have attachments.

Size is the **decoded** octet count, measured by streaming the part through a counting reader and discarding the bytes. That is compatible with never materializing attachment content and must be stated, because an implementer reading only the privacy rule would otherwise fall back to the encoded length. MIME declares no per-part length, so this is a measured value rather than a declared one, and the sum of attachment sizes does not equal the message size that IMAP reports.

File names are untrusted input. They arrive RFC 2047 encoded-word or RFC 2231 continuation encoded, and after decoding they can carry path separators and traversal segments, control characters and line breaks, unbounded length, and Unicode bidirectional overrides that make a name render as something other than what it is. The reader decodes the name, then normalizes it: it bounds the length, removes control characters and line breaks, strips any path structure so the result is a name and never a path, and neutralizes bidirectional and other formatting control characters. When normalization changed the name, the record says so, so a caller can tell a plain name from a repaired one. A part with no usable name after normalization is recorded as unnamed rather than given a synthetic one.

Structure is bounded as well as sized. `MaxRawMimeBytes` already bounds the bytes, but a message far below it can carry tens of thousands of parts or deeply nested multiparts, which is an inexpensive way to consume disproportionate CPU and allocations. A maximum part count and a maximum nesting depth are validated settings, and exceeding either ends extraction with the same bounded parse failure a malformed message produces, not an exception.

A message whose body is encrypted is recorded as encrypted with no readable body rather than as an empty or malformed message, so specification 08 can mark it instead of indexing nothing and specification 14 can say why the body is absent. Decrypting it is out of scope and is tracked in #75.

## Safety and privacy

Attachment bytes are never materialized during extraction; parts are streamed and counted, never retained. Extraction is streaming where MimeKit permits it, bounded by `MaxRawMimeBytes` and by the part-count and nesting-depth limits above. Logs record counts and the stable occurrence identity, never addresses, subjects, file names, or body text — file names are mail content and are also attacker-controlled, so logging one both leaks and injects. Extracted participant data is classified as personal data by default per draft section 16.1, which constrains how specification 07 persists and indexes it.

## Testing

`Domain.UnitTests` cover address normalization, malformed address rejection, role assignment, thread reference set construction including a message with no thread headers, and file-name normalization: an RFC 2047 encoded-word name, an RFC 2231 continuation name, a name containing path separators and traversal segments, a name containing control characters, an over-long name, a name carrying a bidirectional override, and a name that needs no repair and must survive unchanged.

`Infrastructure.UnitTests` parse in-memory MIME fixtures covering a plain message, a multipart message with attachments, a message with an encoded non-ASCII subject and display name, a message with a missing `Date` header, a nested multipart, and a truncated body, asserting the malformed case produces a failure result rather than an exception. Classification is covered by its own fixtures: an S/MIME-signed message that must report zero attachments, an HTML message whose only non-body part is a `cid:`-referenced inline image that must report zero attachments and one inline resource, the same image sent with an `attachment` disposition that must report one, a forwarded `message/rfc822` that must report one attachment rather than the nested message's parts, a `winmail.dat` message marked unexpanded, a `text/calendar` invitation, an encrypted message recorded as encrypted with no readable body, a base64 part whose recorded size is its decoded length rather than its encoded length, and messages exceeding the part-count and nesting-depth limits that must produce a bounded parse failure. All fixtures are in-memory byte content; no test touches the file system.

## Out of scope

Persisting the extracted values, which specification 07 owns. Body text extraction and HTML handling, which specification 08 owns. Re-extraction of already stored messages, which specification 08 also addresses as a backfill concern. Expanding TNEF, decrypting encrypted messages (#75), verifying signatures (#75), extracting text from attachment payloads, and scanning attachment content (#77).

## Definition of done

- The synchronizer produces enriched metadata for every stored message without an additional IMAP round trip.
- A malformed message produces an explicit parse failure that is recorded and does not stop the batch or the checkpoint.
- No attachment content is read into memory during extraction.
- A signed message reports no attachment for its signature part, and an inline-only message reports no attachments, both proven by test.
- Every file name reaching the record is decoded and normalized, carries no path structure, and is marked when normalization changed it.
- Part count and nesting depth are validated settings, and exceeding either produces a bounded parse failure rather than an exception.
- `docs/features/imap-synchronization.md` documents the extracted fields, the attachment classification rule, the file-name normalization, the structural limits, and the malformed-message behavior.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
