// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ReminderPanel } from './ReminderPanel';
import type { ReminderAnchor } from './reminderLeads';

const opening = 'Open';

function Editing({ anchor, held }: { readonly anchor: ReminderAnchor; readonly held: readonly number[] }) {
    const panel = useRef<HTMLDialogElement>(null);
    const [reminders, setReminders] = useState(held);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    panel.current?.showModal();
                }}
            >
                {opening}
            </button>

            <ReminderPanel
                panel={panel}
                subject="Design review"
                anchor={anchor}
                reminders={reminders}
                onRemindersChanged={setReminders}
            />
        </>
    );
}

function open(held: readonly number[] = [15], anchor: ReminderAnchor = 'eventTime'): void {
    render(
        <LocalizationProvider>
            <Editing anchor={anchor} held={held} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: opening }));
}

/** The leads the event carries, read off the chips, which are the only ones drawn with a way to remove them. */
function chosen(): readonly string[] {
    return screen
        .queryAllByRole('button', { name: /^Remove / })
        .map((removal) => removal.parentElement?.textContent.trim() ?? '');
}

function typeLead(count: string, unit: string): void {
    fireEvent.change(screen.getByRole('spinbutton', { name: 'How long before' }), { target: { value: count } });
    fireEvent.click(within(screen.getByRole('group', { name: 'Unit' })).getByRole('button', { name: unit }));
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
}

describe('ReminderPanel', () => {
    it('says what it is about and how many the event carries', () => {
        open();

        const panel = screen.getByRole('dialog').textContent;

        expect(panel).toContain('Reminders');
        expect(panel).toContain('Design review — before the event time');
        expect(panel).toContain('1 reminder');
    });

    it('names the hour a day is announced from, which is not the hour it begins at', () => {
        open([], 'eventDay');

        expect(screen.getByRole('dialog').textContent).toContain('Design review — 09:00 on the day of the event');
    });

    it('names the hour a due date is announced from, which is the rule the deployment owns', () => {
        open([], 'taskDueDate');

        expect(screen.getByRole('dialog').textContent).toContain('Design review — 09:00 on the day it is due');
    });

    it('offers a due date the leads the design draws for one, and none of the ones around a clock time', () => {
        open([], 'taskDueDate');

        expect(screen.getByRole('button', { name: '4 hours before' }).getAttribute('aria-pressed')).toBe('false');
        expect(screen.getByRole('button', { name: '2 days before' }).getAttribute('aria-pressed')).toBe('false');
        expect(screen.queryByRole('button', { name: '5 min before' })).toBeNull();
    });

    it('says a task announcing nothing is a task rather than an event, because that is what it is about', () => {
        open([], 'taskDueDate');

        expect(screen.getByRole('dialog').textContent).toContain(
            "No reminder — you won't be notified about this task.",
        );
    });

    it('draws a lead the event already carries as pressed and one it does not as offered', () => {
        open();

        expect(screen.getByRole('button', { name: '15 min before' }).getAttribute('aria-pressed')).toBe('true');
        expect(screen.getByRole('button', { name: '1 hour before' }).getAttribute('aria-pressed')).toBe('false');
    });

    it('offers the wider leads on an event stated as a day, which has no hour to precede', () => {
        open([], 'eventDay');

        expect(screen.getByRole('button', { name: 'on the day' }).getAttribute('aria-pressed')).toBe('false');
        expect(screen.queryByRole('button', { name: '5 min before' })).toBeNull();
    });

    it('sets a lead the event did not carry, in the order the earliest warning reads first', () => {
        open();

        fireEvent.click(screen.getByRole('button', { name: '1 day before' }));

        expect(chosen()).toEqual(['1 day before', '15 min before']);
    });

    it('turns a pressed lead off again, which is what the same control means the second time', () => {
        open();

        fireEvent.click(screen.getByRole('button', { name: '15 min before' }));

        expect(chosen()).toEqual([]);
    });

    it('removes one lead from its own chip and leaves the rest where they were', () => {
        open([1440, 15]);

        fireEvent.click(screen.getByRole('button', { name: 'Remove 1 day before' }));

        expect(chosen()).toEqual(['15 min before']);
    });

    it('says an event announcing nothing does so in as many words, rather than drawing an untouched control', () => {
        open([]);

        expect(screen.getByRole('dialog').textContent).toContain("No reminder — you won't be notified");
        expect(chosen()).toEqual([]);
    });

    it('adds a typed lead in the unit that is pressed', () => {
        open([]);

        typeLead('3', 'hrs');

        expect(chosen()).toEqual(['3 hours before']);
    });

    it('adds nothing where what was typed is longer than a reminder may state', () => {
        open([]);

        typeLead('30', 'days');

        expect(chosen()).toEqual([]);
    });

    it('turns every reminder off at once, which is a decision the event then states', () => {
        open([1440, 60, 15]);

        fireEvent.click(screen.getByRole('button', { name: 'Turn all off' }));

        expect(chosen()).toEqual([]);
        expect(screen.getByRole('dialog').textContent).toContain('off');
    });

    it('closes on being done with it, so nothing is reachable that a person cannot leave', () => {
        open();

        fireEvent.click(screen.getByRole('button', { name: 'Done' }));

        expect(screen.getByRole('dialog', { hidden: true }).hasAttribute('open')).toBe(false);
    });
});
