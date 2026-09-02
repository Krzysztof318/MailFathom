// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { MailMessageHeaders } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageHeaders } from './MessageHeaders';

const headers: MailMessageHeaders = {
    subject: 'Quarterly invoice',
    sentAt: '2026-08-31T09:41:00+00:00',
    receivedAt: '2026-08-31T09:41:10+00:00',
    participants: [
        { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
        { role: 'To', address: 'reader@example.invalid', displayName: null },
        { role: 'Cc', address: 'archive@example.invalid', displayName: 'Archive' },
    ],
    messageId: 'abc@example.invalid',
    inReplyTo: null,
    references: [],
};

// Found by the words a person reads and then read as the element it is, because whether a disclosure is open is a
// property of that element rather than something jsdom expresses by hiding what is inside it.
function disclosure(): HTMLDetailsElement {
    const opened = screen.getByText(/^Everybody else this message names/u).closest('details');

    if (opened === null) {
        throw new Error('The summary naming the other participants is not inside a disclosure.');
    }

    return opened;
}

function drawing(written: Partial<MailMessageHeaders> = {}): void {
    render(
        <LocalizationProvider>
            <MessageHeaders headers={{ ...headers, ...written }} />
        </LocalizationProvider>,
    );
}

// A zone pinned for one test is put back for the reason a fake clock is released: it is the worker's, not the file's.
const machineZone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = machineZone;
});

describe('MessageHeaders', () => {
    it('draws the subject as the heading of the message', () => {
        drawing();

        expect(screen.getByRole('heading', { name: 'Quarterly invoice', level: 2 })).toBeDefined();
    });

    it('says a message carries no subject rather than heading it with nothing', () => {
        drawing({ subject: null });

        expect(screen.getByRole('heading', { name: 'No subject', level: 2 })).toBeDefined();
    });

    it('draws the author with the address beside the name the sender wrote', () => {
        drawing();

        expect(screen.getByText('Billing <billing@example.invalid>')).toBeDefined();
    });

    it('draws an author who wrote no name as the address alone', () => {
        drawing({ participants: [{ role: 'From', address: 'billing@example.invalid', displayName: null }] });

        expect(screen.getByText('billing@example.invalid')).toBeDefined();
    });

    it('says a message names nobody as its author rather than drawing an empty line', () => {
        drawing({ participants: [] });

        expect(screen.getByText('This message names nobody as its author.')).toBeDefined();
    });

    it('places both instants against the reader own clock, so a message that arrived overnight says so', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        drawing({ receivedAt: '2026-08-31T23:12:00+00:00' });

        // Written out rather than compared against a formatter built here, because that comparison passes just as
        // happily for a screen that pinned a zone of its own — which is the defect this is about. Warsaw is two hours
        // ahead in August, which is what carries the arrival into the next day.
        expect(screen.getByText('Sent August 31, 2026 at 11:41 AM')).toBeDefined();
        expect(screen.getByText('Received September 1, 2026 at 1:12 AM')).toBeDefined();
    });

    it('reads the same instants a day earlier for a reader west of the sender', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        drawing({ receivedAt: '2026-08-31T23:12:00+00:00' });

        expect(screen.getByText('Sent August 31, 2026 at 2:41 AM')).toBeDefined();
        expect(screen.getByText('Received August 31, 2026 at 4:12 PM')).toBeDefined();
    });

    it('keeps the instant the service sent beside the words, so what a machine reads is not the wording', () => {
        drawing();

        expect(screen.getByText(/^Sent /u).getAttribute('datetime')).toBe('2026-08-31T09:41:00+00:00');
        expect(screen.getByText(/^Received /u).getAttribute('datetime')).toBe('2026-08-31T09:41:10+00:00');
    });

    it('says the sender wrote no readable date rather than drawing a broken one', () => {
        drawing({ sentAt: 'the day before yesterday' });

        expect(screen.getByText('The sender wrote no date this client can read.')).toBeDefined();
    });

    it('keeps everybody else behind a disclosure, so a message to two hundred people is not a screen of addresses', () => {
        drawing();

        expect(disclosure().open).toBe(false);
    });

    it('names everybody else under the header each address appeared in, once the disclosure is opened', () => {
        drawing();

        fireEvent.click(screen.getByText('Everybody else this message names (2)'));

        expect(disclosure().open).toBe(true);
        expect(screen.getByText('To')).toBeDefined();
        expect(screen.getByText('reader@example.invalid')).toBeDefined();
        expect(screen.getByText('Archive <archive@example.invalid>')).toBeDefined();
    });

    it('draws a display name written to look like markup as the characters the sender wrote', () => {
        drawing({
            participants: [
                { role: 'From', address: 'billing@example.invalid', displayName: '<script>alert(1)</script>' },
            ],
        });

        expect(screen.getByText('<script>alert(1)</script> <billing@example.invalid>')).toBeDefined();
    });
});
