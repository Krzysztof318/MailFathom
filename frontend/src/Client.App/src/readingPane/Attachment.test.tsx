// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailAttachment } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { Attachment } from './Attachment';
import type { Download } from './downloadingAttachment';

const invoice: MailAttachment = {
    position: 1,
    fileName: 'invoice.pdf',
    wasFileNameNormalized: false,
    mediaType: 'application/pdf',
    sizeOctets: 2_048,
};

const described: Download = { stage: 'described' };

function drawing(
    attachment: MailAttachment,
    downloading: Download = described,
): { onOpen: () => void; onDownload: () => void; onStop: () => void } {
    const controls = { onOpen: vi.fn(), onDownload: vi.fn(), onStop: vi.fn() };

    render(
        <LocalizationProvider>
            <ul>
                <Attachment attachment={attachment} downloading={downloading} {...controls} />
            </ul>
        </LocalizationProvider>,
    );

    return controls;
}

describe('Attachment', () => {
    it('describes the file before anything is fetched, so a reader decides before it arrives', () => {
        drawing(invoice);

        expect(screen.getByText('invoice.pdf')).toBeDefined();
        expect(screen.getByText('pdf')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Open invoice.pdf' }).getAttribute('title')).toBe('application/pdf');
        expect(screen.getByText(sizeReadAs(2_048))).toBeDefined();
    });

    it('names an unnamed part rather than offering a control with nothing to say', () => {
        drawing({ ...invoice, fileName: null });

        expect(screen.getByRole('button', { name: 'Open Unnamed file' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Download Unnamed file' })).toBeDefined();
    });

    it('names the kind of a part from what the message declared it to be', () => {
        drawing({ ...invoice, fileName: 'photo.jpg', mediaType: 'image/jpeg' });

        expect(screen.getByText('image')).toBeDefined();
    });

    it('says where the sender wrote a file name the deployment would not use', () => {
        drawing({ ...invoice, wasFileNameNormalized: true });

        expect(
            screen.getByText(
                'The sender wrote a file name this deployment would not use, so what is shown is the name it was given instead.',
            ),
        ).toBeDefined();
    });

    // Opening and downloading are two acts and two controls, which is what keeps a reader who wanted to look at
    // something from having to find it in a downloads folder afterwards.
    it('opens the file when the chip is pressed, and downloads nothing', () => {
        const controls = drawing(invoice);

        fireEvent.click(screen.getByRole('button', { name: 'Open invoice.pdf' }));

        expect(controls.onOpen).toHaveBeenCalledTimes(1);
        expect(controls.onDownload).not.toHaveBeenCalled();
    });

    it('asks for the file when the control beside the chip is pressed, and opens nothing', () => {
        const controls = drawing(invoice);

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        expect(controls.onDownload).toHaveBeenCalledTimes(1);
        expect(controls.onOpen).not.toHaveBeenCalled();
    });

    it('asks for nothing more while the file is still arriving', () => {
        const controls = drawing(invoice, { stage: 'arriving', octets: 0 });

        fireEvent.click(screen.getByRole('button', { name: 'Download invoice.pdf' }));

        expect(controls.onDownload).not.toHaveBeenCalled();
    });

    it('opens the file while it is arriving, because looking at it stops nothing', () => {
        const controls = drawing(invoice, { stage: 'arriving', octets: 0 });

        fireEvent.click(screen.getByRole('button', { name: 'Open invoice.pdf' }));

        expect(controls.onOpen).toHaveBeenCalledTimes(1);
    });

    it('says how much has arrived while the file is still arriving', () => {
        drawing(invoice, { stage: 'arriving', octets: 1_024 });

        expect(screen.getByText(`${sizeReadAs(1_024)} of ${sizeReadAs(2_048)}`)).toBeDefined();
    });

    it('offers a way out of a download in flight', () => {
        const controls = drawing(invoice, { stage: 'arriving', octets: 1_024 });

        fireEvent.click(screen.getByRole('button', { name: 'Stop downloading' }));

        expect(controls.onStop).toHaveBeenCalledTimes(1);
    });

    it('says the file was downloaded once it has been', () => {
        drawing(invoice, { stage: 'finished', outcome: 'delivered' });

        expect(screen.getByText('invoice.pdf was downloaded.')).toBeDefined();
    });

    it.each([
        [
            'unauthenticated',
            'This deployment no longer accepts the credential, so the file was not downloaded. Sign in again.',
        ],
        ['unauthorized', 'This credential may not read mail on this deployment, so the file was not downloaded.'],
        ['unavailable', 'The deployment did not answer, so the file was not downloaded. Try again.'],
        [
            'largerThanDescribed',
            'The deployment sent more than this message said the file holds, so nothing was saved. Report this as a defect.',
        ],
        ['abandoned', 'The download was stopped, so nothing was saved.'],
    ] as const)('says what became of a download that answered %s', (outcome, said) => {
        drawing(invoice, { stage: 'finished', outcome });

        expect(screen.getByRole('alert').textContent).toBe(said);
    });
});

// The size is `Intl`'s under the active language, so a test asks it the same question the screen asked rather than
// spelling out an answer that would be about this machine.
function sizeReadAs(octets: number): string {
    return new Intl.NumberFormat('en', {
        style: 'unit',
        unit: 'kilobyte',
        unitDisplay: 'short',
        maximumFractionDigits: 1,
    }).format(octets / 1_000);
}
