// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResult, MailAccount, MailAccountDirectory } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionSummary } from './ConnectionSummary';

const workAccount: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const archiveAccount: MailAccount = { ...workAccount, id: 'archive', displayName: 'Archive', behind: true };

const failingAccount: MailAccount = {
    ...workAccount,
    id: 'failing',
    displayName: 'Newsletters',
    synchronizationState: 'Failing',
};

const unreachableAccount: MailAccount = { ...failingAccount, synchronizationState: 'Unreachable' };

function directory(
    synchronizationEnabled: boolean,
    accounts: readonly MailAccount[],
): ClientResult<MailAccountDirectory> {
    return { outcome: 'read', value: { synchronizationEnabled, accounts } };
}

function renderSummary(accounts: ClientResult<MailAccountDirectory> | null): void {
    render(
        <LocalizationProvider>
            <ConnectionSummary accounts={accounts} reread={() => undefined} />
        </LocalizationProvider>,
    );
}

describe('ConnectionSummary', () => {
    it('says it is reading while the answer has not arrived', () => {
        renderSummary(null);

        expect(screen.getByText('Reading accounts…')).toBeDefined();
    });

    it('says every account is current when none of them is behind', () => {
        renderSummary(directory(true, [workAccount]));

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
    });

    it('says some accounts are behind when one of them is', () => {
        renderSummary(directory(true, [workAccount, archiveAccount]));

        expect(screen.getByText('Some accounts are behind.')).toBeDefined();
    });

    it('says an account stopped synchronizing rather than reporting it as merely behind', () => {
        renderSummary(directory(true, [workAccount, failingAccount]));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
        expect(screen.queryByText('Some accounts are behind.')).toBeNull();
    });

    it('reads an account it cannot reach the same way as one that is failing', () => {
        renderSummary(directory(true, [workAccount, unreachableAccount]));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
    });

    it('says an account stopped synchronizing even where another is only behind', () => {
        renderSummary(directory(true, [archiveAccount, failingAccount]));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
    });

    it('says the deployment is not refreshing these accounts when it is not', () => {
        renderSummary(directory(false, [workAccount]));

        expect(screen.getByText('This deployment is not refreshing the local copy of these accounts.')).toBeDefined();
    });

    it('tells an owner holding no account that, rather than showing a failure', () => {
        renderSummary(directory(true, []));

        expect(screen.getByText('No mail account is configured for this owner yet.')).toBeDefined();
    });

    it('names why the accounts could not be read, and offers the way out', () => {
        renderSummary({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });

        expect(screen.getByText(/The accounts could not be read: unavailable\./)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it.each(['unauthenticated', 'unauthorized', 'unreadable'] as const)(
        'still says what failed at a %s failure, and offers no second attempt that would repeat it',
        (reason) => {
            renderSummary({ outcome: 'failed', failure: { reason, status: 401 } });

            expect(screen.getByText(/The accounts could not be read:/)).toBeDefined();
            expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
        },
    );

    it('puts no status code on the screen', () => {
        renderSummary({ outcome: 'failed', failure: { reason: 'unavailable', status: 503 } });

        expect(screen.queryByText(/503/)).toBeNull();
    });
});
