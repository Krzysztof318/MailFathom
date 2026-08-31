# PoC: the MailFathom client in React + Tauri

A throwaway experiment for [issue #1382](https://github.com/Krzysztof318/MailFathom/issues/1382).
It answers one question — *would React + Tailwind + Tauri give us materially better AI-driven UI
development and a more convincing cross-platform feel than the Uno client* — and nothing else.

**This is not a migration and not production code.** It is deliberately outside every rule the rest
of the repository is held to: no unit tests, no ADR, no documentation-site page, no coverage, and no
`verify-full`. `frontend/` is untouched. Nothing here is meant to be merged; the branch is the
deliverable and no pull request was opened.

## What was built

`react-client/` is a Vite + React 19 + TypeScript + Tailwind v4 application inside a Tauri 2 shell,
reproducing the four application mockups from the Claude Design project *MailFathom Demo* — read
through the Claude Design MCP integration rather than transcribed from screenshots:

| Mockup | Screen | Deep link |
|---|---|---|
| 05 Odkrywaj — stan startowy | intent field, scope bar, recent questions, account freshness, saved views | `/` |
| 06 Adaptacyjny wynik | Answer / Timeline / FactTable blocks with an EvidenceList and SuggestedAction panel | `#/result` |
| 07 Droga do dowodu | citation opens the evidence inspector beside the untouched result, plus the decision trail | `#/result/5` |
| 08 Poczta — źródłowy mail | mailboxes, AI filters, annotated message list, reading pane, ThreadState, thread intent field | `#/mail/aneks` |

All data is mock data in `react-client/src/data.ts`, transcribed from the mockup. There is no backend
call; wiring `/api/client` in was not needed to answer the question. The mailbox carries **200
messages** — the five scripted ones the mockup shows, plus 195 generated deterministically so the
list has a real length. Every row opens: a generated message gets a thread derived from its own row
rather than a dead end.

Both layouts are real rather than merely fluid: the icon rail becomes a bottom tab bar, the result's
two panes become one scroll, and the evidence inspector and the reading pane become pushed screens
with a back affordance.

## Running it

```bash
cd poc/react-client
npm install
npm run dev                 # browser, http://localhost:1420
npm run tauri dev           # desktop shell — see the blocker below
```

## What we learned

**Reproducing the mockup was fast and close.** The four screens went from a design read to a matching
implementation in one sitting, with two correction rounds driven by screenshots. The mockup is HTML
with inline styles, so the design *is* already the target medium — measurements, colours and spacing
transfer directly instead of being re-derived. That is the largest single difference against the Uno
path, where the same mockup has to be translated into XAML before anything can be compared.
The whole PoC is about 900 lines across nine files.

**Platform-specific code so far: none.** Nothing in `src/` branches on platform. The only
platform-shaped decisions are two CSS lines (`overscroll-behavior`, disabling text selection outside
content) and `env(safe-area-inset-bottom)` on the tab bar.

**Back behaviour is application-like, with no router.** Navigation is 40 lines over the History API
(`src/navigation.ts`); Android's back gesture and the desktop window's back binding both arrive as
`popstate`. Verified in a real browser: tapping a recent question, then a citation, then going back
twice returns through the inspector to the start screen, and inside the mail client a thread pushes
and pops correctly. Selecting a message in the two-pane desktop layout uses `replaceState` so it does
not become a back step — the distinction a mail client needs, and it cost one conditional.

**Scrolling and list interaction are ordinary web behaviour.** Measured in a mobile-emulated
Chromium against the 200-message list: 200 rows are 1 593 DOM nodes, `DOMContentLoaded` at 253 ms,
`load` at 263 ms, an 18 138 px scroll height, and jumping to the bottom forces layout in 0.1 ms. No
virtualization, and none needed at this size.

**This does not settle the question it looks like it settles.** 200 rows is two orders of magnitude
below the 214k-message mailbox the real client has to render, and the honest reading of these numbers
is only that nothing pathological happens in the small case. A list that keeps every row in the DOM
grows linearly, so the deeper prototype still has to test a real mailbox and will almost certainly
need windowing.

**Tauri's friction is entirely on the host side, not in the code.** Not one line of the application
knows it is in Tauri. The shell is the scaffold's own `lib.rs` with the sample command removed.

## Both targets were built

**Desktop: builds and starts.** `cargo build --release` finishes in 3m29s and produces a 13 MB
binary, which then runs on a virtual display and holds its window with nothing on stderr:

```bash
xvfb-run -a -s "-screen 0 1440x900x24" ./src-tauri/target/release/react-client
```

This host needed the Linux WebView development packages first — without them the build stops at
`The system library gobject-2.0 required by crate gobject-sys was not found`:

```bash
sudo apt install libwebkit2gtk-4.1-dev build-essential curl wget file libxdo-dev \
  libssl-dev libayatana-appindicator3-dev librsvg2-dev xvfb
```

**Android: an APK was built**, `pl.mailfathom.poc`, `minSdk 24`, `targetSdk 36`, four ABIs. No
emulator or device is needed — only `tauri android dev` wants one. What it took, on a host that had
none of it:

```bash
sudo apt install -y openjdk-21-jdk                # see the JDK note below
# command-line tools unzipped to $ANDROID_HOME/cmdline-tools/latest
android sdk install platform-tools platforms/android-36 build-tools/36.1.0 ndk/29.0.14206865
rustup target add aarch64-linux-android armv7-linux-androideabi \
  i686-linux-android x86_64-linux-android
npm run tauri -- android init
npm run tauri -- android build --apk --debug
```

`sdkmanager` is superseded by `android sdk`, whose package names are full paths — bare `platforms`
and `build-tools` return `Package not found`. `compileSdk` is 36, so `platforms/android-36` is the
platform the template actually wants.

**The JDK is the one real trap.** The template pins Gradle 8.14.3, which cannot read Java 25 class
files and fails with `Unsupported class file major version 69` — after the Rust cross-compilation has
already succeeded, so the error looks unrelated to Java at first. Nothing has to be uninstalled: run
the Android build with `JAVA_HOME` pointing at a JDK 21, or set `org.gradle.java.home` in
`gen/android/gradle.properties`. The SDK tooling itself is fine on 25.

**Size: a release APK is 34 MB, a debug one 444 MB.** The debug figure is entirely four unstripped
Rust libraries at 100–115 MB per ABI and is worth quoting to nobody. The release universal APK breaks
down as 8.7 MB (`arm64-v8a`) + 5.8 MB (`armeabi-v7a`) + 8.5 MB (`x86`) + 8.3 MB (`x86_64`) of native
code, plus a 2.0 MB `classes.dex`. Shipping per-ABI splits rather than the universal APK would put an
arm64 device at roughly 11 MB, which is unremarkable for an app of this kind.

The release APK is unsigned (`app-universal-release-unsigned.apk`); signing needs a keystore and a
`signingConfig`, neither of which this PoC set up.

**Google Fonts do not load in the sandboxed screenshot environment**, so the captures fall back to
the system sans instead of Instrument Sans. Cosmetic, and it does not affect layout.

## Verdict

**Worth a deeper prototype.** On what the PoC could measure — how closely an agent reproduces a
design, how much platform-specific code the result needs, and whether both shells build — React +
Tailwind came out clearly ahead of the Uno path, and Tauri stayed invisible in the source: not one
line of `src/` knows it is in Tauri, and the same tree produced a desktop binary and an Android APK
with no platform branches at all.

What it did *not* measure is what would decide a migration. The mailbox is 200 messages, two orders
of magnitude below the real one, so the list cost here says nothing about the case that matters.
Nothing ran on a physical device, so touch, keyboard insets and the real back gesture are still
unverified — back was proven in a desktop browser, not on Android. And there is no backend:
`/api/client` was never wired in.

A follow-up should start there — a real API behind the mail screen, a mailbox big enough to hurt, and
the signed APK on an actual phone.
