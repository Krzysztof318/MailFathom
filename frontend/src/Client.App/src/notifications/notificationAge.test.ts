// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { renderHook } from '@testing-library/react';
import { act } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useCurrentMinute, wordNotificationAge } from './notificationAge';

// The clock is pinned rather than read, so the ladder is asserted at each of its steps instead of at whichever one the
// suite happened to run on. The wording itself is `Intl`'s, so what is proven here is which step an age falls on and
// that both languages reach their own words for it.

const now = Date.parse('2026-09-04T12:00:00Z');

function ago(milliseconds: number): string {
    return new Date(now - milliseconds).toISOString();
}

const aMinute = 60_000;
const anHour = 60 * aMinute;
const aDay = 24 * anHour;

describe('wordNotificationAge', () => {
    it('words anything inside the minute as having just happened rather than counting seconds', () => {
        expect(wordNotificationAge(ago(30_000), 'en', now)).toBe('now');
    });

    it('words the minutes up to an hour', () => {
        expect(wordNotificationAge(ago(5 * aMinute), 'en', now)).toBe('5 min. ago');
    });

    it('words the hours up to a day', () => {
        expect(wordNotificationAge(ago(3 * anHour), 'en', now)).toBe('3 hr. ago');
    });

    it('words the day before as the language’s own word for it rather than as a count', () => {
        expect(wordNotificationAge(ago(aDay), 'en', now)).toBe('yesterday');
    });

    it('words anything older in days', () => {
        expect(wordNotificationAge(ago(4 * aDay), 'en', now)).toBe('4 days ago');
    });

    it('words the same age in Polish, which needs a form English does not', () => {
        expect(wordNotificationAge(ago(5 * aMinute), 'pl', now)).toBe('5 min temu');
    });

    it('words an instant a few seconds ahead of this machine as having just happened, not as a countdown', () => {
        expect(wordNotificationAge(ago(-4_000), 'en', now)).toBe('now');
    });

    it('answers with nothing at all where what arrived is not an instant this client can read', () => {
        expect(wordNotificationAge('the day before the engine', 'en', now)).toBeNull();
    });
});

describe('useCurrentMinute', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-04T12:00:30Z'));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('answers with the minute it is, so every row drawn in one render is measured against one instant', () => {
        const { result } = renderHook(() => useCurrentMinute());

        expect(result.current).toBe(Date.parse('2026-09-04T12:00:00Z'));
    });

    it('answers the same while the minute lasts, rather than a new value on every render', () => {
        const { result, rerender } = renderHook(() => useCurrentMinute());
        const first = result.current;

        act(() => {
            vi.setSystemTime(new Date('2026-09-04T12:00:59Z'));
        });
        rerender();

        expect(result.current).toBe(first);
    });

    it('moves on as the minute turns over, so an age on the screen stops being the one it was drawn at', () => {
        const { result } = renderHook(() => useCurrentMinute());

        act(() => {
            vi.advanceTimersByTime(aMinute);
        });

        expect(result.current).toBe(Date.parse('2026-09-04T12:01:00Z'));
    });
});
