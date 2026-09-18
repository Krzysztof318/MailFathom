// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ContactCorrespondence } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { PersonCorrespondence } from './PersonCorrespondence';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';

const found: ContactCorrespondence = {
    contactId: 'anna',
    threads: [
        {
            threadId: 'thread-1',
            latestMessageId: 'message-1',
            subject: 'Contract annex — signatures',
            lastCorrespondedAt: '2026-08-31T09:41:00+00:00',
        },
    ],
    documents: [
        {
            messageId: 'message-1',
            position: 3,
            fileName: 'annex.pdf',
            mediaType: 'application/pdf',
            receivedAt: '2026-08-31T09:41:00+00:00',
        },
    ],
};

const nothingRead: ContactCorrespondenceInForce = {
    correspondence: null,
    reading: false,
    failure: null,
    readAgain: () => undefined,
};

function drawColumns({
    reading = { ...nothingRead, correspondence: found },
    onOpenThread = vi.fn(),
    onOpenDocument = vi.fn(),
}: {
    reading?: ContactCorrespondenceInForce;
    onOpenThread?: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;
    onOpenDocument?: (document: { readonly messageId: string; readonly position: number }) => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <PersonCorrespondence reading={reading} onOpenThread={onOpenThread} onOpenDocument={onOpenDocument} />
        </LocalizationProvider>,
    );
}

describe('PersonCorrespondence', () => {
    it('draws the conversations naming the person and the files they sent as two columns', () => {
        drawColumns();

        expect(
            within(screen.getByRole('region', { name: 'Shared threads' })).getByText('Contract annex — signatures'),
        ).toBeDefined();
        expect(
            within(screen.getByRole('region', { name: 'Documents from this person' })).getByText('annex.pdf'),
        ).toBeDefined();
    });

    it('opens the conversation at the message that last named the person', () => {
        const onOpenThread = vi.fn();
        drawColumns({ onOpenThread });

        fireEvent.click(screen.getByRole('button', { name: /Contract annex/u }));

        expect(onOpenThread).toHaveBeenCalledWith({
            threadId: 'thread-1',
            messageId: 'message-1',
            subject: 'Contract annex — signatures',
        });
    });

    it('opens a file as which file of which message, which is all a row knows about one', () => {
        const onOpenDocument = vi.fn();
        drawColumns({ onOpenDocument });

        fireEvent.click(screen.getByRole('button', { name: /annex\.pdf/u }));

        expect(onOpenDocument).toHaveBeenCalledWith({ messageId: 'message-1', position: 3 });
    });

    // Two empty states rather than one, because a person who has written nothing is a different answer from a person
    // who has written and attached nothing.
    it('says each column is empty in that column’s own terms', () => {
        drawColumns({ reading: { ...nothingRead, correspondence: { ...found, threads: [], documents: [] } } });

        expect(screen.getByText('No conversation here names this person.')).toBeDefined();
        expect(screen.getByText('This person has sent no files.')).toBeDefined();
    });

    it('says both columns are waiting on the one read they share', () => {
        drawColumns({ reading: { ...nothingRead, reading: true } });

        expect(screen.getAllByRole('status').map((said) => said.textContent)).toStrictEqual(['Reading…', 'Reading…']);
    });

    it('says why the correlation did not answer and offers the way out of it', () => {
        const readAgain = vi.fn();
        drawColumns({ reading: { ...nothingRead, failure: 'unauthorized', readAgain } });

        const threads = within(screen.getByRole('region', { name: 'Shared threads' }));

        expect(threads.getByRole('alert').textContent).toBe(
            'This credential may not read what this person is named in.',
        );

        fireEvent.click(threads.getByRole('button', { name: 'Try again' }));

        expect(readAgain).toHaveBeenCalledTimes(1);
    });
});
