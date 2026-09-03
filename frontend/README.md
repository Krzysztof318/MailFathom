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

`src/main.rs` is the whole of it, and there is no library target beside it. What it holds beyond the window is four
commands of this repository's own and one upstream plugin.

The commands exist for one reason: [ADR 0023](../docs/decisions/0023-where-the-client-keeps-the-credential-it-signs-in-with.md)
keeps the credential this head signs in with in the operating system's own store, which a webview cannot reach and a
shell can. `keychain_reachable` says whether there is a store to keep one in, and `keep_credential`, `read_credential`,
and `forget_credential` are the three operations on it. Two of them answer whether they succeeded, which is what lets
the client say that a store refused to delete rather than report a sign-out that did not happen; none of them answers
_why_ one failed, because everything a failure could name is about a password. No capability file names them, and
writing one would be a mistake rather than an omission: Tauri gates its own plugin commands through an access-control
list and never an application's, so a `capabilities/` entry naming these four would grant the webview reach into
plugins nothing here pins. What the webview reaches them through is `app.withGlobalTauri` in `tauri.conf.json`, which
puts `invoke` on `window.__TAURI__`, and that is what lets this shell pin no JavaScript binding of its own: the four
commands are this repository's, so nothing upstream publishes a package for them and writing one would be a package of
ours to keep in step with them.

The plugin is `tauri-plugin-opener`, and it is there because opening a followed link outside the application is the one
thing the desktop head cannot do from the page. Its commands are a plugin's rather than this repository's, so they are
gated where the four above are not: `capabilities/open-a-link.json` grants the webview that one operation over `http`,
`https`, and `mailto` rather than the plugin's own default permission set — a capability is reach handed to whatever
runs in the page, so it names the operation the application actually makes and nothing beside it. A second permission
is added the same way, by the change that needs it.

| What                                                                | Where it is decided                                                                                                      |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| Product name, application identifier, window title and minimum size | `src-tauri/tauri.conf.json`                                                                                              |
| The version the application reports                                 | `<VersionPrefix>` in `Version.props`, merged in by `src-tauri/run-tauri.ts`                                              |
| Which crates the shell links, and at which versions                 | `src-tauri/Cargo.toml`, resolved into the committed `Cargo.lock`                                                         |
| Whether the webview may call the shell's own commands               | `app.withGlobalTauri` in `src-tauri/tauri.conf.json`, which is what puts `invoke` on `window.__TAURI__`                  |
| What the webview may load, connect to, and submit to                | `app.security.csp` in `src-tauri/tauri.conf.json`, which Tauri serves as the document's own Content-Security-Policy      |
| What the webview may ask a registered plugin for                    | `src-tauri/capabilities/`, one file per capability                                                                       |
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
written for; the control in the account menu at the foot of the navigation overrides that, the choice survives a
restart of either head, and changing it rewrites the screen without anything restarting.

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

## What the device holds, and what follows the person

Three things the client keeps belong to the machine somebody is reading on rather than to the person: the theme, the
language, and how wide the message list is drawn beside the reading pane. `src/Client.App/src/device/deviceStore.ts` is
the one module they go through — reading a value, writing one, and removing one, under the names it declares, so no
screen spells a storage key and the handling that a browser or a WebView refusing storage needs is written once.

Which implementation answers follows what the system offers rather than which system it is. The web head and the
desktop head on either Linux or Windows reach the same origin storage through the WebView they render in, so all three
resolve to it; a system that refuses it falls back to a store that lasts the run, so the client still mounts and a
value then lasts the session instead of outliving it. That is the seam a head diverging later is added behind.

The width of the message list is the one of the three kept **per signed-in person**, so two people sharing a machine do
not inherit each other's split, and the key it is written under folds the name to a digest rather than spelling it out.
What says how somebody wants to work — rather than how much room this screen has — follows them between machines and is
the deployment's to hold instead.

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

## Seven spaces, one frame, and no router

The client is **Discover**, **Mail**, **Cases**, **Agent**, **Tasks**, **Calendar**, and **People**, and they are one
application rather than seven: `src/App.tsx` is the frame that holds them, and it is what a person carries their
question, their scope, and their selection across. Only **Mail** is built — `src/routing/spaces.ts` names that in
`implementedSpaces` — and the other six are present, named as placeholders, and say in a sentence that there is nothing
behind them yet. They are drawn rather than hidden because the design project is what the client is measured against
and it shows all seven; a rail with three destinations would be a different product from the one that was designed.
The frame is one tree laid out two ways by the width it is given — a navigation rail beside the workspace at or above
the `workspace` breakpoint, bottom navigation under a stack of screens below it — and nothing in it reads which head or
which platform it is running on.

Two questions are answered before the frame is drawn at all: which deployment this client belongs to, and who is
asking it. `src/signIn/` is where both are, as one form rather than two screens, because a person was handed the
address and the credential together — and it stands in front of the frame rather than inside it, because a frame
with nothing behind it is a frame around nothing. Where the origin that served the client is the deployment, the
address half is simply not rendered. One form is still two requests wherever the address was typed: the client asks
what is at it before handing it a password, so a mistyped host is told apart from a refused credential and receives
nothing. Once somebody is signed in, focus moves to the start of the workspace rather than staying on the control that
reached it.

Each space is reached at a **fragment address** of its own: `#/discover`, `#/mail`, and one per space after them.
`src/routing/` is the whole of it, and it is deliberately not a package — a handful of addresses with no segment, no
parameter, and no nested tree are what `location.hash` and `hashchange` already are, and the browser keeps the history
for us. A fragment rather than
a path because a path would have to be reloadable, and the service serves the bundle with no fallback mapping an
unmatched path onto the entry document; a fragment never reaches a server, so every address reloads on both heads with
nothing configured.

`src/workspace/` is what survives moving between spaces, and it is mounted above the frame for exactly that reason. It
holds one **scope** — every mailbox at once, one role across all of them, one mailbox, or one folder of one — beside the
question, the message that is open, the conversation being read in front of it, the messages that have been picked
out, what was searched for before, and the rows of the folder tree somebody has folded away. It is kept in the store the web head keeps its credential in, so a reload returns to what was on the
screen; signing in and signing out both empty it, because what somebody was looking at and about to ask is theirs rather
than the machine's.

`src/folders/` is what writes that scope. It draws the owner's mailboxes and their folders as one tree, read from the
folders route in a single exchange, with the roles that span every mailbox above them — so asking about every inbox at
once is one act rather than three. A folder is placed and named by the role the deployment gave it rather than by what
its server calls it, because a name is whatever a provider chose in whatever language.

`src/messageList/` is what that scope is drawn as. It reads `/api/client/emails`, which is keyset-paged in both
directions, and it is where the client's three hardest constraints meet: a mailbox of two hundred and fourteen thousand
messages that has to stay smooth, a reading position that has to survive leaving the folder and reloading, and a
multi-selection the rest of the client reads as scope for the question it is about to ask.

Three bounds hold that up, and each is one module. **The document holds a window of rows rather than the folder** —
`src/messageRows/rowWindow.ts` is arithmetic over four numbers, and the number of rows in the document is the same on
the first screen as at message forty thousand. **The list holds a window of pages rather than every page it has read** —
`heldTimeline.ts`; a page too far from the reader keeps its place and its cursor but loses its rows, so the list keeps
its height, nothing under the reader moves, and scrolling back into it reads that page again from its own cursor rather
than reading the folder from its leading end. **Where the reader is lives outside React** — `rememberedListings.ts`,
keyed by the deployment and the folder, holding a cursor, a row, an order, and the filters together so a cursor cannot
outlive the list it was issued for, and holding nothing about any message.

`src/messageRows/` is that arithmetic and the row it measures, and it sits apart from either screen because both of
them draw the same row: the folder's list, and what a search found. A second arrangement of the same three lines is how
the client would stop looking like one product, and a second copy of the windowing is how the two would stop agreeing
on what a row measures.

What a row opens is what the reading pane beside it draws. The list writes the message into the workspace and the pane
reads it from there, so the two meet over one value rather than over each other — and nothing is open until a reader
has opened it.

`src/thread/` is the conversation that message belongs to, drawn in the same place and **in front of** it rather than
instead of it: the workspace still holds the message, so closing the conversation returns to it and nothing had to
remember where it came from. It reads `/api/client/threads/{threadId}`, which spans folders and accounts because a
conversation does — the question is in the inbox, the answer is in the sent folder — and it takes the participants and
the message count from that answer rather than walking the messages it happens to hold.

Presentation is the whole difficulty there, because a long conversation is mostly repetition. Three things answer it.
Each message is one line until somebody opens it, and **opening it is what mounts its body**, so a conversation of
thirty messages costs one read rather than thirty-one. The line shows what that message added, trimmed of the history it
quoted by the deployment rather than here. And inside an opened message the quotation it ended on is folded behind a
disclosure — `messageBody/quotedHistory.ts` splits it — because the message it quotes is a row of its own a few lines
up. A conversation opens at the message somebody arrived at, else at what they have not read, else at its last word,
and that is decided once from what is held when it is first drawn so a page arriving later cannot close what somebody is
reading. A conversation longer than one page is read on rather than cut off, and says which of the two it is.

**No package windows it, and that is a measurement rather than a preference.** Every row of this list is one height,
because the row is a fixed three lines by design — who wrote, what about, and a line for a sentence about the message
rather than from it, which carries why a search result matched today and what MailFathom made of the message when
stage 3 lands — and the browser suite asserts that every drawn row measures the same. What a
virtualizer buys over sixty lines of arithmetic is the machinery for measuring rows that are _not_ one height, which
this list has no use for; what it costs is a dependency, a licence review, and a census in
[the third-party register](../THIRD_PARTY_LICENSES.md). So the arithmetic stays, the one height it depends on is
measured off a rendered row rather than written down twice, and the assertion that keeps it true runs on every pull
request. A row that ever stops being one height is the argument for reopening this, and it fails a test rather than
degrading quietly.

`src/search/` stands above that list rather than on a screen of its own, because that is where somebody reaches for
it: they are looking at a folder and the message is not in front of them. It reads `/api/client/emails/search`, which
ranks by words and by meaning at once and says in its answer which of the two happened — so a page ranked by words
alone says whether this deployment embeds nothing by choice or whether its provider is refusing, rather than being a
quietly narrower answer. Each result carries the extract around what matched, marked by the deployment and drawn as
text rather than as markup; one that matched by meaning carries no extract and says so, because a row with nothing
under it would read as unexplained.

The hard part is making the scope legible, and the answer is that a search carries its own. The mailbox or folder
somebody was looking at is copied onto the search when it is submitted and drawn as an object they can take off, beside
every other filter in force — sender, recipient, a range of days, read and flag state, attachments, and whether junk
takes part. So an empty result is a search somebody can widen one press at a time rather than an absence, and a search
narrowed by something nobody can see cannot happen. What ranks is the words; every one of those constrains.

The field promises what it does today, which is a phrase. Turning a sentence into filters is stage 3's work, and it
lands on this screen rather than replacing it — which is why the filters here are objects with values in them for
something to write into.

## Styling, and the two themes

Tailwind is wired CSS-first through `@tailwindcss/vite`. The palette, the type scale, the radii, the shadow steps, the
breakpoints the composition changes at, the safe-area insets, and the motion defaults are `@theme` tokens in
`src/Client.App/src/styles.css`, and there is no JavaScript configuration file.

Every value there is the design project's rather than this repository's, declared in OKLCH under two themes. What a
screen composes against is never a hue but a **semantic** name — a page, a panel, a rail, a sunken region, three line
weights, four text weights, an accent, a healthy state, a warning. Both themes declare the same names, which is what
lets the light and the dark client be one set of utilities rather than a `dark:` variant on every one of them.

The typeface and the symbols are in the bundle for the same reason the credential never leaves it: **no screen reaches
an external origin.** Instrument Sans is committed under `src/Client.App/src/assets/fonts/` and declared by
`@font-face` rules pointing at those files, and the Material Symbols Rounded outlines under
`src/Client.App/src/assets/icons/` are inlined at build time by `src/Client.App/src/controls/icons.ts`, which is the
only place the client draws a symbol from. A deployment on a private network therefore renders the way it was designed,
a reader hands no font CDN a request per screen, and the browser suite asserts that the built bundle asks for nothing
off its own origin.

`src/theme/` decides which of the two is painted, from the person's choice of light, dark, or following the machine.
It writes one `data-theme` attribute on the document before the first paint, so nothing on a screen ever asks which
theme is in force — the same rule that keeps a screen from asking which language it is in.

Which of the three is chosen follows the person rather than the machine, and `src/preferences/` is what makes it so:
the choice is read from and written to the deployment over `/api/client/preferences`, so somebody who set the client up
the way they read does not set it up again per browser profile. The device still resolves a theme before anything is
signed in — there is no session to read one over above the sign-in screen, and a client that waited on the network to
paint itself would open blank — and what the deployment answers replaces that value once a session exists. The language
is the opposite case and stays on the device: it has to be resolved for somebody who has not signed in and may never
get a session, so there is nothing to read it over.

## What the build produces

`pnpm build` writes `src/Client.App/dist/` — a directory of static files and nothing else. No Node process joins any
deployment shape: the container image builds this in a stage of its own and copies the result beneath its web root, so
what a deployment gains is files and a setting rather than a second service.

`src/Client.App/public/` is copied into that directory verbatim, and it holds one file:
`THIRD-PARTY-NOTICES.txt`, the notices of everything the bundle actually redistributes — the MIT text of the five
packages, the SIL Open Font License the typeface is under, and the Apache-2.0 grant the icon outlines are under. The
build minifies every module into one chunk and none of the five carries a banner of its own, and a `woff2` file and an
inlined path carry nothing at all, so the notices travel as text beside the code rather than inside it. [The third-party register](../THIRD_PARTY_LICENSES.md) is where the review behind it
lives, and it is what says which packages that file has to name.

The version the client displays comes from `<VersionPrefix>` in `Version.props`, read at build time through
`scripts/read-declared-version.sh` and substituted into the bundle. No version number is written into a manifest or
into source.
