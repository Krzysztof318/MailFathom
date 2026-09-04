# The client workspace

`frontend/` is a [pnpm](https://pnpm.io/) workspace holding the two packages the MailFathom client is split into, and
`src-tauri/` beside them holding the native shell that wraps what they build — the desktop head, and under
[ADR 0027](../docs/decisions/0027-an-android-head-built-every-night-and-supported-by-nothing.md) an Android one that is
supported by nothing and, until the nightly job that record describes lands, built by nobody but a developer running
the command for it. It shares no build file and no configuration file with the service under
`backend/`; the two meet only over the HTTP API served beneath `/api/client`, which
[the client endpoint](../docs/operations/client-endpoint.md) describes.

```bash
pnpm install --frozen-lockfile   # restore, refusing to rewrite pnpm-lock.yaml
pnpm dev                         # the development server
pnpm build                       # the static bundle, into src/Client.App/dist/
pnpm desktop:dev                 # the desktop shell around that server, rebuilt as the shell changes
pnpm desktop:build               # the desktop application and its installers
pnpm android:init                # restore anything missing from the committed Gradle project
pnpm android:dev                 # the client on a running emulator or an attached device, against that server
pnpm android:build               # one debug APK covering arm64-v8a and x86_64
pnpm typecheck                   # both packages and eslint.config.ts, under the strict set below
pnpm lint                        # every rule an error, no warning tolerated
pnpm test                        # both packages' suites, once, non-interactively
pnpm test:browser                # build the bundle and drive it in a real browser
pnpm format                      # rewrite; pnpm format:check reports instead
```

The two `desktop:` commands need a Rust toolchain and the platform's WebView development packages, and the three
`android:` ones need an Android SDK, an NDK, a JDK, and four more Rust targets on top of that; none of the others needs
any of it. [Local development](../docs/operations/local-development.md) has both sets and names the failure a missing
one produces.

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

The deployment's signal channel is the same shape and the same reason. `Client.Backend` owns what a connection _is_ —
the ticket it is opened against, the address it is opened at, what a payload has to be before it becomes a signal, and
how long to wait before opening again — and publishes a `MailFathomSignalChannel` beside a `SignalStreamSchedule`;
`src/Client.App/src/signals/signalChannel.ts` is the one module that names `@microsoft/signalr`, because that package's
type declarations name the DOM. What the deployment says has changed then reaches a screen through context, and each
screen decides for itself what to read again.

## The native shell, and why it is not a third package

`src-tauri/` is a Rust crate rather than a workspace package, and it sits beside `src/` rather than inside it because
it is not application source: `frontend/src/` is TypeScript throughout and holds what every head renders, while the
shell holds what only a native one has. It owns the window, the application identity, and the installers — nothing
else. A screen is written once and reaches the web head, the desktop head, and the Android head unchanged, which is the
property ADR 0021 chose Tauri for; a platform difference that genuinely exists belongs here or in one stated rule in
`styles.css`, and never in a component.

`src/lib.rs` is the whole of it, and `src/main.rs` beside it is the three lines the desktop binary needs. The split is
Android's and only Android's: an application there is started by the platform through a JNI entry point in a shared
object rather than by running an executable, so `run` carries `#[cfg_attr(mobile, tauri::mobile_entry_point)]` and both
heads start from the same function. What the file holds beyond the window is four commands of this repository's own and
one upstream plugin.

The commands exist for one reason: [ADR 0023](../docs/decisions/0023-where-the-client-keeps-the-credential-it-signs-in-with.md)
keeps the credential this head signs in with in the operating system's own store, which a webview cannot reach and a
shell can. `credential_arrangement` says where the credential will live this run, and `keep_credential`,
`read_credential`, and `forget_credential` are the three operations on it. Two of them answer whether they succeeded,
which is what lets the client say that a store refused to delete rather than report a sign-out that did not happen;
none of them answers _why_ one failed, because everything a failure could name is about a password. No capability file
names them, and writing one would be a mistake rather than an omission: Tauri gates its own plugin commands through an
access-control
list and never an application's, so a `capabilities/` entry naming these four would grant the webview reach into
plugins nothing here pins. What the webview reaches them through is `app.withGlobalTauri` in `tauri.conf.json`, which
puts `invoke` on `window.__TAURI__`, and that is what lets this shell pin no JavaScript binding of its own: the four
commands are this repository's, so nothing upstream publishes a package for them and writing one would be a package of
ours to keep in step with them.

What answers those four is `src/credentials.rs`, in two implementations selected by target: the desktop reaches the
machine's keychain through the `keyring` crate, and the Android head reaches the Android Keystore through a Kotlin
class in `gen/android/app/src/main/java/io/github/krzysztof318/mailfathom/CredentialStorePlugin.kt`, registered as a
Tauri Android plugin.
[ADR 0027](../docs/decisions/0027-an-android-head-built-every-night-and-supported-by-nothing.md) is the amendment that
put it there, and it is why the first command answers with an _arrangement_ rather than with whether a store exists:
the same fact — protected storage this client cannot reach — is a fallback to the page on the desktop and a refusal to
keep anything at all on a phone, and only the shell knows which head it is on. The Kotlin side reaches the Keystore
directly, with AES-GCM, rather than through `androidx.security:security-crypto`, whose every API is deprecated with no
further release planned — so the head carries no dependency for it and the APK's closure is the one it already had.

The plugin is `tauri-plugin-opener`, and it is there because opening a followed link outside the application is the one
thing the desktop head cannot do from the page. Its commands are a plugin's rather than this repository's, so they are
gated where the four above are not: `capabilities/open-a-link.json` grants the webview that one operation over `http`,
`https`, and `mailto` rather than the plugin's own default permission set — a capability is reach handed to whatever
runs in the page, so it names the operation the application actually makes and nothing beside it. A second permission
is added the same way, by the change that needs it.

| What                                                                | Where it is decided                                                                                                                                                                                    |
| ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Product name, application identifier, window title and minimum size | `src-tauri/tauri.conf.json`                                                                                                                                                                            |
| The version the application reports                                 | `<VersionPrefix>` in `Version.props`, merged in by `src-tauri/run-tauri.ts`                                                                                                                            |
| Which crates the shell links, and at which versions                 | `src-tauri/Cargo.toml`, resolved into the committed `Cargo.lock`                                                                                                                                       |
| Whether the webview may call the shell's own commands               | `app.withGlobalTauri` in `src-tauri/tauri.conf.json`, which is what puts `invoke` on `window.__TAURI__`                                                                                                |
| What the webview may load, connect to, and submit to                | `app.security.csp` in `src-tauri/tauri.conf.json`, which Tauri serves as the document's own Content-Security-Policy                                                                                    |
| What the webview may ask a registered plugin for                    | `src-tauri/capabilities/`, one file per capability                                                                                                                                                     |
| The icon every bundle carries                                       | `src-tauri/icons/` and the Android project's `mipmap-*`, both generated from `assets/icon-1254.png` with `pnpm exec tauri icon`; the adaptive-icon foregrounds are inset into the safe zone afterwards |
| The terms every bundle installs beside the application              | `bundle.resources` in `src-tauri/tauri.conf.json`, which takes the repository's own `LICENSE` and `NOTICE`                                                                                             |
| What a Linux package declares it needs                              | `bundle.linux.deb.depends` in `src-tauri/tauri.conf.json`; the `rpm` names none, per `docs/operations/desktop-client.md`                                                                               |
| The oldest Android release the head installs on                     | `minSdk` in `src-tauri/gen/android/app/build.gradle.kts`, which Gradle reads, seeded by `bundle.android.minSdkVersion` in `src-tauri/tauri.conf.json`                                                  |
| What the Android head asks the platform for, and what it refuses    | `src-tauri/gen/android/app/src/main/AndroidManifest.xml` and the `res/xml/data_extraction_rules.xml` beside it                                                                                         |

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

**The Android head is a fourth thing this crate builds, and its Gradle project is committed.**
[ADR 0027](../docs/decisions/0027-an-android-head-built-every-night-and-supported-by-nothing.md) is what says the head
exists at all and on what terms — one debug-signed APK, left on the run that built it, supported by nothing — and
`gen/android/` is the Android Studio project `pnpm android:init` generates from `tauri.conf.json` and this crate.
**One pipeline builds it**: `Build the Android client`, which `Nightly` calls and no release channel does, so an APK
comes either from a nightly run or from `pnpm android:build` on somebody's own machine.
Everything else under `gen/` is ignored; this one is tracked, because the decisions in it have nowhere else to live:

| The decision                        | Where it is written                                                                                     | What it says                                                                                                                                                                                     |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| The application identifier          | `bundle.identifier` in `tauri.conf.json`, and nowhere else                                              | `io.github.krzysztof318.mailfathom` — the same one the desktop head carries, because it is one application                                                                                       |
| The oldest release it installs on   | `bundle.android.minSdkVersion` in `tauri.conf.json`, and `minSdk` in `gen/android/app/build.gradle.kts` | 24, which is Tauri's own floor for the target. Gradle reads the second; the first is what a regeneration would render into it, and the two move together or the key is fiction                   |
| What it asks the platform for       | `uses-permission` in `AndroidManifest.xml`                                                              | `INTERNET`, and nothing else. A notification permission arrives with the notification                                                                                                            |
| What the APK's libraries resolve to | `dependencyLocking` in `gen/android/build.gradle.kts`, and `gen/android/app/gradle.lockfile` beside it  | The 80 artifacts behind the five declarations, fixed rather than resolved afresh on every build — `./gradlew :app:dependencies --write-locks` is what rewrites it                                |
| What it refuses to let leave        | `android:allowBackup` and `res/xml/data_extraction_rules.xml`                                           | Both halves off: the cloud backup, and the device-to-device transfer that the attribute alone stops governing at API 31                                                                          |
| Which platforms may open a link     | `platforms` in `capabilities/open-a-link.json`                                                          | Every platform this crate can be built for, named rather than left to the default of all of them — which is wider than what a release publishes, and narrower by iOS, for which no head is built |
| The version the artifact reports    | `<VersionPrefix>` in `Version.props`, merged in by `run-tauri.ts`                                       | The same number as everything else, which is why `android init` runs through the wrapper too — the CLI reads `Cargo.toml` for it otherwise, and that states none                                 |

`tauri android init` **writes no file that already exists**, which is what makes both halves of that arrangement work
and is the whole argument for committing rather than regenerating. Running it on a fresh clone restores whatever the
project is missing and leaves every edited file alone, so it is safe at any time and is what `pnpm android:init` is
for; and an _ignored_ project would silently revert to the template on every clean machine, taking the permission set
and the backup exclusions with it. The cost is the other side of the same property: a Tauri upgrade brings a new
template that the committed project will not pick up, so moving the pin means regenerating this directory deliberately
and reading the diff, which `frontend/AGENTS.md` § _The Android head_ states as a rule.

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
written for; the control on the settings screen — reached from the account menu at the foot of the navigation, and
standing beside the one on the sign-in screen for somebody who has no session yet — overrides that, the choice survives
a restart of either head, and changing it rewrites the screen without anything restarting.

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

## What the client records about itself

The client carries one OpenTelemetry pipeline for all three signals, composed once in `src/Client.App/src/main.tsx`
beside the deployment and the credential, and handed down as a value: `src/Client.App/src/telemetry/clientTelemetry.ts`
publishes it, and a screen that reports something takes it out of context rather than registering anything of its own.
`src/Client.Backend/src/telemetry.ts` is the other half — every request that package composes goes through it, so one
span and two measurements per request happen in one place whatever the operation did with the answer. That package pins
`@opentelemetry/api` and nothing else, which names no browser API and so crosses none of the boundary above.

**Composes rather than sends, because four of those requests are put on the wire by this package instead.** A file a
message carries and the picture the signed-in person is drawn by answer in octets, so the `fetch` for each of them is
in `src/Client.App/src/deployment/`. `Client.Backend` still owns the record: it publishes an operation for each, and
not the request builder behind it, which opens the span, composes the request inside it, hands that request to the
adapter, and closes on the reason the adapter read off the answer. A request composed outside a span would carry no
trace context and leave no record, so composing one is not something this package lets a caller do.

**That span is also what the request travels under.** It is the active context while the operation composes its
request, so `headersFor` writes the W3C trace context into the headers and the span the deployment opens is this one's
child — one trace over the screen, the request, the use case, and the query beneath it.
[What a client-originated trace contains](../docs/operations/telemetry.md#what-a-client-originated-trace-contains) is
the operator's page. What it asks of an operation here is that it compose its request before it awaits anything: the
context manager the SDK registers holds the active context across a synchronous run rather than across a suspension.

It exports to [the deployment's own OTLP receiver](../docs/operations/client-endpoint.md#the-telemetry-routes) on the
client surface, over HTTP with protobuf, presenting the session's credential exactly as every read does — so nothing is
exported until somebody has signed in.
[What it publishes](../docs/operations/telemetry.md#what-the-client-publishes-about-itself) is the operator's page,
including the one measurement only a client the deployment itself served can make, and why anything else reports
nothing in its place rather than a zero.

**Recording begins before exporting can, and `telemetry/holding.ts` is the gap between them.** The three providers are
registered where the client is composed, against exporters that hold rather than send, so starting up, resolving which
deployment this client belongs to, and a sign-in that did not succeed are recorded — which is exactly what nobody can
describe afterwards and what the deployment never saw. Signing in names the three OTLP destinations and empties what
was held into one export attributed to that session; signing out flushes what the session recorded and returns to
holding. What is held is bounded on records and on bytes, the oldest going first and the loss reported as a counter,
and it lives in memory alone: a client closed without a session keeps nothing, and a restart begins empty.

**The SDK behind the exporter is fetched rather than bundled.** `telemetry/exporting.ts` is reached through a dynamic
import, so the chunk carrying the three providers and the three exporters — 127 kB, 35 kB compressed — is downloaded
beside the first screen rather than inside it, and the document is on screen without waiting for any of it. What the
pipeline costs that document is the two interface packages in front of it — `@opentelemetry/api` and
`@opentelemetry/api-logs`, the registries every recording call reaches whether or not a pipeline was registered behind
them: 15 kB, 5 kB compressed.

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
out, what was searched for before, the rows of the folder tree somebody has folded away, and whether the mailbox column
itself is folded to its rail. It is kept in the store the web head keeps its credential in, so a reload returns to what was on the
screen; signing in and signing out both empty it, because what somebody was looking at and about to ask is theirs rather
than the machine's.

`src/folders/` is what writes that scope. It draws the owner's mailboxes and their folders as one tree, read from the
folders route in a single exchange, with the roles that span every mailbox above them — so asking about every inbox at
once is one act rather than three. A folder is placed and named by the role the deployment gave it rather than by what
its server calls it, because a name is whatever a provider chose in whatever language. The same tree is what the folded
column draws: a row keeps its place and its symbol and loses its label, a mailbox is marked by its colour where its name
stood, and the two folds stay independent — narrowing the column changes nothing about which mailboxes are open in it.

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

`src/contextMenu/` is what a row offers, and it is one component rather than a menu per list because the design draws
the same one on seven of them. Two gestures open it and each is the platform's own: the second button under a fine
pointer, replacing the browser's own menu, and a press held under a coarse one, which cancels past a small drift rather
than on any movement — a finger resting on glass reports jitter — and suppresses the tap it would otherwise have been,
so a finger that meant to open a message does, and one that meant to ask what the row offers never does both. `rowPress.ts` is those two openers, and `menuPlacement.ts` keeps the panel inside the pane,
because a menu drawn past an edge hides the acts at the bottom of it and those are the destructive ones. Where it stands
follows the width and nothing else: anchored at the gesture where there is room, and centred as a sheet where there is
not.

**Nothing in it performs an act.** Every item calls what the toolbar above the list calls and reports through the same
toasts, so filing a message from its row and filing it from the strip are one act asked for two ways rather than two
implementations that will come to disagree; the two acts standing behind a question are raised by the list instead,
because the question outlives the menu that asked it. It is also how a finger reaches the multi-selection at all — that
is the menu's first item, and it is what the list draws instead of a check control of its own.

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

`src/fullHtml/` is the second surface [ADR 0024](../docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md)
takes, and it stands in front of the message the same way a conversation does. What the reading pane draws is a closed
document tree; what this draws is the markup the sender actually wrote, and two different mechanisms are what make that
safe rather than one. The frame it is drawn in carries a `sandbox` attribute naming neither `allow-scripts` nor
`allow-same-origin`, so nothing in the markup runs whatever the markup holds; and what is drawn in it is the
self-contained representation the service composes, whose every remote address is already gone, so nothing in it
reaches the sender until a reader asks for that one message's pictures. The footer states those two separately and
credits each to what actually holds it, because a reader who is told the frame stops both learns nothing about what
asking for the pictures gives up.

Reaching it is a question rather than a control: pressing the one on the message's head opens a confirmation, and
neither answer is written down — per message, per reader, or at all. `workspace/rememberedWorkspace.ts` is what keeps
that true across a reload, by refusing to keep the value and refusing to read one back. `messageBody/MessageMarkupFrame.tsx`
is the one file in this tree permitted to write `srcdoc` at all, and `eslint.config.ts` names it by path rather than
letting a call site suppress the rule.

A file a message carries opens on a third surface, in the same place and by the same rule: `readingPane/Attachment.tsx`
is the row that describes it, and `readingPane/AttachmentView.tsx` is what a press on it opens — a tab of its own where
somebody works in tabs, and standing in front of the message where they do not. Opening and downloading are two controls
on that row rather than one, because they are two acts: the chip opens the file inside the client and the control at its
end writes it to the person's machine, so looking at something never costs a trip to a downloads folder. It reads the one route the download
already used, `/api/client/messages/{storedEmailId}/attachments/{position}`, under the size the message declared, so
nothing new is exposed by showing a file rather than saving it.

**What it draws is decided by kind rather than by attempt**, in `readingPane/shownAttachment.ts`, and the module carries
the reasoning: a raster picture is drawn in an `img`, where the element itself is what makes a sender's octets safe;
text is decoded under the character set the message declared and drawn as text. Everything else — a PDF, an SVG, a
document — says so and offers the download it already had, because the answer has to be the same in a browser and in
the WebView the desktop head loads, and neither an engine mode nor a bundled viewer is a promise both keep. So the
decision is a short list of kinds and two size ceilings rather than a `try`: a file this client will not show is named
as one before anything is fetched, which is also what keeps a reader from waiting for a read that was never going to
draw. Nothing an attachment carries reaches a host other than the deployment, on the rule [ADR
0024](../docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) states for a message's own
markup and for the same reason.

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

## Writing a message

`src/composer/` is the other half of the Mail space: everything above reads mail, and this is where one is written.
It is one model drawn in two shapes — the reading column at a wide width, so what is being written stands exactly
where what is being read stands and the mailboxes and the list stay beside it, and the whole screen at a narrow width,
because a column that has to hold a header, four fields, and a footer has nothing left over to show a message
underneath. Which of the two is drawn is the width the client has, not the head it runs on.

It is asked for from three unrelated places — the toolbar over the space, the control a narrow window puts at the
corner a thumb reaches, and the answering controls beside a message — none of which is its parent, so `useComposing`
is the context that carries the ask and `App.tsx` is the one place the screen is mounted, because what is being written
outlives moving between the spaces. What is being written is deliberately not in that context: the composition belongs
to the composer, so nothing above it can read half a message off the frame.

**Two drafts are kept, and they are different promises.** What is being typed is written continuously to the tab's own
store, so a reload returns to it — that is `keptComposition.ts`, and it is the session's store rather than the
machine's because words, a subject, and the addresses they are for are personal data under the same rules as the mail
already on the screen; signing out drops it in the same act that empties the workspace. The draft in the owner's own
drafts folder is a separate thing that somebody asks for, because every revision of that one reaches their mail
server — `useDraftAtDeployment.ts` holds it, and attaching a file and sending both file it first, each being an act the
author asked for.

**Nothing is sent without an explicit confirmation, and the confirmation names every address**, the blind copies
included — those being the ones a header row never shows back. It also names what the message would go out without: no
recipient, no subject, no words. None of the three refuses the send, because a message meant to go out that way is a
message somebody meant; the confirmation is the moment to notice rather than the moment to be stopped. Once queued, the
send can be taken back for as long as the deployment says it can, and what became of that is four answers rather than a
success and a failure — a message already being transmitted cannot be recalled, and saying so is the answer.

A refusal is a state of the screen rather than an error on it. Screening, the recipient policy, and a spending ceiling
each refuse a send, and each is drawn as its own sentence saying what would change it; a temporary refusal says the
message is still here. The four failure reasons are the four sentences they are everywhere else in the client.

Two things it does not do yet, and both are somebody else's route rather than a decision taken here. **A recipient is
completed from the conversation being answered rather than from the contact directory**, because that directory is not
served to the client yet — `src/Client.Backend` names no contacts operation, and the field is a `datalist` so it gains
one by being handed a longer list. And **the subject of an answer is read-only**: a save either names an account and a
subject or names the message it answers and lets the deployment derive both, so an edited reply subject is a value the
client surface has nowhere to put, and offering the field would be offering an edit that is discarded. The body is
plain text for the same kind of reason — what the surface takes is a plain-text draft with an optional HTML
alternative, and rich authoring is a stage of its own.

## Confirming what leaves the deployment

`src/confirmation/` is the one question the client puts in front of an act somebody cannot simply undo. Sending,
discarding what was written, and closing every open tab at once ask it there today, and flagging, filing, moving, and
deleting mail arrive at the same component as the stages that perform them land.

One question still draws its own dialog: the stop offered by the blocking overlay, in `src/blocking/`. Two things keep
it there rather than an oversight. It opens _over_ a dialog already open and relies on the platform stacking the two,
which is a property of that screen rather than of a question; and stopping an operation is refusing something that has
not finished, so it states no `Reversal` at all, and the union below has no member for that. Bringing it onto this
component means answering both — and the destructive fill it paints itself needs a `Manner` of its own, which is what
the first screen with a delete to confirm will add.

**It writes no sentence.** _Are you sure?_ teaches a reader to press yes without reading, so the question, what will
change, and what every way out is called are the caller's own words, in the terms of the thing being changed — the
addresses a message is going to, the count and destination of a batch — and the control that performs the act is named
after the act rather than `OK`. What the component does state is what happens **afterwards**, because that is the half a
caller forgets and the half that decides how heavy the question should have been: a `Reversal` is a closed union with no
default, so an act is either taken back within a period the client names, taken back for as long as the deployment
allows, or not taken back at all with what that costs said in the act's own terms. There is no fourth answer meaning
nobody thought about it.

The dialog is the platform's `<dialog>` opened modally, which is where the page behind it going inert, focus moving in
and being held, Escape leaving, and focus returning to the control that opened it all come from — four obligations
nothing in this tree implements. Whether it is open is therefore the element's own state, which is why a caller hands
over the reference and draws its own control: the composer's two ways to reach the send question are still one question.

`ProposedAction.tsx` beside it is the other half of MailFathom's autonomy scale, for the stages where the model offers
to act. It draws what would be done, why it was offered, what would change, and whether a confirmation stands between
agreeing and it happening — and it performs nothing on being drawn, there being no effect and no timer in it at all.

## Styling, and the two themes

Tailwind is wired CSS-first through `@tailwindcss/vite`. The palette, the type scale, the radii, the shadow steps, the
breakpoints the composition changes at, the safe-area insets, and the motion defaults are `@theme` tokens in
`src/Client.App/src/styles.css`, and there is no JavaScript configuration file.

Every value there is the design project's rather than this repository's, declared in OKLCH under two themes. What a
screen composes against is never a hue but a **semantic** name — a page, a panel, a rail, a sunken region, three line
weights, four text weights, an accent, a healthy state, a warning. A name means the same thing under both themes, which
is what lets the light and the dark client be one set of utilities rather than a `dark:` variant on every one of them —
the dark block redeclares the names whose value differs and leaves alone the two the design draws as one colour on both
of its artboards, the surface a sender's markup is drawn on and the mailbox-mark ramp past its first hue.

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
