// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
    sessionExchangeRoute,
    type ClientRequest,
    type DeploymentAddress,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { AdoptedDeployment } from '../deployment/adoptedDeployment';
import { LocalizationProvider } from '../localization/Localization';
import type { CredentialNotice } from './CredentialNotices';
import { SignIn } from './SignIn';
import { longestCredentialPart } from './credentialEntry';
import type { CredentialLifetime } from './credentialStore';
import type { KeptSession } from './keptSession';

// Everything below reaches the transport this screen takes from its caller, so nothing here patches a global or stands
// up a server. What is under test is the real request, the real parsing, and the real failure mapping; only the answer
// they are given is the test's.

const mintedToken = 'mfs_signinscreen.c2lnbi1pbi1zY3JlZW4tcHJvb2Y';
const mintedExpiry = '2126-08-31T21:41:00+00:00';

/** What the screen reports once a deployment took the credential, which is a session rather than the credential. */
const mintedSession: KeptSession = { authorization: `Bearer ${mintedToken}`, expiresAt: mintedExpiry, person: 'user' };

// One deployment answering two routes, because signing in asks two questions: what is at this address, and will it take
// this credential. The second answers with a session, and that is the only place a token comes from.
const signedIn: MailFathomTransport = (request) =>
    Promise.resolve(
        request.path.endsWith(sessionExchangeRoute)
            ? { status: 200, body: JSON.stringify({ token: mintedToken, expiresAt: mintedExpiry }), headers: {} }
            : {
                  status: 200,
                  body: JSON.stringify({ service: 'MailFathom', version: '0.8.0', permissions: [] }),
                  headers: {},
              },
    );

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

/** The deployment the client was served by, which is the shape a web head's sign-in screen is rendered in. */
const servingDeployment: AdoptedDeployment = { deployment: knownDeployment, origin: 'serving' };

/** The deployment a client somebody was handed was configured with, which is the shape that locks the address field. */
const configuredDeployment: AdoptedDeployment = { deployment: knownDeployment, origin: 'configured' };

/** What the screen reported and what it started, so a test sees an attempt being called off rather than only ignored. */
interface Rendered {
    readonly presented: { deployment: DeploymentAddress; session: KeptSession }[];
    readonly attempts: AbortSignal[];
    readonly pointedAway: boolean[];
}

// The screen is handed a transport per attempt rather than one transport, because giving up on an attempt is what
// abandons it. Collecting the signals it was given is how a test sees whether an attempt was actually called off,
// which is the difference between the screen ignoring an answer and the request being cancelled.
function renderScreen(
    send: MailFathomTransport,
    adopted: AdoptedDeployment | null = null,
    lifetime: CredentialLifetime = 'untilTheTabCloses',
    notices: readonly CredentialNotice[] = [],
    clearTextPermitted: boolean | null = null,
): Rendered {
    const presented: { deployment: DeploymentAddress; session: KeptSession }[] = [];
    const attempts: AbortSignal[] = [];
    const pointedAway: boolean[] = [];

    render(
        <LocalizationProvider>
            <SignIn
                adopted={adopted}
                clearTextPermitted={clearTextPermitted}
                lifetime={lifetime}
                notices={notices}
                send={(abandoned) => {
                    attempts.push(abandoned);

                    return send;
                }}
                onSignedIn={(reached, session) => {
                    presented.push({ deployment: reached, session });
                }}
                onPointSomewhereElse={() => {
                    pointedAway.push(true);
                }}
            />
        </LocalizationProvider>,
    );

    return { presented, attempts, pointedAway };
}

function typeAddress(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Server' }), { target: { value: entry } });
}

function typeCredential(userName = 'user', password = 'open sesame'): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Login' }), { target: { value: userName } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
}

function submit(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
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

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
        expect(screen.getByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeDefined();
        expect(screen.getByRole('textbox', { name: 'Login' })).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
    });

    it('asks for two controls rather than four where the deployment is already known', () => {
        renderScreen(signedIn, servingDeployment);

        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
        expect(screen.queryByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeNull();
        expect(screen.getByRole('textbox', { name: 'Login' })).toBeDefined();
    });

    it('puts the cursor in the address, because the view changed and focus is placed rather than left behind', () => {
        renderScreen(signedIn);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Server' }));
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
        const { presented } = renderScreen(signedIn, servingDeployment);

        typeCredential('user', '');
        submit();

        expect(screen.getByRole('alert').textContent).toBe('Type the login and the password your deployment gave you.');
        expect(presented).toEqual([]);
    });

    it('refuses a login carrying the separator, rather than presenting a credential split in the wrong place', () => {
        const { presented } = renderScreen(signedIn, servingDeployment);

        typeCredential('own:er');
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'A login cannot contain a colon, which is what separates it from the password when it is sent.',
        );
        expect(presented).toEqual([]);
    });

    it('refuses a credential longer than it will present, rather than sending a truncated one', () => {
        const { presented } = renderScreen(signedIn, servingDeployment);

        typeCredential('user', 'p'.repeat(longestCredentialPart + 1));
        submit();

        expect(screen.getByRole('alert').textContent).toBe(
            'That login or password is longer than this client will present. Check what was pasted in.',
        );
        expect(presented).toEqual([]);
    });

    it('lets a credential exactly as long as the bound through, so the bound is where it says it is', async () => {
        const { presented } = renderScreen(signedIn, servingDeployment);

        typeCredential('user', 'p'.repeat(longestCredentialPart));
        submit();

        await vi.waitFor(() => {
            expect(presented.length).toBe(1);
        });
    });

    it('leaves the password unmarked for a refusal about the login alone', () => {
        renderScreen(signedIn, servingDeployment);

        typeCredential('own:er');
        submit();

        expect(screen.getByRole('textbox', { name: 'Login' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe('false');
    });

    it('marks both halves of the credential when the deployment refused the credential itself', async () => {
        renderScreen(credentialRefused, servingDeployment);

        typeCredential();
        await screen.findByRole('button', { name: 'Connect' });
        submit();
        await screen.findByRole('alert');

        expect(screen.getByRole('textbox', { name: 'Login' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByLabelText('Password').getAttribute('aria-invalid')).toBe('true');
    });

    it('says the deployment did not answer, without naming an address nobody typed', async () => {
        renderScreen(nothingThere, servingDeployment);

        typeCredential();
        submit();

        // The two-control shape has no address on it and nobody named one, so the sentence about checking an address
        // and checking that the deployment is running would point at something that is not theirs to act on.
        expect((await screen.findByRole('alert')).textContent).toBe(
            'The deployment did not answer. Try again in a moment.',
        );
    });

    it('puts focus back on the control that started an attempt when the attempt is refused', async () => {
        renderScreen(credentialRefused, servingDeployment);

        typeCredential();
        submit();
        await screen.findByRole('alert');

        // Starting an attempt disables the submit button, which drops focus to the document — so without placing it
        // the refusal is announced with focus nowhere and a keyboard reader tabs in from the top of the page.
        //
        // Waited for rather than read once the refusal is on the screen: placing focus is an effect, and an effect
        // runs after the commit that inserted the text this awaited.
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Connect' }));
        });
    });

    it('puts focus back on the control that started an attempt when the attempt is given up on', () => {
        renderScreen(() => new Promise(() => undefined), servingDeployment);

        typeCredential();
        submit();
        fireEvent.click(screen.getByRole('button', { name: 'Stop trying' }));

        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Connect' }));
    });

    it('marks the control a refusal is about, so what has to change is the field that is wrong', () => {
        renderScreen(signedIn);

        typeAddress('my mail server');
        submit();

        expect(screen.getByRole('textbox', { name: 'Server' }).getAttribute('aria-invalid')).toBe('true');
        expect(screen.getByRole('textbox', { name: 'Login' }).getAttribute('aria-invalid')).toBe('false');
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
                { deployment: { baseAddress: 'http://mail.example.test' }, session: mintedSession },
            ]);
        });

        // Both requests belong to the second attempt, and the first of them carries nothing: the refusal ran before
        // anything went out at all, so no credential travelled over the transport the first attempt was refused for.
        expect(asked.map((request) => request.path)).toEqual([
            'http://mail.example.test/api/client/session',
            'http://mail.example.test/api/client/session/token',
        ]);
        expect(credentialsSent(asked)).toEqual([undefined, 'Basic dXNlcjpvcGVuIHNlc2FtZQ==']);
    });

    // The fields stay editable while an attempt runs, so somebody who spots a typo can correct it without waiting.
    // What must not follow is the screen renaming the attempt: the request already went to the address it was
    // started against, and a status naming the one being typed would be reporting something that is not happening.
    it('keeps naming the address the attempt was started against while it is being edited', () => {
        renderScreen(() => new Promise(() => undefined));
        typeAddress('first.example.test');
        typeCredential();
        submit();

        typeAddress('second.example.test');

        expect(screen.getAllByText('Connecting to first.example.test…').length).toBeGreaterThan(0);
        expect(screen.queryByText('Connecting to second.example.test…')).toBeNull();
    });

    it('says it is signing in while the answer has not arrived', () => {
        renderScreen(() => new Promise(() => undefined), servingDeployment);

        typeCredential();
        submit();

        expect(screen.getByRole('status').textContent).toBe('Connecting to mail.example.invalid…');
    });

    it('lets an attempt that never answers be given up on, rather than holding the screen on it', () => {
        const { attempts } = renderScreen(() => new Promise(() => undefined), servingDeployment);

        typeCredential();
        submit();

        fireEvent.click(screen.getByRole('button', { name: 'Stop trying' }));

        // Back where the person left it: no attempt is reported as running, the credential can be corrected and tried
        // again, and the request the abandoned attempt started was cancelled rather than left on the wire.
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.getByRole('button', { name: 'Connect' }).hasAttribute('disabled')).toBe(false);
        expect(attempts.map((abandoned) => abandoned.aborted)).toEqual([true]);
    });

    it('offers nothing to give up on before an attempt has been started', () => {
        renderScreen(signedIn, servingDeployment);

        expect(screen.queryByRole('button', { name: 'Stop trying' })).toBeNull();
    });

    it('signs in against the deployment once it answers as one, under the scheme the client supplied', async () => {
        const { presented } = renderScreen(signedIn);

        typeAddress('mail.example.test:8443');
        typeCredential();
        submit();

        await vi.waitFor(() => {
            expect(presented).toEqual([
                { deployment: { baseAddress: 'https://mail.example.test:8443' }, session: mintedSession },
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
        expect(credentialsSent(asked)).toEqual([undefined, 'Basic dXNlcjpvcGVuIHNlc2FtZQ==']);
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
            'This deployment does not accept a login and a password. Whoever runs it has to enable that before you can sign in here.',
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
        }, servingDeployment);

        typeCredential();
        submit();

        await screen.findByRole('alert');
        expect(asked).toEqual(['https://mail.example.invalid/api/client/session/token']);
    });

    it('says what answered was not MailFathom rather than signing in against anything that replies', async () => {
        const { presented } = renderScreen(somethingElse, servingDeployment);

        typeCredential();
        submit();

        expect((await screen.findByRole('alert')).textContent).toBe('Something answered there, but not as MailFathom.');
        expect(presented).toEqual([]);
    });

    it('says why it is asking again when the deployment stopped accepting what was kept', () => {
        renderScreen(signedIn, servingDeployment, 'untilSignedOut', ['credentialNoLongerAccepted']);

        expect(screen.getByRole('status').textContent).toBe(
            'This deployment has stopped accepting the sign-in that was kept. Sign in again.',
        );
    });

    it('says the password is still on the machine when signing out could not remove it', () => {
        renderScreen(signedIn, servingDeployment, 'untilSignedOut', ['sessionNotRemoved']);

        expect(screen.getByRole('status').textContent).toBe(
            'Signing out did not remove the sign-in from this machine’s credential store, so it is still kept there. MailFathom was asked to end the session, and it stops working on its own in any case. Remove the entry in the store itself if you would rather it were gone now.',
        );
    });

    it('says both things at once when the credential was refused and the password could not be removed', () => {
        renderScreen(signedIn, servingDeployment, 'untilSignedOut', [
            'credentialNoLongerAccepted',
            'sessionNotRemoved',
        ]);

        // Two facts rather than one told twice: a person is signed out for one reason and is still carrying a password
        // for another, and hearing only the first would leave them believing the machine holds nothing.
        expect(screen.getAllByRole('status').map((shown) => shown.textContent)).toEqual([
            'This deployment has stopped accepting the sign-in that was kept. Sign in again.',
            'Signing out did not remove the sign-in from this machine’s credential store, so it is still kept there. MailFathom was asked to end the session, and it stops working on its own in any case. Remove the entry in the store itself if you would rather it were gone now.',
        ]);
    });

    it('says the credential was accepted and reads nothing when the deployment holds no grant for it', async () => {
        renderScreen(grantMissing, servingDeployment);
        typeCredential('user', 'open sesame');
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

        renderScreen(transport, servingDeployment);
        typeCredential('user', 'open sesame');
        submit();

        // Every path out of a refusal at once, because the obligation is about all of them rather than about the one a
        // screen happens to render: what a person is shown, and what anything watching this run was told, carry
        // neither half of the credential nor the value the two were encoded into.
        await screen.findByRole('alert');
        expect(reported).toEqual([]);
        expect(document.body.textContent).not.toContain('open sesame');
        expect(document.body.textContent).not.toContain('dXNlcjpvcGVuIHNlc2FtZQ==');
    });

    it('says the password lasts only as long as the tab where nothing may be kept beyond it', () => {
        renderScreen(signedIn, servingDeployment);

        expect(
            screen.getByText(
                'Your password is not stored anywhere. This sign-in is kept until you close this tab, and you will be asked for your password again — anything that reaches this page can read what a browser keeps.',
            ),
        ).toBeDefined();
    });

    // ADR 0027's amendment: a head whose protected storage is there and unreachable keeps nothing, and the sentence
    // says which of the two reasons it was rather than borrowing the desktop's — which would tell somebody holding a
    // device that offers a keychain that their device offers none.
    it.each([
        [
            'notKeptStorageUnreachable' as const,
            'Your password is not stored anywhere, and this sign-in will not be kept either, so you will be asked for your password again the next time MailFathom starts — this device’s protected storage could not be reached, and MailFathom will not leave a credential anywhere less safe.',
        ],
        [
            'notKeptKeyInvalidated' as const,
            'Your password is not stored anywhere, and this sign-in will not be kept either, so you will be asked for your password again the next time MailFathom starts — this device can no longer give back the key MailFathom stored it under, so anything kept earlier has been removed.',
        ],
    ])('says nothing will be kept, and why, where the store reports %s', (lifetime, sentence) => {
        renderScreen(signedIn, servingDeployment, lifetime);

        expect(screen.getByText(sentence)).toBeDefined();
    });
    // The screen the design project draws says three things about the connection before a password is typed into it:
    // what port will be reached, what it costs to turn TLS off, and whether the password about to be entered can be
    // read back. None of the three is decoration, so each is asserted here rather than looked at once.

    it('says which port the address will reach, so a deployment on one of its own is visible before signing in', () => {
        renderScreen(signedIn);
        typeAddress('mail.example.test:8443');

        expect(screen.getByText('port 8443')).toBeDefined();
    });

    it('says the port is optional and which one it would reach, where the address named none', () => {
        renderScreen(signedIn);
        typeAddress('mail.example.test');

        expect(screen.getByText(/The port is optional — without one this client reaches 443\./u)).toBeDefined();
    });

    // The sentence is about the port reached in the absence of the one that is there, so an address naming its own
    // leaves it unchanged: reading the typed port back would promise that dropping `:8443` reaches 8443.
    it('keeps naming the scheme’s port in the hint where the address named one of its own', () => {
        renderScreen(signedIn);
        typeAddress('mail.example.test:8443');

        expect(screen.getByText(/The port is optional — without one this client reaches 443\./u)).toBeDefined();
    });

    // Before anything is typed there is no resolved connection to read the port off, and the field is exactly where
    // somebody is deciding whether to name one — so the scheme's own port is what the hint states.
    it('names the port the scheme reaches before an address has been typed at all', () => {
        renderScreen(signedIn);

        expect(screen.getByText(/The port is optional — without one this client reaches 443\./u)).toBeDefined();
    });

    it('names the unsecured port instead once a clear-text connection is what has been permitted', () => {
        renderScreen(signedIn);
        permitClearText();

        expect(screen.getByText(/The port is optional — without one this client reaches 80\./u)).toBeDefined();
    });

    it('lets somebody read back the password they typed, and put it away again', () => {
        renderScreen(signedIn);
        typeCredential();

        // A password field carries no role, so it being findable as a textbox is exactly what revealed means: the
        // characters are on the screen and a screen reader will read them.
        expect(screen.queryByRole('textbox', { name: 'Password' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Show the password' }));

        expect(screen.getByRole('textbox', { name: 'Password' })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Hide the password' }));

        expect(screen.queryByRole('textbox', { name: 'Password' })).toBeNull();
    });
    // Somebody handed a packaged client has to be able to read what their password is about to cross, so the address
    // is named under the title beside the lock, as the design draws it, rather than hidden — and there is no field for
    // it, because a field would say it can be changed here.
    it('names a configured address under the title, and offers no field to change it in', () => {
        renderScreen(signedIn, configuredDeployment);

        expect(screen.getByText('mail.example.invalid', { selector: 'span' })).toBeDefined();
        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
    });

    // The disclosure is drawn for every shape of the address, because what a password crosses is worth checking
    // whether or not the address can be changed — but the permission it holds arrived with the configuration, so no
    // control for it is offered, and nothing here offers to point elsewhere.
    it('reads a configured address back row by row, and offers neither the permission nor a way to change it', () => {
        renderScreen(signedIn, configuredDeployment);

        fireEvent.click(screen.getByText('Advanced'));

        expect(screen.getByText('mail.example.invalid', { selector: 'dd' })).toBeDefined();
        expect(screen.queryByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Change the server' })).toBeNull();
    });

    it('offers the way out of a chosen address inside the disclosure, and says so when it is taken', () => {
        const { pointedAway } = renderScreen(signedIn, { deployment: knownDeployment, origin: 'chosen' });

        fireEvent.click(screen.getByText('Advanced'));
        fireEvent.click(screen.getByRole('button', { name: 'Change the server' }));

        expect(pointedAway).toEqual([true]);
    });

    // The badge a closed disclosure carries reads what the address resolved to rather than a permission somebody gave
    // on this screen: an address kept from an earlier run arrived with no permission to read, and is unencrypted
    // exactly when it resolves so.
    it('marks a kept address that resolves to plain HTTP as unencrypted before the disclosure is opened', () => {
        renderScreen(signedIn, { deployment: { baseAddress: 'http://mail.example.invalid' }, origin: 'chosen' });

        expect(screen.getByText('Advanced').closest('summary')?.textContent).toContain('no TLS');
    });

    it('carries no unencrypted mark for a kept address that resolves to TLS', () => {
        renderScreen(signedIn, { deployment: knownDeployment, origin: 'chosen' });

        expect(screen.getByText('Advanced').closest('summary')?.textContent).not.toContain('no TLS');
    });

    // The design offers sign-in through a provider above the password form, and this client speaks HTTP Basic alone:
    // the three stand as controls that are not built rather than being left out, and none of them acts.
    it('draws the provider sign-in the design offers as controls that say they are not built yet', () => {
        renderScreen(signedIn, servingDeployment);

        for (const provider of ['GitHub', 'Gmail', 'Keycloak']) {
            expect(screen.getByRole('button', { name: `${provider} — not built yet` })).toBeDefined();
        }
    });

    it('signs in over a configured clear-text permission without anybody having to declare it again', async () => {
        const { presented } = renderScreen(signedIn, null, 'untilTheTabCloses', [], true);

        typeAddress('mail.example.test');
        typeCredential();
        submit();

        await vi.waitFor(() => {
            expect(presented.map((attempt) => attempt.deployment)).toEqual([
                { baseAddress: 'http://mail.example.test' },
            ]);
        });
    });
});
