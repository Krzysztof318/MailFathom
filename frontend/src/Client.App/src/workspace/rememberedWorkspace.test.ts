// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { rememberedWorkspace, rememberWorkspace } from './rememberedWorkspace';
import { emptyWorkspace, type Workspace } from './useWorkspace';

const storageKey = 'mailfathom.workspace';

const kept: Workspace = {
    scope: { kind: 'folder', accountId: 'work', alias: 'INBOX' },
    collapsed: ['account:personal'],
    mailboxesFolded: true,
    panelsHidden: true,
    selection: 'AAMkAD-42',
    conversation: { threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d', openAt: 'AAMkAD-42' },
    fullHtml: null,
    attachment: null,
    citedAttachment: null,
    fragment: null,
    selected: ['AAMkAD-42', 'AAMkAD-43'],
    question: 'what did Nordwind send',
    askScopeKey: 'account:work',
    askedBefore: [{ question: 'what did they promise', scope: { kind: 'thread', threadId: 'thread-1' } }],
    recentSearches: ['quarterly figures'],
};

function stored(value: unknown): void {
    window.sessionStorage.setItem(storageKey, JSON.stringify(value));
}

// The store is one per file rather than one per test, so what one test kept would be what the next one read back.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('rememberWorkspace', () => {
    it('keeps a workspace the next start reads back whole', () => {
        rememberWorkspace(kept);

        expect(rememberedWorkspace()).toEqual(kept);
    });

    // The one part of the workspace that is mail rather than a name for one, so it is the one part a store never sees.
    it('keeps no part of the message somebody had selected', () => {
        rememberWorkspace({
            ...kept,
            fragment: { messageId: 'AAMkAD-42', text: 'the part of the message somebody pointed at' },
        });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('somebody pointed at');
        expect(rememberedWorkspace().fragment).toBeNull();
    });

    // The same rule reaching the one other place a passage could get into the store. What somebody asked is theirs and
    // is kept; the words they highlighted to ask it are somebody's mail and are not.
    it('keeps a question asked about a passage under the message rather than under the words', () => {
        rememberWorkspace({
            ...kept,
            askedBefore: [
                {
                    question: 'when did they say it would arrive',
                    scope: { kind: 'fragment', messageId: 'AAMkAD-42', text: 'by the end of the month' },
                },
            ],
        });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('by the end of the month');
        expect(rememberedWorkspace().askedBefore).toEqual([
            { question: 'when did they say it would arrive', scope: { kind: 'message', messageId: 'AAMkAD-42' } },
        ]);
    });

    // A consent given for one message after being told what a stranger's markup can carry, which is why a reload finds
    // the surface closed and the control asks again rather than reopening it.
    it('keeps no record of the markup surface having been open', () => {
        rememberWorkspace({ ...kept, fullHtml: 'AAMkAD-42' });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('"fullHtml":"AAMkAD-42"');
        expect(rememberedWorkspace().fullHtml).toBeNull();
    });

    it('opens the surface for nobody where a store was edited to say it was open', () => {
        window.sessionStorage.setItem(storageKey, JSON.stringify({ ...kept, fullHtml: 'AAMkAD-42' }));

        expect(rememberedWorkspace().fullHtml).toBeNull();
    });

    // The file being read is the other part a store never sees: it is a sender's own name for something, and a reload
    // that reopened it would fetch a file nobody asked for again.
    it('keeps no name of the file somebody had open', () => {
        rememberWorkspace({
            ...kept,
            attachment: {
                storedEmailId: 'AAMkAD-42',
                attachment: {
                    position: 0,
                    fileName: 'the-file-somebody-opened.png',
                    wasFileNameNormalized: false,
                    mediaType: 'image/png',
                    sizeOctets: 2_048,
                },
            },
        });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('the-file-somebody-opened');
        expect(rememberedWorkspace().attachment).toBeNull();
    });

    // The other half of that, and the half a store cannot be trusted for: what is read back is refused rather than
    // merely never written, because a store is a place a person can write.
    it('opens no file for anybody where a store was edited to say one was open', () => {
        window.sessionStorage.setItem(
            storageKey,
            JSON.stringify({
                ...kept,
                attachment: {
                    storedEmailId: 'AAMkAD-42',
                    attachment: {
                        position: 0,
                        fileName: 'the-file-nobody-asked-for.png',
                        wasFileNameNormalized: false,
                        mediaType: 'image/png',
                        sizeOctets: 2_048,
                    },
                },
            }),
        );

        expect(rememberedWorkspace().attachment).toBeNull();
    });

    // A citation names a message the rest of the kept workspace does not, so what is asserted here is that identifier
    // rather than the one `kept` already carries in its selection.
    it('keeps no coordinate of the file a search result cited', () => {
        rememberWorkspace({ ...kept, citedAttachment: { storedEmailId: 'AAMkAD-cited', position: 3 } });

        expect(window.sessionStorage.getItem(storageKey)).not.toContain('AAMkAD-cited');
        expect(rememberedWorkspace().citedAttachment).toBeNull();
    });

    it('follows no citation for anybody where a store was edited to carry one', () => {
        window.sessionStorage.setItem(
            storageKey,
            JSON.stringify({ ...kept, citedAttachment: { storedEmailId: 'AAMkAD-cited', position: 3 } }),
        );

        expect(rememberedWorkspace().citedAttachment).toBeNull();
    });
});

describe('rememberedWorkspace', () => {
    it('answers an empty workspace where nothing was kept', () => {
        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    // A workspace this client wrote before it kept searches at all is one it wrote, so it opens on what was kept
    // rather than on nothing.
    it('reads a workspace kept before searches were offered back as one with none', () => {
        const { recentSearches, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, recentSearches: [] });
        expect(recentSearches).toHaveLength(1);
    });

    // A workspace kept before the column could be folded is one this client wrote, so it opens on what was kept with
    // the column at the width every workspace before it was drawn at.
    it('reads a workspace kept before the column could fold as one drawn at the column width', () => {
        const { mailboxesFolded, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, mailboxesFolded: false });
        expect(mailboxesFolded).toBe(true);
    });

    it('refuses a folded column that is not one, a store being a place a person can write', () => {
        stored({ ...emptyWorkspace, mailboxesFolded: 'yes' });

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    // The same pair for the panels, which is the other thing this client keeps about how the space is laid out: a
    // workspace kept before the control existed is one this client wrote, and opens with the panels where every
    // workspace before it was drawn with them.
    it('reads a workspace kept before the panels could be hidden as one drawn with them', () => {
        const { panelsHidden, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, panelsHidden: false });
        expect(panelsHidden).toBe(true);
    });

    it('refuses hidden panels that are not hidden, for the reason a folded column that is not one is refused', () => {
        stored({ ...emptyWorkspace, panelsHidden: 'yes' });

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    // The same pair again for what the intent field holds, which is a workspace kept before the field had a scope of
    // its own: it is one this client wrote, so it opens on what was kept, asking about whatever is on the screen and
    // with nothing offered back.
    it('reads a workspace kept before the field held a scope and a history as one holding neither', () => {
        const { askScopeKey, askedBefore, ...before } = kept;

        stored(before);

        expect(rememberedWorkspace()).toEqual({ ...kept, askScopeKey: null, askedBefore: [] });
        expect(askScopeKey).not.toBeNull();
        expect(askedBefore).toHaveLength(1);
    });

    it('opens on the mailbox the field was pointed at and the questions asked under it', () => {
        stored(kept);

        expect(rememberedWorkspace().askScopeKey).toBe('account:work');
        expect(rememberedWorkspace().askedBefore).toEqual([
            { question: 'what did they promise', scope: { kind: 'thread', threadId: 'thread-1' } },
        ]);
    });

    it.each([
        { kind: 'everything' },
        { kind: 'role', role: 'Sent' },
        { kind: 'account', accountId: 'work' },
        { kind: 'folder', accountId: 'work', alias: 'ARCHIVE-2024' },
    ])('reads back the scope %o a person had chosen', (scope) => {
        stored({ ...emptyWorkspace, scope });

        expect(rememberedWorkspace().scope).toEqual(scope);
    });

    it.each([
        { shape: 'something that is not JSON at all', value: 'workspace' },
        { shape: 'a workspace that is not an object', value: JSON.stringify([]) },
        { shape: 'a scope naming something this client cannot show', value: JSON.stringify({ scope: 'INBOX' }) },
        {
            shape: 'a scope of a kind this client does not have',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'everywhere' } }),
        },
        {
            shape: 'a role this surface does not publish',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'role', role: 'Spam' } }),
        },
        {
            shape: 'a folder scope naming no account',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'folder', alias: 'INBOX' } }),
        },
        {
            shape: 'an account identifier longer than any the service assigned',
            value: JSON.stringify({ ...emptyWorkspace, scope: { kind: 'account', accountId: 'a'.repeat(257) } }),
        },
        {
            shape: 'a folder alias longer than any the service assigned',
            value: JSON.stringify({
                ...emptyWorkspace,
                scope: { kind: 'folder', accountId: 'work', alias: 'a'.repeat(257) },
            }),
        },
        {
            shape: 'folded rows that are not rows',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: [42] }),
        },
        {
            shape: 'a folded row longer than any key this client writes',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: ['a'.repeat(1_025)] }),
        },
        {
            shape: 'more folded rows than a tree has',
            value: JSON.stringify({ ...emptyWorkspace, collapsed: Array.from({ length: 513 }, () => 'account:work') }),
        },
        {
            shape: 'a question longer than anybody typed',
            value: JSON.stringify({ ...emptyWorkspace, question: 'a'.repeat(4_097) }),
        },
        {
            shape: 'a selection that is not an identifier',
            value: JSON.stringify({ ...emptyWorkspace, selection: 7 }),
        },
        {
            shape: 'a selection longer than any identifier the client wrote there',
            value: JSON.stringify({ ...emptyWorkspace, selection: 'a'.repeat(257) }),
        },
        {
            shape: 'messages picked out as something other than a list of them',
            value: JSON.stringify({ ...emptyWorkspace, selected: 'message-1' }),
        },
        {
            shape: 'a message picked out that is not an identifier',
            value: JSON.stringify({ ...emptyWorkspace, selected: [7] }),
        },
        {
            shape: 'a picked-out identifier longer than any the client wrote there',
            value: JSON.stringify({ ...emptyWorkspace, selected: ['a'.repeat(257)] }),
        },
        {
            shape: 'a scope the field was pointed at that is not one',
            value: JSON.stringify({ ...emptyWorkspace, askScope: { kind: 'somewhere' } }),
        },
        {
            shape: 'a scope the field was pointed at naming a folder, which the field cannot offer',
            value: JSON.stringify({
                ...emptyWorkspace,
                askScope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
            }),
        },
        {
            shape: 'a scope the field was pointed at naming a folder role, which the field cannot offer',
            value: JSON.stringify({ ...emptyWorkspace, askScope: { kind: 'role', role: 'Inbox' } }),
        },
        {
            shape: 'questions asked before kept as something other than a list of them',
            value: JSON.stringify({ ...emptyWorkspace, askedBefore: 'what did they promise' }),
        },
        {
            shape: 'a question asked before that is not text',
            value: JSON.stringify({
                ...emptyWorkspace,
                askedBefore: [{ question: 42, scope: { kind: 'thread', threadId: 'thread-1' } }],
            }),
        },
        {
            shape: 'a question asked before about nothing at all',
            value: JSON.stringify({ ...emptyWorkspace, askedBefore: [{ question: 'what did they promise' }] }),
        },
        {
            shape: 'a question asked before about a selection holding no message',
            value: JSON.stringify({
                ...emptyWorkspace,
                askedBefore: [{ question: 'what did they promise', scope: { kind: 'selection', messages: [] } }],
            }),
        },
        {
            shape: 'more questions asked before than one tab keeps',
            value: JSON.stringify({
                ...emptyWorkspace,
                askedBefore: Array.from({ length: 9 }, () => ({
                    question: 'what did they promise',
                    scope: { kind: 'mail', scope: { kind: 'everything' } },
                })),
            }),
        },
        {
            shape: 'searches kept as something other than a list of them',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: 'invoice' }),
        },
        {
            shape: 'a kept search that is not text',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: [7] }),
        },
        {
            shape: 'a kept search longer than this surface ranks against',
            value: JSON.stringify({ ...emptyWorkspace, recentSearches: ['a'.repeat(513)] }),
        },
        {
            shape: 'more kept searches than one tab offers back',
            value: JSON.stringify({
                ...emptyWorkspace,
                recentSearches: Array.from({ length: 9 }, (_, at) => `search-${String(at)}`),
            }),
        },
        {
            shape: 'more messages picked out than one question may be asked about',
            value: JSON.stringify({
                ...emptyWorkspace,
                selected: Array.from({ length: 1_025 }, (_, at) => `message-${String(at)}`),
            }),
        },
        {
            shape: 'a conversation that is not a conversation',
            value: JSON.stringify({ ...emptyWorkspace, conversation: 'a-conversation' }),
        },
        {
            shape: 'a conversation naming no thread',
            value: JSON.stringify({ ...emptyWorkspace, conversation: { openAt: 'message-1' } }),
        },
        {
            shape: 'a conversation opened at something longer than any identifier the client wrote there',
            value: JSON.stringify({
                ...emptyWorkspace,
                conversation: { threadId: 'a-conversation', openAt: 'a'.repeat(257) },
            }),
        },
    ])('opens on nothing rather than on $shape', ({ value }) => {
        window.sessionStorage.setItem(storageKey, value);

        expect(rememberedWorkspace()).toEqual(emptyWorkspace);
    });

    it('opens on the conversation this tab had open, at the message it was opened at', () => {
        rememberWorkspace(kept);

        expect(rememberedWorkspace().conversation).toEqual({
            threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d',
            openAt: 'AAMkAD-42',
        });
    });

    it('opens on no conversation where a tab was reading one message', () => {
        rememberWorkspace({ ...kept, conversation: null });

        expect(rememberedWorkspace().conversation).toBeNull();
    });
});
