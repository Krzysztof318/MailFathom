# Client Test Instructions

These instructions govern every test in the client stack, in addition to the repository root instructions. They are
reached from the root table rather than from the directory a test sits in, because most of the client's tests do not sit
here: a unit test sits beside the source it covers, and this directory holds the contract beside the one suite that
belongs to neither package. The first section says why the two are placed differently.

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
- `frontend/tests/` therefore holds this file and the browser suite beside it, and nothing else. A unit test written
  here would resolve neither package, which is the whole of the argument above; the browser suite is what can live here
  precisely because it imports neither — it drives a built bundle over HTTP rather than importing a module out of one.
- **The two suites are told apart by the name.** A unit test is `*.test.ts` or `*.test.tsx` beside its source; a browser
  spec is `*.spec.ts` under this directory. Each runner's default finds its own and neither finds the other's, so a file
  named for the wrong one silently joins the wrong suite — and a browser spec run by Vitest would fail on an import
  Playwright supplies.
- Neither runner is given an `include` glob, so what makes a file part of a suite is its name and nothing else. A helper
  either suite imports is an ordinary module and carries neither marker in its name.

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
- Prefer a component that takes its transport, or the value already read through one, from its caller. Where a
  component reaches for its own — `App` does today for mail, from `stubMailFathom` — the module supplying it is replaced with
  `vi.mock`, and that is read as the seam being wrong rather than as the pattern to copy.
- **Never `vi.mock` a module of `Client.Backend` from an application test.** The parsing and the failure mapping are
  part of what the screen is being proven against; faking them leaves a test that asserts a screen renders whatever it
  was handed.
- No mock service worker, no request-interception package, and no local HTTP server. A real exchange belongs to the
  browser suite below.

## A localized screen

- **Assert the words, not a key.** A test reads what a person reads, so it queries the sentence the catalogue carries.
  Writing that sentence out is the clearer form where it is short and English; importing the entry out of `en.ts` or
  `pl.ts` is the clearer form where the point of the test is that the _other_ language reached the screen.
- **A formatted value is asserted by asking `Intl` the same question the screen asked it.** The machine's own time zone
  decides what a date reads as, so an expectation spelled out by hand would be an expectation about the machine rather
  than about the locale reaching the formatter — and it would fail on a runner in another zone.
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
- That file also puts `localStorage` back. Node publishes a Web Storage implementation of its own, and the jsdom window
  this project runs in is the worker's global object — so Node's getter is the one on it and it answers `undefined`
  unless the process was started with `--localstorage-file`, which makes a browser API read as absent. jsdom's own
  storage is there under another name and is reinstated under the right one. A test that writes to it clears it
  afterwards: the store is one per file, not one per test.
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
- **The service is not started and no credential is used.** The bundle carries `stubMailFathom`, so the mail the client
  reads is a canned body and the suite proves the screen rather than a deployment. What is not stubbed is reaching a
  deployment at all, and the preview server is the reason that costs nothing here: it serves the bundle from a loopback
  origin, which the client adopts as its deployment without asking anybody, so the connect screen never opens and
  nothing is sent. Driving a real deployment is the agent's own work with `@playwright/cli`, which
  `frontend/AGENTS.md` covers, and it is not this suite.
- **Nothing here retries.** A check that passes on a second attempt has reported that the client is flaky rather than
  that it works.
- **A failure keeps its trace and its screenshot in `frontend/.playwright/`, which Git ignores and nothing uploads.**
  That is a privacy decision rather than a storage one: the moment this suite drives anything but the stub, a capture
  shows somebody's mail, and an artifact anybody with the run's link can download is the wrong place for it. A pipeline
  failure is therefore read from the job log and reproduced locally — where the trace is, on the machine that produced
  it.
- **Where it runs is decided**: on every pull request that reaches the client stack, in
  `.github/workflows/build-test-frontend.yml`, which carries the argument for that rather than nightly or local-only.
  Neither verification gate runs it, because it needs a browser install the gates would otherwise demand of every
  machine.

Two things it does not cover yet, because the client does not have them. There is no router, so the only navigation
there is to check is the browser's own — leaving the client and coming back to it — and an in-application back gesture
is asserted here on the day one exists. And nothing goes over the wire to a service, because the mail is stubbed inside
the bundle and the origin serving it is the deployment it is pointed at, so what is asserted about the network today is
that the client reaches its own origin and no other.

## Coverage

**Every `pnpm test` collects it, and nothing is enforced on it.** `frontend/vitest.config.ts` turns the v8 provider on
for both projects, so the figure arrives with the run that already had to happen rather than behind a flag somebody has
to remember — the same rule that makes `pnpm test` the whole of how this suite runs applies to what measures it. A text
summary goes to the terminal, where a verification gate and a CI job both print it, and an HTML report to
`artifacts/coverage/client/` at the repository root, which `.gitignore` covers along with everything else written there.

**What is measured is both packages' `src/`, whether or not a test imported it.** A module nobody covers is the one the
number exists to show, so it sits at zero in the report instead of being absent from it. Two things are left out and
neither is a gap: a declaration file states types and runs nothing, and `main.tsx` mounts React into the document and
decides nothing — the client's counterpart to `Host` and `AppHost`, which the service excludes for the same reason.
Vitest drops this suite's own test files.

**Nothing gates on the figure, in either verification script or any workflow**, and the value that would be easiest to
add is the one deliberately absent: a threshold. The service enforces 85% and `docs/operations/agent-workflow.md`
§ _The mutation score is read, never enforced_ records what that stopped buying — a number above 95 for months, saying
that a line ran rather than that anything asserted its result, which a test executing a branch and checking nothing
raises exactly as far as one that pins the answer down. A second enforced number over a suite this size would inherit
that before earning anything. So the figure is read the way the integration report and the mutation score are read: as
a place to look, never as a bar to clear. What it is good for is the file sitting at zero and the branch nobody
reaches; what it cannot tell you is whether the tests above it assert anything, and no threshold would fix that.
