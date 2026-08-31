// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { IntentField } from './IntentField';

const workAccount: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

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

        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', '');
        expect(screen.getByRole('option', { name: 'All mailboxes' })).toBeDefined();
    });
});
