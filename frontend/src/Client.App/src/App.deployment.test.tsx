// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import {
    accepted,
    asked,
    chose,
    complete,
    deploymentAnswering,
    directory,
    framed,
    heldCredential,
    nothingAdopted,
    renderApp,
    resetsBetweenTests,
    routesAsked,
    servedFrom,
    servingAddress,
    signIn,
    signOut,
    storeKeeping,
    typeAddress,
    wasConfiguredWith,
    workAccount,
} from './App.harness';

// Which deployment the client reads from, how it was adopted, and what pointing it somewhere else does. The
// arrangement is `App.harness`, which the rest of this family shares.

resetsBetweenTests();

describe('App deployment', () => {
    it('reads from the deployment it was pointed at, rather than from one written into the client', async () => {
        renderApp(chose('https://elsewhere.example.invalid'));
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://elsewhere.example.invalid/api/client/session',
                'https://elsewhere.example.invalid/api/client/accounts',
                'https://elsewhere.example.invalid/api/client/preferences',
                'https://elsewhere.example.invalid/api/client/display-name',
                'https://elsewhere.example.invalid/api/client/signals/ticket',
                'https://elsewhere.example.invalid/api/client/notifications/unread-count',
            ]);
        });
    });

    // A deployment that configured this client wrongly is said out loud rather than worked around: every control on
    // the sign-in screen is about a connection this run has already been refused, so offering the form would invite a
    // password against an address the client will not use.
    it('says what a deployment configured wrongly, in place of the form, rather than asking for a password', () => {
        renderApp({ outcome: 'refused', refusal: 'clearTextContradictsAddress' }, null);

        expect(screen.getByRole('heading', { name: 'This client is configured wrongly' })).toBeDefined();
        expect(
            screen.getByText(
                'It was told to permit an unsecured connection and given an https address, which are two different answers to one question. Remove whichever of the two is wrong.',
            ),
        ).toBeDefined();
        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Connect' })).toBeNull();
    });

    // The form somebody was on their way to filling is gone, which is a view change like any other: focus left on the
    // document would tab past the one sentence saying why there is nothing to fill.
    it('puts focus at the start of the refusal, rather than leaving it where the form would have been', () => {
        renderApp({ outcome: 'refused', refusal: 'addressMalformed' }, null);

        expect(document.activeElement).toBe(
            screen.getByRole('heading', { name: 'This client is configured wrongly' }).parentElement,
        );
    });

    it('says where the two settings are read from, there being no way out of that screen from inside the client', () => {
        renderApp({ outcome: 'refused', refusal: 'addressMalformed' }, null);

        expect(
            screen.getByText(
                'Both settings are read from the arguments MailFathom was started with, from its environment, and from client.conf beside its own configuration, in that order.',
            ),
        ).toBeDefined();
    });

    // An address a deployment configured is not one changing an address could move, so the client asks for none and
    // is not offered as something to point elsewhere — which is the same reason an origin that served the client is
    // not. Where the address arrived from is stated under the disclosure instead, as the design draws it.
    it('offers no way out of a configured address, that being nobody on this machine’s to change', () => {
        renderApp(wasConfiguredWith('https://configured.example.invalid'), null);

        expect(screen.queryByRole('textbox', { name: 'Server' })).toBeNull();
        expect(screen.getAllByText('configured.example.invalid').length).toBeGreaterThan(0);
        expect(screen.queryByRole('button', { name: 'Change the server', hidden: true })).toBeNull();
    });

    it('asks for an address when nothing has said where the deployment is', () => {
        renderApp(nothingAdopted, null);

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
        expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
    });

    it('places focus on the address where that is the first thing it is asking for', () => {
        renderApp(nothingAdopted, null);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Server' }));
    });

    it('signs in against the address somebody named, in the same act as naming it', async () => {
        renderApp(nothingAdopted, null);

        typeAddress('mail.example.test');
        signIn();
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://mail.example.test/api/client/session',
                'https://mail.example.test/api/client/accounts',
                'https://mail.example.test/api/client/preferences',
                'https://mail.example.test/api/client/display-name',
                'https://mail.example.test/api/client/signals/ticket',
                'https://mail.example.test/api/client/notifications/unread-count',
            ]);
        });
    });

    // The menu inside the frame does not carry this and the design project draws it nowhere there. Pointing the client
    // elsewhere ends the session anyway, so the screen that offers it is the one signing out lands on.
    it('offers to be pointed elsewhere once the session ends, where somebody named the deployment themselves', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        expect(screen.queryByRole('button', { name: 'Change the server', hidden: true })).toBeNull();

        await signOut();

        expect(screen.getByRole('button', { name: 'Change the server', hidden: true })).toBeDefined();
    });

    it('offers a way out of the sign-in screen a chosen deployment left behind', () => {
        renderApp(chose('https://mail.example.invalid'), null);

        // A chosen address renders no address field, and it is read back out of storage on every later start — so
        // without this, somebody whose password no longer works has no way to point the client anywhere else.
        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
    });

    it('offers nothing to change on the sign-in screen the origin that served the client left', () => {
        renderApp(servedFrom, null);

        expect(screen.queryByRole('button', { name: 'Change the server', hidden: true })).toBeNull();
    });

    it('offers nothing to change where the origin that served the client is the deployment', async () => {
        renderApp();
        await framed();

        expect(screen.queryByRole('button', { name: 'Change the server', hidden: true })).toBeNull();
    });

    it('asks for an address again, and shows no space, once it is pointed somewhere else', async () => {
        renderApp(chose('https://mail.example.invalid'));
        await framed();

        await signOut();
        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));

        expect(screen.getByRole('textbox', { name: 'Server' })).toBeDefined();
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
        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));
        answering = true;
        for (const answer of held) {
            answer();
        }

        expect(await screen.findByRole('textbox', { name: 'Server' })).toBeDefined();
        await waitFor(() => {
            expect(screen.queryByRole('navigation', { name: 'Spaces' })).toBeNull();
        });
        // Waited for rather than read once the screen above has settled: what the store was asked to do is a
        // promise the frame started, and the commit that put that screen up is not the one it resolves in.
        await waitFor(() => {
            expect([...credentials.kept]).toEqual([]);
        });
    });

    it('forgets the credential of the deployment it is pointed away from', async () => {
        const credentials = storeKeeping();
        const chosen = chose('https://mail.example.invalid');
        const pointedAt = (chosen.outcome === 'resolved' ? chosen.adopted?.deployment : null) ?? servingAddress;

        // Seeded against the address this test itself chose rather than against the serving fixture that happens to
        // spell the same one: the two are different origins, and a test that passed on the coincidence would stop
        // proving what its name says the moment either literal moved.
        await credentials.keep(pointedAt, heldCredential);

        // Read on the sign-in screen rather than inside the frame, which is where the control now is — and where the
        // assertion is about pointing elsewhere alone, signing out having its own reason to clear the same store.
        renderApp(chosen, null, deploymentAnswering(), credentials);

        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));

        // Waited for rather than read once the screen above has settled: what the store was asked to do is a
        // promise the frame started, and the commit that put that screen up is not the one it resolves in.
        await waitFor(() => {
            expect([...credentials.kept]).toEqual([]);
        });
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

        await signOut();
        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Server' }));
    });

    it('starts an empty workspace when somebody signs in, rather than handing them the last person’s', async () => {
        // Written before the provider mounts, which is how a tab that was not signed out keeps what was on the screen:
        // a second person signing into the same tab must not find the first one's question, mailbox, or reading
        // position waiting for them.
        window.sessionStorage.setItem(
            'mailfathom.workspace',
            JSON.stringify({
                scope: { kind: 'account', accountId: 'work' },
                collapsed: [],
                selection: 'AAMkAD-42',
                selected: ['AAMkAD-42'],
                question: 'what did the last person ask',
            }),
        );

        renderApp(servedFrom, null);
        signIn();
        await framed();

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
        expect(screen.getByRole('combobox', { name: 'Mailbox in scope' })).toHaveProperty('value', '');
    });

    it('signs in against the next deployment of its own, and never reads the one before it again', async () => {
        renderApp(chose('https://first.example.invalid'));
        await framed();

        await signOut();
        fireEvent.click(screen.getByRole('button', { name: 'Change the server', hidden: true }));
        typeAddress('second.example.invalid');
        signIn();
        await framed();

        await waitFor(() => {
            expect(routesAsked()).toEqual([
                'https://first.example.invalid/api/client/session',
                'https://first.example.invalid/api/client/accounts',
                'https://first.example.invalid/api/client/preferences',
                'https://first.example.invalid/api/client/display-name',
                'https://first.example.invalid/api/client/signals/ticket',
                'https://first.example.invalid/api/client/notifications/unread-count',
                'https://second.example.invalid/api/client/session',
                'https://second.example.invalid/api/client/accounts',
                'https://second.example.invalid/api/client/preferences',
                'https://second.example.invalid/api/client/display-name',
                'https://second.example.invalid/api/client/signals/ticket',
                'https://second.example.invalid/api/client/notifications/unread-count',
            ]);
        });
    });
});
