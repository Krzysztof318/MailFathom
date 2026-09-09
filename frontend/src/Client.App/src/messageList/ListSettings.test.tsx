// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ListHeadRowContext } from '../mailSpace/listHeadRow';
import { ListSettings } from './ListSettings';
import { openingListing, type MailListing } from './listing';

// The instant every span in these tests is reckoned from, pinned so that "the last seven days" is one span rather than
// whichever one the suite happened to run on.
const readingAt = new Date(2026, 8, 3, 15, 42);

function renderSettings(
    listing: MailListing = openingListing,
    onRead: (listing: MailListing) => void = () => undefined,
    junkAskable = false,
    readings: { shown?: boolean; onDrawReadings?: (shown: boolean) => void } = {},
): void {
    render(
        <LocalizationProvider>
            <ListSettings
                listing={listing}
                junkAskable={junkAskable}
                readingsShown={readings.shown ?? true}
                onRead={onRead}
                onDrawReadings={readings.onDrawReadings ?? (() => undefined)}
            />
        </LocalizationProvider>,
    );
}

// The panel is folded away, and everything inside it is reached by opening it first.
function openFilters(): void {
    fireEvent.click(screen.getByText('Filters'));
}

function narrowedTo(range: string): MailListing | undefined {
    const read = vi.fn<(listing: MailListing) => void>();

    renderSettings(openingListing, read);
    openFilters();
    fireEvent.click(screen.getByRole('button', { name: range }));

    return read.mock.calls[0]?.[0];
}

describe('ListSettings', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(readingAt);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('renders the control that opens it into the head row the column offers, and the panel under the row', () => {
        const row = document.createElement('div');
        document.body.append(row);

        try {
            render(
                <LocalizationProvider>
                    <ListHeadRowContext value={row}>
                        <ListSettings
                            listing={openingListing}
                            junkAskable={false}
                            readingsShown
                            onRead={() => undefined}
                            onDrawReadings={() => undefined}
                        />
                    </ListHeadRowContext>
                </LocalizationProvider>,
            );

            const opener = screen.getByRole('button', { name: 'Filters' });

            expect(row.contains(opener)).toBe(true);
            expect(opener.getAttribute('aria-expanded')).toBe('false');
            expect(screen.queryByText('No active filters')).toBeNull();

            fireEvent.click(opener);

            expect(opener.getAttribute('aria-expanded')).toBe('true');
            expect(row.contains(screen.getByText('No active filters'))).toBe(false);
        } finally {
            row.remove();
        }
    });

    it('offers turning the readings off for this view, drawn as on where they are shown', () => {
        renderSettings();
        openFilters();

        expect(screen.getByRole('switch', { name: 'Show a reading on each row', checked: true })).toBeTruthy();
    });

    it('says the readings are off for this view where somebody turned them off', () => {
        renderSettings(openingListing, () => undefined, false, { shown: false });
        openFilters();

        expect(screen.getByRole('switch', { name: 'Show a reading on each row', checked: false })).toBeTruthy();
    });

    it('asks for the plain row rather than asking the deployment for the folder again', () => {
        const drawn = vi.fn<(shown: boolean) => void>();
        const read = vi.fn<(listing: MailListing) => void>();

        renderSettings(openingListing, read, false, { onDrawReadings: drawn });
        openFilters();
        fireEvent.click(screen.getByRole('switch', { name: 'Show a reading on each row' }));

        expect(drawn).toHaveBeenCalledWith(false);
        expect(read).not.toHaveBeenCalled();
    });

    it('says nothing narrows the folder rather than drawing a count nobody has to act on', () => {
        renderSettings();
        openFilters();

        expect(screen.getByText('No active filters')).toBeTruthy();
        expect(screen.queryByText('Clear filters')).toBeNull();
    });

    it('carries the count of what the reader narrowed, so an empty folder is not a filter they forgot', () => {
        const filters = { ...openingListing.filters, unread: true, flagged: true };

        renderSettings({ ...openingListing, filters });
        openFilters();

        expect(screen.getByText('Active filters: 2')).toBeTruthy();
    });

    it('offers the folder in both of the orders the deployment serves', () => {
        renderSettings();
        openFilters();

        expect(screen.getByRole('radio', { name: 'Newest first' })).toBeTruthy();
        expect(screen.getByRole('radio', { name: 'Oldest first' })).toBeTruthy();
    });

    it('reads the folder the other way round when the other order is picked', () => {
        const read = vi.fn<(listing: MailListing) => void>();

        renderSettings(openingListing, read);
        openFilters();
        fireEvent.click(screen.getByRole('radio', { name: 'Oldest first' }));

        expect(read.mock.calls[0]?.[0].order).toBe('oldestFirst');
    });

    it.each([
        ['Today', '2026-09-03T00:00'],
        ['Last 7 days', '2026-08-28T00:00'],
        ['Last 30 days', '2026-08-05T00:00'],
        ['This year', '2026-01-01T00:00'],
    ])('narrows the folder to %s, beginning at %s', (range, expected) => {
        const listing = narrowedTo(range);

        expect(listing?.filters.receivedFrom).toBe(expected);
        expect(listing?.filters.receivedTo).toBeNull();
    });

    it('takes the span off again when the one in force is pressed', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = { ...openingListing.filters, dateRange: 'today' as const, receivedFrom: '2026-09-03T00:00' };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: 'Today' }));

        expect(read.mock.calls[0]?.[0].filters.dateRange).toBeNull();
        expect(read.mock.calls[0]?.[0].filters.receivedFrom).toBeNull();
    });

    it('says which span is in force, so the one narrowing the folder is the one drawn as chosen', () => {
        const filters = { ...openingListing.filters, dateRange: 'thisYear' as const, receivedFrom: '2026-01-01T00:00' };

        renderSettings({ ...openingListing, filters });
        openFilters();

        expect(screen.getByRole('button', { name: 'This year' }).getAttribute('aria-pressed')).toBe('true');
        expect(screen.getByRole('button', { name: 'Today' }).getAttribute('aria-pressed')).toBe('false');
    });

    it('draws the two fields empty under a span, because the reader picked the span rather than an instant', () => {
        const filters = { ...openingListing.filters, dateRange: 'today' as const, receivedFrom: '2026-09-03T00:00' };

        renderSettings({ ...openingListing, filters });
        openFilters();

        expect(screen.getByLabelText<HTMLInputElement>('from').value).toBe('');
    });

    it('takes the span off when the reader types a start of their own', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = { ...openingListing.filters, dateRange: 'today' as const, receivedFrom: '2026-09-03T00:00' };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.change(screen.getByLabelText('from'), { target: { value: '2026-05-01T09:00' } });

        expect(read.mock.calls[0]?.[0].filters.dateRange).toBeNull();
        expect(read.mock.calls[0]?.[0].filters.receivedFrom).toBe('2026-05-01T09:00');
    });

    it('says a range selects nothing rather than asking the deployment for one that cannot', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = { ...openingListing.filters, receivedTo: '2026-05-01T09:00' };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.change(screen.getByLabelText('from'), { target: { value: '2026-06-01T09:00' } });

        expect(screen.getByRole('alert').textContent).toContain('falls before its start');
        expect(read).not.toHaveBeenCalled();
    });

    it('keeps a refused range in the two fields, so the moment the reader picked is not taken away from them', () => {
        const filters = { ...openingListing.filters, receivedTo: '2026-05-01T09:00' };

        renderSettings({ ...openingListing, filters });
        openFilters();
        fireEvent.change(screen.getByLabelText('from'), { target: { value: '2026-06-01T09:00' } });

        expect(screen.getByLabelText<HTMLInputElement>('from').value).toBe('2026-06-01T09:00');
    });

    it('offers reaching into junk only where the list spans folders', () => {
        renderSettings(openingListing, () => undefined, true);
        openFilters();

        expect(screen.getByLabelText('Include junk')).toBeTruthy();
    });

    it('does not offer junk where the reader has pointed at one folder, which would change nothing', () => {
        renderSettings();
        openFilters();

        expect(screen.queryByLabelText('Include junk')).toBeNull();
    });

    it.each([
        ['What this is about', 'Sense'],
        ['Why this may matter', 'Significance'],
        ['What is committed to', 'Commitment'],
    ])('narrows the list to the %s a derivation recorded', (named, aspect) => {
        const read = vi.fn<(listing: MailListing) => void>();

        renderSettings(openingListing, read);
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: named }));

        expect(read.mock.calls[0]?.[0].filters.markAspect).toBe(aspect);
    });

    it('draws the reading in force as chosen, which is what a standing view pressed in the tree looks like here', () => {
        const filters = { ...openingListing.filters, markAspect: 'Significance' as const };

        renderSettings({ ...openingListing, filters });
        openFilters();

        expect(screen.getByRole('button', { name: 'Why this may matter' }).getAttribute('aria-pressed')).toBe('true');
        expect(screen.getByRole('button', { name: 'What is committed to' }).getAttribute('aria-pressed')).toBe('false');
    });

    it('takes the reading off again when the one in force is pressed, so a view can be removed one criterion at a time', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = { ...openingListing.filters, markAspect: 'Commitment' as const };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: 'What is committed to' }));

        expect(read.mock.calls[0]?.[0].filters.markAspect).toBeNull();
    });

    it('bounds the commitments to the reader’s own week, reckoned from the start of their day', () => {
        const read = vi.fn<(listing: MailListing) => void>();

        renderSettings(openingListing, read);
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: 'Due this week' }));

        expect(read.mock.calls[0]?.[0].filters.markDueFrom).toBe('2026-09-03T00:00');
        expect(read.mock.calls[0]?.[0].filters.markDueTo).toBe('2026-09-10T00:00');
    });

    it('takes the due window off again, leaving the reading it was set beside in force', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = {
            ...openingListing.filters,
            markAspect: 'Commitment' as const,
            markDueFrom: '2026-09-03T00:00',
            markDueTo: '2026-09-10T00:00',
        };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.click(screen.getByRole('button', { name: 'Due this week' }));

        const narrowed = read.mock.calls[0]?.[0];

        expect(narrowed?.filters.markDueFrom).toBeNull();
        expect(narrowed?.filters.markDueTo).toBeNull();
        expect(narrowed?.filters.markAspect).toBe('Commitment');
    });

    it('counts what a standing view put in force among the narrowings, so the control says the list is narrowed', () => {
        const filters = {
            ...openingListing.filters,
            markAspect: 'Commitment' as const,
            markDueFrom: '2026-09-03T00:00',
            markDueTo: '2026-09-10T00:00',
        };

        renderSettings({ ...openingListing, filters });
        openFilters();

        expect(screen.getByText('Active filters: 1')).toBeTruthy();
    });

    it('clears what a standing view put in force with every other narrowing', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = {
            ...openingListing.filters,
            markAspect: 'Commitment' as const,
            markDueFrom: '2026-09-03T00:00',
            markDueTo: '2026-09-10T00:00',
        };

        renderSettings({ ...openingListing, filters }, read);
        openFilters();
        fireEvent.click(screen.getByText('Clear filters'));

        const cleared = read.mock.calls[0]?.[0];

        expect(cleared?.filters.markAspect).toBeNull();
        expect(cleared?.filters.markDueFrom).toBeNull();
        expect(cleared?.filters.markDueTo).toBeNull();
    });

    it('leaves what the reader chose about junk alone when the narrowings are cleared', () => {
        const read = vi.fn<(listing: MailListing) => void>();
        const filters = { ...openingListing.filters, unread: true, includeJunk: true };

        renderSettings({ ...openingListing, order: 'oldestFirst', filters }, read, true);
        openFilters();
        fireEvent.click(screen.getByText('Clear filters'));

        const cleared = read.mock.calls[0]?.[0];

        expect(cleared?.order).toBe('newestFirst');
        expect(cleared?.filters.unread).toBeNull();
        expect(cleared?.filters.includeJunk).toBe(true);
    });
});
