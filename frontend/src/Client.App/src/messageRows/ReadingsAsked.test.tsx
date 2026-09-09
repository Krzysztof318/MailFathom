// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailEnrichmentMark,
    MailFathomTransport,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingsAsked, type AskedReadings } from './ReadingsAsked';

// What the harness's own control says, held as a value rather than written into the markup: a literal there is a
// user-visible string, and the rule that refuses one does not know this button belongs to a test.
const opening = 'Check';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const commitment: MailEnrichmentMark = {
    aspect: 'Commitment',
    text: 'An answer is owed by Friday.',
    reason: 'The sender asks for confirmation before the end of the week.',
    dueAt: '2026-09-04T16:00:00+00:00',
    source: 'Model',
    origin: 'agents/reader',
    evidence: ['fragment-1', 'fragment-2'],
};

const sense: MailEnrichmentMark = {
    aspect: 'Sense',
    text: 'An invoice for August.',
    reason: 'The message attaches a document the sender calls an invoice.',
    dueAt: null,
    source: 'DeterministicRule',
    origin: 'rules/invoice',
    evidence: [],
};

function asking(marks: readonly MailEnrichmentMark[]): AskedReadings {
    return { storedEmailId: 'message-1', subject: 'Contract annex — signatures', marks };
}

function resolving(citations: readonly unknown[]): ClientResponse {
    return { status: 200, body: JSON.stringify({ citations }), headers: {} };
}

function Checking({ asked, transport }: { readonly asked: AskedReadings; readonly transport: MailFathomTransport }) {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <ReadingsAsked asked={asked} session={session} transport={transport} dialog={dialog} />
        </>
    );
}

function check(asked: AskedReadings, transport: MailFathomTransport): void {
    render(
        <LocalizationProvider>
            <Checking asked={asked} transport={transport} />
        </LocalizationProvider>,
    );

    act(() => {
        fireEvent.click(screen.getByRole('button', { name: opening }));
    });
}

function answering(...responses: readonly (ClientResponse | null)[]): MailFathomTransport {
    return recording(...responses).transport;
}

function recording(...responses: readonly (ClientResponse | null)[]): {
    transport: MailFathomTransport;
    requests: ClientRequest[];
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const response = responses[requests.length - 1];

            // A transport answers or rejects, exactly as the real one does: nothing reached is a rejected promise
            // rather than an absent answer, which is what `send` reads as a deployment that could not be reached.
            return response === undefined || response === null
                ? Promise.reject(new Error('the name does not resolve'))
                : Promise.resolve(response);
        },
    };
}

describe('ReadingsAsked', () => {
    it('draws every reading of the message, in the order it was handed them', async () => {
        check(
            asking([commitment, sense]),
            answering(resolving([{ outcome: 'Unresolvable' }, { outcome: 'Unresolvable' }])),
        );

        expect(screen.getAllByRole('heading', { level: 3 }).map((one) => one.textContent)).toStrictEqual([
            'What is committed to',
            'What this is about',
        ]);
        expect(await screen.findByText('An answer is owed by Friday.')).toBeTruthy();
        expect(screen.getByText('An invoice for August.')).toBeTruthy();
    });

    it('names what a reading is about the message rather than of it', () => {
        check(asking([sense]), answering());

        expect(screen.getByRole('dialog', { name: 'What MailFathom made of this message' })).toBeTruthy();
        expect(screen.getByText('Contract annex — signatures')).toBeTruthy();
    });

    it('says a model produced a reading in words rather than only in a colour', () => {
        check(
            asking([commitment, sense]),
            answering(resolving([{ outcome: 'Unresolvable' }, { outcome: 'Unresolvable' }])),
        );

        expect(screen.getByText('A model — agents/reader')).toBeTruthy();
        expect(screen.getByText('A rule of this deployment — rules/invoice')).toBeTruthy();
    });

    it('says why the producer says it, so the reading can be disagreed with', () => {
        check(asking([sense]), answering());

        expect(screen.getByText('Why: The message attaches a document the sender calls an invoice.')).toBeTruthy();
    });

    it('says when a commitment falls due', () => {
        check(asking([commitment]), answering(resolving([{ outcome: 'Unresolvable' }, { outcome: 'Unresolvable' }])));

        expect(screen.getByText(/^Due /)).toBeTruthy();
    });

    it('says a reading names no passage rather than drawing an empty list under it', () => {
        check(asking([sense]), answering());

        expect(screen.getByText('This reading names no passage.')).toBeTruthy();
    });

    it('asks nothing of the deployment where no reading rests on a passage', () => {
        const { transport, requests } = recording();

        check(asking([sense]), transport);

        expect(requests).toHaveLength(0);
    });

    it('follows the whole message’s evidence in one request', async () => {
        const { transport, requests } = recording(
            resolving([
                { outcome: 'Resolved', fragment: { fragmentId: 'fragment-1', ordinal: 0, text: 'Please confirm.' } },
                { outcome: 'Resolved', fragment: { fragmentId: 'fragment-2', ordinal: 4, text: 'Before Friday.' } },
            ]),
        );

        check(asking([commitment, sense]), transport);

        expect(await screen.findByText('Please confirm.')).toBeTruthy();
        expect(screen.getByText('Before Friday.')).toBeTruthy();
        expect(requests).toHaveLength(1);
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            citations: [
                { kind: 'fragment', email: 'message-1', fragment: 'fragment-1' },
                { kind: 'fragment', email: 'message-1', fragment: 'fragment-2' },
            ],
        });
    });

    it('says it is reading the passages while the request is in flight', () => {
        check(asking([commitment]), answering(resolving([])));

        expect(screen.getByRole('status').textContent).toBe('Reading the passages this rests on…');
    });

    it('keeps a passage that has moved apart from one this sign-in may not read', async () => {
        check(asking([commitment]), answering(resolving([{ outcome: 'Unresolvable' }, { outcome: 'PrivateSource' }])));

        expect(
            await screen.findByText('This passage is no longer where it was — the message has been read again since.'),
        ).toBeTruthy();
        expect(screen.getByText('This passage cannot be read with this sign-in.')).toBeTruthy();
    });

    it('says a passage past what one request follows was not followed, rather than that it has moved', async () => {
        const crowded: MailEnrichmentMark[] = ['Commitment', 'Significance', 'Sense'].map((aspect, at) => ({
            ...commitment,
            aspect: aspect as MailEnrichmentMark['aspect'],
            evidence: Array.from({ length: 4 }, (_unused, which) => `fragment-${String(at)}-${String(which)}`),
        }));

        const followed = Array.from({ length: 10 }, (_unused, at) => ({
            outcome: 'Resolved',
            fragment: {
                fragmentId: `fragment-${String(Math.floor(at / 4))}-${String(at % 4)}`,
                ordinal: at,
                text: 'Words.',
            },
        }));

        check(asking(crowded), answering(resolving(followed)));

        expect(await screen.findAllByText('Words.')).toHaveLength(10);
        expect(
            screen.getAllByText('Only the first ten passages a message rests on are followed, and this is past them.'),
        ).toHaveLength(2);
    });

    it('offers to read the passages again where the deployment could not be reached', async () => {
        const { transport, requests } = recording(
            null,
            resolving([
                { outcome: 'Resolved', fragment: { fragmentId: 'fragment-1', ordinal: 0, text: 'Please confirm.' } },
                { outcome: 'Resolved', fragment: { fragmentId: 'fragment-2', ordinal: 4, text: 'Before Friday.' } },
            ]),
        );

        check(asking([commitment]), transport);

        const again = await screen.findByRole('button', { name: 'Try again' });

        expect(screen.getByRole('alert').textContent).toContain('The passages this rests on could not be read');

        act(() => {
            fireEvent.click(again);
        });

        expect(await screen.findByText('Please confirm.')).toBeTruthy();
        expect(requests).toHaveLength(2);
    });

    it('closes on the control it draws for that, because no state is reachable that cannot be left', () => {
        check(asking([sense]), answering());
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(screen.queryByRole('dialog')).toBeNull();
    });
});
