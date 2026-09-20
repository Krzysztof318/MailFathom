// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CalendarEvent, CalendarEventAmendment } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { EventDialog } from './EventDialog';

// The fields resolve a day and a clock reading against the reader's own zone, so the zone is pinned.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

const meeting: CalendarEvent = {
    id: 'review',
    title: 'Review with Anna',
    start: '2026-09-24T09:00:00+02:00',
    end: '2026-09-24T10:00:00+02:00',
    isAllDay: false,
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-01T09:00:00+02:00',
    amendedAt: '2026-09-01T09:00:00+02:00',
};

const fromMail: CalendarEvent = { ...meeting, id: 'lunch', origin: 'Proposed', sourceMessage: 'message-4' };

// What opens the dialog in the test, named here rather than written into the markup: the localization rule holds over
// every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

function Asking({
    event,
    remindersAsked,
    onAmend,
    onAskDeletion,
    onOpenSource,
    onLeft,
}: {
    readonly event: CalendarEvent;
    readonly remindersAsked: boolean;
    readonly onAmend: (amendment: CalendarEventAmendment) => void;
    readonly onAskDeletion: () => void;
    readonly onOpenSource: (messageId: string) => void;
    readonly onLeft: () => void;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asks}
            </button>

            <EventDialog
                event={event}
                asked={asked}
                said={null}
                remindersAsked={remindersAsked}
                onAmend={onAmend}
                onAskDeletion={onAskDeletion}
                onOpenSource={onOpenSource}
                onLeft={onLeft}
            />
        </>
    );
}

function drawDialog(
    event = meeting,
    { remindersAsked = false }: { remindersAsked?: boolean } = {},
): {
    readonly onAmend: ReturnType<typeof vi.fn>;
    readonly onAskDeletion: ReturnType<typeof vi.fn>;
    readonly onOpenSource: ReturnType<typeof vi.fn>;
    readonly onLeft: ReturnType<typeof vi.fn>;
} {
    const acts = {
        onAmend: vi.fn(),
        onAskDeletion: vi.fn(),
        onOpenSource: vi.fn(),
        onLeft: vi.fn(),
    };

    render(
        <LocalizationProvider>
            <Asking event={event} remindersAsked={remindersAsked} {...acts} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return acts;
}

describe('EventDialog', () => {
    it('reads the event before it offers to change it', () => {
        drawDialog();

        expect(screen.getByRole('heading', { name: 'Review with Anna' })).toBeDefined();
        expect(screen.getByText('Your own')).toBeDefined();
        expect(screen.queryByLabelText('Title')).toBeNull();
    });

    it('says where an event read out of mail came from, and offers the message rather than describing it', () => {
        const { onOpenSource } = drawDialog(fromMail);

        expect(screen.getByText('From mail')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Open the message' }));

        expect(onOpenSource).toHaveBeenCalledWith('message-4');
    });

    it('opens the fields already holding the event, so amending one is changing what is there', () => {
        drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('Review with Anna');
        expect(screen.getByLabelText<HTMLInputElement>('Day').value).toBe('2026-09-24');
        expect(screen.getByLabelText<HTMLInputElement>('Starts').value).toBe('09:00');
        expect(screen.getByLabelText<HTMLInputElement>('Ends').value).toBe('10:00');
    });

    it('states the whole record rather than the field that changed, which is what the route takes', () => {
        const { onAmend } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
        fireEvent.change(screen.getByLabelText('Starts'), { target: { value: '11:00' } });
        fireEvent.change(screen.getByLabelText('Ends'), { target: { value: '12:00' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onAmend).toHaveBeenCalledWith({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24, 11).toISOString(),
            end: new Date(2026, 8, 24, 12).toISOString(),
            isAllDay: false,
            reminders: [],
        });
    });

    it('refuses to save an end that is not after the beginning rather than letting the deployment answer it', () => {
        const { onAmend } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
        fireEvent.change(screen.getByLabelText('Ends'), { target: { value: '08:00' } });

        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onAmend).not.toHaveBeenCalled();
    });

    it('puts the event back where somebody abandoned an amendment', () => {
        drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Something else' } });
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(screen.getByRole('heading', { name: 'Review with Anna' })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('Review with Anna');
    });

    it('opens what announces the event from the event itself, and states the whole record as the panel answers', () => {
        const { onAmend } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'off' }));
        fireEvent.click(
            within(screen.getByRole('dialog', { name: 'Reminders' })).getByRole('button', { name: '15 min before' }),
        );

        // There is no *Save* outside the fields, so what the panel answered is written as it is answered.
        expect(onAmend).toHaveBeenCalledWith({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24, 9).toISOString(),
            end: new Date(2026, 8, 24, 10).toISOString(),
            isAllDay: false,
            reminders: [15],
        });
    });

    it('opens on the panel where what opened the event asked for its reminders rather than for the event', () => {
        drawDialog(meeting, { remindersAsked: true });

        expect(screen.getByRole('dialog', { name: 'Reminders' }).hasAttribute('open')).toBe(true);
    });

    it('raises the question the deletion stands behind rather than deleting the event itself', () => {
        const { onAskDeletion } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Delete the event' }));

        expect(onAskDeletion).toHaveBeenCalledTimes(1);
    });

    it('says it has been left, which is what lets the screen stop holding the event', () => {
        const { onLeft } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(onLeft).toHaveBeenCalledTimes(1);
    });
});
