// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createHash } from 'node:crypto';
import { defineConfig } from '@playwright/test';

// `pnpm test:browser` is the whole of how the browser suite runs, and this file is what it runs under. It sits beside
// `vitest.config.ts` because the two are the same kind of decision for the two suites, and `frontend/tests/AGENTS.md`
// is where the boundary between them is drawn: this one starts a real browser against the built bundle, and that is
// the only reason a check belongs here rather than there.

// The bundle is served from `src/Client.App/dist/` by Vite's own preview server, which is what `pnpm build` has just
// written. Serving it rather than running `pnpm dev` is the point: a development server transforms modules on demand,
// so a screen proven against one has not been proven against the directory of static files a deployment publishes.
//
// The port is derived from this workspace's own path rather than fixed. Several sessions run on one machine at a
// time — each in a worktree of its own, beside whatever else is listening — so a conventional port, Vite's own 4173
// among them, is contended, and the failure that produces is the dangerous kind: a run that attached to a preview
// server somebody else started would report on that server's bundle instead of this one. Deriving it gives every
// worktree a port of its own and gives one worktree the same port on every run, which is what a free port asked of the
// operating system could not do — Playwright re-reads this file in each worker process, and an answer that differed
// between those reads would point the browser somewhere the server is not.
//
// The range is 20000 to 32767, which stops below the floor of Linux's default ephemeral range: a port above it can be
// handed to any outbound socket on the machine between one run and the next, which would be exactly the intermittent
// bind failure this is meant to avoid. `--strictPort` is what makes the remaining cases loud rather than silent — a
// hash collision between two worktrees, or a stale server left behind by a crashed run, stops the suite by name
// instead of moving quietly to the next port up.
const previewPort =
    20000 +
    (createHash('sha256')
        .update(import.meta.dirname)
        .digest()
        .readUInt16BE(0) %
        12768);

const previewOrigin = `http://127.0.0.1:${String(previewPort)}`;

const runningInPipeline = process.env['CI'] !== undefined;

export default defineConfig({
    testDir: './tests',

    // Traces and screenshots of a failure are personal data the moment this drives a real deployment rather than the
    // stubbed transport, so they are written inside the workspace, ignored by Git, and uploaded nowhere.
    // `frontend/tests/AGENTS.md` holds that rule and the reason a pipeline run keeps them on the runner.
    outputDir: './.playwright',

    fullyParallel: true,
    forbidOnly: runningInPipeline,

    // No retries anywhere. A check that passes on a second attempt has reported that the client is flaky rather than
    // that it works, and hiding that in the pipeline is how a real intermittent defect stops being visible.
    retries: 0,
    reporter: 'list',

    use: {
        baseURL: previewOrigin,
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
        video: 'off',
    },

    // Chromium alone, and named rather than taken from the device registry so that what the pipeline installs is
    // exactly what it runs. The web head is served to whatever browser a person has, but a second engine here would
    // double the browser download for checks that are about the client rather than about engine differences; the
    // desktop head's WebView is not a browser this drives at all.
    projects: [
        {
            name: 'chromium',
            use: {
                browserName: 'chromium',
                viewport: { width: 1280, height: 720 },
            },
        },
    ],

    webServer: {
        // `--host 127.0.0.1` rather than the default, which is the name `localhost`: where that name resolves to `::1`
        // first — as it does on a GitHub-hosted runner — the server listens on the IPv6 loopback alone and the address
        // below is unreachable, which arrives as the web server timing out rather than as an address mismatch. Binding
        // the same literal the suite navigates to removes the resolution from the question.
        command: `pnpm --filter @mailfathom/client-app exec vite preview --host 127.0.0.1 --port ${String(previewPort)} --strictPort`,
        url: previewOrigin,

        // Piped rather than ignored, so a server that refuses to start says why in the run that failed. It prints one
        // line naming the address it bound, which is the whole diagnosis for this class of failure.
        stdout: 'pipe',

        // Never reused, even though the port is this worktree's own. A server already listening on it is either a
        // stale one from a crashed run, serving a bundle this one did not build, or somebody else's — and a suite that
        // reported on either would be answering about the wrong tree.
        reuseExistingServer: false,
    },
});
