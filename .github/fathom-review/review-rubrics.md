## What to look for

Seven rubrics. Apply each one the change actually reaches, and say nothing about the
ones it does not: these describe where defects have been found here, not a form to
fill in.

**Which rules a file is judged against follows the stack it sits in.** `backend/` is the
.NET solution and `frontend/` is the pnpm workspace of React and TypeScript, and each
carries its own `AGENTS.md` files, its own toolchain, and its own answer to questions the
other also asks. So a rule stated for one of them supports no finding against a file in
the other: a C# convention applied to a `.tsx` file and a React rule applied to a `.cs`
file are the same wrong-rule failure, and it is the one this rubric is most exposed to now
that a pull request can carry both. Where a rubric below is split, what stands under **The
service** holds for `backend/` and what stands under **The client** for `frontend/`;
everything outside those two headings holds wherever the change reaches. A file in neither
tree — under `docs/`, `deploy/`, `scripts/`, `.github/`, or at the root — reaches the
unsplit paragraphs alone, and the last rubric not at all.

### The repository's rules

`AGENTS.md` is a contract, so breaking it is a defect even where the code would run
correctly. Give these the same weight as a wrong result.

Two hold in either stack. A dependency, service, image, or copied sample the change
introduces needs a row in `THIRD_PARTY_LICENSES.md` recording its exposure and the version
the graph resolves. And every file that is not C# carries the licensing header by hand, in
the form its own readers parse — three `// ` lines opening a `.ts`, `.tsx`, `.js`, `.mjs`,
or `.cjs` module, one `/* … */` block opening a `.css` file, one `<!-- … -->` comment
opening an `.html` document, `# ` lines in a `.yml` file or under a `.sh` shebang — because
no formatter writes one there and neither gate reports its absence.

#### The service

- **Boundaries.** `Domain` depends on no framework. `Application` depends only on
  `Domain` and owns its ports. `Infrastructure` keeps EF Core entities, MailKit types,
  Npgsql and `bytea` details, MCP SDK types, and provider-specific AI types inside the
  adapter that owns them. `Mcp` maps protocol to use cases and holds no persistence or
  mail-protocol logic. `Host` is composition, configuration, and wiring only. Raw RFC
  822 content is reached through `IEmailContentStore` and nothing else.
- **Naming.** Domain-correct, unabbreviated, and unambiguous where a reader meets it:
  `Email` and never `Message` or `MailMessage` for the mail artifact, `IMailboxSession`
  or `IPersistenceSession` rather than a bare `Session`, a method named after the
  result it produces rather than `Handle`, `Process`, `Manage`, or `Execute`.
- **Type shape.** Enum members carry explicit contiguous values that are never
  reordered or reused, `[Flags]` members are explicit powers of two with `None = 0`, a
  value that must publish an identity surviving a rename is a closed enumeration rather
  than an enum, data that represents a value is an immutable record or value object,
  implementations default to `internal sealed`, collections cross boundaries as
  read-only abstractions, and byte payloads cross them as `ReadOnlyMemory<byte>` or a
  span rather than `byte[]`.
- **Imports.** Every type reached through a `using` and written by its simple name,
  qualification only for a real collision and then on every side of it, and no `using`
  or `global using` alias anywhere.
- **Async and time.** I/O asynchronous end to end, a `CancellationToken` accepted,
  placed last, and propagated rather than replaced with `None` inside a chain, `Task`
  unless measurement chose `ValueTask`, no blanket `ConfigureAwait(false)` in
  application code, `DateTimeOffset` for timestamps, and an injected `TimeProvider`
  wherever current time affects behavior.
- **Failures.** An expected application failure is an explicit result type and an
  exceptional one is an exception; a `catch` exists only to add context, translate at a
  boundary, apply a defined retry policy, or complete cleanup, and preserves the
  original as `InnerException`. `null` never encodes more than one state.
- **Ownership.** A type that owns a resource implements the disposal contract that
  matches it and never disposes a dependency the container owns.
- **Documentation.** Public types and members documented, with XML documentation that
  still matches the signature and behavior it describes.
- **Email invariants.** `(account, folder, UIDVALIDITY, UID)` is the remote occurrence
  identity, retrieval never sets `\Seen`, synchronization and outbox work is
  idempotent, an MCP read is served locally and never triggers a synchronous IMAP
  fetch, and every public query is keyset-paginated and bounded.

#### The client

- **What the client is, and is not.** It reads one person's own mail from a deployment
  that person controls, over `/api/client` and nothing else. It is not where a rule is
  evaluated, a message is parsed, a credential is composed, or a permission is judged —
  each of those is the service's, and a client that re-derives one is a second
  implementation that will disagree with the first. A screen that appears to need one
  needs a route that answers it instead.
- **The package boundary.** `Client.Backend` owns the wire — the routes, the request and
  response shapes, the session, the failure model, and the parsing that turns a body into
  a type — and is the only place in the client that knows a status code, a header name, or
  a path. `Client.App` owns what a person sees and receives values that are already
  correct. Three things cross in neither direction: React, the DOM, or a browser API into
  `Client.Backend`, which is why an operation takes a `MailFathomTransport` rather than
  calling `fetch`; an unvalidated value out of it; and a component, a hook, or a rendering
  decision into it, since a type it publishes says what the service said and never how a
  screen will show it. `Client.App` imports `@mailfathom/client-backend` and never a path
  inside it.
- **The two heads.** Nothing in this tree branches on a platform. No `if (isDesktop)`, no
  per-head component, and no module chosen by target: a difference between the web bundle
  and the Tauri application is a CSS one or a shell concern, and taking that branch is what
  turns one client into two.
- **Failures.** An expected failure is a `ClientResult` value rather than an exception, and
  it carries one of the four `ClientFailureReason` members, which exist because a screen
  does something different with each. A new operation reuses them; a fifth member for the
  same four outcomes is the defect, and a genuinely new outcome is argued in the change
  that adds it.
- **State.** Store the smallest thing that cannot be computed, and compute the rest during
  render rather than storing it beside its source and keeping the two in step. One owner
  per piece of state, at the lowest component that renders everything reading it. An effect
  synchronizes with something outside React — a subscription, a timer, an imperative
  browser API, a request going out — and is the wrong answer when it computes, copies, or
  reconciles: an effect that sets state from props, keeps a second value in step with a
  first, or exists so that something runs *after* something else is a render-cycle bug
  waiting for a slow machine.
- **Naming.** The vocabulary the service already uses — `MailAccount`,
  `synchronizationState`, `behind` — and never the mechanism: no `data`, `item`, `info`,
  `handler`, `manager`, `utils`, or `helpers`, and no component named for where it sits on
  the screen when it has a name for what it shows. A boolean reads as an assertion and a
  function as what it does.
- **Suppressions.** `tsconfig.base.json`, `eslint.config.ts`, and `--max-warnings 0` are
  argued rather than default, so relaxing one to make a file pass moves that file's problem
  onto every file and is a decision with an issue of its own. A suppression is arguable
  only where the checker is provably wrong about that line, and is then written as narrowly
  as the tool allows with its reason above it: `// eslint-disable-next-line` naming the
  rule, or `@ts-expect-error` — never `@ts-ignore` — with the sentence saying what the
  compiler cannot see. `any`, a cast asserting something the code has not checked, `!` on a
  value the type says may be absent, a file-level `/* eslint-disable */`, and a bare
  `eslint-disable` naming no rule are each refused outright.
- **Dependencies.** An exact version in the package's own `package.json`, never a range,
  with `pnpm-lock.yaml` regenerated by pnpm in the same change — both gates install
  `--frozen-lockfile` and fail on a manifest and a lock file that disagree. A second
  registry source is a supply-chain decision rather than a line added to make an install
  work.

### Security and privacy

Email content, metadata, embeddings, retrieval snippets, tokens, certificate material,
and audit traces are sensitive by default, and an embedding inherits the retention,
access, deletion, and export constraints of the mail it was derived from.

- Untrusted input — email HTML, headers, filenames, URLs, tool arguments, model output,
  and whatever a remote server returns — is validated and encoded for the context it
  reaches, and an explicit size and count limit is applied at every public or remote
  boundary before the value is expanded rather than after.
- Nothing sensitive reaches a log, span, exception message, or exporter: no
  credentials, tokens, message bodies, attachment content, or raw MIME, and a value
  derived from an exception, a configuration entry, a certificate, or a server response
  is redacted first, on startup and shutdown paths too.
- Secrets are compared in constant time, tokens and security-sensitive identifiers come
  from a cryptographically secure generator, and database roles, OAuth scopes,
  filesystem access, and certificates carry least privilege.
- A security decision that fails open where the documentation says it fails closed, an
  authorization check reachable on only one of several paths, or a trust decision taken
  on a key alone is the most serious kind there is.
- Options are validated at startup, so unsafe or misspelled configuration fails fast
  instead of binding a default.

**The client is on the far side of that boundary rather than inside it**, so four of those
rules take a shape of their own there:

- A response body is untrusted input at a trust boundary. Every field is checked before it
  becomes a value the application may render, and every collection carries a bound checked
  during the walk rather than after it — a route that can answer with a mailbox-sized
  collection is called with the window the screen shows, and an answer larger than what was
  asked for is refused rather than rendered.
- The credential arrives as a finished header value and nothing in the client composes one.
  Nothing logs it, puts it in a URL, hands it to anything but a request on the client
  surface, or stores it where another origin can read it.
- Mail is untrusted text on a screen exactly as it is in a parser. A body rendered as HTML,
  a link whose scheme came from a server value unchecked, or an attachment name written
  into a path are each the injection this rule exists for.
- A capture of a signed-in client — a screenshot, a snapshot, a trace, a console log, a
  response body — is somebody's mail, and so is a fixture drawn from a real mailbox.
  Neither belongs in this tree, in a pull request, or in an issue.

**When the pull request carries the `security` label.** Read the `labels` of
`pull-request.json` before the first pass. That label is this project's statement that the
change needs a security review before it merges — `docs/operations/issue-tracking.md`,
"Labels" — written on the issue and carried onto the pull request by a workflow of its own.
It is the one input here that changes how you work rather than only what you judge:

- Apply the rubric above to **every** file in front of you — every file of your group if
  you are reading one, the whole change if you are judging it — and not only to the ones
  whose diff invites it. What this label exists for is the path nobody was looking at: the second
  call site that skips the check, the failure branch that returns the value unredacted,
  the fake that stands in for the very thing being hardened.
- Where the change closes the security-labelled issue, confirm the weakness that issue
  names is closed on every path the change reaches, rather than on the one the diff
  illustrates. Merging closes the issue, so a weakness still reachable through a second
  entry point leaves a closed issue and a live defect. That is the most serious level
  there is, as a security defect and not merely as an unmet acceptance item. Where the label
  came from an issue the change is only related to — one you will not find in
  `issues.json` — there is no acceptance list to hold it to, and the rest of this section
  is the whole of what the label asks for.
- This widens what you **read** and nothing about what you report. The callers, the other
  implementations of the port, and the configuration that reaches it are all worth
  opening; a defect in code this change does not touch is still not a finding, and where
  reading wider is what shows the change to be incomplete, the finding anchors to the line
  in the diff that leaves it so.
- Say that the pass ran and what surface it covered — in your notes if you are reading a
  group, in the summary if you are judging, and there under an approval as much as under
  findings. The label asks for a security review, and a verdict that does not say what was
  examined is not evidence that one happened.

**The sweep is what the first three passes are for.** `REVIEW POSTURE` at the top of your
prompt says which kind of pass this is, and where it reads `settling` — a fourth automatic
pass or later — the four points above narrow to one: apply the security rubric to what the
change touches, as every other rubric here is applied, and leave the wide sweep behind. It
earns its cost while the change is still being shaped and stops earning it once the author
is answering threads, because a fifth reading of a path no fix moved finds what the four
before it already read and let through. Nothing else about the label changes: the costlier
model still performs the pass, the rubric above still applies in full to what the pass
reads, and a security defect is still the most serious kind there is. Say in your notes or
your summary that the pass was a settling one and what it covered, for the same reason the
fourth point gives — a verdict that does not say what was examined is not evidence of
anything.

The absence of the label means nothing at all: the rubric above applies to every change
that reaches it, and an unlabelled change is not read more loosely for it. An entry of
`issues.json` whose `labels` are `null` is an issue nobody was given, so the judge says
that in the summary rather than deciding either way about what it asked for.

### Reliability

#### The service

- Every external call is bounded by a timeout, and its failure is sorted into caller
  cancellation, shutdown, timeout, authentication failure, or transient transport
  failure rather than collapsed into one outcome.
- A retry exists only where repeating is safe, and is bounded, jittered, and never
  nested inside another retry. An exhausted budget becomes the domain outcome its
  caller acts on.
- Mailbox synchronization, MIME processing, embedding generation, and delivery run
  under an explicit concurrency limit with backpressure, and state a crash could
  duplicate or lose is durable.

#### The client

- A request cancels or is discarded. A screen that starts a read and unmounts, or starts a
  second read before the first answers, must not render the older answer: the ordering is
  not guaranteed, and the failure arrives looking like a rendering defect rather than a
  race.
- A failure is sorted into the four reasons the screen acts on differently rather than
  collapsed into one, and a status the service can answer with that nothing maps is the
  same defect as an unhandled failure mode in the service.
- Repeating a request is the person's to ask for, through the way out the failure offers.
  A client that retries on its own is a retry nested inside whatever the service already
  does.

### Performance

Measure before optimizing, in either stack. A micro-optimization with no measurement
behind it is not a finding, and neither is a cost you cannot demonstrate.

#### The service

- Work is proportional to the input: a database projection rather than a full entity, a
  streamed MIME body rather than one buffered twice, no large `bytea` tracked by EF
  Core without a reason, no query issued once per element of a sequence, and no
  unbounded result set.
- A sequence is enumerated once. A query that is filtered, counted, and read again is
  materialized first, and a lazily evaluated query is never handed to a caller that
  will iterate it more than once.

#### The client

- The mailbox this client has to render holds 214 000 messages, so a list that can exceed
  200 rows is windowed. A list whose length is bounded by something the screen itself chose
  — an account list, a folder list — is never windowed regardless of the number.
- No render-blocking work during render or in an effect. Sorting, parsing, grouping, or
  measuring a whole collection before the first paint is what makes a screen appear frozen;
  do it where the data arrives, memoize it against the data, or ask the service for the
  shape the screen needs.
- `memo`, `useMemo`, and `useCallback` answer a measurement and never a suspicion. Each
  costs a comparison on every render and keeps its inputs alive, and a dependency array
  that is wrong buys a stale screen — a correctness defect — in exchange for nothing.

### Clean code

A comment explains why the code must behave this way and never narrates a readable
statement, and a misleading name is renamed rather than annotated. No abstraction without
a current testing, protocol, or replacement need. Both hold in either stack.

#### The service

- A method reads as one sequence of decisions: guard clauses instead of nesting, a
  named private method instead of a comment announcing the next stage, and blank lines
  separating the guards, each stage, and the return.
- Work over a sequence is a LINQ pipeline that names the operation, and a loop survives
  only where the body does something a query cannot express. A pipeline never carries a
  side effect, and a chain that stops reading as one sentence is broken into a named
  local or a named method.
- No inheritance used only to share implementation, and no collaborator hidden behind a
  service locator or static mutable state.

#### The client

- A component is one thing a reader can name. When naming it needs "and", it is two
  components; when it renders a list, the row is its own component, because the row is what
  gains state, a keyboard path, and a test.
- A prop travels at most one component that does not read it. A second such hop means the
  tree is wrong rather than that context is needed. And a prop is the value rather than the
  container it came from: a component taking a whole account to read one field of it cannot
  be rendered from anything else and cannot be tested without building one.
- Files that change together sit together, in a directory named for the screen. Imports go
  one way — a screen may reach what is shared and what is shared never reaches a screen —
  and a barrel that exists only to save an import line makes every consumer depend on
  everything behind it.
- A component's body reads top to bottom as what is on the screen. A computation, a
  decision with more than one branch, or a mapping from a service value to something a
  person reads is a named function or constant above the component rather than an
  expression inside the markup: a ternary choosing between two elements is markup, and a
  chain choosing between four is a function returning one.

### The screen a person uses

This rubric is the client's alone. A change that renders nothing reaches none of it, and
saying so is not required — but a screen is not proven by compiling, and every rule here is
invisible in a diff that type-checks.

- **Nothing waits in silence.** Every surface that waits says it is waiting, from the
  moment the wait starts, in the place the answer will appear. A screen that looks finished
  while a read is in flight is a screen a person acts on twice.
- **Every failure says what failed and offers the way out.** The four failure reasons are
  four different sentences and four different next steps — signing in again, saying the
  grant is missing, retrying, and reporting a defect. "Something went wrong" is none of
  them, and neither is a status code on a screen.
- **Every screen has its five states, each designed rather than defaulted**: loading,
  empty, partial, error, and offline. Empty says why it is empty and what would fill it,
  partial says which part is missing, and offline is distinguishable from an empty answer.
- **No state is reachable that a person cannot leave.** Every dialog closes, every flow can
  be abandoned, and every error state offers something other than reloading the page. A
  destructive action is confirmed and the confirmation names what it will do — which
  message, which account, how many.
- **Focus is placed deliberately whenever a view changes.** Opening a dialog moves focus
  into it and traps it there, closing returns focus to what opened it, and navigating puts
  focus at the start of the new content. Focus left on a removed element is where keyboard
  and screen-reader use silently stops working.
- **Nothing shifts under a reader's cursor.** Content arriving later occupies space
  reserved for it; a row that moves as the list loads is how somebody opens the wrong
  message.
- **No literal colour, spacing, radius, or type size outside the token layer**, which is
  Tailwind's theme plus the `@theme` block in `Client.App/src/styles.css`. An
  arbitrary-value utility — `text-[#0048e0]`, `p-[13px]`, `text-[15px]` — is what this rule
  refuses, and a value that needs one is a token missing from the theme. Duration and
  easing are the same drift as a hexadecimal colour.
- **A repeated structure has one shape, stated once.** The second screen needing a card, a
  list row, a section header, or a page title uses the first one's component rather than a
  similar arrangement of utilities. This is the one rule here that is invisible in review
  by construction, because each diff is fine on its own.
- **`prefers-reduced-motion` is honoured**, and motion is removed under it rather than
  shortened; a transition that conveys meaning still conveys it without the movement.
- **Semantic elements before ARIA.** A `button` is a button, a link navigates, a heading is
  a heading in order, a list is a list, and a wrong role is worse than no role.
- **Every action has a keyboard path**, in an order that follows the screen; nothing is
  reachable only by hover. Focus is always visible, and an outline removed without
  something at least as visible in its place is refused.
- **Every control has an accessible name that says what it does**, not what it looks like.
  An icon-only control carries a label, a meaningful image carries alternative text, and a
  decorative one is hidden from the accessibility tree. The bar is that a test can find a
  control by its role and its name — a test reaching for a CSS selector is the symptom of a
  screen that has none.

### Tests and documentation

A behavior change carries unit tests. Durable documentation is updated in the same change
set, and prose that describes a validator, a guarantee, or an ownership rule the code does
not implement is a defect in the documentation.

**The service.** `backend/tests/AGENTS.md` states what a test must look like: no real
clock, no real delay, no wall-clock ordering, no test that cannot fail, and a fake that
preserves the ordering and identity guarantees of what it replaces.

**The client.** `frontend/tests/AGENTS.md` answers the same questions again rather than
translating those answers, so read it rather than reasoning from the service's:

- A test sits beside the source it covers and is named after it — `mailAccounts.test.ts`
  beside `mailAccounts.ts`, `App.test.tsx` beside `App.tsx` — because the package boundary
  is what a test outside the package would have to reach through. Nothing lives under
  `frontend/tests/`, which holds the contract and no test.
- A test asserts what a person sees and does: queried by role first and then by the text
  they would read, never by a class name, a `data-testid`, or a position in the tree, and
  never through hooks, the value a `useState` holds, the props passed downwards, or whether
  a component re-rendered. A refactor that changes nothing on the screen must rewrite no
  test. Tailwind class names are styling and are asserted nowhere.
- `Client.Backend` is pure functions over values, so thin coverage of request construction,
  response parsing, the failure model, and the session has no excuse: every status, every
  shape a body can arrive in, and every shape it must be refused in is reachable for
  nothing. `Client.App` is covered through rendering and interaction as far as jsdom
  reaches, which is not as far as layout — element size, position, overflow, scrolling, and
  focus rings are outside what a test here may claim.
- `MailFathomTransport` is the network boundary and the only thing a read fakes. Nothing
  patches `fetch`, starts a server, or adds a request-interception package, and an
  application test never `vi.mock`s a module of `Client.Backend` — faking the parsing and
  the failure mapping leaves a test asserting that a screen renders whatever it was handed.
- A component that reads the current time takes it from its caller; where it genuinely
  cannot, the clock is fixed with `vi.useFakeTimers()` and released in an `afterEach` of
  the same file. Randomness is passed in or stubbed and restored. Nothing sleeps —
  `await screen.findBy*` waits on the document rather than on a duration.
- A claim that needs real layout, real navigation, a real exchange, or a running deployment
  belongs in the Playwright harness rather than in this suite. Moving it there is the
  answer; dropping it is not.

## What is not a finding here

- Anything a tool already enforces. In the service that is formatting, the
  `.editorconfig` severities, the `CA` and `IDE` set, Roslynator, the xUnit analyzers, the
  banned symbols in `.config/BannedSymbols.txt`, and the threading analyzers, all of which
  `Required CI` fails on. In the client it is `tsconfig.base.json`, the lint set in
  `eslint.config.ts` — which `pnpm lint` runs at `--max-warnings 0`, so a warning is
  already a failure — and Prettier, which reads `.editorconfig` for whitespace and quote
  style; both verification gates run all three for a change reaching `frontend/`, before
  the pull request exists. Repeating one of them costs a thread to resolve for nothing.
- A request that the client adopt a component library, a router, a state container, or a
  data-fetching library. All four are absent deliberately, and adopting one is a decision
  with its own issue and its own licence review rather than a review suggestion. The
  finding in that neighbourhood runs the other way: a change that adds one without that
  argument is taking a decision in a diff.
- A coverage number, threshold, or collector for the client suite. Nothing measures
  coverage there, and whether anything should is a decision of its own. Naming the specific
  untested behavior remains a finding; asking for a percentage does not.
- Backward compatibility, migration paths, deprecation shims, versioning machinery, or
  obsolete markers. `AGENTS.md` § "Project status" refuses all of them outright, so
  asking for one is a defect in the review. What that section does ask for is the
  opposite reading of the same paragraph: a breaking change to a configuration key, a
  database schema, an MCP tool contract, or a public API has to be argued rather than
  assumed, so a change that takes one silently is a finding.
- Praise, a summary of what the change does, or a restatement of the diff.
- Speculative refactors, alternative designs the change is not obliged to adopt, and
  suggestions that begin "consider".
- A request for tests in general. Name the specific untested case and the behavior that
  would go unnoticed, or say nothing.
- A row of `obligations.json` restated as a finding. The index is where to look, and it
  is derived from file names and declared markers, so it points at obligations a change
  does not always incur: a rename with no behavior change owes no test, a page whose
  marker covers a path may say nothing about the part that moved, and a register may
  already carry the row. Confirm it in the file or drop it. A finding whose whole
  content is that a file was not touched is a defect in the review.
- A defect in code, documentation, or tests this change does not touch. You are reading
  the repository to judge this change, not auditing it.
- A rubric item the change does not reach. The lists above say where to look; a finding
  exists because the code is wrong, never because a category went unmentioned.
- Anything that would quote a credential, a token, message content, or raw MIME.
