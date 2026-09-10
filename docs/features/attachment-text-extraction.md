# Attachment text extraction

<!-- describes: backend/src/Application/Emails/Extraction/Attachments/**, backend/src/Application/Emails/Extraction/Images/**, backend/src/Application/Emails/AttachmentText/DerivedAttachmentText.cs, backend/src/Infrastructure/Documents/**, backend/src/Host/Configuration/Embeddings/AttachmentTextOptions.cs -->

MailFathom reads the words inside a document somebody attached, so that a contract or an invoice is findable by what it
says rather than only by the note it arrived with. `IAttachmentTextExtractor` is the one way that happens: it is handed
one attachment already opened from stored content and answers with its plain text, or with the reason there is none —
never with an exception raised by whatever read the document, and never with an empty string standing in for "nothing
found". The one thing a caller does have to handle is a failure reading the stored content itself, which is about the
attempt rather than about the document and is described under the posture below.

**Three paths call it, and only one of them is background work.** The account run's attachment-reading stage is that
one: it is off unless `Embeddings:AttachmentText:Enabled` says otherwise, runs behind the passage cut and outside any
transaction, and what it does with the answer is [Message chunks](message-chunks.md) — a document's text is cut into
passages and embedded, and it joins the lexical index as a document of its own.

The other two are paths a caller waits on. A message being sent or saved as a draft is screened whole on a deployment
that screens outgoing mail, and an attachment about to be streamed is screened before its first octet leaves —
[sensitive-content scanning](sensitive-content-scanning.md#outgoing-mail-is-screened-rather-than-redacted) holds both.
Neither depends on the switch above, because what they judge is what would leave rather than what is worth indexing, and
neither can be answered later. So the bounds below are what stands between a caller and a hostile document rather than a
courtesy to a worker, and a screening deployment reads a document on a request whatever it decided about embedding one.

A read also records **where each page, slide, or sheet begins** in the text it produced. That list is what turns a
passage's offset into a place a citation can name, and it is written at extraction rather than re-derived later —
re-cutting a mailbox after a boundary-rule change then reads the stored text and the stored boundaries, and opens no
document again. A workbook records a boundary per sheet and not per cell range: a boundary per row would be tens of
thousands of them for one exported table, which costs more to store than the text it points into. A screen reads none of
that: it takes the text and discards it with the answer.

## What is read, and what is only recognized

Recognition reads the declared media type first and falls back to the file name's extension whenever that media type
names no format it recognizes — a generic `application/octet-stream` over a correctly named file is the ordinary shape
of a mail-borne document rather than an edge case. A recognized media type is never overridden by the extension, but
for every other media type the file name alone decides which parser is offered the bytes.

| Recognized as | Extracted | Read by |
| --- | --- | --- |
| PDF | yes | PdfPig, one page at a time |
| Office Open XML word-processing document (`.docx`) | yes | the base class library's zip and XML readers |
| Office Open XML workbook (`.xlsx`) | yes | the same, resolving the shared string table each cell indexes into |
| Office Open XML presentation (`.pptx`) | yes | the same, one page per slide |
| OpenDocument text document (`.odt`) | yes | the same, over the single content part the format packages |
| OpenDocument spreadsheet (`.ods`) | yes | the same, one page per sheet |
| OpenDocument presentation (`.odp`) | yes | the same, one page per drawing page |
| Plain text (`.txt`) | yes | decoded rather than parsed, under the byte-order mark it opens with or as UTF-8 |
| Markdown (`.md`, `.markdown`) | yes | the same reader, keeping the markup as the characters it is |
| Delimiter-separated data (`.csv`) | yes | the same reader, keeping the delimiters and the quoting as written |
| Legacy binary Word document (`.doc`) | no | — |
| Legacy binary Excel workbook (`.xls`) | no | — |
| Legacy binary PowerPoint presentation (`.ppt`) | no | — |

**A plain-text, Markdown, or `.csv` attachment is the one family whose octets already are the text**, so no parser
reads one and none is wanted. Nothing is rendered, no markup is interpreted, no HTML is parsed out of a Markdown
file, no field is split out of a delimited row, and no link or image reference is resolved or fetched — a heading
marker, a list bullet, a link's own target, and the comma between two cells are characters somebody typed and are
as searchable as the prose around them, so stripping any of them would lose words and would put the reader in the
business of deciding what a file means. A `.csv` is therefore searched by what it says and not by its columns: a
row matches on the values in it, and no header, no field boundary, and no cell coordinate is recorded. That same
restraint is what keeps the reader clear of the parsing surface an HTML attachment would carry, which is why
`.html` is recognized as nothing and stays so.

The octets still get no benefit of the doubt, a sender having written both the media type and the file name. The
reader decodes strictly: a byte-order mark decides the encoding — UTF-8, either UTF-16, or either UTF-32 — and
everything without one is read as UTF-8, so octets that are not text answer `Malformed` rather than arriving as a
page of replacement characters a user would then be told matched. A NUL is refused on the same rule and is
worth stating separately, because it decodes cleanly: it is the one shape of binary a strict decoder would
otherwise admit, so a photograph renamed `notes.txt` ends as a stated reason instead of as indexed noise. Nothing
else about the file is guessed — not its language, not a legacy code page, and not its line-ending convention.
The container ceilings do not apply to any of the three, there being no package to inflate, no part to count, and no
element tree to descend; `MaxInputOctets`, `MaxExtractedTextCharacters`, and the timeout hold exactly as they do
everywhere else.

The three legacy binary formats are recognized deliberately rather than left unknown. They are OLE compound files, and
no permissively licensed .NET parser reads all three — so an attachment carrying one is reported as a format MailFathom
does not extract, which tells a mailbox user their file was skipped instead of leaving them to conclude it was searched
and empty. Recognizing them is what makes that sentence possible.

Both office families are read as the zip archives of XML they are, rather than through a document model. That is not
only the smaller dependency: a document model inflates a part before handing it over, which is exactly the moment a
decompression bomb has already won. Only the parts carrying text are opened — a macro project, a Basic library, an
embedded object, an OLE package, and every image in the package are never read, never decoded, and never handed to
anything.

Text is not only in the body. A `.docx` keeps a letterhead's invoice number in a header part, a page number in a footer
part, and a contract's terms in a footnote or endnote part, so all of those are read after the body and in that fixed
order — the format records where a header *prints* rather than where its words belong in a reading, so a fixed order is
the honest arrangement available. An OpenDocument text document keeps the same material in its single content part and
needs nothing extra.

Where the two families differ is the shape inside, and it is the only place they are read differently. Office Open XML
puts each page in a part of its own, so its reader selects parts by name and by number. OpenDocument puts a whole
document in one `content.xml`, so its reader walks that single part and segments it by the element the format begins a
page with — a sheet in a spreadsheet, a drawing page in a presentation, and nothing in a text document, which counts as
one page for the same reason a `.docx` does. Every character an OpenDocument file shows sits inside a paragraph or a
heading, so one walk serves all three of its formats.

Both families write a tab and a line break as elements rather than as characters, and both readers read them as the
whitespace they stand for, because a reader gathering only text nodes would join the words on either side of one into a
word nobody wrote — which is what a `.docx` invoice line, a table of contents, and a form field are each separated by.
Office Open XML writes the same names for something else inside a paragraph's properties, where `tab` declares a tab
*stop* and stands in for no character at all, so a properties element is skipped whole rather than read.

## What a read reports

Every outcome is one of a closed set, and each is distinguishable from every other.

| Outcome | What it means | What an operator or user does |
| --- | --- | --- |
| `Extracted` | The attachment was read. Its text is present, with the page count and the pages that carried no text | Nothing |
| `FormatNotRecognized` | Neither the media type nor the file name names a document format | Nothing; the attachment is not a document |
| `FormatNotExtracted` | The format is recognized and nothing here reads it — the three legacy binary formats, and any format the deployment excluded | Convert the document, or widen `Embeddings:AttachmentText:Formats` where it was narrowed |
| `InputTooLarge` | The attachment holds more octets than `MaxInputOctets` | Raise the ceiling deliberately, having seen what it costs in memory |
| `ExtractedTextTooLarge` | The attachment yielded more characters than `MaxExtractedTextCharacters` | Raise the ceiling; nothing is truncated into a partial answer |
| `ContainerBoundExceeded` | An archive passed its decompression total, its inflation ratio, its part count, its element depth, or — for a workbook — the number of entries its string table may hold, and for an OpenDocument file the number of pages one content part may declare | Treat it as an attachment worth looking at rather than a ceiling to raise, unless the document really is that large: a workbook of more than `MaxExtractedTextCharacters` distinct strings, or a spreadsheet of more than `MaxContainerParts` sheets, is stopped here rather than by the ceiling those keys name for their own outcome |
| `Encrypted` | The document is password-protected and this system holds no password for it | Nothing automatic; no password is stored anywhere here |
| `Malformed` | The bytes do not parse as the format they declare | Nothing; badly formed documents are expected of real mail |
| `TimedOut` | The read passed `Timeout` | Raise the ceiling, or treat a document that needs more than thirty seconds as one worth looking at — the message keeps no stamp and is read again on a later run — which is the account run's answer alone, a screened send or a screened download having no later run to wait for and refusing the act instead |

**`Encrypted` currently also answers for one document that is not locked.** A password-protected Open XML package is
not an archive at all — the package is encrypted whole and wrapped in an OLE compound file — and that wrapper is what
the check recognizes. A legacy `.doc`, `.xls`, or `.ppt` is a compound file too, so one that arrived under a package
format's name, which happens when the media type says nothing and the file name carries the package extension, is
reported as `Encrypted` rather than as the format nothing here reads. Both answers already mean the text was not read,
so what is wrong is the reason the user is given; #1685 is where telling the two apart is tracked.

`Extracted` with an empty text and every page named as carrying none is a scan, and it is deliberately not a failure. A
page with no text layer is the exact target an optical-character-recognition pass would be given, which is why the pages
are reported as a list rather than as a flag on the document — a scanned page bound into an otherwise textual report is
the ordinary case. No optical character recognition happens here; that is a decision of its own and this is not it.

**Both ISO 29500 conformance classes are read, so that answer means a scan and nothing else.** The standard defines a
**Transitional** class, which is what an office suite writes unless somebody explicitly chooses otherwise, and a
**Strict** class, whose parts declare a namespace of their own. Each of the three formats admits both, so a Strict
`.docx`, `.xlsx`, or `.pptx` yields the text its Transitional equivalent does rather than walking to completion with no
run recognized. A conformance class is the only thing that differs between the two: same archive, same part names, same
elements, and therefore the same bounds. The three OpenDocument formats have no such split and never did.

The order pages are reported in has its own limit. A presentation's slides and a workbook's sheets are read in the order
their parts are *named*, and neither format records order there — a deck or a workbook somebody reordered before sending
therefore reads back in the order it was first built in, and the pages named as carrying no text are named by their part
number. #1682 is where that is tracked. An OpenDocument file holds one content part in document order and is unaffected.

A "page" is what the format has one of: a PDF page, a presentation slide, an OpenDocument drawing page, and a sheet of
either spreadsheet format each count as one. A word-processing document counts as one page whatever it prints as,
because neither office format records pagination and reading one would mean laying the document out. A text file
counts as one page for the same reason, so a citation into a `.txt` or a `.md` resolves through the same
coordinate scheme as one into a `.docx`.

## What a described picture reports, and what a message's own ceiling reports

A picture is not read by a parser, so it carries a set of its own: `Described` where a description came back, and one of
nine refusals where none did. The stored row names whichever word applies beside the word `Document` or
`ImageDescription`, so the two sets never have to be told apart by guessing which one a value came from.

| Outcome | What it means | What an operator or user does |
| --- | --- | --- |
| `Described` | The provider answered, and the words it produced are the attachment's text | Nothing |
| `NotActivated` | `Embeddings:ImageDescription:Enabled` is off, so no octets left this deployment | Turn it on, having read what it sends and to whom |
| `FormatNotSupported` | The octets are not one of the raster formats a request may carry | Nothing; the attachment is not a picture this can send |
| `FormatExcluded` | The octets are a markup document — an SVG among them — rather than a raster picture | Nothing; rendering one is executing a document somebody else composed |
| `ImageTooLarge` | The attachment holds more octets than one of the two ceilings it passes: `Embeddings:AttachmentText:MaxInputOctets`, which bounds what any attachment may cost to read, or `Chat:MaxRequestImageOctets`, which bounds what one request may carry | Raise whichever of the two refused it — the attachment ceiling is checked first, and neither constrains the other — deliberately, having seen what one request then costs |
| `PixelGridTooLarge` | The image's header declares a grid larger than `Embeddings:ImageDescription:MaxPixels` | Raise the ceiling, or treat a file declaring an enormous grid as one worth looking at |
| `ImageUnreadable` | The octets name a supported format and do not hold one | Nothing; truncated and malformed pictures are expected of real mail |
| `ProviderTimedOut` | The request outlived the time one chat call is allowed | Raise the timeout, or accept that the message is read again on a later run |
| `ProviderUnavailable` | The provider did not answer, and asking again later may produce one | Nothing; the message is read again on a later run |
| `ProviderRefused` | The provider answered by refusing, and repeating cannot change that | Read the log line: a rejected credential is an operator's to fix, a refused request is not |

`ProviderTimedOut` and `ProviderUnavailable` are the two refusals here that leave the message unsettled, so it is
offered again on the next account run. The extractor's own `TimedOut` is the third outcome that does, for the same
reason: a deadline reached under load says nothing about the file, while `Malformed` and `Encrypted` are properties of
the octets that repeating cannot change. Every other refusal settles the message, including the ones a configuration
change lifts — raising a ceiling or turning the switch on therefore changes what arrives next rather than what is
already stored.

**A message's own octet ceiling reports `MessageBudgetExhausted`,** which belongs to neither set above and is written
for an attachment nothing was offered at all. `Embeddings:AttachmentText:MaxInputOctetsPerEmail` bounds what one
message may cost to read, and an attachment past it is recorded as having yielded nothing rather than left absent —
so a user asking why their contract was not searched is given the ceiling as the answer. The row carries no text
and no pages, and neither index holds anything for it.

`MaxAttachmentsPerEmail` is the ceiling that writes nothing at all, because it bounds the walk rather than what the
walk decides: a message declaring more parts than the deployment reads has the rest neither opened nor recorded, and
what says how many that was is the attachment count on the message beside the rows actually stored. Opening each of
them only to write a refusal down would let a sender choose how many MIME parts this stage reads and how many rows
it stores.

## The posture every read is performed under

An attachment is bytes a hostile sender fully controls, and a document parser asked to read one is the largest attack
surface this project has taken on since it started storing mail. Everything below is structural rather than a rule
somebody keeps.

- **Nothing is executed.** No macro, embedded script, open action, embedded object, form submission, or other active
  content is run, evaluated, followed, or handed to anything that would run it. Extraction reads structure and text.
- **Nothing is written to the file system.** No parser here needs a path, so the question of a location a later step
  could execute never arises. An attachment is buffered in memory for the length of one extraction and released.
- **No external entity is resolved and no external resource is fetched.** Every XML part is read with document type
  declarations prohibited and no resolver, so an entity cannot even be declared, let alone dereferenced. That is
  asserted by a test over the reader's own configuration rather than only stated here.
- **Decompression is bounded while it happens.** An archive's declared uncompressed size is never read — it is the
  sender's number, and a bomb is precisely a file that lies about it. What is counted is what actually inflates,
  against a total shared by every part and against each part's own compressed length.
- **Nothing a parser raises reaches a caller.** Whatever a parser does with adversarial input becomes one of the
  outcomes above. The three things never swallowed are a caller's own cancellation, a process out of memory, and a
  failure reading the attachment's stored content, which propagates as whatever the content store raised — a connection
  that dropped is a fact about this attempt rather than about the document, and an outcome a caller may record once and
  never revisit would turn it into a permanently unreadable attachment.
- **The extracted text is untrusted output.** It is never logged, never rendered as markup, and nothing downstream may
  treat it as anything but opaque characters.
- **It never happens inside a synchronization transaction.** A checkpoint cannot stall behind a document parser, and no
  database transaction is held open while one runs. What has changed is the other half of that claim: two egress paths
  read an attachment while their caller waits — a send or a draft being screened, and a download being screened — so a
  hostile document does delay the caller who supplied or requested it. The ceilings below are what bounds that delay,
  and they are applied per attachment and across the whole message together, so a hundred small files cost no more
  than one large one. **With one exception, stated in the next paragraph and not softened here:** the timeout is
  observed between units of work, so a single PDF page whose content stream inflates enormously is bounded by the input
  ceiling alone and by no clock. On a synchronization stage that costs a worker; on a screened send or a screened
  download it costs the caller, on a request nothing here will cut short.

The timeout is honest about its own limit: it is observed between units of work — a page, an archive part, an element —
because no parser here accepts a cancellation token and .NET cannot abort a thread. A parser that never returns from a
single unit is bounded by whatever else bounds its path, which is why none of those ceilings is optional.

**The two package families and the PDF are not bounded to the same depth, and the difference is worth knowing before
raising a ceiling.** An archive is read by this repository's own code, so the decompression total, the per-part ratio,
and the element depth all apply to it while it inflates. A PDF is read by a library that inflates a page's content
streams itself, with no ceiling MailFathom can set and no way to see the text before it is built — so on that path the
input ceiling on the octets that arrive and the between-pages timeout are the whole of it, and a single page whose
content stream inflates enormously is bounded by neither. Nothing here treats that as acceptable; it is recorded as a
gap rather than described as a guard.

An attachment an antivirus pass has judged infected is not excluded, because no such pass exists yet. When one lands,
this port is where it gates: an infected attachment is skipped before a parser is offered its bytes.

## What reading a mailbox costs, and what bounds it

Reading attachments is the second thing MailFathom does that costs money per unit of mail, and it costs it in two units
that do not convert. **Extraction** opens octets out of stored attachments, which costs the deployment's own processor
and memory. **Description** sends a picture to a chat provider, which costs one call per picture at whatever that
provider charges. Chunking and the lexical index cost neither: they reach no provider, and what they cost is disk.

Each of the two is bounded independently, over the same fixed window `Embeddings:SpendPeriod` names and in its own
unit, and each carries a per-user share beside the deployment's own — the shape
[`Embeddings:MaxInputCharactersPerPeriod`](../operations/configuration-ai.md#embeddings) already has. All four default
to `0`, which declares no ceiling and still counts, so an operator sees the figures before choosing a number.

| Step | Unit | Deployment | Per user |
| --- | --- | --- | --- |
| Extraction | octets handed to a document parser | `Embeddings:AttachmentText:MaxInputOctetsPerPeriod` | `Embeddings:AttachmentText:MaxInputOctetsPerPeriodPerUser` |
| Description | calls a chat provider answered | `Embeddings:ImageDescription:MaxDescriptionsPerPeriod` | `Embeddings:ImageDescription:MaxDescriptionsPerPeriodPerUser` |

**An attachment a parser never saw is charged no octets.** Extraction counts what was handed to a parser rather than
what the walk stepped over, so a picture, a format this deployment does not parse, and an attachment whose declared
size already puts it past `Embeddings:AttachmentText:MaxInputOctets` cost this ceiling nothing — each is decided from
the declaration before an octet is read. That is what keeps the two independent: without it a mailbox of photographs
would exhaust the ceiling a mailbox of contracts is bounded by, and raising the extraction figure would be the remedy
for a bill nothing extracted. What a picture still costs is `MaxInputOctetsPerEmail` and `MaxInputOctetsPerAccountRun`,
which bound what a walk reads whichever port ends up with it.

**A picture refused before the call is charged nothing.** A format outside the allow-list, a grid past `MaxPixels`, a
file past `Chat:MaxRequestImageOctets`, and a header that does not hold the format it claims are all decided here, so
no request leaves and no unit is spent. A provider that timed out, was unavailable, or refused *is* charged one call,
because the request was made and a provider bills for having been asked.

**Embedding attachment text has no ceiling of its own**, and that is a decision rather than an omission:
`Embeddings:MaxInputCharactersPerPeriod` counts an attachment's characters exactly as it counts a message's own, so a
second ceiling on the same send would be two keys answering for one behaviour. [ADR
0029](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md)
records it. **Concurrency has no ceiling of its own here either**: how many account runs read at once is
`Mail:MaxConcurrentAccounts`, and how many provider calls are in flight is
`Resilience:AiProviderInvocation:ConcurrencyLimit`. What this section adds is a *rate* for the description workload —
`Embeddings:ImageDescription:MaxRequestsPerMinute`, paced separately from the embedding provider's, because the two are
different endpoints with different published quotas. That rate is the *deployment's* — its slot marker is a row every
replica moves — while the concurrency limit beside it stays each process's, which is the split
[ADR 0031](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)
records: a rate names a quota the provider counts across the deployment, and an in-flight count names what one process
has open.

**Reaching a ceiling waits rather than fails.** Both are read before a message is opened, so the account run ends where
it is with that message untouched and unstamped. The surrounding synchronization run succeeds, nothing is dropped — a
message with no attachment reading is exactly what the next pass selects on — and the first run after the period rolls
over reaches it. A per-user ceiling stops that user's mail alone. It is the same degradation an exhausted embedding
budget produces, which is what makes the two readable together.

The run says which one it met, at information level and naming both halves: the step, which is the key to raise, and
whether it was the deployment's ceiling or that user's share of it. The two have different remedies — raising a
user's share achieves nothing while the deployment itself has stopped spending — so reporting only that *a* ceiling
was met would send an operator to the wrong key.

## What a mailbox reports about how far reading has come

`mfctl embedding status` and `mfctl mailbox status` report attachment and image coverage separately from message
coverage, because the two say different things: a mailbox may be entirely embedded on its message text with every
document in it still unread.

What each reports is the messages carrying an attachment, how many of those have been read, what reading the remainder
would open and how many descriptions it could make, what reading has yielded — document extracts, described images, and
the characters the lexical index grew by — and an aggregate of why the rest yielded nothing, one line per reason.
The reasons are the deployment's own outcome names, so the reading is exact rather than bucketed: encrypted, malformed
or unreadable, a format it does not read, past a size bound, timed out, and a description the provider refused, was
unavailable for, or did not answer. One more is composed rather than stored — an attachment that parsed and carried no
text, which is what a scan looks like, and which is reported as a skip rather than as an extract so a page of scanned
paper is never counted as searchable. It is an aggregate over reasons and never a list of attachments, which is what
makes it safe to serve: it says how much of a mailbox cannot be read without naming one message, one filename, or one
sender. `mfctl mailbox status` scopes it to one account; `mfctl embedding
status` reports the deployment, with both periods beside it.

**Activating an embedding profile weighs the same figures.** On an instance already holding mail,
`mfctl embedding activate` reports what reading the stored attachments would open and how many descriptions it could
make, beside the passages it would send, and refuses the activation outright when either estimate is past what one
period admits — so an operator agreeing to one bill is not handed the other afterwards. The refusal names the key to
raise.

## Configuration

Every ceiling and the format list live under `Embeddings:AttachmentText`, beside the embedding ceilings rather than
inside them, with the description ceilings and the description rate under `Embeddings:ImageDescription`. [AI configuration](../operations/configuration-ai.md) holds each key, its default, and what happens when it
binds.
