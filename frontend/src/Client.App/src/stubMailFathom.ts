// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { clientRoutePrefix, mailAccountsRoute, type MailFathomTransport } from '@mailfathom/client-backend';

// What the proving screen reads its mail through. The deployment the client belongs to is resolved for real now, and
// reaching one is a real request; what is still canned here is everything behind a credential, because signing in is
// separate work and nothing composes a credential yet. This module goes when that work lands.
//
// The answer is matched on the route rather than on a whole address, because which deployment the client is pointed at
// is no longer this module's to know.

export const stubAuthorization = 'Basic stub-credential-that-is-never-sent';

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
        request.path.endsWith(`${clientRoutePrefix}${mailAccountsRoute}`)
            ? { status: 200, body: stubAccounts, headers: {} }
            : { status: 404, body: '', headers: {} },
    );
