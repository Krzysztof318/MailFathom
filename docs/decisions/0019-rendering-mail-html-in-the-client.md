---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-27
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Render mail as a structured document by default, offer the sender's own HTML in an isolated engine on request, and let neither path fetch a remote resource unasked

<!-- describes: backend/src/Application/EmailContent/Rendering/**, backend/src/Application/Emails/GetEmailContent/**, backend/src/Infrastructure/Mail/Mime/**, backend/src/Host/Api/ClientMailBodyEndpoint.cs, frontend/src/Client.Backend/Mail/**, frontend/src/Client/Presentation/Spaces/Mail/Reading/** -->

## Context and Problem Statement

Reading a message means rendering whatever arrived, and what arrives is HTML written by a stranger. It carries CSS that will escape whatever element it is given, script, embedded objects, forms, and remote references whose only function is to tell the sender that the message was opened. Nothing in the client renders a body yet — [`ClientMailThreadEndpoint`](https://github.com/Krzysztof318/MailFathom/blob/main/backend/src/Host/Api/ClientMailThreadEndpoint.cs) serves a conversation and deliberately carries no body at all — so the first screen that renders one settles the question for every screen after it, and for the mobile heads that do not exist yet.

The service already answers part of it. `EmailHtmlSanitizer` reduces a body to an allow-list at every level the library offers — 69 elements, six attributes, no CSS property, no at-rule, and **no URI scheme at all** — and [`email-content.md`](../features/email-content.md) records why. That output is correct for the caller it was written for: a model reading mail wants the words, and a reference nothing resolves is a reference nothing can leak through. It is not a mail body a person can read, and it is worth being exact about why, because the reason is narrower than "it strips everything". **Structure survives it**: the element allow-list keeps `table` with its row and cell elements, `col` and `colgroup`, the list elements, and `blockquote`, and the attribute list keeps `colspan` and `rowspan` precisely so a table's shape stays readable. What does not survive is everything that makes structure legible as mail — no styling, no width, no alignment, no colour — and no reference of any kind, so every link is dead and every inline image is gone.

That is what the client cannot reuse, and it is a smaller gap than it first appears: the tree below is this same structure with the presentational properties and the resolved references put back. The decision is therefore not "reuse the sanitizer or not" but what to add to what it already gets right.

The heads constrain the answer unequally, and the browser head constrains it most. Uno's `WebView2` is [supported on every target](https://platform.uno/docs/articles/controls/WebView.html), but it is a different engine on each: WebKitGTK on Linux desktop, reached through `libwebkit2gtk` and needing `GDK_BACKEND=x11` under Wayland; the Chromium-based runtime on Windows; `WKWebView` on macOS and iOS; and on WebAssembly **a native `<iframe>` in the same browser as the application's own origin**. Uno's own documentation is explicit that `WebResourceRequested` "has significant platform-specific limitations", and that on WebAssembly requests made by HTML elements cannot be intercepted at all — the suggested remedies are a service worker or a server-side proxy. A remote image therefore cannot be blocked from C# on the head where blocking it matters most.

Two obligations pull against each other here, and both are the project's own. [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) asks that reading not be reporting. [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) asks, in a user story, that a message "look like what the sender sent, with their own images", so that the reader "is reading mail rather than a transcription of it". A decision that satisfies only the first produces a mail client people keep another mail client beside, which is not a privacy win: it is the privacy guarantee being carried out of the application by the reader.

The decision question is therefore five questions an implementation would otherwise answer by accident: what renders mail HTML on each head, where markup is neutralized, what happens to remote references, what happens to links, and what a message the renderer refuses falls back to.

Recorded on issue [#1142](https://github.com/Krzysztof318/MailFathom/issues/1142). [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) builds what this names, inside the reading pane [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) composes.

## Decision Drivers

- **A rendering defect must have one place to be fixed.** A renderer chosen per head is a security boundary described once per engine behind it, and the weakest of those descriptions is the real one.
- **Reading must not be reporting.** A tracking pixel is the ordinary case rather than the exotic one, and the default has to defeat it by construction rather than by a setting somebody could get wrong.
- **A reader who cannot read their mail here will read it somewhere else.** Fidelity is not a comfort feature: it decides whether the guarantees above apply to the message at all.
- **The same guarantee on every head, including ones that do not exist.** `frontend/src/AGENTS.md` states that no code may assume `net10.0-android` and `net10.0-ios` never open.
- **Two parses of the same markup by two different engines is a vulnerability class, not an inefficiency.** Mutation XSS is what happens when the thing that sanitized the document and the thing that renders it disagree about what the document is.
- **Mail content is personal data of the reader and of everyone who writes to them**, which bears on what is fetched, what is remembered, and what is stored.
- **A published head must not silently depend on something an operator has to install.** MailFathom is released; a head that renders mail only where a system library happens to be present is a defect against a user unless it says so.

## Considered Options

- Render every message with `WebView2`, isolating it with the engine's own facilities.
- Render every message as a structured document tree in native XAML, with no engine anywhere.
- Render as a structured document tree by default, and offer the sender's own HTML in an isolated engine on request.
- Sanitize in the service as today and parse the resulting markup in the client into native XAML.

## Decision Outcome

Chosen option: **"Render as a structured document tree by default, and offer the sender's own HTML in an isolated engine on request"**, because it is the only option that makes the private path the one a reader lands on without asking, while leaving them a way to see the message as its sender built it that does not involve closing this application and opening another.

The two paths are not two renderers of equal standing. **The tree is what reading a message means here**; the engine is a deliberate, visible, per-message act with a stated cost. That asymmetry is the decision, and everything below follows from it.

### The default path: a structured document tree, rendered natively

The service reduces mail HTML to a closed, versioned document tree, and the client renders that tree with native XAML text and layout controls, identically on every head present and future.

On this path the isolation statement is an absence rather than a policy, which is what makes it checkable: **the client receives no markup and runs no engine.** Script cannot run because nothing that could run it is on this path. A remote resource cannot be fetched because the default tree contains no remote reference to fetch.

Message style cannot escape the pane because of the **closed property set** the tree admits, and not because of anything about XAML's rendering model — a negative margin, a `RenderTransform`, or a child of a `Canvas` all paint outside a parent, since the panels a body is composed from do not clip by default. What confines a message is that no declaration surviving the reduction can place a node anywhere: there is no offset, no transform, no margin a message controls, no absolute or floated position, and no `z-index`. A width survives only as a share of the parent it sits in — a table column's proportion of its table — which cannot resolve to a position outside that parent. If [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) ever admits a positional or absolute-dimensional property, this guarantee is what it breaks, and the property set is where that has to be argued rather than in the renderer.

**This path is more faithful than "structured text", and the record is explicit about that because the first draft of it was not.** Mail layout is overwhelmingly built from tables — that is how mail has been authored for twenty years, because that is what mail clients render — and a table maps onto a XAML grid without an engine. So the tree carries the table structure with its column and row spans, cell alignment, and relative widths; lists with their nesting; block structure and quoting depth; emphasis; foreground and background colour; and inline images resolved from the message's own parts. A two-column newsletter is two columns here.

What genuinely does not survive is what a table cannot express: absolute and floated positioning, overlap, background images, web fonts, media queries, animation, and anything sized in a way that only means something against a viewport. Those are the messages the second path exists for.

### The opt-in path: the sender's own HTML in an isolated engine

On request, the same message is rendered as the HTML its sender wrote, in the platform's own engine through `WebView2`.

What the engine is handed is **not the raw MIME body**. It is that body parsed once by the service and re-serialized under a permissive *presentation* policy. Handing an engine markup that could execute would give away the only property that makes this path acceptable at all, and "the reader asked for it" is not consent to run a stranger's code.

**That policy is an allow-list at every level, exactly as the strict one is**, and it is permissive about CSS and layout rather than about the element vocabulary. [`email-content.md`](../features/email-content.md) records the doctrine and the reason — a deny-list cannot be proven complete — and this path is the one place in MailFathom where being wrong about it means a real browser engine acting on a stranger's markup, so it is the last place to depart from it. Naming what is excluded would be the departure: `<base href>` re-bases every relative reference in the document and `<meta http-equiv="refresh">` navigates, and neither is a script, an embedded object, a frame, a form, an event handler, or a remote reference — so an enumeration written against those five admits both. What the policy states instead is the elements and attributes that survive, the CSS properties and at-rules that survive, and the schemes that survive, which are `http`, `https`, and `mailto` on a link, `cid` resolved before serialization, and `data:` for an image and nothing else.

Remote references are removed on this path exactly as on the other one, so the engine is never handed a URL the reader has not asked to load. Inline `cid:` parts are resolved by the service into bounded `data:` URIs before the markup is serialized, so no per-head request-interception API is involved — which matters, because that is precisely the API Uno documents as unavailable on the browser head.

**This path's availability varies on two different axes, and conflating them is how it would be got wrong.** Two heads exist — `net10.0-desktop`, which covers Windows, Linux, and macOS from one target framework, and `net10.0-browserwasm` — so "per head" is the wrong grain for most of what follows.

*Within the desktop head it varies per operating system*, because the engine behind `WebView2` is the operating system's: `WKWebView` on macOS and the WebView2 runtime on current Windows are part of the system, while on Linux it depends on `libwebkit2gtk`, which many distributions do not install. That last one is a **runtime fact about the machine rather than about the head** — the same published binary offers the path on one Linux machine and not the next — so the head detects its absence and says the original view is unavailable on this machine and why, rather than presenting a blank pane.

*Between the heads it varies once*, on the browser head, where the path is offered **only** inside a sandboxed browsing context with an opaque origin — a sandbox carrying neither `allow-scripts` nor `allow-same-origin` — because without that the message renders in the application's own origin. Uno's `WebView2` exposes no sandbox attribute, so [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) either reaches it through the native element behind the control template, which Uno documents as internal and subject to change, or does not offer this path there at all. It does not offer a weakened version of it: an isolation boundary honestly missing is better than one quietly absent on the head nobody checked, because only the first gets noticed. The mobile heads, when they open, join the first axis rather than the second — both bring their engine with the operating system.

The engine is entered per message and left when the message is left. Nothing about the choice is remembered, for the reasons remote content is not remembered, below.

### One parse, two projections

Both paths are produced from **one parse, in the service**, by the AngleSharp stack `HtmlSanitizer` already brings in and `THIRD_PARTY_LICENSES.md` already records. The tree and the presentation-policy markup are two projections of the same parsed document, not two readings of the same string, and the client links no HTML parser on either path.

**On the default path that ends the matter, and on the engine path it does not.** Serializing markup for an engine means the engine re-parses it, so there a second parser does decide what the document is, and it can decide differently from the one that sanitized it — which is the mutation-XSS structure this record names as a vulnerability class and rejects option 4 for. Saying it is absent "in either direction" would be false, and worse than false: it would tell [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) there is nothing to test for on the only path where it applies. Foreign content, `template`, and `noscript` are where sanitizer and browser have historically disagreed about the same string, and the allow-list above excludes all three — but that is a mitigation rather than the absence the default path has, and it is a standing cost of offering the engine at all. It is also the second reason the strict allow-list is not a place to be generous: the narrower the vocabulary that survives, the less there is for two parsers to disagree about.

`get_email_content` is unchanged. Its caller is a model, its guarantee is that no reference survives for anything to resolve, and that guarantee is the right one there; neither of the two projections above relaxes it.

### Remote resources, on both paths

**The default carries no remote reference at all, in either projection.** Not a blocked one, not a placeholder pointing at one, not a flag telling a renderer to abstain: the absolute references are dropped in the service, and what the reader is told instead is how many were removed. The guarantee is worth wording as `EmailHtmlSanitizer`'s own remarks word it, because the loose form of it is false: **no reference survives that anything resolves unasked.** A sender-controlled absolute URL *is* on the client, by design — a link carries its resolved target so the reader can be shown where it goes before following it — and what the default removes is every reference a renderer would fetch on its own. A rendering bug therefore cannot leak by fetching, and on the engine path that is what makes the engine's own inability to be policed on the browser head stop mattering. It is not a licence to resolve a link target for any other purpose: a hover preview, a favicon, or an engine's own link preloading would each turn a target the reader has not clicked into a request the sender receives, and each is ruled out by this sentence rather than by the absence of the URL.

Overriding is per message and is a second request, asking for the message with its remote references retained. The client then loads them, and the interface says plainly what that reveals: the reader's address and the fact that the message was opened, to whoever wrote it. The service does not proxy the fetch — proxying would hide the reader's address while still telling the sender the message was read, and would give the service an outbound path to arbitrary attacker-chosen URLs, which is a server-side request forgery surface bought for a partial improvement to a leak the reader just consented to.

**The override is offered on both paths, and on the engine path it is a policy of its own rather than the presentation policy with a scheme added.** Refusing it there would be refusing it where it matters most — a graphically rich newsletter is the case the engine path exists for, and its images are usually remote — so the retained-reference projection is the fourth policy this decision creates, counted with the three below. It is the presentation allow-list with exactly one widening: `http` and `https` become admissible **on the image element's source attributes and nowhere else**. Not in CSS, so no `url()` and no `@import` can fetch; not on any other element; not as a document-level reference. That narrowness is what keeps the browser head's uninterceptable requests bounded to the thing the reader asked to see, because a document handed to an engine there fetches everything it carries with no per-reference control — so what it carries has to be the whole of the control. On the default path the same override widens nothing else either: the tree's closed property set never admitted a CSS reference to begin with.

**The override is not remembered** — not for the message, not for the sender, not for the session's other messages. Not for want of somewhere to put it: every head has a preferences store and the client already uses one, `ApplicationData.Current.LocalSettings`, which holds the deployment address and the mailbox scope — the chosen account, the chosen folder, and which folders are expanded, whose keys are composed verbatim from a mail server's own folder names. What `frontend/src/AGENTS.md` and [ADR 0018](0018-where-the-client-keeps-its-sign-in-credential.md) say the browser head keeps nothing *of* is credential material, and that rule does not reach a preference.

So the refusal is not "the client never writes anything about mail down", which would be untrue the moment somebody read the mailbox tree's memory and concluded this record had not noticed it. **The line is what the remembered thing is a record of.** A folder somebody expanded is their own arrangement of their own client — it says where they like to work, and knowing it tells an attacker what the mailbox looks like, which the mailbox itself would tell them. A remembered remote-content override is a record of **which correspondent's content that person agreed to load**, which is a judgement they made about a sender rather than a shape they gave their own screen, and it is the input to a decision that reaches the network on their behalf next time.

Two reasons follow from that, and both are about the remembering rather than the store. A remembered per-sender allowance is a standing consent that outlives the reason it was given and survives the sender being compromised — the sender who earns it is not necessarily the sender who later uses it. A remembered per-message one puts that judgement at rest, on the browser head in a store scoped to the page's origin rather than to a person. Neither is worth a click saved. A later record may add a per-sender allowance if use argues for one, and it would have to argue past this paragraph rather than past the mailbox tree.

### Links

`href` survives for `http`, `https`, and `mailto`, and for nothing else, on both paths. A link carries its text and its resolved absolute target, and the target is shown before it is followed rather than after. Following one hands the URI to the platform's opener and leaves the application; the browser head opens a new browsing context with `noopener` and **never navigates the document**, which would destroy the running application along with the page.

On the default path the pane owns the click, so that is all there is to it. **On the engine path the mechanism has to be stated per head, because a link is the one thing a sandbox does not restrain.** An opaque-origin sandbox carrying neither `allow-scripts` nor `allow-same-origin` still lets a plain anchor navigate the frame it sits in, and a frame navigating to the sender's URL from the reader's own address is the leak this whole design exists to make impossible — arriving through the one path the record has already admitted it cannot police. Where the engine offers navigation interception, the click is intercepted and handed to the opener like any other. **Where it does not — the browser head — the projection carries no live target at all**: the anchor is rendered as the text and target it displays and not as something that can be followed, so no in-frame navigation can begin. Following a link there means the default path, where links work properly. The engine path is for seeing the message as it was built, not for acting on it, and that is a smaller loss than a link that reports on the reader when clicked.

A mismatch between what a link says and where it goes is determined in the service and carried beside the link, rather than re-derived by each renderer: where the link's own text parses as a URI or a host and that host differs from the target's, the contract says so. A host is shown in its ASCII form as well as its Unicode one wherever the two differ, so a homograph is visible as one. Putting the determination in the contract is what stops the two paths from disagreeing about whether a link is deceptive, and what makes a second client unable to be quieter about it than the first.

### When a renderer refuses

The plain-text representation, rendered as itself, with a visible reason. It is a first-class rendering rather than a fallback that looks broken, which [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163) requires of it independently. **Three things reach it**: a message with no HTML part, a body whose reduction fails, and a body whose reduction yields nothing renderable.

Two other refusals exist and neither reaches plain text, which is worth separating because an implementer reading a single list would ship the wrong fallback for both. A node whose revision this build does not implement is refused rather than read as though it were the revision the client knows — the rule [`presentation-plan.md`](../features/presentation-plan.md) already states for a plan, where whether a refused node costs the reader that node or the whole document is the reader's own decision, and the pane makes it here by **refusing the node and rendering the rest of the message**. And a head where no engine is available refuses **the engine path alone**: the reader stays on the tree, which is the better answer wherever it can be given, and the interface says the original view is unavailable on this machine and why.

### What this decides for composing, and what it deliberately does not

Composing is out of scope for [#1142](https://github.com/Krzysztof318/MailFathom/issues/1142) and gets its own decision. Two consequences of *this* one reach it anyway, and are recorded here so that decision starts from them rather than rediscovering them.

**The rule above is about untrusted markup, not about the letter HTML.** Outgoing HTML is authored by this application out of its own editor state, so the client producing HTML to send breaks nothing here: what it must never do is round-trip a received body, because HTML that came from a stranger and left as ours is markup nobody has taken responsibility for. Its shape is a question for the composing decision, and if it is settled by having the service serialize from an editor document rather than by the client emitting markup, that would keep the "no HTML written in the client" property — but this record does not decide it.

**Reply and forward are where the two surfaces actually meet**, because quoting means the received message becomes part of an outgoing one. The client holds a tree, not the sender's markup, so the quoted original is re-serialized from that tree by the service, or the reply quotes the plain-text rendering. It is never assembled from the presentation-policy markup the engine path was handed: that projection exists to be rendered under a boundary and would be leaving through a path that has none. Naming this now is the point — it is the consequence most likely to be discovered late, by a reply that quotes something nobody sanitized for sending.

### Consequences

- Good, because the path a reader lands on without asking is the one with no engine, no markup, and no reference anything resolves unasked, so the private answer is the default rather than the informed choice.
- Good, because the tracking pixel is defeated on both paths by the reference not being there to fetch rather than by a renderer honouring a setting, which is what lets the engine path exist at all despite being unpoliceable on the browser head.
- Good, because a reader who needs to see the message as built has somewhere to do it inside this application, so the guarantees keep applying to that message instead of being escaped along with it.
- Good, because the body on the default path is selectable, keyboard-reachable, and screen-reader-navigable text in the application's own visual tree, which an engine surface is not, and the pane keeps the last word on legibility where a sender's colour would otherwise decide it.
- Good, because a deceptive link and a stripped remote reference are facts on the contract rather than conclusions each renderer reaches for itself.
- Neutral, because the reduction costs a parse per read, as the sanitization pass it sits beside already does, and both projections come from that one parse. Unlike that pass it is not opt-in: the tree is what reading a message means here, so it is produced for every message somebody opens.
- Neutral, because the block catalogue is a closed enumeration to be versioned per member, exactly as [`presentation-plan.md`](../features/presentation-plan.md) versions its own.
- Bad, because the engine path's availability varies on two axes at once — per operating system inside the desktop head, and once more for the browser head — so the product's answer to "can I see the original" depends on the machine as well as the build, and the client has to say which, honestly, in the interface. Links are inert on the browser head, so "see the original" and "use the original" are not the same offer everywhere either.
- Bad, because the engine re-parses what the service serialized, so the second-parser disagreement this record calls a vulnerability class is present on that path and mitigated rather than absent. It is the standing cost of offering an engine, and the reason the presentation allow-list is not a place to be generous.
- Bad, and this is the cost most likely to be underestimated: **an opt-in path is opt-in at runtime and not at build time.** Linking `WebView2` puts the `WebView` entry into `UnoFeatures` and the engine into the published head for everyone, including every reader who never opens it — with the Linux prerequisite, the Wayland caveat, the footprint, and every future advisory against that engine becoming MailFathom's to track. Supporting the second path means accepting a browser engine in the desktop head.
- Bad, because the service now maintains four policies over the same parsed document — the existing strict one for `get_email_content`, the tree, the presentation policy, and the retained-reference variant of it the override serves — and a widening applied to the wrong one is a security change that looks like a rendering change.
- Bad, because two paths is two renderings to keep saying the same thing about one message, and a discrepancy between them is a defect a reader will read as the application lying about one of the two.
- Bad, because what does not survive on the default path still does not survive there: absolute positioning, background images, web fonts, and media queries are what the second path is for, and a reader who wants them pays a request for them.

## Validation

- The default path's absence of an engine is checkable mechanically: no type under `frontend/` reaches `WebView2` outside the one control that implements the opt-in path, and nothing under `frontend/` restores an HTML-parsing package. An entry in `.config/BannedSymbols.txt` — which both stacks already read — fails a build at the reference; the package's absence is MSBuild rather than API, so a contract assertion over `frontend/Directory.Packages.props` is what reaches it.
- The engine path is asserted to be unreachable without an explicit per-message act, and its markup is asserted against the allow-list rather than against a list of what should have been removed — an assertion naming script, handlers, frames, and forms would pass a document carrying `<base>` or a `meta` refresh. The remote-reference assertion is the one that makes the browser head's uninterceptable requests harmless, so it is the one that must fail loudest, and the corpus behind it carries the constructs where a sanitizer and a browser have historically re-parsed the same string differently.
- The projection served to a head that cannot intercept navigation is asserted to carry no followable target, because there the sandbox does not restrain a link and nothing else will.
- The service's two projections are covered by unit tests over hostile bodies — script in every position the parser can reach it, `style` carrying `url()` and `@import`, an event handler on an allowed element, a `javascript:` and a `data:` target on a link, an absolute image reference, and markup whose reduction yields nothing — asserting the tree and the serialized markup rather than a string equality, since those are what a renderer receives.
- Both projections are asserted to come from one parse of one document, because the argument against a second parser is worth nothing if the two are produced by two passes that could diverge.
- The link mismatch determination and the punycode form are asserted on the contract, so neither path can be the only thing standing between a reader and a homograph.
- `$check-docs-licenses` covers the engine and anything taken to reach it. The `WebView` `UnoFeature` resolves to packages, and `libwebkit2gtk` is a runtime dependency of a head this project publishes, so both are register questions rather than build details.
- **The AngleSharp stack is the third, and the one this decision actually moves.** Holding one parsed document to build two projections from it means holding an AngleSharp type, so the service compiles against AngleSharp directly and pins it centrally — where today `backend/Directory.Packages.props` carries no `PackageVersion` for it and only notes that a direct reference is not foreclosed, and `THIRD_PARTY_LICENSES.md` records the stack as what "HtmlSanitizer brings in transitively" with "MailFathom compiles against neither and references neither directly". That sentence stops being true when [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) builds the projections, so the pin and the row are its to write in the same change. **No row belongs in the register now** — a component a decision only plans gets none, which is why it is recorded here instead.
- Fidelity is judged by reading rather than by a gate, against the theming and responsive-layout obligations `frontend/src/AGENTS.md` carries. Nothing mechanical checks it, which is worth stating plainly: the trade-off this record calls its main cost is the part a reviewer has to look at. The accessibility half has no stated obligation to judge it against — nothing under `frontend/` states one — so [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) is the first screen that would need one, and this record claims the default path's reachability as an advantage rather than as something already checked.

## Pros and Cons of the Options

### Render every message with `WebView2`

- Good, because fidelity is complete and nothing has to be invented: the control exists on every target and the rendering is the engine's.
- Good, because inline `cid:` resolution could be served by a virtual host mapping rather than by inlining anything.
- Bad, because the isolation boundary becomes one description per engine, and the honest statement is the weakest of them — as the *default*, that is the statement the product is making about every message anybody reads.
- Bad, because on the browser head the control is an `<iframe>` in the application's own origin and `WebResourceRequested` cannot intercept requests made by HTML elements there, so a remote image could not be blocked from C# at all. The body would have to be pre-processed to remove them — which is this record's chosen option, done first, with an engine behind it.
- Bad, because it makes `libwebkit2gtk` a prerequisite for reading mail at all on Linux rather than for one opt-in view.

### Render every message as a structured document tree, with no engine anywhere

- Good, because it is the strongest possible version of the isolation argument: one renderer, one boundary, no engine on any head, present or future.
- Good, because it keeps the desktop head free of a browser engine and of the Linux prerequisite entirely.
- Neutral, because the fidelity it loses is narrower than it first appears — table-based layout, the way mail is actually built, survives it.
- Bad, because it has no answer for the messages it cannot express, and [#1163](https://github.com/Krzysztof318/MailFathom/issues/1163)'s own user story asks for one. A reader who meets such a message opens another mail client, and the privacy guarantee leaves with them — which is the failure this option is least able to detect, because nothing in it registers a reader giving up.

### Render as a tree by default, and offer the sender's HTML in an isolated engine on request

The chosen option, argued above.

- Good, because the private path is the one nobody has to choose, and the faithful path is available without leaving the application.
- Good, because remote references are absent on both paths, so the weaker boundary never has to hold back the thing that actually leaks.
- Neutral, because the second path's availability varies per operating system and once per head, stated as both rather than flattened into one.
- Bad, because the engine is in the published head whether or not a reader ever asks for it, and because two paths and four policies are more to keep true than one of each.

### Sanitize in the service as today and parse the resulting markup in the client

- Good, because it reuses the sanitizer that exists and keeps the wire format readable in a debugger.
- Bad, because it parses the same markup a second time with a second parser, which is the setup mutation XSS is built out of; the client's parse disagreeing with the service's would decide what a reader sees on evidence the sanitizer never approved. The chosen option carries that structure too, on its engine path, and rejecting it here rather than there is a judgement about where and how often: this option puts a second parse on **every message a reader opens**, by default, under a vocabulary wide enough to be worth rendering, while the engine path puts one on the messages somebody asks for it on, under the narrow allow-list above, with a path beside it that has none.
- Bad, because it puts an HTML parser into a trimmed WebAssembly head — a package to license, register, pin, and pay for in download size, to redo work the service had already done.

## More Information

- [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs the engine, the packages the `WebView` `UnoFeature` resolves to, and `libwebkit2gtk` as a runtime dependency of a published head.
- [ADR 0018](0018-where-the-client-keeps-its-sign-in-credential.md) governs credential material specifically, and records that the deployment address goes on living in `ApplicationData.Current.LocalSettings` while the credential does not. It is cited here for what it does *not* reach: a preferences store exists on every head, so "the override is not remembered" is a choice about consent rather than a consequence of having nowhere to put it.
- [`features/email-content.md`](../features/email-content.md) documents the representation `get_email_content` serves and the sanitizer behind it, which this record leaves unchanged.
- [`features/presentation-plan.md`](../features/presentation-plan.md) is the precedent for a closed, per-member-versioned catalogue crossing the wire to a client that switches over it exhaustively, and for refusing a revision the build does not implement rather than reading it as one it does.
- [`operations/client-endpoint.md`](../operations/client-endpoint.md) is the surface both projections and the inline parts are served over; none exists yet, and [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) adds them.
- The `describes:` marker names the service paths alone, because that is where every part of this decision is implemented today. It gains the client's paths and the contract's when [#1164](https://github.com/Krzysztof318/MailFathom/issues/1164) lands, which is one of the two edits an accepted ADR is permitted.
- Worth revisiting if the opt-in path turns out to be where readers live rather than where they visit. That would mean the default is not carrying its weight and the tree should be widened toward what people actually open the engine for — not that the default should become the engine.
