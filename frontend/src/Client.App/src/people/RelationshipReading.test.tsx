// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { RelationshipReading } from './RelationshipReading';

function drawCard(lastCorrespondedAt: string | null, reading = false): void {
    render(
        <LocalizationProvider>
            <RelationshipReading lastCorrespondedAt={lastCorrespondedAt} reading={reading} />
        </LocalizationProvider>,
    );
}

describe('RelationshipReading', () => {
    // The reading itself is authored by a run that has not landed, and a card that vanished would leave a reader
    // unable to tell a person MailFathom has nothing to say about from a screen that forgot to draw it.
    it('says the reading is not written yet rather than standing empty or being left off the page', () => {
        drawCard('2026-08-31T09:41:00+00:00');

        expect(screen.getByRole('region', { name: 'Relationship state' })).toBeDefined();
        expect(screen.getByText('MailFathom does not write a reading of a relationship yet.')).toBeDefined();
    });

    it('marks what it draws as authored, so nobody reads it as something the mailbox stated', () => {
        drawCard(null);

        expect(screen.getByText('AI')).toBeDefined();
    });

    it('waits with the correlation the figure beside it is read from', () => {
        drawCard(null, true);

        expect(screen.getByRole('status').textContent).toBe('Reading…');
    });

    it('says nobody has been in touch rather than drawing an empty figure', () => {
        drawCard(null);

        expect(screen.getByText('No mail exchanged with this person.')).toBeDefined();
    });

    it('draws when the person was last in touch instead of saying nobody has been', () => {
        drawCard('2026-08-31T09:41:00+00:00');

        expect(screen.getByText('Last contact')).toBeDefined();
        expect(screen.queryByText('No mail exchanged with this person.')).toBeNull();
    });
});
