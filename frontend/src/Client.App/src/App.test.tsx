// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { App } from './App';

// The screen reads through the transport this module supplies, so replacing the module is how the network boundary is
// faked here: what is under test stays the real request, the real parsing, and the real failure mapping, and only the
// answer they are given is the test's. A screen that takes its transport as a prop needs none of this.
const stub = vi.hoisted((): { session: ClientSession; answer: MailFathomTransport } => ({
    session: { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' },
    answer: () => Promise.resolve({ status: 200, body: '' }),
}));

vi.mock('./stubMailFathom', () => ({
    stubSession: stub.session,
    stubTransport: ((request: ClientRequest) => stub.answer(request)) satisfies MailFathomTransport,
}));

function answering(response: ClientResponse): void {
    stub.answer = () => Promise.resolve(response);
}

function directory(synchronizationEnabled: boolean, accounts: readonly unknown[]): ClientResponse {
    return { status: 200, body: JSON.stringify({ synchronizationEnabled, accounts }) };
}

const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

const archiveAccount = {
    id: 'archive',
    displayName: 'Archive',
    synchronizationState: 'Unreachable',
    lastSynchronizedAt: '2026-08-28T18:02:00+00:00',
    behind: true,
};

describe('App', () => {
    beforeEach(() => {
        answering(directory(true, [workAccount]));
    });

    it('says it is reading while the answer has not arrived', () => {
        stub.answer = () => new Promise<ClientResponse>(() => undefined);

        render(<App />);

        expect(screen.getByText('Reading accounts…')).toBeDefined();
    });

    it('names each account beside the state it is in', async () => {
        answering(directory(true, [workAccount, archiveAccount]));

        render(<App />);

        // One row's whole text, which is what a person reads off it and what a screen reader announces. The name and
        // the state are adjacent elements rather than one string, so the gap between them here is the markup's; what
        // separates them on the screen is layout, and layout is not something this suite may claim.
        const accounts = await screen.findAllByRole('listitem');
        expect(accounts.map((account) => account.textContent)).toEqual([
            'Worksynchronized',
            'Archiveunreachable, behind',
        ]);
    });

    it('says the deployment is refreshing these accounts when it is', async () => {
        render(<App />);

        expect(await screen.findByText('This deployment refreshes the local copy of these accounts.')).toBeDefined();
    });

    it('says the deployment is not refreshing these accounts when it is not', async () => {
        answering(directory(false, [workAccount]));

        render(<App />);

        expect(
            await screen.findByText('This deployment is not refreshing the local copy of these accounts.'),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of an empty list', async () => {
        answering({ status: 401, body: '' });

        render(<App />);

        expect(await screen.findByText('The accounts could not be read: unauthenticated.')).toBeDefined();
        expect(screen.queryByRole('list')).toBeNull();
    });
});
