// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { fixtureOrigin } from './head';

// The shell binary the desktop suite drives, built the one way that puts a populated client in front of its WebView.
//
// `pnpm test:desktop` runs this and then the suite, so the two halves cannot disagree about the address: the origin is
// derived once in `head.ts`, baked into the binary here, and served by the configuration there.
//
// A debug build and no bundles. What is under test is the WebView the shell opens, so a `deb` and an `rpm` would be
// minutes spent on files nothing here reads — `Build the desktop client` is what produces those, and it produces them
// from a release build rather than from this one.

// Through a shell, for the reason `src-tauri/run-tauri.ts` reaches its own fallback through `bash`: on Windows `pnpm` is
// a script rather than an executable, and a direct spawn of it fails there with nothing to read. The arguments are
// literals, so there is nothing for a shell to reinterpret.
execFileSync('pnpm', ['desktop:build', '--debug', '--no-bundle'], {
    cwd: resolve(import.meta.dirname, '../..'),
    env: { ...process.env, MAILFATHOM_FRONTEND_URL: fixtureOrigin },
    stdio: 'inherit',
    shell: true,
});
