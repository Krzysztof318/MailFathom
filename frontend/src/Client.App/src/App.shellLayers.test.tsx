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

    // The reading column goes with them, and for a second reason as well: what stands in it is a step the back gesture
    // has to unwind, so a message left open under another space is a press spent on something nobody can see.
    it('takes the message being read off the screen when the navigation goes to another destination', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();
        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        expect(await screen.findByText('A drawn message.')).toBeDefined();

        await goTo('Discover');
        await goTo('Mail');

        expect(screen.queryByText('A drawn message.')).toBeNull();
    });
});
