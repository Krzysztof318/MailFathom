---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-02
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Draw mail as the closed document tree the service already reduces it to, in the client's own document with no frame and no sanitizer, let a remote picture be a request nothing on either side remembers, and show the sender's own markup nowhere at all

<!-- describes: frontend/src/Client.App/src/**, frontend/src/Client.Backend/src/**, backend/src/Host/Api/ClientMailBodyEndpoint.cs, backend/src/Application/EmailContent/Rendering/Document/** -->

## Context and Problem Statement

Mail HTML is written by strangers and is the most hostile input this application handles. It carries CSS that will escape whatever element it is given, scripts, iframes, forms, and remote images whose only function is to tell the sender that the message was opened. How a reading pane draws it was decided once, in ADR 0019, and [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392) removed that record with the Uno Platform client it was written about — its answers described a WebAssembly bundle and a desktop `WebView`, and neither describes what runs now.

The question is being asked again for a React client that ships as a static web bundle and as a Tauri desktop application from one tree, per [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md). The web head is the hard case and would ordinarily decide the shape: the application *is* a page there, so anything rendering mail HTML sits in the same browser as the application's own origin, and a sandboxed frame, a content security policy, and a sanitization pass are the three parts to weigh against each other. The desktop head then has to arrive at the same answer rather than a second one, because it renders in the operating system's WebView — WebView2 on Windows, WebKitGTK on Linux — and a mechanism that depends on what an engine has implemented is a mechanism that diverges by platform.

What the question turns out to rest on is a contract the service already publishes. `GET /api/client/messages/{id}/body` never asks for markup: it sets `IncludeMailDocument` unconditionally, never sets `IncludeSanitizedHtml`, and answers with the plain text beside `MailDocument` — [the document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws), a closed tree of typed blocks in which every leaf is text, a number, an opaque colour, or a member of a fixed enumeration. There is nowhere in it to put a script, an event handler, an embedded object, a form, a style sheet, or an element. So the client is not handed mail HTML at all, and the decision in front of the client is not *how to render hostile markup safely* but *whether to keep it that way and what the client then owes*.

Recorded on issue [#1427](https://github.com/Krzysztof318/MailFathom/issues/1427). It implements nothing: [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428) builds what this names, [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426) is the pane it is drawn inside, and [#1430](https://github.com/Krzysztof318/MailFathom/issues/1430) is the parent all three sit under. Nothing in the client renders a body today.

**A second question was asked of this record on [#1477](https://github.com/Krzysztof318/MailFathom/issues/1477), and this record answers it while still `proposed` rather than standing beside a second one.** The design project drew a control on the message head — a `code` glyph titled *show the full HTML version* — opening the message's own markup in a surface of its own, a `srcdoc` frame carrying `sandbox=""` and a footer telling the reader that scripts and remote content are blocked. That is not the question the paragraphs above answered. They answered *what the reading pane draws*, and this asks whether a **second** surface, beside that pane and not replacing it, may show what the sender actually sent — for the reader whose message did not reduce well and who is looking for a document the reduction lost. The need is real and is the only thing here that is; the second surface is refused, and what the reader is owed instead is named below.

## Decision Drivers

- **The safety property has to be statable, not merely present.** A policy nobody can write down in a sentence is a policy nobody can review, and a rendering path whose safety rests on the completeness of a filter is one whose safety is only as good as this year's bypass.
- **One answer for both heads.** The two heads run different rendering engines, so anything resting on a recently shipped platform capability is an answer on one head and a gap on the other. A rendering defect must have one place to be fixed.
- **Two implementations of one judgement will disagree.** [`frontend/src/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/src/AGENTS.md) already refuses the client re-deriving anything the service decides, and what is safe to draw is exactly such a judgement.
- **A tracking pixel is defeated by absence, not by a setting.** A renderer that holds a remote address and declines to fetch it is one defect away from fetching it; a document that does not carry the address cannot.
- **What is remembered about a reader's override is a record of what they read.** Which messages somebody chose to load pictures for is a list of the messages that mattered to them, and it is personal data wherever it is written down.
- **A dependency here is the most security-sensitive package in the tree.** Whatever sanitizes mail owns a permanent patch obligation and a licence entry, and it is worth having only if something it does is load-bearing.
- **The pane is a reading surface, not an embedded document.** [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426) requires that a fragment of the body can be selected and that the selection is application state, and the accessibility obligations require the content to be reachable rather than opaque.
- **A reader who says the reduction lost something is reporting a defect, and is owed an answer rather than an escape hatch.** A control that hands them the sender's markup answers the complaint by withdrawing the guarantee that made the reduction worth having, and it answers it in the one place nobody can see it happening — on the message where the reduction was worst, which is the message most likely to be hostile.
- **A second surface is the same client.** Whatever is true of the reading pane's isolation has to be true of anything else in the same application that draws the same message, because a reader does not hold two trust boundaries and a tracking pixel does not care which surface fetched it.

## Considered Options

**A — what the client is handed, and therefore what it renders:**

- A1 — the message's own HTML, sanitized in the client immediately before it is inserted.
- A2 — the sanitized HTML representation the service already produces for `IncludeSanitizedHtml`.
- A3 — `MailDocument`, drawn with the client's own typed components.
- A4 — the plain text alone, and no rendering of the sender's layout at all.
- A5 — the message's own HTML as it arrived, unmodified, which is the only representation that is *what the sender actually sent*.

**B — where the rendered body sits in the document:**

- B1 — in the application's own document, as ordinary React elements.
- B2 — in a sandboxed `iframe` with `srcdoc`, an opaque origin, and a content security policy of its own.
- B3 — in an `iframe` served from a second origin the deployment publishes.
- B4 — B2's frame opened as a second surface *beside* the pane rather than as the pane, on a control the reader presses.

**C — where sanitization happens:**

- C1 — in the service, before storage.
- C2 — in the service, on the way out, which is where it is today for the surface that asks for markup.
- C3 — in the client, before rendering.
- C4 — in neither, on the client's path, because nothing that needs sanitizing crosses it.

**D — the remote-picture override, and what may be remembered about it:**

- D1 — nothing is remembered; the request carries the decision and opening the message again asks again.
- D2 — remembered in memory for as long as the message is open.
- D3 — remembered durably per message, in browser storage.
- D4 — remembered per sender, durably, as an *always load pictures from this sender* rule.

**E — what happens to a message whose document is refused:**

- E1 — an empty pane.
- E2 — the plain text, with the refusal named to the reader.
- E3 — the plain text, silently.

**F — how the isolation is proven:**

- F1 — by review of the pane.
- F2 — by a validating parser with its own tests, component tests written as attacks, browser assertions on what the page fetched and what the document holds, and a lint rule making every call that would undo all of it unwritable.

**G — what a reader is given when the reduction lost something they needed:**

- G1 — nothing; the reduction is what there is, and the complaint has nowhere to go.
- G2 — a wider block catalogue, decided and built in the service, arriving with a schema revision.
- G3 — the message itself, handed out of the application to be opened in something the reader already chose and already trusts.

## Decision Outcome

Chosen: **A3, B1, C4, D1, E2, and F2** — the client is handed the closed document tree and never mail HTML, draws it as ordinary elements in the application's own document, sanitizes nothing because nothing sanitizable reaches it, treats a remote picture as a second request nothing on either side of the boundary writes down, falls back to the plain text with the refusal named, and proves the whole of it mechanically rather than by review.

On the second question: **A5 and B4 are refused, and G2 is what a reader who needs what the reduction lost is given.** No surface of this client shows a message's markup, in any representation and in any container — not the reading pane, not a frame beside it, not a modal, and not a window of its own. A message that reduces badly is a defect in the reduction with an address on the service side, and widening the catalogue is how it is answered.

### The client is never handed mail HTML, so it never renders any

`ClientMailBodyEndpoint` sets `IncludeMailDocument = true` on every read and sets `IncludeSanitizedHtml` on none, which is the whole of the mechanism. What crosses is the plain text and a `MailDocument`: a list of typed blocks — `paragraph`, `heading`, `list`, `table`, `quote`, `image`, `separator`, `preformatted` — whose leaves are text, numbers, a `MailDocumentColour` normalized to `#rrggbb`, and members of `MailTextEmphasis`, `MailBlockAlignment`, `MailLinkDeception`, and `MailDocumentRefusal`. A construct nobody anticipated cannot survive by being unfamiliar, because it has no shape to arrive in.

**The tree is reduced from the message's own parse rather than from the sanitizer's output.** That is the property that rules out the mutation attacks built out of one parser reading what another parser wrote, and it is the service's, stated in [Email content § The document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws). This record adds only that the client must not undo it, which it would do by asking for markup or by reconstructing any.

### What holds each property, and which parts are load-bearing

The four properties [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428) has to hold, each against what actually holds it:

| Property | What holds it | Load-bearing, or defence in depth |
|---|---|---|
| No script, embedded object, form, or event handler executes | The document contract has nowhere to express one, and React escapes text | **Load-bearing: the shape of the contract.** Nothing about the browser is relied on |
| The client never turns a message value back into markup | A lint rule under `frontend/src/` refusing every way a value becomes markup: `dangerouslySetInnerHTML`, `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` and `document.writeln`, `setHTMLUnsafe`, `DOMParser.parseFromString`, `Range.createContextualFragment`, and an `iframe`'s `srcdoc` in either form | **Load-bearing.** Each of these is a call that would undo the decision, so each is made unwritable rather than reviewed |
| Message style cannot escape the pane | The only presentation a message contributes is an opaque colour, an alignment, and an emphasis flag, each applied by the pane's own component. No selector, position, size, or stacking order crosses the wire | **Load-bearing: the shape of the contract.** There is nothing to escape with |
| No remote resource is fetched before a person asks | Every remote address is removed while the tree is built; `RemovedRemoteReferenceCount` is what survives in its place | **Load-bearing: the service's removal.** The client holds no address to decline |
| A link cannot navigate the application | `MailDocumentLink.Target` carries `http`, `https`, or `mailto` and nothing else, and the client opens it out of the application rather than in it | **Load-bearing on both halves**: the scheme allow-list against `javascript:`, and leaving the application against a same-tab navigation that would discard the session |

**A content security policy on the application document is defence in depth, and this decision does not rest on it.** It would protect the application against a defect somewhere else in the client; it protects the pane against nothing, because no message-supplied string ever becomes markup and no remote address ever reaches the page. It is worth serving, and it is a decision about what `ClientApplicationFiles` attaches to the bundle it serves and what `tauri.conf.json` declares under `security` — neither of which is today set to anything, and neither of which is this record's to settle. [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) named a content security policy among the things the stack makes reachable rather than answers, and it is still that.

### No frame, and why a frame was the right answer to a different question

B2 is the correct shape for rendering hostile markup, and rendering hostile markup is not what happens here. Adopting it anyway would buy isolation against a document that never arrives, and it would cost three things the pane needs:

- **Selection across the boundary is impossible.** An opaque-origin frame cannot have its selection read by the parent, so [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426)'s requirement that a selected fragment of the body be application state the intent field can read as scope could not be met at all.
- **Height cannot be measured without letting script run.** An opaque origin hides the frame's own layout from the parent, so a frame either scrolls inside the pane — nested scrolling on the one surface people read longest — or the sandbox is opened for a bridge script, which reintroduces a script boundary to solve a problem created by removing one.
- **The content becomes a second document.** Focus order, the pane's own keyboard path, and the reading order a screen reader follows all stop at the frame edge, and the accessibility obligations in [`frontend/src/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/src/AGENTS.md) are written about a surface, not about an embedded page.

B3 costs all of that and a second published origin besides, which is a deployment surface with its own listener, its own certificate, and its own place in the documentation, for the same nothing.

### And no frame beside it either: *show the original* has no representation to show

B4 answers the three objections above by not being the pane — a modal frame needs no selection read across its boundary, measures no height because it scrolls in a window of its own, and takes the focus order of a dialog rather than of a reading surface. So it has to be refused on what it would *carry*, not on where it would sit, and the question becomes which representation crosses the wire. There are three, and each fails before the frame is reached.

- **The sanitized HTML the service already produces (A2) is not what the reader asked for.** `EmailHtmlSanitizer` allows no URI scheme at all, so every `href`, every `src`, and every `cid:` is gone: what would open is the sender's layout with every picture missing and every link inert — strictly less than the document tree beside it, which at least carries inline images as `data:` and links as `MailDocumentLink`. A control called *show the full HTML version* that draws a thinner message than the pane it was pressed from is a control that answers nobody, and it would carry the whole cost of rendering markup to do it.
- **The message's own HTML (A5) is what the reader asked for, and it is what every load-bearing row was written against.** It is the string this record exists so that nothing hands to a renderer.
- **A third representation, sanitized differently for this one surface, is C1 and C3 arriving together under a new name.** It would be a second security judgement, taken for a second surface, whose completeness has to be argued separately from the first — which is the thing the whole record is written to avoid having to do once, let alone twice.

**The frame does not close the gap A5 opens, and the specific way it fails is the read receipt.** `sandbox=""` applies every sandboxing flag the HTML Standard defines, and the whole set is about what the framed document may *do*: run script, submit a form, navigate, open a popup, hold an origin, lock the pointer, start a download. Not one flag governs what it may *fetch*. A sandboxed `srcdoc` frame loads remote images, stylesheets, and fonts exactly as an unsandboxed one does, so a message opened in it reports itself read to the sender at the instant the frame is attached — before any control offering to load pictures has been drawn, and on the path a reader took *because* something was wrong with the message. That is the failure D1 exists to make impossible, arriving through the one door left open. The design project's own frame carried a footer telling the reader that scripts and remote content were both blocked; the first half is true and the second is not, and a promise the platform does not keep is worse on this surface than no promise at all.

Nothing recovers it. A policy that would stop the fetch has to reach the framed document, and a `srcdoc` document can only be handed one in its own markup — a `<meta>` element written above a stranger's markup that carries `<meta>` elements of its own. That is a mechanism whose behaviour is the engine's rather than the contract's, which is the divergence between WebView2 and WebKitGTK this record exists to prevent, arriving on the property that matters most. And stripping the addresses out of A5 before it crosses the wire is not A5 any more — it is the reduction, which is where the reader already was.

**So four of the five load-bearing rows stop holding at once, which is the case the *Revisit if* clause below anticipated.** Row one goes because a renderer is handed markup and the contract's shape stops being what holds anything; row three goes because a `<style>` element is back, scoped to a frame rather than to nothing; row four goes for the reason just given; row five goes because a `javascript:` target is no longer filtered by an allow-list but by whatever the frame's navigation flags happen to be.

Row two is the one that holds, and it is what settles this in the tree rather than in a review. The lint rule refuses `srcdoc` in both forms already — as a JSX attribute and as a member written on an element — so the control cannot be built under `frontend/src/` without first relaxing the rule that states this decision, and [`frontend/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/AGENTS.md) is where relaxing a rule to admit the code that failed it is refused. That is the shape F2 was chosen for: the question arrived as a design and met a build failure rather than a reviewer's memory.

**The design that drew the control is corrected rather than the code being written to match it.** The client is built from a design, and where the two disagree the design ordinarily wins — that is what makes it worth having. This is the exception it is held under: where implementation proves a design wrong, the design moves to the shape that actually ships, because a screen fixed only in the code leaves the thing everybody reads still saying the opposite. So the control and the frame behind it are to come out of the design, and no screen draws them. That removal has not happened yet — it is [#1483](https://github.com/Krzysztof318/MailFathom/issues/1483), named here rather than left for a reader to discover by opening the design and finding the control still there.

### No sanitizer in the client, and no package to pin

C4 follows from A3 rather than being a separate risk taken. A client drawing a closed tree with its own typed controls is not sanitizing anything, so a sanitizer there would have nothing to filter — it would run over strings that are already text, in a document that has no markup to parse.

**No package is required by this decision, and none is to be added by the change that implements it.** The obvious candidate is `dompurify`, and it is named here so that nobody re-derives the question: at `3.4.14` it is dual-licensed `MPL-2.0 OR Apache-2.0`, so the Apache arm would be elected and the licence review would be routine under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md). That is a survivable cost and it buys nothing on this path, so it is not paid.

**The browser's own `Sanitizer` API is not the alternative either.** As of this record it is not Baseline — Firefox shipped `setHTML()` first, Chromium followed, and WebKit has begun no implementation — so a client resting on it would sanitize on Windows under WebView2 and not on Linux under WebKitGTK, which is precisely the divergence between the heads this record exists to prevent.

**The service's sanitizer stays where it is and is unaffected.** `EmailHtmlSanitizer` continues to serve `IncludeSanitizedHtml` for the surface that asks for markup, under an allow-list that admits no URI scheme at all, and C1 stays refused for the reason it always was: what is stored is the message as it arrived, so a sanitizer improved next year applies to mail received last year, and an audit reads what was received rather than what a filter left of it.

### The client's own trust boundary is a validating parser, not a filter

The document arrives over HTTP, and `frontend/src/AGENTS.md` admits no unvalidated value out of `Client.Backend`. So the tree is parsed rather than cast: every block is matched against a known type and a known revision, every colour against `#rrggbb` exactly, every link target against the three permitted schemes, every inline image source against a `data:` URI whose media type is one the service will produce, and every collection against a bound checked during the walk rather than after it. The bounds `MailDocumentBounds` states — depth, blocks, runs per block, characters per run, table rows and cells, inline images and their octets — are what the client checks against, so a document larger than the service will compose is refused rather than rendered.

**An unknown block type or block revision is a placeholder, and an unknown shape for a known one is a refusal.** The first is a deployment ahead of the client, which the contract anticipates by versioning each block beside `MailDocument.SchemaVersion` and which costs that reader one block. The second is a document this deployment did not produce, and it is refused with the rest of the tree.

**The pane does not linkify plain text.** A link is a `MailDocumentLink` or it is words. Finding addresses in the text and making them clickable would be the client deciding what a link is, which is what `MailLinkDeception` exists to stop two clients from disagreeing about.

### Remote pictures are a request, and nothing on either side remembers it

D1 is already half-decided by the service — `RetainRemoteImageReferences` is per message, the query is the whole of the state, and nothing there writes it down — and this record settles the client's half the same way: **nothing durable, and nothing beyond the message that is open.** Asking for pictures re-reads that one message with `remoteImages=true`; leaving the message and coming back asks again.

**D3 and D4 are refused as privacy decisions rather than as scope.** A durable per-message record is a list, in browser storage on whatever machine somebody signed in from, of which messages they cared enough about to load pictures for — mail metadata by another name, kept for a preference whose entire cost is one click. A per-sender rule is worse: it is a standing decision that reports every future message from that sender as read, taken once, by somebody who then has no place to see what it is still doing. Neither is bought back by a *forget these* control, because the control is a second screen for a problem created by remembering.

The reader is told what the request reveals before it is made, and the pane says how many references were removed rather than leaving a gap where a picture was — `RemovedRemoteReferenceCount` is served for exactly that.

### A refused document is the plain text with the refusal named

`MailDocumentRefusal` distinguishes `NoHtmlPart`, `ReductionFailed`, and `NothingRenderable`, and the plain text travels beside the document in every case, so the pane never has to ask twice and never draws an empty area. Each refusal is a different sentence, because they are different facts about the message: one says the sender wrote no markup, one says this deployment could not read what they wrote, and one says what they wrote reduced to nothing.

`Truncated` is not a refusal and does not fall back — a document a bound stopped is a document, drawn as far as it goes with the reader told it was cut short. `Availability` is a third axis again, and its three non-readable states — `EncryptedNotReadableLocally`, `NotStoredExceededSizeLimit`, `NotStoredAwaitingStorageHeadroom` — are each their own state on the screen, since a message nothing can decrypt and a message waiting for storage headroom lead a reader to different actions. E3 is refused for the reason the UX contract already gives: a fallback nobody is told about reads as a message the sender sent badly.

### What a reader who lost something is given instead

G2. A message that reduced badly is a fidelity complaint, and this record already said where such a complaint is answered: in the reduction, on the service side, as a wider block catalogue arriving with a schema revision of its own. That is slower than a control on the message head, and it is slower on purpose — it is the difference between one reader getting their document back and every reader of that kind of message getting it back, and between a judgement taken once in the service and a judgement taken by whoever presses the control.

Two things make it a real answer rather than a deferral. `MailDocumentRefusal` and `Truncated` already tell a reader *which* of the three things happened to their message, so a reduction that failed is reportable as a defect against a named state rather than as a message that looks wrong. And the block catalogue is versioned per block beside `MailDocument.SchemaVersion`, so widening it costs a client that has not caught up one block rather than the message — which is what makes widening it a routine act instead of a breaking one.

G1 is refused because a guarantee with no route for the complaint it creates is a guarantee that gets removed later, under pressure, by somebody who has only the control to reach for. G3 — handing the reader the message itself, to open in a program they already chose — is not refused here and is not decided here either: it moves nothing inside this client's trust boundary, which is why it survives the reasoning above. What it does do is take a copy of the message out of the deployment on a person's instruction, and what that costs — where the copy lands, what is left behind about it having been taken, and what the person is told before it is — is a question this record has no reasoning for and does not inherit from one. It has no issue yet, this record does not open one for it, and whichever record eventually answers it establishes its own footing.

### Both heads draw the same tree, and the one difference belongs to the shell

Nothing in this decision depends on an engine capability, a platform API, or a browser version: it is React elements over a JSON tree, and it renders identically under WebView2 and WebKitGTK because there is nothing in it for the two to implement differently.

The single difference is what *following* a link means. On the web head it is a new browsing context; on the desktop head it is the system browser rather than the application's own window, because a WebView that navigates to a sender's page has replaced the application with it. `frontend/src/AGENTS.md` forbids the tree branching on which head it is running on and permits the difference to live in the shell, so the application asks for a link to be opened and the desktop shell is what routes that to the system browser.

That is the shape [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) already established for the credential store, and this record takes it rather than inventing a second one: the application depends on one operation, which implementation satisfies it is decided once at the composition root by whether a shell offered the command, and no screen, component, or hook learns which head it is running on. Which Tauri mechanism does the routing, and whether it needs a command and a capability of its own, is [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428)'s to choose.

### How the isolation is proven

F2, split by what each suite can actually answer, per [`frontend/tests/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/tests/AGENTS.md):

- **The unit suite proves the parser.** The document parse is pure logic over values, so every refusal is reachable for nothing: a block type that is not in the catalogue, a known type at an unknown revision, a colour in any other notation, a link target carrying `javascript:`, `data:`, or `file:`, an image source that is not a permitted `data:` media type, and each bound exceeded by one. A malformed body is asserted to be refused rather than read as a tree with a hole in it.
- **The unit suite proves the components as attacks rather than as examples.** A block whose text is `<script>` markup renders as those characters; a link whose `Deception` is `DisplayedHostDiffers` renders as that; an unimplemented block pair renders a placeholder and the rest of the message.
- **The browser suite proves what only a browser can.** Against the built bundle: that drawing a message issues no request to any host but the page's own origin, that the pane's document holds no `iframe`, `script`, `object`, or `form` element, that message content is reachable by role and by name rather than being an opaque surface, and that following a link opens a new context rather than navigating the application.
- **The lint rule is the proof that does not depend on anybody writing a test.** `no-restricted-syntax` refusing every markup-writing call under `frontend/src/` — the list in the table above, `srcdoc` included — fails the build rather than the review, which is the same shape the localization rule already takes in `eslint.config.ts`. Adding a restriction is not the configuration relaxation `frontend/AGENTS.md` refuses; it is the opposite, and it belongs to [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428).

### Consequences

- Good, because the safety property is one sentence a reviewer can hold — the client is handed no markup and writes none — rather than a filter whose completeness has to be argued each time somebody publishes a bypass.
- Good, because there is one renderer for both heads and nothing in it that an engine can implement differently, so a rendering defect has one place to be fixed.
- Good, because the pane is ordinary content: selectable, focusable, in the reading order, and styled from the token layer, which is what lets [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426)'s selection-as-scope and the accessibility obligations be met at all.
- Good, because no package is added to the most security-sensitive path in the client, so there is no permanent patch obligation and no licence condition to carry into the desktop artifacts.
- Neutral, because the client's own trust boundary moves from a filter to a parser, which is work either way — but work whose failure mode is a refused message rather than a rendered attack.
- Neutral, because the decision inherits the service's reduction wholesale, so a fidelity complaint about how a message looks is a defect in the reduction rather than in the pane, and it is answered on the service side.
- Bad, because a message will not look exactly as its sender composed it. The reduction keeps blocks, emphasis, alignment, tables, and colours, and drops everything a closed catalogue cannot express, so a heavily designed newsletter is legible rather than faithful. This is the price of the whole decision and it is paid deliberately.
- Bad, because the client cannot render anything the reduction does not yet cover, and widening it is service work with its own schema revision rather than a change in the pane — which is the right place for it and is still the slower place.
- Bad, because refusing a durable override means somebody who reads one newsletter every week asks for its pictures every week, and the record offers them nothing else.
- Bad, because the client page is still served with no content security policy on either head, and this record explains why the pane does not need one without closing that gap for the application around it.
- Good, on the second question, because the safety property stays one sentence rather than two: there is no surface of this client where a different answer holds, so nobody has to know which surface they are on to know what is true.
- Bad, on the second question, because a reader whose message reduced badly is told that the fix is a release of the service rather than a control in front of them, and for that reader the honest answer is worse than the dishonest one.
- Bad, because refusing it costs the design project a control it had already drawn, and a source of truth that has to be corrected by the code it governs is a source of truth that was wrong for as long as nobody read it against this record.

## Validation

By review of the change that implements it, against the acceptance of [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428), and by the four proofs above — the parser's refusals, the components under attack-shaped input, the browser suite's assertions about what the page fetched and what its document holds, and the lint rule, which fails a build rather than a review and is the one of the four that cannot be forgotten. That the client never asks for markup is held by `ClientMailBodyEndpoint.RequestFor`, which is already asserted on the service side and is the seam that exists for it.

The second question is validated by there being nothing to build: the refusal is held by the same `RequestFor` assertion, since no representation carrying markup is ever requested, and by the browser suite's existing assertion that the pane's document holds no `iframe`. Neither of those covers a frame added on some other screen, and no assertion is added for one — a control drawing a stranger's markup is refused in review against this record, which is where a control that nothing in the tree anticipates has to be caught.

## Pros and Cons of the Options

### A1 — the message's own HTML, sanitized in the client

- Good, because the highest fidelity to what the sender wrote is available, everything the reduction drops included.
- Neutral, because the sanitizer would be a well-maintained package rather than something written here.
- Bad, because it makes the client the place a security judgement is taken, which is the one thing the client contract refuses, and puts a permanent bypass-tracking obligation on a screen.
- Bad, because it needs the frame and the policy underneath it as well, since a sanitizer alone does not stop a remote image or a style escaping.

### A2 — the sanitized HTML the service already produces

- Good, because it exists, is reviewed, and admits no URI scheme at all.
- Bad, because handing markup to a renderer puts the decision the sanitizer just took back where it was taken from: a renderer resolves references, runs what it recognizes, and treats an unfamiliar construct as something to try.
- Bad, because that representation strips every reference including `cid:`, so inline pictures are gone and the pane would show a message the sender did not send while carrying all the risk of markup.

### A4 — the plain text alone

- Good, because it is the smallest surface there is, and the plain text is already a first-class rendering.
- Bad, because a mailbox is mostly composed mail, and a client that cannot draw a table or a heading is not one somebody moves to.

### A5 — the message's own HTML, unmodified

- Good, because it is the only thing that is actually *what the sender sent*, and it is what a reader asking to see the original is asking for.
- Neutral, because the service holds it: the message is stored as it arrived, so serving it is a representation to add rather than data to keep.
- Bad, because it carries every remote address the reduction exists to remove, so opening it is the read receipt this record refuses on every other path.
- Bad, because it puts a stranger's markup in front of a renderer, which is the single thing the load-bearing rows in the table above are all written against.
- Bad, because adding it to the client route would make `ClientMailBodyEndpoint.RequestFor` — the one seam that proves the client never asks for markup — a place where the answer depends on which surface asked.

### B4 — the frame as a second surface beside the pane

- Good, because it costs none of the three things B2 costs the pane: no selection crosses it, no height is measured through it, and a dialog's focus order is the right one for it.
- Neutral, because it works identically on both heads, exactly as B2 does.
- Bad, because no sandboxing flag governs what the framed document fetches, so it stops script and leaves the tracking pixel — and a footer saying otherwise makes the reader trust it more than the pane they came from.
- Bad, because the only mechanism that would close that is a policy carried in the framed document's own markup, which is where the two engines diverge and where the sender's markup is a party to the argument.
- Bad, because building it means relaxing the lint rule that already refuses `srcdoc`, which is the one change `frontend/AGENTS.md` refuses outright — so the option is not merely unwise here, it is unwritable without editing the statement of this decision first.

### G1 — the reduction is what there is

- Good, because it needs nothing built and states the trade-off honestly.
- Bad, because a guarantee whose complaints have nowhere to go is a guarantee somebody removes later under pressure, with only the refused control to reach for.

### G2 — a wider block catalogue

- Good, because it fixes the message for every reader of that kind of message rather than for the one who pressed a control, and it fixes it where the judgement about what is safe to draw already lives.
- Good, because the contract is already shaped for it: each block carries its own revision beside `MailDocument.SchemaVersion`, so a client behind the deployment loses one block rather than the message.
- Bad, because it is service work with a schema revision behind it, so the reader who complained today is answered in a release rather than in a press.

### G3 — hand the reader the message to open elsewhere

- Good, because it moves nothing inside this client's trust boundary: whatever renders it is a program the reader chose, on their own account of the risk.
- Neutral, because the service already stores the message as it arrived, so there is nothing to derive.
- Bad, because it takes a copy of the message out of the deployment, which raises questions about where that copy lands and what the person is told first that a record about rendering has no reasoning for.

### B2 — a sandboxed frame with `srcdoc` and an opaque origin

- Good, because it is the correct mechanism when hostile markup genuinely has to be rendered, and it holds script, style, and top-level navigation without depending on a filter.
- Neutral, because it works identically on both heads.
- Bad, because the parent cannot read an opaque-origin frame's selection, which makes a required feature of the reading pane impossible rather than merely awkward.
- Bad, because measuring the frame's height needs script inside it, so the sandbox is opened to solve a problem the sandbox created.
- Bad, because focus order, the keyboard path, and screen-reader reading order stop at the frame edge.

### B3 — a frame served from a second origin

- Good, because a real origin boundary is stronger than an opaque one and survives a mistake in the `sandbox` attribute.
- Bad, because it carries every cost of B2 and adds a second published origin — a listener, a certificate, a documented deployment surface — to isolate content that carries nothing.

### C1 — sanitize before storage

- Good, because nothing dangerous would ever be at rest.
- Bad, because the stored message would no longer be the message that arrived, which breaks re-reduction under an improved sanitizer and breaks what an audit is entitled to read.
- Bad, because the filter's rules would be frozen at the moment each message was received, so a fix applies only to mail that has not arrived yet.

### C3 — sanitize in the client before rendering

- Good, because it is where a renderer's own quirks are.
- Bad, because there is nothing to sanitize on this path: it would parse markup that does not exist, and the first thing anybody would do to make it useful is ask the service for markup.

### D2 — remember in memory while the message is open

- Good, because it survives a re-render and writes nothing to disk.
- Neutral, because it is nearly D1: the difference is only whether closing and reopening the message asks again.
- Bad, because it puts a second piece of state beside the request that already carries the answer, and two things that must agree is one thing and a function.

### D3 — remember durably per message

- Good, because a newsletter somebody reads repeatedly stops asking.
- Bad, because the store becomes a list of which messages this person cared about, held in the browser of whatever machine they signed in from, for a preference that costs one click to restate.

### D4 — a per-sender rule

- Good, because it is the control mail clients have taught people to expect.
- Bad, because it is a standing decision to report every future message from that sender as read, taken once and thereafter invisible to the person who took it.

### E1 — an empty pane

- Good, because it needs no wording.
- Bad, because it is indistinguishable from a defect, which is the state the UX contract exists to refuse.

### E3 — fall back silently

- Good, because it always shows something.
- Bad, because the reader cannot tell a plain-text message from a message this deployment could not read, so a reduction failure looks like a badly composed email and is never reported.

### F1 — prove it by review

- Good, because it costs nothing to set up.
- Bad, because the one call that would undo the decision is a single identifier somebody adds under deadline, and review is exactly what stops catching that once there are enough screens to read.

## More Information

- [Email content § HTML sanitization](../features/email-content.md#html-sanitization) and [§ The document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws) hold the two representations this record chooses between, including why they are cut from separate parses and what each of the document's bounds is for.
- [The client endpoint](../operations/client-endpoint.md) is the transport the body arrives over, and `ClientMailBodyEndpoint` is where the choice never to ask for markup is written down.
- [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) is why there are two heads on two engines, and it named a content security policy among the questions this stack makes reachable rather than answers — which is still open, and is about the application document rather than about the pane.
- [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) is where the *application asks the shell, never the platform* shape was decided, and opening a link out of the desktop head is the second operation to take it.
- [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) is what a sanitizer package would have been reviewed against, and is why the dual licence above is recorded even though no package is added.
- This record replaces the removed ADR 0019 rather than restoring it. `0018`, `0019`, and `0020` were withdrawn with the client they decided about, and their numbers are not reused.
- [#1477](https://github.com/Krzysztof318/MailFathom/issues/1477) is where the second question was asked and where this amendment was written. It produces no code. **The design project still draws the control as this record is written, and correcting it is [#1483](https://github.com/Krzysztof318/MailFathom/issues/1483)** — outstanding work rather than something this record reports as done, so anyone reading the two together should expect them to disagree until that issue closes, and this record is the one that holds meanwhile. [#1478](https://github.com/Krzysztof318/MailFathom/issues/1478), which redraws the message head the control sits on, is the change that has to arrive without it; it carries that instruction on the issue rather than inheriting it from a design that has not moved yet, which is why it does not wait on #1483.
- The sandboxing flag set is the HTML Standard's, in [§ Sandboxing](https://html.spec.whatwg.org/multipage/browsers.html#sandboxing). It is read here for what it does not contain: no flag governs subresource fetching, which is why `sandbox=""` is not an answer to a remote picture on any surface of this client.
- Revisit if the reduction turns out to lose something readers actually miss, at which point the answer is a wider block catalogue with a schema revision rather than markup on any path — the pane's, or a second surface beside it; if a reader's need for the message itself turns out to be worth answering, at which point it is answered by handing the message *out* of the application — under a record of its own, since nothing here reasons about a copy leaving the deployment — and never by rendering it inside one; if a future body form arrives that is markup by necessity, at which point B2 is the mechanism and this record's reasons for refusing it are the requirements it would have to answer; or if the client is ever asked to render something a deployment did not reduce, at which point every load-bearing row in the table above stops holding at once.
