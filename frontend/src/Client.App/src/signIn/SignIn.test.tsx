// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, DeploymentAddress, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { SignIn, type SignInNotice } from './SignIn';
import { longestCredentialPart } from './credentialEntry';
import type { CredentialLifetime } from './credentialStore';

// Everything below reaches the transport this screen takes from its caller, so nothing here patches a global or stands
// up a server. What is under test is the real request, the real parsing, and the real failure mapping; only the answer
// they are given is the test's.

const signedIn: MailFathomTransport = () =>
    Promise.resolve({
        status: 200,
        body: JSON.stringify({ service: 'MailFathom', version: '0.8.0', permissions: [] }),
        headers: {},
    });

const nothingThere: MailFathomTransport = () => Promise.reject(new TypeError('Failed to fetch'));

const somethingElse: MailFathomTransport = () =>
    Promise.resolve({ status: 200, body: '<!doctype html><title>Sign in</title>', headers: {} });

/** A MailFathom deployment that has not enabled passwords, which challenges without naming the method this client has. */
const noPasswords: MailFathomTransport = () =>
    Promise.resolve({ status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } });

/** A MailFathom deployment that takes passwords and would not take this one. */
const credentialRefused: MailFathomTransport = () =>
    Promise.resolve({
        status: 401,
        body: '',
        headers: { 'www-authenticate': 'Bearer realm="MailFathom", Basic realm="MailFathom", charset="UTF-8"' },
    });

/** A deployment that knows who is asking and will not let them read any mail. */
const grantMissing: MailFathomTransport = () => Promise.resolve({ status: 403, body: '', headers: {} });

/** What every request the screen made carried as a credential, so a test sees where a password did and did not go. */
function credentialsSent(asked: readonly ClientRequest[]): (string | undefined)[] {
    return asked.map((request) => request.headers['Authorization']);
}

/** A transport recording what it was asked, answering the way the one handed to it does. */
function recording(asked: ClientRequest[], answer: MailFathomTransport): MailFathomTransport {
    return (request) => {
        asked.push(request);

        return answer(request);
    };
}

const knownDeployment: DeploymentAddress = { baseAddress: 'https://mail.example.invalid' };

/** What the screen reported and what it started, so a test sees an attempt being called off rather than only ignored. */
interface Rendered {
    readonly presented: { deployment: DeploymentAddress; authorization: string }[];
    readonly attempts: AbortSignal[];
}

// The screen is handed a transport per attempt rather than one transport, because giving up on an attempt is what
// abandons it. Collecting the signals it was given is how a test sees whether an attempt was actually called off,
// which is the difference between the screen ignoring an answer and the request being cancelled.
function renderScreen(
    send: MailFathomTransport,
    deployment: DeploymentAddress | null = null,
    lifetime: CredentialLifetime = 'untilTheTabCloses',
    notices: readonly SignInNotice[] = [],
): Rendered {
    const presented: { deployment: DeploymentAddress; authorization: string }[] = [];
    const attempts: AbortSignal[] = [];

    render(
        <LocalizationProvider>
            <SignIn
                deployment={deployment}
                lifetime={lifetime}
                notices={notices}
                send={(abandoned) => {
                    attempts.push(abandoned);

                    return send;
                }}
                onSignedIn={(reached, authorization) => {
                    presented.push({ deployment: reached, authorization });
                }}
            />
        </LocalizationProvider>,
    );

    return { presented, attempts };
}

function typeAddress(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), { target: { value: entry } });
}

function typeCredential(userName = 'owner', password = 'open sesame'): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'User name' }), { target: { value: userName } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
}

function submit(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

function permitClearText(): void {
    fireEvent.click(screen.getByRole('checkbox', { name: 'Reach this deployment over plain HTTP' }));
}

// The console spies one test below installs would otherwise survive into every test after it in this file, silencing
// whatever React or Testing Library raised there. The store is one per file rather than one per test.
afterEach(() => {
    vi.restoreAllMocks();
});

describe('SignIn', () => {
    it('asks for the address and the credential in one place, rather than across two screens', () => {
        renderScreen(signedIn);

        expect(screen.getByRole('textbox', { name: 'Deployment address' })).toBeDefined();
        expect(screen.getByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeDefined();
        expect(screen.getByRole('textbox', { name: 'User name' })).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
    });

    it('asks for two controls rather than four where the deployment is already known', () => {
        renderScreen(signedIn, knownDeployment);

        expect(screen.queryByRole('textbox', { name: 'Deployment address' })).toBeNull();
        expect(screen.queryByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeNull();
        expect(screen.getByRole('textbox', { name: 'User name' })).toBeDefined();
    });

    it('puts the cursor in the address, because the view changed and focus is placed rather than left behind', () => {
        renderScreen(signedIn);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('says what plain HTTP costs beside the control that permits it, rather than after it is chosen', () => {
        renderScreen(signedIn);

        expect(
            screen.getByText(
                'Your password is encoded rather than encrypted, on every request. Anybody between this client and the deployment can read it. Leave this off unless the network between them is yours.',
            ),
        ).toBeDefined();
    });

    it('asks for an address rather than reaching nowhere when nothing was typed', () => {
        const { presented } = renderScreen(signedIn);

        submit();

        expect(screen.getByRole('alert').textContent).toBe('Name the deployment that holds your mail.');
        expect(presented).toEqual([]);
    });

    it('says what an address is when what was typed is not one', () => {
        renderScreen(signedIn);

        typeAddress('my mail server');
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'That is not an address. Name the host it answers on, and a port where it uses one.',
        );
    });

    it('will not carry a password over plain HTTP until somebody says it may', () => {
        const { presented } = renderScreen(signedIn);

        typeAddress('http://mail.example.test');
        typeCredential();
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'That address is plain HTTP, which this client will not send a password over until you say it may.',
        );
        expect(presented).toEqual([]);
    });

    it('takes the refusal away as soon as the address is being corrected, rather than after the next attempt', () => {
        renderScreen(signedIn);

        typeAddress('my mail server');
        submit();
        typeAddress('mail.example.test');

        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('asks for the half of the credential that is missing, rather than sending the other one', () => {
        const { presented } = renderScreen(signedIn, knownDeployment);

        typeCredential('owner', '');
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'Type the user name and the password your deployment gave you.',
        );
        expect(presented).toEqual([]);
    });

    it('refuses a user name carrying the separator, rather than presenting a credential split in the wrong place', () => {
        const { presented } = renderScreen(signedIn, knownDeployment);

        typeCredential('own:er');
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'A user name cannot contain a colon, which is what separates it from the password when it is sent.',
        );
        expect(presented).toEqual([]);
    });

    it('refuses a credential longer than it will present, rather than sending a truncated one', () => {
        const { presented } = renderScreen(signedIn, knownDeployment);

        typeCredential('owner', 'p'.repeat(longestCredentialPart + 1));
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'That user name or password is longer than this client will present. Check what was pasted in.',
        );
        expect(presented).toEqual([]);
    });

    it('lets a credential exactly as long as the bound through, so the bound is where it says it is', async () => {
        const { presented } = renderScreen(signedIn, knownDeployment);

        typeCredential('owner', 'p'.repeat(longestCredentialPart));
        submit();

        await vi.waitFor(() => {
            expect(presented.length).toBe(1);
        });
    });

    it('leaves the password unmarked for a refusal about the user name alone', () => {
        renderScreen(signedIn, knownDeployment);

        typeCredential('own:er');
        submit();

        expect(screen.getByRole('textbox', { name: 'User name' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe('false');
    });

    it('marks both halves of the credential when the deployment refused the credential itself', async () => {
        renderScreen(credentialRefused, knownDeployment);

        typeCredential();
        await screen.findByRole('button', { name: 'Sign in' });
        submit();
        await screen.findByRole('alert');

        expect(screen.getByRole('textbox', { name: 'User name' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe('true');
    });

    it('says the deployment did not answer, without naming an address nobody typed', async () => {
        renderScreen(nothingThere, knownDeployment);

        typeCredential();
        submit();

        // The two-control shape has no address on it and nobody named one, so the sentence about checking an address
        // and checking that the deployment is running would point at something that is not theirs to act on.
        expect((await screen.findByRole('alert')).textContent).toBe(
            'The deployment did not answer. Try again in a moment.',
        );
    });

    it('puts focus back on the control that started an attempt when the attempt is refused', async () => {
        renderScreen(credentialRefused, knownDeployment);

        typeCredential();
        submit();
        await screen.findByRole('alert');

        // Starting an attempt disables the submit button, which drops focus to the document — so without placing it
        // the refusal is announced with focus nowhere and a keyboard reader tabs in from the top of the page.
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Sign in' }));
    });

    it('puts focus back on the control that started an attempt when the attempt is given up on', () => {
        renderScreen(() => new Promise(() => undefined), knownDeployment);

        typeCredential();
        submit();
        fireEvent.click(screen.getByRole('button', { name: 'Stop trying' }));

        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Sign in' }));
    });

    it('marks the control a refusal is about, so what has to change is the field that is wrong', () => {
        renderScreen(signedIn);

        typeAddress('my mail server');
        submit();

        expect(screen.getByRole('textbox', { name: 'Deployment address' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByRole('textbox', { name: 'User name' }).getAttribute('aria-invalid')).toBe('false');
    });

    it('sends nothing over plain HTTP until that is declared, and then signs in over it', async () => {
        const asked: ClientRequest[] = [];
        const { presented } = renderScreen(recording(asked, signedIn));

        typeAddress('http://mail.example.test');
        typeCredential();
        submit();
        permitClearText();
        submit();

        await vi.waitFor(() => {
            expect(presented).toEqual([
                {
                    deployment: { baseAddress: 'http://mail.example.test' },
                    authorization: 'Basic b3duZXI6b3BlbiBzZXNhbWU=',
                },
            ]);
        });

        // Both requests belong to the second attempt, and the first of them carries nothing: the refusal ran before
        // anything went out at all, so no credential travelled over the transport the first attempt was refused for.
        expect(asked.map((request) => request.path)).toEqual([
            'http://mail.example.test/api/client/session',
            'http://mail.example.test/api/client/session',
        ]);
        expect(credentialsSent(asked)).toEqual([undefined, 'Basic b3duZXI6b3BlbiBzZXNhbWU=']);
    });

    it('says it is signing in while the answer has not arrived', () => {
        renderScreen(() => new Promise(() => undefined), knownDeployment);

        typeCredential();
        submit();

        expect(screen.getByRole('status').textContent).toBe('Signing in…');
    });

    it('lets an attempt that never answers be given up on, rather than holding the screen on it', () => {
        const { attempts } = renderScreen(() => new Promise(() => undefined), knownDeployment);

        typeCredential();
        submit();

        fireEvent.click(screen.getByRole('button', { name: 'Stop trying' }));

        // Back where the person left it: no attempt is reported as running, the credential can be corrected and tried
        // again, and the request the abandoned attempt started was cancelled rather than left on the wire.
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.getByRole('button', { name: 'Sign in' }).hasAttribute('disabled')).toBe(false);
        expect(attempts.map((abandoned) => abandoned.aborted)).toEqual([true]);
    });

    it('offers nothing to give up on before an attempt has been started', () => {
        renderScreen(signedIn, knownDeployment);

        expect(screen.queryByRole('button', { name: 'Stop trying' })).toBeNull();
    });

    it('signs in against the deployment once it answers as one, under the scheme the client supplied', async () => {
        const { presented } = renderScreen(signedIn);

        typeAddress('mail.example.test:8443');
        typeCredential();
        submit();

        await vi.waitFor(() => {
            expect(presented).toEqual([
                {
                    deployment: { baseAddress: 'https://mail.example.test:8443' },
                    authorization: 'Basic b3duZXI6b3BlbiBzZXNhbWU=',
                },
            ]);
        });
    });

    it('asks a typed address what it is before it hands the address a password', async () => {
        const asked: ClientRequest[] = [];
        const { presented } = renderScreen(recording(asked, signedIn));

        typeAddress('mail.example.test');
        typeCredential();
        submit();

        await vi.waitFor(() => {
            expect(presented.length).toBe(1);
        });

        // The property is the order rather than the count: the credential goes out only after an answer came back
        // establishing that MailFathom is at the address somebody typed.
        expect(credentialsSent(asked)).toEqual([undefined, 'Basic b3duZXI6b3BlbiBzZXNhbWU=']);
    });

    it('sends no password at all to a typed address that did not answer as MailFathom', async () => {
        const asked: ClientRequest[] = [];
        const { presented } = renderScreen(recording(asked, somethingElse));

        typeAddress('mail.mistyped.test');
        typeCredential();
        submit();

        // The typo is the case this exists for: whatever is really at that address is handed nothing, and there is no
        // second attempt to take a password back from.
        expect((await screen.findByRole('alert')).textContent).toBe('Something answered there, but not as MailFathom.');
        expect(credentialsSent(asked)).toEqual([undefined]);
        expect(presented).toEqual([]);
    });

    it('says a typed deployment takes no passwords rather than presenting one it would refuse', async () => {
        const asked: ClientRequest[] = [];
        const { presented } = renderScreen(recording(asked, noPasswords));

        typeAddress('mail.example.test');
        typeCredential();
        submit();

        expect((await screen.findByRole('alert')).textContent).toBe(
            'This deployment does not accept a user name and a password. Whoever runs it has to enable that before you can sign in here.',
        );
        expect(credentialsSent(asked)).toEqual([undefined]);
        expect(presented).toEqual([]);
    });

    it('says nothing answered rather than reporting a credential it never presented', async () => {
        const { presented } = renderScreen(nothingThere);

        typeAddress('mail.example.test');
        typeCredential();
        submit();

        // The four-control shape is the one where checking the address is something the person can act on, which is
        // what that sentence asks them to do.
        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing answered there. Check the address, and check that the deployment is running.',
        );
        expect(presented).toEqual([]);
    });

    it('tries once and never again without the transport security the first attempt asked for', async () => {
        const asked: string[] = [];
        renderScreen((request) => {
            asked.push(request.path);

            return nothingThere(request);
        }, knownDeployment);

        typeCredential();
        submit();

        await screen.findByRole('alert');
        expect(asked).toEqual(['https://mail.example.invalid/api/client/session']);
    });

    it('says what answered was not MailFathom rather than signing in against anything that replies', async () => {
        const { presented } = renderScreen(somethingElse, knownDeployment);

        typeCredential();
        submit();

        expect((await screen.findByRole('alert')).textContent).toBe('Something answered there, but not as MailFathom.');
        expect(presented).toEqual([]);
    });

    it('says why it is asking again when the deployment stopped accepting what was kept', () => {
        renderScreen(signedIn, knownDeployment, 'untilSignedOut', ['credentialNoLongerAccepted']);

        expect(screen.getByRole('status').textContent).toBe(
            'This deployment has stopped accepting the password that was kept. Sign in again.',
        );
    });

    it('says the password is still on the machine when signing out could not remove it', () => {
        renderScreen(signedIn, knownDeployment, 'untilSignedOut', ['passwordNotRemoved']);

        expect(screen.getByRole('status').textContent).toBe(
            'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
        );
    });

    it('says both things at once when the credential was refused and the password could not be removed', () => {
        renderScreen(signedIn, knownDeployment, 'untilSignedOut', ['credentialNoLongerAccepted', 'passwordNotRemoved']);

        // Two facts rather than one told twice: a person is signed out for one reason and is still carrying a password
        // for another, and hearing only the first would leave them believing the machine holds nothing.
        expect(screen.getAllByRole('status').map((shown) => shown.textContent)).toEqual([
            'This deployment has stopped accepting the password that was kept. Sign in again.',
            'Signing out did not remove the password from this machine’s credential store, so it is still kept there. Remove it in the store itself, or sign in and out again.',
        ]);
    });

    it('says the credential was accepted and reads nothing when the deployment holds no grant for it', async () => {
        renderScreen(grantMissing, knownDeployment);
        typeCredential('owner', 'open sesame');
        submit();

        // The one refusal that is not about what was typed: retyping the password would change nothing, so the
        // sentence says the credential was accepted rather than asking for it again.
        expect((await screen.findByRole('alert')).textContent).toBe(
            'This deployment accepted the credential, but it is allowed to read no mail.',
        );
        expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe('false');
    });

    it.each([
        ['a deployment that turned the credential away', credentialRefused],
        ['a deployment that takes no passwords', noPasswords],
        ['a deployment that is not there', nothingThere],
        ['an address answering as something else', somethingElse],
        ['a deployment answering a grant this credential does not hold', grantMissing],
    ])('reports %s without the password or the value composed from it reaching anything', async (_, transport) => {
        const reported: unknown[] = [];
        for (const level of ['debug', 'error', 'info', 'log', 'warn'] as const) {
            vi.spyOn(console, level).mockImplementation((...written: unknown[]) => reported.push(...written));
        }

        renderScreen(transport, knownDeployment);
        typeCredential('owner', 'open sesame');
        submit();

        // Every path out of a refusal at once, because the obligation is about all of them rather than about the one a
        // screen happens to render: what a person is shown, and what anything watching this run was told, carry
        // neither half of the credential nor the value the two were encoded into.
        await screen.findByRole('alert');
        expect(reported).toEqual([]);
        expect(document.body.textContent).not.toContain('open sesame');
        expect(document.body.textContent).not.toContain('b3duZXI6b3BlbiBzZXNhbWU=');
    });

    it('says the password lasts only as long as the tab where nothing may be kept beyond it', () => {
        renderScreen(signedIn, knownDeployment);

        expect(
            screen.getByText(
                'Your password is kept until you close this tab, and you will be asked for it again — a password left in a browser can be read by anything that reaches this page.',
            ),
        ).toBeDefined();
    });
});
