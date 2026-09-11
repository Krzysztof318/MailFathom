// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import type { ComposerOpening } from '../composer/composition';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { IntentField } from './IntentField';

const workAccount: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const homeAccount: MailAccount = { ...workAccount, id: 'home', displayName: 'Home' };

// What a deployment that writes no draft looks like to this field, which is the default every case below runs under:
// the bar is the Discover question there, and the drafting cases say so by turning it on.
function composingWith(compose: (opening: ComposerOpening) => void, drafts: boolean): Composing {
    return { offered: true, drafts, opening: null, compose, close: () => undefined };
}

function renderField(accounts: readonly MailAccount[] = [workAccount]): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ComposingContext value={composingWith(() => undefined, false)}>
                    <IntentField accounts={accounts} />
                </ComposingContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

afterEach(() => {
    window.history.replaceState(null, '', '/');
});

describe('IntentField', () => {
    it('asks nothing itself, and goes to the space that will answer', () => {
        window.history.replaceState(null, '', '#/cases');

        renderField();
        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what did Nordwind send' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(window.location.hash).toBe('#/discover');
    });

    it('keeps the question it was given rather than clearing it on the way', () => {
        renderField();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what did Nordwind send' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'what did Nordwind send',
        );
    });

    it('says what the mail space is showing until a scope is chosen, which is the inbox a client opens on', () => {
        renderField();

        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty(
            'value',
            'role:Inbox',
        );
        expect(screen.getByRole('option', { name: 'All mailboxes' })).toBeDefined();
    });

    // The field is the front door, so it is reachable without tabbing out of whatever is being read. The shortcut is
    // announced on the field itself, and a promise a screen reader passes on that nothing listens for is worse than
    // promising none — so both halves are asserted together.
    it('takes focus from anywhere on the shortcut it announces', () => {
        renderField();

        const field = screen.getByRole('searchbox', { name: 'Ask your mail' });

        expect(field.getAttribute('aria-keyshortcuts')).toBe('Control+K Meta+K');

        fireEvent.keyDown(document, { key: 'k', ctrlKey: true });

        expect(field).toBe(document.activeElement);
    });
});

// The workspace reaches this field from gestures made elsewhere — a row ticked in the list, a correspondence opened, a
// passage selected in a message — so what stands in for those here is the same write made on mount. What is asserted
// below is the field's own behaviour once the value is there.
function Standing({ as }: { readonly as: Partial<Workspace> }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise(as);
    }, [revise, as]);

    return null;
}

// What the workspace holds while the field is being driven, which is how *without changing what the mail space
// displays* is proven at all: the scope the list is read under is a value rather than something on this screen.
function Reporting({ onWorkspace }: { readonly onWorkspace: (workspace: Workspace) => void }) {
    const { workspace } = useWorkspace();

    onWorkspace(workspace);

    return null;
}

function fieldStanding(
    as: Partial<Workspace>,
    accounts: readonly MailAccount[] = [workAccount],
    drafts = false,
): { workspace: () => Workspace; composed: ReturnType<typeof vi.fn> } {
    let last: Workspace | null = null;
    const composed = vi.fn();

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ComposingContext value={composingWith(composed, drafts)}>
                    <Standing as={as} />
                    <IntentField accounts={accounts} />
                    <Reporting
                        onWorkspace={(workspace) => {
                            last = workspace;
                        }}
                    />
                </ComposingContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return {
        composed,
        workspace: () => {
            if (last === null) {
                throw new Error('The workspace was never reported.');
            }

            return last;
        },
    };
}

describe('IntentField scope', () => {
    it('says nothing about a passage while the whole message is the scope', () => {
        fieldStanding({ selection: 'AAMkAD-42' });

        expect(screen.queryByRole('option', { name: 'The part you selected' })).toBeNull();
        expect(screen.getByRole('option', { name: 'This message', selected: true })).toBeDefined();
    });

    it('quotes the words a question would be asked about, rather than saying a passage exists', () => {
        fieldStanding({
            selection: 'AAMkAD-42',
            fragment: { messageId: 'AAMkAD-42', text: 'the part of the message somebody pointed at' },
        });

        // Drawn beside the control and said to a reader who cannot see it, which is why the sentence is on the screen
        // twice: being told only that something was narrowed to, and not what, is the disclosure this field prevents.
        const quoted =
            'Asking about the part of this message you selected: \u201Cthe part of the message somebody pointed at\u201D';

        expect(screen.getAllByText(quoted)).toHaveLength(2);
        expect(screen.getByRole('status')).toHaveProperty('textContent', quoted);
    });

    // Widening away from a passage is the same control every other scope is chosen with, and it must not take the
    // highlight off the message: the reader is still looking at the words they selected.
    it('gives the whole message back as the scope without unselecting the words', () => {
        const { workspace: reported } = fieldStanding({
            selection: 'AAMkAD-42',
            fragment: { messageId: 'AAMkAD-42', text: 'the part of the message somebody pointed at' },
        });

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'message:AAMkAD-42' },
        });

        expect(screen.queryByText(/Asking about the part of this message/)).toBeNull();
        expect(reported().fragment).toEqual({
            messageId: 'AAMkAD-42',
            text: 'the part of the message somebody pointed at',
        });
    });

    it('names the correspondence being read as what the question is about', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty(
            'value',
            'thread:thread-1',
        );
        expect(screen.getByRole('option', { name: 'This correspondence' })).toBeDefined();
    });

    it('counts the messages picked out, which are narrower than the correspondence holding them', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null }, selected: ['one', 'two', 'three'] });

        expect(screen.getByRole('option', { name: '3 selected messages', selected: true })).toBeDefined();
        expect(screen.getByRole('option', { name: 'This correspondence' })).toBeDefined();
    });

    it('names the folder and the mailbox it is in', () => {
        fieldStanding({ scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' } });

        expect(screen.getByRole('option', { name: 'Invoices in Work' })).toBeDefined();
    });

    // Four scopes at once, and the person has to be able to reach any of them without deselecting anything: this is
    // the case the issue describes and the one the ladder exists for.
    it('offers the folder, the correspondence, the messages ticked and the passage all at once', () => {
        fieldStanding({
            scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
            conversation: { threadId: 'thread-1', openAt: null },
            selection: 'AAMkAD-42',
            selected: ['AAMkAD-42', 'AAMkAD-43'],
            fragment: { messageId: 'AAMkAD-42', text: 'by the end of the month' },
        });

        for (const name of [
            'The part you selected',
            '2 selected messages',
            'This correspondence',
            'This message',
            'Invoices in Work',
            'Work',
            'All mailboxes',
        ]) {
            expect(screen.getByRole('option', { name })).toBeDefined();
        }
    });

    // The whole point of the field owning a scope of its own: widening a question must not move the list out from
    // under somebody who was reading a folder, nor drop the rows they had picked out.
    it('changes nothing the mail space displays when the scope is chosen in the field', () => {
        const { workspace: reported } = fieldStanding({
            scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
            selected: ['one'],
        });

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'everything' },
        });

        expect(reported().scope).toEqual({ kind: 'folder', accountId: 'work', alias: 'Invoices' });
        expect(reported().selected).toEqual(['one']);
        expect(reported().askScopeKey).toBe('everything');
    });

    // Narrowing is the other half of the same promise, and it is the half a field offering only mailboxes could not
    // keep: the list still shows the folder and the rows stay ticked while the question is about one message.
    it('narrows to one message without changing what the mail space is showing', () => {
        const { workspace: reported } = fieldStanding({
            scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
            selection: 'AAMkAD-42',
            selected: ['AAMkAD-42', 'AAMkAD-43'],
        });

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'message:AAMkAD-42' },
        });

        expect(reported().scope).toEqual({ kind: 'folder', accountId: 'work', alias: 'Invoices' });
        expect(reported().selected).toEqual(['AAMkAD-42', 'AAMkAD-43']);
        expect(screen.getByRole('option', { name: 'This message', selected: true })).toBeDefined();
    });

    // What is in scope is what is read and sent, so the words beside the question and the scope the run is started
    // with are asserted as one value rather than trusted to have stayed in step.
    it('records the scope it was showing when the question is asked', () => {
        const { workspace: reported } = fieldStanding({
            selection: 'AAMkAD-42',
            fragment: { messageId: 'AAMkAD-42', text: 'by the end of the month' },
        });

        expect(screen.getByRole('option', { name: 'The part you selected', selected: true })).toBeDefined();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'when did they say it would arrive' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(reported().askedBefore).toEqual([
            {
                question: 'when did they say it would arrive',
                scope: { kind: 'fragment', messageId: 'AAMkAD-42', text: 'by the end of the month' },
            },
        ]);
    });

    // A reader who names a mailbox here and then walks the folder tree to it is where the named scope and the screen's
    // own meet, and the control has to keep saying what the question is about rather than falling blank between them.
    it('keeps saying what is in scope when the mail space arrives at the mailbox the field was pointed at', () => {
        fieldStanding({ askScopeKey: 'account:home', scope: { kind: 'account', accountId: 'home' } }, [
            workAccount,
            homeAccount,
        ]);

        expect(screen.getByRole('option', { name: 'Home', selected: true })).toBeDefined();
    });

    it('follows the mail space again when what it is showing is chosen back', () => {
        const { workspace: reported } = fieldStanding({}, [workAccount, homeAccount]);

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'account:home' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'role:Inbox' },
        });

        expect(reported().askScopeKey).toBeNull();
    });
});

describe('IntentField history', () => {
    it('offers nothing back before anything has been asked', () => {
        renderField();

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    it('keeps what was asked beside the scope it was asked under', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what did they promise' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(
            screen.getByRole('button', { name: 'what did they promise, asking about This correspondence' }),
        ).toBeDefined();
    });

    it('records nothing for a submission with no question in it', () => {
        fieldStanding({});

        fireEvent.submit(screen.getByRole('search'));

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    // Widening is what somebody does after an answer that was too narrow, so asking a past question again asks it
    // under the scope in force now rather than the one it carries — otherwise widening would mean retyping.
    it('asks a past question again under the scope in force now', () => {
        fieldStanding(
            {
                askedBefore: [{ question: 'what did they promise', scope: { kind: 'thread', threadId: 'thread-1' } }],
            },
            [workAccount, homeAccount],
        );

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'account:home' },
        });
        fireEvent.click(screen.getByRole('button', { name: /what did they promise/ }));

        expect(screen.getByRole('button', { name: 'what did they promise, asking about Home' })).toBeDefined();
        expect(window.location.hash).toBe('#/discover');
    });

    it('lets go of what was asked when that is asked for', () => {
        fieldStanding({
            askedBefore: [
                { question: 'what did they promise', scope: { kind: 'mail', scope: { kind: 'everything' } } },
            ],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Forget these' }));

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    // Forgetting the list takes away the control that was pressed, so focus is placed rather than left on an element
    // that is no longer in the document — which is where keyboard and screen-reader use stops without anything saying
    // so, and is the same reason the fragment chip's own close button places it.
    it('puts the keyboard back on the question when the list it was pressed in goes', () => {
        fieldStanding({
            askedBefore: [
                { question: 'what did they promise', scope: { kind: 'mail', scope: { kind: 'everything' } } },
            ],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Forget these' }));

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toBe(document.activeElement);
    });
});

// The width this bar has is the one thing jsdom cannot answer, and the setup answers every query `false` — so a test
// about the wide composition states it, and every other test inherits the single-pane reading.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

function theWindowHasRoomForTwoPanes(): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

// The design project draws two bars rather than one: the Discover screen's question, and the one under a
// correspondence, where the act somebody wants is a reply. It is the same field either way, so what these assert is
// the wording and what an empty press then asks for.
describe('IntentField wording', () => {
    it('asks the Discover screen\u2019s own question wherever no correspondence is in scope', () => {
        theWindowHasRoomForTwoPanes();
        fieldStanding({});

        expect(screen.getByRole('button', { name: 'Ask' })).toBeDefined();
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'placeholder',
            'What do you want to ask your mail?',
        );
    });

    it('names drafting a reply, and offers both, while a correspondence is being read', () => {
        theWindowHasRoomForTwoPanes();
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'placeholder',
            'Ask about the thread or draft a reply\u2026',
        );
    });

    it('names it the same way over one message and over a passage of one', () => {
        theWindowHasRoomForTwoPanes();
        fieldStanding({
            selection: 'AAMkAD-42',
            fragment: { messageId: 'AAMkAD-42', text: 'the part somebody pointed at' },
        });

        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
    });

    // The list is what is in front of somebody who ticked rows, rather than the exchange, so the bar stays the
    // Discover screen's.
    it('keeps the question wherever the rows picked out are the scope', () => {
        theWindowHasRoomForTwoPanes();
        fieldStanding({ conversation: null, selected: ['one', 'two'] });

        expect(screen.getByRole('button', { name: 'Ask' })).toBeDefined();
    });

    // The design shortens the label where the window draws one pane, because there is no room for the sentence beside
    // the field.
    it('shortens the label where the window draws a single pane', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        expect(screen.getByRole('button', { name: 'Draft' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
    });

    // The control must not name an act the press does not make, so an empty press over a correspondence asks for the
    // thing the control is called — and the whole sentence rather than the shortened label, because what was asked
    // cannot depend on how wide the window was.
    it('asks for the reply the control names when nothing has been typed', () => {
        const { workspace: reported } = fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        fireEvent.submit(screen.getByRole('search'));

        expect(reported().question).toBe('Draft a reply');
        expect(reported().askedBefore).toEqual([
            { question: 'Draft a reply', scope: { kind: 'thread', threadId: 'thread-1' } },
        ]);
    });

    it('records nothing for an empty press where no correspondence is in scope', () => {
        const { workspace: reported } = fieldStanding({});

        fireEvent.submit(screen.getByRole('search'));

        expect(reported().askedBefore).toEqual([]);
    });
});

// Asking for a reply means a reply, in the composer, rather than a trip to the space that answers questions. Every
// case here turns drafting on, because that is the one thing that decides which of the two a press does.
describe('IntentField drafting', () => {
    it('opens the composer on the message being read rather than going anywhere', () => {
        const { composed } = fieldStanding(
            { conversation: { threadId: 'thread-1', openAt: null }, selection: 'AAMkAD-42' },
            [workAccount],
            true,
        );

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'accept the SLA and ask for a cap' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: 'AAMkAD-42',
            asked: 'accept the SLA and ask for a cap',
        });
        expect(window.location.hash).not.toBe('#/discover');
    });

    it('quotes the passage in scope where nothing was typed, which is the whole of what was asked', () => {
        const { composed } = fieldStanding(
            {
                selection: 'AAMkAD-42',
                fragment: { messageId: 'AAMkAD-42', text: 'the response time is two hours' },
            },
            [workAccount],
            true,
        );

        fireEvent.submit(screen.getByRole('search'));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: 'AAMkAD-42',
            asked: 'the response time is two hours',
        });
    });

    // The other half of the case above: a conversation with no passage in scope has nothing to quote, so what the
    // composer is opened asking for is nothing at all and the block it draws sits waiting rather than firing on its
    // own. What this guards is the composer being opened with a sentence nobody typed about a message nobody picked.
    it('opens the composer asking for nothing where nothing was typed and no passage is in scope', () => {
        const { composed } = fieldStanding(
            { conversation: { threadId: 'thread-1', openAt: null }, selection: 'AAMkAD-42' },
            [workAccount],
            true,
        );

        fireEvent.submit(screen.getByRole('search'));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: 'AAMkAD-42',
            asked: '',
        });
    });

    it('asks the space that answers questions where the deployment writes no draft', () => {
        const { composed } = fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        fireEvent.submit(screen.getByRole('search'));

        expect(composed).not.toHaveBeenCalled();
        expect(window.location.hash).toBe('#/discover');
    });

    it('asks the space that answers questions where the rows picked out are the scope', () => {
        const { composed } = fieldStanding({ selected: ['one', 'two'] }, [workAccount], true);

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what do these two have in common' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(composed).not.toHaveBeenCalled();
        expect(window.location.hash).toBe('#/discover');
    });

    it('keeps what was asked beside the scope it was asked under, drafting or not', () => {
        const { workspace: reported } = fieldStanding(
            { conversation: { threadId: 'thread-1', openAt: null }, selection: 'AAMkAD-42' },
            [workAccount],
            true,
        );

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'accept the SLA' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(reported().askedBefore).toEqual([
            { question: 'accept the SLA', scope: { kind: 'thread', threadId: 'thread-1' } },
        ]);
    });
});
