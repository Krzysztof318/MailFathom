// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { useOpenTabs } from './useOpenTabs';

// The hook is exercised through a component rather than in isolation, because what it is for is that the tab set and
// what the reading column draws move together: a harness that read only the returned value would prove half of it.
const openTheQuarterly = 'Open the quarterly figures.';
const openTheInvoice = 'Open the invoice.';
const openTheQuarterlyAgain = 'Open the quarterly figures a second time.';
const readDownTheConversation = 'Read down the conversation, as the pane would.';
const closeEverything = 'Close everything.';
const reopenTheLastRead = 'Open the last message read.';
const showTheMarkup = 'Show the quarterly figures as the sender sent it.';
const closeTheMarkup = 'Close the markup surface.';

function Tabs({ inTabs }: { readonly inTabs: boolean }) {
    const { workspace, revise } = useWorkspace();
    const tabs = useOpenTabs(inTabs);
    const titles = tabs.tabs.map((tab) => tab.title ?? '—');
    const open = `Open: ${titles.join(', ') || 'nothing'}`;
    const reading = `Reading: ${workspace.selection ?? 'nothing'}`;
    const inFrontOfIt = `In front of it: ${workspace.conversation?.threadId ?? 'nothing'}`;
    const markup = `Markup shown for: ${workspace.fullHtml ?? 'nothing'}`;
    const emptied = `Emptied by closing: ${String(tabs.emptiedByClosing)}`;

    return (
        <>
            <p>{open}</p>
            <p>{reading}</p>
            <p>{inFrontOfIt}</p>
            <p>{markup}</p>
            <p>{emptied}</p>

            <button
                type="button"
                onClick={() => {
                    tabs.openMail('message-1', 'The quarterly figures');
                }}
            >
                {openTheQuarterly}
            </button>

            <button
                type="button"
                onClick={() => {
                    tabs.openMail('message-2', 'The invoice');
                }}
            >
                {openTheInvoice}
            </button>

            <button
                type="button"
                onClick={() => {
                    tabs.openMail('message-1', 'The quarterly figures');
                }}
            >
                {openTheQuarterlyAgain}
            </button>

            <button
                type="button"
                onClick={() => {
                    revise({ conversation: { threadId: 'thread-9', openAt: 'message-9' } });
                }}
            >
                {readDownTheConversation}
            </button>

            {tabs.tabs.map((tab, at) => {
                const bringForward = `Bring forward ${titles[at] ?? '—'}`;

                return (
                    <button
                        key={tab.key}
                        type="button"
                        onClick={() => {
                            tabs.activate(tab.key);
                        }}
                    >
                        {bringForward}
                    </button>
                );
            })}

            {tabs.tabs.map((tab, at) => {
                const close = `Close ${titles[at] ?? '—'}`;

                return (
                    <button
                        key={tab.key}
                        type="button"
                        onClick={() => {
                            tabs.close(tab.key);
                        }}
                    >
                        {close}
                    </button>
                );
            })}

            <button
                type="button"
                onClick={() => {
                    tabs.openFullHtml('message-1', 'The quarterly figures');
                }}
            >
                {showTheMarkup}
            </button>

            <button type="button" onClick={tabs.closeFullHtml}>
                {closeTheMarkup}
            </button>

            <button type="button" onClick={tabs.closeEverything}>
                {closeEverything}
            </button>

            {tabs.reopenLastRead === null ? null : (
                <button type="button" onClick={tabs.reopenLastRead}>
                    {reopenTheLastRead}
                </button>
            )}
        </>
    );
}

function renderTabs(inTabs: boolean): void {
    render(
        <WorkspaceProvider>
            <Tabs inTabs={inTabs} />
        </WorkspaceProvider>,
    );
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

function open(): string {
    return screen.getByText(/^Open: /).textContent;
}

function reading(): string {
    return screen.getByText(/^Reading: /).textContent;
}

function markupShownFor(): string {
    return screen.getByText(/^Markup shown for: /).textContent;
}

describe('useOpenTabs, working in tabs', () => {
    it('opens each message beside the last and reads the one just opened', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);

        expect(open()).toBe('Open: The quarterly figures, The invoice');
        expect(reading()).toBe('Reading: message-2');
    });

    it('leaves the message being read where it is when it is opened a second time', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(readDownTheConversation);
        press(openTheQuarterlyAgain);

        expect(open()).toBe('Open: The quarterly figures');
        expect(screen.getByText('In front of it: thread-9')).toBeDefined();
    });

    it('brings a tab forward at the place it was left rather than at the message it was opened from', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(readDownTheConversation);
        press(openTheInvoice);

        expect(screen.getByText('In front of it: nothing')).toBeDefined();

        press('Bring forward The quarterly figures');

        expect(reading()).toBe('Reading: message-1');
        expect(screen.getByText('In front of it: thread-9')).toBeDefined();
    });

    it('leaves what is in front of a message alone when its own tab is pressed again', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(readDownTheConversation);

        // A tab's `opened` is where it was *left*, written as it stops being active, so the tab in front holds a copy
        // that is stale for exactly as long as somebody is using it. Bringing it forward again would revise the
        // workspace back to that copy — closing the conversation the reader is reading, which they never asked for and
        // cannot undo.
        press('Bring forward The quarterly figures');

        expect(reading()).toBe('Reading: message-1');
        expect(screen.getByText('In front of it: thread-9')).toBeDefined();
    });

    it('reads the last remaining tab when the one being read is closed', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);
        press('Close The invoice');

        expect(open()).toBe('Open: The quarterly figures');
        expect(reading()).toBe('Reading: message-1');
    });

    it('leaves what is being read alone when a tab beside it is closed', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);
        press('Close The quarterly figures');

        expect(reading()).toBe('Reading: message-2');
    });

    it('says closing is what left nothing open, so the empty state knows to take focus', () => {
        renderTabs(true);

        expect(screen.getByText('Emptied by closing: false')).toBeDefined();

        press(openTheQuarterly);
        press('Close The quarterly figures');

        expect(open()).toBe('Open: nothing');
        expect(reading()).toBe('Reading: nothing');
        expect(screen.getByText('Emptied by closing: true')).toBeDefined();
    });

    it('closes everything at once, and reads nothing afterwards', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);
        press(closeEverything);

        expect(open()).toBe('Open: nothing');
        expect(reading()).toBe('Reading: nothing');
    });

    it('offers no way back before anything has been read', () => {
        renderTabs(true);

        expect(screen.queryByRole('button', { name: reopenTheLastRead })).toBeNull();
    });

    it('opens the last message read again, at the place it was closed from', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(readDownTheConversation);
        press(closeEverything);
        press(reopenTheLastRead);

        expect(open()).toBe('Open: The quarterly figures');
        expect(reading()).toBe('Reading: message-1');
        expect(screen.getByText('In front of it: thread-9')).toBeDefined();
        expect(screen.getByText('Emptied by closing: false')).toBeDefined();
    });
});

describe('useOpenTabs, not working in tabs', () => {
    it('replaces what is open, so the one tab held is what is on the screen', () => {
        renderTabs(false);

        press(openTheQuarterly);
        press(openTheInvoice);

        expect(open()).toBe('Open: The invoice');
        expect(reading()).toBe('Reading: message-2');
    });
});

// The markup surface takes one of the two shapes the space has, and which one it takes decides where closing it goes
// back to. Both are asserted, because a surface that returned to the wrong place is the defect this split exists to
// avoid rather than a preference about layout.
describe('useOpenTabs opening the sender own markup', () => {
    it('gives the markup a tab of its own where the person works in tabs', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);
        press(showTheMarkup);

        expect(open()).toBe('Open: The quarterly figures, The invoice, The quarterly figures');
        expect(markupShownFor()).toBe('Markup shown for: message-1');
    });

    it('closes that tab back to whatever else was open rather than to the message it came from', () => {
        renderTabs(true);

        press(openTheQuarterly);
        press(openTheInvoice);
        press(showTheMarkup);
        press(closeTheMarkup);

        expect(open()).toBe('Open: The quarterly figures, The invoice');
        expect(reading()).toBe('Reading: message-2');
        expect(markupShownFor()).toBe('Markup shown for: nothing');
    });

    it('stands the markup in front of the message where the person does not work in tabs', () => {
        renderTabs(false);

        press(openTheQuarterly);
        press(showTheMarkup);

        expect(open()).toBe('Open: The quarterly figures');
        expect(reading()).toBe('Reading: message-1');
        expect(markupShownFor()).toBe('Markup shown for: message-1');
    });

    it('closes it back to the message it was opened from where there is one reading column', () => {
        renderTabs(false);

        press(openTheQuarterly);
        press(showTheMarkup);
        press(closeTheMarkup);

        expect(reading()).toBe('Reading: message-1');
        expect(markupShownFor()).toBe('Markup shown for: nothing');
    });
});
