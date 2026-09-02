// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { ResolvedConnection } from './connection';
import { ConnectionDetails } from './ConnectionDetails';

function drawing(connection: ResolvedConnection | null): void {
    render(
        <LocalizationProvider>
            <ConnectionDetails connection={connection} />
        </LocalizationProvider>,
    );
}

describe('ConnectionDetails', () => {
    it('says an encrypted connection is encrypted, in words rather than by the colour of a row', () => {
        drawing({ secure: true, authority: 'mail.example.test', port: null });

        expect(screen.getByText('HTTPS, over TLS')).toBeDefined();
        expect(screen.getByText('In force')).toBeDefined();
    });

    // The one statement on this screen nobody may be left to infer from a hue: what a password is about to travel
    // over. A row that only turned amber would say nothing to somebody who cannot see the difference.
    it('says a clear-text connection carries no encryption, in the same words', () => {
        drawing({ secure: false, authority: 'mail.example.test', port: null });

        expect(screen.getByText('HTTP, unencrypted')).toBeDefined();
        expect(screen.getByText('None')).toBeDefined();
    });

    it('names the host the client will reach', () => {
        drawing({ secure: true, authority: 'mail.example.test:8443', port: '8443' });

        expect(screen.getByText('mail.example.test:8443')).toBeDefined();
        expect(screen.getByText('8443')).toBeDefined();
    });

    it('says which port the scheme supplies where the address named none, rather than leaving the row empty', () => {
        drawing({ secure: true, authority: 'mail.example.test', port: null });

        expect(screen.getByText('443 (default)')).toBeDefined();
    });

    it('says nothing rather than guessing, where what was typed resolves to no address at all', () => {
        drawing(null);

        expect(screen.getAllByText('Nothing named yet').length).toBe(4);
    });
});
