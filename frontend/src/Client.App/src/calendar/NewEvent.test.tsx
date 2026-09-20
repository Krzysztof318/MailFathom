// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    CalendarEventRecord,
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { NewEvent } from './NewEvent';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The fields resolve a day and a clock reading against the reader's own zone, so the zone is pinned: in UTC what is
// saved and what was typed are the same value and the assertion would prove nothing.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

const standing = new Date(2026, 8, 24, 10);

function deployment(answer: ClientResponse): {
    readonly transport: MailFathomTransport;
    readonly sent: () => readonly ClientRequest[];
} {
    const sent: ClientRequest[] = [];

    return {
        sent: () => sent,
        transport: (request) => {
            sent.push(request);

            return Promise.resolve(answer);
        },
    };
}

function drafted(body: unknown): ClientResponse {
    return { status: 200, headers: {}, body: JSON.stringify(body) };
}

// What opens the dialog in the test, named here rather than written into the markup, exactly as
// `people/NewContact.test.tsx` does it: the localization rule holds over every `.tsx` under the packages.
const asks = 'Ask';

function Asking({
    transport,
    readsDescriptions,
    onSave,
}: {
    readonly transport: MailFathomTransport;
    readonly readsDescriptions: boolean;
    readonly onSave: (record: CalendarEventRecord) => void;
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

            <NewEvent
                asked={asked}
                session={session}
                transport={transport}
                on={standing}
                now={() => standing}
                readsDescriptions={readsDescriptions}
                onSave={onSave}
            />
        </>
    );
}

function drawDialog(
    transport: MailFathomTransport,
    { readsDescriptions = true }: { readsDescriptions?: boolean } = {},
): { readonly onSave: ReturnType<typeof vi.fn> } {
    const onSave = vi.fn();

    render(
        <LocalizationProvider>
            <Asking transport={transport} readsDescriptions={readsDescriptions} onSave={onSave} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return { onSave };
}

/** The description field, found by the part of its label that is words rather than the mark beside them. */
function describedIn(): HTMLElement {
    return screen.getByLabelText(/Describe the event/u);
}

describe('NewEvent', () => {
    it('opens its date field on the day the reader is standing on rather than on nothing', () => {
        drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        expect(screen.getByLabelText<HTMLInputElement>('Day').value).toBe('2026-09-24');
    });

    it('saves nothing until a title and a beginning have been typed', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Starts'), { target: { value: '13:00' } });

        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(false);

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith({
            title: 'Lunch with Anna',
            start: new Date(2026, 8, 24, 13).toISOString(),
            end: null,
            isAllDay: false,
            reminders: [],
            sourceMessage: null,
        });
    });

    it('puts the clock away for a day and writes the day"s own midnight down', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Site visit' } });
        fireEvent.click(screen.getByLabelText('All day'));

        // A day carries no clock reading, so the two fields that would ask for one are gone rather than ignored.
        expect(screen.queryByLabelText('Starts')).toBeNull();
        expect(screen.queryByLabelText('Ends')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith({
            title: 'Site visit',
            start: new Date(2026, 8, 24).toISOString(),
            end: null,
            isAllDay: true,
            reminders: [],
            sourceMessage: null,
        });
    });

    it('puts what the deployment read into the fields and saves nothing on its own', async () => {
        const { transport, sent } = deployment(
            drafted({
                drafted: true,
                spent: false,
                title: 'Lunch with Anna',
                start: '2026-09-24T13:00:00+02:00',
                end: '2026-09-24T14:00:00+02:00',
            }),
        );
        const { onSave } = drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'lunch with Anna on Thursday at one' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Read the description' }));

        expect(await screen.findByText('Filled in from your description. Check it before saving.')).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('Lunch with Anna');
        expect(screen.getByLabelText<HTMLInputElement>('Starts').value).toBe('13:00');
        expect(screen.getByLabelText<HTMLInputElement>('Ends').value).toBe('14:00');

        // Read rather than written: nothing has been saved, and the one request that went out is the reading.
        expect(onSave).not.toHaveBeenCalled();
        expect(sent()).toHaveLength(1);
        expect(sent()[0]?.path).toContain('/calendar/drafts');
    });

    it('sends the instant the person is standing on with its own offset, which is what a relative day is read against', async () => {
        const { transport, sent } = deployment(drafted({ drafted: false, spent: false }));
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'tomorrow at nine' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Read the description' }));

        await waitFor(() => {
            expect(sent()).toHaveLength(1);
        });

        // The literal instant rather than its shape: the offset is what makes *tomorrow at nine* the reader's own
        // tomorrow, and an assertion on the format alone would pass for a client that sent somebody else's.
        const written = JSON.parse(sent()[0]?.body ?? '{}') as { writtenAt?: string };
        expect(written.writtenAt).toBe('2026-09-24T10:00:00+02:00');
    });

    it('says a sentence it could read nothing out of rather than filling the fields with a guess', async () => {
        const { transport } = deployment(drafted({ drafted: false, spent: false }));
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'something' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Read the description' }));

        expect(
            await screen.findByText('No event could be read out of that. Try naming a day and a time.'),
        ).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('');
    });

    it('says a spent allowance as itself, the form beside it still being usable', async () => {
        const { transport } = deployment({ status: 429, headers: {}, body: '' });
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'lunch on Thursday' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Read the description' }));

        expect(
            await screen.findByText('This deployment has used up what it may spend on reading descriptions for now.'),
        ).toBeDefined();
        expect(screen.getByLabelText('Title')).toBeDefined();
    });
});
