// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { defineConfig } from 'vitest/config';

// `pnpm test` is the whole of how the client suite runs, and this file is why there is one invocation rather than one
// per package: the two packages are tested differently and Vitest projects are what express that difference without a
// second command. Neither project names an `include` glob, because a test lives beside the source it covers and
// Vitest's default already finds it there — `frontend/tests/AGENTS.md` is where that placement is decided.

/**
 * How long one test may take before it is reported as stopped.
 *
 * Stated because Vitest's default of five seconds is below what this suite's largest tests need, rather than because
 * any of them waits on a duration — nothing here sleeps, and `frontend/tests/AGENTS.md` § Time and randomness is what
 * keeps it that way. Two kinds of test cost more than five seconds on a machine that is busy, and neither has a race
 * left in it to remove: one that mounts the whole application over a fake deployment and walks several screens, and
 * one that renders a few hundred elements synchronously, which cannot be waited for because nothing about it is
 * asynchronous. Measured over ten consecutive runs of the whole suite with a second copy of it running beside them,
 * the heaviest single test reached **11 s** and the suite took between 144 s and 336 s, against roughly 138 s with
 * nothing beside it. The number is five times that worst case rather than the idle figure, because a run competing
 * with another gate is the case the default was reporting as a failure of whatever change happened to be in the tree.
 *
 * It is a ceiling on a test that has stopped rather than a budget to spend: nothing is slower for it being large, a
 * test that approaches it is one to look at, and one that reaches it is stuck. It is written on each project rather
 * than beside `coverage` above because a project inherits none of the root's own test options.
 */
const eachTestGets = 60_000;

export default defineConfig({
    test: {
        // Coverage is collected on every run rather than behind a flag, because `pnpm test` is the whole of how this
        // suite runs and a second invocation is exactly the drift that rule exists to refuse. The v8 provider is what
        // makes that affordable: it reads the counters the runtime already keeps instead of instrumenting the module
        // graph, so the figure costs a report rather than a slower suite.
        //
        // Nothing is enforced on it, and that is a decision rather than a step left undone —
        // `frontend/tests/AGENTS.md` § Coverage holds the reasoning.
        coverage: {
            enabled: true,
            provider: 'v8',
            reporter: ['text', 'html'],
            // Beside the service's own reports under the repository-root `artifacts/`, which `.gitignore` already
            // covers. Vitest resolves this against the root project, which is `frontend/`.
            reportsDirectory: '../artifacts/coverage/client',
            // Relative to each project's root, so one pattern reaches both packages' sources. Without it the report
            // would name only the files a test happened to import, and a module nobody covers — the thing worth
            // seeing — would be missing from it rather than sitting at zero.
            include: ['src/**/*.{ts,tsx}'],
            // Everything the pattern above reaches that is not code somebody wrote a behaviour into. Vitest
            // drops the suite's own test files, and its default exclusion list is empty, so these three are the
            // whole of it: a declaration file states types and runs nothing, `main.tsx` is the composition
            // root that mounts React into the document — the client's counterpart to `Host` and `AppHost`, left
            // out of the service's measurement for the same reason — and a `.harness.tsx` module is the doubles and
            // the render a family of test files shares, which runs only under a test and asserts nothing itself.
            exclude: ['src/**/*.d.ts', 'src/main.tsx', 'src/**/*.harness.tsx'],
        },
        projects: [
            {
                // The half that reaches the service is ordinary logic with no DOM in its closure, so it is run without
                // one. That is the same boundary `src/Client.Backend/tsconfig.json` states — an `environment` of
                // `jsdom` here would put `document` and `window` back at run time for a package whose whole point is
                // that it never had them.
                test: {
                    name: 'Client.Backend',
                    root: 'src/Client.Backend',
                    environment: 'node',
                    testTimeout: eachTestGets,
                },
            },
            {
                // The application half, which extends the package's own Vite configuration rather than restating it:
                // that is what makes the resolver, the plugins, and the `__MAILFATHOM_VERSION__` substitution the same
                // ones the bundle is built with, so a test never proves a screen against a second arrangement.
                extends: './src/Client.App/vite.config.ts',
                test: {
                    name: 'Client.App',
                    root: 'src/Client.App',
                    environment: 'jsdom',
                    setupFiles: ['./vitest.setup.ts'],
                    testTimeout: eachTestGets,
                },
            },
        ],
    },
});
