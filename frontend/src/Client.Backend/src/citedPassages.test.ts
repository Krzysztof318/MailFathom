// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mostCitedPassages, readCitedPassages } from './citedPassages';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';

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

function resolving(citations: readonly unknown[]): Answer {
    return { status: 200, body: JSON.stringify({ citations }) };
}

describe('readCitedPassages', () => {
    it('asks the citation route for one fragment of this message per passage', async () => {
        const { transport, requests } = recording(
            resolving([
                { outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: 0, text: 'The figures.' } },
                { outcome: 'Resolved', fragment: { fragmentId: 'second', ordinal: 3, text: 'By Friday.' } },
            ]),
        );

        await readCitedPassages(session, transport, storedEmailId, ['first', 'second']);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/citations/resolution');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(requests[0]?.headers['Content-Type']).toBe('application/json');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            citations: [
                { kind: 'fragment', email: storedEmailId, fragment: 'first' },
                { kind: 'fragment', email: storedEmailId, fragment: 'second' },
            ],
        });
    });

    it('pairs each resolution with the passage that position asked about', async () => {
        const answer = await readCitedPassages(
            session,
            answering(
                resolving([
                    { outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: 0, text: 'The figures.' } },
                    { outcome: 'Resolved', fragment: { fragmentId: 'second', ordinal: 3, text: 'By Friday.' } },
                ]),
            ),
            storedEmailId,
            ['first', 'second'],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [
                { passage: 'first', outcome: 'Resolved', ordinal: 0, text: 'The figures.' },
                { passage: 'second', outcome: 'Resolved', ordinal: 3, text: 'By Friday.' },
            ],
        });
    });

    it('keeps a passage that has moved apart from a message this sign-in may not read', async () => {
        const answer = await readCitedPassages(
            session,
            answering(resolving([{ outcome: 'Unresolvable' }, { outcome: 'PrivateSource', fragment: null }])),
            storedEmailId,
            ['first', 'second'],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [
                { passage: 'first', outcome: 'Unresolvable', ordinal: null, text: null },
                { passage: 'second', outcome: 'PrivateSource', ordinal: null, text: null },
            ],
        });
    });

    it('reads a passage the message holds as empty words rather than as nothing', async () => {
        const answer = await readCitedPassages(
            session,
            answering(resolving([{ outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: 0, text: '' } }])),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: [{ passage: 'first', outcome: 'Resolved', ordinal: 0, text: '' }],
        });
    });

    it('sends nothing where there is no passage to follow', async () => {
        const { transport, requests } = recording(resolving([]));

        const answer = await readCitedPassages(session, transport, storedEmailId, []);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: null } });
        expect(requests).toHaveLength(0);
    });

    it('sends nothing where more passages are named than the route follows at once', async () => {
        const { transport, requests } = recording(resolving([]));
        const passages = Array.from({ length: mostCitedPassages + 1 }, (_unused, at) => `fragment-${String(at)}`);

        const answer = await readCitedPassages(session, transport, storedEmailId, passages);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: null } });
        expect(requests).toHaveLength(0);
    });

    it('reports a deployment that did not answer as unreachable', async () => {
        const answer = await readCitedPassages(
            session,
            () => Promise.reject(new Error('the name does not resolve')),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('reads what a refused status stands for', async () => {
        const answer = await readCitedPassages(session, answering({ status: 403, body: '' }), storedEmailId, ['first']);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('refuses a body that is not an answer at all', async () => {
        const answer = await readCitedPassages(session, answering({ status: 200, body: 'not json' }), storedEmailId, [
            'first',
        ]);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer holding a different number of resolutions than were asked for', async () => {
        const answer = await readCitedPassages(
            session,
            answering(resolving([{ outcome: 'Unresolvable' }])),
            storedEmailId,
            ['first', 'second'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that names a passage other than the one that position asked about', async () => {
        const answer = await readCitedPassages(
            session,
            answering(
                resolving([{ outcome: 'Resolved', fragment: { fragmentId: 'second', ordinal: 0, text: 'Words.' } }]),
            ),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses words attached to an outcome that resolved to none, rather than drawing them', async () => {
        const attached = {
            outcome: 'PrivateSource',
            fragment: { fragmentId: 'first', ordinal: 0, text: 'What this sign-in may not read.' },
        };

        const answer = await readCitedPassages(session, answering(resolving([attached])), storedEmailId, ['first']);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a resolution that says it resolved and names no passage', async () => {
        const answer = await readCitedPassages(
            session,
            answering(resolving([{ outcome: 'Resolved', fragment: null }])),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an outcome this client does not know, rather than drawing a conclusion from it', async () => {
        const answer = await readCitedPassages(
            session,
            answering(resolving([{ outcome: 'Redacted' }])),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a resolved passage that does not say where in the message it sits', async () => {
        const answers = [
            { outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: -1, text: 'Words.' } },
            { outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: 0.5, text: 'Words.' } },
            { outcome: 'Resolved', fragment: { fragmentId: 'first', text: 'Words.' } },
        ];

        for (const one of answers) {
            const answer = await readCitedPassages(session, answering(resolving([one])), storedEmailId, ['first']);

            expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
        }
    });

    it('refuses a passage larger than one chunk of a message', async () => {
        const answer = await readCitedPassages(
            session,
            answering(
                resolving([
                    { outcome: 'Resolved', fragment: { fragmentId: 'first', ordinal: 0, text: 'a'.repeat(8_193) } },
                ]),
            ),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer whose resolutions are not records', async () => {
        const answer = await readCitedPassages(session, answering(resolving(['Resolved'])), storedEmailId, ['first']);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that holds no citations at all', async () => {
        const answer = await readCitedPassages(
            session,
            answering({ status: 200, body: JSON.stringify({ citations: 'none' }) }),
            storedEmailId,
            ['first'],
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
