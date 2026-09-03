// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { activated, closed, nothingOpen, nothingOpened, opened, tabFor, tabIn, type OpenTabs } from './openTabs';

const quarterly = tabFor('thread', 'message-1', 'The quarterly figures', {
    selection: 'message-1',
    conversation: null,
    fullHtml: null,
});

const invoice = tabFor('thread', 'message-2', 'The invoice', {
    selection: 'message-2',
    conversation: null,
    fullHtml: null,
});

const nowhere = nothingOpened;

function twoOpen(): OpenTabs {
    return opened(opened(nothingOpen, quarterly, nowhere, true), invoice, nowhere, true);
}

describe('tabFor', () => {
    it('identifies a tab by what it is and what it holds, so two kinds of the same thing are two tabs', () => {
        expect(tabFor('thread', 'message-1', null).key).not.toBe(tabFor('fullHtml', 'message-1', null).key);
    });

    it('opens a tab that is not a message beside nothing being read', () => {
        expect(tabFor('draft', 'draft-1', 'New message').opened).toEqual(nothingOpened);
    });
});

describe('opened', () => {
    it('adds a tab beside what is already open and reads the one just opened', () => {
        const open = twoOpen();

        expect(open.tabs.map((tab) => tab.key)).toEqual([quarterly.key, invoice.key]);
        expect(open.active).toBe(invoice.key);
    });

    it('brings a tab already open forward instead of adding a second one', () => {
        const again = opened(twoOpen(), quarterly, nowhere, true);

        expect(again.tabs).toHaveLength(2);
        expect(again.active).toBe(quarterly.key);
    });

    it('leaves the tab it moved off holding where the reader had got to in it', () => {
        const conversation = { threadId: 'thread-1', openAt: 'message-1' };
        const open = opened(twoOpen(), quarterly, { selection: 'message-2', conversation, fullHtml: null }, true);

        expect(tabIn(open, invoice.key)?.opened).toEqual({ selection: 'message-2', conversation, fullHtml: null });
    });

    it('replaces what is open where the person is not working in tabs, so one tab is what is on the screen', () => {
        const open = opened(twoOpen(), tabFor('thread', 'message-3', 'A third'), nowhere, false);

        expect(open.tabs.map((tab) => tab.title)).toEqual(['A third']);
        expect(open.active).toBe('thread:message-3');
    });
});

describe('activated', () => {
    it('reads the tab named and leaves the one it moved off holding where it was', () => {
        const open = activated(twoOpen(), quarterly.key, {
            selection: 'message-2',
            conversation: null,
            fullHtml: null,
        });

        expect(open.active).toBe(quarterly.key);
        expect(tabIn(open, invoice.key)?.opened).toEqual({
            selection: 'message-2',
            conversation: null,
            fullHtml: null,
        });
    });

    it('changes nothing where no tab carries the key', () => {
        const open = twoOpen();

        expect(activated(open, 'thread:message-9', nowhere)).toBe(open);
    });
});

describe('closed', () => {
    it('moves to the last remaining tab when the one being read is closed', () => {
        const open = closed(twoOpen(), invoice.key);

        expect(open.tabs.map((tab) => tab.key)).toEqual([quarterly.key]);
        expect(open.active).toBe(quarterly.key);
    });

    it('leaves what is being read alone when another tab is closed', () => {
        expect(closed(twoOpen(), quarterly.key).active).toBe(invoice.key);
    });

    it('leaves nothing open when the last tab is closed', () => {
        expect(closed(closed(twoOpen(), invoice.key), quarterly.key)).toEqual(nothingOpen);
    });
});
