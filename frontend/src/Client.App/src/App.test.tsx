// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse } from '@mailfathom/client-backend';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { startingListWidth, storeListWidth } from './mailSpace/listWidth';
import {
    accepted,
    anotherPersonsSession,
    asked,
    complete,
    deploymentAnswering,
    deploymentDrawingAConversation,
    deploymentDrawingAMessage,
    deploymentWorkingInTabs,
    directory,
    framed,
    goTo,
    heldSession,
    openingAt,
    preferencesAnswering,
    renderApp,
    resetsBetweenTests,
    routesAsked,
    servedFrom,
    sessionAnswering,
    typedSession,
    workAccount,
    type Answer,
} from './App.harness';

// The frame itself: which space it opens in, what a row of the list opens beside it, and what the workspace holds
// while somebody moves between spaces. Everything the frame composes rather than any one screen inside it.
//
// Six files stand beside this one — the session, sign-in, the deployment, language, telemetry, and the shell's
// layers — each named for the group it proves, and all seven mount the same frame out of `App.harness`.

resetsBetweenTests();

describe('App', () => {
    it('says it is reaching the deployment while nothing has answered', () => {
        renderApp(servedFrom, heldSession, () => () => new Promise<ClientResponse>(() => undefined));

        expect(screen.getByText('Reaching your deployment…')).toBeDefined();
    });

    it('says it is reading the accounts once the deployment has said what the credential may do', async () => {
        const send: DeploymentTransport = () => (request) =>
            request.path.endsWith('/session')
                ? Promise.resolve(complete(accepted))
                : new Promise<ClientResponse>(() => undefined);

        renderApp(servedFrom, heldSession, send);

        expect(await screen.findByText('Reading accounts…')).toBeDefined();
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

        expect(await screen.findByRole('main', { name: space })).toBeDefined();
    });

    it('draws the message a row of the list opened, which is what the frame wires the two together for', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('names what a person working in tabs has opened, in a strip above the mail', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByRole('tab', { name: 'Quarterly invoice' })).toBeDefined();
        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    // The section is the reader's to remove, and what removes it is a preference the deployment holds — so this is the
    // one assertion that the answer reaches the tree rather than stopping at the hook that read it.
    it('draws the standing views under the tree for a person who has not taken them out of it', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering(undefined, accepted, preferencesAnswering(true)));
        await framed();

        await goTo('Mail');

        expect(await screen.findByRole('region', { name: 'AI filters' })).toBeDefined();
    });

    it('draws no standing views at all for a person who took the section out of the tree', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering(undefined, accepted, preferencesAnswering(true, false)));
        await framed();

        await goTo('Mail');

        await screen.findByText('Folders');

        expect(screen.queryByRole('region', { name: 'AI filters' })).toBeNull();
    });

    it('says nothing is open to a person working in tabs who has opened none', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        expect(await screen.findByText('Nothing is open')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('opens mail in the reading column, and draws no strip, for somebody who has not asked for tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('divides the mail space where the person now signed in last left it, rather than where anybody did', async () => {
        // The width is stored against the person the held credential names, so this is the one assertion that the name
        // the frame takes the credential apart for is the name the store was written under. Everything below the frame
        // is handed that name and could not tell a wrong one from a right one.
        storeListWidth('test', 468);

        renderApp();
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe('468');
    });

    it('divides it where it starts for somebody else signing in on the same machine', async () => {
        storeListWidth('test', 468);

        renderApp(servedFrom, anotherPersonsSession);
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe(String(startingListWidth));
    });

    it('opens the conversation a message belongs to from the row itself, and returns to that message when it is closed', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        const conversation = await screen.findByRole('region', { name: 'Conversation' });
        expect(within(conversation).getByText('Messages in this conversation: 1')).toBeDefined();

        fireEvent.click(within(conversation).getByRole('button', { name: 'Back to the message' }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByRole('region', { name: 'Conversation' })).toBeNull();

        // Returning to the message is a navigation rather than a landing, so it places the reader in what it drew.
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('article', { name: /Quarterly invoice/ }));
        });
    });

    // What #1759 is about: a correspondence costs one read of the deployment however many messages are drawn out of
    // it. The route carries every message's description and words, so nothing under the conversation asks for either.
    it('reads a whole conversation in one request, and asks for no message of it on its own', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        await screen.findByText('A drawn message.');

        // Where the conversation stands is read beside it and is a route of its own, so it is not one of the reads
        // this counts: what #1759 settled is that the messages arrive together, not that nothing else is asked for.
        const conversations = routesAsked().filter((path) => path.includes('/threads/') && !path.endsWith('/state'));

        expect(conversations).toHaveLength(1);
        expect(conversations[0]).toContain('content=true');
        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    // The message this one opens belongs to no conversation, which is deliberate: what is being proven is the shape the
    // sender's own view is drawn in, and a threaded message would draw the way into the conversation beside it — a
    // second surface for every query here to walk past, in the heaviest test this suite has.
    it('draws the sender own markup in a window over the message where the person does not work in tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the full HTML version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the HTML' }));

        const standing = await screen.findByRole('dialog', { name: "The sender's own version of this message" });
        const surface = within(standing).getByRole('region', { name: "The sender's own version of this message" });

        // The message is where it was rather than replaced by the surface, which is the whole of what a window over it
        // means: the reading column goes on drawing what the reader was reading.
        expect(screen.getByText('A drawn message.')).toBeDefined();

        // Opening it is a view change, so the reader is placed in what was opened. Where that focus goes when the
        // window closes is the platform's own — the browser suite is where a real modal can be asked.
        await waitFor(() => {
            expect(document.activeElement).toBe(surface);
        });

        fireEvent.click(within(surface).getByRole('button', { name: 'Close this view' }));

        expect(screen.queryByRole('dialog')).toBeNull();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    it('draws the sender own markup in the reading column where the person works in tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the full HTML version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the HTML' }));

        // A tab of its own, so it takes the column the message had rather than standing in a window over it — which is
        // the mode deciding the shape, and the one difference between this test and the one above.
        expect(await screen.findByRole('region', { name: "The sender's own version of this message" })).toBeDefined();
        expect(screen.queryByRole('dialog')).toBeNull();
        expect(screen.queryByText('A drawn message.')).toBeNull();
    });

    // Three things decide whether opening a message marks it read, and all three are the frame's: the reader's own
    // setting, the grant the credential signed in under, and there being a session to submit over. Nothing below the
    // frame asks the question, so this is where each of them is proven.
    it('marks read a message a row opened, where the credential may write a flag', async () => {
        renderApp(
            servedFrom,
            heldSession,
            deploymentDrawingAMessage(
                sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.ask', 'mailfathom.mail.flags.write']),
            ),
        );
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        await waitFor(() => {
            expect(
                asked.some(
                    (request) =>
                        request.path === 'https://mail.example.invalid/api/client/mutations/flags' &&
                        request.method === 'POST',
                ),
            ).toBe(true);
        });
    });

    it('marks nothing where the credential was never granted a flag write, and says so rather than failing', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        expect(routesAsked().some((path) => path.includes('/mutations/'))).toBe(false);

        // An absence nobody explained is a client that looks broken, which is what the notice strip is for — so the
        // sentence is asserted on the screen rather than the withholding being asserted on its own.
        expect(
            screen.getByText(
                'This credential may not change a flag on your mail server, so opening a message leaves it unread there, flagging and marking unread are not offered, and this client shows what the server last reported. Whoever runs the deployment can grant that.',
            ),
        ).toBeDefined();
    });

    it('draws no message until a row of the list opens one', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');
        await screen.findByRole('listbox', { name: 'Messages' });

        expect(screen.getByText('Nothing is open')).toBeDefined();
        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    it('reads no message while the space on the screen is not Mail', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
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
        fireEvent.change(screen.getByRole('combobox', { name: 'What the question is asked about' }), {
            target: { value: 'account:work' },
        });

        await goTo('Mail');

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'the renewal Nordwind sent',
        );
        expect(screen.getByRole('combobox', { name: 'What the question is asked about' })).toHaveProperty(
            'value',
            'account:work',
        );
    });

    it('offers every mailbox the user holds as a scope, beside all of them at once', async () => {
        const twoMailboxes = directory(true, [workAccount, { ...workAccount, id: 'archive', displayName: 'Archive' }]);

        renderApp(servedFrom, heldSession, deploymentAnswering(twoMailboxes));

        const scope = await screen.findByRole('combobox', { name: 'What the question is asked about' });
        await waitFor(() => {
            expect([...scope.querySelectorAll('option')].map((option) => option.textContent)).toEqual([
                'All mailboxes',
                'Work',
                'Archive',
            ]);
        });
    });

    it('says the deployment is not refreshing these accounts when it is not, as its setting rather than a grant', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering(directory(false, [workAccount])));

        expect(
            await screen.findByText(
                'This deployment is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the deployment rather than a permission you are missing.',
            ),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of saying nothing about them', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering({ status: 403, body: '' }));

        expect(await screen.findByText('The accounts could not be read: unauthorized.')).toBeDefined();
    });

    it('reads the accounts again when the person asks it to, rather than only on a reload', async () => {
        let accounts: Answer = { status: 503, body: '' };
        const deployment: DeploymentTransport = () => (request) =>
            Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));

        renderApp(servedFrom, heldSession, deployment);
        const retry = await screen.findByRole('button', { name: 'Try again' });

        accounts = directory(true, [workAccount]);
        fireEvent.click(retry);

        expect(await screen.findByText('Every account is up to date.')).toBeDefined();
    });

    it('reads its mail with the credential it holds rather than with one written into the client', async () => {
        renderApp(servedFrom, typedSession);
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([
            typedSession.authorization,
        ]);
    });
});
