// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { defineConfig } from '@playwright/test';

// The third suite, and the only one that reaches a service. `pnpm test` renders components, `pnpm test:browser` drives
// the built bundle against routed answers, and this one drives that same bundle against a MailFathom that was stood up
// the way an operator stands one up. `frontend/tests/AGENTS.md` § *The end-to-end suite* is where the boundary between
// the three is drawn.
//
// It is a configuration of its own rather than a second project inside `playwright.config.ts`, because the two suites
// disagree about everything a configuration decides: this one starts no server, is handed an origin it did not choose,
// fakes no route at all, and keeps what a failure produced. Folding them together would put the pull-request suite one
// mistake away from reaching a deployment.

// Where the deployment is, handed over by `scripts/run-end-to-end-client.sh`, which is what started it. Read rather
// than defaulted: a default would let this suite run against whatever happened to be listening on a conventional port,
// which is the one failure a run answering "the client works" must not be able to have.
function required(variable: string): string {
    const value = process.env[variable];

    if (value === undefined || value.trim() === '') {
        throw new Error(
            `${variable} is not set. This suite is run by scripts/run-end-to-end-client.sh, which stands the deployment up and hands it over; it has no deployment of its own to fall back on.`,
        );
    }

    return value;
}

export default defineConfig({
    testDir: './tests/end-to-end',

    // Kept, unlike the pull-request suite's, and the reason is the mail rather than the storage: every message this run
    // reads is a fabricated corpus delivered into a container that is destroyed with the run, so a trace carries no
    // personal data and a screenshot shows nobody's mailbox. `frontend/tests/AGENTS.md` holds the other half of that
    // rule — the suite that drives routed answers keeps its output on the machine that produced it, because the moment
    // a capture shows real mail it is personal data whatever produced it.
    outputDir: './.playwright-end-to-end',

    // One worker, and no parallelism. There is one mailbox and one credential, and reading a message marks it read —
    // so two specs racing over the same folder would each be asserting against what the other just changed.
    fullyParallel: false,
    workers: 1,

    forbidOnly: process.env['CI'] !== undefined,

    // No retries, for the reason the pull-request suite has none: a check that passes on a second attempt has reported
    // that something is flaky rather than that it works, and this is the suite where that something might be the
    // service.
    retries: 0,
    reporter: 'list',

    use: {
        baseURL: required('MAILFATHOM_CLIENT_ORIGIN'),
        trace: 'on',
        screenshot: 'on',
        video: 'off',
    },

    // Chromium alone, named rather than taken from the device registry, for the reason the pull-request suite names it:
    // what this run installs is what it drives.
    projects: [
        {
            name: 'chromium',
            use: {
                browserName: 'chromium',
                viewport: { width: 1280, height: 720 },
            },
        },
    ],
});
