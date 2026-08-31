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
call; wiring `/api/client` in was not needed to answer the question.

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

**Scrolling, list interaction and responsive transitions are ordinary web behaviour** and behave
accordingly. Nothing needed virtualization at this list size, and nothing here proves the 214k-message
timeline the real client has to render — that is the first thing a deeper prototype must test.

**Tauri's friction is entirely on the host side, not in the code.** Not one line of the application
knows it is in Tauri. The shell is the scaffold's own `lib.rs` with the sample command removed.

## Blockers found on this host

**Desktop Tauri build — blocked on system packages.** `cargo build --release` resolves and compiles
the whole dependency graph down to `gobject-sys`, which then stops:

```
The system library `gobject-2.0` required by crate `gobject-sys` was not found.
```

`libwebkit2gtk-4.1-dev` is available in apt (2.52.3) but not installed, and installing it needs root
on a shared machine. One command unblocks it:

```bash
sudo apt install libwebkit2gtk-4.1-dev build-essential curl wget file libxdo-dev \
  libssl-dev libayatana-appindicator3-dev librsvg2-dev
```

Even then the host is headless, so a window needs `xvfb-run` or an X11-forwarded session. So the
acceptance item *"the Tauri application starts successfully on at least one desktop target"* is
**not met**, and the reason is this machine's package set rather than anything about the stack.

**Android — not exercised.** No Android SDK, no NDK, no `adb` and no `ANDROID_HOME` on this host.
`npm run tauri android init` would require installing the SDK, the NDK and a platform image, which
is exactly the substantial environment work the issue said to stop at.

**Google Fonts do not load in the sandboxed screenshot environment**, so the captures fall back to
the system sans instead of Instrument Sans. Cosmetic, and it does not affect layout.

## Verdict

**Worth a deeper prototype.** On the two things the PoC could actually measure — how closely an agent
reproduces a design, and how much platform-specific code the result needs — React + Tailwind came out
clearly ahead of the Uno path, and Tauri stayed invisible. What it did *not* measure is what would
decide a migration: the desktop shell never ran here, Android was never touched, and no large mailbox
was ever rendered. A follow-up should start by installing the Linux WebView packages, then put a real
`/api/client` behind the mail screen with a mailbox big enough to hurt.
