# Attachment text extraction

<!-- describes: backend/src/Application/Emails/Extraction/Attachments/**, backend/src/Infrastructure/Documents/**, backend/src/Host/Configuration/Embeddings/AttachmentTextOptions.cs -->

MailFathom reads the words inside a document somebody attached, so that a contract or an invoice is findable by what it
says rather than only by the note it arrived with. `IAttachmentTextExtractor` is the one way that happens: it is handed
one attachment already opened from stored content and answers with its plain text, or with the reason there is none —
never with an exception a caller has to interpret, and never with an empty string standing in for "nothing found".

Nothing in this release calls it yet. The pipeline that cuts and embeds what an attachment yields is separate work, and
the switch deciding whether an attachment is read at all arrives with it. What exists today is the port, the parsers
behind it, and every ceiling around them.

## What is read, and what is only recognized

Recognition reads the declared media type first and falls back to the file name's extension where the media type says
nothing — a generic `application/octet-stream` over a correctly named file is the ordinary shape of a mail-borne
document rather than an edge case. The extension is a fallback and never an override, so renaming a file does not
decide which parser reads it.

| Recognized as | Extracted | Read by |
| --- | --- | --- |
| PDF | yes | PdfPig, one page at a time |
| Office Open XML word-processing document (`.docx`) | yes | the base class library's zip and XML readers |
| Office Open XML workbook (`.xlsx`) | yes | the same, resolving the shared string table each cell indexes into |
| Office Open XML presentation (`.pptx`) | yes | the same, one page per slide |
| Legacy binary Word document (`.doc`) | no | — |
| Legacy binary Excel workbook (`.xls`) | no | — |
| Legacy binary PowerPoint presentation (`.ppt`) | no | — |

The three legacy binary formats are recognized deliberately rather than left unknown. They are OLE compound files, and
no permissively licensed .NET parser reads all three — so an attachment carrying one is reported as a format MailFathom
does not extract, which tells a mailbox owner their file was skipped instead of leaving them to conclude it was searched
and empty. Recognizing them is what makes that sentence possible.

An Office Open XML package is read as the zip archive of XML parts it is, rather than through a document model. That is
not only the smaller dependency: a document model inflates a part before handing it over, which is exactly the moment a
decompression bomb has already won. Only the parts carrying text are opened — a macro project, an embedded object, an
OLE package, and every image in the package are never read, never decoded, and never handed to anything.

## What a read reports

Every outcome is one of a closed set, and each is distinguishable from every other.

| Outcome | What it means | What an operator or owner does |
| --- | --- | --- |
| `Extracted` | The attachment was read. Its text is present, with the page count and the pages that carried no text | Nothing |
| `FormatNotRecognized` | Neither the media type nor the file name names a document format | Nothing; the attachment is not a document |
| `FormatNotExtracted` | The format is recognized and nothing here reads it — the three legacy binary formats, and any format the deployment excluded | Convert the document, or widen `Embeddings:AttachmentText:Formats` where it was narrowed |
| `InputTooLarge` | The attachment holds more octets than `MaxInputOctets` | Raise the ceiling deliberately, having seen what it costs in memory |
| `ExtractedTextTooLarge` | The attachment yielded more characters than `MaxExtractedTextCharacters` | Raise the ceiling; nothing is truncated into a partial answer |
| `ContainerBoundExceeded` | An archive passed its decompression total, its inflation ratio, its part count, or its element depth | Treat it as an attachment worth looking at rather than a ceiling to raise |
| `Encrypted` | The document is password-protected and this system holds no password for it | Nothing automatic; no password is stored anywhere here |
| `Malformed` | The bytes do not parse as the format they declare | Nothing; badly formed documents are expected of real mail |
| `TimedOut` | The read passed `Timeout` | Raise the ceiling, or treat a document that needs more than thirty seconds as one worth looking at |

`Extracted` with an empty text and every page named as carrying none is a scan, and it is deliberately not a failure. A
page with no text layer is the exact target an optical-character-recognition pass would be given, which is why the pages
are reported as a list rather than as a flag on the document — a scanned page bound into an otherwise textual report is
the ordinary case. No optical character recognition happens here; that is a decision of its own and this is not it.

A "page" is what the format has one of: a PDF page, a presentation slide, and a worksheet each count as one. A
word-processing document counts as one page whatever it prints as, because Office Open XML records no pagination and
reading one would mean laying the document out.

## The posture every read is performed under

An attachment is bytes a hostile sender fully controls, and a document parser asked to read one is the largest attack
surface this project has taken on since it started storing mail. Everything below is structural rather than a rule
somebody keeps.

- **Nothing is executed.** No macro, embedded script, open action, embedded object, form submission, or other active
  content is run, evaluated, followed, or handed to anything that would run it. Extraction reads structure and text.
- **Nothing is written to the file system.** Neither parser needs a path, so the question of a location a later step
  could execute never arises. An attachment is buffered in memory for the length of one extraction and released.
- **No external entity is resolved and no external resource is fetched.** Every XML part is read with document type
  declarations prohibited and no resolver, so an entity cannot even be declared, let alone dereferenced. That is
  asserted by a test over the reader's own configuration rather than only stated here.
- **Decompression is bounded while it happens.** An archive's declared uncompressed size is never read — it is the
  sender's number, and a bomb is precisely a file that lies about it. What is counted is what actually inflates,
  against a total shared by every part and against each part's own compressed length.
- **Nothing a parser raises reaches a caller.** Whatever a parser does with adversarial input becomes one of the
  outcomes above. The two things never swallowed are a caller's own cancellation and a process out of memory.
- **The extracted text is untrusted output.** It is never logged, never rendered as markup, and nothing downstream may
  treat it as anything but opaque characters.
- **It is background work.** Reading an attachment never happens inside a synchronization transaction and is never
  reachable from an MCP or client read path, so a slow or hostile attachment cannot make a caller wait or a checkpoint
  stall.

The timeout is honest about its own limit: it is observed between units of work — a page, an archive part, an element —
because no parser here accepts a cancellation token and .NET cannot abort a thread. A parser that never returns from a
single unit is bounded by the size, ratio, and depth ceilings instead, which is why none of those is optional.

An attachment an antivirus pass has judged infected is not excluded, because no such pass exists yet. When one lands,
this port is where it gates: an infected attachment is skipped before a parser is offered its bytes.

## Configuration

Every ceiling and the format list live under `Embeddings:AttachmentText`, beside the embedding ceilings rather than
inside them. [AI configuration](../operations/configuration-ai.md) holds each key, its default, and what happens when it
binds.
