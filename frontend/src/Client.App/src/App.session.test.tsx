// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { mostReconnectionAttempts } from './shell/useConnection';
import {
    asked,
    deploymentAnswering,
    deploymentRefusing,
    directory,
    framed,
    heldCredential,
    nothingAdopted,
    openingAt,
    renderApp,
    resetsBetweenTests,
    routesAsked,
    servedFrom,
    sessionAnswering,
    signIn,
    signOut,
    storeKeeping,
    workAccount,
} from './App.harness';

// What the grant the credential signed in under does to the frame, and what a deployment that does not answer does
// to it. The arrangement is `App.harness`, which the rest of this family shares.

resetsBetweenTests();

describe('App session', () => {
    /** A deployment that grants the credential exactly these names, and answers the accounts normally. */
    function granting(...permissions: readonly string[]): DeploymentTransport {
        return deploymentAnswering(directory(true, [workAccount]), sessionAnswering(permissions));
    }

    it('offers only the spaces the grant permits, rather than ones the deployment would refuse', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(screen.getAllByRole('link').map((space) => space.textContent)).toEqual([
            'Mail',
            'Cases',
            'Tasks',
            'Calendar',
            'People',
        ]);
    });

    it('says what the credential may not do, so an absence is not read as a client that is broken', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(
            screen.getByText(
                'This credential may not ask questions of your mail on this deployment, so asking is not offered. Whoever runs the deployment can grant that.',
            ),
        ).toBeDefined();
    });

    it('offers nothing to ask with where the credential may not ask', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));
        await framed();

        expect(screen.queryByRole('searchbox', { name: 'Ask your mail' })).toBeNull();
    });

    it('never asks for the mail a credential may not read, rather than letting the read be refused', async () => {
        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.ask'));
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        await waitFor(() => {
            expect(routesAsked()).toEqual(['https://mail.example.invalid/api/client/session']);
        });
        expect(screen.queryByText(/The accounts could not be read/)).toBeNull();
    });

    it('hands the keyboard back to what asked for a message once the message is closed', async () => {
        renderApp(
            servedFrom,
            heldCredential,
            granting('mailfathom.mail.read', 'mailfathom.mail.drafts.write', 'mailfathom.mail.send'),
        );
        await framed();

        // The toolbar's, rather than the one the empty pane offers under the same name: both are on the screen.
        const toolbar = screen.getByRole('toolbar', { name: 'Mail actions' });
        const asks = within(toolbar).getByRole('button', { name: 'New message' });

        asks.focus();
        fireEvent.click(asks);

        await screen.findByLabelText('Message');

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(within(toolbar).getByRole('button', { name: 'New message' }));
        });
    });

    it('answers an address naming a space this credential may not open with one it may', async () => {
        openingAt('#/discover');

        renderApp(servedFrom, heldCredential, granting('mailfathom.mail.read'));

        expect(await screen.findByRole('main', { name: 'Mail' })).toBeDefined();
        await waitFor(() => {
            expect(window.location.hash).toBe('#/mail');
        });
    });

    it('tells a credential granted nothing everything it may not do, rather than leaving it to guess', async () => {
        renderApp(servedFrom, heldCredential, granting());

        expect(await screen.findByText(/This credential may not read mail on this deployment/)).toBeDefined();
        expect(screen.getByText(/This credential may not ask questions of your mail/)).toBeDefined();
        expect(screen.getByText(/This credential may not change a flag on your mail server/)).toBeDefined();
        expect(screen.getByText(/This credential may not file mail in another folder/)).toBeDefined();
        expect(screen.queryByRole('link', { name: 'Mail' })).toBeNull();
    });

    it('reaches for a deployment that did not answer on its own, and says which attempt it is on', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });

        renderApp(servedFrom, heldCredential, deploymentRefusing({ status: 503, body: '' }));
        await screen.findByText(/Trying again — attempt 1 of/);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(10_000);
        });

        expect(screen.getByText(/Trying again — attempt 2 of/)).toBeDefined();
    });

    it('stops reaching once the budget is spent, and hands the way out to the person', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });

        renderApp(servedFrom, heldCredential, deploymentRefusing({ status: 503, body: '' }));
        await screen.findByText(/Trying again — attempt 1 of/);

        // One pass per wait in the budget, because each attempt is only scheduled once the one before it has answered:
        // a single long advance would find no timer to fire past the first.
        for (let attempt = 0; attempt < mostReconnectionAttempts; attempt += 1) {
            await act(async () => {
                await vi.advanceTimersByTimeAsync(60_000);
            });
        }

        expect(await screen.findByText(/Your deployment has not answered after/)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('asks a deployment for nothing while this machine has no network, and says that rather than blaming it', () => {
        vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);

        renderApp();

        expect(
            screen.getByText('This machine is offline. The client reconnects on its own when the network comes back.'),
        ).toBeDefined();
        expect(asked).toEqual([]);
    });

    it('reads again on its own when the network comes back, without anybody restarting the client', async () => {
        const connected = vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);

        renderApp();
        await screen.findByText(/This machine is offline\./);

        connected.mockReturnValue(true);
        fireEvent(window, new Event('online'));

        await framed();
    });

    it('offers the next person nothing of the last one until their own grant has been read', async () => {
        renderApp(servedFrom, null, deploymentAnswering(), storeKeeping());
        signIn();
        await framed();
        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();

        // No network from here on, so nothing can arrive to replace what is on the screen: what has to clear it is the
        // credential changing rather than an answer about the new one.
        vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(false);
        fireEvent(window, new Event('offline'));

        await signOut();
        signIn('somebody', 'else');
        await screen.findByText(/This machine is offline\./);

        expect(within(screen.getByRole('navigation', { name: 'Spaces' })).queryAllByRole('link')).toEqual([]);
    });

    it('reports the deployment it is reading from beside the client it is running, on the settings screen', async () => {
        renderApp();
        await framed();
        fireEvent.click(screen.getByRole('button', { name: 'Settings', hidden: true }));

        expect(screen.getByText(`MailFathom v${__MAILFATHOM_VERSION__} · deployment 0.8.7`)).toBeDefined();
    });

    it('names the client it is running on the sign-in screen, no deployment having answered yet', () => {
        renderApp(nothingAdopted, null);

        expect(screen.getByText(`MailFathom v${__MAILFATHOM_VERSION__}`)).toBeDefined();
    });

    it('names each account and what its last attempt did, behind the line that summarizes them', async () => {
        const failing = { ...workAccount, id: 'news', displayName: 'Newsletters', synchronizationState: 'Unreachable' };

        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(true, [workAccount, failing])));

        // The one gesture the design asks for: the reading is closed when the frame is drawn, and this is what a
        // person does to it.
        fireEvent.click(await screen.findByText('Some accounts stopped synchronizing.'));

        // Scoped to the panel, because the mailbox in scope offers the same names and this is about the freshness
        // reading rather than about the field beside it.
        const panel = within(screen.getByRole('group'));
        expect(screen.getByRole('group')).toHaveProperty('open', true);
        expect(panel.getByText('Work')).toBeDefined();
        expect(panel.getByText('Up to date')).toBeDefined();
        expect(panel.getByText('Newsletters')).toBeDefined();
        expect(panel.getByText('The mail server did not answer')).toBeDefined();
    });

    it('tells a user holding no account what would fill it, rather than showing a failure', async () => {
        renderApp(servedFrom, heldCredential, deploymentAnswering(directory(true, [])));

        expect(await screen.findByText(/No mail account is configured for this user yet\./)).toBeDefined();
        expect(
            screen.getByText(/Whoever runs this deployment declares which mailboxes it reads for you/),
        ).toBeDefined();
    });
});
