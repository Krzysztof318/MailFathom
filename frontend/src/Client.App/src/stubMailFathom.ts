// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { mailAccountsRoute, routeFor, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';

// What the proving screen reads through. Signing in and reaching a running deployment are separate work, so the
// transport answers from a canned body here: it is what proves the workspace, the package boundary, the build, and the
// styling end to end without a service behind it.

export const stubSession: ClientSession = {
    baseAddress: 'https://mailfathom.invalid',
    authorization: 'Basic stub-credential-that-is-never-sent',
};

const stubAccounts = JSON.stringify({
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'work',
            displayName: 'Work',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
            behind: false,
        },
        {
            id: 'archive',
            displayName: 'Archive',
            synchronizationState: 'Unreachable',
            lastSynchronizedAt: '2026-08-28T18:02:00+00:00',
            behind: true,
        },
        {
            id: 'personal',
            displayName: 'Personal',
            synchronizationState: 'NeverSynchronized',
            lastSynchronizedAt: null,
            behind: false,
        },
    ],
});

export const stubTransport: MailFathomTransport = (request) =>
    Promise.resolve(
        request.path === routeFor(stubSession, mailAccountsRoute)
            ? { status: 200, body: stubAccounts }
            : { status: 404, body: '' },
    );
