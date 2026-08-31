// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// `Version.props` is the one place a version number is written, and `scripts/read-declared-version.sh` is how anything
// that has to put it somewhere reads it. Substituting it at build time is what keeps the number out of a manifest and
// out of source; a screen reads `__MAILFATHOM_VERSION__`, which `src/environment.d.ts` declares.
const declaredVersion = execFileSync(resolve(import.meta.dirname, '../../../scripts/read-declared-version.sh'), {
    encoding: 'utf8',
}).trim();

export default defineConfig({
    plugins: [react(), tailwindcss()],
    define: {
        __MAILFATHOM_VERSION__: JSON.stringify(declaredVersion),
    },
    build: {
        // The whole of what this stack produces: a directory of static files the container image serves from its web
        // root. No Node process joins any deployment shape.
        outDir: 'dist',
    },
});
