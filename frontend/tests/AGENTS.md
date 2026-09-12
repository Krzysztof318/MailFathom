# Client Test Instructions

These instructions govern every test in the client stack, in addition to the repository root instructions. They are
reached from the root table rather than from the directory a test sits in, because most of the client's tests do not sit
here: a unit test sits beside the source it covers, and this directory holds the contract beside the three suites that
belong to neither package. The first section says why the two are placed differently.

`backend/tests/AGENTS.md` is the same file for the service, and the two share only what the root one states for both.
Nothing below translates a C# convention into TypeScript; where the service's answer does not survive the stack, the
question is answered again rather than reworded.

## Where a test lives

- **Beside the source it covers**, named after it: `mailAccounts.ts` is covered by `mailAccounts.test.ts` in the same
  directory, and `App.tsx` by `App.test.tsx`. That holds in both packages.
- The reason is the package boundary rather than taste. What separates `Client.Backend` from `Client.App` is the
  resolver — each package's manifest and `tsconfig.json` decide what a file in it can import at all — and a component
  test has to import the component, which is not part of either package's published entry. A test tree beside `src/`
  could reach one only through a relative path out of the package, which is the one thing that boundary exists to
  refuse. A test inside the package inherits it instead: a `Client.Backend` test cannot import React, exactly as its
  source cannot, and nothing has to check that it did not.
- `frontend/tests/` therefore holds this file, the browser suite beside it, [the end-to-end suite](#the-end-to-end-suite)
  under `end-to-end/`, [the desktop suite](#the-desktop-suite) under `desktop/`, and [the corpus](#the-corpus) all three
  read — and nothing else. A unit test written here would resolve neither package, which is the whole of the argument
  above; the three suites are what can live here precisely because they import neither — each drives a built client over
  HTTP rather than importing a module out of one, and the corpus is data rather than a test.
- **The suites are told apart by the name, and the last three by the directory.** A unit test is `*.test.ts` or
  `*.test.tsx` beside its source; a browser spec is `*.spec.ts` under this directory. Each runner's default finds its
  own and neither finds the other's, so a file named for the wrong one silently joins the wrong suite — and a browser
  spec run by Vitest would fail on an import Playwright supplies. The three browser suites share that extension and are
  separated by where they sit: `end-to-end/` is the third one and `desktop/` the fourth, and `playwright.config.ts`
  ignores both paths so a spec needing a deployment or a shell binary cannot be picked up by the suite that has
  neither.
- Neither runner is given an `include` glob, so what makes a file part of a suite is its name and nothing else. A helper
  either suite imports is an ordinary module and carries neither marker in its name.
- **A subject too large for one file is split by the concern each group of tests exercises**, into files named
  `<Subject>.<concern>.test.tsx` beside the source, and the arrangement they share moves into one `<Subject>.harness.tsx`
  next to them. `App.tsx` is the worked example and the reason the rule exists: ninety-two tests in one file each
  mounting the whole application meant a timeout had two thousand lines of setup to look at rather than one screen's,
  and the file was where a new test went to inherit whatever the nearest one did. So the frame, the session, sign-in,
  the deployment, language, telemetry, and the shell's layers are seven files over one `App.harness.tsx`. The threshold
  is the same judgement the root instructions ask of a directory rather than a line count: a file stops being one thing
  when its `describe` blocks no longer read as one subject.
- **A harness is a module and not a suite.** It exports the doubles, the render, and the helpers, and it carries no
  `describe` and no `it` — the hooks a family shares are exported as one function each file calls at its top level, so
  a reader of that file can see what its tests are given rather than having to know what an import did to them.
  `frontend/vitest.config.ts` leaves a `.harness.tsx` out of the coverage report, because it runs only under a test and
  asserts nothing itself.

## The unit runner

- **`pnpm test` is the whole of how the unit suite runs**, locally and in CI. It is `vitest run` — one pass,
  non-interactive, both packages — and there is no second invocation to drift from it. Both verification gates run it
  for any change reaching the client stack. `pnpm test:browser` is the other suite and runs elsewhere, which
  [the browser suite](#the-browser-suite) below decides.
- `frontend/vitest.config.ts` declares one project per package, because the two are tested differently: `Client.Backend`
  under `node` and `Client.App` under `jsdom`. A test that needs a DOM in the first project has been written in the
  wrong package.
- The `Client.App` project extends that package's own `vite.config.ts` rather than restating any of it, so a test runs
  through the resolver, the plugins, and the `__MAILFATHOM_VERSION__` substitution the shipped bundle is built with.
  A screen proven against a second arrangement proves nothing about the first.
- **Globals are off.** Import `describe`, `it`, `expect`, `vi`, and the hooks from `vitest` in every file. An ambient
  global set would be a second way to write a test, and it would put declarations back into a package whose whole
  contract is that `types` is empty.

## Naming

- `describe` names the subject: the exported function, or the component. `it` states one behaviour as a sentence that
  reads after the word _it_ — `it('reads an account that has never synchronized as one with no time on it')`. The
  runner prints the two together, so that sentence is what a failure reports and it is written to be read by somebody
  who has not opened the file.
- `Member_Scenario_ExpectedBehavior` is the service's form and stays there. It exists because a C# test is a method and
  a method name has no spaces; here the name is a string, and compressing a sentence into three underscore-joined
  fragments would lose the only advantage the string has.
- The `// Arrange`, `// Act`, `// Assert` markers are the service's too, and are not written here. A test in this suite
  is a few lines and a blank line is the boundary; one that needs the markers to be readable is a test doing more than
  one thing.
- One test describes one behaviour. `it.each` is how the same behaviour is stated over several inputs, and it is this
  suite's counterpart to `[Theory]` — the status-to-reason mapping and the shapes a parser refuses are the worked
  examples.

## What a test asserts

- **What a person sees and does.** Query the rendered output the way somebody using the screen would find it: by role
  first, then by the text they would read. Never by a class name, never by a `data-testid`, and never by the position
  of an element in the tree.
- **Never through internal state.** A component's hooks, the value a `useState` holds, the props it passed downwards,
  and whether it re-rendered are not the contract. A refactor that changes none of what is on the screen must rewrite
  no test, and that is the whole reason for the rule.
- Tailwind class names are styling rather than behaviour and are asserted nowhere. What a screen looks like is decided
  by looking at it.
- An absence is asserted with the query that would have found the thing — `queryBy*` returning `null` — beside a test
  that produces it, so a selector that stopped matching anything fails rather than passing everything.

## Every assertion waits for the condition it reads

This is the rule both suites are held to, and it is written out because getting it wrong produces a test that passes on
the machine it was written on and fails on a loaded one — which reads as a defect in whatever change was in the tree
rather than as a defect in the test. It has been the whole of this suite's flakiness: the same handful of tests failing
under a second gate running beside them, passing alone, and passing on the re-run.

- **An `await` is not a barrier.** `await screen.findByText(…)` resolves the moment _that_ text is on the screen and
  says nothing about anything else — a second read that has not answered, an effect that has not run, a record a double
  has not been handed. So a `getBy*`, a `queryBy*`, or a read of a double placed after it is reading a moment that has
  no relationship to the state it asserts on, and a machine loaded enough to put the two commits apart is where it
  stops holding. `findBy*` for something that will be on the screen, `waitFor` around the exact condition otherwise.
- **Three shapes are where it always is**, and each of them is an `await` for one thing followed by a read of another.
  An assertion on which routes were asked for, after awaiting the screen the _first_ of them drew. An assertion on what
  a double was last handed, after awaiting something a different answer produced. And an assertion on
  `document.activeElement` after a view changed, because placing focus is an effect and an effect runs after the commit
  that inserted the thing it is placed on — the two are the same screen and not the same moment.
- **A gesture is not one of them.** Focus a handler moves — a key that walks a list, a press that closes a dialog — is
  in place when the event returns, and waiting for it would say a synchronous thing is asynchronous. What is waited for
  is what a _render_ produced.
- **An absence cannot be waited for**, so a `queryBy*` asserting `null` is written after the thing that would have
  produced the element has been waited for. The test that proves the element does appear is what keeps that assertion
  honest.
- **Where a fake clock is installed, the wait drives it.** Advancing by exactly a timer's own duration assumes the
  effect that scheduled it has already run; `waitFor` after the advance is what makes that true rather than assumed,
  because it advances the clock again on each attempt.
- **Neither of the two budgets is a substitute for any of this.** `frontend/vitest.config.ts` bounds a test and
  `src/Client.App/vitest.setup.ts` bounds one wait, both above what the largest tests here measure, and each carries the
  measurement it was set from. They exist so that a slow screen is not reported as a broken one — a test that is only
  green because of them is a test with a race still in it, and raising either to make one pass, retrying a test, or
  marking one flaky is refused.

## The two packages are covered differently

- **`Client.Backend` is ordinary logic and there is no excuse for thin coverage of it.** Request construction, response
  parsing, the failure model, and the session are pure functions over values, so every branch is reachable for nothing:
  each status the service can answer with, each shape the body can arrive in, and each shape it must be refused in. It
  is where a wrong answer reaches a person as wrong mail, which is why a malformed body is asserted to be refused rather
  than read as a directory with a hole in it.
- **`Client.App` is covered through rendering and interaction, as far as jsdom reaches.** A component that carries
  behaviour and has no test is a gap to justify, not a default. What jsdom cannot answer is anything about layout: it
  computes no geometry, so element size, position, overflow, scrolling, focus rings, and anything visual are outside
  what a test here may claim.

## What is faked, and where

- **The network boundary is `MailFathomTransport`, and it is the only thing a read fakes.** It is a function the caller
  supplies, so a test hands one over and nothing patches `fetch`, starts a server, or adds an HTTP mocking package.
- Every component that reads takes its transport, or the value already read through one, from its caller, so no
  application test replaces a module to fake the network. `Message` is the shape stated in one component: it declares
  the transport as a prop, `App` hands it down, and its test hands one over with no `vi.mock` anywhere in the file.
  Where the credential is part of what is being proven, the `CredentialStore` is the second thing supplied that way —
  a fake holding a map is enough, and it is what lets a test assert what was kept rather than which method was called.
  A component that reached for either itself would be a seam in the wrong place, and the answer is to move it rather
  than to reach for `vi.mock`.
- **Never `vi.mock` a module of `Client.Backend` from an application test.** The parsing and the failure mapping are
  part of what the screen is being proven against; faking them leaves a test that asserts a screen renders whatever it
  was handed.
- No mock service worker, no request-interception package, and no local HTTP server. A real exchange belongs to the
  browser suite below.
- **What is faked is the boundary; what travels over it is [the corpus](#the-corpus).** The section below is where the
  example mail itself is decided, and the two rules do not overlap: this one says nothing may stand between the client
  and its transport, and that one says the values handed to whatever does stand there are written once.

## The corpus

`fixtures/` is one corpus of example mail, and it is what every client check reads. It exists because the same shapes
were being invented in three places at once — the browser suite, a throwaway spec, and whatever a session wrote before
it could take a screenshot — and three copies of one corpus is three places to be wrong about a contract the service
owns. Reaching a populated screen is most of the work in a client task, and it is work that had already been done.

- **It is data, never routing.** It exports values, and every consumer decides how they reach the client: `page.route`
  in the browser suite, a transport function in a unit test, and a second `MailFathomTransport` under the development
  server — `frontend/README.md` § _Looking at a screen with mail in it_ is that third consumer, and it is why the
  corpus is read by something that is not a test at all. Nothing in it parses a request, names a route, or knows
  what a `fetch` is. Where an answer genuinely depends on what was asked for — how far into a folder somebody has read,
  whether the reader asked for a sender's pictures — it is stated as a function of that question and the consumer
  decides what was asked; that is still data, and it is the only shape a mailbox of two hundred thousand messages has.
- **It states what the service answers, not what the client parses.** No file in it imports a type from
  `@mailfathom/client-backend`. A fixture typed by the parser that reads it would agree with that parser by
  construction and say nothing about the deployment, which is the one thing a fixture is for.
- **Every message body carries a `plainText` object.** It is not an alternative representation a sender may have
  omitted: it is what MailFathom derived, present on every readable body whatever the message carried. A `null` there
  is refused by `mailBody.ts` before any screen sees it, and what a reader meets instead is the pane saying the message
  could not be read — which reads as a defect in the client and is a malformed fixture. What the message route's own
  `body.plainText` says is a different thing, and it is a `boolean`: whether the _message_ carried a text part.
- **It covers the states a screen has to be looked at in, not only the resting one.** An empty folder, a message whose
  sender wrote no text part, a conversation long enough that the screen folds it, a mailbox whose synchronization is
  failing, and a mailbox that is behind are each in it, because none of them can be reached from the resting corpus by
  scrolling or clicking and each is a screen somebody has to be able to draw.
- **Nothing in it is anybody's.** Every address, name, subject, and sentence is invented, and the hosts are reserved
  names — `.invalid` and `.example` — so no fixture can reach a machine that exists. That is the same rule
  `frontend/AGENTS.md` states about a capture of a signed-in client, read from the other end.
- **The browser suite is the proof that it is sufficient.** `client.spec.ts` declares no fixture of its own, so a shape
  the corpus states wrongly fails a committed suite rather than one session's scratch file.
- **What proves it is _right_ is outside this stack.** A suite here can only prove that the corpus and the client agree
  with each other, which is what they do while both disagree with the deployment. So
  `scripts/test-agent-workflow.sh` holds every value in this directory against
  `backend/tests/PublicSurfaces.UnitTests/http-api-contract.json`, the byte-for-byte recording of the HTTP surface, and
  fails naming the route, the field, and both spellings where they disagree. It lives there rather than under
  `frontend/` because neither stack's change filter would run it when the other side moved, which
  [Agent workflow](../../docs/operations/agent-workflow.md#which-stack-a-gate-runs) argues. Which answer each value
  states is written in that script rather than here, for the reason the first rule above gives: a fixture naming a route
  would be the routing this corpus refuses to know about.

## A localized screen

- **Assert the words, not a key.** A test reads what a person reads, so it queries the sentence the catalogue carries.
  Writing that sentence out is the clearer form where it is short and English; importing the entry out of `en.ts` or
  `pl.ts` is the clearer form where the point of the test is that the _other_ language reached the screen.
- **A formatted value is asserted by asking `Intl` the same question the screen asked it**, wherever the zone is not
  part of what is being proven. A number, a list, or a plural reads the same everywhere, so an expectation spelled out
  by hand there would be an expectation about the catalogue rather than about the locale reaching the formatter.
- **An instant is the exception, and is asserted by pinning a zone and writing the spelling out.** The rule above cannot
  prove the one thing that matters about a date — that the screen placed it against the _reader's_ day — because a
  formatter the test built the same way passes identically for a screen that named `timeZone: 'UTC'` and for one that
  named nothing, which is the defect. So a test for `localization/instants.ts` or for anything wording an instant sets
  `process.env['TZ']` before it formats, restores it in an `afterEach` of the same file for the reason a fake clock is
  released, and asserts the literal string that zone produces — the same instant read in two zones, so that a screen
  that stopped honouring the runtime's zone fails rather than agreeing with itself. Node re-reads the variable at
  assignment, which is what makes this work at all and why a zone is never passed to `Intl` to simulate one.
- `LocalizationProvider` is mounted above whatever is rendered, the way `main.tsx` mounts it. `useLocalization` throws
  without it rather than falling back to English, so a test that forgets it fails loudly instead of proving a screen
  against an arrangement the application does not use.
- What language a unit test runs in is decided by what it writes to `navigator.languages` and to storage before
  rendering. Both are put back afterwards, for the reason the next section gives about a fake clock.
- **A Polish sentence is never written out in the browser suite.** That suite's files are spell-checked and the Polish
  catalogue is the one file excluded from it, so a copy of its wording there is both a string to keep in step with the
  catalogue and a word for the check to object to. Assert the English sentence being _gone_ and `<html lang>` naming
  the other language instead — which is the stronger assertion anyway, being about the switch rather than about one
  translation.

## Time and randomness

- A component that reads the current time takes it from its caller. That is the first answer and the one to reach for,
  because `TimeProvider` has no React counterpart and an injected value needs no runner feature at all.
- Where it genuinely cannot — a hook that ticks, a relative timestamp that recomputes — the clock is fixed with
  `vi.useFakeTimers()` and `vi.setSystemTime()`, and released with `vi.useRealTimers()` in an `afterEach` of the same
  file. A fake clock left installed changes the next file the worker runs.
- Randomness is the same shape: a drawn value is passed in, or `Math.random` and `crypto.randomUUID` are stubbed with
  `vi.spyOn` and restored. A test never asserts against a value it did not decide.
- **Nothing sleeps.** `await screen.findBy*` waits on the document rather than on a duration, which is what makes the
  suite fast and what keeps it from failing on a loaded machine.

## Isolation

- `frontend/src/Client.App/vitest.setup.ts` unmounts what a test rendered, after every test. Nothing else may rely on
  the document surviving between tests.
- That file also puts both `localStorage` and `sessionStorage` back, and it puts them back for two different reasons.
  Node publishes a Web Storage implementation of its own, and the jsdom window this project runs in is the worker's
  global object — so Node's getters are the ones on it. `localStorage` answers `undefined` unless the process was
  started with `--localstorage-file`, which makes a browser API read as absent; `sessionStorage` answers a store
  belonging to the worker rather than to the document, which is worse, because it is present, it works, and it is
  shared by every file that worker runs. jsdom's own two are there under other names and are reinstated under the
  right ones. **That file also empties both in front of every test**, which is where the emptying belongs rather than
  behind it: each store is one per file and not one per test, and a test that cleared on the way out would be clearing
  too early to be sure. A component writes to storage in an effect, and a read resolving between a file's own teardown
  and the unmount commits once more — so that write lands after the clear and the next test opens holding it. Emptying
  on the way in cannot race anything. A test therefore states what it wants in the store and never has to put it back,
  and a file that still clears on the way out is doing something already done for it.
- The rule above is why an application test supplies a `CredentialStore` rather than writing to storage: what is kept
  is then a map the test holds, and nothing has to be cleared. Storage itself is asserted where it is the subject — the
  store's own test — and the bound the web head's keeping actually has is the browser suite's, a second tab being a
  second document and jsdom having one.
- A file that reassigns a module-level double sets it back to its default in a `beforeEach`, so the order tests run in
  cannot decide what one of them sees.
- Vitest runs files in parallel workers, so a test shares nothing with another file: no fixture written at module
  scope and read across files, no temporary directory, no port, and no environment variable.

## The browser suite

`pnpm test:browser` is the second suite, and it is the answer to everything the first one structurally cannot make.
**`pnpm test` starts no browser and no server**, so a claim that needs real layout or geometry, real navigation, the
back gesture, a real network exchange, or the built bundle rather than the source belongs here. Moving such a check is
the answer; dropping it is not, and neither is asserting it in jsdom where it would pass for the wrong reason.

- **It runs against the built bundle, never the development server.** `pnpm test:browser` runs `pnpm build` and then
  Playwright, whose configuration starts Vite's preview server over `src/Client.App/dist/`. A development server
  transforms modules on demand, so a screen proven against one has not been proven against the directory of static
  files a deployment publishes — which is half of what this suite is for.
- **A check earns its place here by being unanswerable in jsdom.** Rendering a component with a value handed to it, a
  branch, a label, a failure message: all of that is faster and clearer in `pnpm test`, and duplicating it here buys a
  slower copy. What only a browser answers is the bundle, the document's history, layout and geometry, and the requests
  the page actually issued.
- **The same rule about what a test asserts holds**, and this suite has no exemption from it: a role first, then the
  text a person would read. Playwright's `getByRole` is the same query React Testing Library's is. No CSS selector, no
  `data-testid`, no coordinate, and no assertion on a class name.
- **The service is not started, and the credential belongs to nobody.** The preview server serves the bundle and
  answers nothing else, so the routes the client reaches are fulfilled by Playwright's own routing — a browser
  feature rather than a package, and the reason a mocking library is refused here as it is above. What that fakes is
  one side of a real exchange: the request is composed, sent, and read by the built bundle exactly as it would be
  against a deployment, which is what lets this suite assert the header the bundle put on the wire. The password typed
  into it is invented in the spec and reaches a loopback origin, so nothing here is anybody's mail or anybody's
  credential. Driving a real deployment is the agent's own work with `@playwright/cli`, which `frontend/AGENTS.md`
  covers, and it is not this suite.
- **Nothing here retries.** A check that passes on a second attempt has reported that the client is flaky rather than
  that it works.
- **A geometry read after `setViewportSize` waits for the composition it is about.** The width is the browser's the
  moment it is set, and everything a stylesheet lays out from it moves with it — but what a screen _holds_ at a width
  is `useWideWorkspace` and its two neighbours answering a `matchMedia` change, which is a render a task later. A box
  read before that render measures the previous composition laid out at the new width, and two boxes read either side
  of it belong to two different screens, which is what an assertion comparing them reports as a defect in the client.
  So the wait is an element only the new composition draws — the bottom bar's own overflow control, the mailbox column
  that stands beside the list — rather than a duration, and the boxes are read after it. An element present in both
  compositions says nothing about which one drew it and is not that wait.
- **A failure keeps its trace and its screenshot in `frontend/.playwright/`, which Git ignores and nothing uploads.**
  That is a privacy decision rather than a storage one: the moment this suite drives anything but a routed answer, a
  capture shows somebody's mail and a trace carries the header it was read with, and an artifact anybody with the run's
  link can download is the wrong place for either. A pipeline
  failure is therefore read from the job log and reproduced locally — where the trace is, on the machine that produced
  it.
- **Where it runs is decided**: on every pull request that reaches the client stack, in
  `.github/workflows/build-test-frontend.yml`, which carries the argument for that rather than nightly or local-only.
  Neither verification gate runs it, because it needs a browser install the gates would otherwise demand of every
  machine.

What the suite asserts about the network is therefore three things rather than one: that the client reaches the origin
it was served from and no other, that what it sends there is the credential the bundle composed — the second being the
one claim jsdom structurally cannot make, since the encoding runs through the browser's own `TextEncoder` and `btoa`
after the bundler has been over it — and that the sender's own host is the one exception to the first, asserted in both
directions. Nothing is fetched from that host until the reader asks for the pictures of one message, a request does
leave for it once they have, and the ask is gone again after a reload: that is the property ADR 0024 turns on, and a
browser is the only witness to it. Where the credential is kept is here for the same reason as the second: a reload is
a real one, and a second tab is a second document, which is what `sessionStorage` is bounded by and what jsdom has one
of.

Navigation is answered here as well. Each space is reached at a fragment address of its own, so this suite moves
between them, reloads one, and moves back and forward through the client's own history — which is the whole reason the
address is a fragment, and which nothing but a real document with a history can answer. Where the composition changes
is here as well, because it is geometry: the navigation sits beside the workspace in a wide window and under it in a
narrow one, asked of two viewport widths rather than of two heads.

## The end-to-end suite

`frontend/tests/end-to-end/` is the third suite, and the only one that reaches a service. Both suites above answer
every request themselves — one with a transport a test handed over, the other with the browser's own routing — so
between them they establish that the client and the corpus agree with each other, and neither can say a word about
whether either agrees with a deployment. This one drives the same built bundle against a MailFathom that was stood up
the way an operator stands one up, with mail that arrived at a mail server and was synchronized out of it.

- **It runs on request and nowhere else.** `scripts/run-end-to-end-client.sh` is what runs it, `End-to-end client` is
  the manual-dispatch workflow that calls that script, and neither verification gate nor any pull-request check reaches
  it. [Agent workflow](../../docs/operations/agent-workflow.md) and
  [the end-to-end client run](../../docs/operations/end-to-end-client-run.md) hold what the run costs and what it gates,
  which is nothing.
- **It has a configuration of its own**, `frontend/playwright.end-to-end.config.ts`, and the pull-request suite's
  configuration ignores this directory so that it cannot pick it up. Two configurations rather than two projects,
  because the two disagree about everything a configuration decides: this one starts no server, is handed an origin it
  did not choose, and fakes no route at all. Folding them together would put the pull-request suite one mistake away
  from reaching a deployment.
- **Nothing here routes a request, and that is the whole point.** A `page.route` in this directory would make the suite
  a slower copy of the one above. A check earns its place here by needing a service to answer it: that mail
  synchronized out of an IMAP server reaches a list, that a body derived from real MIME is drawn, that the service
  threaded a conversation, and that its own index finds a message somebody searched for.
- **It asserts what the client says rather than what the corpus says.** The mail is generated, so no subject, sender,
  or sentence in it is a value to write down: what is written is the roles and the words the client itself draws, and
  what the deployment must have produced for them to mean anything. A spec restating a subject would be asserting
  against the archive rather than against the service.
- **It keeps its traces and its screenshots**, which is the one place this directory's privacy rule reads the other
  way. Every message a run reads is fabricated, delivered into a container that is destroyed with the run, so a capture
  shows nobody's mailbox and a trace carries a credential that exists for the length of one run. The pull-request suite
  keeps its output on the machine that produced it for the opposite reason, and that rule is unchanged: the moment a
  capture could show real mail it is personal data whatever produced it.

## The desktop suite

`frontend/tests/desktop/` is the fourth suite, and the only one that drives the head this repository ships as something
somebody installs. The three above all run in a browser somebody downloaded: the web head is that browser, so they
answer for it completely — and they answer for the desktop head only as far as the two are the same bundle. What they
cannot reach is the WebView the shell renders in, which is where the two can differ without a line of this client
changing. `pnpm test:desktop` is the whole of how it runs.

- **`tauri-driver` 2.0.6 is the mechanism, and it is Tauri's own.** It speaks the W3C WebDriver protocol, proxies to the
  platform's native driver — `WebKitWebDriver` on Linux, Microsoft Edge Driver on Windows — and launches the shell
  binary the session names. macOS is unreachable because no WKWebView driver exists, which costs nothing: this
  repository builds no macOS head. The protocol client is written in `tests/desktop/head.ts` rather than taken from a
  package, because four requests out of a frozen standard is sixty lines against a pinned dependency, a register row,
  and a re-counted census. The proxy itself is a binary a machine holds rather than a dependency a lock file resolves,
  so its version is written in the workflow step that installs it and in this bullet, and nowhere else;
  `THIRD_PARTY_LICENSES.md` § _Test-only_ carries its row and says why it is in none of the three closures.
- **It drives a shell whose client is served over HTTP rather than out of the bundle**, which is the one place this
  suite is not looking at what ships. The fixture corpus is gated on `import.meta.env.DEV`, so a bundled shell has no
  example mail to answer with and no signed-in screen to reach — and that gate is deliberate. So the binary is built
  with `build.frontendDist` pointed at the corpus development server, which `MAILFATHOM_FRONTEND_URL` in
  `src-tauri/run-tauri.ts` is for. What differs from a published shell is the scheme the document was served from;
  what does not differ is the WebView, which is the thing under test.
- **A case that needs a different environment needs a shell of its own.** Neither the timezone nor the language
  preference is something a WebDriver session can be asked for: a WebView reads both from the process it was started
  in, and `tauri:options` carries no environment. So one driver and one shell are started per case, under a profile
  directory of their own — without which a case asserting what a first run resolves would read back whatever the case
  before it chose, and the suite would pass or fail by the order it happened to run in.
- **What it owns is what the platform answers, and nothing else.** Two rules qualify today and both are
  [#1462](https://github.com/Krzysztof318/MailFathom/issues/1462)'s: that an instant is placed against the zone the
  runtime reports, and that a first run opens in the language the platform states. A check belongs here by being
  unanswerable in the other three — a component, a label, a layout, a request on the wire, or anything about the
  accessibility tree belongs above, and a copy of one here buys a slower answer and a second thing to keep in step.
- **It asserts a literal spelling, never a formatter built the same way.** That is the rule § _A localized screen_
  already states, and it is the whole content of the timezone half: an expectation written as the output of a second
  `Intl.DateTimeFormat` passes for a head that named a zone of its own as happily as for one that did not.
- **A selector here is a CSS one, and that is a protocol limit rather than an exemption.** WebDriver has no locator for
  a role and a name, so the elements this suite reaches are the ids the labels point at and the one control that states
  the resolved language during a render. That the control carries the right accessible name is asserted by the unit
  suite and by the browser suite, which is why it is not asserted twice.
- **Two facts about the Linux head were measured rather than assumed**, and both are recorded in the spec because
  nothing else in this repository would record them. WebKitGTK answers `navigator.languages` out of `LC_ALL` and `LANG`
  and ignores `LANGUAGE`; and it reports exactly one language however many the environment names, so the list-walking
  half of the language rule is a web-head property and only its first step can be asked here.
- **Nothing a run captures is kept.** No screenshot, no trace, and no video — on the rule § _The browser suite_ states
  and for the same reason, except that here there is nothing to weigh: the suite reads text out of a document and a
  failure is diagnosed from the expectation it printed.
- **Where it runs is decided**: on every pull request that reaches the client stack, in the `Drive the desktop head`
  job of `.github/workflows/build-test-frontend.yml`, which carries the argument for gating it there rather than
  nightly. Neither verification gate runs it, for the reason neither runs the browser suite and a stronger one — it
  needs a Rust toolchain, the platform's WebView libraries, a WebDriver, and a display.

## Coverage

**Every `pnpm test` collects it, and nothing is enforced on it.** `frontend/vitest.config.ts` turns the v8 provider on
for both projects, so the figure arrives with the run that already had to happen rather than behind a flag somebody has
to remember — the same rule that makes `pnpm test` the whole of how this suite runs applies to what measures it. A text
summary goes to the terminal, where a verification gate and a CI job both print it, and an HTML report to
`artifacts/coverage/client/` at the repository root, which `.gitignore` covers along with everything else written there.

**What is measured is both packages' `src/`, whether or not a test imported it.** A module nobody covers is the one the
number exists to show, so it sits at zero in the report instead of being absent from it. Three things are left out and
none of them is a gap: a declaration file states types and runs nothing, `main.tsx` mounts React into the document and
decides nothing — the client's counterpart to `Host` and `AppHost`, which the service excludes for the same reason —
and a `<Subject>.harness.tsx` module is the arrangement a family of test files shares, which runs only under a test and
asserts nothing itself. Vitest drops this suite's own test files.

**Nothing gates on the figure, in either verification script or any workflow**, and the value that would be easiest to
add is the one deliberately absent: a threshold. The service enforces 85% and `docs/operations/agent-workflow.md`
§ _The mutation score is read, never enforced_ records what that stopped buying — a number above 95 for months, saying
that a line ran rather than that anything asserted its result, which a test executing a branch and checking nothing
raises exactly as far as one that pins the answer down. A second enforced number over a suite this size would inherit
that before earning anything. So the figure is read the way the integration report and the mutation score are read: as
a place to look, never as a bar to clear. What it is good for is the file sitting at zero and the branch nobody
reaches; what it cannot tell you is whether the tests above it assert anything, and no threshold would fix that.
