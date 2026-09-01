// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { ClientRequest, ClientResponse } from '@mailfathom/client-backend';
import { App } from './App';
import type { AdoptedDeployment } from './deployment/adoptedDeployment';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { LocalizationProvider } from './localization/Localization';
import { localeNames, locales, readStoredLocale } from './localization/locale';
import type { CredentialLifetime, CredentialStore } from './signIn/credentialStore';
import { ThemeProvider } from './theme/Theme';
import { WorkspaceProvider } from './workspace/Workspace';

// The network boundary is the transport, and the credential this run holds is the store — both arrive as props, so a
// test supplies each and nothing patches `fetch`, starts a server, or replaces a module. What is under test stays the
// real request, the real parsing, and the real failure mapping, and only the answers they are given are the test's.

type Answer = Omit<ClientResponse, 'headers'> & { readonly headers?: Readonly<Record<string, string>> };

/** What a deployment answers a caller it accepts, which is what proves the address is MailFathom and the password works. */
const accepted: Answer = { status: 200, body: JSON.stringify({ service: 'MailFathom', version: '0.8.0' }) };

// The two challenges a MailFathom surface answers a refusal with, in one header value: the bearer one every deployment
// produces, and the password one beside it where the deployment accepts passwords.
const challenged = 'Bearer realm="MailFathom", Basic realm="MailFathom", charset="UTF-8"';

const workAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
};

/** What a run opens already holding, where something was kept for it. */
const heldCredential = 'Basic dGVzdDpzZWNyZXQ=';

/** What the screen composes out of what `signIn` below types, which is what a test asserts was kept and presented. */
const typedCredential = 'Basic b3duZXI6b3BlbiBzZXNhbWU=';

function directory(synchronizationEnabled: boolean, accounts: readonly unknown[]): Answer {
    return { status: 200, body: JSON.stringify({ synchronizationEnabled, accounts }) };
}

function complete(answer: Answer): ClientResponse {
    return { status: answer.status, body: answer.body, headers: answer.headers ?? {} };
}

// Every request the doubles below were asked, in the order they were asked. `StrictMode` invokes the effect that reads
// the accounts twice on mount, as React does in development and as `main.tsx` therefore does here, so a repeat of a
// route already asked for is the mode rather than the screen — what these tests are about is which routes those are.
const asked: ClientRequest[] = [];

function routesAsked(): string[] {
    return [...new Set(asked.map((request) => request.path))];
}

/** A deployment that accepts any credential and answers the accounts with what a test named. */
function deploymentAnswering(accounts: Answer = directory(true, [workAccount])): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        return Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));
    };
}

/** A deployment answering every route the same way, which is how a refusal to sign anybody in is stated. */
function deploymentRefusing(answer: Answer): DeploymentTransport {
    return () => (request) => {
        asked.push(request);

        return Promise.resolve(complete(answer));
    };
}

interface RecordingStore extends CredentialStore {
    /** What this store holds, by deployment, so a test asserts on what was kept rather than on what was called. */
    readonly kept: Map<string, string>;
}

function storeKeeping(lifetime: CredentialLifetime = 'untilTheTabCloses'): RecordingStore {
    const kept = new Map<string, string>();

    return {
        kept,
        lifetime,
        read: (deployment) => Promise.resolve(kept.get(deployment.baseAddress) ?? null),
        keep: (deployment, authorization) => {
            kept.set(deployment.baseAddress, authorization);

            return Promise.resolve(true);
        },
        forget: (deployment) => {
            kept.delete(deployment.baseAddress);

            return Promise.resolve(true);
        },
    };
}

/** A store that will not write, which is a keychain locked between being found and being written to. */
function storeRefusingToKeep(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, keep: () => Promise.resolve(false) };
}

/** A store that holds the credential and will not give it up, which is a locked keychain from the client's side. */
function storeRefusingToForget(): RecordingStore {
    const store = storeKeeping('untilSignedOut');

    return { ...store, forget: () => Promise.resolve(false) };
}

// What `main.tsx` resolves at the edge and hands down. The origin that served the client is the case a web head is in,
// and it is the default here because the screens below are about the spaces rather than about where the deployment is.
const servedFrom: AdoptedDeployment = {
    deployment: { baseAddress: 'https://mail.example.invalid' },
    chosen: false,
};

function chose(baseAddress: string): AdoptedDeployment {
    return { deployment: { baseAddress }, chosen: true };
}

// The application is mounted the way `main.tsx` mounts it, `StrictMode` and all four providers included. Nothing below
// the frame may decide the language, the theme, or what the person is carrying, so a test that supplied fewer would be
// proving a second arrangement — and the mode is half of that arrangement rather than a detail of it: it invokes every
// effect twice on mount, which is the difference between a screen that behaves and one that behaves the first time.
function renderApp(
    deployment: AdoptedDeployment | null = servedFrom,
    signedInWith: string | null = heldCredential,
    send: DeploymentTransport = deploymentAnswering(),
    credentials: CredentialStore = storeKeeping(),
): void {
    render(
        <StrictMode>
            <LocalizationProvider>
                <ThemeProvider>
                    <WorkspaceProvider>
                        <App
                            credentials={credentials}
                            deployment={deployment}
                            send={send}
                            signedInWith={signedInWith}
                        />
                    </WorkspaceProvider>
                </ThemeProvider>
            </LocalizationProvider>
        </StrictMode>,
    );
}

// The frame is on the screen once the summary above the space has an answer to state, which is what every test that
// acts on the frame waits for rather than for a timer.
async function framed(): Promise<void> {
    await screen.findByText('Every account is up to date.');
}

function typeAddress(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), { target: { value: entry } });
}

function signIn(userName = 'owner', password = 'open sesame'): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'User name' }), { target: { value: userName } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

function openingAt(address: string): void {
    window.history.replaceState(null, '', address);
}

async function goTo(space: string): Promise<void> {
    fireEvent.click(screen.getByRole('link', { name: space }));

    await screen.findByRole('heading', { name: space, level: 1 });
}

beforeEach(() => {
    openingAt('/');
});

afterEach(() => {
    openingAt('/');
    asked.length = 0;
    window.localStorage.clear();
    window.sessionStorage.clear();
    document.documentElement.removeAttribute('lang');
    document.documentElement.removeAttribute('data-theme');
});

describe('App', () => {
    it('says it is reading while the answer has not arrived', () => {
        renderApp(servedFrom, heldCredential, () => () => new Promise<ClientResponse>(() => undefined));

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
        const twoMailboxes = directory(true, [workAccount, { ...workAccount, id: 'archive', displayName: 'Archive' }]);

        renderApp(servedFrom, heldCredential, deploymentAnswering(twoMailboxes));

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
        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(false, [workAccount])));

        expect(
            await screen.findByText('This deployment is not refreshing the local copy of these accounts.'),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of saying nothing about them', async () => {
        renderApp(servedFrom, heldCredential, deploymentAnswering({ status: 403, body: '' }));

        expect(await screen.findByText('The accounts could not be read: unauthorized.')).toBeDefined();
    });

    it('reads the accounts again when the person asks it to, rather than only on a reload', async () => {
        let accounts: Answer = { status: 503, body: '' };
        const deployment: DeploymentTransport = () => (request) =>
            Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));

        renderApp(servedFrom, heldCredential, deployment);
        const retry = await screen.findByRole('button', { name: 'Try again' });

        accounts = directory(true, [workAccount]);
        fireEvent.click(retry);

        expect(await screen.findByText('Every account is up to date.')).toBeDefined();
    });

    it('reads its mail with the credential it holds rather than with one written into the client', async () => {
        renderApp(servedFrom, typedCredential);
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([typedCredential]);
    });
});

describe('App sign-in', () => {
    it('asks for a user name and a password when nothing has been signed in with', () => {
        renderApp(servedFrom, null);

        expect(screen.getByRole('textbox', { name: 'User name' })).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('asks for nothing but the credential where the origin that served the client is the deployment', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('textbox', { name: 'Deployment address' })).toBeNull();
    });

    it('opens the frame once the credential somebody typed has been accepted', async () => {
        renderApp(servedFrom, null);

        signIn();

        await framed();
        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('presents the credential it composed rather than anything it was handed', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([typedCredential]);
    });

    it('keeps the credential it signed in with, so a later start opens already signed in', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        expect([...credentials.kept]).toEqual([[servedFrom.deployment.baseAddress, typedCredential]]);
    });

    it('says how long the password will be kept before anybody has typed one', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('untilSignedOut'));

        expect(
            screen.getByText(
                'Your password is kept in this machine’s keychain until you sign out. Signing out is what removes it.',
            ),
        ).toBeDefined();
    });

    it('says why it will ask again where nothing may be kept beyond the run', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('untilTheClientCloses'));

        expect(
            screen.getByText(
                'Your password is kept until you close MailFathom, and you will be asked for it again — this machine offers no keychain to keep it in safely.',
            ),
        ).toBeDefined();
    });

    it('reports a refused credential as one thing rather than as a guess about which half was wrong', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn();

        expect(
            await screen.findByText('The user name or the password is not accepted by this deployment.'),
        ).toBeDefined();
    });

    it('tells somebody whose deployment offers no passwords that, rather than refusing their credential', async () => {
        const bearerOnly = { status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } };

        renderApp(servedFrom, null, deploymentRefusing(bearerOnly));
        signIn();

        expect(
            await screen.findByText(
                'This deployment does not accept a user name and a password. Whoever runs it has to enable that before you can sign in here.',
            ),
        ).toBeDefined();
    });

    it('names neither the password nor the value composed from it when the deployment refuses it', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn('owner', 'open sesame');
        await screen.findByRole('alert');

        expect(document.body.textContent).not.toContain('open sesame');
        expect(document.body.textContent).not.toContain('b3duZXI6b3BlbiBzZXNhbWU=');
    });

    it('puts somebody in front of the sign-in when the deployment stops accepting what was kept', async () => {
        const credentials = storeKeeping();
        await credentials.keep(servedFrom.deployment, typedCredential);

        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), credentials);

        expect(
            await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.'),
        ).toBeDefined();
        expect([...credentials.kept]).toEqual([]);
    });

    it('clears what the person carried between the spaces when the deployment stops accepting what was kept', async () => {
        // One deployment whose answer to the accounts changes under the client, which is what a service restarted with
        // a different password looks like from here: the read that was working starts refusing.
        let accounts: Answer = { status: 503, body: '' };
        const send: DeploymentTransport = () => (request) => {
            asked.push(request);

            return Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));
        };

        renderApp(servedFrom, null, send, storeKeeping());
        signIn();
        await screen.findByRole('button', { name: 'Try again' });

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });

        accounts = { status: 401, body: '' };
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.');

        // Being turned away mid-session returns this person to the sign-in exactly as signing out does, so what they
        // were carrying goes with the credential there too rather than waiting for the next person to read.
        accounts = directory(true, [workAccount]);
        signIn();
        await framed();

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
    });

    it('says the password is still on the machine when the deployment stops accepting it and the store will not', async () => {
        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), storeRefusingToForget());

        // Two things went wrong at once and both are the person's to act on: the deployment no longer accepts what was
        // kept, and the store would not give it up — so it is read back on every later start until they remove it.
        expect(
            await screen.findByText('This deployment has stopped accepting the password that was kept. Sign in again.'),
        ).toBeDefined();
        expect(
            await screen.findByText(
                'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
            ),
        ).toBeDefined();
    });

    it('clears the credential and everything read with it when somebody signs out', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

        expect(screen.getByRole('textbox', { name: 'User name' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        expect([...credentials.kept]).toEqual([]);
    });

    it('clears what the person carried between the spaces when somebody signs out', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping());
        signIn();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'Mailbox in scope' }), { target: { value: 'work' } });

        fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
        signIn();
        await framed();

        // The next person to sign in on this machine reads their own empty screen rather than the last one's question
        // and the mailbox it was scoped to.
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', '');
    });

    it('says the password was not kept when the store would not write it, without refusing the sign-in', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToKeep());
        signIn();
        await framed();

        // The screen promised how long the password would last before anybody typed, so a store that refused the write
        // says so — inside the frame, because signing in worked and only the keeping did not.
        expect(
            screen.getByText(
                'Your password could not be stored on this machine, so you will be asked for it again the next time you open MailFathom. You are signed in either way.',
            ),
        ).toBeDefined();
    });

    it('says the password is still on the machine when the store would not remove it', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToForget());
        signIn();
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));

        // Signing out told them the password would be removed, so a store that refused has to say so rather than let
        // the next start read it back while they believe it is gone.
        expect(
            await screen.findByText(
                'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
            ),
        ).toBeDefined();
    });

    it('places focus on what it has to say about the credential, rather than on the field below it', async () => {
        renderApp(servedFrom, typedCredential, deploymentAnswering({ status: 401, body: '' }), storeKeeping());

        const notice = await screen.findByText(
            'This deployment has stopped accepting the password that was kept. Sign in again.',
        );

        // Each of these sentences is inserted in the same commit as its own text, which a live region does not
        // announce — so somebody signed out mid-session would otherwise land in the form with nothing read to them.
        expect(document.activeElement).toBe(notice.parentElement);
    });

    it('places focus on the credential where the deployment is already known', () => {
        renderApp(servedFrom, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'User name' }));
    });

    it('puts focus at the start of the workspace once somebody has signed in, rather than leaving it behind', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        expect(document.activeElement?.contains(screen.getByRole('main'))).toBe(true);
    });
});

describe('App deployment', () => {
    it('reads from the deployment it was pointed at, rather than from one written into the client', async () => {
        renderApp(chose('https://elsewhere.example.invalid'));
        await framed();

        expect(routesAsked()).toEqual(['https://elsewhere.example.invalid/api/client/accounts']);
    });

    it('asks for an address when nothing has said where the deployment is', () => {
        renderApp(null, null);

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('places focus on the address where that is the first thing it is asking for', () => {
        renderApp(null, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('signs in against the address somebody named, in the same act as naming it', async () => {
        renderApp(null, null);

        typeAddress('mail.example.test');
        signIn();
        await framed();

        expect(routesAsked()).toEqual([
            'https://mail.example.test/api/client/session',
            'https://mail.example.test/api/client/accounts',
        ]);
    });

    it('offers to be pointed elsewhere once somebody named the deployment themselves', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        expect(screen.getByRole('button', { name: 'Point somewhere else' })).toBeDefined();
    });

    it('offers a way out of the sign-in screen a chosen deployment left behind', () => {
        renderApp(chose('https://mail.example.invalid'), null);

        // A chosen address renders no address field, and it is read back out of storage on every later start — so
        // without this, somebody whose password no longer works has no way to point the client anywhere else.
        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
    });

    it('offers nothing to change on the sign-in screen the origin that served the client left', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('button', { name: 'Point somewhere else' })).toBeNull();
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

    it('calls off an attempt whose deployment was abandoned while it ran', async () => {
        const credentials = storeKeeping();
        const held: (() => void)[] = [];
        let answering = false;

        // A deployment that answers nothing until this test says so, and everything from then on: what the attempt is
        // holding on is the first request, and what would carry it through to signing somebody in is the rest of them
        // answering normally once it resumes.
        const send: DeploymentTransport = () => (request) => {
            asked.push(request);
            const answer = complete(request.path.endsWith('/session') ? accepted : directory(true, [workAccount]));

            if (answering) {
                return Promise.resolve(answer);
            }

            return new Promise((resolved) => {
                held.push(() => {
                    resolved(answer);
                });
            });
        };

        renderApp(chose('https://mail.example.invalid'), null, send, credentials);
        signIn();

        // The way out of a chosen address sits above the form and stays live while an attempt runs. An answer for the
        // address somebody has just pointed away from would sign them back in to it and write the credential into the
        // store that was asked to clear it.
        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));
        answering = true;
        for (const answer of held) {
            answer();
        }

        expect(await screen.findByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        await waitFor(() => {
            expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        });
        expect([...credentials.kept]).toEqual([]);
    });

    it('forgets the credential of the deployment it is pointed away from', async () => {
        const credentials = storeKeeping();
        const chosen = chose('https://mail.example.invalid');
        await credentials.keep(chosen.deployment, heldCredential);

        renderApp(chosen, heldCredential, deploymentAnswering(), credentials);
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect([...credentials.kept]).toEqual([]);
    });

    it('leaves focus where the document opened it when the credential was already held', async () => {
        renderApp();
        await framed();

        // A cold start is not a view change. `main.tsx` mounts under `StrictMode`, so every effect runs twice here as
        // it does under `pnpm dev`, and a guard that only survives one invocation would have pulled focus by now.
        expect(document.activeElement).toBe(document.body);
    });

    it('puts focus back in the address when it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('signs in against the next deployment of its own, and never reads the one before it again', async () => {
        renderApp(chose('https://first.example.invalid'));
        await framed();

        fireEvent.click(screen.getByRole('button', { name: 'Point somewhere else' }));
        typeAddress('second.example.invalid');
        signIn();
        await framed();

        expect(routesAsked()).toEqual([
            'https://first.example.invalid/api/client/accounts',
            'https://second.example.invalid/api/client/session',
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
