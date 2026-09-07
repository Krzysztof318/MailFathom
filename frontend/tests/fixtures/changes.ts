// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// A change travels in two directions on this surface, and this file holds both ends of it.
//
// Outward: the client submits a batch of flag or folder changes and the deployment writes down a record per value,
// which is what the mutation answers below are. Inward: the deployment says something changed while somebody had the
// client open, which is what the signal payloads below are. They sit together because a screen reads them against each
// other — a row that has just been marked read locally and a `mail.changed` naming the same message are the same event
// arriving twice, and a fixture stating one without the other could not be looked at in that state.

import { markupOnlyId, newsletterId } from './messages';

/** What a submitted batch of flag changes answers with: one result per message, and a record per value inside it. */
export const flagsRecorded = {
    results: [
        {
            storedEmailId: newsletterId,
            outcome: 'recorded',
            detail: null,
            changes: [
                { mutation: 'set-seen', recordId: '00000000-0000-4000-8000-0000000000a1', state: 'pending' },
                { mutation: 'add-keywords', recordId: '00000000-0000-4000-8000-0000000000a2', state: 'pending' },
            ],
        },
    ],
};

/**
 * What a batch answers with when one of its messages did not apply.
 *
 * A refusal about one message is that message's own result rather than the request's, so a batch composed from a list
 * that has moved on since the screen drew it reports exactly which entries did not apply and writes the rest down —
 * which is the state a screen has to be looked at in and the one a fixture of nothing but successes never reaches.
 */
export const movesPartlyRecorded = {
    results: [
        {
            storedEmailId: newsletterId,
            outcome: 'recorded',
            detail: null,
            changes: [{ mutation: 'relocate', recordId: '00000000-0000-4000-8000-0000000000a3', state: 'pending' }],
        },
        { storedEmailId: markupOnlyId, outcome: 'message-not-found', detail: null, changes: [] },
    ],
};

/**
 * Where the caller's own changes stand, read back.
 *
 * The three rows are the three answers a screen says something different about: one still converging against a mailbox
 * that is being retried, one that reached the server, and one whose placement command went out and whose answer never
 * came back — the last being the only field here a person acts on rather than waits through.
 */
export const mutationRecords = {
    changes: [
        {
            recordId: '00000000-0000-4000-8000-0000000000a1',
            storedEmailId: newsletterId,
            mutation: 'set-seen',
            state: 'converging',
            outcomeUnknown: false,
            attemptCount: 2,
            lastFailure: 22001,
            recordedAt: '2026-08-31T09:00:00+00:00',
            stateChangedAt: '2026-08-31T09:04:00+00:00',
        },
        {
            recordId: '00000000-0000-4000-8000-0000000000a2',
            storedEmailId: newsletterId,
            mutation: 'add-keywords',
            state: 'completed',
            outcomeUnknown: false,
            attemptCount: 1,
            lastFailure: null,
            recordedAt: '2026-08-31T09:00:00+00:00',
            stateChangedAt: '2026-08-31T09:01:00+00:00',
        },
        {
            recordId: '00000000-0000-4000-8000-0000000000a3',
            storedEmailId: newsletterId,
            mutation: 'relocate',
            state: 'converging',
            outcomeUnknown: true,
            attemptCount: 3,
            lastFailure: 22001,
            recordedAt: '2026-08-31T09:02:00+00:00',
            stateChangedAt: '2026-08-31T09:06:00+00:00',
        },
    ],
};

/** The ticket one signal connection is opened against, and when presenting it stops working. */
export const signalTicket = {
    ticket: '00000000-0000-4000-8000-0000000000b0',
    expiresAt: '2026-08-31T10:41:00+00:00',
};

/**
 * The five payloads a deployment sends over the channel, one of each kind.
 *
 * A signal is an instruction to look again rather than something to keep, so nothing here carries mail: what each says
 * is which folder, which mailbox, or which messages to re-read over the routes the client already reads.
 */
export const signals = {
    mailArrived: { kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 3 },
    mailChanged: { kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: [newsletterId, markupOnlyId] },
    foldersChanged: { kind: 'folders.changed', account: 'work' },
    notificationRaised: {
        kind: 'notification.raised',
        notificationKind: 'Mail',
        headline: 'Nordwind wrote about the racking quote',
        secondLine: 'Booked for the ninth. The crew will need the yard for a morning.',
        count: 2,
    },
    accountState: { kind: 'account.state', account: 'club' },
};
