# Client Test Instructions

These instructions govern every test in the client stack, in addition to the repository root instructions. They are
reached from the root table rather than from the directory a test sits in, because a client test does not sit here: this
directory holds the contract and no test, for the reason the first section gives.

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
- `frontend/tests/` therefore holds this file alone. It is the contract's address, not the suite's, and a test written
  here would resolve neither package.
- No `include` glob is configured, so what makes a file part of the suite is that name and nothing else. A helper a test
  imports is an ordinary module and carries no `.test.` in its name.

## The runner

- **`pnpm test` is the whole of how the suite runs**, locally and in CI. It is `vitest run` — one pass, non-interactive,
  both packages — and there is no second invocation to drift from it. Both verification gates run it for any change
  reaching the client stack.
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
  component reaches for its own — `App` does today, from `stubMailFathom` — the module supplying it is replaced with
  `vi.mock`, and that is read as the seam being wrong rather than as the pattern to copy.
- **Never `vi.mock` a module of `Client.Backend` from an application test.** The parsing and the failure mapping are
  part of what the screen is being proven against; faking them leaves a test that asserts a screen renders whatever it
  was handed.
- No mock service worker, no request-interception package, and no local HTTP server. A real exchange belongs to the
  browser harness below.

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
- A file that reassigns a module-level double sets it back to its default in a `beforeEach`, so the order tests run in
  cannot decide what one of them sees.
- Vitest runs files in parallel workers, so a test shares nothing with another file: no fixture written at module
  scope and read across files, no temporary directory, no port, and no environment variable.

## What belongs in the browser harness instead

The Playwright harness [#1404](https://github.com/Krzysztof318/MailFathom/issues/1404) establishes is where a check goes
that this suite structurally cannot make. **This suite starts no browser and no server**, so a claim that needs real
layout or geometry, real navigation between screens, a real network exchange, a real credential, or a running
deployment belongs there. Moving such a check is the answer; dropping it is not.

## Coverage

Nothing collects coverage of this suite and no threshold is enforced on it. What that costs, and what a number over
this stack would be worth, is a decision of its own rather than something to settle by adding a collector here.
