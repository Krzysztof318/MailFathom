// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { readdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { expect } from '@playwright/test';

import { declaredVersion, test } from './client.harness';
import * as messages from './fixtures/messages';

// What the build publishes, read off the directory it wrote rather than off a page.

test('carries no example mail and no fixture deployment in what it publishes', async () => {
    // `pnpm dev:fixtures` serves this same client out of the corpus above, so the one thing that has to be proved
    // about that convenience is that it is a convenience: a production build folds away the condition
    // `src/Client.App/src/main.tsx` reaches it behind, and nothing under `dist/` may therefore mention either the
    // corpus or the options a development run publishes on `window`. This suite is where it is asserted because this
    // suite is the one that builds — `pnpm test` never does, so it could only assert it about the source.
    const published = resolve(import.meta.dirname, '../src/Client.App/dist');
    const files = await readdir(published, { recursive: true, withFileTypes: true });
    const written = await Promise.all(
        files.filter((entry) => entry.isFile()).map((file) => readFile(resolve(file.parentPath, file.name), 'utf8')),
    );
    const bundle = written.join('\n');

    // The version the build stamped in, asserted present before anything is asserted absent: an absence read off a
    // directory nothing was read from would pass whatever the build had written.
    expect(bundle).toContain(declaredVersion);

    expect(bundle).not.toContain(messages.newsletterSubject);
    expect(bundle).not.toContain('mailfathomFixtures');
});
