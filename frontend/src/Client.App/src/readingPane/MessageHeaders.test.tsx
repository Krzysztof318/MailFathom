// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailMessageHeaders, MailParticipant } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { storeLocale } from '../localization/locale';
import { pl } from '../localization/pl';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';

import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { MessageHeaders } from './MessageHeaders';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';

const headers: MailMessageHeaders = {
    subject: 'Quarterly invoice',
    sentAt: '2026-08-31T09:41:00+00:00',
    receivedAt: '2026-08-31T09:41:10+00:00',
    participants: [
        { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
        { role: 'To', address: 'reader@example.invalid', displayName: null },
        { role: 'Cc', address: 'archive@example.invalid', displayName: 'Archive' },
    ],
    messageId: 'abc@example.invalid',
    inReplyTo: null,
    references: [],
};

// The message the head's acts are about, which this file states only so the head can be drawn: what those acts do is
// `mailSpace/HeadActs.test.tsx`'s.
const message: ActedMessage = {
    storedEmailId: 'a1',
    account: 'reader@example.invalid',
    folder: 'INBOX',
    unread: false,
    flagged: false,
};

// Found by the words a person reads and then read as the element it is, because whether a disclosure is open is a
// property of that element rather than something jsdom expresses by hiding what is inside it.
function disclosure(): HTMLDetailsElement {
    const opened = screen.getByText(/^everybody else \(/u).closest('details');

    if (opened === null) {
        throw new Error('The summary naming the other participants is not inside a disclosure.');
    }

    return opened;
}

// The *fullscreen* toolbar control writes this, and the toolbar is not what a head is drawn beside — so a test about
// what the control takes away states the value rather than pressing the control that sets it.
function HidesThePanels({ hidden }: { readonly hidden: boolean }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise({ panelsHidden: hidden });
    }, [revise, hidden]);

    return null;
}

const author: MailParticipant = { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' };

const conversation = '0198f4a1-0000-7000-8000-00000000b001';

function drawing(
    written: Partial<MailMessageHeaders> = {},
    panelsHidden = false,
    handToAgent: (handOver: AgentHandOver) => void = vi.fn(),
    thread: string | null = conversation,
    messagesInThread: number | null = null,
): void {
    render(
        <LocalizationProvider>
            <AgentHandOverContext value={handToAgent}>
                <WorkspaceProvider>
                    <HidesThePanels hidden={panelsHidden} />
                    <MessageHeaders
                        headers={{ ...headers, ...written }}
                        message={message}
                        thread={thread}
                        messagesInThread={messagesInThread}
                    />
                </WorkspaceProvider>
            </AgentHandOverContext>
        </LocalizationProvider>,
    );
}

// The width the head composes at is the one thing about it jsdom cannot answer, so a test about either shape states
// which it is about. Every test that states neither inherits the narrow reading: `vitest.setup.ts` puts a `matchMedia`
// over jsdom's missing one that answers `false` to every query, so the head composes as it does at the phone.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

function atWorkspaceWidth(wideEnough: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: wideEnough,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

// A zone pinned for one test is put back for the reason a fake clock is released: it is the worker's, not the file's.
const machineZone = process.env['TZ'];

afterEach(() => {
    // Assigning `undefined` to an environment variable writes the string "undefined", which is a zone of its own.
    if (machineZone === undefined) {
        Reflect.deleteProperty(process.env, 'TZ');
    } else {
        process.env['TZ'] = machineZone;
    }

    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('MessageHeaders', () => {
    it('draws the subject as the heading of the message', () => {
        drawing();

        expect(screen.getByRole('heading', { name: 'Quarterly invoice', level: 2 })).toBeDefined();
    });

    it('says a message carries no subject rather than heading it with nothing', () => {
        drawing({ subject: null });

        expect(screen.getByRole('heading', { name: 'No subject', level: 2 })).toBeDefined();
    });

    it('draws the author with the address beside the name the sender wrote', () => {
        drawing();

        expect(screen.getByText('Billing <billing@example.invalid>')).toBeDefined();
    });

    it('draws an author who wrote no name as the address alone', () => {
        drawing({ participants: [{ role: 'From', address: 'billing@example.invalid', displayName: null }] });

        expect(screen.getByText('billing@example.invalid')).toBeDefined();
    });

    it('says a message names nobody as its author rather than drawing an empty line', () => {
        drawing({ participants: [] });

        expect(screen.getByText('This message names nobody as its author.')).toBeDefined();
    });

    it('places the instant the author wrote against the reader own clock, on the line naming the author', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        drawing({ sentAt: '2026-08-31T23:12:00+00:00' });

        // Written out rather than compared against a formatter built here, because that comparison passes just as
        // happily for a screen that pinned a zone of its own — which is the defect this is about. Warsaw is two hours
        // ahead in August, which is what carries the instant into the next day.
        expect(screen.getByText('9/1/26, 1:12 AM')).toBeDefined();
        expect(screen.getByText('Billing <billing@example.invalid>')).toBeDefined();
    });

    it('reads the same instant a day earlier for a reader west of the sender', () => {
        process.env['TZ'] = 'America/Los_Angeles';

        drawing({ sentAt: '2026-08-31T23:12:00+00:00' });

        expect(screen.getByText('8/31/26, 4:12 PM')).toBeDefined();
    });

    it('keeps the instant the service sent beside the words, so what a machine reads is not the wording', () => {
        process.env['TZ'] = 'Europe/Warsaw';

        drawing();

        expect(screen.getByText('8/31/26, 11:41 AM').getAttribute('datetime')).toBe('2026-08-31T09:41:00+00:00');
    });

    it('draws when this deployment recorded the message nowhere in the head, which is the copy rather than the message', () => {
        drawing();

        expect(screen.queryByText(/^Received /u)).toBeNull();
    });

    it('says the sender wrote no readable date rather than drawing a broken one', () => {
        drawing({ sentAt: 'the day before yesterday' });

        expect(screen.getByText('The sender wrote no date this client can read.')).toBeDefined();
    });

    it('keeps everybody else behind a disclosure, so a message to two hundred people is not a screen of addresses', () => {
        drawing();

        expect(disclosure().open).toBe(false);
    });

    it('names everybody else under the header each address appeared in, once the disclosure is opened', () => {
        drawing();

        fireEvent.click(screen.getByText('everybody else (2)'));

        expect(disclosure().open).toBe(true);
        expect(screen.getByText('To')).toBeDefined();
        expect(screen.getByText('reader@example.invalid')).toBeDefined();
        expect(screen.getByText('Archive <archive@example.invalid>')).toBeDefined();
    });

    it('folds everybody else away again on a second press, and names what the next press does each time', async () => {
        drawing();

        const line = screen.getByText('everybody else (2)');

        fireEvent.click(line);
        await waitFor(() => {
            expect(line.closest('summary')?.title).toBe('Collapse the address details');
        });

        fireEvent.click(line);
        await waitFor(() => {
            expect(line.closest('summary')?.title).toBe('Everybody else this message names');
        });
        expect(disclosure().open).toBe(false);
    });

    it.each([
        ['Sender', 'Submitted by'],
        ['ReplyTo', 'Reply to'],
        ['To', 'To'],
        ['Cc', 'Copy to'],
        ['Bcc', 'Blind copy to'],
    ] as const)('names somebody the message lists as %s under the header "%s"', (role, header) => {
        drawing({ participants: [author, { role, address: 'named@example.invalid', displayName: null }] });

        fireEvent.click(screen.getByText('everybody else (1)'));

        expect(screen.getByRole('term').textContent).toBe(header);
        expect(screen.getByRole('definition').textContent).toBe('named@example.invalid');
    });

    it('lists the headers in the order a reader reads them rather than the order the message wrote them', () => {
        drawing({
            participants: [
                author,
                { role: 'Bcc', address: 'hidden@example.invalid', displayName: null },
                { role: 'Cc', address: 'archive@example.invalid', displayName: null },
                { role: 'To', address: 'reader@example.invalid', displayName: null },
                { role: 'ReplyTo', address: 'answers@example.invalid', displayName: null },
                { role: 'Sender', address: 'relay@example.invalid', displayName: null },
            ],
        });

        fireEvent.click(screen.getByText('everybody else (5)'));

        expect(screen.getAllByRole('term').map((header) => header.textContent)).toStrictEqual([
            'Submitted by',
            'Reply to',
            'To',
            'Copy to',
            'Blind copy to',
        ]);
    });

    it('counts the messages the conversation holds at the end of the author line', () => {
        drawing({}, false, vi.fn(), conversation, 3);

        expect(screen.getByText('thread: 3 messages')).toBeDefined();
    });

    it('counts nothing where the conversation holds the message alone', () => {
        drawing({}, false, vi.fn(), conversation, 1);

        expect(screen.getByText('Billing <billing@example.invalid>')).toBeDefined();
        expect(screen.queryByText(/^thread: /u)).toBeNull();
    });

    // Polish is where a count has to be read: two through four take one form and five upwards another, and twenty-two
    // is back in the first. The catalogue entry is read out of `pl.ts` because what is proven is that the other
    // language reached the line, and the number is asked of `Intl` the way the screen asked it.
    it.each([
        [2, 'thread.held.few'],
        [5, 'thread.held.many'],
        [22, 'thread.held.few'],
    ] as const)('counts a conversation of %i messages in the form Polish takes at that number', (held, form) => {
        storeLocale('pl');

        drawing({}, false, vi.fn(), conversation, held);

        expect(screen.getByText(pl[form].replace('{count}', new Intl.NumberFormat('pl').format(held)))).toBeDefined();
    });

    it('draws a display name written to look like markup as the characters the sender wrote', () => {
        drawing({
            participants: [
                { role: 'From', address: 'billing@example.invalid', displayName: '<script>alert(1)</script>' },
            ],
        });

        expect(screen.getByText('<script>alert(1)</script> <billing@example.invalid>')).toBeDefined();
    });
});

describe('MessageHeaders at the width its column has', () => {
    // The head is the one place in the reading column a composition changes what is drawn rather than how it is laid
    // out, so it is asked at both widths: the words go and the control stays, named by what it does either way.
    it('draws the three acts as words alone wherever the column is not the whole screen', () => {
        atWorkspaceWidth(true);
        drawing();

        expect(screen.getByText('Reply')).toBeDefined();
        expect(screen.getByText('Forward')).toBeDefined();
        expect(screen.getByText('Flag')).toBeDefined();
    });

    it('draws the three acts as symbols alone at the width the column is the whole screen', () => {
        atWorkspaceWidth(false);
        drawing();

        expect(screen.queryByText('Reply')).toBeNull();
        expect(screen.queryByText('Forward')).toBeNull();
        expect(screen.queryByText('Flag')).toBeNull();
    });

    it('carries the way back to the list at the width the column is the whole screen, and nowhere else', () => {
        atWorkspaceWidth(false);
        drawing();

        expect(screen.getByRole('button', { name: 'Back to the list' })).toBeDefined();
    });

    it('carries no way back where the list stands beside the message', () => {
        atWorkspaceWidth(true);
        drawing();

        expect(screen.queryByRole('button', { name: 'Back to the list' })).toBeNull();
    });

    it('goes with the panels where the column stands beside the list, which is what the control takes away', () => {
        atWorkspaceWidth(true);
        drawing({}, true);

        expect(screen.queryByRole('heading', { name: 'Quarterly invoice' })).toBeNull();
    });

    it('stays at the width the column is the whole screen, because it carries the way back to the list', () => {
        atWorkspaceWidth(false);
        drawing({}, true);

        expect(screen.getByRole('heading', { name: 'Quarterly invoice' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Back to the list' })).toBeDefined();
    });

    it('offers handing the conversation to the agent everywhere but on a phone', () => {
        const handToAgent = vi.fn();
        atWorkspaceWidth(true);
        drawing({}, false, handToAgent);

        fireEvent.click(screen.getByRole('button', { name: 'Ask' }));

        expect(handToAgent).toHaveBeenCalledWith({
            scope: { kind: 'thread', subject: conversation },
            title: 'Quarterly invoice',
        });

        cleanup();
        atWorkspaceWidth(false);
        drawing();

        expect(screen.queryByText('Ask')).toBeNull();
    });

    // A message the deployment placed in no conversation has nothing the conversation routes accept as a subject, so
    // the head offers no way to the agent rather than a hand-over naming nothing.
    it('offers no way to the agent for a message that belongs to no conversation', () => {
        const handToAgent = vi.fn();
        atWorkspaceWidth(true);
        drawing({}, false, handToAgent, null);

        expect(screen.queryByRole('button', { name: 'Ask' })).toBeNull();
        expect(handToAgent).not.toHaveBeenCalled();
    });

    it('offers the address details from the sender line itself, and names what pressing it does either way', () => {
        atWorkspaceWidth(true);
        drawing();

        const line = screen.getByText('everybody else (2)').closest('summary');

        expect(line?.title).toBe('Everybody else this message names');

        fireEvent(disclosure(), new Event('toggle', { bubbles: false }));
        expect(disclosure().open).toBe(false);
    });

    it('names each act the same way at either width, so nothing is reachable at one and nameless at the other', () => {
        atWorkspaceWidth(false);
        drawing();

        // Drawn with neither a composer nor a mailbox act above them, which is what this harness is: each says why it
        // cannot act rather than going nameless. What each does when it can is `mailSpace/HeadActs.test.tsx`'s.
        expect(screen.getByRole('button', { name: 'Reply — not built yet' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Forward — not built yet' })).toBeDefined();
        expect(
            screen.getByRole('button', {
                name: 'Flag — this credential may not change mail on your mail server.',
            }),
        ).toBeDefined();
    });
});
