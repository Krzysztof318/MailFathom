# Client Source Development Instructions

These instructions apply under `frontend/src/` in addition to `frontend/AGENTS.md` and the repository root
instructions. `frontend/AGENTS.md` holds what is true of the stack — the toolchain and the suppression policy, the
dependency and lock-file rules, and driving the running client in a browser — and none of it is repeated here.

What follows is what a linter cannot decide: where a boundary is, what belongs on which side of it, and what a screen
owes the person reading it. Every rule below exists because getting it wrong is invisible in a diff and expensive at
twenty screens.

## What the application is, and is not

MailFathom's client is a **reader of one person's own mail**, served by a deployment that person controls, reaching it
over `/api/client` and over nothing else. It shows what the service already holds, and it asks the service to act;
it does not talk to a mail server, hold a second copy of anything, or decide what MailFathom is allowed to do.

That settles a class of question before it is asked. The client is not where a rule is evaluated, a message is parsed, a
credential is composed, or a permission is judged — each of those is the service's, and a client that re-derives one is
a second implementation that will disagree with the first. When a screen appears to need one, the answer is a route on
`/api/client` that answers it, which is service work with an issue of its own.

It is also a **single-page application**: one document, loaded once, whose every later screen is rendered rather than
fetched. Both heads are that same page — the web head is the bundle `Client.App/vite.config.ts` builds, and the desktop
head is that bundle in a WebView — so it is a property of the client rather than a fact about browsers. Three things
follow. Navigation is the application's own state, so moving between screens changes what is rendered and never asks
for a document. A reload is a cold start rather than a way out, which is why § _UX_ refuses it as the only escape from
an error state: it discards everything the session holds in order to recover from something the screen could have
recovered from itself. And whatever carries routes is a decision rather than an import — the workspace pins no router,
`frontend/AGENTS.md` holds that among four deliberate absences, and adopting one is an issue with a licence review
rather than a line added by the first screen that wanted a second address.

## The package boundary, and what may never cross it

The two packages and the three mechanisms that hold them apart are in [`frontend/README.md`](../README.md). What that
page cannot state is which side a new file belongs on, and the answer is not "whatever imports it".

**`Client.Backend` owns the wire.** The shape of every request and response, the routes, the session, the failure model,
and the parsing that turns a body into a type belong there, because they are what the service's contract actually is.
It is the only place in the client that knows a status code, a header name, or a path.

**`Client.App` owns everything a person sees**, and it receives values that are already correct. A screen never parses,
never reads a status code, and never learns why a call failed from anything but `ClientFailureReason`.

Four things may never cross, in either direction:

- **No React, no DOM, and no browser API into `Client.Backend`.** It declares neither, which is why `readMailAccounts`
  takes a `MailFathomTransport` instead of calling `fetch`: the adapter that calls one is the application's, and the
  boundary is the reason that indirection exists rather than a layer kept in case a second transport appears.
- **No unvalidated value out of `Client.Backend`.** A response body is untrusted input at a trust boundary, so every
  field is checked before it becomes a value the application may render, and every collection carries a bound checked
  during the walk rather than after it. `Client.Backend/src/mailAccounts.ts` is the worked example, and a new operation
  follows its shape.
- **No component, no hook, and no rendering decision into `Client.Backend`.** A type it publishes describes what the
  service said, never how a screen will show it — a label, a colour, a sort order, and a relative time are the
  application's.
- **No catalogue, no locale, and no localization dependency into `Client.Backend`.** Language is a rendering decision
  by the rule above: that package answers with the closed sets — a `ClientFailureReason`, a `MailSynchronizationState` —
  and what each is called in a language is the screen's, named by a table in it.

## Reaching `/api/client`

- One exported function per thing the client asks for, named for what it asks — `readMailAccounts`, not `getAccounts`
  and not `accountsApi.fetch`. It takes the session and the transport and answers a `ClientResult`.
- **An expected failure is a value, never an exception.** The four `ClientFailureReason` values exist because a screen
  does something different with each, so a new operation reuses them rather than inventing a fifth for the same four
  outcomes; a genuinely new outcome is a new member argued in the change that adds it.
- **The credential is a finished header value and nothing composes it here.** Nothing logs it, puts it in a URL, stores
  it where another origin can read it, or passes it to anything but a request on the client surface.
- Bound what a screen asks for. A route that can answer with a mailbox-sized collection is called with the window the
  screen actually shows, and the client refuses an answer larger than it asked for rather than rendering it.

## State: what is stored, what is derived, and where an effect is wrong

- **Store the smallest thing you cannot compute.** Everything computable from stored state is computed during render,
  not stored beside it and kept in step. Two pieces of state that must agree are one piece of state and a function; the
  bug this prevents is the pair that disagrees, which no type catches and no test finds unless somebody thought of it.
- **One owner per piece of state**, and it lives at the lowest component that renders everything reading it. Lifting
  further "so it is available" is what turns a screen into a single component holding fifteen values.
- **An effect synchronizes with something outside React, and that is all it is for**: a subscription, a timer, an
  imperative browser API, or a request going out. `Client.App/src/App.tsx` is the permitted shape — it starts a read,
  ignores the answer if the component stopped listening, and cleans up.
- **An effect is the wrong answer when it computes, copies, or reconciles.** An effect that sets state from props, keeps
  a second value in step with a first, or exists so that something runs "after" something else is a render-cycle bug
  waiting for a slow machine. Derive it during render, or do it in the event handler that caused it: a value that
  changes because a person clicked belongs to the click, not to an effect watching what the click changed.
- **A request cancels or is discarded.** A screen that starts a read and unmounts, or starts a second read before the
  first answers, must not render the older answer — the ordering is not guaranteed and the failure looks like a
  rendering defect rather than a race.

## Components, props, and names

- **A component is one thing a reader can name.** When naming it needs "and", it is two components. When it renders a
  list, the row is its own component: `SpaceLink` beside `SpaceNavigation` is the shape, because the row is what gains
  state, a keyboard path, and a test.
- **A prop travels at most one component that does not read it.** A second such hop means the tree is wrong rather than
  that context is needed — move the component that reads the value to where the value is, or render it as a child.
  Reach for context only for what genuinely belongs to the whole screen, such as the session.
- **A prop is the value, not the container it came from.** A component that needs a display name takes the name; one
  that takes the whole account to read one field cannot be rendered from anything else and cannot be tested without
  building one.
- **A name says what the thing is in this domain**, in the vocabulary the service already uses: `MailAccount`,
  `synchronizationState`, `behind`. Never name after the mechanism — no `data`, `item`, `info`, `handler`, `manager`,
  `utils`, or `helpers`, and no component named for where it sits on the screen when it has a name for what it shows.
  A boolean reads as an assertion (`behind`, `synchronizationEnabled`), and a function for what it does.

## Modules: what one may know about another

- **Files that change together sit together.** A screen's components and the state they share belong in one directory
  named for the screen; something moves up only when a second screen actually needs it, not when one might.
- **Imports go one way: a screen may reach what is shared, and what is shared never reaches a screen.** A shared module
  importing from a feature is a cycle the bundler will tolerate and a reader will not.
- **A package is entered through its entry point.** `Client.App` imports from `@mailfathom/client-backend`, never from a
  path inside it, which is what lets that package rearrange its own files.
- **Never re-export a whole directory to save an import line.** A barrel that exists only for brevity makes every
  consumer depend on everything behind it.

## Where markup ends and logic begins

A component's body reads top to bottom as _what is on the screen_. Anything that takes a paragraph to follow — a
computation, a decision with more than one branch, a mapping from a service value to something a person reads — is a
named function or constant above the component, not an expression inside the markup.

The line is drawn where a reader stops being able to see the structure: a ternary choosing between two elements is
markup, a ternary chain choosing between four is a function returning one. `failureLabels` in
`Client.App/src/shell/ConnectionSummary.tsx` is the shape a mapping takes — a lookup declared once, exhaustive by its
own type, rather than a chain inside the element that renders it.

## The two heads

MailFathom ships a static web bundle and, when it is built, a Tauri desktop application from this same tree. Android,
iOS, and macOS are reachable and supported by nothing.

**Nothing in this tree branches on a platform.** No `if (isDesktop)`, no per-head component, and no module chosen by
target: a difference between the heads is a CSS one — a safe-area inset, a pointer or hover query — or it is a shell
concern that belongs to the shell rather than to the application. A screen that cannot be written without knowing which
head it is running on is a design that has not been finished, and taking that branch is what turns one client into two.

**Which deployment the client belongs to is the worked example of that**, and it is where the branch would have been
most tempting: a web bundle is served by its deployment and a desktop shell is served by nothing. What resolves it is
one rule about addresses rather than a question about heads — `Client.App/src/deployment/adoptedDeployment.ts` reads
the address somebody named, else the one that served the client, and takes the second only where it is an address this
client may address at all. A shell loaded from a scheme of its own resolves to nothing there and is asked for an
address; a page served by its deployment resolves to that deployment and is asked for none. Both meet the same sign-in
screen either way, and what differs is how many controls it renders — which is a value that screen was handed rather
than a head it looked up. Both answers are addresses, which is why neither case has to know about the other, and it is
why `main.tsx` is where the resolution happens: the edge supplies a value, and no screen underneath asks where it came
from.

**The other exception is a shell operation, and it is resolved the same way.** Where the application genuinely depends
on something only a shell can do — opening a followed link outside the application, under
[ADR 0024](../../docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md), and keeping the
credential, under [ADR 0023](../../docs/decisions/0023-where-the-client-keeps-the-credential-it-signs-in-with.md) — the
application declares the operation it needs, and which implementation satisfies it is decided in one module at the
composition root by whether a shell offered the command. `Client.App/src/shellOperations/linkOpener.ts` is that shape: one
function resolves it, `main.tsx` calls that function once, and every component below receives the operation through
context. It is the first module of that shape and `shellOperations/` is where the next one goes, rather than beside the
screen that happens to need it first, because what such a module answers is the application's and not one screen's.
`Client.App/src/signIn/credentialStore.ts` is the other one today and predates the directory, so it still sits beside
the sign-in screen; moving it is a change of its own rather than a side effect of adding the second. What is refused
above is a component, a hook, or a screen asking the question itself, and that refusal is unchanged by either
paragraph.

**What a screen adapts to instead is the width it has been given and what the pointer can do.** Those two are the whole
of it, and the refusal above is not an instruction to adapt to nothing: a window is resized far more often than a head
is changed, and one head produces both shapes anyway. A half-width window on a desktop gets the composition a
phone-width viewport gets — a stack, not a wide layout that stopped fitting — and the same desktop maximized gets the
wide one, out of the same tree at the same breakpoint. `pointer: coarse` is asked where a target has to be big enough
for a finger and `hover` where an affordance would otherwise exist only under one; neither is a question about the
operating system, and a touch screen on a desktop answers both the way a phone does.

The bar is stated rather than left to taste. Every screen is usable from 320 CSS pixels upward and at every width above
it, with no horizontal scrolling of the page: content reflows, and what a narrow width cannot show at once is reached
rather than dropped. Nothing is hidden by width alone — a control that disappears below a breakpoint has moved
somewhere a reader can still get to, or it was not needed at the wide width either. The number is the narrowest
viewport a supported head presents rather than a threshold anybody measured a failure at, and a window dragged narrower
than any phone is where the client is allowed to scroll.

## The two languages

The client reads in English and in Polish, and the mechanism that makes it so is described in
[`frontend/README.md`](../README.md). What that page cannot state is what a person writing a screen owes it. The lint
rule catches a string written into markup and nothing else, so everything below is held by whoever writes the screen.

- **A user-visible string is a catalogue entry, however it reaches the screen.** A default prop, a value a helper
  returns, a message assembled before it is rendered — each is a sentence somebody reads, and the lint rule sees none
  of them.
- **A sentence is one entry with `{name}` holes, never fragments joined at the call site.** Word order, agreement, and
  where a value falls in a sentence all differ between languages, and a sentence assembled from pieces can be
  translated in only the order English happens to use.
- **A key is added to every catalogue in the change that adds it.** The compiler refuses a key one language declares
  and another does not, so what is left to a person is that the second entry is a translation rather than the English
  copied across to satisfy the type.
- **Nothing branches on the language.** No per-locale component and no `if (locale === 'pl')`, for the reason nothing
  branches on a head: a difference between languages is a catalogue entry or an option handed to `Intl`. A screen that
  cannot be written without knowing which language it is in is a screen whose copy has not been finished.
- **A date, a number, a relative time, a list, or a plural is formatted by `Intl` under the active locale.** None of
  that is spelled into a catalogue — a month name, a decimal separator, or a hand-written plural form there is a second
  copy of something the platform already knows and gets right in every language. `Intl.PluralRules` is what selects a
  form when a screen first counts something, which Polish needs and English hides.
- **The list of languages is the one thing never translated.** Each is named in its own language, so somebody who has
  landed in one they cannot read finds their own.

## UX: nothing waits in silence, and nothing waits without a way out

This is the contract's oldest rule and it survived the platform it was written for. It is an obligation on every surface,
not a preference about polish.

- **Every surface that waits says it is waiting**, from the moment the wait starts, in the place the answer will appear.
  A screen that looks finished while a read is in flight is a screen a person acts on twice.
- **Every failure says what failed and offers the way out.** The four failure reasons are four different sentences and
  four different next steps: signing in again, saying the grant is missing, retrying, and reporting a defect. "Something
  went wrong" is none of them, and neither is a status code on a screen.
- **No state is reachable that a person cannot leave.** Every dialog closes, every flow can be abandoned, and every
  error state offers something other than reloading the page.
- **Every screen has its five states, and each is designed rather than defaulted**: loading, empty, partial (some of it
  arrived, or some of it is stale), error, and offline. Empty says why it is empty and what would fill it; partial says
  which part is missing; offline is distinguishable from an empty answer.
- **A destructive action is confirmed, and the confirmation names what it will do** — which message, which account, how
  many. Deleting mail, discarding a draft, and removing an account are destructive. Sending is not confirmed twice, but
  it is withdrawable for exactly as long as the service says it is.
- **Focus is placed deliberately whenever a view changes.** Opening a dialog moves focus into it and traps it there;
  closing returns focus to what opened it; navigating puts focus at the start of the new content. Focus left behind on a
  removed element is where keyboard and screen-reader use silently stops working.
- **Nothing shifts under a reader's cursor.** Content that arrives later occupies space reserved for it. A row that
  moves as the list loads is how somebody opens the wrong message.

## UI: the token layer is the only place a value is written

The withdrawn client had an open defect for exactly one failure — two stated conventions the screens stopped holding to —
so this is written to be mechanically visible in a diff rather than remembered.

- **No literal colour, spacing, radius, or type size appears outside the token layer.** That layer is Tailwind's theme
  plus the `@theme` block in `Client.App/src/styles.css`, which is where MailFathom's own values are declared and the
  only place a hexadecimal colour is written. An arbitrary-value utility — `text-[#0048e0]`, `p-[13px]`, `text-[15px]` —
  is the thing this rule refuses: a value that needs one is a token missing from the theme, so add it there.
- **A breakpoint is a token like any other.** The widths a composition changes at are declared once, beside the colours,
  and every screen composes against those names — so the point where narrow stops is one decision rather than one per
  screen. An arbitrary width such as `min-[734px]:` is the same defect as `text-[#0048e0]`, and is answered the same
  way: add the breakpoint to the theme.
- **A repeated structure has one shape, stated once.** The second screen needing a card, a list row, a section header,
  or a page title uses the first one's component rather than a similar arrangement of utilities. Two implementations of
  one shape is how a client stops looking like one product, and it is invisible in review because each diff is fine.
- **Density and motion are decided once**, in the theme, and a screen does not opt out of either. A duration or an easing
  written into a component is the same drift as a hexadecimal colour.
- **`prefers-reduced-motion` is honoured**, and that is an accessibility obligation rather than a nicety: motion is
  removed under it, not merely shortened, and any transition conveying meaning still conveys it without the movement.

## Accessibility is a property of the two sections above

The bar is not "screen-reader compatible" in the abstract. It is that a person can do everything on the screen without a
mouse, and that a test can find every control by what it is rather than by where it is.

- **Semantic elements before ARIA.** A `button` is a button, a link navigates, a heading is a heading in order, and a
  list is a list. ARIA is for what the platform genuinely has no element for, and a wrong role is worse than no role.
- **Every action has a keyboard path**, in an order that follows the screen. Nothing is reachable only by hover, and
  nothing that takes focus is not operable from the keyboard.
- **Focus is always visible.** Removing an outline without replacing it with something at least as visible is refused.
- **Every control has an accessible name that says what it does**, not what it looks like. Icon-only controls carry a
  label; an image that carries meaning carries alternative text and a decorative one is hidden from the accessibility
  tree. The bar is that the browser suite can assert on a role and a name — a screen whose controls have no names is
  untestable as well as unusable, and a test reaching for a CSS selector is the symptom.

## Performance: what the real mailbox costs

The proof of concept rendered a 200-row list at about 1 600 DOM nodes and settled nothing: the mailbox the client
actually has to render holds 214 000 messages, and a list that keeps every row in the DOM grows with it.

- **A list that can exceed 200 rows is windowed.** Below that, render every row; a list whose length is bounded by
  something the screen chose — an account list, a folder list — is never windowed regardless. The number is where the
  measurement stops saying anything, not a threshold anybody has measured a failure at.
- **No render-blocking work in an effect, and none during render.** Sorting, parsing, grouping, or measuring a whole
  collection before the first paint is what makes a screen appear frozen. Do it where the data arrives, memoize it
  against the data rather than against the render, or ask the service for it in the shape the screen needs.
- **`memo`, `useMemo`, and `useCallback` are answers to a measurement**, never to a suspicion. Each costs a comparison
  on every render and keeps its inputs alive, and a dependency array that is wrong is a stale screen rather than a slow
  one — which is a correctness defect bought in exchange for nothing.
