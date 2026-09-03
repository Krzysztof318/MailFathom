// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { AdvancedConnection } from './AdvancedConnection';
import type { ResolvedConnection } from './connection';

// Everything a person is owed about the connection before they type a password into the field above this disclosure.
// The values are read back out of what `Client.Backend` resolved, so what a test hands in here is a resolved
// connection rather than an address to parse — the parsing has its own tests beside the module that does it.

function drawing(
    connection: ResolvedConnection | null,
    clearTextPermitted = false,
    clearTextConfigured = false,
): { permitted: boolean[] } {
    const permitted: boolean[] = [];

    render(
        <LocalizationProvider>
            <AdvancedConnection
                clearTextConfigured={clearTextConfigured}
                clearTextPermitted={clearTextPermitted}
                connection={connection}
                onPermitClearText={(permission) => {
                    permitted.push(permission);
                }}
            />
        </LocalizationProvider>,
    );

    return { permitted };
}

const secure: ResolvedConnection = { secure: true, authority: 'mail.example.test', port: null };

describe('AdvancedConnection', () => {
    it('says what a secure connection is, in words rather than by the colour of a row', () => {
        drawing(secure);

        expect(screen.getByText('HTTPS, over TLS')).toBeDefined();
        expect(screen.getByText('Required')).toBeDefined();
    });

    // The one statement on this screen nobody may be left to infer from a hue: what a password is about to travel
    // over. A row that only turned amber would say nothing to somebody who cannot see the difference.
    it('says a clear-text connection is checked by nothing, in the same words', () => {
        drawing({ secure: false, authority: 'mail.example.test', port: null }, true);

        expect(screen.getByText('HTTP, unencrypted')).toBeDefined();
        expect(screen.getByText('None — nothing is encrypted')).toBeDefined();
    });

    it('names the host and the port the client will reach', () => {
        drawing({ secure: true, authority: 'mail.example.test:8443', port: '8443' });

        expect(screen.getByText('mail.example.test:8443')).toBeDefined();
        expect(screen.getByText('8443')).toBeDefined();
    });

    it('says which port the scheme supplies where the address named none, rather than leaving the row empty', () => {
        drawing(secure);

        expect(screen.getByText('443 (default)')).toBeDefined();
    });

    it('says nothing rather than guessing, where what was typed resolves to no address at all', () => {
        drawing(null);

        expect(screen.getAllByText('Nothing named yet').length).toBe(4);
    });

    it('keeps what turning TLS off costs out of the way until it is turned off', () => {
        drawing(secure);

        expect(screen.queryByText(/^TLS is off\./u)).toBeNull();
    });

    it('says what turning TLS off costs once it is off', () => {
        drawing(secure, true);

        expect(screen.getByText(/^TLS is off\./u)).toBeDefined();
    });

    // A closed disclosure still says the permission is on, because a screen that folded away the one setting
    // weakening the connection would be folding away exactly what a reader came to check.
    it('marks the disclosure itself once an unsecured connection is permitted', () => {
        drawing(secure, true);

        expect(screen.getByText('no TLS')).toBeDefined();
    });

    it('carries no such mark while the connection is secured', () => {
        drawing(secure);

        expect(screen.queryByText('no TLS')).toBeNull();
    });

    it('reports the permission rather than deciding anything itself', () => {
        const { permitted } = drawing(secure);

        fireEvent.click(screen.getByRole('checkbox', { name: 'Reach this deployment over plain HTTP' }));

        expect(permitted).toEqual([true]);
    });

    // A disabled control saying nothing about why is worse than a sentence that says it, so the row states the
    // decision instead of offering one that was taken by whoever installed the client.
    it('states a configured permission and offers no control for it', () => {
        drawing({ secure: false, authority: 'mail.example.test', port: null }, true, true);

        expect(screen.queryByRole('checkbox', { name: 'Reach this deployment over plain HTTP' })).toBeNull();
        expect(
            screen.getByText(
                'This was set for you when the client was installed, so it is not yours to change here. Whoever configured it decides it.',
            ),
        ).toBeDefined();
    });
});
