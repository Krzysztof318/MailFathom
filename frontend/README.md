# The client workspace

`frontend/` is a [pnpm](https://pnpm.io/) workspace holding the two packages the MailFathom client is split into, and
`src-tauri/` beside them holding the desktop shell that wraps what they build. It shares no build file and no
configuration file with the service under `backend/`; the two meet only over the HTTP API served beneath
`/api/client`, which [the client endpoint](../docs/operations/client-endpoint.md) describes.

```bash
pnpm install --frozen-lockfile   # restore, refusing to rewrite pnpm-lock.yaml
pnpm dev                         # the development server
pnpm build                       # the static bundle, into src/Client.App/dist/
pnpm desktop:dev                 # the desktop shell around that server, rebuilt as the shell changes
pnpm desktop:build               # the desktop application and its installers
pnpm typecheck                   # both packages and eslint.config.ts, under the strict set below
pnpm lint                        # every rule an error, no warning tolerated
pnpm test                        # both packages' suites, once, non-interactively
pnpm test:browser                # build the bundle and drive it in a real browser
pnpm format                      # rewrite; pnpm format:check reports instead
```

The two `desktop:` commands need a Rust toolchain and the platform's WebView development packages; none of the others
does. [Local development](../docs/operations/local-development.md) has them and names the failure a missing one
produces.

`packageManager` in `package.json` names the pnpm version this lock file was written by, and `engines` the Node
version the toolchain is run under. Corepack no longer ships with Node, so `pnpm` comes from a global install and that
field is what says which version to install. `.npmrc` declares the registry those packages come from, rather than
leaving it to whatever a machine configured. [Local development](../docs/operations/local-development.md) has the
prerequisites and how the verification gates run all of this.

`pnpm dev` is also what the Aspire app host starts as its `mailfathom-client` resource, so one command brings up the
database, the service, and this development server already pointed at the client surface — [the client
resource](../docs/operations/local-development.md#the-client-resource) is that arrangement, including the environment
variable it hands over and how to leave the client out of a run.

## Two packages, and the resolver is what separates them

- **`src/Client.Backend/`** is everything that reaches the service: the request and response types, the session, the
  failure model, and the operations composed from them. It declares **no React and no DOM-typed dependency**.
- **`src/Client.App/`** is the application: screens, components, state, styling. It depends on `Client.Backend`, and
  nothing depends on it.

The boundary is the dependency graph rather than a convention, so crossing it fails a build instead of waiting for a
reviewer. Three mechanisms hold it, and each can be reproduced by writing the offending line and running the command
beside it:

| Write this in `Client.Backend`     | What refuses it                                                                                                              |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `import { useState } from 'react'` | `pnpm typecheck` — the package's manifest declares no React, so the module does not resolve                                  |
| `document.title`                   | `pnpm typecheck` — its `tsconfig.json` names `lib: ["ES2023"]` and `types: []`, so no browser global is declared             |
| either of the above                | `pnpm lint` — `no-restricted-imports` names the boundary rather than leaving a resolution error to read as a missing install |

`Client.Backend` therefore names no HTTP API of its own. It publishes a `MailFathomTransport` — a function from a
request to a response — and `Client.App` supplies the adapter that calls one. That is the boundary's consequence
rather than an abstraction kept in case a second transport appears.

## The desktop shell, and why it is not a third package

`src-tauri/` is a Rust crate rather than a workspace package, and it sits beside `src/` rather than inside it because
it is not application source: `frontend/src/` is TypeScript throughout and holds what both heads render, while the
shell holds what only one of them has. It owns the window, the application identity, and the installers — nothing
else. A screen is written once and reaches the web head and the desktop head unchanged, which is the property ADR 0021
chose Tauri for; a platform difference that genuinely exists belongs here or in one stated rule in `styles.css`, and
never in a component.

`src/main.rs` is the whole of it. There is no library target beside it, no command registered, and no capability file:
the application calls into Rust nowhere, so granting the webview a permission would be granting reach nothing asked
for. The first command to exist is what adds both.

| What                                                                | Where it is decided                                                                                                      |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| Product name, application identifier, window title and minimum size | `src-tauri/tauri.conf.json`                                                                                              |
| The version the application reports                                 | `<VersionPrefix>` in `Version.props`, merged in by `src-tauri/run-tauri.ts`                                              |
| Which crates the shell links, and at which versions                 | `src-tauri/Cargo.toml`, resolved into the committed `Cargo.lock`                                                         |
| The icon every bundle carries                                       | `src-tauri/icons/`, generated from `assets/icon-1254.png` with `pnpm exec tauri icon`                                    |
| The terms every bundle installs beside the application              | `bundle.resources` in `src-tauri/tauri.conf.json`, which takes the repository's own `LICENSE` and `NOTICE`               |
| What a Linux package declares it needs                              | `bundle.linux.deb.depends` in `src-tauri/tauri.conf.json`; the `rpm` names none, per `docs/operations/desktop-client.md` |

**The version is never typed into a manifest.** `tauri.conf.json` carries no `version` and neither does `Cargo.toml`;
`run-tauri.ts` reads `scripts/read-declared-version.sh` and hands the answer to the Tauri CLI as a configuration
patch, which is how the image gets its tag and the chart its `appVersion`. So `pnpm desktop:build` stamps the same
number the service reports, and `cargo build` run on its own produces `0.0.0` rather than a number that looks right
and is not. A publication is the one caller that hands the number in instead, through `MAILFATHOM_VERSION`: a release
and a nightly resolve the commit and the version once, and a nightly's identifier is not in `Version.props` for a
build to find.

**The bundle formats are chosen rather than defaulted.** Tauri's own default is every format the host can produce, so
`bundle.targets` names three instead: `deb` and `rpm`, because a native package is what a Linux user installs and
removes through their own package manager and those are the two families that covers; and `nsis` on Windows, because
it installs per user without administrator rights and one Windows installer is enough. An `msi` is deliberately
absent, and so is an `appimage` — that format packages the host's own GTK and WebKitGTK shared objects into the
artifact, several of them LGPL-2.1-or-later, which `THIRD_PARTY_LICENSES.md` decides against redistributing where the
two native packages depend on the distribution's copies and carry none of it. macOS is not built at all, which is why
`icons/` carries no `.icns`.

**The development port is reserved rather than fixed.** `pnpm desktop:dev` starts the Vite development server and
loads it in the shell, so a change to a screen reloads in the desktop window exactly as it does in a browser tab —
and `run-tauri.ts` asks the operating system for a free port before either half starts, hands it to Vite and to
`devUrl` together, and `tauri.conf.json` therefore names no address. Vite's default port would be wrong here on any
machine running two of these at once: the second server moves to the next free port while the shell that started it
goes on loading the first, and the window then renders the other run's client rather than failing. `pnpm dev` on its
own is unaffected and keeps both the default port and the freedom to move.

## TypeScript only, at the strictest setting

Every source file under `frontend/src/` is `.ts` or `.tsx`. A `.js` or `.jsx` file there fails `pnpm lint` on a rule
written for exactly that, so the convention is enforced rather than remembered.

`tsconfig.base.json` is what both packages compile under. It goes past `strict`: an unchecked index access, an
inexact optional property, an unchecked `override`, an unused local or parameter, a switch fallthrough, a missing
return, and a property read off an index signature are each errors. That file carries every relaxation from the
maximum as a named entry with its reason, and there is one.

A lint violation is a build failure. `pnpm lint` runs with `--max-warnings 0`, so a rule the plugins ship as a warning
still fails — which is what `TreatWarningsAsErrors` and the analyzer set are to the service half of this repository.

## Two languages, and no library for them

`Client.App` is localized in English and Polish. English is the default and the fallback. A first run with no choice
stored reads what the browser or the operating system says the person prefers, narrowed to a language a catalogue was
written for; the control in the header overrides that, the choice survives a restart of either head, and changing it
rewrites the screen without anything restarting.

The mechanism is `src/Client.App/src/localization/` and it depends on nothing. `Intl` — which every engine both heads
render in already carries — formats dates, numbers, relative times, lists, and plural categories, so what was left to
own is a catalogue, a lookup, and a `{name}` hole to fill. That is less than the configuration an internationalization
library is adopted with, and it adds nothing to the bundle and nothing to `THIRD_PARTY_LICENSES.md`.

- `en.ts` declares the keys and `pl.ts` is annotated with the type it exports, so a key one language carries and the
  other does not fails `pnpm typecheck` rather than reaching a screen. The unit suite asserts the same parity at run
  time.
- `locale.ts` resolves which language a run opens in and stores an explicit choice; `Localization.tsx` holds the
  provider and `useLocalization.ts` the hook a screen reads through. Nothing else in `Client.App` reads either
  directly.

**A user-visible string written into a component fails `pnpm lint`**, on `no-restricted-syntax` selectors in
`eslint.config.ts`. Each can be reproduced by writing the offending line and running that command:

| Write this in a `Client.App` component | What it reports                                                                                                  |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `<p>Reading accounts…</p>`             | a string in markup belongs in `en.ts`, with its Polish counterpart, and reaches the screen through `translate()` |
| `<p>{'Reading accounts…'}</p>`         | the same, for a string used as a child rather than written as text                                               |
| ``<p>{`Reading ${what}…`}</p>``        | a sentence assembled in markup cannot be reordered by a translator; it is one entry with a hole                  |
| `<img alt="Accounts" />`               | an attribute read out to somebody is a user-visible string                                                       |
| `<img alt={'Accounts'} />`             | the same, in the braced form — each attribute is matched bare, braced, and as a template literal                 |

`.config/typos.toml` excludes `pl.ts` from the spell check, because a dictionary of English has nothing true to say
about Polish. Every other file in this workspace stays checked, `en.ts` included.

## The two suites

`pnpm test` is the unit suite, and `vitest.config.ts` declares one Vitest project per package because the two are tested
differently: `Client.Backend` is ordinary logic run without a DOM, and `Client.App` is components rendered into jsdom
with React Testing Library. A test file sits beside the source it covers — the package boundary above is the reason.
That one command also collects the suite's coverage, over both packages' `src/` whether or not a test imported the
file, and prints a summary beside the results; the HTML report goes to `artifacts/coverage/client/` at the repository
root. No threshold is enforced on the figure.

`pnpm test:browser` is the other one. It runs `pnpm build`, serves `src/Client.App/dist/` with Vite's preview server,
and drives it with Playwright, so what it proves is the bundle a deployment publishes rather than the source: the
application loading, the version the build stamped, the screen rendering through roles and accessible names, each space
reloading at its own address and the back gesture moving through the client's own history, which composition a width
produces, and the requests the page actually issued. It needs a browser of its own —
`pnpm exec playwright install chromium` — which is why neither verification gate runs it and the pipeline does, on every
pull request that reaches this stack. Its configuration is `playwright.config.ts` and its specs are under `tests/`.

[`tests/AGENTS.md`](tests/AGENTS.md) is where both suites' policy is decided, including which check belongs to which.

## Whitespace is decided in `.editorconfig`

The repository's one `.editorconfig` at the root holds indentation, line width, line endings, and quote style for the
client's file types. Prettier reads it: its CLI respects `.editorconfig` by default, so nothing here restates any of
those values in a second file that would drift. Prettier's own configuration here is `.prettierignore` and nothing
more.

## Three spaces, one frame, and no router

The client is **Discover**, **Mail**, and **Cases**, and they are one application rather than three: `src/App.tsx` is
the frame that holds them, and it is what a person carries their question, their scope, and their selection across.
The frame is one tree laid out two ways by the width it is given — a navigation rail beside the workspace at or above
the `workspace` breakpoint, bottom navigation under a stack of screens below it — and nothing in it reads which head or
which platform it is running on.

Each space is reached at a **fragment address** of its own: `#/discover`, `#/mail`, `#/cases`. `src/routing/` is the
whole of it, and it is deliberately not a package — three addresses with no segment, no parameter, and no nested tree
are what `location.hash` and `hashchange` already are, and the browser keeps the history for us. A fragment rather than
a path because a path would have to be reloadable, and the service serves the bundle with no fallback mapping an
unmatched path onto the entry document; a fragment never reaches a server, so every address reloads on both heads with
nothing configured.

`src/workspace/` is what survives moving between spaces, and it is mounted above the frame for exactly that reason.

## Styling, and the two themes

Tailwind is wired CSS-first through `@tailwindcss/vite`. The palette, the type scale, the breakpoint the composition
changes at, the safe-area insets, and the motion defaults are `@theme` tokens in `src/Client.App/src/styles.css`, and
there is no JavaScript configuration file.

The colours come in two layers. `--color-fathom-*` is MailFathom's own ramp, sampled from the product icon; everything
a screen actually composes against is a **semantic** name set from it — a panel, a rail, a sunken region, two line
weights, four text weights, an accent, a healthy state, a warning. Both themes declare the same names, which is what
lets the light and the dark client be one set of utilities rather than a `dark:` variant on every one of them.

`src/theme/` decides which of the two is painted, from the person's choice of light, dark, or following the machine.
It writes one `data-theme` attribute on the document before the first paint, so nothing on a screen ever asks which
theme is in force — the same rule that keeps a screen from asking which language it is in.

## What the build produces

`pnpm build` writes `src/Client.App/dist/` — a directory of static files and nothing else. No Node process joins any
deployment shape: the container image builds this in a stage of its own and copies the result beneath its web root, so
what a deployment gains is files and a setting rather than a second service.

`src/Client.App/public/` is copied into that directory verbatim, and it holds one file:
`THIRD-PARTY-NOTICES.txt`, the MIT notice of the three packages the bundle actually redistributes. The build minifies
every module into one chunk and none of the three carries a banner of its own, so the notice travels as text beside
the code rather than inside it. [The third-party register](../THIRD_PARTY_LICENSES.md) is where the review behind it
lives, and it is what says which packages that file has to name.

The version the client displays comes from `<VersionPrefix>` in `Version.props`, read at build time through
`scripts/read-declared-version.sh` and substituted into the bundle. No version number is written into a manifest or
into source.
