// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    deploymentDrawingAMessage,
    framed,
    goTo,
    heldSession,
    inOnePane,
    openSettings,
    renderApp,
    resetsBetweenTests,
    servedFrom,
} from './App.harness';

// The shell composes two things no surface can compose for itself: which of them the back gesture reaches, and what
// leaving for another destination does to all of them. Both are asserted here rather than beside a surface, because
// what they are about is the arrangement rather than any one surface in it. That arrangement is `App.harness`, which
// the rest of this family shares.

resetsBetweenTests();

describe('App shell layers', () => {
    it('closes what stands over the screen before the back gesture leaves it, one layer to a press', async () => {
        renderApp();
        await framed();
        openSettings();

        expect(screen.getByRole('dialog', { name: 'Settings' })).toBeDefined();

        act(() => {
            window.history.back();
        });

        await waitFor(() => {
            expect(screen.queryByRole('dialog', { name: 'Settings' })).toBeNull();
        });
    });

    it('leaves nothing standing over the screen when the navigation goes to another destination', async () => {
        renderApp();
        await framed();
        openSettings();

        await goTo('Mail');

        expect(screen.queryByRole('dialog', { name: 'Settings' })).toBeNull();
    });

    // The reading column does not go with them. The Mail space is stood aside rather than taken down, so the message
    // standing beside the list outlives leaving it exactly as the list's own pages and scroll offset do — and what the
    // back gesture counts asks which space is in front rather than being answered by an empty workspace.
    it('leaves the message being read open when the navigation goes to another destination and comes back', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();
        await goTo('Mail');
        await openTheInvoice();

        await goTo('Discover');
        await goTo('Mail');

        expect(screen.getByText('A drawn message.')).toBeDefined();
        expect(theInvoiceRow().getAttribute('aria-current')).toBe('true');
    });

    it('leaves the message in front of the list in the single-pane composition, still with its way back', async () => {
        inOnePane();
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();
        await goTo('Mail');
        await openTheInvoice();

        await goTo('Discover');
        await goTo('Mail');

        expect(screen.getByText('A drawn message.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Back to the list' }));

        expect(await screen.findByRole('listbox', { name: 'Messages' })).toBeDefined();
    });

    // The message counts as a step only while Mail is in front of the screen. Counted from another space, the press
    // that should have left that space would be spent closing a message nobody can see instead.
    it('leaves the space a press was made on rather than closing the message standing on another one', async () => {
        inOnePane();
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();
        await goTo('Mail');
        await openTheInvoice();

        await goTo('Discover');

        act(() => {
            window.history.back();
        });

        expect(await screen.findByRole('main', { name: 'Mail' })).toBeDefined();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });
});

// The one message the deployment draws, opened the way a reader opens it: a row picked out of the list.
async function openTheInvoice(): Promise<void> {
    fireEvent.pointerDown(await screen.findByRole('option', { name: /Quarterly invoice/ }));

    await screen.findByText('A drawn message.');
}

function theInvoiceRow(): HTMLElement {
    return within(screen.getByRole('listbox', { name: 'Messages' })).getByRole('option', {
        name: /Quarterly invoice/,
    });
}
