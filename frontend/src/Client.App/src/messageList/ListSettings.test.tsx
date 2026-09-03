// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ListSettings } from './ListSettings';
import { openingListing, type MailListing } from './listing';

// The instant every span in these tests is reckoned from, pinned so that "the last seven days" is one span rather than
// whichever one the suite happened to run on.
const readingAt = new Date(2026, 8, 3, 15, 42);

function renderSettings(
    listing: MailListing = openingListing,
    onRead: (listing: MailListing) => void = () => undefined,
    junkAskable = false,
): void {
    render(
        <LocalizationProvider>
            <ListSettings listing={listing} junkAskable={junkAskable} onRead={onRead} />
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
