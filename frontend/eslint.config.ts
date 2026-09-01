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

// The calls that turn a value back into markup: `dangerouslySetInnerHTML`, `innerHTML`, `outerHTML`,
// `insertAdjacentHTML`, `document.write`, `document.writeln`, `setHTMLUnsafe` on an element or a shadow root,
// `DOMParser.parseFromString`, `Range.createContextualFragment`, and an `iframe`'s `srcdoc` in either form. The client is handed no markup and writes none, which is the
// whole of ADR 0024's safety statement and the reason no sanitizer is pinned: a message reaches a screen as a closed
// tree of typed values, and React escapes every one of them. Writing any of these is the single change that would
// undo that, so it is made unwritable rather than left to a reviewer — the same shape the localization rule takes.
//
// They are declared here because `no-restricted-syntax` is replaced rather than merged when a later configuration
// object sets it again, so the block that states the localization rule has to carry these as well.
const markupWritingSyntax = [
    {
        selector: 'JSXAttribute[name.name="dangerouslySetInnerHTML"]',
        message:
            'A message reaches this client as a closed document tree and never as markup, per ADR 0024. Draw it with the typed components in src/Client.App/src/messageBody/ instead.',
    },
    {
        selector: 'MemberExpression[property.name=/^(innerHTML|outerHTML|insertAdjacentHTML)$/]',
        message:
            'Writing markup from a value is what ADR 0024 refuses: nothing the service sends is markup, and building any here would put a parser back on the path that exists not to have one.',
    },
    {
        selector: 'MemberExpression[property.value=/^(innerHTML|outerHTML|insertAdjacentHTML)$/]',
        message:
            'Writing markup from a value is what ADR 0024 refuses, and reaching the same member through a computed name reaches the same parser.',
    },
    {
        selector: 'MemberExpression[object.name="document"][property.name=/^write(ln)?$/]',
        message:
            'document.write and document.writeln parse whatever they are given as markup, which is the one thing this client never does. Render elements instead.',
    },
    {
        selector: 'MemberExpression[property.name="setHTMLUnsafe"]',
        message:
            'setHTMLUnsafe parses its argument as markup on an element or a shadow root, which is the parser ADR 0024 exists not to have. Render elements instead.',
    },
    {
        selector: 'MemberExpression[property.name=/^(parseFromString|createContextualFragment)$/]',
        message:
            'This parses a string into nodes, which is the parser ADR 0024 exists not to have: a message reaches a screen as a closed tree of typed values and nothing here builds one from markup.',
    },
    {
        selector: 'JSXAttribute[name.name="srcdoc"]',
        message:
            "srcdoc is a document written from a value and parsed as markup. ADR 0024 draws a message with typed components in the application's own document, and neither a frame nor a second parser is part of that.",
    },
    {
        selector: 'MemberExpression[property.name="srcdoc"]',
        message:
            "srcdoc is a document written from a value and parsed as markup. ADR 0024 draws a message with typed components in the application's own document, and neither a frame nor a second parser is part of that.",
    },
];

export default defineConfig(
    // `dist/` is what the client build writes, and `src-tauri/target/` and `src-tauri/gen/` are what the desktop
    // shell's build writes; none of the three is source, and the crate target directory alone is larger than
    // everything this workspace actually holds.
    globalIgnores(['**/dist/**', 'src-tauri/target/**', 'src-tauri/gen/**']),
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
        // Both packages and every file in them, tests included: a test reaching for one of these would be proving a
        // screen against an arrangement the application refuses.
        files: ['src/*/**/*.{ts,tsx}'],
        rules: {
            'no-restricted-syntax': ['error', ...markupWritingSyntax],
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
                ...markupWritingSyntax,
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
