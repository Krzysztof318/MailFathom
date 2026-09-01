// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailAuthorAuthentication, MailDeploymentTrust } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { SenderVerdict } from './SenderVerdict';

function drawing(
    authorAuthentication: MailAuthorAuthentication,
    deploymentTrust: MailDeploymentTrust,
    authenticatedDomain: string | null = null,
): void {
    render(
        <LocalizationProvider>
            <SenderVerdict verdict={{ authorAuthentication, deploymentTrust, authenticatedDomain }} />
        </LocalizationProvider>,
    );
}

const failed =
    'A receiving mail server checked who actually sent this message and reported that the author it displays did not hold.';

const recognized = 'This deployment recognizes the sender of this message.';

describe('SenderVerdict', () => {
    it.each([
        ['a message whose author authenticated and whom nobody has named', 'Authenticated'],
        ['a message nothing established an author for', 'NotEstablished'],
    ] as const)('says nothing about %s, which is the ordinary state of legitimate mail', (_case, outcome) => {
        drawing(outcome, 'Unknown');

        expect(screen.queryByText(failed)).toBeNull();
        expect(screen.queryByText(recognized)).toBeNull();
    });

    it('says so where the receiving server reported that the displayed author did not hold', () => {
        drawing('Failed', 'Unknown');

        expect(screen.getByText(failed)).toBeDefined();
    });

    it('says so where this deployment recognizes the sender', () => {
        drawing('Authenticated', 'Trusted');

        expect(screen.getByText(recognized)).toBeDefined();
    });

    it('names the domain that actually authenticated rather than the address the message displays', () => {
        drawing('Failed', 'Unknown', 'mail.example.invalid');

        expect(
            screen.getByText(
                'Authenticated by mail.example.invalid, which is who actually sent it rather than the name above.',
            ),
        ).toBeDefined();
    });

    it('says nothing authenticated a sender where the deployment named no domain', () => {
        drawing('Failed', 'Unknown');

        expect(screen.getByText('Nothing authenticated a sender for this message.')).toBeDefined();
    });
});
