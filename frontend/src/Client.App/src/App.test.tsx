// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, MailFathomTransport } from '@mailfathom/client-backend';
import { App } from './App';
import type { AdoptedDeployment } from './deployment/adoptedDeployment';
import { LocalizationProvider } from './localization/Localization';
import { localeNames, locales, readStoredLocale } from './localization/locale';

// The screen reads its mail through the transport this module supplies, so replacing the module is how that network
// boundary is faked here: what is under test stays the real request, the real parsing, and the real failure mapping,
// and only the answer they are given is the test's. A screen that takes its transport as a prop needs none of this,
// which is why the deployment the client is pointed at arrives as one below.
const stub = vi.hoisted((): { answer: MailFathomTransport } => ({
    answer: () => Promise.resolve({ status: 200, body: '', headers: {} }),
}));

vi.mock('./stubMailFathom', () => ({
    stubAuthorization: 'Basic dGVzdA==',
    stubTransport: ((request: ClientRequest) => stub.answer(request)) satisfies MailFathomTransport,
}));

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): void {
    stub.answer = () => Promise.resolve({ ...response, headers: {} });
}

function directory(synchronizationEnabled: boolean, accounts: readonly unknown[]): Answer {
    return { status: 200, body: JSON.stringify({ synchronizationEnabled, accounts }) };
}

// What `main.tsx` resolves at the edge and hands down. The origin that served the client is the case a web head is in,
// and it is the default here because the screens below are about mail rather than about where the deployment is.
const servedFrom: AdoptedDeployment = {
    deployment: { baseAddress: 'https://mail.example.invalid' },
    chosen: false,
};

const nothingSent: MailFathomTransport = () => Promise.reject(new Error('This screen reaches no deployment.'));

// The application is mounted the way `main.tsx` mounts it. Nothing reads a message without the provider above it, and
// a test that supplied its own would be proving a second arrangement.
function renderApp(deployment: AdoptedDeployment | null = servedFrom, send: MailFathomTransport = nothingSent): void {
    render(
        <LocalizationProvider>
            <App deployment={deployment} send={send} />
        </LocalizationProvider>,
    );
}

function chose(baseAddress: string): AdoptedDeployment {
    return { deployment: { baseAddress }, chosen: true };
}

function recordingWhatIsAsked(asked: string[]): void {
    stub.answer = (request) => {
        asked.push(request.path);

        return Promise.resolve({ ...directory(true, [workAccount]), headers: {} });
    };
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

const neverSynchronizedAccount = {
    id: 'personal',
    displayName: 'Personal',
    synchronizationState: 'NeverSynchronized',
    lastSynchronizedAt: null,
    behind: false,
};

describe('App', () => {
    beforeEach(() => {
        answering(directory(true, [workAccount]));
    });

    afterEach(() => {
        window.localStorage.clear();
        document.documentElement.removeAttribute('lang');
    });

    it('says it is reading while the answer has not arrived', () => {
        stub.answer = () => new Promise<ClientResponse>(() => undefined);

        renderApp();

        expect(screen.getByText('Reading accounts…')).toBeDefined();
    });

    it('names each account beside the state it is in', async () => {
        answering(directory(true, [workAccount, archiveAccount]));

        renderApp();

        // One row's whole text, which is what a person reads off it and what a screen reader announces. The name, the
        // state, and the time are adjacent elements rather than one string, so the gaps between them here are the
        // markup's; what separates them on the screen is layout, and layout is not something this suite may claim.
        const accounts = await screen.findAllByRole('listitem');
        expect(accounts.map((account) => account.textContent)).toEqual([
            `Worksynchronizedlast synchronized ${whenEnglishWrites(workAccount.lastSynchronizedAt)}`,
            `Archiveunreachable, behindlast synchronized ${whenEnglishWrites(archiveAccount.lastSynchronizedAt)}`,
        ]);
    });

    it('shows no time against an account that has never synchronized', async () => {
        answering(directory(true, [neverSynchronizedAccount]));

        renderApp();

        const account = await screen.findByRole('listitem');
        expect(account.textContent).toBe('Personalnever synchronized');
    });

    it('says the deployment is refreshing these accounts when it is', async () => {
        renderApp();

        expect(await screen.findByText('This deployment refreshes the local copy of these accounts.')).toBeDefined();
    });

    it('says the deployment is not refreshing these accounts when it is not', async () => {
        answering(directory(false, [workAccount]));

        renderApp();

        expect(
            await screen.findByText('This deployment is not refreshing the local copy of these accounts.'),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of an empty list', async () => {
        answering({ status: 401, body: '' });

        renderApp();

        expect(await screen.findByText('The accounts could not be read: unauthenticated.')).toBeDefined();
        expect(screen.queryByRole('list')).toBeNull();
    });
});

describe('App deployment', () => {
    // A deployment that is there and wants a credential, which is what every deployment somebody runs answers a client
    // that has not signed in yet. The challenge is what says the answer came from MailFathom.
    const reachable: MailFathomTransport = () =>
        Promise.resolve({ status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } });

    beforeEach(() => {
        answering(directory(true, [workAccount]));
    });

    afterEach(() => {
        window.localStorage.clear();
        document.documentElement.removeAttribute('lang');
    });

    it('reads from the deployment it was pointed at, rather than from one written into the client', async () => {
        const asked: string[] = [];
        recordingWhatIsAsked(asked);

        renderApp(chose('https://elsewhere.example.invalid'));
        await screen.findAllByRole('listitem');

        expect(asked).toEqual(['https://elsewhere.example.invalid/api/client/accounts']);
    });

    it('asks for an address when nothing has said where the deployment is', () => {
        renderApp(null);

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.queryByRole('list')).toBeNull();
    });

    it('offers to be pointed elsewhere once somebody named the deployment themselves', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await screen.findAllByRole('listitem');

        expect(screen.getByRole('button', { name: 'Point somewhere else' })).toBeDefined();
    });

    it('offers nothing to change where the origin that served the client is the deployment', async () => {
        renderApp();
        await screen.findAllByRole('listitem');

        expect(screen.queryByRole('button', { name: 'Point somewhere else' })).toBeNull();
    });

    it('asks for an address again, and shows no mail, once it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await screen.findAllByRole('listitem');

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.queryByRole('list')).toBeNull();
    });

    it('puts focus at the start of the mail once the deployment has been named, rather than leaving it behind', async () => {
        renderApp(null, reachable);

        fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), {
            target: { value: 'mail.example.test' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
        await screen.findAllByRole('listitem');

        expect(document.activeElement?.contains(screen.getByRole('list'))).toBe(true);
    });

    it('puts focus back in the address when it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await screen.findAllByRole('listitem');

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('reads the next deployment with a session of its own, and never the one before it again', async () => {
        const asked: string[] = [];
        recordingWhatIsAsked(asked);

        renderApp(chose('https://first.example.invalid'), reachable);
        await screen.findAllByRole('listitem');

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), {
            target: { value: 'second.example.invalid' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
        await screen.findAllByRole('listitem');

        expect(asked).toEqual([
            'https://first.example.invalid/api/client/accounts',
            'https://second.example.invalid/api/client/accounts',
        ]);
    });
});

describe('App language', () => {
    beforeEach(() => {
        answering(directory(true, [workAccount]));
    });

    afterEach(() => {
        window.localStorage.clear();
        document.documentElement.removeAttribute('lang');
    });

    it('offers each language under its own name, and no other', () => {
        renderApp();

        const choice = screen.getByRole('combobox', { name: 'Language' });
        expect([...choice.querySelectorAll('option')].map((option) => option.textContent)).toEqual(
            locales.map((locale) => localeNames[locale]),
        );
    });

    it('rewrites the screen when another language is chosen, without anything being restarted', async () => {
        renderApp();
        await screen.findByText('This deployment refreshes the local copy of these accounts.');

        fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'pl' } });

        expect(screen.getByText('To wdrożenie odświeża lokalną kopię tych kont.')).toBeDefined();
        expect(document.documentElement.lang).toBe('pl');
    });

    it('remembers the choice, so a later run of either head opens in it', () => {
        renderApp();

        fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'pl' } });

        expect(readStoredLocale()).toBe('pl');
    });

    it('writes a time under the chosen language rather than under the one the catalogue was written in', async () => {
        renderApp();
        await screen.findAllByRole('listitem');

        fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'pl' } });

        const account = screen.getByRole('listitem');
        expect(account.textContent).toBe(
            `Workzsynchronizowanoostatnia synchronizacja ${whenLocaleWrites('pl', workAccount.lastSynchronizedAt)}`,
        );
    });
});

// What `Intl` makes of the instant, rather than a string this file spells out. The machine's own time zone decides what
// a person actually reads, so an expectation written by hand would be an expectation about the machine; asking `Intl`
// the same question the screen asks it is what makes the assertion about the locale reaching the formatter.
function whenLocaleWrites(locale: string, instant: string): string {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(instant));
}

function whenEnglishWrites(instant: string): string {
    return whenLocaleWrites('en', instant);
}
