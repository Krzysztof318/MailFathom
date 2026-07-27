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

Classification runs in a fixed order, and the order is the specification. Applying the rules in any other sequence produces different counts for ordinary mail, because several real parts satisfy more than one rule at once.

**First, the cryptographic envelope.** A `multipart/encrypted` container is recognized from its `protocol` parameter rather than from any child's media type, and a `multipart/signed` container likewise. This matters for PGP/MIME, where the envelope holds an `application/pgp-encrypted` control part next to ciphertext that is usually typed `application/octet-stream`: matching on child media types alone catches the control part and lets the ciphertext fall through as an attachment, so a PGP message reports a file that does not exist. The container sets the message-level marker and **no child of an encrypted envelope is classified at all** — not as body, not as attachment. For a signed envelope the signed content is classified normally and the detached signature is not.

**Second, cryptographic leaf parts**, whose media type is `application/pkcs7-signature`, `application/pgp-signature`, `application/pkcs7-mime`, or `application/pgp-encrypted`. This precedes disposition on purpose: an `smime.p7s` part almost always arrives with `Content-Disposition: attachment; filename="smime.p7s"`, so a rule that honored disposition first would count exactly the part this specification exists to stop counting.

**Third, the body branch**, selected by walking the structure rather than by looking at any single part. The branch is resolved recursively: in a `multipart/mixed` the body is its first child, resolved again by these same rules; in a `multipart/related` it is the root part named by the `start` parameter, or the first child when that parameter is absent; in a `multipart/alternative` every member is a body representation. Selecting recursively is the difference between correct and inflated counts on the most ordinary message there is — a `multipart/mixed` carrying a `text/plain` body and one PDF — where a rule that recognized only alternatives and the message root would classify the body text itself as an attachment.

**Fourth, inline resources.** A part is an inline resource when it has a `Content-ID` that an HTML body part references and its disposition is `inline` or absent. The absent case carries the weight: senders routinely omit `Content-Disposition` on embedded images, and requiring the header would reintroduce exactly the header-dependent behavior described above. An explicit `attachment` disposition overrides this, because there the sender has said what the part is.

**Last, everything else is an attachment** — a payload a person would recognize as a separate file.

Three cases need naming because they are common and are read wrongly by default. A nested `message/rfc822` is one attachment and is not recursed into, so a forwarded thread does not report the attachment count of every message inside it. A TNEF `winmail.dat` part is recorded as one attachment and marked as unexpanded, because expanding it is a separate decision with its own parsing surface. A `text/calendar` part depends on where it sits: as a member of a `multipart/alternative` it is an alternative rendering of the body and is not an attachment, which is how Outlook sends a meeting invitation, while the same content sent as a separate `.ics` part is an attachment, which is how several other clients send one. The body-branch rule wins wherever two readings meet, so classification never depends on the media type alone.

The record therefore carries the attachment count and total size, the inline-resource count, and markers stating whether the message is encrypted, carries an **unverified** signature, or contains an unexpanded TNEF part, alongside the per-attachment file name, media type, and size. The signature marker states presence only, and its name must say so. Verification is out of scope here and is tracked in #75, so anyone can attach a signature-typed part or a malformed signature and reach this marker; a name like "signed" would be read downstream — by a query, a rule, or a person — as an authenticity result the extraction never established. Attachment presence means the attachment count is greater than zero; an inline-only or signature-only message does not have attachments.

Size is the **decoded** octet count, measured by streaming the part through a counting reader and discarding the bytes. That is compatible with never materializing attachment content and must be stated, because an implementer reading only the privacy rule would otherwise fall back to the encoded length. MIME declares no per-part length, so this is a measured value rather than a declared one, and the sum of attachment sizes does not equal the message size that IMAP reports.

File names are untrusted input. They arrive RFC 2047 encoded-word or RFC 2231 continuation encoded, and after decoding they can carry path separators and traversal segments, control characters and line breaks, unbounded length, and Unicode bidirectional overrides that make a name render as something other than what it is. The reader decodes the name, then normalizes it: it bounds the length, removes control characters and line breaks, strips any path structure so the result is a name and never a path, and neutralizes bidirectional and other formatting control characters. When normalization changed the name, the record says so, so a caller can tell a plain name from a repaired one. A part with no usable name after normalization is recorded as unnamed rather than given a synthetic one.

Structure is bounded as well as sized. `MaxRawMimeBytes` already bounds the bytes, but a message far below it can carry tens of thousands of parts or deeply nested multiparts, which is an inexpensive way to consume disproportionate CPU and allocations. A maximum part count and a maximum nesting depth are validated settings, and exceeding either ends extraction with the same bounded parse failure a malformed message produces, not an exception.

The limit has to be enforced **while the message is being read, not after**. Checking the counts on a fully materialized `MimeMessage` would concede the allocations the limit exists to prevent — by the time the traversal could observe that a message has forty thousand parts, MimeKit has already built forty thousand objects. MimeKit provides the mechanism: `MimeReader` is a forward-only streaming reader whose boundary and part callbacks expose the structure without constructing an object tree, so the limits are applied in a bounded pre-pass that abandons the message as soon as a limit is crossed. The test for this asserts that the over-limit message never reaches tree construction, not merely that a failure result comes back afterwards, because the second assertion passes even for the implementation this paragraph rejects.

A message whose body is encrypted is recorded as encrypted with no readable body rather than as an empty or malformed message, so specification 08 can mark it instead of indexing nothing and specification 14 can say why the body is absent. Decrypting it is out of scope and is tracked in #75.

## Safety and privacy

Attachment bytes are never materialized during extraction; parts are streamed and counted, never retained. Extraction is streaming where MimeKit permits it, bounded by `MaxRawMimeBytes` and by the part-count and nesting-depth limits above. Logs record counts and the stable occurrence identity, never addresses, subjects, file names, or body text — file names are mail content and are also attacker-controlled, so logging one both leaks and injects. Extracted participant data is classified as personal data by default per draft section 16.1, which constrains how specification 07 persists and indexes it.

## Testing

`Domain.UnitTests` cover address normalization, malformed address rejection, role assignment, thread reference set construction including a message with no thread headers, and file-name normalization: an RFC 2047 encoded-word name, an RFC 2231 continuation name, a name containing path separators and traversal segments, a name containing control characters, an over-long name, a name carrying a bidirectional override, and a name that needs no repair and must survive unchanged.

`Infrastructure.UnitTests` parse in-memory MIME fixtures covering a plain message, a multipart message with attachments, a message with an encoded non-ASCII subject and display name, a message with a missing `Date` header, a nested multipart, and a truncated body, asserting the malformed case produces a failure result rather than an exception. Classification is covered by its own fixtures, one per ordering rule, because the ordering is where this goes wrong. A `multipart/mixed` carrying a `text/plain` body and one PDF must report exactly one attachment, proving the body branch is resolved recursively rather than only at the message root. An S/MIME-signed message whose `smime.p7s` part carries `Content-Disposition: attachment` must report zero attachments, proving cryptographic classification precedes disposition. A PGP/MIME `multipart/encrypted` envelope whose ciphertext is typed `application/octet-stream` must report zero attachments and set the encrypted marker, proving the envelope is recognized from its `protocol` parameter. An HTML message whose `cid:`-referenced image carries no `Content-Disposition` header at all must report zero attachments and one inline resource, and the same image sent with an explicit `attachment` disposition must report one.

The remaining fixtures cover the named cases and the measurements: a forwarded `message/rfc822` reporting one attachment rather than the nested message's parts, a `winmail.dat` message marked unexpanded, a `text/calendar` invitation in both placements, an encrypted message recorded as encrypted with no readable body and an unverified-signature marker that never claims verification, a base64 part whose recorded size is its decoded length rather than its encoded length, and messages exceeding the part-count and nesting-depth limits that must produce a bounded parse failure without the object tree ever being constructed. All fixtures are in-memory byte content; no test touches the file system.

## Out of scope

Persisting the extracted values, which specification 07 owns. Body text extraction and HTML handling, which specification 08 owns. Re-extraction of already stored messages, which specification 08 also addresses as a backfill concern. Expanding TNEF, decrypting encrypted messages (#75), verifying signatures (#75), extracting text from attachment payloads, and scanning attachment content (#77).

## Definition of done

- The synchronizer produces enriched metadata for every stored message without an additional IMAP round trip.
- A malformed message produces an explicit parse failure that is recorded and does not stop the batch or the checkpoint.
- No attachment content is read into memory during extraction.
- A signed message reports no attachment for its signature part, and an inline-only message reports no attachments, both proven by test.
- Every file name reaching the record is decoded and normalized, carries no path structure, and is marked when normalization changed it.
- Part count and nesting depth are validated settings enforced during a streaming pre-pass, and exceeding either produces a bounded parse failure before the object tree is built, rather than an exception afterwards.
- No marker asserts that a signature was verified, because this specification verifies none.
- `docs/features/imap-synchronization.md` documents the extracted fields, the attachment classification rule, the file-name normalization, the structural limits, and the malformed-message behavior.
- `dotnet msbuild .config/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
