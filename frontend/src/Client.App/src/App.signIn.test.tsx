// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { sessionExchangeRoute } from '@mailfathom/client-backend';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import {
    accepted,
    asked,
    challenged,
    complete,
    deploymentAnswering,
    deploymentRefusing,
    directory,
    framed,
    mintedCredential,
    mintedKeptSession,
    mintedSession,
    renderApp,
    resetsBetweenTests,
    servedFrom,
    servingAddress,
    signIn,
    signOut,
    storeKeeping,
    storeRefusingToForget,
    storeRefusingToKeep,
    storeRefusingToKeepGrant,
    typedCredential,
    typedSession,
    workAccount,
    type Answer,
} from './App.harness';
import { writeKeptSession } from './signIn/keptSession';
import { startOAuthSignIn } from './signIn/oauthFlow';
import { grantRenewalMargin, type OAuthGrant } from './signIn/oauthGrant';
import { receivesNoRedirect, type SignInRedirectAnswer } from './shellOperations/signInRedirect';
import { noTelemetry } from './telemetry/clientTelemetry';

/** A grant an authorization server issued, which is the other thing a start may already be holding. */
const issuedGrant: OAuthGrant = {
    authorization: 'Bearer mfo_appsignin.YXBwLXNpZ24taW4tcHJvb2Y',
    expiresAt: '2126-08-31T21:41:00.000Z',
    refreshToken: 'a-refresh-token',
    issuer: 'https://id.example.invalid',
    clientId: 'mailfathom-client',
    resource: `${servingAddress.baseAddress}/api/client`,
    person: 'K. Kowalska',
};

/**
 * The deployment answering as it always does, beside the authorization server publishing where a token is withdrawn.
 *
 * The harness's own double answers every path with the accounts directory, which is enough for a test asserting that a
 * discovery document was asked for and not enough for one asserting what was done with it.
 */
function alsoAnsweringTheProvider(): DeploymentTransport {
    const deployment = deploymentAnswering();

    return (signal) => (request) => {
        if (!request.path.startsWith(issuedGrant.issuer)) {
            return deployment(signal)(request);
        }

        asked.push(request);

        return Promise.resolve(
            complete({
                status: 200,
                body: JSON.stringify({
                    issuer: issuedGrant.issuer,
                    authorization_endpoint: `${issuedGrant.issuer}/authorize`,
                    token_endpoint: `${issuedGrant.issuer}/token`,
                    revocation_endpoint: `${issuedGrant.issuer}/revoke`,
                }),
            }),
        );
    };
}

/**
 * The deployment and the authorization server together, as the run that comes back from a provider meets them.
 *
 * It answers the four things a redemption asks for — the server's discovery document, its token endpoint, who the
 * deployment says the token belongs to, and everything the frame reads afterwards.
 */
function answeringTheWholeGrant(): DeploymentTransport {
    const deployment = deploymentAnswering();

    return (signal) => (request) => {
        if (request.path.startsWith(issuedGrant.issuer)) {
            asked.push(request);

            return Promise.resolve(
                complete({
                    status: 200,
                    body: request.path.endsWith('/token')
                        ? JSON.stringify({
                              access_token: 'mfo_returned.cmV0dXJuZWQtcHJvb2Y',
                              token_type: 'Bearer',
                              expires_in: 3600,
                          })
                        : JSON.stringify({
                              issuer: issuedGrant.issuer,
                              authorization_endpoint: `${issuedGrant.issuer}/authorize`,
                              token_endpoint: `${issuedGrant.issuer}/token`,
                          }),
                }),
            );
        }

        if (request.path.endsWith('/api/client/display-name')) {
            asked.push(request);

            return Promise.resolve(
                complete({ status: 200, body: JSON.stringify({ displayName: 'K. Kowalska', changeable: false }) }),
            );
        }

        return deployment(signal)(request);
    };
}

/**
 * An attempt written down by the run the authorization server's page replaced, which is what the return redeems.
 *
 * @returns The `state` that run generated, which is what the answer coming back has to carry.
 */
async function attemptWaitingForItsAnswer(): Promise<string> {
    window.sessionStorage.clear();

    const handed: string[] = [];

    void startOAuthSignIn({
        deployment: servingAddress,
        server: {
            name: 'keycloak',
            displayName: 'Keycloak',
            issuer: issuedGrant.issuer,
            clientId: 'mailfathom-client',
        },
        resource: { resource: issuedGrant.resource, scopes: ['mailfathom.read'] },
        redirect: {
            offered: true,
            redirectUri: `${servingAddress.baseAddress}/`,
            hand: (address) => {
                handed.push(address);

                // Nothing settles, which is what the document being replaced looks like from here.
                return new Promise<SignInRedirectAnswer | null>(() => undefined);
            },
            abandon: () => undefined,
            answerWaiting: () => null,
        },
        transport: answeringTheWholeGrant()(new AbortController().signal),
    });

    await waitFor(() => {
        expect(handed).toHaveLength(1);
    });

    return new URL(handed[0] ?? '').searchParams.get('state') ?? '';
}

// Signing in, signing out, and everything the credential store does or refuses to do along the way. The arrangement
// is `App.harness`, which the rest of this family shares.

resetsBetweenTests();

describe('App sign-in', () => {
    it('asks for a login and a password when nothing has been signed in with', () => {
        renderApp(servedFrom, null);

        expect(screen.getByRole('textbox', { name: 'Login' })).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('asks for nothing but the credential where the origin that served the client is the deployment', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
    });

    it('opens the frame once the credential somebody typed has been accepted', async () => {
        renderApp(servedFrom, null);

        signIn();

        await framed();
        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('presents the credential it composed once, and the session it was given for it on everything after', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        // Counted rather than collapsed into a set: a client that presented the password again on every later request
        // would produce the same two distinct values, so the set would be green for the one property this whole change
        // exists for. The password reaches the exchange, once, and every request after it carries the token.
        const presentingTheCredential = asked.filter((request) => request.headers['Authorization'] === typedCredential);

        expect(presentingTheCredential.length).toBe(1);
        expect(presentingTheCredential[0]?.path).toBe(`${servingAddress.baseAddress}/api/client/session/token`);
        expect(asked.filter((request) => request.headers['Authorization'] === mintedCredential).length).toBeGreaterThan(
            0,
        );
        expect(
            asked.filter(
                (request) =>
                    request.headers['Authorization'] !== typedCredential &&
                    request.headers['Authorization'] !== mintedCredential &&
                    request.headers['Authorization'] !== undefined,
            ),
        ).toEqual([]);
    });

    it('keeps the session it was given rather than the credential it typed, so a later start opens already signed in', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        // Waited for rather than read once the screen above has settled: what the store was asked to do is a
        // promise the frame started, and the commit that put that screen up is not the one it resolves in.
        await waitFor(() => {
            expect([...credentials.kept]).toEqual([[servingAddress.baseAddress, writeKeptSession(mintedKeptSession)]]);
        });
    });

    it('offers to keep the sign-in on the device before anybody has typed a password', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('inTheDeviceStore'));

        expect(screen.getByRole('checkbox', { name: 'Keep me signed in' })).toBeDefined();
        expect(
            screen.getByText(
                'This sign-in is kept until you close this tab. Applies to password sign-in only — provider sessions follow their own rules.',
            ),
        ).toBeDefined();
    });

    // A choice between one place and the same place is not a choice, so the screen states what will happen instead of
    // offering something to tick. This is the head ADR 0027 wrote that answer for.
    it('offers nothing to tick and says why it will ask again where nothing may be kept beyond the run', () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping('nowhereTheShellKeepsTheRun'));

        expect(screen.queryByRole('checkbox', { name: 'Keep me signed in' })).toBeNull();
        expect(
            screen.getByText(
                'Your password is not stored anywhere. This sign-in is kept until you close MailFathom, and you will be asked for your password again — this machine offers no keychain to keep it in safely.',
            ),
        ).toBeDefined();
    });

    it('reports a refused credential as one thing rather than as a guess about which half was wrong', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn();

        expect(await screen.findByText('The login or the password is not accepted by this deployment.')).toBeDefined();
    });

    it('tells somebody whose deployment offers no passwords that, rather than refusing their credential', async () => {
        const bearerOnly = { status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } };

        renderApp(servedFrom, null, deploymentRefusing(bearerOnly));
        signIn();

        expect(
            await screen.findByText(
                'This deployment does not accept a login and a password. Whoever runs it has to enable that before you can sign in here.',
            ),
        ).toBeDefined();
    });

    it('names neither the password nor the value composed from it when the deployment refuses it', async () => {
        const refusing = deploymentRefusing({ status: 401, body: '', headers: { 'www-authenticate': challenged } });

        renderApp(servedFrom, null, refusing);
        signIn('user', 'open sesame');
        await screen.findByRole('alert');

        expect(document.body.textContent).not.toContain('open sesame');
        expect(document.body.textContent).not.toContain('dXNlcjpvcGVuIHNlc2FtZQ==');
    });

    it('puts somebody in front of the sign-in when the deployment stops accepting what was kept', async () => {
        const credentials = storeKeeping();
        await credentials.keep(servingAddress, writeKeptSession(typedSession));

        renderApp(servedFrom, typedSession, deploymentAnswering({ status: 401, body: '' }), credentials);

        expect(
            await screen.findByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.'),
        ).toBeDefined();
        // Waited for rather than read once the screen above has settled: what the store was asked to do is a
        // promise the frame started, and the commit that put that screen up is not the one it resolves in.
        await waitFor(() => {
            expect([...credentials.kept]).toEqual([]);
        });
    });

    it('clears what the person carried between the spaces when the deployment stops accepting what was kept', async () => {
        // One deployment whose answer to the accounts changes under the client, which is what a service restarted with
        // a different password looks like from here: the read that was working starts refusing.
        let accounts: Answer = { status: 503, body: '' };
        const send: DeploymentTransport = () => (request) => {
            asked.push(request);

            if (request.path.endsWith(sessionExchangeRoute)) {
                return Promise.resolve(complete(mintedSession(request)));
            }

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
        await screen.findByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.');

        // Being turned away mid-session returns this person to the sign-in exactly as signing out does, so what they
        // were carrying goes with the credential there too rather than waiting for the next person to read.
        accounts = directory(true, [workAccount]);
        signIn();
        await framed();

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
    });

    it('says the sign-in is still on the machine when the deployment stops accepting it and the store will not', async () => {
        renderApp(servedFrom, typedSession, deploymentAnswering({ status: 401, body: '' }), storeRefusingToForget());

        // Two things went wrong at once and both are the person's to act on: the deployment no longer accepts what was
        // kept, and the store would not give it up — so it is read back on every later start until they remove it.
        expect(
            await screen.findByText('This deployment has stopped accepting the sign-in that was kept. Sign in again.'),
        ).toBeDefined();
        expect(
            await screen.findByText(
                'Signing out did not remove the sign-in from this machine’s credential store, so it is still kept there. MailFathom was asked to end the session, and it stops working on its own in any case. Remove the entry in the store itself if you would rather it were gone now.',
            ),
        ).toBeDefined();
    });

    it('clears the credential and everything read with it when somebody signs out', async () => {
        const credentials = storeKeeping();

        renderApp(servedFrom, null, deploymentAnswering(), credentials);
        signIn();
        await framed();

        await signOut();

        // Signing out asks the store to forget before the form comes back, so the form is waited for rather than read
        // in the commit the press produced.
        expect(await screen.findByRole('textbox', { name: 'Login' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        // Waited for rather than read once the screen above has settled: what the store was asked to do is a
        // promise the frame started, and the commit that put that screen up is not the one it resolves in.
        await waitFor(() => {
            expect([...credentials.kept]).toEqual([]);
        });
    });

    it('clears what the person carried between the spaces when somebody signs out', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping());
        signIn();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });
        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'account:work' },
        });

        await signOut();
        signIn();
        await framed();

        // The next person to sign in on this machine reads their own empty screen rather than the last one's question
        // and the mailbox it was scoped to.
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty(
            'value',
            'role:Inbox',
        );
    });

    it('says the password was not kept when the store would not write it, without refusing the sign-in', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToKeep());
        signIn();
        await framed();

        // The screen promised how long the password would last before anybody typed, so a store that refused the write
        // says so — inside the frame, because signing in worked and only the keeping did not.
        expect(
            screen.getByText(
                'This sign-in could not be stored on this machine, so you will be asked for your password again the next time you open MailFathom. You are signed in either way.',
            ),
        ).toBeDefined();
    });

    it('says the sign-in is still on the machine when the store would not remove it', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeRefusingToForget());
        signIn();
        await framed();

        await signOut();

        // Signing out told them the password would be removed, so a store that refused has to say so rather than let
        // the next start read it back while they believe it is gone.
        expect(
            await screen.findByText(
                'Signing out did not remove the sign-in from this machine’s credential store, so it is still kept there. MailFathom was asked to end the session, and it stops working on its own in any case. Remove the entry in the store itself if you would rather it were gone now.',
            ),
        ).toBeDefined();
    });

    it('asks the deployment with a grant whose token is still good', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping(), noTelemetry, issuedGrant);

        await waitFor(() => {
            expect(asked.map((request) => request.headers['Authorization'])).toContain(issuedGrant.authorization);
        });
    });

    // A start against a grant last used yesterday holds an expired access token beside a refresh token that still
    // works. Presenting the expired one draws an `unauthenticated` from the deployment, which this frame acts on by
    // clearing the sign-in — the refresh token with it — at exactly the moment the renewal was about to replace it.
    it('renews an access token that has run out rather than presenting it', async () => {
        const expired: OAuthGrant = { ...issuedGrant, expiresAt: '2000-01-01T00:00:00.000Z' };

        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping(), noTelemetry, expired);

        // The renewal starts on the frame's first effect, which is what says the token was read rather than ignored.
        await waitFor(() => {
            expect(asked.map((request) => request.path)).toContain(
                `${expired.issuer}/.well-known/oauth-authorization-server`,
            );
        });

        expect(asked.map((request) => request.headers['Authorization'])).not.toContain(expired.authorization);

        // Nobody was signed out, so what is on the screen is the frame waiting rather than the sign-in.
        expect(screen.queryByLabelText('Password')).toBeNull();
    });

    // The other side of that, and the five minutes between them is what separates the two: a token inside the renewal
    // margin is one the deployment still accepts, so withholding it would empty the frame over a credential that works
    // — for as long as the renewal takes, which is indefinitely where the authorization server cannot be reached.
    it('presents an access token the renewal is due for but which has not run out', async () => {
        const nearly: OAuthGrant = {
            ...issuedGrant,
            expiresAt: new Date(Date.now() + grantRenewalMargin / 2).toISOString(),
        };

        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping(), noTelemetry, nearly);

        await framed();

        expect(asked.map((request) => request.headers['Authorization'])).toContain(nearly.authorization);
    });

    // A grant the server issued no refresh token for ends with its access token, and nothing in this client can
    // replace it. Waiting on a read that expired token would never be made with is a frame nobody can leave.
    it('puts somebody back on the sign-in screen where a grant that is due has no refresh token to renew with', async () => {
        const spent: OAuthGrant = { ...issuedGrant, expiresAt: '2000-01-01T00:00:00.000Z', refreshToken: null };

        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping(), noTelemetry, spent);

        expect(
            await screen.findByText(
                'Your provider ended this sign-in, so it has been cleared from this machine. Sign in again to carry on.',
            ),
        ).toBeDefined();
        expect(screen.getByLabelText('Password')).toBeDefined();
        expect(asked.map((request) => request.headers['Authorization'])).not.toContain(spent.authorization);
    });

    // A refresh token nobody withdrew outlives the sign-out at the server, which is a credential surviving the act
    // that was supposed to end it.
    it('asks the authorization server to withdraw the refresh token when somebody signs out', async () => {
        renderApp(servedFrom, null, alsoAnsweringTheProvider(), storeKeeping(), noTelemetry, issuedGrant);

        await framed();
        await signOut();

        // Asked and not waited on, so the request lands after the screen the sign-out produced rather than in it.
        await waitFor(() => {
            expect(asked.map((request) => request.path)).toContain(`${issuedGrant.issuer}/revoke`);
        });

        const withdrawal = asked.find((request) => request.path === `${issuedGrant.issuer}/revoke`);

        expect(withdrawal?.body).toContain(`token=${issuedGrant.refreshToken ?? ''}`);
        expect(withdrawal?.body).toContain('token_type_hint=refresh_token');
    });

    // Nobody typed a password on this path, and a deployment taking none offers no way back in through one — so the
    // sentence the password flow uses would name a control the next start may not draw.
    it('says a grant that could not be kept sends somebody back to their provider, not to a password', async () => {
        const state = await attemptWaitingForItsAnswer();

        renderApp(
            servedFrom,
            null,
            answeringTheWholeGrant(),
            storeRefusingToKeepGrant(),
            noTelemetry,
            null,
            receivesNoRedirect,
            { answered: 'code', code: 'a-code', state },
        );

        expect(
            await screen.findByText(
                'This sign-in could not be stored on this machine, so you will be sent to your provider again the next time you open MailFathom. You are signed in either way.',
            ),
        ).toBeDefined();
    });

    it('places focus on what it has to say about the credential, rather than on the field below it', async () => {
        renderApp(servedFrom, typedSession, deploymentAnswering({ status: 401, body: '' }), storeKeeping());

        const notice = await screen.findByText(
            'This deployment has stopped accepting the sign-in that was kept. Sign in again.',
        );

        // Each of these sentences is inserted in the same commit as its own text, which a live region does not
        // announce — so somebody signed out mid-session would otherwise land in the form with nothing read to them.
        //
        // Waited for rather than read once the sentence is on the screen: placing focus is an effect, and an effect
        // runs after the commit that inserted the text this awaited. The two are the same commit and not the same
        // moment, and asserting on the earlier one is how this passes on an idle machine and fails on a busy one.
        await waitFor(() => {
            expect(document.activeElement).toBe(notice.parentElement);
        });
    });

    it('places focus on the credential where the deployment is already known', () => {
        renderApp(servedFrom, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Login' }));
    });

    it('puts focus at the start of the workspace once somebody has signed in, rather than leaving it behind', async () => {
        renderApp(servedFrom, null);

        signIn();
        await framed();

        // Waited for rather than read once the frame is on the screen: placing focus is an effect, and the summary
        // `framed` waits for is a commit the workspace is already mounted by — so the two are the same screen and not
        // the same moment, and reading the earlier one leaves focus wherever the sign-in form dropped it.
        await waitFor(() => {
            expect(document.activeElement?.contains(screen.getByRole('main'))).toBe(true);
        });
    });
});
