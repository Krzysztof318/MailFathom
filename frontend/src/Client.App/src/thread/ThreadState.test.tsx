// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactElement } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailThreadMessage, MailThreadState, MailThreadStateEntry } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ThreadState } from './ThreadState';

// The block is drawn three ways and the width decides which, so every test here states the width it is about. jsdom
// answers every media query `false` unless something says otherwise, which is the phone reading.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

/** The window is as wide as the widest breakpoint the client has, which is the composition the cards are drawn in. */
function theDesktopComposition(): void {
    widthsMatching(() => true);
}

/** The window is wide enough for the workspace and not for the desktop, which is the tablet and the fold. */
function theTabletComposition(): void {
    widthsMatching((query) => query.includes('43.75rem'));
}

function widthsMatching(matches: (query: string) => boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: matches(query),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function message(id: string, position: number): MailThreadMessage {
    return {
        position,
        answeredId: null,
        email: {
            id,
            account: 'work',
            folder: 'INBOX',
            threadId: 'a-conversation',
            subject: 'The quarterly figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: null,
            senderAddress: 'karolina@example.invalid',
            senderDisplayName: 'Karolina Nowak',
            toAddresses: [],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            sizeOctets: 512,
            preview: null,
            enrichment: null,
            threadMessageCount: null,
        },
        message: null,
        body: null,
    };
}

const held: readonly MailThreadMessage[] = [message('one', 0), message('two', 1)];

function entry(overrides: Partial<MailThreadStateEntry> = {}): MailThreadStateEntry {
    return {
        aspect: 'Agreement',
        text: 'The response time stays at two hours.',
        owedBy: null,
        dueAt: null,
        sources: [{ kind: 'email', email: 'two' }],
        ...overrides,
    };
}

function stateOf(entries: readonly MailThreadStateEntry[], coverage: MailThreadState['coverage'] = 'WholeThread') {
    return { threadId: 'a-conversation', coverage, derivedAt: '2026-09-08T09:00:00+00:00', entries };
}

function drawing(
    state: MailThreadState | null,
    reading = false,
    onFollowSource: (storedEmailId: string) => void = () => undefined,
    online = true,
): ReactElement {
    return (
        <LocalizationProvider>
            <ThreadState
                state={state}
                reading={reading}
                online={online}
                messages={held}
                onFollowSource={onFollowSource}
            />
        </LocalizationProvider>
    );
}

describe('ThreadState', () => {
    it('says what the conversation settled, under the label for that aspect', () => {
        theDesktopComposition();

        render(drawing(stateOf([entry()])));

        expect(screen.getByText('Agreed')).toBeDefined();
        expect(screen.getByText('The response time stays at two hours.')).toBeDefined();
    });

    it('says who owes a commitment and when it falls due, which is what makes it one', () => {
        theDesktopComposition();

        render(
            drawing(
                stateOf([
                    entry({
                        aspect: 'Commitment',
                        text: 'Karolina sends the revised figures.',
                        owedBy: 'Karolina',
                        dueAt: '2026-09-12T09:00:00+00:00',
                    }),
                ]),
            ),
        );

        expect(screen.getByText(/Owed by Karolina, due /u)).toBeDefined();
    });

    it('says only the half a commitment the conversation gave no date for has', () => {
        theDesktopComposition();

        render(drawing(stateOf([entry({ aspect: 'Commitment', owedBy: 'Karolina' })])));

        expect(screen.getByText('Owed by Karolina')).toBeDefined();
    });

    // The whole point of the block: a statement stands on a message, and following it takes the reader there.
    it('reveals the message a statement rests on when its source is followed', () => {
        theDesktopComposition();
        const followed = vi.fn();

        render(drawing(stateOf([entry()]), false, followed));

        fireEvent.click(screen.getByRole('button', { name: 'message 2 · Karolina' }));

        expect(followed).toHaveBeenCalledWith('two');
    });

    it('draws no source for a statement resting on a message this conversation has not read', () => {
        theDesktopComposition();

        render(drawing(stateOf([entry({ sources: [{ kind: 'email', email: 'nine' }] })])));

        expect(screen.queryByRole('button', { name: /message/u })).toBeNull();
    });

    // A conversation of one message has one message to cite, and following that citation would reveal what the reader
    // is already looking at.
    it('draws no source at all in a conversation of one message', () => {
        theDesktopComposition();

        render(
            <LocalizationProvider>
                <ThreadState
                    state={stateOf([entry({ sources: [{ kind: 'email', email: 'one' }] })])}
                    reading={false}
                    online={true}
                    messages={[message('one', 0)]}
                    onFollowSource={() => undefined}
                />
            </LocalizationProvider>,
        );

        expect(screen.queryByRole('button', { name: /message/u })).toBeNull();
    });

    // The tablet chip holds a label and a line, which is what its width has room for.
    it('draws the statements without their sources in the tablet composition', () => {
        theTabletComposition();

        render(drawing(stateOf([entry()])));

        expect(screen.getByText('The response time stays at two hours.')).toBeDefined();
        expect(screen.queryByRole('button', { name: /message/u })).toBeNull();
    });

    // The sheet is drawn closed rather than absent, so that the platform keeps focus where it found it. A stylesheet
    // is what takes a closed one off the screen, and jsdom compiles none — so what is asserted here is the state the
    // element is in rather than what is in the document.
    it('says the first statement on one line on a phone, and opens the rest as a sheet', () => {
        render(
            drawing(stateOf([entry(), entry({ aspect: 'OpenQuestion', text: 'What the upper indexation limit is.' })])),
        );

        const sheet = screen.getByRole<HTMLDialogElement>('dialog', { hidden: true });

        expect(sheet.open).toBe(false);
        expect(screen.getByText('What the upper indexation limit is.').closest('dialog')).toBe(sheet);

        fireEvent.click(screen.getByRole('button', { name: 'Where this conversation stands' }));

        expect(sheet.open).toBe(true);
    });

    it('closes the sheet a phone opened, so nothing is reachable that cannot be left', () => {
        render(drawing(stateOf([entry()])));

        fireEvent.click(screen.getByRole('button', { name: 'Where this conversation stands' }));
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(screen.getByRole<HTMLDialogElement>('dialog', { hidden: true }).open).toBe(false);
    });

    // The same rule the desktop card keeps, on the composition that draws the citation in a sheet instead: the sheet is
    // reached from the conversation it would cite back into.
    it('draws no source in the sheet either, in a conversation of one message', () => {
        render(
            <LocalizationProvider>
                <ThreadState
                    state={stateOf([entry({ sources: [{ kind: 'email', email: 'one' }] })])}
                    reading={false}
                    online={true}
                    messages={[message('one', 0)]}
                    onFollowSource={() => undefined}
                />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Where this conversation stands' }));

        expect(screen.queryByRole('button', { name: /message/u })).toBeNull();
    });

    // Absence is the common answer rather than an exception: a deployment that never turned the derivation on and one
    // that has not reached this conversation yet both arrive here.
    it('says a conversation nothing has been derived about, rather than reporting a failure', () => {
        theDesktopComposition();

        render(drawing(null));

        expect(screen.getByRole('status').textContent).toBe(
            'Nothing has been derived about where this conversation stands.',
        );
    });

    it('says a conversation too long to derive a state for, rather than drawing one from part of it', () => {
        theDesktopComposition();

        render(drawing(stateOf([], 'ThreadTooLarge')));

        expect(screen.getByRole('status').textContent).toContain('longer than a state can be derived from');
    });

    // A conversation already on the screen is what makes this reachable: the frame's own offline sentence is drawn
    // only where there is nothing to draw instead, so a block left saying "reading" would be the only thing on the
    // screen that never resolves and nothing would say why.
    it('says the machine has no network, rather than looking as though a read were in flight', () => {
        theDesktopComposition();

        render(drawing(null, true, () => undefined, false));

        expect(screen.getByRole('status').textContent).toContain('offline');
    });

    it('says it is reading, so the block does not look finished while the answer is in flight', () => {
        theDesktopComposition();

        render(drawing(null, true));

        expect(screen.getByRole('status').textContent).toBe('Reading where this conversation stands…');
    });
});
