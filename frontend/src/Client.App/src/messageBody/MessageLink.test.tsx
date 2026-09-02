// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailDocumentLink } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageLink } from './MessageLink';
import { LinkOpenerContext, type OpenLink } from '../shellOperations/linkOpener';

const honest: MailDocumentLink = {
    target: 'https://example.invalid/offer',
    host: 'example.invalid',
    asciiHost: null,
    deception: 'None',
    worthWarningAbout: false,
};

function drawing(link: MailDocumentLink, openLink: OpenLink = () => Promise.resolve()) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={openLink}>
                <MessageLink link={link}>{link.host ?? link.target}</MessageLink>
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('MessageLink', () => {
    it('shows where the link goes before anybody follows it', () => {
        drawing(honest);

        expect(screen.getByText('goes to example.invalid')).toBeDefined();
    });

    it('shows the whole target for a scheme that has no host to name', () => {
        drawing({
            target: 'mailto:someone@example.invalid',
            host: null,
            asciiHost: null,
            deception: 'NotApplicable',
            worthWarningAbout: false,
        });

        expect(screen.getByText('goes to mailto:someone@example.invalid')).toBeDefined();
    });

    it('says so when the words of a link name one place and the link goes to another', () => {
        drawing({
            target: 'https://offers.invalid/claim',
            host: 'offers.invalid',
            asciiHost: null,
            deception: 'DisplayedHostDiffers',
            worthWarningAbout: true,
        });

        expect(screen.getByText('This link does not go where its words say. It goes to offers.invalid.')).toBeDefined();
    });

    it('names the written form of a host that reads as one thing and is spelled as another', () => {
        drawing({
            target: 'https://xn--nave-6pa.invalid/',
            host: 'naïve.invalid',
            asciiHost: 'xn--nave-6pa.invalid',
            deception: 'None',
            worthWarningAbout: true,
        });

        expect(
            screen.getByText('This link goes to naïve.invalid, which is written xn--nave-6pa.invalid.'),
        ).toBeDefined();
    });

    it('says nothing about deception for a link whose words claim nothing', () => {
        drawing(honest);

        expect(screen.queryByText(/does not go where its words say/)).toBeNull();
    });

    it('warns on a link the service judged worth warning about for a reason this client does not enumerate', () => {
        drawing({ ...honest, worthWarningAbout: true });

        expect(
            screen.getByText('This link is worth checking before you follow it. It goes to example.invalid.'),
        ).toBeDefined();
    });

    it('says nothing about a link the service did not judge worth warning about', () => {
        drawing({ ...honest, deception: 'DisplayedHostDiffers', asciiHost: 'xn--nave-6pa.invalid' });

        expect(screen.queryByText(/does not go where its words say/)).toBeNull();
        expect(screen.queryByText(/which is written/)).toBeNull();
    });

    it('asks for the link to be opened out of the application rather than letting the document navigate', () => {
        const asked: string[] = [];
        drawing(honest, (target) => {
            asked.push(target);

            return Promise.resolve();
        });

        // `fireEvent` answers false for an event something cancelled, which is how the default navigation being
        // prevented is asserted without reaching into the handler.
        const followed = fireEvent.click(screen.getByRole('link', { name: 'example.invalid' }));

        expect(followed).toBe(false);
        expect(asked).toEqual(['https://example.invalid/offer']);
    });

    it('says so when the link could not be opened, rather than appearing to have done nothing', async () => {
        drawing(honest, () => Promise.reject(new Error('nothing opened it')));

        fireEvent.click(screen.getByRole('link', { name: 'example.invalid' }));

        expect(await screen.findByText('This link could not be opened.')).toBeDefined();
    });
});
