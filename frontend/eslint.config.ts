// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What `TreatWarningsAsErrors` and the analyzer set are to the service, this file is to the client. Every rule below
// reports as an error and `pnpm lint` runs with `--max-warnings 0`, so a lint violation fails a build rather than
// leaving a note in a log.

import js from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import prettier from 'eslint-config-prettier/flat';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default defineConfig(
    globalIgnores(['**/dist/**']),
    js.configs.recommended,
    {
        // Type-aware linting, which reads the `tsconfig.json` nearest each file: one per package, and the workspace's
        // own for this file. ADR 0021 puts configuration and build files in TypeScript too, so this one is source that
        // is linted and type-checked like any other rather than a blind spot at the root of the workspace.
        files: ['**/*.{ts,tsx}'],
        extends: [tseslint.configs.strictTypeChecked, tseslint.configs.stylisticTypeChecked],
        languageOptions: {
            parserOptions: {
                projectService: true,
                tsconfigRootDir: import.meta.dirname,
            },
        },
    },
    {
        // TypeScript only. A `.js` or `.jsx` file under either package's source is refused here rather than noticed in
        // review, because nothing else would catch one: the type checker reads what its `include` names, and a
        // JavaScript file added beside a TypeScript one would simply be built.
        files: ['src/*/src/**/*.{js,jsx,cjs,mjs}'],
        rules: {
            'no-restricted-syntax': [
                'error',
                {
                    selector: 'Program',
                    message:
                        'Client sources are TypeScript. Write this as a .ts or .tsx file under the package it belongs to.',
                },
            ],
        },
    },
    {
        // The package boundary, stated where a reader meets it. `Client.Backend` declares neither React nor a
        // DOM-typed dependency, so the import below already fails to resolve and `document` and `fetch` are already
        // undeclared; this says why rather than leaving a resolution error to be read as a missing install.
        files: ['src/Client.Backend/**/*.{ts,tsx}'],
        rules: {
            'no-restricted-imports': [
                'error',
                {
                    patterns: [
                        {
                            group: ['react', 'react-*', 'react/*', 'react-dom/*'],
                            message:
                                'Client.Backend is what reaches the service and nothing more. A screen, a component, or anything React belongs in Client.App.',
                        },
                    ],
                },
            ],
        },
    },
    {
        files: ['src/Client.App/**/*.{ts,tsx}'],
        extends: [reactHooks.configs.flat.recommended, reactRefresh.configs.vite],
    },
    prettier,
);
