// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CalendarEvent } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TodayCalendar } from './TodayCalendar';
import type { TodayCalendarInForce } from './useTodayCalendar';

// The hour an event is drawn at is the reader's own, so the zone is pinned rather than left to the machine: the same
// instant is nine in Warsaw and seven in London, and an assertion written in whichever zone the runner reports would
// pass in one country and fail in the next.
const declaredZone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = declaredZone;
});

function eventAt(id: string, title: string, start: string): CalendarEvent {
    return {
        id,
        title,
        start,
        end: start,
        isAllDay: false,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        sourceMessage: null,
        recordedAt: start,
        amendedAt: start,
    };
}

function drawDay(day: Partial<TodayCalendarInForce> = {}): void {
    render(
        <LocalizationProvider>
            <TodayCalendar
                day={{ events: [], reading: false, failure: null, readAgain: vi.fn(), ...day }}
            />
        </LocalizationProvider>,
    );
}

describe('TodayCalendar', () => {
    // The one control the panel carries. It is an address rather than a button because a space is reached at a
    // fragment, so what is asserted is where it points as well as what it is named.
    it('offers the way through to the calendar itself', () => {
        drawDay({ events: [] });

        expect(screen.getByRole('link', { name: 'Open the calendar' }).getAttribute('href')).toBe('#/calendar');
    });

    it('draws what the day holds, at the hour the reader’s own clock shows', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        drawDay({ events: [eventAt('a', 'Standup', '2026-09-21T07:00:00+00:00')] });

        expect(screen.getByText('Standup')).toBeDefined();

        // Written out rather than compared against a formatter built here, because that comparison passes just as
        // happily for a panel that named a zone of its own — which is the defect this is about. Warsaw is two hours
        // ahead in September, and English words an hour on a twelve-hour clock.
        expect(screen.getByText('09:00 AM')).toBeDefined();
    });

    // What a `<time>` carries in `dateTime` is the instant the deployment sent, unchanged: the human spelling and the
    // machine-readable form are different things and neither replaces the other.
    it('carries the instant the deployment sent beside the hour it drew', () => {
        drawDay({ events: [eventAt('a', 'Standup', '2026-09-21T07:00:00+00:00')] });

        expect(screen.getByText('Standup').closest('li')?.querySelector('time')?.getAttribute('dateTime')).toBe(
            '2026-09-21T07:00:00+00:00',
        );
    });

    it('says it is reading before the day has answered', () => {
        drawDay({ reading: true });

        expect(screen.getByRole('status').textContent).toBe('Reading the day…');
    });

    it('says the day is empty rather than leaving the panel looking unfinished', () => {
        drawDay();

        expect(screen.getByText('Nothing is in the calendar today.')).toBeDefined();
    });

    it('says why the day did not answer and offers the way out', () => {
        const readAgain = vi.fn();

        drawDay({ failure: 'unauthorized', readAgain });

        expect(screen.getByRole('alert').textContent).toBe('This credential may not read your calendar.');

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(readAgain).toHaveBeenCalledOnce();
    });

    it('does not call a day it could not read an empty one', () => {
        drawDay({ failure: 'unavailable' });

        expect(screen.queryByText('Nothing is in the calendar today.')).toBeNull();
    });
});
