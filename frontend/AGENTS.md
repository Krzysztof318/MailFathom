# Client Stack Development Instructions

These instructions apply under `frontend/` in addition to the repository root instructions. They hold across
`frontend/src/` and `frontend/tests/` alike, which is why they sit here rather than in either of those: a rule stated in
one of them would be silently absent from the other.

`frontend/src/AGENTS.md` adds what governs the application source, and `frontend/tests/AGENTS.md` what governs the two
suites — a directory holding that contract and the browser suite, because a unit test sits beside the source it covers
and only the browser suite belongs to neither package.
Nothing here is restated in either, and nothing here restates [`frontend/README.md`](README.md), which is the workspace's page: the commands, the
package boundary and the three mechanisms that hold it, the strict compiler settings, the styling, and what the build
produces. [ADR 0021](../docs/decisions/0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) is the decision
all of it implements, and it is required reading before a change to the stack itself.

## What the toolchain already decides

Three files hold every rule a tool can check, and none of them is repeated in prose here or in any file below:
`tsconfig.base.json` for the compiler, `eslint.config.ts` for the lint set, and `.editorconfig` for whitespace and quote
style, which Prettier reads rather than restating. A rule that would fail `pnpm lint` or `pnpm typecheck` is already
enforced, so writing it down again gains nothing and rots separately from the setting it describes.

What is left is the rule those tools cannot state: **what a person does when one of them blocks them.** The answer is
almost always to change the code, because both configurations are argued rather than default, and a suppression is a
claim that this call site is the exception the argument did not cover.

- **Never relax the configuration to pass.** Editing `tsconfig.base.json`, adding a rule override to `eslint.config.ts`,
  or lowering `--max-warnings` moves one file's problem onto every file, including the ones nobody has written yet.
  Loosening either is a decision of its own, taken with an issue, and `tsconfig.base.json` already carries the one
  relaxation it holds with its reason beside it — that is the form a second would have to take.
- **A suppression is arguable when the checker is provably wrong about this line**, and then it is written as narrowly as
  the tool allows and carries the reason on the line above it: `// eslint-disable-next-line <rule>` naming the rule, or
  `@ts-expect-error` with the sentence saying what the compiler cannot see. `@ts-expect-error` rather than `@ts-ignore`,
  always, because it fails once the problem it describes is gone and so cannot outlive its reason.
- **A suppression is never acceptable when it hides a fact rather than a false positive.** `any`, a cast asserting
  something the code has not checked, `!` on a value the type says may be absent, a file-level `/* eslint-disable */`,
  and a bare `eslint-disable` naming no rule are each refused. `unknown` with a check is the alternative to `any`, a
  type guard is the alternative to an unchecked cast, and handling absence is the alternative to `!` — which is what
  `src/Client.Backend/src/mailAccounts.ts` does with a response body no type describes, casting only on the far side of
  the check that earned it.

## Dependencies, the lock file, and where a package may come from

The root instructions require every dependency to be pinned centrally in the stack that owns it. Here that is each
package's own `package.json` and `frontend/pnpm-lock.yaml` together, which are one decision recorded in two places
exactly as `Directory.Packages.props` and the `packages.lock.json` files are for the service.

- Pin an exact version. A range is a floating pin whatever the lock file says, because the next regeneration may move
  under it.
- Regenerate the lock file in the change that moves a pin, by running pnpm rather than by editing the file. Both
  verification gates install with `--frozen-lockfile`, which fails on a manifest and a lock file that disagree rather
  than quietly resolving something new.
- `.npmrc` declares the registry, for the reason `NuGet.config` clears its inherited source list. Adding a second source
  is a supply-chain decision with a licence review, never a line added to make an install work.
- A new package needs an entry in `THIRD_PARTY_LICENSES.md`, per artifact, under
  [ADR 0016](../docs/decisions/0016-third-party-licence-obligations-per-artifact.md). Name it by its **package
  identifier** — `react-dom`, not _ReactDOM_ — because that is what a manifest, a registry, and the survey all read.
  `scripts/update-dependencies.sh --only npm` reads this family and `--only crates` the one below, so noticing a pin is
  behind is a sentence in the report and an issue of its own, never a line in an unrelated diff.
- **A family of files committed into the client's own tree is registered like a package**, and for a stronger reason
  than one: a typeface or a set of icon outlines under `Client.App/src/assets/` is redistributed verbatim inside the
  image and inside the desktop application, which is what puts a notice obligation on a release rather than on a file
  here. It carries no pin a manifest could hold, so the row names what a reader would otherwise have no way to check —
  the upstream, the exact revision or version the files were taken at, and the paths they were written to. That is
  where the client's typeface and its symbols are recorded, and it is the other half of the rule in
  `frontend/src/AGENTS.md` § _UI_ that keeps them out of an external origin.
- A pin that moves costs the register a second thing the service's does not. Every client closure is recorded there as
  a census as well as a row — § _The client's three dependency closures_ — and nothing recomputes one, so re-run that
  section's enumeration command for the closure the pin belongs to, in the same change, and write what it printed.
- An npm pin costs a third thing, and it is the one that ships. `src/Client.App/public/THIRD-PARTY-NOTICES.txt` is the
  notice the bundle carries: `pnpm build` copies it verbatim into the output every published image and every desktop
  package redistributes, and it names each redistributed package and its version. So a moved npm pin is read against
  that file too, and a package that newly reaches the bundle is added to it. `pnpm licenses list --prod` from this
  directory says which packages those are. The crate pins below owe nothing here — they reach no bundle.
- `package.json`'s `version` field is inert, and so is `Cargo.toml`'s, which the desktop shell therefore omits.
  `<VersionPrefix>` in `Version.props` is the only application version number in this repository:
  `Client.App/vite.config.ts` is how it reaches the bundle, and `src-tauri/run-tauri.ts` is how it reaches the desktop
  application, as a configuration patch the Tauri CLI merges rather than as a number committed anywhere.
- The desktop shell's crate closure is pinned the same way, in `src-tauri/Cargo.toml` and the `Cargo.lock` committed
  beside it. Cargo reads a bare `"2"` as a caret range, so a Tauri pin is written `"=2.11.5"` to be exact, and
  `src-tauri/run-tauri.ts` hands Cargo `--locked` for the reason `--frozen-lockfile` is passed to pnpm — so a manifest
  that has moved away from the lock file stops `pnpm desktop:dev` and `pnpm desktop:build` rather than being resolved
  into a rewritten one. Nothing else holds that: a `cargo` command run by hand updates the lock file as Cargo always
  does, and neither verification gate reaches the crate graph at all. `scripts/update-dependencies.sh --only crates`
  reads every pin in that manifest against crates.io and against the terms the register recorded for it.

Four things the client is often assumed to need are absent, and each stays absent until a change argues for it: a
component library, which ADR 0021 excluded deliberately; a router; a state container; and a data-fetching library.
Adopting one is a decision with its own issue, its own licence review, and its own reasoning about what it buys — never
a side effect of writing the screen that wanted one thing it does.

The router is the one of the four whose absence has already been argued rather than merely inherited: the client
reaches each space at a fragment address and reads it back through `hashchange`, which
[the workspace's page](README.md) describes. So a change proposing one is answering that argument, not filling a gap.

## The Android head

`src-tauri/gen/android/` is a generated Gradle project that is nevertheless **committed and reviewed like source**, and
[the workspace's page](README.md) holds what is decided in it and why it is tracked when nothing else under `gen/` is.
What belongs here is the rule that follows from that, because no tool enforces it:

- **Edit the committed project rather than a regeneration of it.** `tauri android init` writes no file that already
  exists, so it can neither undo an edit nor re-apply one. A change to the manifest, the permissions, the backup rules,
  or the Gradle build is an edit to the tracked file, in a change that says what moved and why — never a command run by
  hand whose output the next person has to reproduce.
- **`tauri icon` writes more icons than this repository keeps.** It fills the Android project's `mipmap-*` from
  `assets/icon-1254.png`, which is what the launcher shows and is why it is run at all here — and beside that it writes
  a macOS `.icns`, an iOS set, and the Windows Store logos, none of which any bundle this repository builds carries.
  Delete those again before staging, so `src-tauri/icons/` keeps naming the five files three bundle formats actually
  read rather than every size the tool can produce. **Its adaptive-icon foregrounds are wrong as written**, and that is
  the second thing to do afterwards: `mipmap-*/ic_launcher_foreground.png` comes out full-bleed, while Android composes
  an adaptive icon from a 108dp layer of which only the centre 72dp survives the mask — so the artwork is inset to two
  thirds of each canvas with a transparent surround, or every launcher above API 26 crops the mark. `ic_launcher.png`
  and `ic_launcher_round.png` beside them stay full-bleed, which is what the legacy launcher wants.
- **A moved Tauri pin costs a regeneration.** The template ships with the CLI, so a project generated against an older
  one keeps whatever that version wrote. Move the pin, delete `src-tauri/gen/android/`, run `pnpm android:init`, put the
  hand-written decisions back, and read the diff — that diff is the upgrade, and a pin moved without it leaves the head
  building against a template nobody looked at. `$update-dependencies` is where a pin moves at all. A regeneration also moves what
  the APK carries, so re-run the Android enumeration in `THIRD_PARTY_LICENSES.md` § _How the inventory is produced_ and write what
  it printed into that closure's row: nothing recomputes a census, exactly as with the two above.
- **The head is exempt from no rule and gated by nothing at all.** No verification gate, no pull request check, and —
  until [#1615](https://github.com/Krzysztof318/MailFathom/issues/1615) lands the nightly job ADR 0027 describes — no
  workflow builds it. A change that breaks the Android toolchain alone therefore merges green with nothing to notice
  it, and running `pnpm android:build` by hand is what a change reaching this crate owes instead.
- **Nothing Android's belongs above the shell.** The credential store, the system notification, and the back gesture are
  shell operations resolved at the composition root, and `frontend/src/AGENTS.md` § _The two heads_ is the rule that
  keeps a component from asking which head it is on. A third head does not widen that permission.

## Driving the running client in a real browser

A screen is not proven by compiling. The client is served by `pnpm dev` and driven with
[`@playwright/cli`](https://www.npmjs.com/package/@playwright/cli), which is the tool for the question _what did the
client send, what came back, and what did the screen do about it_: it opens a page, signs in, fills and clicks, and then
reports the requests the page issued, one request or response body, and what the application logged. Use it to diagnose a
defect and to check your own work before claiming a screen behaves, rather than reasoning about what the code should
have done.

It is pinned here like any other dependency, so it is reached as `pnpm exec playwright-cli` from `frontend/` rather than
from whatever a machine happens to have installed globally, and a browser for it comes from
`pnpm exec playwright install chromium`. It is a **different package from `@playwright/test`** and the two are not
interchangeable: this one holds a browser open across invocations for a person or an agent to drive, and that one runs
the committed suite below. Both are pinned, both are Apache-2.0, and `THIRD_PARTY_LICENSES.md` records them.

Five things a first session gets wrong, and each of them wastes a session rather than failing loudly:

- **Name a browser.** `open` defaults to a Chrome channel and looks for an installation this repository never asked for.
  Pass `--browser chromium`, which is what `playwright install chromium` provided.
- **Name a session.** Several agent sessions run on one machine, and an unnamed session is one shared browser between
  them. Pass `-s=<session>` on every invocation, `list` shows what is open, and `close` ends your own — never somebody
  else's.
- **Start it from your own scratch directory, never from a worktree.** It writes snapshots, console logs, and traces
  into a `.playwright-cli/` directory in the working directory it was started in, and it keeps that directory for the
  life of the session. `.gitignore` covers the name so a stray run cannot leave untracked files for the full gate to
  refuse, but the reason for the rule is the next paragraph rather than the gate.
- **Element refs come from `snapshot`.** The targets `click`, `fill`, and `press` take are the refs its own
  accessibility snapshot returned. A selector invented from reading the source is the single most common way to spend
  an afternoon on a locator that never matched anything.
- **Read its own skill once.** The package ships an agent skill describing every command it has, under
  `playwright-core/lib/tools/skills/playwright-cli/SKILL.md` in the installed package. It is the current reference and
  it is shorter than guessing.

It is a development tool and not a suite. What it establishes is turned into an assertion a suite runs — `pnpm test`
where jsdom can answer it, and `pnpm test:browser` where only a browser can — because a run in somebody's session proves
nothing on the next pull request. `frontend/tests/AGENTS.md` is where that boundary is drawn.

**A capture of a signed-in client is personal data.** A screenshot, a snapshot, a trace, a video, a console log, and a
response body from a real deployment each show somebody's mail. They stay in the session's scratch directory and reach
no issue, no pull request, no commit, and no external service; what travels is a description of what was observed. That
is why the directory a session starts the CLI from is part of the rule rather than a detail of it, and it is why the
committed suite retains its own failures in `frontend/.playwright/` and uploads none of them. The same holds for a
fixture: nothing standing in for mail in this tree comes from a real mailbox.
