---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-02
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Draw mail as the closed document tree the service already reduces it to, in the client's own document with no frame and no sanitizer, let a remote picture be a request nothing on either side remembers, and show the sender's own markup on a second surface only when a reader asks for it, in a frame, from a representation that carries no address at all

<!-- describes: frontend/src/Client.App/src/**, frontend/src/Client.Backend/src/**, backend/src/Host/Api/ClientMailBodyEndpoint.cs, backend/src/Application/EmailContent/Rendering/Document/** -->

## Context and Problem Statement

Mail HTML is written by strangers and is the most hostile input this application handles. It carries CSS that will escape whatever element it is given, scripts, iframes, forms, and remote images whose only function is to tell the sender that the message was opened. How a reading pane draws it was decided once, in ADR 0019, and [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392) removed that record with the Uno Platform client it was written about — its answers described a WebAssembly bundle and a desktop `WebView`, and neither describes what runs now.

The question is being asked again for a React client that ships as a static web bundle and as a Tauri desktop application from one tree, per [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md). The web head is the hard case and would ordinarily decide the shape: the application *is* a page there, so anything rendering mail HTML sits in the same browser as the application's own origin, and a sandboxed frame, a content security policy, and a sanitization pass are the three parts to weigh against each other. The desktop head then has to arrive at the same answer rather than a second one, because it renders in the operating system's WebView — WebView2 on Windows, WebKitGTK on Linux — and a mechanism that depends on what an engine has implemented is a mechanism that diverges by platform.

What the question turns out to rest on is a contract the service already publishes. `GET /api/client/messages/{id}/body` never asks for markup: it sets `IncludeMailDocument` unconditionally, never sets `IncludeSanitizedHtml`, and answers with the plain text beside `MailDocument` — [the document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws), a closed tree of typed blocks in which every leaf is text, a number, an opaque colour, or a member of a fixed enumeration. There is nowhere in it to put a script, an event handler, an embedded object, a form, a style sheet, or an element. So the client is not handed mail HTML at all, and the decision in front of the client is not *how to render hostile markup safely* but *whether to keep it that way and what the client then owes*.

Recorded on issue [#1427](https://github.com/Krzysztof318/MailFathom/issues/1427). It implements nothing: [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428) builds what this names, [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426) is the pane it is drawn inside, and [#1430](https://github.com/Krzysztof318/MailFathom/issues/1430) is the parent all three sit under. Nothing in the client renders a body today.

**A second question was asked of this record on [#1477](https://github.com/Krzysztof318/MailFathom/issues/1477), and this record answers it while still `proposed` rather than standing beside a second one.** The design project drew a control on the message head — a `code` glyph titled *show the full HTML version* — opening the message's own markup in a surface of its own, a `srcdoc` frame carrying `sandbox=""` and a footer telling the reader that scripts and remote content are blocked. That is not the question the paragraphs above answered. They answered *what the reading pane draws*, and this asks whether a **second** surface, beside that pane and not replacing it, may show what the sender actually sent — for the reader whose message did not reduce well and who is looking for a document the reduction lost.

The second surface is taken, and the reduced tree stays what a message opens as. What the question costs is not the frame and not the markup: it is that `sandbox=""` stops a script and does not stop a fetch, so the surface that shows the sender's layout is also the surface on which a tracking pixel would fire. The answer below is therefore about the *representation* rather than about the container — the markup crosses the wire with every remote address already gone, which is the same mechanism the tree's own anti-tracking property rests on rather than a second one invented for a frame.

**A third question was asked on [#1527](https://github.com/Krzysztof318/MailFathom/issues/1527), and this record answers it in place for the same reason it answered the second one.** The design project has since drawn a *second* use of that frame, which nothing above weighed: an embedded HTML view, chosen in Settings and built by the embedded-HTML half of [#1508](https://github.com/Krzysztof318/MailFathom/issues/1508), drawing every open message's markup inline in the conversation rather than in a dialog opened one message at a time. [#1507](https://github.com/Krzysztof318/MailFathom/issues/1507) found the prototype's frame carrying `sandbox="allow-same-origin"` and an `onLoad` handler reading the framed document's `contentDocument` to fit the frame to its content. `allow-same-origin` is refused under every reading in this record: a framed document same-origin with the client page reaches that page's DOM, its storage, and its cookies, which is no sandbox at all applied to the most hostile input this application handles.

What the handler was reaching for is a real requirement rather than a slip, and it is the one objection the paragraphs above answered by *not being the pane*. A dialog has a size the page gives it and scrolls inside itself. An embedded view has to draw every message at its full height inside one scrolling conversation, and no CSS property, no attribute, and no shipped platform feature fits a sandboxed frame to its content — so on that surface the choice is between a height the page guesses and a frame that runs one script the client wrote. This record chose the first when the only surface in view was the dialog, and the paragraph and the option bullet where it did are corrected below rather than left standing against the answer.

The answer is that the two markup surfaces carry two different `sandbox` values, because they have two different problems. The dialog keeps `sandbox=""`. The embedded frame carries `sandbox="allow-scripts"` and nothing else — no `allow-same-origin`, so the framed document holds an opaque origin and reaches nothing of the page — and the client prepends its own measuring script to the `srcdoc`, ahead of the message markup, which reports the height by `postMessage`. What then holds *nothing in the message runs* on that surface is no longer the sandbox but the representation: [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484) serves markup with nothing executable in it. That is a property moving off the platform and onto a build, and the tables and consequences below count it as one rather than presenting it as free.

## Decision Drivers

- **The safety property has to be statable, not merely present.** A policy nobody can write down in a sentence is a policy nobody can review, and a rendering path whose safety rests on the completeness of a filter is one whose safety is only as good as this year's bypass.
- **One answer for both heads.** The two heads run different rendering engines, so anything resting on a recently shipped platform capability is an answer on one head and a gap on the other. A rendering defect must have one place to be fixed.
- **Two implementations of one judgement will disagree.** [`frontend/src/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/src/AGENTS.md) already refuses the client re-deriving anything the service decides, and what is safe to draw is exactly such a judgement.
- **A tracking pixel is defeated by absence, not by a setting.** A renderer that holds a remote address and declines to fetch it is one defect away from fetching it; a document that does not carry the address cannot.
- **What is remembered about a reader's override is a record of what they read.** Which messages somebody chose to load pictures for is a list of the messages that mattered to them, and it is personal data wherever it is written down.
- **A dependency here is the most security-sensitive package in the tree.** Whatever sanitizes mail owns a permanent patch obligation and a licence entry, and it is worth having only if something it does is load-bearing.
- **The pane is a reading surface, not an embedded document.** [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426) requires that a fragment of the body can be selected and that the selection is application state, and the accessibility obligations require the content to be reachable rather than opaque.
- **A reader who says the reduction lost something is reporting a real loss, and a mail client that cannot show them what arrived is one they leave.** The reduction keeps what a closed catalogue can express and drops the rest, so a heavily designed message is legible rather than faithful — and *legible rather than faithful* is a judgement the reader is entitled to check rather than one they have to accept on the client's word.
- **A second surface is the same client.** Whatever is true of the reading pane's isolation has to be true of anything else in the same application that draws the same message, because a reader does not hold two trust boundaries and a tracking pixel does not care which surface fetched it. A second surface is therefore worth having only where every property below is answered on it too, by a mechanism rather than by a sentence on the screen saying so.
- **A surface somebody reads down and a surface somebody opens are not the same surface.** A dialog is entered deliberately, has the size the page gives it, and may scroll inside itself; a message drawn inline in a conversation has to be whole, at whatever height it turns out to be, or the reader is scrolling inside a scroll on the surface they spend the most time on. So a mechanism that is unnecessary on one of them can be load-bearing on the other, and the same *is this frame worth it* question genuinely has two answers rather than one that ought to have been applied twice.
- **A flag is granted for what it buys, not withheld as a posture.** `sandbox` is a set of independent flags rather than a dial, and the cost of granting one is exactly what that flag permits — so the question on each surface is which flag buys something there and what it costs *there*, and an answer that grants none on every surface because none was needed on the first one is a rule of thumb rather than a decision.

## Considered Options

**A — what the client is handed, and therefore what it renders:**

- A1 — the message's own HTML, sanitized in the client immediately before it is inserted.
- A2 — the sanitized HTML representation the service already produces for `IncludeSanitizedHtml`.
- A3 — `MailDocument`, drawn with the client's own typed components.
- A4 — the plain text alone, and no rendering of the sender's layout at all.
- A5 — the message's own HTML as it arrived, unmodified, which is the only representation that is literally *what the sender sent*.
- A6 — the message's own HTML with every remote address removed while it is prepared and every `cid:` part inlined as a `data:` URI: the sender's markup, carrying nothing that resolves outward.

**B — where the rendered body sits in the document:**

- B1 — in the application's own document, as ordinary React elements.
- B2 — in a sandboxed `iframe` with `srcdoc`, an opaque origin, and a content security policy of its own.
- B3 — in an `iframe` served from a second origin the deployment publishes.
- B4 — B2's frame opened as a second surface *beside* the pane rather than as the pane, on a control the reader presses.
- B5 — B2's frame drawn inline for every open message in the conversation, on a standing preference rather than on a control pressed per message.

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

**H — how an inline frame is fitted to the message inside it, which is a question only B5 asks:**

- H1 — the page picks the height, from a constant or from a guess made about the markup before it is drawn.
- H2 — a fixed height and the frame scrolls inside itself, which is nested scrolling on the conversation.
- H3 — `allow-same-origin`, so the parent reads the framed document's `contentDocument` and measures it directly.
- H4 — `allow-scripts` and no `allow-same-origin`, so the frame keeps an opaque origin and the client's own script, prepended to the `srcdoc` ahead of the message markup, reports the height by `postMessage`.

## Decision Outcome

Chosen: **A3, B1, C4, D1, E2, and F2** — the client is handed the closed document tree and never mail HTML, draws it as ordinary elements in the application's own document, sanitizes nothing because nothing sanitizable reaches it, treats a remote picture as a second request nothing on either side of the boundary writes down, falls back to the plain text with the refusal named, and proves the whole of it mechanically rather than by review.

On the second question: **A6 and B4, with G2 still the answer to a reduction that loses something.** A message opens as the reduced tree and always does; a control on its head opens a second surface showing the sender's own markup, in a sandboxed frame, from a representation the service prepared by removing every remote address and inlining every `cid:` part. The frame is what stops the markup *running*; the removal is what stops it *reporting*. Neither substitutes for the other, and the footer on that surface says what is actually true rather than what a sandbox is popularly believed to do.

The reduced tree is not demoted by this. It stays what a message opens as, it stays what the pane draws, and it stays where a fidelity complaint is answered — the second surface is a way to check the reduction rather than a replacement for improving it.

On the third question: **B5 and H4, and the two markup surfaces therefore carry two different `sandbox` values.** The embedded HTML view draws each open message's markup inline in the conversation, in a `srcdoc` frame carrying `sandbox="allow-scripts"` and nothing else — no `allow-same-origin`, no `allow-forms`, no `allow-popups`, no `allow-top-navigation` — and the client prepends its own measuring script to that `srcdoc`, ahead of the message markup, which reports `document.documentElement.scrollHeight` by `postMessage` for the parent to set the frame's height from. The dialog is unchanged and keeps `sandbox=""`. A6 is what both of them render, unchanged and not weakened by either.

**The property that moves is *nothing in the message runs*, and on the embedded surface it is held by the representation rather than by the sandbox.** Every previous paragraph in this record put that property on the frame there; granting `allow-scripts` takes it off the frame, and what is left holding it is that [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484) serves markup with nothing executable in it, so the only script that runs inside that frame is the one the client put there. The record states that plainly rather than letting the two decisions net out to a sentence about the surface being safe: it is a promise moved off a platform guarantee and onto a build that has to keep being right.

The reduced tree is not demoted by the embedded view either — it is what an unset preference reads as and what the surface falls back to, per [#1508](https://github.com/Krzysztof318/MailFathom/issues/1508).

### The client is never handed mail HTML, so it never renders any

`ClientMailBodyEndpoint` sets `IncludeMailDocument = true` on every read and sets `IncludeSanitizedHtml` on none, which is the whole of the mechanism. What crosses is the plain text and a `MailDocument`: a list of typed blocks — `paragraph`, `heading`, `list`, `table`, `quote`, `image`, `separator`, `preformatted` — whose leaves are text, numbers, a `MailDocumentColour` normalized to `#rrggbb`, and members of `MailTextEmphasis`, `MailBlockAlignment`, `MailLinkDeception`, and `MailDocumentRefusal`. A construct nobody anticipated cannot survive by being unfamiliar, because it has no shape to arrive in.

**The tree is reduced from the message's own parse rather than from the sanitizer's output.** That is the property that rules out the mutation attacks built out of one parser reading what another parser wrote, and it is the service's, stated in [Email content § The document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws). This record adds only that the client must not undo it, which it would do by asking for markup or by reconstructing any.

### What holds each property, and which parts are load-bearing

The five properties [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428) has to hold, each against what actually holds it — the same five the tables further down carry onto the two markup surfaces:

| Property | What holds it | Load-bearing, or defence in depth |
|---|---|---|
| No script, embedded object, form, or event handler executes | The document contract has nowhere to express one, and React escapes text | **Load-bearing: the shape of the contract.** Nothing about the browser is relied on |
| The client never turns a message value back into markup *on this surface* | A lint rule under `frontend/src/` refusing every way a value becomes markup: `dangerouslySetInnerHTML`, `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` and `document.writeln`, `setHTMLUnsafe`, `DOMParser.parseFromString`, `Range.createContextualFragment`, and an `iframe`'s `srcdoc` in either form — the last of these excepted in exactly one named file, which is the single component both markup surfaces below are drawn in | **Load-bearing.** Each of these is a call that would undo the decision, so each is made unwritable rather than reviewed, and the one exception is named in the configuration rather than waived at a call site |
| Message style cannot escape the pane | The only presentation a message contributes is an opaque colour, an alignment, and an emphasis flag, each applied by the pane's own component. No selector, position, size, or stacking order crosses the wire | **Load-bearing: the shape of the contract.** There is nothing to escape with |
| No remote resource is fetched before a person asks | Every remote address is removed while the tree is built; `RemovedRemoteReferenceCount` is what survives in its place | **Load-bearing: the service's removal.** The client holds no address to decline |
| A link cannot navigate the application | `MailDocumentLink.Target` carries `http`, `https`, or `mailto` and nothing else, and the client opens it out of the application rather than in it | **Load-bearing on both halves**: the scheme allow-list against `javascript:`, and leaving the application against a same-tab navigation that would discard the session |

**A content security policy on the application document is defence in depth, and this decision does not rest on it.** It would protect the application against a defect somewhere else in the client; it protects the pane against nothing, because no message-supplied string ever becomes markup and no remote address ever reaches the page. It is worth serving, and it is a decision about what `ClientApplicationFiles` attaches to the bundle it serves and what `tauri.conf.json` declares under `security` — neither of which is today set to anything, and neither of which is this record's to settle. [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) named a content security policy among the things the stack makes reachable rather than answers, and it is still that.

### No frame around the pane, and why a frame is still the right answer one surface over

B2 is the correct shape for rendering hostile markup, and rendering hostile markup is not what the *pane* does. Adopting it there would buy isolation against a document that never arrives, and it would cost three things the pane needs. The section after next takes the frame anyway, on a surface where none of these three is owed — so read what follows as the reason the pane is not a frame rather than as a reason nothing is.

- **Selection across the boundary is impossible.** An opaque-origin frame cannot have its selection read by the parent, so [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426)'s requirement that a selected fragment of the body be application state the intent field can read as scope could not be met at all.
- **Height cannot be measured without letting script run, and on the pane that price is not worth paying.** An opaque origin hides the frame's own layout from the parent, so a frame either scrolls inside the pane — nested scrolling on the one surface people read longest — or the sandbox is opened for a bridge script. On the pane that trade is refused outright, because the pane is handed no markup at all and a frame there would open the sandbox to solve a problem that only exists once a frame is introduced. **It is not refused everywhere, and this record originally said it was.** The embedded surface below has to draw markup inline at full height whatever this row decides, so there the same trade is between a guessed height and one script the client wrote, and it is taken — see *The embedded surface* and option H4. What stays true on the pane is the conclusion; what was wrong was stating the premise as though no surface could ever be worth it.
- **The content becomes a second document.** Focus order, the pane's own keyboard path, and the reading order a screen reader follows all stop at the frame edge, and the accessibility obligations in [`frontend/src/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/src/AGENTS.md) are written about a surface, not about an embedded page.

B3 costs all of that and a second published origin besides, which is a deployment surface with its own listener, its own certificate, and its own place in the documentation, for the same nothing.

### The second surface: a frame stops it running, and the representation stops it reporting

B4 is the pane's three objections answered by not being the pane. A modal frame needs no selection read across its boundary, because selection-as-scope belongs to the reading pane and this surface is not it. It measures no height, because it scrolls in a window of its own rather than inside a column somebody is reading down. And it takes a dialog's focus order — opened deliberately, trapped while open, returning focus to the control that opened it — which is an accessible shape rather than an obstacle to one. So the objections that made B2 wrong *as the pane* do not carry to B4 *beside* it, and what is left to settle is what the frame is handed.

**A6, and the reason it is not either of the two representations that already exist.** The sanitized HTML the service produces for `IncludeSanitizedHtml` allows no URI scheme at all: every `href`, every `src`, and every `cid:` is gone with them, so it would open the sender's layout with every picture missing and every link inert — thinner than the tree beside it, which at least carries inline images as `data:` and links as `MailDocumentLink`. A control called *show the full HTML version* has to draw more than the pane it was pressed from, not less. And the message's own HTML unmodified (A5) carries every remote address the sender put in it, which is the next paragraph.

So the service prepares a third representation for this surface, and what defines it is a property rather than a filter: **it carries nothing that resolves outward.** Every remote address is removed while it is prepared — the same removal, at the same point in the pipeline, that `MailDocument` already gets — and every `cid:` part is inlined as a `data:` URI, so the pictures the sender actually attached are there while the ones that would have been fetched are not. `IncludeSelfContainedHtml` is the shape it takes on `GetEmailContentRequest`, beside the two that exist, and it is cut from the message's own parse rather than from the sanitizing pass's output, for the reason [Email content § The document a reading pane draws](../features/email-content.md#the-document-a-reading-pane-draws) already gives about the other two.

**That property is load-bearing, and the sandbox is not what provides it.** `sandbox=""` applies every sandboxing flag the HTML Standard defines, and the whole set governs what the framed document may *do*: run script, submit a form, navigate, open a popup, hold an origin, lock the pointer, start a download. Not one flag governs what it may *fetch*. A sandboxed `srcdoc` frame loads remote images, stylesheets, and fonts exactly as an unsandboxed one does — so a surface built on A5 would report the message read to its sender at the instant the frame was attached, before any control offering to load pictures had been drawn. The frame is the answer to *executes*; only the representation is an answer to *reports*. Both are promised on the surface and both are kept, by two mechanisms rather than by one — what this record refuses is a build that keeps the promise and drops half of what holds it.

Nor is a content security policy the missing piece. One that reached the framed document would have to travel in the `srcdoc` document's own markup — a `<meta>` element written above a stranger's markup that carries `<meta>` elements of its own — which is engine behaviour rather than contract, and the two heads are exactly where this record refuses to depend on that. It is worth writing as defence in depth and it holds nothing on its own.

**Remote pictures on this surface are the same request, under the same rule.** Asking for them re-reads the one message with `remoteImages=true`, which is what `RetainRemoteImageReferences` already means, and the self-contained representation is prepared with the addresses left in for exactly that read. D1 governs it unchanged: per message, the query is the whole of the state, and leaving the message and coming back asks again. So the reader who wants the sender's design *and* the sender's remote images makes one decision, on the same terms and with the same disclosure as they would in the pane.

### The embedded surface: the same frame and the same representation, and the one flag that separates them

B5 is B4 drawn inline. The markup is A6's, the container is the same `srcdoc` frame written in the same one file, and the reader chose it once in Settings rather than pressing a control per message — so the head's `code` control is not drawn in that mode, there being nothing left for it to open. Everything the section above settled about *what the frame is handed* carries over untouched, and one question it never had to ask arrives with the surface: **how tall is the frame.**

A dialog answers that by having a size the page gave it. An embedded view cannot: every open message in the conversation is drawn whole, and the conversation is the only thing that scrolls. H2 — a fixed height with the frame scrolling inside itself — is a scroll inside a scroll on the surface people spend the most time on, and it is refused on the UX obligations rather than on anything in this record. H1, a height the page picks, is refused because the page has nothing to pick it from: the markup is a stranger's and its rendered height is a function of the engine, the viewport, the fonts that resolved, and the pictures that decoded, so every guess is wrong by an amount the reader sees as a gap or as a message cut off.

That leaves the frame reporting its own height, and the two ways to arrange it are not close. **H3 is refused outright.** `allow-same-origin` makes the framed document same-origin with the client page, which hands the most hostile input this application handles the page's DOM, its storage, and its cookies — everything the frame exists to prevent, given away to make a measurement. It is not a weaker sandbox but the absence of one, and no requirement about layout buys it.

**H4 is taken.** `sandbox="allow-scripts"` and nothing else: the framed document runs script in an opaque origin, so it can lay itself out and measure itself and it can reach nothing of the page — not the DOM, not `localStorage`, not a cookie, not the session. It still may not submit a form, open a popup, navigate the top-level context, lock the pointer, or start a download, because each of those is a separate flag and none is granted. The client prepends its own script to the `srcdoc` ahead of the message markup; it reports `document.documentElement.scrollHeight` by `postMessage` on load and on resize, and the parent sets that frame's height from what arrives.

**The parent matches a report to a frame by its source, not by its origin.** An opaque origin serializes as the string `"null"`, which is what every such frame reports and is therefore no evidence of anything; the check that means something is that `event.source` is the `contentWindow` of a frame this component created. A handler that trusted the origin string would accept a report from any sandboxed frame on the page, and the height is a number the page acts on.

**What granting the flag costs is that the strip becomes a trust boundary.** On the dialog, *nothing in the message runs* is held by the sandbox, and [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s removal of every executable construct is a second line behind it — belt and braces, and the record said so. On the embedded surface there is no belt. A frame granting `allow-scripts` will execute whatever executable thing survives the strip, so a construct that gets past it is not a tidiness defect any more: it is script running against the message's own markup, in a frame that can still reach the network. That is why [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s acceptance is written as *nothing executable survives* rather than as a list of forms handled, and why its tests are the ones this surface actually rests on.

**The anti-tracking property is unchanged in what holds it and changed in what could defeat it.** The representation still carries no address to fetch, on both surfaces, and that is still the whole mechanism — the sandbox never governed fetching and does not begin to now. What is new is that a script surviving the strip could *construct* an address rather than merely resolve one the sender wrote, so on this surface the two properties stop being independent: the first row of the table is what keeps the fourth row true. They are two rows because they fail differently, and one build holds them both.

**There is no second `srcdoc` call site.** The frame is the one file `eslint.config.ts` names, reused by the embedded view rather than copied into it, and no `eslint-disable` is written at any call site. The `sandbox` value is a property of that one component rather than a second component with a second value, which is what keeps *two surfaces, two values* a fact a reader can check in one file instead of a convention two files are trusted to observe.

### What each property rests on across the three surfaces

The first table is the reading pane's. The same five properties rest on different things on each of the two markup surfaces, and the differences are what each surface costs:

| Property | On the reading pane | On the dialog | On the embedded view |
|---|---|---|---|
| No script, embedded object, form, or event handler executes | The document contract has nowhere to express one | **The sandbox.** No `allow-scripts` and no `allow-forms`, so the markup is inert, with the representation's strip behind it | **The representation.** `allow-scripts` is granted, so the strip is the whole of it: nothing executable survives [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s pass, and the only script in the frame is the client's own. Off the platform and onto a build |
| The client never turns a message value back into markup | A lint rule refusing every markup-writing call | **Narrowed, not lifted.** The rule keeps refusing `srcdoc` everywhere but the one component that is this surface, which is named in the configuration rather than waived at a call site | The same one component, reused. No second exception and no call-site suppression |
| Message style cannot escape | Nothing crosses the wire that could escape | **The frame's browsing context**, which is the browser's guarantee rather than the contract's — counted below with the sandbox rows for that reason, even though a frame's isolation is older than the attribute on it | The same, and unaffected by the flag: `allow-scripts` does not make the document same-origin, so its style and its script are still confined to it |
| No remote resource is fetched before a person asks | The service removed every address | **The service removed every address**, identically, from this representation too. Unchanged, and deliberately not delegated to the sandbox | The same removal, and now resting on the row above it as well — an address the sender wrote is gone, and a script that survived the strip could write one |
| A link cannot navigate the application | A scheme allow-list, and the client opens it out of the application | **The sandbox and the allow-list together.** No `allow-top-navigation`, and the same scheme filtering applied while the representation is prepared | The same two, unchanged. `allow-top-navigation` and `allow-popups` are as absent here as there, so script buys the markup no way out of the frame |

**On the dialog, three of those five depend on the browser where none did before**, and that is the honest cost of that surface. Rows one and five rest on the sandboxing flags; row three rests on the frame being its own browsing context, which is as much the engine's guarantee as the flags are and is counted with them rather than treated as free because a frame feels structural. Only rows two and four stay off the platform — one in a build that fails, one in a removal the service already performed.

**On the embedded view the count is two rather than three, and that is not an improvement.** Row one came off the platform, which is what moved the number — and it landed on a filter, which is the thing this record's own drivers say a safety property should not rest on: *a rendering path whose safety rests on the completeness of a filter is one whose safety is only as good as this year's bypass.* Trading a browser guarantee for a filter is a worse position than the one the count reports, so the count is given with the sentence rather than on its own. What makes it acceptable rather than a reversal of the record is that the filter is a removal over the message's own parse in the service, exercised by its own tests, asserted as *nothing executable survives* rather than as a list of forms — and that everything it might miss is confined to an opaque origin that can reach nothing of the page. It is the weakest of the three surfaces and this record says so plainly.

It is bounded rather than open-ended. Neither an `iframe`'s browsing context nor its `sandbox` attribute is a recently shipped capability whose support diverges between WebView2 and WebKitGTK, which is the divergence [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) makes this record wary of; both predate either engine, and so do `postMessage` and `scrollHeight`. The property that genuinely would have diverged — a policy delivered through the framed document's own markup — is the one deliberately not relied on.

**The lint rule is narrowed rather than suppressed, and that distinction is the whole of it.** `frontend/AGENTS.md` refuses relaxing a configuration so failing code passes, and it is right to: a file-level disable, or an `eslint-disable-next-line` at the call site, would make the next `srcdoc` anywhere in the client a matter of whoever reviews it. What this decision takes instead is a rule that still refuses `srcdoc` under `frontend/src/` and names one file where the surface lives, so adding a second frame is a change to `eslint.config.ts` that a reviewer meets as a diff rather than a line nobody sees. The rule keeps being the thing that says where this may happen; it stops being the thing that says it may not happen at all. **Two surfaces did not make it two files**: the embedded view reuses the named component, so the exception is still one entry and the rule still answers *how many frames are there* with a number a reader can count.

**The design that drew the control is right about the dialog, and the code is built to match it — the footer included.** #1477 asked whether the control may exist and the answer is that it may, on the terms above. Nothing in that half of the design needs correcting: it draws the reduced view, a control beside it, and a preview whose footer tells the reader that the message is isolated and that scripts and remote content are blocked. Both of those are true of what this record decides, and the footer is right not to say which mechanism holds which, because that is not a distinction a reader has to hold in order to trust the surface.

**The embedded view as the design draws it today is not right, and [#1507](https://github.com/Krzysztof318/MailFathom/issues/1507) is where it is corrected.** The prototype's inline frame carries `sandbox="allow-same-origin"` and fits itself by reading `contentDocument` from the page — H3, which the section above refuses outright. The correction is the design's to make and belongs there rather than only in the client: the frame grants `allow-scripts` and nothing else, its height comes from a report rather than from a read, and the design shows what the frame occupies before the first report arrives, what it settles at if none ever does, and a very long message drawn whole with no nested scrollbar. Building the client to the corrected shape while the project still draws the refused one would leave the source of truth saying something this record forbids.

**What the footer is, then, is a requirement on the build rather than a claim to check.** It is only true while both mechanisms are present: drop the frame and scripts run, drop the removal and remote content loads. A surface built on the frame alone — the sandbox honoured, the message's markup served as it arrived — would keep that footer word for word and make it a lie, on the one screen a careful reader opens *because* they do not trust the message. So the sentence to carry into the implementation is not *change the copy*; it is that the copy is already the promise, and the representation is half of what keeps it.

**On the embedded surface the same footer is carried by the representation alone.** The frame there grants `allow-scripts`, so *scripts are blocked* is true only because nothing executable reached the frame — the same words, one mechanism short of what the dialog has behind them. The copy is still right and still stays as it is, because what a reader is owed is what is true of the message in front of them rather than an account of which layer secured it; what changes is that on this surface the footer is a claim about [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s pass and about nothing else.

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

**The second surface is not the answer to a bad reduction, and treating it as one is the failure mode to name now.** It shows a reader what arrived; it does not make the pane draw it. G2 is still where a fidelity complaint is answered — in the reduction, on the service side, as a wider block catalogue arriving with a schema revision of its own — and that is slower than a control on the message head on purpose: it is the difference between one reader looking at their document in a frame and every reader of that kind of message getting it back in the pane.

Two things make G2 a real answer rather than a deferral. `MailDocumentRefusal` and `Truncated` already tell a reader *which* of the three things happened to their message, so a reduction that failed is reportable as a defect against a named state rather than as a message that looks wrong. And the block catalogue is versioned per block beside `MailDocument.SchemaVersion`, so widening it costs a client that has not caught up one block rather than the message — which is what makes widening it a routine act instead of a breaking one.

The risk the second surface creates is that it makes the complaint stop arriving: a reader with a working escape hatch reports nothing, and the reduction stops improving because nobody is inconvenienced by it any more. So the two are kept in their places — the pane is what a message opens as and the surface is a way to check it — and a message that regularly needs the frame is a reduction defect to file rather than a control working as intended.

**The embedded view sharpens that risk rather than escaping it, and this record accepts it with its eyes open.** A standing preference is exactly a reader deciding to stop meeting the reduction at all, so their complaints stop and the surface they chose is the weakest of the three. The preference is still taken, because refusing somebody the way they want to read their own mail in order to keep their dissatisfaction visible is not a trade this project gets to make on their behalf. What follows from it is that the reduction's health can no longer be inferred from how often anybody presses anything: G2 stays the answer to a bad reduction, and a reduction defect is filed from the refusal states `MailDocumentRefusal` and `Truncated` already report rather than from the traffic on a control.

G1 is refused because a guarantee with no route for the complaint it creates is a guarantee that gets removed later, under pressure, by somebody who has only the control to reach for. G3 — handing the reader the message itself, to open in a program they already chose — is not refused here and is not decided here either: it moves nothing inside this client's trust boundary, which is why it survives the reasoning above, and the second surface reduces the appetite for it rather than settling it. What it does do is take a copy of the message out of the deployment on a person's instruction, and what that costs — where the copy lands, what is left behind about it having been taken, and what the person is told before it is — is a question this record has no reasoning for and does not inherit from one. It has no issue yet, this record does not open one for it, and whichever record eventually answers it establishes its own footing.

### Both heads draw the same tree, and the one difference belongs to the shell

Nothing in this decision depends on an engine capability, a platform API, or a browser version: it is React elements over a JSON tree, and it renders identically under WebView2 and WebKitGTK because there is nothing in it for the two to implement differently.

The single difference is what *following* a link means. On the web head it is a new browsing context; on the desktop head it is the system browser rather than the application's own window, because a WebView that navigates to a sender's page has replaced the application with it. `frontend/src/AGENTS.md` forbids the tree branching on which head it is running on and permits the difference to live in the shell, so the application asks for a link to be opened and the desktop shell is what routes that to the system browser.

That is the shape [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) already established for the credential store, and this record takes it rather than inventing a second one: the application depends on one operation, which implementation satisfies it is decided once at the composition root by whether a shell offered the command, and no screen, component, or hook learns which head it is running on. Which Tauri mechanism does the routing, and whether it needs a command and a capability of its own, is [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428)'s to choose.

### How the isolation is proven

F2, split by what each suite can actually answer, per [`frontend/tests/AGENTS.md`](https://github.com/Krzysztof318/MailFathom/blob/main/frontend/tests/AGENTS.md):

- **The unit suite proves the parser.** The document parse is pure logic over values, so every refusal is reachable for nothing: a block type that is not in the catalogue, a known type at an unknown revision, a colour in any other notation, a link target carrying `javascript:`, `data:`, or `file:`, an image source that is not a permitted `data:` media type, and each bound exceeded by one. A malformed body is asserted to be refused rather than read as a tree with a hole in it.
- **The unit suite proves the components as attacks rather than as examples.** A block whose text is `<script>` markup renders as those characters; a link whose `Deception` is `DisplayedHostDiffers` renders as that; an unimplemented block pair renders a placeholder and the rest of the message.
- **The browser suite proves what only a browser can.** Against the built bundle: that drawing a message issues no request to any host but the page's own origin, that the pane's document holds no `iframe`, `script`, `object`, or `form` element, that message content is reachable by role and by name rather than being an opaque surface, and that following a link opens a new context rather than navigating the application.
- **The lint rule is the proof that does not depend on anybody writing a test.** `no-restricted-syntax` refusing every markup-writing call under `frontend/src/` — the list in the table above — fails the build rather than the review, which is the same shape the localization rule already takes in `eslint.config.ts`. Adding a restriction is not the configuration relaxation `frontend/AGENTS.md` refuses; it is the opposite, and it belongs to [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428). The markup surfaces' single `srcdoc` exception is written into that configuration by name — one entry for both of them, since they are one component — so the rule keeps saying *where* this may happen and a second frame is a diff rather than an oversight.

### Consequences

- Good, because the safety property on the pane is one sentence a reviewer can hold — the pane is handed no markup and writes none — rather than a filter whose completeness has to be argued each time somebody publishes a bypass. It takes two more sentences to cover the whole client now, one per markup surface, and the sections below are what those sentences are about.
- Good, because there is one renderer for both heads and nothing in it that an engine can implement differently, so a rendering defect has one place to be fixed.
- Good, because the pane is ordinary content: selectable, focusable, in the reading order, and styled from the token layer, which is what lets [#1426](https://github.com/Krzysztof318/MailFathom/issues/1426)'s selection-as-scope and the accessibility obligations be met at all.
- Good, because no package is added to the most security-sensitive path in the client, so there is no permanent patch obligation and no licence condition to carry into the desktop artifacts.
- Neutral, because the client's own trust boundary moves from a filter to a parser, which is work either way — but work whose failure mode is a refused message rather than a rendered attack.
- Neutral, because the pane inherits the service's reduction wholesale, so a fidelity complaint about how a message looks is a defect in the reduction rather than in the pane, and it is answered on the service side.
- Bad, because a message will not look exactly as its sender composed it *in the pane*. The reduction keeps blocks, emphasis, alignment, tables, and colours, and drops everything a closed catalogue cannot express, so a heavily designed newsletter is legible rather than faithful there. The second surface is what stops that being the last word, and it is a place to look rather than a place to read.
- Bad, because the client cannot render anything the reduction does not yet cover, and widening it is service work with its own schema revision rather than a change in the pane — which is the right place for it and is still the slower place.
- Bad, because refusing a durable override means somebody who reads one newsletter every week asks for its pictures every week, and the record offers them nothing else.
- Bad, because the client page is still served with no content security policy on either head, and this record explains why the pane does not need one without closing that gap for the application around it.
- Good, on the second question, because a reader can see what actually arrived instead of taking the reduction's word for it, which is what makes *legible rather than faithful* a trade somebody can accept rather than one imposed on them.
- Good, because the anti-tracking guarantee is held in one place on all three surfaces: the service removes the addresses, and no renderer, sandbox, or policy is asked to decline a fetch it could make. What differs is only what could defeat it, which the embedded surface's own consequence below states.
- Neutral, because the second surface is where the sender's own CSS finally runs, and a frame is exactly the container for that — the isolation the pane did not need is bought where it is worth its cost.
- Bad, because three of the five properties now rest on the browser where none rested on it before — two on the sandboxing flags and one on the frame being its own browsing context. Both mechanisms are old and universally implemented rather than recently shipped, which bounds the risk, but the sentence *nothing about the browser is relied on* is no longer true of the whole client.
- Bad, because the lint rule stops being an unconditional refusal and becomes a rule with one named exception, so the guard that could not be forgotten is now a guard somebody could widen — deliberately, in a diff, but widen.
- Bad, because the service grows a third body representation to keep correct, and the removal that makes it safe has to be exercised by its own tests rather than inherited from the tree's.
- Good, on the third question, because somebody whose mail is mostly composed HTML reads it as its senders drew it without pressing a control on every message, and the reduced view stays the default for everybody else — the standing preference is what makes both readers first-class rather than one of them tolerated.
- Good, because the embedded frame is measured rather than guessed, so every message is drawn whole in one scrolling conversation with no nested scrollbar and no message cut off at a height the page invented.
- Good, because `allow-same-origin` is refused in the record rather than only in the diff: the flag that would hand a stranger's markup the page's DOM, storage, and cookies is named as refused on every surface, so a later frame cannot arrive carrying it as an oversight.
- **Bad, because the embedded frame grants `allow-scripts`, so it will execute anything executable that survives [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s strip.** *Nothing in the message runs* stops being a platform guarantee there and becomes a property of a build: the strip is a trust boundary, a construct it misses is script running against the sender's own markup rather than an untidy artifact, and its tests are what this surface actually rests on. That is the whole price of the surface, and it is paid on the mode where every message in the thread is drawn this way rather than on one a reader opened deliberately.
- Bad, because the anti-tracking property and the inertness property stop being independent on that surface. The representation still carries no address, which is unchanged, but a script the strip missed could construct one — so a single miss costs both promises rather than one, and the two rows of the table fail together where on every other surface they fail alone.
- Neutral, because the count of properties resting on the browser goes *down* on the embedded surface, from three to two, and that is not the improvement the number suggests: what moved came off the platform and onto a filter, which is the arrangement this record's own drivers are most sceptical of. The count is reported with that sentence attached wherever it appears.
- Bad, because the client now carries a `postMessage` handler that acts on a number a framed document sent it, which is a message-passing boundary the client did not have. It is bounded — an opaque origin, one frame's `contentWindow` matched as the source, a height applied to that frame alone — but it is a boundary to keep right rather than one the platform keeps.
- Neutral, because the two markup surfaces now carry two different `sandbox` values, which is a difference a reader has to hold rather than one rule to remember. It is deliberate, it is stated here with the reason, and it costs nothing in the code because both surfaces are the same component with the value as a property of it.
- Bad, because the design project draws the embedded frame with `allow-same-origin` and a `contentDocument` read until [#1507](https://github.com/Krzysztof318/MailFathom/issues/1507) lands, so the source of truth and this record disagree for as long as that takes, and the client is built to the record.

## Validation

By review of the change that implements it, against the acceptance of [#1428](https://github.com/Krzysztof318/MailFathom/issues/1428), and by the four proofs above — the parser's refusals, the components under attack-shaped input, the browser suite's assertions about what the page fetched and what its document holds, and the lint rule, which fails a build rather than a review and is the one of the four that cannot be forgotten. That the client never asks for markup is held by `ClientMailBodyEndpoint.RequestFor`, which is already asserted on the service side and is the seam that exists for it.

The second surface is validated separately, because it is where the properties above stop being held by a contract's shape:

- **The service's own suite proves the representation, and it is the half that matters.** A message whose markup carries a remote `img`, a `background: url()`, an `@import`, a remote font, a `srcset`, a `<picture>` source, and an address in an attribute nobody expected produces a self-contained representation containing none of them — asserted by there being no absolute remote URL left in the output rather than by checking the forms one at a time, because a list of forms is the deny-list this record refuses everywhere else. A `cid:` part arrives as a `data:` URI of the media type it actually is, and the same message read with `RetainRemoteImageReferences` keeps its addresses, so the reader's ask is what changes the answer.
- **The browser suite proves the frame, against the built bundle.** Opening the surface on a message whose markup would fetch from another host issues no request to any host but the page's own origin; a message carrying a script does not run it; the frame carries `sandbox` with neither `allow-scripts` nor `allow-same-origin`; and the reading pane's own document still holds no `iframe`, so the surface is where it is meant to be and nowhere else.
- **The lint rule proves that there is exactly one of them.** It still refuses `srcdoc` under `frontend/src/` everywhere but the single file named in `eslint.config.ts`, so a second frame is a configuration diff rather than a component nobody noticed.
- **The dialog is proven as a dialog**, by the unit suite: focus moves into it when it opens, is trapped while it is open, and returns to the control that opened it when it closes.

The embedded surface takes every assertion above that is not about the dialog's flags, and the browser suite carries the ones only a browser can answer. It draws message markup with a flag granted, so the obligation is wider there rather than inherited:

- **It reaches no host but the page's own origin.** A thread drawn in the embedded view, on messages whose markup would fetch from elsewhere in every form the representation removes — an `img`, a background `url()`, an `@import`, a `srcset`, a web font — issues no request off the page's origin. This is the assertion that fails if the strip and the removal ever disagree, and it is the one that matters most, because the surface draws every open message rather than one a reader chose.
- **It cannot read the page.** A message carrying markup that attempts to reach the parent document — its DOM, its storage, its cookies — reaches none of them, which is the assertion that `allow-same-origin` is genuinely absent rather than merely unwritten in the source somebody read. Asserting the attribute's value is worth doing beside it and is not a substitute for it: the value proves what was requested and this proves what the engine gave.
- **A message carrying a script does not run one.** The frame permits script, so this is an assertion about [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s representation observed through a container that would have executed it — which is precisely the property that has no platform guarantee behind it here and therefore the one a passing unit test on the service does not finish proving.
- **The frame's height comes from a report.** It fits the message it holds, no message is cut off and none leaves a gap, nothing nested scrolls, and a frame that never reports settles at the stated fallback rather than at zero.
- **The parent ignores a report it did not source.** A `postMessage` arriving from anything other than a frame this component created changes no frame's height, which is the assertion that the handler matches on `event.source` rather than on an origin string every opaque origin shares.

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

- Good, because it is the only thing that is literally *what the sender sent*, down to the addresses.
- Neutral, because the service holds it: the message is stored as it arrived, so serving it is a representation to add rather than data to keep.
- Bad, because it carries every remote address the reduction exists to remove, and no sandboxing flag stops a framed document fetching one — so opening it is the read receipt this record refuses on every other path, fired before the reader is offered any choice about it.
- Bad, because the only mechanism that would close that inside the client is a policy carried in the framed document's own markup, which is engine behaviour rather than contract and is where the two heads diverge.

### A6 — the message's own HTML with every address removed

- Good, because it is the sender's layout, CSS, tables, and typography — everything the reduction drops — and a reader can hold it against the pane and judge the reduction for themselves.
- Good, because the property that makes it safe is the one already proven on the other representation, applied at the same point in the same pipeline, so there is one anti-tracking mechanism in this system rather than two that must agree.
- Good, because `cid:` parts survive as `data:`, so the pictures the sender actually attached are in the picture — which is what separates it from the sanitized representation, whose scheme allow-list takes those with the rest.
- Neutral, because a reader who wants the remote images too asks for them exactly as they would in the pane, and the same read serves both surfaces.
- Bad, because it is a third body representation for the service to keep correct, and its safety is a removal that its own tests have to exercise rather than one inherited from the tree's.
- Bad, because it is not, strictly, what the sender sent: a message whose design depends on remotely hosted images looks broken until the reader asks for them, on the surface whose whole promise is fidelity.

### B4 — the frame as a second surface beside the pane

- Good, because it costs none of the three things B2 costs the pane: no selection crosses it, no height is measured through it, and a dialog's focus order is the right one for it.
- Good, because it is where the sender's own CSS can finally run without a way out, which is the isolation the pane genuinely did not need and this surface genuinely does.
- Neutral, because it works identically on both heads: `sandbox` is not a recently shipped capability, so this is not the platform divergence the drivers warn about.
- Bad, because it moves three of the five properties onto the browser — two onto the sandboxing flags and one onto the frame's own browsing context — so *nothing about the browser is relied on* stops being true of the client as a whole.
- Bad, because it needs the lint rule narrowed to one named file, and a rule with an exception is a rule somebody can widen — which is why the exception is in the configuration rather than at the call site.

### B5 — the frame drawn inline for every open message

- Good, because a reader whose mail is mostly composed HTML reads it as sent without pressing a control per message, which is what a standing preference is for and what a per-message control cannot become.
- Good, because it reuses the frame, the representation, and the single named component B4 already established, so it adds a mode rather than a mechanism.
- Neutral, because the reduced view stays the default and stays what an unset preference reads as, so nobody arrives at this surface without choosing it.
- Bad, because it needs the frame fitted to its content, which B4 never had to answer and which group H exists for — and the answer taken there is the only place in this record where a flag is granted.
- Bad, because it draws every open message this way rather than one a reader deliberately opened, so whatever the representation misses is met on every message in the thread instead of on one.

### H1 — the page picks the height

- Good, because it grants no flag, reads nothing across the boundary, and adds no message-passing surface.
- Bad, because the page has nothing to compute a height from: the rendered height depends on the engine, the viewport, the fonts that resolved, and the pictures that decoded, none of which the markup states.
- Bad, because every wrong guess is visible as a gap under a short message or as a long one cut off, on the surface somebody chose *because* they wanted the message drawn faithfully.

### H2 — a fixed height, and the frame scrolls inside itself

- Good, because it is deterministic, needs no measurement, and is what the standalone dialog legitimately does.
- Bad, because it is a scroll inside a scroll on the surface people read longest, which is the nested scrolling the pane's own objections named and which the UX obligations refuse.
- Bad, because it makes the conversation unreadable as a conversation: a thread of framed windows is a list of scrollable boxes rather than messages one after another.

### H3 — `allow-same-origin`, and the parent reads `contentDocument`

- Good, because it is the least code: one `onLoad` handler, one property read, no protocol between the two documents.
- Bad, because a framed document same-origin with the client page reaches that page's DOM, its storage, and its cookies. It is not a weaker sandbox but the absence of one, applied to the most hostile input this application handles.
- Bad, because it would make the strip the only thing standing between a stranger's markup and the reader's session rather than between it and a frame — the same filter, holding incomparably more.

### H4 — `allow-scripts`, and the frame reports its own height

- Good, because the frame measures itself accurately, at every width and after every resize, which is the only way to draw a message whole without guessing.
- Good, because the opaque origin is kept: the framed document reaches no DOM, no storage, and no cookie of the page, and every other flag — forms, popups, top-level navigation, pointer lock, downloads — stays ungranted.
- Neutral, because the mechanism is old on both engines: `sandbox`, `postMessage`, and `scrollHeight` all predate WebView2 and WebKitGTK, so this is not the platform divergence the drivers warn about.
- Bad, because it moves *nothing in the message runs* off the platform and onto [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s strip, which is the trade this record's drivers are most sceptical of and the reason the embedded surface is named as the weakest of the three.
- Bad, because it adds a message-passing boundary the client did not have, whose handler has to match a report to the frame that sent it — an opaque origin serializes as `"null"` and identifies nothing.

### G1 — the reduction is what there is

- Good, because it needs nothing built and states the trade-off honestly.
- Bad, because a guarantee whose complaints have nowhere to go is a guarantee somebody removes later under pressure, and the pressure lands on the surface with the fewest defences rather than on the reduction that caused it.

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
- Bad, **as the pane**, because measuring the frame's height needs script inside it, and opening the sandbox to solve a problem the sandbox created is not worth it on a surface that is handed no markup in the first place. This bullet originally read as a general objection to letting a frame report its own height, and that reading is withdrawn: on the embedded surface, where markup is drawn inline whatever this option decides, the same mechanism is what H4 takes.
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
- [#1477](https://github.com/Krzysztof318/MailFathom/issues/1477) is where the second question was asked and where the first amendment was written. It implements nothing, exactly as the first half of the record implements nothing: [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484) is the service's half — the self-contained representation, its removal, and the tests that exercise it — and [#1485](https://github.com/Krzysztof318/MailFathom/issues/1485) is the client's, which is the control, the frame, the narrowed lint rule, and the browser assertions about what the surface fetched. [#1478](https://github.com/Krzysztof318/MailFathom/issues/1478) redraws the message head the control sits on and does not wait on either.
- [#1527](https://github.com/Krzysztof318/MailFathom/issues/1527) is where the third question was asked and where this second amendment was written, and it implements nothing either. What each of the issues around it keeps is unchanged by it: [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484) keeps the representation and its acceptance that *nothing executable survives*, which this amendment makes load-bearing on the embedded surface rather than rewrites; [#1485](https://github.com/Krzysztof318/MailFathom/issues/1485) keeps `sandbox=""`, its dialog, and the single named component, and grants nothing; the embedded-HTML half of [#1508](https://github.com/Krzysztof318/MailFathom/issues/1508) builds the mode, the preference behind it, the measuring script, and the parent's handler; and [#1507](https://github.com/Krzysztof318/MailFathom/issues/1507) corrects the design project's own frame, which waits on this record because a design cannot depict what the record forbade.
- The design project drew both surfaces first and is right about the dialog, the footer included: *isolated, scripts and remote content blocked* is true of what this record decides, and it is right not to name which mechanism holds which. On the embedded view it is not right yet — it draws `allow-same-origin` and a `contentDocument` read, which this record refuses — and [#1507](https://github.com/Krzysztof318/MailFathom/issues/1507) is the correction. That is the one design change this record produces, and it is the owner's to make in the project rather than something the client closes by building around it.
- The sandboxing flag set is the HTML Standard's, in [§ Sandboxing](https://html.spec.whatwg.org/multipage/browsers.html#sandboxing). It is read here for what it does not contain: no flag governs subresource fetching, which is the whole reason the representation rather than the container is what carries the anti-tracking property. `allow-scripts` and `allow-same-origin` are read there as the two independent flags they are: withholding the second sets the *sandboxed origin browsing context flag*, which the section defines as forcing the content into an opaque origin, and granting the first alone therefore leaves the document opaque-origin and unable to reach anything of the page. The standard warns against the pair separately, in [§ The `iframe` element](https://html.spec.whatwg.org/multipage/iframe-embed-object.html#attr-iframe-sandbox) — an embedded page of the container's own origin, given script, can remove the `sandbox` attribute and reload out of the sandbox altogether. This record refuses `allow-same-origin` for what it grants directly rather than for that, which is the page's DOM, its storage, and its cookies; the warning is why the pair is not weighed as a middle option either.
- Revisit if a reader's need for the message itself turns out to survive the markup surfaces, at which point it is answered by handing the message *out* of the application — under a record of its own, since nothing here reasons about a copy leaving the deployment; if the embedded view becomes what most readers choose, at which point the reduction is failing for them and G2 is the answer rather than a wider frame; if either the sandbox attribute or a frame's browsing-context isolation ever stops being uniformly implemented across the two heads, at which point the platform rows of the tables above lose what holds them at once; if a construct is ever found to survive [#1484](https://github.com/Krzysztof318/MailFathom/issues/1484)'s strip, at which point the embedded surface's inertness and its anti-tracking property have both already failed and the flag is what is reconsidered rather than the filter that missed; or if anything asks for a *third* surface drawing markup, at which point the lint rule's single named exception is the thing to defend rather than to extend.
