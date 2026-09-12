// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';

/**
 * One directory for every profile a run writes, removed when the run ends.
 *
 * A case needs a profile of its own — `head.ts` says why — and removing one as its shell closes does not work: the
 * shell outlives the session that ended it, and the graphics stack writes a shader cache under the profile after the
 * driver is already gone, so a removal there either races that write or has to wait on a process this suite never
 * started. Waiting out a duration nobody measured is the one thing `frontend/tests/AGENTS.md` refuses, so the removal
 * moves to the one moment at which every shell is provably gone: after the last case.
 *
 * Playwright calls the returned function as the run's teardown, which is why this is one module rather than a setup and
 * a teardown that would each have to find the same directory.
 */
export default function profilesForThisRun(): () => void {
    const root = mkdtempSync(resolve(tmpdir(), 'mailfathom-desktop-'));

    // Read by `freshProfile`, which runs in a worker process: a value set here reaches one, because a worker is forked
    // after this has run.
    process.env['MAILFATHOM_DESKTOP_PROFILES'] = root;

    return () => {
        // Retried but never fatal. Every shell has exited by now, so this is expected to succeed; a directory that
        // survives it is litter in the temporary directory rather than anything about the run, and a teardown that
        // threw would report it as a failed suite.
        try {
            rmSync(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
        } catch {
            // Left behind, deliberately.
        }
    };
}
