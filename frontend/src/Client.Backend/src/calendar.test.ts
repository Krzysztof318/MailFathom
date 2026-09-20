// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import {
    acceptCalendarEvent,
    amendCalendarEvent,
    calendarEventRoute,
    deleteCalendarEvent,
    describedAt,
    draftCalendarEvent,
    isStatableReminderLead,
    isStatableReminderSet,
    longestReminderLead,
    mostRemindersOnAnEvent,
    parseCalendarEvent,
    readCalendarEvent,
    readCalendarWindow,
    readsCalendarDescriptions,
    recordCalendarEvent,
} from './calendar';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asserted = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    title: 'Addendum signing',
    start: '2026-09-24T09:00:00+02:00',
    end: '2026-09-24T09:45:00+02:00',
    isAllDay: false,
    reminders: [15],
    remindsAt: ['2026-09-24T08:45:00+02:00'],
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-20T08:00:00+00:00',
    amendedAt: '2026-09-20T08:00:00+00:00',
};

const proposed = {
    ...asserted,
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
    origin: 'Proposed',
    sourceMessage: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92',
};

// The transport is the network boundary and the whole of what a test here fakes. No operation in this module reads a
// header off an answer, so every helper supplies the empty set.
type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

const oneEvent: Answer = { status: 200, body: JSON.stringify(asserted) };

const aWindow = { from: '2026-09-21T00:00:00.000Z', until: '2026-09-28T00:00:00.000Z' };

describe('readCalendarWindow', () => {
    it('asks for the span it was given, the half it was given, and a bound it can render', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ events: [asserted] }) });

        await readCalendarWindow(session, transport, { ...aWindow, origin: 'Proposed', count: 40 });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/calendar' +
                '?from=2026-09-21T00%3A00%3A00.000Z&until=2026-09-28T00%3A00%3A00.000Z&origin=Proposed&count=40',
        );
    });

    it('names no half where the caller asked for both', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ events: [] }) });

        await readCalendarWindow(session, transport, aWindow);

        expect(requests[0]?.path).not.toContain('origin=');
    });

    it('never asks for more events than the deployment answers with', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ events: [] }) });

        await readCalendarWindow(session, transport, { ...aWindow, count: 5000 });

        expect(requests[0]?.path).toContain('count=1000');
    });

    it('reads the events a window answered with', async () => {
        const answer = await readCalendarWindow(
            session,
            answering({ status: 200, body: JSON.stringify({ events: [asserted, proposed] }) }),
            aWindow,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { events: [parseCalendarEvent(asserted), parseCalendarEvent(proposed)] },
        });
    });

    it('refuses a window carrying more events than it asked for', async () => {
        const answer = await readCalendarWindow(
            session,
            answering({ status: 200, body: JSON.stringify({ events: [asserted, proposed] }) }),
            { ...aWindow, count: 1 },
        );

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });

    it('refuses a window in which one event is malformed', async () => {
        const answer = await readCalendarWindow(
            session,
            answering({ status: 200, body: JSON.stringify({ events: [asserted, { id: 'only' }] }) }),
            aWindow,
        );

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });

    it('reports a deployment that answered nothing at all as unavailable', async () => {
        const answer = await readCalendarWindow(session, () => Promise.reject(new Error('refused')), aWindow);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reads a %i as %s', async (status, reason) => {
        const answer = await readCalendarWindow(session, answering({ status, body: '' }), aWindow);

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe(reason);
    });
});

describe('readCalendarEvent', () => {
    it('reads one event out of the calendar', async () => {
        const answer = await readCalendarEvent(session, answering(oneEvent), asserted.id);

        expect(answer.outcome === 'read' && answer.value.title).toBe('Addendum signing');
    });

    it('lets go of an event the calendar no longer holds rather than offering to retry it', async () => {
        const answer = await readCalendarEvent(session, answering({ status: 404, body: '' }), asserted.id);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it('escapes the identity it was given rather than writing it into the path', async () => {
        const { transport, requests } = recording(oneEvent);

        await readCalendarEvent(session, transport, 'a b/c');

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client${calendarEventRoute('a b/c')}`);
    });
});

describe('recordCalendarEvent', () => {
    const record = {
        title: 'Addendum signing',
        start: '2026-09-24T09:00:00+02:00',
        end: null,
        isAllDay: false,
        reminders: [],
        sourceMessage: null,
    };

    it('states the record it was given', async () => {
        const { transport, requests } = recording(oneEvent);

        await recordCalendarEvent(session, transport, record);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.body).toBe(JSON.stringify(record));
    });

    it('answers the event as the calendar now holds it', async () => {
        const answer = await recordCalendarEvent(session, answering(oneEvent), record);

        expect(answer.outcome === 'read' && answer.value.outcome).toBe('Written');
        expect(answer.outcome === 'read' && answer.value.event?.id).toBe(asserted.id);
    });

    it('reads a refused record as an outcome rather than as a failure to retry', async () => {
        const answer = await recordCalendarEvent(session, answering({ status: 400, body: '' }), record);

        expect(answer).toStrictEqual({ outcome: 'read', value: { outcome: 'Refused', event: null } });
    });

    it('refuses an answer that is not an event', async () => {
        const answer = await recordCalendarEvent(session, answering({ status: 200, body: '{}' }), record);

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });

    // The branch every write shares, which is what a status neither the contract nor the refusal above accounts for
    // arrives on: a credential that stopped being one is reported as itself rather than as a deployment that is down.
    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reads %s as a failure of its own kind', async (status, reason) => {
        const answer = await recordCalendarEvent(session, answering({ status, body: '' }), record);

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe(reason);
    });
});

describe('amendCalendarEvent', () => {
    it('states the whole record rather than the part that changed', async () => {
        const { transport, requests } = recording(oneEvent);

        await amendCalendarEvent(session, transport, asserted.id, {
            title: 'Addendum signing, moved',
            start: '2026-09-25T09:00:00+02:00',
            end: null,
            isAllDay: false,
            reminders: [15],
        });

        expect(requests[0]?.method).toBe('PUT');
        expect(requests[0]?.body).toBe(
            JSON.stringify({
                title: 'Addendum signing, moved',
                start: '2026-09-25T09:00:00+02:00',
                end: null,
                isAllDay: false,
                reminders: [15],
            }),
        );
    });

    it('reads an event the calendar no longer holds as an outcome', async () => {
        const answer = await amendCalendarEvent(session, answering({ status: 404, body: '' }), asserted.id, {
            title: 'Gone',
            start: '2026-09-25T09:00:00+02:00',
            end: null,
            isAllDay: false,
            reminders: [],
        });

        expect(answer).toStrictEqual({ outcome: 'read', value: { outcome: 'NotFound', event: null } });
    });
});

describe('acceptCalendarEvent', () => {
    it('takes a proposal onto the calendar and answers with what it now stands as', async () => {
        const answer = await acceptCalendarEvent(session, answering(oneEvent), proposed.id);

        expect(answer.outcome === 'read' && answer.value.event?.origin).toBe('Asserted');
    });

    it('reads a proposal somebody else already took as an outcome rather than as a failure', async () => {
        const answer = await acceptCalendarEvent(session, answering({ status: 409, body: '' }), proposed.id);

        expect(answer).toStrictEqual({ outcome: 'read', value: { outcome: 'AlreadyOnTheCalendar', event: null } });
    });
});

describe('deleteCalendarEvent', () => {
    it('reports an event that was removed', async () => {
        const answer = await deleteCalendarEvent(session, answering({ status: 204, body: '' }), asserted.id);

        expect(answer).toStrictEqual({ outcome: 'read', value: { wasHeld: true } });
    });

    it('reports an event the calendar had already let go of as removed rather than as a failure', async () => {
        const answer = await deleteCalendarEvent(session, answering({ status: 404, body: '' }), asserted.id);

        expect(answer).toStrictEqual({ outcome: 'read', value: { wasHeld: false } });
    });

    it('reports a refusal as one', async () => {
        const answer = await deleteCalendarEvent(session, answering({ status: 403, body: '' }), asserted.id);

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unauthorized');
    });
});

describe('readsCalendarDescriptions', () => {
    it('reads whether this deployment turns a description into an event', async () => {
        const answer = await readsCalendarDescriptions(
            session,
            answering({ status: 200, body: JSON.stringify({ readsDescriptions: true }) }),
        );

        expect(answer).toStrictEqual({ outcome: 'read', value: { readsDescriptions: true } });
    });

    it('refuses an answer that says nothing about descriptions', async () => {
        const answer = await readsCalendarDescriptions(session, answering({ status: 200, body: '{}' }));

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });
});

describe('draftCalendarEvent', () => {
    const described = { description: 'call with Anna on Friday at one', writtenAt: '2026-09-20T18:04:00+02:00' };

    it('sends the sentence and the instant it was typed at in the body rather than in the address', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ drafted: true, title: 'Call with Anna', start: '2026-09-25T13:00:00+02:00' }),
        });

        await draftCalendarEvent(session, transport, described);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/calendar/drafts');
        expect(requests[0]?.body).toBe(JSON.stringify(described));
    });

    it('reads a draft the deployment composed', async () => {
        const answer = await draftCalendarEvent(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    drafted: true,
                    title: 'Call with Anna',
                    start: '2026-09-25T13:00:00+02:00',
                    end: null,
                }),
            }),
            described,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                drafted: true,
                spent: false,
                title: 'Call with Anna',
                start: '2026-09-25T13:00:00+02:00',
                end: null,
            },
        });
    });

    it('reads a deployment that drafted nothing as an answer rather than as a failure', async () => {
        const answer = await draftCalendarEvent(
            session,
            answering({ status: 200, body: JSON.stringify({ drafted: false }) }),
            described,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { drafted: false, spent: false, title: null, start: null, end: null },
        });
    });

    it('reads a spent allowance as its own answer, so the field can say why it stopped working', async () => {
        const answer = await draftCalendarEvent(session, answering({ status: 429, body: '' }), described);

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { drafted: false, spent: true, title: null, start: null, end: null },
        });
    });

    it('refuses a draft that claims to have read something and names no day', async () => {
        const answer = await draftCalendarEvent(
            session,
            answering({ status: 200, body: JSON.stringify({ drafted: true, title: 'Call with Anna' }) }),
            described,
        );

        expect(answer.outcome === 'failed' && answer.failure.reason).toBe('unreadable');
    });
});

// The zone is pinned through Vitest rather than by assigning `process.env` directly, because this package declares no
// ambient Node types at all — `tsconfig.json` sets `types` to nothing, which is what keeps a browser or a runtime
// global out of the wire. `vi.stubEnv` writes the same variable, which the runtime re-reads, and `unstubAllEnvs` puts
// the machine's own zone back so a later file is not run in somebody else's.
describe('describedAt', () => {
    afterEach(() => {
        vi.unstubAllEnvs();
    });

    it('writes the wall clock and the offset of the zone the runtime reports', () => {
        vi.stubEnv('TZ', 'Europe/Warsaw');

        expect(describedAt(new Date('2026-09-20T16:04:09Z'))).toBe('2026-09-20T18:04:09+02:00');
    });

    it('writes a zone behind Greenwich with its own sign', () => {
        vi.stubEnv('TZ', 'America/New_York');

        expect(describedAt(new Date('2026-09-20T16:04:09Z'))).toBe('2026-09-20T12:04:09-04:00');
    });

    it('writes an offset that is not a whole number of hours', () => {
        vi.stubEnv('TZ', 'Asia/Kolkata');

        expect(describedAt(new Date('2026-09-20T16:04:09Z'))).toBe('2026-09-20T21:34:09+05:30');
    });
});

describe('parseCalendarEvent', () => {
    it('reads an event that names the message it came out of', () => {
        expect(parseCalendarEvent(proposed)?.sourceMessage).toBe(proposed.sourceMessage);
    });

    it('reads an event that states no end as one with no end', () => {
        expect(parseCalendarEvent({ ...asserted, end: null })?.end).toBeNull();
    });

    it.each([
        ['nothing at all', null],
        ['an array', []],
        ['an origin this surface does not publish', { ...asserted, origin: 'Invented' }],
        ['a title that is not text', { ...asserted, title: 7 }],
        ['no beginning', { ...asserted, start: undefined }],
        ['an end that is not text', { ...asserted, end: 7 }],
        ['a source message that is not text', { ...asserted, sourceMessage: 7 }],
        ['no identity', { ...asserted, id: undefined }],
        ['no statement of whether it is a day', { ...asserted, isAllDay: undefined }],
        ['reminders that are not a list', { ...asserted, reminders: 15 }],
        ['a reminder further ahead than one may state', { ...asserted, reminders: [longestReminderLead + 1] }],
        [
            'more reminders than an event may carry',
            { ...asserted, reminders: Array.from({ length: mostRemindersOnAnEvent + 1 }, (_, at) => at) },
        ],
        ['one lead stated twice', { ...asserted, reminders: [15, 15] }],
        ['instants for its reminders that are not text', { ...asserted, remindsAt: [7] }],
    ])('refuses an answer carrying %s', (_, value) => {
        expect(parseCalendarEvent(value)).toBeNull();
    });
});

describe('isStatableReminderLead', () => {
    it('accepts the event itself and the longest lead, which are the two ends the deployment allows', () => {
        expect(isStatableReminderLead(0)).toBe(true);
        expect(isStatableReminderLead(longestReminderLead)).toBe(true);
    });

    it.each([-1, longestReminderLead + 1, 1.5, Number.NaN, Number.POSITIVE_INFINITY])(
        'refuses %s, which no reminder may state',
        (minutesBefore) => {
            expect(isStatableReminderLead(minutesBefore)).toBe(false);
        },
    );
});

describe('isStatableReminderSet', () => {
    it('accepts an event that announces nothing, which is a decision rather than an omission', () => {
        expect(isStatableReminderSet([])).toBe(true);
    });

    it('accepts as many as an event may carry and refuses one more', () => {
        const full = Array.from({ length: mostRemindersOnAnEvent }, (_, at) => at + 1);

        expect(isStatableReminderSet(full)).toBe(true);
        expect(isStatableReminderSet([...full, 0])).toBe(false);
    });

    it('refuses one lead stated twice, because asking for it twice asked for one thing', () => {
        expect(isStatableReminderSet([15, 15])).toBe(false);
    });

    it('refuses a set carrying a lead no reminder may state', () => {
        expect(isStatableReminderSet([15, longestReminderLead + 1])).toBe(false);
    });
});
