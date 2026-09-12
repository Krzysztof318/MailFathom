// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { defineConfig } from '@playwright/test';

import { fixturePort } from './tests/desktop/head';

// The fourth suite, and the only one that drives the head this repository ships as something somebody installs.
// `pnpm test` renders components, `pnpm test:browser` drives the built bundle in Chromium, `pnpm test:end-to-end`
// drives it against a real deployment, and this one drives the desktop shell's own WebView.
// `frontend/tests/AGENTS.md` § *The desktop suite* is where the boundary between the four is drawn.
//
// It is a configuration of its own for the reason the end-to-end one is: the two disagree about everything a
// configuration decides. No browser is launched here at all — the window is opened by `tauri-driver` and reached over
// the WebDriver protocol, so there is no project, no viewport, and no trace to retain. What Playwright is here is the
// runner the workspace already has, which is the whole reason this suite is not a second test framework.

export default defineConfig({
    testDir: './tests/desktop',

    // One at a time. Every case starts a shell of its own, each one a WebView process tree against a single X display,
    // and a machine running four of those at once reports contention as a screen that never settled.
    fullyParallel: false,
    workers: 1,

    forbidOnly: process.env['CI'] !== undefined,

    // No retries, for the reason neither of the other browser suites has any: a case that passes on a second attempt
    // has reported that something is flaky rather than that it works, and here that something would be the shell.
    retries: 0,
    reporter: 'list',

    // Nothing is written here — the suite captures no screenshot, no trace and no video, on the rule
    // `frontend/tests/AGENTS.md` states for the browser suite: a capture of a signed-in client is personal data, and
    // the description of what was observed is what travels. Playwright wants somewhere to put a result regardless, and
    // this keeps it inside the workspace, where `.gitignore` already covers the name.
    outputDir: './.playwright-desktop',

    // The client the shell loads, served from the fixture corpus rather than from the built bundle — which is what
    // makes a signed-in screen reachable at all. `src/Client.App/src/deployment/transportForThisRun.ts` gates the
    // corpus on `import.meta.env.DEV`, so a bundled shell has no example mail to answer with, and that gate is
    // deliberate: example mail has no business in a bundle a deployment publishes.
    webServer: {
        command: `pnpm --filter @mailfathom/client-app exec vite --mode fixtures --host 127.0.0.1 --port ${String(fixturePort)} --strictPort`,
        url: `http://127.0.0.1:${String(fixturePort)}/`,
        stdout: 'pipe',

        // Never reused, for the reason the browser suite never reuses its preview server: one already listening is
        // either stale or somebody else's, and a run that reported on either would be answering about the wrong tree.
        reuseExistingServer: false,
    },
});
