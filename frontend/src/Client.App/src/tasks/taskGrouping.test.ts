// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { groupOf, groupedTasks, inDueOrder } from './taskGrouping';

// A day is the reader's own, so every case here is read in a stated zone and put back afterwards. Node re-reads the
// variable at assignment, which is what makes this work and why no zone is ever passed to a formatter to simulate one.
const zone = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = zone;
});

function task(id: string, dueOn: string | null): PersonalTask {
    return { id, title: id, dueOn, origin: 'Asserted', completed: false, sourceMessageId: null };
}

// Monday 21 September 2026, late morning in Warsaw.
const monday = Date.parse('2026-09-21T10:30:00+02:00');

describe('groupedTasks', () => {
    it('draws today, this week and later in the order the design draws them', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        const groups = groupedTasks(
            [task('now', '2026-09-21'), task('soon', '2026-09-24'), task('far', '2026-11-02')],
            monday,
        );

        expect(groups.map((group) => group.name)).toEqual(['today', 'thisWeek', 'later']);
    });

    it('leaves out a heading nothing falls under rather than drawing an empty one', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        const groups = groupedTasks([task('far', '2026-11-02')], monday);

        expect(groups.map((group) => group.name)).toEqual(['later']);
    });

    it('keeps the order the deployment walked the list in inside a heading', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        const groups = groupedTasks([task('first', '2026-09-22'), task('second', '2026-09-23')], monday);

        expect(groups[0]?.tasks.map((held) => held.id)).toEqual(['first', 'second']);
    });

    it('draws nothing at all for a list with nothing in it', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        expect(groupedTasks([], monday)).toEqual([]);
    });

    it('reads the day the reader is in rather than the one the machine was built in', () => {
        // Late on the 21st in Warsaw is already the 22nd in Auckland, so the same instant puts the same task under a
        // different heading — which is the whole of what reading a day in the reader's own zone means.
        const lateOnMonday = Date.parse('2026-09-21T23:30:00+02:00');

        process.env['TZ'] = 'Europe/Warsaw';
        expect(groupedTasks([task('now', '2026-09-22')], lateOnMonday)[0]?.name).toBe('thisWeek');

        process.env['TZ'] = 'Pacific/Auckland';
        expect(groupedTasks([task('now', '2026-09-22')], lateOnMonday)[0]?.name).toBe('today');
    });
});

describe('inDueOrder', () => {
    it('interleaves the two halves by the day each is due on', () => {
        const order = inDueOrder(
            [task('own-monday', '2026-09-21'), task('own-friday', '2026-09-25')],
            [task('proposed-wednesday', '2026-09-23')],
        );

        expect(order.map((held) => held.id)).toEqual(['own-monday', 'proposed-wednesday', 'own-friday']);
    });

    it('puts what nobody has dated last, which is where the deployment walks it', () => {
        const order = inDueOrder([task('undated', null), task('dated', '2026-09-25')], []);

        expect(order.map((held) => held.id)).toEqual(['dated', 'undated']);
    });

    it('keeps two tasks due on one day in the order they were given', () => {
        const order = inDueOrder([task('committed', '2026-09-21')], [task('proposed', '2026-09-21')]);

        expect(order.map((held) => held.id)).toEqual(['committed', 'proposed']);
    });
});

describe('groupOf', () => {
    it.each([
        ['the day it is read on', '2026-09-21', 'today'],
        ['a day already past', '2026-09-14', 'today'],
        ['tomorrow', '2026-09-22', 'thisWeek'],
        ['the last day the week ahead reaches', '2026-09-27', 'thisWeek'],
        ['the day after that', '2026-09-28', 'later'],
    ] as const)('reads %s as %s', (_, dueOn, expected) => {
        expect(groupOf(task('one', dueOn), '2026-09-21', '2026-09-27')).toBe(expected);
    });

    it('reads a task nobody has dated as later, which is where the deployment walks it too', () => {
        expect(groupOf(task('undated', null), '2026-09-21', '2026-09-27')).toBe('later');
    });
});
