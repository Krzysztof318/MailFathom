// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientSession } from './session';
import {
    acceptTask,
    arrangesDays,
    eraseTask,
    layOutToday,
    longestTaskTitle,
    parseTask,
    readOwnTasks,
    readProposedTasks,
    recordTask,
    setTaskCompletion,
} from './tasks';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asserted = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    title: 'Answer the counter-proposal',
    dueOn: '2026-09-21',
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
};

const proposed = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
    title: 'Send the signed annex',
    dueOn: null,
    origin: 'Proposed',
    completed: false,
    sourceMessageId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92',
};

const pageBody = JSON.stringify({ tasks: [asserted], nextCursor: 'page-two' });

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

describe('readOwnTasks', () => {
    it('asks for the committed half with the window it will render', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks?pageSize=50');
    });

    it('carries a cursor into the request that continues the walk', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport, { cursor: 'page two/first' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/tasks?pageSize=50&cursor=page%20two%2Ffirst',
        );
    });

    it('never asks for more than the bound the route enforces', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport, { pageSize: 5000 });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks?pageSize=200');
    });

    it('reads the page and where the walk continues', async () => {
        const answer = await readOwnTasks(session, answering({ status: 200, body: pageBody }));

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                tasks: [
                    {
                        id: asserted.id,
                        title: 'Answer the counter-proposal',
                        dueOn: '2026-09-21',
                        origin: 'Asserted',
                        completed: false,
                        sourceMessageId: null,
                    },
                ],
                nextCursor: 'page-two',
            },
        });
    });

    it('reads the end of the half as a walk with nowhere left to go', async () => {
        const answer = await readOwnTasks(
            session,
            answering({ status: 200, body: JSON.stringify({ tasks: [], nextCursor: null }) }),
        );

        expect(answer.outcome === 'read' && answer.value.nextCursor).toBeNull();
    });

    it('refuses a page holding more tasks than it asked for', async () => {
        const overlong = JSON.stringify({
            tasks: Array.from({ length: 3 }, () => asserted),
            nextCursor: null,
        });

        const answer = await readOwnTasks(session, answering({ status: 200, body: overlong }), { pageSize: 2 });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ])('reads a %i as %s', async (status, reason) => {
        const answer = await readOwnTasks(session, answering({ status, body: '' }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reads a deployment that did not answer at all as unavailable', async () => {
        const answer = await readOwnTasks(session, () => Promise.reject(new Error('no route to host')));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('readProposedTasks', () => {
    it('asks the half of its own rather than the committed one with a filter', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ tasks: [proposed], nextCursor: null }),
        });

        await readProposedTasks(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks/proposed?pageSize=50');
    });

    it('reads the message a proposal was taken out of', async () => {
        const answer = await readProposedTasks(
            session,
            answering({ status: 200, body: JSON.stringify({ tasks: [proposed], nextCursor: null }) }),
        );

        expect(answer.outcome === 'read' && answer.value.tasks[0]?.sourceMessageId).toBe(proposed.sourceMessageId);
    });
});

describe('recordTask', () => {
    it('states the line, the day and the message the task cites', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await recordTask(session, transport, {
            title: 'Answer the counter-proposal',
            dueOn: '2026-09-21',
            sourceMessageId: null,
        });

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks');
        expect(requests[0]?.body).toBe(
            JSON.stringify({ title: 'Answer the counter-proposal', dueOn: '2026-09-21', sourceMessageId: null }),
        );
    });

    it('reads the task as it was written', async () => {
        const answer = await recordTask(session, answering({ status: 200, body: JSON.stringify(asserted) }), {
            title: 'Answer the counter-proposal',
            dueOn: '2026-09-21',
            sourceMessageId: null,
        });

        expect(answer.outcome === 'read' && answer.value.origin).toBe('Asserted');
    });

    it('reads a refusal of what it stated as a body this client could not use', async () => {
        const answer = await recordTask(session, answering({ status: 400, body: '' }), {
            title: '',
            dueOn: null,
            sourceMessageId: null,
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 400 } });
    });

    it('reads a 404 as the deployment being out of reach, this route naming no task to be gone', async () => {
        const answer = await recordTask(session, answering({ status: 404, body: '' }), {
            title: 'Answer the counter-proposal',
            dueOn: null,
            sourceMessageId: null,
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 404 } });
    });
});

describe('setTaskCompletion', () => {
    it('states the completion on the one route that carries both directions', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ ...asserted, completed: true }),
        });

        await setTaskCompletion(session, transport, asserted.id, true);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/tasks/${asserted.id}/completion`);
        expect(requests[0]?.body).toBe(JSON.stringify({ completed: true }));
    });

    it('reads a task the list no longer holds as missing rather than as the deployment being down', async () => {
        const answer = await setTaskCompletion(session, answering({ status: 404, body: '' }), asserted.id, true);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('acceptTask', () => {
    it('takes a proposal on at the acceptance route and states no body', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ ...proposed, origin: 'Asserted' }),
        });

        await acceptTask(session, transport, proposed.id);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/tasks/${proposed.id}/acceptance`);
        expect(requests[0]?.body).toBeUndefined();
    });

    it('reads the task as one the person now owes', async () => {
        const answer = await acceptTask(
            session,
            answering({ status: 200, body: JSON.stringify({ ...proposed, origin: 'Asserted' }) }),
            proposed.id,
        );

        expect(answer.outcome === 'read' && answer.value.origin).toBe('Asserted');
    });
});

describe('eraseTask', () => {
    it('reads what the erasure removed', async () => {
        const answer = await eraseTask(
            session,
            answering({ status: 200, body: JSON.stringify({ id: asserted.id, erased: true }) }),
            asserted.id,
        );

        expect(answer).toEqual({ outcome: 'read', value: { id: asserted.id, erased: true } });
    });

    it('reads an erasure that removed nothing as an answer rather than as a failure', async () => {
        const answer = await eraseTask(
            session,
            answering({ status: 200, body: JSON.stringify({ id: asserted.id, erased: false }) }),
            asserted.id,
        );

        expect(answer.outcome === 'read' && answer.value.erased).toBe(false);
    });

    it('refuses an answer that is not an erasure', async () => {
        const answer = await eraseTask(session, answering({ status: 200, body: '{"id":1}' }), asserted.id);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('arrangesDays', () => {
    it('reads whether this deployment arranges a day at all', async () => {
        const answer = await arrangesDays(
            session,
            answering({ status: 200, body: JSON.stringify({ arrangesDays: true }) }),
        );

        expect(answer).toEqual({ outcome: 'read', value: true });
    });

    it('reads a deployment that arranges none', async () => {
        const answer = await arrangesDays(
            session,
            answering({ status: 200, body: JSON.stringify({ arrangesDays: false }) }),
        );

        expect(answer).toEqual({ outcome: 'read', value: false });
    });

    it('refuses an answer saying nothing about whether days are arranged', async () => {
        const answer = await arrangesDays(session, answering({ status: 200, body: '{}' }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('layOutToday', () => {
    const day = { from: '2026-09-21T07:00:00+02:00', until: '2026-09-21T18:00:00+02:00' };

    const arranged = JSON.stringify({
        arranged: true,
        placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 45 }],
        notToday: [proposed.id],
    });

    it('states the window the reader’s own client drew', async () => {
        const { transport, requests } = recording({ status: 200, body: arranged });

        await layOutToday(session, transport, day);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks/today/layout');
        expect(requests[0]?.body).toBe(JSON.stringify({ from: day.from, until: day.until }));
    });

    it('reads the arrangement and what does not fit the day', async () => {
        const answer = await layOutToday(session, answering({ status: 200, body: arranged }), day);

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                outcome: 'Arranged',
                placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 45 }],
                notToday: [proposed.id],
            },
        });
    });

    it('reads a deployment that composed no arrangement', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ arranged: false, placements: [], notToday: [] }),
            }),
            day,
        );

        expect(answer.outcome === 'read' && answer.value.outcome).toBe('NotArranged');
    });

    it('reads a spent provider allowance as an answer the screen says something about', async () => {
        const answer = await layOutToday(session, answering({ status: 429, body: '' }), day);

        expect(answer).toEqual({
            outcome: 'read',
            value: { outcome: 'AllowanceSpent', placements: [], notToday: [] },
        });
    });

    it('refuses a placement stating no duration', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    arranged: true,
                    placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 0 }],
                    notToday: [],
                }),
            }),
            day,
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an arrangement naming something other than a task', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ arranged: true, placements: [], notToday: [7] }),
            }),
            day,
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('parseTask', () => {
    it('reads a task nobody has said a day for', () => {
        expect(parseTask({ ...asserted, dueOn: null })?.dueOn).toBeNull();
    });

    it.each([
        ['an origin the surface does not publish', { ...asserted, origin: 'Invented' }],
        ['a completion that is not a flag', { ...asserted, completed: 'no' }],
        ['a day that is not written down', { ...asserted, dueOn: 20260921 }],
        ['a citation that is not an identity', { ...asserted, sourceMessageId: 12 }],
        ['a line past the bound the deployment states', { ...asserted, title: 'x'.repeat(longestTaskTitle + 1) }],
        ['an array where a record belongs', []],
        ['nothing at all', null],
    ])('refuses %s', (_, value) => {
        expect(parseTask(value)).toBeNull();
    });
});
