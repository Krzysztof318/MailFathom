// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
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

function renderField(accounts: readonly MailAccount[] = [workAccount]): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <IntentField accounts={accounts} />
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

    it('says every mailbox is in scope until one is chosen', () => {
        renderField();

        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty('value', '');
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

function fieldStanding(as: Partial<Workspace>, accounts: readonly MailAccount[] = [workAccount]): () => Workspace {
    let last: Workspace | null = null;

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <Standing as={as} />
                <IntentField accounts={accounts} />
                <Reporting
                    onWorkspace={(workspace) => {
                        last = workspace;
                    }}
                />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return () => {
        if (last === null) {
            throw new Error('The workspace was never reported.');
        }

        return last;
    };
}

describe('IntentField scope', () => {
    it('says nothing about a fragment while the whole message is the scope', () => {
        renderField();

        expect(screen.queryByRole('button', { name: 'Ask about the whole message instead' })).toBeNull();
    });

    it('quotes the words a question would be asked about, rather than saying a fragment exists', () => {
        fieldStanding({ fragment: 'the part of the message somebody pointed at' });

        expect(
            screen.getByText(
                'Asking about the part of this message you selected: “the part of the message somebody pointed at”',
            ),
        ).toBeDefined();
    });

    it('gives the whole message back as the scope when that is asked for', () => {
        fieldStanding({ fragment: 'the part of the message somebody pointed at' });

        fireEvent.click(screen.getByRole('button', { name: 'Ask about the whole message instead' }));

        expect(screen.queryByRole('button', { name: 'Ask about the whole message instead' })).toBeNull();
    });

    // Widening the scope takes the control that did it off the screen, so where focus lands is the behaviour:
    // left to the browser it falls to the document, which is where reading from a keyboard silently stops.
    it('puts focus on the question when the control that widened the scope goes', () => {
        fieldStanding({ fragment: 'the part of the message somebody pointed at' });

        fireEvent.click(screen.getByRole('button', { name: 'Ask about the whole message instead' }));

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toBe(document.activeElement);
    });

    it('names the correspondence being read as what the question is about', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty('value', '');
        expect(screen.getByRole('option', { name: 'This correspondence' })).toBeDefined();
    });

    it('counts the messages picked out, which are narrower than the correspondence holding them', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null }, selected: ['one', 'two', 'three'] });

        expect(screen.getByRole('option', { name: '3 selected messages' })).toBeDefined();
        expect(screen.queryByRole('option', { name: 'This correspondence' })).toBeNull();
    });

    it('names the folder and the mailbox it is in', () => {
        fieldStanding({ scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' } });

        expect(screen.getByRole('option', { name: 'Invoices in Work' })).toBeDefined();
    });

    // The whole point of the field owning a scope of its own: widening a question must not move the list out from
    // under somebody who was reading a folder, nor drop the rows they had picked out.
    it('changes nothing the mail space displays when the scope is chosen in the field', () => {
        const reported = fieldStanding({
            scope: { kind: 'folder', accountId: 'work', alias: 'Invoices' },
            selected: ['one'],
        });

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'everything' },
        });

        expect(reported().scope).toEqual({ kind: 'folder', accountId: 'work', alias: 'Invoices' });
        expect(reported().selected).toEqual(['one']);
        expect(reported().askScope).toEqual({ kind: 'everything' });
    });

    // The list the control renders drops the mailbox the mail space is already showing, so that it is not offered
    // twice. A reader who names a mailbox here and then walks the folder tree to it is the case where those two meet,
    // and the control has to keep saying what the question is about rather than falling blank between them.
    it('keeps saying what is in scope when the mail space arrives at the mailbox the field was pointed at', () => {
        fieldStanding(
            { askScope: { kind: 'account', accountId: 'home' }, scope: { kind: 'account', accountId: 'home' } },
            [workAccount, homeAccount],
        );

        expect(screen.getByRole('option', { name: 'Home', selected: true })).toBeDefined();
    });

    it('follows the mail space again when what it is showing is chosen back', () => {
        const reported = fieldStanding({}, [workAccount, homeAccount]);

        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'account:home' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: '' },
        });

        expect(reported().askScope).toBeNull();
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
