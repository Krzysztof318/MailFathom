// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { WorkspaceProvider } from './Workspace';
import { useWorkspace, type Workspace } from './useWorkspace';

// A space writes what it owns and reads what the others left, so what is proven here is that one part of the workspace
// can be revised without any other part of it being lost. Which spaces write which part is decided by the space:
// today the frame writes the question and the mailbox in scope, and the folder and the selection arrive with Mail.

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

function renderProbe(change: Partial<Workspace>): void {
    render(
        <WorkspaceProvider>
            <Probe change={change} />
        </WorkspaceProvider>,
    );
}

function carried(): Workspace {
    return JSON.parse(screen.getByRole('status').textContent) as Workspace;
}

describe('WorkspaceProvider', () => {
    it('carries nothing until a space puts something in it', () => {
        renderProbe({});

        expect(carried()).toEqual({ accountId: null, folder: null, selection: null, question: '' });
    });

    it.each([
        { accountId: 'work' },
        { folder: 'Archive/2026' },
        { selection: 'AAMkAD-42' },
        { question: 'what did Nordwind send' },
    ])('changes %o and leaves the rest of the workspace as it was', (change) => {
        renderProbe(change);

        fireEvent.click(screen.getByRole('button'));

        expect(carried()).toEqual({ accountId: null, folder: null, selection: null, question: '', ...change });
    });
});

describe('useWorkspace', () => {
    it('refuses to answer outside the provider rather than inventing a workspace of its own', () => {
        expect(() => {
            render(<Probe change={{}} />);
        }).toThrow(/WorkspaceProvider/);
    });
});
