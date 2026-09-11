// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    draftMailReply,
    draftsMailReplies,
    longestDraftInstruction,
    longestDraftSelection,
    mailReplyDraftingRoute,
} from './mailReplyDrafting';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const answered = '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d';

const cited = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';

function draftBody(draft: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        drafted: true,
        body: 'We accept the two-hour response time, with a 5% cap on indexation.',
        claims: [
            {
                text: 'The response time is two hours.',
                supported: true,
                sources: [{ kind: 'email', email: cited }],
            },
            { text: 'The cap is 5% a year.', supported: false, sources: [] },
        ],
        proposedRecipients: [{ address: 'karolina@example.test', displayName: 'Karolina' }],
        ...draft,
    });
}

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

function request(overrides: Readonly<Record<string, unknown>> = {}) {
    return { answeredEmailId: answered, selection: null, instruction: null, ...overrides };
}

describe('draftsMailReplies', () => {
    it('reads a deployment that drafts one', async () => {
        const result = await draftsMailReplies(
            session,
            answering({ status: 200, body: JSON.stringify({ draftsReplies: true }) }),
        );

        expect(result).toStrictEqual({ outcome: 'read', value: true });
    });

    it('reads a deployment that drafts none as a value rather than as a failure', async () => {
        const result = await draftsMailReplies(
            session,
            answering({ status: 200, body: JSON.stringify({ draftsReplies: false }) }),
        );

        expect(result).toStrictEqual({ outcome: 'read', value: false });
    });

    it('reports a grant that does not carry asking', async () => {
        const result = await draftsMailReplies(session, answering({ status: 403, body: '' }));

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('refuses an answer that does not say whether replies are drafted', async () => {
        const result = await draftsMailReplies(session, answering({ status: 200, body: JSON.stringify({}) }));

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('draftMailReply', () => {
    it('reads the draft, its claims, and who it proposes to reach', async () => {
        const result = await draftMailReply(session, answering({ status: 200, body: draftBody() }), request());

        expect(result).toStrictEqual({
            outcome: 'read',
            value: {
                outcome: 'drafted',
                body: 'We accept the two-hour response time, with a 5% cap on indexation.',
                claims: [
                    {
                        text: 'The response time is two hours.',
                        supported: true,
                        sources: [{ kind: 'email', email: cited }],
                    },
                    { text: 'The cap is 5% a year.', supported: false, sources: [] },
                ],
                proposedRecipients: [{ address: 'karolina@example.test', displayName: 'Karolina' }],
            },
        });
    });

    it('posts the message being answered and the two texts beside it', async () => {
        const { transport, requests } = recording({ status: 200, body: draftBody() });

        await draftMailReply(
            session,
            transport,
            request({ selection: 'the 2 h SLA', instruction: 'Ask for a 5% cap.' }),
        );

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`${session.baseAddress}/api/client${mailReplyDraftingRoute}`);
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            answeredEmailId: answered,
            selection: 'the 2 h SLA',
            instruction: 'Ask for a 5% cap.',
        });
    });

    it('names no message where the composer answers none', async () => {
        const { transport, requests } = recording({ status: 200, body: draftBody() });

        await draftMailReply(
            session,
            transport,
            request({ answeredEmailId: null, instruction: 'Ask Contoso for a 5% cap.' }),
        );

        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            answeredEmailId: null,
            selection: null,
            instruction: 'Ask Contoso for a 5% cap.',
        });
    });

    it('shortens a typed text to the bound the deployment reads rather than having it refused', async () => {
        const { transport, requests } = recording({ status: 200, body: draftBody() });

        await draftMailReply(
            session,
            transport,
            request({
                selection: 's'.repeat(longestDraftSelection + 10),
                instruction: 'i'.repeat(longestDraftInstruction + 10),
            }),
        );

        const sent = JSON.parse(requests[0]?.body ?? '') as { selection: string; instruction: string };

        expect(sent.selection).toHaveLength(longestDraftSelection);
        expect(sent.instruction).toHaveLength(longestDraftInstruction);
    });

    it('sends nothing at all for a text that is only whitespace', async () => {
        const { transport, requests } = recording({ status: 200, body: draftBody() });

        await draftMailReply(session, transport, request({ selection: '   ', instruction: '\n' }));

        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            answeredEmailId: answered,
            selection: null,
            instruction: null,
        });
    });

    it('reads a deployment that drafted nothing as the composer as it was', async () => {
        const result = await draftMailReply(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ drafted: false, body: '', claims: [], proposedRecipients: [] }),
            }),
            request(),
        );

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'notDrafted' } });
    });

    it('reads a message the deployment no longer holds as the composer as it was', async () => {
        const result = await draftMailReply(session, answering({ status: 404, body: '' }), request());

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'notDrafted' } });
    });

    it('tells a spent allowance apart from a deployment that drafts none', async () => {
        const result = await draftMailReply(session, answering({ status: 429, body: '' }), request());

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'allowanceSpent' } });
    });

    it('reports a deployment nothing could be sent to', async () => {
        const unreachable: MailFathomTransport = () => Promise.reject(new TypeError('Failed to fetch'));
        const result = await draftMailReply(session, unreachable, request());

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('reports a refused credential', async () => {
        const result = await draftMailReply(session, answering({ status: 401, body: '' }), request());

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });

    it('refuses a claim whose support the deployment did not state', async () => {
        const result = await draftMailReply(
            session,
            answering({
                status: 200,
                body: draftBody({ claims: [{ text: 'The cap is 5%.', sources: [] }] }),
            }),
            request(),
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a proposed recipient with no address to send to', async () => {
        const result = await draftMailReply(
            session,
            answering({ status: 200, body: draftBody({ proposedRecipients: [{ displayName: 'Karolina' }] }) }),
            request(),
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a draft the deployment said it wrote and then did not', async () => {
        const result = await draftMailReply(
            session,
            answering({ status: 200, body: draftBody({ body: '' }) }),
            request(),
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that is not JSON at all', async () => {
        const result = await draftMailReply(session, answering({ status: 200, body: 'not json' }), request());

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
