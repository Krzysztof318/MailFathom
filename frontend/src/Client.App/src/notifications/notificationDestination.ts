// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { NotificationScreen, NotificationTarget } from '@mailfathom/client-backend';
import type { Space } from '../routing/spaces';

// Where a notification leads. The deployment says what a notification is *about*, and this says what this client can
// do about it — which is two things and no more: open the mail it names, or go to a space. It is a function rather
// than a branch inside the frame because it is the one place the two vocabularies meet, and because a mapping nothing
// can call on its own is a mapping nothing can be asserted about either.
//
// **A target this client cannot open leaves the reader where they were.** A screen no space here answers for is a
// stage that has not shipped, and a notification about nothing is one that was never a place to go: neither is a
// failure to report, so neither moves anybody. The panel closing is the reader's own act and happens either way.

/** What the client can do about a target, which is what the frame supplies and what a test stands in for. */
export interface NotificationDestinations {
    /** Opens one message, which is where a notification about mail leads. */
    readonly openMail: (storedEmailId: string) => void;

    /** Goes to a space, which is where a notification about a screen leads when this client has that space. */
    readonly goTo: (space: Space) => void;
}

// Exhaustive by its own type, so a screen the service adds fails to compile here until somebody has decided where it
// leads. `Settings` is reached from the account menu rather than from an address, so it has none — which is the
// stage-that-has-not-shipped case above rather than an omission.
const screenSpaces: Readonly<Record<NotificationScreen, Space | null>> = {
    Mail: 'mail',
    Settings: null,
};

export function followTarget(target: NotificationTarget, client: NotificationDestinations): void {
    if (target.kind === 'Message') {
        client.openMail(target.storedEmailId);

        return;
    }

    if (target.kind !== 'Screen') {
        return;
    }

    const space = screenSpaces[target.screen];

    if (space !== null) {
        client.goTo(space);
    }
}
