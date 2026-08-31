// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, MailFathomTransport } from '@mailfathom/client-backend';
import { App } from './App';
import type { AdoptedDeployment } from './deployment/adoptedDeployment';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { localeNames, locales, readStoredLocale } from './localization/locale';
import { ThemeProvider } from './theme/Theme';
import { WorkspaceProvider } from './workspace/Workspace';

// The frame reads its mail through the transport this module supplies, so replacing the module is how that network
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
// and it is the default here because the screens below are about the spaces rather than about where the deployment is.
const servedFrom: AdoptedDeployment = {
    deployment: { baseAddress: 'https://mail.example.invalid' },
    chosen: false,
};

const nothingSent: DeploymentTransport = () => () => Promise.reject(new Error('This screen reaches no deployment.'));

const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

// The application is mounted the way `main.tsx` mounts it, `StrictMode` and all four providers included. Nothing below
// the frame may decide the language, the theme, or what the person is carrying, so a test that supplied fewer would be
// proving a second arrangement — and the mode is half of that arrangement rather than a detail of it: it invokes every
// effect twice on mount, which is the difference between a screen that behaves and one that behaves the first time.
function renderApp(deployment: AdoptedDeployment | null = servedFrom, send: DeploymentTransport = nothingSent): void {
    render(
        <StrictMode>
            <LocalizationProvider>
                <ThemeProvider>
                    <WorkspaceProvider>
                        <App deployment={deployment} send={send} />
                    </WorkspaceProvider>
                </ThemeProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}

function chose(baseAddress: string): AdoptedDeployment {
    return { deployment: { baseAddress }, chosen: true };
}

// Which deployments were reached, in the order they were first reached. `StrictMode` invokes the effect that reads the
// accounts twice on mount, as React does in development and as `main.tsx` therefore does here, so a repeat of the
// address already being read is the mode rather than the screen — what these tests are about is which address that is.
function deploymentsAsked(asked: string[]): string[] {
    return [...new Set(asked)];
}

function recordingWhatIsAsked(asked: string[]): void {
    stub.answer = (request) => {
        asked.push(request.path);

        return Promise.resolve({ ...directory(true, [workAccount]), headers: {} });
    };
}

// The frame is on the screen once the summary above the space has an answer to state, which is what every test that
// acts on the frame waits for rather than for a timer.
async function framed(): Promise<void> {
    await screen.findByText('Every account is up to date.');
}

function openingAt(address: string): void {
    window.history.replaceState(null, '', address);
}

async function goTo(space: string): Promise<void> {
    fireEvent.click(screen.getByRole('link', { name: space }));

    await screen.findByRole('heading', { name: space, level: 1 });
}

beforeEach(() => {
    answering(directory(true, [workAccount]));
    openingAt('/');
});

afterEach(() => {
    openingAt('/');
    window.localStorage.clear();
    document.documentElement.removeAttribute('lang');
    document.documentElement.removeAttribute('data-theme');
});

describe('App', () => {
    it('says it is reading while the answer has not arrived', () => {
        stub.answer = () => new Promise<ClientResponse>(() => undefined);

        renderApp();

        expect(screen.getByText('Reading accounts…')).toBeDefined();
    });

    it('opens in Discover when the address names no space', async () => {
        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
    });

    it('writes the space it is showing into an address that named none, so it can be reloaded', async () => {
        renderApp();

        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it('corrects an address naming a space the client does not have, rather than showing one it does not name', async () => {
        openingAt('#/nowhere');

        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it.each(['Mail', 'Cases'])('opens in %s when that is what the address names', async (space) => {
        openingAt(`#/${space.toLowerCase()}`);

        renderApp();

        expect(await screen.findByRole('heading', { name: space, level: 1 })).toBeDefined();
    });

    it('shows the space whose link was activated, and marks it as the current one', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        await goTo('Cases');

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Cases');
    });

    it('keeps the question and the mailbox in scope while the person moves between spaces', async () => {
        renderApp();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'Mailbox in scope' }), { target: { value: 'work' } });

        await goTo('Mail');

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'the renewal Nordwind sent',
        );
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', 'work');
    });

    it('offers every mailbox the owner holds as a scope, beside all of them at once', async () => {
        answering(directory(true, [workAccount, { ...workAccount, id: 'archive', displayName: 'Archive' }]));

        renderApp();

        const scope = await screen.findByRole('combobox', { name: 'Mailbox in scope' });
        await waitFor(() => {
            expect([...scope.querySelectorAll('option')].map((option) => option.textContent)).toEqual([
                'All mailboxes',
                'Work',
                'Archive',
            ]);
        });
    });

    it('says the deployment is not refreshing these accounts when it is not', async () => {
        answering(directory(false, [workAccount]));

        renderApp();

        expect(
            await screen.findByText('This deployment is not refreshing the local copy of these accounts.'),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of saying nothing about them', async () => {
        answering({ status: 401, body: '' });

        renderApp();

        expect(await screen.findByText('The accounts could not be read: unauthenticated.')).toBeDefined();
    });

    it('reads the accounts again when the person asks it to, rather than only on a reload', async () => {
        answering({ status: 503, body: '' });

        renderApp();
        const retry = await screen.findByRole('button', { name: 'Try again' });

        answering(directory(true, [workAccount]));
        fireEvent.click(retry);

        expect(await screen.findByText('Every account is up to date.')).toBeDefined();
    });
});

describe('App deployment', () => {
    // A deployment that is there and wants a credential, which is what every deployment somebody runs answers a client
    // that has not signed in yet. The challenge is what says the answer came from MailFathom.
    const reachable: DeploymentTransport = () => () =>
        Promise.resolve({ status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } });

    it('reads from the deployment it was pointed at, rather than from one written into the client', async () => {
        const asked: string[] = [];
        recordingWhatIsAsked(asked);

        renderApp(chose('https://elsewhere.example.invalid'));
        await framed();

        expect(deploymentsAsked(asked)).toEqual(['https://elsewhere.example.invalid/api/client/accounts']);
    });

    it('asks for an address when nothing has said where the deployment is', () => {
        renderApp(null);

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('offers to be pointed elsewhere once somebody named the deployment themselves', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        expect(screen.getByRole('button', { name: 'Point somewhere else' })).toBeDefined();
    });

    it('offers nothing to change where the origin that served the client is the deployment', async () => {
        renderApp();
        await framed();

        expect(screen.queryByRole('button', { name: 'Point somewhere else' })).toBeNull();
    });

    it('asks for an address again, and shows no space, once it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('leaves focus where the document opened it when the deployment was already known', async () => {
        renderApp();
        await framed();

        // A cold start is not a view change. `main.tsx` mounts under `StrictMode`, so every effect runs twice here as
        // it does under `pnpm dev`, and a guard that only survives one invocation would have pulled focus by now.
        expect(document.activeElement).toBe(document.body);
    });

    it('puts focus at the start of the workspace once the deployment has been named, rather than leaving it behind', async () => {
        renderApp(null, reachable);

        fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), {
            target: { value: 'mail.example.test' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
        await framed();

        expect(document.activeElement?.contains(screen.getByRole('main'))).toBe(true);
    });

    it('puts focus back in the address when it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('reads the next deployment with a session of its own, and never the one before it again', async () => {
        const asked: string[] = [];
        recordingWhatIsAsked(asked);

        renderApp(chose('https://first.example.invalid'), reachable);
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), {
            target: { value: 'second.example.invalid' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
        await framed();

        expect(deploymentsAsked(asked)).toEqual([
            'https://first.example.invalid/api/client/accounts',
            'https://second.example.invalid/api/client/accounts',
        ]);
    });
});

describe('App language', () => {
    it('offers each language under its own name, and no other', () => {
        renderApp();

        const choice = screen.getByRole('combobox', { name: 'Language' });
        expect([...choice.querySelectorAll('option')].map((option) => option.textContent)).toEqual(
            locales.map((locale) => localeNames[locale]),
        );
    });

    it('rewrites the screen when another language is chosen, without anything being restarted', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'pl' } });

        expect(screen.getByRole('heading', { name: 'Odkrywaj', level: 1 })).toBeDefined();
        expect(document.documentElement.lang).toBe('pl');
    });

    it('remembers the choice, so a later run of either head opens in it', () => {
        renderApp();

        fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'pl' } });

        expect(readStoredLocale()).toBe('pl');
    });
});
