# Attachment text extraction

<!-- describes: backend/src/Application/Emails/Extraction/Attachments/**, backend/src/Infrastructure/Documents/**, backend/src/Host/Configuration/Embeddings/AttachmentTextOptions.cs -->

MailFathom reads the words inside a document somebody attached, so that a contract or an invoice is findable by what it
says rather than only by the note it arrived with. `IAttachmentTextExtractor` is the one way that happens: it is handed
one attachment already opened from stored content and answers with its plain text, or with the reason there is none —
never with an exception raised by whatever read the document, and never with an empty string standing in for "nothing
found". The one thing a caller does have to handle is a failure reading the stored content itself, which is about the
attempt rather than about the document and is described under the posture below.

Nothing in this release calls it yet. The pipeline that cuts and embeds what an attachment yields is separate work, and
the switch deciding whether an attachment is read at all arrives with it. What exists today is the port, the parsers
behind it, and every ceiling around them.

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
| Legacy binary Word document (`.doc`) | no | — |
| Legacy binary Excel workbook (`.xls`) | no | — |
| Legacy binary PowerPoint presentation (`.ppt`) | no | — |

The three legacy binary formats are recognized deliberately rather than left unknown. They are OLE compound files, and
no permissively licensed .NET parser reads all three — so an attachment carrying one is reported as a format MailFathom
does not extract, which tells a mailbox owner their file was skipped instead of leaving them to conclude it was searched
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

| Outcome | What it means | What an operator or owner does |
| --- | --- | --- |
| `Extracted` | The attachment was read. Its text is present, with the page count and the pages that carried no text | Nothing |
| `FormatNotRecognized` | Neither the media type nor the file name names a document format | Nothing; the attachment is not a document |
| `FormatNotExtracted` | The format is recognized and nothing here reads it — the three legacy binary formats, and any format the deployment excluded | Convert the document, or widen `Embeddings:AttachmentText:Formats` where it was narrowed |
| `InputTooLarge` | The attachment holds more octets than `MaxInputOctets` | Raise the ceiling deliberately, having seen what it costs in memory |
| `ExtractedTextTooLarge` | The attachment yielded more characters than `MaxExtractedTextCharacters` | Raise the ceiling; nothing is truncated into a partial answer |
| `ContainerBoundExceeded` | An archive passed its decompression total, its inflation ratio, its part count, its element depth, or — for a workbook — the number of entries its string table may hold, and for an OpenDocument file the number of pages one content part may declare | Treat it as an attachment worth looking at rather than a ceiling to raise, unless the document really is that large: a workbook of more than `MaxExtractedTextCharacters` distinct strings, or a spreadsheet of more than `MaxContainerParts` sheets, is stopped here rather than by the ceiling those keys name for their own outcome |
| `Encrypted` | The document is password-protected and this system holds no password for it | Nothing automatic; no password is stored anywhere here |
| `Malformed` | The bytes do not parse as the format they declare | Nothing; badly formed documents are expected of real mail |
| `TimedOut` | The read passed `Timeout` | Raise the ceiling, or treat a document that needs more than thirty seconds as one worth looking at |

**`Encrypted` currently also answers for one document that is not locked.** A password-protected Open XML package is
not an archive at all — the package is encrypted whole and wrapped in an OLE compound file — and that wrapper is what
the check recognizes. A legacy `.doc`, `.xls`, or `.ppt` is a compound file too, so one that arrived under a package
format's name, which happens when the media type says nothing and the file name carries the package extension, is
reported as `Encrypted` rather than as the format nothing here reads. Both answers already mean the text was not read,
so what is wrong is the reason the owner is given; #1685 is where telling the two apart is tracked.

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
because neither office format records pagination and reading one would mean laying the document out.

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
- **It is background work.** Reading an attachment never happens inside a synchronization transaction and is never
  reachable from an MCP or client read path, so a slow or hostile attachment cannot make a caller wait or a checkpoint
  stall.

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

## Configuration

Every ceiling and the format list live under `Embeddings:AttachmentText`, beside the embedding ceilings rather than
inside them. [AI configuration](../operations/configuration-ai.md) holds each key, its default, and what happens when it
binds.
