// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { defineConfig } from 'vitest/config';

// `pnpm test` is the whole of how the client suite runs, and this file is why there is one invocation rather than one
// per package: the two packages are tested differently and Vitest projects are what express that difference without a
// second command. Neither project names an `include` glob, because a test lives beside the source it covers and
// Vitest's default already finds it there — `frontend/tests/AGENTS.md` is where that placement is decided.

export default defineConfig({
    test: {
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
                },
            },
        ],
    },
});
