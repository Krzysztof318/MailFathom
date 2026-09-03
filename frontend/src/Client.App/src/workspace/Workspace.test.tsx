// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { WorkspaceProvider } from './Workspace';
import { emptyWorkspace, useWorkspace, type Workspace } from './useWorkspace';

// A space writes what it owns and reads what the others left, so what is proven here is that one part of the workspace
// can be revised without any other part of it being lost, and that what it holds survives the reload a single-page
// application makes a cold start. Which spaces write which part is decided by the space: today the frame writes the
// question, the folder tree writes the scope and what it has folded away, and the selection arrives with the list.

function Probe({ change }: { readonly change: Partial<Workspace> }) {
    const { workspace, revise } = useWorkspace();

    return (
        <div>
            <button
                type="button"
                onClick={() => {
                    revise(change);
                }}
            >
                {JSON.stringify(change)}
            </button>
            <output>{JSON.stringify(workspace)}</output>
        </div>
    );
}

function renderProbe(change: Partial<Workspace>): { rerender: () => void } {
    const rendered = render(
        <WorkspaceProvider>
            <Probe change={change} />
        </WorkspaceProvider>,
    );

    return {
        rerender: () => {
            rendered.unmount();
            render(
                <WorkspaceProvider>
                    <Probe change={change} />
                </WorkspaceProvider>,
            );
        },
    };
}

function carried(): Workspace {
    return JSON.parse(screen.getByRole('status').textContent) as Workspace;
}

// The store outlives a test rather than a file, so what one test kept would be what the next one opened with.
afterEach(() => {
    window.sessionStorage.clear();
});

describe('WorkspaceProvider', () => {
    it('carries nothing until a space puts something in it', () => {
        renderProbe({});

        expect(carried()).toEqual(emptyWorkspace);
    });

    it.each<Partial<Workspace>>([
        { scope: { kind: 'account', accountId: 'work' } },
        { collapsed: ['account:work'] },
        { selection: 'AAMkAD-42' },
        { fragment: 'the part of the message somebody pointed at' },
        { question: 'what did Nordwind send' },
    ])('changes %o and leaves the rest of the workspace as it was', (change) => {
        renderProbe(change);

        fireEvent.click(screen.getByRole('button'));

        expect(carried()).toEqual({ ...emptyWorkspace, ...change });
    });

    it('opens on what the last run of this tab was looking at, so a reload returns to it', () => {
        const change: Partial<Workspace> = {
            scope: { kind: 'folder', accountId: 'work', alias: 'INBOX' },
            collapsed: ['account:personal'],
        };
        const { rerender } = renderProbe(change);

        fireEvent.click(screen.getByRole('button'));
        rerender();

        expect(carried()).toEqual({ ...emptyWorkspace, ...change });
    });

    it('opens on nothing where what was kept is not a workspace this client wrote', () => {
        window.sessionStorage.setItem('mailfathom.workspace', '{"scope":{"kind":"everywhere"}}');

        renderProbe({});

        expect(carried()).toEqual(emptyWorkspace);
    });
});

// A conversation stands in front of the message it was opened from, so the two are revised together whatever a screen
// asked for. Two revisions in sequence is what proves it, which the single-change probe above cannot express.
function Revisions({ changes }: { readonly changes: readonly Partial<Workspace>[] }) {
    const { workspace, revise } = useWorkspace();

    return (
        <div>
            {changes.map((change, at) => (
                <button
                    key={at}
                    type="button"
                    onClick={() => {
                        revise(change);
                    }}
                >
                    {String(at)}
                </button>
            ))}
            <output>{JSON.stringify(workspace)}</output>
        </div>
    );
}

function applying(changes: readonly Partial<Workspace>[]): void {
    render(
        <WorkspaceProvider>
            <Revisions changes={changes} />
        </WorkspaceProvider>,
    );

    changes.forEach((_, at) => {
        fireEvent.click(screen.getByRole('button', { name: String(at) }));
    });
}

describe('the conversation beside the selection', () => {
    const conversation = { threadId: 'a-conversation', openAt: 'the-message-it-was-opened-from' };

    it('closes the conversation when a different message is picked, so the click has an effect', () => {
        applying([{ selection: 'the-message-it-was-opened-from' }, { conversation }, { selection: 'another-message' }]);

        expect(carried().conversation).toBeNull();
        expect(carried().selection).toBe('another-message');
    });

    it('keeps the conversation where the same message is picked again, which changes nothing', () => {
        applying([
            { selection: 'the-message-it-was-opened-from' },
            { conversation },
            { selection: 'the-message-it-was-opened-from' },
        ]);

        expect(carried().conversation).toEqual(conversation);
    });

    it('leaves a revision naming both alone, because that is a conversation being opened rather than abandoned', () => {
        applying([{ selection: 'another-message', conversation }]);

        expect(carried().conversation).toEqual(conversation);
        expect(carried().selection).toBe('another-message');
    });
});

// The markup surface stands in front of a message the same way, and closing it on a change of message matters more:
// what it draws is a stranger's own markup, shown for one message after the reader was asked about that message.
describe('the markup surface beside the selection', () => {
    it('closes the surface when a different message is picked, so no message inherits another consent', () => {
        applying([
            { selection: 'the-message-it-was-opened-from' },
            { fullHtml: 'the-message-it-was-opened-from' },
            { selection: 'another-message' },
        ]);

        expect(carried().fullHtml).toBeNull();
        expect(carried().selection).toBe('another-message');
    });

    it('keeps the surface where the same message is picked again, which changes nothing', () => {
        applying([
            { selection: 'the-message-it-was-opened-from' },
            { fullHtml: 'the-message-it-was-opened-from' },
            { selection: 'the-message-it-was-opened-from' },
        ]);

        expect(carried().fullHtml).toBe('the-message-it-was-opened-from');
    });

    it('leaves a revision naming both alone, because that is the surface being opened on another message', () => {
        applying([{ selection: 'another-message', fullHtml: 'another-message' }]);

        expect(carried().fullHtml).toBe('another-message');
        expect(carried().selection).toBe('another-message');
    });
});

describe('useWorkspace', () => {
    it('refuses to answer outside the provider rather than inventing a workspace of its own', () => {
        expect(() => {
            render(<Probe change={{}} />);
        }).toThrow(/WorkspaceProvider/);
    });
});
