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

// The attributes a browser reads out to somebody, which makes each of them a sentence rather than a setting. Written
// once because the same list is matched in three shapes below, and a list that drifted between them would leave a hole
// nobody could see by reading one of the three.
const readOutAttribute =
    'JSXAttribute[name.name=/^(alt|title|placeholder|aria-label|aria-description|aria-placeholder|aria-roledescription|aria-valuetext)$/]';

const readOutAttributeMessage =
    'This attribute is read out to somebody, so it is a user-visible string: put it in src/Client.App/src/localization/en.ts and pass translate() to it.';

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
    {
        // Every user-visible string leaves the component that shows it, which is the one rule that decides whether a
        // third language is a catalogue or a sweep through every screen written before it. A literal in markup is
        // refused here rather than noticed in review, because review is exactly what stops catching it once there are
        // enough screens to read. `no-restricted-syntax` is what states it: `Intl` and the catalogues are the whole of
        // the mechanism, so there is no plugin to install for this and nothing joins the bundle.
        files: ['src/Client.App/**/*.tsx'],
        rules: {
            'no-restricted-syntax': [
                'error',
                {
                    selector: 'JSXText[value=/\\S/]',
                    message:
                        'A user-visible string belongs in src/Client.App/src/localization/en.ts, with its Polish counterpart, and reaches the screen through translate().',
                },
                {
                    selector: ':matches(JSXElement, JSXFragment) > JSXExpressionContainer > Literal[value=/\\S/]',
                    message:
                        'A user-visible string belongs in src/Client.App/src/localization/en.ts and reaches the screen through translate(); a number is written with Intl under the active locale.',
                },
                {
                    selector: ':matches(JSXElement, JSXFragment) > JSXExpressionContainer > TemplateLiteral',
                    message:
                        'A sentence assembled in markup cannot be reordered by a translator. Write it as one catalogue entry with a {name} hole and fill it through translate().',
                },
                // The same attribute in its three written forms, because a selector is matched against the syntax
                // rather than against the value: `alt="…"` puts the literal directly under the attribute, while
                // `alt={'…'}` and ``alt={`…`}`` put it one level down inside an expression container. A rule catching
                // only the first would pass the two a person reaches for after being refused once. The step is
                // deliberately not a descendant match: `aria-label={translate('shell.language')}` carries a string
                // literal too, and it is the catalogue key rather than a sentence.
                {
                    selector: `${readOutAttribute} > Literal`,
                    message: readOutAttributeMessage,
                },
                {
                    selector: `${readOutAttribute} > JSXExpressionContainer > Literal[value=/\\S/]`,
                    message: readOutAttributeMessage,
                },
                {
                    selector: `${readOutAttribute} > JSXExpressionContainer > TemplateLiteral`,
                    message: readOutAttributeMessage,
                },
            ],
        },
    },
    prettier,
);
