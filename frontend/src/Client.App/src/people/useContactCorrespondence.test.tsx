// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useContactCorrespondence } from './useContactCorrespondence';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The probe's own control, named here rather than written into its markup, for the reason the book's probe
// names its own: the localization rule holds over every `.tsx` under the packages, tests included.
const asksAgain = 'Read again';

function correspondenceOf(contactId: string, subject: string): string {
    return JSON.stringify({
        contactId,
        threads: [
            {
                threadId: 'thread-1',
                latestMessageId: 'message-1',
                subject,
                lastCorrespondedAt: '2026-08-31T09:41:00+00:00',
            },
        ],
        documents: [],
    });
}

function Correlated({
    transport,
    contactId = 'anna',
}: {
    readonly transport: MailFathomTransport;
    readonly contactId?: string | null;
}) {
    const held = useContactCorrespondence(session, transport, contactId);

    return (
        <div>
            <p>{held.reading ? 'reading' : 'settled'}</p>
            <p>{held.failure ?? 'answered'}</p>
            <ul>
                {(held.correspondence?.threads ?? []).map((thread) => (
                    <li key={thread.threadId}>{thread.subject}</li>
                ))}
            </ul>
            <button type="button" onClick={held.readAgain}>
                {asksAgain}
            </button>
        </div>
    );
}

describe('useContactCorrespondence', () => {
    it('reads what the mailbox says about the person it was asked about', async () => {
        const transport: MailFathomTransport = () =>
            Promise.resolve({ status: 200, body: correspondenceOf('anna', 'Contract annex'), headers: {} });

        render(<Correlated transport={transport} />);

        expect(await screen.findByText('Contract annex')).toBeDefined();
    });

    it('lets go of a person the deployment no longer holds rather than reporting the deployment as down', async () => {
        const transport: MailFathomTransport = () => Promise.resolve({ status: 404, body: '', headers: {} });

        render(<Correlated transport={transport} />);

        expect(await screen.findByText('missing')).toBeDefined();
    });

    it('reads again when asked to, which is the way out of a failure', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve(
                attempts === 1
                    ? { status: 503, body: '', headers: {} }
                    : { status: 200, body: correspondenceOf('anna', 'Contract annex'), headers: {} },
            );
        };

        render(<Correlated transport={transport} />);
        expect(await screen.findByText('unavailable')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: asksAgain }));

        expect(await screen.findByText('Contract annex')).toBeDefined();
    });

    it('draws the person asked about second rather than the one left behind', async () => {
        const transport: MailFathomTransport = ({ path }) =>
            Promise.resolve({
                status: 200,
                body: correspondenceOf(path, path.includes('bartek') ? 'Invoice 4711' : 'Contract annex'),
                headers: {},
            });

        const drawn = render(<Correlated transport={transport} />);
        expect(await screen.findByText('Contract annex')).toBeDefined();

        drawn.rerender(<Correlated transport={transport} contactId="bartek" />);

        expect(screen.queryByText('Contract annex')).toBeNull();
        expect(await screen.findByText('Invoice 4711')).toBeDefined();
    });

    it('reads nothing where nobody is open', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve({ status: 200, body: correspondenceOf('anna', 'Contract annex'), headers: {} });
        };

        render(<Correlated transport={transport} contactId={null} />);

        expect(await screen.findByText('settled')).toBeDefined();
        expect(attempts).toBe(0);
    });
});
